using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saltline.Core;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Saltline.Net
{
    public struct HostSettings
    {
        public string SessionName;
        public string PlayerName;
        public bool IsPrivate;
        public string Password; // optional, 8-64 chars when set
        public string MapId;
    }

    /// <summary>
    /// Thin, game-specific facade over the Multiplayer Services SDK.
    /// Sessions own discovery, join codes, privacy and capacity; the session's Relay
    /// network brings up Netcode for GameObjects (host = authoritative player-host).
    /// </summary>
    public static class SessionService
    {
        public static ISession Current { get; private set; }
        public static bool InSession => Current != null;

        /// <summary>Fired with a human-readable reason when the session ends unexpectedly.</summary>
        public static event Action<string> SessionLost;
        public static event Action<ISession> SessionStarted;

        /// <summary>Refuses online actions when the update check says this build is too old to play online.</summary>
        static void EnsureUpToDate()
        {
            if (UpdateCheck.Blocking) throw new UpdateRequiredException(UpdateCheck.Current.Message);
        }

        public static async Task<ISession> HostAsync(HostSettings settings)
        {
            EnsureUpToDate();
            await UgsAuth.EnsureSignedInAsync();
            await LeaveAsync();

            var nm = RequireNetworkManager();
            ConnectionGate.Install(nm);
            ConnectionGate.SetLocalPayload(nm, settings.PlayerName);

            var props = new Dictionary<string, SessionProperty>
            {
                [BuildInfo.PropMode] = new SessionProperty("TDM", VisibilityPropertyOptions.Public, PropertyIndex.String1),
                [BuildInfo.PropMap] = new SessionProperty(settings.MapId ?? "kappa", VisibilityPropertyOptions.Public, PropertyIndex.String2),
                [BuildInfo.PropPhase] = new SessionProperty("Lobby", VisibilityPropertyOptions.Public, PropertyIndex.String3),
                [BuildInfo.PropProtocol] = new SessionProperty(BuildInfo.NetProtocol.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String4),
                [BuildInfo.PropHostName] = new SessionProperty(ConnectionPayload.SanitizeName(settings.PlayerName), VisibilityPropertyOptions.Public),
            };

            var options = new SessionOptions
            {
                Type = BuildInfo.SessionType,
                Name = string.IsNullOrWhiteSpace(settings.SessionName)
                    ? $"{ConnectionPayload.SanitizeName(settings.PlayerName)}'s match"
                    : settings.SessionName.Trim(),
                MaxPlayers = BuildInfo.MaxPlayers,
                IsPrivate = settings.IsPrivate,
                Password = string.IsNullOrEmpty(settings.Password) ? null : settings.Password,
                SessionProperties = props,
            }.WithRelayNetwork();

            var session = await MultiplayerService.Instance.CreateSessionAsync(options);
            Attach(session);
            Debug.Log($"[SALTLINE] Hosted session id={session.Id} code={session.Code} private={settings.IsPrivate}");
            return session;
        }

        public static async Task<ISession> JoinByCodeAsync(string code, string playerName, string password = null)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4) throw new ArgumentException("Enter a valid join code.");

            EnsureUpToDate();
            await UgsAuth.EnsureSignedInAsync();
            await LeaveAsync();
            var nm = RequireNetworkManager();
            ConnectionGate.SetLocalPayload(nm, playerName);

            var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code,
                new JoinSessionOptions { Type = BuildInfo.SessionType, Password = password });
            Attach(session);
            Debug.Log($"[SALTLINE] Joined session id={session.Id} by code");
            return session;
        }

        public static async Task<ISession> JoinByIdAsync(string sessionId, string playerName, string password = null)
        {
            EnsureUpToDate();
            await UgsAuth.EnsureSignedInAsync();
            await LeaveAsync();
            var nm = RequireNetworkManager();
            ConnectionGate.SetLocalPayload(nm, playerName);

            var session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId,
                new JoinSessionOptions { Type = BuildInfo.SessionType, Password = password });
            Attach(session);
            return session;
        }

        /// <summary>Public, unlocked, version-compatible sessions with free slots.</summary>
        public static async Task<IList<ISessionInfo>> BrowseAsync()
        {
            EnsureUpToDate();
            await UgsAuth.EnsureSignedInAsync();
            var query = new QuerySessionsOptions
            {
                Count = 50,
                FilterOptions = new List<FilterOption>
                {
                    new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater),
                    new FilterOption(FilterField.IsLocked, "false", FilterOperation.Equal),
                    new FilterOption(FilterField.StringIndex4, BuildInfo.NetProtocol.ToString(), FilterOperation.Equal),
                },
                SortOptions = new List<SortOption>
                {
                    new SortOption(SortOrder.Descending, SortField.CreationTime),
                },
            };
            var results = await MultiplayerService.Instance.QuerySessionsAsync(query);
            return results.Sessions;
        }

        /// <summary>Host only: mirror match phase into the session so browsers can show it.</summary>
        public static async Task PublishPhaseAsync(string phase, bool lockJoins, string mapId = null)
        {
            if (Current == null || !Current.IsHost) return;
            try
            {
                var host = Current.AsHost();
                host.SetProperty(BuildInfo.PropPhase, new SessionProperty(phase, VisibilityPropertyOptions.Public, PropertyIndex.String3));
                if (!string.IsNullOrEmpty(mapId))
                    host.SetProperty(BuildInfo.PropMap, new SessionProperty(mapId, VisibilityPropertyOptions.Public, PropertyIndex.String2));
                host.IsLocked = lockJoins;
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SALTLINE] Could not publish session phase: {Describe(e)}");
            }
        }

        public static async Task LeaveAsync()
        {
            var s = Current;
            if (s == null) return;
            Detach(s);
            Current = null;
            try { await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning($"[SALTLINE] Leave session: {e.Message}"); }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
            ConnectionGate.Clear();
        }

        static void Attach(ISession s)
        {
            Current = s;
            s.RemovedFromSession += OnRemoved;
            s.Deleted += OnDeleted;
            s.SessionHostChanged += OnHostChanged;
            SessionStarted?.Invoke(s);
        }

        static void Detach(ISession s)
        {
            s.RemovedFromSession -= OnRemoved;
            s.Deleted -= OnDeleted;
            s.SessionHostChanged -= OnHostChanged;
        }

        static void OnRemoved() => Lose("You were removed from the session.");
        static void OnDeleted() => Lose("The host closed the session.");

        // The lobby service may promote another member when the host vanishes. The game's
        // authoritative simulation lived on the old host, so the match cannot continue.
        static void OnHostChanged(string newHostId)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer) return; // we are still the network host
            Lose("The host left the match.");
        }

        /// <summary>Called by connection code when the transport drops (host left, timeout).</summary>
        public static void NotifyNetworkLost(string reason)
        {
            if (Current == null) return;
            Lose(reason);
        }

        static void Lose(string reason)
        {
            var s = Current;
            if (s == null) return;
            Detach(s);
            Current = null;
            _ = SafeLeave(s);
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
            ConnectionGate.Clear();
            Debug.Log($"[SALTLINE] Session lost: {reason}");
            SessionLost?.Invoke(reason);
        }

        static async Task SafeLeave(ISession s)
        {
            try { await s.LeaveAsync(); } catch { /* session may already be gone */ }
        }

        static NetworkManager RequireNetworkManager()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) throw new InvalidOperationException("NetworkManager missing from the Boot scene.");
            return nm;
        }

        public static string Describe(Exception e)
        {
            if (e is AggregateException ag && ag.InnerException != null) e = ag.InnerException;
            if (e is SessionException se)
            {
                switch (se.Error)
                {
                    case SessionError.SessionNotFound: return "No match found with that code. It may have ended.";
                    case SessionError.SessionDeleted: return "That match has ended.";
                    case SessionError.Forbidden: return "You can't join that match (full, locked or wrong password).";
                    case SessionError.NotAuthorized: return "Not signed in to Unity services. Restart and try again.";
                    case SessionError.RateLimitExceeded: return "Too many requests. Wait a few seconds and retry.";
                    case SessionError.InvalidParameter: return "Invalid match settings or code.";
                    case SessionError.NetworkManagerStartFailed:
                    case SessionError.NetworkSetupFailed: return "Couldn't start the network connection through Relay.";
                    case SessionError.QoSMeasurementFailed: return "Couldn't reach any Relay region. Check your connection.";
                    default:
                        string m = se.Message ?? "";
                        if (m.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return "That match is full (8/8).";
                        if (m.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0) return "That match is in progress and locked.";
                        if (m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0) return "Wrong match password.";
                        return $"Session error: {se.Error} — {m}";
                }
            }
            if (e is ArgumentException || e is UpdateRequiredException) return e.Message;
            return UgsAuth.Describe(e);
        }
    }
}
