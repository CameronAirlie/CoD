"""Generate an original Terminal-inspired Defusal airport with native LPGM geometry.
Reference: user-supplied minimap (blue = interior) and Terminal environment screenshots.
The concave plan is traced in screenshot coordinates; metric dimensions are approximated.
Run: python Tools/build_terminal.py (Pillow). Existing scenes are read, never rewritten.
"""
from pathlib import Path
from collections import defaultdict
from PIL import Image, ImageDraw, ImageFont
import math, random, struct, re, uuid, io
from fix_terminal_sign_uvs import SIGN_MATERIALS, sign_uvs
ROOT=Path(__file__).resolve().parents[1]; A=ROOT/'Assets';generated=[]
def save(rel,data):
 p=A/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data if isinstance(data,bytes) else data.encode())
 Path(str(p)+'.plutometa').write_text('PLUTOASSET\t1\nID\t'+uuid.uuid5(uuid.NAMESPACE_URL,'cod/terminal/'+rel).hex+'\nIMPORTER_VERSION\t1\n');generated.append(p)
def vec(v):return ','.join(f'{x:.5f}' for x in v)
def comp(i,n,ps):return f'COMPONENT\t{i}\t{n}\t1\n'+''.join(f'PROPERTY\t{k}\t{t}\t{v}\t0\n' for k,t,v in ps)+'END_COMPONENT\n'
source=(A/'Scenes/Foundry.plutoscene').read_text();blocks=re.split(r'(?=^ENTITY\t)',source,flags=re.M)[1:];byid={int(b.split('\t')[1]):b for b in blocks};parents={i:int(b.split('\t')[2]) for i,b in byid.items()};keep={1,5,6,707,1784}
def retained(i):return i in keep or (parents[i]!=0 and retained(parents[i]))
scene='SCENE\t1\n'+''.join(b for i,b in byid.items() if retained(i));scene=re.sub(r'(^ENTITY\t1\t0\t1\tPlayer\t)[^\t]+\t[^\t]+',r'\g<1>-54,4.02,6\t0,-90,0',scene,flags=re.M)
for key,v in [('Bounds Minimum','-65,-2,-40'),('Bounds Maximum','55,8,41'),('Cell Size','0.4')]:scene=re.sub(r'(^PROPERTY\t'+key+r'\t\d\t)[^\t]+',r'\g<1>'+v,scene,flags=re.M)
# Resolve Navmesh by name at runtime, matching Foundry's stable scene binding.
nextid=90000;meshes=defaultdict(lambda:defaultdict(list));collision_count=0
materials={
 'Tile':((174,170,155),.9,0),'White':((192,197,195),.65,.1),'Dark':((39,46,50),.7,.25),'Steel':((111,126,135),.4,.75),
 'Blue':((35,65,112),.8,.15),'Red':((147,37,31),.65,.1),'Orange':((205,87,27),.7,.15),'Wood':((120,93,57),.9,0),
 'Concrete':((121,124,119),.95,0),'Asphalt':((77,79,75),1,0),'Leaves':((49,81,39),.9,0),'Glass':((107,164,178),.15,.1),
 'Rubber':((25,29,33),.9,0),'Yellow':((217,176,46),.6,.15),'Carpet':((39,53,78),1,0),'Light':((245,242,216),.4,0)}
def material(name,im,color=(1,1,1,1),rough=.8,metal=0,glass=False,emission=False):
 buf=io.BytesIO();im.save(buf,format='PNG');save('Textures/Terminal/'+name+'.png',buf.getvalue())
 save('Materials/Terminal/'+name+'.plutomaterial',f'Color={vec(color)}\nSurfaceType=Standard\nAlphaMode={"Blend" if glass else "Opaque"}\nCastsShadow={str(not glass).lower()}\nTwoSided=true\nMetallic={metal}\nRoughness={rough}\nAlbedoTexture=project://Textures/Terminal/{name}.png\n'+('Emission=1,1,0.8\n' if emission else '')+'ShaderGraph=engine://builtin/shadergraph/default-lit\n')
for name,(color,rough,metal) in materials.items():
 rng=random.Random(name);im=Image.new('RGB',(256,256));pixels=im.load()
 for y in range(256):
  for x in range(256):
   n=rng.gauss(0,2.5 if name not in ['Concrete','Asphalt'] else 6);pixels[x,y]=tuple(max(0,min(255,int(c+n))) for c in color)
 d=ImageDraw.Draw(im)
 if name=='Tile':
  for n in range(0,256,64):d.line((n,0,n,255),fill=(95,96,92),width=1);d.line((0,n,255,n),fill=(95,96,92),width=1)
 if name=='Wood':
  for n in range(0,256,24):d.line((n,0,n,255),fill=(82,66,43),width=2)
 if name=='Carpet':
  for y in range(0,256,8):
   for x in range(y%16,256,16):d.point((x,y),fill=(152,137,90))
 material(name,im,(1,1,1,.16 if name=='Glass' else 1),rough,metal,name=='Glass',name=='Light')
# Original airport boards and storefront lettering, kept as native bitmap materials.
fontpath='C:/Windows/Fonts/arialbd.ttf'
def signmat(name,title,sub='',bg=(22,32,47),fg=(236,228,192),departures=False):
 im=Image.new('RGB',(1024,256),bg);d=ImageDraw.Draw(im)
 font=ImageFont.truetype(fontpath,65);small=ImageFont.truetype(fontpath,25)
 d.text((28,18),title,font=font,fill=fg);d.line((28,102,995,102),fill=fg,width=3)
 if departures:
  for n,(flight,dest,time) in enumerate([('KA 418','LONDON','14:25'),('KA 206','BERLIN','14:40'),('KA 810','PARIS','15:10'),('KA 072','ROME','15:25')]):
   y=112+n*30;d.text((30,y),flight+'       '+dest+'              '+time+'    BOARDING',font=small,fill=(202,220,227))
 else:d.text((30,147),sub,font=ImageFont.truetype(fontpath,40),fill=fg)
 material(name,im,rough=.7)
