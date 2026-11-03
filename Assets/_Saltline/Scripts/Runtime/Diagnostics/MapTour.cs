using System.Collections;
using System.Diagnostics;
using System.IO;
using Saltline.Combat;
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
    ///
    /// With -fx, it reviews the weapon effects at a fixed 60 Hz step: impacts on concrete, metal,
    /// wood and ground test panels (burst, dust, bullet holes, close-ups), every weapon's muzzle
    /// flash from the side and in first person, a burst with spent cases and barrel smoke, a body
    /// hit and a tracer, logging particle, decal and case counts.
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
            if (CommandLine.Has("fx"))
                yield return FxShowcase(cam, views.transform, adapt, dir, rt, tex);
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
                FirstPersonArms arms = null;
                if (lib.armsPrefab != null)
                {
                    arms = Instantiate(lib.armsPrefab, holder, false).GetComponent<FirstPersonArms>();
                    arms.SetLayer(0);
                    arms.SetTeamMaterial(lib.armsTeamBlue);
                    arms.Bind(gun, rig, w.weaponClass, w.viewmodelOffset);
                }
                Vector3 ads = new Vector3(0f, 0f, w.adsEyeRelief) - sight.localPosition;
                bool shells = w.weaponClass == WeaponClass.Shotgun;
                bool cycles = w.fireMode == FireMode.PumpAction || w.fireMode == FireMode.BoltAction;
                var reloadAction = shells ? WeaponAction.ShellReloading : WeaponAction.Reloading;
                Vector3 reloadPos = w.viewmodelOffset + ViewmodelController.ReloadShift, reloadRot = ViewmodelController.ReloadTilt;
                var poses = new System.Collections.Generic.List<(string name, Vector3 pos, Vector3 euler, WeaponAction action, float p, bool hands)>
                {
                    ("hip", w.viewmodelOffset, Vector3.zero, WeaponAction.Ready, 1f, true),
                    ("ads", ads, Vector3.zero, WeaponAction.Ready, 1f, true),
                    ("reload", reloadPos, reloadRot, reloadAction, shells ? 0.12f : 0.06f, true),
                    ("reload_insert", reloadPos, reloadRot, reloadAction, shells ? 0.5f : 0.62f, true),
                };
                if (cycles) poses.Add(("cycle", w.viewmodelOffset, new Vector3(4f, 0f, -8f), WeaponAction.Cycling, 0.4f, true));
                poses.Add(("side", new Vector3(0.02f, -0.02f, 0.62f), new Vector3(8f, 72f, 0f), WeaponAction.Ready, 1f, false));
                foreach (var pose in poses)
                {
                    gun.localPosition = pose.pos;
                    gun.localRotation = Quaternion.Euler(pose.euler);
                    if (rig != null) rig.Tick(pose.action, pose.p, true, pose.action == WeaponAction.Reloading, false, 0f);
                    if (arms != null)
                    {
                        arms.Pose(pose.action, pose.p, false, pose.hands, 1f, shells && pose.name == "reload_insert");
                        if (pose.hands) Debug.Log($"[SALTLINE] MapTour arms {w.shortName} {pose.name}: wrist error right {arms.RightHandError * 1000f:F0} mm, left {arms.LeftHandError * 1000f:F0} mm");
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

        // ------------------------------------------------------------------ effects

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static Material SceneMaterial(string name)
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name == name) return m;
            return null;
        }

        IEnumerator FxShowcase(Camera cam, Transform views, ExposureAdaptation adapt, string dir, RenderTexture rt, Texture2D tex)
        {
            var lib = PresentationLibrary.Instance;
            var db = lib != null ? lib.weapons : null;
            var view = views.Find("VIEW_" + CommandLine.Get("fxview", CommandLine.Get("weaponview", "04_mid_courtyard")));
            FxManager.Ensure();
            yield return null;
            var fx = FxManager.Instance;
            if (db == null || view == null || fx == null)
            {
                Debug.LogError("[SALTLINE] MapTour: no weapon data, FX view or FxManager.");
                yield break;
            }
            Time.captureDeltaTime = 1f / 60f;
            float fov = cam.fieldOfView, near = cam.nearClipPlane;
            int mask = cam.cullingMask;
            cam.orthographic = false;
            cam.cullingMask = mask | (1 << Layers.Viewmodel) | (1 << Layers.Debris);
            adapt.enabled = true;

            Vector3 flat = view.forward;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Vector3 eye = view.position;
            Vector3 ground = eye + flat * 4f;
            if (Physics.Raycast(ground + Vector3.up * 3f, Vector3.down, out var gh, 30f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) ground = gh.point;
            eye = ground - flat * 4f + Vector3.up * 1.6f;

            // Test panels: the map's own materials and surface types.
            var panels = new (string mat, SurfaceType surface, string label)[]
            {
                ("M_Concrete", SurfaceType.Concrete, "concrete"), ("M_Container", SurfaceType.Metal, "metal"),
                ("M_Wood", SurfaceType.Wood, "wood"), ("M_Salt", SurfaceType.Dirt, "ground"),
            };
            var made = new GameObject("FxPanels");
            var centers = new Vector3[panels.Length];
            for (int i = 0; i < panels.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "FxPanel_" + panels[i].label;
                go.layer = Layers.Environment;
                go.transform.SetParent(made.transform, false);
                centers[i] = ground + flat * 0.6f + right * ((i - 1.5f) * 1.45f) + Vector3.up * 0.75f;
                go.transform.SetPositionAndRotation(centers[i], Quaternion.LookRotation(flat));
                go.transform.localScale = new Vector3(1.25f, 1.5f, 0.2f);
                var m = SceneMaterial(panels[i].mat);
                if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
                go.AddComponent<SurfaceTag>().surface = panels[i].surface;
            }
            Physics.SyncTransforms();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.02f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(ground + Vector3.up * 0.8f - eye));
            adapt.Snap();
            yield return Frames(2);

            // 1. Impacts: four shots per panel, traced from the eye like real bullets.
            var shotsAt = new[] { new Vector2(-0.3f, 0.35f), new Vector2(0.25f, 0.2f), new Vector2(-0.1f, -0.3f), new Vector2(0.3f, -0.35f) };
            var sw = Stopwatch.StartNew();
            int impacts = 0;
            for (int i = 0; i < panels.Length; i++)
                foreach (var o in shotsAt)
                {
                    Vector3 target = centers[i] - flat * 0.1f + right * o.x + Vector3.up * o.y;
                    Vector3 d = (target - eye).normalized;
                    if (Physics.Raycast(eye, d, out var h, 20f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                    {
                        fx.Impact(h.point, h.normal, d, SurfaceTag.TypeOf(h.collider));
                        impacts++;
                    }
                }
            sw.Stop();
            Debug.Log($"[SALTLINE] MapTour fx: {impacts} impacts emitted in {sw.Elapsed.TotalMilliseconds:F2} ms; {fx.ParticleCount} particles, {fx.ActiveMarks} bullet holes");
            yield return Frames(2);
            Capture(cam, rt, tex, Path.Combine(dir, "fx_impacts_burst.png"));
            yield return Frames(24);
            Capture(cam, rt, tex, Path.Combine(dir, "fx_impacts_dust.png"));
            Debug.Log($"[SALTLINE] MapTour fx impacts at 0.43 s: {fx.ParticleCount} particles");
            yield return Frames(150);
            Capture(cam, rt, tex, Path.Combine(dir, "fx_impacts_holes.png"));
            Debug.Log($"[SALTLINE] MapTour fx impacts at 2.9 s: {fx.ParticleCount} particles, {fx.ActiveMarks} bullet holes");
            cam.fieldOfView = 38f;
            for (int i = 0; i < panels.Length; i++)
            {
                Vector3 face = centers[i] - flat * 0.1f;
                Vector3 p = face - flat * 0.75f + right * 0.22f + Vector3.up * 0.12f;
                cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(face - p));
                adapt.Snap();
                yield return Frames(2);
                Capture(cam, rt, tex, Path.Combine(dir, $"fx_hole_{panels[i].label}.png"));
            }

            // 2. Muzzle flash from the side, every weapon, and smoke after a burst.
            cam.fieldOfView = 45f;
            Vector3 side = ground + Vector3.up * 1.3f - flat * 1.6f;
            Vector3 camPos = side - flat * 1.7f + Vector3.up * 0.05f;
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(side + right * 0.35f - camPos));
            adapt.Snap();
            foreach (var w in db.weapons)
            {
                if (w == null) continue;
                var holder = new GameObject("FxGun").transform;
                holder.SetPositionAndRotation(side - right * 0.15f, Quaternion.LookRotation(right));
                var gun = WeaponModel.Build(w.weaponClass, holder, 0, WeaponView.Display, out var muzzle, out _, out var gunRig);
                yield return Frames(2);
                Vector3 aim = right;
                fx.MuzzleWorld(muzzle != null ? muzzle.position : side, aim, w, Vector3.zero);
                fx.SpawnLight(muzzle != null ? muzzle.position : side, aim, w);
                var eject = gun.Find("Eject");
                if (FxManager.EjectsOnShot(w.weaponClass)) fx.EjectShell(eject, w.weaponClass, Vector3.zero, false);
                if (gunRig != null) gunRig.OnShot(w.FireInterval);
                yield return Frames(1);
                Capture(cam, rt, tex, Path.Combine(dir, $"fx_flash_{w.shortName}.png"));
                if (w.weaponClass == WeaponClass.AssaultRifle)
                {
                    for (int k = 0; k < 7; k++)
                    {
                        yield return Frames(6);
                        fx.MuzzleWorld(muzzle.position, aim, w, Vector3.zero);
                        fx.SpawnLight(muzzle.position, aim, w);
                        fx.EjectShell(eject, w.weaponClass, Vector3.zero, false);
                        if (gunRig != null) gunRig.OnShot(w.FireInterval);
                    }
                    yield return Frames(3);
                    Capture(cam, rt, tex, Path.Combine(dir, "fx_burst_AR_cases.png"));
                    yield return Frames(30);
                    Capture(cam, rt, tex, Path.Combine(dir, "fx_burst_AR_smoke.png"));
                    Debug.Log($"[SALTLINE] MapTour fx burst: {fx.Shells.Active} cases live, {fx.ParticleCount} particles");
                }
                Destroy(holder.gameObject);
                yield return Frames(20);
            }

            // Spent cases on the ground after the burst, framed on where they came to rest.
            cam.fieldOfView = 35f;
            Vector3 drop = side;
            if (fx.Shells.RestingCentroid(out var rest, out int resting)) drop = rest;
            else if (Physics.Raycast(drop, Vector3.down, out var dh, 5f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) drop = dh.point;
            Debug.Log($"[SALTLINE] MapTour fx cases: {resting} at rest around {drop - ground} from the panels' ground point");
            camPos = drop + right * 0.9f + Vector3.up * 0.55f;
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(drop - camPos));
            adapt.Snap();
            yield return Frames(2);
            Capture(cam, rt, tex, Path.Combine(dir, "fx_cases_ground.png"));

            // 3. First person: the gun on the viewmodel layer at its hip and aiming positions.
            cam.fieldOfView = 56f;
            cam.nearClipPlane = 0.01f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flat + Vector3.down * 0.05f));
            adapt.Snap();
            foreach (var w in db.weapons)
            {
                if (w == null) continue;
                var holder = new GameObject("FxViewmodel").transform;
                holder.SetParent(cam.transform, false);
                var gun = WeaponModel.Build(w.weaponClass, holder, Layers.Viewmodel, WeaponView.FirstPerson, out var muzzle, out var sight, out var gunRig);
                gun.localPosition = w.viewmodelOffset;
                var eject = gun.Find("Eject");
                fx.SetViewmodelGun(muzzle, eject);
                FirstPersonArms arms = null;
                if (lib.armsPrefab != null)
                {
                    arms = Instantiate(lib.armsPrefab, holder, false).GetComponent<FirstPersonArms>();
                    arms.SetLayer(Layers.Viewmodel);
                    arms.SetTeamMaterial(lib.armsTeamRed);
                    arms.Bind(gun, gunRig, w.weaponClass, w.viewmodelOffset);
                    arms.Pose(WeaponAction.Ready, 1f, true, true, 1f);
                }
                yield return Frames(2);
                int shots = w.weaponClass == WeaponClass.AssaultRifle ? 10 : 1;
                for (int k = 0; k < shots; k++)
                {
                    if (k > 0) yield return Frames(6);
                    fx.MuzzleFirstPerson(w, cam.transform.forward, Vector3.zero, 0f);
                    fx.SpawnLight(muzzle.position, cam.transform.forward, w);
                    fx.SpawnTracer(muzzle.position, eye + cam.transform.forward * 60f, w);
                    if (FxManager.EjectsOnShot(w.weaponClass)) fx.EjectShell(eject, w.weaponClass, Vector3.zero, true);
                    if (gunRig != null) gunRig.OnShot(w.FireInterval);
                    if (k == 0 || k == 4)
                    {
                        yield return Frames(1);
                        Capture(cam, rt, tex, Path.Combine(dir, k == 0 ? $"fx_fp_{w.shortName}.png" : "fx_fp_AR_burst.png"));
                    }
                }
                if (w.weaponClass == WeaponClass.AssaultRifle)
                {
                    yield return Frames(50);
                    Capture(cam, rt, tex, Path.Combine(dir, "fx_fp_AR_wisp.png"));
                    gun.localPosition = new Vector3(0f, 0f, w.adsEyeRelief) - sight.localPosition;
                    if (arms != null) arms.Pose(WeaponAction.Ready, 1f, true, true, 1f);
                    yield return Frames(2);
                    fx.MuzzleFirstPerson(w, cam.transform.forward, Vector3.zero, 1f);
                    fx.SpawnLight(muzzle.position, cam.transform.forward, w);
                    yield return Frames(1);
                    Capture(cam, rt, tex, Path.Combine(dir, "fx_fp_AR_ads.png"));
                }
                fx.SetViewmodelGun(null, null);
                Destroy(holder.gameObject);
                yield return Frames(30);
            }

            // 4. A body hit and a passing tracer.
            cam.fieldOfView = 50f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flat));
            adapt.Snap();
            Vector3 hitAt = eye + flat * 2.6f + right * 0.3f - Vector3.up * 0.2f;
            fx.BodyHit(hitAt, (flat + right * 0.3f).normalized);
            yield return Frames(3);
            Capture(cam, rt, tex, Path.Combine(dir, "fx_body_hit.png"));
            yield return Frames(12);
            Capture(cam, rt, tex, Path.Combine(dir, "fx_body_hit_late.png"));
            var ar = db.weapons.Length > 0 ? db.weapons[0] : null;
            if (ar != null)
            {
                fx.SpawnTracer(eye + right * 0.6f - Vector3.up * 0.3f, eye + right * 6f + flat * 120f + Vector3.up * 3f, ar);
                yield return Frames(3);
                Capture(cam, rt, tex, Path.Combine(dir, "fx_tracer.png"));
            }
            Debug.Log($"[SALTLINE] MapTour fx done: {fx.ParticleCount} particles, {fx.ActiveMarks} bullet holes, {fx.Shells.Active} cases live");

            Destroy(made);
            Time.captureDeltaTime = 0f;
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.cullingMask = mask;
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
