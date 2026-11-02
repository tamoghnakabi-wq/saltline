using System.Collections.Generic;
using NUnit.Framework;
using Saltline.Core;
using Saltline.Sim;
using UnityEditor;
using UnityEngine;

namespace Saltline.Tests
{
    /// <summary>
    /// Movement tests on real PhysX geometry. Prediction and reconciliation depend on the motor
    /// producing the same result when the same inputs are replayed from the same state.
    /// </summary>
    public class MotorTests
    {
        const float Dt = 1f / 60f;
        MotorSettings m_M;
        readonly List<GameObject> m_Objects = new();
        CharacterController m_Cc;

        [SetUp]
        public void SetUp()
        {
            m_M = AssetDatabase.LoadAssetAtPath<MotorSettings>("Assets/_Saltline/Data/MotorSettings.asset");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.layer = Layers.Environment;
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.transform.localScale = new Vector3(200, 1, 200);
            m_Objects.Add(ground);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer = Layers.Environment;
            wall.transform.position = new Vector3(0, 1.5f, 30);
            wall.transform.localScale = new Vector3(20, 3, 1);
            m_Objects.Add(wall);

            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.layer = Layers.Environment;
            step.transform.position = new Vector3(-20, 0.15f, 8);
            step.transform.localScale = new Vector3(4, 0.3f, 8);
            m_Objects.Add(step);

            var p = new GameObject("TestPlayer") { layer = Layers.Player };
            m_Cc = p.AddComponent<CharacterController>();
            m_Cc.slopeLimit = m_M.slopeLimit;
            m_Cc.stepOffset = m_M.stepOffset;
            m_Cc.skinWidth = m_M.skinWidth;
            m_Cc.radius = m_M.radius;
            m_Cc.height = m_M.standHeight;
            m_Objects.Add(p);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in m_Objects) if (o != null) Object.DestroyImmediate(o);
            m_Objects.Clear();
        }

        static InputCmd Cmd(int tick, float x, float y, float yaw, InputButtons b = InputButtons.None) => new InputCmd
        {
            Tick = tick,
            MoveX = InputCmd.QuantizeAxis(x),
            MoveY = InputCmd.QuantizeAxis(y),
            Yaw = InputCmd.QuantizeYaw(yaw),
            Buttons = b,
        };

        void Step(ref MotorState s, in InputCmd c) => PlayerMotor.Step(ref s, c, Dt, m_Cc, m_M, PlayerMotor.StepParams.Default);

        [Test]
        public void Run_ReachesRunSpeedAndStaysGrounded()
        {
            var s = MotorState.Spawned(new Vector3(0, 0, 0), m_M);
            for (int i = 0; i < 60; i++) Step(ref s, Cmd(i, 0, 1, 0));
            Assert.IsTrue(s.Grounded);
            Assert.AreEqual(m_M.runSpeed, s.HorizontalSpeed, 0.05f);
            Assert.AreEqual(0f, s.Position.y, 0.06f);
            Assert.Greater(s.Position.z, 4f);
        }

        [Test]
        public void Sprint_IsFasterThanRun()
        {
            var s = MotorState.Spawned(Vector3.zero, m_M);
            for (int i = 0; i < 60; i++) Step(ref s, Cmd(i, 0, 1, 0, InputButtons.Sprint));
            Assert.IsTrue(s.Sprinting);
            Assert.AreEqual(m_M.sprintSpeed, s.HorizontalSpeed, 0.05f);
        }

        [Test]
        public void Jump_ApexMatchesGravityAndVelocity()
        {
            var s = MotorState.Spawned(new Vector3(10, 0, 0), m_M);
            Step(ref s, Cmd(0, 0, 0, 0));
            float apex = 0f;
            Step(ref s, Cmd(1, 0, 0, 0, InputButtons.Jump));
            for (int i = 2; i < 90; i++) { Step(ref s, Cmd(i, 0, 0, 0)); apex = Mathf.Max(apex, s.Position.y); }
            float expected = m_M.jumpVelocity * m_M.jumpVelocity / (2f * m_M.gravity);
            Assert.AreEqual(expected, apex, 0.08f);
            Assert.IsTrue(s.Grounded, "lands again");
        }

        [Test]
        public void Wall_StopsMovement()
        {
            var s = MotorState.Spawned(new Vector3(0, 0, 25), m_M);
            for (int i = 0; i < 120; i++) Step(ref s, Cmd(i, 0, 1, 0));
            Assert.Less(s.Position.z, 29.5f - m_M.radius + 0.05f, "must not pass through the wall");
        }

        [Test]
        public void Step_ClimbsLowLedge()
        {
            var s = MotorState.Spawned(new Vector3(-20, 0, 0), m_M);
            for (int i = 0; i < 90; i++) Step(ref s, Cmd(i, 0, 1, 0));
            Assert.Greater(s.Position.z, 5f, "walked onto the 0.3 m block");
            Assert.AreEqual(0.3f, s.Position.y, 0.06f);
        }

