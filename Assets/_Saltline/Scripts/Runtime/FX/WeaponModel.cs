using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.FX
{
    public enum WeaponView : byte
    {
        FirstPerson, // detailed model on the viewmodel layer, no shadows
        ThirdPerson, // decimated model held at the hand, casts shadows
        Display,     // detailed model with shadows (menu stand, showcases)
    }

    /// <summary>
    /// Builds the presentation model for a weapon class: the Blender-authored model from
    /// PresentationLibrary.weaponModels when one exists, otherwise the greybox stand-in.
    ///
    /// Imported models are laid out flat by ArtSource/Blender/weapons_build.py, all children of W_tag:
    ///   tag_Body, tag_Mag, tag_Bolt, ...      detailed meshes, one per moving part
    ///   tag_TP_Body, tag_TP_Mag               decimated third-person meshes
    ///   Muzzle, Sight, Eject, Grip_R, Grip_L, MagWell markers
    /// with the bore along +z, origin at the receiver.
    /// </summary>
    public static class WeaponModel
    {
        public static bool HasModel(WeaponClass cls) => Source(cls) != null;

        static GameObject Source(WeaponClass cls)
        {
            var lib = PresentationLibrary.Instance;
            var models = lib != null ? lib.weaponModels : null;
            int i = (int)cls;
            return models != null && i < models.Length ? models[i] : null;
        }

        /// <param name="sight">Point on the sight line, a direct child of the returned root (ADS lines it up with the eye).</param>
        /// <param name="rig">Animates the moving parts; null for the greybox stand-in.</param>
        public static Transform Build(WeaponClass cls, Transform parent, int layer, WeaponView view,
            out Transform muzzle, out Transform sight, out WeaponRig rig)
        {
            rig = null;
            var src = Source(cls);
            if (src == null)
            {
                var grey = GreyboxGun.Build(cls, parent, layer, out muzzle, out sight);
                return grey;
            }

            var model = Object.Instantiate(src);
            model.name = src.name;
            bool thirdPerson = view == WeaponView.ThirdPerson;
            for (int i = model.transform.childCount - 1; i >= 0; i--)
            {
                var c = model.transform.GetChild(i);
                if (c.GetComponent<MeshFilter>() == null) continue; // markers stay
                if (IsThirdPersonMesh(c.name) != thirdPerson) Object.DestroyImmediate(c.gameObject);
            }

            var shadows = view == WeaponView.FirstPerson ? ShadowCastingMode.Off : ShadowCastingMode.On;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = shadows;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                // The viewmodel camera only draws the gun; skipping motion vectors avoids smearing it with camera motion.
                r.motionVectorGenerationMode = view == WeaponView.FirstPerson ? MotionVectorGenerationMode.ForceNoMotion : MotionVectorGenerationMode.Object;
            }
            SetLayer(model.transform, layer);

            muzzle = model.transform.Find("Muzzle");
            sight = model.transform.Find("Sight");

            Transform root = model.transform;
            if (view == WeaponView.ThirdPerson)
            {
                // Third person: the root sits at the right hand, so put the grip there.
                var holder = new GameObject($"Gun_{cls}");
                holder.layer = layer;
                holder.transform.SetParent(parent, false);
                model.transform.SetParent(holder.transform, false);
                var grip = model.transform.Find("Grip_R");
                if (grip != null) model.transform.localPosition = -grip.localPosition;
                root = holder.transform;
            }
            else model.transform.SetParent(parent, false);

            rig = model.AddComponent<WeaponRig>();
            rig.Init(model.transform, model.transform);
            rig.Configure(cls);
            return root;
        }

        /// <summary>Third-person meshes are named tag_TP_Part; everything else is first-person detail.</summary>
        public static bool IsThirdPersonMesh(string name) => name.Contains("_TP_");

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
