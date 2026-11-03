using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Saltline.Combat;
using Saltline.Match;
using Saltline.Net;
using Saltline.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Saltline.Tests
{
    /// <summary>
    /// Checks the built Kappa scene with physics queries against its real colliders: a walkable
    /// grid (25 cm) is flood-filled from each spawn using the movement rules (step height, slope
    /// limit, body clearance) to prove the lanes connect, the bounds hold, and spawns are hidden.
    /// </summary>
    public class KappaMapTests
    {
        const string ScenePath = "Assets/_Saltline/Scenes/Kappa.unity";
        const float Cell = 0.25f;
        const float MinX = -62f, MaxX = 62f, MinZ = -40f, MaxZ = 40f;
        const float BoundX = 58.2f, BoundZ = 36.3f;
        const float Eye = 1.62f;

        struct Node
        {
            public int ix, iz;
            public float h;
            public string floor;
        }

        static MotorSettings s_Motor;
        static List<Node> s_Nodes;
        static Dictionary<(int, int), List<int>> s_Columns;
        static List<SpawnPoint> s_Spawns;
        static string s_Skip;

        [OneTimeSetUp]
        public void Setup()
        {
            if (!File.Exists(ScenePath)) { s_Skip = "Kappa scene not built"; return; }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            s_Motor = AssetDatabase.LoadAssetAtPath<MotorSettings>("Assets/_Saltline/Data/MotorSettings.asset") ?? MotorSettings.CreateDefault();
            s_Spawns = Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.InstanceID).ToList();
            BuildGrid();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void SkipIfMissing()
        {
            if (s_Skip != null) Assert.Ignore(s_Skip);
        }

        // ------------------------------------------------------------------ grid

        static bool BodyFits(Vector3 feet)
        {
            // Body above knee height (step height) must be free, like a character controller stepping.
            float r = s_Motor.radius;
            var a = feet + Vector3.up * (s_Motor.stepOffset + r + 0.02f);
            var b = feet + Vector3.up * (s_Motor.standHeight - r);
            return !Physics.CheckCapsule(a, b, r, Layers.MovementMask, QueryTriggerInteraction.Ignore);
        }

        static void BuildGrid()
        {
            s_Nodes = new List<Node>();
            s_Columns = new Dictionary<(int, int), List<int>>();
            float minNormalY = Mathf.Cos(s_Motor.slopeLimit * Mathf.Deg2Rad);
            int nx = Mathf.RoundToInt((MaxX - MinX) / Cell), nz = Mathf.RoundToInt((MaxZ - MinZ) / Cell);
            for (int ix = 0; ix <= nx; ix++)
            for (int iz = 0; iz <= nz; iz++)
            {
                float x = MinX + ix * Cell, z = MinZ + iz * Cell;
                float top = 20f;
                for (int k = 0; k < 10; k++)
                {
                    if (!Physics.Raycast(new Vector3(x, top, z), Vector3.down, out var hit, top + 10f, Layers.MovementMask, QueryTriggerInteraction.Ignore))
                        break;
                    top = hit.point.y - 0.02f;
                    if (hit.normal.y < minNormalY) continue;
                    var feet = new Vector3(x, hit.point.y, z);
                    if (!BodyFits(feet)) continue;
                    if (!s_Columns.TryGetValue((ix, iz), out var list)) s_Columns[(ix, iz)] = list = new List<int>();
                    list.Add(s_Nodes.Count);
                    s_Nodes.Add(new Node { ix = ix, iz = iz, h = hit.point.y, floor = hit.collider.name });
                }
            }
        }

        static Vector3 Pos(in Node n) => new Vector3(MinX + n.ix * Cell, n.h, MinZ + n.iz * Cell);

        /// <summary>Flood fill. maxUp = highest ledge that can be climbed (step height, or a jump).</summary>
        static HashSet<int> Reach(IEnumerable<int> starts, float maxUp)
        {
            var seen = new HashSet<int>(starts);
            var queue = new Queue<int>(seen);
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                var n = s_Nodes[id];
                foreach (var (dx, dz) in dirs)
                {
                    if (!s_Columns.TryGetValue((n.ix + dx, n.iz + dz), out var col)) continue;
                    // The floor we land on is the highest one in that column we can get onto.
                    int best = -1;
                    float bestH = float.MinValue;
                    foreach (int c in col)
                    {
                        float h = s_Nodes[c].h;
                        if (h <= n.h + maxUp && h > bestH) { best = c; bestH = h; }
                    }
                    if (best < 0 || seen.Contains(best)) continue;
                    // Walking off a ledge: the body must fit at the old height above the new column.
                    if (bestH < n.h - s_Motor.stepOffset)
                    {
                        var over = Pos(s_Nodes[best]);
                        over.y = n.h;
                        if (!BodyFits(over)) continue;
                    }
                    seen.Add(best);
                    queue.Enqueue(best);
                }
            }
            return seen;
        }

        static List<int> NodesNear(Vector3 p)
        {
            int ix = Mathf.RoundToInt((p.x - MinX) / Cell), iz = Mathf.RoundToInt((p.z - MinZ) / Cell);
            var result = new List<int>();
            if (s_Columns.TryGetValue((ix, iz), out var col))
                foreach (int c in col)
                    if (Mathf.Abs(s_Nodes[c].h - p.y) < 0.5f) result.Add(c);
            return result;
        }

        static HashSet<int> WalkFrom(Team team, float maxUp)
        {
            var starts = s_Spawns.Where(s => s.team == team).SelectMany(s => NodesNear(s.transform.position)).ToList();
            Assert.IsNotEmpty(starts, $"no walkable cell at any {team} spawn");
            return Reach(starts, maxUp);
        }

        /// <summary>Breadth-first search like Reach, but keeps parents so a route can be read back.</summary>
        static Dictionary<int, int> Routes(int start, float maxUp, System.Func<int, bool> allowed, bool bodyWidthSteps = false)
        {
            var parent = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                var n = s_Nodes[id];
                foreach (var (dx, dz) in dirs)
                {
                    if (!s_Columns.TryGetValue((n.ix + dx, n.iz + dz), out var col)) continue;
                    int best = -1;
                    float bestH = float.MinValue;
                    foreach (int c in col)
                    {
                        float h = s_Nodes[c].h;
                        if (h <= n.h + maxUp && h > bestH) { best = c; bestH = h; }
                    }
                    if (best < 0 || parent.ContainsKey(best) || !allowed(best)) continue;
                    if (bestH < n.h - s_Motor.stepOffset)
                    {
                        var over = Pos(s_Nodes[best]);
                        over.y = n.h;
                        if (!BodyFits(over)) continue;
                    }
                    if (bodyWidthSteps && bestH > n.h + 0.02f && !StepClearAcrossBody(n, s_Nodes[best], dx, dz)) continue;
                    parent[best] = id;
                    queue.Enqueue(best);
                }
            }
            return parent;
        }

        /// <summary>
        /// Stepping up from a onto b: the whole width of the body meets the step, not just its
        /// centre line. Fails if anything across the body is above a climbable step (for example
        /// walking onto the side of a staircase, where the next tread is right beside this one).
        /// </summary>
        static bool StepClearAcrossBody(in Node a, in Node b, int dx, int dz)
        {
            var side = new Vector3(dz, 0, -dx);
            var c = Pos(b);
            foreach (float o in new[] { -0.4f, -0.2f, 0.2f, 0.4f })
            {
                var top = c + side * o;
                top.y = a.h + 1.0f;
                if (Physics.Raycast(top, Vector3.down, out var hit, 1.0f - s_Motor.stepOffset, Layers.MovementMask, QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }

        static List<int> PathTo(Dictionary<int, int> parent, int goal)
        {
            var path = new List<int>();
            for (int at = goal; at >= 0; at = parent[at]) path.Add(at);
            path.Reverse();
            return path;
        }

        // ------------------------------------------------------------------ tests

        [Test]
        public void SpawnPointsStandOnFloorWithRoom()
        {
            SkipIfMissing();
            Assert.AreEqual(8, s_Spawns.Count(s => s.team == Team.Red), "Red spawn count");
            Assert.AreEqual(8, s_Spawns.Count(s => s.team == Team.Blue), "Blue spawn count");
            foreach (var s in s_Spawns)
            {
                var p = s.transform.position;
                Assert.IsTrue(Physics.Raycast(p + Vector3.up, Vector3.down, out var hit, 1.3f, Layers.EnvironmentMask), $"{s.name}: no floor below");
                Assert.Less(p.y - hit.point.y, 0.2f, $"{s.name}: floating {p.y - hit.point.y:F2} m");
                Assert.IsTrue(BodyFits(hit.point), $"{s.name}: body does not fit");
                // A spawning player looks at open space (the way out), not into a wall.
                var eye = hit.point + Vector3.up * Eye;
                bool blocked = Physics.Raycast(eye, s.transform.forward, out var wall, 5f, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
                Assert.IsFalse(blocked, $"{s.name} faces {(blocked ? wall.collider.name : "")} {wall.distance:F1} m away");
            }
            // Mirror symmetry: every Red spawn has a Blue twin, facing the mirrored way.
            foreach (var r in s_Spawns.Where(s => s.team == Team.Red))
            {
                var m = r.transform.position;
                m.x = -m.x;
                var f = r.transform.forward;
                f.x = -f.x;
                Assert.IsTrue(s_Spawns.Any(b => b.team == Team.Blue && (b.transform.position - m).sqrMagnitude < 0.01f && Vector3.Dot(b.transform.forward, f) > 0.999f),
                    $"{r.name} has no mirrored Blue spawn");
            }
        }

        [Test]
        public void LanesConnectOnFoot()
        {
            SkipIfMissing();
            string[] mustReach = { "Ground_Pans", "Catwalk_Deck", "Channel_Floor", "Pump_Floor", "Pump_Stairs", "Pump_Slab", "Shed_Floor", "Dock_Platform", "Ground_Asphalt" };
            foreach (var team in new[] { Team.Red, Team.Blue })
            {
                var reach = WalkFrom(team, s_Motor.stepOffset);
                var floors = new HashSet<string>(reach.Select(i => s_Nodes[i].floor));
                foreach (string f in mustReach)
                    Assert.IsTrue(floors.Any(n => n.StartsWith(f)), $"{team} cannot walk to {f}. Reached: {string.Join(", ", floors.OrderBy(x => x))}");
                var enemy = team == Team.Red ? Team.Blue : Team.Red;
                foreach (var s in s_Spawns.Where(s => s.team == enemy))
                    Assert.IsTrue(NodesNear(s.transform.position).Any(reach.Contains), $"{team} cannot walk to {s.name}");
                TestContext.WriteLine($"{team}: {reach.Count} of {s_Nodes.Count} standable cells reachable on foot");
            }
        }

        /// <summary>
        /// The grid is a model; this drives the real movement code (PlayerMotor + CharacterController,
        /// the same step the network simulation runs) along routes found on the grid, from a spawn to
        /// each lane's key floor, and checks the player actually arrives there.
        /// </summary>
        [Test]
        public void MotorWalksFromSpawnToEveryLane()
        {
            SkipIfMissing();
            string[] targets = { "Pump_Slab", "Catwalk_Deck", "Channel_Floor", "Dock_Platform", "Shed_Floor", "Ground_Pans" };
            var go = new GameObject("RouteWalker") { layer = Layers.Player };
            try
            {
                var cc = go.AddComponent<CharacterController>();
                cc.slopeLimit = s_Motor.slopeLimit;
                cc.stepOffset = s_Motor.stepOffset;
                cc.skinWidth = s_Motor.skinWidth;
                cc.radius = s_Motor.radius;
                cc.height = s_Motor.standHeight;
                // Prefer routes with a little room either side and floor under the whole footprint
                // (the grid samples one ray per cell, so a cell on the open side edge of a stair
                // counts as floor even though a body centred there would slide off).
                var roomy = new Dictionary<int, bool>();
                bool Roomy(int id)
                {
                    if (roomy.TryGetValue(id, out bool ok)) return ok;
                    var f = Pos(s_Nodes[id]);
                    // The extra width is checked from 1 m up: it is about walls and posts, and lower
                    // down a wider body would clip the next stair treads.
                    float r = s_Motor.radius + 0.15f;
                    ok = !Physics.CheckCapsule(f + Vector3.up * 1.0f, f + Vector3.up * (s_Motor.standHeight - r), r,
                        Layers.MovementMask, QueryTriggerInteraction.Ignore);
                    const float o = 0.3f;
                    foreach (var off in new[] { new Vector3(o, 0, 0), new Vector3(-o, 0, 0), new Vector3(0, 0, o), new Vector3(0, 0, -o) })
                    {
                        if (!ok) break;
                        ok = Physics.Raycast(f + off + Vector3.up * 0.5f, Vector3.down, out var h, 0.5f + s_Motor.stepOffset, Layers.MovementMask, QueryTriggerInteraction.Ignore);
                    }
                    return roomy[id] = ok;
                }

                var lines = new List<string>();
                foreach (var team in new[] { Team.Red, Team.Blue })
                {
                    var spawn = s_Spawns.Where(sp => sp.team == team).OrderBy(sp => sp.name).First();
                    int start = NodesNear(spawn.transform.position).First();
                    var wide = Routes(start, s_Motor.stepOffset, Roomy, bodyWidthSteps: true);
                    var any = Routes(start, s_Motor.stepOffset, _ => true);
                    foreach (string target in targets)
                    {
                        var parent = wide.Keys.Any(i => s_Nodes[i].floor.StartsWith(target)) ? wide : any;
                        var depth = new Dictionary<int, int>();
                        int Depth(int id) => depth.TryGetValue(id, out int d) ? d : (depth[id] = parent[id] < 0 ? 0 : Depth(parent[id]) + 1);
                        var onTarget = parent.Keys.Where(i => s_Nodes[i].floor.StartsWith(target)).ToList();
                        Assert.IsNotEmpty(onTarget, $"{team}: grid finds no route to {target}");
                        // Aim about 2 m past where the route first reaches the floor, not its edge.
                        int first = onTarget.Min(Depth);
                        int goal = onTarget.OrderBy(i => Mathf.Abs(Depth(i) - (first + 8))).First();
                        var path = PathTo(parent, goal);

                        var s = MotorState.Spawned(spawn.transform.position, s_Motor);
                        int wp = 0, tick = 0, lastProgress = 0;
                        while (wp < path.Count)
                        {
                            var p = Pos(s_Nodes[path[wp]]);
                            var d = p - s.Position;
                            d.y = 0;
                            if (d.magnitude < 0.45f && Mathf.Abs(p.y - s.Position.y) < 0.6f)
                            {
                                wp = Mathf.Min(wp + 4, wp == path.Count - 1 ? path.Count : path.Count - 1);
                                lastProgress = tick;
                                continue;
                            }
                            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                            var cmd = new InputCmd { Tick = tick, MoveX = 0, MoveY = InputCmd.QuantizeAxis(1f), Yaw = InputCmd.QuantizeYaw(yaw) };
                            PlayerMotor.Step(ref s, cmd, 1f / 60f, cc, s_Motor, PlayerMotor.StepParams.Default);
                            tick++;
                            if (tick - lastProgress > 240)
                                Assert.Fail($"{team} -> {target}: stuck at {s.Position} (grounded {s.Grounded}) heading for {p}, waypoint {wp}/{path.Count}, " +
                                            (parent == wide ? "roomy route" : $"narrow route; roomy routes reach: {string.Join(", ", wide.Keys.Select(i => s_Nodes[i].floor).Distinct().OrderBy(x => x))}") +
                                            $"; route tail: {string.Join(" ", path.Skip(System.Math.Max(0, wp - 12)).Take(20).Select(i => $"({Pos(s_Nodes[i]).x:F2},{s_Nodes[i].h:F2},{Pos(s_Nodes[i]).z:F2})"))}");
                        }
                        Assert.IsTrue(Physics.Raycast(s.Position + Vector3.up * 0.5f, Vector3.down, out var hit, 1.5f, Layers.MovementMask, QueryTriggerInteraction.Ignore),
                            $"{team} -> {target}: no floor under the player at the end");
                        Assert.IsTrue(hit.collider.name.StartsWith(target), $"{team} -> {target}: ended on {hit.collider.name} at {s.Position}");
                        lines.Add($"{team} -> {target}: {path.Count * Cell:F0} m route, {tick / 60f:F1} s, ended at y={s.Position.y:F2} on {hit.collider.name}{(parent == wide ? "" : " (narrow route)")}");
                    }
                }
                TestContext.WriteLine(string.Join("\n", lines));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NothingOutOfBoundsIsReachableEvenWithJumps()
        {
            SkipIfMissing();
            // Jump apex from the motor settings, plus the step height for a ledge grab margin.
            float apex = s_Motor.jumpVelocity * s_Motor.jumpVelocity / (2f * s_Motor.gravity);
            float maxUp = apex + s_Motor.stepOffset;
            foreach (var team in new[] { Team.Red, Team.Blue })
            {
                var reach = WalkFrom(team, maxUp);
                var outside = reach.Select(i => s_Nodes[i]).Where(n => { var p = Pos(n); return Mathf.Abs(p.x) > BoundX || Mathf.Abs(p.z) > BoundZ; }).ToList();
                Assert.IsEmpty(outside, $"{team} reaches {outside.Count} cells out of bounds, e.g. " +
                    string.Join("; ", outside.Take(5).Select(n => $"{Pos(n)} on {n.floor}")));
                // Roofs, walls and machinery tops must stay out of reach (crates and sacks may be climbed).
                string[] forbidden = { "Pump_Roof", "Pump_Walls", "Shed_Roof", "Shed_Walls", "Office", "Hut_Roof", "Hut_Walls", "Control_Roof",
                    "Weighbridge", "PumpShed", "Yard_Canopy", "Yard_Walls", "Tank", "Truck_Hopper", "Truck_Cab", "DumpTruck_Bed", "DumpTruck_Cab",
                    "Boundary_North", "Seawall", "Warehouse", "Divider", "Containers", "Crane_Base", "Sluice", "Pump_Annex", "Alcove_Divider", "HTank" };
                var bad = reach.Where(i => forbidden.Any(f => s_Nodes[i].floor.StartsWith(f))).Select(i => s_Nodes[i]).ToList();
                Assert.IsEmpty(bad, $"{team} can climb onto {string.Join(", ", bad.Select(n => n.floor).Distinct())}, e.g. " +
                    string.Join("; ", bad.Take(5).Select(n => $"{Pos(n)} on {n.floor}")));
                float maxH = reach.Max(i => s_Nodes[i].h);
                var extra = new HashSet<string>(reach.Select(i => s_Nodes[i].floor));
                extra.ExceptWith(WalkFrom(team, s_Motor.stepOffset).Select(i => s_Nodes[i].floor));
                TestContext.WriteLine($"{team}: {reach.Count} cells reachable with jumps (climb {maxUp:F2} m), highest floor {maxH:F2} m; " +
                    $"only by jumping: {string.Join(", ", extra.OrderBy(x => x))}");
            }
        }

        [Test]
        public void SpawnsAreHiddenFromEverywhereOutsideTheirYard()
        {
            SkipIfMissing();
            var reachable = WalkFrom(Team.Red, s_Motor.stepOffset);
            reachable.UnionWith(WalkFrom(Team.Blue, s_Motor.stepOffset));
            var sample = reachable.Where(i => (s_Nodes[i].ix % 2 == 0) && (s_Nodes[i].iz % 2 == 0)).Select(i => s_Nodes[i]).ToList();
            var problems = new List<string>();
            int checks = 0;
            foreach (var s in s_Spawns)
            {
                var eye = s.transform.position + Vector3.up * Eye;
                float side = Mathf.Sign(eye.x);
                foreach (var n in sample)
                {
                    var p = Pos(n);
                    bool insideOwnYard = Mathf.Sign(p.x) == side && Mathf.Abs(p.x) > 44.05f && Mathf.Abs(p.z) < 12.0f;
                    if (insideOwnYard) continue;
                    checks++;
                    var target = p + Vector3.up * Eye;
                    if (!Physics.Linecast(eye, target, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                        problems.Add($"{s.name} seen from {target} ({n.floor}, {Vector3.Distance(eye, target):F0} m)");
                }
            }
            TestContext.WriteLine($"{checks} spawn-to-position sight checks");
            Assert.IsEmpty(problems, $"{problems.Count} positions can see a spawn, e.g.\n" +
                string.Join("\n", problems.Where((x, i) => i % System.Math.Max(1, problems.Count / 15) == 0).Take(15)));
        }

        [Test]
        public void CollidersHaveSurfacesAndClipsAreInvisible()
        {
            SkipIfMissing();
            int tagged = 0;
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                Assert.IsNotNull(c.sharedMesh, $"{c.name}: collider without mesh");
                if (c.gameObject.layer == Layers.PlayerClip)
                {
                    Assert.IsNull(c.GetComponent<Renderer>(), $"{c.name}: player clip is visible");
                    Assert.IsNull(c.GetComponent<SurfaceTag>(), $"{c.name}: player clip has a surface");
                    continue;
                }
                Assert.AreEqual(Layers.Environment, c.gameObject.layer, $"{c.name}: wrong layer");
                Assert.IsNotNull(c.GetComponent<SurfaceTag>(), $"{c.name}: no SurfaceTag");
                tagged++;
            }
            Assert.Greater(tagged, 100);
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                Assert.IsNotNull(m, $"{r.name}: no material");
                Assert.IsTrue(AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Saltline/Art/Maps/Kappa/Materials/"), $"{r.name}: material {m.name} is not a Kappa material");
                Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, $"{m.name}: wrong shader");
            }
        }

        /// <summary>The lamp panels must glow, and every other map material must have no emission.</summary>
        [Test]
        public void OnlyLampsAreEmissive()
        {
            SkipIfMissing();
            var lamp = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Saltline/Art/Maps/Kappa/Materials/M_Lamp.mat");
            Assert.IsNotNull(lamp, "M_Lamp missing");
            Assert.IsTrue(lamp.IsKeywordEnabled("_EMISSION"), "M_Lamp has no _EMISSION keyword: the lamps render unlit");
            Assert.Greater(lamp.GetColor("_EmissionColor").maxColorComponent, 1f, "M_Lamp emission too weak");
            var others = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Select(r => r.sharedMaterial).Where(m => m != null && m != lamp && m.IsKeywordEnabled("_EMISSION"))
                .Select(m => m.name).Distinct().ToList();
            Assert.IsEmpty(others, $"unexpected emissive materials: {string.Join(", ", others)}");
        }
    }
}
