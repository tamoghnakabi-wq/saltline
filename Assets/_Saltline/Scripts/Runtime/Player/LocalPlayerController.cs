using Saltline.Input;
using Saltline.Settings;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.Player
{
    /// <summary>
    /// Human input for the local player. Mouse look updates the view every rendered frame;
    /// buttons pressed between simulation ticks are latched so short taps are never lost.
    /// In third person the eye-ray aim is derived from what the camera's crosshair points at.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class LocalPlayerController : MonoBehaviour, IPlayerInputProvider
    {
        public static bool InputBlocked; // set by menus / chat

        public NetPlayer Player { get; private set; }
        public PlayerCameraRig Rig { get; private set; }
        public float ViewYaw;
        public float ViewPitch;
        public Vector2 LastLookDelta { get; private set; }

        Vector2 m_RecoilOffset;
        float m_SinceShot = 10f;
        float m_RecoverySpeed = 8f;
        float m_RecoveryDelay = 0.12f;
        InputButtons m_Latched;
        bool m_AimToggle;
        bool m_CrouchToggle;

        public static LocalPlayerController Attach(NetPlayer p)
        {
            var c = p.gameObject.AddComponent<LocalPlayerController>();
            c.Player = p;
            c.ViewYaw = p.SimAimYaw;
            c.ViewPitch = p.SimAimPitch;
            p.InputProvider = c;
            p.ThirdPerson = GameSettings.StartInThirdPerson;
            if (!Application.isBatchMode) c.Rig = PlayerCameraRig.Create(p, c);
            return c;
        }

        void OnDestroy()
        {
            if (Rig != null) Destroy(Rig.gameObject);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        bool Active => !InputBlocked && Application.isFocused;

        void Update()
        {
            if (Player == null) return;
            var gi = GameInput.Instance;
            bool active = Active;
            Cursor.lockState = active ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !active;
            LastLookDelta = Vector2.zero;

            if (active)
            {
                float mult = 1f;
                var w = Player.ActiveWeapon;
                if (Player.SimWeapon.Ads > 0.5f)
                    mult = w != null && w.hasScope ? GameSettings.ScopeSensitivity : GameSettings.AdsSensitivity;

                Vector2 look = gi.Look.ReadValue<Vector2>() * (GameSettings.MouseSensitivity * GameSettings.DegreesPerCount * mult);
                look += gi.LookStick.ReadValue<Vector2>() * (GameSettings.GamepadSensitivity * GameSettings.GamepadDegreesPerSecond * mult * Time.deltaTime);
                float dPitch = GameSettings.InvertY ? -look.y : look.y;
                ViewYaw += look.x;
                ViewPitch += dPitch;
                LastLookDelta = new Vector2(look.x, dPitch);
                // Pulling down against recoil reduces how much the view will auto-recover.
                if (dPitch < 0f && m_RecoilOffset.y > 0f) m_RecoilOffset.y = Mathf.Max(0f, m_RecoilOffset.y + dPitch);

                if (gi.Jump.WasPressedThisFrame()) m_Latched |= InputButtons.Jump;
                if (gi.Fire.WasPressedThisFrame()) m_Latched |= InputButtons.Fire;
                if (gi.Reload.WasPressedThisFrame()) m_Latched |= InputButtons.Reload;
                if (gi.Crouch.WasPressedThisFrame())
                {
                    if (GameSettings.ToggleCrouch) m_CrouchToggle = !m_CrouchToggle;
                    else m_Latched |= InputButtons.Crouch;
                }
                if (gi.Aim.WasPressedThisFrame() && GameSettings.ToggleAim) m_AimToggle = !m_AimToggle;
                if (gi.Primary.WasPressedThisFrame()) m_Latched |= InputButtons.SelectPrimary;
                if (gi.Secondary.WasPressedThisFrame()) m_Latched |= InputButtons.SelectSecondary;
                if (gi.SwapWeapon.WasPressedThisFrame())
                    m_Latched |= Player.SimWeapon.Pending == WeaponSlot.Primary ? InputButtons.SelectSecondary : InputButtons.SelectPrimary;
                if (gi.ToggleView.WasPressedThisFrame()) Player.ThirdPerson = !Player.ThirdPerson;
                if (gi.ShoulderSwap.WasPressedThisFrame() && Rig != null) Rig.SwapShoulder();
            }

            RecoverRecoil(Time.deltaTime);
            ViewPitch = Mathf.Clamp(ViewPitch, -88f, 88f);
            ViewYaw = Mathf.Repeat(ViewYaw, 360f);
        }

        public void Sample(NetPlayer p, ref PlayerIntent intent)
        {
            var gi = GameInput.Instance;
            bool active = Active;
            InputButtons b = active ? m_Latched : InputButtons.None;
            m_Latched = InputButtons.None;
            Vector2 move = Vector2.zero;
            if (active)
            {
                if (gi.Jump.IsPressed()) b |= InputButtons.Jump;
                if (gi.Fire.IsPressed()) b |= InputButtons.Fire;
                if (GameSettings.ToggleAim ? m_AimToggle : gi.Aim.IsPressed()) b |= InputButtons.Aim;
                if (GameSettings.ToggleCrouch ? m_CrouchToggle : gi.Crouch.IsPressed()) b |= InputButtons.Crouch;
                if (gi.Sprint.IsPressed()) b |= InputButtons.Sprint;
                if (gi.Reload.IsPressed()) b |= InputButtons.Reload;
                move = Vector2.ClampMagnitude(gi.Move.ReadValue<Vector2>(), 1f);
            }
            if ((b & InputButtons.Sprint) != 0) m_CrouchToggle = false; // sprinting stands you up

            float aimYaw = ViewYaw, aimPitch = ViewPitch;
            if (Rig != null && Rig.ThirdPersonActive && Rig.TryGetAimAngles(p.RenderEye, out float ay, out float ap))
            {
                aimYaw = ay;
                aimPitch = ap;
            }

            // Movement stays relative to the camera even when the eye-ray aim differs slightly.
            float rel = Mathf.DeltaAngle(aimYaw, ViewYaw);
            if (Mathf.Abs(rel) > 0.01f)
            {
                Vector3 v = Quaternion.Euler(0f, rel, 0f) * new Vector3(move.x, 0f, move.y);
                move = new Vector2(v.x, v.z);
            }

            intent.Move = move;
            intent.AimYaw = aimYaw;
            intent.AimPitch = aimPitch;
            intent.Buttons = b;
        }

        public void OnRecoil(Vector2 kick, WeaponDefinition weapon)
        {
            ViewYaw += kick.x;
            ViewPitch += kick.y;
            m_RecoilOffset += kick * weapon.recoilRecoveryFraction;
            m_SinceShot = 0f;
            m_RecoverySpeed = weapon.recoilRecoverySpeed;
            m_RecoveryDelay = Mathf.Max(weapon.FireInterval * 1.2f, 0.06f);
            if (Rig != null) Rig.OnShot(weapon);
        }

        public void OnRespawned(float yaw)
        {
            ViewYaw = yaw;
            ViewPitch = 0f;
            m_RecoilOffset = Vector2.zero;
            m_CrouchToggle = false;
            m_AimToggle = false;
        }

        void RecoverRecoil(float dt)
        {
            m_SinceShot += dt;
            if (m_SinceShot < m_RecoveryDelay || m_RecoilOffset.sqrMagnitude < 1e-6f) return;
            Vector2 next = Vector2.MoveTowards(m_RecoilOffset, Vector2.zero, m_RecoverySpeed * dt);
            Vector2 d = m_RecoilOffset - next;
            ViewYaw -= d.x;
            ViewPitch -= d.y;
            m_RecoilOffset = next;
        }
    }
}
