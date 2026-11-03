# Performance and quality presets

## Presets

`VideoSettings` applies a preset to the active URP asset, the sun light and `QualitySettings` at runtime, so
one build serves every machine. The player picks it under Settings > Video. On first launch, an Apple-silicon
laptop starts on Laptop, a GPU with under 3 GB starts on Medium, and everything else starts on High.

| Preset | Render scale | Anti-aliasing | Shadows: distance, cascades, map, softness | SSAO | Textures | LOD bias |
|---|---|---|---|---|---|---|
| Low | 75% | FXAA | 35 m, 1, 1024, low | off | half resolution, no anisotropic filtering | 0.6 |
| Medium | 90% | 2x MSAA | 60 m, 2, 2048, medium | low | full, anisotropic filtering | 1.0 |
| High | 100% | 4x MSAA | 90 m, 4, 4096, high | medium | full, anisotropic filtering forced | 1.5 |
| Ultra | 100% | 4x MSAA | 130 m, 4, 4096, high | high | full, anisotropic filtering forced | 2.0 |
| Laptop | 80% | 2x MSAA | 55 m, 2, 2048, medium | low | full, anisotropic filtering | 1.0 |

Every preset has HDR, post-processing (bloom, ACES tonemapping, colour grading, eye adaptation) and Kappa's
occlusion culling. SSAO low is half resolution with 8 samples, medium is half resolution with 12 samples and a
bilateral blur, and high is full resolution.

Frame rates on a MacBook Air M4 on mains power (2026-11-03), from the benchmark with eight operators fighting:

| Preset | 1920 x 1080, window: average | 1% low | Full screen, 2940 x 1912: average | 1% low |
|---|---|---|---|---|
| Low | 469 fps (438 when repeated at the end) | 283 (297) fps | not measured | |
| Medium | 285 fps | 236 fps | not measured | |
| High | 208 fps | 179 fps | 79 fps (87 when repeated) | 66 (76) fps |
| Ultra | 163 fps | 129 fps | 63 fps | 48 fps |
| Laptop | 316 fps | 249 fps | 135 fps | 114 fps |

2940 x 1912 is the size macOS gives a full-screen app on the Air's display at its default scaling, 2.7 times the
pixels of 1080p. The Air's display refreshes at 60 Hz, so with VSync on (the default) High and Laptop hold 60 fps
at full screen on mains power (their 1% lows are above 60) and Ultra does not. Low and Medium cost less than High
but were not measured at full screen. Laptop is the default on Apple-silicon laptops because of what
happens on battery: in one breakdown the Air started on battery at 36% and the same High run went from 6.5 ms to
8.7–9.7 ms per frame, and back to 6.5 ms once the charger was connected (`docs/TestReports/2026-11-03_M10_Performance.md`).
High at full screen was not measured on battery. If it slowed by the same share, its 1% low (66 fps) would fall
below 60; Laptop's (114 fps) would not.

The render scale slider in Settings overrides the preset's scale. Below 100% the frame is upscaled with FSR.

## What each setting costs

Measured on a MacBook Air M4 on mains power, at 1920 x 1080 in a window, with one setting changed at a time
(2026-11-03). Each change was run between two runs of the unchanged preset and is compared with their average, so
the machine slowing down over time cancels out. The unchanged High runs varied between 5.85 and 6.16 ms, so a
difference under about 0.3 ms is within the noise. The frame interval is the median over a 44 s run.

Starting from High as it was before the changes below (6.1 ms; full-resolution SSAO, no post anti-aliasing):

| Change | Frame interval | Difference |
|---|---|---|
| SSAO off | 4.26 ms | −1.61 ms (−27%) |
| SSAO at half resolution (8 samples, gaussian blur) | 4.58 ms | −1.38 ms (−23%) |
| SSAO with 12 samples instead of 8 | 6.07 ms | none |
| Shadows off | 5.25 ms | −0.88 ms (−14%) |
| Shadow distance 60 m or 130 m instead of 90 m | 6.10 ms | none |
| 2 shadow cascades instead of 4 | 5.92 ms | −0.24 ms (−4%) |
| Soft shadows medium / low instead of high | 5.92 / 5.80 ms | −0.17 / −0.27 ms |
| FXAA on top of 4x MSAA | 6.35 ms | +0.22 ms |
| SMAA on top of 4x MSAA | 6.59 ms | +0.49 ms (+8%) |
| Bloom off | 6.35 ms | no saving (within the noise) |
| Post-processing off | 4.88 ms | −1.22 ms (−20%) |

