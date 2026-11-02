using System.Collections.Generic;
using Saltline.Net;
using Saltline.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saltline.Match
{
    /// <summary>
    /// Host-side spawn selection. Prefers the team's own points, then scores each by distance
    /// to living enemies, enemy line of sight, crowding and recent use.
    /// </summary>
    public static class SpawnDirector
    {
        static readonly List<SpawnPoint> s_Points = new();
        static Scene s_CachedScene;

        static void Refresh()
        {
            var active = SceneManager.GetActiveScene();
            if (s_CachedScene == active && s_Points.Count > 0 && s_Points[0] != null) return;
            s_CachedScene = active;
            s_Points.Clear();
            s_Points.AddRange(Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.InstanceID));
        }

        public static bool HasSpawnPoints
        {
            get { Refresh(); return s_Points.Count > 0; }
        }

        public static bool Pick(Team team, NetPlayer self, out Vector3 position, out float yaw)
        {
            Refresh();
            position = Vector3.up;
            yaw = 0f;
            if (s_Points.Count == 0) return false;

            bool teamHasPoints = false;
            foreach (var p in s_Points) if (p != null && p.team == team && team != Team.None) { teamHasPoints = true; break; }

            float now = Time.time;
            SpawnPoint best = null;
            float bestScore = float.MinValue;
            foreach (var p in s_Points)
            {
                if (p == null) continue;
                if (teamHasPoints && p.team != team) continue;

                Vector3 pos = p.transform.position;
                Vector3 eye = pos + Vector3.up * 1.6f;
                float score = p.priority + Random.value * 0.5f;
                float nearestEnemy = 60f;
                foreach (var other in NetPlayer.All)
                {
                    if (other == null || other == self || !other.IsSpawned || !other.AliveState.Value) continue;
                    Vector3 o = other.SimMotor.Position;
                    float d = Vector3.Distance(o, pos);
                    if (d < 1.2f) score -= 100f; // occupied
                    bool enemy = other.TeamState.Value != team || team == Team.None;
                    if (!enemy) continue;
                    nearestEnemy = Mathf.Min(nearestEnemy, d);
                    if (d < 50f && !Physics.Linecast(o + Vector3.up * 1.6f, eye, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                        score -= 30f; // an enemy could see us spawn
                }
                score += Mathf.Min(nearestEnemy, 40f);
                if (now - p.LastUsedTime < 3f) score -= 20f;

                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (best == null) return false;
            best.LastUsedTime = now;
            position = best.transform.position;
            yaw = best.transform.eulerAngles.y;
            return true;
        }
    }
}
