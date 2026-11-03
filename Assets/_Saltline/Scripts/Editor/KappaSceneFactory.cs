using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Saltline.Combat;
using Saltline.Match;
using Saltline.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Builds the Kappa Saltworks scene from the Blender export (ArtSource/Blender/kappa_build.py).
    /// The FBX carries the geometry; object-name suffixes decide colliders and shadows, material
    /// names decide the surface type, and empties mark spawn points and lamps.
    /// </summary>
    public static class KappaSceneFactory
    {
        public const string MapDir = ContentFactory.ArtDir + "/Maps/Kappa";
        public const string FbxPath = MapDir + "/Kappa.fbx";
        public const string TexDir = MapDir + "/Textures";
        /// <summary>CC0 Poly Haven scans converted by ArtSource/Textures/import_cc0.py: _D albedo, _N normal, _M mask (R metal, G AO, A smoothness).</summary>
        public const string CC0Dir = MapDir + "/TexturesCC0";
        /// <summary>CC0 Poly Haven sky with its sun taken out (ArtSource/Blender/sky_remove_sun.py); the Sun light replaces it.</summary>
        public const string SkyPath = ContentFactory.ArtDir + "/Sky/Sky_Kloofendal48d_NoSun_4k.hdr";
        const string LightingPath = MapDir + "/Lighting_Kappa.lighting";
        const string HashPath = MapDir + "/Kappa.scenehash";

        /// <summary>
        /// Bump whenever this factory changes what the scene contains or how it bakes (import settings,
        /// lights, probes, bake settings). The scene and its lightmaps are only rebuilt when this or the
        /// FBX changes, because a bake takes minutes; materials are refreshed on every run.
        /// </summary>
        const int SceneVersion = 6;

        /// <summary>The HDRI's sun (brightest pixel: u, and v from the bottom), measured by sky_remove_sun.py.</summary>
        static readonly Vector2 SkySunUV = new(0.59509f, 0.76587f);
        /// <summary>Sun heading: late in the day, from over the sea. Its elevation comes from the HDRI.</summary>
        const float SunYaw = 32f;
        static float SunElevation => 90f - (1f - SkySunUV.y) * 180f;
        static Quaternion SunRotation => Quaternion.Euler(SunElevation, SunYaw, 0f);
        public const string MatDir = MapDir + "/Materials";
        public const string ScenePath = BootSceneFactory.ScenesDir + "/Kappa.unity";

        /// <summary>
        /// Material name -> (texture, tint, smoothness, metallic, surface, penetration cost).
        /// When a CC0 scan of that texture exists, metallic and smoothness come from its mask and
        /// "smoothness" here scales the mask's smoothness.
        /// </summary>
        static readonly Dictionary<string, (string tex, Color tint, float smooth, float metal, SurfaceType surface, float cost)> Mats = new()
        {
            ["M_Asphalt"] = ("Asphalt", Color.white, 1f, 0f, SurfaceType.Concrete, 0f),
            ["M_Concrete"] = ("Concrete", new Color(0.93f, 0.97f, 1.04f), 1f, 0f, SurfaceType.Concrete, 0f),
            ["M_ConcreteFloor"] = ("ConcreteFloor", Color.white, 1f, 0f, SurfaceType.Concrete, 0f),
            ["M_Facade"] = ("Facade", Color.white, 1f, 0f, SurfaceType.Concrete, 0f),
            ["M_Salt"] = ("Salt", Color.white, 1f, 0f, SurfaceType.Dirt, 0f),
            ["M_Sacks"] = ("Sacks", Color.white, 0.4f, 0f, SurfaceType.Dirt, 0f),
            ["M_Metal"] = ("Metal", Color.white, 1f, 0.45f, SurfaceType.Metal, 0f),
            ["M_Plate"] = ("Metal", new Color(1.45f, 1.38f, 1.25f), 1f, 0.45f, SurfaceType.ThinMetal, 0.6f),
            ["M_Grate"] = ("Grate", Color.white, 1f, 0.6f, SurfaceType.Metal, 0f),
            ["M_TankPaint"] = ("TankPaint", Color.white, 1f, 0.1f, SurfaceType.Metal, 0f),
            ["M_Rust"] = ("Rust", Color.white, 1f, 0.3f, SurfaceType.Metal, 0f),
            ["M_Corrugated"] = ("Corrugated", Color.white, 1f, 0.35f, SurfaceType.ThinMetal, 0.6f),
            ["M_Container"] = ("Container", Color.white, 1f, 0.25f, SurfaceType.Metal, 0f),
            ["M_Wood"] = ("Wood", Color.white, 1f, 0f, SurfaceType.Wood, 0.35f),
            ["M_Rock"] = ("Rock", Color.white, 1f, 0f, SurfaceType.Concrete, 0f),
            ["M_Ground"] = ("Ground", Color.white, 1f, 0f, SurfaceType.Dirt, 0f),
            ["M_Hazard"] = ("Hazard", Color.white, 0.6f, 0f, SurfaceType.Metal, 0f),
            ["M_PaintYellow"] = ("Paint", new Color(0.86f, 0.62f, 0.1f), 0.6f, 0.1f, SurfaceType.Metal, 0f),
            ["M_PaintRed"] = ("Paint", new Color(0.72f, 0.13f, 0.09f), 0.5f, 0f, SurfaceType.Concrete, 0f),
            ["M_PaintBlue"] = ("Paint", new Color(0.1f, 0.32f, 0.72f), 0.5f, 0f, SurfaceType.Concrete, 0f),
            ["M_Dark"] = ("Paint", new Color(0.07f, 0.07f, 0.07f), 0.45f, 0f, SurfaceType.Metal, 0f),
            ["M_Brine"] = ("Water", new Color(0.86f, 0.64f, 0.68f), 0.92f, 0f, SurfaceType.Water, 0f),
            ["M_Sea"] = ("Water", new Color(0.05f, 0.15f, 0.2f), 0.93f, 0f, SurfaceType.Water, 0f),
            ["M_Lamp"] = (null, new Color(1f, 0.86f, 0.62f), 0.5f, 0f, SurfaceType.Glass, 0.05f),
            ["M_Clip"] = (null, Color.magenta, 0f, 0f, SurfaceType.Concrete, 0f),
            // Detail pass (kappa_props.py).
            // The scan is yellowish (linear mean 0.27, 0.24, 0.16); the tint brings it back to a neutral grey.
            ["M_ConcreteCast"] = ("ConcreteSmooth", new Color(0.95f, 0.99f, 1.12f), 1f, 0f, SurfaceType.Concrete, 0f),
            ["M_Shutter"] = ("Shutter", Color.white, 1f, 0.3f, SurfaceType.ThinMetal, 0.6f),
            // ContainerPaint is a grey copy of the container scan (mean linear albedo 0.5); the tint is the paint.
            ["M_ContainerRed"] = ("ContainerPaint", new Color(0.80f, 0.33f, 0.27f), 1f, 0.25f, SurfaceType.Metal, 0f),
            ["M_ContainerBlue"] = ("ContainerPaint", new Color(0.40f, 0.55f, 0.75f), 1f, 0.25f, SurfaceType.Metal, 0f),
            ["M_ContainerWhite"] = ("ContainerPaint", new Color(1f, 1f, 0.97f), 1f, 0.25f, SurfaceType.Metal, 0f),
            ["M_ContainerGreen"] = ("ContainerPaint", new Color(0.48f, 0.63f, 0.47f), 1f, 0.25f, SurfaceType.Metal, 0f),
            ["M_ContainerOrange"] = ("ContainerPaint", new Color(0.95f, 0.58f, 0.30f), 1f, 0.25f, SurfaceType.Metal, 0f),
            // Window glass over a dark interior. Partly metallic so the sky and buildings show in it; plain 4%
            // dielectric reflection read as flat black panes in the captures.
            ["M_Glass"] = (null, new Color(0.16f, 0.19f, 0.21f), 0.95f, 0.5f, SurfaceType.Glass, 0.05f),
            ["M_Rubber"] = ("Paint", new Color(0.24f, 0.24f, 0.24f), 0.35f, 0f, SurfaceType.Plastic, 0f),
            ["M_PaintWhite"] = ("Paint", new Color(0.82f, 0.82f, 0.80f), 0.6f, 0.1f, SurfaceType.Metal, 0f),
            ["M_PaintGrey"] = ("Paint", new Color(0.34f, 0.36f, 0.37f), 0.55f, 0.1f, SurfaceType.Metal, 0f),
            ["M_PaintOrange"] = ("Paint", new Color(0.90f, 0.35f, 0.06f), 0.5f, 0f, SurfaceType.Plastic, 0f),
            ["M_DrumBlue"] = ("Paint", new Color(0.10f, 0.24f, 0.48f), 0.55f, 0.1f, SurfaceType.Metal, 0f),
            ["M_Plastic"] = ("Paint", new Color(0.86f, 0.86f, 0.82f), 0.5f, 0f, SurfaceType.Plastic, 0f),
            ["M_Bag"] = ("Bag", Color.white, 1f, 0f, SurfaceType.Dirt, 0f),
            // Wet patches: about half the asphalt's albedo (linear 0.12), mirror-smooth.
            ["M_Puddle"] = ("Water", new Color(0.25f, 0.26f, 0.27f), 0.95f, 0f, SurfaceType.Water, 0f),
        };

        /// <summary>
        /// Scans whose normal and mask maps are shared with another scan: the painted containers use
        /// the original container scan's relief and mask, with their own grey albedo.
        /// </summary>
        static readonly Dictionary<string, string> SharedScanMaps = new() { ["ContainerPaint"] = "Container" };

        [MenuItem("Saltline/Build/Kappa Scene")]
        public static void Build()
        {
            if (!File.Exists(FbxPath))
            {
                Debug.LogWarning($"[SALTLINE] {FbxPath} missing: run ArtSource/Blender/kappa_build.py in Blender first. Kappa scene skipped.");
                return;
            }
            Directory.CreateDirectory(MatDir);
            ConfigureTextures();
            var materials = BuildMaterials();

            string hash = InputHash();
            bool force = Environment.GetCommandLineArgs().Contains("-rebuildkappa");
            if (!force && File.Exists(ScenePath) && File.Exists(HashPath) && File.ReadAllText(HashPath).Trim() == hash && HasBake())
            {
                Debug.Log("[SALTLINE] Kappa scene and bake are up to date (FBX and scene version unchanged); materials refreshed. Pass -rebuildkappa to force.");
                return;
            }
            ConfigureModel(materials);
            if (BuildScene()) File.WriteAllText(HashPath, hash + "\n");
        }

        static string InputHash()
        {
            using var sha = SHA1.Create();
            var fbx = File.ReadAllBytes(FbxPath);
            var tag = System.Text.Encoding.UTF8.GetBytes($"v{SceneVersion}|sky {new FileInfo(SkyPath).Length}");
            sha.TransformBlock(fbx, 0, fbx.Length, null, 0);
            sha.TransformFinalBlock(tag, 0, tag.Length);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }

        static bool HasBake()
        {
            string dir = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath));
            return File.Exists(Path.Combine(dir, "LightingData.asset")) && Directory.GetFiles(dir, "Lightmap-*_comp_light.exr").Length > 0;
        }

        // ------------------------------------------------------------------ assets

        static void ConfigureTextures()
        {
            var files = Directory.GetFiles(TexDir, "T_*.png").Select(p => (p, 1024))
                .Concat(Directory.Exists(CC0Dir) ? Directory.GetFiles(CC0Dir, "T_*.*").Where(p => !p.EndsWith(".meta")).Select(p => (p, 2048)) : Enumerable.Empty<(string, int)>());
            foreach (var (path, maxSize) in files)
            {
                string p = path.Replace('\\', '/');
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (ti == null) { AssetDatabase.ImportAsset(p); ti = (TextureImporter)AssetImporter.GetAtPath(p); }
                string stem = Path.GetFileNameWithoutExtension(p);
                bool normal = stem.EndsWith("_N");
                bool mask = stem.EndsWith("_M");
                bool changed = false;
                void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); changed = true; } }
                Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
                Set(ti.sRGBTexture, !normal && !mask, v => ti.sRGBTexture = v);
                Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
                Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
                Set(ti.anisoLevel, 8, v => ti.anisoLevel = v);
                Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
                Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
                Set(ti.maxTextureSize, maxSize, v => ti.maxTextureSize = v);
                Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
                Set(ti.streamingMipmaps, false, v => ti.streamingMipmaps = v);
                if (changed) ti.SaveAndReimport();
            }

            // Sky: equirectangular HDR sampled by Skybox/Panoramic. No mips (they show a seam where u wraps).
            var sky = (TextureImporter)AssetImporter.GetAtPath(SkyPath);
            if (sky != null)
            {
                bool changed = false;
                void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); changed = true; } }
                Set(sky.textureType, TextureImporterType.Default, v => sky.textureType = v);
                Set(sky.textureShape, TextureImporterShape.Texture2D, v => sky.textureShape = v);
                Set(sky.mipmapEnabled, false, v => sky.mipmapEnabled = v);
                Set(sky.wrapModeU, TextureWrapMode.Repeat, v => sky.wrapModeU = v);
                Set(sky.wrapModeV, TextureWrapMode.Clamp, v => sky.wrapModeV = v);
                Set(sky.filterMode, FilterMode.Bilinear, v => sky.filterMode = v);
                Set(sky.maxTextureSize, 4096, v => sky.maxTextureSize = v);
                Set(sky.textureCompression, TextureImporterCompression.CompressedHQ, v => sky.textureCompression = v);
                if (changed) sky.SaveAndReimport();
            }
        }

        static Texture2D Cc0(string tex, string suffix, string ext) =>
            tex == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{CC0Dir}/T_{tex}_{suffix}.{ext}");

        static Dictionary<string, Material> BuildMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var result = new Dictionary<string, Material>();
            foreach (var (name, spec) in Mats)
            {
                string path = $"{MatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = shader;
                m.SetColor("_BaseColor", spec.tint);
                m.SetFloat("_Smoothness", spec.smooth);
                m.SetFloat("_Metallic", spec.metal);
                var scanAlbedo = Cc0(spec.tex, "D", "jpg");
                string mapTex = spec.tex != null && SharedScanMaps.TryGetValue(spec.tex, out var shared) ? shared : spec.tex;
                var scanNormal = Cc0(mapTex, "N", "jpg");
                var scanMask = Cc0(mapTex, "M", "png");
                bool scanned = scanAlbedo != null && scanNormal != null && scanMask != null;
                var albedo = scanned ? scanAlbedo : spec.tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{spec.tex}_D.png") : null;
                var normal = scanned ? scanNormal : spec.tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{spec.tex}_N.png") : null;
                m.SetTexture("_BaseMap", albedo);
                m.SetTexture("_MainTex", albedo);
                // Scanned sets: metallic (R), occlusion (G) and smoothness (A) come from the mask, and
                // _Smoothness scales the mask's smoothness.
                m.SetTexture("_MetallicGlossMap", scanned ? scanMask : null);
                m.SetTexture("_OcclusionMap", scanned ? scanMask : null);
                m.SetFloat("_OcclusionStrength", 1f);
                if (scanned) { m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP"); }
                else { m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_OCCLUSIONMAP"); }
                if (scanned)
                {
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                }
                // Generated sets: smoothness comes from the albedo alpha (rust is rough, paint and puddles are not).
                else if (albedo != null)
                {
                    m.SetFloat("_SmoothnessTextureChannel", 1f);
                    m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                }
                else
                {
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                }
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.SetFloat("_BumpScale", name == "M_Sea" ? 0.35f : name == "M_Puddle" ? 0.2f : 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                else
                {
                    m.SetTexture("_BumpMap", null);
                    m.DisableKeyword("_NORMALMAP");
                }
                if (name == "M_Lamp")
                {
                    m.SetColor("_EmissionColor", spec.tint * 6f);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = LampGIFlags;
                }
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                result[name] = m;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static void ConfigureModel(Dictionary<string, Material> materials)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = true;                 // mesh colliders
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            // Lightmap UVs, with UV-chart margins worked out for the bake resolution below.
            mi.generateSecondaryUV = true;
            mi.secondaryUVMarginMethod = ModelImporterSecondaryUVMarginMethod.Calculate;
            mi.secondaryUVMinLightmapResolution = LightmapTexelsPerMetre;
            mi.secondaryUVMinObjectScale = FarLightmapScale;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var (name, mat) in materials)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ scene

        static bool BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "Kappa";

            // Blender (x east, y north, z up) -> Unity. Measured from markers instead of assumed,
            // so an exporter axis change cannot silently mirror the spawns or capture views.
            var red0 = root.transform.Find("SPAWN_RED_00");
            var lamp0 = root.transform.Find("LIGHT_00");
            if (red0 == null || lamp0 == null) { Debug.LogError("[SALTLINE] Kappa.fbx is missing its markers."); return false; }
            float sx = Mathf.Sign(red0.position.x / -50f);
            float sz = Mathf.Sign(lamp0.position.z / 32.5f);
            Vector3 B(float x, float y, float z) => new Vector3(sx * x, z, sz * y);
            Debug.Log($"[SALTLINE] Kappa axes: SPAWN_RED_00 at {red0.position}, LIGHT_00 at {lamp0.position} -> x*{sx}, z*{sz}");

            int colliders = 0, clips = 0, decor = 0, spawns = 0, lights = 0;
            var all = root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform).ToList();
            var spawnRoot = new GameObject("SpawnPoints").transform;
            var lightRoot = new GameObject("Lamps").transform;
            foreach (var t in all)
            {
                var go = t.gameObject;
                string n = go.name;
                if (n.StartsWith("SPAWN_"))
                {
                    var team = n.StartsWith("SPAWN_RED") ? Team.Red : Team.Blue;
                    var sp = new GameObject(n).AddComponent<SpawnPoint>();
                    sp.team = team;
                    // Face the middle of the map (east for Red, west for Blue in Unity space).
                    // Face the gap round the nearer baffle: that is the way out of the yard, and the
                    // office wall straight ahead would otherwise fill the first frame after spawning.
                    var exit = new Vector3(Mathf.Sign(t.position.x) * 55.25f, t.position.y, Mathf.Sign(t.position.z) * 10.5f);
                    var look = exit - t.position;
                    float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                    sp.transform.SetParent(spawnRoot, false);
                    sp.transform.SetPositionAndRotation(t.position, Quaternion.Euler(0f, yaw, 0f));
                    // Points nearer the middle of the yard are tried first when scores tie.
                    sp.priority = -Mathf.Abs(t.position.z) * 0.1f;
                    Object.DestroyImmediate(go);
                    spawns++;
                    continue;
                }
                if (n.StartsWith("LIGHT_"))
                {
                    var l = new GameObject(n).AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = 14f;
                    l.intensity = 6f;
                    l.color = new Color(1f, 0.84f, 0.62f);
                    // Baked: lamps light the lightmaps and light probes, with soft shadows, at no runtime cost.
                    l.lightmapBakeType = LightmapBakeType.Baked;
                    l.shadows = LightShadows.Soft;
                    l.shadowRadius = 0.15f;
                    l.transform.SetParent(lightRoot, false);
                    l.transform.position = t.position;
                    Object.DestroyImmediate(go);
                    lights++;
                    continue;
                }
                var mr = go.GetComponent<MeshRenderer>();
                var mf = go.GetComponent<MeshFilter>();
                go.layer = Layers.Environment;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
                if (mr == null || mf == null) continue;
                ConfigureGI(go, mr);

                if (n.EndsWith("__PC"))
                {
                    Object.DestroyImmediate(mr);
                    go.layer = Layers.PlayerClip;
                    go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    clips++;
                    continue;
                }
                if (n.EndsWith("__NC") || n.EndsWith("__FAR"))
                {
                    if (n.EndsWith("__FAR")) mr.shadowCastingMode = ShadowCastingMode.Off;
                    decor++;
                    continue;
                }
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                var matName = mr.sharedMaterial != null ? mr.sharedMaterial.name : "";
                var tag = go.AddComponent<SurfaceTag>();
                if (Mats.TryGetValue(matName, out var spec))
                {
                    tag.surface = spec.surface;
                    tag.penetrationCost = spec.cost;
                    tag.damageRetained = SurfaceTag.DefaultRetained(spec.surface);
                }
                else Debug.LogWarning($"[SALTLINE] Kappa: {n} uses unknown material '{matName}'.");
                colliders++;
            }

            BuildLighting();
            BuildLightProbes();
            BuildReflectionProbes(B);
            BuildCameras(B);
            AddCaptureViews(B);
            BuildAmbience(B);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SALTLINE] Kappa scene: {colliders} collidable, {clips} clips, {decor} decor, {spawns} spawns, {lights} lamps -> {ScenePath}");
            bool baked = Bake();
            EnsureLampEmission();
            EditorSceneManager.SaveScene(scene, ScenePath);
            return baked;
        }

        // ------------------------------------------------------------------ global illumination

        /// <summary>Lightmap texels per metre at scale 1.</summary>
        const float LightmapTexelsPerMetre = 4f;
        /// <summary>Lightmap scale for the backdrop (hillside, cliff, warehouses): seen from far away only.</summary>
        const float FarLightmapScale = 0.15f;

        /// <summary>
        /// Large surfaces get lightmaps (baked bounce light and ambient occlusion). Thin or small things
        /// (rails, pipes, wheels, posts) are lit from light probes instead: lightmaps on them would cost
        /// space and show seams. The sea takes no part in the bake.
        /// </summary>
        static void ConfigureGI(GameObject go, MeshRenderer mr)
        {
            string n = go.name;
            // The sea is huge and flat; the lamp heads are emissive glass with no UVs. Neither helps the bake.
            if (n.StartsWith("Sea__") || n.StartsWith("Lamps"))
            {
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) & ~StaticEditorFlags.ContributeGI);
                mr.receiveGI = ReceiveGI.LightProbes;
                return;
            }
            string[] probeLit = { "Rail", "Pipe", "Bands", "Beams", "Rollers", "_AC_", "Flywheel", "Scoop", "Trim", "Door", "Lamps",
                                  "Wheels", "LampPosts", "Bollards", "Barrels", "Tracks", "Legs", "Cols", "Columns", "Pillars", "Saddles", "Crane_Boom",
                                  // detail pass: vehicle parts, small props, pallets, thin trims
                                  "Tyres", "Chassis", "Bumper", "Truck_Tank", "Harvester_Glass", "Manway", "IBC", "CableDrum", "Pallet",
                                  "Guides", "Coil", "ShutterPosts", "Coping" };
            bool probes = probeLit.Any(n.Contains);
            // Decals lying on lightmapped surfaces keep lightmaps so they match what is under them.
            string[] lightmappedDecals = { "Brine", "Yard_Paint", "RoadMarks", "Dock_Edge", "Puddles", "Weighbridge_Plate_", "ParkingLines", "Hatch" };
            if (n.EndsWith("__NC") && !lightmappedDecals.Any(n.Contains)) probes = true;
            if (probes)
            {
                mr.receiveGI = ReceiveGI.LightProbes;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                return;
            }
            mr.receiveGI = ReceiveGI.Lightmaps;
            mr.scaleInLightmap = n.EndsWith("__FAR") ? FarLightmapScale : 1f;
            mr.stitchLightmapSeams = true;
        }

        static LightingSettings LightingAsset()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "Lighting_Kappa" };
                AssetDatabase.CreateAsset(ls, LightingPath);
            }
            ls.autoGenerate = false;
            ls.bakedGI = true;
            ls.realtimeGI = false;
            // Shadowmask: the sun stays realtime (shadows on players), the bake adds bounce light and
            // a shadow mask that keeps distant buildings shadowed beyond the realtime shadow distance.
            ls.mixedBakeMode = MixedLightingMode.Shadowmask;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = LightmapTexelsPerMetre;
            ls.lightmapMaxSize = 2048;
            ls.lightmapPadding = 4;
            ls.lightmapCompression = LightmapCompression.HighQuality;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            ls.ao = true;
            ls.aoMaxDistance = 1.5f;
            ls.aoExponentIndirect = 1f;
            ls.aoExponentDirect = 0f;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 256;
            ls.maxBounces = 3;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(ls);
            return ls;
        }

        /// <summary>Probe columns on a 3 m grid over the playable area, at every floor with headroom.</summary>
        static void BuildLightProbes()
        {
            Physics.SyncTransforms();
            int mask = 1 << Layers.Environment;
            var positions = new List<Vector3>();
            for (float x = -60f; x <= 60.01f; x += 3f)
            for (float z = -39f; z <= 39.01f; z += 3f)
            {
                var hits = Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 60f, mask, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.normal.y < 0.6f) continue;
                    float clear = Physics.Raycast(h.point + Vector3.up * 0.05f, Vector3.up, out var up, 30f, mask, QueryTriggerInteraction.Ignore) ? up.distance : 30f;
                    if (clear < 1.2f) continue;
                    positions.Add(h.point + Vector3.up * 0.6f);
                    if (clear > 2.6f) positions.Add(h.point + Vector3.up * 2.2f);
                    if (clear > 7f) positions.Add(h.point + Vector3.up * 5.5f);
                }
            }
            var group = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            group.probePositions = positions.ToArray();
            Debug.Log($"[SALTLINE] Kappa: {positions.Count} light probes");
        }

        /// <summary>Baked reflection probes: a grid outdoors, and a box-projected probe in each interior.</summary>
        static void BuildReflectionProbes(Func<float, float, float, Vector3> B)
        {
            var root = new GameObject("ReflectionProbes").transform;
            void Probe(string name, Vector3 centre, Vector3 blenderSize, bool interior)
            {
                var p = new GameObject("RP_" + name).AddComponent<ReflectionProbe>();
                p.transform.SetParent(root, false);
                p.transform.position = centre;
                p.mode = ReflectionProbeMode.Baked;
                p.size = new Vector3(blenderSize.x, blenderSize.z, blenderSize.y);
                p.boxProjection = interior;
                p.blendDistance = interior ? 0.6f : 4f;
                p.importance = interior ? 2 : 1;
                p.resolution = 128;
                p.hdr = true;
                p.nearClipPlane = 0.1f;
                p.farClipPlane = 600f;
            }
            foreach (float x in new[] { -43.5f, -14.5f, 14.5f, 43.5f })
            foreach (float y in new[] { -18f, 18f })
                Probe($"Outdoor_{x:F0}_{y:F0}", B(x, y, 2.5f), new Vector3(37f, 44f, 24f), false);
            Probe("Pump_Ground", B(0f, 0f, 1.75f), new Vector3(15.2f, 13.2f, 3.4f), true);
            Probe("Pump_Upper", B(0f, 0f, 5.4f), new Vector3(15.2f, 13.2f, 3.4f), true);
            Probe("Pump_Annex", B(0f, 8f, 1.5f), new Vector3(6f, 2f, 3f), true);
            Probe("Hut", B(0f, 32.5f, 4.3f), new Vector3(5f, 4f, 2.6f), true);
            Probe("ControlRoom", B(0f, -13.8f, 1.7f), new Vector3(9f, 4.4f, 3.4f), true);
            foreach (float side in new[] { -1f, 1f })
            {
                string s = side < 0 ? "W" : "E";
                Probe("Shed_" + s, B(side * 20f, -18f, 2.75f), new Vector3(12f, 14f, 5.5f), true);
                Probe("PumpShed_" + s, B(side * 31f, 30.75f, 1.8f), new Vector3(5f, 5.5f, 3.6f), true);
                Probe("Weighbridge_" + s, B(side * 50f, -19.5f, 1.6f), new Vector3(6f, 5f, 3.2f), true);
            }
        }

        /// <summary>Looping machinery: the pump house in the middle and the two pump sheds behind the lanes.</summary>
        static void BuildAmbience(Func<float, float, float, Vector3> B)
        {
            var root = new GameObject("Ambience").transform;
            void Emit(string name, Vector3 at, float volume, float min, float max)
            {
                var go = new GameObject("AMB_" + name);
                go.transform.SetParent(root, false);
                go.transform.position = at;
                var e = go.AddComponent<Saltline.Audio.AmbientEmitter>();
                e.sound = "amb_hum";
                e.volume = volume;
                e.minDistance = min;
                e.maxDistance = max;
            }
            Emit("PumpHouse", B(0f, 0f, 1.5f), 1f, 5f, 34f);
            Emit("PumpShed_W", B(-31f, 30.75f, 1.2f), 0.7f, 3f, 20f);
            Emit("PumpShed_E", B(31f, 30.75f, 1.2f), 0.7f, 3f, 20f);
        }

        static bool Bake()
        {
            Lightmapping.lightingSettings = LightingAsset();
            var sw = Stopwatch.StartNew();
            // Unity cannot lay out lightmap UVs for some meshes made of many tiny pieces spread over a wide
            // area (pallet slats, fence pales, drain grates, bag loops) and leaves them out of the bake with a
            // warning each. They are all probe-lit details: they keep probe lighting and realtime sun shadows
            // (distance shadowmask), but do not block baked lamp light. Count them for the summary.
            int leftOut = 0;
            void OnLog(string msg, string _, LogType __) { if (msg.Contains("Invalid Mesh was removed from light baking input")) leftOut++; }
            Application.logMessageReceived += OnLog;
            bool ok;
            try { ok = Lightmapping.Bake(); }
            finally { Application.logMessageReceived -= OnLog; }
            int probes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
            if (ok) Debug.Log($"[SALTLINE] Kappa bake done in {sw.Elapsed.TotalSeconds:F0} s: {LightmapSettings.lightmaps.Length} lightmaps, {probes} light probes; " +
                                $"{leftOut} small probe-lit meshes left out by Unity (no usable lightmap UVs)");
            else Debug.LogError($"[SALTLINE] Kappa bake FAILED after {sw.Elapsed.TotalSeconds:F0} s");
            return ok;
        }

        /// <summary>
        /// "Realtime" emission, as the URP material inspector sets it. With no GI flag (None) Unity's material
        /// validation dropped the _EMISSION keyword whenever the baked Kappa scene was loaded (the bake, the
        /// tests), and the lamps rendered unlit. The project has no realtime GI, and the lamp renderers take no
        /// part in the bake, so the flag changes nothing else.
        /// </summary>
        const MaterialGlobalIlluminationFlags LampGIFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        /// <summary>
        /// Makes sure the lamp panels glow: called after the bake and before every player build.
        /// KappaMapTests.OnlyLampsAreEmissive checks it too.
        /// </summary>
        public static void EnsureLampEmission()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Lamp.mat");
            if (m == null || (m.IsKeywordEnabled("_EMISSION") && m.globalIlluminationFlags == LampGIFlags)) return;
            Debug.Log($"[SALTLINE] Kappa: M_Lamp emission was off (keyword {m.IsKeywordEnabled("_EMISSION")}, GI {m.globalIlluminationFlags}); enabling it.");
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = LampGIFlags;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
        }

        static void BuildLighting()
        {
            // The sun sits where the HDRI's sun was, so shadows agree with the sky's lighting.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.94f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadowAngle = 1.2f; // softness of the baked (shadowmask) shadows
            sun.transform.rotation = SunRotation;
            RenderSettings.sun = sun;

            RenderSettings.skybox = SkyMaterial();
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0042f;
            RenderSettings.fogColor = new Color(0.70f, 0.73f, 0.79f); // the HDRI's horizon

            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = PostProfile();
        }

        static Material SkyMaterial()
        {
            string path = MapDir + "/Sky_Kappa.mat";
            var shader = Shader.Find("Skybox/Panoramic");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SkyPath));
            m.SetFloat("_Mapping", 1f); // latitude-longitude
            m.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            m.SetFloat("_ImageType", 0f); // 360 degrees
            m.SetFloat("_MirrorOnBack", 0f);
            m.SetFloat("_Layout", 0f);
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            m.SetFloat("_Exposure", 1f);
            m.SetFloat("_Rotation", SkyRotation());
            EditorUtility.SetDirty(m);

            string oldCube = MapDir + "/Reflection_Kappa.asset"; // replaced by the baked sky reflection
            if (AssetDatabase.LoadAssetAtPath<Cubemap>(oldCube) != null) AssetDatabase.DeleteAsset(oldCube);
            return m;
        }

        /// <summary>
        /// Skybox/Panoramic samples u = 0.5 - (atan2(z, x) - _Rotation) / 2pi. The sign was measured, not
        /// read from the shader: a render looking straight at the Sun light matched the HDRI (0.99
        /// correlation) only with this sign. Pick the turn that puts the HDRI's sun behind the Sun light.
        /// </summary>
        static float SkyRotation()
        {
            Vector3 toSun = -(SunRotation * Vector3.forward);
            float wanted = Mathf.Atan2(toSun.z, toSun.x) * Mathf.Rad2Deg;
            float inImage = (0.5f - SkySunUV.x) * 360f;
            return Mathf.Repeat(wanted - inImage, 360f);
        }

        static VolumeProfile PostProfile()
        {
            string path = ContentFactory.DataDir + "/PostFX_Kappa.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            else
            {
                // Rebuild the overrides in place so the asset GUID (referenced by the scene) stays stable.
                foreach (var old in profile.components.ToList())
                {
                    profile.components.Remove(old);
                    Object.DestroyImmediate(old, true);
                }
            }
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.45f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.65f);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.35f);
            ca.contrast.Override(10f);
            ca.saturation.Override(4f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 1.0f, 1.08f, 0f));
            smh.highlights.Override(new Vector4(1.06f, 1.02f, 0.95f, 0f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.2f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void BuildCameras(System.Func<float, float, float, Vector3> B)
        {
            var camGo = new GameObject("SpectatorCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.farClipPlane = 1600f;
            Vector3 pos = B(0f, -78f, 42f);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(B(0f, 2f, 0f) - pos));
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        /// <summary>Named viewpoints used by the map tour (-maptour) for visual review.</summary>
        static void AddCaptureViews(System.Func<float, float, float, Vector3> B)
        {
            var root = new GameObject("CaptureViews").transform;
            void View(string name, Vector3 pos, Vector3 look, float ortho = 0f)
            {
                var t = new GameObject("VIEW_" + name).transform;
                t.SetParent(root, false);
                t.SetPositionAndRotation(pos, ortho > 0f ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.LookRotation(look - pos));
                if (ortho > 0f) t.localScale = new Vector3(ortho, 1f, 1f);
            }
            const float eye = 1.62f;
            View("00_overview_south", B(0f, -78f, 42f), B(0f, 2f, 0f));
            View("01_overview_north", B(30f, 82f, 46f), B(0f, -4f, 0f));
            View("02_red_spawn", B(-56f, -10.5f, eye), B(-44f, 4f, 1.4f));
            View("03_red_exit", B(-41f, 10.5f, eye), B(-12f, 2f, 2f));
            View("04_mid_courtyard", B(-23f, -5f, eye), B(0f, 0f, 2.5f));
            View("05_pump_ground", B(-6.8f, 3.0f, 0.1f + eye), B(6f, -2.5f, 1.4f));
            View("06_pump_upper", B(-3.0f, 0.5f, 3.7f + eye), B(-30f, -1f, 2.5f));
            View("07_pans", B(-25f, 21f, eye), B(8f, 27f, 2f));
            View("08_catwalk", B(-18.5f, 32.5f, 3.0f + eye), B(6f, 29.5f, 3f));
            View("09_channel", B(0f, 15.2f, -1.2f + eye), B(0f, 30f, -0.6f));
            View("10_shed", B(-25f, -13.5f, 0.06f + eye), B(-15f, -20.5f, 1.4f));
            View("11_dock", B(-21f, -33f, eye), B(4f, -28f, 2f));
            View("12_cliff_road", B(-56f, -33.5f, eye), B(-24f, -32f, 1.6f));
            View("13_dock_yard", B(-4f, -25f, eye), B(-14f, -15f, 1.6f));
            View("14_north_staging", B(-52f, 18f, eye), B(-28f, 31f, 2.5f));
            View("15_hut_window", B(0f, 31.3f, 3.0f + eye), B(0f, 8f, 1.5f));
            View("16_topdown", B(0f, 0f, 150f), B(0f, 0.01f, 0f), ortho: 40f);
            // Close-ups of the set dressing, for reviewing materials and lighting on the props.
            View("18_detail_truck", B(-25f, 17f, eye), B(-31f, 10f, 1.6f));
            View("19_detail_container", B(-25.5f, -4f, eye), B(-30.7f, -10.4f, 1.3f));
            View("20_detail_harvester", B(-6.5f, 18.5f, eye), B(-11.7f, 14.6f, 1.6f));
            View("21_detail_office", B(-30f, -3f, eye), B(-41f, 2f, 3.5f));
            View("22_detail_dump_truck", B(-43f, -24.5f, eye), B(-49f, -29.4f, 1.5f));
            View("23_detail_shutter", B(-46f, 22f, eye), B(-57.7f, 24f, 4f));
            var eyeMid = B(0f, -25f, 30f);
            View("17_sun_check", eyeMid, eyeMid - SunRotation * Vector3.forward * 10f);
        }
    }
}
