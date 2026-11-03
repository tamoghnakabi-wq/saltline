# Audio

Every sound in SALTLINE is synthesised by a script in this repository. Nothing is sampled, recorded or downloaded,
so all of it is original and can be regenerated. Sound is cosmetic: it plays from the same presentation events as
the visual effects and never affects the simulation. Headless processes (`-batchmode` without rendering) never
create the audio system.

## Sounds

`ArtSource/Audio/gen_sfx.py` builds 56 sounds (98 files with variants) from seeded noise, oscillators, filters and
a synthetic room response:

```bash
python3 ArtSource/Audio/gen_sfx.py
```

It writes 48 kHz, 16-bit mono WAV files and `manifest.json` to `Assets/_Saltline/Audio/Generated`. For each sound
the manifest records its group, variants, lengths and loudness: BS.1770 K-weighted LUFS over the loudest 400 ms.

| Group | Sounds |
|---|---|
| Weapons | a near and a far shot per weapon class (rifle, SMG, pistol, shotgun, sniper), each near shot in three variants and each far shot in two; dry fire; magazine out and in; charging handle back and release; slide release; pump back and forward; bolt up, back, forward and down; shell insert; equip |
| Player | footsteps on concrete, metal, wood, gravel and water; jump; land; slide loop; hurt; death |
| Impacts | bullet hits on concrete, metal, thin metal, wood, dirt, water, glass and flesh |
| Feedback | hit marker, headshot, kill |
| Interface | hover, click, back, ready, countdown beep, match start, victory, defeat |
| Ambience | a coastal wind and surf bed, and a machinery hum |

### How a gunshot is built

A gunshot has an onset click and a band-limited crack. Under them sit a blast centred on a per-weapon frequency, a
falling thump and a small mechanical clack. Two or three slapback echoes and a convolution tail follow. The far
version is the same shot low-passed, with the echoes, tail and a low rumble raised, so distant fire sounds dull and
rolling rather than just quieter.

The recipe was tuned against measurements, not by ear alone. A first version put most of its energy below 150 Hz
and lost 3–8 dB through a laptop speaker model (a 200 Hz high-pass). The final shots lose 0.9–1.8 dB.

### Weapon handling follows the animation

Reloads, pumps and bolts are made of single actions, such as `bolt_back` or `pump_fwd`, not one long recording.
Each plays at the fraction of the action where the weapon rig shows it (`GameAudio.s_Cues`), so a cancelled or
interrupted reload stops sounding where the animation stops. On a remote shotgun, the shell inserts are counted from
the replicated progress restarting with each shell.

## Import and volume

`AudioFactory` (run by `SaltlineBuild.Setup`, or from Saltline > Build > Sound bank) imports the files:

- Effects: mono, Vorbis at quality 0.8, decompressed on load.
- Ambience loops: mono, Vorbis at quality 0.6, compressed in memory.

It then builds `Resources/SoundBank.asset`. Each sound's volume is `10^((target − measured) / 20)`, where the
target is a loudness for its kind, set in `AudioFactory.SpecFor`. The mix is therefore set by numbers that can be
checked, and the build warns about any sound that is too quiet for its target even at full volume. There are none
now.

| Kind | Target (LUFS) |
|---|---|
| Near shots | −13 (sniper) to −17 (SMG, pistol) |
| Far shots | 3 dB under the near shot |
| Whizz | −18 |
| Kill, headshot, hit marker | −20, −21, −24 |
| Hurt, flesh impact | −21, −22 |
| Footsteps, other impacts | −25 |
| Weapon handling | −27 |
| Interface | −26 to −32 |
| Ambience bed, machinery hum | −30, −26 |

## Playback (`GameAudio`)

- **Voices.** 40 positional and 12 non-positional voices are pooled. Each sound has a voice limit and a priority.
  When the pool is full, the oldest voice of the lowest priority is stolen, but only if that priority is no higher
  than the new sound's; otherwise the new sound is dropped. The same variant never plays twice in a row, and the
  pitch varies slightly.
- **Distance.** Sounds use logarithmic roll-off between each sound's minimum and maximum distance, and sounds past
  their maximum are not started. Beyond 25 m a sound is delayed by its distance over the speed of sound (343 m/s),
  so a shot at 100 m is heard about 0.3 s after its muzzle flash.
