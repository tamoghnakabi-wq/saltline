using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Headless Team Deathmatch flow test over real Unity Relay:
    ///
    ///   Host:   -autotest match-host   -codefile f -warmup 5 -scorelimit 3 -resultstime 5 -spawnprotect 0.5 -respawndelay 1
    ///   Client: -autotest match-client -codefile f
    ///
    /// With -scenario draw (host) the session is public and the match runs to its time limit:
    ///   Host:   ... -scenario draw -latefile g -timelimit 60
    ///   Client: ... -expectwinner none
    ///   Late:   -autotest match-late -codefile g   (finds the match by browsing, joins mid-match)
    /// Checks: public browse + join by id, late join spawns into the live match on the smaller
    /// team, an assist is credited, a team kill scores nothing, the late player leaving mid-match
    /// does not disturb the match, and equal scores at the time limit end in a draw.
    ///
    /// Lobby rules (start refused until the client is ready, team switching), then a full match:
    /// Loading → Warmup → Live → Results → Lobby. Kills are applied on the host through the
    /// normal damage path (ServerApplyDamage); aiming and hit registration are covered by the
    /// combat test, not here. Checks: warmup kills and suicides score nothing, team changes are
    /// refused mid-match, the score limit ends the match with the right winner, damage is off
    /// on the results screen, and everyone returns to the lobby scene still connected.
    /// </summary>
    public sealed class MatchAutoTest : AutoTestBase
    {
        const int ClientDone = 99;
        bool m_ClientDone;
        readonly List<MatchPhase> m_Seen = new();

        protected override Task<bool> Run() => Role.Contains("host") ? RunHost() : Role.Contains("late") ? RunLate() : RunClient();

        bool Draw => CommandLine.Get("scenario", "") == "draw";

        static PlayerRecord Rec(ulong id)
        {
            PlayerRecord r = default;
            if (GameSession.Instance != null) GameSession.Instance.TryGet(id, out r);
            return r;
        }

        static ulong FirstGuest()
        {
            foreach (var r in GameSession.Instance.Roster) if (!r.IsHost) return r.ClientId;
            return ulong.MaxValue;
        }

        // ================================================================== host

        async Task<bool> RunHost()
        {
            await UgsAuth.EnsureSignedInAsync();
            if (File.Exists(CodeFile)) File.Delete(CodeFile);
            var session = await SessionService.HostAsync(new HostSettings
            {
                PlayerName = "MatchHost",
                SessionName = $"match-{DateTime.UtcNow:HHmmss}-{UnityEngine.Random.Range(100, 999)}",
                IsPrivate = !Draw,
                MapId = "arena",
            });
            var nm = NetworkManager.Singleton;
            if (!await WaitUntil(() => nm.IsHost && GameSession.Instance != null && MatchDirector.Instance != null && NetDiagnostics.Instance != null, 30))
                return Fail("host network or match director did not start");
            var utp = nm.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport;
            string proto = utp != null ? utp.Protocol.ToString() : "n/a";
            Result["transportProtocol"] = proto;
            if (proto != "RelayUnityTransport") return Fail($"expected Relay transport, got {proto}");

            var md = MatchDirector.Instance;
            md.ServerSetMap("arena");
            var rules = md.Rules.Value;
            Result["rules"] = $"{rules.TimeLimit:F0}s/{rules.ScoreLimit} kills/warmup {rules.Warmup:F0}s/results {rules.Results:F0}s";
            NetDiagnostics.Instance.ClientPhase += (id, phase, value) => { if (phase == ClientDone) m_ClientDone = true; };

            WriteCode(session.Code);
            Note($"host: join code {session.Code} written");
            if (!await WaitUntil(() => GameSession.Instance.Roster.Count >= 2, Timeout)) return Fail("client never joined");
            ulong cid = FirstGuest();

            // Lobby: the host cannot start while the client is not ready.
            bool blocked = !md.CanStart(out string why);
            Result["startBlockedWhileNotReady"] = blocked.ToString();
            Note($"host: start before ready -> blocked={blocked} ({why})");
            if (!blocked) return Fail("match could start before the client was ready");

            bool sawRed = false;
            if (!await WaitUntil(() =>
                {
                    var r = Rec(cid);
                    sawRed |= r.Team == Team.Red;
                    return sawRed && r.Team == Team.Blue && r.Ready;
                }, 30))
                return Fail($"client did not switch Red then Blue and ready (sawRed={sawRed}, now {Rec(cid).Team}/{Rec(cid).Ready})");
            Result["teamSwitchSeen"] = "True";

            if (!md.ServerStartMatch(out why)) return Fail("start refused: " + why);
            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Warmup && NetPlayer.All.Count >= 2 && NetPlayer.All.All(p => p.AliveState.Value), 60))
                return Fail($"warmup did not begin with both players spawned (phase {md.Phase.Value}, players {NetPlayer.All.Count})");
            Result["matchScene"] = SceneManager.GetActiveScene().name;
            var me = NetPlayer.Local;
            var them = NetPlayer.All.First(p => p != me);

            // Warmup kills do not count.
            if (!await Kill(me, them)) return Fail("warmup kill failed");
            Result["warmupKillScored"] = (md.RedScore.Value != 0).ToString();
            if (md.RedScore.Value != 0) return Fail("a warmup kill changed the team score");

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Live, rules.Warmup + 15)) return Fail("match did not go live");
            await WaitSeconds(0.5f);
            if (Rec(cid).Deaths != 0 || md.MatchNumber.Value != 1) return Fail("records were not reset when the match went live");

            if (Draw) { if (!await DrawScenario(md, session, me, them, cid)) return false; }
            else if (!await ScoreLimitScenario(md, rules, me, them)) return false;
            return await AfterMatch(md, rules, me, them, cid);
        }

        async Task<bool> ScoreLimitScenario(MatchDirector md, MatchRules rules, NetPlayer me, NetPlayer them)
        {
            // A suicide scores nothing.
            if (!await Kill(me, me)) return Fail("suicide failed");
            Result["suicideScored"] = (md.RedScore.Value + md.BlueScore.Value != 0).ToString();
            if (md.RedScore.Value + md.BlueScore.Value != 0) return Fail("a suicide changed a team score");

            // One kill for Blue, then Red to the score limit.
            if (!await Kill(them, me)) return Fail("client kill failed");
            if (md.BlueScore.Value != 1) return Fail($"blue score {md.BlueScore.Value} after one kill");
            for (int i = 0; i < rules.ScoreLimit; i++)
                if (!await Kill(me, them)) return Fail($"host kill {i + 1} failed");

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Results, 5)) return Fail("score limit did not end the match");
            if (md.Winner.Value != Team.Red || md.EndReason.Value != MatchEndReason.ScoreLimit) return Fail($"wrong winner or end reason: {md.Winner.Value} {md.EndReason.Value}");
            return true;
        }

        async Task<bool> DrawScenario(MatchDirector md, Unity.Services.Multiplayer.ISession session, NetPlayer me, NetPlayer them, ulong cid)
        {
            // A third player finds the public match by browsing and joins while it is live.
            string late = CommandLine.Get("latefile");
            File.WriteAllText(late + ".tmp", session.Name);
            if (File.Exists(late)) File.Delete(late);
            File.Move(late + ".tmp", late);
            Note($"host: live; waiting for a late joiner to find '{session.Name}'");
            if (!await WaitUntil(() => GameSession.Instance.Roster.Count == 3 && NetPlayer.All.Count == 3 && NetPlayer.All.All(p => p.AliveState.Value), 60))
                return Fail($"late joiner did not spawn into the live match (roster {GameSession.Instance.Roster.Count}, players {NetPlayer.All.Count})");
            var lateP = NetPlayer.All.First(p => p != me && p != them);
            ulong lid = lateP.OwnerClientId;
            Result["lateJoinTeam"] = Rec(lid).Team.ToString();
            Result["lateJoinPhase"] = md.Phase.Value.ToString();
            if (Rec(lid).Team != Team.Red || lateP.TeamState.Value != Team.Red) return Fail("late joiner was not put on the smaller-or-equal team (Red)");

            // Assist: the late joiner (Red) wounds the client, the host finishes it.
            if (!await WaitUntil(() => them.AliveState.Value && !them.Protected.Value, 20)) return Fail("client never vulnerable");
            them.ServerApplyDamage(lateP, 50, 0, false, false, lateP.SimMotor.Position);
            if (!await Kill(me, them)) return Fail("assisted kill failed");
            await WaitSeconds(0.3f);
            Result["lateAssists"] = Rec(lid).Assists.ToString();
            Result["lateScore"] = Rec(lid).Score.ToString();
            if (Rec(lid).Assists != 1 || md.RedScore.Value != 1) return Fail($"assist not credited (assists {Rec(lid).Assists}, red {md.RedScore.Value})");

            if (!await Kill(them, me)) return Fail("client kill failed");
            if (md.BlueScore.Value != 1) return Fail("blue kill not scored");

            // A team kill scores nothing and credits nothing.
            int hostKills = Rec(me.OwnerClientId).Kills;
            if (!await Kill(me, lateP)) return Fail("team kill failed");
            await WaitSeconds(0.3f);
            Result["teamKillScored"] = (md.RedScore.Value != 1 || Rec(me.OwnerClientId).Kills != hostKills).ToString();
            if (md.RedScore.Value != 1 || Rec(me.OwnerClientId).Kills != hostKills) return Fail("a team kill changed the score or the killer's record");

            // The late joiner leaves mid-match; the match carries on.
            if (!await WaitUntil(() => GameSession.Instance.Roster.Count == 2 && NetPlayer.All.Count == 2, 30)) return Fail("late joiner's departure was not handled");
            Result["midMatchLeaveHandled"] = (md.Phase.Value == MatchPhase.Live).ToString();
            if (md.Phase.Value != MatchPhase.Live) return Fail("a player leaving ended the match");

            float left = md.SecondsLeft;
            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Results, left + 5)) return Fail("time limit did not end the match");
            if (md.Winner.Value != Team.None || md.EndReason.Value != MatchEndReason.TimeLimit) return Fail($"expected a draw at the time limit, got {md.Winner.Value} {md.EndReason.Value}");
            return true;
        }

        async Task<bool> AfterMatch(MatchDirector md, MatchRules rules, NetPlayer me, NetPlayer them, ulong cid)
        {
            Result["winner"] = md.Winner.Value.ToString();
            Result["endReason"] = md.EndReason.Value.ToString();
            Result["finalScore"] = $"{md.RedScore.Value}-{md.BlueScore.Value}";
            Result["clientRecord"] = $"K{Rec(cid).Kills}/D{Rec(cid).Deaths}";
            Result["hostRecord"] = $"K{Rec(me.OwnerClientId).Kills}/D{Rec(me.OwnerClientId).Deaths}";
            Note($"host: results {Result["finalScore"]} winner {Result["winner"]} ({Result["endReason"]})");

            int hpBefore = me.Health.Value;
            me.ServerApplyDamage(them, 50, 0, false, false, them.SimMotor.Position);
            Result["damageInResults"] = (me.Health.Value != hpBefore).ToString();
            if (me.Health.Value != hpBefore) return Fail("damage applied on the results screen");

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Lobby && SceneManager.GetActiveScene().name == MatchDirector.LobbyScene && NetPlayer.All.Count == 0, rules.Results + 30))
                return Fail($"did not return to the lobby (phase {md.Phase.Value}, scene {SceneManager.GetActiveScene().name}, players {NetPlayer.All.Count})");
            Result["rosterAfterLobby"] = GameSession.Instance.Roster.Count.ToString();
            if (GameSession.Instance.Roster.Count != 2 || Rec(cid).Ready) return Fail("roster lost a player or ready flags were not cleared");

            if (!await WaitUntil(() => m_ClientDone, 30)) return Fail("client did not report completion");
            return true;
        }

        /// <summary>Host: kill <paramref name="victim"/> through the normal damage path once it can be hurt.</summary>
        async Task<bool> Kill(NetPlayer attacker, NetPlayer victim)
        {
            if (!await WaitUntil(() => victim.AliveState.Value && !victim.Protected.Value, 20)) return false;
            victim.ServerApplyDamage(attacker, 500, 0, false, false, attacker.SimMotor.Position);
            return await WaitUntil(() => !victim.AliveState.Value, 2);
        }

        // ================================================================== client

        async Task<bool> RunClient()
        {
            await UgsAuth.EnsureSignedInAsync();
            string code = await ReadCode();
            if (string.IsNullOrEmpty(code)) return Fail("no join code");
            MatchDirector.PhaseChanged += (_, cur) => m_Seen.Add(cur);
            await SessionService.JoinByCodeAsync(code, "MatchClient");
            var nm = NetworkManager.Singleton;
            if (!await WaitUntil(() => nm.IsConnectedClient && GameSession.Instance != null && MatchDirector.Instance != null && GameSession.Instance.IndexOf(nm.LocalClientId) >= 0, 30))
                return Fail("did not connect");
            var gs = GameSession.Instance;
            var md = MatchDirector.Instance;
            ulong me = nm.LocalClientId;

            await WaitSeconds(2f); // give the host time to check that it cannot start yet
            gs.RequestTeamRpc(Team.Red);
            if (!await WaitUntil(() => Rec(me).Team == Team.Red, 10)) return Fail("switch to Red was refused");
            gs.RequestTeamRpc(Team.Blue);
            if (!await WaitUntil(() => Rec(me).Team == Team.Blue, 10)) return Fail("switch to Blue was refused");
            gs.SetReadyRpc(true);

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Live, 90)) return Fail("match never went live");
            gs.RequestTeamRpc(Team.Red);
            await WaitSeconds(1.5f);
            Result["midMatchTeamChange"] = (Rec(me).Team != Team.Blue).ToString();
            if (Rec(me).Team != Team.Blue) return Fail("team change was accepted during a live match");

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Results, 90)) return Fail("never saw results");
            await WaitSeconds(0.3f);
            Result["winner"] = md.Winner.Value.ToString();
            Result["finalScore"] = $"{md.RedScore.Value}-{md.BlueScore.Value}";
            Result["inputLockedInResults"] = NetPlayer.InputLocked().ToString();
            var expect = CommandLine.Get("expectwinner", "red").ToLowerInvariant() switch { "none" => Team.None, "blue" => Team.Blue, _ => Team.Red };
            if (md.Winner.Value != expect || !NetPlayer.InputLocked()) return Fail($"results not replicated correctly (winner {md.Winner.Value}, expected {expect})");

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Lobby && SceneManager.GetActiveScene().name == MatchDirector.LobbyScene, 60))
                return Fail("did not return to the lobby");
            Result["phases"] = string.Join(">", m_Seen);
            int w = m_Seen.IndexOf(MatchPhase.Warmup), l = m_Seen.IndexOf(MatchPhase.Live), r = m_Seen.IndexOf(MatchPhase.Results), b = m_Seen.LastIndexOf(MatchPhase.Lobby);
            if (!(w >= 0 && w < l && l < r && r < b)) return Fail("phase order wrong: " + Result["phases"]);
            NetDiagnostics.Instance?.ClientPhaseRpc(ClientDone, 0f);
            await WaitSeconds(1f);
            return true;
        }

        // ================================================================== late joiner

        async Task<bool> RunLate()
        {
            await UgsAuth.EnsureSignedInAsync();
            string name = await ReadCode(); // the host writes its public session name once the match is live
            if (string.IsNullOrEmpty(name)) return Fail("no session name");
            Unity.Services.Multiplayer.ISessionInfo found = null;
            int queries = 0;
            float end = Time.realtimeSinceStartup + 40f;
            while (found == null && Time.realtimeSinceStartup < end)
            {
                queries++;
                var list = await SessionService.BrowseAsync();
                found = list.FirstOrDefault(i => i.Name == name);
                if (found == null) await WaitSeconds(2.5f);
            }
            Result["browseQueries"] = queries.ToString();
            if (found == null) return Fail($"public match '{name}' not found by browsing");
            if (found.Properties != null && found.Properties.TryGetValue(Saltline.Core.BuildInfo.PropPhase, out var ph)) Result["browsePhase"] = ph.Value;
            Result["browseSlots"] = $"{found.AvailableSlots}/{found.MaxPlayers}";
            Note($"late: found '{name}' after {queries} queries (phase {Result.GetValueOrDefault("browsePhase", "?")})");

            await SessionService.JoinByIdAsync(found.Id, "LateJoiner");
            var nm = NetworkManager.Singleton;
            if (!await WaitUntil(() => nm.IsConnectedClient && MatchDirector.Instance != null && NetPlayer.Local != null && NetPlayer.Local.AliveState.Value, 30))
                return Fail("did not spawn after joining by id");
            var md = MatchDirector.Instance;
            ulong me = nm.LocalClientId;
            Result["phaseOnJoin"] = md.Phase.Value.ToString();
            Result["team"] = Rec(me).Team.ToString();
            if (md.Phase.Value != MatchPhase.Live) return Fail("joined but the match was not live");
            if (!await WaitUntil(() => Rec(me).Assists >= 1 && Rec(me).Deaths >= 1, 60)) return Fail("assist and death were not replicated to the late joiner");
            Result["record"] = $"K{Rec(me).Kills}/D{Rec(me).Deaths}/A{Rec(me).Assists} score {Rec(me).Score}";
            return true; // leaving (in Finish) is part of the test: the host checks the match carries on
        }
    }
}
