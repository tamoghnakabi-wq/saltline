using System.Collections.Generic;
using System.IO;
using System.Linq;
using Saltline.Core;
using Saltline.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Generates the Boot scene, the persistent network prefabs and the prefab registry from
    /// code, so the networking setup is reproducible and reviewable instead of hand-wired.
    /// </summary>
    public static class BootSceneFactory
    {
        public const string ScenesDir = "Assets/_Saltline/Scenes";
        public const string NetPrefabsDir = "Assets/_Saltline/Prefabs/Net";
        public const string PrefabListPath = "Assets/_Saltline/Prefabs/Net/SaltlineNetworkPrefabs.asset";
        public const string BootScenePath = ScenesDir + "/Boot.unity";

        [MenuItem("Saltline/Build/Boot Scene + Net Prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(NetPrefabsDir);

            var sessionPrefab = BuildSessionPrefab();
            var list = LoadOrCreatePrefabList();
            Register(list, sessionPrefab);
            foreach (var extra in ExtraNetworkPrefabs.Collect()) Register(list, extra);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var utp = nmGo.AddComponent<UnityTransport>();
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.TickRate = 60;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.EnableSceneManagement = true;
            nm.NetworkConfig.ForceSamePrefabs = true;
            nm.NetworkConfig.RecycleNetworkIds = true;
            nm.NetworkConfig.ClientConnectionBufferTimeout = 15;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { list };

            utp.MaxPacketQueueSize = 512;
            utp.MaxSendQueueSize = 0;
            utp.HeartbeatTimeoutMS = 500;
            utp.ConnectTimeoutMS = 1000;
            utp.MaxConnectAttempts = 30;
            utp.DisconnectTimeoutMS = 10000;

            var bootGo = new GameObject("GameBootstrap");
            var boot = bootGo.AddComponent<GameBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("sessionPrefab").objectReferenceValue = sessionPrefab.GetComponent<NetworkObject>();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(ContentFactory.PlayerPrefabPath);
            if (player != null) so.FindProperty("playerPrefab").objectReferenceValue = player.GetComponent<NetworkObject>();
            else Debug.LogWarning("[SALTLINE] Player prefab missing; run ContentFactory first.");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, BootScenePath);
            EnsureBuildScenes();

            uint hash = ReadHash(sessionPrefab);
            Debug.Log($"[SALTLINE] Boot scene built. GameSession prefab hash={hash}, registered prefabs={list.PrefabList.Count}");
            if (hash == 0) Debug.LogError("[SALTLINE] GameSession prefab has no GlobalObjectIdHash.");
        }

        static GameObject BuildSessionPrefab()
        {
            string path = NetPrefabsDir + "/GameSession.prefab";
            var go = new GameObject("GameSession");
            go.AddComponent<NetworkObject>();
            go.AddComponent<GameSession>();
            go.AddComponent<NetDiagnostics>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static NetworkPrefabsList LoadOrCreatePrefabList()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list != null) return list;
            list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            AssetDatabase.CreateAsset(list, PrefabListPath);
            return list;
        }

        public static void Register(NetworkPrefabsList list, GameObject prefab)
        {
            if (prefab == null || list.Contains(prefab)) return;
            list.Add(new NetworkPrefab { Prefab = prefab });
        }

        static uint ReadHash(GameObject prefab)
        {
            var so = new SerializedObject(prefab.GetComponent<NetworkObject>());
            var p = so.FindProperty("GlobalObjectIdHash");
            return p != null ? p.uintValue : 0;
        }

        /// <summary>Boot must be index 0; other game scenes follow if they exist.</summary>
        public static void EnsureBuildScenes()
        {
            var wanted = new List<string> { BootScenePath };
            foreach (string name in new[] { "Menu", "Arena", "Kappa" })
            {
                string p = $"{ScenesDir}/{name}.unity";
                if (File.Exists(p)) wanted.Add(p);
            }
            EditorBuildSettings.scenes = wanted.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        }
    }

    /// <summary>Other factories add their network prefabs here so the registry stays complete.</summary>
    public static class ExtraNetworkPrefabs
    {
        public static IEnumerable<GameObject> Collect()
        {
            if (!Directory.Exists(BootSceneFactory.NetPrefabsDir)) yield break;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Saltline/Prefabs" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.GetComponent<NetworkObject>() != null && go.name != "GameSession") yield return go;
            }
        }
    }
}
