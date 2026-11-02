using System;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Combat
{
    public struct HitConfirm : INetworkSerializeByMemcpy
    {
        public ulong Victim;
        public short Damage;
        public bool Headshot;
        public bool Kill;
    }

    public struct DamageTaken : INetworkSerializeByMemcpy
    {
        public ulong Attacker;
        public Vector3 From;
        public short Damage;
        public bool Headshot;
    }

    public struct KillEvent : INetworkSerializeByMemcpy
    {
        public ulong Killer;      // == Victim for suicides / falls
        public ulong Victim;
        public ulong Assister;    // ulong.MaxValue when none
        public byte WeaponId;     // 255 = environment
        public bool Headshot;
        public bool Penetration;
    }

    public struct ShotFx : INetworkSerializeByMemcpy
    {
        public ulong Shooter;
        public Vector3 Origin;
        public ushort Yaw;
        public short Pitch;
        public float Spread;
        public uint ShotNumber;
        public byte WeaponId;
    }

    /// <summary>
    /// Presentation hooks. Gameplay code raises these; FX, audio and HUD subscribe. Nothing here
    /// affects simulation, so it is safe to have no listeners (e.g. headless builds).
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>A shot to draw. local = fired by this machine's own player (predicted).</summary>
        public static event Action<ShotFx, bool> Shot;
        public static event Action<HitConfirm> HitConfirmed;
        public static event Action<DamageTaken> LocalDamaged;
        public static event Action<KillEvent> Killed;
        public static event Action<Impact> ImpactSpawned;

        public static void RaiseShot(in ShotFx fx, bool local) => Shot?.Invoke(fx, local);
        public static void RaiseHitConfirmed(in HitConfirm h) => HitConfirmed?.Invoke(h);
        public static void RaiseLocalDamaged(in DamageTaken d) => LocalDamaged?.Invoke(d);
        public static void RaiseKilled(in KillEvent k) => Killed?.Invoke(k);
        public static void RaiseImpact(in Impact i) => ImpactSpawned?.Invoke(i);
    }
}
