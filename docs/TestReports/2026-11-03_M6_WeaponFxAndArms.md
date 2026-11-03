# Test report: weapon effects and first-person arms (graphics overhaul), 2026-11-03

All results below come from runs on 2026-11-03 on one Apple Silicon Mac (M4), using the macOS release build (0.1.0,
protocol 3). Nothing here is estimated.

**Not tested yet:**
- Windows.
- Separate machines.
- A human playing with mouse and keyboard (the arms and effects were reviewed in still captures, not in motion).
- Frame rate with eight players and effects on screen.

## 1. What was built

- **Weapon and impact effects** (`docs/VFX.md`):
  - effect textures drawn by `ArtSource/FX/fx_textures.py` from seeded noise;
  - materials built by `FxArtFactory` on URP's own particle and Lit shaders;
  - first- and third-person muzzle flashes with a short light, sparks and smoke, and a barrel wisp after sustained fire;
  - shell casings lathed in code per calibre, ejected from each gun's `Eject` marker, bouncing and settling;
  - impacts per surface (dust, chips, sparks, glints), bullet holes on flat surfaces, body-hit mist, pooled tracers.
- **First-person arms** (`docs/ANIMATION.md`, "First-person arms"):
  - the operator's own sleeves and gloves, cut from the body in Blender (`finalize_arms`): 16,512 triangles, one
    skinned mesh, a team-colour material slot;
  - posed in code every frame on the viewmodel gun with `ArmChain`, the two-bone IK shared with the third-person
    operator;
  - hand wraps measured on each model, the trigger finger fitted to the blade, thumbs laid along the gun;
  - magazine and shell reloads, the shotgun pump and the rifle's bolt.
- **Framing.** The hip positions were raised 3.5 cm and brought 3 cm closer, and a reload now lifts and cants the gun
  instead of dropping it, so the hands and the magazine stay on screen.
- **Review tool.** `ArmsPreview` (editor, batch mode, about 10 s) renders every weapon and pose from the eye, from
  two outside angles and magnified on each hand, and logs joint positions and each gun's outline around its grips.

## 2. EditMode tests: 55/55 passed

Results file: `editmode_m6.xml`, outside the repo. The 52 earlier tests still pass. The 3 new ones are:

| Test | What it checks | Result |
|---|---|---|
| `ArmChainPutsTheWristOnTargetAndOrientsTheHand` | 100 random reachable targets and hand orientations: the wrist lands within 1 mm, the fingers and the knuckle line point within 1° of what was asked, bone lengths are kept. | Pass |
| `ArmsPrefabIsWiredForTheGame` | The arms prefab has no Animator, one skinned mesh with main and team materials, 4,000–40,000 triangles and every arm and finger bone; the presentation library uses it and has the three team materials. | Pass |
| `HandsReachTheGripsAndShouldersStayOutOfView` | On all five weapons: both wrists within 5 mm of their targets at the hip and aimed, within 10 mm mid-reload (magazine insert or shell insert, in the new reload pose), and both shoulder cuts outside the 56° viewmodel frustum. | Pass |

## 3. Weapon and effects tour (`-maptour Kappa -weapons -fx`)

The built game renders each weapon in the Kappa courtyard and the effects on its test panels. Final run:
`MapTour/fx5`, outside the repo. Selected captures are in `docs/TestReports/M6_fx_arms/`. The images were reviewed by
eye.

