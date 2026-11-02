using Saltline.Net;
using Saltline.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saltline.Match
{
    /// <summary>
    /// Host-side: creates a player object for every client once a gameplay scene (one with
    /// spawn points) has loaded, including clients that join mid-match.
    /// </summary>
    public static class PlayerSpawner
    {
        static NetworkManager s_Nm;
        static NetworkObject s_Prefab;

        public static bool Enabled = true;

        public static void Install(NetworkManager nm, NetworkObject playerPrefab)
        {
            if (s_Nm == nm && s_Prefab == playerPrefab) return;
            s_Nm = nm;
            s_Prefab = playerPrefab;
            if (nm.SceneManager != null)
            {
                nm.SceneManager.OnLoadEventCompleted += (scene, mode, done, timedOut) => SpawnMissing();
                nm.SceneManager.OnSynchronizeComplete += clientId => SpawnFor(clientId);
            }
            SpawnMissing();
        }

        public static void SpawnMissing()
        {
            if (s_Nm == null || !s_Nm.IsServer || !Enabled) return;
            foreach (ulong id in s_Nm.ConnectedClientsIds) SpawnFor(id);
        }

        public static void SpawnFor(ulong clientId)
        {
            var nm = s_Nm;
            if (nm == null || !nm.IsServer || !Enabled || s_Prefab == null) return;
            if (!SpawnDirector.HasSpawnPoints) return; // not in a gameplay scene
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null) return;

            Team team = Team.None;
            if (GameSession.Instance != null && GameSession.Instance.TryGet(clientId, out var rec)) team = rec.Team;

            var go = Object.Instantiate(s_Prefab.gameObject);
            var player = go.GetComponent<NetPlayer>();
            SpawnDirector.Pick(team, player, out var pos, out float yaw);
            go.transform.position = pos;
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
            player.TeamState.Value = team; // network variables are written after spawn
            player.ServerRespawnAt(pos, yaw);
            Debug.Log($"[SALTLINE] Spawned player for client {clientId} ({team}) at {pos} in {SceneManager.GetActiveScene().name}");
        }
    }
}
