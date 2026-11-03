using UnityEngine;

namespace Saltline.Settings
{
    /// <summary>Per-machine player preferences, persisted in PlayerPrefs.</summary>
    public static class GameSettings
    {
        const string P = "saltline.";

        static float F(string k, float d) => PlayerPrefs.GetFloat(P + k, d);
        static bool B(string k, bool d) => PlayerPrefs.GetInt(P + k, d ? 1 : 0) != 0;
        static void SetF(string k, float v) { PlayerPrefs.SetFloat(P + k, v); Changed?.Invoke(); }
        static void SetB(string k, bool v) { PlayerPrefs.SetInt(P + k, v ? 1 : 0); Changed?.Invoke(); }

        public static event System.Action Changed;

        /// <summary>1.0 = 0.044 degrees per mouse count (a common competitive baseline).</summary>
        public static float MouseSensitivity { get => F("sens", 1f); set => SetF("sens", Mathf.Clamp(value, 0.05f, 10f)); }
        public static float AdsSensitivity { get => F("sensAds", 0.85f); set => SetF("sensAds", Mathf.Clamp(value, 0.1f, 2f)); }
        public static float ScopeSensitivity { get => F("sensScope", 0.75f); set => SetF("sensScope", Mathf.Clamp(value, 0.1f, 2f)); }
        public static float GamepadSensitivity { get => F("sensPad", 1f); set => SetF("sensPad", Mathf.Clamp(value, 0.1f, 4f)); }
        public static bool InvertY { get => B("invertY", false); set => SetB("invertY", value); }
        public static bool ToggleAim { get => B("toggleAim", false); set => SetB("toggleAim", value); }
        public static bool ToggleCrouch { get => B("toggleCrouch", false); set => SetB("toggleCrouch", value); }
        /// <summary>Horizontal field of view in degrees (converted to vertical per aspect ratio).</summary>
        public static float FieldOfView { get => F("fov", 100f); set => SetF("fov", Mathf.Clamp(value, 70f, 120f)); }
        public static bool StartInThirdPerson { get => B("tps", false); set => SetB("tps", value); }
        public static bool CameraBob { get => B("bob", true); set => SetB("bob", value); }
        public static float MasterVolume { get => F("volMaster", 0.9f); set => SetF("volMaster", Mathf.Clamp01(value)); }
        /// <summary>Weapons, footsteps, impacts and hit feedback.</summary>
        public static float EffectsVolume { get => F("volFx", 1f); set => SetF("volFx", Mathf.Clamp01(value)); }
        /// <summary>Menu sounds and match stings.</summary>
        public static float InterfaceVolume { get => F("volUi", 0.8f); set => SetF("volUi", Mathf.Clamp01(value)); }
        /// <summary>Wind, sea and machinery.</summary>
        public static float AmbienceVolume { get => F("volAmb", 0.8f); set => SetF("volAmb", Mathf.Clamp01(value)); }

        public const float DegreesPerCount = 0.044f;
        public const float GamepadDegreesPerSecond = 220f;

        public static float VerticalFov(float horizontalFov, float aspect)
        {
            float h = horizontalFov * Mathf.Deg2Rad;
            return 2f * Mathf.Atan(Mathf.Tan(h * 0.5f) / Mathf.Max(0.1f, aspect)) * Mathf.Rad2Deg;
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
