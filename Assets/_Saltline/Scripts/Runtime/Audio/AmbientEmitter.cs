using System.Collections.Generic;
using UnityEngine;

namespace Saltline.Audio
{
    /// <summary>
    /// A looping sound at a fixed place in a map (pump-house machinery and the like). Map
    /// factories place these; the clip comes from the SoundBank by name.
    /// </summary>
    public sealed class AmbientEmitter : MonoBehaviour
    {
        public string sound = "amb_hum";
        [Range(0f, 1f)] public float volume = 1f;
        public float minDistance = 4f;
        public float maxDistance = 30f;

        public static readonly List<AmbientEmitter> All = new();
        AudioSource m_Src;
        SoundDef m_Def;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            if (!Saltline.Core.RunMode.Presentation) { enabled = false; return; }
            var bank = SoundBank.Instance;
            m_Def = bank != null ? bank.Get(sound) : null;
            if (m_Def == null || m_Def.clips == null || m_Def.clips.Length == 0)
            {
                Debug.LogWarning($"[SALTLINE] Ambient emitter '{name}': sound '{sound}' is not in the sound bank.");
                return;
            }
            m_Src = gameObject.AddComponent<AudioSource>();
            m_Src.clip = m_Def.clips[0];
            m_Src.loop = true;
            m_Src.spatialBlend = 1f;
            m_Src.rolloffMode = AudioRolloffMode.Logarithmic;
            m_Src.minDistance = minDistance;
            m_Src.maxDistance = maxDistance;
            m_Src.dopplerLevel = 0f;
            m_Src.spread = 60f;
            m_Src.priority = 200;
            ApplyVolume();
            // Start somewhere in the loop so two emitters of the same sound are not in phase.
            m_Src.time = Random.Range(0f, m_Src.clip.length * 0.9f);
            m_Src.Play();
        }

        public void ApplyVolume()
        {
            if (m_Src == null || m_Def == null) return;
            m_Src.volume = Mathf.Clamp01(m_Def.volume * volume * GameAudio.GroupGain(SoundGroup.Ambience));
        }
    }
}
