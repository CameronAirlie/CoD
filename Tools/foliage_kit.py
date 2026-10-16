"""Shared LeafSet foliage kit for map dressing tools (place_*_foliage.py).

The LeafSet kit (Assets/SourceModels/LeafSet/LeafSet.gltf) is 153 parts of eight plant species,
all authored at the origin. GROUPS turns those parts into named foliage types (overgrowth
patches, shrubs, weeds, ground mats and edge strips) for one instanced FoliageComponent per map.
This module holds everything map-independent: reading the imported mesh, the engine-identical
instance origin maths, the dusty leaf materials, serialization, the palette prefab and asset
registration. Map tools only decide where instances go.
"""
from pathlib import Path
import math, random, re, struct, uuid

ROOT = Path(__file__).resolve().parents[1]
A = ROOT / 'Assets'
PALETTE = 'Prefabs/Foliage/LeafSetFoliage.plutoprefab'

# The kit's lime leaves read as freshly watered next to sun-baked asphalt and painted steel.
# Outdoor weeds are dusty and olive, so foliage uses tinted copies of the imported leaf materials.
DUSTY_TINT = '0.74,0.72,0.50,1'
FLAT_ROLES = {'mat', 'strip'}
DRAW_DISTANCE = {'patch': 0.0, 'shrub': 90.0, 'weed': 50.0, 'mat': 40.0, 'strip': 45.0}  # 0 inherits
PLAY_HEIGHT_LIMIT = 0.5  # metres, for anything in open playable space

# Groups: (name, role, parts, tags). Parts are LeafSet node names; one type stacks its parts at a
# shared origin, the way the kit's sprigs were authored. Tags steer which species grow where.
GROUPS = [
    *[(f'Overgrowth - {label} {n + 1}', 'patch', [part], tags) for label, parts, tags in [
        ('Tall Grass', ['LeafSetA_23', 'LeafSetA_24'], ['dry', 'sun']),
        ('Nettle', ['LeafSetB_20', 'LeafSetB_21'], ['sun']),
        ('Brush', ['LeafSetC_16', 'LeafSetC_17'], ['dry', 'sun']),
        ('Clover', ['LeafSetD_20', 'LeafSetD_21'], ['shade', 'wet']),
        ('Dock', ['LeafSetE_13', 'LeafSetE_14'], ['wet', 'sun']),
        ('Bramble', ['LeafSetF_19', 'LeafSetF_20'], ['sun']),
        ('Creeper', ['LeafSetG_20', 'LeafSetG_21'], ['shade']),
        ('Broadleaf', ['LeafSetH_14', 'LeafSetH_15'], ['shade', 'wet']),
    ] for n, part in enumerate(parts)],
    ('Shrub - Reeds', 'shrub', ['LeafSetA_17', 'LeafSetA_18', 'LeafSetA_15', 'LeafSetA_13'], ['dry', 'sun']),
    ('Shrub - Nettle', 'shrub', ['LeafSetB_10', 'LeafSetB_11', 'LeafSetB_12', 'LeafSetB_9'], ['sun']),
    ('Shrub - Brush', 'shrub', ['LeafSetC_12', 'LeafSetC_10'], ['dry', 'sun']),
    ('Shrub - Sapling', 'shrub', ['LeafSetC_13', 'LeafSetC_9', 'LeafSetC_11'], ['dry', 'sun']),
    ('Shrub - Dock', 'shrub', ['LeafSetE_9', 'LeafSetE_6', 'LeafSetE_7'], ['wet', 'sun']),
    ('Shrub - Bramble', 'shrub', ['LeafSetF_16', 'LeafSetF_12', 'LeafSetF_15'], ['sun']),
    ('Shrub - Creeper', 'shrub', ['LeafSetG_16', 'LeafSetG_12', 'LeafSetG_14'], ['shade']),
    ('Shrub - Broadleaf', 'shrub', ['LeafSetH_12', 'LeafSetH_13', 'LeafSetH_9'], ['shade', 'wet']),
    ('Weeds - Grass Tuft', 'weed', ['LeafSetA_3', 'LeafSetA_5', 'LeafSetA_2'], ['dry', 'sun', 'crack']),
    ('Weeds - Grass Spray', 'weed', ['LeafSetA_13', 'LeafSetA_9'], ['dry', 'sun']),
    ('Weeds - Clover Sprigs', 'weed', ['LeafSetD_8', 'LeafSetD_9', 'LeafSetD_5', 'LeafSetD_3'], ['shade', 'wet', 'crack']),
    ('Weeds - Clover Clump', 'weed', ['LeafSetD_12', 'LeafSetD_13', 'LeafSetD_2', 'LeafSetD_4'], ['shade', 'wet']),
    ('Weeds - Clover Patch', 'weed', ['LeafSetD_19'], ['shade', 'wet']),
    ('Weeds - Pennywort', 'weed', ['LeafSetG_6', 'LeafSetG_7', 'LeafSetG_3', 'LeafSetG_4', 'LeafSetG_10'], ['shade', 'wet']),
    ('Weeds - Creeper Patch', 'weed', ['LeafSetG_19'], ['shade']),
    ('Weeds - Broadleaf', 'weed', ['LeafSetH_7', 'LeafSetH_3', 'LeafSetH_4'], ['shade', 'wet']),
    ('Weeds - Broadleaf Low', 'weed', ['LeafSetH_5', 'LeafSetH_2'], ['shade', 'wet']),
    ('Weeds - Sprouts', 'weed', ['LeafSetC_4', 'LeafSetC_5', 'LeafSetC_3'], ['dry', 'sun', 'crack']),
    ('Weeds - Dock Rosette', 'weed', ['LeafSetE_2', 'LeafSetE_12'], ['wet', 'sun', 'crack']),
    ('Ground - Clover Mat', 'mat', ['LeafSetD_18'], ['shade', 'wet']),
    ('Ground - Leaf Litter', 'mat', ['LeafSetC_15'], ['dry', 'sun']),
    ('Ground - Creeper Mat', 'mat', ['LeafSetG_18'], ['shade']),
    ('Ground - Dock Mat', 'mat', ['LeafSetE_11'], ['wet']),
    ('Ground - Bramble Mat', 'mat', ['LeafSetF_18'], ['sun']),
    ('Edge Strip - Clover', 'strip', ['LeafSetD_17'], ['shade', 'wet']),
    ('Edge Strip - Litter', 'strip', ['LeafSetC_14'], ['dry', 'sun']),
    ('Edge Strip - Dock', 'strip', ['LeafSetE_10'], ['wet']),
    ('Edge Strip - Bramble', 'strip', ['LeafSetF_17'], ['sun']),
    ('Edge Strip - Creeper', 'strip', ['LeafSetG_17'], ['shade']),
]

