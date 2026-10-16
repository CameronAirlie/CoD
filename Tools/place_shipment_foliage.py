"""Dress Shipment with LeafSet foliage. Run from any directory; deterministic, re-runnable.

The LeafSet kit (Assets/SourceModels/LeafSet/LeafSet.gltf) is 153 parts of eight plant
species, all authored at the origin. This tool groups those parts into named foliage types
(overgrowth patches, shrubs, weeds, ground mats and edge strips) on one instanced
FoliageComponent, then places instances around the yard:

  * dense overgrowth in the unreachable strip between the fence and the yard edge,
  * patchy clusters along the inside of the fence and thickets in the corners,
  * low weeds hugging container corners, concrete covers, crates and the parked forklift,
  * moisture-loving plants around the storm drains, and sparse weeds in asphalt cracks.

Inside the playable area nothing taller than about half a metre is placed (except along the
fence line and in the corners), nothing is placed near spawns, and foliage has no collision,
so sightlines, movement and the baked navmesh are unaffected.

Outputs (all replaced on re-run):
  * Shipment.plutoscene: the "Foliage (LeafSet)" entity. The original LeafSet test entity
    (the whole kit stacked in one clump) is removed.
  * Prefabs/Foliage/LeafSetFoliage.plutoprefab: every type with no instances, a palette for
    painting LeafSet foliage onto other maps with the editor's foliage brush.
Run Tools/build_shipment.py first if you regenerate the yard; it rewrites the scene.
"""
from pathlib import Path
import math, random, re, struct, uuid

ROOT = Path(__file__).resolve().parents[1]
A = ROOT / 'Assets'
SCENE = A / 'Scenes/Shipment.plutoscene'
PALETTE = 'Prefabs/Foliage/LeafSetFoliage.plutoprefab'
FOLIAGE_ID = 90000
FOLIAGE_NAME = 'Foliage (LeafSet)'
SEED = 20261016

def read_text(path):
    """Text plus its line ending, so rewrites keep whatever the editor or git checkout produced."""
    raw = path.read_bytes().decode('utf-8')
    return raw.replace('\r\n', '\n'), '\r\n' if '\r\n' in raw else '\n'


def write_text(path, text, newline='\n'):
    path.write_bytes(text.replace('\n', newline).encode('utf-8'))

# ---------------------------------------------------------------- imported LeafSet mesh

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


def read_plutomesh(path):
    """LPGM v5: 88-byte vertices, uint32 indices, submeshes with names and LOD ranges."""
    data = path.read_bytes(); off = 0

    def take(fmt):
        nonlocal off
        values = struct.unpack_from('<' + fmt, data, off); off += struct.calcsize('<' + fmt); return values
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
        (length,) = take('Q'); name = data[off:off + length].decode(); off += length
        (lods,) = take('Q'); off += lods * 16
        submeshes[name] = (index, start, count, material)
    return positions, indices, submeshes


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
# The kit's lime leaves read as freshly watered next to sun-baked asphalt and painted steel.
# Yard weeds are dusty and olive, so foliage uses tinted copies of the imported leaf materials.
DUSTY_TINT = '0.74,0.72,0.50,1'
FLAT_ROLES = {'mat', 'strip'}
DRAW_DISTANCE = {'patch': 0.0, 'shrub': 90.0, 'weed': 50.0, 'mat': 40.0, 'strip': 45.0}  # 0 inherits
PLAY_HEIGHT_LIMIT = 0.5  # metres, for anything in the open playable yard

# ---------------------------------------------------------------- yard layout

def scene_blocks(text):
    parts = re.split(r'(?=^ENTITY\t)', text, flags=re.M)
    return parts[0], parts[1:]


