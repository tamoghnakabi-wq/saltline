using System;
using System.Reflection;
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

        /// <summary>
        /// Screen-space ambient occlusion. Low: half resolution, 8 samples, gaussian blur. Medium: half
        /// resolution, 12 samples, bilateral blur. High: full resolution, 12 samples, bilateral blur, which
        /// costs about five times as much as half resolution and looks only slightly crisper.
        /// </summary>
        public enum AoQuality { Off = 0, Low = 1, Medium = 2, High = 3 }

        /// <summary>What a preset sets. Values are applied to the active URP asset, the sun and QualitySettings.</summary>
        public struct PresetValues
        {
            public float RenderScale;
            public int Msaa;
            /// <summary>Post-process anti-aliasing on the camera (needs post-processing), for presets without MSAA.</summary>
            public AntialiasingMode PostAa;
            public float ShadowDistance;
            public int Cascades;
            public int ShadowResolution;
            public SoftShadowQuality SoftShadows;
            public bool Hdr;
            public bool PostFx;
            public AoQuality Ao;
            public AnisotropicFiltering Aniso;
            public int TextureMipLimit;
            /// <summary>LOD Group distance scale (operators): higher keeps detailed models further away.</summary>
            public float LodBias;
            /// <summary>Mesh LOD error threshold (other players' weapons): higher switches to simpler meshes sooner.</summary>
            public float MeshLodThreshold;
        }

        public static PresetValues Values(QualityPreset p) => p switch
        {
            QualityPreset.Low => new PresetValues
            {
                RenderScale = 0.75f, Msaa = 1, PostAa = AntialiasingMode.FastApproximateAntialiasing,
                ShadowDistance = 35f, Cascades = 1, ShadowResolution = 1024, SoftShadows = SoftShadowQuality.Low,
                Hdr = true, PostFx = true, Ao = AoQuality.Off, Aniso = AnisotropicFiltering.Disable, TextureMipLimit = 1, LodBias = 0.6f, MeshLodThreshold = 2f,
            },
            QualityPreset.Medium => new PresetValues
            {
                RenderScale = 0.9f, Msaa = 2, ShadowDistance = 60f, Cascades = 2, ShadowResolution = 2048, SoftShadows = SoftShadowQuality.Medium,
                Hdr = true, PostFx = true, Ao = AoQuality.Low, Aniso = AnisotropicFiltering.Enable, TextureMipLimit = 0, LodBias = 1f, MeshLodThreshold = 1f,
            },
            QualityPreset.High => new PresetValues
            {
                RenderScale = 1f, Msaa = 4, ShadowDistance = 90f, Cascades = 4, ShadowResolution = 4096, SoftShadows = SoftShadowQuality.High,
                Hdr = true, PostFx = true, Ao = AoQuality.Medium, Aniso = AnisotropicFiltering.ForceEnable, TextureMipLimit = 0, LodBias = 1.5f, MeshLodThreshold = 0.7f,
            },
            QualityPreset.Ultra => new PresetValues
            {
                RenderScale = 1f, Msaa = 4, ShadowDistance = 130f, Cascades = 4, ShadowResolution = 4096, SoftShadows = SoftShadowQuality.High,
                Hdr = true, PostFx = true, Ao = AoQuality.High, Aniso = AnisotropicFiltering.ForceEnable, TextureMipLimit = 0, LodBias = 2f, MeshLodThreshold = 0.5f,
            },
            _ => new PresetValues
            {
                RenderScale = 0.8f, Msaa = 2, ShadowDistance = 55f, Cascades = 2, ShadowResolution = 2048, SoftShadows = SoftShadowQuality.Medium,
                Hdr = true, PostFx = true, Ao = AoQuality.Low, Aniso = AnisotropicFiltering.Enable, TextureMipLimit = 0, LodBias = 1f, MeshLodThreshold = 1f,
            },
        };

        public static QualityPreset Preset
        {
            get => s_Preview ?? (QualityPreset)PlayerPrefs.GetInt(P + "preset", (int)DetectDefault());
            set { PlayerPrefs.SetInt(P + "preset", (int)value); ApplyQuality(); }
        }

        static QualityPreset? s_Preview;

        /// <summary>
        /// Apply a preset with its own render scale without saving anything (the benchmark uses this).
        /// null goes back to the saved settings.
        /// </summary>
        public static void PreviewPreset(QualityPreset? preset)
        {
            s_Preview = preset;
            ApplyQuality();
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

        public static bool PostProcessing => Applied.PostFx;

        /// <summary>Particle budget for weapon and impact effects (1 = full).</summary>
        public static float EffectsDetail => Preset switch
        {
            QualityPreset.Low => 0.4f,
            QualityPreset.Medium => 0.7f,
            QualityPreset.Laptop => 0.7f,
            _ => 1f,
        };
        public static float EffectiveRenderScale => RenderScaleOverride > 0f && s_Preview == null ? RenderScaleOverride : Values(Preset).RenderScale;

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
            Apply(Values(Preset), EffectiveRenderScale);
        }

        /// <summary>Apply a set of values directly (the benchmark uses this to test single changes).</summary>
        public static void Apply(PresetValues v, float renderScale)
        {
            s_Applied = v;
            s_HasApplied = true;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = renderScale;
                urp.upscalingFilter = UpscalingFilterSelection.FSR;
                urp.msaaSampleCount = v.Msaa;
                urp.shadowDistance = v.ShadowDistance;
                urp.shadowCascadeCount = v.Cascades;
                urp.mainLightShadowmapResolution = v.ShadowResolution;
                urp.supportsHDR = v.Hdr;
                ApplyAmbientOcclusion(urp, v.Ao);
            }
            QualitySettings.anisotropicFiltering = v.Aniso;
            QualitySettings.globalTextureMipmapLimit = v.TextureMipLimit;
            QualitySettings.lodBias = v.LodBias;
            QualitySettings.meshLodThreshold = v.MeshLodThreshold;
            ApplyCameras();
            Changed?.Invoke();
        }

        /// <summary>The values last applied (the preset's, or the benchmark's test values).</summary>
        public static PresetValues Applied => s_HasApplied ? s_Applied : Values(Preset);
        static PresetValues s_Applied;
        static bool s_HasApplied;

        // The SSAO feature's settings are internal to URP; they are set through reflection (Mono builds).
        static FieldInfo s_AoSettings, s_AoDownsample, s_AoSamples, s_AoBlur;
        static bool s_AoWarned;

        static void ApplyAmbientOcclusion(UniversalRenderPipelineAsset urp, AoQuality q)
        {
            // In the editor these changes would be saved into the renderer asset, and so into later builds.
            if (Application.isEditor) return;
            foreach (var data in urp.rendererDataList)
            {
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature is not ScreenSpaceAmbientOcclusion ao) continue;
                    ao.SetActive(q != AoQuality.Off);
                    if (q == AoQuality.Off) continue;
                    s_AoSettings ??= typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance);
                    var settings = s_AoSettings?.GetValue(ao);
                    if (settings == null) { WarnAo(); continue; }
                    var st = settings.GetType();
                    s_AoDownsample ??= st.GetField("Downsample", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_AoSamples ??= st.GetField("Samples", BindingFlags.NonPublic | BindingFlags.Instance);
                    s_AoBlur ??= st.GetField("BlurQuality", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (s_AoDownsample == null || s_AoSamples == null || s_AoBlur == null) { WarnAo(); continue; }
                    // URP's enums: samples High 12, Medium 8, Low 4 (0, 1, 2); blur High bilateral, Medium gaussian, Low kawase.
                    s_AoDownsample.SetValue(settings, q != AoQuality.High);
                    s_AoSamples.SetValue(settings, Enum.ToObject(s_AoSamples.FieldType, q == AoQuality.Low ? 1 : 0));
                    s_AoBlur.SetValue(settings, Enum.ToObject(s_AoBlur.FieldType, q == AoQuality.Low ? 1 : 0));
                }
            }
        }

        static void WarnAo()
        {
            if (s_AoWarned) return;
            s_AoWarned = true;
            Debug.LogWarning("[SALTLINE] SSAO quality could not be set: URP's SSAO settings layout changed. SSAO stays at its asset settings.");
        }

        static void ApplyCameras()
        {
            var applied = Applied;
            foreach (var cam in Camera.allCameras)
                if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)) ConfigureCamera(data);
            // The sun's shadow filter (the pipeline-wide soft shadow quality is not settable at runtime).
            var sun = RenderSettings.sun;
            if (sun != null && sun.TryGetComponent<UniversalAdditionalLightData>(out var light))
                light.softShadowQuality = applied.SoftShadows;
        }

        /// <summary>Post-processing and post anti-aliasing for a base camera, from the applied preset.</summary>
        public static void ConfigureCamera(UniversalAdditionalCameraData data)
        {
            if (data == null || data.renderType != CameraRenderType.Base) return;
            var applied = Applied;
            data.renderPostProcessing = applied.PostFx;
            data.antialiasing = applied.PostFx ? applied.PostAa : AntialiasingMode.None;
            data.antialiasingQuality = AntialiasingQuality.Medium;
        }
    }
}