        [Test]
        public void Crouch_SlowerAndShorter()
        {
            var s = MotorState.Spawned(new Vector3(20, 0, 0), m_M);
            for (int i = 0; i < 90; i++) Step(ref s, Cmd(i, 0, 1, 0, InputButtons.Crouch));
            Assert.AreEqual(Stance.Crouching, s.Stance);
            Assert.AreEqual(m_M.crouchHeight, s.Height, 0.02f);
            Assert.AreEqual(m_M.crouchSpeed, s.HorizontalSpeed, 0.05f);
        }

        [Test]
        public void Slide_FromSprintThenEnds()
        {
            var s = MotorState.Spawned(new Vector3(40, 0, -40), m_M);
            for (int i = 0; i < 60; i++) Step(ref s, Cmd(i, 0, 1, 0, InputButtons.Sprint));
            Step(ref s, Cmd(60, 0, 1, 0, InputButtons.Sprint | InputButtons.Crouch));
            Assert.AreEqual(Stance.Sliding, s.Stance);
            Assert.Greater(s.HorizontalSpeed, m_M.sprintSpeed);
            int t = 61;
            for (; t < 61 + 120 && s.Stance == Stance.Sliding; t++) Step(ref s, Cmd(t, 0, 1, 0, InputButtons.Crouch));
            Assert.AreNotEqual(Stance.Sliding, s.Stance, "slide ends");
            Assert.LessOrEqual((t - 61) * Dt, m_M.slideMaxTime + 2 * Dt);
        }

        [Test]
        public void Replay_StaysWithinCorrectionThreshold()
        {
            // Reconciliation replays inputs from an acknowledged state. PhysX's CharacterController
            // keeps a little internal contact state between moves, so replays are not bit-exact;
            // what matters is that they stay far below the 2 cm threshold that triggers a correction.
            var rng = new System.Random(42);
            var cmds = new InputCmd[600];
            float yaw = 0f;
            for (int i = 0; i < cmds.Length; i++)
            {
                yaw += (float)(rng.NextDouble() - 0.5) * 8f;
                InputButtons b = InputButtons.None;
                if (rng.Next(40) == 0) b |= InputButtons.Jump;
                if (rng.Next(3) == 0) b |= InputButtons.Sprint;
                if (rng.Next(25) == 0) b |= InputButtons.Crouch;
                cmds[i] = Cmd(i, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 1.4f - 0.4f, yaw, b);
            }

            var a = MotorState.Spawned(new Vector3(-5, 0, -5), m_M);
            var history = new MotorState[cmds.Length];
            for (int i = 0; i < cmds.Length; i++) { Step(ref a, cmds[i]); history[i] = a; }

            // Typical replay windows (up to 30 ticks = 0.5 s of latency) from many start points.
            float worstShort = 0f;
            for (int k = 0; k + 30 < cmds.Length; k += 7)
            {
                var r = history[k];
                for (int i = k + 1; i <= k + 30; i++) Step(ref r, cmds[i]);
                worstShort = Mathf.Max(worstShort, (r.Position - history[k + 30].Position).magnitude);
                Assert.AreEqual(history[k + 30].Stance, r.Stance, $"stance diverged replaying from tick {k}");
            }

            // A pathological 5-second replay.
            var longReplay = history[299];
            for (int i = 300; i < cmds.Length; i++) Step(ref longReplay, cmds[i]);
            float worstLong = (longReplay.Position - a.Position).magnitude;

            TestContext.WriteLine($"worst 30-tick replay error {worstShort * 1000f:F3} mm, 300-tick {worstLong * 1000f:F3} mm");
            Debug.Log($"[SALTLINE][TEST] Replay error: worst 30-tick {worstShort * 1000f:F3} mm, 300-tick {worstLong * 1000f:F3} mm");
            Assert.Less(worstShort, 0.002f, "30-tick replays must stay within 2 mm (correction threshold is 20 mm)");
            Assert.Less(worstLong, 0.01f);
        }

        [Test]
        public void FreshRuns_AreIdentical()
        {
            // Two controllers with no history, same inputs: exactly the same path.
            var cmds = new InputCmd[240];
            for (int i = 0; i < cmds.Length; i++) cmds[i] = Cmd(i, Mathf.Sin(i * 0.05f), 1f, i * 1.5f, i % 50 == 10 ? InputButtons.Jump : InputButtons.Sprint);
            Vector3 RunFresh()
            {
                var go = new GameObject("Fresh") { layer = Layers.Player };
                m_Objects.Add(go);
                var cc = go.AddComponent<CharacterController>();
                cc.slopeLimit = m_M.slopeLimit; cc.stepOffset = m_M.stepOffset; cc.skinWidth = m_M.skinWidth;
                var s = MotorState.Spawned(new Vector3(30, 0, 30), m_M);
                foreach (var c in cmds) PlayerMotor.Step(ref s, c, Dt, cc, m_M, PlayerMotor.StepParams.Default);
                go.SetActive(false);
                return s.Position;
            }
            m_Cc.gameObject.SetActive(false);
            Physics.SyncTransforms();
            Vector3 p1 = RunFresh();
            Physics.SyncTransforms();
            Vector3 p2 = RunFresh();
            Assert.AreEqual(p1.x, p2.x, 0f);
            Assert.AreEqual(p1.y, p2.y, 0f);
            Assert.AreEqual(p1.z, p2.z, 0f);
        }
    }
}
