using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Saltline.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Saltline.Tests
{
    /// <summary>
    /// The quality presets and the rendering setup they rely on: presets get cheaper in order,
    /// the SSAO settings the presets change still exist in URP, and Kappa has its occlusion data.
    /// </summary>
    public class RenderingTests
    {
        const string KappaPath = "Assets/_Saltline/Scenes/Kappa.unity";
        const string OcclusionPath = "Assets/_Saltline/Scenes/Kappa/OcclusionCullingData.asset";

        static readonly QualityPreset[] Ladder = { QualityPreset.Low, QualityPreset.Medium, QualityPreset.High, QualityPreset.Ultra };

        [Test]
        public void PresetsRiseInCostFromLowToUltra()
        {
            for (int i = 1; i < Ladder.Length; i++)
            {
                var a = VideoSettings.Values(Ladder[i - 1]);
                var b = VideoSettings.Values(Ladder[i]);
                string pair = $"{Ladder[i - 1]} -> {Ladder[i]}";
                Assert.LessOrEqual(a.RenderScale, b.RenderScale, $"{pair}: render scale");
                Assert.LessOrEqual(a.Msaa, b.Msaa, $"{pair}: MSAA");
                Assert.LessOrEqual(a.ShadowDistance, b.ShadowDistance, $"{pair}: shadow distance");
                Assert.LessOrEqual(a.Cascades, b.Cascades, $"{pair}: cascades");
                Assert.LessOrEqual(a.ShadowResolution, b.ShadowResolution, $"{pair}: shadow resolution");
                Assert.LessOrEqual((int)a.SoftShadows, (int)b.SoftShadows, $"{pair}: soft shadows");
                Assert.LessOrEqual((int)a.Ao, (int)b.Ao, $"{pair}: SSAO");
                Assert.LessOrEqual(a.LodBias, b.LodBias, $"{pair}: LOD bias");
                Assert.GreaterOrEqual(a.MeshLodThreshold, b.MeshLodThreshold, $"{pair}: mesh LOD threshold");
                Assert.GreaterOrEqual(a.TextureMipLimit, b.TextureMipLimit, $"{pair}: texture mip limit");
            }
        }

        [Test]
        public void LaptopPresetIsNoCostlierThanHigh()
        {
            var l = VideoSettings.Values(QualityPreset.Laptop);
            var h = VideoSettings.Values(QualityPreset.High);
            Assert.Less(l.RenderScale, h.RenderScale);
            Assert.LessOrEqual(l.Msaa, h.Msaa);
            Assert.LessOrEqual(l.ShadowDistance, h.ShadowDistance);
            Assert.LessOrEqual(l.Cascades, h.Cascades);
            Assert.LessOrEqual((int)l.Ao, (int)h.Ao);
            Assert.LessOrEqual((int)l.SoftShadows, (int)h.SoftShadows);
        }

        [Test]
        public void EveryPresetIsValid()
        {
            foreach (QualityPreset p in System.Enum.GetValues(typeof(QualityPreset)))
            {
                var v = VideoSettings.Values(p);
                Assert.That(v.RenderScale, Is.InRange(0.5f, 1f), $"{p} render scale");
                Assert.That(new[] { 1, 2, 4, 8 }, Does.Contain(v.Msaa), $"{p} MSAA");
                Assert.That(v.Cascades, Is.InRange(1, 4), $"{p} cascades");
                Assert.That(new[] { 256, 512, 1024, 2048, 4096 }, Does.Contain(v.ShadowResolution), $"{p} shadow resolution");
                Assert.AreNotEqual(SoftShadowQuality.UsePipelineSettings, v.SoftShadows, $"{p} soft shadows");
                Assert.Greater(v.LodBias, 0f, $"{p} LOD bias");
                Assert.Greater(v.MeshLodThreshold, 0f, $"{p} mesh LOD threshold");
                // A preset with post-processing but no MSAA would have no anti-aliasing at all.
                if (v.PostFx && v.Msaa <= 1) Assert.AreNotEqual(AntialiasingMode.None, v.PostAa, $"{p} has neither MSAA nor post anti-aliasing");
                Assert.AreNotEqual(AntialiasingMode.TemporalAntiAliasing, v.PostAa, $"{p}: TAA needs MSAA off and smears fast motion");
            }
            Assert.AreEqual(System.Enum.GetValues(typeof(QualityPreset)).Length, VideoSettings.PresetNames.Length, "a name for every preset");
        }

        /// <summary>VideoSettings sets SSAO quality through URP's internal settings; a URP upgrade that renames them must fail here.</summary>
        [Test]
        public void SsaoSettingsLayoutMatchesVideoSettings()
        {
            var settingsField = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(settingsField, "ScreenSpaceAmbientOcclusion.m_Settings");
            var st = settingsField.FieldType;
            var downsample = st.GetField("Downsample", BindingFlags.NonPublic | BindingFlags.Instance);
            var samples = st.GetField("Samples", BindingFlags.NonPublic | BindingFlags.Instance);
            var blur = st.GetField("BlurQuality", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(downsample, "Downsample");
            Assert.AreEqual(typeof(bool), downsample.FieldType);
            Assert.IsNotNull(samples, "Samples");
            Assert.IsNotNull(blur, "BlurQuality");
            // The values VideoSettings writes: samples High = 0, Medium = 1; blur High = 0, Medium = 1.
            Assert.AreEqual("High", System.Enum.GetName(samples.FieldType, 0));
            Assert.AreEqual("Medium", System.Enum.GetName(samples.FieldType, 1));
            Assert.AreEqual("High", System.Enum.GetName(blur.FieldType, 0));
            Assert.AreEqual("Medium", System.Enum.GetName(blur.FieldType, 1));

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            Assert.IsNotNull(renderer, "PC renderer");
            Assert.IsTrue(renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().Any(), "the PC renderer has the SSAO feature the presets control");
        }

        /// <summary>The shader warm-up's recording for macOS (ShaderWarmup) is there and names only shaders that still exist.</summary>
        [Test]
        public void MacShaderWarmupRecordingIsUsable()
        {
            var c = AssetDatabase.LoadAssetAtPath<GraphicsStateCollection>("Assets/_Saltline/Resources/ShaderWarmup/Saltline_OSX_Metal.graphicsstate");
            Assert.IsNotNull(c, "recording imported as a GraphicsStateCollection");
            Assert.AreEqual(RuntimePlatform.OSXPlayer, c.runtimePlatform);
            Assert.AreEqual(GraphicsDeviceType.Metal, c.graphicsDeviceType);
            Assert.Greater(c.variantCount, 30, "shader variants");
            Assert.GreaterOrEqual(c.totalGraphicsStateCount, c.variantCount, "graphics states");
            var variants = new List<GraphicsStateCollection.ShaderVariant>();
            c.GetVariants(variants);
            foreach (var v in variants) Assert.IsNotNull(v.shader, "a recorded shader is missing: record the warm-up again (docs/PERFORMANCE.md)");
            Assert.IsTrue(variants.Any(v => v.shader.name == "Universal Render Pipeline/Lit"), "the map's lit materials are covered");
        }

        [Test]
        public void KappaHasOcclusionCullingData()
        {
            if (!File.Exists(KappaPath)) Assert.Ignore("Kappa scene not built");
            Assert.IsTrue(File.Exists(OcclusionPath), "occlusion data baked (KappaSceneFactory.EnsureOcclusion)");
            string guid = AssetDatabase.AssetPathToGUID(OcclusionPath);
            Assert.IsNotEmpty(guid);
            StringAssert.Contains($"m_OcclusionCullingData: {{fileID: 36300000, guid: {guid}", File.ReadAllText(KappaPath), "the scene uses that data");
        }

        [Test]
        public void ThinKappaDetailsDoNotOcclude()
        {
            if (!File.Exists(KappaPath)) Assert.Ignore("Kappa scene not built");
            EditorSceneManager.OpenScene(KappaPath, OpenSceneMode.Single);
            try
            {
                int occluders = 0;
                foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
                    Assert.IsTrue((flags & StaticEditorFlags.OccludeeStatic) != 0, $"{mr.name} can be culled");
                    bool occluder = (flags & StaticEditorFlags.OccluderStatic) != 0;
                    if (mr.name.EndsWith("__NC")) Assert.IsFalse(occluder, $"{mr.name} has no collider and must not hide what is behind it");
                    if (occluder) occluders++;
                }
                Assert.Greater(occluders, 50, "the big solid pieces occlude");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
