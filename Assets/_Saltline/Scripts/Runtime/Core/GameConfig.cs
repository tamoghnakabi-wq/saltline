using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.Core
{
    /// <summary>Gameplay tuning shared by host and clients. Referenced by the player prefab.</summary>
    [CreateAssetMenu(menuName = "Saltline/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        public MotorSettings motor;
        public WeaponDatabase weapons;

        [Header("Health")]
        public int maxHealth = 100;
        [Tooltip("Seconds without taking damage before health regenerates. 0 disables regen.")]
        public float regenDelay = 6f;
        public float regenPerSecond = 25f;

        [Header("Respawn")]
        public float respawnDelay = 5f;
        public float spawnProtection = 2f;
        [Tooltip("Players below this height are killed (fell out of the map).")]
        public float killPlaneY = -40f;

        [Header("Netcode")]
        [Tooltip("Server sends state snapshots every N ticks (60 Hz tick).")]
        public int snapshotEveryTicks = 2;
        public float maxRewindSeconds = 0.35f;
        public float minInterpolationTicks = 3f;
        public float maxInterpolationTicks = 12f;

        [Header("Scoring")]
        public float assistWindow = 10f;
        public int assistMinDamage = 30;
        public int killScore = 100;
        public int assistScore = 50;
        public int headshotBonus = 25;
    }
}
