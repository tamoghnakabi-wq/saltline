using UnityEngine;

namespace Saltline.FX
{
    /// <summary>
    /// Analytic two-bone IK (shoulder-elbow-wrist or hip-knee-ankle). The middle joint bends in the
    /// plane of the root, the target and the pole; out-of-reach targets straighten the chain towards them.
    /// Bone lengths are taken from the current pose, so it works on any skeleton and scale.
    /// </summary>
    public static class TwoBoneIK
    {
        public static void Solve(Transform upper, Transform mid, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 a = upper.position, b = mid.position, c = end.position;
            float la = Vector3.Distance(a, b), lb = Vector3.Distance(b, c);
            if (la < 1e-5f || lb < 1e-5f) return;
            Vector3 at = target - a;
            float d = Mathf.Clamp(at.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.9999f);
            Vector3 dir = at.sqrMagnitude > 1e-10f ? at.normalized : (c - a).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole - a, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(b - a, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            Vector3 elbow = a + dir * (la * cosA) + bend * (la * Mathf.Sqrt(1f - cosA * cosA));
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            b = mid.position;
            c = end.position;
            mid.rotation = Quaternion.FromToRotation(c - b, a + dir * d - b) * mid.rotation;
        }
    }
}
