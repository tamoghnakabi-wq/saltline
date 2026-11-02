using Saltline.Combat;
using Saltline.FX;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.Player
{
    /// <summary>
    /// First-person weapon presentation: hip/ADS placement, look sway, movement bob, sprint
    /// pose, reload/equip motion and a spring-driven visual kick. Purely cosmetic: aim is
    /// never affected by anything here.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class ViewmodelController : MonoBehaviour
    {
        public static ViewmodelController Current { get; private set; }
        public Transform Muzzle { get; private set; }

        NetPlayer m_P;
        LocalPlayerController m_C;
        PlayerCameraRig m_Rig;
        Transform m_Gun;
        Transform m_Sight;
        WeaponRig m_Parts;
        bool m_ReloadFromEmpty;
        WeaponAction m_LastAction;
        byte m_BuiltFor = 255;
        Vector3 m_Sway;
        Vector3 m_KickPos, m_KickRot;
        float m_Bob;
        float m_SprintBlend;

        public void Init(NetPlayer p, LocalPlayerController c, PlayerCameraRig rig)
        {
            m_P = p;
            m_C = c;
            m_Rig = rig;
            Current = this;
            CombatEvents.Shot += OnShot;
        }

        void OnDestroy()
        {
            CombatEvents.Shot -= OnShot;
            if (Current == this) Current = null;
        }

        void OnShot(ShotFx fx, bool local)
        {
            if (!local || m_P == null || fx.Shooter != m_P.OwnerClientId) return;
            var w = m_P.Weapons.Get(fx.WeaponId);
            if (w == null) return;
            float adsDamp = Mathf.Lerp(1f, 0.45f, m_P.SimWeapon.Ads);
            m_KickPos += new Vector3(0f, 0.004f, -w.viewKickBack) * adsDamp;
            m_KickRot += new Vector3(-w.viewKickUp, Random.Range(-0.4f, 0.4f) * w.viewKickRoll, Random.Range(-1f, 1f) * w.viewKickRoll) * adsDamp;
            if (m_Parts != null && fx.WeaponId == m_BuiltFor) m_Parts.OnShot(w.FireInterval);
        }

        void LateUpdate()
        {
            if (m_P == null || !m_P.IsSpawned) return;
            var w = m_P.ActiveWeapon;
            if (w == null) return;
            if (w.id != m_BuiltFor) Rebuild(w);

            float dt = Time.deltaTime;
            var ws = m_P.SimWeapon;
            var pose = m_P.Render;
            float ads = Mathf.SmoothStep(0f, 1f, ws.Ads);

            Vector2 look = m_C != null ? m_C.LastLookDelta : Vector2.zero;
            Vector3 swayTarget = new Vector3(Mathf.Clamp(look.y * 1.4f, -5f, 5f), Mathf.Clamp(-look.x * 1.4f, -5f, 5f), Mathf.Clamp(-look.x * 1.0f, -4f, 4f));
            m_Sway = Vector3.Lerp(m_Sway, swayTarget * (1f - ads * 0.75f), 1f - Mathf.Exp(-9f * dt));

            float speed = new Vector2(pose.Velocity.x, pose.Velocity.z).magnitude;
            if (pose.Grounded) m_Bob += dt * Mathf.Lerp(5f, 12f, Mathf.Clamp01(speed / 7.6f));
            float bobAmt = pose.Grounded ? Mathf.Clamp01(speed / 6f) * (1f - ads * 0.9f) : 0f;
            Vector3 bob = new Vector3(Mathf.Cos(m_Bob * 0.5f) * 0.012f, Mathf.Abs(Mathf.Sin(m_Bob * 0.5f)) * 0.01f, 0f) * bobAmt;

            m_SprintBlend = Mathf.MoveTowards(m_SprintBlend, pose.Sprinting ? 1f : 0f, dt / 0.15f);

            float lower = 0f;
            Vector3 actionRot = Vector3.zero;
            float p = ws.ActionProgress;
            switch (ws.Action)
            {
                case WeaponAction.Reloading:
                case WeaponAction.ShellReloading:
                    float k = Mathf.Sin(p * Mathf.PI);
                    lower = k * 0.06f;
                    actionRot = new Vector3(k * 12f, 0f, k * 22f);
                    break;
                case WeaponAction.Equipping:
                    lower = (1f - p) * 0.28f;
                    actionRot = new Vector3((1f - p) * 30f, 0f, 0f);
                    break;
                case WeaponAction.Holstering:
                    lower = p * 0.28f;
                    actionRot = new Vector3(p * 30f, 0f, 0f);
                    break;
                case WeaponAction.Cycling:
                    float c = Mathf.Sin(p * Mathf.PI);
                    actionRot = new Vector3(c * 4f, 0f, c * -8f);
                    break;
            }

            m_KickPos = Vector3.Lerp(m_KickPos, Vector3.zero, 1f - Mathf.Exp(-16f * dt));
            m_KickRot = Vector3.Lerp(m_KickRot, Vector3.zero, 1f - Mathf.Exp(-14f * dt));

            // Aiming lines the gun's sight point up with the eye, at the weapon's eye relief.
            Vector3 adsPos = m_Sight != null ? new Vector3(0f, 0f, w.adsEyeRelief) - m_Sight.localPosition : w.adsViewmodelOffset;
            Vector3 basePos = Vector3.Lerp(w.viewmodelOffset, adsPos, ads);
            Vector3 sprintPos = new Vector3(-0.06f, -0.05f, -0.04f) * m_SprintBlend;
            Vector3 sprintRot = new Vector3(8f, -28f, 12f) * m_SprintBlend;
            m_Gun.localPosition = basePos + bob + sprintPos + m_KickPos + Vector3.down * lower;
            m_Gun.localRotation = Quaternion.Euler(m_Sway + m_KickRot + actionRot + sprintRot);
            m_Gun.gameObject.SetActive(pose.Alive);

            // Moving parts. An empty reload is remembered from its first frame (ammo changes at the commit point).
            if (ws.Action == WeaponAction.Reloading && m_LastAction != WeaponAction.Reloading) m_ReloadFromEmpty = ws.Mag == 0;
            m_LastAction = ws.Action;
            if (m_Parts != null) m_Parts.Tick(ws.Action, p, m_ReloadFromEmpty, ws.Mag == 0, ws.TriggerHeld, dt);
        }

        void Rebuild(WeaponDefinition w)
        {
            if (m_Gun != null) Destroy(m_Gun.gameObject);
            m_Gun = WeaponModel.Build(w.weaponClass, transform, Layers.Viewmodel, WeaponView.FirstPerson, out var muzzle, out m_Sight, out m_Parts);
            Muzzle = muzzle;
            m_BuiltFor = w.id;
        }
    }
}
