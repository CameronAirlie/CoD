"""Create original stylized weapon viewmodels, skeletal animations and native assets.

Blender --background --python Tools/create_weapons.py -- <project-root>
All dimensions below use game coordinates: X right, Y up, -Z forward.
"""
import json
import math
import sys
import uuid
from pathlib import Path
import bpy
from mathutils import Vector, Matrix, Quaternion

sys.path.insert(0, str(Path(__file__).parent))
from plutoge_blender_export import export_mesh, export_clip

ROOT = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
OUT = ROOT / 'Assets/Weapons'
SOURCE = ROOT / 'Assets/SourceModels/Weapons'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
COLORS = [('Gunmetal', (.12, .16, .20), .8, .32), ('Polymer', (.025, .035, .045), .1, .65),
          ('Sand', (.40, .29, .14), .25, .58), ('Sleeve', (.10, .14, .12), 0, .85),
          ('Glove', (.045, .055, .065), 0, .75), ('Brass', (.62, .40, .10), .85, .28),
          ('Accent', (.12, .43, .48), .35, .4)]
MATERIAL_REFS = [f'project://Weapons/Materials/{name}.plutomaterial' for name, *_ in COLORS]


def meta(path):
    identity = uuid.uuid5(uuid.NAMESPACE_URL, 'cod-weapons/' + path.relative_to(ROOT).as_posix()).hex
    Path(str(path) + '.plutometa').write_text(f'PLUTOASSET\t1\nID\t{identity}\nIMPORTER_VERSION\t1\n', encoding='utf-8')


def bpos(position):
    x, y, z = position
    return Vector((x, -z, y))


def decorate(obj, name, material, joint):
    obj.name = name
    obj.data.materials.append(bpy.data.materials[COLORS[material][0]])
    obj['joint'] = joint
    obj['material_index'] = material
    group = obj.vertex_groups.new(name=joint)
    group.add(range(len(obj.data.vertices)), 1, 'REPLACE')
    modifier = obj.modifiers.new('Viewmodel Skin', 'ARMATURE')
    modifier.object = RIG
    obj.parent = RIG
    return obj


def box(name, centre, size, material=0, joint='Weapon', bevel=.006, rotation=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=bpos(centre))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    obj.rotation_euler.x = math.radians(rotation)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new('Machined edges', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return decorate(obj, name, material, joint)


def cylinder(name, start, end, radius, material=0, joint='Weapon', vertices=12):
    start, end = bpos(start), bpos(end)
    direction = end - start
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=direction.length,
                                      location=(start + end) / 2)
    obj = bpy.context.object
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = direction.to_track_quat('Z', 'Y')
    return decorate(obj, name, material, joint)


def hands(pistol):
    right = (.045, -.14, -.28)
    left = (-.045, -.15, -.30) if pistol else (-.06, -.065, -.57)
    for side, palm, elbow in [('RightHand', right, (.24, -.40, .08)),
                              ('LeftHand', left, (-.25, -.35, .10))]:
        cuff = Vector(palm).lerp(Vector(elbow), .22)
        cylinder(side + ' Sleeve', elbow, cuff, .058, 3, side)
        cylinder(side + ' Cuff', cuff, Vector(palm).lerp(Vector(elbow), .14), .062, 1, side)
        box(side + ' Palm', palm, (.067, .075, .10), 4, side, .012, rotation=-12)
        sign = 1 if side == 'RightHand' else -1
        for finger in range(4):
            y = palm[1] + .025 - finger * .018
            start = (palm[0] - sign * .025, y, palm[2] - .032)
            mid = (palm[0] - sign * .050, y, palm[2] - .059)
            end = (palm[0] - sign * .067, y - .004, palm[2] - .033)
            cylinder(f'{side} Finger {finger}a', start, mid, .009, 4, side, 8)
            cylinder(f'{side} Finger {finger}b', mid, end, .009, 4, side, 8)
        cylinder(side + ' Thumb', (palm[0] + sign * .027, palm[1] + .027, palm[2]),
                 (palm[0] - sign * .025, palm[1] + .034, palm[2] - .031), .013, 4, side, 8)


