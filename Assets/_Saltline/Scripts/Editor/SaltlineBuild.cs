using System;
using System.IO;
using System.Linq;
using Saltline.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Command-line build entry points. Both platforms are built from the same scenes, assets
    /// and code with the same <see cref="BuildInfo.NetProtocol"/>, so they can share sessions.
    ///
    ///   Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildMac [-devbuild]
    ///   Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildWindows [-devbuild]
    /// </summary>
    public static class SaltlineBuild
    {
        public const string MacPath = "Builds/macOS/SALTLINE.app";
        public const string WinPath = "Builds/Windows/SALTLINE/SALTLINE.exe";

        [MenuItem("Saltline/Build/Setup (configure + boot scene)")]
        public static void Setup()
        {
            ProjectConfigurator.Apply();
            RenderingConfigurator.Apply();
            ContentFactory.Build();
            AudioFactory.Build();
            KappaSceneFactory.Build();
            MenuSceneFactory.Build();
            UiFactory.Build();
            BootSceneFactory.Build();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Saltline/Build/macOS (Apple Silicon)")]
        public static void BuildMac() => Run(BuildTarget.StandaloneOSX, MacPath);

        [MenuItem("Saltline/Build/Windows x64")]
        public static void BuildWindows() => Run(BuildTarget.StandaloneWindows64, WinPath);

        public static void BuildAll()
        {
            bool ok = Build(BuildTarget.StandaloneOSX, MacPath) & Build(BuildTarget.StandaloneWindows64, WinPath);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void Run(BuildTarget target, string path)
        {
            bool ok = Build(target, path);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build(BuildTarget target, string path)
        {
            bool dev = Environment.GetCommandLineArgs().Contains("-devbuild");
            if (target == BuildTarget.StandaloneOSX) SetMacArchitecture("ARM64");
            // Development builds (profiler, network simulator) go next to, never over, release builds.
            if (dev) path = path.Replace("Builds/macOS/", "Builds/macOS-dev/").Replace("Builds/Windows/", "Builds/Windows-dev/");

            BootSceneFactory.EnsureBuildScenes();
            KappaSceneFactory.EnsureLampEmission();
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Read the source state before building: the build itself can touch tracked files.
            string stampPath = StampPath(target, path);
            if (File.Exists(stampPath)) File.Delete(stampPath);
            string commit = Git("rev-parse HEAD");
            string status = Git("status --porcelain");
            var stamp = new BuildStamp
            {
                game = BuildInfo.GameName,
                version = BuildInfo.Version,
                protocol = BuildInfo.NetProtocol,
                target = target == BuildTarget.StandaloneOSX ? "macos-arm64" : "windows-x64",
                development = dev,
                commit = commit ?? "",
                dirty = commit == null || status == null || status.Length > 0,
                updateUrl = BuildInfo.UpdateManifestUrl,
                unity = Application.unityVersion,
            };

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log($"[SALTLINE] Building {target} v{BuildInfo.Version} proto {BuildInfo.NetProtocol} dev={dev} -> {path}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[SALTLINE] Build {target}: {s.result}, {s.totalSize / (1024f * 1024f):F1} MB, {s.totalTime.TotalSeconds:F0}s, errors={s.totalErrors}, warnings={s.totalWarnings}");
            if (s.result != BuildResult.Succeeded) return false;

            stamp.built = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(stampPath, JsonUtility.ToJson(stamp, true) + "\n");
            Debug.Log($"[SALTLINE] Wrote {stampPath} (commit {stamp.commit}, dirty={stamp.dirty})");
            return true;
        }

        /// <summary>What a build was made from. Tools/package_release.py refuses builds that don't match the release.</summary>
        [Serializable]
        sealed class BuildStamp
        {
            public string game, version;
            public int protocol;
            public string target;
            public bool development;
            public string commit;
            public bool dirty;
            public string built, updateUrl, unity;
        }

        /// <summary>Builds/macOS/SALTLINE.buildinfo.json, or Builds/Windows/SALTLINE.buildinfo.json (beside the game folder).</summary>
        static string StampPath(BuildTarget target, string path)
        {
            string dir = Path.GetDirectoryName(path)!;
            if (target != BuildTarget.StandaloneOSX) dir = Path.GetDirectoryName(dir)!;
            return Path.Combine(dir, "SALTLINE.buildinfo.json");
        }

        /// <summary>Runs git in the project folder; null if git is missing or fails.</summary>
        static string Git(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? output.Trim() : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SALTLINE] git {args} failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Sets OSXStandalone.UserBuildSettings.architecture by name (API type varies by version).</summary>
        static void SetMacArchitecture(string value)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("UnityEditor.OSXStandalone.UserBuildSettings");
                if (t == null) continue;
                var prop = t.GetProperty("architecture");
                if (prop == null) continue;
                try
                {
                    object enumValue = Enum.Parse(prop.PropertyType, value, true);
                    prop.SetValue(null, enumValue);
                    Debug.Log($"[SALTLINE] macOS architecture = {prop.GetValue(null)}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SALTLINE] Could not set macOS architecture: {e.Message}");
                }
                return;
            }
            Debug.LogWarning("[SALTLINE] OSXStandalone.UserBuildSettings not found; using default macOS architecture.");
        }
    }
}
