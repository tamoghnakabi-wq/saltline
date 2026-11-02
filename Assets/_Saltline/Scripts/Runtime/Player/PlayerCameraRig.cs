using Saltline.Combat;
using Saltline.Settings;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Saltline.Player
{
    /// <summary>
    /// Local camera: first person at the eye, or an over-the-shoulder third-person camera with
    /// wall collision and shoulder swap, blended smoothly. A URP overlay camera draws the
    /// first-person weapon so it never clips into walls.
    ///
    /// Third-person fairness: bullets always start at the character's eye. The crosshair's
    /// target point is found with a camera ray (starting level with the player so nothing
    /// behind the character counts), and the eye-ray is aimed at that point. If geometry
    /// blocks the eye-ray before the target, the HUD shows an obstruction marker exactly
    /// where the bullet will land.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        public Camera MainCamera { get; private set; }
        public Camera ViewmodelCamera { get; private set; }
        public Transform ViewmodelRoot { get; private set; }
        public bool ThirdPersonActive => m_TpBlend > 0.5f;
        public float ThirdPersonBlend => m_TpBlend;
        public bool AimObstructed { get; private set; }
        public Vector3 ObstructionPoint { get; private set; }
        public Vector3 AimPoint => m_AimPoint;

        NetPlayer m_Player;
        LocalPlayerController m_Ctl;
        float m_TpBlend;
        float m_Shoulder = 1f;
        float m_ShoulderTarget = 1f;
        float m_CamDistance = -1f;
        float m_BobPhase;
        float m_Roll;
        float m_Shake;
        float m_LandDip;
        bool m_WasGrounded = true;
        float m_LastAirSpeed;
        Vector3 m_AimPoint;
        bool m_HasAim;
        Behaviour[] m_DisabledSceneCameras;

        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        public static PlayerCameraRig Create(NetPlayer p, LocalPlayerController c)
        {
            var go = new GameObject("PlayerCamera");
            go.tag = "MainCamera";
            var rig = go.AddComponent<PlayerCameraRig>();
            rig.m_Player = p;
            rig.m_Ctl = c;

            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 800f;
            cam.cullingMask = ~(1 << Layers.Viewmodel);
            go.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = Saltline.Settings.VideoSettings.PostProcessing;
            rig.MainCamera = cam;

            var vmGo = new GameObject("ViewmodelCamera");
            vmGo.transform.SetParent(go.transform, false);
            var vm = vmGo.AddComponent<Camera>();
            vm.cullingMask = 1 << Layers.Viewmodel;
            vm.nearClipPlane = 0.01f;
            vm.farClipPlane = 10f;
            vm.fieldOfView = 56f;
            var vmData = vm.GetUniversalAdditionalCameraData();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            data.cameraStack.Add(vm);
            rig.ViewmodelCamera = vm;

            var root = new GameObject("ViewmodelRoot");
            root.layer = Layers.Viewmodel;
            root.transform.SetParent(vmGo.transform, false);
            rig.ViewmodelRoot = root.transform;
            root.AddComponent<ViewmodelController>().Init(p, c, rig);

            // Turn off any scene camera (menu / spectator) while we own the view.
            var list = new System.Collections.Generic.List<Behaviour>();
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (other == cam || other == vm || !other.enabled) continue;
                other.enabled = false;
                list.Add(other);
                var al = other.GetComponent<AudioListener>();
                if (al != null && al.enabled) { al.enabled = false; list.Add(al); }
            }
            rig.m_DisabledSceneCameras = list.ToArray();
            return rig;
        }

        void OnDestroy()
        {
            if (m_DisabledSceneCameras == null) return;
            foreach (var b in m_DisabledSceneCameras) if (b != null) b.enabled = true;
        }

        public void SwapShoulder() => m_ShoulderTarget = -m_ShoulderTarget;

        public void OnShot(WeaponDefinition w) => m_Shake = Mathf.Min(1.2f, m_Shake + w.cameraShake);

        public void AddShake(float amount) => m_Shake = Mathf.Min(1.5f, m_Shake + amount);

        void LateUpdate()
        {
            if (m_Player == null || !m_Player.IsSpawned) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            var pose = m_Player.Render;
            Vector3 eye = m_Player.RenderEye;
            var weapon = m_Player.ActiveWeapon;
            float ads = pose.Ads;

            m_TpBlend = Mathf.MoveTowards(m_TpBlend, m_Player.ThirdPerson ? 1f : 0f, dt / 0.22f);
            m_Shoulder = Mathf.MoveTowards(m_Shoulder, m_ShoulderTarget, dt / 0.18f);

            // ---- First-person motion: bob, strafe roll, landing dip
            float speed = new Vector2(pose.Velocity.x, pose.Velocity.z).magnitude;
            bool bobOn = GameSettings.CameraBob && pose.Grounded && pose.Stance != Stance.Sliding;
            if (bobOn) m_BobPhase += dt * Mathf.Lerp(6f, 13f, Mathf.Clamp01(speed / 7.6f));
            float bobAmt = bobOn ? Mathf.Clamp01(speed / 6f) * (1f - ads * 0.85f) : 0f;
            Vector3 bob = new Vector3(Mathf.Cos(m_BobPhase * 0.5f) * 0.011f, Mathf.Sin(m_BobPhase) * 0.016f, 0f) * bobAmt;

            if (!pose.Grounded) m_LastAirSpeed = Mathf.Max(m_LastAirSpeed, -pose.Velocity.y);
            if (pose.Grounded && !m_WasGrounded) { m_LandDip = Mathf.Clamp(m_LastAirSpeed * 0.012f, 0f, 0.12f); m_LastAirSpeed = 0f; }
            m_WasGrounded = pose.Grounded;
            m_LandDip = Mathf.MoveTowards(m_LandDip, 0f, dt * 0.6f);

            Quaternion yawOnly = Quaternion.Euler(0f, m_Ctl.ViewYaw, 0f);
            float lateral = Vector3.Dot(pose.Velocity, yawOnly * Vector3.right);
            float targetRoll = -lateral * 0.3f * (1f - ads * 0.7f) + (pose.Stance == Stance.Sliding ? -3.5f : 0f);
            m_Roll = Mathf.Lerp(m_Roll, targetRoll, 1f - Mathf.Exp(-8f * dt));

            m_Shake = Mathf.MoveTowards(m_Shake, 0f, dt * 4f);
            float t = Time.time * 30f;
            Quaternion shake = Quaternion.Euler(
                (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * m_Shake * 1.6f,
                (Mathf.PerlinNoise(0.3f, t) - 0.5f) * m_Shake * 1.2f, 0f);

            Quaternion view = Quaternion.Euler(-m_Ctl.ViewPitch, m_Ctl.ViewYaw, m_Roll) * shake;
            Vector3 fpPos = eye + view * bob + Vector3.down * m_LandDip;

            // ---- Third person with collision
            Quaternion tpRot = Quaternion.Euler(-m_Ctl.ViewPitch, m_Ctl.ViewYaw, 0f) * shake;
            Vector3 pivot = eye + Vector3.up * 0.08f;
            Vector3 offset = new Vector3(Mathf.Lerp(0.7f, 0.62f, ads) * m_Shoulder, 0.1f, -Mathf.Lerp(2.5f, 1.4f, ads));
            Vector3 desired = pivot + tpRot * offset;
            Vector3 dir = desired - pivot;
            float dist = dir.magnitude;
            dir /= Mathf.Max(0.001f, dist);
            float allowed = dist;
            if (Physics.SphereCast(pivot, 0.2f, dir, out var hit, dist, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.05f, hit.distance - 0.05f);
            if (m_CamDistance < 0f) m_CamDistance = allowed;
            m_CamDistance = allowed < m_CamDistance ? allowed : Mathf.MoveTowards(m_CamDistance, allowed, dt * 4f);
            Vector3 tpPos = pivot + dir * m_CamDistance;

            float b = Mathf.SmoothStep(0f, 1f, m_TpBlend);
            Vector3 camPos = Vector3.Lerp(fpPos, tpPos, b);
            Quaternion camRot = Quaternion.Slerp(view, tpRot, b);
            transform.SetPositionAndRotation(camPos, camRot);

            // ---- Field of view
            float baseV = GameSettings.VerticalFov(GameSettings.FieldOfView, MainCamera.aspect);
            float adsV = weapon != null ? (weapon.hasScope ? weapon.scopeFov : weapon.adsFov) : baseV;
            float zoom = ads * (b < 0.5f ? 1f : 0.55f);
            MainCamera.fieldOfView = Mathf.Lerp(baseV, Mathf.Min(baseV, adsV), zoom);
            bool scoped = weapon != null && weapon.hasScope && ads > 0.92f && b < 0.5f;
            ViewmodelCamera.enabled = b < 0.5f && pose.Alive && !scoped;

            UpdateAim(camPos, camRot * Vector3.forward, eye, b);
        }

        void UpdateAim(Vector3 camPos, Vector3 camFwd, Vector3 eye, float tpBlend)
        {
            AimObstructed = false;
            if (tpBlend < 0.5f) { m_HasAim = false; return; }

            float skip = Mathf.Max(0f, Vector3.Dot(eye - camPos, camFwd));
            Vector3 o = camPos + camFwd * skip;
            const float range = 400f;
            float best = range;
            if (ShotResolver.EnvironmentRay(o, camFwd, range, out var h)) best = h.distance;
            foreach (var other in NetPlayer.All)
            {
                if (other == m_Player || other == null || !other.Render.Alive) continue;
                var hp = other.Render.ToHitbox();
                if (!HitboxModel.RayNearPlayer(o, camFwd, hp, best)) continue;
                HitboxModel.Build(hp, m_Player.MotorConfig, s_Caps);
                if (HitboxModel.Raycast(o, camFwd, s_Caps, best, out float tt, out _)) best = Mathf.Min(best, tt);
            }
            m_AimPoint = o + camFwd * best;
            m_HasAim = true;

            Vector3 toAim = m_AimPoint - eye;
            float d = toAim.magnitude;
            if (d > 0.3f && ShotResolver.EnvironmentRay(eye, toAim / d, d - 0.1f, out var block))
            {
                AimObstructed = true;
                ObstructionPoint = block.point;
            }
        }

        public bool TryGetAimAngles(Vector3 eye, out float yaw, out float pitch)
        {
            yaw = pitch = 0f;
            if (!m_HasAim) return false;
            Vector3 d = m_AimPoint - eye;
            if (d.sqrMagnitude < 1f) return false;
            d.Normalize();
            if (Vector3.Dot(d, transform.forward) < 0.2f) return false;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            return true;
        }
    }
}