- **Near and far shots.** A remote shot plays both layers, crossfaded by distance. The near layer fades out
  between 12 and 55 m, and the far layer fades in between 8 and 40 m. The local player's own shots are
  non-positional.
- **Occlusion.** A ray from the listener to the source is tested against map geometry. If it is blocked, the sound
  is low-passed at 1400 Hz and lowered to 55%.
- **Whizz.** A bullet passing within 0.3–2.5 m of the listener, through open air, plays a whizz. A bullet the
  host says hit the listener does not: being hit plays the hurt sound instead.
- **Impacts.** Impacts come from the effects system with their surface. Body hits use the flesh sound. At most four
  impacts start per frame, which caps the shotgun's pellets. Another player's shot reaches each machine from the
  host after the host has resolved it. The shot carries which pellets hit a player, and that decides whether this
  machine shows a body hit or a wall hit. Without it, a hit on a strafing player could show as a hit on the wall
  behind them, because the host rewinds to where the shooter saw them. When the local player is hit, no impact is
  shown or played on their own body: the hurt sound covers it.
- **Movement.** Each player is tracked for footsteps. A step plays every 0.75 + 0.2 × speed metres, which is about
  2 steps a second crouched, 3 running and 3.3 sprinting. The surface comes from a downward ray and the map's
  `SurfaceTag`. Each player also gets jump, landing (after more than 0.25 s in the air, louder for faster falls)
  and slide sounds. The local player's own body sounds are non-positional and at half volume.
- **Feedback.** The hit marker, headshot and kill sounds play when the local player scores a hit. Hurt sounds are
  panned toward the attacker.
- **Match.** The countdown beeps in the last three seconds of warmup. Match start plays when the match goes live,
  and victory or defeat at the results (a draw plays defeat).
- **Interface.** Hover and click sounds are attached to the whole UI tree, so new screens get them without extra
  code.
- **Ambience.** The coastal bed fades between levels: silent while booting, half in the menu and full on a map.
  Kappa has three machinery emitters (`AmbientEmitter`), at the pump house and the two pump sheds.

- **Master limiter.** Unity's mixer has no ceiling of its own, so overlapping loud sounds would clip. A look-ahead
  peak limiter (`MasterLimiter`) sits on the active listener and processes the whole output. It holds the output
  under −1 dBFS, with 2 ms of look-ahead and about 120 ms of recovery. The gunshot targets above leave room for
  held fire, so the limiter should only catch rare pile-ups. Its input peak and the share of time it worked are
  logged in test runs.

Settings has Effects, Interface and Ambience volume sliders under the existing master volume.

## Testing

- `AudioTests` (EditMode) checks these things:
  - the bank matches the generator manifest;
  - every volume came from a loudness target, without clamping;
  - every sound the code can ask for exists, and every sound in the bank is used;
  - the import settings;
  - Kappa's emitters;
  - the limiter leaves audio under the ceiling untouched;
  - the limiter holds overlapping bursts at 2.5 times full scale under the ceiling, then recovers fully.
- A player started with `-audiocapture file.wav` records the limiter's output (what reaches the speakers) to a
  WAV file. With `-autotest`,
  `-uitest` or `-audiocapture`, it also logs play counts on exit (`[SALTLINE][AUTOTEST] audio: ...`), including
  voices stolen, sounds out of range, sounds muffled and names not found.
- `Tools/run_relay_tests.sh combataudio` runs the combat test over Relay with a host that renders and records. The
  client moves, then shoots the host with the sniper rifle and the pistol, and then the host shoots back. The play
  counts and the recorded mix are in `AutoTest/combataudio_*`.

## Limitations

- **Synthesised sounds.** The sounds are synthesised, not recorded. They are original and consistent, but they do
  not have the detail of recorded and layered weapon foley in a commercial shooter.
- **Simple occlusion.** Occlusion is one ray and a filter, with no paths around corners and no reverb zones.
  Indoor and outdoor spaces share the tail baked into each sound.
- **Laptop speakers.** The loudness targets were set with a speaker model, not on real speakers in a listening
  test. The machinery hum still loses about 4 dB through the laptop model and landings about 2.6 dB. Both are low
  sounds by nature.