def obstacles_from_scene(blocks):
    """Ground-level box colliders (containers, covers, crates, forklift) as oriented rectangles."""
    boxes = []
    for block in blocks:
        f = block.split('\n', 1)[0].split('\t')
        name = f[4]
        if not name.endswith(' collision') or name in ('Yard collision', 'Fence collision'):
            continue
        px, py, pz = map(float, f[5].split(',')); yaw = float(f[6].split(',')[1]); sx, sy, sz = map(float, f[7].split(','))
        if py - sy / 2 > 0.3:
            continue  # stacked container, nothing grows up there
        boxes.append(dict(name=name[:-len(' collision')], x=px, z=pz, yaw=yaw, hx=sx / 2, hz=sz / 2, h=sy))
    return boxes


def to_local(box, x, z):
    a = math.radians(box['yaw']); dx, dz = x - box['x'], z - box['z']
    return dx * math.cos(a) - dz * math.sin(a), dx * math.sin(a) + dz * math.cos(a)


def to_world(box, lx, lz):
    a = math.radians(box['yaw'])
    return box['x'] + lx * math.cos(a) + lz * math.sin(a), box['z'] - lx * math.sin(a) + lz * math.cos(a)


def circle_hits_box(box, x, z, r):
    lx, lz = to_local(box, x, z)
    dx = max(abs(lx) - box['hx'], 0.0); dz = max(abs(lz) - box['hz'], 0.0)
    return dx * dx + dz * dz < r * r


SPAWNS = [(-16, -16), (16, -16), (16, 16), (-16, 16)]
HARDPOINTS = [(0, 0), (-12, -12), (-13, 10), (12, 12), (13, -10)]
DRAINS = [(-10, 0), (10, 0), (0, -10), (0, 10)]


def pallets():
    """Wooden pallets beside the concrete covers (meshes only, no colliders)."""
    return [dict(name='Pallet', x=x + (j - 1) * .8, z=z + 1, yaw=0, hx=.36, hz=.55, h=.24)
            for x, z in [(-16, -15), (16, 15), (-16, 15), (16, -15)] for j in range(3)]

# ---------------------------------------------------------------- placement

