# Soldier rifle animations

`Assets/SourceModels/Solider/Rigged/Soldier_Bot_Animations.blend` contains eight
editable actions: Rifle_Idle, Rifle_Walk, Rifle_Jog, Rifle_Sprint, Rifle_Aim,
Rifle_Fire, Rifle_Hit and Rifle_Reload. The original Soldier_Rig_Review.blend,
source soldier GLB and M16 GLB are preserved. Choose an action on Soldier_Rig
in Blender's Action Editor. Locomotion is in place; navigation moves the actor.

Enemy and RemotePlayer now use the generated weighted soldier, rifle graph and
M16. Their animation owner has a saved IKComponent for UpperArm.L > LowerArm.L >
Hand.L. Select the owner in the editor to inspect the rig, target and elbow hint.
The M16 socket follows Hand.R, with mesh geometry calibrated to that bone.
EnemySoldierBot updates the independent support target in LateUpdate from the
current Hand.R pose and the calibrated supportGripOffset. The final pose refresh
applies IK after the target moves, avoiding a frame of weapon-grip lag.

MovementSpeed selects idle/walk/jog/sprint at 0.05, 2.4 and 5.8 m/s. HasTarget,
Shoot and Hit activate masked upper-body layers. Reload activates a two-second
reload gesture; SupportHandIK releases the support hand during the authoritative
reload interval. Ammunition remains owned by existing gameplay logic. This first
pass does not detach/replace the magazine or animate the weapon's internal parts.
Death continues to use the existing ragdoll system. Skin weights, glove contact
and foot placement are first-pass assets intended for correction in Blender.

Network protocol 13 adds host-to-client bot reload effects on channel 20.
Rebuild both peers. Late joiners do not reconstruct an already-running reload
gesture; subsequent reloads animate normally. The authored clip is two seconds;
changing botReloadDuration changes gameplay/IK release timing, not clip speed.

Tools/create_bot_animations.py runs inside the original review Blender scene
after importing the M16 source. It refuses to replace existing named actions.
Tools/export_bot_assets.py exports weighted vertices, humanoid bindings and baked
poses with the engine's native v5 formats. Tools/install_bot_assets.py wires the
two prefabs and registers assets. Changing the rig or grip calibration requires
re-exporting all eight clips and updating supportGripOffset together.

Validation: Tools/verify_bot_assets.cpp checks actual native readers and eight
sampled clip palettes. Tools/run_bot_smoke.py runs the actual Enemy and RemotePlayer
prefabs in isolated Vulkan scenes with the managed bot script, attachment and IK
checks. It writes fixtures/results under ignored Tools/Native/BotSmoke.

Validation on 2026-10-06: editor/runtime and both managed assemblies build;
IK/attachment/managed engine tests and gameplay tests pass. All eight native
clips load and evaluate. Both live Vulkan prefab probes pass their gameplay,
socket and IK assertions, but the native processes hang after reporting
"Benchmark: shutting down engine". The smoke runner reports a failed timeout;
clean Vulkan shutdown remains unresolved.

Performance update: both prefabs now use `Bots/Soldier/Soldier_Game.plutomesh`.
The game mesh has 65,157 exported vertices and 96,129 triangles, compared with
93,538 vertices and 137,328 triangles in the preserved full-detail mesh. All 46
material sections, UVs, 63 joints and eight clips remain. Head and hand influences
reduce decimation in those areas. `Soldier_Bot_Game.blend` contains the game mesh
alongside the hidden original mesh; `Tools/optimize_bot_assets.py` reproduces it
from the original animation file.

The engine now shares deformed vertex buffers between the main camera and its
temporal camera-stack overlays for the same scene frame. Camera/depth histories
remain independent. This prevents world shadow casters from being skinned and
uploaded again for each weapon overlay. Shared geometry is retained safely by
the views; asset-cache invalidation detaches it rather than freeing another
view's resources. Standalone views keep their own skinning history.

Skinned shadow-cluster bounds cache per-bone bind-space boxes at topology changes.
Pose updates transform and merge those boxes, rather than scanning every triangle.
The bounds conservatively contain normalized positive-weight skinning, including
unweighted vertices and reflected/nonuniform bone transforms. Invalid bounds
disable cluster culling. Arbitrary non-skeletal dynamic meshes retain exact bounds.

`Tools/benchmark_bot_skinning.cpp` compares nine bots across three views, including
CPU deformation, vertex staging copies and shadow bounds. On this machine:
98.9 ms baseline, 14.1 ms with renderer changes, 8.3 ms with the game mesh.
These are preparation microbenchmark timings, not whole-game FPS or GPU timings.
Rebuild/restart the editor and restart Play to load the changed prefab meshes.

Performance validation on 2026-10-06: rebuilt editor/runtime, ScriptCore and game.
CPU skinning, Vulkan/OpenGL skinning reuse, camera-stack, shadow membership and
IK regression checks pass; all 16 gameplay test groups pass. Native asset checks
verify the lighter mesh and all eight clips. A fresh in-game profiling capture
is still needed to measure whole-frame improvement; the previously documented
Vulkan shutdown timeout has not been resolved by this work.
