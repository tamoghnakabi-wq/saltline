using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Saltline.EditorTools
{
    /// <summary>
    /// The app icon (macOS and Windows) and the import settings of the logo images, which are drawn by
    /// ArtSource/Branding/make_logo.py. Called from <see cref="ProjectConfigurator.Apply"/>.
    /// </summary>
    public static class Branding
    {
        public const string Folder = "Assets/_Saltline/Art/Branding";
        public const string MarkPath = Folder + "/SaltlineMark.png";
        public static readonly int[] IconSizes = { 1024, 512, 256, 128, 64, 48, 32, 16 };

        public static string IconPath(int size) => $"{Folder}/SaltlineIcon_{size}.png";

        [MenuItem("Saltline/Apply App Icon")]
        public static void Apply()
        {
            foreach (var path in Directory.GetFiles(Folder, "*.png").Select(p => p.Replace('\\', '/')))
                Configure(path);

            // Each size Unity asks for gets the file drawn at that size; anything else, the nearest larger one.
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            var icons = sizes.Select(Icon).ToArray();
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { Icon(1024) }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SALTLINE] App icon set for sizes {string.Join(", ", sizes)}");
        }

        static Texture2D Icon(int size)
        {
            int pick = IconSizes.Where(s => s >= size).DefaultIfEmpty(IconSizes[0]).Min();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath(pick));
            if (tex == null) throw new FileNotFoundException($"Missing {IconPath(pick)}; run ArtSource/Branding/make_logo.py");
            return tex;
        }

        static void Configure(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) return;
            bool mark = path == MarkPath;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = mark; // the menu draws the mark smaller than its file
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }
    }
}
