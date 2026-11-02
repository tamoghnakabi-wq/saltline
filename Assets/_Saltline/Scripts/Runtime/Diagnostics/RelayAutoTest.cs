using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Saltline.Core;
using Saltline.Net;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Headless end-to-end multiplayer test over the real Unity Relay service.
    ///
    ///   Host:   -autotest host   -codefile path [-clients N] [-testout file] [-profile p]
    ///   Client: -autotest client -codefile path [-expectreject] [-testout file] [-profile p]
    ///
    /// The host creates a private session (Relay network), publishes the join code to a file,
    /// waits for N clients to connect and report, then validates each report. A client joins by
    /// code, waits until the host's roster lists it and the host's sync token replicated, runs a
    /// ping series over RPC, reports, and waits for the host's verdict. Exit code 0 = pass.
    /// </summary>
    public class RelayAutoTest : MonoBehaviour
    {
        string m_Role;
        string m_CodeFile;
        string m_OutFile;
        float m_Timeout;
        readonly StringBuilder m_Log = new();
        readonly Dictionary<string, string> m_Result = new();

        async void Start()
        {
            m_Role = CommandLine.Get("autotest", "host").ToLowerInvariant();
            m_CodeFile = CommandLine.Get("codefile", Path.Combine(Application.temporaryCachePath, "saltline_code.txt"));
            m_OutFile = CommandLine.Get("testout");
            m_Timeout = CommandLine.GetFloat("testtimeout", 90f);
            m_Result["role"] = m_Role;
            m_Result["platform"] = PlayerRecord.LocalPlatform().ToString();
            m_Result["protocol"] = BuildInfo.NetProtocol.ToString();
            m_Result["unity"] = Application.unityVersion;
            m_Result["netsim"] = NetSimBridge.ActiveDescription;

            bool ok;
            try
            {
                ok = m_Role == "host" ? await RunHost() : await RunClient();
            }
            catch (Exception e)
            {
                Note($"EXCEPTION: {SessionService.Describe(e)} :: {e}");
                ok = false;
            }
            await Finish(ok);
        }

        // ------------------------------------------------------------------ host

        async Task<bool> RunHost()
        {
            int expected = Mathf.Max(1, CommandLine.GetInt("clients", 1));
            Note($"host: signing in, expecting {expected} client(s)");
            await UgsAuth.EnsureSignedInAsync();

            if (File.Exists(m_CodeFile)) File.Delete(m_CodeFile);
            var session = await SessionService.HostAsync(new HostSettings
            {
                PlayerName = "AutoHost",
                SessionName = $"autotest-{DateTime.UtcNow:HHmmss}",
                IsPrivate = true,
                MapId = "kappa",
            });
            m_Result["sessionId"] = session.Id;
            m_Result["joinCode"] = session.Code;

            var nm = NetworkManager.Singleton;
            if (!await WaitUntil(() => nm.IsHost && GameSession.Instance != null && NetDiagnostics.Instance != null, 30))
                return Fail("host network did not start");
            var utp = nm.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport;
            string proto = utp != null ? utp.Protocol.ToString() : "n/a";
            Note($"host: network up, transport={nm.NetworkConfig.NetworkTransport.GetType().Name} protocol={proto}, tick={nm.NetworkConfig.TickRate}");
            m_Result["transportProtocol"] = proto;
            if (proto != "RelayUnityTransport") return Fail($"expected Relay transport, got {proto}");

            var reports = new Dictionary<ulong, NetDiagnostics.TestReport>();
            NetDiagnostics.Instance.ReportReceived += (id, r) => reports[id] = r;

            string tmp = m_CodeFile + ".tmp";
            File.WriteAllText(tmp, session.Code);
            if (File.Exists(m_CodeFile)) File.Delete(m_CodeFile);
            File.Move(tmp, m_CodeFile);
            Note($"host: join code {session.Code} written");

            if (!await WaitUntil(() => reports.Count >= expected, m_Timeout))
                return Fail($"only {reports.Count}/{expected} client report(s) arrived; roster={GameSession.Instance.Roster.Count}");

            uint token = GameSession.Instance.SyncToken.Value;
            bool allOk = true;
            int i = 0;
            foreach (var kv in reports)
            {
                var r = kv.Value;
                bool tokenOk = r.TokenEcho == token;
                bool pingOk = r.PongsReceived >= r.PingsSent / 2 && r.PongsReceived > 0;
                bool rosterOk = r.RosterCountSeen >= 2;
                bool inRoster = GameSession.Instance.TryGet(kv.Key, out var rec);
                bool clientOk = tokenOk && pingOk && rosterOk && inRoster;
                allOk &= clientOk;
                string plat = ((ClientPlatform)r.Platform).ToString();
                Note($"host: client {kv.Key} [{plat}] name={rec.Name} team={rec.Team} token={(tokenOk ? "match" : "MISMATCH")} " +
                     $"pongs={r.PongsReceived}/{r.PingsSent} rtt avg={r.RttAvgMs:F1} min={r.RttMinMs:F1} max={r.RttMaxMs:F1}ms " +
                     $"rosterSeen={r.RosterCountSeen} => {(clientOk ? "OK" : "FAIL")}");
                m_Result[$"client{i}"] = $"{plat},rttAvg={r.RttAvgMs:F1},pongs={r.PongsReceived}/{r.PingsSent},ok={clientOk}";
                NetDiagnostics.Instance.SendAck(kv.Key, clientOk, clientOk ? "verified" : "validation failed");
                i++;
            }

            int red = GameSession.Instance.CountTeam(Team.Red), blue = GameSession.Instance.CountTeam(Team.Blue);
            Note($"host: roster {GameSession.Instance.Roster.Count} players, red={red} blue={blue}");
            m_Result["roster"] = GameSession.Instance.Roster.Count.ToString();
            m_Result["teams"] = $"{red}v{blue}";

            if (CommandLine.Has("expectfull"))
            {
                // A later client is expected to be refused; give it time to try.
                await WaitSeconds(CommandLine.GetFloat("holdopen", 25f));
                Note($"host: roster after hold {GameSession.Instance.Roster.Count} (must be <= {BuildInfo.MaxPlayers})");
                allOk &= GameSession.Instance.Roster.Count <= BuildInfo.MaxPlayers;
            }

            await WaitSeconds(2f); // let acks flush before shutting the host down
            if (CommandLine.Has("idleafter")) await WaitSeconds(CommandLine.GetFloat("idleafter", 60f));
            return allOk;
        }

        // ---------------------------------------------------------------- client

        async Task<bool> RunClient()
        {
            bool expectReject = CommandLine.Has("expectreject");
            Note("client: signing in");
            await UgsAuth.EnsureSignedInAsync();

            Note($"client: waiting for join code at {m_CodeFile}");
            string code = null;
            if (!await WaitUntil(() =>
                {
                    if (!File.Exists(m_CodeFile)) return false;
                    try { code = File.ReadAllText(m_CodeFile).Trim(); } catch { return false; }
                    return code.Length > 0;
                }, m_Timeout))
                return Fail("join code never appeared");

            float joinStart = Time.realtimeSinceStartup;
            string lost = null;
            SessionService.SessionLost += r => lost = r;
            try
            {
                await SessionService.JoinByCodeAsync(code, $"AutoClient-{UgsAuth.Profile}");
            }
            catch (Exception e)
            {
                string msg = SessionService.Describe(e);
                if (expectReject) { Note($"client: join refused as expected: {msg}"); m_Result["rejectReason"] = msg; return true; }
                return Fail($"join failed: {msg}");
            }

            var nm = NetworkManager.Singleton;
            bool ready = await WaitUntil(() =>
                lost != null ||
                (nm.IsConnectedClient && GameSession.Instance != null && NetDiagnostics.Instance != null &&
                 GameSession.Instance.SyncToken.Value != 0 && GameSession.Instance.IndexOf(nm.LocalClientId) >= 0), 45);

            if (expectReject)
            {
                if (lost != null || !ready) { Note($"client: refused as expected: {lost ?? nm.DisconnectReason}"); m_Result["rejectReason"] = lost ?? nm.DisconnectReason; return true; }
                return Fail("client was admitted but expected to be refused (capacity not enforced)");
            }
            if (lost != null) return Fail($"connection lost: {lost}");
            if (!ready) return Fail($"never became ready (connected={nm.IsConnectedClient}, reason='{nm.DisconnectReason}')");

            float joinSec = Time.realtimeSinceStartup - joinStart;
            Note($"client: connected as client {nm.LocalClientId} in {joinSec:F2}s, roster={GameSession.Instance.Roster.Count}");
            m_Result["joinSeconds"] = joinSec.ToString("F2");

            // Ping series over unreliable RPCs.
            var sent = new Dictionary<uint, double>();
            var rtts = new List<double>();
            NetDiagnostics.Instance.Pong += (seq, t0) =>
            {
                if (sent.Remove(seq)) rtts.Add((Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
            };
            const int pings = 40;
            for (uint s = 1; s <= pings; s++)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                sent[s] = now;
                NetDiagnostics.Instance.PingRpc(s, now);
                await WaitSeconds(0.1f);
            }
            await WaitSeconds(1.5f);

            double sum = 0, min = double.MaxValue, max = 0;
            foreach (double r in rtts) { sum += r; min = Math.Min(min, r); max = Math.Max(max, r); }
            var report = new NetDiagnostics.TestReport
            {
                TokenEcho = GameSession.Instance.SyncToken.Value,
                PingsSent = pings,
                PongsReceived = rtts.Count,
                RttAvgMs = rtts.Count > 0 ? (float)(sum / rtts.Count) : -1f,
                RttMinMs = rtts.Count > 0 ? (float)min : -1f,
                RttMaxMs = (float)max,
                RosterCountSeen = GameSession.Instance.Roster.Count,
                Platform = (byte)PlayerRecord.LocalPlatform(),
            };
            Note($"client: pongs {report.PongsReceived}/{pings}, rtt avg {report.RttAvgMs:F1}ms min {report.RttMinMs:F1} max {report.RttMaxMs:F1}");
            m_Result["rttAvgMs"] = report.RttAvgMs.ToString("F1");
            m_Result["pongs"] = $"{report.PongsReceived}/{pings}";

            bool? verdict = null;
            string verdictMsg = "";
            NetDiagnostics.Instance.AckReceived += (ok, msg) => { verdict = ok; verdictMsg = msg; };
            NetDiagnostics.Instance.ReportRpc(report);

            if (!await WaitUntil(() => verdict.HasValue, 30)) return Fail("host verdict never arrived");
            Note($"client: host verdict = {(verdict.Value ? "PASS" : "FAIL")} ({verdictMsg})");
            if (!verdict.Value) return false;

            // Optional: stay connected so capacity tests can try to over-fill the match.
            float hold = CommandLine.GetFloat("holdopen", 0f);
            if (hold > 0f && !CommandLine.Has("waitforhostloss"))
            {
                Note($"client: holding connection for {hold:F0}s");
                await WaitUntil(() => lost != null, hold);
                if (lost != null) return Fail($"connection dropped while holding: {lost}");
            }

            // Optional: host-departure test. The harness kills the host; we must notice and
            // surface a clear reason within the allowed window.
            if (CommandLine.Has("waitforhostloss"))
            {
                float window = CommandLine.GetFloat("waitforhostloss", 40f);
                Note($"client: waiting up to {window:F0}s for host departure");
                float t0 = Time.realtimeSinceStartup;
                if (!await WaitUntil(() => lost != null, window)) return Fail("host departure was never detected");
                float dt = Time.realtimeSinceStartup - t0;
                Note($"client: host departure detected after {dt:F1}s: '{lost}'");
                m_Result["hostLossReason"] = lost;
                m_Result["hostLossDetectSeconds"] = dt.ToString("F1");
            }
            return true;
        }

        // --------------------------------------------------------------- helpers

        bool Fail(string why)
        {
            Note("FAIL: " + why);
            m_Result["failure"] = why;
            return false;
        }

        void Note(string line)
        {
            string stamped = $"[{Time.realtimeSinceStartup,7:F2}s] {line}";
            m_Log.AppendLine(stamped);
            Debug.Log("[SALTLINE][AUTOTEST] " + stamped);
        }

        async Task Finish(bool ok)
        {
            m_Result["result"] = ok ? "PASS" : "FAIL";
            Debug.Log($"[SALTLINE][AUTOTEST] RESULT {m_Role.ToUpperInvariant()} {(ok ? "PASS" : "FAIL")}");
            if (!string.IsNullOrEmpty(m_OutFile))
            {
                try
                {
                    var sb = new StringBuilder("{\n");
                    int n = 0;
                    foreach (var kv in m_Result)
                        sb.Append($"  \"{kv.Key}\": \"{kv.Value.Replace("\\", "/").Replace("\"", "'")}\"{(++n < m_Result.Count ? "," : "")}\n");
                    sb.Append("}\n");
                    File.WriteAllText(m_OutFile, sb.ToString());
                    File.WriteAllText(m_OutFile + ".log", m_Log.ToString());
                }
                catch (Exception e) { Debug.LogWarning($"could not write test output: {e.Message}"); }
            }

            try { await SessionService.LeaveAsync(); } catch { }
            await WaitSeconds(0.5f);
            Application.Quit(ok ? 0 : 1);
        }

        static async Task<bool> WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) return false;
                await Task.Yield();
            }
            return true;
        }

        static async Task WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) await Task.Yield();
        }
    }
}