def gun_geometry(kind):
    pistol = kind == 'pistol'
    box('Pistol Grip', (0, -.145, -.28), (.066, .18, .08), 1, rotation=-12)
    box('Trigger Guard Lower', (0, -.115, -.375), (.036, .018, .10), 0)
    box('Trigger Guard Front', (0, -.075, -.423), (.036, .078, .014), 0)
    box('Trigger', (0, -.067, -.367), (.015, .045, .012), 1)
    if pistol:
        box('P12 Slide', (0, .005, -.37), (.073, .078, .245), 0, 'Slide')
        box('P12 Frame', (0, -.055, -.35), (.065, .036, .20), 1)
        box('P12 Magazine', (0, -.183, -.274), (.047, .13, .062), 0, 'Magazine')
        box('P12 Baseplate', (0, -.252, -.273), (.07, .017, .08), 6, 'Magazine')
        cylinder('P12 Barrel', (0, .005, -.46), (0, .005, -.505), .02)
    else:
        lmg = kind == 'lmg'
        box('Receiver', (0, -.018, -.37), (.115 if lmg else .087, .115, .28), 0)
        box('Top Cover', (0, .05, -.38), (.12 if lmg else .083, .029, .26), 2 if lmg else 0, 'Slide')
        box('Handguard', (0, -.005, -.625), (.096, .095, .235), 2)
        box('Stock Spine', (0, -.025, -.145), (.055, .052, .18), 0)
        box('Stock Butt', (0, -.045, -.07), (.08, .145, .05), 1)
        for z in [-.53, -.57, -.61, -.65, -.69]:
            for x in [-.051, .051]:
                box('Cooling Slot', (x, 0, z), (.007, .029, .025), 1, bevel=.001)
        barrel_end = -.98 if lmg else -.85
        cylinder('Barrel', (0, .014, -.72), (0, .014, barrel_end), .019)
        cylinder('Muzzle Brake', (0, .014, barrel_end + .055), (0, .014, barrel_end - .015), .026, 1)
        for z in [-.48, -.45, -.42, -.39, -.36, -.33]:
            box('Rail Tooth', (0, .072, z), (.078, .013, .013), 1, bevel=.001)
        if lmg:
            box('Ammo Box', (-.055, -.13, -.405), (.185, .18, .18), 2, 'Magazine', .012)
            box('Ammo Box Lid', (-.055, -.03, -.405), (.19, .014, .19), 1, 'Magazine')
            for i in range(7):
                x = -.06 - i * .018
                cylinder('Belt Cartridge', (x, .02, -.39), (x, .02, -.45), .009, 5, 'Magazine', 8)
            for x in [-.055, .055]:
                cylinder('Folded Bipod', (x, -.04, -.75), (x, -.07, -.60), .011)
        else:
            box('AR24 Magazine', (0, -.165, -.408), (.055, .22, .10), 1, 'Magazine', .008, rotation=10)
            for y in [-.09, -.13, -.17, -.21]:
                box('Magazine Rib', (.03, y, -.408), (.008, .008, .085), 0, 'Magazine', .001)
        box('Charging Handle', (.07, .006, -.285), (.038, .022, .035), 0, 'Slide')
    # Open sight notch and front post, unobstructed from the aim pose.
    box('Rear Sight Base', (0, .046 if pistol else .081, -.275), (.061, .012, .027), 1, bevel=.002)
    if not pistol:
        box('Front Sight Riser', (0, .062, -.70), (.020, .043, .030), 1, bevel=.002)
    for x in [-.024, .024]:
        box('Rear Sight', (x, .062 if pistol else .095, -.275), (.012, .024, .018), 1, bevel=.002)
    box('Front Sight', (0, .060 if pistol else .095, -.465 if pistol else -.70), (.009, .029, .014), 1, bevel=.002)
    box('Sight Highlight', (0, .072 if pistol else .109, -.465 if pistol else -.70), (.006, .005, .015), 6, bevel=.001)
    hands(pistol)


