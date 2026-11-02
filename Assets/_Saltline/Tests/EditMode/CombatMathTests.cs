using NUnit.Framework;
using Saltline.Combat;
using Saltline.Sim;
using Saltline.Weapons;
using UnityEditor;
using UnityEngine;

namespace Saltline.Tests
{
    public class CombatMathTests
    {
        static MotorSettings Motor => AssetDatabase.LoadAssetAtPath<MotorSettings>("Assets/_Saltline/Data/MotorSettings.asset");
        static WeaponDatabase Db => AssetDatabase.LoadAssetAtPath<WeaponDatabase>("Assets/_Saltline/Data/WeaponDatabase.asset");

        // ---------------- Ray vs capsule

        [Test]
        public void RayCapsule_HitsCylinderAtSurface()
        {
            Assert.IsTrue(HitMath.RayCapsule(new Vector3(0, 1, 0), Vector3.forward, new Vector3(0, 0, 5), new Vector3(0, 2, 5), 0.5f, out float t));
            Assert.AreEqual(4.5f, t, 1e-4f);
        }

        [Test]
        public void RayCapsule_HitsEndCap()
        {
            // 0.3 m above the top segment point: sphere cap depth is sqrt(0.25 - 0.09) = 0.4.
            Assert.IsTrue(HitMath.RayCapsule(new Vector3(0, 2.3f, 0), Vector3.forward, new Vector3(0, 0, 5), new Vector3(0, 2, 5), 0.5f, out float t));
            Assert.AreEqual(4.6f, t, 1e-4f);
        }

        [Test]
        public void RayCapsule_MissesOutsideRadius()
        {
            Assert.IsFalse(HitMath.RayCapsule(new Vector3(0.51f, 1, 0), Vector3.forward, new Vector3(0, 0, 5), new Vector3(0, 2, 5), 0.5f, out _));
            Assert.IsFalse(HitMath.RayCapsule(new Vector3(0, 2.51f, 0), Vector3.forward, new Vector3(0, 0, 5), new Vector3(0, 2, 5), 0.5f, out _));
        }

        [Test]
        public void RayCapsule_IgnoresCapsuleBehindRay()
        {
            Assert.IsFalse(HitMath.RayCapsule(new Vector3(0, 1, 0), Vector3.forward, new Vector3(0, 0, -5), new Vector3(0, 2, -5), 0.5f, out _));
        }

        [Test]
        public void RayCapsule_OriginInsideCountsAsZero()
        {
            Assert.IsTrue(HitMath.RayCapsule(new Vector3(0, 1, 5), Vector3.forward, new Vector3(0, 0, 5), new Vector3(0, 2, 5), 0.5f, out float t));
            Assert.AreEqual(0f, t, 1e-5f);
        }

        [Test]
        public void RayCapsule_DiagonalMatchesBruteForce()
        {
            var rng = new System.Random(1234);
            for (int i = 0; i < 500; i++)
            {
                Vector3 a = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble(), 6 + (float)rng.NextDouble());
                Vector3 b = a + new Vector3((float)rng.NextDouble() - 0.5f, 1f, (float)rng.NextDouble() - 0.5f);
                float r = 0.1f + (float)rng.NextDouble() * 0.3f;
                Vector3 dir = new Vector3((float)rng.NextDouble() * 0.3f - 0.15f, (float)rng.NextDouble() * 0.3f - 0.05f, 1f).normalized;
                bool hit = HitMath.RayCapsule(Vector3.zero, dir, a, b, r, out float t);

                // Brute force: march along the ray and find the first point within r of the segment.
                float brute = -1f;
                for (float s = 0f; s < 12f; s += 0.0005f)
                    if (HitMath.PointSegmentDistance(dir * s, a, b) <= r) { brute = s; break; }

                Assert.AreEqual(brute >= 0f, hit, $"case {i}: hit mismatch");
                if (hit) Assert.AreEqual(brute, t, 0.002f, $"case {i}: distance mismatch");
            }
        }

        // ---------------- Hitbox model

        static readonly HitCapsule[] s_Caps = new HitCapsule[HitboxModel.Count];

        static HitRegion Shoot(in HitboxPose pose, Vector3 origin, out float dist)
        {
            HitboxModel.Build(pose, Motor, s_Caps);
            HitboxModel.Raycast(origin, Vector3.forward, s_Caps, 100f, out dist, out var region);
            return region;
        }

        static HitboxPose Standing => new HitboxPose { Feet = new Vector3(0, 0, 10), Yaw = 0f, Height = Motor.standHeight, Stance = Stance.Standing };

