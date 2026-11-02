using System.Collections;
using System.Diagnostics;
using System.IO;
using Saltline.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Offline visual review of a map: loads the scene without networking, renders every
    /// "CaptureViews/VIEW_*" viewpoint with the scene's spectator camera (post-processing on)
    /// and quits. Needs graphics, so run in batch mode without -nographics:
    ///
    ///   SALTLINE -batchmode -maptour Kappa -capture &lt;dir&gt; [-capturesize 1920x1080]
    ///
    /// Each view is also rendered a few extra times to report the average time Camera.Render
    /// took on this machine (a rough CPU-side cost, not a frame-rate measurement).
    /// </summary>
    public sealed class MapTour : MonoBehaviour
    {
        IEnumerator Start()
        {
            string scene = CommandLine.Get("maptour", "Kappa");
            string dir = CommandLine.Get("capture", "MapTour");
            Directory.CreateDirectory(dir);
            int w = 1920, h = 1080;
            var parts = CommandLine.Get("capturesize", "1920x1080").Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int pw) && int.TryParse(parts[1], out int ph)) { w = pw; h = ph; }

            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"[SALTLINE] MapTour: scene '{scene}' is not in the build.");
                Application.Quit(2);
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(scene);
            for (int i = 0; i < 3; i++) yield return null;

            Camera cam = null;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.name == "SpectatorCamera") cam = c;
            var views = GameObject.Find("CaptureViews");
            if (cam == null || views == null)
            {
                Debug.LogError("[SALTLINE] MapTour: scene has no SpectatorCamera or CaptureViews.");
                Application.Quit(3);
                yield break;
            }

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            int saved = 0;
            bool sceneFog = RenderSettings.fog;
            var sw = new Stopwatch();
            foreach (Transform v in views.transform)
            {
                bool ortho = v.localScale.x > 1.01f;
                cam.orthographic = ortho;
                if (ortho) cam.orthographicSize = v.localScale.x;
                // Distance fog from 150 m up would wash out a plan view.
                RenderSettings.fog = !ortho && sceneFog;
                cam.transform.SetPositionAndRotation(v.position, v.rotation);
                // Let anything frame-dependent (auto exposure, TAA history) settle.
                for (int i = 0; i < 2; i++) { cam.Render(); yield return null; }
                sw.Restart();
                const int timed = 5;
                for (int i = 0; i < timed; i++) cam.Render();
                sw.Stop();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                string name = v.name.Replace("VIEW_", "");
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                saved++;
                Debug.Log($"[SALTLINE] MapTour {name}: {sw.Elapsed.TotalMilliseconds / timed:F1} ms per Camera.Render at {w}x{h}");
                yield return null;
            }
            RenderSettings.fog = sceneFog;
            cam.targetTexture = null;
            rt.Release();
            Debug.Log($"[SALTLINE] MapTour done: {saved} views of {scene} ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}')");
            Application.Quit(0);
        }
    }
}
