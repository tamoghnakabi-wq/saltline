using System;
using System.Collections.Generic;
using System.Text;
using Saltline.Core;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Net
{
    /// <summary>Identity a client presents when connecting. Validated by the host.</summary>
    public struct ConnectionPayload
    {
        public int Protocol;
        public string PlayerName;
        public string AuthPlayerId;
        public ClientPlatform Platform;

        const char Sep = '\u001f';

        public byte[] Encode() =>
            Encoding.UTF8.GetBytes($"{Protocol}{Sep}{PlayerName}{Sep}{AuthPlayerId}{Sep}{(int)Platform}");

        public static bool TryDecode(byte[] data, out ConnectionPayload payload)
        {
            payload = default;
            if (data == null || data.Length == 0 || data.Length > 512) return false;
            string[] parts;
            try { parts = Encoding.UTF8.GetString(data).Split(Sep); }
            catch { return false; }
            if (parts.Length != 4 || !int.TryParse(parts[0], out int proto)) return false;
            int.TryParse(parts[3], out int plat);
            payload = new ConnectionPayload
            {
                Protocol = proto, PlayerName = parts[1], AuthPlayerId = parts[2],
                Platform = plat >= 0 && plat <= 4 ? (ClientPlatform)plat : ClientPlatform.Unknown,
            };
            return true;
        }

        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Operator";
            var sb = new StringBuilder(16);
            foreach (char c in raw.Trim())
            {
                if (sb.Length >= 16) break;
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' || c == '.') sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "Operator" : s;
        }
    }

    /// <summary>
    /// Host-side connection approval: protocol check, capacity, duplicate identity, and
    /// join-in-progress policy. Approved identities are kept until the client connects
    /// so the roster is built from validated data rather than client claims made later.
    /// </summary>
    public static class ConnectionGate
    {
        public struct ApprovedIdentity
        {
            public string Name;
            public string AuthPlayerId;
            public ClientPlatform Platform;
        }

        static readonly Dictionary<ulong, ApprovedIdentity> s_Identities = new();

        /// <summary>Set by the match logic. When false, new joins are refused.</summary>
        public static Func<bool> AllowJoin = () => true;

        public static bool TryGetIdentity(ulong clientId, out ApprovedIdentity identity)
            => s_Identities.TryGetValue(clientId, out identity);

        public static void Forget(ulong clientId) => s_Identities.Remove(clientId);

        public static void Clear() => s_Identities.Clear();

        public static void Install(NetworkManager nm)
        {
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = Approve;
        }

        /// <summary>Sets the payload this process sends when it joins or hosts.</summary>
        public static void SetLocalPayload(NetworkManager nm, string playerName)
        {
            var payload = new ConnectionPayload
            {
                Protocol = BuildInfo.NetProtocol,
                PlayerName = ConnectionPayload.SanitizeName(playerName),
                AuthPlayerId = UgsAuth.PlayerId,
                Platform = PlayerRecord.LocalPlatform(),
            };
            nm.NetworkConfig.ConnectionData = payload.Encode();
        }

        static void Approve(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (!ConnectionPayload.TryDecode(request.Payload, out var payload))
            {
                Reject(response, "Invalid connection data.");
                return;
            }

            if (payload.Protocol != BuildInfo.NetProtocol)
            {
                Reject(response, $"Version mismatch (host v{BuildInfo.NetProtocol}, you v{payload.Protocol}). Update the game.");
                return;
            }

            bool isHostSelf = request.ClientNetworkId == NetworkManager.ServerClientId;

            if (!isHostSelf)
            {
                int connected = nm != null ? nm.ConnectedClientsIds.Count : 0;
                if (connected >= BuildInfo.MaxPlayers)
                {
                    Reject(response, "Match is full (8/8).");
                    return;
                }

                if (!AllowJoin())
                {
                    Reject(response, "The match is not accepting new players right now.");
                    return;
                }

                if (!string.IsNullOrEmpty(payload.AuthPlayerId))
                {
                    foreach (var kv in s_Identities)
                    {
                        if (kv.Value.AuthPlayerId == payload.AuthPlayerId)
                        {
                            Reject(response, "This account is already connected to the match.");
                            return;
                        }
                    }
                }
            }

            s_Identities[request.ClientNetworkId] = new ApprovedIdentity
            {
                Name = ConnectionPayload.SanitizeName(payload.PlayerName),
                AuthPlayerId = payload.AuthPlayerId ?? "",
                Platform = payload.Platform,
            };
            response.Approved = true;
        }

        static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.Reason = reason;
            Debug.Log($"[SALTLINE] Connection rejected: {reason}");
        }
    }
}
