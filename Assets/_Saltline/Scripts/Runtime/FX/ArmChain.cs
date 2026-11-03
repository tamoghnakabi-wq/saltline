using UnityEngine;

namespace Saltline.FX
{
    /// <summary>
    /// Upper arm, forearm and hand, plus the hand's own axes measured from its finger roots, so a
    /// hand can be posed by "fingers point this way, knuckles run that way" on any skeleton.
    /// Shared by the third-person operator (OperatorRig) and the first-person arms (FirstPersonArms).
    /// </summary>
    public struct ArmChain
    {
        public Transform upper, fore, hand;
        public Vector3 longLocal, acrossLocal;   // wrist -> middle knuckle, index -> little knuckle (hand space)
        public float palm;                       // wrist to middle knuckle (m)
        public bool oriented;
        public bool Valid => upper != null && fore != null && hand != null;
        public float Reach => Valid ? Vector3.Distance(upper.position, fore.position) + Vector3.Distance(fore.position, hand.position) : 0f;

        /// <param name="middle">Middle finger root; with index and little it gives the hand's axes. Any may be null (no orientation).</param>
        public static ArmChain Create(Transform upper, Transform fore, Transform hand, Transform middle, Transform index, Transform little)
        {
            var arm = new ArmChain { upper = upper, fore = fore, hand = hand, palm = 0.09f };
            if (!arm.Valid || middle == null || index == null || little == null) return arm;
            // Finger roots are children of the hand, so these are the same in every pose.
            Vector3 l = hand.InverseTransformDirection(middle.position - hand.position);
            Vector3 c = hand.InverseTransformDirection(little.position - index.position);
            arm.palm = Vector3.Distance(middle.position, hand.position);
            arm.longLocal = l.normalized;
            arm.acrossLocal = Vector3.ProjectOnPlane(c, arm.longLocal).normalized;
            arm.oriented = arm.acrossLocal.sqrMagnitude > 0.5f;
            return arm;
        }

        /// <summary>Analytic two-bone IK: elbow in the plane of shoulder, target and pole; then the hand orientation,
        /// with half of its twist taken by the forearm so the wrist does not candy-wrap.
        /// A zero <paramref name="handLong"/> leaves the hand's orientation alone.</summary>
        public static void Solve(in ArmChain arm, Vector3 target, Vector3 pole, Vector3 handLong, Vector3 handAcross)
        {
            TwoBoneIK.Solve(arm.upper, arm.fore, arm.hand, target, pole);
            if (!arm.oriented || handLong.sqrMagnitude < 1e-6f) return;

            Quaternion local = Quaternion.LookRotation(arm.longLocal, arm.acrossLocal);
            Quaternion want = Quaternion.LookRotation(handLong, Vector3.ProjectOnPlane(handAcross, handLong)) * Quaternion.Inverse(local);
            Vector3 axis = (arm.hand.position - arm.fore.position).normalized;
            Quaternion delta = want * Quaternion.Inverse(arm.hand.rotation);
            Vector3 proj = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            var twist = new Quaternion(proj.x, proj.y, proj.z, delta.w);
            float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (mag > 1e-5f)
            {
                twist = new Quaternion(twist.x / mag, twist.y / mag, twist.z / mag, twist.w / mag);
                twist.ToAngleAxis(out float angle, out Vector3 ax);
                if (angle > 180f) angle -= 360f;
                if (float.IsFinite(angle) && ax.sqrMagnitude > 0.5f)
                    arm.fore.rotation = Quaternion.AngleAxis(angle * 0.5f, ax) * arm.fore.rotation;
            }
            arm.hand.rotation = want;
        }
    }
}
