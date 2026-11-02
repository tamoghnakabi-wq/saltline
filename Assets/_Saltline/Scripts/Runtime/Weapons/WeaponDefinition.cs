using UnityEngine;

namespace Saltline.Weapons
{
    public enum WeaponClass : byte { AssaultRifle, SMG, Shotgun, Sniper, Pistol }
    public enum FireMode : byte { Automatic, SemiAuto, PumpAction, BoltAction }
    public enum ReloadMode : byte { Magazine, PerShell }
    public enum WeaponSlot : byte { Primary = 0, Secondary = 1 }

    /// <summary>
    /// Everything that makes a weapon feel and behave differently. Values are authored data;
    /// the simulation and damage code never special-cases a particular weapon.
    /// </summary>
    [CreateAssetMenu(menuName = "Saltline/Weapon Definition", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public byte id;
        public string displayName = "Weapon";
        public string shortName = "WPN";
        [TextArea] public string description;
        public WeaponClass weaponClass;
        public WeaponSlot slot;
        public FireMode fireMode = FireMode.Automatic;

        [Header("Damage")]
        public float damage = 25f;
        public int pellets = 1;
        public float headMultiplier = 1.6f;
        public float upperTorsoMultiplier = 1.0f;
        public float lowerTorsoMultiplier = 0.95f;
        public float limbMultiplier = 0.8f;
        [Tooltip("Full damage until this distance (m).")] public float falloffStart = 25f;
        [Tooltip("Minimum damage reached at this distance (m).")] public float falloffEnd = 50f;
        [Range(0f, 1f)] public float minDamageFactor = 0.7f;
        public float maxRange = 250f;
        [Tooltip("How much material the round can push through (see SurfaceType costs).")]
        public float penetrationPower = 1f;

        [Header("Rate & ammo")]
        public float roundsPerMinute = 600f;
        public int magazineSize = 30;
        public int reserveAmmo = 120;
        public ReloadMode reloadMode = ReloadMode.Magazine;
        public float reloadTime = 2.1f;
        public float emptyReloadTime = 2.6f;
        [Range(0.1f, 1f), Tooltip("Fraction of the reload after which ammo is committed.")]
        public float reloadCommitPoint = 0.8f;
        public float shellInsertTime = 0.5f;   // PerShell only
        public float reloadStartTime = 0.35f;  // PerShell only

        [Header("Handling")]
        public float equipTime = 0.45f;
        public float holsterTime = 0.25f;
        public float adsTime = 0.22f;
        public float adsFov = 55f;
        public bool hasScope;
        public float scopeFov = 22f;
        [Tooltip("Movement speed multiplier while this weapon is out.")]
        public float moveSpeedMultiplier = 1f;
        public float adsMoveMultiplier = 1f;
        [Tooltip("Can't sprint while reloading this weapon (heavy weapons).")]
        public bool blockSprintWhileReloading;

        [Header("Accuracy (degrees, cone half-angle)")]
        public float hipSpread = 2.2f;
        public float adsSpread = 0.15f;
        public float moveSpread = 1.6f;   // added at full run speed
        public float airSpread = 4f;
        public float crouchSpreadMultiplier = 0.8f;
        public float bloomPerShot = 0.35f;
        public float maxBloom = 3f;
        public float bloomRecovery = 6f;  // degrees/s

        [Header("Recoil (camera, degrees)")]
        [Tooltip("Per-shot view kick: x = yaw (right +), y = pitch (up +). Repeats the tail when exhausted.")]
        public Vector2[] recoilPattern = { new Vector2(0f, 0.6f) };
        public float recoilRandomYaw = 0.12f;
        public float recoilAdsMultiplier = 0.75f;
        [Tooltip("How quickly the view returns toward the pre-burst aim after firing stops.")]
        public float recoilRecoverySpeed = 9f;
        [Range(0f, 1f)] public float recoilRecoveryFraction = 0.65f;
        public float recoilResetTime = 0.22f;

        [Header("Visual recoil (viewmodel only, no aim effect)")]
        public float viewKickBack = 0.05f;
        public float viewKickUp = 2.5f;
        public float viewKickRoll = 1.5f;
        public float cameraShake = 0.25f;

        [Header("Presentation")]
        public int tracerEvery = 1;
        public float tracerSpeed = 380f;
        public Color tracerColor = new Color(1f, 0.82f, 0.55f);
        public float muzzleFlashScale = 1f;
        public string fireSoundId = "rifle";
        public float soundRange = 120f;
        public Vector3 viewmodelOffset = new Vector3(0.18f, -0.17f, 0.34f);
        public Vector3 adsViewmodelOffset = new Vector3(0f, -0.105f, 0.22f); // fallback for models without a sight point
        [Tooltip("Distance from the eye to the rear sight when aiming down sights (m).")]
        public float adsEyeRelief = 0.14f;

        public float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);

        public Vector2 RecoilForShot(int shotIndex)
        {
            if (recoilPattern == null || recoilPattern.Length == 0) return Vector2.zero;
            return recoilPattern[Mathf.Clamp(shotIndex, 0, recoilPattern.Length - 1)];
        }
    }
}
