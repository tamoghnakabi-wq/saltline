using System.Collections.Generic;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.Combat
{
    public struct ShotTarget
    {
        public ulong ClientId;
        public HitboxPose Pose;
    }

    public struct PelletHit
    {
        public ulong Victim;
        public HitRegion Region;
        public float Distance;
        public float PenetrationFactor;
        public Vector3 Point;
    }

    public struct Impact
    {
        public Vector3 Point;
        public Vector3 Normal;
        public SurfaceType Surface;
        /// <summary>The bullet hit a player rather than the world.</summary>
        public bool Body;
    }

    /// <summary>
    /// Traces bullets through the static world (with penetration) and analytic player hitboxes.
    /// The host runs it against rewound poses; clients run it with no targets for impact effects.
    /// </summary>
    public static class ShotResolver
    {
        public const int MaxPenetrations = 2;

        static readonly RaycastHit[] s_Hits = new RaycastHit[24];
        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        public static bool TracePellet(Vector3 origin, Vector3 dir, WeaponDefinition def, MotorSettings ms,
            List<ShotTarget> targets, out PelletHit hit, List<Impact> impacts, out Vector3 endPoint)
        {
            hit = default;
            float range = def.maxRange;

            // Nearest player hitbox along the whole ray.
            float playerT = float.MaxValue;
            int playerIndex = -1;
            HitRegion playerRegion = HitRegion.None;
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var pose = targets[i].Pose;
                    if (!HitboxModel.RayNearPlayer(origin, dir, pose, range)) continue;
                    HitboxModel.Build(pose, ms, s_Caps);
                    if (HitboxModel.Raycast(origin, dir, s_Caps, range, out float t, out HitRegion region) && t < playerT)
                    {
                        playerT = t;
                        playerIndex = i;
                        playerRegion = region;
                    }
                }
            }

            int n = Physics.RaycastNonAlloc(origin, dir, s_Hits, range, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            SortByDistance(s_Hits, n);

            float factor = 1f;
            float power = def.penetrationPower;
            int penetrations = 0;
            for (int i = 0; i < n; i++)
            {
                ref RaycastHit h = ref s_Hits[i];
                if (playerIndex >= 0 && playerT <= h.distance) break; // the player is in front of this surface

                var tag = SurfaceTag.Of(h.collider);
                impacts?.Add(new Impact { Point = h.point, Normal = h.normal, Surface = tag != null ? tag.surface : SurfaceType.Concrete });

                float cost = tag != null ? tag.penetrationCost : 0f;
                if (cost > 0f && cost <= power && penetrations < MaxPenetrations)
                {
                    power -= cost;
                    factor *= tag.damageRetained;
                    penetrations++;
                    continue;
                }

                // Stopped by this surface (and no player before it).
                endPoint = h.point;
                return false;
            }

            if (playerIndex >= 0)
            {
                hit = new PelletHit
                {
                    Victim = targets[playerIndex].ClientId,
                    Region = playerRegion,
                    Distance = playerT,
                    PenetrationFactor = factor,
                    Point = origin + dir * playerT,
                };
                endPoint = hit.Point;
                return true;
            }

            endPoint = origin + dir * range;
            return false;
        }

        /// <summary>First environment surface along a ray (no penetration), for effects and aim checks.</summary>
        public static bool EnvironmentRay(Vector3 origin, Vector3 dir, float range, out RaycastHit hit) =>
            Physics.Raycast(origin, dir, out hit, range, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);

        static void SortByDistance(RaycastHit[] hits, int count)
        {
            for (int i = 1; i < count; i++)
            {
                var key = hits[i];
                int j = i - 1;
                while (j >= 0 && hits[j].distance > key.distance)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = key;
            }
        }
    }
}
