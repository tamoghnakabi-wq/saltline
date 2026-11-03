using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Saltline.Combat;
using Saltline.Core;
using Saltline.FX;
using Saltline.Player;
using Saltline.Settings;
using Saltline.Sim;
using Saltline.Weapons;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Frame-time benchmark of a map, offline (no networking), in a real window:
    ///
    ///   SALTLINE -benchmark Kappa -benchout &lt;dir&gt; [-benchpresets Low,Medium,High,Ultra,Laptop]
    ///            [-benchsize 1920x1080 | -benchfullscreen] [-benchrepeat] [-benchdwell 2.5] [-benchnowarmup]
    ///
    /// Eight operators (four per team) run and fire at points across the map with their real
    /// weapons, effects and lights. The camera carries the first-person arms and a rifle that
    /// fires in bursts, like a player's view. It flies through the map's eye-height review
    /// viewpoints, holding at each one while it pans, and every preset flies the same path.
    ///
    /// Before each preset is measured, every viewpoint is drawn once so shaders and textures are
    /// ready. Then each frame's interval and the CPU and GPU times from FrameTimingManager are
    /// written to bench_&lt;preset&gt;.csv, and a summary line per preset goes to the log. VSync
    /// and the frame cap are off. -benchrepeat measures the first preset again at the end, to
    /// show how much the machine slowed down as it warmed up. -benchnowarmup skips the warm-up,
    /// so the first run shows the hitches a player sees while shaders are compiled for the first time.
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        /// <summary>Eye-height viewpoints in an order that keeps the flights between them short.</summary>
        static readonly string[] PathViews =
        {
            "02_red_spawn", "03_red_exit", "14_north_staging", "07_pans", "08_catwalk", "15_hut_window", "09_channel",
            "05_pump_ground", "06_pump_upper", "04_mid_courtyard", "19_detail_container", "10_shed", "13_dock_yard",
            "11_dock", "12_cliff_road",
        };
        /// <summary>Where the eight operators fight (one each), and which way they shoot.</summary>
        static readonly string[] OperatorViews =
        {
            "03_red_exit", "14_north_staging", "07_pans", "09_channel", "05_pump_ground", "04_mid_courtyard", "13_dock_yard", "11_dock",
        };
        static readonly WeaponClass[] OperatorWeapons =
        {
            WeaponClass.AssaultRifle, WeaponClass.SMG, WeaponClass.Shotgun, WeaponClass.AssaultRifle,
            WeaponClass.Sniper, WeaponClass.SMG, WeaponClass.Pistol, WeaponClass.AssaultRifle,
        };

        const float TransitSpeed = 8f;    // m/s between viewpoints
        const float MaxTransit = 3f;      // s
        const float PanDegrees = 24f;     // camera pans this far each side while holding at a view

        struct Waypoint { public Vector3 Pos; public Quaternion Rot; }

        sealed class Fighter
        {
            public OperatorRig Rig;
            public WeaponDefinition Weapon;
            public Transform Muzzle, Eject;
            public WeaponRig GunRig;
            public Vector3 Centre, Across, Target;
            public float Phase, NextBurst;
            public int BurstLeft;
            public float NextShot;
            public RenderPose Pose;
        }

        readonly List<Waypoint> m_Path = new();
        readonly List<float> m_SegmentStart = new();
        float m_Dwell = 2.5f, m_PathLength;
        readonly List<Fighter> m_Fighters = new();
        Camera m_Cam;
        ExposureAdaptation m_Adapt;
        FxManager m_Fx;
        WeaponDefinition m_LocalWeapon;
        Transform m_LocalMuzzle, m_LocalEject;
        WeaponRig m_LocalRig;
        FirstPersonArms m_Arms;
        float m_LocalNextBurst;
        int m_LocalBurstLeft;
        float m_LocalNextShot;
        float m_Clock;

        IEnumerator Start()
        {
            string scene = CommandLine.Get("benchmark", "Kappa");
            string dir = CommandLine.Get("benchout", "Benchmark");
            Directory.CreateDirectory(dir);
            if (float.TryParse(CommandLine.Get("benchdwell", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var dw) && dw > 0f) m_Dwell = dw;

            var runs = new List<(QualityPreset preset, string variant, string tag)>();
            foreach (var entry in CommandLine.Get("benchpresets", "Low,Medium,High,Ultra,Laptop").Split(','))
            {
                var s = entry.Trim();
                int colon = s.IndexOf(':');
                string name = colon >= 0 ? s.Substring(0, colon) : s, variant = colon >= 0 ? s.Substring(colon + 1) : "";
                if (Enum.TryParse(name, true, out QualityPreset p)) runs.Add((p, variant, p + (variant.Length > 0 ? "_" + variant.Replace('=', '-').Replace(';', '_') : "")));
                else Debug.LogWarning($"[SALTLINE] Benchmark: unknown preset '{name}' skipped");
            }
            if (runs.Count == 0) { Fail("no valid presets in -benchpresets"); yield break; }
            foreach (var r in runs)   // a typo fails now, not after the runs before it
                if (!ParseVariant(r.preset, r.variant, out _, out _, out _, out _)) { Fail($"bad variant '{r.variant}' (write key=value;key=value, runs separated by commas)"); yield break; }
            if (CommandLine.Has("benchrepeat")) runs.Add((runs[0].preset, runs[0].variant, runs[0].tag + "_repeat"));

            if (!Application.CanStreamedLevelBeLoaded(scene)) { Fail($"scene '{scene}' is not in the build"); yield break; }

            if (CommandLine.Has("benchfullscreen"))
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            else
            {
                int w = 1920, h = 1080;
                var parts = CommandLine.Get("benchsize", "1920x1080").Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int pw) && int.TryParse(parts[1], out int ph)) { w = pw; h = ph; }
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
            // As a player would spend a moment in the menu: let the shader warm-up finish first.
            float warmStart = Time.realtimeSinceStartup;
            while (!ShaderWarmup.Done) yield return null;
            Debug.Log($"[SALTLINE] Benchmark: shader warm-up waited {Time.realtimeSinceStartup - warmStart:F2} s");
            // -benchmenuwait s: stay that long longer before loading the map, as a player in the menu would.
            float menuWait = CommandLine.GetFloat("benchmenuwait", 0f);
            for (float until = Time.realtimeSinceStartup + menuWait; Time.realtimeSinceStartup < until;) yield return null;
            float loadStart = Time.realtimeSinceStartup;
            yield return SceneManager.LoadSceneAsync(scene);
            float loaded = Time.realtimeSinceStartup, prev = loaded, firstSlowest = 0f;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                firstSlowest = Mathf.Max(firstSlowest, Time.realtimeSinceStartup - prev);
                prev = Time.realtimeSinceStartup;
            }
            Debug.Log($"[SALTLINE] Benchmark: {scene} loaded in {loaded - loadStart:F2} s ({loaded:F1} s after launch); " +
                      $"slowest of its first 10 frames {firstSlowest * 1000f:F0} ms");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            if (!Setup(out string error)) { Fail(error); yield break; }
            Debug.Log($"[SALTLINE] Benchmark: {scene} at {Screen.width}x{Screen.height} ({Screen.fullScreenMode}), {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                      $"{SystemInfo.processorType}, {SystemInfo.systemMemorySize} MB, battery {SystemInfo.batteryStatus} {SystemInfo.batteryLevel:P0}; " +
                      $"{m_Path.Count} viewpoints, {m_PathLength:F1} s per run; {m_Fighters.Count} operators; Unity quality level {QualitySettings.names[QualitySettings.GetQualityLevel()]}; " +
                      $"build {BuildInfo.Version}{(Debug.isDebugBuild ? " (development)" : "")}");

            string summaryPath = Path.Combine(dir, "summary.csv");
            var summary = new StringBuilder();
            summary.AppendLine("run,preset,variant,width,height,frames,seconds,avg_fps,median_ms,p95_ms,p99_ms,low1_fps,max_ms,cpu_ms,cpu_main_ms,cpu_render_ms,gpu_ms,gpu_p95_ms,batches,setpass,tris_k,shadow_casters,render_scale");
            for (int run = 0; run < runs.Count; run++)
            {
                var (p, variant, tag) = runs[run];
                m_Running = tag;
                if (!ApplyRun(p, variant, out string applied)) { Fail($"bad variant '{variant}'"); yield break; }
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                bool warm = !CommandLine.Has("benchnowarmup");
                if (warm)
                {
                    yield return WarmUp(tag);
                    yield return Screenshot(Path.Combine(dir, $"shot_{tag}.png"));
                }
                var result = new RunResult();
                yield return Measure(result, settle: warm);
                if (!warm) yield return Screenshot(Path.Combine(dir, $"shot_{tag}.png"));
                File.WriteAllText(Path.Combine(dir, $"bench_{tag}.csv"), result.Csv.ToString());
                summary.AppendLine(result.SummaryCsv(tag, p.ToString(), variant, run));
                File.WriteAllText(summaryPath, summary.ToString());   // after every run, so an interrupted benchmark keeps its results
                Debug.Log($"[SALTLINE] Benchmark {tag} ({applied}): {result.SummaryText()}");
            }
            m_Running = null;
            VideoSettings.PreviewPreset(null);
            Debug.Log($"[SALTLINE] Benchmark done: {runs.Count} runs written to {dir}");
            Application.Quit(0);
        }

        string m_Running;

        void OnApplicationQuit()
        {
            if (m_Running != null) Debug.LogWarning($"[SALTLINE] Benchmark interrupted during run '{m_Running}' (the window was closed or the app was quit)");
        }

        /// <summary>
        /// A preset, optionally with single values changed for testing what they cost, written
        /// key=value;key=value: ao (0-3), msaa, shadow (distance, 0 = none), cascades, shadowres, soft (1-3),
        /// hdr, post, scale, lod (LOD Group bias), meshlod (Mesh LOD threshold), occl (occlusion culling 0/1),
        /// aa (post anti-aliasing: 0 none, 1 FXAA, 2 SMAA), bloom (0/1).
        /// </summary>
        bool ApplyRun(QualityPreset p, string variant, out string applied)
        {
            applied = null;
            if (!ParseVariant(p, variant, out var v, out float scale, out bool occl, out bool bloom)) return false;
            VideoSettings.PreviewPreset(p);
            VideoSettings.Apply(v, scale);
            m_Cam.useOcclusionCulling = occl;
            SetBloom(bloom);
            applied = $"scale {scale:0.00}, MSAA {v.Msaa}, post AA {v.PostAa}, bloom {(bloom ? "on" : "off")}, shadows {v.ShadowDistance:0} m / {v.Cascades} cascades / {v.ShadowResolution} / soft {v.SoftShadows}, " +
                      $"HDR {(v.Hdr ? "on" : "off")}, post {(v.PostFx ? "on" : "off")}, SSAO {v.Ao}, LOD bias {v.LodBias:0.0}, mesh LOD threshold {v.MeshLodThreshold:0.0}, " +
                      $"occlusion culling {(occl ? "on" : "off")}";
            return true;
        }

        static bool ParseVariant(QualityPreset p, string variant, out VideoSettings.PresetValues v, out float scale, out bool occl, out bool bloom)
        {
            v = VideoSettings.Values(p);
            scale = v.RenderScale;
            occl = true;
            bloom = true;
            foreach (var kv in variant.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=');
                if (parts.Length != 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
                switch (parts[0].Trim().ToLowerInvariant())
                {
                    case "ao": v.Ao = (VideoSettings.AoQuality)Mathf.Clamp((int)x, 0, 3); break;
                    case "msaa": v.Msaa = (int)x; break;
                    case "shadow": v.ShadowDistance = x; break;
                    case "cascades": v.Cascades = Mathf.Clamp((int)x, 1, 4); break;
                    case "shadowres": v.ShadowResolution = (int)x; break;
                    case "soft": v.SoftShadows = (SoftShadowQuality)Mathf.Clamp((int)x, 1, 3); break;
                    case "hdr": v.Hdr = x != 0f; break;
                    case "post": v.PostFx = x != 0f; break;
                    case "scale": scale = x; break;
                    case "lod": v.LodBias = x; break;
                    case "meshlod": v.MeshLodThreshold = x; break;
                    case "occl": occl = x != 0f; break;
                    case "aa": v.PostAa = (int)x switch { 1 => AntialiasingMode.FastApproximateAntialiasing, 2 => AntialiasingMode.SubpixelMorphologicalAntiAliasing, _ => AntialiasingMode.None }; break;
                    case "bloom": bloom = x != 0f; break;
                    default: return false;
                }
            }
            return true;
        }

        /// <summary>Bloom on or off in every volume of the map (only for measuring what it costs).</summary>
        static void SetBloom(bool on)
        {
            foreach (var vol in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
                if (vol.sharedProfile != null && vol.sharedProfile.TryGet<Bloom>(out var b)) b.active = on;
        }

        /// <summary>One frame of the courtyard view to the PNG, for checking what each run looked like.</summary>
        IEnumerator Screenshot(string path)
        {
            int i = Mathf.Max(0, Array.IndexOf(PathViews, "04_mid_courtyard"));
            float t = i < m_SegmentStart.Count ? m_SegmentStart[i] + m_Dwell * 0.25f : 0f;
            for (int k = 0; k < 20; k++)
            {
                m_Clock += Time.unscaledDeltaTime;
                PlaceCamera(t);
                Simulate(m_Clock, Time.unscaledDeltaTime);
                if (k == 0) m_Adapt.Snap();
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Destroy(shot);
        }

        static void Fail(string why)
        {
            Debug.LogError($"[SALTLINE] Benchmark: {why}");
            Application.Quit(2);
        }

        bool Setup(out string error)
        {
            error = null;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.name == "SpectatorCamera") m_Cam = c;
            var views = GameObject.Find("CaptureViews");
            var lib = PresentationLibrary.Instance;
            if (m_Cam == null || views == null) { error = "scene has no SpectatorCamera or CaptureViews"; return false; }
            if (lib == null || lib.operatorPrefab == null || lib.weapons == null) { error = "no presentation library"; return false; }

            // The gameplay camera's settings (PlayerCameraRig), with the first-person weapon on an overlay camera.
            m_Cam.enabled = true;
            m_Cam.targetTexture = null;
            m_Cam.orthographic = false;
            m_Cam.fieldOfView = 75f;
            m_Cam.nearClipPlane = 0.03f;
            m_Cam.farClipPlane = 800f;
            m_Cam.cullingMask = ~(1 << Layers.Viewmodel);
            m_Adapt = m_Cam.GetComponent<ExposureAdaptation>();
            if (m_Adapt == null) m_Adapt = m_Cam.gameObject.AddComponent<ExposureAdaptation>();
            var vmGo = new GameObject("BenchViewmodelCamera");
            vmGo.transform.SetParent(m_Cam.transform, false);
            var vm = vmGo.AddComponent<Camera>();
            vm.cullingMask = 1 << Layers.Viewmodel;
            vm.nearClipPlane = 0.01f;
            vm.farClipPlane = 10f;
            vm.fieldOfView = 56f;
            var vmData = vm.GetUniversalAdditionalCameraData();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            m_Cam.GetUniversalAdditionalCameraData().cameraStack.Add(vm);

            FxManager.Ensure();
            m_Fx = FxManager.Instance;
            if (m_Fx == null) { error = "no FxManager"; return false; }

            WeaponDefinition Def(WeaponClass c)
            {
                foreach (var w in lib.weapons.weapons) if (w != null && w.weaponClass == c) return w;
                return null;
            }

            // First-person rifle and arms.
            m_LocalWeapon = Def(WeaponClass.AssaultRifle);
            if (m_LocalWeapon != null && WeaponModel.HasModel(WeaponClass.AssaultRifle))
            {
                var holder = new GameObject("BenchViewmodel").transform;
                holder.gameObject.layer = Layers.Viewmodel;
                holder.SetParent(vmGo.transform, false);
                var gun = WeaponModel.Build(WeaponClass.AssaultRifle, holder, Layers.Viewmodel, WeaponView.FirstPerson, out m_LocalMuzzle, out _, out m_LocalRig);
                gun.localPosition = m_LocalWeapon.viewmodelOffset;
                m_LocalEject = gun.Find("Eject");
                m_Fx.SetViewmodelGun(m_LocalMuzzle, m_LocalEject);
                if (lib.armsPrefab != null)
                {
                    m_Arms = Instantiate(lib.armsPrefab, holder, false).GetComponent<FirstPersonArms>();
                    m_Arms.SetLayer(Layers.Viewmodel);
                    m_Arms.SetTeamMaterial(lib.armsTeamBlue);
                    m_Arms.Bind(gun, m_LocalRig, WeaponClass.AssaultRifle, m_LocalWeapon.viewmodelOffset);
                }
            }

            // Camera path.
            foreach (var n in PathViews)
            {
                var v = views.transform.Find("VIEW_" + n);
                if (v != null) m_Path.Add(new Waypoint { Pos = v.position, Rot = v.rotation });
            }
            if (m_Path.Count < 2) { error = "fewer than two path viewpoints found"; return false; }
            float t = 0f;
            for (int i = 0; i < m_Path.Count; i++)
            {
                m_SegmentStart.Add(t);
                t += m_Dwell;
                if (i + 1 < m_Path.Count) t += Transit(i);
            }
            m_PathLength = t;

            // Eight operators, each running back and forth across a viewpoint and firing toward it.
            for (int i = 0; i < OperatorViews.Length; i++)
            {
                var v = views.transform.Find("VIEW_" + OperatorViews[i]);
                var w = Def(OperatorWeapons[i]);
                if (v == null || w == null || !WeaponModel.HasModel(w.weaponClass)) continue;
                Vector3 flat = v.forward;
                flat.y = 0f;
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                var f = new Fighter
                {
                    Weapon = w,
                    Centre = Ground(v.position + flat * 11f),
                    Across = Vector3.Cross(Vector3.up, flat),
                    Target = v.position - Vector3.up * 0.3f,
                    Phase = i * 0.37f,
                    NextBurst = 0.4f + i * 0.31f,
                };
                var body = Instantiate(lib.operatorPrefab);
                body.name = $"BenchOperator{i}";
                f.Rig = body.GetComponent<OperatorRig>();
                f.Rig.SetTeamMaterial(i % 2 == 0 ? lib.operatorTeamRed : lib.operatorTeamBlue);
                var gun = WeaponModel.Build(w.weaponClass, body.transform, 0, WeaponView.ThirdPerson, out f.Muzzle, out _, out f.GunRig);
                f.Rig.SetWeapon(gun, f.GunRig, w.weaponClass);
                f.Eject = gun.Find("Eject");
                f.Rig.Driven = true;
                m_Fighters.Add(f);
            }
            return true;
        }

        float Transit(int i) => Mathf.Min(MaxTransit, Vector3.Distance(m_Path[i].Pos, m_Path[i + 1].Pos) / TransitSpeed);

        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out var h, 8f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return h.point;
            return p - Vector3.up * 1.62f;
        }

        void PlaceCamera(float t)
        {
            int i = m_SegmentStart.Count - 1;
            while (i > 0 && t < m_SegmentStart[i]) i--;
            float local = t - m_SegmentStart[i];
            var a = m_Path[i];
            if (local < m_Dwell || i + 1 >= m_Path.Count)
            {
                float pan = Mathf.Sin(Mathf.Clamp01(local / m_Dwell) * Mathf.PI * 2f) * PanDegrees;
                m_Cam.transform.SetPositionAndRotation(a.Pos, Quaternion.AngleAxis(pan, Vector3.up) * a.Rot);
                return;
            }
            var b = m_Path[i + 1];
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((local - m_Dwell) / Transit(i)));
            m_Cam.transform.SetPositionAndRotation(Vector3.Lerp(a.Pos, b.Pos, s), Quaternion.Slerp(a.Rot, b.Rot, s));
        }

        /// <summary>Operators, their fire and the first-person rifle, all on the benchmark clock.</summary>
        void Simulate(float t, float dt)
        {
            foreach (var f in m_Fighters)
            {
                // Back and forth over 7 m at running speed, turning at the ends.
                float u = Mathf.Sin((t + f.Phase * 10f) * 0.7f);
                float du = Mathf.Cos((t + f.Phase * 10f) * 0.7f) * 0.7f;
                Vector3 feet = Ground(f.Centre + f.Across * (u * 3.5f));
                Vector3 vel = f.Across * (du * 3.5f);
                Vector3 aimDir = (f.Target - (feet + Vector3.up * 1.45f)).normalized;
                f.Pose = new RenderPose
                {
                    Feet = feet, Velocity = vel, Yaw = Quaternion.LookRotation(new Vector3(aimDir.x, 0f, aimDir.z)).eulerAngles.y,
                    Pitch = Mathf.Asin(Mathf.Clamp(aimDir.y, -1f, 1f)) * Mathf.Rad2Deg, Height = 1.8f, Stance = Stance.Standing,
                    Flags = MotorFlags.Grounded, Action = WeaponAction.Ready, ActionProgress = 1f, Ads = 0f, Alive = true,
                };
                f.Rig.Pose = f.Pose;
                if (f.GunRig != null) f.GunRig.Tick(WeaponAction.Ready, 1f, false, false, f.BurstLeft > 0, dt);

                if (t >= f.NextBurst && f.BurstLeft == 0)
                {
                    bool single = f.Weapon.fireMode != FireMode.Automatic;
                    f.BurstLeft = single ? 1 : 5;
                    f.NextShot = t;
                    f.NextBurst = t + (single ? 1.6f : 1.4f);
                }
                if (f.BurstLeft > 0 && t >= f.NextShot)
                {
                    f.BurstLeft--;
                    f.NextShot = t + f.Weapon.FireInterval;
                    Fire(f.Weapon, f.Muzzle != null ? f.Muzzle.position : feet + Vector3.up * 1.4f, f.Target, f.Eject, vel, f.GunRig, false);
                }
            }

            if (m_LocalWeapon != null && m_LocalMuzzle != null)
            {
                if (t >= m_LocalNextBurst && m_LocalBurstLeft == 0) { m_LocalBurstLeft = 6; m_LocalNextShot = t; m_LocalNextBurst = t + 3f; }
                bool trigger = m_LocalBurstLeft > 0;
                if (m_LocalRig != null) m_LocalRig.Tick(WeaponAction.Ready, 1f, false, false, trigger, dt);
                if (m_Arms != null) m_Arms.Pose(WeaponAction.Ready, 1f, trigger, true, dt);
                if (m_LocalBurstLeft > 0 && t >= m_LocalNextShot)
                {
                    m_LocalBurstLeft--;
                    m_LocalNextShot = t + m_LocalWeapon.FireInterval;
                    Vector3 fwd = m_Cam.transform.forward;
                    m_Fx.MuzzleFirstPerson(m_LocalWeapon, fwd, Vector3.zero, 0f);
                    Vector3 end = m_Cam.transform.position + fwd * 120f;
                    if (Physics.Raycast(m_Cam.transform.position, fwd, out var h, 120f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    {
                        end = h.point;
                        m_Fx.Impact(h.point, h.normal, fwd, SurfaceTag.TypeOf(h.collider));
                    }
                    m_Fx.SpawnLight(m_LocalMuzzle.position, fwd, m_LocalWeapon);
                    m_Fx.SpawnTracer(m_LocalMuzzle.position, end, m_LocalWeapon);
                    if (m_LocalEject != null) m_Fx.EjectShell(m_LocalEject, WeaponClass.AssaultRifle, Vector3.zero, true);
                    if (m_LocalRig != null) m_LocalRig.OnShot(m_LocalWeapon.FireInterval);
                }
            }
        }

        void Fire(WeaponDefinition w, Vector3 muzzle, Vector3 target, Transform eject, Vector3 vel, WeaponRig rig, bool viewmodel)
        {
            Vector3 aim = (target - muzzle).normalized;
            m_Fx.MuzzleWorld(muzzle, aim, w, vel);
            m_Fx.SpawnLight(muzzle, aim, w);
            if (eject != null && FxManager.EjectsOnShot(w.weaponClass)) m_Fx.EjectShell(eject, w.weaponClass, vel, viewmodel);
            if (rig != null) rig.OnShot(w.FireInterval);
            int pellets = Mathf.Max(1, w.pellets);
            // Shots pass a metre or two beside the target and land on whatever is behind it.
            Vector3 side = Vector3.Cross(Vector3.up, aim).normalized;
            for (int k = 0; k < pellets; k++)
            {
                float spread = pellets > 1 ? 0.06f : 0.02f;
                Vector3 d = (aim + side * (0.12f + UnityEngine.Random.Range(-spread, spread)) + Vector3.up * UnityEngine.Random.Range(-spread, spread)).normalized;
                Vector3 end = muzzle + d * 150f;
                if (Physics.Raycast(muzzle, d, out var h, 150f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                {
                    end = h.point;
                    if (k < 4) m_Fx.Impact(h.point, h.normal, d, SurfaceTag.TypeOf(h.collider));
                }
                if (k == 0) m_Fx.SpawnTracer(muzzle, end, w);
            }
        }

        /// <summary>Draw every viewpoint once at the current preset so its shaders and textures are ready.</summary>
        IEnumerator WarmUp(string tag)
        {
            // The slowest warm-up frame shows how long first-time shader compiles take (compare a
            // run with the game's Metal shader cache moved aside with one that has it).
            float begin = Time.realtimeSinceStartup, last = begin, slowest = 0f;
            int frames = 0;
            void Tick()
            {
                float now = Time.realtimeSinceStartup;
                slowest = Mathf.Max(slowest, now - last);
                last = now;
                frames++;
            }
            for (int i = 0; i < m_SegmentStart.Count; i++)
            {
                PlaceCamera(m_SegmentStart[i] + m_Dwell * 0.25f);
                m_Adapt.Snap();
                yield return null;
                Tick();
                yield return null;
                Tick();
            }
            float t0 = Time.realtimeSinceStartup;
            m_Clock = 0f;
            while (Time.realtimeSinceStartup - t0 < 1.5f)
            {
                m_Clock += Time.unscaledDeltaTime;
                PlaceCamera(0f);
                Simulate(m_Clock, Time.unscaledDeltaTime);
                yield return null;
                Tick();
            }
            Debug.Log($"[SALTLINE] Benchmark warm-up {tag}: {frames} frames in {last - begin:F2} s, slowest frame {slowest * 1000f:F0} ms");
        }

        sealed class RunResult
        {
            public readonly StringBuilder Csv = new StringBuilder("frame,t,interval_ms,cpu_ms,cpu_main_ms,cpu_render_ms,gpu_ms\n");
            public readonly List<double> Interval = new(), Cpu = new(), CpuMain = new(), CpuRender = new(), Gpu = new();
            public double Seconds, Batches, SetPass, Tris, ShadowCasters;
            public int Samples, Width, Height;
            public float RenderScale;

            static double Pct(List<double> v, double q)
            {
                if (v.Count == 0) return double.NaN;
                var s = new List<double>(v);
                s.Sort();
                return s[Math.Clamp((int)Math.Ceiling(q * s.Count) - 1, 0, s.Count - 1)];
            }

            static double Avg(List<double> v)
            {
                if (v.Count == 0) return double.NaN;
                double sum = 0;
                foreach (var x in v) sum += x;
                return sum / v.Count;
            }

            /// <summary>1% low: the frame rate of the slowest 1% of frames, from their average interval.</summary>
            double Low1()
            {
                if (Interval.Count == 0) return double.NaN;
                var s = new List<double>(Interval);
                s.Sort();
                int n = Math.Max(1, s.Count / 100);
                double sum = 0;
                for (int i = s.Count - n; i < s.Count; i++) sum += s[i];
                return 1000.0 / (sum / n);
            }

            static string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.00", CultureInfo.InvariantCulture);
            static string T(double v, string unit) => double.IsNaN(v) ? "n/a" : v.ToString("0.0", CultureInfo.InvariantCulture) + unit;

            double Per(double total) => Samples > 0 ? total / Samples : double.NaN;

            public string SummaryCsv(string tag, string preset, string variant, int run) => string.Join(",", run, preset, variant.Replace(',', ' '), Width, Height, Interval.Count, F(Seconds),
                F(Interval.Count / Seconds), F(Pct(Interval, 0.5)), F(Pct(Interval, 0.95)), F(Pct(Interval, 0.99)), F(Low1()), F(Pct(Interval, 1.0)),
                F(Avg(Cpu)), F(Avg(CpuMain)), F(Avg(CpuRender)), F(Avg(Gpu)), F(Pct(Gpu, 0.95)),
                F(Per(Batches)), F(Per(SetPass)), F(Per(Tris) / 1000.0), F(Per(ShadowCasters)), F(RenderScale));

            public string SummaryText() =>
                $"{Interval.Count} frames in {Seconds:F1} s at {Width}x{Height} (render scale {RenderScale:F2}): {Interval.Count / Seconds:F1} fps average, " +
                $"frame interval median {T(Pct(Interval, 0.5), " ms")}, 95th {T(Pct(Interval, 0.95), " ms")}, 99th {T(Pct(Interval, 0.99), " ms")}, 1% low {T(Low1(), " fps")}, worst {T(Pct(Interval, 1.0), " ms")}; " +
                $"CPU {T(Avg(Cpu), " ms")} (main {T(Avg(CpuMain), " ms")}, render thread {T(Avg(CpuRender), " ms")}), GPU {T(Avg(Gpu), " ms")} average, 95th {T(Pct(Gpu, 0.95), " ms")}; " +
                $"{T(Per(Batches), "")} batches, {T(Per(SetPass), "")} set-pass calls, {T(Per(Tris) / 1000.0, "k")} triangles, {T(Per(ShadowCasters), "")} shadow casters per frame" +
                (Samples == 0 ? " (render counters not available in this build)" : "");
        }

        IEnumerator Measure(RunResult r, bool settle)
        {
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            using var casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            var timings = new FrameTiming[1];
            r.Width = Screen.width;
            r.Height = Screen.height;
            r.RenderScale = VideoSettings.EffectiveRenderScale;
            m_Clock = 0f;
            PlaceCamera(0f);
            m_Adapt.Snap();
            // The frames right after the screenshot (encoding it takes over 100 ms) and the jump back
            // to the start of the path are not part of the run. Without the warm-up there is no
            // screenshot first, and the first frames are measured: their shader compiles are the point.
            for (int i = 0; settle && i < 15; i++) { Simulate(0f, Time.unscaledDeltaTime); yield return null; }
            float start = Time.realtimeSinceStartup;
            int frame = 0;
            float last = Time.realtimeSinceStartup;
            while (m_Clock < m_PathLength)
            {
                float now = Time.realtimeSinceStartup;
                float dt = now - last;
                last = now;
                m_Clock += dt;
                PlaceCamera(m_Clock);
                Simulate(m_Clock, dt);

                FrameTimingManager.CaptureFrameTimings();
                double cpu = double.NaN, main = double.NaN, render = double.NaN, gpu = double.NaN;
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    cpu = timings[0].cpuFrameTime;
                    main = timings[0].cpuMainThreadFrameTime;
                    render = timings[0].cpuRenderThreadFrameTime;
                    gpu = timings[0].gpuFrameTime;
                }
                if (frame > 0)
                {
                    double ms = dt * 1000.0;
                    r.Interval.Add(ms);
                    if (cpu > 0) r.Cpu.Add(cpu);
                    if (main > 0) r.CpuMain.Add(main);
                    if (render > 0) r.CpuRender.Add(render);
                    if (gpu > 0) r.Gpu.Add(gpu);
                    r.Csv.Append(frame).Append(',').Append(m_Clock.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(ms.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(Num(cpu)).Append(',').Append(Num(main)).Append(',').Append(Num(render)).Append(',').Append(Num(gpu)).Append('\n');
                    if (batches.Valid && batches.LastValue > 0)
                    {
                        r.Samples++;
                        r.Batches += batches.LastValue;
                        r.SetPass += setPass.Valid ? setPass.LastValue : 0;
                        r.Tris += tris.Valid ? tris.LastValue : 0;
                        r.ShadowCasters += casters.Valid ? casters.LastValue : 0;
                    }
                }
                frame++;
                yield return null;
            }
            r.Seconds = Time.realtimeSinceStartup - start;
        }

        static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