def create_rig(kind):
    bpy.ops.object.armature_add(enter_editmode=True)
    rig = bpy.context.object
    rig.name = kind + '_HandsRig'
    rig.data.edit_bones.remove(rig.data.edit_bones[0])
    heads = [('Root', (0, 0, 0), None), ('Weapon', (0, 0, 0), 'Root'),
             ('Slide', (0, 0, -.37), 'Weapon'), ('Magazine', (0, -.12, -.28 if kind == 'pistol' else -.405), 'Weapon'),
             ('RightHand', (.045, -.14, -.28), 'Weapon'),
             ('LeftHand', (-.045, -.15, -.30) if kind == 'pistol' else (-.06, -.065, -.57), 'Weapon')]
    for name, position, parent in heads:
        bone = rig.data.edit_bones.new(name)
        bone.head = bpos(position)
        bone.tail = bone.head + Vector((0, 0, .08))
        if parent:
            bone.parent = rig.data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.animation_data_create()
    return rig


def animate(kind, name, duration):
    action = bpy.data.actions.new(kind + '_' + name)
    action.use_fake_user = True
    RIG.animation_data.action = action
    end = round(duration * 24)
    for frame in range(end + 1):
        t = frame / end
        for bone in RIG.pose.bones:
            bone.location = Vector()
            bone.rotation_mode = 'QUATERNION'
            bone.rotation_quaternion = Quaternion()
        weapon, slide, mag, left = [RIG.pose.bones[n] for n in ['Weapon', 'Slide', 'Magazine', 'LeftHand']]
        if name == 'Idle':
            weapon.location = bpos((0, math.sin(t * math.tau) * .0015, 0))
        elif name == 'Draw':
            amount = (1 - t) ** 2
            weapon.location = bpos((.055 * amount, -.25 * amount, .13 * amount))
            weapon.rotation_quaternion = Quaternion((1, 0, 0), -.55 * amount)
        elif name == 'Fire':
            # Short automatic-fire clips have only 2-3 samples at 24 fps.
            # Put the peak at the midpoint so native/GLB baking retains recoil.
            kick = math.sin(t * math.pi)
            weapon.location = bpos((0, 0, .028 * kick))
            weapon.rotation_quaternion = Quaternion((1, 0, 0), (.12 if kind == 'pistol' else .055) * kick)
            slide.location = bpos((0, 0, (.043 if kind == 'pistol' else .018) * kick))
        elif name == 'Reload':
            tilt = math.sin(t * math.pi) ** .7
            weapon.rotation_quaternion = Quaternion((0, 1, 0), -.24 * tilt)
            weapon.location = bpos((.025 * tilt, -.045 * tilt, .025 * tilt))
            withdrawal = math.sin(max(0, min(1, (t - .12) / .65)) * math.pi)
            mag.location = bpos((-.045 * withdrawal if kind == 'lmg' else 0, -.24 * withdrawal, .045 * withdrawal))
            left.location = bpos((.065 * tilt, -.16 * withdrawal, .13 * tilt))
            left.rotation_quaternion = Quaternion((1, 0, 0), -.5 * withdrawal)
            if kind == 'lmg':
                slide.rotation_quaternion = Quaternion((1, 0, 0), .95 * withdrawal)
            else:
                rack = max(0, 1 - abs(t - .83) / .055)
                slide.location = bpos((0, 0, .042 * rack))
        elif name == 'Sprint':
            weapon.rotation_quaternion = Quaternion((0, 1, 0), -.22)
            weapon.location = bpos((0, math.sin(t * math.tau) * .012, 0))
        for bone in RIG.pose.bones:
            bone.location = bone.bone.matrix_local.to_3x3().inverted() @ bone.location
            bone.keyframe_insert('location', frame=frame)
            bone.keyframe_insert('rotation_quaternion', frame=frame)
    return action


