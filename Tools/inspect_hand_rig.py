"""Run with Blender --background --python Tools/inspect_hand_rig.py -- <project-root>."""
import bpy
import json
import sys
from pathlib import Path

root = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root / 'Assets/SourceModels/animation_low-poly_val/animation_low-poly_val.glb'))
report = {
    'armatures': {obj.name: [bone.name for bone in obj.data.bones]
                  for obj in bpy.data.objects if obj.type == 'ARMATURE'},
    'meshes': {obj.name: {'mesh_name': obj.data.name, 'vertices': len(obj.data.vertices),
                         'vertex_groups': [group.name for group in obj.vertex_groups]}
               for obj in bpy.data.objects if obj.type == 'MESH'},
    'actions': {action.name: list(action.frame_range) for action in bpy.data.actions},
}
destination = root / 'Tools/hand-rig-report.json'
destination.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(f'Inspected {len(report["armatures"])} armatures, {len(report["meshes"])} meshes, {len(report["actions"])} actions: {destination}')
if '--create-template' in sys.argv:
    # Preserve the original skeleton, skin weights and actions for compatibility.
    # Keep sleeves, gloves and skin; remove the VAL geometry and environment.
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.data.name.startswith(('Gorka_', 'Cloth gloves', 'Arms_')):
            bpy.data.objects.remove(obj, do_unlink=True)
    for action in bpy.data.actions:
        action.use_fake_user = True
    output = root / 'Assets/SourceModels/FirstPersonHands'
    output.mkdir(parents=True, exist_ok=True)
    retained = [obj for obj in bpy.data.objects if obj.type == 'MESH']
    assert len(retained) >= 5 and all(len(obj.vertex_groups) > 0 for obj in retained)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'SharedHands.blend'))
