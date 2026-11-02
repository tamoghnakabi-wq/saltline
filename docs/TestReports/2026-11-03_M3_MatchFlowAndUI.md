# Test report — Milestone 3 (match flow, lobby and UI), 2026-11-03

All results below come from runs on 2026-11-02/03. Nothing here is estimated.

**Hardware:** one Apple Silicon Mac (M4). Every player was a separate game process on that one machine.
**Network:** the processes talked only through **Unity Relay** (transport `RelayUnityTransport`). Each one signed in as its own anonymous Unity player.
**Builds:** the macOS release build, plus the development build for the latency tests (Unity strips the network simulator from release builds). Version 0.1.0, network protocol 3.
**Not tested yet:**
- Windows players.
- Players on separate machines or networks.
- A human playing with mouse and keyboard. Every run here was scripted.

## 1. What was built in this milestone

- **Match director.** The host runs the Team Deathmatch flow and every client mirrors it: Lobby → Loading → Warmup → Live → Results → Lobby.
- **Default rules:** 10 minutes, first team to 50 kills, 15 s warmup, 15 s results screen.
  - Equal scores at the time limit end in a draw.
  - Kills during warmup do not count.
  - Suicides and team kills give no team point.
  - Team kills do not credit a personal kill.
- **Lobby rules:**
  - Team changes and ready flags can only change in the lobby.
  - The host can start only when every other player is ready, everyone has a team, and no team has more than 4.
- **UI (UI Toolkit, built in code).** The screens:
  - Main menu: host, join by code, browse public matches, settings, quit.
  - Lobby: two team columns, join code with COPY, map and rules, ready/start.
  - Loading screen.
  - HUD: score bar and timer, banners, kill feed, health, ammo, crosshair, hit markers, damage direction, death overlay with respawn countdown, scope overlay.
  - Hold-Tab scoreboard, results screen with MVP, and pause menu.
  - Settings, with four tabs: gameplay, video, audio and controls with rebinding.
- **Video settings.** Quality presets are applied at runtime: Low, Medium, High, Ultra and a Laptop preset for MacBook Air-class machines. Also display mode, resolution, VSync, frame cap and render scale.
- **Fonts.** Roboto and Roboto Mono, Apache 2.0, copied from Unity's own installed packages. The licence is in `Assets/_Saltline/Art/Fonts/`.

## 2. EditMode unit tests — 41/41 passed

These are the same 41 tests as in Milestone 2, re-run after this milestone's changes. Results file: `TestResults/editmode_m3_final.xml`, outside the repo.

## 3. Match flow over real Unity Relay

Kills in these tests are applied on the host through the normal damage path (`ServerApplyDamage` → `ServerKill`). Aiming and hit registration over the network are what the combat tests in section 5 check. These tests check the rules and the flow around them. The short custom rules (`-warmup`, `-scorelimit`, `-timelimit`, `-resultstime`, `-respawndelay`, `-spawnprotect`) only shorten the match so it fits in a minute.

### `match` — 2 players, ends on the score limit (`AutoTest/match_000001`)

| Check | Result |
|---|---|
| Host cannot start while the client is not ready | refused: "1 player is not ready." |
| Client switches Red, then back to Blue, in the lobby | accepted |
| Client requests a team change during the live match | refused (still Blue) |
| Warmup kill changes the team score | no |
| Records reset when the match goes live | yes |
| Suicide changes a team score | no |
| Final | Red wins **3–1** on the score limit. Client record K1/D3, host K3/D2 |
| Damage applied on the results screen | no |
| Client input locked on the results screen | yes |
| Phase order seen by the client | Loading > Warmup > Live > Results > Lobby |
| After results | Both players back in the lobby scene, still connected, ready flags cleared |

### `matchdraw` — 3 players, public match, ends in a draw (`AutoTest/matchdraw_003206`)

| Check | Result |
|---|---|
| Third player finds the **public** match by browsing | Found on the first query. It showed phase "Live" and 6/8 free slots |
| Third player joins **by session id** while the match is live | Spawned into the live match on Red, at a Red spawn point |
| Assist: the late joiner deals 50 damage, the host gets the kill | Assist credited (A1, +50 score). Red +1 |
| Team kill (host kills the Red late joiner) | No team point, no personal kill credited |
| The late joiner leaves mid-match | Roster back to 2, match still live |
| Time limit with equal scores | **Draw, 1–1**, end reason TimeLimit. The client saw the same |

## 4. Menus and HUD — `Tools/run_relay_tests.sh ui`

A rendering host runs in batch mode with Metal and no window. It presses the real buttons by sending UI Toolkit navigation-submit events. It saves a picture of the 3D view with the UI layered on top. A headless client joins through Relay, switches team, readies and plays along.

The pictures are in [M3_screens/](M3_screens/), downscaled to 1280×720.

Screens checked: main menu (host, join and browse cards), all four settings tabs, the private lobby alone and with a ready client, the warmup HUD, the live HUD in first and third person, the kill feed, the scoreboard, the pause menu, the death overlay, the results screen ("VICTORY", MVP) and the lobby after the match.

