using Saltline.Player;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.FX
{
    /// <summary>
    /// Drives the skinned operator from a player's RenderPose.
    ///
    /// Update: sets the Humanoid animator's locomotion parameters (direction x gait, crouch, air,
    /// death) and scales playback so the feet keep pace with the real ground speed.
    /// LateUpdate, after the animator: blades and pitches the torso with the aim, holds the weapon
    /// along the true aim direction from the right shoulder, and puts both hands on it with an
    /// analytic two-bone IK (the support hand also does the magazine run during reloads).
    ///
    /// Hit detection never reads these bones: the host uses the analytic HitboxModel capsules, and
    /// the character is authored to those proportions.
    /// </summary>
    [DefaultExecutionOrder(310)]
    public sealed class OperatorRig : MonoBehaviour
    {
        public const string MoveXName = "MoveX", MoveZName = "MoveZ", CrouchedName = "Crouched",
            GroundedName = "Grounded", DeadName = "Dead", DeathIndexName = "DeathIndex";
        static readonly int s_MoveX = Animator.StringToHash(MoveXName), s_MoveZ = Animator.StringToHash(MoveZName),
            s_Crouched = Animator.StringToHash(CrouchedName), s_Grounded = Animator.StringToHash(GroundedName),
            s_Dead = Animator.StringToHash(DeadName), s_DeathIndex = Animator.StringToHash(DeathIndexName);

        public Animator animator;
        [Header("Forward speed of each gait's clip (m/s); set from the clips by CharacterArtFactory")]
        public float walkSpeed = 1.5f;
        public float runSpeed = 3.6f;
        public float sprintSpeed = 5.4f;
        public float crouchSpeed = 1.2f;
        public int deathClips = 5;
        [Header("Head height per stance (matches HitboxModel / MotorSettings eye heights)")]
        public float StandEye = 1.62f;
        public float CrouchEye = 1.0f;
        public float SlideEye = 0.82f;
        [Tooltip("Head bone sits this far below the eyes on the operator (measured from the bind pose at startup).")]
        public float HeadBelowEye = 0.07f;
        public float MaxDrop = 0.35f;

        /// <summary>Pose to show this frame. Set by PlayerView (or a showcase) before this Update runs.</summary>
        public RenderPose Pose { get; set; }
        public bool Driven { get; set; }
        /// <summary>Picks the death clip, so each client shows the same one for the same player and death.</summary>
        public int DeathSeed { get; set; }
        /// <summary>Diagnostics: the visual eye height above the feet after the body-height shift.</summary>
        public float HeadHeight { get; private set; }
        /// <summary>Diagnostics: how far each wrist ended from its IK target (m), and the clip blend's own speed.</summary>
        public float RightHandError { get; private set; }
        public float LeftHandError { get; private set; }
        public float ClipSpeed { get; private set; }

        Transform m_Hips, m_Spine, m_Chest, m_UpperChest, m_Head;
        ArmChain m_R, m_L;
        ArmChain m_LegR, m_LegL;   // upper leg, lower leg, foot (no hand orientation)
        float m_Shift;
        bool m_Ready;

        Transform m_Gun;
        WeaponRig m_GunRig;
        WeaponClass m_Class;
        Vector3 m_GripL, m_MagWell;   // in the gun holder's space
        bool m_HasGripL;

        Renderer[] m_Renderers;
        bool m_WasDead;
        int m_Deaths;
        float m_Air, m_Sprint, m_Slide, m_Ads;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            if (animator == null || !animator.isHuman) return;
            m_Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            m_Spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            m_Chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            m_UpperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            m_Head = animator.GetBoneTransform(HumanBodyBones.Head);
            m_R = MakeArm(animator, true);
            m_L = MakeArm(animator, false);
            m_LegR = new ArmChain { upper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), fore = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), hand = animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            m_LegL = new ArmChain { upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), fore = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), hand = animator.GetBoneTransform(HumanBodyBones.LeftFoot) };
            // Before the first animator update the bones are in the bind pose, authored with the eyes at 1.62 m.
            if (m_Head != null) HeadBelowEye = Mathf.Clamp(1.62f - (m_Head.position.y - transform.position.y), 0f, 0.2f);
            m_Ready = m_R.Valid && m_L.Valid;
        }

        static ArmChain MakeArm(Animator a, bool right)
        {
            var little = a.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (little == null) little = a.GetBoneTransform(right ? HumanBodyBones.RightRingProximal : HumanBodyBones.LeftRingProximal);
            return ArmChain.Create(
                a.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm),
                a.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm),
                a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand),
                a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal),
                a.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal),
                little);
        }

        /// <summary>Hand the rig the third-person weapon (a WeaponModel holder whose origin is the right-hand grip).</summary>
        public void SetWeapon(Transform holder, WeaponRig rig, WeaponClass cls)
        {
            m_Gun = holder;
            m_GunRig = rig;
            m_Class = cls;
            var gl = FindDeep(holder, "Grip_L");
            var mw = FindDeep(holder, "MagWell");
            m_HasGripL = gl != null;
            m_GripL = gl != null ? holder.InverseTransformPoint(gl.position) : new Vector3(0f, -0.01f, 0.32f);
            m_MagWell = mw != null ? holder.InverseTransformPoint(mw.position) : new Vector3(0f, 0f, 0.1f);
            if (cls == WeaponClass.Pistol && !m_HasGripL) m_GripL = new Vector3(-0.01f, -0.02f, 0.005f);
            m_Renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void SetTeamMaterial(Material team)
        {
            if (team == null) return;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats.Length < 2) continue;
                mats[1] = team;
                r.sharedMaterials = mats;
            }
        }

        public void SetShadowsOnly(bool on)
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in m_Renderers) r.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        // ------------------------------------------------------------------ animator parameters

        void Update()
        {
            if (!Driven || animator == null) return;
            float dt = Time.deltaTime;
            var r = Pose;
            transform.SetPositionAndRotation(r.Feet, Quaternion.Euler(0f, r.Yaw, 0f));

            bool dead = !r.Alive;
            if (dead && !m_WasDead)
            {
                animator.SetInteger(s_DeathIndex, Mathf.Abs(DeathSeed * 7 + m_Deaths * 3) % Mathf.Max(1, deathClips));
                m_Deaths++;
            }
            m_WasDead = dead;
            animator.SetBool(s_Dead, dead);
            if (dead) { animator.speed = 1f; return; }

            Vector3 v = Quaternion.Inverse(transform.rotation) * r.Velocity;
            v.y = 0f;
            float speed = v.magnitude;
            bool crouched = r.Stance != Stance.Standing;
            bool sliding = r.Stance == Stance.Sliding;
            Vector2 dir = speed > 0.12f ? new Vector2(v.x, v.z) / speed : Vector2.zero;

            float g, reference;
            if (crouched) { g = Mathf.Clamp01(speed / crouchSpeed); reference = crouchSpeed * g; }
            else Gait(speed, r.Sprinting, out g, out reference);
            if (sliding) g = 0f;   // slides use the crouch idle plus a lean (see LateUpdate)

            animator.SetFloat(s_MoveX, dir.x * g, 0.1f, dt);
            animator.SetFloat(s_MoveZ, dir.y * g, 0.1f, dt);
            animator.SetBool(s_Crouched, crouched);
            m_Air = r.Grounded ? 0f : m_Air + dt;
            bool grounded = r.Grounded || m_Air < 0.15f;   // steps and small drops stay on the ground
            animator.SetBool(s_Grounded, grounded);

            // Feet keep pace with the ground: scale playback by real speed over the clip blend's speed.
            float target = 1f;
            if (speed > 0.3f && grounded && !sliding)
            {
                float clip = animator.speed > 0.05f ? animator.velocity.magnitude / animator.speed : 0f;
                if (clip < 0.2f) clip = Mathf.Max(0.2f, reference);
                ClipSpeed = clip;
                target = Mathf.Clamp(speed / clip, 0.7f, 1.5f);
            }
            animator.speed = Mathf.MoveTowards(animator.speed, target, dt * 2.5f);
        }

        void Gait(float s, bool sprint, out float g, out float reference)
        {
            reference = s;
            if (s <= walkSpeed) g = s / walkSpeed;
            else if (s <= runSpeed) g = 1f + (s - walkSpeed) / Mathf.Max(0.1f, runSpeed - walkSpeed);
            else if (sprint)
            {
                float t = Mathf.Clamp01((s - runSpeed) / Mathf.Max(0.1f, sprintSpeed - runSpeed));
                g = 2f + t;
                reference = Mathf.Lerp(runSpeed, sprintSpeed, t);
            }
            else { g = 2f; reference = runSpeed; }
            if (sprint && s > walkSpeed) g = Mathf.Max(g, 2.6f);
            if (!sprint) g = Mathf.Min(g, 2f);
        }

        // ------------------------------------------------------------------ procedural upper body

        void LateUpdate()
        {
            if (!Driven || !m_Ready) return;
            var r = Pose;
            if (m_Gun != null && m_Gun.gameObject.activeSelf != r.Alive) m_Gun.gameObject.SetActive(r.Alive);
            if (!r.Alive) return;
            float dt = Time.deltaTime;

            Quaternion yaw = transform.rotation;
            Vector3 up = Vector3.up, right = yaw * Vector3.right, fwd = yaw * Vector3.forward;
            float pitch = Mathf.Clamp(r.Pitch, -70f, 70f);
            bool pistol = m_Class == WeaponClass.Pistol;

            m_Slide = Mathf.MoveTowards(m_Slide, r.Stance == Stance.Sliding ? 1f : 0f, dt * 6f);
            m_Sprint = Mathf.MoveTowards(m_Sprint, r.Sprinting && r.Grounded && r.Action == WeaponAction.Ready ? 1f : 0f, dt * 5f);
            m_Ads = Mathf.MoveTowards(m_Ads, r.Ads, dt * 6f);

            // Body height: the clips' heads do not sit where the host's hitbox puts them (the crouch clips are
            // ~0.3 m too tall, the jump loop tucks ~0.4 m too low). Shift the hips so the head matches; on the
            // ground the legs are re-solved so the feet stay planted, in the air the feet just come along.
            float targetHead = (r.Stance == Stance.Crouching ? CrouchEye : r.Stance == Stance.Sliding ? SlideEye : StandEye) - HeadBelowEye;
            float headNow = m_Head != null ? m_Head.position.y - transform.position.y : targetHead;
            bool air = !r.Grounded && m_Air > 0.15f;
            float delta = targetHead - headNow;
            float want = air ? Mathf.Clamp(delta, 0f, 0.5f)
                : r.Stance == Stance.Standing ? Mathf.Clamp(delta, 0f, 0.05f)
                : Mathf.Clamp(delta, -MaxDrop, 0.05f);
            m_Shift = Mathf.MoveTowards(m_Shift, want, dt * 1.6f);
            if (Mathf.Abs(m_Shift) > 0.002f && m_Hips != null && m_LegR.Valid && m_LegL.Valid)
            {
                Vector3 fr = m_LegR.hand.position, fl = m_LegL.hand.position;
                Quaternion qr = m_LegR.hand.rotation, ql = m_LegL.hand.rotation;
                Vector3 kr = m_LegR.fore.position + fwd * 0.3f, kl = m_LegL.fore.position + fwd * 0.3f;
                m_Hips.position += up * m_Shift;
                if (!air)
                {
                    Solve(m_LegR, fr, kr, Vector3.zero, Vector3.zero);
                    Solve(m_LegL, fl, kl, Vector3.zero, Vector3.zero);
                    m_LegR.hand.rotation = qr;
                    m_LegL.hand.rotation = ql;
                }
            }
            HeadHeight = m_Head != null ? m_Head.position.y - transform.position.y + HeadBelowEye : 0f;

            // Slide: lean the whole body back, feet first.
            if (m_Slide > 0f && m_Hips != null) m_Hips.rotation = Quaternion.AngleAxis(-24f * Smooth(m_Slide), right) * m_Hips.rotation;

            // Torso: blade the shoulders (support side forward) and pitch with the aim; the head turns back to the aim.
            float blade = 0f;
            Vector3 shoulders = m_R.upper.position - m_L.upper.position;
            shoulders.y = 0f;
            if (shoulders.sqrMagnitude > 1e-4f) blade = Vector3.SignedAngle(right, shoulders, up);
            float bladeTarget = pistol ? 8f : 24f;
            float twist = Mathf.Clamp(bladeTarget - blade, -35f, 35f) * (1f - 0.6f * m_Sprint);
            float pitchBody = pitch * (1f - 0.5f * m_Sprint);
            var chain = new[] { m_Spine, m_Chest, m_UpperChest };
            var w = new[] { 0.25f, 0.35f, 0.40f };
            for (int i = 0; i < chain.Length; i++)
            {
                if (chain[i] == null) continue;
                chain[i].rotation = Quaternion.AngleAxis(-pitchBody * w[i], right) * Quaternion.AngleAxis(twist * w[i], up) * chain[i].rotation;
            }
            if (m_Head != null) m_Head.rotation = Quaternion.AngleAxis(-twist * 0.9f, up) * Quaternion.AngleAxis(-(pitch - pitchBody) * 0.6f, right) * m_Head.rotation;

            if (m_Gun == null) return;

            // Weapon: shouldered along the aim; lowered to port arms when sprinting, down and in when switching.
            Quaternion aim = yaw * Quaternion.Euler(-pitch, 0f, 0f);
            float p = r.ActionProgress;
            float switching = r.Action == WeaponAction.Holstering ? Smooth(p) : r.Action == WeaponAction.Equipping ? 1f - Smooth(p) : 0f;
            float reload = r.Action == WeaponAction.Reloading || r.Action == WeaponAction.ShellReloading ? Ramp(p, 0f, 0.12f) * (1f - Ramp(p, 0.85f, 1f)) : 0f;
            Vector3 hold = pistol ? new Vector3(-0.17f, -0.06f, 0.44f) : new Vector3(-0.05f, -0.06f, 0.29f);
            hold.y += 0.03f * m_Ads;
            float low = Smooth(m_Sprint);
            hold += low * (pistol ? new Vector3(0.02f, -0.22f, -0.14f) : new Vector3(-0.10f, -0.10f, -0.05f));
            hold += switching * new Vector3(-0.02f, -0.12f, -0.08f);
            hold += reload * new Vector3(-0.03f, -0.04f, -0.04f);
            // Sprint "low ready": muzzle down about 25 degrees and across the chest.
            Quaternion gunRot = aim * Quaternion.Euler((pistol ? 40f : 24f) * low + 50f * switching - 10f * reload, (pistol ? -10f : -38f) * low, -16f * low + 24f * reload);
            Vector3 gunPos = m_R.upper.position + aim * hold;
            m_Gun.SetPositionAndRotation(gunPos, gunRot);

            if (m_GunRig != null) m_GunRig.Tick(r.Action, p, false, false, false, dt);

            Vector3 gx = gunRot * Vector3.right, gy = gunRot * Vector3.up, gz = gunRot * Vector3.forward;

            // Right hand on the grip.
            Vector3 rLong = (gz - gy * (pistol ? 0.25f : 0.3f)).normalized;
            Vector3 rAcross = (-gy - gz * 0.3f).normalized;
            Vector3 rWrist = gunPos - rLong * (m_R.palm * 0.85f) + gx * 0.015f;
            Solve(m_R, rWrist, m_R.upper.position + (-up * 0.8f + right * (pistol ? 0.3f : 0.6f) - fwd * 0.1f), rLong, rAcross);
            RightHandError = Vector3.Distance(m_R.hand.position, rWrist);

            // Support hand: on the handguard (pumping with a shotgun), or the magazine run during a reload.
            Vector3 lLong, lAcross;
            if (pistol) { lLong = (gz * 1f - gy * 0.35f + gx * 0.15f).normalized; lAcross = (-gy - gz * 0.2f).normalized; }
            else { lLong = (gx * 0.8f + gy * 0.35f + gz * 0.45f).normalized; lAcross = -gz; }
            Vector3 grip = m_Gun.TransformPoint(m_GripL);
            if (r.Action == WeaponAction.Cycling && m_Class == WeaponClass.Shotgun)
                grip -= gz * (0.09f * Ramp(p, 0.05f, 0.42f) * (1f - Ramp(p, 0.5f, 0.88f)));
            Vector3 lWrist = grip - lLong * (m_L.palm * 0.85f) + (pistol ? -gx * 0.02f : -gy * 0.025f);

            // Keep the support hand on the weapon when the arm cannot reach: slide it back along the gun.
            float reach = (Vector3.Distance(m_L.upper.position, m_L.fore.position) + Vector3.Distance(m_L.fore.position, m_L.hand.position)) * 0.985f;
            for (int i = 0; i < 8 && Vector3.Distance(m_L.upper.position, lWrist) > reach; i++) lWrist -= gz * 0.03f;

            if (reload > 0f || r.Action == WeaponAction.Reloading || r.Action == WeaponAction.ShellReloading)
            {
                Vector3 well = m_Gun.TransformPoint(m_MagWell) - gy * 0.06f - lLong * (m_L.palm * 0.6f);
                Vector3 pouch = (m_Hips != null ? m_Hips.position : transform.position + up) + fwd * 0.16f - right * 0.13f + up * 0.04f;
                Vector3 target;
                if (r.Action == WeaponAction.ShellReloading)
                    target = Vector3.Lerp(well, pouch, 0.5f - 0.5f * Mathf.Cos(p * Mathf.PI * 2f));
                else if (p < 0.15f) target = Vector3.Lerp(lWrist, well, Smooth(p / 0.15f));
                else if (p < 0.32f) target = Vector3.Lerp(well, pouch, Smooth((p - 0.15f) / 0.17f));
                else if (p < 0.42f) target = pouch;
                else if (p < 0.58f) target = Vector3.Lerp(pouch, well, Smooth((p - 0.42f) / 0.16f));
                else if (p < 0.74f) target = well;
                else target = Vector3.Lerp(well, lWrist, Smooth(Mathf.Clamp01((p - 0.74f) / 0.14f)));
                lWrist = target;
                Vector3 palmUp = (gz * 0.9f + gy * 0.4f).normalized;
                float k = Ramp(p, 0.05f, 0.15f) * (1f - Ramp(p, 0.74f, 0.88f));
                lLong = Vector3.Slerp(lLong, palmUp, k);
                lAcross = Vector3.Slerp(lAcross, -gx, k);
            }
            Solve(m_L, lWrist, m_L.upper.position + (-up * 0.9f - right * (pistol ? 0.3f : 0.35f)), lLong, lAcross);
            LeftHandError = Vector3.Distance(m_L.hand.position, lWrist);
        }

        static void Solve(in ArmChain arm, Vector3 target, Vector3 pole, Vector3 handLong, Vector3 handAcross)
            => ArmChain.Solve(arm, target, pole, handLong, handAcross);

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float Ramp(float x, float a, float b) => Smooth((x - a) / (b - a));
    }
}
