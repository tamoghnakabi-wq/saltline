using System;
using System.Collections.Generic;
using System.IO;
using Saltline.FX;
using Saltline.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Imports the Blender-authored weapons (ArtSource/Blender/weapons_build.py writes
    /// Art/Weapons/&lt;tag&gt;/W_&lt;tag&gt;.fbx plus baked T_&lt;tag&gt;_D/_N/_M textures), builds their URP
    /// materials and registers each model in the PresentationLibrary. Classes without a model keep
    /// the greybox stand-in.
    /// </summary>
    public static class WeaponArtFactory
    {
        public const string WeaponArtDir = ContentFactory.ArtDir + "/Weapons";

        public static readonly (WeaponClass cls, string tag)[] Models =
        {
            (WeaponClass.AssaultRifle, "AR"),
            (WeaponClass.SMG, "SMG"),
            (WeaponClass.Shotgun, "SG"),
            (WeaponClass.Sniper, "SR"),
            (WeaponClass.Pistol, "PT"),
        };

        public static void Build(PresentationLibrary lib)
        {
            int count = Enum.GetValues(typeof(WeaponClass)).Length;
            if (lib.weaponModels == null || lib.weaponModels.Length != count) lib.weaponModels = new GameObject[count];
            Material glass = null, reticle = null;
            foreach (var (cls, tag) in Models)
            {
                string dir = $"{WeaponArtDir}/{tag}";
                string fbx = $"{dir}/W_{tag}.fbx";
                lib.weaponModels[(int)cls] = null;
                if (!File.Exists(fbx)) continue;

                glass ??= Glass(WeaponArtDir + "/M_W_Glass.mat");
                reticle ??= Reticle(WeaponArtDir + "/M_W_Reticle.mat");
                ConfigureTextures(dir, tag);
                var body = Body(dir, tag);
                if (body == null) { Debug.LogWarning($"[SALTLINE] Weapon {tag}: textures missing, keeping the greybox."); continue; }
                ConfigureModel(fbx, new Dictionary<string, Material>
                {
                    [$"M_W_{tag}"] = body, ["M_W_Glass"] = glass, ["M_W_Reticle"] = reticle,
                });
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                lib.weaponModels[(int)cls] = model;
                Report(tag, model);
            }
            EditorUtility.SetDirty(lib);
        }

        static void Report(string tag, GameObject model)
        {
            int fp = 0, tp = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                int tris = mf.sharedMesh != null ? (int)(mf.sharedMesh.GetIndexCount(0) / 3) : 0;
                for (int s = 1; mf.sharedMesh != null && s < mf.sharedMesh.subMeshCount; s++) tris += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
                if (WeaponModel.IsThirdPersonMesh(mf.name)) tp += tris; else fp += tris;
            }
            var t = model.transform;
            string M(string n) { var m = t.Find(n); return m != null ? m.localPosition.ToString("F3") : "missing"; }
            var size = FirstPersonBounds(model).size;
            Debug.Log($"[SALTLINE] Weapon {tag}: {fp} tris first-person, {tp} third-person; root scale {t.localScale:F2}; " +
                      $"first-person size {size:F3}; Muzzle {M("Muzzle")} Sight {M("Sight")} Grip_R {M("Grip_R")}");
            // The bore runs along +z: a model whose longest side is not z was imported on its side.
            if (size.z < size.x || size.z < size.y)
                throw new InvalidOperationException($"Weapon {tag} imported with the wrong axes: first-person size {size:F3} is not longest along z (the bore).");
        }

        /// <summary>Bounds of the first-person meshes in the model root's space.</summary>
        static Bounds FirstPersonBounds(GameObject model)
        {
            var root = model.transform;
            var b = new Bounds();
            bool any = false;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || WeaponModel.IsThirdPersonMesh(mf.name)) continue;
                var toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                    var p = toRoot.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        internal static void ConfigureTextures(string dir, string tag)
        {
            foreach (var kind in new[] { "D", "N", "M" })
            {
                string p = $"{dir}/T_{tag}_{kind}.png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (ti == null) continue;
                bool normal = kind == "N", mask = kind == "M";
                bool changed = false;
                void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); changed = true; } }
                Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
                Set(ti.sRGBTexture, !normal && !mask, v => ti.sRGBTexture = v);
                Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
                Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
                Set(ti.anisoLevel, 8, v => ti.anisoLevel = v);
                Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
                Set(ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
                Set(ti.maxTextureSize, 2048, v => ti.maxTextureSize = v);
                Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
                Set(ti.streamingMipmaps, false, v => ti.streamingMipmaps = v);
                if (changed) ti.SaveAndReimport();
            }
        }

        internal static Material LoadOrCreate(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            return m;
        }

        /// <summary>Baked set: albedo, tangent-space normal, mask (R metallic, G occlusion, A smoothness).</summary>
        internal static Material Body(string dir, string tag, string materialPath = null)
        {
            var d = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_{tag}_D.png");
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_{tag}_N.png");
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_{tag}_M.png");
            if (d == null || n == null || mk == null) return null;
            var m = LoadOrCreate(materialPath ?? $"{dir}/M_W_{tag}.mat", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", d);
            m.SetTexture("_MainTex", d);
            m.SetTexture("_BumpMap", n);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", mk);
            m.SetTexture("_OcclusionMap", mk);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_OCCLUSIONMAP");
            m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_EnvironmentReflections", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Optic lens: thin, faintly tinted, mostly reflection.</summary>
        static Material Glass(string path)
        {
            var m = LoadOrCreate(path, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.55f, 0.75f, 0.72f, 0.12f));
            m.SetFloat("_Smoothness", 0.97f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Reflex dot: unlit and brighter than white so it blooms a little.</summary>
        static Material Reticle(string path)
        {
            var m = LoadOrCreate(path, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", new Color(3.2f, 0.18f, 0.12f, 1f));
            m.SetFloat("_Surface", 0f);
            m.renderQueue = (int)RenderQueue.Transparent + 10; // after the lens
            EditorUtility.SetDirty(m);
            return m;
        }

        static void ConfigureModel(string path, Dictionary<string, Material> materials)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk; // matches the Blender bake's tangent space
            mi.generateSecondaryUV = false;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var (name, mat) in materials)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            mi.SaveAndReimport();
        }
    }
}
