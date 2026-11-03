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

## First-person arms

The local player sees the operator's own arms. `finalize_arms` in `ArtSource/Blender/operator_build.py` builds
the same body, keeps only the faces skinned mostly to the arm bones, bakes their textures (`T_OPA_D/_N/_M`) and
exports `Art/Characters/Arms/SK_OperatorArms.fbx` with the full skeleton. `CharacterArtFactory` imports it without
an Animator and makes `P_OperatorArms`. `ViewmodelController` spawns one on the viewmodel layer and, every frame
after placing the gun, calls `FirstPersonArms.Pose`:
- the bones go back to the bind pose;
- each shoulder joint is moved to an anchor chosen per weapon, below and behind its grips and outside the
  viewmodel camera's view, so the cut at the shoulder never shows and the elbows stay bent;
- each hand is placed by its middle knuckle and its axes (which way the fingers point, which way the knuckles
  run), worked out from the gun's markers as a wrap around what it holds; the shared two-bone IK (`ArmChain`,
  also used by `OperatorRig`) then reaches the wrist;
- what the support hand holds was measured on each model around `Grip_L` (the preview tool logs the outlines,
  found with raycasts): the rifle's handguard, the sniper's forend and the shotgun's pump are wrapped as round
  sections of their own radius and centre, with the palm underneath, the fingers up the right side and the thumb
  along the left side; the SMG's forward magazine is held like a vertical grip; on the pistol the support hand
  wraps over the firing hand;
- the fingers curl about axes measured from the bind pose (palms down) by angles that wrap them around that
  shape; the right index finger is bent until its tip reaches the trigger blade (further while the trigger is
  held); each thumb is turned until its tip lies on a line along the gun (the left side of the handguard, or
  forwards along the pistol's frame);
- in a magazine reload the support hand goes to the magazine, down out of view while the old one falls, and
  comes back holding the new one (it follows `WeaponRig`'s magazine as it is inserted);
- a shell reload takes the support hand off the pump and down to the shells during the lead-in, then for each
  shell up to the loading port, a short push into the tube, and back down (`WeaponState.ShellInserting` tells the
  arms which phase they are in);
- the support hand rides the shotgun's pump; the right hand lifts and pulls the rifle's bolt;
- the reload path is eased in the viewmodel camera's space (so the hand keeps up while the player runs or turns),
  and entering or leaving a reload, including a cancelled one, blends over 0.1 to 0.2 s.

The arms take the team colour on their accent material.

Review tools:
- `Saltline.EditorTools.ArmsPreview.Render` (batch mode, about 10 s) renders every weapon with the arms in each
  pose from the eye and from two outside angles into `arms_<weapon>.png`, and logs the wrists, knuckles and
  fingertips in gun space. `-hipShift x,y,z` tries another hip position for every weapon without editing the data.
- `-maptour Kappa -weapons` renders each weapon in the built game at the hip, aimed, in two reload phases and
  (pump and bolt) mid-cycle, and logs each wrist's distance from its IK target.

`FirstPersonArmsTests` checks the IK, the prefab, and that on every weapon both hands reach their grips (hip, aimed
and mid-reload) and both shoulder cuts stay outside the view.

The hip positions (`viewmodelOffset`) were raised by 3.5 cm and brought 3 cm closer after the arms went in, so the
support hand and more of the receiver show at the bottom right of the screen.

During a reload the first-person gun no longer drops out of view: it rises 7 cm, moves in towards the centre and
cants left side up (`ViewmodelController.ReloadShift` / `ReloadTilt`), so the magazine well, the new magazine and the
support hand stay on screen. A magazine reload eases into that pose and back out with its progress; a shell reload
holds it through the lead-in and every shell and eases out over about 0.2 s when it ends or is cancelled.

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
- In third person the bolt-action rifle's bolt cycles, but the right hand stays on the grip (the first-person arms do work the bolt).
- Remote players' weapon parts animate from the replicated action and progress. There is no per-shot blowback in third person.
- The first-person arms are posed procedurally, not from authored clips: reload and cycling motions are simple keyed paths, and there is no inspect, melee or wall-lean motion.
- The empty-reload charging handle moves on its own; no hand pulls it.
- No shell is drawn in the support hand during a shell reload, and no magazine in it on the way down to the
  pouch: only the new magazine (which `WeaponRig` shows below the gun) is held.
- The fingers wrap simple shapes (a round section, a box) with fixed curls, measured once per weapon class; there is
  no per-finger collision, so a finger can sink a few millimetres into the gun or float above it. The trigger
  finger stops a few millimetres short of the blade on some weapons.
