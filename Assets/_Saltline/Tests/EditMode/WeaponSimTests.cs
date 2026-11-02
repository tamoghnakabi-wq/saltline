using NUnit.Framework;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEditor;
using UnityEngine;

namespace Saltline.Tests
{
    public class WeaponSimTests
    {
        const float Dt = 1f / 60f;
        MotorSettings m_Motor;
        WeaponDatabase m_Db;
        MotorState m_Standing;

        [SetUp]
        public void SetUp()
        {
            m_Motor = AssetDatabase.LoadAssetAtPath<MotorSettings>("Assets/_Saltline/Data/MotorSettings.asset");
            m_Db = AssetDatabase.LoadAssetAtPath<WeaponDatabase>("Assets/_Saltline/Data/WeaponDatabase.asset");
            m_Standing = MotorState.Spawned(Vector3.zero, m_Motor);
        }

        WeaponState Ready(byte primary, byte secondary = 4)
        {
            var w = WeaponState.Loadout(m_Db.Get(primary), m_Db.Get(secondary));
            int guard = 0;
            while (w.Action != WeaponAction.Ready && guard++ < 600) Tick(ref w, InputButtons.None);
            Assert.AreEqual(WeaponAction.Ready, w.Action, "weapon never finished equipping");
            return w;
        }

        bool Tick(ref WeaponState w, InputButtons b) => Tick(ref w, b, out _, out _);

        bool Tick(ref WeaponState w, InputButtons b, out ShotEvent shot, out WeaponFx fx)
        {
            var cmd = new InputCmd { Buttons = b };
            return WeaponSim.Step(ref w, cmd, m_Standing, Dt, m_Db, m_Motor, out shot, out fx);
        }

        int HoldFire(ref WeaponState w, float seconds)
        {
            int shots = 0;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++) if (Tick(ref w, InputButtons.Fire)) shots++;
            return shots;
        }

        [Test]
        public void Equip_FinishesAfterEquipTime()
        {
            var def = m_Db.Get(0);
            var w = WeaponState.Loadout(def, m_Db.Get(4));
            int ticks = 0;
            WeaponFx fx = WeaponFx.None;
            while (w.Action != WeaponAction.Ready && ticks < 600) { Tick(ref w, InputButtons.None, out _, out fx); ticks++; }
            Assert.AreEqual(WeaponFx.Equipped, fx);
            Assert.AreEqual(Mathf.CeilToInt(def.equipTime / Dt), ticks, 1);
        }

        [Test]
        public void Automatic_FireRateMatchesRpm()
        {
            var def = m_Db.Get(0);
            var w = Ready(0);
            int shots = HoldFire(ref w, 1f);
            int expected = Mathf.Min(def.magazineSize, Mathf.FloorToInt(1f / def.FireInterval) + 1);
            Assert.AreEqual(expected, shots, 1, $"{def.displayName} at {def.roundsPerMinute} rpm");
            Assert.AreEqual(def.magazineSize - shots, w.Mag);
        }

        [Test]
        public void SemiAuto_OneShotPerPull()
        {
            var w = Ready(4, 4);
            Assert.AreEqual(1, HoldFire(ref w, 1f), "holding the trigger fires once");
            Tick(ref w, InputButtons.None); // release
            int shots = 0;
            for (int i = 0; i < 6; i++)
            {
                for (int k = 0; k < 10; k++) if (Tick(ref w, InputButtons.Fire)) shots++;
                for (int k = 0; k < 10; k++) Tick(ref w, InputButtons.None);
            }
            Assert.AreEqual(6, shots, "six separate pulls fire six shots");
        }

        [Test]
        public void EmptyMag_TriggerStartsReloadAndRefills()
        {
            var def = m_Db.Get(0);
            var w = Ready(0);
            w.MagPrimary = 0;
            short reserve = w.ReservePrimary;
            Tick(ref w, InputButtons.Fire, out _, out var fx);
            Assert.AreEqual(WeaponFx.ReloadStarted, fx);
            Assert.AreEqual(WeaponAction.Reloading, w.Action);
            Assert.AreEqual(def.emptyReloadTime, w.ActionTotal, 1e-5f);

            int guard = 0;
            while (w.Action == WeaponAction.Reloading && guard++ < 1000) Tick(ref w, InputButtons.None);
            Assert.AreEqual(def.magazineSize, w.MagPrimary);
            Assert.AreEqual(reserve - def.magazineSize, w.ReservePrimary);
        }