class Placer:
    def __init__(self, types, boxes):
        self.rng = random.Random(SEED)
        self.types = types; self.boxes = boxes + pallets()
        self.drains = [dict(name='Drain', x=x, z=z, yaw=0, hx=.6, hz=.28, h=.03) for x, z in DRAINS]
        self.placed = []  # (x, z, r, role)

    def pick(self, roles, tags=None, avoid=None):
        options = [t for t in self.types if t.role in roles and (not tags or t.tags & set(tags))]
        if avoid:
            options = [t for t in options if not (t.tags & set(avoid))] or options
        return self.rng.choice(options)

    def scale_for(self, role):
        return {'patch': (.5, .78), 'shrub': (.62, .9), 'weed': (.75, 1.15), 'mat': (.65, 1.0), 'strip': (.75, 1.05)}[role]

    def fits(self, ftype, x, z, scale, *, in_play, overlap=0.65, wall_ok=False):
        r = ftype.radius * scale
        if in_play and ftype.height * scale > PLAY_HEIGHT_LIMIT:
            return False
        if max(abs(x), abs(z)) + r * .55 > 22.1:  # keep leaves over the asphalt
            return False
        if in_play and ftype.role not in FLAT_ROLES and max(abs(x), abs(z)) + r * .8 > 19.95:
            return False  # upright plants inside stay inside the fence; flat litter may run under it
        if any(math.dist((x, z), s) < 1.8 + r for s in SPAWNS):
            return False
        if in_play and math.dist((x, z), HARDPOINTS[0]) < 1.6 + r:
            return False
        core = r * (.35 if wall_ok else .7)
        if any(circle_hits_box(b, x, z, core) for b in self.boxes + self.drains):
            return False
        for px, pz, pr, role in self.placed:
            limit = (r + pr) * (overlap if ftype.role not in FLAT_ROLES and role not in FLAT_ROLES else .35)
            if math.dist((x, z), (px, pz)) < limit:
                return False
        return True

    def add(self, ftype, x, z, scale, yaw=None, **checks):
        if not self.fits(ftype, x, z, scale, **checks):
            return False
        yaw = self.rng.uniform(0, 360) if yaw is None else yaw
        lift = .015 if ftype.role in FLAT_ROLES else 0.0
        y = scale * (ftype.com[1] - ftype.min_y) + lift  # instance origin is the type's centre of mass
        ftype.instances.append((x, y, z, yaw, scale))
        self.placed.append((x, z, ftype.radius * scale, ftype.role))
        return True

    def try_add(self, roles, x, z, *, tags=None, scale=None, tries=6, jitter=0.0, **checks):
        for _ in range(tries):
            ftype = self.pick(roles, tags)
            s = scale or self.rng.uniform(*self.scale_for(ftype.role))
            jx, jz = self.rng.uniform(-jitter, jitter), self.rng.uniform(-jitter, jitter)
            if self.add(ftype, x + jx, z + jz, s, **checks):
                return True
        return False

    # --- zones -------------------------------------------------------------------------

    def colony(self, side, t):
        """Species changes every few metres along the perimeter, so plants grow in colonies."""
        species = ['Tall Grass', 'Nettle', 'Brush', 'Clover', 'Dock', 'Bramble', 'Creeper', 'Broadleaf']
        k = int(math.floor((t + 22 + side * 5.3) / 7.5))
        return species[(k * 3 + side * 5) % len(species)]

    def perimeter(self, side, t, depth):
        """Point at distance `depth` from the yard centre on one of four sides, plus wall yaw."""
        return [(t, -depth, 0.0), (depth, t, 90.0), (t, depth, 0.0), (-depth, t, 90.0)][side]

    def outside_fence(self):
        for side in range(4):
            t = -22.4
            while t < 22.4:
                t += self.rng.uniform(.85, 1.3)
                depth = self.rng.uniform(20.8, 21.3)
                x, z, _ = self.perimeter(side, t, depth)
                species = self.colony(side, t)
                if self.rng.random() < .82:
                    patches = [ft for ft in self.types if ft.role == 'patch' and species in ft.name]
                    ftype = self.rng.choice(patches if self.rng.random() < .8 else [ft for ft in self.types if ft.role == 'patch'])
                    self.add(ftype, x, z, self.rng.uniform(.58, .82), in_play=False, overlap=.34)
                else:
                    self.try_add({'shrub'}, x, z, in_play=False, overlap=.4)
                # Weeds and litter fill the gaps under the wire.
                for _ in range(2):
                    if self.rng.random() > .7:
                        continue
                    x, z, yaw = self.perimeter(side, t + self.rng.uniform(-.7, .7), self.rng.uniform(20.25, 20.65))
                    if self.rng.random() < .45:
                        self.add(self.pick({'strip'}), x, z, self.rng.uniform(.8, 1.05),
                                 yaw=yaw + self.rng.choice([0, 180]) + self.rng.uniform(-8, 8), in_play=False)
                    else:
                        self.try_add({'weed', 'shrub'}, x, z, in_play=False, overlap=.4)

    def fence_line(self):
        """Patchy clusters along the inside of the fence, leaving open runs between them."""
        for side in range(4):
            t = -18.5
            while t < 18.5:
                t += self.rng.uniform(1.6, 3.4)
                if self.rng.random() > .8:
                    continue
                for offset in ([0.0] if self.rng.random() < .55 else [-.7, .7]):
                    strip = self.pick({'strip'})
                    s = self.rng.uniform(.8, 1.05)
                    x, z, yaw = self.perimeter(side, t + offset, 19.93 - strip.depth(s) - .04)
                    self.add(strip, x, z, s, yaw=yaw + self.rng.choice([0, 180]) + self.rng.uniform(-5, 5),
                             in_play=True, wall_ok=True)
                for _ in range(self.rng.randint(2, 4)):
                    x, z, _ = self.perimeter(side, t + self.rng.uniform(-1.2, 1.2), self.rng.uniform(19.2, 19.6))
                    self.try_add({'weed'}, x, z, in_play=True, overlap=.5)
                if self.rng.random() < .45:  # the odd taller plant right against the wire
                    x, z, _ = self.perimeter(side, t + self.rng.uniform(-.8, .8), 19.45)
                    self.try_add({'shrub'}, x, z, scale=self.rng.uniform(.55, .72), in_play=False, overlap=.5)

    def corners(self):
        """Wind-blown litter and seeds collect in the yard corners, so they are the densest inside."""
        for sx in (-1, 1):
            for sz in (-1, 1):
                patches = [ft for ft in self.types if ft.role == 'patch' and ft.height < 1.05]
                self.add(self.rng.choice(patches), sx * 19.05, sz * 19.05, self.rng.uniform(.5, .58), in_play=False)
                for along in ((sx * 17.4, sz * 19.2), (sx * 19.2, sz * 17.4)):  # the thicket runs along both fences
                    self.add(self.rng.choice(patches), *along, self.rng.uniform(.42, .5), in_play=False, overlap=.4)
                for _ in range(24):
                    if sum(1 for px, pz, *_ in self.placed if math.dist((px, pz), (sx * 19, sz * 19)) < 2.8) >= 11:
                        break
                    d = self.rng.uniform(1.0, 2.2); a = self.rng.uniform(0, math.pi / 2)
                    x, z = sx * (19.6 - d * math.cos(a)), sz * (19.6 - d * math.sin(a))
                    self.try_add({'weed', 'shrub', 'mat'}, x, z, in_play=False, overlap=.55)

    def hug(self, box, lx, lz, roles, tags, edge_axis):
        """Place against a box face: (lx, lz) is the contact point in box space."""
        ftype = self.pick(roles, tags)
        s = self.rng.uniform(*self.scale_for(ftype.role))
        if ftype.role == 'strip':
            push = ftype.depth(s) + .04
            yaw = box['yaw'] + (90 if edge_axis == 'z' else 0) + self.rng.choice([0, 180]) + self.rng.uniform(-4, 4)
        else:
            push = ftype.radius * s * .45; yaw = None
        nx = (1 if lx > 0 else -1) if edge_axis == 'z' else 0
        nz = (1 if lz > 0 else -1) if edge_axis == 'x' else 0
        x, z = to_world(box, lx + nx * push, lz + nz * push)
        return self.add(ftype, x, z, s, yaw=yaw, in_play=True, wall_ok=True)

    def container_bases(self):
        """Weeds root where container corner castings shade the asphalt and forklifts never reach."""
        containers = [b for b in self.boxes if b['hx'] > 1.1 and b['hz'] > 2.9 and b['h'] < .3]
        for box in containers:
            passage = 'passage' in box['name']
            for cx in (-1, 1):
                for cz in (-1, 1):
                    if self.rng.random() < .85:
                        for _ in range(self.rng.randint(1, 2)):  # a main tuft and sometimes a companion
                            self.hug(box, cx * box['hx'], cz * (box['hz'] - self.rng.uniform(.15, .9)), {'weed'}, ['shade', 'wet'], 'z')
                for _ in range(self.rng.randint(1, 2)):
                    if self.rng.random() < .7:
                        self.hug(box, cx * box['hx'], self.rng.uniform(-2.0, 2.0), {'strip'}, ['shade', 'wet'], 'z')
            if not passage:  # open-ended passages are walkways
                for cz in (-1, 1):
                    if self.rng.random() < .5:
                        self.hug(box, self.rng.uniform(-.8, .8), cz * box['hz'], {'weed', 'strip'}, ['shade'], 'x')

    def props(self):
        for box in self.boxes:
            if box['name'] == 'Cover':  # concrete footings: weeds at both ends and along the back
                for cx in (-1, 1):
                    self.hug(box, cx * box['hx'], self.rng.uniform(-.2, .2), {'weed'}, ['sun', 'dry'], 'z')
                self.hug(box, self.rng.uniform(-.7, .7), -box['hz'], {'strip'}, None, 'x')
            elif box['name'] == 'Crates':
                corners = [(cx, cz) for cx in (-1, 1) for cz in (-1, 1)]
                for cx, cz in self.rng.sample(corners, 2):
                    self.hug(box, cx * box['hx'], cz * (box['hz'] - .2), {'weed'}, None, 'z')
            elif box['name'] == 'Forklift':  # parked for good: weeds around the wheels and under the rear
                for wx in (-1, 1):
                    for wz in (-.7, .7):
                        if self.rng.random() < .7:
                            x, z = to_world(box, wx * (box['hx'] + .2), wz)
                            self.try_add({'weed'}, x, z, tags=['shade', 'crack'], in_play=True, wall_ok=True, overlap=.4)
                x, z = to_world(box, 0, box['hz'] + .45)
                self.try_add({'mat', 'weed'}, x, z, tags=['shade'], in_play=True, wall_ok=True)

    def drains_ring(self):
        """Run-off keeps the asphalt around each storm drain damp; plants crowd the grate."""
        for dx, dz in DRAINS:
            count = self.rng.randint(5, 8); start = self.rng.uniform(0, math.tau)
            for k in range(count):
                a = start + k * math.tau / count + self.rng.uniform(-.3, .3); d = self.rng.uniform(.75, 1.25)
                self.try_add({'weed', 'mat'}, dx + d * math.cos(a), dz + d * math.sin(a), tags=['wet'], in_play=True, overlap=.45)

    def cracks(self, count=34):
        """Sparse weeds in the asphalt cracks, often two or three along the same crack."""
        placed = 0
        for _ in range(count * 20):
            if placed >= count:
                break
            x, z = self.rng.uniform(-18.4, 18.4), self.rng.uniform(-18.4, 18.4)
            if not self.try_add({'weed'}, x, z, tags=['crack'], scale=self.rng.uniform(.6, .95), tries=2, in_play=True):
                continue
            placed += 1
            heading = self.rng.uniform(0, math.tau)
            for step in range(self.rng.randint(0, 2)):
                d = .35 + .3 * step
                self.try_add({'weed'}, x + d * math.cos(heading), z + d * math.sin(heading), tags=['crack'],
                             scale=self.rng.uniform(.5, .8), tries=2, in_play=True, overlap=.3)

