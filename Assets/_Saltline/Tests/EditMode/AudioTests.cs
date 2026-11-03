using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Saltline.Audio;
using Saltline.Combat;
using Saltline.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Saltline.Tests
{
    /// <summary>
    /// The sound bank against the generator's manifest, every sound name the game code can ask
    /// for, import settings, and the map's ambient emitters.
    /// </summary>
    public class AudioTests
    {
        const string SoundDir = "Assets/_Saltline/Audio/Generated";
        const string BankPath = "Assets/_Saltline/Resources/SoundBank.asset";

        [Serializable] class Manifest { public int sampleRate; public Entry[] sounds; }
        [Serializable] class Entry { public string name; public string group; public int variants; public float lufs; }

        static SoundBank Bank()
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            Assert.IsNotNull(bank, $"{BankPath} missing: run Saltline/Build/Setup");
            bank.Invalidate();
            return bank;
        }

        [Test]
        public void BankMatchesTheGeneratorManifest()
        {
            var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(SoundDir + "/manifest.json"));
            var bank = Bank();
            Assert.AreEqual(m.sounds.Length, bank.sounds.Length, "bank and manifest list different numbers of sounds");
            foreach (var e in m.sounds)
            {
                var d = bank.Get(e.name);
                Assert.IsNotNull(d, $"{e.name} is in the manifest but not in the bank");
                Assert.AreEqual(e.variants, d.clips.Length, $"{e.name}: variant count");
                Assert.IsTrue(d.clips.All(c => c != null), $"{e.name}: a clip is missing");
                Assert.AreEqual(e.lufs, d.measuredLufs, 0.05f, $"{e.name}: loudness not taken from the manifest");
            }
        }

        [Test]
        public void VolumesComeFromLoudnessTargets()
        {
            foreach (var d in Bank().sounds)
            {
                // 1.0 would mean the file is quieter than its target; 0.02 that it was clamped (over 34 dB too loud).
                Assert.That(d.volume, Is.GreaterThan(0.02f).And.LessThan(1f), $"{d.name}: volume {d.volume}");
                Assert.That(d.maxDistance, Is.GreaterThan(d.minDistance), $"{d.name}: distances");
                Assert.That(d.maxVoices, Is.GreaterThanOrEqualTo(1), $"{d.name}: voices");
            }
        }

        [Test]
        public void EverySoundTheCodeAsksForExists()
        {
            var bank = Bank();
            var names = new List<string>();
            foreach (WeaponClass c in Enum.GetValues(typeof(WeaponClass)))
            {
                names.Add(GameAudio.ShotSound(c, false));
                names.Add(GameAudio.ShotSound(c, true));
            }
            foreach (SurfaceType s in Enum.GetValues(typeof(SurfaceType)))
            {
                names.Add(GameAudio.ImpactSound(s));
                names.Add(GameAudio.StepSound(s));
            }
            names.AddRange(GameAudio.CueSounds());
            foreach (var label in new[] { "READY", "BACK TO LOBBY NOW", "LEAVE", "HOST", "STAY", "RESUME" })
                names.Add(GameAudio.UiClickSound(label));
            names.AddRange(new[]
            {
                "whizz", "impact_flesh", "hitmarker", "headshot", "kill", "hurt", "death", "dry_fire", "shell_in",
                "land", "jump", "slide_loop", "match_start", "victory", "defeat", "countdown", "ui_hover", "amb_coast", "amb_hum",
            });
            var missing = names.Distinct().Where(n => bank.Get(n) == null).ToList();
            Assert.IsEmpty(missing, "sounds the game plays but the bank lacks: " + string.Join(", ", missing));

            // And the other way: every sound in the bank has a use.
            var used = new HashSet<string>(names);
            var unused = bank.sounds.Select(d => d.name).Where(n => !used.Contains(n)).ToList();
            Assert.IsEmpty(unused, "sounds in the bank that nothing plays: " + string.Join(", ", unused));
        }

        [Test]
        public void ClipsAreImportedForGameUse()
        {
            foreach (var d in Bank().sounds)
            foreach (var clip in d.clips)
            {
                string path = AssetDatabase.GetAssetPath(clip);
                var imp = (AudioImporter)AssetImporter.GetAtPath(path);
                var s = imp.defaultSampleSettings;
                Assert.IsTrue(imp.forceToMono, $"{path}: not mono (positional sounds must be mono)");
                Assert.AreEqual(AudioCompressionFormat.Vorbis, s.compressionFormat, path);
                var load = d.group == SoundGroup.Ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                Assert.AreEqual(load, s.loadType, path);
                Assert.AreEqual(1, clip.channels, path);
                Assert.AreEqual(48000, clip.frequency, path);
            }
        }

        // ------------------------------------------------------------------ master limiter

        const int Rate = 48000;

        /// <summary>Runs a stereo signal through the limiter in 1024-frame blocks, as the audio thread does.</summary>
        static float[] RunLimiter(PeakLimiter lim, float[] input)
        {
            var output = new float[input.Length];
            var block = new float[1024 * 2];
            for (int at = 0; at < input.Length; at += block.Length)
            {
                int n = Mathf.Min(block.Length, input.Length - at);
                if (n < block.Length) block = new float[n];
                System.Array.Copy(input, at, block, 0, n);
                lim.Process(block, 2);
                System.Array.Copy(block, 0, output, at, n);
            }
            return output;
        }

        [Test]
        public void LimiterLeavesQuietAudioUntouched()
        {
            var lim = new PeakLimiter(Rate);
            var rng = new System.Random(7);
            var x = new float[Rate * 2]; // one second
            for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 1.6 - 0.8); // peaks under -1 dBFS
            var y = RunLimiter(lim, x);
            int delay = lim.Lookahead * 2;
            for (int i = delay; i < x.Length; i++)
                Assert.AreEqual(x[i - delay], y[i], 0f, $"sample {i} changed though nothing reached the ceiling");
        }

        [Test]
        public void LimiterHoldsTheCeilingAndRecovers()
        {
            var lim = new PeakLimiter(Rate);
            var rng = new System.Random(11);
            int burstStart = Rate / 5, burstEnd = burstStart + Rate * 3 / 10, total = Rate * 2;
            var x = new float[total * 2];
            for (int f = 0; f < total; f++)
            {
                float v = 0.3f * Mathf.Sin(2f * Mathf.PI * 220f * f / Rate);
                // Held fire: a shot every 80 ms, each up to 2.5x full scale and overlapping the last.
                if (f >= burstStart && f < burstEnd)
                {
                    int since = (f - burstStart) % (Rate * 8 / 100);
                    v += 2.5f * Mathf.Exp(-since / (0.02f * Rate)) * (float)(rng.NextDouble() * 2 - 1);
                }
                x[f * 2] = v;
                x[f * 2 + 1] = -v;
            }
            var y = RunLimiter(lim, x);

            float peak = 0f;
            foreach (var v in y) peak = Mathf.Max(peak, Mathf.Abs(v));
            Assert.That(peak, Is.LessThanOrEqualTo(lim.Ceiling * 1.01f), $"output peak {20f * Mathf.Log10(peak):F2} dBFS");

            // A second after the last shot (about eight release times) the tone comes through unchanged again.
            int delay = lim.Lookahead;
            for (int f = burstEnd + Rate + delay; f < total; f++)
                Assert.AreEqual(x[(f - delay) * 2], y[f * 2], 1e-3f, $"frame {f}: gain had not recovered");
            Assert.AreEqual(1f, lim.Gain, 1e-3f);
        }

        [Test]
        public void KappaHasItsMachineryEmitters()
        {
            const string scene = "Assets/_Saltline/Scenes/Kappa.unity";
            if (!File.Exists(scene)) Assert.Ignore("Kappa scene not built");
            var s = EditorSceneManager.OpenScene(scene, OpenSceneMode.Additive);
            try
            {
                var emitters = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AmbientEmitter>(true)).ToList();
                Assert.AreEqual(3, emitters.Count, "pump house and two pump sheds");
                var bank = Bank();
                foreach (var e in emitters)
                {
                    Assert.IsNotNull(bank.Get(e.sound), $"{e.name}: '{e.sound}' not in the bank");
                    Assert.That(e.maxDistance, Is.GreaterThan(e.minDistance), e.name);
                }
            }
            finally { EditorSceneManager.CloseScene(s, true); }
        }
    }
}
