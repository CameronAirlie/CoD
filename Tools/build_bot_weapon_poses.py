"""Rebuild reachable rifle/pistol holds from the preserved soldier animation rig.

blender --background Assets/SourceModels/Solider/Rigged/Soldier_Bot_Animations.blend
        --python Tools/build_bot_weapon_poses.py
Writes clips and a separate editable working blend; never changes bind geometry.
"""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Matrix, Vector, Quaternion

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'Tools'))
from plutoge_blender_export import export_clip, engine_matrix
from export_bot_assets import export_weighted_mesh

out = ROOT / 'Assets/Bots/Soldier'
rig = bpy.data.objects['Soldier_Rig']
rest = {bone.name: bone.matrix_local.copy() for bone in rig.data.bones}
rig.animation_data_create()
bpy.context.scene.render.fps = 24
rig.animation_data.action = bpy.data.actions['Rifle_Aim']
bpy.context.scene.frame_set(0)
bpy.context.view_layer.update()
rifle_objects = [obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name not in ('Soldier_Body_Review', 'Cube')]
rifle_world = {obj: obj.matrix_world.copy() for obj in rifle_objects}
rifle_grip = bpy.data.objects['PISTOL GRIP_M16A3_0']
old_rifle_grip = sum((rifle_grip.matrix_world @ Vector(corner) for corner in rifle_grip.bound_box), Vector()) / 8
durations = {'Idle': 2., 'Walk': 1.2, 'Jog': .8, 'Sprint': .6,
             'Aim': 2., 'Fire': .25, 'Hit': .5, 'Reload': 2.}
report = {}

def control(name, position, forward=None):
    matrix = rest[name].copy()
    if forward is not None:
        q = matrix.to_quaternion()
        matrix = ((q @ Vector((0, 1, 0))).rotation_difference(Vector(forward).normalized()) @ q).to_matrix().to_4x4()
    matrix.translation = Vector(position)
    rig.pose.bones[name].matrix = matrix