| Measurement | Result |
|---|---|
| Wrist IK error, 22 poses (5 weapons × hip, aimed, reload start, magazine or shell insert, plus pump and bolt cycles) | 0 mm, both hands, every pose |
| Trigger fingertip to the trigger blade, hip (edit-mode preview) | rifle 6.8 mm, SMG 5.3, shotgun 0.0, sniper 6.9, pistol 5.9 (was 9–13 mm before this milestone's fixes) |
| 16 impacts emitted at once (4 surfaces × 4) | 2.84 ms CPU on the main thread, 203 particles, 16 bullet holes |
| Impact particles after 0.43 s / 2.9 s | 151 / 0 (holes stay: 16) |
| AR burst | 8 cases in the air; 9 cases at rest on the ground afterwards, about 4 m from the panels' ground point |
| Renderer time per `Camera.Render` at 1600×900, 18 map views | 0.5–1.0 ms (CPU submit time only, not a frame-rate measurement) |

Issues found by looking at the images and fixed in this milestone:
1. **Support thumbs stood up** beside the handguard instead of lying along it. A thumb aimed at a point overshoots
   when it is longer than the distance, and the pistol thumb hit its 70° turn limit. Each thumb is now turned until its
   tip lies on a line along the gun.
2. **Support fingers wrapped over the top of the handguard.** The hand assumed a 2.4 cm round handguard centred 2.4 cm
   above `Grip_L`; raycast outlines showed the rifle's is centred 2.9 cm above it and 5.4 cm tall with the rail. Each
   long gun now has its measured radius and centre, and the knuckles start further under it.
3. **The SMG's support palm passed through its magazine.** The `Grip_L` marker sits on the SMG's forward magazine, a
   vertical box, which the hand wrapped like a horizontal tube. The SMG now holds the magazine like a vertical grip.
4. **Shell reloads dipped back to the pump between shells**, and the start of every shell looked like a new reload.
   The hand now goes down to the shells during the lead-in and, for each shell, up to the loading port, pushes the shell
   in and goes back down (`WeaponState.ShellInserting` tells the arms which phase it is).
5. **The reload hand would trail behind while running or turning.** Its path was eased in world space, so camera
   movement showed up as lag (with the 36 ms easing, roughly 25 cm at sprint speed: calculated, not measured). It is now eased in the viewmodel camera's space.
   Entering and leaving a reload, including a cancelled one, blends over 0.1–0.2 s instead of snapping.
6. **The hands were barely visible.** At the hip the guns sat with their grips below the frame; raising them 3.5 cm
   and bringing them 3 cm closer shows the support hand and sleeve on every weapon.
7. **Reloads went off screen.** The old reload dropped the gun 6 cm and tilted it away, so the magazine and hand were
   below the frame. It now rises 7 cm, moves in towards the centre and cants left side up; the new magazine and the
   support hand are visible in the captures for all five weapons.
8. **Trigger fingers stopped 9–13 mm short** of the blade (the fitting curl could not bend far enough); the fitting
   range was widened.

## 4. Live match over Relay

Both tests ran the same macOS build as two processes on this Mac, joined through a real Relay allocation (not a
local connection). Logs: `AutoTest/ui_113053/` and `AutoTest/combat_113138/`, outside the repo.

| Test | Result | Notes |
|---|---|---|
| `ui` (menus, lobby, warmup, live match, kill feed, scoreboard, pause, death, results, back to lobby) | PASS, host and client | 19 captures; the first-person arms and the new hip position show in the live-match frames (`10_live_fps`, `11_kill_feed`) |
| `combat` (movement, sniper, pistol, host fire, respawn) | PASS, host and client | client moved 19.2 m with 0 corrections; sniper 11/11 and pistol 18/18 hits confirmed by the host; 5,053 inputs processed, 0 skipped or dropped; 50 lag-compensation rewinds, 0 clamped; round-trip time 55.5 ms average (49.7–67.4) |

No exceptions in any of the four player logs. Both processes were on one machine, so the round-trip time is the
path to the Relay server and back, not the time between two players on different networks.

## 5. Known limitations

- The arms are posed procedurally, not from authored clips: reloads and cycling are simple keyed paths, and there is
  no inspect, melee or wall-lean motion. No shell is drawn in the hand during a shell reload.
- The fingers wrap simple shapes (a round section, a box) measured once per weapon class, with no per-finger
  collision, so a finger can sink a few millimetres into the gun or float above it.
- Glass and water impacts are simple (shards and a glint; a puff). Bullet holes are small alpha quads, not decals that
  wrap round corners. Particle collision can let chips pass through thin geometry.
- Remote players' muzzle flashes use the third-person system; spectators see no first-person effects.
- The Mixamo clips are not in the repository. They cannot be redistributed. See `docs/ANIMATION.md`.