# ---------------------------------------------------------------- files

def read_text(path):
    """Text plus its line ending, so rewrites keep whatever the editor or git checkout produced."""
    raw = path.read_bytes().decode('utf-8')
    return raw.replace('\r\n', '\n'), '\r\n' if '\r\n' in raw else '\n'


def write_text(path, text, newline='\n'):
    path.write_bytes(text.replace('\n', newline).encode('utf-8'))


def register(rel, kind):
    path = A / rel
    meta = Path(str(path) + '.plutometa')
    if not meta.exists():
        write_text(meta, 'PLUTOASSET\t1\nID\t' + uuid.uuid5(uuid.NAMESPACE_URL, 'cod/foliage/' + rel).hex + '\nIMPORTER_VERSION\t1\n')
    manifest = ROOT / 'CoDplutoproject.plutoproject'
    ref = 'project://' + rel
    text, newline = read_text(manifest)
    lines = text.splitlines(); entry = f'ASSET\t{ref}\t{path.stat().st_size}\t{kind}'
    existing = next((i for i, l in enumerate(lines) if l.startswith('ASSET\t' + ref + '\t')), None)
    if existing is None: lines.append(entry)
    elif lines[existing] == entry: return
    else: lines[existing] = entry  # update in place so the manifest does not churn
    write_text(manifest, '\n'.join(lines) + '\n', newline)

# ---------------------------------------------------------------- meshes

def model_identity():
    meta = (A / 'SourceModels/LeafSet/LeafSet.gltf.plutometa').read_text()
    asset_id = re.search(r'^ID\t(\w+)', meta, re.M).group(1)
    candidates = sorted((ROOT / 'Library/Artifacts').glob('*/Files/SourceModels/LeafSet/LeafSet.plutomodel'),
                        key=lambda p: p.stat().st_mtime)
    if not candidates:
        raise SystemExit('LeafSet.gltf has not been imported yet; open the project in the editor first.')
    model = candidates[-1]
    object_id = re.search(r'^OBJECT\t(\d+)\tMesh\t', model.read_text(errors='replace'), re.M).group(1)
    return asset_id, object_id, model.with_suffix('.plutomesh')