        [Test]
        public void Hitboxes_StandingRegions()
        {
            Assert.AreEqual(HitRegion.Head, Shoot(Standing, new Vector3(0, 1.70f, 0), out _));
            Assert.AreEqual(HitRegion.UpperTorso, Shoot(Standing, new Vector3(0, 1.25f, 0), out float d));
            Assert.AreEqual(10f - 0.19f, d, 1e-3f);
            Assert.AreEqual(HitRegion.LowerTorso, Shoot(Standing, new Vector3(0, 0.90f, 0), out _));
            Assert.AreEqual(HitRegion.Leg, Shoot(Standing, new Vector3(0.11f, 0.40f, 0), out _));
            Assert.AreEqual(HitRegion.None, Shoot(Standing, new Vector3(0, 1.90f, 0), out _));
            Assert.AreEqual(HitRegion.None, Shoot(Standing, new Vector3(0.5f, 1.25f, 0), out _));
        }

        [Test]
        public void Hitboxes_CrouchLowersHead()
        {
            var crouch = Standing;
            crouch.Height = Motor.crouchHeight;
            crouch.Stance = Stance.Crouching;
            Assert.AreEqual(HitRegion.None, Shoot(crouch, new Vector3(0, 1.70f, 0), out _), "standing head height must miss a crouched player");
            float eye = PlayerMotor.EyeHeight(new MotorState { Height = Motor.crouchHeight, Stance = Stance.Crouching }, Motor);
            Assert.AreEqual(HitRegion.Head, Shoot(crouch, new Vector3(0, eye + 0.05f, 0), out _));
        }

