using Saltline.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Saltline.EditorTools
{
    /// <summary>Generates the Menu scene: the backdrop behind the main menu and the lobby.</summary>
    public static class MenuSceneFactory
    {
        public const string MenuScenePath = BootSceneFactory.ScenesDir + "/Menu.unity";

        [MenuItem("Saltline/Build/Menu Scene")]
        public static void Build()
        {
            var floorMat = ContentFactory.Lit("Menu_Floor", new Color(0.075f, 0.085f, 0.1f), 0.55f, 0.2f);
            var standMat = ContentFactory.Lit("Menu_Stand", new Color(0.16f, 0.17f, 0.19f), 0.35f, 0.6f);
            var accentMat = ContentFactory.Lit("Env_Accent", new Color(0.95f, 0.55f, 0.12f), 0.3f, 0f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.26f, 0.33f);
            RenderSettings.ambientEquatorColor = new Color(0.1f, 0.11f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.035f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.045f, 0.05f, 0.06f);
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = 18f;

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = new Color(1f, 0.93f, 0.84f);
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(38f, -140f, 0f);
            RenderSettings.sun = key;

            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(1f, 0.55f, 0.15f);
            rim.intensity = 14f;
            rim.range = 9f;
            rim.spotAngle = 55f;
            rim.transform.SetPositionAndRotation(new Vector3(2.8f, 2.2f, 2.2f), Quaternion.LookRotation(new Vector3(-1.6f, -1.7f, -2.2f)));

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.45f, 0.6f, 0.95f);
            fill.intensity = 2.5f;
            fill.range = 7f;
            fill.transform.position = new Vector3(-1.5f, 1.4f, -2.2f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            var root = new GameObject("Backdrop");
            root.transform.position = new Vector3(1.25f, 0f, 0.4f);
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Plinth";
            stand.transform.SetParent(root.transform, false);
            stand.transform.localScale = new Vector3(2.6f, 0.06f, 2.6f);
            stand.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            stand.GetComponent<MeshRenderer>().sharedMaterial = standMat;
            Object.DestroyImmediate(stand.GetComponent<Collider>());
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "PlinthEdge";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localScale = new Vector3(2.66f, 0.012f, 2.66f);
            ring.transform.localPosition = new Vector3(0f, 0.115f, 0f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = accentMat;
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            var gunsRoot = new GameObject("Weapons");
            gunsRoot.transform.SetParent(root.transform, false);
            gunsRoot.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            gunsRoot.AddComponent<MenuBackdrop>();

            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.045f, 0.05f, 0.06f);
            cam.fieldOfView = 38f;
            cam.transform.position = new Vector3(0.1f, 1.25f, -3.6f);
            cam.transform.LookAt(new Vector3(1.0f, 0.45f, 0.4f));
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = ContentFactory.PostProfile();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            Debug.Log("[SALTLINE] Menu scene built.");
        }
    }
}
