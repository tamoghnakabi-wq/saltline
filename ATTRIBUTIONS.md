# Attributions and licences

Everything in SALTLINE is either made for this project or used under a licence that allows it.
No assets were taken from commercial games.

## Made for this project

- **Code:** all C# under `Assets/_Saltline/Scripts`, and the tools under `Tools/` and `ArtSource/`.
- **Kappa Saltworks map:** modelled in Blender from `ArtSource/Blender/kappa_build.py`.
- **Kappa props:** containers, trucks, the salt harvester, bulk bags, pallets, IBC tanks, drums, lamps, windows,
  doors, shutters and the other set dressing, modelled from `ArtSource/Blender/kappa_props.py`.
- **Generated textures:** sacks, woven bulk bags, hazard stripes, paint, water and the Arena textures, made from seeded noise by `ArtSource/Textures/gen_kappa_textures.py`.
- **Sound effects and ambience:** every sound (gunshots, weapon handling, footsteps, impacts, player sounds, hit
  feedback, interface sounds, match stings, the coastal bed and the machinery hum) is synthesised from seeded noise,
  oscillators and filters by `ArtSource/Audio/gen_sfx.py`. No recordings, samples or sound libraries were used.
- **Weapons:** original designs, modelled in Blender from `ArtSource/Blender/weapons_build.py` (none copies a real firearm). Their textures are baked by `ArtSource/Blender/weapons_lib.py`.
- **Operator character:** original design (no real unit, insignia or commercial character), modelled, rigged, skinned and baked in Blender from `ArtSource/Blender/operator_build.py`. Its rig uses Mixamo-compatible bone names so it can be set up as a Unity Humanoid.
- **Weapon handling:** the third-person weapon hold, hand placement and reload hand motion are procedural (`OperatorRig.cs`), not Mixamo clips.
- **First-person arms:** the operator's own arms, cut from the same Blender body by `finalize_arms` in `ArtSource/Blender/operator_build.py`, and posed in code (`FirstPersonArms.cs`).
- **Weapon and impact effects:** every effect texture (muzzle flash front and side, smoke, glow, debris chips, tracer, bullet-hole atlas with normal and mask maps) is drawn from seeded noise by `ArtSource/FX/fx_textures.py`. Shell casings are lathed in code from millimetre profiles (`ShellCasings.cs`). No effect textures, flipbooks or decals come from other sources.

## CC0 (public domain) assets

