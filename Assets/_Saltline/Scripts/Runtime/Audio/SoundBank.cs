using System;
using System.Collections.Generic;
using UnityEngine;

namespace Saltline.Audio
{
    /// <summary>Volume buses. Each has its own slider in the settings; the master volume scales them all.</summary>
    public enum SoundGroup : byte { Weapon, Player, Impact, Feedback, Interface, Ambience }

    [Serializable]
    public sealed class SoundDef
    {
        public string name;
        public SoundGroup group;
        [Tooltip("Variants; one is picked at random, never the same one twice in a row.")]
        public AudioClip[] clips;
        [Tooltip("Set by AudioFactory from the measured loudness and the group's target.")]
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch change either way, as a fraction (0.04 = ±4%).")]
        public float pitchJitter = 0.04f;
        [Tooltip("Full volume up to this distance (m).")]
        public float minDistance = 2f;
        [Tooltip("Silent beyond this distance (m); also the distance past which the sound is not started.")]
        public float maxDistance = 40f;
        [Tooltip("Most copies playing at once; a new one replaces the oldest.")]
        public int maxVoices = 4;
        [Tooltip("Higher wins when every voice is busy.")]
        public int priority = 1;
        [Tooltip("Loudness of the source file (LUFS, loudest 400 ms), from the generator's manifest.")]
        public float measuredLufs;

        [NonSerialized] public int lastVariant = -1;
    }

    /// <summary>
    /// Every sound the game plays, built by AudioFactory from the generator's manifest
    /// (ArtSource/Audio/gen_sfx.py) and loaded from Resources so builds include it.
    /// </summary>
    [CreateAssetMenu(menuName = "Saltline/Sound Bank", fileName = "SoundBank")]
    public sealed class SoundBank : ScriptableObject
    {
        public SoundDef[] sounds = Array.Empty<SoundDef>();

        Dictionary<string, SoundDef> m_ByName;

        public SoundDef Get(string name)
        {
            if (m_ByName == null)
            {
                m_ByName = new Dictionary<string, SoundDef>(StringComparer.Ordinal);
                foreach (var s in sounds) if (s != null && !string.IsNullOrEmpty(s.name)) m_ByName[s.name] = s;
            }
            return m_ByName.TryGetValue(name, out var d) ? d : null;
        }

        public void Invalidate() => m_ByName = null;

        static SoundBank s_Instance;
        public static SoundBank Instance
        {
            get
            {
                if (s_Instance == null) s_Instance = Resources.Load<SoundBank>("SoundBank");
                return s_Instance;
            }
        }
    }
}
