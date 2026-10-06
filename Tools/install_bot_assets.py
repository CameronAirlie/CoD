"""Register generated bot assets and replace only the two prefabs' visual rigs."""
import json, re, uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
ASSETS=ROOT/'Assets'
BASE='project://Bots/Soldier/'

def component(entity,kind,properties):
    return f'COMPONENT\t{entity}\t{kind}\t1\n'+''.join(f'PROPERTY\t{name}\t{type_}\t{value}\t0\n' for name,type_,value in properties)+'END_COMPONENT\n'

def entity(id,parent,name,position='0,0,0'):
    return f'ENTITY\t{id}\t{parent}\t1\t{name}\t{position}\t0,0,0\t1,1,1\n'

def mesh(id,reference,offset='0,0,0'):
    return component(id,'MeshComponent',[('Static',4,'false'),('Visible',4,'true'),('SubmeshIndex',1,-1),('MeshAssetReference',2,reference),('MeshPositionOffset',3,offset)])

graph='AnimationGraphVersion=4\nDefaultStateId=1\n'
for i,(name,type_,default) in enumerate([('MovementSpeed','Float',0),('HasTarget','Bool',0),('Shoot','Trigger',0),('Hit','Trigger',0),('Reload','Trigger',0),('SupportHandIK','Float',1)],1):
    graph+=f'Parameter={i}|{name}|{type_}|{default}|0|0\n'
for i,name in enumerate(['Idle','Walk','Jog','Sprint'],1):
    graph+=f'State={i}|Rifle {name}|Rifle_{name}|{i-1}|{i*180}|100|1|1|{BASE}Rifle_{name}.plutoclip\n'
for i,(a,b,op,value) in enumerate([(1,2,'Greater',.05),(2,1,'Less',.05),(2,3,'Greater',2.4),(3,2,'Less',2.4),(3,4,'Greater',5.8),(4,3,'Less',5.8)],1):
    graph+=f'Transition={i}|{a}|{b}|0.14|0|0.9\nCondition={i}|MovementSpeed|{op}|{value}\n'
graph+='BoneMask=1|Rifle Upper Body|0\nBoneMaskEntry=1|1|1|1\n'
for i,(clip,activation,loop) in enumerate([('Aim','HasTarget',1),('Fire','Shoot',0),('Hit','Hit',0),('Reload','Reload',0)],1):
    graph+=f'Layer={i}|Rifle {clip}|{BASE}Rifle_{clip}.plutoclip|Rifle_{clip}|0|1|0|1||{activation}|1|0.04|0.10|{loop}|1|1|\n'
(ASSETS/'Bots/Soldier/Soldier.plutoanimgraph').write_text(graph,encoding='utf8')

for filename,root,model,body,extra,socket,target,hint,gun in [('Enemy',156,159,160,161,2537,7001,7002,7003),('RemotePlayer',1,2,3,6107,7000,7001,7002,7003)]:
    path=ASSETS/f'Prefabs/{filename}.plutoprefab'
    text=path.read_text(encoding='utf8')
    # COMPONENT records carry their own owner id, even when saved below another ENTITY.
    text=re.sub(r'COMPONENT\t\d+\tAnimationComponent\t[^\n]*\n.*?END_COMPONENT\n','',text,flags=re.S)
    text=re.sub(r'COMPONENT\t'+str(root)+r'\tIKComponent\t[^\n]*\n.*?END_COMPONENT\n','',text,flags=re.S)
    text=re.sub(r'^PROPERTY\t(?:soldierMesh|supportHandTarget)\t[^\n]*\n','',text,flags=re.M)
    blocks=re.split(r'(?=^ENTITY\t)',text,flags=re.M)
    result=[]
    for block in blocks:
        match=re.match(r'ENTITY\t(\d+)\t',block)
        if not match: result.append(block); continue
        id=int(match[1])
        if id in (extra,7001,7002,7003) or (filename=='RemotePlayer' and id==7000): continue
        if id==root:
            marker='PROPERTY\tSource\t2\tCoD.Scripts.EnemySoldierBot\t0\n'
            block=block.replace(marker,marker+f'PROPERTY\tsoldierMesh\t9\t{body}\t0\nPROPERTY\tsupportHandTarget\t9\t{target}\t0\n')
            block+=component(root,'AnimationComponent',[('SourceAnimation',2,BASE+'Soldier.plutoanim'),('AnimationGraph',2,BASE+'Soldier.plutoanimgraph'),('Playing',4,'true'),('Looping',4,'true'),('Autoplay',4,'true'),('PoseUpdateRate',0,30)])
            props=[('Mesh',9,body),('PreviewInEditor',4,'true'),('ConstraintCount',1,1)]
            props += [('Constraints.0.'+n,t,v) for n,t,v in [('Name',2,'Rifle support hand'),('Enabled',4,'true'),('RootBone',2,'UpperArm.L'),('MiddleBone',2,'LowerArm.L'),('TipBone',2,'Hand.L'),('Target',9,target),('Hint',9,hint),('Weight',0,1),('RotationWeight',0,0),('WeightParameter',2,'SupportHandIK')]]
            block+=component(root,'IKComponent',props)
        if id==body:
            game_mesh='Soldier_Game.plutomesh' if (ASSETS/'Bots/Soldier/Soldier_Game.plutomesh').exists() else 'Soldier.plutomesh'
            block=entity(body,model,'Rigged Soldier')+mesh(body,BASE+game_mesh,'0,-1,0')
        if id==socket:
            block=entity(socket,body,'M16 firing hand socket')+component(socket,'SkeletonAttachmentComponent',[('JointName',2,'Hand.R'),('TargetNodeIndex',1,-1)])
        result.append(block)
    if filename=='RemotePlayer':
        result.append(entity(socket,body,'M16 firing hand socket')+component(socket,'SkeletonAttachmentComponent',[('JointName',2,'Hand.R'),('TargetNodeIndex',1,-1)]))
    result += [entity(target,root,'Support Hand IK Target','0.07,0.39,-0.46'),entity(hint,root,'Support Elbow IK Hint','-0.45,0.3,-0.1'),entity(gun,socket,'M16')+mesh(gun,BASE+'M16_HandSpace.plutomesh')]
    path.write_text(''.join(result),encoding='utf8',newline='\r\n')

types={'.plutomesh':'Mesh','.plutoclip':'Animation Clip','.plutoanim':'Animation','.plutoanimgraph':'Animation Graph','.blend':'Model','.cs':'Script'}
paths=list((ASSETS/'Bots/Soldier').glob('*'))+list((ASSETS/'SourceModels/Solider/Rigged').glob('Soldier_Bot_*.blend'))
project=ROOT/'CoDplutoproject.plutoproject'
registry=project.read_text(encoding='utf8')
for path in paths:
    if path.suffix not in types: continue
    reference='project://'+path.relative_to(ASSETS).as_posix()
    meta=Path(str(path)+'.plutometa')
    if not meta.exists(): meta.write_text(f'PLUTOASSET\t1\nID\t{uuid.uuid5(uuid.NAMESPACE_URL,"cod-bots/"+reference).hex}\nIMPORTER_VERSION\t1\n',encoding='utf8')
    if not any(line.startswith('ASSET\t'+reference+'\t') for line in registry.splitlines()): registry+=f'ASSET\t{reference}\t0\t{types[path.suffix]}\n'
project.write_text(registry,encoding='utf8',newline='\r\n')
print('Installed rifle graph, authored IK and M16 attachments on Enemy and RemotePlayer.')
