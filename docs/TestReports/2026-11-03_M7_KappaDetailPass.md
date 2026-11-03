# Test report: Kappa environment detail pass (graphics overhaul), 2026-11-03

All results below come from runs on 2026-11-03 on one Apple Silicon Mac (M4), using the macOS release build (0.1.0,
protocol 3). Nothing here is estimated.

**Not tested yet:**
- Windows.
- Separate machines.
- A human playing the map with mouse and keyboard. The new props were reviewed in still captures only.
- Hit registration on the network after a stall is not fixed: one of three combat runs failed (section 4).
- Frame rate with eight players on Kappa. The render times below are CPU submit times, not frame rates.

## 1. What was built

The Kappa map kept its layout, lanes and heights. What changed is how its props and buildings are modelled and
dressed. Everything is still generated from Python in Blender (through the Blender MCP connection), and nothing was
copied from another game.

- **Prop library.** `ArtSource/Blender/kappa_props.py` is new. It holds chamfered boxes, lathes, extruded profiles
  and catenaries, and builds these props from them:
  - **20-foot containers.** Corner posts and castings, rails, door leaves with locking bars and hinges, and forklift
    pockets. They come in red, blue, white, green and orange, and keep their old footprints.
  - **Trucks.** A cab-over truck with a hopper and a dump truck. Each has a ladder-frame chassis, wheels with tyres,
    rims and hubs, a fuel tank, mudguards, lamps, mirrors and glass. The underside is open, so players can see and
    shoot between the wheels.
  - **A tracked salt harvester.** Its body sits 1.5 m above the tracks, so it cannot be climbed.
  - **Small props:**
    - woven bulk bags on pallets, which replace the old sack stacks;
    - framed crates and pallet stacks;
    - IBC tanks in cages;
    - drums with rolling hoops, a cable drum and traffic cones.
- **Buildings.**
  - Windows with frames, mullions and sills; doors with frames, handles and canopies; and downpipes and plinths.
  - Concrete copings on walls.
  - Roller shutters with guides and bump posts in the warehouse faces.
  - Parapets on the control room and offices.
- **Around the map:**
  - caged ladders, roof rails, manways and nozzles on the storage tanks;
  - street lamps and a wooden power line on the hillside;
  - a steel palisade on the north wall;
  - drains, manholes, puddles, parking lines and a weighbridge plate.
- **Materials.** There are 16 new map materials:
  - five container paints;
  - cast concrete, a roller shutter, glass, rubber, white, grey and orange paint, blue drums, plastic, woven bags and
    puddles.

  The container paints share one neutral-grey copy of the CC0 container scan, made by
  `ArtSource/Textures/derive_container_paint.py`. Each material's tint is the paint. The bag texture is generated
  (`gen_kappa_textures.py`, `bag()`).
- **Review views.** The scene has six new map-tour viewpoints (`18_detail_truck` to `23_detail_shutter`), close
  to the props.

| | Before (M6) | After |
|---|---|---|
| Objects in the FBX | 232 | 514 |
| Triangles | 27,862 | 176,830 |
| Collidable objects | 168 | 240 |
| Map materials | 25 | 41 |
| macOS build size | 503.0 MB | 554.5 MB |

Selected captures are in `docs/TestReports/M7_kappa_detail/`. `before_after.jpg` shows six views from the last
milestone's build next to this one.

## 2. EditMode tests: 56/56 passed

Results file: `editmode_detail7.xml`, outside the repo. The 55 earlier tests still pass. The map tests ran against
the rebuilt scene and its new colliders:

| Test | Result |
|---|---|
| `LanesConnectOnFoot` | Pass. 101,637 of 163,459 standable cells reachable on foot, for both teams. The figures were 102,100 of 159,677 before. Props take up some floor, and their tops add standable cells that nobody can reach. |
| `MotorWalksFromSpawnToEveryLane` | Pass, 12 routes. The times are unchanged: dock 12.1 s, pump-house upper floor 13.4 s, shed 7.6 s. |
| `NothingOutOfBoundsIsReachableEvenWithJumps` | Pass. With jumps (1.36 m), no roof, container, truck cab, hopper, tank or building is reachable. The highest reachable floor is still 3.70 m, and the only floor reachable just by jumping is still the top of a salt pile. |
| `SpawnsAreHiddenFromEverywhereOutsideTheirYard` | Pass, 395,216 sight-line checks. |
| `SpawnPointsStandOnFloorWithRoom`, `CollidersHaveSurfacesAndClipsAreInvisible` | Pass |
| `OnlyLampsAreEmissive` (new) | Pass. The lamp material has `_EMISSION` and a strong emission colour, and no other map material emits. |

## 3. Bake and map tour

- **Bake:** 10–15 s, 2 lightmaps and 4,342 light probes.
- **Unity's exclusions:** Unity leaves 53 meshes out of the bake. They are all small probe-lit details made of many
  tiny pieces spread over a wide area: bag loops, pallet slats, fence pales, drain grates, hubs and cones. Unity
  cannot lay out lightmap UVs for them. They are still lit by light probes and cast realtime sun shadows within 40 m
  (distance shadowmask), but they do not block baked lamp light. The bake log now counts them in one line.
