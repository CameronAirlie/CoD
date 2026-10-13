"""Build the original Shipment-inspired yard. Run from any directory; deterministic assets."""
from pathlib import Path
import math, random, re, struct, uuid
from collections import defaultdict
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[1]; A=ROOT/'Assets'; generated=[]
def save(rel,data):
 p=A/rel;p.parent.mkdir(parents=True,exist_ok=True)
 p.write_bytes(data if isinstance(data,bytes) else data.encode())
 Path(str(p)+'.plutometa').write_text('PLUTOASSET\t1\nID\t'+uuid.uuid5(uuid.NAMESPACE_URL,'cod/shipment/'+rel).hex+'\nIMPORTER_VERSION\t1\n');generated.append(p)
def comp(i,n,ps):
 return f'COMPONENT\t{i}\t{n}\t1\n'+''.join(f'PROPERTY\t{k}\t{t}\t{v}\t0\n' for k,t,v in ps)+'END_COMPONENT\n'
def vec(v):return ','.join(f'{x:.5f}' for x in v)
# Painted steel with speckled wear, rust streaks and exposed edges. Original procedural art.
colors={'Blue':(38,80,104),'Red':(124,43,31),'Green':(55,77,55),'Cream':(165,157,128),'Steel':(74,79,77),'Asphalt':(61,61,56),'Wood':(106,82,48),'Yellow':(183,137,34),'Rubber':(26,29,28)}
for name,col in colors.items():
 rng=random.Random(name); im=Image.new('RGB',(512,512));px=im.load()
 for y in range(512):
  for x in range(512):
   n=rng.gauss(0,5 if name=='Asphalt' else 3);shade=1-.15*math.exp(-min(y,511-y)/16)
   px[x,y]=tuple(max(0,min(255,int(c*shade+n))) for c in col)
 d=ImageDraw.Draw(im)
 if name not in ('Asphalt','Rubber'):
  for _ in range(180):
   x=rng.randrange(512);y=rng.randrange(512);w=rng.randrange(1,7);h=rng.randrange(2,35)
   d.line((x,y,x+rng.randrange(-2,3),min(511,y+h)),fill=(95,62,35),width=w)
  d.line((0,2,511,2),fill=(155,137,103),width=3)
  if name=='Wood':
   for x in range(0,512,64):d.line((x,0,x,511),fill=(48,36,22),width=3)
 import io
 buf=io.BytesIO();im.save(buf,format='PNG');save(f'Textures/Shipment/{name}.png',buf.getvalue())
 save(f'Materials/Shipment/{name}.plutomaterial',f'Color=1,1,1,1\nSurfaceType=Standard\nAlphaMode=Opaque\nCastsShadow=true\nMetallic={0 if name in ("Asphalt","Wood","Rubber") else .55}\nRoughness=.86\nAlbedoTexture=project://Textures/Shipment/{name}.png\nShaderGraph=engine://builtin/shadergraph/default-lit\n')
# Geometry is merged by material in each yard section; detail ribs do not add draw calls/colliders.
source=(A/'Scenes/Main.plutoscene').read_text();blocks=re.split(r'(?=^ENTITY\t)',source,flags=re.M)[1:]
byid={int(b.split('\t')[1]):b for b in blocks}
keep={1,5,6,707,1784}
parents={int(b.split('\t')[1]):int(b.split('\t')[2]) for b in blocks}
def retained(i):return i in keep or (parents[i]!=0 and retained(parents[i]))
scene='SCENE\t1\n'+''.join(b for i,b in byid.items() if retained(i))
scene=re.sub(r'(^ENTITY\t1\t0\t1\tPlayer\t)[^\t]+\t[^\t]+',r'\g<1>-15,1.5,15\t0,135,0',scene,flags=re.M)
scene=scene.replace('Hardpoint Courtyard|Hardpoint West|Hardpoint East','Hardpoint Center|Hardpoint Gate|Hardpoint Blue|Hardpoint Forklift|Hardpoint Red')
for key,val in [('hardpointRadius','3.2'),('botFallbackSpawnMinimumDistance','8'),('botFallbackSpawnRadius','19')]:scene=re.sub(r'(^PROPERTY\t'+key+r'\t0\t)[^\t]+',r'\g<1>'+val,scene,flags=re.M)
scene=scene.replace('-50.000000,-10.000000,-50.000000','-19,-1,-19').replace('100.000000,20.000000,100.000000','19,8,19').replace('PROPERTY\tCell Size\t0\t1.000000','PROPERTY\tCell Size\t0\t0.4')
# The session serializes the navigation component handle (707 + 6536).
assert 'PROPERTY\tnavigationMesh\t9\t7243' in scene
nextid=80000; meshes=defaultdict(lambda:defaultdict(list)); colliders=[]
def entity(name,pos=(0,0,0),rot=(0,0,0),scale=(1,1,1)):
 global nextid,scene
 i=nextid;nextid+=1;scene+=f'ENTITY\t{i}\t0\t1\t{name}\t{vec(pos)}\t{vec(rot)}\t{vec(scale)}\n';return i
