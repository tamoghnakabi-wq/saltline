using System;
using System.Collections.Generic;
using Saltline.Combat;
using Saltline.Core;
using Saltline.Match;
using Saltline.Net;
using Saltline.Sim;
using Saltline.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Player
{
    /// <summary>
    /// One networked player. Roles:
    ///  - Owner (any machine): samples input at a fixed 60 Hz, predicts movement and weapons,
    ///    sends inputs with redundancy, and reconciles against the host's acknowledged state by
    ///    replaying unacknowledged inputs.
    ///  - Host: consumes each remote player's inputs in order through a rate-limited queue,
    ///    runs the same simulation, resolves shots against lag-compensated hitboxes, and sends
    ///    snapshots. The host's own player is simulated directly (no network delay).
    ///  - Other clients: draw the player by interpolating snapshots slightly in the past.
    /// Movement and weapon logic live in PlayerMotor / WeaponSim so every role runs identical code.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-50)]
    public sealed class NetPlayer : NetworkBehaviour
    {
        public static readonly List<NetPlayer> All = new();
        public static NetPlayer Local { get; private set; }
        public static event Action<NetPlayer> LocalSpawned;
        public static event Action<NetPlayer> AnySpawned;
        public static event Action<NetPlayer> AnyDespawned;
        /// <summary>Host-side kill hook for match scoring.</summary>
        public static event Action<KillEvent, NetPlayer, NetPlayer> ServerKilled;
        /// <summary>Host decides whether a dead player may respawn yet (match phases).</summary>
        public static Func<NetPlayer, bool> CanRespawn = _ => true;
        /// <summary>Match phases that freeze players (loading, results). Checked on owner and host alike.</summary>
        public static Func<bool> InputLocked = () => false;
        /// <summary>Host: whether damage is applied at all (off on the results screen).</summary>
        public static Func<bool> DamageEnabled = () => true;

        [SerializeField] GameConfig config;

        public readonly NetworkVariable<byte> Health = new(100);
        public readonly NetworkVariable<bool> AliveState = new(false);
        public readonly NetworkVariable<Team> TeamState = new(Team.None);
        public readonly NetworkVariable<bool> Protected = new(false);
        public readonly NetworkVariable<byte> PrimaryChoice = new(0);
        public readonly NetworkVariable<float> RespawnAt = new(0f);

        public GameConfig Config => config;
        public MotorSettings MotorConfig => config.motor;
        public WeaponDatabase Weapons => config.weapons;
        public IPlayerInputProvider InputProvider { get; set; }
        public bool ThirdPerson { get; set; }

        public MotorState SimMotor => m_Motor;
        public WeaponState SimWeapon => m_Weapon;
        public float SimAimYaw => m_AimYaw;
        public float SimAimPitch => m_AimPitch;
        public RenderPose Render { get; private set; }
        public Vector3 RenderEye => Render.Feet + Vector3.up * EyeHeightFor(Render.Height);
        public bool IsLocalHuman => IsOwner && InputProvider is LocalPlayerController;
        /// <summary>The camera rig viewing through this player on this machine, if any.</summary>
        public PlayerCameraRig LocalRig { get; internal set; }
        public WeaponDefinition ActiveWeapon => Weapons != null ? Weapons.Get((IsOwner || IsServer) ? m_Weapon.ActiveId : Render.WeaponId) : null;
        public string DisplayName => GameSession.Instance != null && GameSession.Instance.TryGet(OwnerClientId, out var r) ? r.Name.ToString() : $"Player {OwnerClientId}";

        // ---- Diagnostics
        public int LocalTick => m_LocalTick;
        public int LastAck => m_LastAck;
        public int Reconciliations { get; private set; }
        public float LastCorrectionMeters { get; private set; }
        public int ShotsPredicted { get; private set; }
        public int HitsConfirmed { get; private set; }
        public int KillsConfirmed { get; private set; }
        public int ServerInputsProcessed { get; private set; }
        public int ServerInputsSkipped { get; private set; }
        public int ServerInputsDropped { get; private set; }
        /// <summary>Host: extra inputs simulated to shrink a queue that stayed too deep.</summary>
        public int ServerInputsCaughtUp { get; private set; }
        /// <summary>Host: inputs from this player waiting to be simulated.</summary>
        public int ServerQueueDepth => CountPending();
        public int ServerShotsResolved { get; private set; }
        public int ServerPelletsHit { get; private set; }
        public int QueuedInputs => CountPending();

        CharacterController m_Cc;
        MotorState m_Motor;
        WeaponState m_Weapon;
        float m_AimYaw, m_AimPitch;
        float m_SpawnYaw;
        byte m_SpawnSerial;

        // ---- Owner prediction
        const int Ring = 256;
        readonly InputCmd[] m_Inputs = new InputCmd[Ring];
        readonly MotorState[] m_PredMotor = new MotorState[Ring];
        readonly WeaponState[] m_PredWeapon = new WeaponState[Ring];
        int m_LocalTick;
        double m_Accumulator;
        MotorState m_PrevMotor;
        Vector3 m_ErrorOffset;
        int m_LastAck = int.MinValue;
        int m_LastOwnerSnapshotTick = int.MinValue;

        // ---- Host input queue
        const int PendingSize = 64;
        const float MaxBudgetBank = 6f;
        const int MaxQueued = 10;
        // A queue only needs to cover jitter. Input arrives at one per tick and is used at one per
        // tick, so a backlog left by a network stall never drains by itself, and every shot in it
        // waits that much longer before the host rewinds for it. When the queue's lowest depth over
        // half a second stays above TargetQueue, the host simulates one extra input now and then,
        // at most CatchUpPerSecond a second: a client sending faster than real time gains at most 10%.
        const int TargetQueue = 2, QueueWindowTicks = 30;
        const float CatchUpPerSecond = 6f;
        int m_QueueWindowMin = int.MaxValue, m_QueueWindowTicks, m_QueueExcess;
        float m_CatchUpBudget;
        readonly InputCmd[] m_Pending = new InputCmd[PendingSize];
        readonly bool[] m_PendingValid = new bool[PendingSize];
        int m_LastProcessed = int.MinValue;
        float m_Budget = 2f;
        int m_StallTicks;

        // ---- Host combat bookkeeping
        float m_LastDamageTime = -100f;
        float m_RegenAccum;
        float m_ProtectUntil;
        // Host match rules (custom-match overrides: -respawndelay, -spawnprotect).
        float m_RespawnDelay, m_SpawnProtection;
        bool m_PendingRespawn;
        readonly List<(ulong attacker, int damage, float time)> m_Contributions = new();

        // ---- Remote interpolation (non-owner clients)
        const int SnapRing = 32;
        readonly RemoteSnapshot[] m_Snaps = new RemoteSnapshot[SnapRing];
        int m_SnapCount;
        byte m_RemoteSerial = 255;

        static NetworkTickSystem s_TickSystem;
        static readonly List<NetPlayer> s_Iter = new();
        static readonly List<ShotTarget> s_Targets = new();
        static readonly Dictionary<ulong, (float damage, bool head, bool pen)> s_ShotDamage = new();

        float Dt => NetClock.TickInterval;
        float ServerNow => (float)NetworkManager.ServerTime.Time;

        public float EyeHeightFor(float capsuleHeight)
        {
            var m = config.motor;
            float t = Mathf.InverseLerp(m.slideHeight, m.standHeight, capsuleHeight);
            return Mathf.Lerp(m.slideEye, m.standEye, t);
        }

        // ================================================================== lifecycle

        void Awake()
        {
            m_Cc = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            Layers.ConfigureCollisionMatrix();
            gameObject.layer = Layers.Player;
            All.Add(this);
            m_Cc.enabled = IsServer || IsOwner;
            m_Motor = MotorState.Spawned(transform.position, config.motor);
            m_PrevMotor = m_Motor;
            NetClock.MinTicks = config.minInterpolationTicks;
            NetClock.MaxTicks = config.maxInterpolationTicks;
            NetClock.SnapshotEveryTicks = Mathf.Max(1, config.snapshotEveryTicks);

            if (IsServer)
            {
                EnsureServerLoop(NetworkManager);
                LagCompensation.Server.MaxRewindSeconds = CommandLine.GetFloat("lagcomp", config.maxRewindSeconds);
                m_RespawnDelay = Mathf.Max(0.5f, CommandLine.GetFloat("respawndelay", config.respawnDelay));
                m_SpawnProtection = Mathf.Max(0f, CommandLine.GetFloat("spawnprotect", config.spawnProtection));
            }
            UpdateRender();
            if (IsOwner)
            {
                Local = this;
                LocalSpawned?.Invoke(this);
            }
            AnySpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (IsServer) LagCompensation.Server.Remove(OwnerClientId);
            AnyDespawned?.Invoke(this);
        }

        static void EnsureServerLoop(NetworkManager nm)
        {
            var ts = nm.NetworkTickSystem;
            if (ts == null || s_TickSystem == ts) return;
            if (s_TickSystem != null) s_TickSystem.Tick -= ServerTickAll;
            s_TickSystem = ts;
            ts.Tick += ServerTickAll;
        }

        static void ServerTickAll()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            int tick = nm.ServerTime.Tick;
            s_Iter.Clear();
            s_Iter.AddRange(All);
            foreach (var p in s_Iter) if (p != null && p.IsSpawned) p.ServerTick();
            foreach (var p in s_Iter) if (p != null && p.IsSpawned) p.ServerRecord(tick);
            int every = s_Iter.Count > 0 && s_Iter[0] != null ? Mathf.Max(1, s_Iter[0].config.snapshotEveryTicks) : 2;
            if (tick % every == 0)
                foreach (var p in s_Iter) if (p != null && p.IsSpawned) p.ServerSendSnapshots(tick);
        }

        // ================================================================== frame update

        void Update()
        {
            if (!IsSpawned) return;
            NetClock.Update();
            if (IsOwner) OwnerUpdate();
            UpdateRender();
        }

        void OwnerUpdate()
        {
            float dt = Dt;
            m_Accumulator += Time.deltaTime;
            int steps = 0;
            while (m_Accumulator >= dt)
            {
                m_Accumulator -= dt;
                if (++steps > 8) { m_Accumulator = 0; break; } // hitch: drop time rather than spiral
                OwnerTick();
            }
            // Visual correction offset decays smoothly after a reconciliation.
            if (m_ErrorOffset.sqrMagnitude > 9f) m_ErrorOffset = Vector3.zero;
            m_ErrorOffset = Vector3.Lerp(m_ErrorOffset, Vector3.zero, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        void OwnerTick()
        {
            var intent = new PlayerIntent { AimYaw = m_AimYaw, AimPitch = m_AimPitch };
            InputProvider?.Sample(this, ref intent);

            var cmd = new InputCmd
            {
                Tick = ++m_LocalTick,
                MoveX = InputCmd.QuantizeAxis(intent.Move.x),
                MoveY = InputCmd.QuantizeAxis(intent.Move.y),
                Yaw = InputCmd.QuantizeYaw(intent.AimYaw),
                Pitch = InputCmd.QuantizePitch(intent.AimPitch),
                Buttons = intent.Buttons | (ThirdPerson ? InputButtons.ThirdPerson : InputButtons.None),
            };
            double rt = NetClock.RemoteRenderTick();
            int vt = (int)Math.Floor(rt);
            cmd.ViewTick = vt;
            cmd.ViewTickFraction = (byte)Mathf.Clamp((int)((rt - vt) * 256.0), 0, 255);

            int slot = m_LocalTick & (Ring - 1);
            m_Inputs[slot] = cmd;
            m_PrevMotor = m_Motor;
            if (AliveState.Value && SimStep(cmd, false, out var shot)) OwnerShot(cmd, shot);
            m_PredMotor[slot] = m_Motor;
            m_PredWeapon[slot] = m_Weapon;

            if (IsServer) m_LastProcessed = cmd.Tick;
            else SendInputs();
        }

        void SendInputs()
        {
            var b = new InputBundle();
            int count = Mathf.Min(InputBundle.Capacity, m_LocalTick);
            b.Count = (byte)count;
            for (int i = 0; i < count; i++)
                b.Set(i, m_Inputs[(m_LocalTick - (count - 1 - i)) & (Ring - 1)]);
            InputRpc(b);
        }

        // ================================================================== shared simulation

        /// <summary>One deterministic tick of movement + weapon. Identical on owner and host.</summary>
        bool SimStep(in InputCmd input, bool replay, out ShotEvent shot)
        {
            var db = config.weapons;
            var ms = config.motor;
            var def = db.Get(m_Weapon.ActiveId);
            InputCmd cmd = input;
            if (InputLocked())
            {
                // Frozen by the match (results screen): looking around is allowed, nothing else.
                cmd.MoveX = 0;
                cmd.MoveY = 0;
                cmd.Buttons &= InputButtons.ThirdPerson;
            }
            if (cmd.Has(InputButtons.Fire)) cmd.Buttons &= ~InputButtons.Sprint; // firing cancels sprint

            var p = PlayerMotor.StepParams.Default;
            if (def != null)
            {
                p.WeaponMoveMultiplier = def.moveSpeedMultiplier;
                p.AimMoveMultiplier = def.adsMoveMultiplier;
                p.CanSprint = !(def.blockSprintWhileReloading && m_Weapon.Action == WeaponAction.Reloading);
            }
            PlayerMotor.Step(ref m_Motor, cmd, Dt, m_Cc, ms, p);
            m_AimYaw = cmd.YawDegrees;
            m_AimPitch = cmd.PitchDegrees;

            bool fired = WeaponSim.Step(ref m_Weapon, cmd, m_Motor, Dt, db, ms, out shot, out var fx);
            if (!replay && fx != WeaponFx.None) WeaponEvent?.Invoke(this, fx);
            return fired;
        }

        /// <summary>Reload/equip/dry-fire cues for presentation (not raised during replays).</summary>
        public static event Action<NetPlayer, WeaponFx> WeaponEvent;

        void OwnerShot(in InputCmd cmd, in ShotEvent shot)
        {
            ShotsPredicted++;
            var def = config.weapons.Get(shot.WeaponId);
            if (def != null) InputProvider?.OnRecoil(shot.RecoilKick, def);
            var fx = new ShotFx
            {
                Shooter = OwnerClientId,
                Origin = PlayerMotor.EyePosition(m_Motor, config.motor),
                Yaw = cmd.Yaw,
                Pitch = cmd.Pitch,
                Spread = shot.SpreadDegrees,
                ShotNumber = shot.ShotNumber,
                WeaponId = shot.WeaponId,
            };
            CombatEvents.RaiseShot(fx, true);
            if (IsServer) ServerResolveShot(cmd, shot, fx);
        }

        // ================================================================== owner reconciliation

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        void OwnerStateRpc(OwnerSnapshot s)
        {
            if (IsServer) return;
            if (s.ServerTick <= m_LastOwnerSnapshotTick) return; // stale / reordered
            m_LastOwnerSnapshotTick = s.ServerTick;
            Reconcile(s);
        }

        void Reconcile(in OwnerSnapshot s)
        {
            Vector3 before = PredictedFeet();
            bool respawned = s.SpawnSerial != m_SpawnSerial;
            if (respawned)
            {
                m_SpawnSerial = s.SpawnSerial;
                m_ErrorOffset = Vector3.zero;
                m_AimYaw = s.SpawnYaw * InputCmd.YawScale;
                m_AimPitch = 0f;
                InputProvider?.OnRespawned(m_AimYaw);
            }

            if (s.AckTick == int.MinValue)
            {
                // Host has not simulated any of our input yet: just adopt its state.
                m_Motor = s.Motor;
                m_Weapon = s.Weapon;
                m_PrevMotor = m_Motor;
                return;
            }

            int ack = Math.Min(s.AckTick, m_LocalTick);
            m_LastAck = ack;
            int slot = ack & (Ring - 1);
            bool known = m_Inputs[slot].Tick == ack && m_LocalTick - ack < Ring - 8;
            bool matches = known && MotorState.NearlyEqual(m_PredMotor[slot], s.Motor) && WeaponState.NearlyEqual(m_PredWeapon[slot], s.Weapon);
            if (matches && !respawned) return;

            m_Motor = s.Motor;
            m_Weapon = s.Weapon;
            m_PrevMotor = m_Motor;
            for (int t = ack + 1; t <= m_LocalTick; t++)
            {
                int sl = t & (Ring - 1);
                if (m_Inputs[sl].Tick != t) continue;
                if (t == m_LocalTick) m_PrevMotor = m_Motor;
                if (s.Alive) SimStep(m_Inputs[sl], true, out _);
                m_PredMotor[sl] = m_Motor;
                m_PredWeapon[sl] = m_Weapon;
            }

            Vector3 after = PredictedFeet();
            LastCorrectionMeters = Vector3.Distance(before, after);
            if (!respawned) m_ErrorOffset += before - after;
            Reconciliations++;
        }

        Vector3 PredictedFeet()
        {
            float a = Mathf.Clamp01((float)(m_Accumulator / Dt));
            return Vector3.Lerp(m_PrevMotor.Position, m_Motor.Position, a);
        }

        // ================================================================== host: input queue

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void InputRpc(InputBundle b)
        {
            int n = Mathf.Min(b.Count, InputBundle.Capacity);
            for (int i = 0; i < n; i++) ServerEnqueue(b.Get(i));
        }

        void ServerEnqueue(InputCmd cmd)
        {
            if (m_LastProcessed == int.MinValue) m_LastProcessed = cmd.Tick - 1;
            if (cmd.Tick <= m_LastProcessed) return; // already simulated (redundant copy)
            if (cmd.Tick > m_LastProcessed + PendingSize - 1)
            {
                // Client jumped far ahead (long stall or reset): resynchronize.
                Array.Clear(m_PendingValid, 0, PendingSize);
                ServerInputsSkipped += cmd.Tick - m_LastProcessed - 1;
                m_LastProcessed = cmd.Tick - 1;
            }
            int s = Mod(cmd.Tick);
            m_Pending[s] = cmd;
            m_PendingValid[s] = true;
        }

        static int Mod(int t) => ((t % PendingSize) + PendingSize) % PendingSize;

        int CountPending()
        {
            int n = 0;
            for (int i = 0; i < PendingSize; i++) if (m_PendingValid[i]) n++;
            return n;
        }

        int EarliestPending()
        {
            int best = int.MaxValue;
            for (int i = 0; i < PendingSize; i++)
                if (m_PendingValid[i] && m_Pending[i].Tick > m_LastProcessed && m_Pending[i].Tick < best) best = m_Pending[i].Tick;
            return best == int.MaxValue ? -1 : best;
        }

        void ServerTick()
        {
            if (!IsOwner) ServerConsumeInputs();

            float now = ServerNow;
            if (AliveState.Value)
            {
                if (Protected.Value && now >= m_ProtectUntil) Protected.Value = false;
                if (config.regenDelay > 0f && Health.Value < config.maxHealth && now - m_LastDamageTime >= config.regenDelay)
                {
                    m_RegenAccum += config.regenPerSecond * Dt;
                    int add = Mathf.FloorToInt(m_RegenAccum);
                    if (add > 0)
                    {
                        m_RegenAccum -= add;
                        Health.Value = (byte)Mathf.Min(config.maxHealth, Health.Value + add);
                    }
                }
                if (m_Motor.Position.y < config.killPlaneY) ServerKill(this, 255, false, false);
            }
            else if (m_PendingRespawn && now >= RespawnAt.Value && CanRespawn(this))
            {
                ServerRespawn();
            }
        }

        void ServerConsumeInputs()
        {
            // Budget: one input per tick on average, with a small bank to absorb network jitter.
            // A client that sends inputs faster than real time cannot move faster than real time.
            m_Budget = Mathf.Min(m_Budget + 1f, MaxBudgetBank);
            while (m_Budget >= 1f)
            {
                int next = m_LastProcessed + 1;
                int s = Mod(next);
                if (m_LastProcessed != int.MinValue && m_PendingValid[s] && m_Pending[s].Tick == next)
                {
                    m_PendingValid[s] = false;
                    ServerProcess(m_Pending[s]);
                    m_LastProcessed = next;
                    m_Budget -= 1f;
                    m_StallTicks = 0;
                    continue;
                }
                int earliest = EarliestPending();
                if (earliest < 0) break; // starved: wait for the client
                if (++m_StallTicks >= 3)
                {
                    // The missing inputs were lost beyond what redundancy covers: skip them.
                    ServerInputsSkipped += earliest - next;
                    m_LastProcessed = earliest - 1;
                    m_StallTicks = 0;
                    continue;
                }
                break;
            }

            // Too much queued input means added latency; drop the oldest to catch up.
            int queued = CountPending();
            if (queued > MaxQueued)
            {
                int drop = queued - MaxQueued / 2;
                for (int i = 0; i < drop; i++)
                {
                    int e = EarliestPending();
                    if (e < 0) break;
                    m_PendingValid[Mod(e)] = false;
                    m_LastProcessed = e;
                    ServerInputsDropped++;
                }
                queued = CountPending();
            }

            // Shrink a queue that stays deeper than jitter needs (see TargetQueue).
            m_CatchUpBudget = Mathf.Min(m_CatchUpBudget + CatchUpPerSecond * Dt, 1f);
            if (queued < m_QueueWindowMin) m_QueueWindowMin = queued;
            if (++m_QueueWindowTicks >= QueueWindowTicks)
            {
                m_QueueExcess = Mathf.Max(0, m_QueueWindowMin - TargetQueue);
                m_QueueWindowMin = int.MaxValue;
                m_QueueWindowTicks = 0;
            }
            if (m_QueueExcess > 0 && m_CatchUpBudget >= 1f)
            {
                int next = m_LastProcessed + 1;
                int s = Mod(next);
                if (m_PendingValid[s] && m_Pending[s].Tick == next)
                {
                    m_PendingValid[s] = false;
                    ServerProcess(m_Pending[s]);
                    m_LastProcessed = next;
                    m_CatchUpBudget -= 1f;
                    m_QueueExcess--;
                    ServerInputsCaughtUp++;
                }
            }
        }

        void ServerProcess(in InputCmd cmd)
        {
            ServerInputsProcessed++;
            if (!AliveState.Value) return;
            if (SimStep(cmd, false, out var shot))
            {
                var fx = new ShotFx
                {
                    Shooter = OwnerClientId,
                    Origin = PlayerMotor.EyePosition(m_Motor, config.motor),
                    Yaw = cmd.Yaw,
                    Pitch = cmd.Pitch,
                    Spread = shot.SpreadDegrees,
                    ShotNumber = shot.ShotNumber,
                    WeaponId = shot.WeaponId,
                };
                ServerResolveShot(cmd, shot, fx);
            }
        }

        void ServerRecord(int tick)
        {
            LagCompensation.Server.Record(OwnerClientId, tick, HitboxPose.From(m_Motor, m_AimYaw, m_AimPitch), AliveState.Value);
        }

        void ServerSendSnapshots(int tick)
        {
            var rs = new RemoteSnapshot
            {
                ServerTick = tick,
                Position = m_Motor.Position,
                Velocity = m_Motor.Velocity,
                Height = m_Motor.Height,
                Yaw = InputCmd.QuantizeYaw(m_AimYaw),
                Pitch = InputCmd.QuantizePitch(m_AimPitch),
                Stance = m_Motor.Stance,
                Flags = m_Motor.Flags,
                WeaponId = m_Weapon.ActiveId,
                Action = m_Weapon.Action,
                ActionProgress = (byte)Mathf.RoundToInt(m_Weapon.ActionProgress * 255f),
                Ads = (byte)Mathf.RoundToInt(m_Weapon.Ads * 255f),
                SpawnSerial = m_SpawnSerial,
                Alive = AliveState.Value,
            };
            RemoteStateRpc(rs);

            if (!IsOwner)
            {
                OwnerStateRpc(new OwnerSnapshot
                {
                    AckTick = m_LastProcessed,
                    ServerTick = tick,
                    SpawnSerial = m_SpawnSerial,
                    Alive = AliveState.Value,
                    SpawnYaw = InputCmd.QuantizeYaw(m_SpawnYaw),
                    Motor = m_Motor,
                    Weapon = m_Weapon,
                });
            }
        }

        // ================================================================== host: combat

        void ServerResolveShot(in InputCmd cmd, in ShotEvent shot, in ShotFx fx)
        {
            var def = config.weapons.Get(shot.WeaponId);
            if (def == null) return;
            ServerShotsResolved++;
            if (Protected.Value) Protected.Value = false; // shooting ends spawn protection

            Vector3 eye = PlayerMotor.EyePosition(m_Motor, config.motor);
            Vector3 aim = cmd.AimDirection;
            int currentTick = NetworkManager.ServerTime.Tick;
            double viewTick = cmd.ViewTick + cmd.ViewTickFraction / 256.0;

            s_Targets.Clear();
            foreach (var p in All)
            {
                if (p == this || p == null || !p.IsSpawned || !p.AliveState.Value) continue;
                if (TeamState.Value != Team.None && p.TeamState.Value == TeamState.Value) continue; // no friendly fire
                if (!LagCompensation.Server.TryGetPose(p.OwnerClientId, viewTick, currentTick, NetClock.TickRate, out var pose, out bool wasAlive) || !wasAlive)
                    continue;
                s_Targets.Add(new ShotTarget { ClientId = p.OwnerClientId, Pose = pose });
            }

            s_ShotDamage.Clear();
            int pellets = Mathf.Max(1, def.pellets);
            uint pelletHits = 0;
            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = WeaponSim.PelletDirection(aim, shot.SpreadDegrees, OwnerClientId, shot.ShotNumber, i);
                if (!ShotResolver.TracePellet(eye, dir, def, config.motor, s_Targets, out var hit, null, out _)) continue;
                ServerPelletsHit++;
                if (i < 32) pelletHits |= 1u << i;
                float dmg = DamageModel.PelletDamage(def, hit.Region, hit.Distance, hit.PenetrationFactor);
                s_ShotDamage.TryGetValue(hit.Victim, out var acc);
                s_ShotDamage[hit.Victim] = (acc.damage + dmg, acc.head || hit.Region == HitRegion.Head, acc.pen || hit.PenetrationFactor < 0.999f);
            }

            foreach (var kv in s_ShotDamage)
            {
                var victim = Find(kv.Key);
                if (victim != null) victim.ServerApplyDamage(this, DamageModel.ToHealth(kv.Value.damage), def.id, kv.Value.head, kv.Value.pen, eye);
            }

            // Everyone else draws the shot from the host's answer, not from where they see the target now.
            var sent = fx;
            sent.PelletHits = pelletHits;
            ShotFxRpc(sent);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void ShotFxRpc(ShotFx fx) => CombatEvents.RaiseShot(fx, false);

        public void ServerApplyDamage(NetPlayer attacker, int amount, byte weaponId, bool headshot, bool penetrated, Vector3 from)
        {
            if (!IsServer || !AliveState.Value || Protected.Value || amount <= 0 || !DamageEnabled()) return;
            float now = ServerNow;
            m_LastDamageTime = now;
            m_RegenAccum = 0f;
            int dealt = Mathf.Min(amount, Health.Value);
            int hp = Health.Value - amount;
            bool kill = hp <= 0;
            Health.Value = (byte)Mathf.Max(0, hp);

            if (attacker != null && attacker != this)
            {
                m_Contributions.Add((attacker.OwnerClientId, dealt, now));
                GameSession.Instance?.ModifyRecord(attacker.OwnerClientId, r => { r.DamageDealt += dealt; return r; });
                attacker.HitConfirmRpc(new HitConfirm { Victim = OwnerClientId, Damage = (short)dealt, Headshot = headshot, Kill = kill });
            }
            DamagedRpc(new DamageTaken
            {
                Attacker = attacker != null ? attacker.OwnerClientId : OwnerClientId,
                From = from,
                Damage = (short)dealt,
                Headshot = headshot,
            });
            if (kill) ServerKill(attacker != null ? attacker : this, weaponId, headshot, penetrated);
        }

        void ServerKill(NetPlayer killer, byte weaponId, bool headshot, bool penetrated)
        {
            if (!AliveState.Value) return;
            float now = ServerNow;
            AliveState.Value = false;
            Health.Value = 0;
            Protected.Value = false;
            m_PendingRespawn = true;
            RespawnAt.Value = now + m_RespawnDelay;

            ulong assister = ulong.MaxValue;
            int bestAssist = config.assistMinDamage - 1;
            var totals = new Dictionary<ulong, int>();
            foreach (var c in m_Contributions)
            {
                if (now - c.time > config.assistWindow) continue;
                totals.TryGetValue(c.attacker, out int t);
                totals[c.attacker] = t + c.damage;
            }
            foreach (var kv in totals)
            {
                if (killer != null && kv.Key == killer.OwnerClientId) continue;
                if (kv.Value > bestAssist) { bestAssist = kv.Value; assister = kv.Key; }
            }
            m_Contributions.Clear();

            var gs = GameSession.Instance;
            bool suicide = killer == null || killer == this;
            // Team kills are not credited (friendly fire is off, but environmental or future
            // damage sources must not hand out kills against teammates).
            bool teamKill = !suicide && TeamState.Value != Team.None && killer.TeamState.Value == TeamState.Value;
            if (gs != null)
            {
                gs.ModifyRecord(OwnerClientId, r => { r.Deaths++; return r; });
                if (!suicide && !teamKill)
                {
                    int score = config.killScore + (headshot ? config.headshotBonus : 0);
                    gs.ModifyRecord(killer.OwnerClientId, r => { r.Kills++; r.Score += score; return r; });
                }
                if (assister != ulong.MaxValue)
                {
                    int a = config.assistScore;
                    gs.ModifyRecord(assister, r => { r.Assists++; r.Score += a; return r; });
                }
            }

            var ev = new KillEvent
            {
                Killer = suicide ? OwnerClientId : killer.OwnerClientId,
                Victim = OwnerClientId,
                Assister = assister,
                WeaponId = weaponId,
                Headshot = headshot,
                Penetration = penetrated,
            };
            ServerKilled?.Invoke(ev, suicide ? this : killer, this);
            KillFeedRpc(ev);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void KillFeedRpc(KillEvent e) => CombatEvents.RaiseKilled(e);

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(HitConfirm h)
        {
            HitsConfirmed++;
            if (h.Kill) KillsConfirmed++;
            CombatEvents.RaiseHitConfirmed(h);
        }

        [Rpc(SendTo.Owner)]
        void DamagedRpc(DamageTaken d) => CombatEvents.RaiseLocalDamaged(d);

        /// <summary>Host: place the player at a spawn point with a fresh loadout.</summary>
        public void ServerRespawn()
        {
            if (!IsServer) return;
            if (!SpawnDirector.Pick(TeamState.Value, this, out var pos, out float yaw))
            {
                pos = Vector3.up;
                yaw = 0f;
            }
            ServerRespawnAt(pos, yaw);
        }

        public void ServerRespawnAt(Vector3 pos, float yaw)
        {
            var db = config.weapons;
            var primary = db.IsValidPrimary(PrimaryChoice.Value) ? db.Get(PrimaryChoice.Value) : db.Get(db.defaultPrimary);
            var secondary = db.Get(db.defaultSecondary);
            m_Motor = MotorState.Spawned(pos, config.motor);
            m_Weapon = WeaponState.Loadout(primary, secondary);
            m_AimYaw = yaw;
            m_AimPitch = 0f;
            m_SpawnYaw = yaw;
            unchecked { m_SpawnSerial++; }
            m_PendingRespawn = false;
            m_Contributions.Clear();
            m_LastDamageTime = -100f;
            Health.Value = (byte)config.maxHealth;
            AliveState.Value = true;
            m_ProtectUntil = ServerNow + m_SpawnProtection;
            Protected.Value = m_SpawnProtection > 0f;
            if (m_Cc.enabled)
            {
                m_Cc.enabled = false;
                transform.position = pos;
                m_Cc.enabled = true;
            }
            else transform.position = pos;

            if (IsOwner)
            {
                m_PrevMotor = m_Motor;
                m_ErrorOffset = Vector3.zero;
                InputProvider?.OnRespawned(yaw);
            }
        }

        /// <summary>Loadout request: applies immediately while still protected at spawn, else next spawn.</summary>
        [Rpc(SendTo.Server)]
        public void RequestPrimaryRpc(byte weaponId)
        {
            if (!config.weapons.IsValidPrimary(weaponId)) return;
            PrimaryChoice.Value = weaponId;
            if (AliveState.Value && Protected.Value)
            {
                var keepPos = m_Motor.Position;
                ServerRespawnAt(keepPos, m_AimYaw);
            }
        }

        public static NetPlayer Find(ulong clientId)
        {
            foreach (var p in All) if (p != null && p.OwnerClientId == clientId) return p;
            return null;
        }

        // ================================================================== remote view

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void RemoteStateRpc(RemoteSnapshot s)
        {
            if (IsServer) return;
            NetClock.OnSnapshotArrived(s.ServerTick);
            if (s.SpawnSerial != m_RemoteSerial)
            {
                m_SnapCount = 0;
                m_RemoteSerial = s.SpawnSerial;
            }

            int i = m_SnapCount - 1;
            while (i >= 0 && m_Snaps[i].ServerTick > s.ServerTick) i--;
            if (i >= 0 && m_Snaps[i].ServerTick == s.ServerTick) return; // duplicate
            if (m_SnapCount == SnapRing)
            {
                if (i < 0) return; // older than everything we keep
                Array.Copy(m_Snaps, 1, m_Snaps, 0, m_SnapCount - 1);
                m_SnapCount--;
                i--;
            }
            int at = i + 1;
            if (at < m_SnapCount) Array.Copy(m_Snaps, at, m_Snaps, at + 1, m_SnapCount - at);
            m_Snaps[at] = s;
            m_SnapCount++;
        }

        /// <summary>Remote player's pose at a fractional server tick (clients only).</summary>
        public bool SampleRemote(double renderTick, out RenderPose pose)
        {
            pose = default;
            if (m_SnapCount == 0) return false;

            int idx = -1;
            for (int i = m_SnapCount - 1; i >= 0; i--)
                if (m_Snaps[i].ServerTick <= renderTick) { idx = i; break; }

            if (idx < 0) { pose = FromSnapshot(m_Snaps[0]); return true; }
            if (idx == m_SnapCount - 1)
            {
                // Past the newest snapshot: extrapolate a little, then hold.
                var s = m_Snaps[idx];
                pose = FromSnapshot(s);
                float ahead = Mathf.Min(0.1f, (float)(renderTick - s.ServerTick) * Dt);
                if (ahead > 0f && s.Alive)
                {
                    Vector3 v = s.Velocity;
                    if ((s.Flags & MotorFlags.Grounded) != 0) v.y = 0f;
                    pose.Feet += v * ahead;
                    NetClock.ExtrapolatedFrames++;
                }
                return true;
            }

            var a = m_Snaps[idx];
            var b = m_Snaps[idx + 1];
            float t = (float)((renderTick - a.ServerTick) / Math.Max(1, b.ServerTick - a.ServerTick));
            var pa = FromSnapshot(a);
            var pb = FromSnapshot(b);
            pose = pa;
            pose.Feet = Vector3.Lerp(pa.Feet, pb.Feet, t);
            pose.Velocity = Vector3.Lerp(pa.Velocity, pb.Velocity, t);
            pose.Yaw = Mathf.LerpAngle(pa.Yaw, pb.Yaw, t);
            pose.Pitch = Mathf.Lerp(pa.Pitch, pb.Pitch, t);
            pose.Height = Mathf.Lerp(pa.Height, pb.Height, t);
            pose.Ads = Mathf.Lerp(pa.Ads, pb.Ads, t);
            pose.ActionProgress = pb.Action == pa.Action ? Mathf.Lerp(pa.ActionProgress, pb.ActionProgress, t) : pa.ActionProgress;
            if (t >= 0.5f)
            {
                pose.Stance = pb.Stance;
                pose.Flags = pb.Flags;
                pose.WeaponId = pb.WeaponId;
                pose.Action = pb.Action;
                pose.Alive = pb.Alive;
            }
            return true;
        }

        RenderPose FromSnapshot(in RemoteSnapshot s) => new RenderPose
        {
            Feet = s.Position,
            Velocity = s.Velocity,
            Yaw = s.Yaw * InputCmd.YawScale,
            Pitch = s.Pitch * InputCmd.PitchScale,
            Height = s.Height,
            Stance = s.Stance,
            Flags = s.Flags,
            WeaponId = s.WeaponId,
            Action = s.Action,
            ActionProgress = s.ActionProgress / 255f,
            Ads = s.Ads / 255f,
            Alive = s.Alive,
        };

        RenderPose FromSim(in MotorState m, Vector3 feet)
        {
            return new RenderPose
            {
                Feet = feet,
                Velocity = m.Velocity,
                Yaw = m_AimYaw,
                Pitch = m_AimPitch,
                Height = m.Height,
                Stance = m.Stance,
                Flags = m.Flags,
                WeaponId = m_Weapon.ActiveId,
                Action = m_Weapon.Action,
                ActionProgress = m_Weapon.ActionProgress,
                Ads = m_Weapon.Ads,
                Alive = AliveState.Value,
            };
        }

        void UpdateRender()
        {
            if (IsOwner)
            {
                float a = Mathf.Clamp01((float)(m_Accumulator / Dt));
                Vector3 feet = Vector3.Lerp(m_PrevMotor.Position, m_Motor.Position, a) + m_ErrorOffset;
                var r = FromSim(m_Motor, feet);
                r.Height = Mathf.Lerp(m_PrevMotor.Height, m_Motor.Height, a);
                Render = r;
                return;
            }

            if (IsServer)
            {
                // Host drawing a remote player: blend the last two simulated ticks.
                var nm = NetworkManager;
                if (LagCompensation.Server.TryGetPose(OwnerClientId, NetClock.RemoteRenderTick(), nm.ServerTime.Tick, NetClock.TickRate, out var hp, out _, false))
                {
                    var r = FromSim(m_Motor, hp.Feet);
                    r.Yaw = hp.Yaw;
                    r.Pitch = hp.Pitch;
                    r.Height = hp.Height;
                    r.Velocity = hp.Velocity;
                    Render = r;
                }
                else Render = FromSim(m_Motor, m_Motor.Position);
                return;
            }

            if (SampleRemote(NetClock.RemoteRenderTick(), out var pose))
            {
                Render = pose;
                transform.position = pose.Feet;
            }
        }
    }
}
