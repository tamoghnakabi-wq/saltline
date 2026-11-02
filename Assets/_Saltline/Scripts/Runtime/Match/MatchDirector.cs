using System;
using Saltline.Combat;
using Saltline.Core;
using Saltline.Net;
using Saltline.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saltline.Match
{
    /// <summary>
    /// Team Deathmatch flow, run by the host and mirrored to every client:
    /// Lobby → Loading → Warmup → Live → Results → Lobby.
    ///
    /// Lives on the persistent session object, so it survives scene changes. Only the host
    /// changes the phase, scores and timers; clients read the replicated values. The host is a
    /// player too, so this is not protection against a dishonest host.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchDirector : NetworkBehaviour
    {
        public const string LobbyScene = "Menu";

        public static MatchDirector Instance { get; private set; }
        /// <summary>(previous, current) on every machine, host included.</summary>
        public static event Action<MatchPhase, MatchPhase> PhaseChanged;
        public static event Action<MatchDirector> Spawned;

        public readonly NetworkVariable<MatchPhase> Phase = new(MatchPhase.Lobby);
        /// <summary>Server time at which the current phase ends; 0 when the phase has no timer.</summary>
        public readonly NetworkVariable<double> PhaseEndsAt = new(0);
        public readonly NetworkVariable<short> RedScore = new(0);
        public readonly NetworkVariable<short> BlueScore = new(0);
        public readonly NetworkVariable<Team> Winner = new(Team.None);
        public readonly NetworkVariable<MatchEndReason> EndReason = new(MatchEndReason.None);
        public readonly NetworkVariable<FixedString32Bytes> MapId = new("arena");
        public readonly NetworkVariable<MatchRules> Rules = new(MatchRules.Default);
        /// <summary>Increments each time a match goes live (lets UIs tell matches apart).</summary>
        public readonly NetworkVariable<int> MatchNumber = new(0);

        string m_LoadingScene;
        double m_LoadStarted;
        GameSession m_Session;

        const double LoadingTimeout = 90.0;

        public bool InMatch => Phase.Value is MatchPhase.Warmup or MatchPhase.Live or MatchPhase.Results;
        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        /// <summary>Seconds left in the current phase (0 if it has no timer).</summary>
        public float SecondsLeft => PhaseEndsAt.Value <= 0 ? 0f : Mathf.Max(0f, (float)(PhaseEndsAt.Value - Now));

        public short ScoreOf(Team t) => t == Team.Red ? RedScore.Value : t == Team.Blue ? BlueScore.Value : (short)0;

        // ================================================================== lifecycle

        void Awake()
        {
            m_Session = GetComponent<GameSession>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Phase.OnValueChanged += OnPhaseValueChanged;
            NetPlayer.InputLocked = () => Phase.Value is MatchPhase.Loading or MatchPhase.Results;

            if (IsServer)
            {
                Rules.Value = MatchRules.FromCommandLine();
                MapId.Value = MapCatalog.DefaultId();
                NetPlayer.ServerKilled += OnServerKilled;
                NetPlayer.CanRespawn = _ => Phase.Value is MatchPhase.Lobby or MatchPhase.Warmup or MatchPhase.Live;
                NetPlayer.DamageEnabled = () => Phase.Value is MatchPhase.Lobby or MatchPhase.Warmup or MatchPhase.Live;
                PlayerSpawner.CanSpawn = () => Phase.Value is MatchPhase.Lobby or MatchPhase.Warmup or MatchPhase.Live;
                if (m_Session != null) m_Session.LobbyEditable = () => Phase.Value == MatchPhase.Lobby;
                if (NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            }
            Spawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnPhaseValueChanged;
            NetPlayer.InputLocked = () => false;
            if (IsServer)
            {
                NetPlayer.ServerKilled -= OnServerKilled;
                NetPlayer.CanRespawn = _ => true;
                NetPlayer.DamageEnabled = () => true;
                PlayerSpawner.CanSpawn = () => true;
                if (NetworkManager != null && NetworkManager.SceneManager != null)
                    NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            }
            if (Instance == this) Instance = null;
        }

        void OnPhaseValueChanged(MatchPhase previous, MatchPhase current)
        {
            Debug.Log($"[SALTLINE] Match phase {previous} -> {current}");
            PhaseChanged?.Invoke(previous, current);
        }

        // ================================================================== host: lobby

        /// <summary>Host: may the match start now? Every other player must be ready.</summary>
        public bool CanStart(out string reason)
        {
            reason = null;
            if (!IsServer) { reason = "Only the host can start the match."; return false; }
            if (Phase.Value != MatchPhase.Lobby) { reason = "A match is already running."; return false; }
            if (m_Session == null || m_Session.Roster.Count == 0) { reason = "No players."; return false; }
            if (!MapCatalog.TryGet(MapId.Value.ToString(), out var map) || !Application.CanStreamedLevelBeLoaded(map.Scene))
            { reason = "The selected map is not in this build."; return false; }

            int notReady = 0;
            foreach (var r in m_Session.Roster)
            {
                if (r.Team == Team.None) { reason = $"{r.Name} has no team."; return false; }
                if (!r.IsHost && !r.Ready) notReady++;
            }
            if (m_Session.CountTeam(Team.Red) > BuildInfo.MaxPerTeam || m_Session.CountTeam(Team.Blue) > BuildInfo.MaxPerTeam)
            { reason = $"A team has more than {BuildInfo.MaxPerTeam} players."; return false; }
            if (notReady > 0) { reason = notReady == 1 ? "1 player is not ready." : $"{notReady} players are not ready."; return false; }
            return true;
        }

        /// <summary>Host: choose the map (lobby only).</summary>
        public void ServerSetMap(string mapId)
        {
            if (!IsServer || Phase.Value != MatchPhase.Lobby) return;
            if (!MapCatalog.TryGet(mapId, out var m) || MapId.Value.ToString() == m.Id) return;
            MapId.Value = m.Id;
            _ = SessionService.PublishPhaseAsync(Phase.Value.ToString(), false, m.Id);
        }

        /// <summary>Host: start the match on the selected map. Returns false with a reason if it cannot.</summary>
        public bool ServerStartMatch(out string reason)
        {
            if (!CanStart(out reason)) return false;
            MapCatalog.TryGet(MapId.Value.ToString(), out var map);

            SetPhase(MatchPhase.Loading, 0);
            m_LoadingScene = map.Scene;
            m_LoadStarted = Time.realtimeSinceStartupAsDouble;
            var status = NetworkManager.SceneManager.LoadScene(map.Scene, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                reason = $"Could not load {map.Name} ({status}).";
                SetPhase(MatchPhase.Lobby, 0);
                return false;
            }
            Debug.Log($"[SALTLINE] Match starting on {map.Name} ({map.Scene}); rules {Rules.Value.TimeLimit:F0}s / {Rules.Value.ScoreLimit} kills");
            return true;
        }

        void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> done, System.Collections.Generic.List<ulong> timedOut)
        {
            if (!IsServer || Phase.Value != MatchPhase.Loading || sceneName != m_LoadingScene) return;
            if (timedOut != null && timedOut.Count > 0) Debug.LogWarning($"[SALTLINE] {timedOut.Count} client(s) timed out loading {sceneName}.");
            BeginWarmup();
        }

        // ================================================================== host: match

        void BeginWarmup()
        {
            RedScore.Value = 0;
            BlueScore.Value = 0;
            Winner.Value = Team.None;
            EndReason.Value = MatchEndReason.None;
            if (Rules.Value.Warmup <= 0f) { BeginLive(); return; }
            SetPhase(MatchPhase.Warmup, Now + Rules.Value.Warmup);
            PlayerSpawner.SpawnMissing();
        }

        void BeginLive()
        {
            // Warmup kills do not count: everyone starts the match at zero, freshly spawned.
            if (m_Session != null)
            {
                for (int i = 0; i < m_Session.Roster.Count; i++)
                {
                    var r = m_Session.Roster[i];
                    r.Kills = 0; r.Deaths = 0; r.Assists = 0; r.Score = 0; r.DamageDealt = 0; r.Ready = false;
                    m_Session.Roster[i] = r;
                }
            }
            RedScore.Value = 0;
            BlueScore.Value = 0;
            MatchNumber.Value++;
            SetPhase(MatchPhase.Live, Now + Rules.Value.TimeLimit);

            PlayerSpawner.SpawnMissing();
            foreach (var p in NetPlayer.All.ToArray())
            {
                if (p == null || !p.IsSpawned) continue;
                if (m_Session != null && m_Session.TryGet(p.OwnerClientId, out var rec) && rec.Team != Team.None) p.TeamState.Value = rec.Team;
                p.ServerRespawn();
            }
        }

        void OnServerKilled(KillEvent ev, NetPlayer killer, NetPlayer victim)
        {
            if (Phase.Value != MatchPhase.Live || killer == null || victim == null || killer == victim) return;
            Team kt = killer.TeamState.Value, vt = victim.TeamState.Value;
            if (kt == Team.None || kt == vt) return; // no team point for suicides, falls or team kills
            if (kt == Team.Red) RedScore.Value++;
            else BlueScore.Value++;
            if (ScoreOf(kt) >= Rules.Value.ScoreLimit) EndMatch(kt, MatchEndReason.ScoreLimit);
        }

        void EndMatch(Team winner, MatchEndReason reason)
        {
            if (Phase.Value != MatchPhase.Live && Phase.Value != MatchPhase.Warmup) return;
            Winner.Value = winner;
            EndReason.Value = reason;
            SetPhase(MatchPhase.Results, Now + Rules.Value.Results);
            Debug.Log($"[SALTLINE] Match over: {(winner == Team.None ? "draw" : winner + " wins")} {RedScore.Value}-{BlueScore.Value} ({reason})");
        }

        /// <summary>Host: end the current match early (counts as a result by current score).</summary>
        public void ServerEndMatchNow()
        {
            if (!IsServer) return;
            if (Phase.Value == MatchPhase.Live) EndMatch(Leader(), MatchEndReason.HostEnded);
            else if (Phase.Value is MatchPhase.Warmup or MatchPhase.Loading) ServerReturnToLobby();
        }

        Team Leader() => RedScore.Value > BlueScore.Value ? Team.Red : BlueScore.Value > RedScore.Value ? Team.Blue : Team.None;

        /// <summary>Host: back to the lobby scene with everyone; ready flags reset.</summary>
        public void ServerReturnToLobby()
        {
            if (!IsServer) return;
            if (m_Session != null)
            {
                for (int i = 0; i < m_Session.Roster.Count; i++)
                {
                    var r = m_Session.Roster[i];
                    if (r.Ready) { r.Ready = false; m_Session.Roster[i] = r; }
                }
            }
            SetPhase(MatchPhase.Lobby, 0);
            if (Application.CanStreamedLevelBeLoaded(LobbyScene) && SceneManager.GetActiveScene().name != LobbyScene)
            {
                var status = NetworkManager.SceneManager.LoadScene(LobbyScene, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started) Debug.LogWarning($"[SALTLINE] Could not load lobby scene: {status}");
            }
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            switch (Phase.Value)
            {
                case MatchPhase.Loading:
                    if (Time.realtimeSinceStartupAsDouble - m_LoadStarted > LoadingTimeout && SceneManager.GetActiveScene().name == m_LoadingScene)
                        BeginWarmup();
                    break;
                case MatchPhase.Warmup:
                    if (Now >= PhaseEndsAt.Value) BeginLive();
                    break;
                case MatchPhase.Live:
                    if (Now >= PhaseEndsAt.Value) EndMatch(Leader(), MatchEndReason.TimeLimit);
                    break;
                case MatchPhase.Results:
                    if (Now >= PhaseEndsAt.Value) ServerReturnToLobby();
                    break;
            }
        }

        void SetPhase(MatchPhase phase, double endsAt)
        {
            PhaseEndsAt.Value = endsAt;
            Phase.Value = phase;
            _ = SessionService.PublishPhaseAsync(phase.ToString(), false, MapId.Value.ToString());
        }
    }
}