        [Test]
        public void TacticalReload_CanBeCancelledBeforeCommit()
        {
            var def = m_Db.Get(0);
            var w = Ready(0);
            HoldFire(ref w, 0.3f);
            Tick(ref w, InputButtons.None);
            short mag = w.MagPrimary;
            Tick(ref w, InputButtons.Reload);
            Assert.AreEqual(WeaponAction.Reloading, w.Action);
            Tick(ref w, InputButtons.None);
            short reserve = w.ReservePrimary;
            bool fired = Tick(ref w, InputButtons.Fire); // cancel early: the reload aborts and the shot goes out
            Assert.AreEqual(WeaponAction.Ready, w.Action);
            Assert.IsTrue(fired);
            Assert.AreEqual(mag - 1, w.MagPrimary, "a cancelled reload transfers no ammo");
            Assert.AreEqual(reserve, w.ReservePrimary);
        }

        [Test]
        public void PerShellReload_InsertsShellsOneByOne()
        {
            var def = m_Db.Get(2);
            Assert.AreEqual(ReloadMode.PerShell, def.reloadMode);
            var w = Ready(2);
            w.MagPrimary = (short)(def.magazineSize - 3);
            short reserve = w.ReservePrimary;
            Tick(ref w, InputButtons.Reload, out _, out var fx);
            Assert.AreEqual(WeaponFx.ReloadStarted, fx);

            int inserted = 0, guard = 0;
            bool finished = false;
            while (!finished && guard++ < 2000)
            {
                Tick(ref w, InputButtons.None, out _, out fx);
                if (fx == WeaponFx.ShellInserted) inserted++;
                if (fx == WeaponFx.ReloadFinished) { inserted++; finished = true; } // the last shell finishes the reload
            }
            Assert.IsTrue(finished);
            Assert.AreEqual(3, inserted);
            Assert.AreEqual(def.magazineSize, w.MagPrimary);
            Assert.AreEqual(reserve - 3, w.ReservePrimary);
            float expected = def.reloadStartTime + 3 * def.shellInsertTime;
            Assert.AreEqual(expected, guard * Dt, 3 * Dt);
        }

        [Test]
        public void PerShellReload_FireInterruptsAfterCurrentShell()
        {
            var def = m_Db.Get(2);
            var w = Ready(2);
            w.MagPrimary = 2;
            Tick(ref w, InputButtons.Reload);
            for (int i = 0; i < Mathf.CeilToInt((def.reloadStartTime + def.shellInsertTime * 0.5f) / Dt); i++) Tick(ref w, InputButtons.None);
            Tick(ref w, InputButtons.Fire);
            int guard = 0;
            while (w.Action == WeaponAction.ShellReloading && guard++ < 600) Tick(ref w, InputButtons.Fire);
            Assert.AreEqual(3, w.MagPrimary, "only the shell in progress is inserted");
        }

        [Test]
        public void BoltAction_CyclesBetweenShots()
        {
            var def = m_Db.Get(3);
            var w = Ready(3);
            Assert.IsTrue(Tick(ref w, InputButtons.Fire));
            Assert.AreEqual(WeaponAction.Cycling, w.Action);
            int ticks = Mathf.FloorToInt(def.FireInterval * 0.9f / Dt);
            for (int i = 0; i < ticks; i++)
                Assert.IsFalse(Tick(ref w, (i & 1) == 0 ? InputButtons.None : InputButtons.Fire), "cannot fire while the bolt cycles");
            int guard = 0;
            while (w.Action != WeaponAction.Ready && guard++ < 600) Tick(ref w, InputButtons.None);
            Assert.IsTrue(Tick(ref w, InputButtons.Fire), "fires again once cycled");
        }

