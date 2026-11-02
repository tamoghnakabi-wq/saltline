using Saltline.Weapons;
using UnityEngine;

namespace Saltline.Combat
{
    public static class DamageModel
    {
        public static float RegionMultiplier(WeaponDefinition w, HitRegion r) => r switch
        {
            HitRegion.Head => w.headMultiplier,
            HitRegion.UpperTorso => w.upperTorsoMultiplier,
            HitRegion.LowerTorso => w.lowerTorsoMultiplier,
            HitRegion.Arm => w.limbMultiplier,
            HitRegion.Leg => w.limbMultiplier,
            _ => 0f,
        };

        /// <summary>Linear falloff from 1 at falloffStart to minDamageFactor at falloffEnd.</summary>
        public static float Falloff(WeaponDefinition w, float distance)
        {
            if (distance <= w.falloffStart) return 1f;
            if (distance >= w.falloffEnd) return w.minDamageFactor;
            float t = (distance - w.falloffStart) / Mathf.Max(0.01f, w.falloffEnd - w.falloffStart);
            return Mathf.Lerp(1f, w.minDamageFactor, t);
        }

        /// <summary>Damage of a single pellet/bullet, before armor or modifiers.</summary>
        public static float PelletDamage(WeaponDefinition w, HitRegion region, float distance, float penetrationFactor)
        {
            return w.damage * RegionMultiplier(w, region) * Falloff(w, distance) * penetrationFactor;
        }

        /// <summary>Rounds accumulated pellet damage to the integer health change, minimum 1.</summary>
        public static int ToHealth(float damage) => damage <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(damage));
    }
}
