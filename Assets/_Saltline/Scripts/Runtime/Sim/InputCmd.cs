using System;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Sim
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Crouch = 1 << 1,
        Sprint = 1 << 2,
        Fire = 1 << 3,
        Aim = 1 << 4,
        Reload = 1 << 5,
        SelectPrimary = 1 << 6,
        SelectSecondary = 1 << 7,
        ThirdPerson = 1 << 8, // informational: lets the host know which view the shooter used
    }

    /// <summary>
    /// One simulation tick of player intent. The client quantizes this *before* simulating it
    /// locally, so the client's prediction and the host's authoritative simulation consume
    /// bit-identical input.
    /// </summary>
    public struct InputCmd : INetworkSerializeByMemcpy, IEquatable<InputCmd>
    {
        public int Tick;
        public sbyte MoveX;      // -127..127 strafe
        public sbyte MoveY;      // -127..127 forward
        public ushort Yaw;       // 0..65535 => 0..360 degrees
        public short Pitch;      // -16384..16384 => -90..90 degrees
        public InputButtons Buttons;

        /// <summary>
        /// Server time (in ticks, with 1/256 fraction) at which this client was rendering other
        /// players. The host rewinds hitboxes to this moment for lag-compensated hit tests.
        /// </summary>
        public int ViewTick;
        public byte ViewTickFraction;

        public const float YawScale = 360f / 65536f;
        public const float PitchScale = 90f / 16384f;

        public bool Has(InputButtons b) => (Buttons & b) != 0;

        public float YawDegrees => Yaw * YawScale;
        public float PitchDegrees => Pitch * PitchScale;

        public Vector2 Move => new Vector2(MoveX / 127f, MoveY / 127f);

        public double ViewTime(double tickInterval) => (ViewTick + ViewTickFraction / 256.0) * tickInterval;

        public static ushort QuantizeYaw(float degrees)
        {
            float d = Mathf.Repeat(degrees, 360f);
            return (ushort)(Mathf.RoundToInt(d / YawScale) & 0xFFFF);
        }

        public static short QuantizePitch(float degrees)
        {
            float d = Mathf.Clamp(degrees, -89.5f, 89.5f);
            return (short)Mathf.RoundToInt(d / PitchScale);
        }

        public static sbyte QuantizeAxis(float v) => (sbyte)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 127f);

        /// <summary>World direction the player is aiming (yaw/pitch, Unity: +Z forward, +Y up).</summary>
        public Vector3 AimDirection => Quaternion.Euler(-PitchDegrees, YawDegrees, 0f) * Vector3.forward;

        public bool Equals(InputCmd o) =>
            Tick == o.Tick && MoveX == o.MoveX && MoveY == o.MoveY && Yaw == o.Yaw && Pitch == o.Pitch &&
            Buttons == o.Buttons && ViewTick == o.ViewTick && ViewTickFraction == o.ViewTickFraction;

        public override bool Equals(object obj) => obj is InputCmd o && Equals(o);
        public override int GetHashCode() => Tick;
    }
}