def read_plutomesh(path, with_materials=False):
    """LPGM v5: 88-byte vertices, uint32 indices, submeshes with names and LOD ranges, then the
    material reference table. Returns positions, indices, {name: (index, start, count, material)}
    and, when asked, the material references."""
    data = path.read_bytes(); off = 0

    def take(fmt):
        nonlocal off
        values = struct.unpack_from('<' + fmt, data, off); off += struct.calcsize('<' + fmt); return values

    def string():
        nonlocal off
        (length,) = take('Q'); text = data[off:off + length].decode(); off += length; return text
    magic, version, vertex_count = take('IIQ')
    if (magic, version) != (0x4d47504c, 5):
        raise SystemExit(f'Unsupported mesh format in {path}')
    positions = [struct.unpack_from('<3f', data, off + i * 88) for i in range(vertex_count)]
    off += vertex_count * 88
    (index_count,) = take('Q')
    indices = struct.unpack_from(f'<{index_count}I', data, off); off += index_count * 4
    (submesh_count,) = take('Q')
    submeshes = {}
    for index in range(submesh_count):
        start, count, material, *_ = take('IIIi4f')
        name = string()
        (lods,) = take('Q'); off += lods * 16
        submeshes[name] = (index, start, count, material)
    if not with_materials:
        return positions, indices, submeshes
    take('BQQQ')
    (material_count,) = take('Q')
    return positions, indices, submeshes, [string() for _ in range(material_count)]


class Part:
    """Geometry facts the FoliageComponent derives at runtime, computed identically."""

    def __init__(self, positions, indices, index, start, count, material):
        self.index, self.material = index, material
        used = [positions[indices[k]] for k in range(start, start + count)]
        lo = [min(p[a] for p in used) for a in range(3)]; hi = [max(p[a] for p in used) for a in range(3)]
        center = [(lo[a] + hi[a]) / 2 for a in range(3)]
        self.radius = max(math.dist(p, center) for p in used)          # Mesh::ComputeBounds
        total, weighted = 0.0, [0.0, 0.0, 0.0]                          # area-weighted centroid
        for k in range(start, start + count - 2, 3):
            p0, p1, p2 = (positions[indices[k + j]] for j in range(3))
            u = [p1[a] - p0[a] for a in range(3)]; v = [p2[a] - p0[a] for a in range(3)]
            area = 0.5 * math.sqrt((u[1] * v[2] - u[2] * v[1]) ** 2 + (u[2] * v[0] - u[0] * v[2]) ** 2 + (u[0] * v[1] - u[1] * v[0]) ** 2)
            if area > 0:
                total += area
                for a in range(3): weighted[a] += (p0[a] + p1[a] + p2[a]) / 3 * area
        self.com = [w / total for w in weighted] if total else center
        self.points = used


class FoliageType:
    def __init__(self, name, role, parts, tags, catalog):
        self.name, self.role, self.tags = name, role, set(tags)
        self.parts = [catalog[p] for p in parts]
        materials = {p.material for p in self.parts}
        if len(materials) != 1:
            raise SystemExit(f'{name} mixes leaf atlases; a foliage type takes one material')
        self.material = materials.pop()
        weights = [max(1.0, p.radius * p.radius) for p in self.parts]  # ComputeSubmeshGroupCenterOfMass
        self.com = [sum(p.com[a] * w for p, w in zip(self.parts, weights)) / sum(weights) for a in range(3)]
        points = [q for p in self.parts for q in p.points]
        self.min_y = min(q[1] for q in points); self.height = max(q[1] for q in points) - self.min_y
        rel = [(q[0] - self.com[0], q[2] - self.com[2]) for q in points]
        self.extent = (min(r[0] for r in rel), max(r[0] for r in rel), min(r[1] for r in rel), max(r[1] for r in rel))
        self.radius = max(math.hypot(*r) for r in rel)
        self.instances = []

    def depth(self, scale):
        """Half thickness across the long (local X) axis, for edge strips laid along walls."""
        return max(-self.extent[2], self.extent[3]) * scale

    def half_length(self, scale):
        return max(-self.extent[0], self.extent[1]) * scale

    def origin_height(self, scale):
        """Instance y that rests the type's base on the ground: the instance origin is its centre of mass."""
        return scale * (self.com[1] - self.min_y) + (.015 if self.role in FLAT_ROLES else 0.0)


def load_types():
    asset_id, object_id, mesh_path = model_identity()
    positions, indices, submeshes = read_plutomesh(mesh_path)
    catalog = {name: Part(positions, indices, *info) for name, info in submeshes.items()}
    types = [FoliageType(*group, catalog) for group in GROUPS]
    for t in types:
        if t.role in ('weed', 'mat', 'strip') and t.height > PLAY_HEIGHT_LIMIT + .05:
            raise SystemExit(f'{t.name} is {t.height:.2f} m tall; too tall for open playable space')
    return types, asset_id, object_id, mesh_path

# ---------------------------------------------------------------- serialization

