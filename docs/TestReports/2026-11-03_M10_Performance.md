# Test report: graphics presets, frame rate and first-launch stalls, 2026-11-03

All results below come from runs on 2026-11-03 on one machine, using the macOS release build (0.1.0, protocol 4).
Nothing here is estimated unless it says so. The summaries of every run are in `M10_performance/`.

**Test machine:** MacBook Air 13" (Mac16,12), Apple M4 with a 10-core GPU, 16 GB, macOS 27.2. Its display is
2560 x 1664 and refreshes at 60 Hz. The Air has no fan.

**Not tested yet:**
- Windows, and any GPU other than the M4. Windows has no shader warm-up recording (section 5).
- Frame rate in a networked match. The benchmark runs offline, with eight operators driven by the benchmark itself.
- A human playing. The camera follows a fixed path.
- Long sessions on battery. One breakdown started on battery by accident (section 6).
- Low and Medium at full screen.

## 1. What was built

- **A benchmark mode in the release build** (`-benchmark`, see `docs/PERFORMANCE.md`). It flies a fixed path through
  15 viewpoints of Kappa while eight operators run and fire their real weapons, with the first-person arms and
  rifle on screen. It writes every frame's interval and CPU and GPU time, a screenshot per run and a summary line.
  Options change one setting at a time (`High:ao=0;msaa=1`), repeat the first run, skip the warm-up to show
  first-time shader compiles, and wait in the menu before loading.
- **Presets set more.** Besides render scale, MSAA, shadows, HDR and post-processing, they now set post
  anti-aliasing, soft shadow quality, SSAO (off, low, medium, high), anisotropic filtering, texture resolution,
  operator LOD distance and weapon Mesh LOD.
- **Occlusion culling on Kappa.** Big solid parts of the map hide what is behind them. Thin parts (rails, pipes,
  ladders, wheels) and anything without a collider never hide anything.
- **Mesh LOD on weapons.** Other players' guns drop to simpler meshes with distance.
- **Shader warm-up** (`ShaderWarmup`). The shader pipeline states the game draws are recorded once and compiled
  while the main menu is up, instead of when the first match starts (section 5).
- **`PerfAudit`**, an editor report of a map's triangles, shadow casters and LODs.
- **Settings screen.** The preset description now includes soft shadows and ambient occlusion, and it and the
  render scale slider follow the preset you pick. Before, both kept showing the
  preset the screen was opened with.

## 2. Method

Every run is the same 44 s (`-benchdwell 1`) or 66 s flight at 1920 x 1080 in a window, with VSync and the frame
cap off. Before each run every viewpoint is drawn once, so shaders are ready, and the first frames after that are
not measured. The frame interval is measured on the main thread.

To find what one setting costs, each changed run sat between two runs of the unchanged preset and was compared with
their average. This cancels out the machine slowing down or speeding up over time, which turned out to matter
(section 6). The unchanged High runs on mains power varied between 5.85 and 6.16 ms, so differences under about
0.3 ms are within the noise.

Two breakdowns were run: the first (31 runs) started on battery and only its runs on mains power are used; the
second (41 runs) ran entirely on mains power.

## 3. What each setting costs

The full tables are in `docs/PERFORMANCE.md`, "What each setting costs". On High at 1080p:

| Change from High (as it was) | Difference |
|---|---|
| SSAO off | −1.61 ms (−27%) |
| SSAO at half resolution | −1.38 ms (−23%) |
| Render scale 80% | −1.59 ms (−25%) |
| Post-processing off | −1.22 ms (−20%) |
| Shadows off | −0.88 ms (−14%) |
| SMAA on top of 4x MSAA | +0.49 ms |
| 4x MSAA off | −0.40 ms |
| Shadow map 2048 instead of 4096 / 2 cascades instead of 4 | −0.27 / −0.24 ms |
| Occlusion culling off | +0.24 ms, 29% more draw batches |
| Shadow distance 60 or 130 m, HDR off, bloom off, weapon Mesh LOD off | within the noise |

The GPU's time goes to full-screen passes and pixels, not triangles.

## 4. Preset changes, and the final numbers

From those costs:

- **High uses SSAO at half resolution.** Full resolution cost a quarter of the frame. In a zoomed comparison
  (`M10_performance/ssao_levels_zoom.jpg`) half resolution keeps the contact shading under the barrier and at the
  door threshold and is slightly softer. Ultra keeps full resolution.
- **No preset is without anti-aliasing.** Laptop and Low had MSAA off and nothing instead. Laptop now uses 2x MSAA
  (the same cost as FXAA), Low uses FXAA. A test fails if a preset with post-processing has neither.
- **Low keeps post-processing.** Without it Low had no tonemapping or eye adaptation, looked unlike the other
  presets and left interiors dark.
- **The benchmark camera no longer runs SMAA.** The map's spectator camera had SMAA on top of MSAA, which the
  player's camera never had, so the first measurements overstated the cost of post-processing. Every camera now
  takes its anti-aliasing from the preset.

Final runs, on mains power (`presets_1080p_summary.csv`, `presets_fullscreen_summary.csv`):

