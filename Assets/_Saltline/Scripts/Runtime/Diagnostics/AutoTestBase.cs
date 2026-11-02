using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Saltline.Core;
using Saltline.Net;
using UnityEngine;

namespace Saltline.Diagnostics
{
    /// <summary>Shared plumbing for headless tests: logging, JSON result file, waits, exit code.</summary>
    public abstract class AutoTestBase : MonoBehaviour
    {
        protected string Role;
        protected string CodeFile;
        protected string OutFile;
        protected float Timeout;
        readonly StringBuilder m_Log = new();
        protected readonly Dictionary<string, string> Result = new();

        protected abstract Task<bool> Run();

        async void Start()
        {
            Role = CommandLine.Get("autotest", "host").ToLowerInvariant();
            CodeFile = CommandLine.Get("codefile", Path.Combine(Application.temporaryCachePath, "saltline_code.txt"));
            OutFile = CommandLine.Get("testout");
            Timeout = CommandLine.GetFloat("testtimeout", 120f);
            Result["role"] = Role;
            Result["platform"] = PlayerRecord.LocalPlatform().ToString();
            Result["protocol"] = BuildInfo.NetProtocol.ToString();
            Result["version"] = BuildInfo.Version;
            Result["netsim"] = NetSimBridge.ActiveDescription;
            bool ok;
            try { ok = await Run(); }
            catch (Exception e)
            {
                Note($"EXCEPTION: {SessionService.Describe(e)} :: {e}");
                ok = false;
            }
            // A latency test that silently ran without latency would be a false pass.
            Result["netsim"] = NetSimBridge.ActiveDescription;
            if (CommandLine.Has("netsim") && NetSimBridge.ActiveDescription == "off")
                ok = Fail("-netsim was requested but the network simulator is not active (development build required)");
            await Finish(ok);
        }

        /// <summary>Round-trip time series over unreliable RPCs (client side).</summary>
        protected async Task<(float avg, float min, float max, int received)> MeasureRtt(int pings, float interval)
        {
            var sent = new Dictionary<uint, double>();
            var rtts = new List<double>();
            void OnPong(uint seq, double t0)
            {
                if (sent.Remove(seq)) rtts.Add((Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
            }
            Saltline.Net.NetDiagnostics.Instance.Pong += OnPong;
            for (uint i = 1; i <= pings; i++)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                sent[i + 100000u] = now;
                Saltline.Net.NetDiagnostics.Instance.PingRpc(i + 100000u, now);
                await WaitSeconds(interval);
            }
            await WaitSeconds(1.0f);
            if (Saltline.Net.NetDiagnostics.Instance != null) Saltline.Net.NetDiagnostics.Instance.Pong -= OnPong;
            if (rtts.Count == 0) return (-1f, -1f, -1f, 0);
            double sum = 0, mn = double.MaxValue, mx = 0;
            foreach (double r in rtts) { sum += r; mn = Math.Min(mn, r); mx = Math.Max(mx, r); }
            return ((float)(sum / rtts.Count), (float)mn, (float)mx, rtts.Count);
        }

        protected bool Fail(string why)
        {
            Note("FAIL: " + why);
            Result["failure"] = why;
            return false;
        }

        protected void Note(string line)
        {
            string stamped = $"[{Time.realtimeSinceStartup,7:F2}s] {line}";
            m_Log.AppendLine(stamped);
            Debug.Log("[SALTLINE][AUTOTEST] " + stamped);
        }

        protected void WriteCode(string code)
        {
            string tmp = CodeFile + ".tmp";
            File.WriteAllText(tmp, code);
            if (File.Exists(CodeFile)) File.Delete(CodeFile);
            File.Move(tmp, CodeFile);
        }

        protected async Task<string> ReadCode()
        {
            string code = null;
            await WaitUntil(() =>
            {
                if (!File.Exists(CodeFile)) return false;
                try { code = File.ReadAllText(CodeFile).Trim(); } catch { return false; }
                return code.Length > 0;
            }, Timeout);
            return code;
        }

        async Task Finish(bool ok)
        {
            Result["result"] = ok ? "PASS" : "FAIL";
            Debug.Log($"[SALTLINE][AUTOTEST] RESULT {Role.ToUpperInvariant()} {(ok ? "PASS" : "FAIL")}");
            if (!string.IsNullOrEmpty(OutFile))
            {
                try
                {
                    var sb = new StringBuilder("{\n");
                    int n = 0;
                    foreach (var kv in Result)
                        sb.Append($"  \"{kv.Key}\": \"{kv.Value.Replace("\\", "/").Replace("\"", "'")}\"{(++n < Result.Count ? "," : "")}\n");
                    sb.Append("}\n");
                    File.WriteAllText(OutFile, sb.ToString());
                    File.WriteAllText(OutFile + ".log", m_Log.ToString());
                }
                catch (Exception e) { Debug.LogWarning($"could not write test output: {e.Message}"); }
            }
            try { await SessionService.LeaveAsync(); } catch { }
            await WaitSeconds(0.5f);
            Application.Quit(ok ? 0 : 1);
        }

        protected static async Task<bool> WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) return false;
                await Task.Yield();
            }
            return true;
        }

        protected static async Task WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) await Task.Yield();
        }
    }
}
