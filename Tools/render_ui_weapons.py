"""Blender background renderer: transparent side portraits from the game's editable weapon rigs."""
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent.parent
output = ROOT / 'Assets/UI/Weapons'
output.mkdir(parents=True, exist_ok=True)
for kind in ['ar', 'pistol', 'lmg']:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / f'Assets/SourceModels/Weapons/{kind}.blend'))
    for obj in list(bpy.data.objects):
        if obj.type in ['CAMERA', 'LIGHT'] or obj.name.startswith(('RightHand', 'LeftHand')):
            bpy.data.objects.remove(obj, do_unlink=True)
        elif obj.type == 'ARMATURE':
            obj.animation_data_clear()
            obj.data.pose_position = 'REST'
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    corners = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    low = Vector(tuple(min(p[i] for p in corners) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in corners) for i in range(3)))
    target = (low + high) / 2
    bpy.ops.object.camera_add(location=target + Vector((2, -.2, .14)))
    camera = bpy.context.object
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = max(high.y - low.y, (high.z - low.z) * 2.4) * 1.15
    scene = bpy.context.scene
    scene.camera = camera
    for offset, energy in [(Vector((1, -.5, 2)), 180), (Vector((-1, .4, 1)), 120)]:
        bpy.ops.object.light_add(type='AREA', location=target + offset)
        light = bpy.context.object
        light.data.energy = energy
        light.data.size = 2
        light.rotation_euler = (target - light.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.render.film_transparent = True
    scene.render.resolution_x = 768
    scene.render.resolution_y = 320
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.filepath = str(output / f'{kind}.png')
    bpy.ops.render.render(write_still=True)
    # PlutoGE's RmlUi renderer consumes uncompressed TGA files.
    scene.render.image_settings.file_format = 'TARGA_RAW'
    bpy.data.images['Render Result'].save_render(str(output / f'{kind}.tga'), scene=scene)
