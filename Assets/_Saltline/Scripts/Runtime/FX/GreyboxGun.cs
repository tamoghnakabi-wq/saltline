using Saltline.Weapons;
using UnityEngine;

namespace Saltline.FX
{
    /// <summary>
    /// Placeholder weapon geometry built from primitives, with distinct silhouettes per class.
    /// Replaced by the Blender-authored models when they are imported.
    /// </summary>
    public static class GreyboxGun
    {
        public static Transform Build(WeaponClass cls, Transform parent, int layer, out Transform muzzle)
        {
            var lib = PresentationLibrary.Instance;
            var root = new GameObject($"Gun_{cls}").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = layer;

            Material metal = lib != null ? lib.gunMetal : null;
            Material accent = lib != null ? lib.gunAccent : null;
            float barrelEnd;
            switch (cls)
            {
                case WeaponClass.SMG:
                    Box(root, layer, metal, new Vector3(0f, 0f, 0.02f), new Vector3(0.055f, 0.08f, 0.30f));
                    Box(root, layer, metal, new Vector3(0f, 0.005f, 0.22f), new Vector3(0.028f, 0.028f, 0.14f));
                    Box(root, layer, accent, new Vector3(0f, -0.10f, 0.06f), new Vector3(0.035f, 0.16f, 0.045f));
                    Box(root, layer, accent, new Vector3(0f, -0.07f, -0.06f), new Vector3(0.035f, 0.09f, 0.04f));
                    Box(root, layer, metal, new Vector3(0f, 0f, -0.2f), new Vector3(0.03f, 0.05f, 0.14f));
                    barrelEnd = 0.29f;
                    break;
                case WeaponClass.Shotgun:
                    Box(root, layer, metal, new Vector3(0f, 0f, 0.0f), new Vector3(0.06f, 0.085f, 0.36f));
                    Box(root, layer, metal, new Vector3(0f, 0.02f, 0.36f), new Vector3(0.034f, 0.034f, 0.42f));
                    Box(root, layer, accent, new Vector3(0f, -0.025f, 0.3f), new Vector3(0.05f, 0.045f, 0.18f));
                    Box(root, layer, accent, new Vector3(0f, -0.03f, -0.28f), new Vector3(0.045f, 0.1f, 0.24f));
                    barrelEnd = 0.57f;
                    break;
                case WeaponClass.Sniper:
                    Box(root, layer, metal, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.085f, 0.46f));
                    Box(root, layer, metal, new Vector3(0f, 0.01f, 0.48f), new Vector3(0.026f, 0.026f, 0.52f));
                    Box(root, layer, accent, new Vector3(0f, 0.085f, 0.02f), new Vector3(0.05f, 0.05f, 0.30f));
                    Box(root, layer, accent, new Vector3(0f, -0.09f, 0.04f), new Vector3(0.04f, 0.1f, 0.06f));
                    Box(root, layer, accent, new Vector3(0f, -0.03f, -0.34f), new Vector3(0.05f, 0.11f, 0.26f));
                    barrelEnd = 0.74f;
                    break;
                case WeaponClass.Pistol:
                    Box(root, layer, metal, new Vector3(0f, 0.02f, 0.03f), new Vector3(0.035f, 0.045f, 0.19f));
                    Box(root, layer, accent, new Vector3(0f, -0.05f, -0.03f), new Vector3(0.032f, 0.11f, 0.05f));
                    barrelEnd = 0.13f;
                    break;
                default: // assault rifle
                    Box(root, layer, metal, new Vector3(0f, 0f, 0.02f), new Vector3(0.06f, 0.09f, 0.40f));
                    Box(root, layer, metal, new Vector3(0f, 0.01f, 0.34f), new Vector3(0.028f, 0.028f, 0.28f));
                    Box(root, layer, accent, new Vector3(0f, -0.115f, 0.08f), new Vector3(0.04f, 0.15f, 0.07f));
                    Box(root, layer, accent, new Vector3(0f, -0.03f, -0.27f), new Vector3(0.05f, 0.09f, 0.2f));
                    Box(root, layer, accent, new Vector3(0f, 0.065f, 0.02f), new Vector3(0.03f, 0.035f, 0.09f));
                    barrelEnd = 0.48f;
                    break;
            }
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.01f, barrelEnd);
            return root;
        }

        static void Box(Transform parent, int layer, Material m, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (m != null) r.sharedMaterial = m;
            r.shadowCastingMode = layer == Layers.Viewmodel ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