def box(group,mat,pos,size,yaw=0,solid=False):
 if not (group=="Fence" and size[1]==3): meshes[group][mat].append((pos,size,yaw))
 if solid:
  i=entity(group+' collision',pos,(0,yaw,0),size)
  global scene
  scene+=comp(i,'ColliderComponent',[('Shape',6,'0\t5\tBox\tSphere\tCapsule\tTerrain\tMesh'),('Size',3,'1,1,1'),('Is Trigger',4,'false'),('Blocks Audio',4,'true')]);colliders.append((pos,size,yaw))
def container(name,x,z,yaw,mat,openends=False,base=0):
 a=math.radians(yaw)
 def part(m,p,s,solid=False):
  px,py,pz=p;box(name,m,(x+px*math.cos(a)+pz*math.sin(a),base+py,z-px*math.sin(a)+pz*math.cos(a)),s,yaw,solid)
 # 20 foot ISO proportions, cross lane corridors between four paired blocks.
 part(mat,(0,.10,0),(2.44,.20,6.06),True);part(mat,(0,2.52,0),(2.44,.16,6.06),True)
 for side in (-1,1):
  part(mat,(side*1.18,1.31,0),(.08,2.42,6.06),True)
  for n in range(24):part(mat,(side*1.235,1.31,-2.9+n*.25),(.055,2.20,.10))
  for zz in (-2.98,2.98):part('Steel',(side*1.17,1.30,zz),(.16,2.60,.16))
  for yy in (.18,2.43):part('Steel',(side*1.20,yy,0),(.12,.12,6.06))
 for end in (-1,1):
  if not openends:
   part(mat,(0,1.30,end*2.99),(2.3,2.4,.08),True)
   for xx in (-.58,.58):
    for dx in (-.38,.38):part('Steel',(xx+dx,1.3,end*3.05),(.045,2.22,.05))
    part('Steel',(xx,1.0,end*3.10),(.48,.05,.08))
  else:
   # Doors held flat against the outside sidewalls, passage remains clear.
   for side in (-1,1):part(mat,(side*1.29,1.3,end*2.38),(.09,2.30,1.17))
 for n in range(24):part(mat,(0,2.62,-2.9+n*.25),(2.30,.035,.10))
 # Pale shipping stencil bars on outer walls, small serial plate.
 for side in (-1,1):
  part('Cream',(side*1.27,1.78,-.65),(.018,.25,1.15))
  for n in range(7):part('Steel',(side*1.285,1.78,-1.10+n*.15),(.012,.17,.055))
  part('Cream',(side*1.28,.55,2.4),(.02,.30,.38))
box('Yard','Asphalt',(0,-.2,0),(44,.4,44),solid=True)
for x,z,yaw,mat in [(-5,-5,0,'Green'),(-7.6,-5,0,'Blue'),(5,-5,90,'Red'),(5,-7.6,90,'Cream'),(-5,5,90,'Red'),(-5,7.6,90,'Green'),(5,5,0,'Blue'),(7.6,5,0,'Cream')]:container('Center '+mat+f' {x} {z}',x,z,yaw,mat)
# Angled outer pass-through containers break perimeter sightlines.
container('West passage',-14,0,-8,'Blue',True);container('East passage',14,0,-8,'Red',True)
container('North cargo',0,-14,90,'Green');container('South cargo',0,14,90,'Cream')
container('North stack',0,-14,90,'Blue',base=2.60);container('South stack',0,14,90,'Red',base=2.60)
# Yard fence: continuous collision boundary and modeled diamond wire sections.
for side in (-1,1):
 box('Fence','Steel',(side*20,1.5,0),(.12,3,40),solid=True)
 box('Fence','Steel',(0,1.5,side*20),(40,3,.12),solid=True)
 for n in range(-10,11):
  q=n*2
  for p in [(q,1.6,side*20),(side*20,1.6,q)]:box('Fence','Steel',p,(.08,3.2,.08))
 for h in (.2,2.9,3.2):
  box('Fence','Steel',(0,h,side*20),(40,.045,.045));box('Fence','Steel',(side*20,h,0),(.045,.045,40))
