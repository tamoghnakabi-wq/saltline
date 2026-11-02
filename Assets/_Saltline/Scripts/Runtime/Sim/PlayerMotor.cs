using UnityEngine;

namespace Saltline.Sim
{
    /// <summary>
    /// Deterministic-per-input movement step built on CharacterController collision.
    /// The same function runs for client prediction, reconciliation replays and the host's
    /// authoritative simulation. It only queries static world geometry (players do not block
    /// each other's movement), so client and host see the same collisions.
    /// </summary>
    public static class PlayerMotor
    {
        public struct StepParams
        {
            public float WeaponMoveMultiplier; // from the equipped weapon (1 = no penalty)
            public float AimMoveMultiplier;    // weapon-specific ADS movement multiplier
            public bool CanSprint;             // false while reloading heavy weapons, etc.
            public static StepParams Default => new StepParams { WeaponMoveMultiplier = 1f, AimMoveMultiplier = 1f, CanSprint = true };
        }

        public static int EnvironmentMask = ~0;

        public static void Step(ref MotorState s, in InputCmd cmd, float dt, CharacterController cc,
            MotorSettings m, in StepParams p)
        {
            Place(cc, s.Position, s.Height, m);

            bool wasGrounded = s.Grounded;
            s.Flags &= ~(MotorFlags.JumpedThisTick | MotorFlags.LandedThisTick);

            Quaternion yawRot = Quaternion.Euler(0f, cmd.YawDegrees, 0f);
            Vector2 mv = cmd.Move;
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            Vector3 wishDir = yawRot * new Vector3(mv.x, 0f, mv.y);
            float wishAmount = Mathf.Clamp01(wishDir.magnitude);
            if (wishAmount > 0.001f) wishDir /= wishAmount;

            bool aim = cmd.Has(InputButtons.Aim);
            bool crouchHeld = cmd.Has(InputButtons.Crouch);
            bool wantsSprint = cmd.Has(InputButtons.Sprint) && mv.y > m.sprintForwardThreshold && !aim && p.CanSprint;

            s.SlideCooldown = Mathf.Max(0f, s.SlideCooldown - dt);
            Vector3 hv = new Vector3(s.Velocity.x, 0f, s.Velocity.z);
            float vy = s.Velocity.y;

            // ---- Jump input edge + timers
            bool jumpPressed = cmd.Has(InputButtons.Jump) && !s.JumpHeld;
            s.JumpHeld = cmd.Has(InputButtons.Jump);
            s.JumpBufferTimer = jumpPressed ? m.jumpBuffer : Mathf.Max(0f, s.JumpBufferTimer - dt);
            s.CoyoteTimer = wasGrounded ? 0f : s.CoyoteTimer + dt;

            // ---- Stance transitions
            switch (s.Stance)
            {
                case Stance.Standing:
                    if (crouchHeld)
                    {
                        bool fastEnough = hv.magnitude >= m.slideMinSpeed;
                        if (wasGrounded && fastEnough && s.Sprinting && s.SlideCooldown <= 0f)
                        {
                            s.Stance = Stance.Sliding;
                            s.SlideTimer = 0f;
                            Vector3 dir = hv.sqrMagnitude > 0.01f ? hv.normalized : wishDir;
                            hv = dir * (hv.magnitude + m.slideBoost);
                        }
                        else s.Stance = Stance.Crouching;
                    }
                    break;

                case Stance.Crouching:
                    if ((!crouchHeld || wantsSprint) && CanStand(s.Position, s.Height, m)) s.Stance = Stance.Standing;
                    break;

                case Stance.Sliding:
                    s.SlideTimer += dt;
                    bool end = hv.magnitude < m.slideEndSpeed || s.SlideTimer > m.slideMaxTime || !crouchHeld ||
                               (s.JumpBufferTimer > 0f && wasGrounded);
                    if (end)
                    {
                        s.SlideCooldown = m.slideCooldown;
                        s.Stance = crouchHeld && !(s.JumpBufferTimer > 0f) ? Stance.Crouching
                            : CanStand(s.Position, s.Height, m) ? Stance.Standing : Stance.Crouching;
                    }
                    break;
            }

            bool sprinting = wantsSprint && s.Stance == Stance.Standing && (wasGrounded || s.Sprinting);
            s.SprintRecover = sprinting ? 0f : s.SprintRecover + dt;

            // ---- Ground probe for slope information
            bool groundHit = ProbeGround(s.Position, m, out Vector3 groundNormal, out float groundDist);
            float slopeAngle = groundHit ? Vector3.Angle(groundNormal, Vector3.up) : 0f;

            // ---- Horizontal velocity
            if (s.Stance == Stance.Sliding)
            {
                float speed = Mathf.Max(0f, hv.magnitude - m.slideFriction * dt);
                if (groundHit && slopeAngle > 3f)
                {
                    // Gain speed sliding downhill, lose it faster uphill.
                    Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                    downhill.y = 0f;
                    float along = hv.sqrMagnitude > 0.01f ? Vector3.Dot(hv.normalized, downhill.normalized) : 0f;
                    speed += along * m.gravity * Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * dt;
                }
                Vector3 dir = hv.sqrMagnitude > 0.0001f ? hv.normalized : wishDir;
                if (wishAmount > 0.1f) dir = Vector3.RotateTowards(dir, wishDir, m.slideSteer * dt * 0.35f, 0f);
                hv = dir * Mathf.Max(0f, speed);
            }
            else if (wasGrounded)
            {
                float baseSpeed = s.Stance == Stance.Crouching ? m.crouchSpeed : sprinting ? m.sprintSpeed : m.runSpeed;
                float dirScale = mv.y < -0.1f ? m.backwardMultiplier : Mathf.Abs(mv.x) > Mathf.Abs(mv.y) ? m.strafeMultiplier : 1f;
                float target = baseSpeed * dirScale * wishAmount * p.WeaponMoveMultiplier;
                if (aim) target *= m.aimSpeedMultiplier * p.AimMoveMultiplier;
                Vector3 targetVel = wishDir * target;

                bool accelerating = targetVel.sqrMagnitude > 0.0001f && targetVel.sqrMagnitude >= hv.sqrMagnitude * 0.98f;
                float rate = accelerating ? m.groundAcceleration : m.groundDeceleration;
                if (targetVel.sqrMagnitude > 0.0001f && Vector3.Dot(targetVel, hv) < 0f) rate *= m.reverseBoost;
                hv = Vector3.MoveTowards(hv, targetVel, rate * dt);
            }
            else
            {
                // Air strafing: only add speed along the wish direction up to the air cap.
                float cap = m.maxAirSpeed * p.WeaponMoveMultiplier;
                float current = Vector3.Dot(hv, wishDir);
                float add = cap * wishAmount - current;
                if (add > 0f && wishAmount > 0f)
                    hv += wishDir * Mathf.Min(m.airAcceleration * m.airControl * dt * cap, add);
                float prev = new Vector2(s.Velocity.x, s.Velocity.z).magnitude;
                float limit = Mathf.Max(prev, cap);
                if (hv.magnitude > limit) hv = hv.normalized * limit;
            }

            // ---- Jump
            bool canJump = (wasGrounded || s.CoyoteTimer < m.coyoteTime) && vy <= 0.5f;
            if (s.JumpBufferTimer > 0f && canJump)
            {
                if (s.Stance == Stance.Crouching && CanStand(s.Position, s.Height, m)) s.Stance = Stance.Standing;
                if (s.Stance != Stance.Crouching)
                {
                    if (s.Stance == Stance.Sliding) s.Stance = Stance.Standing;
                    vy = m.jumpVelocity;
                    s.JumpBufferTimer = 0f;
                    s.CoyoteTimer = m.coyoteTime + 1f;
                    s.Flags |= MotorFlags.JumpedThisTick;
                    wasGrounded = false;
                }
            }

            // ---- Gravity
            if (wasGrounded && vy <= 0f) vy = -2f;
            else vy = Mathf.Max(vy - m.gravity * dt, -m.terminalVelocity);

            // ---- Capsule height
            float targetH = s.Stance == Stance.Standing ? m.standHeight : s.Stance == Stance.Crouching ? m.crouchHeight : m.slideHeight;
            float hStep = (m.standHeight - m.slideHeight) * m.heightLerpSpeed * dt;
            s.Height = Mathf.MoveTowards(s.Height, targetH, hStep);
            Place(cc, s.Position, s.Height, m);

            // ---- Move
            Vector3 start = s.Position;
            CollisionFlags cf = cc.Move(new Vector3(hv.x, vy, hv.z) * dt);
            Vector3 pos = cc.transform.position;
            bool jumped = (s.Flags & MotorFlags.JumpedThisTick) != 0;

            if ((cf & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;

            bool grounded = (cf & CollisionFlags.Below) != 0;
            if (!grounded && wasGrounded && !jumped && vy <= 0f)
            {
                // Snap down stairs and ramp crests instead of launching off them.
                if (ProbeGround(pos, m, out var n2, out float d2) && d2 <= m.stepOffset + 0.05f &&
                    Vector3.Angle(n2, Vector3.up) <= m.slopeLimit)
                {
                    cc.Move(Vector3.down * (d2 + 0.01f));
                    pos = cc.transform.position;
                    grounded = true;
                }
            }
            if (grounded && ProbeGround(pos, m, out var n3, out _) && Vector3.Angle(n3, Vector3.up) > m.slopeLimit + 1f)
                grounded = false; // too steep to stand on: slide off

            // Horizontal velocity follows what actually happened (walls absorb it).
            Vector3 actual = (pos - start) / dt;
            Vector3 newHv = new Vector3(actual.x, 0f, actual.z);
            if (newHv.sqrMagnitude < hv.sqrMagnitude) hv = newHv;

            if (grounded)
            {
                if (!wasGrounded && !jumped)
                {
                    s.Flags |= MotorFlags.LandedThisTick;
                    s.LastLandSpeed = -s.Velocity.y;
                }
                if (vy < 0f) vy = 0f;
            }

            s.Position = pos;
            s.Velocity = new Vector3(hv.x, vy, hv.z);
            s.Flags = (s.Flags & (MotorFlags.JumpedThisTick | MotorFlags.LandedThisTick))
                      | (grounded ? MotorFlags.Grounded : 0)
                      | (sprinting ? MotorFlags.Sprinting : 0)
                      | (aim ? MotorFlags.Aiming : 0)
                      | (crouchHeld ? MotorFlags.WantsCrouch : 0);
        }

        public static float EyeHeight(in MotorState s, MotorSettings m)
        {
            // Eye follows the capsule height continuously so stance changes are smooth.
            float t = Mathf.InverseLerp(m.slideHeight, m.standHeight, s.Height);
            return Mathf.Lerp(m.slideEye, m.standEye, t);
        }

        public static Vector3 EyePosition(in MotorState s, MotorSettings m) => s.Position + Vector3.up * EyeHeight(s, m);

        static void Place(CharacterController cc, Vector3 feet, float height, MotorSettings m)
        {
            if (!Mathf.Approximately(cc.height, height))
            {
                cc.height = height;
                cc.center = new Vector3(0f, height * 0.5f + m.skinWidth, 0f);
            }
            if (!Mathf.Approximately(cc.radius, m.radius)) cc.radius = m.radius;
            if ((cc.transform.position - feet).sqrMagnitude > 1e-12f)
            {
                cc.transform.position = feet;
                Physics.SyncTransforms();
            }
        }

        static bool ProbeGround(Vector3 feet, MotorSettings m, out Vector3 normal, out float distance)
        {
            float r = m.radius * 0.9f;
            Vector3 origin = feet + Vector3.up * (r + m.skinWidth + 0.05f);
            if (Physics.SphereCast(origin, r, Vector3.down, out RaycastHit hit, m.stepOffset + 0.25f, EnvironmentMask,
                    QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                distance = Mathf.Max(0f, hit.distance - 0.05f - m.skinWidth);
                return true;
            }
            normal = Vector3.up;
            distance = float.MaxValue;
            return false;
        }

        public static bool CanStand(Vector3 feet, float currentHeight, MotorSettings m)
        {
            float r = m.radius * 0.95f;
            Vector3 bottom = feet + Vector3.up * (Mathf.Max(currentHeight - r, r) + m.skinWidth);
            Vector3 top = feet + Vector3.up * (m.standHeight - r + m.skinWidth);
            if (top.y <= bottom.y) return true;
            return !Physics.CheckCapsule(bottom, top, r, EnvironmentMask, QueryTriggerInteraction.Ignore);
        }
    }
}
