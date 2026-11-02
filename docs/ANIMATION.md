# Character and animation pipeline

## The operator

`ArtSource/Blender/operator_build.py` builds the operator in Blender, all from code:
- body, gear and helmet;
- an armature with Mixamo-compatible bone names (`Hips`, `Spine`, `Spine1`, `Spine2`, `LeftArm`, ...);
- skin weights (rigid weights for armour plates, the helmet and boots; transferred weights for cloth);
- UVs, and a bake of albedo, normal and a mask (metallic, AO, smoothness);
- three LODs, exported as `Assets/_Saltline/Art/Characters/Operator/SK_Operator.fbx`.

`Assets/_Saltline/Scripts/Editor/CharacterArtFactory.cs` runs as part of `SaltlineBuild.Setup`. It:
- imports the model as a Unity **Humanoid**, and stops the setup if the avatar is not valid;
- creates the main and team-colour materials (the second material slot is the team patch and armband);
- imports the clips in `Art/Characters/Mixamo/` as Humanoid clips:
  - locomotion loops in place;
  - body yaw comes from the aim;
  - height comes from the feet;
- builds `AC_Operator.controller`, which has:
  - a standing 2D blend tree: idle, then walk, run and sprint rings in 8 directions;
  - a crouch tree: crouch idle plus 8 directions;
  - an air state;
  - five death states;
- builds `Prefabs/Player/P_Operator.prefab` and registers it in the PresentationLibrary.

Without the model, players fall back to the hitbox-capsule bodies.

## At runtime

`PlayerView` instantiates the operator for every player and passes it the player's `RenderPose` each frame. `OperatorRig` then works in two steps.

**Update** sets the animator parameters:
- local move direction × gait (1 walk, 2 run, 3 sprint);
- crouch, grounded and dead.

It also scales playback speed (0.7–1.5×) so the feet keep pace with the real ground speed.

**LateUpdate**, after the animator, adjusts the body procedurally:
- shifts the hips so the head sits where the host's hitbox puts it:
  - the crouch clips are ~0.3 m too tall, the slide pose ~0.3 m too tall, and the jump loop ~0.4 m too low;
  - on the ground the legs are re-solved with the same two-bone IK, so the feet stay planted;
  - standing poses may rise by at most 5 cm;
- turns the shoulders into a bladed stance and pitches the spine with the aim, and turns the head back to the aim;
- holds the weapon along the true aim direction from the right shoulder:
  - lowered when sprinting;
  - down and in while switching weapons;
  - tilted during reloads;
- uses two-bone IK to put the right hand on `Grip_R` and the support hand on `Grip_L`:
  - the support hand rides the pump on the shotgun;
  - during a magazine reload the support hand goes to the magazine well, down to the belt pouch, and back.

Hit detection never uses these bones. The host tests the analytic `HitboxModel` capsules, and the character is authored to those proportions.

`SALTLINE -batchmode -maptour Kappa -characters -capture <dir>` renders the operator in about 20 poses and logs:
- how far each hand ended from its IK target;
- the visual eye height against the hitbox eye height;
- the clip speed against the ground speed.

## Mixamo clips (not in the repository)

The clips are from Adobe Mixamo. They may be used in the game, but the files may not be redistributed. They are
listed in `.gitignore`. On a new machine, download them again from mixamo.com:
- format FBX, without skin;
- give each file the name below and put it in `Assets/_Saltline/Art/Characters/Mixamo/`.

Most come from the **Rifle 8-Way Locomotion Pack**:

| Group | Clips |
|---|---|
| Idle | `idle`, `idle_aiming`, `idle_crouching`, `idle_crouching_aiming` |
| Walk (8 directions) | `walk_forward`, `walk_forward_left`, `walk_forward_right`, `walk_left`, `walk_right`, `walk_backward`, `walk_backward_left`, `walk_backward_right` |
| Run (8 directions) | `run_*`, with the same 8 suffixes |
| Sprint (8 directions) | `sprint_*`, with the same 8 suffixes |
| Crouch walk (8 directions) | `walk_crouching_*`, with the same 8 suffixes |
| Turns | `turn_90_left`, `turn_90_right`, `crouching_turn_90_left`, `crouching_turn_90_right` |
| Jump | `jump_up`, `jump_loop`, `jump_down` |
| Deaths | `death_from_the_front`, `death_from_the_back`, `death_from_right`, `death_from_front_headshot`, `death_from_back_headshot`, `death_crouching_headshot_front` |

The controller uses the idle, locomotion, crouch, `jump_loop` and the first five death clips. Turns, jump up/down and the crouching death are imported but not used yet.

## Known limitations

- No ground-adaptive foot IK: on stairs and slopes the feet follow the clip, not the ground. The leg IK is used only for the body-height correction.
- While moving, the clips lean forward, so the visual head is 2–10 cm lower than the hitbox head (inside its 12.5 cm radius).
- The bolt-action rifle's bolt cycles, but the right hand stays on the grip.
- Remote players' weapon parts animate from the replicated action and progress. There is no per-shot blowback in third person.
- The local player has no first-person arms yet. The first-person view shows only the weapon.