def render_preview(kind):
    bpy.ops.object.camera_add(location=(1.5, -1.9, 1.15))
    camera = bpy.context.object
    target = bpos((0, -.12, -.4))
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 1.45
    bpy.context.scene.camera = camera
    for location, energy, size in [((1, -1, 2), 180, 2), ((-1, 1, 1), 130, 2)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        light = bpy.context.object
        light.data.energy = energy
        light.data.shape = 'DISK'
        light.data.size = size
        light.rotation_euler = (target - light.location).to_track_quat('-Z', 'Y').to_euler()
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.render.resolution_x = 900
    scene.render.resolution_y = 600
    scene.render.resolution_percentage = 100
    if scene.world is None:
        scene.world = bpy.data.worlds.new('Weapon Studio')
    scene.world.color = (.12, .12, .12)
    scene.render.filepath = str(SOURCE / f'{kind}_preview.png')
    bpy.ops.render.render(write_still=True)
    meta(SOURCE / f'{kind}_preview.png')
    # Check the same camera-relative poses/FOV used by the game's overlay camera.
    camera.data.type = 'PERSP'
    camera.data.lens_unit = 'FOV'
    camera.data.angle = 2 * math.atan(math.tan(math.radians(65) / 2) * 960 / 540)
    camera.data.clip_start = .03
    camera.location = Vector()
    camera.rotation_euler = Vector((0, 1, 0)).to_track_quat('-Z', 'Y').to_euler()
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540
    for pose, position in [('hip', (.18, -.15, -.16)), ('aim', (0, -.074 if kind == 'pistol' else -.105, -.10))]:
        RIG.location = bpos(position)
        scene.render.filepath = str(SOURCE / f'{kind}_{pose}.png')
        bpy.ops.render.render(write_still=True)
        meta(SOURCE / f'{kind}_{pose}.png')


manifest = {}
for kind, reload_seconds, fire_seconds, draw_seconds in [('ar', 2.1, .09, .45), ('pistol', 1.5, .16, .3), ('lmg', 3.4, .075, .6)]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 24
    for name, color, metallic, roughness in COLORS:
        material = bpy.data.materials.new(name)
        material.diffuse_color = (*color, 1)
        material.use_nodes = True
        bsdf = material.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (*color, 1)
        bsdf.inputs['Metallic'].default_value = metallic
        bsdf.inputs['Roughness'].default_value = roughness
        material_path = OUT / 'Materials' / (name + '.plutomaterial')
        material_path.parent.mkdir(parents=True, exist_ok=True)
        material_path.write_text(f'Color={color[0]},{color[1]},{color[2]},1\nSurfaceType=Standard\nAlphaMode=Opaque\nCastsShadow=false\nMetallic={metallic}\nRoughness={roughness}\nShaderGraph=engine://builtin/shadergraph/default-lit\n', encoding='utf-8')
        meta(material_path)
    RIG = create_rig(kind)
    gun_geometry(kind)
    actions = {}
    durations = {'Idle': 2., 'Draw': draw_seconds, 'Fire': fire_seconds, 'Reload': reload_seconds, 'Sprint': .8}
    for name, duration in durations.items():
        actions[name] = animate(kind, name, duration)
        clip = OUT / kind / (name + '.plutoclip')
        export_clip(clip, RIG, actions[name], duration, [(duration * phase, event) for phase, event in [(.18, 'ReloadMagOut'), (.74, 'ReloadMagIn'), (.80, 'ReloadRack'), (.86, 'ReloadFinish')]] if name == 'Reload' else [])
        meta(clip)
    mesh_path = OUT / kind / (kind + '.plutomesh')
    manifest[kind] = export_mesh(mesh_path, RIG, MATERIAL_REFS, f'project://SourceModels/Weapons/{kind}.glb')
    manifest[kind]['animations'] = durations
    meta(mesh_path)
    animation_set = OUT / kind / (kind + '.plutoanim')
    animation_set.write_text('AnimationSetVersion=1\n' + ''.join(f'Clip=project://Weapons/{kind}/{name}.plutoclip\n' for name in durations), encoding='utf-8')
    meta(animation_set)
    RIG.animation_data.action = actions['Idle']
    bpy.context.scene.frame_set(0)
    # Keep models and all actions editable; the preview camera is excluded from GLB.
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f'{kind}.blend'))
    meta(SOURCE / f'{kind}.blend')
    bpy.ops.export_scene.gltf(filepath=str(SOURCE / f'{kind}.glb'), export_format='GLB',
                              export_animations=True, export_animation_mode='ACTIONS', export_cameras=False, export_lights=False)
    meta(SOURCE / f'{kind}.glb')
    if '--no-previews' not in sys.argv:
        render_preview(kind)
(OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print('Created three original weapon rigs and fifteen skeletal animation clips.')
