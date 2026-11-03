using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Saltline.FX;
using Saltline.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Review tool for the first-person arms, quicker than a player build: renders every weapon with the arms in
    /// each pose from the eye, from two outside angles (left side, below-front) and magnified from the eye on each hand,
    /// into one sheet per weapon.
    ///
    ///   Unity -batchmode -projectPath Saltline -executeMethod Saltline.EditorTools.ArmsPreview.Render -quit -previewOut DIR
    /// </summary>
    public static class ArmsPreview
    {
        const int W = 640, H = 360;

        public static void Render()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-previewOut");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("../MapTour/arms_preview");
            // -hipShift x,y,z tries a different hip position for every weapon (camera space, metres) without editing the data.
            Vector3 hipShift = Vector3.zero;
            // -reloadShift x,y,z / -reloadTilt x,y,z try another reload pose (camera space, metres / degrees).
            Vector3 reloadShift = ArgVector(args, "-reloadShift", Saltline.Player.ViewmodelController.ReloadShift);
            Vector3 reloadTilt = ArgVector(args, "-reloadTilt", Saltline.Player.ViewmodelController.ReloadTilt);
            int hs = Array.IndexOf(args, "-hipShift");
            if (hs >= 0 && hs + 1 < args.Length)
            {
                var c = args[hs + 1].Split(',');
                hipShift = new Vector3(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(dir);

            var lib = PresentationLibrary.Instance;
            if (lib == null || lib.armsPrefab == null || lib.weapons == null) throw new InvalidOperationException("Run SaltlineBuild.Setup first (no arms prefab or weapons).");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.18f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2.2f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            sun.shadows = LightShadows.None;

            var eye = MakeCamera("Eye", 56f);
            var outside = MakeCamera("Outside", 40f);
            var zoom = MakeCamera("Zoom", 16f);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            eye.targetTexture = rt;
            outside.targetTexture = rt;
            zoom.targetTexture = rt;

            foreach (var w in lib.weapons.weapons)
            {
                if (w == null || !WeaponModel.HasModel(w.weaponClass)) continue;
                var gun = WeaponModel.Build(w.weaponClass, eye.transform, 0, WeaponView.FirstPerson, out _, out var sight, out var rig);
                var arms = Object.Instantiate(lib.armsPrefab, eye.transform, false).GetComponent<FirstPersonArms>();
                arms.SetTeamMaterial(lib.armsTeamBlue);
                Vector3 hip = w.viewmodelOffset + hipShift;
                arms.Bind(gun, rig, w.weaponClass, hip);
                // Outside play mode nothing re-skins between manual renders; do it per render.
                foreach (var smr in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                var ej = gun.Find("Eject");
                Debug.Log($"[SALTLINE] ArmsPreview {w.shortName} markers (gun space): Eject {(ej != null ? ej.localPosition.ToString("F3") : "-")}, Grip_R {gun.Find("Grip_R")?.localPosition.ToString("F3")}, Grip_L {gun.Find("Grip_L")?.localPosition.ToString("F3")}");

                LogSection(w.shortName, gun, "Grip_L");
                LogSection(w.shortName, gun, "Grip_R");
                bool shells = w.weaponClass == WeaponClass.Shotgun;
                var reload = shells ? WeaponAction.ShellReloading : WeaponAction.Reloading;
                Vector3 adsPos = new Vector3(0f, 0f, w.adsEyeRelief) - sight.localPosition;
                Vector3 rp = hip + reloadShift, rr = reloadTilt;
                var poses = new List<(string name, Vector3 pos, Vector3 rot, WeaponAction action, float p, bool trigger)>
                {
                    ("hip", hip, Vector3.zero, WeaponAction.Ready, 1f, false),
                    ("fire", hip, Vector3.zero, WeaponAction.Ready, 1f, true),
                    ("ads", adsPos, Vector3.zero, WeaponAction.Ready, 1f, false),
                    ("sprint", hip + new Vector3(-0.06f, -0.05f, -0.04f), new Vector3(8f, -28f, 12f), WeaponAction.Ready, 1f, false),
                    ("reload06", hip + reloadShift * 0.5f, rr * 0.5f, reload, shells ? 0.12f : 0.06f, false),
                    ("reload62", rp, rr, reload, shells ? 0.5f : 0.62f, false),
                };
                if (w.fireMode == FireMode.PumpAction || w.fireMode == FireMode.BoltAction)
                    poses.Add(("cycle", hip, new Vector3(4f, 0f, -8f), WeaponAction.Cycling, 0.35f, false));

                var frames = new List<Texture2D>();
                foreach (var pose in poses)
                {
                    gun.localPosition = pose.pos;
                    gun.localRotation = Quaternion.Euler(pose.rot);
                    if (rig != null) rig.Tick(pose.action, pose.p, true, pose.action == WeaponAction.Reloading, pose.trigger, 0f);
                    arms.Pose(pose.action, pose.p, pose.trigger, true, 1f, shells && pose.name == "reload62");
                    Vector3 hands = (FindDeep(arms.transform, "RightHand").position + FindDeep(arms.transform, "LeftHand").position) * 0.5f;

                    frames.Add(Shot(eye, rt, tex));
                    outside.transform.position = eye.transform.TransformPoint(new Vector3(-0.75f, 0.05f, 0.25f));
                    outside.transform.rotation = Quaternion.LookRotation(hands - outside.transform.position, eye.transform.up);
                    frames.Add(Shot(outside, rt, tex));
                    outside.transform.position = eye.transform.TransformPoint(new Vector3(0.35f, -0.55f, 0.9f));
                    outside.transform.rotation = Quaternion.LookRotation(hands - outside.transform.position, eye.transform.up);
                    frames.Add(Shot(outside, rt, tex));
                    // What the player sees of each hand, magnified: from the eye, aimed at the hand.
                    foreach (var bone in new[] { "LeftHandMiddle1", "RightHandMiddle1" })
                    {
                        zoom.transform.SetPositionAndRotation(eye.transform.position, Quaternion.LookRotation(FindDeep(arms.transform, bone).position - eye.transform.position, eye.transform.up));
                        frames.Add(Shot(zoom, rt, tex));
                    }
                    string Tip(string bone) { var t = FindDeep(arms.transform, bone); return t != null ? gun.InverseTransformPoint(t.position).ToString("F3") : "-"; }
                    Debug.Log($"[SALTLINE] ArmsPreview {w.shortName} {pose.name} left fingers: index {Tip("LeftHandIndex1")} -> {Tip("LeftHandIndex4")}, middle {Tip("LeftHandMiddle1")} -> {Tip("LeftHandMiddle4")}, ring -> {Tip("LeftHandRing4")}, little {Tip("LeftHandPinky1")} -> {Tip("LeftHandPinky4")}, thumb {Tip("LeftHandThumb1")} -> {Tip("LeftHandThumb2")} -> {Tip("LeftHandThumb3")} -> {Tip("LeftHandThumb4")}, eye {gun.InverseTransformPoint(eye.transform.position).ToString("F3")}");
                    Debug.Log($"[SALTLINE] ArmsPreview {w.shortName} {pose.name}: wrist error right {arms.RightHandError * 1000f:F1} mm, left {arms.LeftHandError * 1000f:F1} mm, trigger finger {arms.TriggerFingerError * 1000f:F1} mm; "
                        + $"gun space: L wrist {Tip("LeftHand")} L middle knuckle {Tip("LeftHandMiddle1")} tip {Tip("LeftHandMiddle4")} L thumb root {Tip("LeftHandThumb1")} tip {Tip("LeftHandThumb4")}; "
                        + $"R wrist {Tip("RightHand")} R middle knuckle {Tip("RightHandMiddle1")} tip {Tip("RightHandMiddle4")} R thumb root {Tip("RightHandThumb1")} tip {Tip("RightHandThumb4")} R index tip {Tip("RightHandIndex4")}");
                }
                Sheet(frames, 5, Path.Combine(dir, $"arms_{w.shortName}.png"), poses.Select(p => p.name).ToArray());
                Object.DestroyImmediate(arms.gameObject);
                Object.DestroyImmediate(gun.gameObject);
            }
            eye.targetTexture = null;
            outside.targetTexture = null;
            zoom.targetTexture = null;
            rt.Release();
            Debug.Log($"[SALTLINE] ArmsPreview: sheets written to {dir}");
        }

        /// <summary>Logs the gun's outline (gun space) around a marker, found with raycasts on temporary mesh colliders:
        /// the cross-section across the bore (for a handguard or pump) and the one along it (for a grip or magazine).</summary>
        static void LogSection(string weapon, Transform gun, string marker)
        {
            var m = gun.Find(marker);
            if (m == null) return;
            var added = new List<MeshCollider>();
            foreach (var mf in gun.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null) added.Add(mf.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();
            Vector3 c = m.localPosition + new Vector3(0f, 0.02f, 0f);
            string Probe(Vector3 u, Vector3 v)
            {
                var pts = new List<string>();
                for (int a = 0; a < 360; a += 30)
                {
                    float r = a * Mathf.Deg2Rad;
                    Vector3 dir = u * Mathf.Sin(r) - v * Mathf.Cos(r);   // 0 = below (-v), 90 = +u
                    Vector3 from = gun.TransformPoint(c + dir * 0.2f);
                    Vector3 to = gun.TransformDirection(-dir);
                    pts.Add(Physics.Raycast(from, to, out var hit, 0.2f) ? $"{a}:{gun.InverseTransformPoint(hit.point).ToString("F3")}" : $"{a}:-");
                }
                return string.Join(" ", pts);
            }
            Debug.Log($"[SALTLINE] ArmsPreview {weapon} {marker} {m.localPosition.ToString("F3")} across (x,y): {Probe(Vector3.right, Vector3.up)}");
            Debug.Log($"[SALTLINE] ArmsPreview {weapon} {marker} along (z,y): {Probe(Vector3.forward, Vector3.up)}");
            foreach (var mc in added) Object.DestroyImmediate(mc);
        }

        static Vector3 ArgVector(string[] args, string name, Vector3 fallback)
        {
            int i = Array.IndexOf(args, name);
            if (i < 0 || i + 1 >= args.Length) return fallback;
            var c = args[i + 1].Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv));
        }

        static Camera MakeCamera(string name, float fov)
        {
            var cam = new GameObject(name).AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.4f, 0.45f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            cam.enabled = false;
            return cam;
        }

        static Texture2D Shot(Camera cam, RenderTexture rt, Texture2D tex)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var copy = new Texture2D(W, H, TextureFormat.RGB24, false);
            copy.SetPixels32(tex.GetPixels32());
            copy.Apply(false);
            return copy;
        }

        static void Sheet(List<Texture2D> frames, int cols, string path, string[] rowNames)
        {
            int rows = (frames.Count + cols - 1) / cols;
            var sheet = new Texture2D(W * cols, H * rows, TextureFormat.RGB24, false);
            for (int k = 0; k < frames.Count; k++)
            {
                int x = (k % cols) * W, y = (rows - 1 - k / cols) * H;
                sheet.SetPixels32(x, y, W, H, frames[k].GetPixels32());
                Object.DestroyImmediate(frames[k]);
            }
            sheet.Apply(false);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log($"[SALTLINE] ArmsPreview: {path} rows {string.Join(", ", rowNames)}");
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
