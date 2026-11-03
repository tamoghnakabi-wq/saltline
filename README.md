# SALTLINE

An original 4v4 team deathmatch shooter for macOS (Apple silicon) and Windows, made in Unity 6 with the
Universal Render Pipeline. One map, Kappa Saltworks: a coastal salt works with three lanes (salt pans to the
north, the pump house in the middle, the dock to the south).

**Status: version 0.1.1, a public test release.** Download it from
[saltline-five.vercel.app](https://saltline-five.vercel.app/#download) or
[GitHub Releases](https://github.com/tamoghnakabi-wq/saltline/releases/latest).
The macOS build has been played and tested on one MacBook Air M4.
The Windows build compiles and packages, but has never been run on a Windows machine. See
[Known limitations](#known-limitations).

## Playing

### Getting a match going

One player hosts and the others join. There is no dedicated server: the host's game runs the match, and the
other players connect to it through Unity Relay, which forwards the traffic. Sign-in is anonymous and automatic;
no account is needed.

- **Host match:** name the match, choose public or private, pick the map and create the lobby. Its join code is
  shown at the top.
- **Join with code:** type the host's code.
- **Browse matches:** public matches of the same network version, with free slots.

In the lobby everyone picks a team (4 per team at most) and readies up; the host starts the match. The match
ends at 50 kills or after 10 minutes. If the host leaves, the match ends for everyone.

### Controls

| Action | Keyboard and mouse | Gamepad |
|---|---|---|
| Move | W A S D | left stick |
| Look | mouse | right stick |
| Fire / aim | left / right mouse button | right / left trigger |
| Jump | Space | A / Cross |
| Crouch (slide while sprinting) | Left Ctrl or C | B / Circle |
| Sprint | Left Shift | left stick press |
| Reload | R | X / Square |
| Primary / sidearm | 1 / 2, or Q to swap | Y / Triangle to swap |
| First / third person | V | d-pad up |
| Swap shoulder (third person) | X or middle mouse button | right stick press |
| Scoreboard | Tab | Back / Share |
| Pause | Esc | Start / Options |

Keys can be rebound under Settings > Controls (the screen is tested; rebinding a key has not been run in a
test). The gamepad bindings exist but have not been tried with a controller.

### Weapons

| Weapon | Type | Damage | Rate | Magazine |
|---|---|---|---|---|
| VK-21 Halyard | assault rifle | 26 | 750 rpm | 30 |
| SMR-9 Wasp | submachine gun | 22 | 900 rpm | 32 |
| TS-12 Breaker | shotgun | 9 pellets × 14 | 70 rpm | 6 |
| LR-338 Longreach | sniper rifle | 95 | 45 rpm | 5 |
| P-45 Brace | pistol (always the sidearm) | 34 | 400 rpm | 12 |

### Graphics presets

Low, Medium, High and Ultra, plus Laptop, which is the default on Apple-silicon laptops. Measured on the
MacBook Air M4 (offline benchmark, not a networked match): Laptop averages 135 fps at full screen and High 79 fps.
Details and every measurement: [docs/PERFORMANCE.md](docs/PERFORMANCE.md).

## Downloads and installing

Download from the [website](https://saltline-five.vercel.app/#download) or from
[GitHub Releases](https://github.com/tamoghnakabi-wq/saltline/releases). Each file's SHA-256 checksum is on both,
and in `SHA256SUMS.txt`. To check a download, compare the output of `shasum -a 256 <file>` (macOS) or
`Get-FileHash <file>` (PowerShell) with it.

Neither build is signed by a recognised developer, so:

- **macOS** blocks the app the first time. Open System Settings > Privacy & Security, choose Open Anyway next to
  the message about SALTLINE, and confirm with your password. Or run
  `xattr -dr com.apple.quarantine /Applications/SALTLINE.app`.
- **Windows** SmartScreen is likely to warn that it "protected your PC". Choose More info > Run anyway.

Why, and what removing the warnings would take: [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md).

The game checks the website's `version.json` when it starts. If the network version has changed, the menu
blocks hosting, joining and browsing and offers the download instead.

## Building from source

Requirements:

- Unity **6000.3.22f1** with the macOS and Windows build support modules.
- Git LFS (textures, models and audio are stored with LFS).
- The Unity project linked to a Unity Gaming Services project with Relay enabled (Edit > Project Settings >
  Services). The tests for this project ran without a paid plan.
- The Adobe Mixamo animation clips. Their licence lets them ship inside the game but not as files in the
  repository, so download them again from mixamo.com as listed in
  [docs/ANIMATION.md](docs/ANIMATION.md#mixamo-clips-not-in-the-repository).

Build from the command line (run in the project folder; `Unity` is the editor binary):

```bash
Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildMac
Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildWindows
```

Add `-devbuild` for a development build (profiler and network simulator, written to `Builds/*-dev/`). Each build
writes `SALTLINE.buildinfo.json` beside it. `Tools/package_release.py` turns two release builds into the release
files (see [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md#packaging)).

## Tests

- **Unit tests** (EditMode):
  `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`
- **Multiplayer tests** over real Unity Relay, with separate game processes each signed in as its own player:
  `Tools/run_relay_tests.sh all`. The latency and stall tests need a development build.
- **Visual review and benchmarks** from the release build: `-maptour` renders the map's review viewpoints,
  `-benchmark` measures frame times per preset ([docs/PERFORMANCE.md](docs/PERFORMANCE.md#measuring)).

The reports of every test run, with their results and what was not tested, are in
[docs/TestReports/](docs/TestReports/).

## Repository layout

| Path | Contents |
|---|---|
| `Assets/_Saltline/Scripts` | all game code: `Runtime` (game), `Editor` (scene, content and build tools) |
| `Assets/_Saltline/Art`, `Audio`, `Scenes`, `UI` | game content |
| `Assets/_Saltline/Tests/EditMode` | unit tests |
| `ArtSource/` | the Blender, texture and audio scripts that generate the art |
| `Tools/` | multiplayer test runner, release packager |
| `Web/` | the download website (Next.js, exported as static files) |
| `docs/` | design notes, distribution, performance and test reports |

## How the networking works, and what it does not do

- Netcode for GameObjects with the host as the authority: the host simulates movement and shots; clients
  predict their own movement and are corrected by the host. The host rewinds other players to what the shooter
  saw (lag compensation, up to 350 ms).
- Unity Relay only forwards traffic between the players. It is not a game server and runs none of the game.
- The host decides everything, so a modified host can cheat. Host authority stops clients from cheating in
  the ways the host checks; it does nothing against a dishonest host.

## Known limitations

- **Windows has not been run.** No Windows machine was available. The build is untested, and so is cross-play
  between macOS and Windows, although both builds use the same content and network protocol.
- **Tested on one machine.** Every multiplayer test ran as several game processes on one Mac, talking through
  Relay. Players on separate machines and networks have not been tested.
- **Not signed or notarized.** See [Downloads and installing](#downloads-and-installing).
- **One map and one mode.**
- **No dedicated server, no host migration.** The match ends when the host leaves.
- **Windows has no shader warm-up.** Its first match will stutter while shaders compile.
- **No gamepad testing.** Gamepad bindings exist, but no controller has been tried, in the menus or in a match.
- **Licence not chosen yet.** The repository is public, but it has no licence file, so the project's own code
  and art are not licensed for reuse. Third-party assets keep their own licences ([ATTRIBUTIONS.md](ATTRIBUTIONS.md)).

More detail on each area: [animation](docs/ANIMATION.md#known-limitations), [audio](docs/AUDIO.md#limitations),
[effects](docs/VFX.md#known-limitations), [performance](docs/TestReports/2026-11-03_M10_Performance.md).

## Credits

Everything is either made for this project or used under a licence that allows it; nothing is taken from
commercial games. Full list: [ATTRIBUTIONS.md](ATTRIBUTIONS.md).
