"""Generate persistent throwable effects and register only this feature's resources."""
from pathlib import Path
from string import Template
import json
import uuid

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'Assets'
generated = []

def save(relative, content):
    path = ASSETS / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding='utf-8')
    generated.append(path)

for name, color in [('Flashbang', '.65,.7,.74,1'), ('SmokeGrenade', '.2,.29,.23,1'), ('Molotov', '.4,.17,.04,1')]:
    save(f'Materials/Grenades/{name}.plutomaterial', f'BaseColor={color}\nRoughness=0.65\nMetallic=0\nSurfaceType=Standard\nAlphaMode=Opaque\nCastsShadow=true\n')
    prefab = f'SCENE\t1\nENTITY\t1\t0\t1\t{name}\t0,0,0\t0,0,0\t1,1,1\n'
    parts = [('0,0,0', '.11,.2,.11', 'sphere')]
    if name == 'Molotov': parts += [('0,.23,0', '.045,.12,.045', 'sphere'), ('0,.34,0', '.09,.025,.04', 'cube')]
    else: parts += [('0,.19,0', '.07,.04,.07', 'cube')]
    for identity, (position, scale, mesh) in enumerate(parts, 2):
        prefab += f'ENTITY\t{identity}\t1\t1\t{name} part\t{position}\t0,0,0\t{scale}\n'
        prefab += f'''COMPONENT\t{identity}\tMeshComponent\t1
PROPERTY\tStatic\t4\tfalse\t0
PROPERTY\tVisible\t4\ttrue\t0
PROPERTY\tMeshAssetReference\t2\tengine://builtin/mesh/{mesh}\t0
PROPERTY\tSubmeshIndex\t1\t-1\t0
PROPERTY\tMaterialSlots.0.MaterialAsset\t2\tproject://Materials/Grenades/{name}.plutomaterial\t0
PROPERTY\tSubmeshOverrides.0.MaterialAsset\t2\tproject://Materials/Grenades/{name}.plutomaterial\t0
END_COMPONENT
'''
    save(f'Prefabs/Grenades/{name}.plutoprefab', prefab)

for name, life, speed, size, radius, color in [
    ('SmokeCloud', 3, .25, 5, 2.3, '.42,.44,.45,.95'),
    ('Fire', .8, .8, .9, 2.5, '1,.3,.02,1'),
]:
    save(f'Particles/Grenades/{name}.plutoparticles', f'''ParticleSystemVersion=2
PlayOnAwake=false
Looping=false
Duration=0.1
MaxParticles=256
StartLifetime={life}
StartSpeed={speed}
StartSize={size}
StartColor={color}
ColorOverLifetimeEnabled=true
EndColor={color.rsplit(',', 1)[0]},0
SizeOverLifetimeEnabled=true
EndSize={size * 1.2}
GravityModifier=0
EmissionRateOverTime=0
BurstCount=0
SimulationSpace=World
Shape={"Sphere" if name == "SmokeCloud" else "Box"}
ShapeRadius={radius}
ShapeSize={radius * 2},0.2,{radius * 2}
RenderShape=Quad
MaterialAsset=project://Materials/Grenades/Particle.plutomaterial
CollisionEnabled=false
TrailsEnabled=false
''')
    save(f'Prefabs/Grenades/{name}.plutoprefab', f'''SCENE\t1
ENTITY\t1\t0\t1\t{name}\t0,0,0\t0,0,0\t1,1,1
COMPONENT\t1\tParticleSystemComponent\t1
PROPERTY\tParticleSystemAsset\t2\tproject://Particles/Grenades/{name}.plutoparticles\t0
END_COMPONENT
''')

tokens = json.loads((ASSETS / 'UI/theme.tokens.json').read_text())
for name in ['inventory', 'hud']:
    source = ASSETS / f'UI/{name}.rcss.in'
    save(f'UI/{name}.rcss', Template(source.read_text()).substitute(tokens))
generated += [ASSETS / f'UI/{name}.rml' for name in ['inventory', 'hud']]
generated += [ASSETS / f'Scripts/{name}.cs' for name in [
    'EconomyRules', 'BotEquipmentRules', 'MultiplayerSession.BotEquipment',
    'MultiplayerSession.Economy', 'MultiplayerSession.Throwables',
    'GrenadeRules', 'MultiplayerSession.Grenades', 'MultiplayerSession',
    'MultiplayerSession.Authority', 'MultiplayerSession.Defusal', 'EnemySoldierBot',
    'PlayerController', 'InventoryMenu', 'UIManager', 'PlayerHud', 'WeaponCatalog',
    'MultiplayerSession.Objectives', 'DedicatedServerSettings']]
manifest = ROOT / 'CoDplutoproject.plutoproject'
lines = manifest.read_text().splitlines()
for path in generated:
    meta = Path(str(path) + '.plutometa')
    if not meta.exists():
        meta.write_text(f'PLUTOASSET\t1\nID\t{uuid.uuid5(uuid.NAMESPACE_URL, "cod/economy/" + path.relative_to(ASSETS).as_posix()).hex}\nIMPORTER_VERSION\t1\n')
    reference = 'project://' + path.relative_to(ASSETS).as_posix()
    kind = {'.cs': 'Script', '.plutoprefab': 'Prefab', '.plutoparticles': 'Particle System', '.plutomaterial': 'Material'}.get(path.suffix, 'Unknown')
    prefix = 'ASSET\t' + reference + '\t'
    previous = next((i for i, line in enumerate(lines) if line.startswith(prefix)), None)
    if previous is not None:
        kind = lines[previous].split('\t')[-1]
        lines[previous] = f'{prefix}{path.stat().st_size}\t{kind}'
    else:
        lines.append(f'{prefix}{path.stat().st_size}\t{kind}')
    if path.suffix == '.plutoprefab':
        include = 'BUILD_ALWAYS_INCLUDE\t' + reference
        if include not in lines: lines.append(include)
manifest.write_text('\n'.join(lines) + '\n')
print(f'Registered {len(generated)} economy and throwable resources.')
