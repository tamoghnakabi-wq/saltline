# Test report: hit registration after a network stall, 2026-11-03

All results below come from runs on 2026-11-03 on one Apple Silicon Mac (M4). Nothing here is estimated. The stall
tests use the development build (0.1.0, protocol 4), because Unity strips the network simulator from release builds.
The other combat tests use the builds named in their rows.

**Not tested yet:**
- A real network stall on separate machines. The stalls here are simulated on the client.
- Several clients stalling at once, and a stall on the host's side.

## 1. The defect

In the Kappa detail pass, one of three combat runs failed after a network stall (`combat_151527`, see
`2026-11-03_M7_KappaDetailPass.md`, section 4). The client confirmed 2 of 14 sniper shots and 13 of 25 pistol shots,
although the round trip was a steady 66 ms by the end. The suspected cause was two delays that a stall leaves behind,
which together push shots past the host's 350 ms rewind limit. That was not confirmed at the time.

## 2. Reproducing it

`Tools/run_relay_tests.sh combatstall` runs the combat test with the client on the development build's network
simulator: 30 ms each way, 5 ms of jitter, no loss. Halfway through the movement phase the client's link stalls for
2 s (`-netstall 2,hold`). Every packet in both directions is held and then arrives together, as on a congested link
or a VPN that briefly loses its route. `STALLMODE=drop` loses the packets instead.

The host now logs, for each phase, the client's input queue depth, how far back the client's shots asked it to
rewind, and how many rewinds it clamped.

Before the fix (`combatstall_hold_160912`):

| Phase | Interpolation delay at start | Client's input queue on the host | Shots asked to rewind | Clamped | Client hits |
|---|---|---|---|---|---|
| Movement (stall in the middle) | 3.0 ticks | avg 3.3, max 9 | | | |
| Sniper | 12.0 ticks (the maximum) | avg 7.6, max 9 | 502 ms on average | 15 of 15 | **0 of 15** |
| Pistol | 12.0 ticks | avg 8.2, max 9 | 489 ms on average | 58 of 58 | **2 of 58** |
| Host shoots | 12.0 ticks | avg 8.2, max 9 | | | |

The interpolation delay was still at 12 ticks when the last phase began, 59 s after the stall ended. At the end of
the run, 77 s after the stall, it had come down to 5.5. This is the failure seen in `combat_151527`, now on demand,
and both suspected causes show in the numbers:

1. **The interpolation delay stayed at its maximum.** The client sizes the delay from the latest snapshot lateness
   it has seen, forgetting it at 1.5 ticks a second. A 2 s stall makes a snapshot about 120 ticks late. The delay is
   capped at 12 ticks (200 ms), but at that rate the 120 ticks take over a minute to be forgotten.
2. **The host's input queue never drained.** The stall delivered a backlog of inputs. The host simulates one input
   per tick and the client sends one per tick, so the backlog stayed at about 8 ticks (130 ms) for the rest of the
   run. Every shot waited that long before the host rewound for it.

200 ms of interpolation, 130 ms in the queue and the trip to the host add up to about 500 ms, past the 350 ms limit.
The host clamps such a rewind to the oldest pose it keeps, where the target no longer is, so the shots missed.

## 3. The fix

- **The client remembers at most 12 ticks of lateness** (`NetClock.OnSnapshotArrived`). No interpolation delay can
  cover a stall, so a stall now counts the same as the worst jitter the delay can absorb. The delay also shrinks at
  3 ticks a second instead of 1.5, during which other players play 5% fast.
- **The host shrinks a queue that stays too deep** (`NetPlayer.ServerConsumeInputs`). When the lowest queue depth
  over half a second is above 2 ticks, the host simulates one extra input now and then, at most 6 a second. The
  inputs are not dropped, so the client's movement and shots are kept and its prediction stays right. A modified
  client sending inputs faster than real time gains at most 10% speed this way. The existing limits still apply:
  a burst bank of 6 inputs, and dropping down to 5 when more than 10 are queued.

## 4. Results after the fix

EditMode tests: 63/63 passed. Both builds (development and release) succeeded with no errors. All runs below
passed on host and client, with no exceptions in any log.

Held stall (`combatstall_hold_161230`):

| Phase | Interpolation delay at start | Client's input queue on the host | Shots asked to rewind | Clamped | Client hits |
|---|---|---|---|---|---|
| Movement (stall in the middle) | 3.0 ticks | avg 1.1, max 5 | | | |
| Sniper | 9.8 ticks | avg 2.4, max 4 | 262 ms on average, at most 343 ms | 0 of 11 | **11 of 11** |
| Pistol | 3.0 ticks | avg 1.9, max 3 | 281 ms on average, at most 324 ms | 0 of 17 | **17 of 17** |
| Host shoots | 4.3 ticks | avg 2.2, max 4 | | | |

The sniper phase began 3.8 s after the stall ended, with the delay still coming down from the stall. Its slowest shot
asked for 343 ms, just inside the limit. By the pistol phase the delay was back at its normal 3 ticks. The host
simulated 2 extra inputs over the run to drain the queue.

Dropped stall (`combatstall_drop_161409`), where the packets are lost instead of held:

| Phase | Interpolation delay at start | Client's input queue on the host | Shots asked to rewind | Clamped | Client hits |
|---|---|---|---|---|---|
| Sniper | 3.0 ticks | avg 0.4, max 3 | 227 ms on average, at most 302 ms | 0 of 11 | **11 of 11** |
| Pistol | 4.0 ticks | avg 2.2, max 4 | 258 ms on average, at most 312 ms | 0 of 18 | **17 of 18** |

Lost packets leave no backlog, so the drop case recovers at once. It was not broken before the fix either; it is
here to show that the fix does not harm it.

The existing combat tests, with the same release build (`combat_161546`, `combatlag_161726`, `combatnolag_161904`):

| Test | Network | Round trip | Sniper hits | Pistol hits | Clamped |
|---|---|---|---|---|---|
| `combat` | Relay only | 53 ms | 11 of 11 | 18 of 18 | 0 |
| `combatlag` | 50 ms each way, 10 ms jitter, 1% loss | 180 ms | 11 of 11 | 17 of 18 | 0 |
| `combatnolag` | as `combatlag`, lag compensation off | 182 ms | 3 of 14 | 2 of 59 | all |

`combatnolag` is a control: without rewinding, shots at a moving target should mostly miss, and they still do. That
the other runs hit is the work of lag compensation, not of a lenient check.

**The stall still moves the player back.** In both stall runs the client's position was corrected once, by 7.3 m
(held) and 7.5 m (dropped). The run before the fix shows the same correction (7.0 m). The host does not wait for a
player whose inputs stop arriving. Otherwise a player could freeze themself on everyone else's screen by holding
back packets. The inputs that arrive too late are skipped, and the client is moved to where the host had it. That
is deliberate and unchanged.

## 5. Known limitations

- **The rewind limit.** A client whose round trip plus interpolation delay is over 350 ms still cannot land hits.
  That limit is deliberate: a larger one lets shots land on players who are already behind cover on the host.
- **Recovery takes a few seconds.** The interpolation delay comes down at 3 ticks a second, so after a long stall it
  takes about 3 s to return to normal. Shots in that time ask the host to rewind further than usual, and on a
  link that is also slow they can still pass the limit.
- **A stall moves the player back.** The host does not wait for missing inputs, so the client is corrected to the
  host's position after the stall, by about 7 m after 2 s of running (section 4).
- **Simulated stalls.** The stalls are made by the simulator on one Mac. A real network can stall in other ways,
  for example by reordering packets or stalling in one direction only.
