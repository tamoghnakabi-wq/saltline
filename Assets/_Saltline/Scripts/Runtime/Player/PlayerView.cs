using Saltline.Combat;
using Saltline.FX;
using Saltline.Net;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.Player
{
    /// <summary>
    /// Greybox third-person body drawn from the exact hitbox capsules, so what players see is
    /// what the host tests against. Replaced by the Blender character once it is imported;
    /// the character is authored to these proportions.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class PlayerView : MonoBehaviour
    {
        NetPlayer m_P;
        Transform m_Root;
        readonly Transform[] m_Parts = new Transform[HitboxModel.Count];
        readonly MeshRenderer[] m_PartRenderers = new MeshRenderer[HitboxModel.Count];
        Transform m_Gun;
        byte m_GunFor = 255;
        Team m_TeamFor = (Team)255;
        bool m_FirstPersonHidden;
        public Transform WorldMuzzle { get; private set; }

        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        void Awake()
        {
            m_P = GetComponent<NetPlayer>();
            if (Application.isBatchMode) { enabled = false; return; }
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

        void LateUpdate()
        {
            if (m_P == null || !m_P.IsSpawned) return;
            var r = m_P.Render;

            var team = m_P.TeamState.Value;
            if (team != m_TeamFor) ApplyTeam(team);

            bool alive = r.Alive;
            m_Root.gameObject.SetActive(alive);
            if (!alive) return;

            var rig = m_P.IsLocalHuman ? (m_P.InputProvider as LocalPlayerController)?.Rig : null;
            bool hide = rig != null && !rig.ThirdPersonActive;
            if (hide != m_FirstPersonHidden) SetShadowsOnly(hide);

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

            var w = m_P.Weapons.Get(r.WeaponId);
            if (w != null && w.id != m_GunFor) BuildGun(w);
            if (m_Gun != null)
            {
                Quaternion aim = Quaternion.Euler(-r.Pitch, r.Yaw, 0f);
                m_Gun.SetPositionAndRotation(s_Caps[3].B + aim * new Vector3(-0.02f, 0.02f, 0.05f), aim);
            }
        }

        void BuildGun(WeaponDefinition w)
        {
            if (m_Gun != null) Destroy(m_Gun.gameObject);
            m_Gun = GreyboxGun.Build(w.weaponClass, m_Root, gameObject.layer == Layers.LocalBody ? Layers.LocalBody : Layers.Default, out var muzzle);
            WorldMuzzle = muzzle;
            m_GunFor = w.id;
            if (m_FirstPersonHidden) SetShadowsOnly(true);
        }

        void ApplyTeam(Team team)
        {
            m_TeamFor = team;
            var lib = PresentationLibrary.Instance;
            if (lib == null) return;
            var mat = team == Team.Red ? lib.teamRed : team == Team.Blue ? lib.teamBlue : lib.neutralBody;
            foreach (var mr in m_PartRenderers) if (mr != null && mat != null) mr.sharedMaterial = mat;
        }

        void SetShadowsOnly(bool on)
        {
            m_FirstPersonHidden = on;
            foreach (var mr in m_Root.GetComponentsInChildren<MeshRenderer>(true))
                mr.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
