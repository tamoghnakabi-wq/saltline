using System;
using Saltline.Combat;
using Saltline.Net;
using Saltline.Player;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.Diagnostics
{
    /// <summary>Scripted input for automated tests. The brain is swapped per test phase.</summary>
    public sealed class BotInputProvider : IPlayerInputProvider
    {
        public Func<NetPlayer, PlayerIntent, PlayerIntent> Brain;
        public int Respawns;
        public float LastSpawnYaw;

        public void Sample(NetPlayer player, ref PlayerIntent intent)
        {
            if (Brain != null) intent = Brain(player, intent);
        }

        public void OnRecoil(Vector2 kick, WeaponDefinition weapon) { } // bots re-aim every tick
        public void OnRespawned(float yaw) { Respawns++; LastSpawnYaw = yaw; }

        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        /// <summary>
        /// Where <paramref name="target"/> is drawn on this machine at the render time this tick's
        /// input will carry (the time the host rewinds to).
        /// </summary>
        public static bool TargetChest(NetPlayer self, NetPlayer target, out Vector3 point)
        {
            point = default;
            if (target == null || !target.IsSpawned) return false;
            double rt = NetClock.RemoteRenderTick();
            HitboxPose pose;
            if (self.IsServer)
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (!LagCompensation.Server.TryGetPose(target.OwnerClientId, rt, nm.ServerTime.Tick, NetClock.TickRate, out pose, out bool alive, false) || !alive)
                    return false;
            }
            else
            {
                if (!target.SampleRemote(rt, out var rp) || !rp.Alive) return false;
                pose = rp.ToHitbox();
            }
            HitboxModel.Build(pose, self.MotorConfig, s_Caps);
            point = (s_Caps[1].A + s_Caps[1].B) * 0.5f;
            return true;
        }

        public static void AimAt(NetPlayer self, Vector3 point, ref PlayerIntent intent)
        {
            Vector3 eye = PlayerMotor.EyePosition(self.SimMotor, self.MotorConfig);
            Vector3 d = (point - eye).normalized;
            intent.AimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            intent.AimPitch = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
