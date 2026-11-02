using Saltline.Combat;
using Saltline.Sim;
using Saltline.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Player
{
    /// <summary>What a controller (human or bot) wants to do on one tick.</summary>
    public struct PlayerIntent
    {
        public Vector2 Move;      // already rotated so +Y is "forward along the aim yaw"
        public float AimYaw;      // eye-ray direction, degrees
        public float AimPitch;
        public InputButtons Buttons;
    }

    public interface IPlayerInputProvider
    {
        void Sample(NetPlayer player, ref PlayerIntent intent);
        /// <summary>A predicted shot kicked the view (owner only, never during replays).</summary>
        void OnRecoil(Vector2 kick, WeaponDefinition weapon);
        /// <summary>The player (re)spawned facing <paramref name="yaw"/>.</summary>
        void OnRespawned(float yaw);
    }

    /// <summary>Last few inputs, newest last. Redundancy covers lost unreliable packets.</summary>
    public struct InputBundle : INetworkSerializeByMemcpy
    {
        public const int Capacity = 4;
        public byte Count;
        public InputCmd C0, C1, C2, C3;

        public InputCmd Get(int i) => i switch { 0 => C0, 1 => C1, 2 => C2, _ => C3 };

        public void Set(int i, in InputCmd c)
        {
            switch (i)
            {
                case 0: C0 = c; break;
                case 1: C1 = c; break;
                case 2: C2 = c; break;
                default: C3 = c; break;
            }
        }
    }

    /// <summary>Full-precision authoritative state for the owning client to reconcile against.</summary>
    public struct OwnerSnapshot : INetworkSerializeByMemcpy
    {
        public int AckTick;       // last input tick the host simulated (int.MinValue = none yet)
        public int ServerTick;
        public byte SpawnSerial;
        public bool Alive;
        public ushort SpawnYaw;
        public MotorState Motor;
        public WeaponState Weapon;
    }

    /// <summary>What other clients need to draw this player.</summary>
    public struct RemoteSnapshot : INetworkSerializeByMemcpy
    {
        public int ServerTick;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Height;
        public ushort Yaw;
        public short Pitch;
        public Stance Stance;
        public MotorFlags Flags;
        public byte WeaponId;
        public WeaponAction Action;
        public byte ActionProgress;
        public byte Ads;
        public byte SpawnSerial;
        public bool Alive;
    }

    /// <summary>Visual state of a player this frame, whatever its network role.</summary>
    public struct RenderPose
    {
        public Vector3 Feet;
        public Vector3 Velocity;
        public float Yaw;
        public float Pitch;
        public float Height;
        public Stance Stance;
        public MotorFlags Flags;
        public byte WeaponId;
        public WeaponAction Action;
        public float ActionProgress;
        public float Ads;
        public bool Alive;

        public bool Grounded => (Flags & MotorFlags.Grounded) != 0;
        public bool Sprinting => (Flags & MotorFlags.Sprinting) != 0;

        public HitboxPose ToHitbox() => new HitboxPose
        {
            Feet = Feet, Velocity = Velocity, Yaw = Yaw, Pitch = Pitch, Height = Height, Stance = Stance,
        };
    }
}
