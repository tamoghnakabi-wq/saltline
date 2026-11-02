using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Saltline.FX;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Imports the Blender-built operator (ArtSource/Blender/operator_build.py writes
    /// Art/Characters/Operator/SK_Operator.fbx with three LODs plus baked T_OP_D/_N/_M) as a Humanoid,
    /// imports the Mixamo rifle locomotion clips (Art/Characters/Mixamo/*.fbx), builds the team
    /// materials, the animator controller and the P_Operator prefab, and registers them in the
    /// PresentationLibrary. Without the model the game keeps the hitbox-capsule bodies.
    /// </summary>
    public static class CharacterArtFactory
    {
        public const string Dir = ContentFactory.ArtDir + "/Characters/Operator";
        public const string ClipDir = ContentFactory.ArtDir + "/Characters/Mixamo";
        public const string ModelPath = Dir + "/SK_Operator.fbx";
        public const string ControllerPath = Dir + "/AC_Operator.controller";
        public const string PrefabPath = "Assets/_Saltline/Prefabs/Player/P_Operator.prefab";

        // 8 directions, clockwise from forward: file-name suffix and blend direction (x right, y forward).
        static readonly (string suffix, Vector2 dir)[] Directions =
        {
            ("forward", new Vector2(0f, 1f)), ("forward_right", new Vector2(0.7071f, 0.7071f)), ("right", new Vector2(1f, 0f)),
            ("backward_right", new Vector2(0.7071f, -0.7071f)), ("backward", new Vector2(0f, -1f)),
            ("backward_left", new Vector2(-0.7071f, -0.7071f)), ("left", new Vector2(-1f, 0f)), ("forward_left", new Vector2(-0.7071f, 0.7071f)),
        };

        public static readonly string[] DeathClips =
        {
            "death_from_the_front", "death_from_the_back", "death_from_right", "death_from_front_headshot", "death_from_back_headshot",
        };

        public static void Build(PresentationLibrary lib)
        {
            lib.operatorPrefab = null;
            if (!File.Exists(ModelPath))
            {
                Debug.LogWarning("[SALTLINE] Operator: model missing, players keep the hitbox-capsule bodies.");
                return;
            }
            WeaponArtFactory.ConfigureTextures(Dir, "OP");
            AssetDatabase.Refresh();
            var main = WeaponArtFactory.Body(Dir, "OP", Dir + "/M_OP_Main.mat");
            if (main == null) { Debug.LogWarning("[SALTLINE] Operator: textures missing."); return; }
            lib.operatorTeamRed = Tinted(main, Dir + "/M_OP_TeamRed.mat", new Color(0.86f, 0.16f, 0.11f));
            lib.operatorTeamBlue = Tinted(main, Dir + "/M_OP_TeamBlue.mat", new Color(0.14f, 0.40f, 0.98f));
            lib.operatorTeamNeutral = Tinted(main, Dir + "/M_OP_TeamNeutral.mat", new Color(0.78f, 0.78f, 0.74f));

            var avatar = ImportModel(main, lib.operatorTeamNeutral);
            var clips = ImportClips();
            var controller = BuildController(clips);
            lib.operatorPrefab = BuildPrefab(avatar, controller, clips);
            EditorUtility.SetDirty(lib);
        }

        static Material Tinted(Material main, string path, Color tint)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(main);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(main);
            m.shaderKeywords = main.shaderKeywords;
            m.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Avatar ImportModel(Material main, Material team)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;   // the rig adjusts spine and arm bones after the animator
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_OP_Main"), main);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_OP_Team"), team);
            mi.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException($"Operator: Humanoid avatar failed (avatar {(avatar == null ? "missing" : avatar.isValid ? "not human" : "invalid")}). Check the bone names in operator_build.py.");
            var mapped = mi.humanDescription.human.Select(h => h.humanName).ToArray();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var lods = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => $"{r.name} {r.sharedMesh.triangles.Length / 3} tris").ToArray();
            var lod0 = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            float height = lod0 != null ? lod0.sharedMesh.bounds.size.y * lod0.transform.lossyScale.y : 0f;
            Debug.Log($"[SALTLINE] Operator: Humanoid avatar valid, {mapped.Length} bones mapped, mesh height {height:F2} (scaled by {lod0?.transform.lossyScale.y:F3}); {string.Join(", ", lods)}; LODGroup {(model.GetComponentInChildren<LODGroup>() != null ? "yes" : "no")}");
            return avatar;
        }

        static bool Loops(string name) => name.StartsWith("idle") || name.StartsWith("walk") || name.StartsWith("run") || name.StartsWith("sprint") || name == "jump_loop";

        static Dictionary<string, AnimationClip> ImportClips()
        {
            var clips = new Dictionary<string, AnimationClip>();
            if (!Directory.Exists(ClipDir)) { Debug.LogWarning("[SALTLINE] Operator: no animation clips; the character will hold its bind pose."); return clips; }
            foreach (var path in Directory.GetFiles(ClipDir, "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.importCameras = false;
                mi.importLights = false;
                mi.importBlendShapes = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.resampleCurves = true;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                var defs = mi.defaultClipAnimations;
                if (defs.Length == 0) { Debug.LogWarning($"[SALTLINE] Operator: {name} has no animation."); continue; }
                var c = defs[0];
                bool loop = Loops(name);
                bool death = name.StartsWith("death");
                c.name = name;
                c.loopTime = loop;
                c.loopPose = false;
                c.lockRootRotation = true;           // body yaw comes from the aim, not the clip
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;            // feet stay on the motor's ground height
                c.keepOriginalPositionY = false;
                c.heightFromFeet = true;
                c.lockRootPositionXZ = death;        // deaths fall where they were authored; locomotion stays in place
                c.keepOriginalPositionXZ = true;
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => a.name == name);
                if (clip != null) clips[name] = clip;
            }
            Debug.Log($"[SALTLINE] Operator: {clips.Count} clips; forward speeds (clip units/s) walk {Speed(clips, "walk_forward")}, run {Speed(clips, "run_forward")}, sprint {Speed(clips, "sprint_forward")}, crouch {Speed(clips, "walk_crouching_forward")}");
            return clips;
        }

        static string Speed(Dictionary<string, AnimationClip> clips, string n) => clips.TryGetValue(n, out var c) ? c.averageSpeed.magnitude.ToString("F2") : "-";

        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter(OperatorRig.MoveXName, AnimatorControllerParameterType.Float);
            ac.AddParameter(OperatorRig.MoveZName, AnimatorControllerParameterType.Float);
            ac.AddParameter(OperatorRig.CrouchedName, AnimatorControllerParameterType.Bool);
            ac.AddParameter(OperatorRig.GroundedName, AnimatorControllerParameterType.Bool);
            ac.AddParameter(OperatorRig.DeadName, AnimatorControllerParameterType.Bool);
            ac.AddParameter(OperatorRig.DeathIndexName, AnimatorControllerParameterType.Int);
            var ps = ac.parameters;
            foreach (var p in ps) if (p.name == OperatorRig.GroundedName) p.defaultBool = true;
            ac.parameters = ps;

            var layers = ac.layers;
            layers[0].iKPass = false;
            ac.layers = layers;
            var sm = ac.layers[0].stateMachine;
            AnimationClip C(string n) => clips.TryGetValue(n, out var c) ? c : null;

            // standing: idle in the middle, walk / run / sprint rings at gait 1 / 2 / 3
            var loco = sm.AddState("Locomotion");
            var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = OperatorRig.MoveXName, blendParameterY = OperatorRig.MoveZName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ac);
            if (C("idle") != null) tree.AddChild(C("idle"), Vector2.zero);
            foreach (var (gait, prefix) in new[] { (1f, "walk_"), (2f, "run_"), (3f, "sprint_") })
                foreach (var (suffix, dir) in Directions)
                    if (C(prefix + suffix) != null) tree.AddChild(C(prefix + suffix), dir * gait);
            loco.motion = tree;
            sm.defaultState = loco;

            var crouch = sm.AddState("Crouch");
            var ctree = new BlendTree { name = "Crouch", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = OperatorRig.MoveXName, blendParameterY = OperatorRig.MoveZName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(ctree, ac);
            if (C("idle_crouching") != null) ctree.AddChild(C("idle_crouching"), Vector2.zero);
            foreach (var (suffix, dir) in Directions)
                if (C("walk_crouching_" + suffix) != null) ctree.AddChild(C("walk_crouching_" + suffix), dir);
            crouch.motion = ctree;

            var air = sm.AddState("Air");
            air.motion = C("jump_loop");

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = dur;
                t.interruptionSource = TransitionInterruptionSource.Destination;
                return t;
            }
            T(loco, crouch, 0.18f).AddCondition(AnimatorConditionMode.If, 0, OperatorRig.CrouchedName);
            T(crouch, loco, 0.18f).AddCondition(AnimatorConditionMode.IfNot, 0, OperatorRig.CrouchedName);
            T(loco, air, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0, OperatorRig.GroundedName);
            T(crouch, air, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0, OperatorRig.GroundedName);
            var land = T(air, loco, 0.10f);
            land.AddCondition(AnimatorConditionMode.If, 0, OperatorRig.GroundedName);
            land.AddCondition(AnimatorConditionMode.IfNot, 0, OperatorRig.CrouchedName);
            var landC = T(air, crouch, 0.10f);
            landC.AddCondition(AnimatorConditionMode.If, 0, OperatorRig.GroundedName);
            landC.AddCondition(AnimatorConditionMode.If, 0, OperatorRig.CrouchedName);

            int deaths = 0;
            for (int n = 0; n < DeathClips.Length; n++)
            {
                var clip = C(DeathClips[n]);
                if (clip == null) continue;
                int i = deaths++;   // DeathIndex counts only the clips that exist
                var d = sm.AddState("Death" + i);
                d.motion = clip;
                var t = sm.AddAnyStateTransition(d);
                t.hasExitTime = false;
                t.duration = 0.08f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.If, 0, OperatorRig.DeadName);
                t.AddCondition(AnimatorConditionMode.Equals, i, OperatorRig.DeathIndexName);
                T(d, loco, 0f).AddCondition(AnimatorConditionMode.IfNot, 0, OperatorRig.DeadName);
            }
            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        static GameObject BuildPrefab(Avatar avatar, AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = new GameObject("P_Operator");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // the local player's body only casts shadows
            anim.updateMode = AnimatorUpdateMode.Normal;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = false;
                smr.quality = SkinQuality.Bone4;
                smr.skinnedMotionVectors = true;
            }
            var lod = inst.GetComponentInChildren<LODGroup>();
            if (lod != null)
            {
                var lods = lod.GetLODs();
                float[] heights = { 0.28f, 0.09f, 0.008f };
                for (int i = 0; i < lods.Length && i < heights.Length; i++) lods[i].screenRelativeTransitionHeight = heights[i];
                lod.SetLODs(lods);
                lod.fadeMode = LODFadeMode.None;
            }
            var rig = root.AddComponent<OperatorRig>();
            rig.animator = anim;
            float S(string n, float fallback) => clips.TryGetValue(n, out var c) && c.averageSpeed.magnitude > 0.2f ? c.averageSpeed.magnitude : fallback;
            rig.walkSpeed = S("walk_forward", rig.walkSpeed);
            rig.runSpeed = Mathf.Max(rig.walkSpeed + 0.5f, S("run_forward", rig.runSpeed));
            rig.sprintSpeed = Mathf.Max(rig.runSpeed + 0.5f, S("sprint_forward", rig.sprintSpeed));
            rig.crouchSpeed = S("walk_crouching_forward", rig.crouchSpeed);
            rig.deathClips = DeathClips.Count(n => clips.ContainsKey(n));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[SALTLINE] Operator: prefab {PrefabPath} ({controller.layers[0].stateMachine.states.Length} animator states)");
            return prefab;
        }
    }
}
