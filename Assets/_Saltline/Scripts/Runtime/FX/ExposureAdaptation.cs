using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Saltline.FX
{
    /// <summary>
    /// Eye adaptation for URP, which has no auto exposure. Stepping from the sunlit yard into the pump
    /// house, the baked light probes around the camera get several times darker; this raises exposure
    /// over about a second (and lowers it faster on the way out), the way eyes and cameras adjust.
    ///
    /// It reads only the baked probes (no GPU readback), so it costs a few microseconds per frame.
    /// It adds a high-priority global volume that overrides post exposure with the scene's own value
    /// plus the adaptation.
    /// </summary>
    [DefaultExecutionOrder(250)]
    public sealed class ExposureAdaptation : MonoBehaviour
    {
        [Tooltip("Probe brightness that needs no correction: open ground in daylight.")]
        public float referenceLuminance = 0.6f;
        [Tooltip("Fraction of the brightness difference made up (1 = full, 0.6 keeps interiors a bit darker).")]
        public float strength = 0.6f;
        public float maxBoost = 1.4f;    // stops
        public float maxCut = 0.4f;      // stops
        public float darkenSpeed = 2.5f; // stops per second when coming out into light
        public float brightenSpeed = 1.2f;

        const float OwnPriority = 100f;

        // Six axis directions plus the view direction (slot 6, filled per frame).
        static readonly Vector3[] s_Dirs =
        {
            Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.forward,
        };
        static readonly Color[] s_Out = new Color[7];

        Volume m_Volume;
        VolumeProfile m_Profile;
        ColorAdjustments m_Adjust;
        float m_Base;
        float m_Current;
        bool m_HaveBase;

        public float CurrentStops => m_Current;

        void OnEnable()
        {
            var go = new GameObject("ExposureAdaptationVolume") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            m_Volume = go.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = OwnPriority;
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.hideFlags = HideFlags.DontSave;
            m_Adjust = m_Profile.Add<ColorAdjustments>(true);
            m_Volume.sharedProfile = m_Profile;
            m_HaveBase = false;
        }

        void OnDisable()
        {
            if (m_Volume != null) Destroy(m_Volume.gameObject);
            if (m_Profile != null) Destroy(m_Profile);
            m_Volume = null;
            m_Profile = null;
            m_Adjust = null;
        }

        /// <summary>The post exposure every other global volume would give, without this one.</summary>
        static float SceneExposure()
        {
            float best = 0f, bestPriority = float.MinValue;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isGlobal || v.priority >= OwnPriority || v.sharedProfile == null) continue;
                if (v.sharedProfile.TryGet<ColorAdjustments>(out var ca) && ca.postExposure.overrideState && v.priority > bestPriority)
                {
                    best = ca.postExposure.value;
                    bestPriority = v.priority;
                }
            }
            return best;
        }

        /// <summary>Adaptation (stops) the current position and view would settle on.</summary>
        public float TargetStops()
        {
            var probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) return 0f;
            LightProbes.GetInterpolatedProbe(transform.position, null, out SphericalHarmonicsL2 sh);
            s_Dirs[6] = transform.forward;
            sh.Evaluate(s_Dirs, s_Out);
            float around = 0f;
            for (int i = 0; i < 6; i++) around += Luma(s_Out[i]);
            around /= 6f;
            // What the eye sees: half the view direction, half the surroundings.
            float lum = Mathf.Max(1e-3f, 0.5f * around + 0.5f * Luma(s_Out[6]));
            return Mathf.Clamp(Mathf.Log(referenceLuminance / lum, 2f) * strength, -maxCut, maxBoost);
        }

        /// <summary>Jump straight to the settled exposure (after a teleport, respawn or screenshot cut).</summary>
        public void Snap()
        {
            EnsureBase();
            m_Current = TargetStops();
            Apply();
        }

        void LateUpdate()
        {
            if (m_Adjust == null) return;
            EnsureBase();
            float target = TargetStops();
            float speed = target < m_Current ? darkenSpeed : brightenSpeed;
            m_Current = Mathf.MoveTowards(m_Current, target, speed * Time.deltaTime);
            Apply();
        }

        void EnsureBase()
        {
            if (m_HaveBase) return;
            m_Base = SceneExposure();
            m_HaveBase = true;
        }

        void Apply()
        {
            if (m_Adjust != null) m_Adjust.postExposure.Override(m_Base + m_Current);
        }

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
