using System;
using System.Collections.Generic;
using System.IO;
using Saltline.Audio;
using UnityEditor;
using UnityEngine;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Imports the generated sound effects (ArtSource/Audio/gen_sfx.py) and builds the SoundBank.
    /// Each sound's volume comes from its measured loudness (in the generator's manifest) and a
    /// target for its kind, so the mix is set by numbers that can be checked rather than by ear.
    /// </summary>
    public static class AudioFactory
    {
        public const string SoundDir = "Assets/_Saltline/Audio/Generated";
        public const string BankPath = ContentFactory.ResourcesDir + "/SoundBank.asset";

        [Serializable] class Manifest { public int sampleRate; public Entry[] sounds; }
        [Serializable] class Entry { public string name; public string group; public int variants; public float[] seconds; public float lufs; }

        /// <summary>How loud (LUFS, loudest 400 ms) a sound should be at full volume, and how it carries.</summary>
        public readonly struct Spec
        {
            public readonly float Lufs, Min, Max, Jitter;
            public readonly int Voices, Priority;
            public Spec(float lufs, float min, float max, int voices, int priority, float jitter)
            {
                Lufs = lufs; Min = min; Max = max; Voices = voices; Priority = priority; Jitter = jitter;
            }
        }

        // A single shot's loudest 400 ms. Held fire stacks the tails about 4 dB higher; these leave room
        // for that under the MasterLimiter's ceiling, which then only has to catch rare pile-ups.
        static readonly Dictionary<string, float> ShotTargets = new()
        {
            ["ar"] = -15f, ["smg"] = -17f, ["pistol"] = -17f, ["shotgun"] = -14f, ["sniper"] = -13f,
        };

        public static Spec SpecFor(string name)
        {
            if (name.StartsWith("shot_"))
            {
                bool far = name.EndsWith("_far");
                string w = name.Substring(5, name.Length - 5 - (far ? 4 : 0));
                float t = ShotTargets.TryGetValue(w, out var v) ? v : -15f;
                // Near shots carry across the whole map; the far layer is what the rest of the map hears.
                return far ? new Spec(t - 3f, 25f, 400f, 6, 4, 0.04f) : new Spec(t, 6f, 140f, 8, 5, 0.03f);
            }
            if (name == "impact_flesh") return new Spec(-22f, 2f, 40f, 4, 3, 0.08f);
            if (name.StartsWith("impact_")) return new Spec(-25f, 2f, 45f, 6, 2, 0.08f);
            if (name == "whizz") return new Spec(-18f, 1f, 8f, 3, 4, 0.06f);
            if (name.StartsWith("step_")) return new Spec(-25f, 2f, 32f, 10, 3, 0.06f);
            switch (name)
            {
                case "land": return new Spec(-23f, 2f, 30f, 4, 3, 0.05f);
                case "jump": return new Spec(-28f, 2f, 20f, 3, 2, 0.05f);
                case "slide_loop": return new Spec(-26f, 2f, 30f, 3, 3, 0.04f);
                case "hurt": return new Spec(-21f, 1f, 10f, 2, 6, 0.06f);
                case "death": return new Spec(-24f, 3f, 45f, 3, 4, 0.04f);
                case "hitmarker": return new Spec(-24f, 1f, 10f, 2, 6, 0.02f);
                case "headshot": return new Spec(-21f, 1f, 10f, 2, 6, 0f);
                case "kill": return new Spec(-20f, 1f, 10f, 2, 6, 0f);
                case "ui_hover": return new Spec(-32f, 1f, 10f, 2, 6, 0.02f);
                case "ui_click":
                case "ui_back": return new Spec(-28f, 1f, 10f, 2, 6, 0.02f);
                case "ui_ready": return new Spec(-26f, 1f, 10f, 2, 6, 0f);
                case "countdown": return new Spec(-24f, 1f, 10f, 2, 6, 0f);
                case "match_start":
                case "victory":
                case "defeat": return new Spec(-20f, 1f, 10f, 1, 7, 0f);
                case "amb_coast": return new Spec(-30f, 1f, 10f, 1, 0, 0f);
                case "amb_hum": return new Spec(-26f, 4f, 30f, 1, 0, 0f);
            }
            // Weapon handling: magazines, bolts, pumps, shells, equip, dry fire.
            return new Spec(-27f, 1f, 15f, 4, 2, 0.03f);
        }

        static SoundGroup GroupOf(string g) => g switch
        {
            "weapon" => SoundGroup.Weapon,
            "player" => SoundGroup.Player,
            "impact" => SoundGroup.Impact,
            "feedback" => SoundGroup.Feedback,
            "ui" => SoundGroup.Interface,
            "ambience" => SoundGroup.Ambience,
            _ => SoundGroup.Weapon,
        };

        [MenuItem("Saltline/Build/Sound bank")]
        public static void Build()
        {
            string manifestPath = SoundDir + "/manifest.json";
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[SALTLINE] {manifestPath} not found: run python3 ArtSource/Audio/gen_sfx.py first.");
                return;
            }
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var defs = new List<SoundDef>();
            int files = 0, reimported = 0, quiet = 0;
            foreach (var e in manifest.sounds)
            {
                var clips = new AudioClip[Mathf.Max(1, e.variants)];
                for (int v = 0; v < clips.Length; v++)
                {
                    string path = $"{SoundDir}/{(e.variants > 1 ? $"{e.name}_{v}" : e.name)}.wav";
                    if (Configure(path, e.group == "ambience")) reimported++;
                    clips[v] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clips[v] == null) Debug.LogError($"[SALTLINE] Audio: {path} missing or not imported.");
                    files++;
                }
                var spec = SpecFor(e.name);
                float volume = Mathf.Pow(10f, (spec.Lufs - e.lufs) / 20f);
                if (volume > 1f)
                {
                    quiet++;
                    Debug.LogWarning($"[SALTLINE] Audio: {e.name} is {20f * Mathf.Log10(volume):F1} dB below its target even at full volume.");
                }
                defs.Add(new SoundDef
                {
                    name = e.name,
                    group = GroupOf(e.group),
                    clips = clips,
                    volume = Mathf.Clamp(volume, 0.02f, 1f),
                    pitchJitter = spec.Jitter,
                    minDistance = spec.Min,
                    maxDistance = spec.Max,
                    maxVoices = spec.Voices,
                    priority = spec.Priority,
                    measuredLufs = e.lufs,
                });
            }

            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }
            bank.sounds = defs.ToArray();
            bank.Invalidate();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SALTLINE] Sound bank: {defs.Count} sounds, {files} files ({reimported} reimported), {quiet} below target -> {BankPath}");
        }

        /// <summary>Mono, Vorbis, decompressed on load (short effects) or compressed in memory (loops).</summary>
        static bool Configure(string path, bool loop)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) return false;
            var s = imp.defaultSampleSettings;
            var load = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            float quality = loop ? 0.6f : 0.8f;
            bool same = imp.forceToMono && !imp.loadInBackground && s.loadType == load
                        && s.compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Approximately(s.quality, quality)
                        && s.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate && s.preloadAudioData;
            if (same) return false;
            imp.forceToMono = true;
            imp.loadInBackground = false;
            s.loadType = load;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = quality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = true;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
            return true;
        }
    }
}
