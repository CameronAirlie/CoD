"""Dress Shipment with LeafSet foliage. Run from any directory; deterministic, re-runnable.

Uses the LeafSet foliage types from foliage_kit.py (overgrowth patches, shrubs, weeds, ground mats
and edge strips) on one instanced FoliageComponent, and places instances around the yard:

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
import math, random

from foliage_kit import (A, FLAT_ROLES, PLAY_HEIGHT_LIMIT, load_types, dusty_materials, read_text, scene_blocks,
                         summary, write_palette, write_scene)

SCENE = A / 'Scenes/Shipment.plutoscene'
FOLIAGE_ID = 90000
FOLIAGE_NAME = 'Foliage (LeafSet)'
SEED = 20261016

# ---------------------------------------------------------------- yard layout

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

def main():
    types, asset_id, object_id, mesh_path = load_types()
    scene_text, scene_newline = read_text(SCENE)
    head, blocks = scene_blocks(scene_text)
    parents = {int(b.split('	')[1]): int(b.split('	')[2]) for b in blocks}
    names = {int(b.split('	')[1]): b.split('	')[4] for b in blocks}
    # Drop the earlier attempt (the whole kit stacked in one clump) and any previous run.
    drop = {i for i, n in names.items() if parents[i] == 0 and n in ('LeafSet', FOLIAGE_NAME)} | {FOLIAGE_ID}

    def dropped(i):
        return i in drop or (parents.get(i, 0) != 0 and dropped(parents[i]))
    kept = [b for b in blocks if not dropped(int(b.split('	')[1]))]
    placer = Placer(types, obstacles_from_scene(kept))
    placer.outside_fence(); placer.corners(); placer.fence_line()
    placer.container_bases(); placer.props(); placer.drains_ring(); placer.cracks()

    materials = dusty_materials(mesh_path, types)
    write_scene(SCENE, [], kept, head, scene_newline, FOLIAGE_NAME, types, asset_id, object_id, materials, previous_id=FOLIAGE_ID)
    write_palette(types, asset_id, object_id, materials)
    print(f'Placed {summary(types)}; replaced {len(blocks) - len(kept)} previous foliage/LeafSet entities.')


if __name__ == '__main__':
    main()
