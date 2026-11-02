using System.Collections.Generic;
using System.IO;
using System.Linq;
using Saltline.Combat;
using Saltline.Match;
using Saltline.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        public const string MatDir = MapDir + "/Materials";
        public const string ScenePath = BootSceneFactory.ScenesDir + "/Kappa.unity";

        /// <summary>Material name -> (texture, tint, smoothness, metallic, surface, penetration cost).</summary>
        static readonly Dictionary<string, (string tex, Color tint, float smooth, float metal, SurfaceType surface, float cost)> Mats = new()
        {
            ["M_Asphalt"] = ("Asphalt", Color.white, 0.55f, 0f, SurfaceType.Concrete, 0f),
            ["M_Concrete"] = ("Concrete", Color.white, 0.45f, 0f, SurfaceType.Concrete, 0f),
            ["M_ConcreteFloor"] = ("ConcreteFloor", Color.white, 0.55f, 0f, SurfaceType.Concrete, 0f),
            ["M_Facade"] = ("Facade", Color.white, 0.6f, 0f, SurfaceType.Concrete, 0f),
            ["M_Salt"] = ("Salt", Color.white, 0.5f, 0f, SurfaceType.Dirt, 0f),
            ["M_Sacks"] = ("Sacks", Color.white, 0.4f, 0f, SurfaceType.Dirt, 0f),
            ["M_Metal"] = ("Metal", Color.white, 0.75f, 0.45f, SurfaceType.Metal, 0f),
            ["M_Plate"] = ("Metal", new Color(1.45f, 1.38f, 1.25f), 0.7f, 0.45f, SurfaceType.ThinMetal, 0.6f),
            ["M_Grate"] = ("Grate", Color.white, 0.7f, 0.6f, SurfaceType.Metal, 0f),
            ["M_TankPaint"] = ("TankPaint", Color.white, 0.7f, 0.1f, SurfaceType.Metal, 0f),
            ["M_Rust"] = ("Rust", Color.white, 0.6f, 0.3f, SurfaceType.Metal, 0f),
            ["M_Corrugated"] = ("Corrugated", Color.white, 0.7f, 0.35f, SurfaceType.ThinMetal, 0.6f),
            ["M_Container"] = ("Container", Color.white, 0.6f, 0.25f, SurfaceType.Metal, 0f),
            ["M_Wood"] = ("Wood", Color.white, 0.5f, 0f, SurfaceType.Wood, 0.35f),
            ["M_Rock"] = ("Rock", Color.white, 0.45f, 0f, SurfaceType.Concrete, 0f),
            ["M_Hazard"] = ("Hazard", Color.white, 0.6f, 0f, SurfaceType.Metal, 0f),
            ["M_PaintYellow"] = ("Paint", new Color(0.86f, 0.62f, 0.1f), 0.6f, 0.1f, SurfaceType.Metal, 0f),
            ["M_PaintRed"] = ("Paint", new Color(0.72f, 0.13f, 0.09f), 0.5f, 0f, SurfaceType.Concrete, 0f),
            ["M_PaintBlue"] = ("Paint", new Color(0.1f, 0.32f, 0.72f), 0.5f, 0f, SurfaceType.Concrete, 0f),
            ["M_Dark"] = ("Paint", new Color(0.07f, 0.07f, 0.07f), 0.45f, 0f, SurfaceType.Metal, 0f),
            ["M_Brine"] = ("Water", new Color(0.86f, 0.64f, 0.68f), 0.92f, 0f, SurfaceType.Water, 0f),
            ["M_Sea"] = ("Water", new Color(0.05f, 0.15f, 0.2f), 0.93f, 0f, SurfaceType.Water, 0f),
            ["M_Lamp"] = (null, new Color(1f, 0.86f, 0.62f), 0.5f, 0f, SurfaceType.Glass, 0.05f),
            ["M_Clip"] = (null, Color.magenta, 0f, 0f, SurfaceType.Concrete, 0f),
        };

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
            ConfigureModel(materials);
            BuildScene();
        }

        // ------------------------------------------------------------------ assets

        static void ConfigureTextures()
        {
            foreach (string path in Directory.GetFiles(TexDir, "T_*.png"))
            {
                string p = path.Replace('\\', '/');
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (ti == null) { AssetDatabase.ImportAsset(p); ti = (TextureImporter)AssetImporter.GetAtPath(p); }
                bool normal = p.EndsWith("_N.png");
                bool changed = false;
                void Set<T>(T current, T wanted, System.Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); changed = true; } }
                Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
                Set(ti.sRGBTexture, !normal, v => ti.sRGBTexture = v);
                Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
                Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
                Set(ti.anisoLevel, 8, v => ti.anisoLevel = v);
                Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
                Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
                Set(ti.maxTextureSize, 1024, v => ti.maxTextureSize = v);
                Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
                if (changed) ti.SaveAndReimport();
            }
        }

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
                var albedo = spec.tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{spec.tex}_D.png") : null;
                var normal = spec.tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{spec.tex}_N.png") : null;
                m.SetTexture("_BaseMap", albedo);
                m.SetTexture("_MainTex", albedo);
                // Smoothness comes from the albedo alpha (rust is rough, paint and puddles are not).
                if (albedo != null)
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
                    m.SetFloat("_BumpScale", name == "M_Sea" ? 0.35f : 1f);
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
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
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
            mi.generateSecondaryUV = false;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var (name, mat) in materials)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene()
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
            if (red0 == null || lamp0 == null) { Debug.LogError("[SALTLINE] Kappa.fbx is missing its markers."); return; }
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
                    l.range = 9f;
                    l.intensity = 2.2f;
                    l.color = new Color(1f, 0.84f, 0.62f);
                    l.shadows = LightShadows.None;
                    l.transform.SetParent(lightRoot, false);
                    l.transform.position = t.position;
                    Object.DestroyImmediate(go);
                    lights++;
                    continue;
                }
                var mr = go.GetComponent<MeshRenderer>();
                var mf = go.GetComponent<MeshFilter>();
                go.layer = Layers.Environment;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                if (mr == null || mf == null) continue;

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
            BuildCameras(B);
            AddCaptureViews(B);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SALTLINE] Kappa scene: {colliders} collidable, {clips} clips, {decor} decor, {spawns} spawns, {lights} lamps -> {ScenePath}");
        }

        static void BuildLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.7f;
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sun.transform.rotation = Quaternion.Euler(36f, 32f, 0f); // late afternoon, from over the sea
            RenderSettings.sun = sun;

            RenderSettings.skybox = SkyMaterial();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.64f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.49f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.22f, 0.2f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = ReflectionCubemap();
            RenderSettings.reflectionIntensity = 0.9f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0042f;
            RenderSettings.fogColor = new Color(0.71f, 0.77f, 0.83f);

            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = PostProfile();
        }

        static Material SkyMaterial()
        {
            string path = MapDir + "/Sky_Kappa.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_SunDisk", 2f);
            m.SetFloat("_SunSize", 0.035f);
            m.SetFloat("_SunSizeConvergence", 6f);
            m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetColor("_SkyTint", new Color(0.5f, 0.56f, 0.64f));
            m.SetColor("_GroundColor", new Color(0.33f, 0.38f, 0.43f));
            m.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Small sky-gradient cubemap so metal and water reflect a sky without a lighting bake.</summary>
        static Cubemap ReflectionCubemap()
        {
            string path = MapDir + "/Reflection_Kappa.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            const int size = 64;
            var cube = new Cubemap(size, TextureFormat.RGBA32, true);
            Color zenith = new Color(0.36f, 0.5f, 0.68f), horizon = new Color(0.74f, 0.79f, 0.84f), ground = new Color(0.3f, 0.29f, 0.27f);
            Vector3 sunDir = -(Quaternion.Euler(36f, 32f, 0f) * Vector3.forward);
            foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
            {
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float s = (x + 0.5f) / size * 2f - 1f, t = (y + 0.5f) / size * 2f - 1f;
                    Vector3 d = face switch
                    {
                        CubemapFace.PositiveX => new Vector3(1, -t, -s),
                        CubemapFace.NegativeX => new Vector3(-1, -t, s),
                        CubemapFace.PositiveY => new Vector3(s, 1, t),
                        CubemapFace.NegativeY => new Vector3(s, -1, -t),
                        CubemapFace.PositiveZ => new Vector3(s, -t, 1),
                        _ => new Vector3(-s, -t, -1),
                    };
                    d.Normalize();
                    Color c = d.y >= 0f ? Color.Lerp(horizon, zenith, Mathf.Pow(d.y, 0.6f)) : Color.Lerp(horizon, ground, Mathf.Pow(-d.y, 0.35f));
                    float sunAmt = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, sunDir)), 64f);
                    c += new Color(1f, 0.9f, 0.75f) * sunAmt * 2.5f;
                    px[y * size + x] = c;
                }
                cube.SetPixels(px, face);
            }
            cube.Apply(true);
            if (existing != null)
            {
                EditorUtility.CopySerialized(cube, existing); // keep the GUID stable across rebuilds
                Object.DestroyImmediate(cube);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(cube, path);
            return cube;
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
        }
    }
}
