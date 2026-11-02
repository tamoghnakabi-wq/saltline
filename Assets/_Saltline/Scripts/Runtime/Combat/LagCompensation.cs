using System.Collections.Generic;
using UnityEngine;

namespace Saltline.Combat
{
    /// <summary>
    /// Host-side history of every player's hitbox pose per server tick. Shots are tested against
    /// the poses the shooter was actually looking at (their interpolated view time), within a
    /// bounded rewind window. This favours the shooter by design; the window caps how far a
    /// high-latency shooter can reach into the past.
    /// </summary>
    public sealed class LagCompensation
    {
        public const int HistoryLength = 64; // ~1 s at 60 Hz

        struct Entry
        {
            public int Tick;
            public HitboxPose Pose;
            public bool Alive;
        }

        readonly Dictionary<ulong, Entry[]> m_History = new();

        public static LagCompensation Server { get; } = new LagCompensation();

        /// <summary>Maximum rewind in seconds (host setting).</summary>
        public float MaxRewindSeconds = 0.35f;

        public int Rewinds { get; private set; }
        public int ClampedRewinds { get; private set; }

        public void Clear() => m_History.Clear();
        public void Remove(ulong clientId) => m_History.Remove(clientId);

        public void Record(ulong clientId, int tick, in HitboxPose pose, bool alive)
        {
            if (!m_History.TryGetValue(clientId, out var ring))
            {
                ring = new Entry[HistoryLength];
                for (int i = 0; i < ring.Length; i++) ring[i].Tick = int.MinValue;
                m_History[clientId] = ring;
            }
            ring[Mod(tick)] = new Entry { Tick = tick, Pose = pose, Alive = alive };
        }

        /// <summary>
        /// Pose at fractional server tick <paramref name="viewTick"/>, clamped to the rewind window
        /// ending at <paramref name="currentTick"/>.
        /// </summary>
        public bool TryGetPose(ulong clientId, double viewTick, int currentTick, double tickRate, out HitboxPose pose, out bool alive, bool countStats = true)
        {
            pose = default;
            alive = false;
            if (!m_History.TryGetValue(clientId, out var ring)) return false;

            double minTick = currentTick - MaxRewindSeconds * tickRate;
            if (viewTick < minTick) { viewTick = minTick; if (countStats) ClampedRewinds++; }
            if (viewTick > currentTick) viewTick = currentTick;
            if (countStats) Rewinds++;

            int t0 = (int)System.Math.Floor(viewTick);
            float frac = (float)(viewTick - t0);
            ref Entry e0 = ref ring[Mod(t0)];
            ref Entry e1 = ref ring[Mod(t0 + 1)];
            bool has0 = e0.Tick == t0;
            bool has1 = e1.Tick == t0 + 1;

            if (has0 && has1)
            {
                pose = HitboxPose.Lerp(e0.Pose, e1.Pose, frac);
                alive = frac < 0.5f ? e0.Alive : e1.Alive;
                return true;
            }
            if (has0) { pose = e0.Pose; alive = e0.Alive; return true; }
            if (has1) { pose = e1.Pose; alive = e1.Alive; return true; }

            // Fall back to the newest sample at or before the requested tick.
            int bestTick = int.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i].Tick <= t0 && ring[i].Tick > bestTick)
                {
                    bestTick = ring[i].Tick;
                    pose = ring[i].Pose;
                    alive = ring[i].Alive;
                }
            }
            return bestTick != int.MinValue;
        }

        static int Mod(int tick) => ((tick % HistoryLength) + HistoryLength) % HistoryLength;
    }
}
