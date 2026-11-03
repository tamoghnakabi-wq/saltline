using System.Collections.Generic;
using System.Text;
using Saltline.Combat;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Saltline.Player;
using Saltline.Settings;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Saltline.Audio
{
    /// <summary>
    /// Plays every sound in the game from a pool of voices. It listens to the same presentation
    /// events as the effects (shots, impacts, hits, kills, match phases) and watches each
    /// player's movement and weapon actions for footsteps, reloads and bolt or pump cycles.
    /// Nothing here affects the simulation; headless processes never create it.
    /// </summary>
    [DefaultExecutionOrder(320)]
    public sealed class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        const int Voices3D = 40;
        const int Voices2D = 12;
        const float SpeedOfSound = 343f;
        /// <summary>Sounds further than this start late by distance / speed of sound.</summary>
        const float DelayFrom = 25f;
        const float OccludedCutoff = 1400f;
        const float OccludedGain = 0.55f;
        const int ImpactsPerFrame = 4;

        sealed class Voice
        {
            public AudioSource Src;
            public AudioLowPassFilter Lp;
            public SoundDef Def;
            public float Start;
            public float BusyUntil;
            public int Priority;
        }

        readonly List<Voice> m_3D = new();
        readonly List<Voice> m_2D = new();
        AudioSource m_Bed;
        float m_BedTarget;
        SoundBank m_Bank;
        int m_ImpactsFrame = -1, m_ImpactsThisFrame;
        float m_LastHover;
        MatchPhase m_LastPhase = (MatchPhase)255;
        int m_LastCount = -1;
        readonly Dictionary<NetPlayer, Tracker> m_Trackers = new();
        MasterLimiter m_Limiter;
        readonly List<NetPlayer> m_Gone = new();
        static readonly HashSet<string> s_Missing = new();

        // ------------------------------------------------------------------ statistics (tests read these)

        public static class Stats
        {
            public static readonly Dictionary<string, int> Plays = new();
            public static int Stolen, Dropped, Culled, Occluded, Missing, PeakVoices;
            public static void Reset() { Plays.Clear(); Stolen = Dropped = Culled = Occluded = Missing = PeakVoices = 0; }
            public static int Count(string prefix)
            {
                int n = 0;
                foreach (var kv in Plays) if (kv.Key.StartsWith(prefix)) n += kv.Value;
                return n;
            }
            public static string Summary()
            {
                var sb = new StringBuilder();
                int total = 0;
                foreach (var kv in Plays) total += kv.Value;
                sb.Append($"audio: {total} sounds started, peak {PeakVoices} voices, {Stolen} stolen, {Dropped} dropped, {Culled} out of range, {Occluded} muffled, {Missing} missing;");
                var keys = new List<string>(Plays.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                foreach (var k in keys) sb.Append(' ').Append(k).Append('=').Append(Plays[k]);
                return sb.ToString();
            }
        }

        public static void Ensure()
        {
            if (Instance != null || !RunMode.Presentation) return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GameAudio>();
        }

        void Awake()
        {
            Instance = this;
            m_Bank = SoundBank.Instance;
            if (m_Bank == null) Debug.LogError("[SALTLINE] SoundBank missing (Resources/SoundBank); the game will be silent.");
            for (int i = 0; i < Voices3D; i++) m_3D.Add(MakeVoice($"Voice3D_{i}", true));
            for (int i = 0; i < Voices2D; i++) m_2D.Add(MakeVoice($"Voice2D_{i}", false));
            m_Bed = gameObject.AddComponent<AudioSource>();
            m_Bed.loop = true;
            m_Bed.playOnAwake = false;
            m_Bed.spatialBlend = 0f;
            m_Bed.priority = 0;
            m_Bed.volume = 0f;
            if (CommandLine.Has("audiocapture")) gameObject.AddComponent<AudioCapture>();
        }

        Voice MakeVoice(string name, bool spatial)
        {
            // Built inactive: a filter added next to an enabled, clip-less source makes Unity try to play it.
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.spread = 0f;
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.enabled = false;
            go.SetActive(true);
            return new Voice { Src = src, Lp = lp };
        }

        void OnEnable()
        {
            CombatEvents.Shot += OnShot;
            CombatEvents.ImpactSpawned += OnImpact;
            CombatEvents.HitConfirmed += OnHit;
            CombatEvents.LocalDamaged += OnDamaged;
            CombatEvents.Killed += OnKilled;
            NetPlayer.WeaponEvent += OnWeaponEvent;
            NetPlayer.AnyDespawned += OnDespawned;
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameSettings.Changed += ApplyVolumes;
        }

        void OnDisable()
        {
            CombatEvents.Shot -= OnShot;
            CombatEvents.ImpactSpawned -= OnImpact;
            CombatEvents.HitConfirmed -= OnHit;
            CombatEvents.LocalDamaged -= OnDamaged;
            CombatEvents.Killed -= OnKilled;
            NetPlayer.WeaponEvent -= OnWeaponEvent;
            NetPlayer.AnyDespawned -= OnDespawned;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameSettings.Changed -= ApplyVolumes;
        }

        void Start() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit()
        {
            // Test runs print what played, so a silent build cannot pass unnoticed.
            if (CommandLine.Has("autotest") || CommandLine.Has("uitest") || CommandLine.Has("audiocapture"))
            {
                Debug.Log("[SALTLINE][AUTOTEST] " + Stats.Summary());
                Debug.Log("[SALTLINE][AUTOTEST] " + MasterLimiter.Stats.Summary());
            }
        }

        // ------------------------------------------------------------------ volumes

        public static float GroupGain(SoundGroup g) => g switch
        {
            SoundGroup.Interface => GameSettings.InterfaceVolume,
            SoundGroup.Ambience => GameSettings.AmbienceVolume,
            _ => GameSettings.EffectsVolume,
        };

        // The ambience bed follows its slider every frame (UpdateBed); one-shots pick up a change on their next play.
        void ApplyVolumes()
        {
            AudioListener.volume = GameSettings.MasterVolume;
            foreach (var e in AmbientEmitter.All) e.ApplyVolume();
        }

        // ------------------------------------------------------------------ listener

        static Transform s_Listener;
        static int s_ListenerFrame = -1;

        public static Transform Listener
        {
            get
            {
                if (s_ListenerFrame != Time.frameCount)
                {
                    s_ListenerFrame = Time.frameCount;
                    if (s_Listener == null || !s_Listener.gameObject.activeInHierarchy || !s_Listener.GetComponent<AudioListener>().enabled)
                    {
                        s_Listener = null;
                        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                            if (l.enabled && l.gameObject.activeInHierarchy) { s_Listener = l.transform; break; }
                    }
                }
                return s_Listener;
            }
        }

        static Vector3 ListenerPos => Listener != null ? Listener.position : Vector3.zero;

        static bool Occluded(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1.5f) return false;
            // Stop short of the source: it usually stands on or against a collider.
            return Physics.Raycast(from, d / len, len - 0.6f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ playing

        /// <summary>A positioned sound. Out-of-range sounds are not started; walls muffle the rest.</summary>
        public static bool PlayAt(string name, Vector3 pos, float gain = 1f, bool occlude = true, float delay = -1f)
        {
            var self = Instance;
            if (self == null) return false;
            var def = self.Def(name);
            if (def == null) return false;
            Vector3 l = ListenerPos;
            float dist = Vector3.Distance(l, pos);
            if (dist > def.maxDistance) { Stats.Culled++; return false; }
            bool occ = occlude && Occluded(l, pos);
            if (occ) Stats.Occluded++;
            if (delay < 0f) delay = dist > DelayFrom ? dist / SpeedOfSound : 0f;
            return self.Start(def, true, pos, gain * (occ ? OccludedGain : 1f), occ ? OccludedCutoff : 0f, 0f, delay);
        }

        /// <summary>A sound heard as part of the player (own gun, own steps, hit feedback, interface).</summary>
        public static bool Play2D(string name, float gain = 1f, float pan = 0f)
        {
            var self = Instance;
            if (self == null) return false;
            var def = self.Def(name);
            return def != null && self.Start(def, false, Vector3.zero, gain, 0f, pan, 0f);
        }

        SoundDef Def(string name)
        {
            var def = m_Bank != null ? m_Bank.Get(name) : null;
            if (def == null || def.clips == null || def.clips.Length == 0)
            {
                Stats.Missing++;
                if (s_Missing.Add(name)) Debug.LogWarning($"[SALTLINE] Sound '{name}' is not in the sound bank.");
                return null;
            }
            return def;
        }

        bool Start(SoundDef def, bool spatial, Vector3 pos, float gain, float cutoff, float pan, float delay)
        {
            var v = Acquire(def, spatial);
            if (v == null) { Stats.Dropped++; return false; }

            int n = def.clips.Length;
            int pick = n == 1 ? 0 : Random.Range(0, n - 1);
            if (n > 1 && pick >= def.lastVariant && def.lastVariant >= 0) pick++; // never the same variant twice running
            def.lastVariant = pick;
            var clip = def.clips[Mathf.Clamp(pick, 0, n - 1)];
            if (clip == null) return false;

            var s = v.Src;
            s.Stop();
            s.clip = clip;
            s.volume = Mathf.Clamp01(def.volume * GroupGain(def.group) * gain);
            s.pitch = 1f + Random.Range(-def.pitchJitter, def.pitchJitter);
            s.priority = 128 - Mathf.Clamp(def.priority * 16, 0, 120);
            if (spatial)
            {
                s.transform.position = pos;
                s.minDistance = def.minDistance;
                s.maxDistance = def.maxDistance;
            }
            else s.panStereo = pan;
            v.Lp.enabled = cutoff > 0f;
            if (cutoff > 0f) v.Lp.cutoffFrequency = cutoff;
            if (delay > 0.005f) s.PlayDelayed(delay); else s.Play();

            float now = Time.realtimeSinceStartup;
            v.Def = def;
            v.Start = now;
            v.Priority = def.priority;
            v.BusyUntil = now + delay + clip.length / Mathf.Max(0.5f, s.pitch);

            Stats.Plays.TryGetValue(def.name, out int c);
            Stats.Plays[def.name] = c + 1;
            int busy = 0;
            foreach (var x in m_3D) if (x.BusyUntil > now) busy++;
            foreach (var x in m_2D) if (x.BusyUntil > now) busy++;
            if (busy > Stats.PeakVoices) Stats.PeakVoices = busy;
            return true;
        }

        Voice Acquire(SoundDef def, bool spatial)
        {
            var pool = spatial ? m_3D : m_2D;
            float now = Time.realtimeSinceStartup;
            Voice free = null, oldestSame = null, weakest = null;
            int same = 0;
            foreach (var v in pool)
            {
                if (v.BusyUntil <= now) { if (free == null) free = v; continue; }
                if (v.Def == def)
                {
                    same++;
                    if (oldestSame == null || v.Start < oldestSame.Start) oldestSame = v;
                }
                if (weakest == null || v.Priority < weakest.Priority || (v.Priority == weakest.Priority && v.Start < weakest.Start)) weakest = v;
            }
            if (same >= Mathf.Max(1, def.maxVoices)) { Stats.Stolen++; return oldestSame; }
            if (free != null) return free;
            if (weakest != null && weakest.Priority <= def.priority) { Stats.Stolen++; return weakest; }
            return null;
        }

        // ------------------------------------------------------------------ combat events

        static string ShotKey(WeaponClass c) => c switch
        {
            WeaponClass.AssaultRifle => "ar",
            WeaponClass.SMG => "smg",
            WeaponClass.Shotgun => "shotgun",
            WeaponClass.Sniper => "sniper",
            _ => "pistol",
        };

        public static string ShotSound(WeaponClass c, bool far) => far ? $"shot_{ShotKey(c)}_far" : $"shot_{ShotKey(c)}";

        public static string ImpactSound(SurfaceType s) => s switch
        {
            SurfaceType.Metal => "impact_metal",
            SurfaceType.ThinMetal => "impact_thinmetal",
            SurfaceType.Wood => "impact_wood",
            SurfaceType.Glass => "impact_glass",
            SurfaceType.Dirt => "impact_dirt",
            SurfaceType.Water => "impact_water",
            SurfaceType.Fabric => "impact_dirt",
            SurfaceType.Plastic => "impact_thinmetal",
            _ => "impact_concrete",
        };

        public static string StepSound(SurfaceType s) => s switch
        {
            SurfaceType.Metal or SurfaceType.ThinMetal => "step_metal",
            SurfaceType.Wood => "step_wood",
            SurfaceType.Dirt or SurfaceType.Fabric => "step_gravel",
            SurfaceType.Water => "step_water",
            _ => "step_concrete",
        };

        void OnShot(ShotFx fx, bool local)
        {
            var shooter = NetPlayer.Find(fx.Shooter);
            var db = shooter != null ? shooter.Weapons : NetPlayer.All.Count > 0 ? NetPlayer.All[0].Weapons : null;
            var w = db != null ? db.Get(fx.WeaponId) : null;
            if (w == null) return;
            if (local)
            {
                Play2D(ShotSound(w.weaponClass, false));
                return;
            }

            // Near and far recordings cross-fade with distance; both arrive late by the speed of sound.
            Vector3 l = ListenerPos;
            float d = Vector3.Distance(l, fx.Origin);
            float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 55f, d));
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 40f, d));
            if (near > 0.02f) PlayAt(ShotSound(w.weaponClass, false), fx.Origin, near);
            if (far > 0.02f) PlayAt(ShotSound(w.weaponClass, true), fx.Origin, far);
            Whizz(fx, w);
        }

        /// <summary>A bullet passing close to the local player's head, unless a wall stopped it first.</summary>
        static void Whizz(in ShotFx fx, WeaponDefinition w)
        {
            var me = NetPlayer.Local;
            if (me == null || !me.Render.Alive || fx.Shooter == me.OwnerClientId) return;
            Vector3 aim = Quaternion.Euler(-fx.Pitch * InputCmd.PitchScale, fx.Yaw * InputCmd.YawScale, 0f) * Vector3.forward;
            Vector3 head = me.RenderEye;
            float t = Vector3.Dot(head - fx.Origin, aim);
            if (t < 3f || t > w.maxRange) return;
            Vector3 closest = fx.Origin + aim * t;
            float miss = Vector3.Distance(head, closest);
            if (miss > 2.5f || miss < 0.3f) return; // too far to hear, or straight through our head
            // The host says the bullet hit someone; if that was us, it was a hit, not a near miss (hurt plays).
            if ((fx.PelletHits & 1u) != 0 && Saltline.FX.FxManager.ClosestToRay(fx.Origin, aim, fx.Shooter, w.maxRange, out _) == me) return;
            if (ShotResolver.EnvironmentRay(fx.Origin, aim, t, out _)) return;
            PlayAt("whizz", closest, 1f - miss / 3f, occlude: false, delay: 0f);
        }

        void OnImpact(Impact i)
        {
            if (m_ImpactsFrame != Time.frameCount) { m_ImpactsFrame = Time.frameCount; m_ImpactsThisFrame = 0; }
            if (++m_ImpactsThisFrame > ImpactsPerFrame) return; // a shotgun blast is a few impacts, not eight
            PlayAt(i.Body ? "impact_flesh" : ImpactSound(i.Surface), i.Point);
        }

        void OnHit(HitConfirm h)
        {
            if (h.Kill) Play2D("kill");
            else Play2D(h.Headshot ? "headshot" : "hitmarker");
        }

        void OnDamaged(DamageTaken d)
        {
            // Panned toward the attacker so the hit also says where it came from.
            float pan = 0f;
            var l = Listener;
            if (l != null)
            {
                Vector3 to = d.From - l.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) pan = Mathf.Clamp(Vector3.Dot(to.normalized, l.right) * 0.7f, -0.7f, 0.7f);
            }
            Play2D("hurt", Mathf.Lerp(0.6f, 1f, d.Damage / 60f), pan);
        }

        void OnKilled(KillEvent k)
        {
            var v = NetPlayer.Find(k.Victim);
            if (v == null) return;
            if (v == NetPlayer.Local) Play2D("death");
            else PlayAt("death", v.Render.Feet + Vector3.up * 0.9f);
        }

        void OnWeaponEvent(NetPlayer p, WeaponFx fx)
        {
            // Only the local player's own predicted events; everyone else is followed through their render state.
            if (p == null || p != NetPlayer.Local) return;
            if (fx == WeaponFx.DryFire) Play2D("dry_fire");
            else if (fx == WeaponFx.ShellInserted) Play2D("shell_in", 0.9f);
        }

        void OnDespawned(NetPlayer p) => m_Trackers.Remove(p);

        // ------------------------------------------------------------------ per-player movement and weapon actions

        sealed class Tracker
        {
            public bool Alive, WasGrounded = true, EmptyReload;
            public float Stride, AirTime, MinVy, Progress = -1f;
            public Stance Stance;
            public WeaponAction Action;
            public byte WeaponId = 255;
            public int ShellWraps;
        }

        /// <summary>A sound at a point of a weapon action, e.g. the magazine leaving the gun at 12% of a reload.</summary>
        readonly struct Cue
        {
            public readonly WeaponAction Action;
            public readonly float At;
            public readonly string Sound;
            public readonly int Classes;     // bit mask of WeaponClass; 0 = any
            public readonly bool EmptyOnly;  // only on a reload that started with an empty magazine

            public Cue(WeaponAction a, float at, string sound, int classes = 0, bool emptyOnly = false)
            {
                Action = a; At = at; Sound = sound; Classes = classes; EmptyOnly = emptyOnly;
            }
        }

        static int C(params WeaponClass[] cs) { int m = 0; foreach (var c in cs) m |= 1 << (int)c; return m; }

        // Timings follow WeaponRig's animation (magazine, charging handle, slide, bolt and pump).
        static readonly Cue[] s_Cues =
        {
            new(WeaponAction.Reloading, 0.12f, "mag_out"),
            new(WeaponAction.Reloading, 0.66f, "mag_in"),
            new(WeaponAction.Reloading, 0.78f, "charge_back", C(WeaponClass.AssaultRifle, WeaponClass.SMG), true),
            new(WeaponAction.Reloading, 0.84f, "charge_release", C(WeaponClass.AssaultRifle, WeaponClass.SMG), true),
            new(WeaponAction.Reloading, 0.80f, "bolt_back", C(WeaponClass.Sniper), true),
            new(WeaponAction.Reloading, 0.86f, "bolt_fwd", C(WeaponClass.Sniper), true),
            new(WeaponAction.Reloading, 0.86f, "slide_release", C(WeaponClass.Pistol), true),
            new(WeaponAction.Cycling, 0.02f, "pump_back", C(WeaponClass.Shotgun)),
            new(WeaponAction.Cycling, 0.50f, "pump_fwd", C(WeaponClass.Shotgun)),
            new(WeaponAction.Cycling, 0.00f, "bolt_up", C(WeaponClass.Sniper)),
            new(WeaponAction.Cycling, 0.18f, "bolt_back", C(WeaponClass.Sniper)),
            new(WeaponAction.Cycling, 0.48f, "bolt_fwd", C(WeaponClass.Sniper)),
            new(WeaponAction.Cycling, 0.72f, "bolt_down", C(WeaponClass.Sniper)),
            new(WeaponAction.Equipping, 0.00f, "equip"),
        };

        /// <summary>Every sound name the cue table can play (for tests).</summary>
        public static IEnumerable<string> CueSounds()
        {
            foreach (var c in s_Cues) yield return c.Sound;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            EnsureLimiter();
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (!m_Trackers.TryGetValue(p, out var tr)) m_Trackers[p] = tr = new Tracker();
                Track(p, tr, dt);
            }
            m_Gone.Clear();
            foreach (var kv in m_Trackers) if (kv.Key == null) m_Gone.Add(kv.Key);
            foreach (var g in m_Gone) m_Trackers.Remove(g);

            UpdateMatch();
            UpdateBed(dt);
        }

        /// <summary>The limiter follows whichever listener is active (menu camera, player camera).</summary>
        void EnsureLimiter()
        {
            var l = Listener;
            if (l == null || (m_Limiter != null && m_Limiter.gameObject == l.gameObject)) return;
            m_Limiter = l.GetComponent<MasterLimiter>();
            if (m_Limiter == null) m_Limiter = l.gameObject.AddComponent<MasterLimiter>();
        }

        void Track(NetPlayer p, Tracker tr, float dt)
        {
            var r = p.Render;
            bool own = p == NetPlayer.Local;
            if (!r.Alive)
            {
                tr.Alive = false;
                return;
            }
            if (!tr.Alive)
            {
                // Spawned or respawned: start clean, without a landing or an equip sound.
                tr.Alive = true;
                tr.WasGrounded = r.Grounded;
                tr.Stance = r.Stance;
                tr.AirTime = tr.MinVy = 0f;
                tr.Stride = 0.6f;
                tr.WeaponId = own ? p.SimWeapon.ActiveId : r.WeaponId;
                tr.Action = own ? p.SimWeapon.Action : r.Action;
                tr.Progress = 2f;
                return;
            }

            // ---- feet
            Vector3 hv = r.Velocity;
            hv.y = 0f;
            float speed = hv.magnitude;
            if (r.Grounded)
            {
                if (!tr.WasGrounded && tr.AirTime > 0.25f)
                    Body(p, own, "land", Mathf.InverseLerp(2f, 9f, -tr.MinVy) * 0.7f + 0.3f);
                if (r.Stance == Stance.Sliding)
                {
                    if (tr.Stance != Stance.Sliding) Body(p, own, "slide_loop", 1f);
                    tr.Stride = 0f;
                }
                else if (speed > 0.8f)
                {
                    float stride = 0.75f + 0.2f * speed; // about 2, 3 and 3.3 steps a second crouched, running, sprinting
                    tr.Stride += speed * dt;
                    if (tr.Stride >= stride)
                    {
                        tr.Stride -= stride;
                        float gain = r.Stance == Stance.Crouching ? 0.35f : Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(2.7f, 7.6f, speed));
                        Body(p, own, StepSound(SurfaceUnder(r.Feet)), gain);
                    }
                }
                else tr.Stride = 0.6f; // the first step after standing still comes quickly
                tr.AirTime = 0f;
                tr.MinVy = 0f;
            }
            else
            {
                if (tr.WasGrounded && r.Velocity.y > 2f) Body(p, own, "jump", 1f);
                tr.AirTime += dt;
                tr.MinVy = Mathf.Min(tr.MinVy, r.Velocity.y);
            }
            tr.WasGrounded = r.Grounded;
            tr.Stance = r.Stance;

            // ---- weapon actions (the local player's from its predicted state, others' from what is drawn)
            byte id = own ? p.SimWeapon.ActiveId : r.WeaponId;
            WeaponAction a = own ? p.SimWeapon.Action : r.Action;
            float pr = own ? p.SimWeapon.ActionProgress : r.ActionProgress;
            var w = p.Weapons != null ? p.Weapons.Get(id) : null;
            if (id != tr.WeaponId) { tr.WeaponId = id; tr.Action = WeaponAction.Ready; }
            if (a != tr.Action)
            {
                // A remote shell reload that ran to its end inserted one last shell.
                if (!own && tr.Action == WeaponAction.ShellReloading && tr.ShellWraps >= 1 && tr.Progress > 0.75f)
                    Mech(p, own, "shell_in");
                tr.Action = a;
                tr.Progress = -1f;
                tr.ShellWraps = 0;
                tr.EmptyReload = own && a == WeaponAction.Reloading && p.SimWeapon.Mag == 0;
            }
            if (w == null) { tr.Progress = pr; return; }
            if (a == WeaponAction.ShellReloading && !own && tr.Progress >= 0f && pr + 0.4f < tr.Progress)
            {
                // The progress restarts for each shell; the first restart ends the lead-in.
                if (++tr.ShellWraps >= 2) Mech(p, own, "shell_in");
            }
            int bit = 1 << (int)w.weaponClass;
            foreach (var c in s_Cues)
            {
                if (c.Action != a || (c.Classes != 0 && (c.Classes & bit) == 0)) continue;
                if (c.EmptyOnly && !tr.EmptyReload) continue;
                if (tr.Progress < c.At && pr >= c.At) Mech(p, own, c.Sound);
            }
            tr.Progress = pr;
        }

        static SurfaceType SurfaceUnder(Vector3 feet)
        {
            return Physics.Raycast(feet + Vector3.up * 0.4f, Vector3.down, out var hit, 1.0f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                ? SurfaceTag.TypeOf(hit.collider)
                : SurfaceType.Concrete;
        }

        /// <summary>Feet and body sounds: quieter and unpositioned for the local player, at the feet for others.</summary>
        static void Body(NetPlayer p, bool own, string sound, float gain)
        {
            if (own) Play2D(sound, gain * 0.5f);
            else PlayAt(sound, p.Render.Feet + Vector3.up * 0.1f, gain, delay: 0f);
        }

        static void Mech(NetPlayer p, bool own, string sound)
        {
            if (own) Play2D(sound, 0.9f);
            else PlayAt(sound, p.Render.Feet + Vector3.up * 1.3f, 1f, delay: 0f);
        }

        // ------------------------------------------------------------------ match

        void UpdateMatch()
        {
            var md = MatchDirector.Instance;
            if (md == null || !md.IsSpawned) { m_LastPhase = (MatchPhase)255; return; }
            var ph = md.Phase.Value;
            if (ph != m_LastPhase)
            {
                if (ph == MatchPhase.Live && m_LastPhase == MatchPhase.Warmup) Play2D("match_start");
                if (ph == MatchPhase.Results && m_LastPhase == MatchPhase.Live)
                {
                    var me = NetPlayer.Local;
                    var mine = me != null ? me.TeamState.Value : Team.None;
                    // A draw has no winner; it gets the defeat sting.
                    Play2D(md.Winner.Value != Team.None && md.Winner.Value == mine ? "victory" : "defeat");
                }
                m_LastPhase = ph;
            }
            if (ph == MatchPhase.Warmup)
            {
                int s = Mathf.CeilToInt(md.SecondsLeft);
                if (s != m_LastCount && s >= 1 && s <= 3) Play2D("countdown");
                m_LastCount = s;
            }
            else m_LastCount = -1;
        }

        // ------------------------------------------------------------------ ambience

        const string BedName = "amb_coast";

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_Trackers.Clear();
            // The coast is behind every map and, quieter, behind the menu; the boot scene is silent.
            string n = scene.name;
            m_BedTarget = n == "Boot" ? 0f : n == "Menu" ? 0.5f : 1f;
            var def = m_Bank != null ? m_Bank.Get(BedName) : null;
            if (def != null && def.clips.Length > 0 && m_Bed.clip != def.clips[0])
            {
                m_Bed.clip = def.clips[0];
                m_Bed.Play();
            }
        }

        void UpdateBed(float dt)
        {
            if (m_Bed == null || m_Bed.clip == null) return;
            var def = m_Bank.Get(BedName);
            float target = def != null ? def.volume * GroupGain(SoundGroup.Ambience) * m_BedTarget : 0f;
            m_Bed.volume = Mathf.MoveTowards(m_Bed.volume, target, dt * 0.5f);
        }

        // ------------------------------------------------------------------ interface

        /// <summary>Hover and click sounds for every button under <paramref name="root"/>.</summary>
        public static void HookUi(VisualElement root)
        {
            root.RegisterCallback<PointerEnterEvent>(e =>
            {
                var b = ButtonOf(e.target as VisualElement);
                if (b == null || !b.enabledInHierarchy || Instance == null) return;
                if (Time.unscaledTime - Instance.m_LastHover < 0.05f) return;
                Instance.m_LastHover = Time.unscaledTime;
                Play2D("ui_hover");
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(e =>
            {
                var b = ButtonOf(e.target as VisualElement);
                if (b == null || !b.enabledInHierarchy) return;
                Play2D(UiClickSound(b.text));
            }, TrickleDown.TrickleDown);
        }

        static Button ButtonOf(VisualElement e)
        {
            for (int i = 0; e != null && i < 4; i++, e = e.parent)
                if (e is Button b) return b;
            return null;
        }

        public static string UiClickSound(string label)
        {
            string t = (label ?? "").Trim().ToUpperInvariant();
            if (t == "READY") return "ui_ready";
            if (t.StartsWith("BACK") || t.StartsWith("LEAVE") || t == "CANCEL" || t == "STAY" || t == "CLOSE" || t == "RESUME") return "ui_back";
            return "ui_click";
        }
    }
}