# ---------------------------------------------------------------- serialization

def dusty_materials(mesh_path, types):
    """Tinted copies of the imported leaf materials; textures stay shared with the model."""
    references = {}
    for index in sorted({t.material for t in types}):
        source = mesh_path.with_name(f'M_LeafSet_{index}.plutomaterial')
        text, _ = read_text(source)
        rel = f'Materials/Foliage/LeafSet_Dusty_{index}.plutomaterial'
        (A / rel).parent.mkdir(parents=True, exist_ok=True)
        write_text(A / rel, re.sub(r'^Color=.*$', 'Color=' + DUSTY_TINT, text, flags=re.M))
        register(rel, 'Material')
        references[index] = 'project://' + rel
    return references


def foliage_component(entity_id, types, asset_id, object_id, materials, with_instances=True):
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
                  (p + 'AssetReference', 2, ''), (p + 'CellSize', 0, '24.000000'),
                  (p + 'MaxDrawDistance', 0, f'{DRAW_DISTANCE[t.role]:.6f}'), (p + 'CollisionEnabled', 4, 'false'),
                  (p + 'CollisionCenter', 3, '0.000000,1.000000,0.000000'), (p + 'CollisionRadius', 0, '0.350000'),
                  (p + 'CollisionHeight', 0, '2.000000'), (p + 'Instances', 2, instances)]
    return (f'COMPONENT\t{entity_id}\tFoliageComponent\t1\n' +
            ''.join(f'PROPERTY\t{k}\t{kind}\t{v}\t0\n' for k, kind, v in props) + 'END_COMPONENT\n')