# Crisscross wire uses thin diagonal beams, leaving an airy fence visually.
# Pitch is 0.5m; mesh faces carry the wire. Collision already uses thin fence slabs.
for axis in (0,2):
 for side in (-1,1):
  for n in range(80):
   for sign in (-1,1):
    # diagonal in fence plane, represented by tilted world vertices in export below
    meshes['Fence']['Steel'].append(((n*.5-19.75,1.55,side*20),(.018,3.0,.018),('wire',axis,sign)))
# Concrete footings and forklift corner cover.
for x,z in [(-16,-15),(16,15),(-16,15),(16,-15)]:
 box('Cover','Cream',(x,.45,z),(2.6,.9,.65),solid=True)
 for j in range(3):
  box('Cover','Wood',(x+(j-1)*.8,.12,z+1),( .72,.24,1.1))
for x,z in [(-11,-12),(11,12),(-12,11),(12,-11)]:
 box('Crates','Wood',(x,.65,z),(1.2,1.3,1.2),solid=True)
 for h in (.12,1.18):box('Crates','Steel',(x,h,z),(1.24,.07,1.24))
# Forklift with mast, overhead guard, forks, engine and tires.
x,z=15,12
box('Forklift','Yellow',(x,.65,z),(1.25,.9,2.1),solid=True)
box('Forklift','Rubber',(x,1.30,z+.25),(.65,.35,.7))
for sx in (-.65,.65):
 for zz in (-.7,.7):box('Forklift','Rubber',(x+sx,.36,z+zz),(.25,.65,.65))
 for zz in (-.65,.65):box('Forklift','Steel',(x+sx,1.75,z+zz),(.06,1.5,.06))
 box('Forklift','Steel',(x+sx,1.55,z-1.15),(.12,2.9,.12))
 box('Forklift','Steel',(x+sx,.15,z-1.9),(.12,.12,1.6))
box('Forklift','Yellow',(x,2.5,z),(1.45,.1,1.65))
# Industrial backdrop: stacked cargo, warehouse walls and cranes outside playable fence.
for n in range(7):
 box('Backdrop','Steel',(n*9-27,5,-32),(8,10,8))
 box('Backdrop','Cream',(-32,3,n*8-24),(7,6,7))
for x in (-26,27):
 box('Crane','Yellow',(x,14,-30),(1.2,28,1.2));box('Crane','Yellow',(x,27,-10),(1,1,42))
 box('Crane','Steel',(x,21,10),(.06,12,.06))
# Painted lane markings and storm drains.
for side in (-1,1):
 for q in range(-17,18,4):
  box('Markings','Yellow',(q,.012,side*17),(1.8,.02,.08));box('Markings','Yellow',(side*17,.012,q),(.08,.02,1.8))
for x,z in [(-10,0),(10,0),(0,-10),(0,10)]:
 box('Markings','Steel',(x,.015,z),(1.1,.025,.45))
 for j in range(8):box('Markings','Rubber',(x-.45+j*.13,.03,z),(.05,.01,.36))
# Spawn names used directly by MultiplayerSession.TryBotSpawnPosition.
for name,pos in [('North West',(-16,1,-16)),('North East',(16,1,-16)),('South East',(16,1,16)),('South West',(-16,1,16))]:entity('Spawn '+name,pos)
for n,(name,pos) in enumerate([('Center',(0,.2,0)),('Gate',(-12,.2,-12)),('Blue',(-13,.2,10)),('Forklift',(12,.2,12)),('Red',(13,.2,-10))]):
 i=entity('Hardpoint '+name,pos)
 marker=byid[50101].replace('50101',str(i+1)).replace('50100',str(i))
 marker=marker.replace('PROPERTY\tsiteIndex\t1\t0','PROPERTY\tsiteIndex\t1\t'+str(n))
 scene+=marker;nextid+=1
