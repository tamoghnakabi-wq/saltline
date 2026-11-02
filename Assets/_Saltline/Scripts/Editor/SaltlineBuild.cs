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
            ContentFactory.Build();
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
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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
            return s.result == BuildResult.Succeeded;
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