These are from [Poly Haven](https://polyhaven.com). Poly Haven releases every asset under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/), so no credit is required. We list the authors anyway.

### Sky

| File in the game | Source | Authors | Changes |
|---|---|---|---|
| `Art/Sky/Sky_Kloofendal48d_NoSun_4k.hdr` | [kloofendal_48d_partly_cloudy_puresky](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky) (4K HDR) | Greg Zaal, Jarod Guest | Sun clamped out by `ArtSource/Blender/sky_remove_sun.py`. The game's sun light takes its place. |

### Surface textures (2K PBR scans)

These live in `Art/Maps/Kappa/TexturesCC0/`. Each set was converted by `ArtSource/Textures/import_cc0.py` into:
- albedo
- normal map
- one mask packing metallic, ambient occlusion and smoothness

The "Real size" column is the area one tile covers. The map's UVs use it, so textures show at their true scale.

Where the Changes column gives a brightness, the albedo was rescaled so its average linear brightness matches that value. The scans were shot at different exposures, and the raw ones made interiors read almost black under the game's lighting.

| Game texture | Source | Authors | Real size | Changes |
|---|---|---|---|---|
| `T_Asphalt_*` | [asphalt_02](https://polyhaven.com/a/asphalt_02) | Rob Tuytel | 3.0 m | Albedo brightness 0.12. |
| `T_Concrete_*` | [concrete_wall_006](https://polyhaven.com/a/concrete_wall_006) | Charlotte Baglioni, Dario Barresi | 2.0 m | Albedo brightness 0.24. |
| `T_ConcreteSmooth_*` | [concrete_wall_008](https://polyhaven.com/a/concrete_wall_008) | Charlotte Baglioni, Dario Barresi | 2.71 m | |
| `T_ConcreteFloor_*` | [concrete_floor_worn_001](https://polyhaven.com/a/concrete_floor_worn_001) | Dimitrios Savva, Rico Cilliers | 3.0 m | Albedo brightness 0.18. |
| `T_Facade_*` | [painted_plaster_wall](https://polyhaven.com/a/painted_plaster_wall) | Amal Kumar | 2.0 m | |
| `T_Salt_*` | [sand_02](https://polyhaven.com/a/sand_02) | Charlotte Baglioni | 2.14 m | Recoloured to salt white. |
| `T_Metal_*` | [metal_plate_02](https://polyhaven.com/a/metal_plate_02) | Rob Tuytel | 2.0 m | Albedo brightness 0.14. |
| `T_Grate_*` | [metal_grate_rusty](https://polyhaven.com/a/metal_grate_rusty) | Dimitrios Savva, Rob Tuytel | 0.5 m | Albedo brightness 0.12. |
| `T_TankPaint_*` | [rusty_metal_04](https://polyhaven.com/a/rusty_metal_04) | Amal Kumar | 2.0 m | Albedo brightness 0.12. |
| `T_Rust_*` | [rust_coarse_01](https://polyhaven.com/a/rust_coarse_01) | Dimitrios Savva, Rico Cilliers | 2.2 m | Albedo brightness 0.10. |
| `T_Corrugated_*` | [corrugated_iron_02](https://polyhaven.com/a/corrugated_iron_02) | Jenelle van Heerden, Sergej Majboroda | 2.7 m | Albedo brightness 0.16. |
| `T_CorrugatedRust_*` | [rusty_corrugated_iron](https://polyhaven.com/a/rusty_corrugated_iron) | Charlotte Baglioni | 2.0 m | Albedo brightness 0.10. |
| `T_Container_*` | [container_side](https://polyhaven.com/a/container_side) | Dimitrios Savva | 1.94 m | |
| `T_ContainerPaint_D` | [container_side](https://polyhaven.com/a/container_side) | Dimitrios Savva | 1.94 m | Albedo only, made neutral grey (linear brightness 0.5) by `ArtSource/Textures/derive_container_paint.py`, so each container's material sets its paint colour. It uses `T_Container_N` and `T_Container_M`. |
| `T_Wood_*` | [weathered_planks](https://polyhaven.com/a/weathered_planks) | Dario Barresi, Dimitrios Savva | 2.0 m | Albedo brightness 0.16. |
| `T_Rock_*` | [rock_face](https://polyhaven.com/a/rock_face) | Dario Barresi, Greg Zaal | 2.38 m | Albedo brightness 0.14. |
| `T_Ground_*` | [dry_ground_rocks](https://polyhaven.com/a/dry_ground_rocks) | Rob Tuytel | 4.0 m | Saturation reduced to 70%. |
| `T_Gravel_*` | [gravel_floor](https://polyhaven.com/a/gravel_floor) | Jenelle van Heerden, Matterfield | 2.25 m | Albedo brightness 0.22. |
| `T_Shutter_*` | [painted_metal_shutter](https://polyhaven.com/a/painted_metal_shutter) | Charlotte Baglioni, Dario Barresi, Rico Cilliers | 2.0 m | |

## Animation: Adobe Mixamo

The operator's locomotion, crouch, jump, turn and death animations are from [Adobe Mixamo](https://www.mixamo.com)
(the "Rifle 8-Way Locomotion Pack" and individual death clips), retargeted onto the original operator in Unity.
Mixamo animations may be used in commercial and non-commercial projects, including games, but the animation files
must not be redistributed on their own. So:

- the game builds contain them only as compiled game data;
- the source clips in `Assets/_Saltline/Art/Characters/Mixamo/` are listed in `.gitignore` and must never be committed to a
  public repository or attached to a release. `docs/ANIMATION.md` lists the clips to download again on a new machine.

## Fonts

Roboto and Roboto Mono, by Google. Licensed under [Apache 2.0](Assets/_Saltline/Art/Fonts/LICENSE-Apache-2.0.txt).

## Engine and packages

Built with Unity 6 and Unity packages under the Unity Companion License: URP, Netcode for GameObjects, Multiplayer Services, Input System and others.
