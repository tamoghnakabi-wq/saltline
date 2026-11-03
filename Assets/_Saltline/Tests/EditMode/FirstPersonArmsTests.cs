using System.Linq;
using NUnit.Framework;
using Saltline.FX;
using Saltline.Weapons;
using UnityEditor;
using UnityEngine;

namespace Saltline.Tests
{
    /// <summary>
    /// The first-person arms: the hand-orienting IK they share with the third-person operator, the imported
    /// prefab, and that on every weapon the hands land on the grips while the shoulder cut stays out of view.
    /// </summary>
    public class FirstPersonArmsTests
    {
        const string PrefabPath = "Assets/_Saltline/Prefabs/Player/P_OperatorArms.prefab";
        const float ViewmodelFov = 56f;   // PlayerCameraRig's viewmodel camera

        [Test]
        public void ArmChainPutsTheWristOnTargetAndOrientsTheHand()
        {
            var root = new GameObject("arm");
            try
            {
                Transform Bone(string n, Transform parent, Vector3 local)
                {
                    var t = new GameObject(n).transform;
                    t.SetParent(parent, false);
                    t.localPosition = local;
                    return t;
                }
                // T-pose right arm along +x, palm down, thumb forward (+z), like the operator.
                var upper = Bone("upper", root.transform, Vector3.zero);
                var fore = Bone("fore", upper, new Vector3(0.27f, 0f, 0f));
                var hand = Bone("hand", fore, new Vector3(0.255f, 0f, 0f));
                var index = Bone("index", hand, new Vector3(0.095f, 0f, 0.026f));
                var middle = Bone("middle", hand, new Vector3(0.099f, 0f, 0.001f));
                var little = Bone("little", hand, new Vector3(0.085f, 0f, -0.042f));
                var arm = ArmChain.Create(upper, fore, hand, middle, index, little);
                Assert.IsTrue(arm.oriented);
                Assert.AreEqual(0.099f, arm.palm, 1e-3f);

                var rng = new System.Random(11);
                for (int i = 0; i < 100; i++)
                {
                    Vector3 target = new Vector3(R(rng, -0.15f, 0.25f), R(rng, -0.3f, 0.05f), R(rng, 0.1f, 0.3f));   // all within reach (0.525 m)
                    Vector3 longDir = new Vector3(R(rng, -1f, 1f), R(rng, -1f, 1f), R(rng, -1f, 1f)).normalized;
                    Vector3 across = Vector3.Cross(longDir, Random3(rng)).normalized;
                    ArmChain.Solve(arm, target, new Vector3(0.3f, -0.8f, 0f), longDir, across);
                    Assert.Less(Vector3.Distance(hand.position, target), 1e-3f, $"wrist off target {i}");
                    Assert.Less(Vector3.Angle(middle.position - hand.position, longDir), 1f, $"fingers not along the requested direction {i}");
                    Vector3 knuckles = Vector3.ProjectOnPlane(little.position - index.position, longDir);
                    Assert.Less(Vector3.Angle(knuckles, across), 1f, $"knuckle line not along the requested direction {i}");
                    Assert.AreEqual(0.27f, Vector3.Distance(upper.position, fore.position), 1e-4f);
                    Assert.AreEqual(0.255f, Vector3.Distance(fore.position, hand.position), 1e-4f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static Vector3 Random3(System.Random r) => new Vector3(R(r, -1f, 1f), R(r, -1f, 1f), R(r, -1f, 1f));

        [Test]
        public void ArmsPrefabIsWiredForTheGame()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "P_OperatorArms.prefab missing (run SaltlineBuild.Setup)");
            var arms = prefab.GetComponent<FirstPersonArms>();
            Assert.IsNotNull(arms);
            Assert.IsNotNull(arms.model);
            Assert.IsEmpty(prefab.GetComponentsInChildren<Animator>(true), "the arms are posed in code; an Animator would fight it");
            var smr = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(1, smr.Length);
            Assert.AreEqual(2, smr[0].sharedMaterials.Length, "main and team materials");
            Assert.IsTrue(smr[0].sharedMaterials.All(m => m != null));
            int tris = smr[0].sharedMesh.triangles.Length / 3;
            Assert.That(tris, Is.InRange(4000, 40000));
            foreach (var side in new[] { "Left", "Right" })
                foreach (var b in new[] { "Shoulder", "Arm", "ForeArm", "Hand", "HandIndex1", "HandIndex3", "HandMiddle1", "HandPinky1", "HandThumb1" })
                    Assert.IsTrue(prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == side + b), $"bone {side + b} missing");

            var lib = PresentationLibrary.Instance;
            Assert.AreEqual(prefab, lib.armsPrefab);
            Assert.IsNotNull(lib.armsTeamRed);
            Assert.IsNotNull(lib.armsTeamBlue);
            Assert.IsNotNull(lib.armsTeamNeutral);
        }

        [Test]
        public void HandsReachTheGripsAndShouldersStayOutOfView()
        {
            var lib = PresentationLibrary.Instance;
            Assert.IsNotNull(lib.armsPrefab, "arms prefab missing (run SaltlineBuild.Setup)");
            Assert.IsNotNull(lib.weapons);
            float tanHalf = Mathf.Tan(ViewmodelFov * 0.5f * Mathf.Deg2Rad);
            var view = new GameObject("viewmodel root");
            try
            {
                foreach (var w in lib.weapons.weapons)
                {
                    if (w == null || !WeaponModel.HasModel(w.weaponClass)) continue;
                    var gun = WeaponModel.Build(w.weaponClass, view.transform, 0, WeaponView.FirstPerson, out _, out var sight, out var rig);
                    var arms = Object.Instantiate(lib.armsPrefab, view.transform, false).GetComponent<FirstPersonArms>();
                    try
                    {
                        gun.localPosition = w.viewmodelOffset;
                        arms.Bind(gun, rig, w.weaponClass, w.viewmodelOffset);
                        Assert.IsTrue(arms.Bound, $"{w.shortName}: no Grip_R marker");

                        // The cut at each shoulder (about 7 cm around the joint) is below or behind the viewmodel frustum.
                        foreach (var a in new[] { arms.AnchorRight, arms.AnchorLeft })
                            Assert.IsTrue(a.z <= 0f || a.y + 0.07f < -a.z * tanHalf, $"{w.shortName}: shoulder {a} would be on screen");

                        void Check(string what, WeaponAction action, float p, float maxError, bool shellInsert = false)
                        {
                            if (rig != null) rig.Tick(action, p, true, action == WeaponAction.Reloading, false, 0f);
                            arms.Pose(action, p, false, true, 1f, shellInsert);
                            Assert.Less(arms.RightHandError, maxError, $"{w.shortName} {what}: right wrist {arms.RightHandError * 1000f:F0} mm from its target");
                            Assert.Less(arms.LeftHandError, maxError, $"{w.shortName} {what}: left wrist {arms.LeftHandError * 1000f:F0} mm from its target");
                        }
                        Check("hip", WeaponAction.Ready, 1f, 0.005f);
                        gun.localPosition = new Vector3(0f, 0f, w.adsEyeRelief) - sight.localPosition;
                        Check("ads", WeaponAction.Ready, 1f, 0.005f);
                        gun.localPosition = w.viewmodelOffset + Saltline.Player.ViewmodelController.ReloadShift;
                        gun.localRotation = Quaternion.Euler(Saltline.Player.ViewmodelController.ReloadTilt);
                        if (w.weaponClass == WeaponClass.Shotgun) Check("shell insert", WeaponAction.ShellReloading, 0.5f, 0.01f, true);
                        else Check("magazine insert", WeaponAction.Reloading, 0.62f, 0.01f);
                    }
                    finally
                    {
                        Object.DestroyImmediate(arms.gameObject);
                        Object.DestroyImmediate(gun.gameObject);
                    }
                }
            }
            finally { Object.DestroyImmediate(view); }
        }
    }
}
