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
        public static Transform Build(WeaponClass cls, Transform parent, int layer, out Transform muzzle) =>
            Build(cls, parent, layer, out muzzle, out _);

        /// <param name="sight">Point on the sight line the eye looks along when aiming down sights.</param>
        public static Transform Build(WeaponClass cls, Transform parent, int layer, out Transform muzzle, out Transform sight)
        {
            var lib = PresentationLibrary.Instance;
            var root = new GameObject($"Gun_{cls}").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = layer;

            Material metal = lib != null ? lib.gunMetal : null;
            Material accent = lib != null ? lib.gunAccent : null;
            float barrelEnd;
            Vector3 sightPoint;
            switch (cls)
            {
                case WeaponClass.SMG:
                    Box(root, layer, metal, new Vector3(0f, 0f, 0.02f), new Vector3(0.055f, 0.08f, 0.30f));
                    Box(root, layer, metal, new Vector3(0f, 0.005f, 0.22f), new Vector3(0.028f, 0.028f, 0.14f));
                    Box(root, layer, accent, new Vector3(0f, -0.10f, 0.06f), new Vector3(0.035f, 0.16f, 0.045f));
                    Box(root, layer, accent, new Vector3(0f, -0.07f, -0.06f), new Vector3(0.035f, 0.09f, 0.04f));
                    Box(root, layer, metal, new Vector3(0f, 0f, -0.2f), new Vector3(0.03f, 0.05f, 0.14f));
                    sightPoint = ApertureSights(root, layer, metal, 0.04f, -0.08f, 0.2f, 0.012f, 0.078f);
                    barrelEnd = 0.29f;
                    break;
                case WeaponClass.Shotgun:
                    Box(root, layer, metal, new Vector3(0f, 0f, 0.0f), new Vector3(0.06f, 0.085f, 0.36f));
                    Box(root, layer, metal, new Vector3(0f, 0.02f, 0.36f), new Vector3(0.034f, 0.034f, 0.42f));
                    Box(root, layer, accent, new Vector3(0f, -0.025f, 0.3f), new Vector3(0.05f, 0.045f, 0.18f));
                    Box(root, layer, accent, new Vector3(0f, -0.03f, -0.28f), new Vector3(0.045f, 0.1f, 0.24f));
                    // Vent rib with a front bead; the eye looks along the rib.
                    Box(root, layer, metal, new Vector3(0f, 0.043f, 0.33f), new Vector3(0.012f, 0.008f, 0.46f));
                    Box(root, layer, accent, new Vector3(0f, 0.051f, 0.555f), new Vector3(0.006f, 0.006f, 0.006f));
                    Box(root, layer, metal, new Vector3(-0.012f, 0.05f, -0.1f), new Vector3(0.006f, 0.012f, 0.012f));
                    Box(root, layer, metal, new Vector3(0.012f, 0.05f, -0.1f), new Vector3(0.006f, 0.012f, 0.012f));
                    sightPoint = new Vector3(0f, 0.054f, -0.1f);
                    barrelEnd = 0.57f;
                    break;
                case WeaponClass.Sniper:
                    Box(root, layer, metal, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.085f, 0.46f));
                    Box(root, layer, metal, new Vector3(0f, 0.01f, 0.48f), new Vector3(0.026f, 0.026f, 0.52f));
                    Box(root, layer, accent, new Vector3(0f, 0.085f, 0.02f), new Vector3(0.05f, 0.05f, 0.30f));
                    Box(root, layer, accent, new Vector3(0f, -0.09f, 0.04f), new Vector3(0.04f, 0.1f, 0.06f));
                    Box(root, layer, accent, new Vector3(0f, -0.03f, -0.34f), new Vector3(0.05f, 0.11f, 0.26f));
                    sightPoint = new Vector3(0f, 0.085f, -0.13f); // rear of the scope
                    barrelEnd = 0.74f;
                    break;
                case WeaponClass.Pistol:
                    Box(root, layer, metal, new Vector3(0f, 0.02f, 0.03f), new Vector3(0.035f, 0.045f, 0.19f));
                    Box(root, layer, accent, new Vector3(0f, -0.05f, -0.03f), new Vector3(0.032f, 0.11f, 0.05f));
                    // Notch rear sight and front post: post tip sits level with the notch.
                    Box(root, layer, metal, new Vector3(-0.0095f, 0.0485f, -0.055f), new Vector3(0.008f, 0.012f, 0.008f));
                    Box(root, layer, metal, new Vector3(0.0095f, 0.0485f, -0.055f), new Vector3(0.008f, 0.012f, 0.008f));
                    Box(root, layer, accent, new Vector3(0f, 0.0485f, 0.11f), new Vector3(0.004f, 0.012f, 0.006f));
                    sightPoint = new Vector3(0f, 0.0545f, -0.055f);
                    barrelEnd = 0.13f;
                    break;
                default: // assault rifle
                    Box(root, layer, metal, new Vector3(0f, 0f, 0.02f), new Vector3(0.06f, 0.09f, 0.40f));
                    Box(root, layer, metal, new Vector3(0f, 0.01f, 0.34f), new Vector3(0.028f, 0.028f, 0.28f));
                    Box(root, layer, accent, new Vector3(0f, -0.115f, 0.08f), new Vector3(0.04f, 0.15f, 0.07f));
                    Box(root, layer, accent, new Vector3(0f, -0.03f, -0.27f), new Vector3(0.05f, 0.09f, 0.2f));
                    sightPoint = ApertureSights(root, layer, metal, 0.045f, -0.06f, 0.30f, 0.024f, 0.088f);
                    barrelEnd = 0.48f;
                    break;
            }
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0f, 0.01f, barrelEnd);
            sight = new GameObject("Sight").transform;
            sight.SetParent(root, false);
            sight.localPosition = sightPoint;
            return root;
        }

        /// <summary>
        /// Top rail, ghost-ring rear aperture and protected front post. Returns the sight point
        /// (aperture centre), which the front post tip lines up with.
        /// </summary>
        static Vector3 ApertureSights(Transform root, int layer, Material m, float receiverTop, float zRear, float zFront, float barrelTop, float lineY)
        {
            const float hole = 0.011f, wall = 0.0035f, depth = 0.006f;
            float railTop = receiverTop + 0.008f;
            float railStart = zRear - 0.012f, railEnd = 0.15f;
            Box(root, layer, m, new Vector3(0f, receiverTop + 0.004f, (railStart + railEnd) * 0.5f), new Vector3(0.02f, 0.008f, railEnd - railStart));
            // rear ghost ring: two thin posts rising from the rail and a top bar; open underneath
            float holeTop = lineY + hole;
            float postH = holeTop - railTop;
            Box(root, layer, m, new Vector3(-(hole + wall * 0.5f), railTop + postH * 0.5f, zRear), new Vector3(wall, postH, depth));
            Box(root, layer, m, new Vector3(hole + wall * 0.5f, railTop + postH * 0.5f, zRear), new Vector3(wall, postH, depth));
            Box(root, layer, m, new Vector3(0f, holeTop + wall * 0.5f, zRear), new Vector3(2f * (hole + wall), wall, depth));
            // front: base block, thin post (tip on lineY), protective ears
            float postBase = lineY - 0.016f;
            Box(root, layer, m, new Vector3(0f, (barrelTop + postBase) * 0.5f, zFront), new Vector3(0.016f, postBase - barrelTop, 0.012f));
            Box(root, layer, m, new Vector3(0f, lineY - 0.008f, zFront), new Vector3(0.0035f, 0.016f, 0.004f));
            Box(root, layer, m, new Vector3(-0.009f, lineY - 0.006f, zFront), new Vector3(0.003f, 0.02f, 0.01f));
            Box(root, layer, m, new Vector3(0.009f, lineY - 0.006f, zFront), new Vector3(0.003f, 0.02f, 0.01f));
            return new Vector3(0f, lineY, zRear);
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
