using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Saltline.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Drives the real menus with simulated button presses and saves a picture of every screen
    /// (3D view and UI composited). Runs in batch mode with a graphics device:
    ///
    ///   -batchmode -capture dir -uitest host -codefile f [-warmup 4 -scorelimit 3 -resultstime 6 ...]
    ///
    /// Main menu cards → settings tabs → create a private lobby through the host card → wait for
    /// a headless client (match-client) to switch team and ready → START MATCH → loading, warmup
    /// HUD, live HUD in first and third person, kill feed, scoreboard, pause → results → lobby.
    /// Kills are applied on the host (as in the match test); this test is about the screens.
    /// </summary>
    public sealed class UiAutoTest : AutoTestBase
    {
        protected override string RoleKey => "uitest";

        int m_W = 1920, m_H = 1080;
        RenderTexture m_UiRt, m_SceneRt;
        Texture2D m_UiTex, m_SceneTex;
        string m_Dir;
        int m_Shots;
        bool m_ClientDone;

        UiRoot R => UiRoot.Instance;
        VisualElement Root => R.RootElement;

        protected override async Task<bool> Run()
        {
            m_Dir = CommandLine.Get("capture", Path.Combine(Application.temporaryCachePath, "uitest"));
            Directory.CreateDirectory(m_Dir);
            if (!RunMode.Presentation) return Fail("UI test needs a graphics device (batch mode without -nographics, with -capture)");
            if (!await WaitUntil(() => R != null && R.Menu != null && R.Menu.Visible && SceneManager.GetActiveScene().name == MatchDirector.LobbyScene, 20))
                return Fail("main menu did not appear in the menu scene");
            SetupCapture();
            await WaitSeconds(1f);

            // ---- main menu
            await Shot("01_menu_host");
            if (!await Click("JOIN WITH CODE")) return false;
            await Shot("02_menu_join");
            if (!await Click("BROWSE MATCHES")) return false;
            await WaitSeconds(3f); // a real session query
            await Shot("03_menu_browse");
            if (!await Click("HOST MATCH")) return false;

            // ---- settings
            if (!await Click("SETTINGS")) return false;
            if (!await WaitUntil(() => R.Settings.Visible, 2)) return Fail("settings did not open");
            await Shot("04_settings_gameplay");
            foreach (string tab in new[] { "Video", "Audio", "Controls" })
            {
                if (!await ClickNamed("tab" + tab)) return false;
                await Shot($"05_settings_{tab.ToLowerInvariant()}");
            }
            if (!await Click("DONE")) return false;
            if (!await WaitUntil(() => !R.Settings.Visible, 2)) return Fail("settings did not close");

            // ---- host a private lobby through the host card
            Root.Q<TextField>("callsign").value = "UiHost";
            Root.Q<TextField>("sessionName").value = "UI test lobby";
            if (!await Click("PRIVATE · CODE ONLY")) return false;
            if (!await ClickNamed("createLobby")) return false;
            if (!await WaitUntil(() => SessionService.InSession && R.Lobby.Visible && GameSession.Instance != null && MatchDirector.Instance != null, 40))
                return Fail("lobby did not open after CREATE LOBBY");
            var code = SessionService.Current.Code;
            Result["lobbyCodeShown"] = (Root.Q<Label>("lobbyCode")?.text?.Contains(code) == true).ToString();
            NetDiagnostics.Instance.ClientPhase += (_, phase, _) => { if (phase == 99) m_ClientDone = true; };
            WriteCode(code);
            await WaitSeconds(0.8f);
            await Shot("06_lobby_alone");

            var start = Root.Q<Button>("lobbyAction");
            if (!await WaitUntil(() => GameSession.Instance.Roster.Count >= 2, Timeout)) return Fail("client never joined");
            await WaitSeconds(0.3f);
            Result["startDisabledWhileNotReady"] = (!start.enabledSelf).ToString();
            if (start.enabledSelf && !ClientReady()) return Fail("START MATCH was enabled while the client was not ready");
            if (!await WaitUntil(ClientReady, 40)) return Fail("client did not ready up");
            await WaitSeconds(0.8f);
            await Shot("07_lobby_ready");
            if (!start.enabledSelf) return Fail("START MATCH stayed disabled with everyone ready");

            // ---- match
            var md = MatchDirector.Instance;
            bool sawLoading = false;
            void OnPhase(MatchPhase prev, MatchPhase cur) { if (cur == MatchPhase.Loading) sawLoading = true; }
            MatchDirector.PhaseChanged += OnPhase;
            if (!await ClickNamed("lobbyAction")) return false;
            if (!await WaitUntil(() => sawLoading, 5)) return Fail("START MATCH did not start loading");
            MatchDirector.PhaseChanged -= OnPhase;
            // The test map loads in a fraction of a second, so the loading screen may already be gone.
            if (md.Phase.Value == MatchPhase.Loading) await Shot("08_loading");
            else Note("ui: loading finished before a frame could be captured");
            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Warmup && NetPlayer.Local != null && NetPlayer.Local.LocalRig != null && R.Hud.Visible, 40))
                return Fail("warmup HUD did not appear");
            await WaitSeconds(1.2f);
            await Shot("09_warmup_hud");
            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Live, md.Rules.Value.Warmup + 10)) return Fail("match did not go live");
            await WaitSeconds(1.6f);
            await Shot("10_live_fps");

            var me = NetPlayer.Local;
            var them = NetPlayer.All.First(p => p != me);
            if (!await Kill(me, them)) return Fail("kill failed");
            await WaitSeconds(0.4f);
            await Shot("11_kill_feed");

            me.ThirdPerson = true;
            await WaitSeconds(1f);
            await Shot("12_live_tps");
            me.ThirdPerson = false;

            R.ForceScoreboard(true);
            await WaitSeconds(0.4f);
            Result["scoreboardShown"] = R.Board.Visible.ToString();
            await Shot("13_scoreboard");
            R.ForceScoreboard(false);

            R.ForcePause(true);
            await WaitSeconds(0.4f);
            Result["pauseShown"] = R.Pause.Visible.ToString();
            await Shot("14_pause");
            R.ForcePause(false);

            // Host dies once so the death overlay shows.
            if (!await Kill(them, me)) return Fail("host death failed");
            await WaitSeconds(0.5f);
            await Shot("15_death");

            while (md.Phase.Value == MatchPhase.Live)
                if (!await Kill(me, them)) break;
            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Results && R.Results.Visible, 10)) return Fail("results screen did not appear");
            await WaitSeconds(1f);
            Result["verdict"] = Root.Q<Label>("verdict")?.text ?? "";
            await Shot("16_results");

            if (!await WaitUntil(() => md.Phase.Value == MatchPhase.Lobby && R.Lobby.Visible && SceneManager.GetActiveScene().name == MatchDirector.LobbyScene, md.Rules.Value.Results + 30))
                return Fail("did not return to the lobby screen");
            await WaitSeconds(1.5f);
            await Shot("17_lobby_after");

            Result["shots"] = m_Shots.ToString();
            if (!await WaitUntil(() => m_ClientDone, 30)) return Fail("client did not report completion");
            bool ok = Result["lobbyCodeShown"] == "True" && Result["scoreboardShown"] == "True" && Result["pauseShown"] == "True" && Result["verdict"] == "VICTORY";
            return ok || Fail("a screen check failed (see result fields)");
        }

        bool ClientReady()
        {
            foreach (var r in GameSession.Instance.Roster) if (!r.IsHost && r.Ready) return true;
            return false;
        }

        async Task<bool> Kill(NetPlayer attacker, NetPlayer victim)
        {
            if (!await WaitUntil(() => victim.AliveState.Value && !victim.Protected.Value, 20)) return false;
            victim.ServerApplyDamage(attacker, 500, 0, false, false, attacker.SimMotor.Position);
            return await WaitUntil(() => !victim.AliveState.Value, 2);
        }

        // ================================================================== input

        static bool Shown(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.ClassListContains("hidden")) return false;
            return true;
        }

        Task<bool> Click(string text) => Press(Root.Query<Button>().Where(b => b.text == text && Shown(b)).First(), text);
        Task<bool> ClickNamed(string name) => Press(Root.Query<Button>(name).Where(Shown).First(), name);

        async Task<bool> Press(Button b, string what)
        {
            if (b == null) return Fail($"button '{what}' not found on screen");
            if (!b.enabledInHierarchy) return Fail($"button '{what}' is disabled");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
            Note($"ui: pressed {what}");
            await WaitSeconds(0.25f);
            return true;
        }

        // ================================================================== capture

        void SetupCapture()
        {
            string size = CommandLine.Get("capturesize", "1920x1080");
            var parts = size.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) { m_W = w; m_H = h; }
            m_UiRt = new RenderTexture(m_W, m_H, 24, RenderTextureFormat.ARGB32);
            m_SceneRt = new RenderTexture(m_W, m_H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            m_UiTex = new Texture2D(m_W, m_H, TextureFormat.RGBA32, false);
            m_SceneTex = new Texture2D(m_W, m_H, TextureFormat.RGBA32, false);
            var ps = R.Panel;
            ps.targetTexture = m_UiRt;
            ps.clearColor = true;
            ps.colorClearValue = Color.clear;
        }

        static Camera SceneCamera()
        {
            var me = NetPlayer.Local;
            if (me != null && me.LocalRig != null && me.LocalRig.MainCamera != null) return me.LocalRig.MainCamera;
            Camera best = null;
            foreach (var c in Camera.allCameras)
                if (c.isActiveAndEnabled && (best == null || c.depth > best.depth)) best = c;
            return best;
        }

        async Task Shot(string name)
        {
            await Task.Yield(); // let the panel repaint into its texture this frame
            var cam = SceneCamera();
            if (cam != null)
            {
                var prev = cam.targetTexture;
                cam.targetTexture = m_SceneRt;
                cam.Render();
                cam.targetTexture = prev;
                Read(m_SceneRt, m_SceneTex);
            }
            else m_SceneTex.SetPixels32(new Color32[m_W * m_H]);
            Read(m_UiRt, m_UiTex);

            // UI Toolkit writes premultiplied colour: out = ui + scene * (1 - ui.a).
            var s = m_SceneTex.GetPixels32();
            var u = m_UiTex.GetPixels32();
            for (int i = 0; i < s.Length; i++)
            {
                int inv = 255 - u[i].a;
                s[i] = new Color32(
                    (byte)Mathf.Min(255, u[i].r + s[i].r * inv / 255),
                    (byte)Mathf.Min(255, u[i].g + s[i].g * inv / 255),
                    (byte)Mathf.Min(255, u[i].b + s[i].b * inv / 255), 255);
            }
            m_SceneTex.SetPixels32(s);
            m_SceneTex.Apply(false);
            string path = Path.Combine(m_Dir, name + ".png");
            File.WriteAllBytes(path, m_SceneTex.EncodeToPNG());
            m_Shots++;
            Note($"ui: captured {name} (camera {(cam != null ? cam.name : "none")}, screen {R.CurrentScreen?.GetType().Name})");
        }

        static void Read(RenderTexture rt, Texture2D into)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            into.Apply(false);
            RenderTexture.active = prev;
        }

        void OnDestroy()
        {
            if (m_UiRt != null) m_UiRt.Release();
            if (m_SceneRt != null) m_SceneRt.Release();
        }
    }
}
