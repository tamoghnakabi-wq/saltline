using Saltline.Combat;
using Saltline.Player;
using Saltline.Settings;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.FX
{
    /// <summary>
    /// Shot presentation: muzzle flash (front star, side flame jet, sparks, smoke, light), tracers,
    /// spent cases, and per-surface impacts (dust, chips, sparks, bullet holes; mist on players).
    /// Remote shots are rebuilt from the same seeded spread the host used, so tracers and impacts
    /// match the real pellets. Everything is pooled: shared particle systems fed with Emit(),
    /// fixed pools of decals, tracers, lights and cases.
    ///
    /// The local player's first-person flash and smoke are drawn on the viewmodel layer at the
    /// viewmodel muzzle, so they sit exactly on the gun drawn by the overlay camera.
    /// </summary>
    [DefaultExecutionOrder(400)]   // after the viewmodel (300): first-person emitters follow this frame's muzzle
    public sealed class FxManager : MonoBehaviour
    {
        public static FxManager Instance { get; private set; }

        struct Tracer { public LineRenderer Lr; public Vector3 A, B; public float Traveled, Length, Speed; public bool Active; }
        struct Mark { public Transform T; public MeshFilter F; public float Age, Size; public bool Active; }
        struct Flash { public Light L; public float Age, Peak; public bool Active; }

        readonly Tracer[] m_Tracers = new Tracer[64];
        readonly Mark[] m_Marks = new Mark[256];
        readonly Flash[] m_Flashes = new Flash[12];
        readonly Mesh[] m_HoleMesh = new Mesh[4];
        int m_NextTracer, m_NextMark, m_NextFlash;
        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        // World-space systems.
        ParticleSystem m_Core, m_Flame, m_MuzzleSparks, m_MuzzleSmoke, m_Dust, m_Debris, m_Sparks, m_Glint, m_Mist;
        // First person, viewmodel layer: flash/flame/sparks ride the muzzle; smoke lives in the world.
        ParticleSystem m_VCore, m_VFlame, m_VSparks, m_VSmoke, m_VGlow;
        Transform m_VmAnchor, m_VmMuzzle, m_VmEject;
        ShellCasings m_Shells;
        float m_Detail = 1f;
        float m_Heat, m_SinceVmShot = 10f, m_WispAcc;

        public ShellCasings Shells => m_Shells;
        public int ActiveMarks { get; private set; }
        public int ParticleCount =>
            Count(m_Core) + Count(m_Flame) + Count(m_MuzzleSparks) + Count(m_MuzzleSmoke) + Count(m_Dust) + Count(m_Debris) +
            Count(m_Sparks) + Count(m_Glint) + Count(m_Mist) + Count(m_VCore) + Count(m_VFlame) + Count(m_VSparks) + Count(m_VSmoke) + Count(m_VGlow);
        static int Count(ParticleSystem ps) => ps != null ? ps.particleCount : 0;

        public static void Ensure()
        {
            if (Instance != null || !Saltline.Core.RunMode.Presentation) return;
            var go = new GameObject("FxManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FxManager>();
        }

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            var lib = PresentationLibrary.Instance;
            Material flash = lib != null ? Pick(lib.fxFlash, lib.muzzleFlash) : null;
            Material flame = lib != null ? Pick(lib.fxFlame, lib.muzzleFlash) : null;
            Material spark = lib != null ? Pick(lib.fxSpark, lib.muzzleFlash) : null;
            Material glowM = lib != null ? Pick(lib.fxGlow, lib.muzzleFlash) : null;
            Material smoke = lib != null ? lib.fxSmoke : null;
            Material debris = lib != null ? lib.fxDebris : null;
            var cross = ParticleKit.FlameCross();
            int world = Layers.Default, vm = Layers.Viewmodel;

            m_Core = FlashCore("FlashCore", transform, world, flash, true);
            m_Flame = FlashFlame("FlashFlame", transform, world, flame, cross, true);
            m_MuzzleSparks = MuzzleSparks("MuzzleSparks", transform, world, spark, true);
            m_MuzzleSmoke = Smoke("MuzzleSmoke", transform, world, smoke, 320, 0.25f);
            m_Dust = Smoke("ImpactDust", transform, world, smoke, 400, 0.05f);
            ParticleKit.Drag(m_Dust, 4.5f);

            m_Debris = ParticleKit.System("ImpactDebris", transform, world, debris, 400, true);
            ParticleKit.RandomFrame(m_Debris);
            var dm = m_Debris.main;
            dm.gravityModifier = 1f;
            dm.startRotation3D = false;
            ParticleKit.Drag(m_Debris, 0.6f);
            ParticleKit.Bounce(m_Debris, 0.25f, 0.35f);
            var rot = m_Debris.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            ParticleKit.Fade(m_Debris, 0f, 0.85f, Color.white, Color.white);

            m_Sparks = ParticleKit.System("ImpactSparks", transform, world, spark, 500, true, ParticleSystemRenderMode.Stretch);
            StretchSparks(m_Sparks, 0.06f);
            var sm = m_Sparks.main;
            sm.gravityModifier = 1f;
            ParticleKit.Drag(m_Sparks, 1.2f);
            ParticleKit.Bounce(m_Sparks, 0.35f, 0.4f);
            ParticleKit.Size(m_Sparks, 1f, 0.2f);
            ParticleKit.Fade(m_Sparks, 0f, 0.5f, Color.white, new Color(1f, 0.45f, 0.2f));

            m_Glint = ParticleKit.System("ImpactGlint", transform, world, glowM, 64, true);
            ParticleKit.Size(m_Glint, 0.7f, 1.3f);
            ParticleKit.Fade(m_Glint, 0f, 0.2f, Color.white, new Color(1f, 0.6f, 0.3f));

            m_Mist = Smoke("HitMist", transform, world, smoke, 160, 0f);
            ParticleKit.Drag(m_Mist, 6f);

            m_VmAnchor = new GameObject("ViewmodelAnchor").transform;
            m_VmAnchor.SetParent(transform, false);
            m_VmAnchor.gameObject.layer = vm;
            m_VCore = FlashCore("VmFlashCore", m_VmAnchor, vm, flash, false);
            m_VFlame = FlashFlame("VmFlashFlame", m_VmAnchor, vm, flame, cross, false);
            m_VSparks = MuzzleSparks("VmSparks", m_VmAnchor, vm, spark, false);
            m_VSmoke = Smoke("VmSmoke", transform, vm, smoke, 200, 0.12f);
            // A soft bloom around the muzzle: shows around the optic when aiming, where the star is hidden.
            m_VGlow = ParticleKit.System("VmGlow", m_VmAnchor, vm, glowM, 16, false);
            ParticleKit.Size(m_VGlow, 1f, 1.3f);
            ParticleKit.Fade(m_VGlow, 0f, 0.2f, Color.white, new Color(1f, 0.6f, 0.3f));

            m_Shells = new ShellCasings(transform, 64, lib != null ? lib.brass : null, lib != null ? lib.shellHull : null);

            for (int i = 0; i < m_Tracers.Length; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.02f;
                lr.numCapVertices = 0;
                lr.textureMode = LineTextureMode.Stretch;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (lib != null) lr.sharedMaterial = lib.tracer;
                lr.enabled = false;
                m_Tracers[i].Lr = lr;
            }

            for (int i = 0; i < 4; i++) m_HoleMesh[i] = AtlasQuad(i);
            Material hole = lib != null ? Pick(lib.bulletHole, lib.impact) : null;
            for (int i = 0; i < m_Marks.Length; i++)
            {
                var go = new GameObject("BulletHole");
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = m_HoleMesh[0];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = hole;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                go.SetActive(false);
                m_Marks[i] = new Mark { T = go.transform, F = mf };
            }

            for (int i = 0; i < m_Flashes.Length; i++)
            {
                var go = new GameObject("MuzzleLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 6f;
                l.color = new Color(1f, 0.74f, 0.42f);
                l.shadows = LightShadows.None;
                go.SetActive(false);
                m_Flashes[i] = new Flash { L = l };
            }

            ApplyDetail();
            VideoSettings.Changed += ApplyDetail;
        }

        static Material Pick(Material a, Material b) => a != null ? a : b;

        void OnDestroy()
        {
            VideoSettings.Changed -= ApplyDetail;
            if (Instance == this) Instance = null;
        }

        void ApplyDetail()
        {
            m_Detail = VideoSettings.EffectsDetail;
            if (m_Shells != null) m_Shells.RestSeconds = Mathf.Lerp(3f, 10f, m_Detail);
            if (m_Debris != null) { var c = m_Debris.collision; c.enabled = m_Detail > 0.5f; }
            if (m_Sparks != null) { var c = m_Sparks.collision; c.enabled = m_Detail > 0.5f; }
        }

        int N(float n) => Mathf.Max(1, Mathf.RoundToInt(n * m_Detail));
        int N0(float n) => Mathf.RoundToInt(n * m_Detail + Random.value * 0.5f);

        static ParticleSystem FlashCore(string name, Transform parent, int layer, Material mat, bool world)
        {
            var ps = ParticleKit.System(name, parent, layer, mat, 32, world);
            ParticleKit.RandomFrame(ps);
            ParticleKit.Size(ps, 0.75f, 1.2f);
            ParticleKit.Fade(ps, 0f, 0.35f, Color.white, new Color(1f, 0.7f, 0.45f));
            return ps;
        }

        static ParticleSystem FlashFlame(string name, Transform parent, int layer, Material mat, Mesh cross, bool world)
        {
            var ps = ParticleKit.System(name, parent, layer, mat, 32, world, ParticleSystemRenderMode.Mesh);
            var main = ps.main;
            main.startRotation3D = true;
            main.startSize3D = true;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.mesh = cross;
            r.alignment = world ? ParticleSystemRenderSpace.World : ParticleSystemRenderSpace.Local;
            ParticleKit.RandomFrame(ps);
            ParticleKit.Size(ps, 0.8f, 1.15f);
            ParticleKit.Fade(ps, 0f, 0.3f, Color.white, new Color(1f, 0.6f, 0.35f));
            return ps;
        }

        static ParticleSystem MuzzleSparks(string name, Transform parent, int layer, Material mat, bool world)
        {
            var ps = ParticleKit.System(name, parent, layer, mat, 128, world, ParticleSystemRenderMode.Stretch);
            StretchSparks(ps, 0.025f);
            var main = ps.main;
            main.gravityModifier = 0.25f;
            ParticleKit.Drag(ps, 3f);
            ParticleKit.Size(ps, 1f, 0.3f);
            ParticleKit.Fade(ps, 0f, 0.4f, Color.white, new Color(1f, 0.5f, 0.2f));
            return ps;
        }

        static void StretchSparks(ParticleSystem ps, float velocityScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = velocityScale;
            r.lengthScale = 1.5f;
            r.cameraVelocityScale = 0f;
            r.sortMode = ParticleSystemSortMode.None;
        }

        static ParticleSystem Smoke(string name, Transform parent, int layer, Material mat, int max, float rise)
        {
            var ps = ParticleKit.System(name, parent, layer, mat, max, true);
            ParticleKit.RandomFrame(ps);
            ParticleKit.Size(ps, 1f, 4.5f, 2.2f);
            ParticleKit.Fade(ps, 0.03f, 0.35f, Color.white, new Color(0.92f, 0.92f, 0.92f));
            ParticleKit.Drag(ps, 2.5f);
            if (rise != 0f) ParticleKit.Rise(ps, rise);
            ParticleKit.Turbulence(ps, 0.25f, 0.6f);
            return ps;
        }

        /// <summary>A unit quad facing -z showing one cell of the 2x2 bullet-hole atlas.</summary>
        static Mesh AtlasQuad(int cell)
        {
            float u0 = (cell % 2) * 0.5f, v0 = cell < 2 ? 0.5f : 0f;
            var m = new Mesh { name = $"BulletHole{cell}" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + 0.5f, v0), new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.5f, v0 + 0.5f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            m.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>The first-person gun's muzzle and ejection port (null when there is no viewmodel).</summary>
        public void SetViewmodelGun(Transform muzzle, Transform eject)
        {
            m_VmMuzzle = muzzle;
            m_VmEject = eject;
            m_Heat = 0f;
        }

        void OnEnable() => CombatEvents.Shot += OnShot;
        void OnDisable() => CombatEvents.Shot -= OnShot;

        public static bool EjectsOnShot(WeaponClass c) => c == WeaponClass.AssaultRifle || c == WeaponClass.SMG || c == WeaponClass.Pistol;

        void OnShot(ShotFx fx, bool local)
        {
            var shooter = NetPlayer.Find(fx.Shooter);
            var db = shooter != null ? shooter.Weapons : NetPlayer.All.Count > 0 ? NetPlayer.All[0].Weapons : null;
            var w = db != null ? db.Get(fx.WeaponId) : null;
            if (w == null) return;

            Vector3 aim = Quaternion.Euler(-fx.Pitch * InputCmd.PitchScale, fx.Yaw * InputCmd.YawScale, 0f) * Vector3.forward;
            Vector3 muzzle = fx.Origin + aim * 0.6f + Vector3.down * 0.12f;
            var rig = shooter != null && shooter.IsOwner ? shooter.LocalRig : null;
            bool firstPerson = local && rig != null && !rig.ThirdPersonActive && m_VmMuzzle != null;
            PlayerView view = shooter != null ? shooter.GetComponent<PlayerView>() : null;
            if (firstPerson) muzzle = m_VmMuzzle.position;
            else if (view != null && view.WorldMuzzle != null) muzzle = view.WorldMuzzle.position;
            Vector3 inherit = shooter != null ? shooter.Render.Velocity : Vector3.zero;

            int pellets = Mathf.Max(1, w.pellets);
            int tracers = pellets > 1 ? Mathf.Min(pellets, 5) : 1;
            bool drawTracer = pellets > 1 || w.tracerEvery <= 1 || fx.ShotNumber % (uint)w.tracerEvery == 0;
            float impactScale = pellets > 1 ? 0.55f : 1f;
            // A remote shot comes from the host after it resolved it, rewound to where the shooter saw
            // the targets. This machine draws the targets where they are now, so a fresh test can
            // disagree (the victim strafed on); the host's answer decides hit or miss.
            bool resolved = !local;
            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = WeaponSim.PelletDirection(aim, fx.Spread, fx.Shooter, fx.ShotNumber, i);
                bool env = ShotResolver.EnvironmentRay(fx.Origin, dir, w.maxRange, out var h);
                float envRange = env ? h.distance : w.maxRange;
                NetPlayer victim = null;
                float tVictim = envRange;
                foreach (var p in NetPlayer.All)
                {
                    if (p == null || p.OwnerClientId == fx.Shooter || !p.Render.Alive) continue;
                    var hp = p.Render.ToHitbox();
                    if (!HitboxModel.RayNearPlayer(fx.Origin, dir, hp, tVictim)) continue;
                    HitboxModel.Build(hp, p.MotorConfig, s_Caps);
                    if (HitboxModel.Raycast(fx.Origin, dir, s_Caps, tVictim, out float t, out _)) { victim = p; tVictim = t; }
                }
                if (resolved)
                {
                    bool hostHit = i < 32 && (fx.PelletHits & (1u << i)) != 0;
                    if (!hostHit) victim = null; // drawn across someone the host says it missed: it flies on
                    else if (victim == null) victim = ClosestToRay(fx.Origin, dir, fx.Shooter, envRange, out tVictim);
                }

                Vector3 end;
                if (victim != null)
                {
                    end = fx.Origin + dir * tVictim;
                    // Our own body is not drawn in first person, and the hurt sound already says we were hit.
                    if (!victim.IsOwner)
                    {
                        BodyHit(end, dir, impactScale);
                        CombatEvents.RaiseImpact(new Saltline.Combat.Impact { Point = end, Normal = -dir, Body = true });
                    }
                }
                else if (env)
                {
                    end = h.point;
                    var surface = SurfaceTag.TypeOf(h.collider);
                    Impact(h.point, h.normal, dir, surface, impactScale);
                    CombatEvents.RaiseImpact(new Saltline.Combat.Impact { Point = h.point, Normal = h.normal, Surface = surface });
                }
                else end = fx.Origin + dir * w.maxRange;
                if (drawTracer && i < tracers) SpawnTracer(muzzle, end, w);
            }

            if (firstPerson) MuzzleFirstPerson(w, aim, inherit, shooter != null ? shooter.SimWeapon.Ads : 0f);
            else MuzzleWorld(muzzle, aim, w, inherit);
            SpawnLight(muzzle, aim, w);

            if (EjectsOnShot(w.weaponClass))
            {
                if (firstPerson) EjectShell(m_VmEject, w.weaponClass, inherit, true);
                else if (view != null) EjectShell(view.WorldEject, w.weaponClass, inherit, false);
            }
        }

        /// <summary>Throws a spent case from an ejection-port marker (child of the gun model, bore along +z).</summary>
        public void EjectShell(Transform port, WeaponClass cls, Vector3 inherit, bool viewmodel)
        {
            if (port == null || m_Shells == null) return;
            if (!viewmodel && Camera.main != null && (port.position - Camera.main.transform.position).sqrMagnitude > 60f * 60f) return;
            var gun = port.parent != null ? port.parent.rotation : port.rotation;
            m_Shells.Eject(port.position, gun, cls, inherit, viewmodel);
        }

        // ------------------------------------------------------------------ muzzle

        /// <summary>Muzzle flash seen from outside: other players, and the local player in third person.</summary>
        public void MuzzleWorld(Vector3 pos, Vector3 aim, WeaponDefinition w, Vector3 inherit)
        {
            float s = w.muzzleFlashScale;
            float life = Mathf.Max(0.05f, Time.deltaTime * 2.2f);
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + aim * 0.02f,
                velocity = inherit,
                startSize = Random.Range(0.22f, 0.32f) * s,
                startLifetime = life,
                rotation = Random.Range(0f, 360f),
                startColor = Color.white,
            };
            m_Core.Emit(ep, 1);

            Quaternion q = Quaternion.LookRotation(aim) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = inherit,
                rotation3D = q.eulerAngles,
                startSize3D = new Vector3(0.13f * s, 0.13f * s, Random.Range(0.36f, 0.50f) * s),
                startLifetime = life,
                startColor = Color.white,
            };
            m_Flame.Emit(ep, 1);

            int sparks = N0(2.5f * s);
            for (int i = 0; i < sparks; i++)
            {
                ep = new ParticleSystem.EmitParams
                {
                    position = pos + aim * 0.03f,
                    velocity = inherit + (aim + Random.insideUnitSphere * 0.25f).normalized * Random.Range(10f, 22f),
                    startSize = Random.Range(0.008f, 0.014f),
                    startLifetime = Random.Range(0.05f, 0.14f),
                    startColor = Color.white,
                };
                m_MuzzleSparks.Emit(ep, 1);
            }
            EmitSmoke(m_MuzzleSmoke, pos, aim, w, inherit, 1f);
        }

        void EmitSmoke(ParticleSystem ps, Vector3 pos, Vector3 aim, WeaponDefinition w, Vector3 inherit, float amount)
        {
            float s = w.muzzleFlashScale;
            int puffs = w.pellets > 1 || w.weaponClass == WeaponClass.Sniper ? N(4f) : N(2f);
            for (int i = 0; i < puffs; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + aim * Random.Range(0.04f, 0.22f) * s,
                    velocity = inherit * 0.6f + aim * Random.Range(0.8f, 2.6f) + Vector3.up * 0.15f + Random.insideUnitSphere * 0.25f,
                    startSize = Random.Range(0.08f, 0.14f) * s,
                    startLifetime = Random.Range(1.1f, 2.2f),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-40f, 40f),
                    startColor = new Color(0.80f, 0.79f, 0.77f, Random.Range(0.30f, 0.45f) * amount),
                };
                ps.Emit(ep, 1);
            }
        }

        /// <summary>First-person flash on the viewmodel layer, attached to the viewmodel muzzle.</summary>
        public void MuzzleFirstPerson(WeaponDefinition w, Vector3 aim, Vector3 inherit, float ads)
        {
            float s = w.muzzleFlashScale * Mathf.Lerp(0.85f, 0.6f, ads);
            float life = Mathf.Max(0.05f, Time.deltaTime * 2.2f);
            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(0f, 0f, 0.015f),
                startSize = Random.Range(0.20f, 0.28f) * s,
                startLifetime = life,
                rotation = Random.Range(0f, 360f),
                startColor = Color.white,
            };
            m_VCore.Emit(ep, 1);
            ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(0f, 0f, 0.03f),
                startSize = (0.30f + 0.25f * ads) * w.muzzleFlashScale,
                startLifetime = life * 1.2f,
                rotation = Random.Range(0f, 360f),
                startColor = new Color(1f, 1f, 1f, 0.35f + 0.25f * ads),
            };
            m_VGlow.Emit(ep, 1);
            ep = new ParticleSystem.EmitParams
            {
                position = Vector3.zero,
                rotation3D = new Vector3(0f, 0f, Random.Range(0f, 360f)),
                startSize3D = new Vector3(0.10f * s, 0.10f * s, Random.Range(0.32f, 0.44f) * s),
                startLifetime = life,
                startColor = Color.white,
            };
            m_VFlame.Emit(ep, 1);
            int sparks = N0(2f);
            for (int i = 0; i < sparks; i++)
            {
                ep = new ParticleSystem.EmitParams
                {
                    position = new Vector3(0f, 0f, 0.02f),
                    velocity = (Vector3.forward + Random.insideUnitSphere * 0.3f).normalized * Random.Range(6f, 14f),
                    startSize = Random.Range(0.005f, 0.009f),
                    startLifetime = Random.Range(0.04f, 0.1f),
                    startColor = Color.white,
                };
                m_VSparks.Emit(ep, 1);
            }
            EmitSmoke(m_VSmoke, m_VmMuzzle.position, aim, w, inherit, 0.8f);
            m_Heat = Mathf.Min(1f, m_Heat + w.weaponClass switch
            {
                WeaponClass.Shotgun => 0.3f,
                WeaponClass.Sniper => 0.35f,
                WeaponClass.Pistol => 0.08f,
                WeaponClass.SMG => 0.05f,
                _ => 0.06f,
            });
            m_SinceVmShot = 0f;
        }

        public void SpawnLight(Vector3 muzzle, Vector3 aim, WeaponDefinition w)
        {
            ref var f = ref m_Flashes[m_NextFlash];
            m_NextFlash = (m_NextFlash + 1) % m_Flashes.Length;
            f.L.gameObject.SetActive(true);
            f.L.transform.position = muzzle + aim * 0.15f;
            f.Peak = 2.6f * Mathf.Sqrt(w.muzzleFlashScale);
            f.L.intensity = f.Peak;
            f.Age = 0f;
            f.Active = true;
        }

        // ------------------------------------------------------------------ tracers

        public void SpawnTracer(Vector3 a, Vector3 b, WeaponDefinition w)
        {
            ref var t = ref m_Tracers[m_NextTracer];
            m_NextTracer = (m_NextTracer + 1) % m_Tracers.Length;
            t.A = a;
            t.B = b;
            t.Traveled = 0f;
            t.Length = Mathf.Max(0.01f, Vector3.Distance(a, b));
            t.Speed = w.tracerSpeed;
            t.Active = true;
            t.Lr.widthMultiplier = w.pellets > 1 ? 0.012f : 0.02f;
            t.Lr.startColor = w.tracerColor;
            t.Lr.endColor = new Color(w.tracerColor.r, w.tracerColor.g, w.tracerColor.b, 0f);
            t.Lr.enabled = true;
            UpdateTracer(ref t, 0f);
        }

        static void UpdateTracer(ref Tracer t, float dt)
        {
            t.Traveled += t.Speed * dt;
            float head = Mathf.Min(t.Traveled, t.Length);
            float tail = Mathf.Clamp(t.Traveled - 5f, 0f, t.Length);
            Vector3 dir = (t.B - t.A) / t.Length;
            t.Lr.SetPosition(0, t.A + dir * head);
            t.Lr.SetPosition(1, t.A + dir * tail);
            if (tail >= t.Length - 0.001f) { t.Active = false; t.Lr.enabled = false; }
        }

        // ------------------------------------------------------------------ impacts

        /// <summary>A bullet striking the world: burst by surface type plus a bullet hole.</summary>
        public void Impact(Vector3 point, Vector3 normal, Vector3 dir, SurfaceType surface, float scale = 1f)
        {
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, point) : 0f;
            bool near = dist < 140f;
            Vector3 bounce = Vector3.Reflect(dir, normal);
            switch (surface)
            {
                case SurfaceType.Metal:
                case SurfaceType.ThinMetal:
                    if (near)
                    {
                        Sparks(point, normal, bounce, N(20f * scale));
                        Glint(point, normal, 0.16f + 0.10f * scale, 1f);
                        Dust(point, normal, new Color(0.45f, 0.44f, 0.42f), N(1.5f * scale), 0.7f);
                    }
                    Hole(point, normal, 1, surface == SurfaceType.ThinMetal ? 0.085f : 0.095f, false);
                    break;
                case SurfaceType.Wood:
                    if (near)
                    {
                        Dust(point, normal, new Color(0.58f, 0.48f, 0.36f), N(3f * scale), 1.0f);
                        Chips(point, normal, bounce, new Color(0.66f, 0.52f, 0.36f), N(12f * scale), 0.015f, 0.04f);
                    }
                    Hole(point, normal, 2, 0.11f, true);
                    break;
                case SurfaceType.Dirt:
                    if (near)
                    {
                        Dust(point, normal, new Color(0.84f, 0.82f, 0.76f), N(6f * scale), 1.5f);
                        Chips(point, normal, bounce, new Color(0.70f, 0.68f, 0.62f), N(10f * scale), 0.010f, 0.024f);
                    }
                    Hole(point, normal, 3, 0.13f, false);
                    break;
                case SurfaceType.Water:
                    if (near) Dust(point, normal, new Color(0.82f, 0.86f, 0.90f), N(4f * scale), 1.1f);
                    break;
                case SurfaceType.Glass:
                    if (near)
                    {
                        Glint(point, normal, 0.10f, 0.8f);
                        Chips(point, normal, bounce, new Color(0.85f, 0.92f, 0.95f), N(8f * scale), 0.005f, 0.014f);
                    }
                    Hole(point, normal, 1, 0.07f, false);
                    break;
                case SurfaceType.Fabric:
                case SurfaceType.Plastic:
                    if (near) Dust(point, normal, new Color(0.50f, 0.49f, 0.47f), N(2f * scale), 0.6f);
                    Hole(point, normal, 3, 0.07f, false);
                    break;
                default: // concrete, asphalt, rock
                    if (near)
                    {
                        Dust(point, normal, new Color(0.64f, 0.62f, 0.58f), N(5f * scale), 1.2f);
                        Chips(point, normal, bounce, new Color(0.58f, 0.57f, 0.54f), N(11f * scale), 0.010f, 0.026f);
                        Glint(point, normal, 0.10f, 0.35f);
                        if (Random.value < 0.3f * scale) Sparks(point, normal, bounce, N(4f));
                    }
                    Hole(point, normal, 0, 0.095f, false);
                    break;
            }
        }

        /// <summary>A bullet striking a player: a brief dark-red mist and a few droplets. No decals.</summary>
        /// <summary>
        /// The living player (not the shooter) whose chest the ray passes closest to, and the distance
        /// along the ray to that point: where to show a hit the host confirmed but this machine draws
        /// the victim elsewhere.
        /// </summary>
        public static NetPlayer ClosestToRay(Vector3 origin, Vector3 dir, ulong shooter, float maxT, out float t)
        {
            NetPlayer best = null;
            float bestMiss = float.MaxValue;
            t = maxT;
            foreach (var p in NetPlayer.All)
            {
                if (p == null || p.OwnerClientId == shooter || !p.Render.Alive) continue;
                var r = p.Render;
                Vector3 chest = r.Feet + Vector3.up * (r.Height * 0.7f);
                float along = Mathf.Clamp(Vector3.Dot(chest - origin, dir), 0f, maxT);
                float miss = Vector3.Distance(origin + dir * along, chest);
                if (miss < bestMiss) { bestMiss = miss; best = p; t = along; }
            }
            return best;
        }

        public void BodyHit(Vector3 point, Vector3 dir, float scale = 1f)
        {
            int puffs = N(3f * scale);
            for (int i = 0; i < puffs; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = point + Random.insideUnitSphere * 0.03f,
                    velocity = dir * Random.Range(0.4f, 1.8f) + Random.insideUnitSphere * 0.6f,
                    startSize = Random.Range(0.07f, 0.12f),
                    startLifetime = Random.Range(0.25f, 0.5f),
                    rotation = Random.Range(0f, 360f),
                    startColor = new Color(0.52f, 0.05f, 0.04f, 0.75f),
                };
                m_Mist.Emit(ep, 1);
            }
            Chips(point, -dir, dir, new Color(0.22f, 0.02f, 0.02f), N(4f * scale), 0.004f, 0.009f);
        }

        void Dust(Vector3 p, Vector3 n, Color c, int count, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = p + n * 0.03f,
                    velocity = n * Random.Range(0.8f, 3.2f) + Random.insideUnitSphere * 0.8f,
                    startSize = Random.Range(0.09f, 0.16f) * size,
                    startLifetime = Random.Range(0.9f, 1.9f),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-60f, 60f),
                    startColor = new Color(c.r, c.g, c.b, Random.Range(0.55f, 0.8f)),
                };
                m_Dust.Emit(ep, 1);
            }
        }

        void Chips(Vector3 p, Vector3 n, Vector3 bounce, Color c, int count, float minSize, float maxSize)
        {
            for (int i = 0; i < count; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = p + n * 0.02f,
                    velocity = n * Random.Range(1.2f, 4f) + bounce * Random.Range(0.5f, 2f) + Random.insideUnitSphere * 1.4f + Vector3.up * 0.6f,
                    startSize = Random.Range(minSize, maxSize),
                    startLifetime = Random.Range(0.7f, 1.3f),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-900f, 900f),
                    startColor = c,
                };
                m_Debris.Emit(ep, 1);
            }
        }

        void Sparks(Vector3 p, Vector3 n, Vector3 bounce, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 d = (bounce * 0.6f + n * 0.5f + Random.insideUnitSphere * 0.55f).normalized;
                var ep = new ParticleSystem.EmitParams
                {
                    position = p + n * 0.01f,
                    velocity = d * Random.Range(3f, 10f),
                    startSize = Random.Range(0.010f, 0.018f),
                    startLifetime = Random.Range(0.15f, 0.5f),
                    startColor = Color.white,
                };
                m_Sparks.Emit(ep, 1);
            }
        }

        void Glint(Vector3 p, Vector3 n, float size, float brightness)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = p + n * 0.02f,
                startSize = size * Random.Range(0.8f, 1.2f),
                startLifetime = Mathf.Max(0.06f, Time.deltaTime * 2.5f),
                rotation = Random.Range(0f, 360f),
                startColor = new Color(1f, 1f, 1f, brightness),
            };
            m_Glint.Emit(ep, 1);
        }

        /// <summary>Places a bullet hole, shrinking or skipping it where it would hang off an edge.</summary>
        void Hole(Vector3 point, Vector3 normal, int cell, float size, bool grainHorizontal)
        {
            size *= Random.Range(0.85f, 1.15f);
            Quaternion facing = Quaternion.LookRotation(-normal);
            float roll = Random.Range(0f, 360f);
            if (grainHorizontal)
            {
                // Line the splinters up with the horizontal along the surface (planks run sideways in Kappa).
                Vector3 along = Vector3.ProjectOnPlane(Vector3.up, normal).sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, normal).normalized : Vector3.right;
                roll = Vector3.SignedAngle(facing * Vector3.right, along, -normal) + Random.Range(-12f, 12f);
            }
            Quaternion rot = facing * Quaternion.Euler(0f, 0f, roll);
            if (!Fits(point, normal, rot, size * 0.5f))
            {
                size *= 0.55f;
                if (!Fits(point, normal, rot, size * 0.5f)) return;
            }
            ref var m = ref m_Marks[m_NextMark];
            m_NextMark = (m_NextMark + 1) % m_Marks.Length;
            if (!m.Active) ActiveMarks++;
            m.F.sharedMesh = m_HoleMesh[cell];
            m.T.gameObject.SetActive(true);
            m.T.SetPositionAndRotation(point + normal * 0.003f, rot);
            m.T.localScale = Vector3.one * size;
            m.Size = size;
            m.Age = 0f;
            m.Active = true;
        }

        static bool Fits(Vector3 p, Vector3 n, Quaternion rot, float half)
        {
            Vector3 a = rot * Vector3.right * half, b = rot * Vector3.up * half;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = p + ((i & 1) == 0 ? a : -a) + ((i & 2) == 0 ? b : -b);
                if (!Physics.Raycast(corner + n * 0.03f, -n, 0.06f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ update

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (m_VmMuzzle != null)
            {
                var gun = m_VmMuzzle.parent != null ? m_VmMuzzle.parent : m_VmMuzzle;
                m_VmAnchor.SetPositionAndRotation(m_VmMuzzle.position, gun.rotation);
                BarrelWisp(dt, gun.forward);
            }
            else m_Heat = 0f;

            for (int i = 0; i < m_Tracers.Length; i++)
                if (m_Tracers[i].Active) UpdateTracer(ref m_Tracers[i], dt);
            float markLife = Mathf.Lerp(12f, 40f, m_Detail);
            for (int i = 0; i < m_Marks.Length; i++)
            {
                ref var m = ref m_Marks[i];
                if (!m.Active) continue;
                m.Age += dt;
                if (m.Age > markLife)
                {
                    float k = 1f - (m.Age - markLife) / 0.5f;
                    if (k <= 0f) { m.Active = false; ActiveMarks--; m.T.gameObject.SetActive(false); }
                    else m.T.localScale = Vector3.one * (m.Size * k);
                }
            }
            for (int i = 0; i < m_Flashes.Length; i++)
            {
                ref var f = ref m_Flashes[i];
                if (!f.Active) continue;
                f.Age += dt;
                // Never shorter than one rendered frame, however low the frame rate.
                float life = Mathf.Max(0.05f, dt * 1.5f);
                f.L.intensity = Mathf.Lerp(f.Peak, 0f, f.Age / life);
                if (f.Age > life) { f.Active = false; f.L.gameObject.SetActive(false); }
            }
            m_Shells.Tick(dt);
        }

        /// <summary>A thin wisp curls from a hot barrel for a while after sustained fire (first person).</summary>
        void BarrelWisp(float dt, Vector3 fwd)
        {
            m_SinceVmShot += dt;
            m_Heat = Mathf.Max(0f, m_Heat - dt * 0.3f);
            if (m_SinceVmShot < 0.2f || m_Heat < 0.25f || m_Detail < 0.5f) { m_WispAcc = 0f; return; }
            m_WispAcc += dt * 16f * m_Heat;
            while (m_WispAcc >= 1f)
            {
                m_WispAcc -= 1f;
                var ep = new ParticleSystem.EmitParams
                {
                    position = m_VmMuzzle.position + fwd * 0.01f,
                    velocity = Vector3.up * Random.Range(0.12f, 0.3f) + fwd * 0.04f + Random.insideUnitSphere * 0.03f,
                    startSize = Random.Range(0.02f, 0.035f),
                    startLifetime = Random.Range(1.0f, 1.6f),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-30f, 30f),
                    startColor = new Color(0.88f, 0.88f, 0.88f, 0.22f + 0.25f * m_Heat),
                };
                m_VSmoke.Emit(ep, 1);
            }
        }
    }
}
