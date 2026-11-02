using Saltline.Core;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
#endif

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Enables the Multiplayer Tools network simulator from the command line in editor and
    /// development builds: -netsim delayMs,jitterMs,lossPercent  (e.g. -netsim 120,25,3).
    /// Release builds compile this to a no-op.
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
    }
}