for weapon in ('Rifle', 'Pistol'):
    minimum_margin = 10.
    maximum_error = 0.
    for motion, duration in durations.items():
        name = f'{weapon}_{motion}'
        if name in bpy.data.actions:
            bpy.data.actions.remove(bpy.data.actions[name])
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data.action = action
        steps = round(duration * 24)
        for frame in range(steps + 1):
            bpy.context.scene.frame_set(frame)
            for bone in rig.pose.bones:
                bone.rotation_mode = 'QUATERNION'
                bone.matrix_basis = Matrix.Identity(4)
            t = frame / steps
            phase = t * math.tau
            stride = {'Walk': .20, 'Jog': .28, 'Sprint': .34}.get(motion, 0.)
            bob = (.012 if stride else .002) * (1 - math.cos(phase * 2))
            rig.pose.bones['Hips'].location.z = bob
            hit = math.sin(math.pi * t) * math.exp(-3 * t) if motion == 'Hit' else 0.
            rig.pose.bones['Chest'].rotation_quaternion = Quaternion((1, 0, 0), .06 - .12 * hit)
            rig.pose.bones['Spine'].rotation_quaternion = Quaternion((0, 0, 1), .012 * math.sin(phase))
            bpy.context.view_layer.update()
            # Protract the shoulders, rather than stretching either arm.
            for side, forward in [('L', (.85, -.45, -.06)), ('R', (-.92, -.30, -.05))]:
                shoulder = 'LeftShoulder' if side == 'L' else 'RightShoulder'
                position = rig.pose.bones[shoulder].head.copy()
                control(shoulder, position, forward)
            bpy.context.view_layer.update()
            for side in ('L', 'R'):
                foot = rest['CTRL_Foot.' + side].translation.copy()
                foot_phase = phase + (0 if side == 'L' else math.pi)
                foot.y += stride * math.sin(foot_phase)
                if stride:
                    foot.z += .05 * max(0, math.cos(foot_phase))
                control('CTRL_Foot.' + side, foot)
                control('CTRL_Elbow.' + side, ((.34 if side == 'L' else -.34), -.08, 1.12 + bob))
            recoil = .022 * math.sin(math.pi * min(1, t * 3)) * math.exp(-5 * t) if motion == 'Fire' else 0.
            ready_drop = .11 if motion == 'Idle' else .15 if motion == 'Sprint' else .04 if stride else 0.
            if weapon == 'Rifle':
                right = Vector((-.10, -.115 + recoil, 1.445 + bob - ready_drop))
                left = right + Vector((.02, -.245, .075))
            else:
                right = Vector((-.035, -.30 + recoil, 1.52 + bob - ready_drop))
                left = right + Vector((.05, .015, -.02))
            if motion == 'Reload':
                left = left.lerp(Vector((.04, -.18, 1.14 + bob)), math.sin(math.pi * t) ** 2)
            control('CTRL_Hand.R', right, (0, -1, 0))
            bpy.context.view_layer.update()
            firing = rig.pose.bones['CTRL_Hand.R']
            firing_matrix = Quaternion(Vector((0, -1, 0)), math.pi / 2).to_matrix().to_4x4() @ firing.matrix
            firing_matrix.translation = right
            firing.matrix = firing_matrix
            control('CTRL_Hand.L', left, (0, -1, -.1) if weapon == 'Rifle' else (0, -1, 0))
            # Palm into the foregrip / firing hand, rather than facing away.
            bpy.context.view_layer.update()
            controller = rig.pose.bones['CTRL_Hand.L']
            matrix = controller.matrix.copy()
            axis = Vector((0, -1, -.1) if weapon == 'Rifle' else (0, -1, 0))
            rotation = Quaternion(axis.normalized(), math.pi if weapon == 'Rifle' else -math.pi / 2).to_matrix().to_4x4()
            rotated = rotation @ matrix
            rotated.translation = matrix.translation
            controller.matrix = rotated
            bpy.context.view_layer.update()
            # Close the relaxed review fingers around the weapon/support palm.
            for side in ('L', 'R'):
                palm = left + Vector((0, -.09, .025)) if side == 'L' and weapon == 'Rifle' else right + Vector((0, -.09, -.015))
                for finger in ('Index', 'Middle', 'Ring', 'Little'):
                    first = rig.pose.bones[f'{finger}1.{side}']
                    axis = first.matrix.to_3x3()
                    plus = first.head + axis @ Vector((0, math.cos(.4), math.sin(.4))) * first.length
                    minus = first.head + axis @ Vector((0, math.cos(.4), -math.sin(.4))) * first.length
                    sign = 1 if (plus - palm).length < (minus - palm).length else -1
                    for joint, angle in enumerate((.35, .50, .30), 1):
                        if side == 'L' and weapon == 'Rifle': angle *= .6
                        elif finger != 'Index': angle *= 2.2
                        rig.pose.bones[f'{finger}{joint}.{side}'].rotation_quaternion = Quaternion((1, 0, 0), angle * sign)
            bpy.context.view_layer.update()
            for side, target in [('R', right), ('L', left)]:
                upper, lower, hand = (rig.pose.bones[f'{part}.{side}'] for part in ('UpperArm', 'LowerArm', 'Hand'))
                margin = upper.length + lower.length - (target - upper.head).length
                error = (hand.head - target).length
                minimum_margin = min(minimum_margin, margin)
                maximum_error = max(maximum_error, error)
                if margin < .025 or error > .004:
                    raise RuntimeError(f'{name} frame {frame} arm {side}: reach margin {margin:.4f}, error {error:.4f}')
            for bone in rig.pose.bones:
                for channel in ('location', 'rotation_quaternion', 'scale'):
                    bone.keyframe_insert(channel, frame=frame, group=bone.name)
        events = [(.4, 'ReloadMagOut'), (1.35, 'ReloadMagIn'), (1.7, 'ReloadFinish')] if motion == 'Reload' else []
        export_clip(out / f'{name}.plutoclip', rig, action, duration, events)
    rig.animation_data.action = bpy.data.actions[f'{weapon}_Aim']
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    right = rig.matrix_world @ rig.pose.bones['Hand.R'].matrix
    left = rig.matrix_world @ rig.pose.bones['Hand.L'].matrix
    report[weapon] = {'supportGripOffset': list(engine_matrix(right.inverted() @ left).translation),
                      'supportRotationQuaternion': list(engine_matrix(right.inverted() @ left).to_quaternion()),
                      'minimumReachMargin': minimum_margin, 'maximumWristError': maximum_error}