| Check | Result |
|---|---|
| Lobby shows the session's join code | yes |
| START MATCH disabled while the client is not ready | yes |
| START MATCH enabled once everyone is ready, and clicking it starts the match | yes |
| Scoreboard and pause overlays appear | yes |
| Results verdict for the winning host | "VICTORY" |

### Problems found by looking at the pictures, and fixed

- **The kill feed and your own scoreboard row were hard to read.** They used a translucent orange background that turned tan over bright scenery, so team-coloured names disappeared. They are now dark rows with an orange edge.
- **The scoreboard and settings panels were too see-through.** UI Toolkit blends in linear colour space, so bright scenery shows through dark panels far more than their alpha suggests. Those panels are now near-opaque.
- **Placeholder text looked like typed text.** "CODE" and "Leave empty to use your callsign" are now dimmed.
- **Spacing classes were overridden** by the button reset rule, so buttons sat flush against the field above them.
- **On the Controls tab the long list squashed the settings header** until the tabs overlapped the title.
- **Crouch appeared twice** (Left Ctrl and C). The second row now reads "Crouch (alternate)".

## 5. Regression: earlier network and combat tests

The full suite ran after this milestone's code was in. All 9 passed (`AutoTest/summary.txt`, run started 00:16:02). The pair, gameplay and UI tests ran again after the late-join fix; the results are in section 7.

| Test | What it checks | Result |
|---|---|---|
| pair | join by code, roster sync, 40 pings | PASS, RTT 61.3 ms |
| capacity | 8 players connected, a 9th refused ("That match is full (8/8).") | PASS |
| hostloss | host process killed with no clean exit; client notices | PASS, detected after 8.5 s |
| hostquit | host quits normally; client is told | PASS |
| latency | 150 ms + 30 ms jitter + 3 % loss on the client | PASS |
| combat / combatlag / combatnolag | predicted movement and lag-compensated hits; control run with lag compensation off must miss | PASS |
| match | section 3 | PASS |

## 6. Bugs found by these tests and fixed

1. **The menu never appeared.** The UI root built its screens before Unity's `UIDocument` had created its root element. It would have failed the same way in the shipped game. The screens are now built in `Start`.
2. **Players who joined mid-match had no team.** Unity's networking reports that a late joiner has finished loading before the host's "connected" callback, so the player was spawned before the host gave it a team. Their kills would have scored nothing and teammates could have shot them. Players are now spawned only once their team exists.
3. **Team kills credited a personal kill and score.** The team score was already protected; the personal record was not. Friendly fire is off, so it could not happen in play yet.
4. The UI code relied on `VisualElement.Add` returning the element, which it does not (about 130 call sites failed to compile). It now uses a small `Put` helper.

## 7. Final re-run after the fixes

Everything below ran with the final code: release build for most tests, development build for the latency runs.

| Test | Run | Result | Key numbers |
|---|---|---|---|
| EditMode | `editmode_m3_final.xml` | PASS | 41/41 |
| pair | `pair_003429` | PASS | 40/40 pings, RTT 66.0 ms avg |
| combat | `combat_003453` | PASS | RTT 58.4 ms; 0 movement corrections in 529 ticks; sniper 11/11 hits, pistol 18/18; host rifle 100 % |
| combatlag | `combatlag_003632` | PASS | +50 ms each way, 10 ms jitter, 1 % loss: RTT 173.6 ms; 0 corrections; sniper 11/11, pistol 17/17; 0 of 49 rewinds clamped |
| combatnolag (control, lag compensation off) | `combatnolag_003810` | PASS (misses expected) | same latency: sniper 1/15, pistol 1/48 |
| match | `match_003948` | PASS | Red wins 3–1 on the score limit |
| matchdraw | `matchdraw_004022` | PASS | found by browsing on the first query; assist credited; team kill scored nothing; 1–1 draw at the time limit |
| ui | `ui_004151` | PASS | 18 screens captured, verdict "VICTORY" |

The `AutoTest/` folder is not in the repository. The screenshots in `M3_screens/` come from `ui_004151`.

## 8. Limits of these results

- **Scripted kills.** Kills in the match and UI tests are applied on the host, not aimed and fired. Shooting over the network is covered by the combat tests.
- **Loading screen not photographed.** The test map loads in under a quarter of a second, before a frame could be captured. The screen exists and is driven by the phase, but no picture of it was taken.
- **Not run:**
  - The host's END MATCH button: a one-line call to end the match by the current score.
  - Key rebinding: only the bindings list was shown, no key was rebound.
  - Gamepad menu navigation.
- **Browse list not seen in the UI.** The UI test's browse card showed "No public matches", because its own match was private. Public browsing itself was verified through the same `SessionService.BrowseAsync` call the card uses, in `matchdraw`.
- **One resolution.** All screens were checked at 1920×1080 only.
- **No frame-rate numbers.** Nothing in this milestone measures performance.
- **Placeholder art.** The 3D content is still greybox: the Arena test map, block weapons and capsule characters. The Kappa map, models and audio are the next milestones.
- **Host authority.** The match is host-authoritative. The host's machine runs the rules, so a modified host could cheat. Unity Relay only forwards packets; it is not a dedicated server. If the host leaves, the match ends: there is no host migration.