signmat('Airport','ZAKHAEV INTERNATIONAL','DEPARTURES / TERMINAL 2')
signmat('Departures','DEPARTURES',departures=True,bg=(16,40,89),fg=(243,183,52))
signmat('Gate','GATES  1 - 3  >','BOARDING / EXIT',bg=(220,175,32),fg=(18,24,29))
signmat('Burger','BURGER TOWN','BURGERS  /  FRIES  /  SHAKES',bg=(68,28,27),fg=(241,211,157))
signmat('Books','BOOKS & MAGAZINES','READ / DISCOVER / TRAVEL',bg=(47,55,50))
signmat('Beauty','DUTY FREE','FRAGRANCE / COSMETICS',bg=(103,48,70))
signmat('Airline','KASTOVIA AIRLINES','CHECK IN / CUSTOMER SERVICE',bg=(22,53,109))
signmat('CargoLabel','AIR FREIGHT','INTL  /  KNI 63775',bg=(174,180,180),fg=(31,37,42))
def entity(name,pos=(0,0,0),rot=(0,0,0),scale=(1,1,1)):
 global nextid,scene
 i=nextid;nextid+=1;scene+=f'ENTITY\t{i}\t0\t1\t{name}\t{vec(pos)}\t{vec(rot)}\t{vec(scale)}\n';return i
def collider(name,pos,size,rot=(0,0,0)):
 global scene,collision_count
 i=entity(name+' collision',pos,rot,size);scene+=comp(i,'ColliderComponent',[('Shape',6,'0\t5\tBox\tSphere\tCapsule\tTerrain\tMesh'),('Size',3,'1,1,1'),('Is Trigger',4,'false'),('Blocks Audio',4,'true')]);collision_count+=1
# Each primitive adds flat-shaded triangles to a spatial/material batch.
def polygon(g,m,points,uv=None):
 if uv is None:
  if m in ('Tile','Concrete','Asphalt','Wood','Carpet'):
   scale=4 if m=='Tile' else 2 if m=='Wood' else 1 if m=='Carpet' else 6
   axis=next((k for k in (1,0,2) if max(p[k] for p in points)-min(p[k] for p in points)<1e-5),1)
   axes=(0,2) if axis==1 else (2,1) if axis==0 else (0,1)
   uv=[(p[axes[0]]/scale,p[axes[1]]/scale) for p in points]
  else:uv=[(0,0),(1,0),(1,1),(0,1)][:len(points)]
 for j in range(1,len(points)-1):
  tri=[points[k] for k in (0,j,j+1)];a,b,c=tri;u=[b[k]-a[k] for k in range(3)];v=[c[k]-a[k] for k in range(3)];n=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]);length=math.sqrt(sum(x*x for x in n))
  if length<1e-8:continue
  n=tuple(x/length for x in n)
  if m in SIGN_MATERIALS and abs(n[1]) < .5:
   uv=sign_uvs(points,n)
  for k,pt in zip((0,j,j+1),tri):meshes[g][m].append((*pt,*n,*uv[k],1.,0.,0.,1.,0.,0.,-1,-1,-1,-1,0.,0.,0.,0.))
def box(g,m,pos,size,yaw=0,solid=False,render=True):
 if solid:collider(g,pos,size,(0,yaw,0))
 if not render:return
 angle=math.radians(yaw);co,si=math.cos(angle),math.sin(angle)
 for normal,u,v in [((1,0,0),(0,1,0),(0,0,1)),((-1,0,0),(0,0,1),(0,1,0)),((0,1,0),(0,0,1),(1,0,0)),((0,-1,0),(1,0,0),(0,0,1)),((0,0,1),(1,0,0),(0,1,0)),((0,0,-1),(0,1,0),(1,0,0))]:
  pts=[]
  for a,b in [(-1,-1),(1,-1),(1,1),(-1,1)]:
   x,y,z=[(normal[k]+a*u[k]+b*v[k])*size[k]/2 for k in range(3)];pts.append((pos[0]+x*co+z*si,pos[1]+y,pos[2]-x*si+z*co))
  polygon(g,m,pts)
def beam(g,m,a,b,width):
 v=[b[k]-a[k] for k in range(3)];length=math.sqrt(sum(x*x for x in v));v=[x/length for x in v];helper=(0,1,0) if abs(v[1])<.9 else (1,0,0)
 u=(v[1]*helper[2]-v[2]*helper[1],v[2]*helper[0]-v[0]*helper[2],v[0]*helper[1]-v[1]*helper[0]);ul=math.sqrt(sum(x*x for x in u));u=[x/ul*width/2 for x in u];w=[v[1]*u[2]-v[2]*u[1],v[2]*u[0]-v[0]*u[2],v[0]*u[1]-v[1]*u[0]]
 rings=[[tuple(p[k]+aa*u[k]+bb*w[k] for k in range(3)) for aa,bb in [(-1,-1),(1,-1),(1,1),(-1,1)]] for p in (a,b)]
 for j in range(4):polygon(g,m,[rings[0][j],rings[1][j],rings[1][(j+1)%4],rings[0][(j+1)%4]])
def tube(g,m,x0,x1,cy,cz,ry,rz,segments=24,caps=False):
 for j in range(segments):
  a=j*math.tau/segments;b=(j+1)*math.tau/segments
  polygon(g,m,[(x0,cy+ry*math.sin(a),cz+rz*math.cos(a)),(x1,cy+ry*math.sin(a),cz+rz*math.cos(a)),(x1,cy+ry*math.sin(b),cz+rz*math.cos(b)),(x0,cy+ry*math.sin(b),cz+rz*math.cos(b))])
 if caps:
  for x in (x0,x1):
   for j in range(segments):polygon(g,'Dark',[(x,cy,cz),(x,cy+ry*math.sin(j*math.tau/segments),cz+rz*math.cos(j*math.tau/segments)),(x,cy+ry*math.sin((j+1)*math.tau/segments),cz+rz*math.cos((j+1)*math.tau/segments))])
def wall(g,axis,fixed,start,end,base=3,height=7,mat='White'):
 box(g,mat,((start+end)/2,base+height/2,fixed) if axis=='x' else (fixed,base+height/2,(start+end)/2),(end-start,height,.35) if axis=='x' else (.35,height,end-start),solid=True)
