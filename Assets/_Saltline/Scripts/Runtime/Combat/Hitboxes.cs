using Saltline.Sim;
using UnityEngine;

namespace Saltline.Combat
{
    public enum HitRegion : byte
    {
        None = 0,
        Head = 1,
        UpperTorso = 2,
        LowerTorso = 3,
        Arm = 4,
        Leg = 5,
    }

    /// <summary>The minimum state needed to rebuild a player's hitboxes at any past tick.</summary>
    public struct HitboxPose
    {
        public Vector3 Feet;
        public Vector3 Velocity;  // used for slide leg direction
        public float Yaw;         // degrees
        public float Pitch;       // degrees, + = looking up
        public float Height;      // capsule height
        public Stance Stance;

        public static HitboxPose From(in MotorState s, float yaw, float pitch) => new HitboxPose
        {
            Feet = s.Position, Velocity = s.Velocity, Yaw = yaw, Pitch = pitch, Height = s.Height, Stance = s.Stance,
        };

        public static HitboxPose Lerp(in HitboxPose a, in HitboxPose b, float t) => new HitboxPose
        {
            Feet = Vector3.Lerp(a.Feet, b.Feet, t),
            Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
            Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t),
            Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t),
            Height = Mathf.Lerp(a.Height, b.Height, t),
            Stance = t < 0.5f ? a.Stance : b.Stance,
        };
    }

    public struct HitCapsule
    {
        public Vector3 A, B;
        public float Radius;
        public HitRegion Region;
    }

    /// <summary>
    /// Analytic hitboxes derived from movement state, so the host can rebuild them for any
    /// rewound tick without animating a skeleton. The character art is authored to these
    /// proportions (standing: 1.8 m tall, eyes at 1.62 m).
    /// </summary>
    public static class HitboxModel
    {
        public const int Count = 7;

        public static void Build(in HitboxPose p, MotorSettings m, HitCapsule[] caps)
        {
            float eyeT = Mathf.InverseLerp(m.slideHeight, m.standHeight, p.Height);
            float eye = Mathf.Lerp(m.slideEye, m.standEye, eyeT);
            float drop = m.standEye - eye; // how far the upper body has lowered

            Quaternion yaw = Quaternion.Euler(0f, p.Yaw, 0f);
            Vector3 fwd = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;
            Vector3 up = Vector3.up;
            Vector3 f = p.Feet;

            // Head
            caps[0] = new HitCapsule
            {
                A = f + up * (eye + 0.02f) + fwd * 0.03f,
                B = f + up * (eye + 0.06f) + fwd * 0.02f,
                Radius = 0.125f,
                Region = HitRegion.Head,
            };
            // Chest
            caps[1] = new HitCapsule
            {
                A = f + up * (1.18f - drop),
                B = f + up * (1.34f - drop),
                Radius = 0.19f,
                Region = HitRegion.UpperTorso,
            };
            // Abdomen / pelvis
            caps[2] = new HitCapsule
            {
                A = f + up * Mathf.Max(0.35f, 0.88f - drop),
                B = f + up * Mathf.Max(0.42f, 0.98f - drop),
                Radius = 0.18f,
                Region = HitRegion.LowerTorso,
            };

            // Arms hold the weapon in front of the chest and follow pitch around the shoulders.
            Quaternion aim = yaw * Quaternion.Euler(-Mathf.Clamp(p.Pitch, -80f, 80f), 0f, 0f);
            Vector3 shoulderR = f + up * (1.40f - drop) + right * 0.19f;
            Vector3 shoulderL = f + up * (1.40f - drop) - right * 0.19f;
            caps[3] = new HitCapsule { A = shoulderR, B = shoulderR + aim * new Vector3(-0.12f, -0.12f, 0.42f), Radius = 0.065f, Region = HitRegion.Arm };
            caps[4] = new HitCapsule { A = shoulderL, B = shoulderL + aim * new Vector3(0.16f, -0.10f, 0.52f), Radius = 0.065f, Region = HitRegion.Arm };

            // Legs
            if (p.Stance == Stance.Sliding)
            {
                Vector3 h = new Vector3(p.Velocity.x, 0f, p.Velocity.z);
                Vector3 legDir = h.sqrMagnitude > 0.25f ? h.normalized : fwd;
                Vector3 hip = f + up * 0.32f;
                caps[5] = new HitCapsule { A = hip - right * 0.1f, B = hip + legDir * 0.85f + Vector3.down * 0.18f - right * 0.1f, Radius = 0.1f, Region = HitRegion.Leg };
                caps[6] = new HitCapsule { A = hip + right * 0.1f, B = hip + legDir * 0.75f + Vector3.down * 0.18f + right * 0.12f, Radius = 0.1f, Region = HitRegion.Leg };
            }
            else
            {
                float hipY = Mathf.Max(0.3f, 0.80f - drop * 0.85f);
                Vector3 knee = fwd * (drop * 0.35f); // knees push forward when crouched
                caps[5] = new HitCapsule { A = f + up * 0.1f - right * 0.11f, B = f + up * hipY - right * 0.11f + knee, Radius = 0.1f, Region = HitRegion.Leg };
                caps[6] = new HitCapsule { A = f + up * 0.1f + right * 0.11f, B = f + up * hipY + right * 0.11f + knee, Radius = 0.1f, Region = HitRegion.Leg };
            }
        }

        /// <summary>Nearest hitbox hit along a ray, or false if the ray misses the player.</summary>
        public static bool Raycast(Vector3 origin, Vector3 dir, HitCapsule[] caps, float maxDistance, out float distance, out HitRegion region)
        {
            distance = float.MaxValue;
            region = HitRegion.None;
            for (int i = 0; i < Count; i++)
            {
                if (HitMath.RayCapsule(origin, dir, caps[i].A, caps[i].B, caps[i].Radius, out float t) && t < distance && t <= maxDistance)
                {
                    distance = t;
                    region = caps[i].Region;
                }
            }
            return region != HitRegion.None;
        }

        /// <summary>Cheap bounding test before the per-capsule work.</summary>
        public static bool RayNearPlayer(Vector3 origin, Vector3 dir, in HitboxPose p, float maxDistance)
        {
            Vector3 a = p.Feet;
            Vector3 b = p.Feet + Vector3.up * 1.9f;
            return HitMath.RayCapsule(origin, dir, a, b, 1.2f, out float t) && t <= maxDistance;
        }
    }
}
