using Saltline.Weapons;
using UnityEngine;

namespace Saltline.FX
{
    /// <summary>
    /// Moves the separate parts of an imported weapon model: bolt or slide blowback on each shot
    /// (locked open on an empty magazine), trigger pull, magazine out and in during a reload,
    /// charging handle on an empty reload, pump and bolt-handle cycling. Purely cosmetic; driven by
    /// the simulated weapon state, so it follows prediction like the rest of the viewmodel.
    /// Offsets are authored in the model's own frame: +z along the bore, +y up, +x right.
    /// </summary>
    public sealed class WeaponRig : MonoBehaviour
    {
        struct Part
        {
            public Transform t;
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 back, down, fwd;          // unit offsets (1 m) in the part's parent space
            public Vector3 axisRight, axisFwd;       // rotation axes in the part's parent space
            public Renderer[] renderers;
            public bool Valid => t != null;
        }

        [Tooltip("Bolt or slide travel on each shot (m).")] public float boltTravel = 0.07f;
        [Tooltip("Charging handle travel on an empty reload (m).")] public float chargeTravel = 0.07f;
        [Tooltip("Pump travel when cycling (m).")] public float pumpTravel = 0.09f;
        [Tooltip("Bolt handle lift for a bolt action (degrees).")] public float boltLift = 62f;
        public float triggerPull = 14f;
        public float magDrop = 0.32f;
        [Tooltip("Forward share of the magazine's drop direction: positive for mags angled forward, negative for a raked pistol grip.")]
        public float magDropForward = 0.2f;

        Part m_Mag, m_Bolt, m_Charge, m_Trigger, m_Pump, m_Slide;
        float m_SinceShot = 10f;
        float m_Cycle = 0.1f;

        public void Init(Transform root, Transform parts)
        {
            foreach (Transform c in parts)
            {
                string n = c.name;
                if (n.EndsWith("_Mag")) m_Mag = Make(c, root);
                else if (n.EndsWith("_Bolt")) m_Bolt = Make(c, root);
                else if (n.EndsWith("_Charge")) m_Charge = Make(c, root);
                else if (n.EndsWith("_Trigger")) m_Trigger = Make(c, root);
                else if (n.EndsWith("_Pump")) m_Pump = Make(c, root);
                else if (n.EndsWith("_Slide")) m_Slide = Make(c, root);
            }
        }

        /// <summary>Per-class travel distances (what the eye expects from each action type).</summary>
        public void Configure(WeaponClass cls)
        {
            switch (cls)
            {
                case WeaponClass.AssaultRifle: boltTravel = 0.075f; chargeTravel = 0.07f; magDropForward = 0.2f; break;
                case WeaponClass.SMG: boltTravel = 0.05f; chargeTravel = 0.05f; magDropForward = 0f; break;
                case WeaponClass.Shotgun: boltTravel = 0.0f; pumpTravel = 0.09f; break;
                case WeaponClass.Sniper: boltTravel = 0.0f; magDropForward = 0f; break;
                case WeaponClass.Pistol: boltTravel = 0.032f; magDrop = 0.24f; magDropForward = -0.32f; break; // grip rakes back ~18 degrees
            }
        }

        static Part Make(Transform t, Transform root)
        {
            var parent = t.parent;
            return new Part
            {
                t = t,
                pos = t.localPosition,
                rot = t.localRotation,
                back = parent.InverseTransformVector(root.TransformVector(Vector3.back)),
                down = parent.InverseTransformVector(root.TransformVector(Vector3.down)),
                fwd = parent.InverseTransformVector(root.TransformVector(Vector3.forward)),
                axisRight = parent.InverseTransformDirection(root.TransformDirection(Vector3.right)).normalized,
                axisFwd = parent.InverseTransformDirection(root.TransformDirection(Vector3.forward)).normalized,
                renderers = t.GetComponentsInChildren<Renderer>(true),
            };
        }

        /// <summary>A round was fired; the action cycles over (most of) one fire interval.</summary>
        public void OnShot(float fireInterval)
        {
            m_SinceShot = 0f;
            m_Cycle = Mathf.Clamp(fireInterval * 0.85f, 0.045f, 0.12f);
        }