        [Test]
        public void Switch_HolstersThenEquipsSecondary()
        {
            var w = Ready(0, 4);
            Tick(ref w, InputButtons.SelectSecondary);
            Assert.AreEqual(WeaponAction.Holstering, w.Action);
            Assert.IsFalse(Tick(ref w, InputButtons.Fire), "cannot fire while holstering");
            int guard = 0;
            while (w.Action != WeaponAction.Ready && guard++ < 600) Tick(ref w, InputButtons.None);
            Assert.AreEqual(WeaponSlot.Secondary, w.Active);
            Assert.AreEqual(4, w.ActiveId);
            float expected = m_Db.Get(0).holsterTime + m_Db.Get(4).equipTime;
            Assert.AreEqual(expected, (guard + 1) * Dt, 3 * Dt);
        }

        [Test]
        public void Sprint_BlocksFire()
        {
            var w = Ready(0);
            var sprinting = m_Standing;
            sprinting.Flags |= MotorFlags.Sprinting;
            var cmd = new InputCmd { Buttons = InputButtons.Fire };
            Assert.IsFalse(WeaponSim.Step(ref w, cmd, sprinting, Dt, m_Db, m_Motor, out _, out _));
            var recovering = m_Standing;
            recovering.SprintRecover = 0f;
            Assert.IsFalse(WeaponSim.Step(ref w, cmd, recovering, Dt, m_Db, m_Motor, out _, out _), "sprint-to-fire delay");
            Assert.IsTrue(WeaponSim.Step(ref w, cmd, m_Standing, Dt, m_Db, m_Motor, out _, out _));
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            var a = Ready(1);
            var b = Ready(1);
            var rng = new System.Random(99);
            for (int i = 0; i < 1200; i++)
            {
                var buttons = (InputButtons)(rng.Next(0, 4) switch { 0 => 0, 1 => (int)InputButtons.Fire, 2 => (int)(InputButtons.Fire | InputButtons.Aim), _ => (int)InputButtons.Reload });
                if (rng.Next(100) == 0) buttons |= InputButtons.SelectSecondary;
                if (rng.Next(100) == 0) buttons |= InputButtons.SelectPrimary;
                bool sa = Tick(ref a, buttons, out var shotA, out var fxA);
                bool sb = Tick(ref b, buttons, out var shotB, out var fxB);
                Assert.AreEqual(sa, sb);
                Assert.AreEqual(fxA, fxB);
                Assert.AreEqual(shotA.SpreadDegrees, shotB.SpreadDegrees);
                Assert.AreEqual(shotA.RecoilKick, shotB.RecoilKick);
                Assert.IsTrue(WeaponState.NearlyEqual(a, b));
                Assert.AreEqual(a.ActionTimer, b.ActionTimer);
                Assert.AreEqual(a.Bloom, b.Bloom);
            }
        }

        [Test]
        public void Pellets_DeterministicAndInsideCone()
        {
            Vector3 aim = Quaternion.Euler(-10f, 37f, 0f) * Vector3.forward;
            const float spread = 4.5f;
            for (uint shot = 1; shot < 200; shot++)
            {
                for (int p = 0; p < 9; p++)
                {
                    Vector3 d = WeaponSim.PelletDirection(aim, spread, 3, shot, p);
                    Assert.AreEqual(d, WeaponSim.PelletDirection(aim, spread, 3, shot, p));
                    Assert.AreEqual(1f, d.magnitude, 1e-4f);
                    Assert.LessOrEqual(Vector3.Angle(aim, d), spread + 0.01f);
                }
                Assert.AreNotEqual(WeaponSim.PelletDirection(aim, spread, 3, shot, 0), WeaponSim.PelletDirection(aim, spread, 4, shot, 0), "different shooters get different patterns");
            }
            Assert.AreEqual(aim, WeaponSim.PelletDirection(aim, 0f, 3, 1, 0), "zero spread is exact");
        }

        [Test]
        public void Hash01_InRangeAndSpread()
        {
            int below = 0;
            for (uint i = 0; i < 10000; i++)
            {
                float h = WeaponSim.Hash01(i);
                Assert.GreaterOrEqual(h, 0f);
                Assert.Less(h, 1f);
                if (h < 0.5f) below++;
            }
            Assert.AreEqual(5000, below, 250, "roughly uniform");
        }
    }
}