        [Test]
        public void Hitboxes_BroadPhaseContainsAllCapsules()
        {
            // Any ray that hits a capsule must also pass the cheap bounding test.
            var rng = new System.Random(7);
            var poses = new[] { Standing, new HitboxPose { Feet = new Vector3(0, 0, 10), Yaw = 90, Pitch = 60, Height = Motor.slideHeight, Stance = Stance.Sliding, Velocity = new Vector3(7, 0, 0) } };
            foreach (var p in poses)
            {
                HitboxModel.Build(p, Motor, s_Caps);
                for (int i = 0; i < 2000; i++)
                {
                    Vector3 o = new Vector3((float)rng.NextDouble() * 4 - 2, (float)rng.NextDouble() * 2.5f, 0);
                    Vector3 dir = (new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2, 10) - o).normalized;
                    if (HitboxModel.Raycast(o, dir, s_Caps, 100f, out _, out _))
                        Assert.IsTrue(HitboxModel.RayNearPlayer(o, dir, p, 100f), $"broad phase rejected a real hit (case {i})");
                }
            }
        }

        // ---------------- Damage

        [Test]
        public void Damage_FalloffCurve()
        {
            var ar = Db.Get(0);
            Assert.AreEqual(1f, DamageModel.Falloff(ar, 0f), 1e-6f);
            Assert.AreEqual(1f, DamageModel.Falloff(ar, ar.falloffStart), 1e-6f);
            Assert.AreEqual(ar.minDamageFactor, DamageModel.Falloff(ar, ar.falloffEnd), 1e-6f);
            Assert.AreEqual(ar.minDamageFactor, DamageModel.Falloff(ar, ar.falloffEnd * 3f), 1e-6f);
            float mid = (ar.falloffStart + ar.falloffEnd) * 0.5f;
            Assert.AreEqual((1f + ar.minDamageFactor) * 0.5f, DamageModel.Falloff(ar, mid), 1e-4f);
        }

        [Test]
        public void Damage_WeaponTuningSanity()
        {
            var sniper = Db.Get(3);
            Assert.GreaterOrEqual(DamageModel.ToHealth(DamageModel.PelletDamage(sniper, HitRegion.Head, 60f, 1f)), 100, "sniper headshot must kill");
            Assert.Less(DamageModel.ToHealth(DamageModel.PelletDamage(sniper, HitRegion.Leg, 10f, 1f)), 100, "sniper leg shot must not kill");

            var shotgun = Db.Get(2);
            float close = shotgun.pellets * DamageModel.PelletDamage(shotgun, HitRegion.UpperTorso, 3f, 1f);
            Assert.GreaterOrEqual(close, 100f, "all shotgun pellets to the chest at 3 m must kill");

            for (byte id = 0; id < 5; id++)
            {
                var w = Db.Get(id);
                Assert.IsNotNull(w, $"weapon {id} missing");
                Assert.AreEqual(id, w.id);
                Assert.Greater(DamageModel.RegionMultiplier(w, HitRegion.Head), DamageModel.RegionMultiplier(w, HitRegion.UpperTorso));
                Assert.Greater(w.magazineSize, 0);
            }
        }

        [Test]
        public void Damage_ToHealthRounding()
        {
            Assert.AreEqual(0, DamageModel.ToHealth(0f));
            Assert.AreEqual(1, DamageModel.ToHealth(0.2f));
            Assert.AreEqual(34, DamageModel.ToHealth(33.6f));
        }

        // ---------------- Input quantization

        [Test]
        public void Input_YawRoundTrip()
        {
            for (float deg = -720f; deg <= 720f; deg += 0.37f)
            {
                float back = InputCmd.QuantizeYaw(deg) * InputCmd.YawScale;
                Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(deg, back)), InputCmd.YawScale * 0.5f + 1e-4f, $"yaw {deg}");
            }
        }

        [Test]
        public void Input_PitchClampedAndRoundTrip()
        {
            Assert.AreEqual(89.5f, InputCmd.QuantizePitch(120f) * InputCmd.PitchScale, InputCmd.PitchScale);
            Assert.AreEqual(-89.5f, InputCmd.QuantizePitch(-120f) * InputCmd.PitchScale, InputCmd.PitchScale);
            for (float deg = -89f; deg <= 89f; deg += 0.29f)
                Assert.AreEqual(deg, InputCmd.QuantizePitch(deg) * InputCmd.PitchScale, InputCmd.PitchScale * 0.5f + 1e-4f);
        }

        [Test]
        public void Input_AxisQuantization()
        {
            Assert.AreEqual(127, InputCmd.QuantizeAxis(1f));
            Assert.AreEqual(-127, InputCmd.QuantizeAxis(-3f));
            Assert.AreEqual(0, InputCmd.QuantizeAxis(0.001f));
            var cmd = new InputCmd { MoveX = InputCmd.QuantizeAxis(0.5f), MoveY = InputCmd.QuantizeAxis(-1f) };
            Assert.AreEqual(0.5f, cmd.Move.x, 1f / 127f);
            Assert.AreEqual(-1f, cmd.Move.y, 1e-6f);
        }

        // ---------------- Lag compensation

        static HitboxPose PoseAt(float x) => new HitboxPose { Feet = new Vector3(x, 0, 0), Height = 1.8f };

        [Test]
        public void LagComp_InterpolatesBetweenTicks()
        {
            var lc = new LagCompensation { MaxRewindSeconds = 1f };
            for (int t = 100; t <= 110; t++) lc.Record(5, t, PoseAt(t), true);
            Assert.IsTrue(lc.TryGetPose(5, 105.25, 110, 60, out var pose, out bool alive));
            Assert.AreEqual(105.25f, pose.Feet.x, 1e-4f);
            Assert.IsTrue(alive);
            Assert.IsFalse(lc.TryGetPose(99, 105, 110, 60, out _, out _), "unknown player");
        }

        [Test]
        public void LagComp_ClampsToRewindWindow()
        {
            var lc = new LagCompensation { MaxRewindSeconds = 0.1f }; // 6 ticks at 60 Hz
            for (int t = 100; t <= 110; t++) lc.Record(1, t, PoseAt(t), true);
            Assert.IsTrue(lc.TryGetPose(1, 100, 110, 60, out var pose, out _));
            Assert.AreEqual(104f, pose.Feet.x, 1e-4f);
            Assert.AreEqual(1, lc.ClampedRewinds);
            Assert.IsTrue(lc.TryGetPose(1, 130, 110, 60, out pose, out _));
            Assert.AreEqual(110f, pose.Feet.x, 1e-4f, "future requests clamp to the current tick");
        }

        [Test]
        public void LagComp_StatsOptional()
        {
            var lc = new LagCompensation { MaxRewindSeconds = 0.1f };
            lc.Record(1, 10, PoseAt(10), true);
            lc.TryGetPose(1, 0, 10, 60, out _, out _, countStats: false);
            Assert.AreEqual(0, lc.Rewinds);
            Assert.AreEqual(0, lc.ClampedRewinds);
        }

        [Test]
        public void LagComp_DeadStateFollowsNearestTick()
        {
            var lc = new LagCompensation { MaxRewindSeconds = 1f };
            lc.Record(2, 50, PoseAt(50), true);
            lc.Record(2, 51, PoseAt(51), false);
            lc.TryGetPose(2, 50.2, 51, 60, out _, out bool a0);
            lc.TryGetPose(2, 50.8, 51, 60, out _, out bool a1);
            Assert.IsTrue(a0);
            Assert.IsFalse(a1);
        }
    }
}