From an earlier breakdown of the same High preset with SMAA on (6.47 ms; only the runs on mains power):

| Change | Frame interval | Difference |
|---|---|---|
| Render scale 80% | 4.88 ms | −1.59 ms (−25%) |
| 4x MSAA off / 2x | 6.10 / 6.35 ms | −0.40 / −0.12 ms |
| Shadow map 2048 instead of 4096 | 6.53 ms | −0.27 ms (−4%) |
| HDR off | 6.47 ms | none |
| Occlusion culling off | 6.71 ms | +0.24 ms (+4%); 538 instead of 416 draw batches |
| Weapons always at full detail (Mesh LOD off) | 6.47 ms | none |

Laptop (3.17 ms before the changes below) and Low:

| Change | Frame interval | Difference |
|---|---|---|
| Laptop: 2x MSAA instead of FXAA | 3.17 ms | none |
| Laptop: SMAA instead of FXAA | 3.30 ms | +0.13 ms |
| Laptop: SSAO off | 2.56 ms | −0.61 ms (−19%) |
| Laptop: render scale 100% instead of 80% | 4.03 ms | +0.86 ms (+27%) |
| Low: post-processing and FXAA on (they were off) | 2.32 ms | +0.86 ms |

What changed because of these numbers:

- **SSAO at half resolution on High.** Full-resolution SSAO cost a quarter of the frame. At half resolution the
  contact shading under walls and in doorways is still there and only slightly softer. Full resolution is kept for
  Ultra. Since 12 samples cost the same as 8, the higher SSAO levels use 12.
- **No preset is without anti-aliasing.** Laptop and Low had MSAA off and nothing in its place. Laptop now uses 2x
  MSAA, which costs the same as FXAA and keeps textures sharp. Low uses FXAA.
- **Low keeps post-processing.** Without it Low had no tonemapping and no eye adaptation, so it looked different
  from the other presets and interiors stayed dark. It costs 0.9 ms on the M4.
- **Kept as they were:** 4 cascades and soft shadows on High (each under 0.3 ms), shadow distance (free), HDR
  (free), bloom (no measurable cost) and occlusion culling (saves 4% and almost a quarter of the draw batches).
- **What the camera renders matters more than triangles.** Mesh LOD on weapons made no difference, and turning
  occlusion culling off drew 17% more triangles and 29% more draw batches for a 4% slower frame. Full-screen passes (SSAO, post-processing)
  and pixel count are what this GPU spends its time on.

## Occlusion culling and LODs

- **Occlusion culling.** Kappa has baked occlusion culling (`KappaSceneFactory.EnsureOcclusion`, also under
  Saltline > Build > Kappa Occlusion Culling). Big solid pieces of the map are occluders. Anything without a
  collider, and thin parts such as rails, pipes, ladders and wheels, can be hidden but never hide anything
  themselves, so nothing disappears behind a gap. The bake does not touch the lightmaps and takes under a minute.
- **Operators.** The operator has three LOD levels (50k, 25k and 11k triangles). The preset's LOD bias sets how
  far away each level is used.
- **Weapons.** Each weapon has a first-person model (9k–34k triangles) and a decimated third-person model
  (3k–10k triangles) that other players' operators carry. The weapon models also get Unity's automatic Mesh
  LOD (`WeaponArtFactory`), so a gun far away drops to a simpler mesh. The preset's mesh LOD threshold sets how
  soon. The first-person gun fills the screen and always draws its full mesh.
- **The map.** Kappa is 177k triangles in 492 renderers, mostly large merged meshes. It has no LODs. Unity's
  automatic Mesh LOD picks a level from how big the whole mesh is on screen, so a 100 m merged mesh would rarely
  drop a level, and static batching ignores Mesh LOD anyway. The measurements below show that triangles are not
  what limits the frame rate here.

## Shader warm-up

The first time the game runs on a Mac, Metal has to compile every shader pipeline state it draws. That used to
happen when the first match started: the screen froze for 1.7 s after the map loaded and again for 1.0 s when the
first-person view appeared. It would happen again whenever macOS discards the game's shader cache, which is a
cache the system may clear (not tested here).