def steps(g,start,end,width,mat='Steel'):
 a,b=start,end;horizontal=math.hypot(b[0]-a[0],b[2]-a[2]);count=math.ceil(abs(b[1]-a[1])/.18);yaw=math.degrees(math.atan2(b[0]-a[0],b[2]-a[2]))
 for n in range(count):
  t=(n+.5)/count;h=a[1]+(b[1]-a[1])*(n+1)/count
  box(g,mat,(a[0]+(b[0]-a[0])*t,h-.12,a[2]+(b[2]-a[2])*t),(width,.24,horizontal/count+.04),yaw,True)

def seats(g,x,z,count,base=3,alongx=True):
 for n in range(count):
  xx=x+n*.72 if alongx else x;zz=z if alongx else z+n*.72
  box(g,'Dark',(xx,base+.52,zz),(.62,.14,.60));box(g,'Dark',(xx,base+.92,zz+.24),(.62,.75,.12))
  for dx in (-.24,.24):box(g,'Steel',(xx+dx,base+.24,zz),(.06,.48,.06))
  box(g,'Steel',(xx-.32,base+.78,zz),(.045,.08,.7))
def planter(g,x,z,base=3,w=3,d=2):
 box(g,'Dark',(x,base+.5,z),(w,1,d),solid=True)
 for n in range(8):
  angle=n*2.4;xx=x+math.cos(angle)*w*.3;zz=z+math.sin(angle)*d*.3
  tip=(xx+math.cos(angle)*.6,base+2.1,zz+math.sin(angle)*.6)
  beam(g,'Leaves',(xx,base+1,zz),tip,.035)
  for sign in (-1,1):
   polygon(g,'Leaves',[(xx,base+1,zz),(xx+math.cos(angle+sign*.7)*.35,base+1.6,zz+math.sin(angle+sign*.7)*.35),tip,(xx+math.cos(angle-sign*.7)*.15,base+1.7,zz+math.sin(angle-sign*.7)*.15)])
# Blueprint coordinates are traced from the user's 421 x 282 minimap.
# Light blue = enclosed terminal; dark blue = exterior apron. World units are metres.
PIXEL_SCALE=.32
def world(px,py,y=3):return ((px-228)*PIXEL_SCALE,y,(py-148)*PIXEL_SCALE)
INTERIOR=[(118,41),(207,41),(207,28),(253,28),(253,30),(340,30),(340,80),(309,104),(228,104),(228,269),(149,269),(130,248),(106,238),(105,225),(80,225),(80,208),(52,208),(52,199),(34,199),(34,128),(46,128),(46,117),(73,117),(73,127),(118,127)]
OUTLINE=[(118,41),(207,41),(207,28),(253,28),(253,30),(385,30),(385,79),(394,90),(394,202),(382,219),(354,237),(228,237),(228,269),(149,269),(130,248),(106,238),(105,225),(80,225),(80,208),(52,208),(52,199),(34,199),(34,128),(46,128),(46,117),(73,117),(73,127),(118,127)]
def spans(poly,y):
 xs=[]
 for (x0,y0),(x1,y1) in zip(poly,poly[1:]+poly[:1]):
  if min(y0,y1)<=y<max(y0,y1):xs.append(x0+(y-y0)*(x1-x0)/(y1-y0))
 xs.sort();return list(zip(xs[::2],xs[1::2]))
def inside(poly,x,y):return any(a<=x<=b for a,b in spans(poly,y))
def floor_mask(poly,name,raised=False):
 # Scanline strips preserve concave/diagonal edges without pretending the airport is rectangular.
 for py in range(28,269,2):
  for a,b in spans(poly,py+1):
   split=sorted(set([a,b]+[q for q in [253] if a<q<b]))
   for x0,x1 in zip(split,split[1:]):
    wx,_,wz=world((x0+x1)/2,py+1)
    height=3 if raised else 0
    if raised and x0>=253 and py>=55:height=0
    if raised and height==0:
     # Lower lounge tile is a thin visual finish over the existing apron slab.
     box(name,'Tile',(wx,.012,wz),((x1-x0)*PIXEL_SCALE,.02,2*PIXEL_SCALE))
    else:box(name,'Tile' if raised else 'Concrete',(wx,height-.18,wz),((x1-x0)*PIXEL_SCALE,.36,2*PIXEL_SCALE),solid=True)
# Apron ground is only the traced playable footprint; terminal platforms cover its interior.
floor_mask(OUTLINE,'Blueprint ground')
floor_mask(INTERIOR,'Interior footprint',True)
# A separate non-colliding runway fills the background outside the multiplayer boundary.
box('Runway scenery','Asphalt',(20,-.5,0),(260,.2,180))

def segment_wall(name,a,b,base=3,height=7,mat='White',solid=True,width=.32):
 x0,_,z0=world(*a);x1,_,z1=world(*b);length=math.hypot(x1-x0,z1-z0)
 yaw=math.degrees(math.atan2(x1-x0,z1-z0))
 box(name,mat,((x0+x1)/2,base+height/2,(z0+z1)/2),(width,height,length),yaw,solid)
# Interior boundary: close the actual room envelope; apron-facing windows have explicit doors.
for a,b in zip(INTERIOR,INTERIOR[1:]+INTERIOR[:1]):
 if a==(309,104) and b==(228,104):continue
 if a==(228,104) and b==(228,269):continue
 if a==(340,80) and b==(309,104):
  segment_wall('Diagonal lounge sill',a,b,0,.65)
  segment_wall('Diagonal lounge glass',a,b,.65,8.8,'Glass',False,.05)
  segment_wall('Diagonal lounge cap',a,b,9.5,.2,'Steel',False)
  continue
 segment_wall('Terminal shell',a,b,0,10.8)
# Interior east window corridor follows the long blue/dark boundary in the minimap.
for py0,py1 in [(104,174),(192,224),(240,269)]:
 a,b=(228,py0),(228,py1)
 segment_wall('Hallway foundation',a,b,0,3.8)
 segment_wall('Hallway glazing',a,b,3.8,6.6,'Glass',False,.045)
 segment_wall('Hallway cornice',a,b,10.4,.25,'Steel',False)
 for py in range(py0,py1,14):segment_wall('Hallway mullions',(228,py),(228,py+.45),3.8,6.6,'Steel',False,.16)