        /// <param name="progress">0..1 through the current action.</param>
        /// <param name="emptyReload">The current reload started with an empty magazine.</param>
        /// <param name="magEmpty">No round in the magazine (semi-autos and rifles lock the bolt open).</param>
        public void Tick(WeaponAction action, float progress, bool emptyReload, bool magEmpty, bool triggerHeld, float dt)
        {
            m_SinceShot += dt;
            float p = progress;
            bool reloading = action == WeaponAction.Reloading;

            // ---- bolt / slide: blowback, held open on empty, released at the end of an empty reload
            float blow = m_SinceShot < m_Cycle ? Blowback(m_SinceShot / m_Cycle) : 0f;
            float lockOpen = magEmpty && action != WeaponAction.Equipping ? 1f : 0f;
            if (reloading && emptyReload) lockOpen = p < 0.86f ? 1f : 1f - Smooth((p - 0.86f) / 0.04f);
            float open = Mathf.Max(blow, lockOpen);

            // ---- charging handle: pulled to the rear and let go during an empty reload
            float charge = 0f;
            if (reloading && emptyReload)
            {
                if (p > 0.78f && p < 0.84f) charge = Smooth((p - 0.78f) / 0.06f);
                else if (p >= 0.84f && p < 0.87f) charge = 1f - Smooth((p - 0.84f) / 0.03f);
                if (charge > 0f) open = Mathf.Max(open, charge * 1.05f);
            }

            // ---- bolt action (rifle) and pump (shotgun): one cycle per Cycling action
            float lift = 0f, boltBack = 0f, pump = 0f;
            if (action == WeaponAction.Cycling)
            {
                lift = Ramp(p, 0f, 0.18f) * (1f - Ramp(p, 0.72f, 0.88f));
                boltBack = Ramp(p, 0.18f, 0.42f) * (1f - Ramp(p, 0.48f, 0.72f));
                pump = Ramp(p, 0.05f, 0.42f) * (1f - Ramp(p, 0.5f, 0.88f));
            }

            if (m_Bolt.Valid)
            {
                float travel = boltTravel > 0f ? open * boltTravel : boltBack * 0.09f;
                if (pumpTravel > 0f && m_Pump.Valid) travel = Mathf.Max(travel, pump * pumpTravel); // bolt rides the pump
                m_Bolt.t.localPosition = m_Bolt.pos + m_Bolt.back * travel;
                m_Bolt.t.localRotation = Quaternion.AngleAxis(lift * boltLift, m_Bolt.axisFwd) * m_Bolt.rot;
            }
            if (m_Slide.Valid) m_Slide.t.localPosition = m_Slide.pos + m_Slide.back * (open * boltTravel);
            if (m_Charge.Valid) m_Charge.t.localPosition = m_Charge.pos + m_Charge.back * (charge * chargeTravel);
            if (m_Pump.Valid) m_Pump.t.localPosition = m_Pump.pos + m_Pump.back * (pump * pumpTravel);

            if (m_Trigger.Valid)
            {
                float pull = triggerHeld || m_SinceShot < 0.06f ? 1f : 0f;
                m_Trigger.t.localRotation = Quaternion.AngleAxis(pull * triggerPull, m_Trigger.axisRight) * m_Trigger.rot;
            }

            // ---- magazine: out, away, new one in, seated (ammo is committed at the weapon's commit point)
            if (m_Mag.Valid)
            {
                float d = 0f, tilt = 0f;
                bool hidden = false;
                if (reloading)
                {
                    if (p < 0.12f) d = 0f;
                    else if (p < 0.30f) { float k = EaseIn((p - 0.12f) / 0.18f); d = k; tilt = k * 18f; }
                    else if (p < 0.48f) { d = 1f; hidden = true; }
                    else if (p < 0.70f) { float k = 1f - EaseOut((p - 0.48f) / 0.22f); d = 0.08f + 0.72f * k; tilt = k * 12f; }
                    else if (p < 0.76f) d = 0.08f * (1f - Smooth((p - 0.70f) / 0.06f));
                }
                m_Mag.t.localPosition = m_Mag.pos + (m_Mag.down + m_Mag.fwd * magDropForward) * (d * magDrop);
                m_Mag.t.localRotation = Quaternion.AngleAxis(-tilt, m_Mag.axisRight) * m_Mag.rot;
                foreach (var r in m_Mag.renderers) if (r != null) r.enabled = !hidden;
            }
        }

        static float Blowback(float t) => t < 0.35f ? Smooth(t / 0.35f) : 1f - Smooth((t - 0.35f) / 0.65f);
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t; }
        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        static float Ramp(float x, float a, float b) => Smooth((x - a) / (b - a));
    }
}
