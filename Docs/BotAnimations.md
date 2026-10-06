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

Second performance pass (2026-10-06), based on frame 9267: 31.70 ms total,
26.86 ms viewport, two 4.15/4.32 ms deformation scopes and two 3.35/3.53 ms
RHI.BeginFrame scopes. The lighter mesh was already active. The next targets
were duplicate render-texture/main-view deformation and large Vulkan uploads.

Render-texture cameras now share skinning within their pass, then provide a
single-use source to the immediately following main view and camera overlays.
Camera history remains per view; material/packet aging uses a separate monotonic
view counter. Empty texture passes expire old sources, and removing cameras or
changing their order detaches caches safely. RenderTextures must run each scene
frame before its on-screen view, including an empty pass when no cameras exist.
Poses remain immutable between these submissions.

Vulkan uploads above 64 KiB now copy through persistent mapped staging blocks
instead of embedding the full vertex payload in vkCmdUpdateBuffer commands.
Blocks are append-only within each submission and recycled only after its
in-flight fence completes. Noncoherent ranges flush before submission; transfer
barriers remain around destination updates. Small updates retain the inline path.
A new command-pool-reset trace distinguishes reset overhead from the vertex
uploads also included in the existing RHI.BeginFrame scope.

The staging arena retains peak demand per in-flight slot (16 MiB blocks, larger
for individual oversized uploads). This trades reusable host-visible memory for
less command-stream copying and driver allocation work. No additional asset
simplification or animation/IK quality reduction was applied in this pass.

The completed skeletal vertex stream is also retained by shared ownership until
its upload is recorded, removing the full per-pose CPU staging-array copy. The
ordinary span-based dynamic-mesh API still copies its input. Shared streams must
remain immutable until Render records the upload; renderer-owned skinning obeys
this contract, and camera reuse does not queue another upload.

The updated nine-bot/three-view CPU preparation microbenchmark measured 8.02 ms
with the game mesh and staging-array copies versus 4.82 ms with shared vertex
streams in the same run (about 40% lower). It excludes GPU uploads and full scene
rendering. Removing the additional render-texture/main-camera pass is a separate
saving confirmed by zero main-view deformation and upload counts in regression
checks. Whole-game FPS still requires a fresh profiling capture after restart.

Final second-pass validation: editor/runtime builds pass. All eight targeted
checks pass: Vulkan/OpenGL skinning, Vulkan preparation cache, Vulkan/OpenGL
camera stack, Vulkan render textures, VSM membership and large-buffer uploads.
The skinning checks include independent actors, animated motion/history,
material edits on a reused skinning frame, texture-camera reordering and removal.
The upload check verifies CPU-source lifetime, partial destination offsets,
staging-block rollover and more submissions than in-flight slots.

## GPU skinning (2026-10-06)

The engine now uses compute skinning automatically on Vulkan/OpenGL when the
GpuSkinning shader artifact is present, retaining the CPU fallback. Animation
and IK still produce bone matrices on the CPU; vertex deformation and previous-
pose positions run on the GPU. Shared cameras reuse the completed output. No
soldier asset, animation or gameplay component changes are required.

Nine soldiers now upload about 71 KiB of bone palettes per changed pose instead
of about 40 MiB of vertex streams. The same-run Vulkan renderer benchmark in
`Tools/benchmark_bot_gpu_skinning.cpp` measured active CPU time of 10.19 ms for
CPU skinning versus 0.43 ms for GPU skinning, with 0.68 ms of GPU skinning work.
This uses 256x256 lit geometry, shadows off, 12 warm-up and 40 measured frames;
it is not a whole-game FPS measurement. All samples rendered successfully, but
the benchmark stalled during final device/asset cleanup and was terminated.

Editor/runtime and shader packages were rebuilt. Vulkan/OpenGL GPU readback and
rendering tests pass, including independent actors, motion history, shadows,
large meshes and shared render-texture cameras. Restart the editor before a new
game capture; captures now include GPU skinning dispatch and palette-byte counts.
See the engine's `docs/GPU_SKINNING.md` for architecture and validation details.

## Universal Animation Library locomotion

The shared Soldier graph now uses the existing in-place UAL `Idle_Loop`,
`Walk_Loop`, `Jog_Fwd_Loop` and `Sprint_Loop` clips from
`Assets/SourceModels/UAL1_Standard`. The engine retargets their humanoid channels
to the soldier skeleton. Navigation still moves the actor, using the existing
MovementSpeed thresholds. Both Enemy and RemotePlayer reference this graph.

An always-active looping Rifle Hold override layer uses the existing Rifle_Idle
pose. The upper-body mask leaves hips and legs at zero weight, blends Spine at
0.25, Chest at 0.75, and UpperChest and its descendants at 1. Aim, fire, hit and
reload layers follow the hold layer in priority order and use the same mask.
The existing M16 socket, support-hand IK and reload IK release remain in use.
Rifle-specific actions remain the authored first-pass clips; this change
replaces locomotion and adds persistent weapon holding, not new rifle mocap.

Run `python Tools/build_bot_animation_graph.py` to regenerate only the graph.
`install_bot_assets.py` uses the same builder, so reinstalling bot assets keeps
the UAL setup. The native validator now exercises UAL state transitions and
masked action playback against the soldier skeleton.

Validation for the UAL change: the native validator passes all four UAL
states, return to idle, and masked action playback with finite soldier palettes.
Both live Vulkan prefab probes report PASS for firing/reload, rifle attachment
and support-hand IK. The previously documented shutdown hang recurs after
benchmark completion. Visual movement quality still needs review in play.

UAL retarget correction: soldier mesh humanoid slots must have empty
`sourceBoneName` overrides. The exporter previously filled them with soldier
bone names, which opted out of automatic UAL name matching and left legs static.
Both soldier meshes are corrected; `export_bot_assets.py` now writes automatic
source mappings while retaining explicit target joint indices.
`Tools/repair_bot_retarget_mappings.py` repairs existing v5 soldier assets.
The validator now requires changing upper-leg matrices during walk/jog/sprint;
it reproduced the missing-leg-animation failure before repair and passes after.