# Front-of-lounge glass wall, two doorways to apron and maintenance stair landing.
for x0,x1 in [(228,248),(265,288),(300,309)]:
 segment_wall('Lounge front sill',(x0,104),(x1,104),0,.65)
 segment_wall('Lounge front glazing',(x0,104),(x1,104),.65,8.85,'Glass',False,.05)
for px in range(228,310,13):segment_wall('Lounge glass posts',(px,104),(px+.45,104),0,9.5,'Steel',False,.18)
# Outer apron edge is fenced rather than turning the outside into another giant room.
for a,b in zip(OUTLINE,OUTLINE[1:]+OUTLINE[:1]):
 mx,my=(a[0]+b[0])/2,(a[1]+b[1])/2
 if inside(INTERIOR,mx-.1,my-.1) or mx<228:continue
 segment_wall('Apron boundary collision',a,b,0,3.3,'Steel',True,.12)
 # Hide continuous slabs; sparse metal wire remains visible.
 meshes['Apron boundary collision'].clear()
 for h in (.3,1.5,3.2):
  pa=world(*a,h);pb=world(*b,h);beam('Apron fence rails','Steel',pa,pb,.045)
 dist=math.dist(a,b);count=max(1,math.ceil(dist/7))
 for n in range(count+1):
  t=n/count;px=a[0]+(b[0]-a[0])*t;py=a[1]+(b[1]-a[1])*t
  beam('Apron fence posts','Steel',world(px,py,0),world(px,py,3.3),.07)
# Solid strip roof and transparent skylight strips track the concave interior footprint.
for py in range(28,269,12):
 for x0,x1 in spans(INTERIOR,py+6):
  x,_,z=world((x0+x1)/2,py+6)
  box('Terminal roof','White',(x,11.1,z),((x1-x0)*PIXEL_SCALE,.16,1.3))
  box('Terminal skylights','Glass',(x,11.15,z+1.55),((x1-x0)*PIXEL_SCALE,.05,1.8))
  beam('Roof trusses','Steel',world(x0,py+6,10.5),world(x1,py+6,10.5),.16)
  for px in range(math.ceil(x0/12)*12,int(x1),12):
   if px+12>x1:continue
   beam('Roof trusses','Steel',world(px,py+6,10.2),world(px+6,py+6,11.1),.10)
   beam('Roof trusses','Steel',world(px+6,py+6,11.1),world(px+12,py+6,10.2),.10)
# Long shopping and lounge columns, black bands and mounted flight information.
def column(px,py,base=3):
 x,_,z=world(px,py);box('Terminal columns','White',(x,base+3.7,z),(1.1,7.4,1.1),solid=True)
 for h in (1.3,3.7,6.6):box('Column dark bands','Dark',(x,base+h,z),(1.15,.18,1.15))
 box('Column flight boards','Departures',(x,base+3.8,z+.58),(.82,1.85,.025))
 for dx in (-2,2):beam('Column crown','White',(x,base+6.2,z),(x+dx,10.7,z),.16)
for px,py in [(128,145),(150,198),(215,145),(215,232),(146,75),(193,75),(255,83),(282,83)]:
 column(px,py,0 if px>=253 and py>=55 else 3)
# Security occupies the west protruding wing in the user's plan.
for a,b in [((118,127),(118,145)),((118,182),(118,208))]:segment_wall('Security partition',a,b,3,6.8)
for py in (140,158,180):
 x,_,z=world(94,py)
 box('Security X ray','White',(x,3.8,z),(3.2,1.6,1.8),solid=True)
 box('Security belts','Rubber',(x-2.6,3.8,z),(2,.12,1.5))
 for dz in (-1.0,1.0):box('Security detector','Steel',(x+3.2,4.5,z+dz),(.22,3,.12),solid=True)
 box('Security detector','Steel',(x+3.2,6,z),(.22,.18,2.12))
# Airline office in the small northwest security room.
for a,b in [((72,117),(72,136)),((46,136),(62,136))]:segment_wall('Airline office',a,b,3,3.6)
x,_,z=world(60,126);box('Airline office','Wood',(x,3.65,z),(4,1.3,1.4),solid=True)
box('Airline signage','Airline',(x,6.4,z-.75),(4.6,1.1,.045))
x,_,z=world(92,128);box('Security departures sign','Airport',(x,8,z),(14,1.6,.06))
# Central concourse: a narrow circulation corridor, shops and real wall-separated rooms.
# Upper central store (large room on the plan), with west entrance and east passage.
for a,b in [((176,86),(210,86)),((176,86),(176,112)),((176,124),(176,134)),((176,134),(210,134)),((210,86),(210,111)),((210,123),(210,134))]:segment_wall('Upper shop walls',a,b,3,4.5)
x,_,z=world(193,86);box('Upper shop signage','Books',(x,7.6,z+.20),(9.8,1.1,.06))
for py in (91,130):
 for px in (183,196):
  x,_,z=world(px,py);box('Upper bookshelves','Wood',(x,4.4,z),(2.6,2.8,.6),solid=True)
  for h in (3.6,4.2,4.8,5.4):
   for n in range(13):box('Upper book spines',['Red','Blue','White','Wood'][n%4],(x-1.1+n*.18,h,z+.34),(.13,.42,.25))
# Lower bookstore block and shortcut doors face shopping and the window hallway.
for a,b in [((178,168),(209,168)),((178,211),(209,211)),((178,168),(178,183)),((178,197),(178,211)),((209,168),(209,183)),((209,197),(209,211))]:segment_wall('Bookstore walls',a,b,3,4.4)
x,_,z=world(193,168);box('Bookstore signage','Books',(x,7.5,z-.20),(9.0,1.1,.05))
for px in (183,204):
 for py in (176,204):
  x,_,z=world(px,py);box('Bookstore shelves','Wood',(x,4.25,z),(1.7,2.5,.65),solid=True)
  for h in (3.5,4.1,4.7,5.3):
   for n in range(8):box('Bookstore spines',['Blue','Red','White'][n%3],(x-.7+n*.18,h,z+.35),(.12,.38,.24))
