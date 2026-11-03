using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Applies the project-wide player settings for both Windows x64 and macOS ARM64.
    /// Idempotent; run from batch mode: -executeMethod Saltline.EditorTools.ProjectConfigurator.Apply
    /// </summary>
    public static class ProjectConfigurator
    {
        public const string Company = "Saltline Studio";
        public const string Product = "SALTLINE";
        public const string BundleId = "com.tamoghnakabi.saltline";

        [MenuItem("Saltline/Configure Project")]
        public static void Apply()
        {
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.bundleVersion = Saltline.Core.BuildInfo.Version;

            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.forceSingleInstance = false; // local multi-instance testing needs this off
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SplashScreen.show = false;

            // High-DPI: render at native backing resolution on Retina displays.
            PlayerSettings.macRetinaSupport = true;

            // Mono for both desktop targets: IL2CPP for Windows can only be built on Windows,
            // and one backend keeps behaviour identical across platforms.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);

            // Graphics APIs: D3D12 first with D3D11 fallback on Windows, Metal on macOS.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D12, UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { UnityEngine.Rendering.GraphicsDeviceType.Metal });

            PlayerSettings.gcIncremental = true;
            // CPU and GPU frame times for the benchmark and the debug overlay (FrameTimingManager).
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.stripEngineCode = false;

            Branding.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log($"[SALTLINE] Project configured: {Product} {PlayerSettings.bundleVersion}");
        }
    }
}
