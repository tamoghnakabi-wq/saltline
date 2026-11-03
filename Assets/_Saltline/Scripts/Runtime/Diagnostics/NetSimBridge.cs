using Saltline.Core;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
#endif

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Enables the Multiplayer Tools network simulator from the command line in editor and
    /// development builds: -netsim delayMs,jitterMs,lossPercent  (e.g. -netsim 120,25,3).
    /// <see cref="Stall"/> adds a network stall on top. Release builds compile this to a no-op.
    /// </summary>
    public static class NetSimBridge
    {
        public static string ActiveDescription { get; private set; } = "off";

        public static void ApplyFromCommandLine(NetworkManager nm)
        {
            string spec = CommandLine.Get("netsim");
            if (string.IsNullOrEmpty(spec)) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var parts = spec.Split(',');
            int delay = parts.Length > 0 && int.TryParse(parts[0], out var d) ? d : 0;
            int jitter = parts.Length > 1 && int.TryParse(parts[1], out var j) ? j : 0;
            int loss = parts.Length > 2 && int.TryParse(parts[2], out var l) ? l : 0;

            var sim = nm.GetComponent<NetworkSimulator>();
            if (sim == null) sim = nm.gameObject.AddComponent<NetworkSimulator>();
            sim.ConnectionPreset = NetworkSimulatorPreset.Create("cli", "command line", delay, jitter, 0, loss);
            ActiveDescription = $"delay {delay}ms, jitter {jitter}ms, loss {loss}%";
            Debug.Log($"[SALTLINE] Network simulator enabled: {ActiveDescription}");
#else
            Debug.LogWarning("[SALTLINE] -netsim ignored: network simulator is only available in development builds.");
#endif
        }

        /// <summary>
        /// A network stall on this machine, both directions, for <paramref name="seconds"/>.
        /// hold: packets are held and then arrive together, as on a congested link or a VPN that
        /// briefly loses its route. drop: packets are lost (the simulator's lag spike). Needs -netsim
        /// (development builds); returns false otherwise.
        /// </summary>
        public static bool Stall(float seconds, bool hold)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var nm = NetworkManager.Singleton;
            var sim = nm != null ? nm.GetComponent<NetworkSimulator>() : null;
            if (sim == null || seconds <= 0f) return false;
            Debug.Log($"[SALTLINE] Network stall: {(hold ? "holding" : "dropping")} packets for {seconds:F2} s");
            if (hold) nm.gameObject.AddComponent<HeldStall>().Begin(sim, seconds);
            else sim.TriggerLagSpike(TimeSpan.FromSeconds(seconds));
            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Raises the simulated delay to "the time left in the stall" every frame, so everything sent
        /// during the stall arrives together when it ends, followed by normal traffic.
        /// </summary>
        sealed class HeldStall : MonoBehaviour
        {
            NetworkSimulator m_Sim;
            INetworkSimulatorPreset m_Base;
            float m_End;

            public void Begin(NetworkSimulator sim, float seconds)
            {
                m_Sim = sim;
                m_Base = sim.ConnectionPreset;
                m_End = Time.realtimeSinceStartup + seconds;
                Apply();
            }

            void Update() => Apply();

            void Apply()
            {
                float left = m_End - Time.realtimeSinceStartup;
                if (left <= 0f || m_Sim == null)
                {
                    if (m_Sim != null) m_Sim.ConnectionPreset = m_Base;
                    Debug.Log("[SALTLINE] Network stall over");
                    Destroy(this);
                    return;
                }
                int extra = Mathf.CeilToInt(left * 1000f);
                m_Sim.ConnectionPreset = NetworkSimulatorPreset.Create("stall", "held stall",
                    m_Base.PacketDelayMs + extra, m_Base.PacketJitterMs, m_Base.PacketLossInterval, m_Base.PacketLossPercent);
            }
        }
#endif
    }
}
