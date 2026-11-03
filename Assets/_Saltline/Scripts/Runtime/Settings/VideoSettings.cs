using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Saltline.Settings
{
    public enum QualityPreset
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
        /// <summary>Thin-and-light laptops such as a MacBook Air: lower render scale, fewer shadow cascades.</summary>
        Laptop = 4,
    }

    /// <summary>
    /// Display mode, resolution, frame pacing and rendering quality. Presets adjust the active URP
    /// asset at runtime, so one build serves every machine. Saved in PlayerPrefs.
    /// </summary>
    public static class VideoSettings
    {
        const string P = "saltline.video.";
        public static event Action Changed;
        static bool s_Hooked;

        public static readonly string[] PresetNames = { "Low", "Medium", "High", "Ultra", "Laptop (MacBook Air class)" };
        public static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };

        public readonly struct PresetValues
        {
            public readonly float RenderScale;
            public readonly int Msaa;
            public readonly float ShadowDistance;
            public readonly int Cascades;
            public readonly bool Hdr;
            public readonly bool PostFx;
            public readonly AnisotropicFiltering Aniso;
            public readonly int TextureMipLimit;
            public readonly int ShadowResolution;

            public PresetValues(float scale, int msaa, float shadow, int cascades, bool hdr, bool post, AnisotropicFiltering aniso, int mip, int shadowRes)
            {
                RenderScale = scale; Msaa = msaa; ShadowDistance = shadow; Cascades = cascades; Hdr = hdr; PostFx = post; Aniso = aniso; TextureMipLimit = mip; ShadowResolution = shadowRes;
            }
        }

        public static PresetValues Values(QualityPreset p) => p switch
        {
            QualityPreset.Low => new PresetValues(0.75f, 1, 35f, 1, false, false, AnisotropicFiltering.Disable, 1, 1024),
            QualityPreset.Medium => new PresetValues(0.9f, 2, 60f, 2, true, true, AnisotropicFiltering.Enable, 0, 2048),
            QualityPreset.High => new PresetValues(1f, 4, 90f, 4, true, true, AnisotropicFiltering.ForceEnable, 0, 4096),
            QualityPreset.Ultra => new PresetValues(1f, 4, 130f, 4, true, true, AnisotropicFiltering.ForceEnable, 0, 4096),
            _ => new PresetValues(0.8f, 1, 55f, 2, true, true, AnisotropicFiltering.Enable, 0, 2048),
        };

        public static QualityPreset Preset
        {
            get => (QualityPreset)PlayerPrefs.GetInt(P + "preset", (int)DetectDefault());
            set { PlayerPrefs.SetInt(P + "preset", (int)value); ApplyQuality(); }
        }

        /// <summary>0 = use the preset's render scale.</summary>
        public static float RenderScaleOverride
        {
            get => PlayerPrefs.GetFloat(P + "scale", 0f);
            set { PlayerPrefs.SetFloat(P + "scale", value <= 0f ? 0f : Mathf.Clamp(value, 0.5f, 1f)); ApplyQuality(); }
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt(P + "vsync", 1) != 0;
            set { PlayerPrefs.SetInt(P + "vsync", value ? 1 : 0); ApplyFramePacing(); }
        }

        /// <summary>0 = unlimited (or display rate with VSync).</summary>
        public static int FrameCap
        {
            get => PlayerPrefs.GetInt(P + "cap", 0);
            set { PlayerPrefs.SetInt(P + "cap", Mathf.Max(0, value)); ApplyFramePacing(); }
        }

        public static FullScreenMode DisplayMode
        {
            get => (FullScreenMode)PlayerPrefs.GetInt(P + "mode", (int)FullScreenMode.FullScreenWindow);
            set { PlayerPrefs.SetInt(P + "mode", (int)value); ApplyDisplay(); }
        }

        public static void SetResolution(int width, int height)
        {
            PlayerPrefs.SetInt(P + "w", width);
            PlayerPrefs.SetInt(P + "h", height);
            ApplyDisplay();
        }

        public static Vector2Int SavedResolution => new(PlayerPrefs.GetInt(P + "w", 0), PlayerPrefs.GetInt(P + "h", 0));

        public static bool PostProcessing => Values(Preset).PostFx;

        /// <summary>Particle budget for weapon and impact effects (1 = full).</summary>
        public static float EffectsDetail => Preset switch
        {
            QualityPreset.Low => 0.4f,
            QualityPreset.Medium => 0.7f,
            QualityPreset.Laptop => 0.7f,
            _ => 1f,
        };
        public static float EffectiveRenderScale => RenderScaleOverride > 0f ? RenderScaleOverride : Values(Preset).RenderScale;

        /// <summary>First launch: Apple-silicon laptops start on the laptop preset, everything else on High.</summary>
        public static QualityPreset DetectDefault()
        {
            bool apple = SystemInfo.graphicsDeviceName != null && SystemInfo.graphicsDeviceName.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0;
            bool laptop = SystemInfo.batteryStatus != BatteryStatus.Unknown;
            if (apple && laptop) return QualityPreset.Laptop;
            if (SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 3000) return QualityPreset.Medium;
            return QualityPreset.High;
        }

        public static void ApplyAll()
        {
            if (!s_Hooked)
            {
                s_Hooked = true;
                SceneManager.sceneLoaded += (_, _) => ApplyCameras();
            }
            ApplyDisplay();
            ApplyFramePacing();
            ApplyQuality();
        }

        public static void ApplyDisplay()
        {
            if (Application.isBatchMode || Application.isEditor) { Changed?.Invoke(); return; }
            var res = SavedResolution;
            var mode = DisplayMode;
#if !UNITY_STANDALONE_WIN
            if (mode == FullScreenMode.ExclusiveFullScreen) mode = FullScreenMode.FullScreenWindow;
#endif
            if (res.x > 0 && res.y > 0) Screen.SetResolution(res.x, res.y, mode);
            else Screen.fullScreenMode = mode;
            Changed?.Invoke();
        }

        public static void ApplyFramePacing()
        {
            if (Application.isBatchMode) return; // tests pin their own frame rate
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            Changed?.Invoke();
        }

        public static void ApplyQuality()
        {
            var v = Values(Preset);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = EffectiveRenderScale;
                urp.upscalingFilter = UpscalingFilterSelection.FSR;
                urp.msaaSampleCount = v.Msaa;
                urp.shadowDistance = v.ShadowDistance;
                urp.shadowCascadeCount = v.Cascades;
                urp.mainLightShadowmapResolution = v.ShadowResolution;
                urp.supportsHDR = v.Hdr;
            }
            QualitySettings.anisotropicFiltering = v.Aniso;
            QualitySettings.globalTextureMipmapLimit = v.TextureMipLimit;
            ApplyCameras();
            Changed?.Invoke();
        }

        static void ApplyCameras()
        {
            bool post = PostProcessing;
            foreach (var cam in Camera.allCameras)
            {
                if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType == CameraRenderType.Base)
                    data.renderPostProcessing = post;
            }
        }
    }
}
