using Saltline.Combat;
using Saltline.Player;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.FX
{
    /// <summary>
    /// Pooled shot presentation: tracers, impact marks and muzzle flashes. Remote shots are
    /// rebuilt from the same seeded spread the host used, so tracers match the real pellets.
    /// Nothing is allocated per shot after warm-up.
    /// </summary>
    public sealed class FxManager : MonoBehaviour
    {
        public static FxManager Instance { get; private set; }

        struct Tracer { public LineRenderer Lr; public Vector3 A, B; public float Traveled, Length, Speed; public bool Active; }
        struct Mark { public Transform T; public float Age; public bool Active; }
        struct Flash { public Light L; public Transform Q; public float Age; public bool Active; }

        readonly Tracer[] m_Tracers = new Tracer[64];
        readonly Mark[] m_Marks = new Mark[192];
        readonly Flash[] m_Flashes = new Flash[12];
        int m_NextTracer, m_NextMark, m_NextFlash;
        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        public static void Ensure()
        {
            if (Instance != null || Application.isBatchMode) return;
            var go = new GameObject("FxManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FxManager>();
        }

        void Awake()
        {
            var lib = PresentationLibrary.Instance;
            for (int i = 0; i < m_Tracers.Length; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.018f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (lib != null) lr.sharedMaterial = lib.tracer;
                lr.enabled = false;
                m_Tracers[i].Lr = lr;
            }
            for (int i = 0; i < m_Marks.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(go.GetComponent<Collider>());
                go.name = "Impact";
                go.transform.SetParent(transform, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (lib != null) mr.sharedMaterial = lib.impact;
                go.SetActive(false);
                m_Marks[i].T = go.transform;
            }
            for (int i = 0; i < m_Flashes.Length; i++)
            {
                var go = new GameObject("MuzzleFlash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 5f;
                l.intensity = 2.5f;
                l.color = new Color(1f, 0.78f, 0.45f);
                l.shadows = LightShadows.None;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(go.transform, false);
                var mr = q.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (lib != null) mr.sharedMaterial = lib.muzzleFlash;
                go.SetActive(false);
                m_Flashes[i] = new Flash { L = l, Q = q.transform };
            }
        }

        void OnEnable() => CombatEvents.Shot += OnShot;
        void OnDisable() => CombatEvents.Shot -= OnShot;

        void OnShot(ShotFx fx, bool local)
        {
            var shooter = NetPlayer.Find(fx.Shooter);
            var db = shooter != null ? shooter.Weapons : NetPlayer.All.Count > 0 ? NetPlayer.All[0].Weapons : null;
            var w = db != null ? db.Get(fx.WeaponId) : null;
            if (w == null) return;

            Vector3 aim = Quaternion.Euler(-fx.Pitch * InputCmd.PitchScale, fx.Yaw * InputCmd.YawScale, 0f) * Vector3.forward;
            Vector3 muzzle = fx.Origin + aim * 0.6f + Vector3.down * 0.12f;
            var vm = ViewmodelController.Current;
            var rig = shooter != null && shooter.IsLocalHuman ? (shooter.InputProvider as LocalPlayerController)?.Rig : null;
            if (local && rig != null && !rig.ThirdPersonActive && vm != null && vm.Muzzle != null) muzzle = vm.Muzzle.position;
            else if (shooter != null)
            {
                var view = shooter.GetComponent<PlayerView>();
                if (view != null && view.WorldMuzzle != null) muzzle = view.WorldMuzzle.position;
            }

            int pellets = Mathf.Max(1, w.pellets);
            int tracers = pellets > 1 ? Mathf.Min(pellets, 5) : 1;
            bool drawTracer = pellets > 1 || w.tracerEvery <= 1 || fx.ShotNumber % (uint)w.tracerEvery == 0;
            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = WeaponSim.PelletDirection(aim, fx.Spread, fx.Shooter, fx.ShotNumber, i);
                float range = w.maxRange;
                Vector3 end = fx.Origin + dir * range;
                bool env = ShotResolver.EnvironmentRay(fx.Origin, dir, range, out var h);
                if (env) { end = h.point; range = h.distance; }
                foreach (var p in NetPlayer.All)
                {
                    if (p == null || p.OwnerClientId == fx.Shooter || !p.Render.Alive) continue;
                    var hp = p.Render.ToHitbox();
                    if (!HitboxModel.RayNearPlayer(fx.Origin, dir, hp, range)) continue;
                    HitboxModel.Build(hp, p.MotorConfig, s_Caps);
                    if (HitboxModel.Raycast(fx.Origin, dir, s_Caps, range, out float t, out _))
                    {
                        range = t;
                        end = fx.Origin + dir * t;
                        env = false;
                    }
                }
                if (env) SpawnMark(h.point, h.normal);
                if (drawTracer && i < tracers) SpawnTracer(muzzle, end, w);
            }
            SpawnFlash(muzzle, aim, w);
        }

        void SpawnTracer(Vector3 a, Vector3 b, WeaponDefinition w)
        {
            ref var t = ref m_Tracers[m_NextTracer];
            m_NextTracer = (m_NextTracer + 1) % m_Tracers.Length;
            t.A = a;
            t.B = b;
            t.Traveled = 0f;
            t.Length = Mathf.Max(0.01f, Vector3.Distance(a, b));
            t.Speed = w.tracerSpeed;
            t.Active = true;
            t.Lr.startColor = w.tracerColor;
            t.Lr.endColor = new Color(w.tracerColor.r, w.tracerColor.g, w.tracerColor.b, 0f);
            t.Lr.enabled = true;
            UpdateTracer(ref t, 0f);
        }

        static void UpdateTracer(ref Tracer t, float dt)
        {
            t.Traveled += t.Speed * dt;
            float head = Mathf.Min(t.Traveled, t.Length);
            float tail = Mathf.Clamp(t.Traveled - 4f, 0f, t.Length);
            Vector3 dir = (t.B - t.A) / t.Length;
            t.Lr.SetPosition(0, t.A + dir * head);
            t.Lr.SetPosition(1, t.A + dir * tail);
            if (tail >= t.Length - 0.001f) { t.Active = false; t.Lr.enabled = false; }
        }

        void SpawnMark(Vector3 point, Vector3 normal)
        {
            ref var m = ref m_Marks[m_NextMark];
            m_NextMark = (m_NextMark + 1) % m_Marks.Length;
            m.T.gameObject.SetActive(true);
            m.T.SetPositionAndRotation(point + normal * 0.004f, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            m.T.localScale = Vector3.one * Random.Range(0.06f, 0.09f);
            m.Age = 0f;
            m.Active = true;
        }

        void SpawnFlash(Vector3 muzzle, Vector3 aim, WeaponDefinition w)
        {
            ref var f = ref m_Flashes[m_NextFlash];
            m_NextFlash = (m_NextFlash + 1) % m_Flashes.Length;
            f.L.gameObject.SetActive(true);
            f.L.transform.position = muzzle + aim * 0.05f;
            var cam = Camera.main;
            f.Q.rotation = cam != null ? Quaternion.LookRotation(cam.transform.forward) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)) : Quaternion.identity;
            f.Q.localScale = Vector3.one * 0.18f * w.muzzleFlashScale;
            f.Age = 0f;
            f.Active = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < m_Tracers.Length; i++)
                if (m_Tracers[i].Active) UpdateTracer(ref m_Tracers[i], dt);
            for (int i = 0; i < m_Marks.Length; i++)
            {
                if (!m_Marks[i].Active) continue;
                m_Marks[i].Age += dt;
                if (m_Marks[i].Age > 12f) { m_Marks[i].Active = false; m_Marks[i].T.gameObject.SetActive(false); }
            }
            for (int i = 0; i < m_Flashes.Length; i++)
            {
                if (!m_Flashes[i].Active) continue;
                m_Flashes[i].Age += dt;
                m_Flashes[i].L.intensity = Mathf.Lerp(2.5f, 0f, m_Flashes[i].Age / 0.05f);
                if (m_Flashes[i].Age > 0.05f) { m_Flashes[i].Active = false; m_Flashes[i].L.gameObject.SetActive(false); }
            }
        }
    }
}
