using Saltline.Diagnostics;
using Saltline.FX;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Saltline.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saltline.Core
{
    /// <summary>
    /// Lives in the Boot scene (build index 0) and persists for the whole process.
    /// Wires the NetworkManager to the game (approval, session object, disconnect handling)
    /// and then hands off to the main menu, or to a headless test when -autotest is passed.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        [SerializeField] NetworkObject sessionPrefab;
        [SerializeField] NetworkObject playerPrefab;
        [SerializeField] string menuScene = "Menu";

        /// <summary>Last reason the session ended unexpectedly; the menu shows it once.</summary>
        public static string PendingNotice;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.runInBackground = true;
            if (Application.isBatchMode)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
            }
        }

        void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[SALTLINE] Boot scene has no NetworkManager.");
                return;
            }

            ConnectionGate.Install(nm);
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientDisconnectCallback += OnClientDisconnect;
            nm.OnTransportFailure += OnTransportFailure;
            NetSimBridge.ApplyFromCommandLine(nm);

            SessionService.SessionLost += reason => PendingNotice = reason;
            NetPlayer.LocalSpawned += OnLocalPlayerSpawned;

            if (CommandLine.Has("autotest"))
            {
                if (CommandLine.Get("autotest", "").StartsWith("combat")) gameObject.AddComponent<CombatAutoTest>();
                else gameObject.AddComponent<RelayAutoTest>();
                return;
            }

            if (Application.CanStreamedLevelBeLoaded(menuScene))
                SceneManager.LoadScene(menuScene);
        }

        void OnServerStarted()
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsServer) return;
            if (playerPrefab != null) PlayerSpawner.Install(nm, playerPrefab);
            if (sessionPrefab == null || GameSession.Instance != null) return;
            var instance = Instantiate(sessionPrefab);
            instance.Spawn(destroyWithScene: false);
        }

        /// <summary>A human gets keyboard/mouse control, camera, HUD and effects. Tests attach bots instead.</summary>
        static void OnLocalPlayerSpawned(NetPlayer p)
        {
            if (CommandLine.Has("autotest")) return;
            LocalPlayerController.Attach(p);
            if (!Application.isBatchMode)
            {
                FxManager.Ensure();
                DebugHud.Attach(p);
            }
        }

        void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer) return;
            if (clientId != nm.LocalClientId && clientId != NetworkManager.ServerClientId) return;

            string reason = string.IsNullOrEmpty(nm.DisconnectReason)
                ? "Lost connection to the host."
                : nm.DisconnectReason;
            Debug.Log($"[SALTLINE] Disconnected from host: {reason}");
            SessionService.NotifyNetworkLost(reason);
        }

        void OnTransportFailure()
        {
            Debug.LogWarning("[SALTLINE] Transport failure.");
            SessionService.NotifyNetworkLost("Network transport failed. Check your connection.");
        }
    }
}
