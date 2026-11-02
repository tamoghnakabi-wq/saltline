using System.Collections;
using System.Diagnostics;
using System.IO;
using Saltline.Core;
using Saltline.FX;
using Saltline.Player;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Offline visual review of a map: loads the scene without networking, renders every
    /// "CaptureViews/VIEW_*" viewpoint with the scene's spectator camera (post-processing on)
    /// and quits. Needs graphics, so run in batch mode without -nographics:
    ///
    ///   SALTLINE -batchmode -maptour Kappa -capture &lt;dir&gt; [-capturesize 1920x1080]
    ///
    /// Each view is also rendered a few extra times to report the average time Camera.Render
    /// took on this machine (a rough CPU-side cost, not a frame-rate measurement).
    ///
    /// With -weapons, it then shows every imported weapon model from one view (default
    /// 04_mid_courtyard, or -weaponview NAME): hip, aim-down-sights, mid-reload and a side view,
    /// and logs where the sight lands on screen when aiming (it should be the centre).
    ///
    /// With -characters, it shows the animated operator in front of that view (-characterview NAME)
    /// in a set of poses (idle, walk, run, sprint, strafe, crouch, aim up/down, reload, each weapon,
    /// jump, slide, death) and logs how far the hands ended from their IK targets.
    /// </summary>
    public sealed class MapTour : MonoBehaviour
    {
        IEnumerator Start()
        {
            string scene = CommandLine.Get("maptour", "Kappa");
            string dir = CommandLine.Get("capture", "MapTour");
            Directory.CreateDirectory(dir);
            int w = 1920, h = 1080;
            var parts = CommandLine.Get("capturesize", "1920x1080").Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int pw) && int.TryParse(parts[1], out int ph)) { w = pw; h = ph; }

            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"[SALTLINE] MapTour: scene '{scene}' is not in the build.");
                Application.Quit(2);
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(scene);
            for (int i = 0; i < 3; i++) yield return null;

            Camera cam = null;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.name == "SpectatorCamera") cam = c;
            var views = GameObject.Find("CaptureViews");
            if (cam == null || views == null)
            {
                Debug.LogError("[SALTLINE] MapTour: scene has no SpectatorCamera or CaptureViews.");
                Application.Quit(3);
                yield break;
            }

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            var adapt = cam.GetComponent<Saltline.FX.ExposureAdaptation>();
            if (adapt == null) adapt = cam.gameObject.AddComponent<Saltline.FX.ExposureAdaptation>();
            int saved = 0;
            bool sceneFog = RenderSettings.fog;
            var sw = new Stopwatch();
            foreach (Transform v in views.transform)
            {
                bool ortho = v.localScale.x > 1.01f;
                cam.orthographic = ortho;
                if (ortho) cam.orthographicSize = v.localScale.x;
                // Distance fog from 150 m up would wash out a plan view.
                RenderSettings.fog = !ortho && sceneFog;
                cam.transform.SetPositionAndRotation(v.position, v.rotation);
                // Each view is a cut, not a walk: settle eye adaptation at once (plan views get none).
                adapt.enabled = !ortho;
                if (!ortho) adapt.Snap();
                // Let anything frame-dependent (auto exposure, TAA history) settle.
                for (int i = 0; i < 2; i++) { cam.Render(); yield return null; }
                sw.Restart();
                const int timed = 5;
                for (int i = 0; i < timed; i++) cam.Render();
                sw.Stop();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                string name = v.name.Replace("VIEW_", "");
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                saved++;
                Debug.Log($"[SALTLINE] MapTour {name}: {sw.Elapsed.TotalMilliseconds / timed:F1} ms per Camera.Render at {w}x{h}, eye adaptation {(adapt.enabled ? adapt.CurrentStops : 0f):+0.00;-0.00} stops");
                yield return null;
            }
            RenderSettings.fog = sceneFog;
            if (CommandLine.Has("weapons"))
                yield return WeaponShowcase(cam, views.transform, adapt, dir, rt, tex);
            if (CommandLine.Has("characters"))
                yield return CharacterShowcase(cam, views.transform, adapt, dir, rt, tex);
            cam.targetTexture = null;
            rt.Release();
            Debug.Log($"[SALTLINE] MapTour done: {saved} views of {scene} ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}')");
            Application.Quit(0);
        }

        IEnumerator WeaponShowcase(Camera cam, Transform views, ExposureAdaptation adapt, string dir, RenderTexture rt, Texture2D tex)
        {
            var lib = PresentationLibrary.Instance;
            var db = lib != null ? lib.weapons : null;
            var view = views.Find("VIEW_" + CommandLine.Get("weaponview", "04_mid_courtyard"));
            if (db == null || view == null)
            {
                Debug.LogError("[SALTLINE] MapTour: no weapon data or showcase view.");
                yield break;
            }
            cam.orthographic = false;
            cam.transform.SetPositionAndRotation(view.position, view.rotation);
            adapt.enabled = true;
            adapt.Snap();
            float fov = cam.fieldOfView, near = cam.nearClipPlane;
            cam.fieldOfView = 56f;   // the viewmodel camera's field of view
            cam.nearClipPlane = 0.01f;
            foreach (var w in db.weapons)
            {
                if (w == null || !WeaponModel.HasModel(w.weaponClass)) continue;
                var holder = new GameObject("Showcase").transform;
                holder.SetParent(cam.transform, false);
                var gun = WeaponModel.Build(w.weaponClass, holder, 0, WeaponView.FirstPerson, out var muzzle, out var sight, out var rig);
                Vector3 ads = new Vector3(0f, 0f, w.adsEyeRelief) - sight.localPosition;
                var poses = new (string name, Vector3 pos, Vector3 euler, float reload)[]
                {
                    ("hip", w.viewmodelOffset, Vector3.zero, -1f),
                    ("ads", ads, Vector3.zero, -1f),
                    ("reload", w.viewmodelOffset + Vector3.down * 0.05f, new Vector3(11f, 0f, 20f), 0.24f),
                    ("side", new Vector3(0.02f, -0.02f, 0.62f), new Vector3(8f, 72f, 0f), -1f),
                };
                foreach (var pose in poses)
                {
                    gun.localPosition = pose.pos;
                    gun.localRotation = Quaternion.Euler(pose.euler);
                    if (rig != null)
                    {
                        if (pose.reload >= 0f) rig.Tick(WeaponAction.Reloading, pose.reload, true, true, false, 0f);
                        else rig.Tick(WeaponAction.Ready, 1f, false, false, false, 0f);
                    }
                    for (int i = 0; i < 2; i++) { cam.Render(); yield return null; }
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    string file = $"weapon_{w.shortName}_{pose.name}.png";
                    File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
                    if (pose.name == "ads")
                    {
                        Vector3 vp = cam.WorldToViewportPoint(sight.position);
                        Vector3 mz = cam.WorldToViewportPoint(muzzle.position);
                        Debug.Log($"[SALTLINE] MapTour weapon {w.shortName}: sight at viewport ({vp.x:F3}, {vp.y:F3}), {vp.z:F3} m from the eye; muzzle at ({mz.x:F3}, {mz.y:F3})");
                    }
                }
                Destroy(holder.gameObject);
                yield return null;
            }
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
        }

        static void Capture(Camera cam, RenderTexture rt, Texture2D tex, string path)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }

        struct CharacterShot
        {
            public string name;
            public WeaponClass weapon;
            public float orbit, distance, lookHeight;   // camera around the character: 0 = in front
            public Vector3 velocity;                    // character space (x right, z forward), m/s
            public Stance stance;
            public MotorFlags flags;
            public float pitch, ads, seconds, progress;
            public WeaponAction action;
            public bool dead, red;
        }

        IEnumerator CharacterShowcase(Camera cam, Transform views, ExposureAdaptation adapt, string dir, RenderTexture rt, Texture2D tex)
        {
            var lib = PresentationLibrary.Instance;
            var view = views.Find("VIEW_" + CommandLine.Get("characterview", CommandLine.Get("weaponview", "04_mid_courtyard")));
            if (lib == null || lib.operatorPrefab == null || view == null)
            {
                Debug.LogError("[SALTLINE] MapTour: no operator prefab or character view.");
                yield break;
            }
            Vector3 flat = view.forward;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 spot = view.position + flat * 4f;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) spot = hit.point;
            float yaw = Quaternion.LookRotation(-flat).eulerAngles.y + 20f;

            var body = Instantiate(lib.operatorPrefab);
            var op = body.GetComponent<OperatorRig>();
            op.SetTeamMaterial(lib.operatorTeamBlue);
            cam.orthographic = false;
            float fov = cam.fieldOfView, near = cam.nearClipPlane;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            adapt.enabled = true;

            CharacterShot S(string n, float orbit) => new CharacterShot
            {
                name = n, weapon = WeaponClass.AssaultRifle, orbit = orbit, distance = 3.4f, lookHeight = 1.05f,
                flags = MotorFlags.Grounded, seconds = 1.2f,
            };
            var shots = new[]
            {
                S("idle_front", 25f),
                S("idle_side", 90f),
                S("idle_back", 165f),
                new CharacterShot { name = "closeup", weapon = WeaponClass.AssaultRifle, orbit = 30f, distance = 1.35f, lookHeight = 1.45f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "walk_ads", weapon = WeaponClass.AssaultRifle, orbit = 50f, distance = 3.4f, lookHeight = 1.05f, velocity = new Vector3(0f, 0f, 3.35f), flags = MotorFlags.Grounded | MotorFlags.Aiming, ads = 1f, seconds = 1.3f },
                new CharacterShot { name = "run", weapon = WeaponClass.AssaultRifle, orbit = 75f, distance = 3.6f, lookHeight = 1.0f, velocity = new Vector3(0f, 0f, 5.4f), flags = MotorFlags.Grounded, seconds = 1.3f },
                new CharacterShot { name = "sprint", weapon = WeaponClass.AssaultRifle, orbit = 70f, distance = 3.6f, lookHeight = 1.0f, velocity = new Vector3(0f, 0f, 7.6f), flags = MotorFlags.Grounded | MotorFlags.Sprinting, seconds = 1.3f },
                new CharacterShot { name = "strafe_left", weapon = WeaponClass.AssaultRifle, orbit = 15f, distance = 3.6f, lookHeight = 1.0f, velocity = new Vector3(-5.0f, 0f, 0f), flags = MotorFlags.Grounded, seconds = 1.3f },
                new CharacterShot { name = "backpedal", weapon = WeaponClass.AssaultRifle, orbit = 40f, distance = 3.6f, lookHeight = 1.0f, velocity = new Vector3(0f, 0f, -4.4f), flags = MotorFlags.Grounded, seconds = 1.3f },
                new CharacterShot { name = "crouch_walk", weapon = WeaponClass.AssaultRifle, orbit = 55f, distance = 3.2f, lookHeight = 0.75f, velocity = new Vector3(0f, 0f, 2.7f), stance = Stance.Crouching, flags = MotorFlags.Grounded | MotorFlags.WantsCrouch, seconds = 1.3f },
                new CharacterShot { name = "aim_up", weapon = WeaponClass.AssaultRifle, orbit = 70f, distance = 3.4f, lookHeight = 1.15f, pitch = 35f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "aim_down", weapon = WeaponClass.AssaultRifle, orbit = 70f, distance = 3.4f, lookHeight = 1.0f, pitch = -30f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "reload_out", weapon = WeaponClass.AssaultRifle, orbit = 40f, distance = 2.8f, lookHeight = 1.1f, action = WeaponAction.Reloading, progress = 0.36f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "reload_in", weapon = WeaponClass.AssaultRifle, orbit = 40f, distance = 2.8f, lookHeight = 1.1f, action = WeaponAction.Reloading, progress = 0.66f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "smg", weapon = WeaponClass.SMG, orbit = 35f, distance = 3.2f, lookHeight = 1.1f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "shotgun", weapon = WeaponClass.Shotgun, orbit = 35f, distance = 3.2f, lookHeight = 1.1f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "sniper", weapon = WeaponClass.Sniper, orbit = 60f, distance = 3.4f, lookHeight = 1.1f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "pistol", weapon = WeaponClass.Pistol, orbit = 35f, distance = 3.0f, lookHeight = 1.15f, flags = MotorFlags.Grounded, seconds = 1f },
                new CharacterShot { name = "red_team", weapon = WeaponClass.AssaultRifle, orbit = -30f, distance = 3.4f, lookHeight = 1.05f, flags = MotorFlags.Grounded, seconds = 0.8f, red = true },
                new CharacterShot { name = "jump", weapon = WeaponClass.AssaultRifle, orbit = 60f, distance = 3.6f, lookHeight = 1.0f, velocity = new Vector3(0f, 0f, 5.4f), flags = MotorFlags.None, seconds = 0.8f },
                new CharacterShot { name = "slide", weapon = WeaponClass.AssaultRifle, orbit = 80f, distance = 3.6f, lookHeight = 0.6f, velocity = new Vector3(0f, 0f, 7f), stance = Stance.Sliding, flags = MotorFlags.Grounded, seconds = 0.8f },
                new CharacterShot { name = "death", weapon = WeaponClass.AssaultRifle, orbit = 75f, distance = 3.8f, lookHeight = 0.5f, flags = MotorFlags.Grounded, seconds = 3f, dead = true },
            };

            Transform gun = null;
            WeaponClass gunFor = (WeaponClass)255;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (var s in shots)
            {
                if (s.weapon != gunFor && WeaponModel.HasModel(s.weapon))
                {
                    if (gun != null) Destroy(gun.gameObject);
                    gun = WeaponModel.Build(s.weapon, body.transform, 0, WeaponView.ThirdPerson, out _, out _, out var wr);
                    op.SetWeapon(gun, wr, s.weapon);
                    gunFor = s.weapon;
                }
                op.SetTeamMaterial(s.red ? lib.operatorTeamRed : lib.operatorTeamBlue);
                var pose = new RenderPose
                {
                    Feet = spot, Velocity = rot * s.velocity, Yaw = yaw, Pitch = s.pitch,
                    Height = s.stance == Stance.Standing ? 1.8f : s.stance == Stance.Crouching ? 1.15f : 0.95f,
                    Stance = s.stance, Flags = s.flags, Action = s.action, ActionProgress = s.action == WeaponAction.Ready ? 1f : s.progress,
                    Ads = s.ads, Alive = true,
                };
                op.Pose = pose;
                op.Driven = true;
                if (s.dead)
                {
                    float settle = 0f;
                    while (settle < 0.4f) { settle += Time.deltaTime; yield return null; }
                    pose.Alive = false;
                    op.Pose = pose;
                }
                float t = 0f;
                while (t < s.seconds) { t += Time.deltaTime; yield return null; }

                Vector3 look = spot + Vector3.up * s.lookHeight;
                Vector3 camPos = look + Quaternion.Euler(0f, yaw + s.orbit, 0f) * Vector3.forward * s.distance + Vector3.up * 0.25f;
                cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(look - camPos));
                adapt.Snap();
                for (int i = 0; i < 2; i++) { cam.Render(); yield return null; }
                Capture(cam, rt, tex, Path.Combine(dir, $"char_{s.name}.png"));
                var anim = op.animator;
                var headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                float headY = headBone != null ? headBone.position.y - spot.y : 0f;
                Debug.Log($"[SALTLINE] MapTour character {s.name}: visual eye {op.HeadHeight:F2} m above the feet (head bone {headY:F2}; hitbox eye {(s.stance == Stance.Standing ? 1.62f : s.stance == Stance.Crouching ? 1.0f : 0.82f):F2}); hand IK error R {op.RightHandError * 100f:F1} cm, L {op.LeftHandError * 100f:F1} cm; playback x{anim.speed:F2}, clip speed {op.ClipSpeed:F2} m/s vs {s.velocity.magnitude:F2}; state {(anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") ? "Locomotion" : anim.GetCurrentAnimatorStateInfo(0).IsName("Crouch") ? "Crouch" : anim.GetCurrentAnimatorStateInfo(0).IsName("Air") ? "Air" : "other")}");
            }
            Destroy(body);
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
        }
    }
}
