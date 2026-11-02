# Test report — Milestone 2 (core combat), 2026-11-02

All results below come from runs on this date. Nothing here is estimated.

**Hardware:** one Apple Silicon Mac. Host and client were separate game processes on that one machine.
**Network:** they talked only through **Unity Relay** (transport `RelayUnityTransport`), each signed in as its own anonymous Unity player.
**Not tested yet:** Windows players, and players on separate machines or networks.

## 1. EditMode unit tests — 41/41 passed

Run with `Unity -batchmode -runTests -testPlatform EditMode`. The results file is `TestResults/editmode.xml`, outside the repo.

| Area | Tests | What is checked |
|---|---|---|
| Ray vs. capsule math | 6 | cylinder/cap hits, misses, origin inside, 500 random cases vs. brute force (≤ 2 mm) |
| Hitbox model | 3 | head/chest/abdomen/leg regions, crouch lowers head, broad phase never rejects a real hit (4 000 rays) |
| Damage | 3 | falloff curve, sniper headshot kills / leg shot does not, shotgun close-range kill, rounding |
| Input quantization | 3 | yaw/pitch round trip within half a step, pitch clamp, move axes |
| Lag compensation | 4 | sub-tick interpolation, rewind cap, future clamp, stats opt-out, alive state per tick |
| Weapon simulation | 13 | equip time, AR fire rate vs. RPM, semi-auto one shot per pull, empty reload, reload cancel, per-shell reload timing and interruption, bolt cycling, weapon switch timing, sprint-to-fire block, 1 200-tick determinism, pellet cone + seeding, hash uniformity |
| Movement (PhysX) | 9 | run/sprint/crouch speeds, jump apex vs. physics, wall blocks movement, 0.3 m step climb, slide start/end, replay error, identical fresh runs |

### Finding: movement replays are not bit-exact

PhysX's `CharacterController` keeps some contact state between moves that our saved state does not include. Replaying the same inputs from a saved state therefore lands slightly off.

| Replay | Worst position error |
|---|---|
| 30 ticks | **0.133 mm** |
| 300 ticks | **0.227 mm** |

The reconciliation threshold is 20 mm, so this has no effect in play. The test now checks for "below 2 mm" instead of an exact match.

### Fix made during testing

`PlayerMotor` only set the capsule's center when the height changed. A controller that started at the correct height but with the default center would sit half underground.

- The game's player prefab was already correct, so the game was not affected.
- The motor now checks the center as well.

## 2. Combat over real Unity Relay — `Tools/run_relay_tests.sh gameplay`

Each run starts two headless processes with scripted bots on both sides:

1. The client moves through a scripted path: sprint, slide, crouch, jump, strafe, and ADS.
2. It shoots the strafing host player with the sniper rifle.
3. It switches to the pistol and shoots again.
4. Then the host shoots the strafing client with the assault rifle.

The host's own hits and kills come from the authoritative simulation. The client's results come from server confirmations of its shots.

To collect enough shots per phase, the host used a 2 s respawn delay and 1 s spawn protection, instead of the defaults of 5 s and 2 s.

| Run | Client latency (simulated) | Measured RTT | Movement corrections | Sniper hits | Pistol hits | Rewinds clamped | Result |
|---|---|---|---|---|---|---|---|
| `combat_201905` | none | 58.1 ms | 0 / 529 ticks | 11/11 | 18/18 | 0 / 50 | PASS |
| `combatlag_202437` | +50 ms each way, 10 ms jitter, 1 % loss | 181.0 ms | 0 / 528 ticks | 11/11 | 17/17 | 0 / 49 | PASS |
| `combatnolag_202614` (control: lag comp **off**) | same as above | 179.5 ms | 0 / 529 ticks | **0/15** | **2/58** | 94 / 94 (window 0) | PASS (misses expected) |

In every run the host's own rifle shots at the strafing client hit 21 of 21.

**Other checks that held in every run:**
- Kill and death counts in the roster matched.
- The client was killed 4–5 times and respawned each time.
- The host processed 5 000+ client inputs: none skipped; none dropped in the three runs above.

The control run is the key evidence. It used the same bot, the same latency and the same aim code, with lag compensation disabled on the host. Hits fell from 100 % to 0–3 %.

### High-latency run (cap behaviour, recorded as-is)

`combatlag_202045` added 120 ms each way (315 ms measured RTT) and **failed** the 85 % hit threshold:

- Sniper: 5/13 hits (38 %).
- Pistol: 13/28 hits (46 %).
- Rewinds clamped: 37 of 58.

At that latency the rewind needed (about RTT plus interpolation, ~365 ms) is over the host's 350 ms rewind cap. The cap is deliberate: it keeps a very high-latency shooter from hitting victims long after they reach cover. The cost is that players above roughly 290 ms RTT must lead moving targets. This is listed as a known limitation.

### Invalid runs (excluded)

`combatlag_201328` and `combatnolag_201504` passed, but the latency simulator was **not active**: the build was a release build, which strips the simulator.

- They are excluded from the results above.
- The test framework now **fails** any run that requests `-netsim` without an active simulator.
- Latency tests now use a separate development build (`Builds/macOS-dev`).

The earlier Milestone 1 latency test (`latency_191127`, 150 ms, measured 380 ms RTT) was checked: it ran on a development build with the simulator on.

## Not covered by this report

- Windows players, and Windows ↔ macOS sessions. There is no Windows hardware in this environment.
- More than two players in combat. The 8-player capacity test from Milestone 1 checks connections only, not combat.
- Rendering, frame rate, and the human first- and third-person camera feel. All runs here were headless.
