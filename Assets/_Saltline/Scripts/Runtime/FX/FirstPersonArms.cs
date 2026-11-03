using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.FX
{
    /// <summary>
    /// The first-person arms (SK_OperatorArms, the operator's own sleeves and gloves cut from the body by
    /// ArtSource/Blender/operator_build.py finalize_arms). There is no animator: every frame the arms are put
    /// back in their bind pose and posed procedurally on the viewmodel gun.
    ///
    /// - Each shoulder sits at its own anchor, chosen per weapon below and behind the visible frame (so the
    ///   cut at the shoulder is never on screen), at a distance that leaves the elbow naturally bent.
    /// - Each hand is placed by its middle knuckle and its axes (fingers' direction, knuckle line), worked out
    ///   from the gun's markers as a wrap around the grip or handguard; two-bone IK then reaches the wrist.
    /// - The fingers curl about their bind-pose axes by the angles that wrap them around that shape; the right
    ///   index finger is fitted to the trigger blade and the thumbs are aimed along the frame.
    /// - Reloads: the support hand goes to the magazine, down out of view while the old one falls, and comes
    ///   back holding the new one (it follows WeaponRig's magazine). Shell reloads feed the loading port, the
    ///   support hand rides the shotgun's pump, and the right hand works the rifle's bolt.
    ///
    /// Must be a sibling of the gun's root under the same parent (the camera-space viewmodel root).
    /// Purely cosmetic: nothing here touches aim or hit detection.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonArms : MonoBehaviour
    {
        [Tooltip("The imported arms model under this root.")] public Transform model;

        public float RightHandError { get; private set; }
        public float LeftHandError { get; private set; }
        /// <summary>Diagnostics: how far the right index fingertip ended from the trigger blade (m).</summary>
        public float TriggerFingerError { get; private set; }
        public bool Bound => m_Gun != null && m_GripR != null;
        /// <summary>Shoulder joints for the bound weapon, in the viewmodel parent's (camera) space.</summary>
        public Vector3 AnchorRight => m_AnchorR;
        public Vector3 AnchorLeft => m_AnchorL;

        // Per-joint curl (degrees) of index, middle, ring, little and thumb, three joints each.
        // Wrapping a cylinder: each joint bends by half the arc of the two bones it joins (bone length / radius).
        static readonly float[] GripR = { 10, 30, 25, 44, 68, 44, 46, 70, 44, 40, 60, 40, 0, 15, 10 };
        static readonly float[] Handguard = { 33, 50, 34, 35, 53, 36, 36, 54, 36, 36, 52, 34, 0, 10, 8 };
        static readonly float[] MagGrip = { 50, 85, 22, 55, 85, 20, 56, 85, 20, 56, 80, 20, 0, 10, 8 };
        static readonly float[] Wrap = { 30, 46, 31, 32, 48, 32, 33, 49, 32, 34, 48, 30, 0, 8, 6 };
        static readonly float[] HoldMag = { 30, 40, 26, 34, 44, 28, 38, 46, 28, 42, 48, 28, 10, 18, 12 };
        static readonly float[] Pinch = { 40, 54, 32, 58, 72, 46, 64, 76, 46, 68, 76, 44, 20, 28, 20 };
        static readonly float[] Relaxed = { 20, 26, 16, 24, 30, 18, 28, 32, 18, 32, 34, 18, 8, 12, 8 };

        struct Finger
        {
            public Transform[] bone;     // three joints
            public Transform tip;        // end of the third bone
            public Quaternion[] bind;    // their bind-pose local rotations
            public Vector3[] axis;       // curl axes in each joint's own space
        }

        struct Side
        {
            public ArmChain arm;
            public Transform clavicle;
            public Vector3 clavicleBindPos;
            public Quaternion clavicleBind, upperBind, foreBind, handBind;
            public Finger[] fingers;     // index, middle, ring, little, thumb
            public Vector3 bindJoint;    // upper-arm joint in this root's space, model at rest
            public bool Valid => arm.Valid && clavicle != null;
        }

        /// <summary>Where a hand goes this frame (world space).</summary>
        struct Hand
        {
            public Vector3 knuckle;      // middle finger root
            public Vector3 along;        // wrist -> knuckles
            public Vector3 across;       // index -> little knuckle
            public float[] curl;
            public Vector3 thumbAim;     // a point the thumb points at, or with thumbLine a point on the line its tip lies on
            public Vector3 thumbLine;    // direction of that line (zero: aim at the point)
            public float thumbWeight;
            public float thumbMaxTurn;
        }

        Side m_R, m_L;
        bool m_Init, m_Ready;
        Vector3 m_ModelPos;
        Quaternion m_ModelRot;
        Renderer[] m_Renderers;

        Transform m_Gun, m_GripR, m_GripL, m_MagWell;
        WeaponRig m_Rig;
        WeaponClass m_Class;
        float m_GuardRadius, m_GuardCentre;
        const float MagHalfDepth = 0.024f;
        Vector3 m_AnchorR, m_AnchorL;   // shoulder joints, in this root's (= the gun parent's) space
        Vector3 m_MagBottom;            // in the magazine's space
        Vector3 m_BoltHandle;           // in the bolt's space
        Vector3 m_TriggerFace;          // in the trigger's space
        bool m_HasTrigger;
        Vector3 m_LKnuckleLocal;        // support hand's reload path, eased, in the parent's (camera) space
        float m_ReloadWeight, m_ReloadK, m_ReloadRelaxed;
        bool m_HadReload;
        readonly float[] m_CurlR = new float[15], m_CurlL = new float[15];

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

        void Awake() => Init();

        /// <summary>Finds the bones (Awake does this; edit-mode tools and tests reach it through Bind).</summary>
        void Init()
        {
            if (m_Init) return;
            m_Init = true;
            if (model == null) model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            m_ModelPos = model.localPosition;
            m_ModelRot = model.localRotation;
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_R = MakeSide("Right");
            m_L = MakeSide("Left");
            m_Ready = m_R.Valid && m_L.Valid;
            if (!m_Ready) Debug.LogWarning("[SALTLINE] FirstPersonArms: arm bones not found; the arms stay hidden.");
            foreach (var r in m_Renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                if (r is SkinnedMeshRenderer smr)
                {
                    smr.skinnedMotionVectors = false;
                    smr.quality = SkinQuality.Bone4;
                    // The shoulders are moved well away from the bind pose; a generous box keeps the arms from being culled.
                    smr.localBounds = new Bounds(smr.localBounds.center, Vector3.one * 4f);
                }
            }
            SetVisible(false);
        }

        Side MakeSide(string prefix)
        {
            var s = new Side
            {
                clavicle = FindDeep(model, prefix + "Shoulder"),
            };
            var upper = FindDeep(model, prefix + "Arm");
            var fore = FindDeep(model, prefix + "ForeArm");
            var hand = FindDeep(model, prefix + "Hand");
            var index = FindDeep(model, prefix + "HandIndex1");
            var middle = FindDeep(model, prefix + "HandMiddle1");
            var little = FindDeep(model, prefix + "HandPinky1");
            if (little == null) little = FindDeep(model, prefix + "HandRing1");
            s.arm = ArmChain.Create(upper, fore, hand, middle, index, little);
            if (!s.Valid) return s;
            s.clavicleBindPos = s.clavicle.localPosition;
            s.clavicleBind = s.clavicle.localRotation;
            s.upperBind = upper.localRotation;
            s.foreBind = fore.localRotation;
            s.handBind = hand.localRotation;
            s.bindJoint = transform.InverseTransformPoint(upper.position);

            // The arms are authored in a T-pose with the palms down: each joint curls about (finger direction x down).
            Vector3 palmDown = -transform.up;
            s.fingers = new Finger[FingerNames.Length];
            for (int f = 0; f < FingerNames.Length; f++)
            {
                var fg = new Finger { bone = new Transform[3], bind = new Quaternion[3], axis = new Vector3[3] };
                for (int k = 0; k < 3; k++)
                {
                    var b = FindDeep(hand, $"{prefix}Hand{FingerNames[f]}{k + 1}");
                    var next = FindDeep(hand, $"{prefix}Hand{FingerNames[f]}{k + 2}");
                    if (b == null) break;
                    fg.bone[k] = b;
                    fg.bind[k] = b.localRotation;
                    if (k == 2) fg.tip = next;
                    Vector3 dir = next != null ? next.position - b.position : b.position - b.parent.position;
                    Vector3 ax = Vector3.Cross(dir.normalized, palmDown);
                    fg.axis[k] = ax.sqrMagnitude > 1e-6f ? b.InverseTransformDirection(ax.normalized) : Vector3.right;
                }
                s.fingers[f] = fg;
            }
            return s;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        public void SetLayer(int layer)
        {
            Init();
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
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

        void SetVisible(bool on)
        {
            if (m_Renderers == null) return;
            foreach (var r in m_Renderers) if (r != null && r.enabled != on) r.enabled = on;
        }

        static Bounds MeshBounds(Transform t)
        {
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds();
        }

        /// <summary>Attach to a newly built first-person gun.</summary>
        /// <param name="gun">The gun's model root (bore along +z), a sibling of this root.</param>
        /// <param name="hipOffset">The gun's hip position in the shared parent's space (WeaponDefinition.viewmodelOffset).</param>
        public void Bind(Transform gun, WeaponRig rig, WeaponClass cls, Vector3 hipOffset)
        {
            Init();
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            m_Gun = gun;
            m_Rig = rig;
            m_Class = cls;
            m_GripR = gun != null ? gun.Find("Grip_R") : null;
            m_GripL = gun != null ? gun.Find("Grip_L") : null;
            m_MagWell = gun != null ? gun.Find("MagWell") : null;
            m_HadReload = false;
            m_ReloadWeight = 0f;
            if (!m_Ready || m_GripR == null) { SetVisible(false); return; }

            // What the support hand wraps, measured on the models around Grip_L (ArmsPreview logs the outlines):
            // a handguard or pump as a round section of this radius, centred this far above the marker; on the SMG,
            // the forward magazine, held like a vertical grip.
            (m_GuardRadius, m_GuardCentre) = cls switch
            {
                WeaponClass.Shotgun => (0.021f, 0.017f),      // pump: 4.4 wide, 4.0 tall
                WeaponClass.Sniper => (0.026f, 0.028f),       // forend: 5.6 wide
                WeaponClass.SMG => (0.015f, 0f),              // magazine: 3.0 wide, 4.7 deep
                _ => (0.024f, 0.029f),                        // rifle handguard: 4.6 wide, 5.4 tall with the rail
            };

            // Shoulder anchors from the grips at the hip: below and behind them, out of the frame.
            bool pistol = cls == WeaponClass.Pistol;
            Vector3 gripR = hipOffset + gun.localRotation * m_GripR.localPosition;
            Vector3 gripL = m_GripL != null ? hipOffset + gun.localRotation * m_GripL.localPosition : gripR + new Vector3(-0.02f, 0f, 0.25f);
            m_AnchorR = gripR + new Vector3(0.03f, -0.13f, -0.33f);
            m_AnchorL = gripL + (pistol ? new Vector3(-0.22f, -0.13f, -0.30f) : new Vector3(-0.27f, -0.13f, -0.30f));

            m_MagBottom = Vector3.zero;
            if (rig != null && rig.Mag != null)
            {
                var b = MeshBounds(rig.Mag);
                m_MagBottom = new Vector3(b.center.x, b.min.y, b.center.z);
            }
            m_BoltHandle = Vector3.zero;
            if (rig != null && rig.Bolt != null)
            {
                var b = MeshBounds(rig.Bolt);
                m_BoltHandle = new Vector3(b.max.x - 0.01f, b.center.y, b.min.z + 0.25f * b.size.z);   // the knob: far right, towards the rear
            }
            m_HasTrigger = rig != null && rig.Trigger != null;
            if (m_HasTrigger)
            {
                var b = MeshBounds(rig.Trigger);
                m_TriggerFace = new Vector3(b.center.x, b.center.y - 0.15f * b.size.y, b.max.z);   // front face, lower half of the blade
            }
        }

        /// <param name="progress">0..1 through the current action.</param>
        /// <param name="trigger">Trigger held (or a shot just went off).</param>
        /// <param name="shellInsert">Shell reload past its lead-in (WeaponState.ShellInserting): progress runs once per shell.</param>
        public void Pose(WeaponAction action, float progress, bool trigger, bool visible, float dt, bool shellInsert = false)
        {
            bool show = visible && m_Ready && Bound && m_Gun.gameObject.activeInHierarchy;
            SetVisible(show);
            if (!show) return;

            Reset(ref m_R);
            Reset(ref m_L);
            PlaceShoulders();

            Transform parent = transform.parent != null ? transform.parent : transform;
            Vector3 gx = m_Gun.right, gy = m_Gun.up, gz = m_Gun.forward;
            Vector3 camRight = parent.right, camUp = parent.up, camFwd = parent.forward;
            bool pistol = m_Class == WeaponClass.Pistol;
            float p = progress;

            // ---- right hand: wrapped around the pistol grip from the right, index finger on the trigger
            Vector3 front = (gz - gy * (pistol ? 0.25f : 0.3f)).normalized;       // across the grip, forwards
            Vector3 down = (-gy - gz * (pistol ? 0.25f : 0.3f)).normalized;       // down the grip
            const float GripWrap = 0.030f;                                        // grip half-width plus the finger bones' depth
            var right = new Hand
            {
                knuckle = m_GripR.position + GripWrap * (0.87f * gx + 0.5f * front),   // 30 degrees round from the right side
                along = (0.87f * front - 0.5f * gx).normalized,
                across = down,
                curl = GripR,
                // Up the left side of the frame; on a pistol forwards along it, resting on the support thumb.
                thumbAim = m_GripR.position + (pistol ? -gx * 0.023f + gy * 0.050f : -gx * 0.03f + gy * 0.045f + gz * 0.03f),
                thumbLine = pistol ? gz : Vector3.zero,
                thumbWeight = 1f,
                thumbMaxTurn = pistol ? 100f : 70f,
            };
            float bolt = 0f;
            if (action == WeaponAction.Cycling && m_Class == WeaponClass.Sniper && m_Rig != null && m_Rig.Bolt != null)
            {
                bolt = Ramp(p, 0f, 0.1f) * (1f - Ramp(p, 0.84f, 0.98f));
                Vector3 knob = m_Rig.Bolt.TransformPoint(m_BoltHandle);
                var pinch = new Hand
                {
                    knuckle = knob + gx * 0.02f - gz * 0.015f + gy * 0.01f,
                    along = (gz * 0.55f - gx * 0.65f + gy * 0.1f).normalized,
                    across = -gy,
                    curl = Pinch,
                };
                right = Mix(right, pinch, bolt, m_CurlR);
            }
            Vector3 rPole = m_R.arm.upper.position + camRight * 0.6f - camUp * 0.8f - camFwd * 0.1f;
            RightHandError = Apply(m_R, right, rPole);
            TriggerFingerError = 0f;
            if (m_HasTrigger && bolt < 0.5f) TriggerFingerError = FitFinger(m_R.fingers[0], m_Rig.Trigger.TransformPoint(m_TriggerFace), trigger ? 1f : 0.8f);

            // ---- support hand: under the handguard (riding the pump), or wrapped over the right hand on a pistol
            Hand left;
            if (pistol)
            {
                Vector3 gl = m_GripL != null ? m_GripL.position : m_GripR.position - gy * 0.015f;
                const float Over = 0.042f;   // wraps over the right hand's fingers
                left = new Hand
                {
                    knuckle = gl + Over * (-0.87f * gx + 0.5f * front),
                    along = (0.87f * front + 0.5f * gx).normalized,
                    across = down,
                    curl = Wrap,
                    thumbAim = gl - gx * 0.023f + gy * 0.05f,   // forwards along the frame, below the slide
                    thumbLine = gz,
                    thumbWeight = 1f,
                    thumbMaxTurn = 100f,
                };
            }
            else if (m_Class == WeaponClass.SMG)
            {
                // The magazine held like a vertical grip: palm on its left side, fingers round the front onto the right
                // side, index finger under the magazine well, thumb forwards along the left of the well.
                Vector3 gl = m_GripL != null ? m_GripL.position : m_GripR.position + gz * 0.1f;
                left = new Hand
                {
                    knuckle = gl - gx * (m_GuardRadius + 0.011f) + gz * (MagHalfDepth + 0.006f),
                    along = (0.82f * gz + 0.57f * gx).normalized,
                    across = -gy,
                    curl = MagGrip,
                    thumbAim = gl - gx * (m_GuardRadius + 0.009f) + gy * 0.042f,
                    thumbLine = gz,
                    thumbWeight = 1f,
                    thumbMaxTurn = 90f,
                };
            }
            else
            {
                Vector3 gl = m_GripL != null ? m_GripL.position : m_GripR.position + gz * 0.25f;
                if (m_Rig != null) gl += m_Rig.PumpShift;
                Vector3 centre = gl + gy * m_GuardCentre;
                float wrap = m_GuardRadius + 0.013f;
                // Palm under the handguard, knuckles 15 degrees round from the bottom on the right: the fingers wrap up
                // the right side and end near its top; the thumb lies along the left side.
                left = new Hand
                {
                    knuckle = centre + wrap * (0.26f * gx - 0.966f * gy),
                    along = (0.966f * gx + 0.26f * gy + 0.25f * gz).normalized,
                    across = -gz,
                    curl = Handguard,
                    thumbAim = centre - gx * (m_GuardRadius + 0.008f) - gy * 0.006f,
                    thumbLine = gz,
                    thumbWeight = 1f,
                    thumbMaxTurn = 90f,
                };
                // Keep the support hand on the weapon when the arm cannot reach: slide it back along the gun.
                float reach = m_L.arm.Reach * 0.985f;
                for (int i = 0; i < 8 && Vector3.Distance(m_L.arm.upper.position, left.knuckle - left.along * m_L.arm.palm) > reach; i++)
                {
                    left.knuckle -= gz * 0.03f;
                    left.thumbAim -= gz * 0.03f;
                }
            }

            bool reloading = action == WeaponAction.Reloading || action == WeaponAction.ShellReloading;
            Vector3 rest = left.knuckle;
            if (reloading)
            {
                Vector3 palmUp = (gz * 0.9f + gy * 0.4f).normalized;
                Vector3 pouch = parent.TransformPoint(new Vector3(-0.14f, -0.5f, 0.12f)) + palmUp * m_L.arm.palm;
                Vector3 well = (m_MagWell != null ? m_MagWell.position : rest) - gy * 0.04f + palmUp * 0.02f;
                Vector3 target;
                if (action == WeaponAction.ShellReloading)
                {
                    // Lead-in: off the pump and down to the shells. Each insert: up to the loading port, thumb the shell
                    // into the tube, back down for the next (the shell counts when the cycle ends).
                    Vector3 port = well + gy * 0.02f, push = port + gz * 0.035f + gy * 0.012f;
                    if (!shellInsert) target = Vector3.Lerp(rest, pouch, Smooth(p / 0.8f));
                    else if (p < 0.40f) target = Vector3.Lerp(pouch, port, Smooth(p / 0.40f));
                    else if (p < 0.62f) target = Vector3.Lerp(port, push, Smooth((p - 0.40f) / 0.22f));
                    else target = Vector3.Lerp(push, pouch, Smooth((p - 0.62f) / 0.38f));
                    m_ReloadK = shellInsert ? 1f : Ramp(p, 0f, 0.3f);
                    m_ReloadRelaxed = 0f;
                }
                else
                {
                    Vector3 mag = m_Rig != null && m_Rig.Mag != null ? m_Rig.Mag.TransformPoint(m_MagBottom) - gy * 0.02f + palmUp * 0.03f : well;
                    if (p < 0.10f) target = Vector3.Lerp(rest, mag, Smooth(p / 0.10f));
                    else if (p < 0.14f) target = mag;                                              // mag release
                    else if (p < 0.32f) target = Vector3.Lerp(well, pouch, Smooth((p - 0.14f) / 0.18f));   // the old one falls
                    else if (p < 0.42f) target = pouch;
                    else if (p < 0.50f) target = Vector3.Lerp(pouch, mag, Smooth((p - 0.42f) / 0.08f));    // the new one
                    else if (p < 0.76f) target = mag;                                              // insert and seat
                    else target = Vector3.Lerp(mag, rest, Smooth((p - 0.76f) / 0.14f));
                    m_ReloadK = Ramp(p, 0.02f, 0.10f) * (1f - Ramp(p, 0.76f, 0.88f));
                    m_ReloadRelaxed = Ramp(p, 0.16f, 0.24f) * (1f - Ramp(p, 0.38f, 0.46f));
                }
                // Eased in the viewmodel's (camera) space, so the hand keeps up when the player runs or turns;
                // the new magazine appears below the gun in one step and the hand glides over it.
                Vector3 local = parent.InverseTransformPoint(target);
                if (!m_HadReload && m_ReloadWeight <= 0f) m_LKnuckleLocal = parent.InverseTransformPoint(rest);
                m_LKnuckleLocal = Vector3.Lerp(m_LKnuckleLocal, local, 1f - Mathf.Exp(-28f * dt));
            }
            // Entering and leaving the reload (finished, cancelled or interrupted) blends over a few frames
            // instead of snapping the hand back to the handguard.
            m_ReloadWeight = Mathf.MoveTowards(m_ReloadWeight, reloading ? 1f : 0f, dt / (reloading ? 0.1f : 0.2f));
            if (m_ReloadWeight > 0f)
            {
                float w = Smooth(m_ReloadWeight);
                Vector3 palmUp = (gz * 0.9f + gy * 0.4f).normalized;
                var hold = new Hand { along = palmUp, across = -gx, curl = HoldMag };
                left = Mix(left, hold, m_ReloadK * w, m_CurlL);
                left.knuckle = Vector3.Lerp(rest, parent.TransformPoint(m_LKnuckleLocal), w);
                if (m_ReloadRelaxed > 0f) Blend(m_CurlL, m_CurlL, Relaxed, m_ReloadRelaxed * w);
            }
            m_HadReload = reloading;
            Vector3 lPole = m_L.arm.upper.position - camRight * 0.5f - camUp * 0.8f;
            LeftHandError = Apply(m_L, left, lPole);
        }

        /// <summary>Wrist from the knuckle and the hand's length, IK, then the fingers.</summary>
        static float Apply(in Side s, in Hand h, Vector3 pole)
        {
            Vector3 along = h.along.normalized;
            Vector3 wrist = h.knuckle - along * s.arm.palm;
            ArmChain.Solve(s.arm, wrist, pole, along, h.across);
            float err = Vector3.Distance(s.arm.hand.position, wrist);
            Curl(s, h.curl);
            if (h.thumbWeight > 0f)
            {
                var thumb = s.fingers[4];
                Vector3 target = h.thumbLine.sqrMagnitude > 1e-6f ? OnLine(thumb, h.thumbAim, h.thumbLine) : h.thumbAim;
                Aim(thumb, target, h.thumbWeight, h.thumbMaxTurn > 0f ? h.thumbMaxTurn : 70f);
            }
            return err;
        }

        static Hand Mix(in Hand a, in Hand b, float t, float[] curlBuffer)
        {
            Blend(curlBuffer, a.curl, b.curl, t);
            return new Hand
            {
                knuckle = Vector3.Lerp(a.knuckle, b.knuckle, t),
                along = Vector3.Slerp(a.along, b.along, t),
                across = Vector3.Slerp(a.across, b.across, t),
                curl = curlBuffer,
                thumbAim = a.thumbAim,
                thumbLine = a.thumbLine,
                thumbWeight = a.thumbWeight * (1f - t),
                thumbMaxTurn = a.thumbMaxTurn,
            };
        }

        /// <summary>The point of a line (a surface the finger should lie along) at the finger's length from its root,
        /// forwards along the line; the closest point when the line is out of reach.</summary>
        static Vector3 OnLine(in Finger f, Vector3 point, Vector3 dir)
        {
            if (f.bone[0] == null || f.tip == null) return point;
            Vector3 root = f.bone[0].position, d = dir.normalized, rel = point - root;
            float len = Vector3.Distance(f.tip.position, root);
            float along = Vector3.Dot(rel, d);
            float perp2 = (rel - d * along).sqrMagnitude;
            float t = -along + Mathf.Sqrt(Mathf.Max(0f, len * len - perp2));
            return point + d * t;
        }

        /// <summary>Turns a finger's first joint so its tip points at <paramref name="target"/> (by at most <paramref name="maxAngle"/>).</summary>
        static void Aim(in Finger f, Vector3 target, float weight, float maxAngle)
        {
            var root = f.bone[0];
            if (root == null || f.tip == null) return;
            Quaternion q = Quaternion.FromToRotation(f.tip.position - root.position, target - root.position);
            q = Quaternion.RotateTowards(Quaternion.identity, q, maxAngle * weight);
            root.rotation = q * root.rotation;
        }

        /// <summary>Bends a finger until its tip is as far from its root as the target, then points it there:
        /// the trigger finger lands on the blade wherever the hand and the blade are.</summary>
        static float FitFinger(in Finger f, Vector3 target, float bendBias)
        {
            if (f.bone[0] == null || f.bone[2] == null || f.tip == null) return 0f;
            float want = Vector3.Distance(f.bone[0].position, target);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++)
            {
                float s = (lo + hi) * 0.5f;
                SetFinger(f, 20f * s * bendBias, 95f * s, 70f * s);
                float d = Vector3.Distance(f.bone[0].position, f.tip.position);
                if (d > want) lo = s; else hi = s;   // more bend, shorter reach
            }
            SetFinger(f, 20f * hi * bendBias, 95f * hi, 70f * hi);
            Aim(f, target, 1f, 60f);
            return Vector3.Distance(f.tip.position, target);
        }

        static void SetFinger(in Finger f, float a, float b, float c)
        {
            f.bone[0].localRotation = f.bind[0] * Quaternion.AngleAxis(a, f.axis[0]);
            f.bone[1].localRotation = f.bind[1] * Quaternion.AngleAxis(b, f.axis[1]);
            f.bone[2].localRotation = f.bind[2] * Quaternion.AngleAxis(c, f.axis[2]);
        }

        void PlaceShoulders()
        {
            // Turn and move the whole model so its shoulders sit near the anchors, then shift each clavicle the rest of
            // the way (the torso is not drawn; the shift only stretches skin that is out of view).
            Vector3 bindAcross = m_R.bindJoint - m_L.bindJoint, anchorAcross = m_AnchorR - m_AnchorL;
            bindAcross.y = 0f;
            anchorAcross.y = 0f;
            Quaternion q = bindAcross.sqrMagnitude > 1e-6f && anchorAcross.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(bindAcross, anchorAcross) : Quaternion.identity;
            Vector3 mid = (m_AnchorR + m_AnchorL) * 0.5f, bindMid = (m_R.bindJoint + m_L.bindJoint) * 0.5f;
            // Rest-space points P move to mid + q (P - bindMid).
            model.localRotation = q * m_ModelRot;
            model.localPosition = mid + q * (m_ModelPos - bindMid);
            m_R.clavicle.position += transform.TransformPoint(m_AnchorR) - m_R.arm.upper.position;
            m_L.clavicle.position += transform.TransformPoint(m_AnchorL) - m_L.arm.upper.position;
        }

        static void Reset(ref Side s)
        {
            s.clavicle.localPosition = s.clavicleBindPos;
            s.clavicle.localRotation = s.clavicleBind;
            s.arm.upper.localRotation = s.upperBind;
            s.arm.fore.localRotation = s.foreBind;
            s.arm.hand.localRotation = s.handBind;
        }

        static void Blend(float[] into, float[] a, float[] b, float t)
        {
            for (int i = 0; i < into.Length; i++) into[i] = Mathf.Lerp(a[i], b[i], t);
        }

        static void Curl(in Side s, float[] curl)
        {
            for (int f = 0; f < s.fingers.Length; f++)
            {
                var fg = s.fingers[f];
                for (int k = 0; k < 3; k++)
                {
                    if (fg.bone[k] == null) break;
                    fg.bone[k].localRotation = fg.bind[k] * Quaternion.AngleAxis(curl[f * 3 + k], fg.axis[k]);
                }
            }
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float Ramp(float x, float a, float b) => Smooth((x - a) / (b - a));
    }
}
