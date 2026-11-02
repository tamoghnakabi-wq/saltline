using System.Collections.Generic;
using UnityEngine;

namespace Saltline.Combat
{
    public enum SurfaceType : byte
    {
        Concrete = 0,
        Metal = 1,
        ThinMetal = 2,
        Wood = 3,
        Glass = 4,
        Dirt = 5,
        Water = 6,
        Fabric = 7,
        Plastic = 8,
    }

    /// <summary>
    /// Marks environment colliders with a material for impact effects, footsteps and bullet
    /// penetration. Untagged geometry counts as impenetrable concrete.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTag : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.Concrete;
        [Tooltip("Penetration power needed to shoot through this object. 0 = bullets never pass through.")]
        public float penetrationCost;
        [Range(0f, 1f), Tooltip("Fraction of damage kept after passing through.")]
        public float damageRetained = 0.7f;

        static readonly Dictionary<int, SurfaceTag> s_Cache = new();

        void Reset()
        {
            penetrationCost = DefaultCost(surface);
            damageRetained = DefaultRetained(surface);
        }

        public static float DefaultCost(SurfaceType s) => s switch
        {
            SurfaceType.Wood => 0.35f,
            SurfaceType.ThinMetal => 0.6f,
            SurfaceType.Glass => 0.05f,
            SurfaceType.Fabric => 0.05f,
            SurfaceType.Plastic => 0.25f,
            _ => 0f,
        };

        public static float DefaultRetained(SurfaceType s) => s switch
        {
            SurfaceType.Glass => 0.9f,
            SurfaceType.Fabric => 0.9f,
            SurfaceType.Wood => 0.7f,
            SurfaceType.Plastic => 0.75f,
            SurfaceType.ThinMetal => 0.55f,
            _ => 0.5f,
        };

        /// <summary>Tag for a collider (searches parents), cached by collider instance id.</summary>
        public static SurfaceTag Of(Collider c)
        {
            if (c == null) return null;
            int id = c.GetInstanceID();
            if (s_Cache.TryGetValue(id, out var tag)) return tag;
            tag = c.GetComponentInParent<SurfaceTag>();
            s_Cache[id] = tag;
            return tag;
        }

        public static SurfaceType TypeOf(Collider c)
        {
            var t = Of(c);
            return t != null ? t.surface : SurfaceType.Concrete;
        }

        public static void ClearCache() => s_Cache.Clear();
    }
}