| Preset | 1080p window: average | 1% low | Median frame | Full screen 2940 x 1912: average | 1% low |
|---|---|---|---|---|---|
| Low | 469 fps (438 repeated) | 283 (297) fps | 2.09 ms | not measured | |
| Medium | 285 fps | 236 fps | 3.56 ms | not measured | |
| High | 208 fps | 179 fps | 4.81 ms | 79 fps (87 repeated) | 66 (76) fps |
| Ultra | 163 fps | 129 fps | 6.13 ms | 63 fps | 48 fps |
| Laptop | 316 fps | 249 fps | 3.14 ms | 135 fps | 114 fps |

2940 x 1912 is what macOS gives a full-screen app on this display at its default scaling. The main thread took
1.7–2.3 ms per frame at 1080p and 2.3–4.8 ms at full screen, so every preset is limited by the GPU. High was 6.1 ms
before its SSAO change and 4.8 ms after.

## 5. First-launch shader stalls

On a machine that has never run the game, Metal compiles every shader pipeline state the first time it is drawn.
To reproduce that, the game's shader caches (`com.apple.metal`, `com.apple.metalfe` in the user cache folder)
were moved aside before a run and put back after it. Each test ran once with the caches moved aside (cold) and
once straight after with the caches that run had built (warm), on the Laptop preset with no warm-up pass, so
every frame from the map's first frame on was measured.

| | Cold, before | Warm, before | Cold, with warm-up | Warm, with warm-up |
|---|---|---|---|---|
| Slowest of the map's first 10 frames | 1,699 ms | 106 ms | 134 ms | 112 ms |
| Slowest frame of the run | 1,029 ms (its first frame) | 32 ms | 82 ms | 30 ms |
| Frames over 50 ms in the run | 1 | 0 | 1 | 0 |
| Warm-up in the menu | | | 3.49 s | 0.24 s |

Without the warm-up, the first match on a machine froze for about 2.7 s in its first frames. With it, the work
moves to the main menu: the warm-up compiled all 91 recorded states (64 shader variants) in 3.49 s with empty
caches, and in 4.94 s in a second cold run (the UI test below). The menu stutters during that time, and its footer
says "preparing shaders for this machine" with a percentage (`M10_performance/menu_preparing_shaders.jpg`, taken
in that run at 44%). With the caches filled the warm-up takes 0.24 s.

The first version of this test missed the stall, because it did not measure the first frames of the run. It was
corrected and run again; the "before" numbers above are from the corrected test.

The map's load time is not affected by the warm-up. With the caches filled, Kappa loaded in 0.89 s after the
warm-up and in 1.09 s without one when the benchmark first waited 2 s; it loaded in 0.25 s only when the load
began a split second after launch, before frames were paced to the display.

The recording only works on the platform and graphics API it was made on. There is one for macOS with Metal. Windows
has none, so there the shaders still compile when first drawn. Unity records only in development builds.

## 6. Battery and heat

The first breakdown began on battery at 36%. The unchanged High run went 6.51, 7.37, 9.69, 9.12, 8.88 ms (median
frame), stayed at 8.7–8.8 ms, and after the charger was connected came back to 7.02 and 6.59 ms and then held
6.47–6.53 ms for the last seven, under the same continuous load. On mains power the second breakdown's High runs stayed within
5.85–6.16 ms over 33 minutes, so heat alone did not slow the Air measurably in these runs. The slowdown went with
running on battery; it was not measured on battery on purpose, and its size will depend on the charge level.

This is why Laptop stays the default on Apple-silicon laptops. At full screen on mains power High's 1% low is 66 fps
and Laptop's is 114 fps. If High slowed by the same share on battery its 1% low would fall below the display's
60 Hz; this is an estimate, not a measurement.

## 7. Tests

- EditMode: 70 of 70 passed (new: `RenderingTests`, with preset ordering, anti-aliasing on every preset, URP's SSAO
  settings layout, Kappa's occlusion data, thin details not occluding, and the shader warm-up recording).
- macOS release and development builds succeeded with no errors.
- UI test with empty shader caches: main menu and settings screens captured; the footer note appeared during the
  warm-up and was gone by the settings captures.

## 8. Known limitations

- **One machine.** All numbers are from one MacBook Air M4. Other GPUs, and Windows, are not measured.
- **Benchmark, not a match.** The operators are driven by the benchmark, there is no networking or HUD, and the camera
  path is fixed. A real match will differ.
- **Windows has no shader warm-up yet.** It needs a recording made on Windows with each graphics API used there.
- **The recording goes out of date.** After changes to shaders, materials or render settings it has to be recorded
  again (`docs/PERFORMANCE.md`, "Shader warm-up"). Missing states still work and are compiled when first drawn; a
  test fails if the recording names a shader that no longer exists.
- **The menu stutters on first launch** for 3.5–5 s while the warm-up runs.
- **GPU time from Metal overlaps frames**, so it is larger than the frame interval and only useful for comparing runs.
- **Battery.** The battery slowdown in section 6 was seen once, not measured on purpose.
