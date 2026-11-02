using Saltline.Sim;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Weapons
{
    public enum WeaponAction : byte
    {
        Ready = 0,
        Reloading = 1,
        ShellReloading = 2,
        Holstering = 3,
        Equipping = 4,
        Cycling = 5,   // pump / bolt action between shots
    }

    /// <summary>Complete simulated weapon state for one player. Predicted by the owner, authoritative on the host.</summary>
    public struct WeaponState : INetworkSerializeByMemcpy
    {
        public byte PrimaryId;
        public byte SecondaryId;
        public WeaponSlot Active;
        public WeaponSlot Pending;
        public WeaponAction Action;
        public float ActionTimer;   // seconds remaining in Action
        public float ActionTotal;   // full length of Action (for animation progress)
        public float FireCooldown;  // seconds until the next shot is allowed
        public short MagPrimary, ReservePrimary, MagSecondary, ReserveSecondary;
        public ushort BurstIndex;   // consecutive shots for the recoil pattern
        public float SinceLastShot;
        public float Bloom;         // degrees of accumulated spread
        public float Ads;           // 0..1 aim-down-sights blend
        public bool TriggerHeld;    // semi-auto edge detection
        public bool ReloadHeld;
        public bool ShellCancel;    // fire pressed during a shell reload
        public bool ShellInserting; // shell reload past its lead-in
        public uint ShotCounter;    // total shots fired; part of the spread seed

        public short Mag { get => Active == WeaponSlot.Primary ? MagPrimary : MagSecondary; set { if (Active == WeaponSlot.Primary) MagPrimary = value; else MagSecondary = value; } }
        public short Reserve { get => Active == WeaponSlot.Primary ? ReservePrimary : ReserveSecondary; set { if (Active == WeaponSlot.Primary) ReservePrimary = value; else ReserveSecondary = value; } }
        public byte ActiveId => Active == WeaponSlot.Primary ? PrimaryId : SecondaryId;

        public float ActionProgress => ActionTotal > 0f ? 1f - Mathf.Clamp01(ActionTimer / ActionTotal) : 1f;

        public static WeaponState Loadout(WeaponDefinition primary, WeaponDefinition secondary)
        {
            return new WeaponState
            {
                PrimaryId = primary.id,
                SecondaryId = secondary.id,
                Active = WeaponSlot.Primary,
                Pending = WeaponSlot.Primary,
                Action = WeaponAction.Equipping,
                ActionTimer = primary.equipTime,
                ActionTotal = primary.equipTime,
                MagPrimary = (short)primary.magazineSize,
                ReservePrimary = (short)primary.reserveAmmo,
                MagSecondary = (short)secondary.magazineSize,
                ReserveSecondary = (short)secondary.reserveAmmo,
                SinceLastShot = 10f,
            };
        }

        public static bool NearlyEqual(in WeaponState a, in WeaponState b) =>
            a.Active == b.Active && a.Action == b.Action && a.MagPrimary == b.MagPrimary &&
            a.MagSecondary == b.MagSecondary && a.ReservePrimary == b.ReservePrimary &&
            a.ReserveSecondary == b.ReserveSecondary && a.ShotCounter == b.ShotCounter &&
            Mathf.Abs(a.ActionTimer - b.ActionTimer) < 0.03f;
    }

    /// <summary>Output of one simulated shot (before hit resolution).</summary>
    public struct ShotEvent
    {
        public byte WeaponId;
        public uint ShotNumber;   // seed component, unique per player
        public float SpreadDegrees;
        public Vector2 RecoilKick; // degrees (yaw, pitch) the owner adds to its view
        public ushort BurstIndex;
    }

    public enum WeaponFx : byte
    {
        None = 0,
        ReloadStarted = 1,
        ReloadFinished = 2,
        ShellInserted = 3,
        Equipped = 4,
        Holstered = 5,
        DryFire = 6,
        Cycled = 7,
    }

    /// <summary>
    /// Deterministic weapon logic: one call per input tick, identical on client and host.
    /// At most one shot per tick (fastest weapon is 900 rpm = 15/s, tick rate 60).
    /// </summary>
    public static class WeaponSim
    {
        public static bool Step(ref WeaponState w, in InputCmd cmd, in MotorState motor, float dt,
            WeaponDatabase db, MotorSettings ms, out ShotEvent shot, out WeaponFx fx)
        {
            shot = default;
            fx = WeaponFx.None;
            var def = db.Get(w.ActiveId);
            if (def == null) return false;

            bool fire = cmd.Has(InputButtons.Fire);
            bool firePressed = fire && !w.TriggerHeld;
            w.TriggerHeld = fire;
            bool reloadPressed = cmd.Has(InputButtons.Reload) && !w.ReloadHeld;
            w.ReloadHeld = cmd.Has(InputButtons.Reload);

            w.FireCooldown = Mathf.Max(0f, w.FireCooldown - dt);
            w.SinceLastShot += dt;
            w.Bloom = Mathf.Max(0f, w.Bloom - def.bloomRecovery * dt);
            if (w.SinceLastShot > def.recoilResetTime) w.BurstIndex = 0;

            // ---- Weapon switching requests
            WeaponSlot requested = w.Pending;
            if (cmd.Has(InputButtons.SelectPrimary)) requested = WeaponSlot.Primary;
            else if (cmd.Has(InputButtons.SelectSecondary)) requested = WeaponSlot.Secondary;
            if (requested != w.Active && w.Action != WeaponAction.Holstering && w.Pending != requested)
            {
                // Switching cancels reloads (no ammo transferred) and starts a holster.
                w.Pending = requested;
                w.Action = WeaponAction.Holstering;
                w.ActionTimer = w.ActionTotal = def.holsterTime;
                w.Ads = 0f;
            }

            // ---- ADS
            bool canAds = !motor.Sprinting && w.Action != WeaponAction.Holstering && w.Action != WeaponAction.Equipping &&
                          !(def.hasScope && (w.Action == WeaponAction.Reloading || w.Action == WeaponAction.Cycling));
            float adsTarget = cmd.Has(InputButtons.Aim) && canAds ? 1f : 0f;
            w.Ads = Mathf.MoveTowards(w.Ads, adsTarget, dt / Mathf.Max(0.05f, def.adsTime));

            // ---- Timed actions
            if (w.Action != WeaponAction.Ready)
            {
                w.ActionTimer -= dt;
                switch (w.Action)
                {
                    case WeaponAction.Reloading:
                        // Tactical cancel: firing with rounds left aborts the reload before the commit point.
                        if (firePressed && w.Mag > 0 && w.ActionProgress < def.reloadCommitPoint)
                        {
                            w.Action = WeaponAction.Ready;
                            w.ActionTimer = 0f;
                            break;
                        }
                        if (w.ActionTimer <= 0f)
                        {
                            int need = def.magazineSize - w.Mag;
                            int take = Mathf.Min(need, w.Reserve);
                            w.Mag += (short)take;
                            w.Reserve -= (short)take;
                            w.Action = WeaponAction.Ready;
                            fx = WeaponFx.ReloadFinished;
                        }
                        break;

                    case WeaponAction.ShellReloading:
                        if (firePressed && w.Mag > 0) w.ShellCancel = true;
                        if (w.ActionTimer <= 0f)
                        {
                            if (!w.ShellInserting)
                            {
                                // Lead-in finished: start inserting shells.
                                w.ShellInserting = true;
                                w.ActionTimer = w.ActionTotal = def.shellInsertTime;
                                if (w.ShellCancel) { w.Action = WeaponAction.Ready; w.ShellCancel = false; w.ShellInserting = false; }
                            }
                            else
                            {
                                if (w.Mag < def.magazineSize && w.Reserve > 0) { w.Mag++; w.Reserve--; fx = WeaponFx.ShellInserted; }
                                if (w.ShellCancel || w.Mag >= def.magazineSize || w.Reserve <= 0)
                                {
                                    w.Action = WeaponAction.Ready;
                                    w.ShellCancel = false;
                                    w.ShellInserting = false;
                                    fx = WeaponFx.ReloadFinished;
                                }
                                else w.ActionTimer = w.ActionTotal = def.shellInsertTime;
                            }
                        }
                        break;

                    case WeaponAction.Holstering:
                        if (w.ActionTimer <= 0f)
                        {
                            w.Active = w.Pending;
                            var next = db.Get(w.ActiveId);
                            w.Action = WeaponAction.Equipping;
                            w.ActionTimer = w.ActionTotal = next != null ? next.equipTime : 0.4f;
                            w.BurstIndex = 0;
                            w.Bloom = 0f;
                            fx = WeaponFx.Holstered;
                            return false;
                        }
                        break;

                    case WeaponAction.Equipping:
                        if (w.ActionTimer <= 0f) { w.Action = WeaponAction.Ready; fx = WeaponFx.Equipped; }
                        break;

                    case WeaponAction.Cycling:
                        if (w.ActionTimer <= 0f) { w.Action = WeaponAction.Ready; fx = WeaponFx.Cycled; }
                        break;
                }
            }

            // ---- Reload start (manual or automatic on empty trigger pull)
            bool wantReload = reloadPressed || (firePressed && w.Mag == 0);
            if (w.Action == WeaponAction.Ready && wantReload && w.Mag < def.magazineSize && w.Reserve > 0)
            {
                if (def.reloadMode == ReloadMode.PerShell)
                {
                    w.ShellInserting = false;
                    w.ShellCancel = false;
                    w.Action = WeaponAction.ShellReloading;
                    w.ActionTimer = w.ActionTotal = def.reloadStartTime;
                }
                else
                {
                    w.Action = WeaponAction.Reloading;
                    w.ActionTimer = w.ActionTotal = w.Mag == 0 ? def.emptyReloadTime : def.reloadTime;
                }
                w.Ads = def.hasScope ? 0f : w.Ads;
                fx = WeaponFx.ReloadStarted;
                return false;
            }

            // ---- Fire
            bool sprintBlocked = motor.Sprinting || motor.SprintRecover < ms.sprintToFireDelay;
            bool triggerOk = def.fireMode == FireMode.Automatic ? fire : firePressed;
            if (!triggerOk || w.Action != WeaponAction.Ready || sprintBlocked || w.FireCooldown > 0f) return false;

            if (w.Mag <= 0)
            {
                if (firePressed) fx = WeaponFx.DryFire;
                return false;
            }

            w.Mag--;
            w.ShotCounter++;
            w.FireCooldown += def.FireInterval;
            if (w.FireCooldown < def.FireInterval * 0.5f) w.FireCooldown = def.FireInterval;

            float spread = ComputeSpread(def, in w, in motor, ms);
            Vector2 kick = def.RecoilForShot(w.BurstIndex);
            float jitter = (Hash01(w.ShotCounter * 2654435761u) - 0.5f) * 2f * def.recoilRandomYaw;
            kick.x += jitter;
            kick *= Mathf.Lerp(1f, def.recoilAdsMultiplier, w.Ads);

            shot = new ShotEvent
            {
                WeaponId = def.id,
                ShotNumber = w.ShotCounter,
                SpreadDegrees = spread,
                RecoilKick = kick,
                BurstIndex = w.BurstIndex,
            };

            w.BurstIndex++;
            w.SinceLastShot = 0f;
            w.Bloom = Mathf.Min(def.maxBloom, w.Bloom + def.bloomPerShot);

            if (def.fireMode == FireMode.PumpAction || def.fireMode == FireMode.BoltAction)
            {
                w.Action = WeaponAction.Cycling;
                w.ActionTimer = w.ActionTotal = def.FireInterval;
                w.FireCooldown = def.FireInterval;
                if (def.fireMode == FireMode.BoltAction) w.Ads = Mathf.Min(w.Ads, 0.999f);
            }
            return true;
        }

        public static float ComputeSpread(WeaponDefinition def, in WeaponState w, in MotorState motor, MotorSettings ms)
        {
            float baseSpread = Mathf.Lerp(def.hipSpread, def.adsSpread, w.Ads);
            float speedFactor = Mathf.Clamp01(motor.HorizontalSpeed / Mathf.Max(0.1f, ms.runSpeed));
            float move = def.moveSpread * speedFactor * Mathf.Lerp(1f, 0.45f, w.Ads);
            float air = motor.Grounded ? 0f : def.airSpread;
            float stance = motor.Stance == Stance.Crouching ? def.crouchSpreadMultiplier : 1f;
            return (baseSpread + move + air + w.Bloom * Mathf.Lerp(1f, 0.5f, w.Ads)) * stance;
        }

        /// <summary>Deterministic [0,1) from an integer seed (same on every platform).</summary>
        public static float Hash01(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return (x & 0x00FFFFFF) / 16777216f;
        }

        /// <summary>
        /// Direction of pellet <paramref name="pellet"/> for a shot: uniform inside the spread cone,
        /// seeded so client prediction and the host produce the exact same pattern.
        /// </summary>
        public static Vector3 PelletDirection(Vector3 aimDir, float spreadDegrees, ulong ownerClientId, uint shotNumber, int pellet)
        {
            if (spreadDegrees <= 0.0001f) return aimDir;
            uint seed = (uint)ownerClientId * 0x9E3779B1u ^ shotNumber * 0x85EBCA77u ^ (uint)pellet * 0xC2B2AE3Du;
            float u = Hash01(seed);
            float v = Hash01(seed ^ 0x27d4eb2fu);
            float r = Mathf.Sqrt(u) * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            float a = v * Mathf.PI * 2f;
            Vector3 right = Vector3.Cross(Vector3.up, aimDir);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(aimDir, right).normalized;
            return (aimDir + right * (Mathf.Cos(a) * r) + up * (Mathf.Sin(a) * r)).normalized;
        }
    }
}
