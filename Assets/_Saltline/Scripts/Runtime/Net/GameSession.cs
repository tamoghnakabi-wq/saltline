using System;
using Saltline.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Saltline.Net
{
    /// <summary>
    /// Persistent, host-owned network object that lives for the whole session (lobby and
    /// match). Holds the authoritative roster. Clients can only *request* changes via RPC;
    /// the host validates and writes.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameSession : NetworkBehaviour
    {
        public static GameSession Instance { get; private set; }
        public static event Action<GameSession> Spawned;
        public static event Action Despawned;

        public NetworkList<PlayerRecord> Roster;

        /// <summary>Host-chosen random token; clients echo it in diagnostics to prove replication.</summary>
        public readonly NetworkVariable<uint> SyncToken = new(0);

        /// <summary>When false, team and ready changes are refused (e.g. during a live match).</summary>
        public Func<bool> LobbyEditable = () => true;

        float m_PingRefresh;
        float m_HeartbeatTimer;
        double m_LastHostHeartbeat;
        bool m_HostLossRaised;

        /// <summary>Clients give up on a silent host after this many seconds (Relay does not
        /// reliably signal a crashed host, so the game watches for it itself).</summary>
        public static float HostTimeoutSeconds = 6f;

        void Awake()
        {
            Roster = new NetworkList<PlayerRecord>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            m_LastHostHeartbeat = Time.realtimeSinceStartupAsDouble;
            m_HostLossRaised = false;
            HostTimeoutSeconds = Saltline.Core.CommandLine.GetFloat("hosttimeout", HostTimeoutSeconds);

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                foreach (ulong id in NetworkManager.ConnectedClientsIds) AddRecord(id);
                SyncToken.Value = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            }

            Spawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (Instance == this) Instance = null;
            Despawned?.Invoke();
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (!IsServer)
            {
                WatchHost();
                return;
            }

            m_HeartbeatTimer -= Time.unscaledDeltaTime;
            if (m_HeartbeatTimer <= 0f)
            {
                m_HeartbeatTimer = 0.25f;
                HeartbeatRpc();
            }

            m_PingRefresh -= Time.unscaledDeltaTime;
            if (m_PingRefresh > 0f) return;
            m_PingRefresh = 2f;

            var transport = NetworkManager.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null) return;
            for (int i = 0; i < Roster.Count; i++)
            {
                var r = Roster[i];
                ushort ping = r.ClientId == NetworkManager.ServerClientId
                    ? (ushort)0
                    : (ushort)Mathf.Min(9999, (int)transport.GetCurrentRtt(r.ClientId));
                if (ping != r.PingMs) { r.PingMs = ping; Roster[i] = r; }
            }
        }

        // ---------- Host liveness ----------

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void HeartbeatRpc()
        {
            m_LastHostHeartbeat = Time.realtimeSinceStartupAsDouble;
        }

        void WatchHost()
        {
            if (m_HostLossRaised) return;
            double silent = Time.realtimeSinceStartupAsDouble - m_LastHostHeartbeat;
            if (silent < HostTimeoutSeconds) return;
            m_HostLossRaised = true;
            Debug.Log($"[SALTLINE] Host silent for {silent:F1}s; ending session.");
            SessionService.NotifyNetworkLost($"Lost connection to the host (no response for {HostTimeoutSeconds:F0}s).");
        }

        // ---------- Roster (host only) ----------

        void OnClientConnected(ulong clientId) => AddRecord(clientId);

        void OnClientDisconnected(ulong clientId)
        {
            int i = IndexOf(clientId);
            if (i >= 0) Roster.RemoveAt(i);
            ConnectionGate.Forget(clientId);
        }

        void AddRecord(ulong clientId)
        {
            if (IndexOf(clientId) >= 0) return;
            ConnectionGate.TryGetIdentity(clientId, out var ident);
            var rec = new PlayerRecord
            {
                ClientId = clientId,
                Name = string.IsNullOrEmpty(ident.Name) ? $"Operator {clientId}" : ident.Name,
                AuthId = ident.AuthPlayerId ?? "",
                Platform = ident.Platform,
                IsHost = clientId == NetworkManager.ServerClientId,
                Team = PickBalancedTeam(),
            };
            Roster.Add(rec);
            Debug.Log($"[SALTLINE] Roster + {rec.Name} (client {clientId}, {rec.Platform}) -> {rec.Team}");
        }

        public int IndexOf(ulong clientId)
        {
            for (int i = 0; i < Roster.Count; i++)
                if (Roster[i].ClientId == clientId) return i;
            return -1;
        }

        public bool TryGet(ulong clientId, out PlayerRecord rec)
        {
            int i = IndexOf(clientId);
            rec = i >= 0 ? Roster[i] : default;
            return i >= 0;
        }

        public int CountTeam(Team t)
        {
            int n = 0;
            for (int i = 0; i < Roster.Count; i++) if (Roster[i].Team == t) n++;
            return n;
        }

        public Team PickBalancedTeam()
        {
            int red = CountTeam(Team.Red), blue = CountTeam(Team.Blue);
            if (red >= BuildInfo.MaxPerTeam && blue >= BuildInfo.MaxPerTeam) return Team.None;
            return blue < red ? Team.Blue : Team.Red;
        }

        /// <summary>Host-side mutation helper used by match logic (scores, kills...).</summary>
        public void ModifyRecord(ulong clientId, Func<PlayerRecord, PlayerRecord> change)
        {
            if (!IsServer) return;
            int i = IndexOf(clientId);
            if (i < 0) return;
            Roster[i] = change(Roster[i]);
        }

        // ---------- Client requests ----------

        [Rpc(SendTo.Server)]
        public void RequestTeamRpc(Team team, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            int i = IndexOf(sender);
            if (i < 0 || !LobbyEditable()) return;
            if (team != Team.Red && team != Team.Blue) return;
            var r = Roster[i];
            if (r.Team == team) return;
            if (CountTeam(team) >= BuildInfo.MaxPerTeam) return; // team full: request ignored
            r.Team = team;
            r.Ready = false;
            Roster[i] = r;
        }

        [Rpc(SendTo.Server)]
        public void SetReadyRpc(bool ready, RpcParams rpc = default)
        {
            int i = IndexOf(rpc.Receive.SenderClientId);
            if (i < 0 || !LobbyEditable()) return;
            var r = Roster[i];
            if (r.Ready == ready) return;
            r.Ready = ready;
            Roster[i] = r;
        }
    }
}
