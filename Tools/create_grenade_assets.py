"""Create grenade prefabs, soft particle sprites and register their project assets."""
from pathlib import Path
import math
import struct
import uuid
import zlib

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'Assets'
generated = []


def save(relative, content):
    path = ASSETS / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content if isinstance(content, bytes) else content.encode('utf-8'))
    identity = uuid.uuid5(uuid.NAMESPACE_URL, 'cod/grenades/' + relative).hex
    Path(str(path) + '.plutometa').write_text(f'PLUTOASSET\t1\nID\t{identity}\nIMPORTER_VERSION\t1\n')
    generated.append(path)


def component(entity, name, values):
    return f'COMPONENT\t{entity}\t{name}\t1\n' + ''.join(
        f'PROPERTY\t{key}\t{kind}\t{value}\t0\n' for key, kind, value in values) + 'END_COMPONENT\n'


def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))


# Procedural radial sprite with transparent edges, shared by flash, sparks and smoke.
pixels = bytearray()
for y in range(64):
    pixels.append(0)
    for x in range(64):
        radius = math.hypot((x - 31.5) / 31.5, (y - 31.5) / 31.5)
        pixels.extend((255, 255, 255, round(max(0, 1 - radius) ** 1.8 * 255)))
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 64, 64, 8, 6, 0, 0, 0))
png += chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b'')
save('Textures/Grenades/SoftParticle.png', png)
save('Materials/Grenades/Body.plutomaterial', 'Color=0.19,0.25,0.10,1\nSurfaceType=Standard\nAlphaMode=Opaque\nCastsShadow=true\nMetallic=0.4\nRoughness=0.65\nShaderGraph=engine://builtin/shadergraph/default-lit\n')
save('Materials/Grenades/Particle.plutomaterial', 'Color=1,1,1,1\nSurfaceType=Standard\nAlphaMode=Blend\nCastsShadow=false\nAlbedoTexture=project://Textures/Grenades/SoftParticle.png\nShaderGraph=engine://builtin/shadergraph/default-unlit\n')
body = 'SCENE\t1\nENTITY\t1\t0\t1\tFrag Grenade\t0,0,0\t0,0,0\t0.16,0.21,0.16\n'
body += component(1, 'MeshComponent', [('Static', 4, 'false'), ('Visible', 4, 'true'),
    ('MeshAssetReference', 2, 'engine://builtin/mesh/sphere'), ('SubmeshIndex', 1, -1),
    ('MaterialSlots.0.MaterialAsset', 2, 'project://Materials/Grenades/Body.plutomaterial'),
    ('SubmeshOverrides.0.MaterialAsset', 2, 'project://Materials/Grenades/Body.plutomaterial')])
save('Prefabs/Grenades/Frag.plutoprefab', body)

for name, life, speed, size, end_size, gravity, color in [
    ('Flash', .22, 5, .8, 1.3, 0, '1,0.65,0.12,1'),
    ('Sparks', .8, 10, .09, .01, .8, '1,0.42,0.04,1'),
    ('Smoke', 2.4, 1.8, .9, 2.6, -.12, '0.24,0.23,0.22,0.55'),
]:
    particle = f'''ParticleSystemVersion=2
PlayOnAwake=false
Looping=false
Duration=0.1
MaxParticles=128
StartLifetime={life}
StartSpeed={speed}
StartSize={size}
StartColor={color}
ColorOverLifetimeEnabled=true
EndColor={color.rsplit(',', 1)[0]},0
SizeOverLifetimeEnabled=true
EndSize={end_size}
GravityModifier={gravity}
EmissionRateOverTime=0
BurstCount=0
SimulationSpace=World
Shape=Sphere
ShapeRadius=0.1
ShapeSize=0.2,0.2,0.2
RenderShape=Quad
MaterialAsset=project://Materials/Grenades/Particle.plutomaterial
CollisionEnabled=false
TrailsEnabled=false
'''
    save(f'Particles/Grenades/{name}.plutoparticles', particle)
    prefab = f'SCENE\t1\nENTITY\t1\t0\t1\tGrenade {name}\t0,0,0\t0,0,0\t1,1,1\n'
    prefab += component(1, 'ParticleSystemComponent', [('ParticleSystemAsset', 2, f'project://Particles/Grenades/{name}.plutoparticles')])
    save(f'Prefabs/Grenades/{name}.plutoprefab', prefab)

for relative in ['Scripts/GrenadeRules.cs', 'Scripts/MultiplayerSession.Grenades.cs']:
    path = ASSETS / relative
    identity = uuid.uuid5(uuid.NAMESPACE_URL, 'cod/grenades/' + relative).hex
    Path(str(path) + '.plutometa').write_text(f'PLUTOASSET\t1\nID\t{identity}\nIMPORTER_VERSION\t1\n')
    generated.append(path)

manifest = ROOT / 'CoDplutoproject.plutoproject'
lines = manifest.read_text().splitlines()
for path in generated:
    reference = 'project://' + path.relative_to(ASSETS).as_posix()
    lines = [line for line in lines if not line.startswith('ASSET\t' + reference + '\t')]
    kind = {'.plutoparticles': 'Particle System', '.plutoprefab': 'Prefab', '.plutomaterial': 'Material', '.cs': 'Script'}.get(path.suffix, 'Texture')
    lines.append(f'ASSET\t{reference}\t{path.stat().st_size}\t{kind}')
manifest.write_text('\n'.join(lines) + '\n')
print(f'Created and registered {len(generated)} grenade assets.')
