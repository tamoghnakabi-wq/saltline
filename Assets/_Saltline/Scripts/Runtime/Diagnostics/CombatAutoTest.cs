using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saltline.Combat;
using Saltline.Core;
using Saltline.FX;
using Saltline.Net;
using Saltline.Player;
using Saltline.Sim;
using Saltline.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saltline.Diagnostics
{
    /// <summary>
    /// Headless end-to-end combat test over real Unity Relay, with scripted bots:
    ///
    ///   Host:   -autotest combat-host   -codefile f [-lagcomp seconds] [-respawndelay s] [-testout f]
    ///   Client: -autotest combat-client -codefile f [-expectmiss] [-netsim d,j,l] [-netstall s,hold|drop] [-testout f]
    ///
    /// 1. Movement: the client runs a scripted path (sprint, strafe, jump, slide, crouch). Its
    ///    predicted state must match the host's authoritative state (few or no corrections).
    /// 2. Sniper: the host's player strafes; the client aims at where it draws the host and
    ///    fires. Hits must register on the host through lag compensation.
    /// 3. Pistol: same with the semi-automatic sidearm (weapon switch included).
    /// 4. The host's player fires AR bursts at the strafing client.
    /// -expectmiss inverts the sniper check for a control run with lag compensation disabled.
    /// -netstall stalls the client's network for s seconds halfway through the movement phase (needs
    /// -netsim), so the shooting phases measure how well hits register after a stall.
    /// The host notes, per phase, the client's input queue depth and how far back its shots rewound.
    /// </summary>
    public sealed class CombatAutoTest : AutoTestBase
    {
        public const string ArenaScene = "Arena";
        const int PhaseMovement = 1, PhaseSniper = 2, PhasePistol = 3, PhaseHostShoots = 4, PhaseDone = 99;

        int m_Phase;
        float m_PhaseStart;

        // host: per-phase network figures for the client
        long m_QueueSum;
        int m_QueueSamples, m_QueueMax, m_RewindsAtPhase, m_ClampedAtPhase;
        double m_AskedAtPhase;

        protected override Task<bool> Run() => Role.Contains("host") ? RunHost() : RunClient();

        // ================================================================== host

        async Task<bool> RunHost()
        {
            Note("host: signing in");
            await UgsAuth.EnsureSignedInAsync();
            if (File.Exists(CodeFile)) File.Delete(CodeFile);
            var session = await SessionService.HostAsync(new HostSettings
            {
                PlayerName = "CombatHost",
                SessionName = $"combat-{DateTime.UtcNow:HHmmss}",
                IsPrivate = true,
                MapId = "arena",
            });
            var nm = NetworkManager.Singleton;
            if (!await WaitUntil(() => nm.IsHost && GameSession.Instance != null && NetDiagnostics.Instance != null, 30))
                return Fail("host network did not start");
            var utp = nm.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport;
            string proto = utp != null ? utp.Protocol.ToString() : "n/a";
            Result["transportProtocol"] = proto;
            if (proto != "RelayUnityTransport") return Fail($"expected Relay transport, got {proto}");
            Result["lagCompMaxRewind"] = CommandLine.GetFloat("lagcomp", 0.35f).ToString("F2");

            NetDiagnostics.Instance.ClientPhase += (id, phase, value) =>
            {
                NotePhaseNetwork();
                m_Phase = phase;
                m_PhaseStart = Time.time;
                Note($"host: client {id} entered phase {phase} ({value:F2})");
            };

            WriteCode(session.Code);
            Note($"host: join code {session.Code} written");
            if (!await WaitUntil(() => GameSession.Instance.Roster.Count >= 2, Timeout)) return Fail("client never joined");

            var status = nm.SceneManager.LoadScene(ArenaScene, LoadSceneMode.Single);
            Note($"host: loading {ArenaScene}: {status}");
            if (status != SceneEventProgressStatus.Started) return Fail($"scene load refused: {status}");

            if (!await WaitUntil(() => NetPlayer.All.Count >= 2 && NetPlayer.All.All(p => p.AliveState.Value), 40))
                return Fail($"players did not spawn (count={NetPlayer.All.Count})");
            var me = NetPlayer.Local;
            var enemy = NetPlayer.All.First(p => p != me);
            Note($"host: players spawned. host team={me.TeamState.Value} at {me.SimMotor.Position}, client team={enemy.TeamState.Value} at {enemy.SimMotor.Position}");

            if (CommandLine.Has("capture"))
            {
                // Visual check: real camera rig and effects, driven by the bot's aim.
                var view = LocalPlayerController.Attach(me);
                view.FollowSimAim = true;
                FxManager.Ensure();
                CaptureDirector.Install(me);
            }

            var bot = new BotInputProvider();
            me.InputProvider = bot;
            bot.Brain = (p, intent) => HostBrain(p, intent, enemy);

            bool done = await WaitUntil(() => m_Phase == PhaseDone || !enemy.IsSpawned, Timeout);
            await WaitSeconds(1f);

            GameSession.Instance.TryGet(enemy.OwnerClientId, out var cr);
            GameSession.Instance.TryGet(me.OwnerClientId, out var hr);
            var lc = LagCompensation.Server;
            float hostRate = me.ShotsPredicted > 0 ? me.HitsConfirmed / (float)me.ShotsPredicted : 0f;
            Note($"host: client record K/D/A {cr.Kills}/{cr.Deaths}/{cr.Assists} dmg {cr.DamageDealt}; host K/D {hr.Kills}/{hr.Deaths}");
            Note($"host: client shots resolved {enemy.ServerShotsResolved}, pellets hit {enemy.ServerPelletsHit}; inputs processed {enemy.ServerInputsProcessed}, skipped {enemy.ServerInputsSkipped}, dropped {enemy.ServerInputsDropped}, caught up {enemy.ServerInputsCaughtUp}");
            Note($"host: own shots {me.ShotsPredicted}, confirmed {me.HitsConfirmed} ({hostRate:P0}); rewinds {lc.Rewinds}, clamped {lc.ClampedRewinds}");
            Result["clientKills"] = cr.Kills.ToString();
            Result["clientDamage"] = cr.DamageDealt.ToString();
            Result["hostDeaths"] = hr.Deaths.ToString();
            Result["hostKills"] = hr.Kills.ToString();
            Result["hostShots"] = me.ShotsPredicted.ToString();
            Result["hostHitRate"] = hostRate.ToString("F2");
            Result["clientShotsResolved"] = enemy.ServerShotsResolved.ToString();
            Result["inputsProcessed"] = enemy.ServerInputsProcessed.ToString();
            Result["inputsSkipped"] = enemy.ServerInputsSkipped.ToString();
            Result["inputsDropped"] = enemy.ServerInputsDropped.ToString();
            Result["inputsCaughtUp"] = enemy.ServerInputsCaughtUp.ToString();
            Result["rewinds"] = lc.Rewinds.ToString();
            Result["clampedRewinds"] = lc.ClampedRewinds.ToString();
            if (CommandLine.Has("capture")) Result["framesCaptured"] = CaptureDirector.Captured.ToString();

            if (!done) return Fail("client never finished its phases");
            bool expectMiss = CommandLine.GetFloat("lagcomp", 0.35f) <= 0.001f;
            bool ok = enemy.ServerShotsResolved > 0 && me.ShotsPredicted > 0 && hostRate >= 0.35f && hr.Kills >= 1;
            if (!expectMiss) ok &= cr.Kills >= 1 && hr.Deaths >= 1;
            if (!ok) return Fail("combat expectations not met (see notes)");
            return true;
        }

        /// <summary>Host: the client's input queue and rewinds during the phase that just ended.</summary>
        void NotePhaseNetwork()
        {
            var lc = LagCompensation.Server;
            int rewinds = lc.Rewinds - m_RewindsAtPhase;
            float tickMs = 1000f / NetClock.TickRate;
            if (m_Phase != 0)
            {
                float queueAvg = m_QueueSamples > 0 ? m_QueueSum / (float)m_QueueSamples : 0f;
                string asked = rewinds > 0
                    ? $"asked {(lc.AskedTicksTotal - m_AskedAtPhase) / rewinds * tickMs:F0} ms back on average, at most {lc.AskedTicksMax * tickMs:F0} ms"
                    : "none";
                Note($"host: phase {m_Phase} network: client input queue avg {queueAvg:F1} max {m_QueueMax} ticks; " +
                     $"rewinds {rewinds} ({asked}), clamped {lc.ClampedRewinds - m_ClampedAtPhase}");
            }
            m_QueueSum = 0; m_QueueSamples = 0; m_QueueMax = 0;
            m_RewindsAtPhase = lc.Rewinds;
            m_ClampedAtPhase = lc.ClampedRewinds;
            m_AskedAtPhase = lc.AskedTicksTotal;
            lc.ResetAskedMax();
        }

        PlayerIntent HostBrain(NetPlayer p, PlayerIntent i, NetPlayer enemy)
        {
            int depth = enemy.ServerQueueDepth;
            m_QueueSum += depth;
            m_QueueSamples++;
            if (depth > m_QueueMax) m_QueueMax = depth;

            var o = new PlayerIntent { AimYaw = i.AimYaw, AimPitch = 0f };
            if (!p.AliveState.Value) return o;
            bool sees = BotInputProvider.TargetChest(p, enemy, out var chest);
            if (sees) BotInputProvider.AimAt(p, chest, ref o);
            float t = Time.time - m_PhaseStart;
            switch (m_Phase)
            {
                case PhaseSniper:
                case PhasePistol:
                    o.Move = new Vector2(((int)(t / 0.7f) % 2 == 0) ? 1f : -1f, 0f); // strafe across the line of fire
                    break;
                case PhaseHostShoots:
                    var ws = p.SimWeapon;
                    o.Buttons |= InputButtons.Aim;
                    bool burst = (t % 0.8f) < 0.36f;
                    if (sees && burst && ws.Ads > 0.95f && enemy.AliveState.Value && !enemy.Protected.Value) o.Buttons |= InputButtons.Fire;
                    if (ws.Mag == 0) o.Buttons |= InputButtons.Reload;
                    break;
            }
            return o;
        }

        // ================================================================== client

        async Task<bool> RunClient()
        {
            Note("client: signing in");
            await UgsAuth.EnsureSignedInAsync();
            string code = await ReadCode();
            if (string.IsNullOrEmpty(code)) return Fail("join code never appeared");
            string lost = null;
            SessionService.SessionLost += r => lost = r;
            await SessionService.JoinByCodeAsync(code, $"CombatClient-{UgsAuth.Profile}");

            if (!await WaitUntil(() => lost != null || (NetPlayer.Local != null && NetPlayer.Local.AliveState.Value && NetPlayer.All.Count >= 2), 60))
                return Fail("local player never spawned");
            if (lost != null) return Fail($"connection lost: {lost}");
            var me = NetPlayer.Local;
            var enemy = NetPlayer.All.First(p => p != me);
            var db = me.Weapons;
            var sniper = db.weapons.First(w => w != null && w.weaponClass == WeaponClass.Sniper);
            Note($"client: spawned as client {me.OwnerClientId} team={me.TeamState.Value}; requesting {sniper.displayName}");

            var bot = new BotInputProvider();
            me.InputProvider = bot;
            me.RequestPrimaryRpc(sniper.id);
            if (!await WaitUntil(() => me.SimWeapon.PrimaryId == sniper.id && me.SimWeapon.Action == WeaponAction.Ready, 8))
                return Fail("loadout change did not apply");

            // ---- Phase 1: movement prediction
            await Announce(PhaseMovement, 0);
            int reconBefore = me.Reconciliations, tickBefore = me.LocalTick;
            Vector3 start = me.SimMotor.Position;
            bot.Brain = (p, i) => MovementBrain(p, i);
            string stall = CommandLine.Get("netstall", "");
            if (stall.Length > 0)
            {
                var sp = stall.Split(',');
                float secs = float.TryParse(sp[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
                bool hold = sp.Length < 2 || sp[1] != "drop";
                await WaitSeconds(3f);
                if (!NetSimBridge.Stall(secs, hold)) return Fail("-netstall needs -netsim and a development build");
                Note($"client: network stall, {(hold ? "holding" : "dropping")} packets for {secs:F1} s");
                Result["netstall"] = stall;
                await WaitSeconds(5f);
            }
            else await WaitSeconds(8f);
            bot.Brain = (p, i) => new PlayerIntent { AimYaw = i.AimYaw };
            await WaitSeconds(0.8f);
            int moveTicks = me.LocalTick - tickBefore;
            int moveRecon = me.Reconciliations - reconBefore;
            float travelled = Vector3.Distance(start, me.SimMotor.Position);
            Note($"client: movement phase {moveTicks} ticks, moved {travelled:F1} m, corrections {moveRecon}, last correction {me.LastCorrectionMeters * 100f:F1} cm");
            Result["movementTicks"] = moveTicks.ToString();
            Result["movementCorrections"] = moveRecon.ToString();

            // ---- Phase 2: sniper vs strafing host
            await Announce(PhaseSniper, 0);
            var (sShots, sHits, sKills) = await ShootPhase(me, enemy, bot, 32f, false);
            float sRate = sShots > 0 ? sHits / (float)sShots : 0f;
            Note($"client: sniper shots {sShots}, confirmed {sHits} ({sRate:P0}), kills {sKills}");
            Result["sniperShots"] = sShots.ToString();
            Result["sniperHitRate"] = sRate.ToString("F2");
            Result["sniperKills"] = sKills.ToString();

            // ---- Phase 3: pistol (switch to secondary first)
            await Announce(PhasePistol, 0);
            bot.Brain = (p, i) => new PlayerIntent { AimYaw = i.AimYaw, Buttons = InputButtons.SelectSecondary };
            if (!await WaitUntil(() => me.SimWeapon.Active == WeaponSlot.Secondary && me.SimWeapon.Action == WeaponAction.Ready, 4))
                return Fail("weapon switch to sidearm did not complete");
            var (pShots, pHits, pKills) = await ShootPhase(me, enemy, bot, 20f, true);
            float pRate = pShots > 0 ? pHits / (float)pShots : 0f;
            Note($"client: pistol shots {pShots}, confirmed {pHits} ({pRate:P0}), kills {pKills}");
            Result["pistolShots"] = pShots.ToString();
            Result["pistolHitRate"] = pRate.ToString("F2");

            // ---- Phase 4: host shoots us while we strafe
            await Announce(PhaseHostShoots, 0);
            int respawnsBefore = bot.Respawns;
            float p4 = Time.time;
            bot.Brain = (p, i) => new PlayerIntent
            {
                AimYaw = i.AimYaw,
                Move = new Vector2(((int)((Time.time - p4) / 0.8f) % 2 == 0) ? 1f : -1f, 0f),
            };
            await WaitSeconds(14f);
            Note($"client: died/respawned {bot.Respawns - respawnsBefore} time(s) during host fire phase; health now {me.Health.Value}");
            Result["timesKilledByHost"] = (bot.Respawns - respawnsBefore).ToString();

            bot.Brain = (p, i) => new PlayerIntent { AimYaw = i.AimYaw };
            var rtt = await MeasureRtt(20, 0.1f);
            Note($"client: rtt avg {rtt.avg:F1} ms (min {rtt.min:F1}, max {rtt.max:F1}, {rtt.received}/20 pongs)");
            Result["rttAvgMs"] = rtt.avg.ToString("F1");
            Result["rttMaxMs"] = rtt.max.ToString("F1");

            await Announce(PhaseDone, 0);
            await WaitSeconds(1.5f);

            Result["reconciliationsTotal"] = me.Reconciliations.ToString();
            Result["interpTicks"] = NetClock.InterpolationTicks.ToString("F1");
            Result["extrapolatedFrames"] = NetClock.ExtrapolatedFrames.ToString();

            bool expectMiss = CommandLine.Has("expectmiss");
            bool ok = true;
            // A stall in the movement phase forces corrections; the shooting phases are what that run measures.
            if (stall.Length == 0 && moveRecon > Mathf.Max(3, moveTicks / 20)) ok = Fail($"too many movement corrections ({moveRecon} in {moveTicks} ticks)");
            if (sShots < 8) ok = Fail($"too few sniper shots fired ({sShots})");
            if (expectMiss)
            {
                if (sRate >= 0.5f) ok = Fail($"control run: expected mostly misses without lag compensation, got {sRate:P0}");
            }
            else
            {
                if (sRate < 0.85f) ok = Fail($"sniper hit rate {sRate:P0} below 85% (lag compensation)");
                if (sKills < 1) ok = Fail("no kill confirmed");
                if (pShots < 8 || pRate < 0.6f) ok = Fail($"pistol {pHits}/{pShots} below 60%");
            }
            return ok;
        }

        async Task Announce(int phase, float value)
        {
            m_Phase = phase;
            m_PhaseStart = Time.time;
            NetDiagnostics.Instance.ClientPhaseRpc(phase, value);
            Note($"client: phase {phase} (interpolation delay {NetClock.InterpolationTicks:F1} ticks, extrapolated frames so far {NetClock.ExtrapolatedFrames})");
            await WaitSeconds(0.3f);
        }

        PlayerIntent MovementBrain(NetPlayer p, PlayerIntent i)
        {
            float t = Time.time - m_PhaseStart;
            var o = new PlayerIntent { AimYaw = i.AimYaw, AimPitch = 0f };
            if (t < 1.6f) { o.Move = Vector2.up; o.Buttons = InputButtons.Sprint; }
            else if (t < 2.0f) { o.Move = Vector2.up; o.Buttons = InputButtons.Sprint | InputButtons.Crouch; } // slide
            else if (t < 2.6f) { o.Move = Vector2.right; o.Buttons = InputButtons.Crouch; }
            else if (t < 3.4f) { o.Move = Vector2.right; o.Buttons = t < 2.8f ? InputButtons.Jump : InputButtons.None; }
            else if (t < 4.6f) { o.Move = new Vector2(-0.7f, -0.7f); o.AimYaw = i.AimYaw + 90f * Time.deltaTime; }
            else if (t < 5.6f) { o.Move = Vector2.down; o.Buttons = (t % 0.5f) < 0.1f ? InputButtons.Jump : InputButtons.None; }
            else if (t < 6.6f) { o.Move = Vector2.left; o.Buttons = InputButtons.Aim; }
            else { o.Move = Vector2.zero; }
            return o;
        }

        async Task<(int shots, int hits, int kills)> ShootPhase(NetPlayer me, NetPlayer enemy, BotInputProvider bot, float seconds, bool pistol)
        {
            int shots0 = me.ShotsPredicted, hits0 = me.HitsConfirmed, kills0 = me.KillsConfirmed;
            bool pressedLast = false;
            float lastPress = -10f;
            bot.Brain = (p, i) =>
            {
                var o = new PlayerIntent { AimYaw = i.AimYaw, AimPitch = i.AimPitch, Buttons = InputButtons.Aim };
                if (!p.AliveState.Value) return o;
                if (!BotInputProvider.TargetChest(p, enemy, out var chest)) return o;
                BotInputProvider.AimAt(p, chest, ref o);
                var ws = p.SimWeapon;
                bool ready = ws.Action == WeaponAction.Ready && ws.FireCooldown <= 0f && ws.Mag > 0 && ws.Ads >= 0.99f;
                bool targetOk = enemy.AliveState.Value && !enemy.Protected.Value;
                bool press = ready && targetOk && !pressedLast && Time.time - lastPress > (pistol ? 0.22f : 0.1f);
                if (press) { o.Buttons |= InputButtons.Fire; lastPress = Time.time; }
                if (ws.Mag == 0 && ws.Action == WeaponAction.Ready) o.Buttons |= InputButtons.Reload;
                pressedLast = press;
                return o;
            };
            await WaitSeconds(seconds);
            bot.Brain = (p, i) => new PlayerIntent { AimYaw = i.AimYaw, AimPitch = i.AimPitch };
            await WaitSeconds(1.0f); // late confirmations
            return (me.ShotsPredicted - shots0, me.HitsConfirmed - hits0, me.KillsConfirmed - kills0);
        }
    }
}
