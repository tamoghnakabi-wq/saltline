using Saltline.Net;
using Saltline.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Saltline.UI
{
    /// <summary>
    /// Network and performance readout (F3): frame rate, round-trip time, interpolation delay,
    /// prediction corrections and shot confirmations. Hidden by default; the game HUD is HudScreen.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        NetPlayer m_P;
        bool m_Show;
        float m_FpsTimer, m_Fps, m_WorstFrame, m_WorstShown;
        int m_Frames;
        GUIStyle m_Style;

        public static DebugHud Attach(NetPlayer p)
        {
            var go = new GameObject("DebugHud");
            var hud = go.AddComponent<DebugHud>();
            hud.m_P = p;
            hud.m_Show = Debug.isDebugBuild && Saltline.Core.CommandLine.Has("netstats");
            return hud;
        }

        void Update()
        {
            if (m_P == null) { Destroy(gameObject); return; }
            m_Frames++;
            m_FpsTimer += Time.unscaledDeltaTime;
            m_WorstFrame = Mathf.Max(m_WorstFrame, Time.unscaledDeltaTime);
            if (m_FpsTimer >= 0.5f)
            {
                m_Fps = m_Frames / m_FpsTimer;
                m_WorstShown = m_WorstFrame;
                m_Frames = 0; m_FpsTimer = 0f; m_WorstFrame = 0f;
            }
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) m_Show = !m_Show;
        }

        void OnGUI()
        {
            if (!m_Show || m_P == null || !m_P.IsSpawned) return;
            m_Style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            var nm = NetworkManager.Singleton;
            string rtt = "host";
            if (!nm.IsServer && nm.NetworkConfig.NetworkTransport is UnityTransport utp)
                rtt = $"{utp.GetCurrentRtt(NetworkManager.ServerClientId)} ms";
            string s = $"{m_Fps:F0} fps (worst {m_WorstShown * 1000f:F1} ms)   rtt {rtt}   interp {NetClock.InterpolationTicks:F1} ticks\n" +
                       $"tick {m_P.LocalTick} ack {m_P.LastAck}   corrections {m_P.Reconciliations} (last {m_P.LastCorrectionMeters * 100f:F1} cm)\n" +
                       $"shots {m_P.ShotsPredicted} confirmed {m_P.HitsConfirmed} kills {m_P.KillsConfirmed}   [F3 to hide]";
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(10, Screen.height - 76, 560, 64), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(16, Screen.height - 74, 550, 62), s, m_Style);
        }
    }
}
