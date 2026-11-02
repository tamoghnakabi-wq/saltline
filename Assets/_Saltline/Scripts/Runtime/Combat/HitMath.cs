using UnityEngine;

namespace Saltline.Combat
{
    /// <summary>Exact ray tests against analytic shapes. Directions must be normalized.</summary>
    public static class HitMath
    {
        /// <summary>Ray vs sphere. Returns the entry distance; 0 when the origin is inside.</summary>
        public static bool RaySphere(Vector3 o, Vector3 d, Vector3 c, float r, out float t)
        {
            Vector3 oc = o - c;
            float b = Vector3.Dot(oc, d);
            float cc = Vector3.Dot(oc, oc) - r * r;
            t = 0f;
            if (cc <= 0f) return true;       // inside
            if (b > 0f) return false;        // in front of us and pointing away
            float h = b * b - cc;
            if (h < 0f) return false;
            t = -b - Mathf.Sqrt(h);
            return true;
        }

        /// <summary>
        /// Ray vs capsule (segment a-b, radius r). The capsule is the union of a cylinder and two
        /// spheres, so the entry point is the nearest entry into any of the three parts.
        /// </summary>
        public static bool RayCapsule(Vector3 o, Vector3 d, Vector3 a, Vector3 b, float r, out float t)
        {
            Vector3 ba = b - a;
            float baba = Vector3.Dot(ba, ba);
            if (baba < 1e-10f) return RaySphere(o, d, a, r, out t);

            Vector3 oa = o - a;
            float bard = Vector3.Dot(ba, d);
            float baoa = Vector3.Dot(ba, oa);
            float rdoa = Vector3.Dot(d, oa);
            float oaoa = Vector3.Dot(oa, oa);
            float k2 = baba - bard * bard;
            float k1 = baba * rdoa - baoa * bard;
            float k0 = baba * oaoa - baoa * baoa - r * r * baba;

            // Origin inside the cylinder part.
            if (k0 <= 0f && baoa >= 0f && baoa <= baba) { t = 0f; return true; }

            float best = float.MaxValue;
            bool hit = false;
            if (k2 > 1e-10f)
            {
                float h = k1 * k1 - k2 * k0;
                if (h >= 0f)
                {
                    float tt = (-k1 - Mathf.Sqrt(h)) / k2;
                    float y = baoa + tt * bard;
                    if (tt >= 0f && y >= 0f && y <= baba) { best = tt; hit = true; }
                }
            }
            if (RaySphere(o, d, a, r, out float ta) && ta < best) { best = ta; hit = true; }
            if (RaySphere(o, d, b, r, out float tb) && tb < best) { best = tb; hit = true; }
            t = hit ? best : 0f;
            return hit;
        }

        /// <summary>Closest distance between a point and a segment (used by debug checks and tests).</summary>
        public static float PointSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float denom = Vector3.Dot(ab, ab);
            float t = denom > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / denom) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