def dusty_materials(mesh_path, types):
    """Tinted copies of the imported leaf materials; textures stay shared with the model.
    Created once: after that they are project materials to tune in the editor, never overwritten."""
    references = {}
    for index in sorted({t.material for t in types}):
        rel = f'Materials/Foliage/LeafSet_Dusty_{index}.plutomaterial'
        if not (A / rel).exists():
            text, _ = read_text(mesh_path.with_name(f'M_LeafSet_{index}.plutomaterial'))
            (A / rel).parent.mkdir(parents=True, exist_ok=True)
            write_text(A / rel, re.sub(r'^Color=.*$', 'Color=' + DUSTY_TINT, text, flags=re.M))
            register(rel, 'Material')
        references[index] = 'project://' + rel
    return references


def foliage_component(entity_id, types, asset_id, object_id, materials, with_instances=True, cell_size=24.0):
    mesh = f'asset://{asset_id}#{object_id}'
    props = [('PaintEnabled', 4, 'false'), ('BrushMode', 2, 'Add'), ('BrushRadius', 0, '2.000000'), ('Density', 1, '2'),
             ('MinScale', 0, '0.700000'), ('MaxScale', 0, '1.050000'), ('MaxDrawDistance', 0, '120.000000'),
             ('MaxShadowDistance', 0, '45.000000'), ('MinRenderLod', 1, '0'), ('MinShadowLod', 1, '1'),
             ('CastShadows', 4, 'true'), ('Static', 4, 'true'), ('SelectedType', 1, '0'),
             ('FoliageTypeCount', 1, str(len(types)))]
    for n, t in enumerate(types):
        p = f'Type.{n}.'
        instances = ';'.join(f'{k + 1},{x:.4f},{y:.4f},{z:.4f},0,{yaw:.2f},0,{s:.3f},{s:.3f},{s:.3f}'
                             for k, (x, y, z, yaw, s) in enumerate(t.instances)) if with_instances else ''
        props += [(p + 'Name', 2, t.name), (p + 'SourceMesh', 2, mesh), (p + 'ModelAssetId', 2, asset_id),
                  (p + 'ModelObjectId', 2, object_id), (p + 'MaterialAsset', 2, materials[t.material]), (p + 'SubmeshIndex', 1, '-1'),
                  (p + 'SubmeshIndices', 2, ','.join(str(part.index) for part in t.parts)),
                  (p + 'UseGeneratedLods', 4, 'false'), (p + 'AlignToTerrainNormal', 4, 'false'),
                  (p + 'AssetReference', 2, ''), (p + 'CellSize', 0, f'{cell_size:.6f}'),
                  (p + 'MaxDrawDistance', 0, f'{DRAW_DISTANCE[t.role]:.6f}'), (p + 'CollisionEnabled', 4, 'false'),
                  (p + 'CollisionCenter', 3, '0.000000,1.000000,0.000000'), (p + 'CollisionRadius', 0, '0.350000'),
                  (p + 'CollisionHeight', 0, '2.000000'), (p + 'Instances', 2, instances)]
    return (f'COMPONENT\t{entity_id}\tFoliageComponent\t1\n' +
            ''.join(f'PROPERTY\t{k}\t{kind}\t{v}\t0\n' for k, kind, v in props) + 'END_COMPONENT\n')


def scene_blocks(text):
    parts = re.split(r'(?=^ENTITY\t)', text, flags=re.M)
    return parts[0], parts[1:]


def write_scene(scene_path, blocks_to_drop, kept_blocks, head, newline, name, types, asset_id, object_id, materials,
                previous_id=None, cell_size=24.0):
    """Append the foliage entity; it keeps its previous ID across runs, else takes the next free one."""
    entity_id = previous_id or 1 + max(int(b.split('\t')[1]) for b in kept_blocks + blocks_to_drop)
    entity = f'ENTITY\t{entity_id}\t0\t1\t{name}\t0.000000,0.000000,0.000000\t0.000000,0.000000,0.000000\t1.000000,1.000000,1.000000\n'
    write_text(scene_path, head + ''.join(kept_blocks) + entity +
               foliage_component(entity_id, types, asset_id, object_id, materials, cell_size=cell_size), newline)
    return entity_id


def write_palette(types, asset_id, object_id, materials):
    """Every type with no instances: drop into any map and paint with the foliage brush."""
    (A / PALETTE).parent.mkdir(parents=True, exist_ok=True)
    write_text(A / PALETTE, 'SCENE\t1\nENTITY\t1\t0\t1\tLeafSet Foliage\t0.000000,0.000000,0.000000\t0.000000,0.000000,0.000000\t1.000000,1.000000,1.000000\n'
               + foliage_component(1, types, asset_id, object_id, materials, with_instances=False))
    register(PALETTE, 'Prefab')


def summary(types):
    by_role = {}
    for t in types:
        by_role[t.role] = by_role.get(t.role, 0) + len(t.instances)
    return (f'{sum(len(t.instances) for t in types)} foliage instances across {len(types)} types '
            f'({", ".join(f"{n} {r}" for r, n in by_role.items())})')
