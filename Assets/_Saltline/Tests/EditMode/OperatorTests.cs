using System.IO;
using System.Linq;
using NUnit.Framework;
using Saltline.FX;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Saltline.Tests
{
    /// <summary>
    /// The operator character: the two-bone IK the rig uses for arms and legs, and the assets
    /// CharacterArtFactory builds during setup (Humanoid avatar, LODs, controller, prefab).
    /// </summary>
    public class OperatorTests
    {
        const string Dir = "Assets/_Saltline/Art/Characters/Operator";
        const string ModelPath = Dir + "/SK_Operator.fbx";
        const string ControllerPath = Dir + "/AC_Operator.controller";
        const string PrefabPath = "Assets/_Saltline/Prefabs/Player/P_Operator.prefab";
        const string ClipDir = "Assets/_Saltline/Art/Characters/Mixamo";

        static (Transform a, Transform b, Transform c, GameObject root) Chain(float upper, float lower)
        {
            var root = new GameObject("ik");
            var a = new GameObject("a").transform; a.SetParent(root.transform, false);
            var b = new GameObject("b").transform; b.SetParent(a, false); b.localPosition = new Vector3(upper, 0f, 0f);
            var c = new GameObject("c").transform; c.SetParent(b, false); c.localPosition = new Vector3(lower, 0f, 0f);
            return (a, b, c, root);
        }

        [Test]
        public void TwoBoneIkReachesTargetsAndKeepsBoneLengths()
        {
            var (a, b, c, root) = Chain(0.30f, 0.26f);
            try
            {
                var rng = new System.Random(7);
                for (int i = 0; i < 200; i++)
                {
                    Vector3 dir = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1).normalized;
                    float dist = Mathf.Lerp(0.08f, 0.54f, (float)rng.NextDouble());
                    Vector3 target = dir * dist;
                    Vector3 pole = Vector3.down + Vector3.right * 0.3f;
                    TwoBoneIK.Solve(a, b, c, target, pole);
                    Assert.Less(Vector3.Distance(c.position, target), 1e-3f, $"target {target}");
                    Assert.AreEqual(0.30f, Vector3.Distance(a.position, b.position), 1e-4f);
                    Assert.AreEqual(0.26f, Vector3.Distance(b.position, c.position), 1e-4f);
                    // the elbow bends toward the pole side of the shoulder-target line
                    Vector3 bend = Vector3.ProjectOnPlane(b.position - a.position, dir);
                    Vector3 poleSide = Vector3.ProjectOnPlane(pole, dir);
                    if (poleSide.sqrMagnitude > 1e-3f && bend.sqrMagnitude > 1e-4f) Assert.Greater(Vector3.Dot(bend, poleSide), 0f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TwoBoneIkStraightensTowardsAnUnreachableTarget()
        {
            var (a, b, c, root) = Chain(0.30f, 0.26f);
            try
            {
                Vector3 target = new Vector3(0.2f, 0.9f, 0.3f);
                TwoBoneIK.Solve(a, b, c, target, Vector3.down);
                Vector3 toTarget = target.normalized;
                Assert.Greater(Vector3.Dot((c.position - a.position).normalized, toTarget), 0.9999f);
                Assert.AreEqual(0.56f, Vector3.Distance(a.position, c.position), 2e-3f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void OperatorModelIsAValidHumanoidWithThreeLods()
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            Assert.IsNotNull(avatar, "SK_Operator.fbx has no avatar (run SaltlineBuild.Setup)");
            Assert.IsTrue(avatar.isValid && avatar.isHuman, "the operator avatar is not a valid Humanoid");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var lod = model.GetComponentInChildren<LODGroup>();
            Assert.IsNotNull(lod, "no LODGroup");
            var lods = lod.GetLODs();
            Assert.AreEqual(3, lods.Length);
            int prev = int.MaxValue;
            foreach (var l in lods)
            {
                var smr = l.renderers.OfType<SkinnedMeshRenderer>().Single();
                int tris = smr.sharedMesh.triangles.Length / 3;
                Assert.Less(tris, prev, "each LOD must have fewer triangles than the one before");
                prev = tris;
                Assert.AreEqual(2, smr.sharedMaterials.Length, "slots: main + team colour");
                Assert.LessOrEqual(smr.sharedMesh.GetAllBoneWeights().Length, smr.sharedMesh.vertexCount * 4);
            }
        }

        [Test]
        public void PrefabIsWiredForTheGame()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "P_Operator.prefab missing (run SaltlineBuild.Setup)");
            var rig = prefab.GetComponent<OperatorRig>();
            Assert.IsNotNull(rig);
            Assert.IsNotNull(rig.animator);
            Assert.IsTrue(rig.animator.avatar != null && rig.animator.avatar.isHuman);
            Assert.IsFalse(rig.animator.applyRootMotion, "the motor moves players; root motion must stay off");
            Assert.AreEqual(ControllerPath, AssetDatabase.GetAssetPath(rig.animator.runtimeAnimatorController));
            Assert.Less(rig.walkSpeed, rig.runSpeed);
            Assert.Less(rig.runSpeed, rig.sprintSpeed);

            var lib = PresentationLibrary.Instance;
            Assert.AreEqual(prefab, lib.operatorPrefab);
            Assert.IsNotNull(lib.operatorTeamRed);
            Assert.IsNotNull(lib.operatorTeamBlue);
            Assert.IsNotNull(lib.operatorTeamNeutral);
        }

        [Test]
        public void ControllerHasEveryStateAndParameter()
        {
            if (!Directory.Exists(ClipDir) || Directory.GetFiles(ClipDir, "*.fbx").Length == 0)
                Assert.Ignore("Mixamo clips are not in this checkout (they are not redistributable; see docs/ANIMATION.md)");
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(ac);
            foreach (var p in new[] { OperatorRig.MoveXName, OperatorRig.MoveZName, OperatorRig.CrouchedName, OperatorRig.GroundedName, OperatorRig.DeadName, OperatorRig.DeathIndexName })
                Assert.IsTrue(ac.parameters.Any(x => x.name == p), $"parameter {p}");
            var states = ac.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            var loco = (BlendTree)states["Locomotion"].motion;
            Assert.AreEqual(1 + 3 * 8, loco.children.Length, "idle + walk/run/sprint x 8 directions");
            Assert.IsTrue(loco.children.All(c => c.motion != null));
            var crouch = (BlendTree)states["Crouch"].motion;
            Assert.AreEqual(1 + 8, crouch.children.Length, "crouch idle + 8 directions");
            Assert.IsNotNull(states["Air"].motion);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            int deaths = prefab.GetComponent<OperatorRig>().deathClips;
            Assert.GreaterOrEqual(deaths, 1);
            for (int i = 0; i < deaths; i++)
            {
                Assert.IsTrue(states.ContainsKey("Death" + i), $"Death{i}");
                Assert.IsFalse(((AnimationClip)states["Death" + i].motion).isLooping, "deaths hold their last frame");
            }
            foreach (var c in loco.children)
                Assert.IsTrue(((AnimationClip)c.motion).isLooping || c.motion.name == "idle", $"{c.motion.name} should loop");
        }
    }
}
