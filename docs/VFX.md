# Weapon and impact effects

All effects are cosmetic and run on every client from the replicated shot events (`CombatEvents.Shot`).
Nothing here affects hit detection, which the host does with its own raycasts and the analytic hitboxes.

## Art

`ArtSource/FX/fx_textures.py` draws every effect texture from seeded noise (seed 20261103), so a rerun gives
the same files. It writes them to `Assets/_Saltline/Art/FX/Textures`:

| Texture | Contents |
|---|---|
| `T_FX_MuzzleFront` | 2×2 flipbook of star-shaped flashes, seen down the barrel |
| `T_FX_MuzzleSide` | 2×2 flipbook of flame jets, seen from the side (u runs from the muzzle to the tip) |
| `T_FX_Smoke` | 2×2 flipbook of smoke and dust puffs |
| `T_FX_Glow` | soft round glow (sparks, impact flashes, glints) |
| `T_FX_Debris` | 2×2 flipbook of chips and splinters |
| `T_FX_Tracer` | tracer streak |
| `T_FX_Hole_D` / `_N` / `_M` | bullet-hole atlas: concrete, metal, wood, soft ground. Normal map is OpenGL-style (+y up). Mask: R metallic, A smoothness. |

`FxArtFactory` (run by `SaltlineBuild.Setup`) builds the materials from these with URP's own particle and Lit
shaders, setting keywords the way URP's material inspector does:
- additive: flash, flame, spark, glow, tracer;
- lit alpha with soft particles: smoke and dust;
- lit cutout: debris chips;
- Lit transparent: bullet holes (preserve-specular off, so the clear part of the quad reflects nothing);
- Lit: brass and the shotgun hull.

Shell casings are meshes lathed in code from millimetre profiles (`ShellCasings.cs`): rifle 45 mm,
pistol and SMG 19 mm, sniper 51 mm, and a 70 mm shotgun shell with a brass head and a red hull.

## Runtime

`FxManager` owns a few shared particle systems. Their emission modules are off; each effect adds particles with
`Emit()`, so one system serves every shot of its kind and nothing is instantiated per shot.

| Effect | What happens |
|---|---|
| Muzzle flash, third person | front star, a side flame on a crossed-quad mesh, stretched sparks, smoke, a short point light |
| Muzzle flash, first person | the same on the viewmodel layer, following the muzzle in local space, plus a soft bloom that stays visible around the optic when aiming |
| Barrel wisp | after sustained fire the first-person barrel smokes for a second or two |
| Tracers | pooled line renderers (64) |
| Shell casings | ejected per class from the `Eject` marker (rifles, SMG, pistol per shot; pump and bolt actions when the action opens); they spin, bounce off the world, settle on their side, and shrink away after 8 s. First-person cases start on the viewmodel layer and move to the world after 0.16 s. |
| Impacts | per surface: dust, chips, sparks, a glint, and a bullet hole if the surface around the hit is flat enough (four corner raycasts) |
| Body hits | a red mist puff; no decals on players |

Impact looks by surface (`SurfaceTag`):

| Surface | Look |
|---|---|
| Concrete (and untagged) | grey dust, stone chips, a faint glint, the occasional spark, concrete hole |
| Metal, thin metal | sparks, a bright glint, a little dust, metal hole |
| Wood | brown dust, splinters, wood hole along the grain |
| Dirt (salt, sand) | a large pale dust cloud, small chips, soft-ground hole |
| Glass | a glint, small pale shards, a small metal-cell hole |
| Fabric, plastic | a small dark puff, a small soft-ground hole |
| Water | a pale splash puff, no hole |

Particles are only spawned for impacts within 140 m of the camera; holes are placed at any distance.

Pools: 256 bullet holes, 64 casings, 64 tracers, 12 lights. When a pool is full the oldest entry is reused.
The Low preset uses 40% of the particle counts and the Medium and Laptop presets 70% (`VideoSettings.EffectsDetail`).

## Review

`SALTLINE -batchmode -maptour Kappa -fx -capture <dir>` renders the effects in the Kappa map with a fixed time
step and writes about 26 images: impacts on four materials (burst, dust, holes, close-ups), side views of each
weapon's flash, an AR burst with cases and smoke, cases on the ground, each weapon's first-person flash with the
arms, the barrel wisp, the aimed flash, a body hit and a passing tracer. It logs the CPU time of the impact
burst and the particle, decal and casing counts.

## Known limitations

- Glass and water have simple looks (shards and a glint; a puff): no cracks, ripples or splash flipbooks.
- No screen-space decals: bullet holes are small alpha quads, so they do not wrap around corners (the corner test
  skips holes that would hang over an edge).
- Particle collision is URP's planar-cached world collision at low quality, so chips can occasionally pass
  through thin geometry.
- Remote players' muzzle flashes use the third-person system; there is no per-shot first-person effect for spectators.
