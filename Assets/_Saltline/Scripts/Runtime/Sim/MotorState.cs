using System;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Sim
{
    public enum Stance : byte
    {
        Standing = 0,
        Crouching = 1,
        Sliding = 2,
    }

    [Flags]
    public enum MotorFlags : byte
    {
        None = 0,
        Grounded = 1 << 0,
        Sprinting = 1 << 1,
        Aiming = 1 << 2,
        JumpedThisTick = 1 << 3,
        LandedThisTick = 1 << 4,
        WantsCrouch = 1 << 5,
    }

    /// <summary>
    /// Complete movement state. Anything that changes how the next tick simulates must live
    /// here, otherwise reconciliation replays would diverge from the host.
    /// </summary>
    public struct MotorState : INetworkSerializeByMemcpy
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Height;          // current capsule height (lerps between stances)
        public float SlideTimer;      // time spent in current slide
        public float SlideCooldown;   // time until a new slide may start
        public float CoyoteTimer;     // time since we left the ground
        public float JumpBufferTimer; // time since jump was pressed while airborne
        public float SprintRecover;   // time since sprint ended (sprint-to-fire)
        public float LastLandSpeed;   // downward speed at the last landing (cosmetic)
        public Stance Stance;
        public MotorFlags Flags;
        public bool JumpHeld;         // edge detection for jump

        public bool Grounded => (Flags & MotorFlags.Grounded) != 0;
        public bool Sprinting => (Flags & MotorFlags.Sprinting) != 0;
        public bool Aiming => (Flags & MotorFlags.Aiming) != 0;

        public float HorizontalSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;

        public static MotorState Spawned(Vector3 position, MotorSettings s) => new MotorState
        {
            Position = position,
            Height = s.standHeight,
            Stance = Stance.Standing,
            Flags = MotorFlags.Grounded,
            SprintRecover = 10f,
        };

        /// <summary>Tolerance check used to decide whether a reconciliation is needed.</summary>
        public static bool NearlyEqual(in MotorState a, in MotorState b)
        {
            return (a.Position - b.Position).sqrMagnitude < 0.0004f &&      // 2 cm
                   (a.Velocity - b.Velocity).sqrMagnitude < 0.04f &&        // 0.2 m/s
                   Mathf.Abs(a.Height - b.Height) < 0.02f &&
                   a.Stance == b.Stance &&
                   ((a.Flags ^ b.Flags) & MotorFlags.Grounded) == 0;
        }
    }
}
