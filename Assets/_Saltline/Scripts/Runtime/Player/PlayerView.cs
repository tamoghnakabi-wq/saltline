using Saltline.Combat;
using Saltline.FX;
using Saltline.Net;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.Player
{
    /// <summary>
    /// Third-person body. With the imported operator (PresentationLibrary.operatorPrefab) the
    /// skinned character is animated by an OperatorRig from the player's RenderPose; without it
    /// the body falls back to the exact hitbox capsules. Either way the host tests hits against
    /// the analytic HitboxModel, and the character is authored to those proportions.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class PlayerView : MonoBehaviour
    {
        NetPlayer m_P;
        Transform m_Root;
        OperatorRig m_Op;
        readonly Transform[] m_Parts = new Transform[HitboxModel.Count];
        readonly MeshRenderer[] m_PartRenderers = new MeshRenderer[HitboxModel.Count];
        Transform m_Gun;
        byte m_GunFor = 255;
        Team m_TeamFor = (Team)255;
        bool m_FirstPersonHidden;
        public Transform WorldMuzzle { get; private set; }
        public bool HasCharacter => m_Op != null;

        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        void Awake()
        {
            m_P = GetComponent<NetPlayer>();
            if (!Saltline.Core.RunMode.Presentation) { enabled = false; return; }
            var lib = PresentationLibrary.Instance;
            if (lib != null && lib.operatorPrefab != null)
            {
                var body = Instantiate(lib.operatorPrefab, transform, false);
                body.name = "Body";
                m_Root = body.transform;
                m_Op = body.GetComponent<OperatorRig>();
                if (m_Op != null) return;
                Destroy(body);
            }
            m_Root = new GameObject("Body").transform;
            m_Root.SetParent(transform, false);
            for (int i = 0; i < HitboxModel.Count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(go.GetComponent<Collider>());
                go.name = $"Part{i}";
                go.transform.SetParent(m_Root, false);
                m_Parts[i] = go.transform;
                m_PartRenderers[i] = go.GetComponent<MeshRenderer>();
            }
        }

        void Update()
        {
            // NetPlayer (-50) has computed this frame's pose; the rig (310) feeds it to the animator.
            if (m_Op == null) return;
            bool live = m_P != null && m_P.IsSpawned;
            m_Op.Driven = live;
            if (!live) return;
            m_Op.Pose = m_P.Render;
            m_Op.DeathSeed = (int)m_P.OwnerClientId;
        }

        void LateUpdate()
        {
            if (m_P == null || !m_P.IsSpawned) return;
            var r = m_P.Render;

            var team = m_P.TeamState.Value;
            if (team != m_TeamFor) ApplyTeam(team);

            bool alive = r.Alive;
            // The operator stays visible to play its death; the capsules just disappear.
            m_Root.gameObject.SetActive(alive || m_Op != null);

            var rig = m_P.IsOwner ? m_P.LocalRig : null;
            bool hide = rig != null && !rig.ThirdPersonActive;
            if (hide != m_FirstPersonHidden) SetShadowsOnly(hide);

            var w = m_P.Weapons.Get(r.WeaponId);
            if (w != null && w.id != m_GunFor) BuildGun(w);
            if (!alive) return;
            if (m_Op != null) return;   // the rig poses the body and the gun after its animator

            HitboxModel.Build(r.ToHitbox(), m_P.MotorConfig, s_Caps);
            for (int i = 0; i < HitboxModel.Count; i++)
            {
                var c = s_Caps[i];
                Vector3 axis = c.B - c.A;
                float len = axis.magnitude;
                var t = m_Parts[i];
                t.position = (c.A + c.B) * 0.5f;
                t.rotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, axis / len) : Quaternion.identity;
                float d = c.Radius * 2f;
                t.localScale = new Vector3(d, (len + d) * 0.5f, d);
            }
            if (m_Gun != null)
            {
                Quaternion aim = Quaternion.Euler(-r.Pitch, r.Yaw, 0f);
                m_Gun.SetPositionAndRotation(s_Caps[3].B + aim * new Vector3(-0.02f, 0.02f, 0.05f), aim);
            }
        }

        void BuildGun(WeaponDefinition w)
        {
            if (m_Gun != null) Destroy(m_Gun.gameObject);
            m_Gun = WeaponModel.Build(w.weaponClass, m_Root, gameObject.layer == Layers.LocalBody ? Layers.LocalBody : Layers.Default, WeaponView.ThirdPerson, out var muzzle, out _, out var gunRig);
            WorldMuzzle = muzzle;
            m_GunFor = w.id;
            if (m_Op != null) m_Op.SetWeapon(m_Gun, gunRig, w.weaponClass);
            if (m_FirstPersonHidden) SetShadowsOnly(true);
        }

        void ApplyTeam(Team team)
        {
            m_TeamFor = team;
            var lib = PresentationLibrary.Instance;
            if (lib == null) return;
            if (m_Op != null)
            {
                m_Op.SetTeamMaterial(team == Team.Red ? lib.operatorTeamRed : team == Team.Blue ? lib.operatorTeamBlue : lib.operatorTeamNeutral);
                return;
            }
            var mat = team == Team.Red ? lib.teamRed : team == Team.Blue ? lib.teamBlue : lib.neutralBody;
            foreach (var mr in m_PartRenderers) if (mr != null && mat != null) mr.sharedMaterial = mat;
        }

        void SetShadowsOnly(bool on)
        {
            m_FirstPersonHidden = on;
            if (m_Op != null) { m_Op.SetShadowsOnly(on); return; }
            foreach (var mr in m_Root.GetComponentsInChildren<Renderer>(true))
                mr.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
