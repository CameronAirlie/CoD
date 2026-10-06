"""Run in the soldier review Blender file. Creates editable, in-place rifle actions.

Exports native skinning/clip assets without changing the reviewed rest rig or weights.
The review file is preserved; the animated working file is saved separately.
"""
import bpy, math, json, sys
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion
ROOT = Path(r'S:/PlutoGE/CoD')
sys.path.insert(0,str(ROOT/'Tools'))
from export_bot_assets import export_weighted_mesh, export_clip, engine_matrix
rig = bpy.data.objects['Soldier_Rig']
body = bpy.data.objects['Soldier_Body_Review']
out = ROOT/'Assets/Bots/Soldier'
out.mkdir(parents=True,exist_ok=True)
durations = {'Rifle_Idle':2.,'Rifle_Walk':1.2,'Rifle_Jog':.8,'Rifle_Sprint':.6,'Rifle_Aim':2.,'Rifle_Fire':.25,'Rifle_Hit':.5,'Rifle_Reload':2.}
if any(name in bpy.data.actions for name in durations):
    raise RuntimeError('Generated actions already exist; review them before regenerating.')
rig.animation_data_create()
bpy.context.scene.render.fps = 24
rest = {p.name:p.bone.matrix_local.copy() for p in rig.pose.bones}

def control(name,position,forward=None):
    p = rig.pose.bones[name]
    matrix = rest[name].copy()
    if forward:
        q = matrix.to_quaternion()
        matrix = ((q@Vector((0,1,0))).rotation_difference(Vector(forward).normalized())@q).to_matrix().to_4x4()
    matrix.translation = Vector(position)
    p.matrix = matrix

actions = {}
for name,duration in durations.items():
    action = bpy.data.actions.new(name); action.use_fake_user=True
    rig.animation_data.action=action
    actions[name]=action
    steps = round(duration*24)
    for frame in range(steps+1):
        key_frame=frame*duration*24/steps
        bpy.context.scene.frame_set(int(key_frame),subframe=key_frame%1)
        for p in rig.pose.bones:
            p.rotation_mode='QUATERNION'; p.matrix_basis=Matrix.Identity(4)
        t=frame/steps; phase=t*2*math.pi
        stride={'Rifle_Walk':.20,'Rifle_Jog':.28,'Rifle_Sprint':.34}.get(name,0.)
        bob=(.018 if stride else .003)*(1-math.cos(phase*2))
        rig.pose.bones['Hips'].location=Vector((0,0,bob))
        lean=.10 if name=='Rifle_Sprint' else .035 if stride else 0
        hit=math.sin(math.pi*t)*math.exp(-3*t) if name=='Rifle_Hit' else 0
        rig.pose.bones['Chest'].rotation_quaternion=Quaternion((1,0,0),lean-.24*hit)
        rig.pose.bones['Spine'].rotation_quaternion=Quaternion((0,0,1),(.025*math.sin(phase) if stride else .006*math.sin(phase)))
        bpy.context.view_layer.update()
        for side,sign in [('L',1),('R',-1)]:
            foot=rest['CTRL_Foot.'+side].translation.copy()
            swing=math.sin(phase+(0 if side=='L' else math.pi))
            foot.y+=stride*swing
            foot.z+=(.075 if name=='Rifle_Sprint' else .05)*max(0,math.cos(phase+(0 if side=='L' else math.pi))) if stride else 0
            control('CTRL_Foot.'+side,foot)
        recoil=.035*math.sin(math.pi*min(1,t*3))*math.exp(-5*t) if name=='Rifle_Fire' else 0
        right=Vector((-.13,-.24+recoil,1.35+bob))
        left=Vector((-.07,-.46+recoil,1.39+bob))
        if name=='Rifle_Idle': right.z-=.045; left.z-=.045
        if name=='Rifle_Sprint': right.z-=.10; left.z-=.10
        if name=='Rifle_Reload':
            reach=math.sin(math.pi*t)**2
            left=left.lerp(Vector((-.10,-.20,1.05)),reach)
        control('CTRL_Hand.R',right,(0,-1,-.1))
        control('CTRL_Hand.L',left,(0,-1,-.1))
        bpy.context.view_layer.update()
        for p in rig.pose.bones:
            p.keyframe_insert('location',frame=key_frame,group=p.name)
            p.keyframe_insert('rotation_quaternion',frame=key_frame,group=p.name)
            p.keyframe_insert('scale',frame=key_frame,group=p.name)
    export_clip(out/(name+'.plutoclip'),rig,action,duration,[(.4,'ReloadMagOut'),(1.35,'ReloadMagIn'),(1.7,'ReloadFinish')] if name=='Rifle_Reload' else [])

# Native mesh must contain the bind pose, never evaluated action geometry.
rig.animation_data.action=None
for p in rig.pose.bones: p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
manifest = export_weighted_mesh(out/'Soldier.plutomesh',[body],rig,[f'project://SourceModels/Solider/M_Solider_{i}.plutomaterial' for i in range(46)],'project://SourceModels/Solider/Rigged/Soldier_Bot_Animations.blend')
rig.animation_data.action=actions['Rifle_Aim']; bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
hand=rig.matrix_world@rig.pose.bones['Hand.R'].matrix
support=rig.matrix_world@rig.pose.bones['Hand.L'].matrix
relative=engine_matrix(hand.inverted()@support)
manifest['supportPosition']=list(relative.translation)
manifest['supportRotationQuaternion']=list(relative.to_quaternion()) # w,x,y,z

# Calibrate imported M16 in metres, then store geometry in firing-hand space.
gun_objects=[o for o in bpy.data.objects if o.type=='MESH' and o not in (body,bpy.data.objects.get('Cube'))]
grip=bpy.data.objects['PISTOL GRIP_M16A3_0']
grip_centre=sum((grip.matrix_world@Vector(c) for c in grip.bound_box),Vector())/8
gun_transform=Matrix.Translation(hand.translation)@Matrix.Rotation(math.pi,4,'Z')@Matrix.Scale(.0012,4)@Matrix.Translation(-grip_centre)
old={o:o.matrix_world.copy() for o in gun_objects}
materials=[]
for o in gun_objects:
    for m in o.data.materials:
        if m not in materials: materials.append(m)
    mapping=[materials.index(m) for m in o.data.materials]
    for polygon in o.data.polygons: polygon.material_index=mapping[polygon.material_index]
    o.data.materials.clear()
    for m in materials: o.data.materials.append(m)
    o.parent=None; o.matrix_world=hand.inverted()@gun_transform@old[o]
manifest['weapon']=export_weighted_mesh(out/'M16_HandSpace.plutomesh',gun_objects,None,[f'project://SourceModels/m16_assault_rifle/M_m16_assault_rifle_{i}.plutomaterial' for i in range(len(materials))],'project://SourceModels/m16_assault_rifle/m16_assault_rifle.glb')
for o in gun_objects:
    local=o.matrix_world.copy()
    o.parent=rig; o.parent_type='BONE'; o.parent_bone='Hand.R'
    o.matrix_world=hand@local
manifest['animations']=durations
(out/'manifest.json').write_text(json.dumps(manifest,indent=2))
(out/'Soldier.plutoanim').write_text('AnimationSetVersion=1\n'+''.join(f'Clip=project://Bots/Soldier/{name}.plutoclip\n' for name in durations))
rig.animation_data.action=actions['Rifle_Idle']; bpy.context.scene.frame_set(0)
bpy.context.scene.frame_start=0; bpy.context.scene.frame_end=48
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Assets/SourceModels/Solider/Rigged/Soldier_Bot_Animations.blend'))
print(json.dumps(manifest))