# Southern retail alcoves follow the stepped/angled bottom of the blue footprint.
for a,b in [((178,234),(178,262)),((178,234),(199,234)),((211,234),(228,234)),((106,208),(132,208)),((132,208),(132,226))]:segment_wall('Retail walls',a,b,3,4.8)
x,_,z=world(200,253);box('Duty free counter','White',(x,3.6,z),(5,1.2,1.4),solid=True)
box('Duty free sign','Beauty',(x,7.2,z+3),(7,1.2,.05))
# Atrium seating, planter islands and a departure kiosk in their minimap positions.
for px,py,w,d in [(143,163,3.0,2.6),(148,229,2.3,2),(216,250,2.3,1.2)]:
 x,_,z=world(px,py);planter('Shopping planters',x,z,3,w,d)
for px,py,n in [(124,187,5),(152,221,4),(217,205,5)]:
 x,_,z=world(px,py);seats('Shopping seating',x,z,n,3,px<200)
x,_,z=world(156,147);box('Departure kiosk','Dark',(x,4.2,z),(1.2,2.4,.7),solid=True);box('Departure kiosk','Departures',(x,4.45,z+.36),(1.0,1.6,.02))
# Dining lies in the northern western wing, separated from the upper store by circulation.
for px in range(124,173,3):
 for py in range(48,100,3):
  if not inside(INTERIOR,px,py):continue
  x,_,z=world(px,py);box('Dining checker floor','Dark' if (px//3+py//3)%2 else 'White',(x,3.014,z),(.95,.025,.95))
for a,b in [((118,41),(143,41)),((143,41),(143,59)),((118,59),(131,59))]:segment_wall('Burger kitchen walls',a,b,3,4.8)
x,_,z=world(132,61);box('Burger counter','Dark',(x,3.65,z),(7.5,1.3,1.2),solid=True);box('Burger counter','White',(x,4.35,z),(7.7,.1,1.4))
box('Burger storefront','Burger',(x,7.5,z-.15),(8.0,1.7,.08))
for px,py in [(138,82),(160,55),(162,87)]:
 x,_,z=world(px,py);box('Food court tables','Wood',(x,3.85,z),(2.7,.15,1.25));beam('Food court legs','Steel',(x,3,z),(x,3.8,z),.13);seats('Food court chairs',x-.8,z+1.1,3)
# Upper lounge platform and the correct north/south escalator run from the lower room.
for px in (260,272):
 x,_,z=world(px,85,0);_,_,topz=world(px,54)
 steps('Lounge escalators',(x,0,z),(x,3,topz),2.8)
 for dx in (-1.48,1.48):beam('Escalator handrails','Steel',(x+dx,1,z),(x+dx,4,topz),.10)
# Lower-level office fronts enclose the underside of the upper platform.
# These are real facades rather than an open black void beneath a floating slab.
segment_wall('Lower lounge office fronts',(281,55),(338,55),0,2.8,'White')
segment_wall('Lower lounge office fronts',(253,55),(253,104),0,2.8,'White')
for px in (294,316,332):
 x,_,z=world(px,55)
 box('Lower lounge office doors','Dark',(x,1.2,z+.18),(1.35,2.4,.04))
 box('Lower lounge office doors','Steel',(x,2.45,z+.20),(1.5,.12,.08))
for py in (67,88,99):
 x,_,z=world(253,py)
 box('Lower lounge office doors','Dark',(x+.18,1.2,z),(.04,2.4,1.35))
# Stair opening rail on the upper platform, with circulation to its west.
for a,b in [((281,55),(337,55)),((253,55),(254,55))]:segment_wall('Upper lounge balustrade',a,b,3,1,'Steel',True,.10)
for px,py,w,d in [(245,75,3.2,1.6),(295,86,3.8,2.0),(323,47,2.0,1.2)]:
 x,_,z=world(px,py);base=0 if px>=253 and py>=55 else 3;planter('Lounge planters',x,z,base,w,d)
for px,py,n,base in [(306,42,5,3),(305,97,5,0),(328,66,5,0)]:
 x,_,z=world(px,py);seats('Lounge seating',x,z,n,base,px<320)
# Checkerboard approach strip and timber insets mirror the lounge screenshots.
for py in range(83,104,2):
 for px in range(262,271,2):
  x,_,z=world(px,py);box('Lounge checker path','Dark' if (px//2+py//2)%2 else 'White',(x,.014,z),(.63,.025,.63))
for px,py,w,d in [(285,89,10,5),(325,75,3,7)]:
 x,_,z=world(px,py);box('Lounge timber insets','Wood',(x,.008,z),(w,.015,d))
for px in range(232,307,7):
 x,_,z=world(px,102);box('Lounge hanging banners','Blue',(x,7.0,z),(1.0,2.3,.025))
# Arched glazing canopy and orange exterior tension braces.
for px in range(230,339,9):
 x,_,z=world(px,104);roof=9.5+1.6*math.sin((px-228)/(340-228)*math.pi)
 beam('Lounge canopy','White',(x,roof,-38),(x,roof,-14),.14)
 if px<309:
  beam('Orange facade braces','Orange',(x,1,-13.7),(x+1.4,8.9,-13.7),.14)
  beam('Orange facade braces','Orange',(x+1.4,8.9,-13.7),(x+2.8,1,-13.7),.14)
# Terminal sign is mounted over the lounge doors, facing the apron.
x,_,z=world(280,104);box('Lounge airport identity','Airport',(x,5.0,z+.12),(10,1.4,.05))
# Window corridor/jet bridge join the nose-side cabin door at terminal level.
box('Jet bridge floor','Tile',(4,2.82,10.55),(8,.36,5.75),solid=True)
wall('Jet bridge sides','x',7.7,0,8,3,3.1);wall('Jet bridge sides','x',13.4,0,4,3,3.1)
box('Jet bridge roof','White',(4,6.3,10.55),(8,.18,5.75))
# Exterior hallway stairwell matches the lower corridor door on the blueprint.
steps('Hallway apron stairs',(8,0,26.2),(0,3,26.2),3.0)
for z in (24.6,27.8):beam('Hall stairs rails','Steel',(8,1,z),(0,4,z),.09)
# Maintenance/garage is directly in front of the lower lounge; the blue stripe is continuous.
for z in (-14,-3.2):wall('Maintenance walls','x',z,7,14.2,0,3.5)
wall('Maintenance walls','z',7,-14,-3.2,0,3.5)
for a,b in [(-14,-11.8),(-5.2,-3.2)]:wall('Maintenance walls','z',14.2,a,b,0,3.5)
for n in range(11):box('Garage shutter','Steel',(14.22,.2+n*.27,-8.5),(.04,.19,6.3))
box('Garage stripe','Blue',(14.28,3.25,-8.6),(.06,.30,10.7))
box('Maintenance upper floor','Concrete',(10.6,3.6,-8.6),(7.4,.25,11.2),solid=True)
for z in (-14.1,-3.1):wall('Maintenance office','x',z,7,14.3,3.7,2.8)
wall('Maintenance office','z',14.3,-14.1,-3.1,3.7,2.8)
for z in (-11.6,-6):box('Maintenance windows','Dark',(14.49,5.2,z),(.05,1.7,2.5))
box('Maintenance roof','White',(10.6,6.65,-8.6),(7.7,.20,11.6),solid=True)
for z in (-11,-6):box('Maintenance rooftop plant','Steel',(10.4,7.15,z),(2.4,.85,2))
steps('Maintenance exterior stairs',(18,0,-2),(18,3.6,-14),2)
box('Maintenance stair landing','Concrete',(15.2,3.5,-14),(5.8,.2,2.3),solid=True)
# All aircraft primitives are translated together to the plan's apron aircraft footprint.
_original_box,_original_polygon,_original_collider,_original_beam=box,polygon,collider,beam
def plane_point(p):return (p[0]-14,p[1],p[2])
def box(g,m,pos,size,yaw=0,solid=False,render=True):
 # The original box calls polygon/collider below; translation occurs at those boundaries.
 return _original_box(g,m,pos,size,yaw,solid,render)
def polygon(g,m,points,uv=None):return _original_polygon(g,m,[plane_point(p) for p in points],uv)
def collider(g,pos,size,rot=(0,0,0)):return _original_collider(g,plane_point(pos),size,rot)
# Beam calls polygon itself; no extra positional transformation is applied here.
# Aircraft cabin, three side exits and navigable center aisle.
box('Aircraft floor','Carpet',(34,2.85,16),(32,.3,5.5),solid=True)
for side in (-1,1):
 gaps=[(20,24),(44,48)] if side==-1 else [(24,28)]
 intervals=[(18,50)]
 for lo,hi in gaps:
  intervals=[piece for a,b in intervals for piece in ([(a,min(b,lo))] if a<lo<b else [])+([(max(a,hi),b)] if a<hi<b else [])] if any(a<lo<b or a<hi<b for a,b in intervals) else intervals
 for a,b in intervals:
  if b>a:collider('Aircraft side',((a+b)/2,4.4,16+side*2.85),(b-a,2.9,.15))
# Curved fuselage and window stripe; cut away the three actual exit openings.
for x in range(18,50):
 for j in range(32):
  a=j*math.tau/32;b=(j+1)*math.tau/32;mid=(a+b)/2;side=1 if math.cos(mid)>0 else -1
  door=(side==-1 and (20<=x<24 or 44<=x<48)) or (side==1 and 24<=x<28)
  if door and -0.65<math.sin(mid)<.75 and abs(math.cos(mid))>.55:continue
  mat='White' if math.sin(mid)>.40 or math.sin(mid)<-.55 else 'Blue'
  if .28<math.sin(mid)<.45:mat='Red'
  polygon('Aircraft fuselage',mat,[(x,4.4+2*math.sin(a),16+3*math.cos(a)),(x+1,4.4+2*math.sin(a),16+3*math.cos(a)),(x+1,4.4+2*math.sin(b),16+3*math.cos(b)),(x,4.4+2*math.sin(b),16+3*math.cos(b))])
for x in range(19,50):
 for side in (-1,1):
  if (side==-1 and (20<=x<24 or 44<=x<48)) or (side==1 and 24<=x<28):continue
  box('Aircraft windows','Dark',(x+.3,5.0,16+side*2.92),(.45,.65,.04))
# Tapered nose and tail shell.
for x0,x1,r0,r1 in [(14,15,.15,.55),(15,16,.55,.8),(16,18,.8,1),(50,53,1,.65),(53,57,.65,.12)]:
 for j in range(24):
  a=j*math.tau/24;b=(j+1)*math.tau/24
  polygon('Aircraft nose tail','White',[(x0,4.4+2*r0*math.sin(a),16+3*r0*math.cos(a)),(x1,4.4+2*r1*math.sin(a),16+3*r1*math.cos(a)),(x1,4.4+2*r1*math.sin(b),16+3*r1*math.cos(b)),(x0,4.4+2*r0*math.sin(b),16+3*r0*math.cos(b))])
box('Cockpit','Dark',(16.5,5.3,16),(2,.65,2.4));wall('Cabin partitions','z',18,13.2,18.8,3,2.5)
wall('Cabin partitions','z',50,13.2,18.8,3,2.5)
for x in [29,31,38,40,42]:
 for z in (14.4,17.6):
  box('Aircraft seats','Blue',(x,3.5,z),(.7,.2,1.1),solid=True);box('Aircraft seats','Blue',(x+.35,4.05,z),(.2,1.1,1.1))
  box('Aircraft seats','Steel',(x,3.22,z),(.12,.44,.8))
for z in (14.4,17.6):box('Overhead bins','White',(34,5.8,z),(27,.45,.75))
for x in range(22,49,5):box('Cabin lights','Light',(x,6.1,16),(3,.05,.15))
# Wings and stabilizers, shaped rather than cuboid silhouettes.
for side in (-1,1):
 polygon('Aircraft wings','White',[(29,3.2,16+side*2),(36,3.2,16+side*15),(41,3.2,16+side*15),(38,3.2,16+side*2)])
 polygon('Aircraft wings','White',[(51,9.4,16+side*1),(54,9.4,16+side*8),(57,9.4,16+side*8),(55,9.4,16+side*1)])
 tube('Aircraft rear engines','White',49,54,4.5,16+side*3.6,1.0,1.0,caps=True)
 for zz in (16+side*.8,):box('Landing gear','Rubber',(35,.65,zz),(1.8,1.2,.6));beam('Landing gear','Steel',(35,1,zz),(35,2.7,zz),.2)
polygon('Tail fin','Blue',[(51,4.4,16),(52,11,16),(55,11,16),(57,4.4,16)])
polygon('Tail fin','Blue',[(57,4.4,16.12),(55,11,16.12),(52,11,16.12),(51,4.4,16.12)])
# Emergency slide is a stepped, traversable exit; rear boarding stairs provide the third route.
steps('Emergency slide',(26,0,27),(26,3,19),3,'Orange')
for x in (24.4,27.6):beam('Emergency slide','Orange',(x,.5,27),(x,3.5,19),.30)
steps('Rear boarding stairs',(46,0,3),(46,3,13),2.6)
for x in (44.6,47.4):beam('Rear stair rails','Steel',(x,1,3),(x,4,13),.08)

tube('Aircraft center intake','White',49,54,6.6,16,.75,.75,caps=True)
box,polygon,collider,beam=_original_box,_original_polygon,_original_collider,_original_beam
# Airfreight stacks and hard cover are placed from the apron objects on the supplied minimap.
for n,(px,py,levels) in enumerate([(313,137,2),(352,116,2),(376,155,1),(269,173,2),(360,180,2),(304,218,1),(239,140,1),(375,211,1),(323,108,1)]):
 x,_,z=world(px,py,0)
 for level in range(levels):
  box('Freight stack '+str(n),'Steel',(x,1.05+level*2.1,z),(2.6,2.1,2.7),solid=True)
  for dx in (-1.25,1.25):box('Freight stack '+str(n),'Dark',(x+dx,1.05+level*2.1,z),(.10,2.05,2.7))
  box('Freight stack '+str(n),'CargoLabel',(x,1.05+level*2.1,z+1.36),(2.1,1.3,.03))
  for yy in (.35,1.7):box('Freight stack '+str(n),'Red',(x,yy+level*2.1,z+1.38),(2.6,.06,.04))
# Orange/white apron barriers, cargo trolleys, police/service car beside the garage.
for x in (16.5,19.5,22.5):
 box('Apron barricades','Orange' if x==19.5 else 'White',(x,.6,-.8),(2.6,1.2,.55),solid=True)
 for dx in (-.9,.9):box('Apron barricades','Dark',(x+dx,.12,-.8),(.12,.25,.8))
for px,py in [(291,166),(368,130),(245,217)]:
 x,_,z=world(px,py,0);box('Cargo trolleys','Steel',(x,.55,z),(2.6,.25,1.3),solid=True)
 for dx in (-1,1):
  for dz in (-.65,.65):box('Cargo trolleys','Rubber',(x+dx,.30,z+dz),(.45,.5,.2))
 for n in range(3):box('Cargo trolleys',['Blue','Red','Wood'][n],(x-.75+n*.7,1,z),(.6,.7,.8))
box('Police car','White',(18,.75,-8),(2,1,4.2),solid=True);box('Police car','Dark',(18,1.4,-8),(1.85,.65,2.2))
box('Police lights','Blue',(17.6,1.85,-8),(.65,.12,.35));box('Police lights','Red',(18.4,1.85,-8),(.65,.12,.35))
for dx in (-1,1):
 for z in (-9.4,-6.6):box('Police car','Rubber',(18+dx,.4,z),(.3,.8,.8))
for z in range(-34,28,4):box('Apron taxi markings','Yellow',(48,.015,z),(.12,.025,2.1))
for x in range(5,42,4):box('Apron taxi markings','Yellow',(x,.015,27.8),(2.1,.025,.12))
# A large four-engine double-deck airliner is visible beyond the eastern boundary,
# matching the second aircraft in the supplied aerial and apron screenshots.
for x0,x1,r0,r1 in [(58,61,.1,.55),(61,66,.55,1),(66,112,1,1),(112,118,1,.65),(118,126,.65,.08)]:
 for j in range(32):
  a=j*math.tau/32;b=(j+1)*math.tau/32
  polygon('Background jumbo fuselage','White',[(x0,7+3*r0*math.sin(a),6+4.3*r0*math.cos(a)),(x1,7+3*r1*math.sin(a),6+4.3*r1*math.cos(a)),(x1,7+3*r1*math.sin(b),6+4.3*r1*math.cos(b)),(x0,7+3*r0*math.sin(b),6+4.3*r0*math.cos(b))])
for side in (-1,1):
 for x in range(65,113,2):
  box('Jumbo windows','Blue',(x,7.6,6+side*4.25),(.70,.5,.04))
  if x<85:box('Jumbo upper deck windows','Blue',(x,9.0,6+side*3.3),(.70,.45,.04))
 polygon('Jumbo wings','White',[(80,5,6+side*4),(93,5,6+side*26),(103,5,6+side*26),(99,5,6+side*4)])
 for dz,x in [(12,85),(21,91)]:tube('Jumbo engines','White',x,x+6,3.5,6+side*dz,1.5,1.5,caps=True)
 polygon('Jumbo stabilizers','White',[(113,7,6+side*1),(121,7,6+side*11),(126,7,6+side*11),(123,7,6+side*1)])
polygon('Jumbo fin','Blue',[(113,7,6),(118,18,6),(122,18,6),(126,7,6)])
# Airport buildings/control tower beyond the north edge of the playable footprint.
for x in (-55,-30,0,28,65):
 box('Airport background buildings','White',(x,5,-54),(22,10,17))
 box('Airport background buildings','Glass',(x,7.8,-45.4),(20,3.2,.05))
box('Control tower','Concrete',(49,12,-45),(6,24,6));box('Control tower','Glass',(49,25,-45),(10,5,10));box('Control tower','White',(49,27.7,-45),(12,.5,12))
# Current scene markers and both bomb sites follow the rebuilt footprint.
markers=[('Attacker Spawn',(-54,4.02,6)),('Defender Spawn',(28,1.02,-23)),('Defusal Attack West',(-26,3.1,-20)),('Defusal Attack Mid',(-3,3.1,13)),('Defusal Attack East',(8,.1,26.2)),('Defusal Defend West',(17,3.1,-34)),('Defusal Defend Mid',(25,.1,-9)),('Defusal Defend East',(32,.1,3))]
for name,pos in markers:entity(name,pos)
for old,pos,label in [(60051,(17,3.1,-34),'Upper Lounge'),(60057,(20,3.1,16),'Aircraft')]:
 i=entity('Bombsite '+('A' if old==60051 else 'B'),pos)
 marker=byid[60056 if old==60051 else 60062];oldchild=60056 if old==60051 else 60062
 marker=marker.replace('ENTITY\t'+str(oldchild)+'\t'+str(old),'ENTITY\t'+str(nextid)+'\t'+str(i));marker=marker.replace('COMPONENT\t'+str(oldchild)+'\t','COMPONENT\t'+str(nextid)+'\t').replace('Courtyard',label).replace('Foundry',label)
 scene+=marker;nextid+=1
 box('Bomb equipment','Dark',(pos[0]+2.3,pos[1]+.45,pos[2]),(1.0,.9,1.2),solid=True)
 box('Bomb equipment','Red',(pos[0]+2.3,pos[1]+.95,pos[2]),(.4,.08,.6))
for x,z,y in [(-52,4,8),(-25,8,8),(-23,-25,8),(-4,23,8),(17,-34,8),(25,-23,7),(20,16,6)]:
 i=entity('Airport fill light',(x,y,z));scene+=comp(i,'LightComponent',[('LightType',6,'0\t3\tPoint\tDirectional\tSpot'),('Color',3,'0.86,0.91,1'),('Intensity',0,12),('Range',0,20),('CastsShadows',4,'false')])
# A readable plan image makes the reference trace and revised interior boundaries reviewable.
plan=Image.new('RGB',(844,564),(239,239,235));d=ImageDraw.Draw(plan)
d.polygon([(x*2,y*2) for x,y in OUTLINE],fill=(24,36,48),outline=(7,14,22),width=3)
d.polygon([(x*2,y*2) for x,y in INTERIOR],fill=(107,147,171),outline=(223,232,234),width=2)
for name,pos in markers[:2]+[('A',(17,3,-34)),('B',(20,3,16))]:
 x=pos[0]/PIXEL_SCALE+228;y=pos[2]/PIXEL_SCALE+148
 d.ellipse((x*2-5,y*2-5,x*2+5,y*2+5),fill=(242,180,55));d.text((x*2+8,y*2-8),name,font=ImageFont.truetype(fontpath,17),fill=(255,255,255))
# Show the functional aircraft footprint and central shop rooms.
d.rounded_rectangle((230*2,188*2,366*2,208*2),radius=15,outline=(234,234,227),width=3)
for rect in [(176,86,210,134),(178,168,209,211),(178,234,228,269)]:d.rectangle(tuple(v*2 for v in rect),outline=(223,232,234),width=2)
preview_dir=ROOT/'Tools/Native/Terminal';preview_dir.mkdir(parents=True,exist_ok=True);plan.save(preview_dir/'Terminal-footprint.png')

def export(group,parts):
 verts=[];subs=[];refs=[]
 for m,points in parts.items():
  start=len(verts);verts+=points;refs.append('project://Materials/Terminal/'+m+'.plutomaterial');subs.append((start,len(points),len(refs)-1))
 data=bytearray()
 def pod(fmt,*vs):data.extend(struct.pack('<'+fmt,*vs))
 def string(s):b=s.encode();pod('Q',len(b));data.extend(b)
 pod('IIQ',0x4d47504c,5,len(verts))
 for v in verts:pod('14f4i4f',*v)
 pod('Q',len(verts));pod(str(len(verts))+'I',*range(len(verts)));pod('Q',len(subs))
 for start,count,mi in subs:
  pts=[v[:3] for v in verts[start:start+count]];center=[sum(v[k] for v in pts)/len(pts) for k in range(3)];radius=max(math.dist(center,v) for v in pts)
  pod('IIIi4f',start,count,mi,-1,*center,radius);string(group+' '+str(mi));pod('Q',0)
 pod('BQQQ',0,0,0,0);pod('Q',len(refs))
 for r in refs:string(r)
 string('');string('');pod('QBBB',0,0,0,0)
 rel='Meshes/Terminal/'+re.sub('[^a-zA-Z0-9_-]','_',group)+'.plutomesh';save(rel,bytes(data));i=entity(group)
 global scene
 scene+=comp(i,'MeshComponent',[('Static',4,'true'),('Visible',4,'true'),('MeshAssetReference',2,'project://'+rel),('SubmeshIndex',1,-1),('UseGeneratedLods',4,'false')])
for group,parts in meshes.items():
 if any(parts.values()):export(group,parts)
save('Scenes/Terminal.plutoscene',scene)
f=ROOT/'CoDplutoproject.plutoproject';lines=f.read_text().splitlines()
for p in generated:
 ref='project://'+p.relative_to(A).as_posix();entry=f'ASSET\t{ref}\t{p.stat().st_size}\t'+('Scene' if p.suffix=='.plutoscene' else 'Mesh' if p.suffix=='.plutomesh' else 'Material' if p.suffix=='.plutomaterial' else 'Texture')
 existing=next((i for i,l in enumerate(lines) if l.startswith('ASSET\t'+ref+'\t')),None)
 if existing is None:lines.append(entry)
 else:lines[existing]=entry
include='BUILD_ALWAYS_INCLUDE\tproject://Scenes/Terminal.plutoscene'
if include not in lines:lines.insert(35,include)
f.write_text('\n'.join(lines)+'\n')
print(f'Terminal: {len(meshes)} spatial mesh batches, {collision_count} colliders, two bomb sites, six bot routes, {len(generated)} assets.')
