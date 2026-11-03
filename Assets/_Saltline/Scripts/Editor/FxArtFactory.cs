using System;
using System.Collections.Generic;
using Saltline.FX;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Weapon and impact effect materials, built from the procedural textures written by
    /// ArtSource/FX/fx_textures.py into Art/FX/Textures. Particle materials use URP's particle
    /// shaders with their inspector's keyword setup, so they behave exactly as if made by hand.
    /// </summary>
    public static class FxArtFactory
    {
        public const string Dir = ContentFactory.ArtDir + "/FX";
        public const string TexDir = Dir + "/Textures";
        const string MatDir = Dir + "/Materials";

        enum Blend { Alpha = 0, Additive = 2, Cutout = 9 }

        public static void Build(PresentationLibrary lib)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_FX_Glow.png") == null)
            {
                Debug.LogWarning("[SALTLINE] FxArtFactory: no FX textures (run ArtSource/FX/fx_textures.py); keeping the plain effect materials.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");

            foreach (var n in new[] { "T_FX_MuzzleFront", "T_FX_MuzzleSide", "T_FX_Smoke", "T_FX_Glow", "T_FX_Debris", "T_FX_Tracer", "T_FX_Hole_D" })
                Configure(n, srgb: true, normal: false, max: 1024);
            Configure("T_FX_Hole_N", srgb: false, normal: true, max: 1024);
            Configure("T_FX_Hole_M", srgb: false, normal: false, max: 1024);

            lib.fxFlash = Particle("M_FX_Flash", "T_FX_MuzzleFront", Blend.Additive, new Color(5.0f, 4.2f, 3.3f, 1f), lit: false, soft: false);
            lib.fxFlame = Particle("M_FX_Flame", "T_FX_MuzzleSide", Blend.Additive, new Color(4.4f, 3.5f, 2.6f, 1f), lit: false, soft: false);
            lib.fxSpark = Particle("M_FX_Spark", "T_FX_Glow", Blend.Additive, new Color(9f, 5.4f, 2.0f, 1f), lit: false, soft: false);
            lib.fxGlow = Particle("M_FX_Glow", "T_FX_Glow", Blend.Additive, new Color(3.2f, 2.6f, 1.8f, 1f), lit: false, soft: true);
            lib.fxSmoke = Particle("M_FX_Smoke", "T_FX_Smoke", Blend.Alpha, Color.white, lit: true, soft: true);
            lib.fxDebris = Particle("M_FX_Debris", "T_FX_Debris", Blend.Cutout, Color.white, lit: true, soft: false);
            lib.tracer = Particle("M_FX_Tracer", "T_FX_Tracer", Blend.Additive, new Color(6f, 4.4f, 2.4f, 1f), lit: false, soft: false);
            lib.bulletHole = Decal();
            lib.brass = Metal("M_FX_Brass", new Color(0.80f, 0.58f, 0.27f), 0.72f, 1f);
            lib.shellHull = Metal("M_FX_ShellHull", new Color(0.50f, 0.07f, 0.05f), 0.42f, 0f);
            lib.muzzleFlash = lib.fxFlash;
            lib.impact = lib.bulletHole;
            Debug.Log("[SALTLINE] FxArtFactory: effect materials built (flash, flame, spark, glow, smoke, debris, tracer, bullet holes, brass).");
        }

        static void Configure(string name, bool srgb, bool normal, int max)
        {
            string p = $"{TexDir}/{name}.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (ti == null) return;
            bool changed = false;
            void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); changed = true; } }
            Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, srgb, v => ti.sRGBTexture = v);
            // Straight alpha: let the importer bleed colour into transparent texels so mips don't darken the edges.
            Set(ti.alphaIsTransparency, srgb, v => ti.alphaIsTransparency = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
            Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set(ti.maxTextureSize, max, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            if (changed) ti.SaveAndReimport();
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

        static Material Particle(string name, string tex, Blend blend, Color color, bool lit, bool soft)
        {
            var shader = Shader.Find(lit ? "Universal Render Pipeline/Particles/Simple Lit" : "Universal Render Pipeline/Particles/Unlit");
            var m = WeaponArtFactory.LoadOrCreate($"{MatDir}/{name}.mat", shader);
            m.SetTexture("_BaseMap", Tex(tex));
            m.SetColor("_BaseColor", color);
            bool cutout = blend == Blend.Cutout;
            m.SetFloat("_Surface", cutout ? 0f : 1f);
            m.SetFloat("_Blend", cutout ? 0f : (float)blend);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", 0f);                    // two-sided: the flame cross is seen from both faces
            m.SetFloat("_ColorMode", 0f);               // multiply by the particle colour
            m.SetFloat("_FlipbookBlending", 0f);
            m.SetFloat("_SoftParticlesEnabled", soft ? 1f : 0f);
            m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            m.SetFloat("_SoftParticlesFarFadeDistance", 0.10f);   // dust hugs walls: a long fade would erase it
            m.SetFloat("_CameraFadingEnabled", soft ? 1f : 0f);
            m.SetFloat("_CameraNearFadeDistance", 0.05f);
            m.SetFloat("_CameraFarFadeDistance", 0.35f);
            if (lit) m.SetFloat("_Smoothness", 0.05f);
            if (lit) m.SetColor("_SpecColor", new Color(0.05f, 0.05f, 0.05f));
            BaseShaderGUI.SetMaterialKeywords(m, null, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Decal()
        {
            var m = WeaponArtFactory.LoadOrCreate($"{MatDir}/M_FX_BulletHole.mat", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", Tex("T_FX_Hole_D"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", Tex("T_FX_Hole_N"));
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_MetallicGlossMap", Tex("T_FX_Hole_M"));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // metallic map alpha
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            // Plain alpha blending: with URP's preserved specular the clear part of the quad would still reflect.
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_ReceiveShadows", 1f);
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Metal(string name, Color c, float smooth, float metal)
        {
            var m = WeaponArtFactory.LoadOrCreate($"{MatDir}/{name}.mat", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
