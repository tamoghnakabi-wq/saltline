using System.IO;
using System.Linq;
using Saltline.Combat;
using Saltline.Core;
using Saltline.FX;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Saltline.Sim;
using Saltline.Weapons;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Generates gameplay data and greybox content from code: layers, movement settings, the five
    /// weapons, game config, presentation materials, the networked player prefab and the Arena
    /// test scene. Weapon assets are only created when missing, so tuning edits in the Inspector
    /// survive re-running the factory (pass -resetweapons to regenerate them).
    /// </summary>
    public static class ContentFactory
    {
        public const string DataDir = "Assets/_Saltline/Data";
        public const string WeaponsDir = DataDir + "/Weapons";
        public const string ArtDir = "Assets/_Saltline/Art";
        public const string ResourcesDir = "Assets/_Saltline/Resources";
        public const string PlayerPrefabPath = "Assets/_Saltline/Prefabs/Player/Player.prefab";
        public const string ArenaScenePath = BootSceneFactory.ScenesDir + "/Arena.unity";

        [MenuItem("Saltline/Build/Content (data, player, arena)")]
        public static void Build()
        {
            foreach (var d in new[] { DataDir, WeaponsDir, ArtDir + "/Materials", ArtDir + "/Textures", ResourcesDir, "Assets/_Saltline/Prefabs/Player" })
                Directory.CreateDirectory(d);

            EnsureLayers();
            var motor = LoadOrCreate<MotorSettings>(DataDir + "/MotorSettings.asset");
            var db = BuildWeapons();
            var config = LoadOrCreate<GameConfig>(DataDir + "/GameConfig.asset");
            config.motor = motor;
            config.weapons = db;
            EditorUtility.SetDirty(config);
            var lib = BuildPresentation();
            AssetDatabase.SaveAssets();

            BuildPlayerPrefab(config);
            BuildArena(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[SALTLINE] Content built: weapons, config, player prefab, Arena scene.");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            foreach (var (index, name) in Layers.Named)
            {
                var el = layers.GetArrayElementAtIndex(index);
                if (el.stringValue != name) el.stringValue = name;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ weapons

        static WeaponDatabase BuildWeapons()
        {
            bool reset = System.Environment.GetCommandLineArgs().Contains("-resetweapons");
            var ar = Weapon("W0_VK21_Halyard", reset, w =>
            {
                w.id = 0; w.displayName = "VK-21 Halyard"; w.shortName = "HALYARD"; w.weaponClass = WeaponClass.AssaultRifle;
                w.description = "Balanced automatic rifle. Four chest hits down a full-health target.";
                w.slot = WeaponSlot.Primary; w.fireMode = FireMode.Automatic;
                w.damage = 26; w.headMultiplier = 1.6f; w.lowerTorsoMultiplier = 0.95f; w.limbMultiplier = 0.8f;
                w.falloffStart = 30; w.falloffEnd = 55; w.minDamageFactor = 0.77f; w.maxRange = 300; w.penetrationPower = 1.0f;
                w.roundsPerMinute = 750; w.magazineSize = 30; w.reserveAmmo = 150; w.reloadTime = 2.1f; w.emptyReloadTime = 2.6f;
                w.equipTime = 0.45f; w.holsterTime = 0.25f; w.adsTime = 0.22f; w.adsFov = 55f; w.moveSpeedMultiplier = 0.97f;
                w.hipSpread = 2.0f; w.adsSpread = 0.12f; w.moveSpread = 1.4f; w.airSpread = 4f; w.bloomPerShot = 0.12f; w.maxBloom = 1.2f; w.bloomRecovery = 5f;
                w.recoilPattern = Pattern(30, 0.55f, 0.8f, 0.09f); w.recoilRandomYaw = 0.12f; w.recoilAdsMultiplier = 0.75f;
                w.viewKickBack = 0.04f; w.viewKickUp = 2.2f; w.viewKickRoll = 1.4f; w.cameraShake = 0.2f;
                w.fireSoundId = "ar"; w.viewmodelOffset = new Vector3(0.19f, -0.2f, 0.40f); w.adsEyeRelief = 0.18f;
            });
            var smg = Weapon("W1_SMR9_Wasp", reset, w =>
            {
                w.id = 1; w.displayName = "SMR-9 Wasp"; w.shortName = "WASP"; w.weaponClass = WeaponClass.SMG;
                w.description = "Fast-firing and mobile. Deadly up close, falls off quickly past 12 m.";
                w.slot = WeaponSlot.Primary; w.fireMode = FireMode.Automatic;
                w.damage = 22; w.headMultiplier = 1.5f; w.limbMultiplier = 0.85f;
                w.falloffStart = 12; w.falloffEnd = 30; w.minDamageFactor = 0.64f; w.maxRange = 150; w.penetrationPower = 0.6f;
                w.roundsPerMinute = 900; w.magazineSize = 32; w.reserveAmmo = 160; w.reloadTime = 1.8f; w.emptyReloadTime = 2.2f;
                w.equipTime = 0.35f; w.holsterTime = 0.2f; w.adsTime = 0.16f; w.adsFov = 60f; w.moveSpeedMultiplier = 1.03f; w.adsMoveMultiplier = 1.05f;
                w.hipSpread = 1.6f; w.adsSpread = 0.4f; w.moveSpread = 0.8f; w.airSpread = 3f; w.bloomPerShot = 0.15f; w.maxBloom = 1.8f; w.bloomRecovery = 7f;
                w.recoilPattern = Pattern(32, 0.38f, 0.5f, 0.16f); w.recoilRandomYaw = 0.22f;
                w.viewKickBack = 0.03f; w.viewKickUp = 1.6f; w.viewKickRoll = 1.8f; w.cameraShake = 0.15f;
                w.fireSoundId = "smg"; w.viewmodelOffset = new Vector3(0.18f, -0.19f, 0.38f); w.adsEyeRelief = 0.17f;
            });
            var shotgun = Weapon("W2_TS12_Breaker", reset, w =>
            {
                w.id = 2; w.displayName = "TS-12 Breaker"; w.shortName = "BREAKER"; w.weaponClass = WeaponClass.Shotgun;
                w.description = "Pump-action. One well-placed shot at close range ends a fight.";
                w.slot = WeaponSlot.Primary; w.fireMode = FireMode.PumpAction; w.reloadMode = ReloadMode.PerShell;
                w.damage = 14; w.pellets = 9; w.headMultiplier = 1.25f; w.limbMultiplier = 0.85f;
                w.falloffStart = 6; w.falloffEnd = 18; w.minDamageFactor = 0.35f; w.maxRange = 60; w.penetrationPower = 0.3f;
                w.roundsPerMinute = 70; w.magazineSize = 6; w.reserveAmmo = 36; w.reloadStartTime = 0.35f; w.shellInsertTime = 0.5f;
                w.equipTime = 0.5f; w.adsTime = 0.25f; w.adsFov = 62f; w.moveSpeedMultiplier = 0.95f;
                w.hipSpread = 4.5f; w.adsSpread = 3.2f; w.moveSpread = 0.5f; w.airSpread = 1f; w.bloomPerShot = 0f; w.maxBloom = 0f;
                w.recoilPattern = new[] { new Vector2(0.2f, 3.5f) }; w.recoilRandomYaw = 0.6f; w.recoilRecoverySpeed = 7f; w.recoilRecoveryFraction = 0.7f;
                w.viewKickBack = 0.09f; w.viewKickUp = 7f; w.viewKickRoll = 3f; w.cameraShake = 0.6f; w.muzzleFlashScale = 1.5f;
                w.fireSoundId = "shotgun"; w.viewmodelOffset = new Vector3(0.2f, -0.2f, 0.36f); w.adsEyeRelief = 0.16f;
            });
            var sniper = Weapon("W3_LR338_Longreach", reset, w =>
            {
                w.id = 3; w.displayName = "LR-338 Longreach"; w.shortName = "LONGREACH"; w.weaponClass = WeaponClass.Sniper;
                w.description = "Bolt-action. Headshots kill outright; perfectly accurate only when scoped and still.";
                w.slot = WeaponSlot.Primary; w.fireMode = FireMode.BoltAction;
                w.damage = 95; w.headMultiplier = 2.2f; w.lowerTorsoMultiplier = 0.95f; w.limbMultiplier = 0.8f;
                w.falloffStart = 80; w.falloffEnd = 200; w.minDamageFactor = 0.85f; w.maxRange = 500; w.penetrationPower = 2.5f;
                w.roundsPerMinute = 45; w.magazineSize = 5; w.reserveAmmo = 25; w.reloadTime = 2.9f; w.emptyReloadTime = 3.5f;
                w.equipTime = 0.65f; w.holsterTime = 0.3f; w.adsTime = 0.36f; w.adsFov = 40f; w.hasScope = true; w.scopeFov = 18f;
                w.moveSpeedMultiplier = 0.9f; w.adsMoveMultiplier = 0.6f; w.blockSprintWhileReloading = true;
                w.hipSpread = 7f; w.adsSpread = 0f; w.moveSpread = 3f; w.airSpread = 10f; w.crouchSpreadMultiplier = 0.6f; w.bloomPerShot = 0f; w.maxBloom = 0f;
                w.recoilPattern = new[] { new Vector2(0.3f, 2.5f) }; w.recoilRandomYaw = 0.4f; w.recoilRecoverySpeed = 6f; w.recoilRecoveryFraction = 0.85f;
                w.viewKickBack = 0.1f; w.viewKickUp = 6f; w.viewKickRoll = 2f; w.cameraShake = 0.5f; w.tracerSpeed = 700f; w.muzzleFlashScale = 1.3f;
                w.fireSoundId = "sniper"; w.soundRange = 220f; w.viewmodelOffset = new Vector3(0.2f, -0.21f, 0.40f); w.adsEyeRelief = 0.09f;
            });
            var pistol = Weapon("W4_P45_Brace", reset, w =>
            {
                w.id = 4; w.displayName = "P-45 Brace"; w.shortName = "BRACE"; w.weaponClass = WeaponClass.Pistol;
                w.description = "Reliable semi-automatic sidearm. Quick to draw.";
                w.slot = WeaponSlot.Secondary; w.fireMode = FireMode.SemiAuto;
                w.damage = 34; w.headMultiplier = 1.6f; w.limbMultiplier = 0.85f;
                w.falloffStart = 15; w.falloffEnd = 35; w.minDamageFactor = 0.65f; w.maxRange = 150; w.penetrationPower = 0.5f;
                w.roundsPerMinute = 400; w.magazineSize = 12; w.reserveAmmo = 60; w.reloadTime = 1.4f; w.emptyReloadTime = 1.8f;
                w.equipTime = 0.3f; w.holsterTime = 0.2f; w.adsTime = 0.16f; w.adsFov = 60f; w.moveSpeedMultiplier = 1.05f;
                w.hipSpread = 1.4f; w.adsSpread = 0.25f; w.moveSpread = 0.9f; w.airSpread = 3f; w.bloomPerShot = 0.3f; w.maxBloom = 1.5f; w.bloomRecovery = 6f;
                w.recoilPattern = new[] { new Vector2(0.05f, 1.4f) }; w.recoilRandomYaw = 0.25f;
                w.viewKickBack = 0.05f; w.viewKickUp = 4f; w.viewKickRoll = 2f; w.cameraShake = 0.2f;
                w.fireSoundId = "pistol"; w.viewmodelOffset = new Vector3(0.16f, -0.16f, 0.36f); w.adsEyeRelief = 0.26f;
            });

            var db = LoadOrCreate<WeaponDatabase>(DataDir + "/WeaponDatabase.asset");
            db.weapons = new[] { ar, smg, shotgun, sniper, pistol };
            db.defaultPrimary = ar.id;
            db.defaultSecondary = pistol.id;
            EditorUtility.SetDirty(db);
            return db;
        }

        static WeaponDefinition Weapon(string file, bool reset, System.Action<WeaponDefinition> configure)
        {
            string path = $"{WeaponsDir}/{file}.asset";
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (w != null && !reset) return w;
            if (w == null)
            {
                w = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(w, path);
            }
            configure(w);
            EditorUtility.SetDirty(w);
            return w;
        }

        /// <summary>A learnable recoil pattern: climbs, then drifts right and back left.</summary>
        static Vector2[] Pattern(int n, float pitchStart, float pitchPeak, float yawSwing)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float pitch = Mathf.Lerp(pitchStart, pitchPeak, Mathf.Clamp01(i / 7f)) * (i > 14 ? 0.85f : 1f);
                float yaw = yawSwing * Mathf.Sin(i * 0.45f) + (i < 10 ? 0.04f : -0.05f);
                p[i] = new Vector2(yaw, pitch);
            }
            return p;
        }

        // ------------------------------------------------------------------ presentation

        static PresentationLibrary BuildPresentation()
        {
            var lib = LoadOrCreate<PresentationLibrary>(ResourcesDir + "/PresentationLibrary.asset");
            var radial = RadialTexture("Radial", 64);
            lib.teamRed = Lit("TeamRed", new Color(0.78f, 0.24f, 0.19f), 0.35f, 0f);
            lib.teamBlue = Lit("TeamBlue", new Color(0.2f, 0.42f, 0.78f), 0.35f, 0f);
            lib.neutralBody = Lit("Neutral", new Color(0.55f, 0.55f, 0.55f), 0.3f, 0f);
            lib.gunMetal = Lit("GunMetal", new Color(0.12f, 0.13f, 0.14f), 0.55f, 0.8f);
            lib.gunAccent = Lit("GunAccent", new Color(0.32f, 0.3f, 0.26f), 0.3f, 0.1f);
            lib.tracer = Unlit("Tracer", new Color(4f, 2.6f, 1.2f, 1f), null, additive: true);
            lib.muzzleFlash = Unlit("MuzzleFlash", new Color(3f, 2f, 0.9f, 1f), radial, additive: true);
            lib.impact = Unlit("Impact", new Color(0.05f, 0.05f, 0.05f, 0.85f), radial, additive: false);
            EditorUtility.SetDirty(lib);
            return lib;
        }

        internal static Material Lit(string name, Color c, float smooth, float metal)
        {
            string path = $"{ArtDir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color c, Texture2D tex, bool additive)
        {
            string path = $"{ArtDir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D RadialTexture(string name, int size)
        {
            string path = $"{ArtDir}/Textures/{name}.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ player prefab

        static void BuildPlayerPrefab(GameConfig config)
        {
            var go = new GameObject("Player") { layer = Layers.Player };
            var cc = go.AddComponent<CharacterController>();
            cc.radius = config.motor.radius;
            cc.height = config.motor.standHeight;
            cc.center = new Vector3(0f, config.motor.standHeight * 0.5f + config.motor.skinWidth, 0f);
            cc.skinWidth = config.motor.skinWidth;
            cc.stepOffset = config.motor.stepOffset;
            cc.slopeLimit = config.motor.slopeLimit;
            cc.minMoveDistance = 0f;
            go.AddComponent<NetworkObject>();
            var np = go.AddComponent<NetPlayer>();
            var so = new SerializedObject(np);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<PlayerView>();
            PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ arena

        static Material s_Concrete, s_Metal, s_Wood, s_Accent, s_Floor;

        static void BuildArena(PresentationLibrary lib)
        {
            s_Floor = Lit("Env_Floor", new Color(0.36f, 0.37f, 0.38f), 0.15f, 0f);
            s_Concrete = Lit("Env_Concrete", new Color(0.62f, 0.62f, 0.6f), 0.1f, 0f);
            s_Metal = Lit("Env_Metal", new Color(0.3f, 0.36f, 0.4f), 0.5f, 0.7f);
            s_Wood = Lit("Env_Wood", new Color(0.55f, 0.38f, 0.22f), 0.2f, 0f);
            s_Accent = Lit("Env_Accent", new Color(0.95f, 0.55f, 0.12f), 0.3f, 0f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.sun = sun;

            var env = new GameObject("Environment").transform;
            Block(env, "Ground", new Vector3(0, -0.5f, 0), new Vector3(80, 1, 80), s_Floor, SurfaceType.Concrete);
            Block(env, "Wall_N", new Vector3(0, 3, 40), new Vector3(80, 6, 1), s_Concrete, SurfaceType.Concrete);
            Block(env, "Wall_S", new Vector3(0, 3, -40), new Vector3(80, 6, 1), s_Concrete, SurfaceType.Concrete);
            Block(env, "Wall_E", new Vector3(40, 3, 0), new Vector3(1, 6, 80), s_Concrete, SurfaceType.Concrete);
            Block(env, "Wall_W", new Vector3(-40, 3, 0), new Vector3(1, 6, 80), s_Concrete, SurfaceType.Concrete);

            // Flank cover (the centre lane x in [-7, 7] stays open for the combat test).
            for (int i = 0; i < 4; i++)
            {
                float z = -15 + i * 10;
                Block(env, $"Crate_E{i}", new Vector3(12, 0.6f, z), Vector3.one * 1.2f, s_Wood, SurfaceType.Wood, 0.35f);
                Block(env, $"Barrier_W{i}", new Vector3(-12, 1.1f, z + 3), new Vector3(3.5f, 2.2f, 0.4f), s_Concrete, SurfaceType.Concrete);
            }
            Block(env, "WoodPanel", new Vector3(-16, 1.25f, 0), new Vector3(4, 2.5f, 0.08f), s_Wood, SurfaceType.Wood, 0.35f);
            Block(env, "SheetMetal", new Vector3(16, 1.25f, 6), new Vector3(4, 2.5f, 0.05f), s_Metal, SurfaceType.ThinMetal, 0.6f);
            Block(env, "LowWall", new Vector3(9, 0.55f, -4), new Vector3(3, 1.1f, 0.4f), s_Concrete, SurfaceType.Concrete);

            // Elevation: ramp up to a platform, stairs down the other side, low tunnel for slides.
            Block(env, "Platform", new Vector3(26, 2.3f, 0), new Vector3(8, 0.4f, 10), s_Metal, SurfaceType.Metal);
            // Ramp (21 degrees) rises from x=15.5 to the platform edge at x=22.
            var ramp = Block(env, "Ramp", new Vector3(18.75f, 1.06f, 0), new Vector3(7.0f, 0.4f, 4), s_Concrete, SurfaceType.Concrete);
            ramp.transform.rotation = Quaternion.Euler(0, 0, 21f);
            // Stairs: 0.3 m risers (below the 0.35 m step height) ending at the platform's south edge.
            for (int i = 0; i < 8; i++)
            {
                float h = 0.3f * (i + 1);
                Block(env, $"Stair{i}", new Vector3(26, h * 0.5f, -7.625f + i * 0.35f), new Vector3(3, h, 0.35f), s_Concrete, SurfaceType.Concrete);
            }
            Block(env, "TunnelRoof", new Vector3(-26, 1.45f, 0), new Vector3(3, 0.3f, 8), s_Concrete, SurfaceType.Concrete);
            Block(env, "TunnelL", new Vector3(-27.65f, 0.65f, 0), new Vector3(0.3f, 1.3f, 8), s_Concrete, SurfaceType.Concrete);
            Block(env, "TunnelR", new Vector3(-24.35f, 0.65f, 0), new Vector3(0.3f, 1.3f, 8), s_Concrete, SurfaceType.Concrete);
            for (int i = 0; i < 6; i++)
                Block(env, $"Stripe{i}", new Vector3(-7.2f + (i % 2) * 14.4f, 0.01f, -12 + (i / 2) * 12), new Vector3(0.2f, 0.02f, 4), s_Accent, SurfaceType.Concrete);

            var spawns = new GameObject("SpawnPoints").transform;
            float[] xs = { -4.5f, -1.5f, 1.5f, 4.5f };
            for (int i = 0; i < xs.Length; i++)
            {
                Spawn(spawns, $"Red_{i}", new Vector3(xs[i], 0.05f, -9f), 0f, Team.Red);
                Spawn(spawns, $"Blue_{i}", new Vector3(xs[i], 0.05f, 9f), 180f, Team.Blue);
            }

            var camGo = new GameObject("SpectatorCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0, 14, -26), Quaternion.Euler(28, 0, 0));
            camGo.AddComponent<AudioListener>();

            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = PostProfile();

            EditorSceneManager.SaveScene(scene, ArenaScenePath);
        }

        internal static VolumeProfile PostProfile()
        {
            string path = DataDir + "/PostFX_Default.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) return existing;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(1.1f);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.2f);
            ca.contrast.Override(8f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.18f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 size, Material m, SurfaceType surface, float penetrationCost = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = Layers.Environment;
            go.isStatic = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            var tag = go.AddComponent<SurfaceTag>();
            tag.surface = surface;
            tag.penetrationCost = penetrationCost;
            tag.damageRetained = SurfaceTag.DefaultRetained(surface);
            return go;
        }

        static void Spawn(Transform parent, string name, Vector3 pos, float yaw, Team team)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.AddComponent<SpawnPoint>().team = team;
        }
    }
}
