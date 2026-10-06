Soldier first-pass rig for manual review

Open Soldier_Rig_Review.blend in Blender. It is also left open in the connected
Blender session in Pose Mode, with CTRL_Hand.R selected.

The original ../Solider.glb was preserved. This copy is normalized to 1.8 metres.
The Blender file includes 52 deform bones, hand/foot IK targets and elbow/knee
pole controls. Bone collections separate Deform Bones, IK Controls and Weapon
Sockets. Enable Deform Bones to adjust bone placement in Edit Mode.

Move CTRL_Hand.L/R or CTRL_Foot.L/R in Pose Mode to test deformation. Rotate
hand/foot controls for orientation. Alt-G and Alt-R reset control transforms.

Review shoulder and wrist placement, curled finger joints, elbows, knees,
clothing seams and accessory weights. The face follows head/neck; no facial
rig is supplied. This is a first-pass rig, not authored locomotion/reload motion.

Blender heat weighting failed on the disconnected equipment. A custom distance
bind uses anatomical regions, normalized to at most four bone influences per
vertex. Small detached equipment is rigid-bound. All vertices have weights.
Exact surface seams were welded while preserving UV/material loops.

Tools/rig_soldier_weights.py regenerates the INITIAL skin weights after editing
rest bones. It overwrites manual weight painting, so save a copy before using it.
The script expects Soldier_Body_Review and Soldier_Rig object names.

Soldier_Rig_Review.glb is a separate review export: one skin, 52 deform joints,
no Blender controls, no animations. After corrections, re-export rig+mesh with
Skinning and Deformation Bones Only enabled, Rest Position enabled, Animations
OFF. The GLB does not automatically update when saving the Blender file.

In PlutoGE add Animation and Two Bone IK Rig to the animation owner; select its
skinned mesh and UpperArm.L > LowerArm.L > Hand.L (or .R). Use the editor's target
and hint controls. Import the corrected GLB before replacing gameplay prefabs.
