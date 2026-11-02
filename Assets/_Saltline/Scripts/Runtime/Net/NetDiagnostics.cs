using System;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Net
{
    /// <summary>
    /// Round-trip measurement and end-to-end replication checks. Lives on the GameSession
    /// prefab. Used by the headless Relay test and the in-game network overlay.
    /// </summary>
    public class NetDiagnostics : NetworkBehaviour
    {
        public static NetDiagnostics Instance { get; private set; }

        public event Action<uint, double> Pong;
        public event Action<ulong, TestReport> ReportReceived;
        public event Action<bool, string> AckReceived;
        /// <summary>Test coordination: a client announced a phase (host side).</summary>
        public event Action<ulong, int, float> ClientPhase;

        public struct TestReport : INetworkSerializeByMemcpy
        {
            public uint TokenEcho;
            public float RttAvgMs;
            public float RttMinMs;
            public float RttMaxMs;
            public int PongsReceived;
            public int PingsSent;
            public int RosterCountSeen;
            public byte Platform;
        }

        public override void OnNetworkSpawn() => Instance = this;

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        public void PingRpc(uint seq, double clientTime, RpcParams rpc = default)
        {
            PongRpc(seq, clientTime, RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        void PongRpc(uint seq, double clientTime, RpcParams rpc)
        {
            Pong?.Invoke(seq, clientTime);
        }

        [Rpc(SendTo.Server)]
        public void ReportRpc(TestReport report, RpcParams rpc = default)
        {
            ReportReceived?.Invoke(rpc.Receive.SenderClientId, report);
        }

        public void SendAck(ulong clientId, bool ok, string message)
        {
            if (!IsServer) return;
            AckRpc(ok, message ?? "", RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void AckRpc(bool ok, string message, RpcParams rpc)
        {
            AckReceived?.Invoke(ok, message);
        }

        [Rpc(SendTo.Server)]
        public void ClientPhaseRpc(int phase, float value, RpcParams rpc = default)
        {
            ClientPhase?.Invoke(rpc.Receive.SenderClientId, phase, value);
        }
    }
}
