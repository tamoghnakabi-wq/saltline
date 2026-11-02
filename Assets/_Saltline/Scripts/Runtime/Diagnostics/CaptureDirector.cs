using System.IO;
using Saltline.Core;
using Saltline.Player;
using UnityEngine;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Renders the local player's camera to PNG files at a fixed interval, alternating first and
    /// third person, plus one overview from the map's spectator camera. Works in batch mode
    /// (without -nographics), so visual checks can run from scripts:
    ///
    ///   -capture &lt;dir&gt; [-captureevery seconds] [-capturesize 1920x1080]
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class CaptureDirector : MonoBehaviour
    {
        NetPlayer m_Player;
        string m_Dir;
        float m_Interval;
        int m_W = 1920, m_H = 1080;
        RenderTexture m_Rt;
        Texture2D m_Tex;
        float m_NextSwitch, m_NextShot = -1f;
        int m_Index;
        bool m_Overview;

        public static int Captured { get; private set; }

        public static CaptureDirector Install(NetPlayer player)
        {
            if (!CommandLine.Has("capture") || !RunMode.Presentation) return null;
            var go = new GameObject("CaptureDirector");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<CaptureDirector>();
            d.m_Player = player;
            return d;
        }

        void Awake()
        {
            m_Dir = CommandLine.Get("capture");
            Directory.CreateDirectory(m_Dir);
            m_Interval = Mathf.Max(0.5f, CommandLine.GetFloat("captureevery", 3f));
            string size = CommandLine.Get("capturesize", "1920x1080");
            var parts = size.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) { m_W = w; m_H = h; }
            m_Rt = new RenderTexture(m_W, m_H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            m_Tex = new Texture2D(m_W, m_H, TextureFormat.RGB24, false);
            m_NextSwitch = Time.time + 1.5f;
            Debug.Log($"[SALTLINE] Capturing {m_W}x{m_H} every {m_Interval:F1}s to {m_Dir} (gfx {SystemInfo.graphicsDeviceType})");
        }

        void LateUpdate()
        {
            if (m_Player == null) return;
            var rig = FindFirstObjectByType<PlayerCameraRig>();
            if (rig == null) return;
            if (rig.MainCamera.targetTexture != m_Rt) rig.MainCamera.targetTexture = m_Rt;

            if (!m_Overview) { m_Overview = true; CaptureOverview(); }

            // Choose the view for the next shot, give the camera time to blend, then shoot.
            if (m_NextShot < 0f && Time.time >= m_NextSwitch)
            {
                m_Player.ThirdPerson = (m_Index % 2) == 1;
                m_NextShot = Time.time + 0.6f;
            }
            if (m_NextShot >= 0f && Time.time >= m_NextShot)
            {
                string view = m_Player.ThirdPerson ? "tps" : "fps";
                Save(rig.MainCamera, $"{m_Index:D3}_{view}");
                m_Index++;
                m_NextShot = -1f;
                m_NextSwitch = Time.time + m_Interval - 0.6f;
            }
        }

        void CaptureOverview()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.name != "SpectatorCamera") continue;
                var prev = cam.targetTexture;
                cam.targetTexture = m_Rt;
                Save(cam, "overview");
                cam.targetTexture = prev;
                return;
            }
        }

        void Save(Camera cam, string name)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = m_Rt;
            m_Tex.ReadPixels(new Rect(0, 0, m_W, m_H), 0, 0);
            m_Tex.Apply(false);
            RenderTexture.active = prev;
            string path = Path.Combine(m_Dir, name + ".png");
            File.WriteAllBytes(path, m_Tex.EncodeToPNG());
            Captured++;
            Debug.Log($"[SALTLINE] Captured {path}");
        }

        void OnDestroy()
        {
            if (m_Rt != null) m_Rt.Release();
        }
    }
}