- **Map tour:** the built game rendered all 24 views at 1600×900 with no exceptions. Each `Camera.Render` took
  0.5–1.7 ms; it was 0.5–1.0 ms before. The overview shots cost the most. These are CPU submit times only.

Issues found by looking at the captures, and fixed in this milestone:
1. **The lamps had stopped glowing.** The lamp panels rendered as dull boxes, and the spawn canopy and shed lost
   their light pools.
   - **Cause:** the lamp material had no emission GI flag (`None`). Whenever the baked scene was loaded, by the bake
     or by the tests, Unity's material validation removed its `_EMISSION` keyword.
   - **Why it was missed:** earlier milestones always ran a setup step after the tests and before building, which put
     the keyword back. This milestone's run order exposed it.
   - **Fix:** the lamp now uses the `Realtime` emission flag, as the URP material inspector would set it. The project
     has no realtime GI, so the flag changes nothing else.
   - **Checks:** the keyword now survives the bake, the tests and the build. Every player build also checks it before
     building, and the new test fails if it is missing. The test did catch it once, after an unrelated setup crash
     (see section 5).
2. **The cast-concrete scan was yellow.** Its linear mean is 0.27, 0.24, 0.16, so the pump house and yard walls
   looked beige. A tint brings it back to neutral grey.
3. **Puddles read as black holes from above.** The water texture is white, so their albedo came from the tint
   alone: linear 0.02, a sixth of the asphalt's. They now have about half the asphalt's albedo.
4. **Window glass was flat black.** The glass is now partly metallic and lighter, so the sky and nearby buildings
   show in it. Panes facing shaded walls are still dark, which is plausible but not proven against a reference.

## 4. Live match over Relay

Both tests ran the same macOS build as two processes on this Mac, joined through a real Relay allocation (not a
local connection). Cloudflare WARP (a VPN) was running throughout. Logs are in `AutoTest/`, outside the repo.

| Run | Result | Notes |
|---|---|---|
| `ui` (`ui_151442`) | PASS, host and client | 19 captures, menus to results and back to the lobby |
| `combat` 1 (`combat_151527`) | **FAIL** (client) | sniper 2/14 hits confirmed, pistol 13/25, 0 kills by the client. 155 inputs dropped by the host, 31 of 61 lag-compensation rewinds clamped, 169 extrapolated frames, 2 movement corrections (the last 1.71 m). The host's own shots were 22/22. |
| `combat` 2 (`combat_151933`) | PASS | sniper 11/11, pistol 18/18; 0 inputs dropped, 0 rewinds clamped; round trip 66.3 ms average |
| `combat` 3 (`combat_152117`) | PASS | sniper 11/11, pistol 18/18; 7 inputs dropped, 0 rewinds clamped; round trip 102.8 ms average with spikes to 349.6 ms |

No exceptions in any of the eight player logs.

**What the failed run shows.** The combat test runs on the Arena test map, which this milestone did not change, and
the test code did not change either. The failure is in the network path, not in the map:
- The client's snapshots stopped arriving for a while: it extrapolated other players for 169 frames, against 0–13
  in the passing runs.
- Its inputs then reached the host in a burst, and the host dropped 155 of them.
- Half of the client's shots were older than the 0.35 s rewind window when the host checked them. The host clamps
  those to the oldest pose it keeps, so they missed.

The round trips measured at the end of that run were a steady 66 ms, so the stall was over by then. What is not
explained is why the client kept missing for most of the 33-second sniper phase after one stall.

**A likely cause, not yet confirmed.** Two delays add up after a stall:
- The host trims its input queue only when it holds more than 10 ticks, and then only down to 5. It takes one input
  per tick and receives one per tick, so a deep queue does not drain by itself. It can stay 5–10 ticks (83–167 ms)
  behind indefinitely.
- The client's interpolation delay grows to as much as 12 ticks (0.2 s) after a lateness spike, and shrinks back by
  only 1.5 ticks per second.

Together with the one-way trip to the host, these can exceed 0.35 s. This is the next thing to test, with the
development build's network simulator. Until then, a network stall can leave a client unable to land hits for tens of
seconds. That is an open defect, not a pass.

## 5. Problems in the tools

- **One setup run crashed.** Unity's Burst compiler crashed (SIGSEGV in a Burst compiler thread) while setup was
  importing animation clips, before the map step. A re-run of the same command succeeded. Nothing in this project
  calls Burst directly. It looks like an intermittent editor crash, but that is a guess from one occurrence.

## 6. Known limitations

- **Generated, not sculpted.** The props are built from chamfered boxes, lathes and extrusions. They have bevelled
  edges, real part breakup and scanned or generated PBR textures, but no sculpted damage, decals, dirt masks or
  baked per-prop ambient occlusion. Up close they read as clean game models, not photoscans.
- **No vehicle interiors.** The cab interiors are not modelled; the dark glass hides them.
- **Uniform dressing.** The dressing is mirror-symmetric like the map, so both sides have the same props in the same
  places, apart from the container and truck colours.
- **Larger build.** The build grew by 51.5 MB. The map's mesh data, the new textures and the lamp's emission shader
  variants were not measured separately.
- **LODs and frame rate.** There are no LODs on the props yet, and no frame-rate measurement with players. Both are
  part of the presets and performance step that comes next.
- **Hit registration after a network stall.** One of three combat runs failed after a stall (section 4). The cause is
  not confirmed yet.