def register(rel, kind):
    path = A / rel
    meta = Path(str(path) + '.plutometa')
    if not meta.exists():
        write_text(meta, 'PLUTOASSET\t1\nID\t' + uuid.uuid5(uuid.NAMESPACE_URL, 'cod/foliage/' + rel).hex + '\nIMPORTER_VERSION\t1\n')
    manifest = ROOT / 'CoDplutoproject.plutoproject'
    ref = 'project://' + rel
    text, newline = read_text(manifest)
    lines = [l for l in text.splitlines() if not l.startswith('ASSET\t' + ref + '\t')]
    lines.append(f'ASSET\t{ref}\t{path.stat().st_size}\t{kind}')
    write_text(manifest, '\n'.join(lines) + '\n', newline)


def main():
    asset_id, object_id, mesh_path = model_identity()
    positions, indices, submeshes = read_plutomesh(mesh_path)
    catalog = {name: Part(positions, indices, *info) for name, info in submeshes.items()}
    types = [FoliageType(*group, catalog) for group in GROUPS]
    for t in types:
        if t.role in ('weed', 'mat', 'strip') and t.height > PLAY_HEIGHT_LIMIT + .05:
            raise SystemExit(f'{t.name} is {t.height:.2f} m tall; too tall for the open yard')

    scene_text, scene_newline = read_text(SCENE)
    head, blocks = scene_blocks(scene_text)
    parents = {int(b.split('\t')[1]): int(b.split('\t')[2]) for b in blocks}
    names = {int(b.split('\t')[1]): b.split('\t')[4] for b in blocks}
    # Drop the earlier attempt (the whole kit stacked in one clump) and any previous run.
    drop = {i for i, n in names.items() if parents[i] == 0 and n in ('LeafSet', FOLIAGE_NAME)} | {FOLIAGE_ID}

    def dropped(i):
        return i in drop or (parents.get(i, 0) != 0 and dropped(parents[i]))
    kept = [b for b in blocks if not dropped(int(b.split('\t')[1]))]
    placer = Placer(types, obstacles_from_scene(kept))
    placer.outside_fence(); placer.corners(); placer.fence_line()
    placer.container_bases(); placer.props(); placer.drains_ring(); placer.cracks()

    entity = f'ENTITY\t{FOLIAGE_ID}\t0\t1\t{FOLIAGE_NAME}\t0.000000,0.000000,0.000000\t0.000000,0.000000,0.000000\t1.000000,1.000000,1.000000\n'
    materials = dusty_materials(mesh_path, types)
    write_text(SCENE, head + ''.join(kept) + entity + foliage_component(FOLIAGE_ID, types, asset_id, object_id, materials), scene_newline)

    (A / PALETTE).parent.mkdir(parents=True, exist_ok=True)
    write_text(A / PALETTE, 'SCENE\t1\nENTITY\t1\t0\t1\tLeafSet Foliage\t0.000000,0.000000,0.000000\t0.000000,0.000000,0.000000\t1.000000,1.000000,1.000000\n'
               + foliage_component(1, types, asset_id, object_id, materials, with_instances=False))
    register(PALETTE, 'Prefab')

    removed = len(blocks) - len(kept)
    total = sum(len(t.instances) for t in types)
    by_role = {}
    for t in types:
        by_role[t.role] = by_role.get(t.role, 0) + len(t.instances)
    print(f'Placed {total} foliage instances across {len(types)} types '
          f'({", ".join(f"{n} {r}" for r, n in by_role.items())}); replaced {removed} previous foliage/LeafSet entities.')


if __name__ == '__main__':
    main()