`ShaderWarmup` now compiles them in advance from a recording, a few per frame from launch while the main menu is
up. On a Mac with an empty cache this took 3.5 s and 4.9 s in two runs, during which the menu stutters and its
footer says "preparing shaders for this machine". Afterwards it takes about 0.25 s.

The recording is `Assets/_Saltline/Resources/ShaderWarmup/Saltline_OSX_Metal.graphicsstate`. Unity records only
in development builds, and a recording fits only the platform and graphics API it was made on, so Windows has
none yet and compiles shaders when they are first drawn. Record again after changing shaders, materials or
render settings:

```bash
Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildMac -devbuild
Builds/macOS-dev/SALTLINE.app/Contents/MacOS/SALTLINE -benchmark Kappa -benchout /tmp/trace -benchdwell 1 \
  -benchpresets Low,Medium,High,Ultra,Laptop -tracegraphicsstates Assets/_Saltline/Resources/ShaderWarmup/Saltline_OSX_Metal.graphicsstate
```

A recording that names a shader that no longer exists fails `RenderingTests.MacShaderWarmupRecordingIsUsable`.
One that misses new states still works; those states compile when first drawn.

## Measuring

The benchmark runs the real build, offline, in a window:

```bash
Builds/macOS/SALTLINE.app/Contents/MacOS/SALTLINE -benchmark Kappa -benchout Benchmark -benchrepeat
```

- Eight operators (four per team) run back and forth and fire their weapons, with muzzle flashes, lights,
  tracers, impacts and spent cases. The camera carries the first-person arms and a rifle that fires a burst
  every 3 s.
- The camera flies through 15 eye-height viewpoints of the map, pausing at each one while it pans. Every run
  flies the same path.
- Before a run is measured, every viewpoint is drawn once so its shaders and textures are ready, and one frame
  of the courtyard is saved as `shot_<run>.png`. The log gives the warm-up's slowest frame, which shows how long
  first-time shader compiles take.
- The log also gives how long the shader warm-up made the benchmark wait, how long the map took to load and
  the slowest of its first 10 frames. A load that starts a split second after launch looks faster (0.25 s instead
  of about 0.9 s here) because frames are not yet paced to the display; use `-benchmenuwait` to compare loads.
- Each frame's interval and the CPU and GPU times from `FrameTimingManager` go to `bench_<run>.csv`, and one
  line per run to `summary.csv` and the log. VSync and the frame cap are off.

Options:

| Option | Meaning |
|---|---|
| `-benchpresets Low,Medium,High` | the runs, in order (default all five presets) |
| `-benchpresets "High:ao=0;msaa=1"` | a preset with single values changed, to measure what they cost |
| `-benchsize 2560x1440` | window size in pixels (default 1920x1080) |
| `-benchfullscreen` | full screen at the display's resolution |
| `-benchdwell 1` | seconds at each viewpoint (default 2.5) |
| `-benchrepeat` | measure the first run again at the end, to show whether the machine slowed during the runs |
| `-benchnowarmup` | skip the warm-up, so the run shows the hitches of shaders being compiled for the first time |
| `-benchmenuwait 2` | stay this many seconds longer in the menu before loading the map, as a player would |

Variant keys: `ao` (0 off to 3 high), `msaa`, `aa` (post anti-aliasing: 0 none, 1 FXAA, 2 SMAA), `shadow`
(distance in metres, 0 for none), `cascades`, `shadowres`, `soft` (1–3), `hdr`, `post`, `bloom` (0/1), `scale`,
`lod`, `meshlod`, `occl` (occlusion culling 0/1).

`PerfAudit` (editor) lists a scene's renderers, triangles, shadow casters, static flags and the character and
weapon models' triangle counts:

```bash
Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.PerfAudit.Run -auditout PerfAudit_Kappa.txt
```

### Reading the numbers

- **Frame interval** is the time between frames, measured on the main thread. Its average gives the frame rate.
  The 1% low is the frame rate of the slowest 1% of frames.
- **CPU main and render thread** come from `FrameTimingManager`.
- **GPU** is Metal's GPU time for the frame. On Apple GPUs consecutive frames overlap on the GPU, so this time
  can be longer than the frame interval, and it is not the share of the GPU a frame used. Compare it between
  runs, not with the frame interval.
- A MacBook Air has no fan. On mains power the High runs here stayed within 5.85–6.16 ms over 33 minutes, but
  on battery the same run slowed from 6.5 to 9.7 ms (median frame). Run on mains power, and use `-benchrepeat` to check.