# Native LPGM v5, static vertices (88-byte layout), per-material submeshes.
def export(group,materials):
 verts=[];inds=[];subs=[];refs=[]
 for mat,parts in materials.items():
  start=len(inds);mi=len(refs);refs.append('project://Materials/Shipment/'+mat+'.plutomaterial')
  for pos,size,yaw in parts:
   wire=isinstance(yaw,tuple)
   angle=0 if wire else math.radians(yaw);c=math.cos(angle);s=math.sin(angle)
   def transform(p):
    xx,yy,zz=p
    if wire:
     _,axis,sign=yaw; xx,yy,zz=xx+sign*yy*.5,yy,zz
     if axis==2:xx,zz=zz,xx
     pp=pos if axis==0 else (pos[2],pos[1],pos[0])
    else:xx,zz=xx*c+zz*s,-xx*s+zz*c;pp=pos
    return (xx+pp[0],yy+pp[1],zz+pp[2])
   for normal,u,v in [((1,0,0),(0,1,0),(0,0,1)),((-1,0,0),(0,0,1),(0,1,0)),((0,1,0),(0,0,1),(1,0,0)),((0,-1,0),(1,0,0),(0,0,1)),((0,0,1),(1,0,0),(0,1,0)),((0,0,-1),(0,1,0),(1,0,0))]:
    base=len(verts);nn=(normal[0]*c+normal[2]*s,normal[1],-normal[0]*s+normal[2]*c)
    for aa,bb in [(-1,-1),(1,-1),(1,1),(-1,1)]:
     p=transform(tuple((normal[k]+aa*u[k]+bb*v[k])*size[k]/2 for k in range(3)))
     verts.append((*p,*nn,(aa+1)/2,(bb+1)/2,1.,0.,0.,1.,0.,0.,-1,-1,-1,-1,0.,0.,0.,0.))
    inds.extend(base+j for j in (0,1,2,0,2,3))
  subs.append((start,len(inds)-start,mi))
 data=bytearray()
 def pod(fmt,*vs):data.extend(struct.pack('<'+fmt,*vs))
 def string(s):b=s.encode();pod('Q',len(b));data.extend(b)
 pod('IIQ',0x4d47504c,5,len(verts))
 for v in verts:pod('14f4i4f',*v)
 pod('Q',len(inds));pod(str(len(inds))+'I',*inds);pod('Q',len(subs))
 for start,count,mi in subs:
  points=[verts[j][:3] for j in inds[start:start+count]];center=[sum(p[k] for p in points)/len(points) for k in range(3)];radius=max(math.dist(center,p) for p in points)
  pod('IIIi4f',start,count,mi,-1,*center,radius);string(group+' '+str(mi));pod('Q',0)
 pod('BQQQ',0,0,0,0);pod('Q',len(refs))
 for r in refs:string(r)
 string('');string('');pod('QBBB',0,0,0,0)
 rel='Meshes/Shipment/'+re.sub('[^a-zA-Z0-9_-]','_',group)+'.plutomesh';save(rel,bytes(data))
 i=entity(group)
 global scene
 scene+=comp(i,'MeshComponent',[('Static',4,'true'),('Visible',4,'true'),('MeshAssetReference',2,'project://'+rel),('SubmeshIndex',1,-1),('UseGeneratedLods',4,'false')])
for group,materials in meshes.items():export(group,materials)
save('Scenes/Shipment.plutoscene',scene)
manifest=ROOT/'CoDplutoproject.plutoproject';lines=manifest.read_text().splitlines()
for p in generated:
 ref='project://'+p.relative_to(A).as_posix();lines=[l for l in lines if not l.startswith('ASSET\t'+ref+'\t')]
 lines.append(f'ASSET\t{ref}\t{p.stat().st_size}\t'+('Scene' if p.suffix=='.plutoscene' else 'Mesh' if p.suffix=='.plutomesh' else 'Material' if p.suffix=='.plutomaterial' else 'Texture'))
include='BUILD_ALWAYS_INCLUDE\tproject://Scenes/Shipment.plutoscene'
if include not in lines:lines.insert(34,include)
manifest.write_text('\n'.join(lines)+'\n')
print(f'Created Shipment: {len(meshes)} merged meshes, {len(colliders)} colliders, 5 hardpoints, 4 spawns; {len(generated)} registered assets.')
