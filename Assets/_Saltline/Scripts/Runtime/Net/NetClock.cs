using Unity.Netcode;
using UnityEngine;

namespace Saltline.Net
{
    /// <summary>
    /// Client-side timing for rendering other players. Remote players are drawn slightly in the
    /// past (an interpolation delay) so there are always two snapshots to blend between. The
    /// delay adapts to observed snapshot lateness (jitter). The same render time is sent with
    /// every input so the host can rewind hitboxes to exactly what the shooter saw.
    /// </summary>
    public static class NetClock
    {
        public static float InterpolationTicks { get; private set; } = 4f;
        public static float MinTicks = 3f;
        public static float MaxTicks = 12f;
        public static int SnapshotEveryTicks = 2;

        static float s_MaxLateness;
        static int s_LastUpdateFrame = -1;
        public static int ExtrapolatedFrames;

        public static int TickRate => NetworkManager.Singleton != null ? (int)NetworkManager.Singleton.NetworkConfig.TickRate : 60;
        public static float TickInterval => 1f / TickRate;

        /// <summary>Called when a snapshot stamped with <paramref name="serverTick"/> arrives.</summary>
        public static void OnSnapshotArrived(int serverTick)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer) return;
            float late = (float)(nm.ServerTime.TickWithPartial - serverTick);
            // A network stall makes snapshots seconds late. No delay can cover that, so remember at most
            // what the delay can be; otherwise the slow forgetting below would hold the delay at its
            // maximum for a minute after a two-second stall, and every shot would need a deeper rewind.
            if (late > MaxTicks) late = MaxTicks;
            if (late > s_MaxLateness) s_MaxLateness = late;
        }

        public static void Update()
        {
            if (s_LastUpdateFrame == Time.frameCount) return;
            s_LastUpdateFrame = Time.frameCount;
            float dt = Time.unscaledDeltaTime;
            s_MaxLateness = Mathf.Max(-2f, s_MaxLateness - 1.5f * dt); // forget old spikes slowly
            float target = Mathf.Clamp(s_MaxLateness + SnapshotEveryTicks + 1.5f, MinTicks, MaxTicks);
            float rate = target > InterpolationTicks ? 30f : 3f; // grow fast, shrink slowly (others play 5% fast meanwhile)
            InterpolationTicks = Mathf.MoveTowards(InterpolationTicks, target, rate * dt);
        }

        /// <summary>Fractional server tick at which other players are rendered right now.</summary>
        public static double RemoteRenderTick()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0;
            double now = nm.ServerTime.TickWithPartial;
            return nm.IsServer ? now - 1.0 : now - InterpolationTicks;
        }

        public static void Reset()
        {
            InterpolationTicks = 4f;
            s_MaxLateness = 0f;
            ExtrapolatedFrames = 0;
        }
    }
}
