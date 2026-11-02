using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Project-wide rendering settings that the scene factories rely on: distance shadowmask (baked
    /// shadows past the realtime shadow distance) and screen-space ambient occlusion strength.
    /// Per-preset values (shadow resolution, distance, cascades) are applied at runtime by VideoSettings.
    /// </summary>
    public static class RenderingConfigurator
    {
        const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        public static void Apply()
        {
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
                QualitySettings.streamingMipmapsActive = false; // whole map fits in memory; streaming would blur the first frames
            }
            QualitySettings.SetQualityLevel(current, false);

            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (rp != null)
            {
                rp.mainLightShadowmapResolution = 4096; // the default (High) preset; VideoSettings lowers it per preset
                EditorUtility.SetDirty(rp);
            }

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var ssao = data != null ? data.rendererFeatures.FirstOrDefault(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") : null;
            if (ssao != null)
            {
                var so = new SerializedObject(ssao);
                so.FindProperty("m_Settings.Intensity").floatValue = 0.7f;
                so.FindProperty("m_Settings.Radius").floatValue = 0.45f;
                so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.2f;
                so.FindProperty("m_Settings.Falloff").floatValue = 60f;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ssao);
            }
            else Debug.LogWarning("[SALTLINE] PC renderer has no SSAO feature.");
            AssetDatabase.SaveAssets();
        }
    }
}
