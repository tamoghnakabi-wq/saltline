using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Creates the runtime UI assets: a theme (Unity's default runtime controls + Saltline.uss)
    /// and the PanelSettings the game loads from Resources. Screens themselves are built in C#.
    /// </summary>
    public static class UiFactory
    {
        public const string UiDir = "Assets/_Saltline/UI";
        public const string ThemePath = UiDir + "/SaltlineTheme.tss";
        public const string PanelPath = "Assets/_Saltline/Resources/UI/SaltlinePanel.asset";

        [MenuItem("Saltline/Build/UI Assets")]
        public static void Build()
        {
            Directory.CreateDirectory(UiDir);
            Directory.CreateDirectory(Path.GetDirectoryName(PanelPath));

            const string theme = "@import url(\"unity-theme://default\");\n" +
                                 "@import url(\"project://database/Assets/_Saltline/UI/Saltline.uss\");\n";
            if (!File.Exists(ThemePath) || File.ReadAllText(ThemePath) != theme)
            {
                File.WriteAllText(ThemePath, theme);
                AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceUpdate);
            }
            var tss = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (tss == null) Debug.LogError("[SALTLINE] UI theme failed to import.");

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelPath);
            }
            ps.themeStyleSheet = tss;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 100;
            ps.clearColor = false;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SALTLINE] UI assets built (theme {(tss != null ? "ok" : "MISSING")}).");
        }
    }
}