# Place the rifle grip in the firing palm instead of at the wrist joint.
rig.animation_data.action = bpy.data.actions['Rifle_Aim']
bpy.context.scene.frame_set(0)
bpy.context.view_layer.update()
hand = rig.matrix_world @ rig.pose.bones['Hand.R'].matrix
placement = Matrix.Translation(hand.translation + Vector((0, -.09, -.015)) - old_rifle_grip)
materials = []
for obj in rifle_objects:
    originals = list(obj.data.materials)
    for material in originals:
        if material not in materials: materials.append(material)
    indices = [materials.index(originals[polygon.material_index]) for polygon in obj.data.polygons]
    obj.data.materials.clear()
    for material in materials: obj.data.materials.append(material)
    for polygon, index in zip(obj.data.polygons, indices): polygon.material_index = index
    obj.parent = None
    obj.matrix_world = hand.inverted() @ placement @ rifle_world[obj]
report['rifleMesh'] = export_weighted_mesh(out / 'M16_HandSpace.plutomesh', rifle_objects, None,
    [f'project://SourceModels/m16_assault_rifle/M_m16_assault_rifle_{i}.plutomaterial' for i in range(len(materials))],
    'project://SourceModels/m16_assault_rifle/m16_assault_rifle.glb')
for obj in rifle_objects:
    local = obj.matrix_world.copy()
    obj.parent = rig
    obj.parent_type = 'BONE'
    obj.parent_bone = 'Hand.R'
    obj.matrix_world = hand @ local

# Reuse the existing P-12 gun geometry, excluding all first-person arms/hands.
with bpy.data.libraries.load(str(ROOT / 'Assets/SourceModels/Weapons/pistol.blend'), link=False) as (source, target):
    target.objects = [name for name in source.objects if not name.startswith(('LeftHand', 'RightHand')) and name != 'pistol_HandsRig']
pistol = [obj for obj in target.objects if obj and obj.type == 'MESH']
for obj in pistol:
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = None
    obj.modifiers.clear()
rig.animation_data.action = bpy.data.actions['Pistol_Aim']
bpy.context.scene.frame_set(0)
bpy.context.view_layer.update()
hand = rig.matrix_world @ rig.pose.bones['Hand.R'].matrix
grip = next(obj for obj in pistol if obj.name.startswith('Pistol Grip'))
grip_center = sum((grip.matrix_world @ Vector(corner) for corner in grip.bound_box), Vector()) / 8
placement = Matrix.Translation(hand.translation + Vector((0, -.09, -.015))) @ Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Translation(-grip_center)
materials = []
for obj in pistol:
    original_materials = list(obj.data.materials)
    for material in original_materials:
        if material not in materials:
            materials.append(material)
    polygon_materials = [materials.index(original_materials[polygon.material_index]) for polygon in obj.data.polygons]
    obj.data.materials.clear()
    for material in materials:
        obj.data.materials.append(material)
    for polygon, material_index in zip(obj.data.polygons, polygon_materials):
        polygon.material_index = material_index
    obj.matrix_world = hand.inverted() @ placement @ obj.matrix_world
report['pistolMesh'] = export_weighted_mesh(out / 'P12_HandSpace.plutomesh', pistol, None,
    [f'project://Weapons/Materials/{material.name.split(".")[0]}.plutomaterial' for material in materials],
    'project://SourceModels/Weapons/pistol.blend')
for obj in pistol:
    local = obj.matrix_world.copy()
    obj.parent = rig
    obj.parent_type = 'BONE'
    obj.parent_bone = 'Hand.R'
    obj.matrix_world = hand @ local
    obj['weapon_pose'] = 'Pistol'
    obj.hide_render = True
    obj.hide_set(True)

manifest_path = out / 'manifest.json'
manifest = json.loads(manifest_path.read_text())
manifest['weaponPoses'] = report
manifest['supportPosition'] = report['Rifle']['supportGripOffset']
manifest['supportRotationQuaternion'] = report['Rifle']['supportRotationQuaternion']
manifest['weapon'] = report['rifleMesh']
manifest['animations'] = {f'{weapon}_{motion}': duration for weapon in ('Rifle', 'Pistol') for motion, duration in durations.items()}
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
(out / 'Soldier.plutoanim').write_text('AnimationSetVersion=1\n' + ''.join(f'Clip=project://Bots/Soldier/{name}.plutoclip\n' for name in manifest['animations']))
rig.animation_data.action = bpy.data.actions['Rifle_Idle']
bpy.context.scene.frame_set(0)
bpy.context.scene.frame_start = 0
bpy.context.scene.frame_end = 48
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'Assets/SourceModels/Solider/Rigged/Soldier_Bot_WeaponPoses.blend'))
print('WEAPON_POSES ' + json.dumps(report))
