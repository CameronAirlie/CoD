"""Run in Soldier_Bot_Animations.blend through Blender MCP.

Creates a game mesh beside the preserved review mesh, keeping its rig and actions.
The native exporter retains UV/material seams and normalizes the four largest
skin influences. Source files and the original full-detail native mesh are kept.
"""
import bpy, json, sys
from pathlib import Path
from mathutils import Matrix

ROOT=Path(r'S:/PlutoGE/CoD')
sys.path.insert(0,str(ROOT/'Tools'))
from export_bot_assets import export_weighted_mesh
source=bpy.data.objects['Soldier_Body_Review']
rig=bpy.data.objects['Soldier_Rig']
if 'Soldier_Body_Game' in bpy.data.objects:
    raise RuntimeError('Game mesh exists; review before regenerating.')
if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
game=source.copy(); game.data=source.data.copy(); game.name='Soldier_Body_Game'
source.users_collection[0].objects.link(game)
for obj in bpy.context.selected_objects: obj.select_set(False)
game.hide_set(False); game.hide_render=False; game.hide_viewport=False
game.select_set(True); bpy.context.view_layer.objects.active=game
region=game.vertex_groups.new(name='_Bot_Decimation_Region')
protected={group.index for group in game.vertex_groups if group.name=='Head' or
           group.name.startswith(('Hand.','Thumb','Index','Middle','Ring','Little'))}
for vertex in game.data.vertices:
    influence=sum(weight.weight for weight in vertex.groups if weight.group in protected)
    region.add([vertex.index],max(0.,1.-influence),'REPLACE')
modifier=game.modifiers.new('Game detail reduction','DECIMATE')
modifier.decimate_type='COLLAPSE'; modifier.ratio=.7
modifier.vertex_group=region.name; modifier.vertex_group_factor=1.
modifier.use_collapse_triangulate=True
while game.modifiers.find(modifier.name)>0: bpy.ops.object.modifier_move_up(modifier=modifier.name)
bpy.ops.object.modifier_apply(modifier=modifier.name)
source.hide_set(True); source.hide_render=True
action=rig.animation_data.action
rig.animation_data.action=None
for bone in rig.pose.bones: bone.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
out=ROOT/'Assets/Bots/Soldier'
stats=export_weighted_mesh(out/'Soldier_Game.plutomesh',[game],rig,
    [f'project://SourceModels/Solider/M_Solider_{i}.plutomaterial' for i in range(46)],
    'project://SourceModels/Solider/Rigged/Soldier_Bot_Game.blend')
rig.animation_data.action=action; bpy.context.scene.frame_set(0)
manifest=json.loads((out/'manifest.json').read_text()); manifest['gameMesh']=stats
manifest['gameMesh']['decimationRatio']=.7
(out/'manifest.json').write_text(json.dumps(manifest,indent=2))
game.select_set(False); rig.select_set(True); bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Assets/SourceModels/Solider/Rigged/Soldier_Bot_Game.blend'))
print(json.dumps(stats))
