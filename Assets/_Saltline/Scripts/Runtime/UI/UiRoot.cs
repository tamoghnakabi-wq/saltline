using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saltline.Core;
using Saltline.Input;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Saltline.UI
{
    /// <summary>
    /// The one persistent UI document. Decides which screen is visible from the session state and
    /// the match phase, owns the pause/settings overlays, modal dialogs, toasts and the busy spinner,
    /// and blocks gameplay input whenever a menu has the cursor.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }

        UIDocument m_Doc;
        VisualElement m_Root, m_ScreenLayer, m_HudLayer, m_BoardLayer, m_OverlayLayer, m_ModalLayer, m_BusyLayer, m_ToastStack;
        Label m_BusyText;
        VisualElement m_Spinner;

        public MainMenuScreen Menu { get; private set; }
        public LobbyScreen Lobby { get; private set; }
        public LoadingScreen Loading { get; private set; }
        public HudScreen Hud { get; private set; }
        public ScoreboardOverlay Board { get; private set; }
        public ResultsScreen Results { get; private set; }
        public PauseScreen Pause { get; private set; }
        public SettingsScreen Settings { get; private set; }

        readonly List<UiScreen> m_Screens = new();
        readonly List<(VisualElement el, float until)> m_Toasts = new();
        VisualElement m_OpenModal;
        int m_BusyCount;
        bool m_PauseOpen;
        bool m_SettingsOpen;
        bool m_BoardForced;

        /// <summary>Panel units per screen pixel (the panel scales with resolution).</summary>
        public float PixelsToPanel => m_Root != null && Screen.height > 0 && m_Root.resolvedStyle.height > 0 ? m_Root.resolvedStyle.height / Screen.height : 1f;
        public bool ModalOpen => m_OpenModal != null;
        public bool Busy => m_BusyCount > 0;
        public UiScreen CurrentScreen { get; private set; }
        public VisualElement RootElement => m_Root;
        public PanelSettings Panel => m_Doc != null ? m_Doc.panelSettings : null;

        public static UiRoot Ensure()
        {
            if (Instance != null) return Instance;
            if (!RunMode.Presentation) return null;
            var ps = Resources.Load<PanelSettings>("UI/SaltlinePanel");
            if (ps == null) { Debug.LogError("[SALTLINE] UI PanelSettings missing (Resources/UI/SaltlinePanel)."); return null; }
            var go = new GameObject("UiRoot");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            go.AddComponent<UiRoot>();
            go.SetActive(true);
            return Instance;
        }

        void Awake()
        {
            Instance = this;
            m_Doc = GetComponent<UIDocument>();
        }

        // Built in Start: UIDocument creates its root element in its own OnEnable, which runs
        // after this component's (this class runs early so it can block input first).
        void Start()
        {
            m_Root = m_Doc.rootVisualElement;
            m_Root.Clear();
            m_Root.AddToClassList("sl-root");
            m_Root.pickingMode = PickingMode.Ignore;

            m_ScreenLayer = Layer("screens");
            m_HudLayer = Layer("hud");
            m_BoardLayer = Layer("board");
            m_OverlayLayer = Layer("overlays");
            m_ModalLayer = Layer("modals");
            m_BusyLayer = m_Root.Put(Ui.Div("layer", "scrim", "busy"));
            m_Spinner = m_BusyLayer.Put(Ui.Div("spinner"));
            m_BusyText = m_BusyLayer.Put(Ui.Text("CONNECTING", "busy-text"));
            m_BusyLayer.Show(false);
            m_ToastStack = m_Root.Put(Ui.Div("toast-stack"));
            m_ToastStack.Ignore();

            m_Screens.Clear();
            Menu = Add(new MainMenuScreen(), m_ScreenLayer);
            Lobby = Add(new LobbyScreen(), m_ScreenLayer);
            Loading = Add(new LoadingScreen(), m_ScreenLayer);
            Results = Add(new ResultsScreen(), m_ScreenLayer);
            Hud = Add(new HudScreen(), m_HudLayer);
            Board = Add(new ScoreboardOverlay(), m_BoardLayer);
            Pause = Add(new PauseScreen(), m_OverlayLayer);
            Settings = Add(new SettingsScreen(), m_OverlayLayer);
        }

        VisualElement Layer(string name)
        {
            var l = Ui.Div("layer");
            l.name = name;
            l.Ignore();
            m_Root.Add(l);
            return l;
        }

        T Add<T>(T screen, VisualElement layer) where T : UiScreen
        {
            screen.Init(this, layer);
            m_Screens.Add(screen);
            return screen;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            LocalPlayerController.InputBlocked = false;
        }

        // ================================================================== frame

        void Update()
        {
            if (m_Root == null) return;
            var nm = NetworkManager.Singleton;
            var md = MatchDirector.Instance;
            bool inSession = SessionService.InSession;
            bool connected = inSession && nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && md != null && md.IsSpawned;

            // Back at the menu after leaving or losing a session: return to the menu scene.
            if (!inSession && nm != null && !nm.IsListening && !nm.ShutdownInProgress && !Busy)
                GameBootstrap.LoadMenuSceneIfNeeded();

            UiScreen want;
            bool hud = false;
            if (!inSession) want = Menu;
            else if (!connected) { want = Loading; Loading.SetConnecting(); }
            else
            {
                switch (md.Phase.Value)
                {
                    case MatchPhase.Lobby:
                        want = Lobby;
                        break;
                    case MatchPhase.Loading:
                        want = Loading;
                        Loading.SetMap(md.MapId.Value.ToString(), md.Rules.Value);
                        break;
                    case MatchPhase.Results:
                        want = Results;
                        break;
                    default:
                        want = null;
                        hud = true;
                        break;
                }
            }

            if (want != CurrentScreen)
            {
                CurrentScreen = want;
                foreach (var s in new UiScreen[] { Menu, Lobby, Loading, Results }) s.SetVisible(s == want);
                if (want != null && want != Results) { m_PauseOpen = false; }
            }

            Hud.SetVisible(hud && NetPlayer.Local != null);

            // Esc: settings → pause → game.
            var gi = GameInput.Instance;
            if (gi.Pause.WasPressedThisFrame() && !Settings.Listening && !Busy)
            {
                if (m_OpenModal != null) CloseModal();
                else if (m_SettingsOpen) CloseSettings();
                else if (hud) m_PauseOpen = !m_PauseOpen;
            }
            if (!hud) m_PauseOpen = false;
            Pause.SetVisible(m_PauseOpen && !m_SettingsOpen);
            Settings.SetVisible(m_SettingsOpen);

            bool boardHeld = hud && !m_PauseOpen && !m_SettingsOpen && (gi.Scoreboard.IsPressed() || m_BoardForced);
            Board.SetVisible(boardHeld);

            LocalPlayerController.InputBlocked = !hud || m_PauseOpen || m_SettingsOpen || m_OpenModal != null || Busy;
            if (LocalPlayerController.InputBlocked && NetPlayer.Local == null)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }

            foreach (var s in m_Screens) if (s.Visible) s.Tick();
            if (Busy) m_Spinner.style.rotate = new Rotate(new Angle(Time.unscaledTime * 360f % 360f, AngleUnit.Degree));
            TickToasts();
        }

        // ================================================================== overlays

        public void OpenSettings() { m_SettingsOpen = true; }
        public void CloseSettings() { m_SettingsOpen = false; GameInput.Instance.SaveBindings(); Saltline.Settings.GameSettings.Save(); }
        public void ClosePause() => m_PauseOpen = false;
        /// <summary>Diagnostics: show the scoreboard without holding the key.</summary>
        public void ForceScoreboard(bool on) => m_BoardForced = on;
        public void ForcePause(bool on) => m_PauseOpen = on;

        public async void RunBusy(string text, Func<Task> work, Action<Exception> onError = null)
        {
            m_BusyCount++;
            m_BusyText.text = text;
            m_BusyLayer.Show(true);
            try { await work(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[SALTLINE] {text}: {e}");
                if (onError != null) onError(e);
                else Alert("SOMETHING WENT WRONG", SessionService.Describe(e));
            }
            finally
            {
                m_BusyCount--;
                if (m_BusyCount <= 0) { m_BusyCount = 0; m_BusyLayer.Show(false); }
            }
        }

        public void Alert(string title, string body) => Confirm(title, body, "OK", null, null);

        public void Confirm(string title, string body, string okText, Action onOk, string cancelText = "CANCEL", bool danger = false)
        {
            CloseModal();
            var scrim = Ui.Div("layer", "scrim", "modal");
            var panel = scrim.Put(Ui.Div("panel", "modal-panel"));
            panel.Add(Ui.Text(title, "h2"));
            panel.Add(Ui.Text(body, "modal-body"));
            var actions = panel.Put(Ui.Div("modal-actions"));
            if (cancelText != null) actions.Add(Ui.Button(cancelText, CloseModal, "btn--ghost"));
            actions.Add(Ui.Button(okText, () => { CloseModal(); onOk?.Invoke(); }, danger ? "btn--danger" : "btn--primary"));
            m_ModalLayer.Add(scrim);
            m_OpenModal = scrim;
        }

        public void CloseModal()
        {
            if (m_OpenModal == null) return;
            m_OpenModal.RemoveFromHierarchy();
            m_OpenModal = null;
        }

        public void Toast(string text, float seconds = 3.5f)
        {
            var t = Ui.Text(text, "toast");
            m_ToastStack.Add(t);
            m_Toasts.Add((t, Time.unscaledTime + seconds));
        }

        void TickToasts()
        {
            for (int i = m_Toasts.Count - 1; i >= 0; i--)
            {
                var (el, until) = m_Toasts[i];
                if (Time.unscaledTime > until) el.AddToClassList("toast--old");
                if (Time.unscaledTime > until + 0.4f) { el.RemoveFromHierarchy(); m_Toasts.RemoveAt(i); }
            }
        }

        // ================================================================== session actions

        public void LeaveSession()
        {
            m_PauseOpen = false;
            m_SettingsOpen = false;
            RunBusy("LEAVING", async () => await SessionService.LeaveAsync());
        }

        public void QuitGame()
        {
            Confirm("QUIT SALTLINE?", SessionService.InSession ? "You will leave the current match." : "See you on the salt.", "QUIT", async () =>
            {
                try { await SessionService.LeaveAsync(); } catch { }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, "CANCEL", true);
        }
    }
}
