"""Dress Standoff with LeafSet foliage. Run from any directory; deterministic, re-runnable (needs numpy).

Uses the LeafSet foliage types from foliage_kit.py on one instanced FoliageComponent. Placement is
driven by the village's real geometry: every level mesh the scene references is rasterised top-down
into a 25 cm surface map (ground material and height, low obstacles, roofs, tree canopies, inside or
outside the playable boundary). Plants then grow where they would in an abandoned village:

  * overgrowth and shrubs in the unreachable verges beyond the boundary walls, hedges and fences,
    with tall grass along the farm-field margins,
  * edge strips, weeds and the odd shrub along the inside of the boundary,
  * strips along wall, kerb and vehicle bases, with tufts in the corners where walls meet,
  * heavier overgrowth around the church rubble and the ruined shed,
  * shade plants under the trees, meadow weeds on the grass yards, sparse weeds on dirt,
  * and the odd weed in cracks in the asphalt, cobbles and concrete.

Nothing grows indoors, under the gas-station canopy, in doorways (strips must follow a wall for
their full length) or near spawns. In open playable space nothing is taller than about half a metre;
taller plants only hug walls or sit outside the boundary. Foliage has no collision, so movement,
sightlines past the plants and the baked navmesh are unaffected.

Outputs (replaced on re-run): the "Foliage (LeafSet)" entity in Standoff.plutoscene, and the shared
LeafSet palette prefab. Run Tools/build_standoff.py first if you regenerate the village; it rewrites
the scene.
"""
from pathlib import Path
import ast, math, random, re

import numpy as np

from foliage_kit import (A, ROOT, FLAT_ROLES, PLAY_HEIGHT_LIMIT, load_types, dusty_materials, read_plutomesh, read_text,
                         scene_blocks, summary, write_palette, write_scene)

SCENE = A / 'Scenes/Standoff.plutoscene'
FOLIAGE_NAME = 'Foliage (LeafSet)'
SEED = 20261017
CELL = .25                     # surface map resolution, metres
CELL_SIZE = 40.0               # FoliageComponent culling cells; Standoff is ~120 m across
BACKDROP = {'Mountains', 'Treeline', 'Distant houses', 'Water tower', 'Windmill'}  # far scenery
NO_COVER = {'Power lines', 'Gas bunting', 'Street lamps'}  # overhead strands are not roofs
RUINS = {'Church rubble', 'Ruined shed'}
CANOPY = {'Leaves', 'LeavesDark'}
SOFT = {'Grass', 'Dirt', 'Field', 'Hay'}
PAVED = {'Asphalt', 'Cobble', 'Concrete', 'ConcreteDark', 'Stone', 'StoneDark'}
LOW, HIGH = .12, 2.2           # geometry between these heights blocks the ground; above it is cover
WALL_BAND = (1, 3)             # cells from an obstacle that count as a wall base (0.25-0.75 m)

# ---------------------------------------------------------------- village layout

def boundary_polygon():
    """The playable outline traced in build_standoff.py, in metres (read, not imported)."""
    tree = ast.parse((ROOT / 'Tools/build_standoff.py').read_text(encoding='utf-8'))
    values = {}
    for node in tree.body:
        if isinstance(node, ast.Assign) and isinstance(node.targets[0], ast.Name) and node.targets[0].id in ('S', 'BOUNDARY'):
            values[node.targets[0].id] = ast.literal_eval(node.value)
    s = values['S']
    return [((px - 1020) * s, (py - 1020) * s) for px, py in values['BOUNDARY']]


def asset_paths():
    """Stable asset id -> project file, for the meshes the scene references."""
    paths = {}
    for meta in (A / 'Meshes').rglob('*.plutomesh.plutometa'):
        match = re.search(r'^ID\t(\w+)', meta.read_text(), re.M)
        if match:
            paths[match.group(1)] = Path(str(meta)[:-len('.plutometa')])
    return paths


def scene_meshes(blocks):
    """(entity name, mesh file) for root level meshes at the origin, skipping the far backdrop."""
    paths = asset_paths(); found = []
    for block in blocks:
        head = block.split('\n', 1)[0].split('\t')
        if head[2] != '0' or head[4] in BACKDROP:
            continue
        match = re.search(r'^PROPERTY\tMeshAssetReference\t2\tasset://(\w+)#0\t', block, re.M)
        if not match or match.group(1) not in paths:
            continue
        if any(abs(float(v)) > 1e-4 for v in (head[5] + ',' + head[6]).split(',')) or head[7] != '1.000000,1.000000,1.000000':
            print(f'Skipping {head[4]}: only level meshes at the origin are rasterised')
            continue
        found.append((head[4], paths[match.group(1)]))
    return found


def markers(blocks, prefix):
    out = []
    for block in blocks:
        head = block.split('\n', 1)[0].split('\t')
        if head[4].startswith(prefix):
            x, _, z = map(float, head[5].split(','))
            out.append((x, z))
    return out

# ---------------------------------------------------------------- surface map

class Surface:
    def __init__(self, outline, meshes):
        xs = [p[0] for p in outline]; zs = [p[1] for p in outline]
        self.x0, self.z0 = min(xs) - 8, min(zs) - 8
        self.nx = int((max(xs) + 8 - self.x0) / CELL) + 1; self.nz = int((max(zs) + 8 - self.z0) / CELL) + 1
        shape = (self.nz, self.nx)
        self.ground_y = np.full(shape, -np.inf, np.float32); self.ground = np.full(shape, -1, np.int16)
        self.low = np.zeros(shape, bool); self.roof = np.zeros(shape, bool); self.canopy = np.zeros(shape, bool)
        self.ruin = np.zeros(shape, bool); self.hedge = np.zeros(shape, bool)
        self.materials = []
        self._bary = {}
        for name, path in meshes:
            self.rasterise(name, path)
        self.derive(outline)

    def cell(self, x, z):
        return int((z - self.z0) / CELL), int((x - self.x0) / CELL)

    def barycentric(self, n):
        if n not in self._bary:
            i, j = np.meshgrid(np.arange(n + 1), np.arange(n + 1))
            keep = i + j <= n
            self._bary[n] = (i[keep] / n, j[keep] / n)
        return self._bary[n]

    def rasterise(self, name, path):
        positions, indices, submeshes, refs = read_plutomesh(path, with_materials=True)
        positions = np.asarray(positions, np.float64); indices = np.asarray(indices)
        for _, start, count, material in submeshes.values():
            mat = refs[material].rsplit('/', 1)[-1].replace('.plutomaterial', '') if material < len(refs) else ''
            if mat not in self.materials: self.materials.append(mat)
            mat_id = self.materials.index(mat)
            tris = positions[indices[start:start + count - count % 3]].reshape(-1, 3, 3)
            for a, b, c in tris:
                lo = np.minimum(np.minimum(a, b), c); hi = np.maximum(np.maximum(a, b), c)
                if hi[0] < self.x0 or hi[2] < self.z0 or lo[0] > self.x0 + self.nx * CELL or lo[2] > self.z0 + self.nz * CELL:
                    continue
                normal = np.cross(b - a, c - a); length = np.linalg.norm(normal)
                if length < 1e-9: continue
                up = abs(normal[1]) / length
                n = max(1, int(math.ceil(max(np.linalg.norm(b - a), np.linalg.norm(c - a), np.linalg.norm(c - b)) / (CELL * .5))))
                u, v = self.barycentric(n)
                pts = a + np.outer(u, b - a) + np.outer(v, c - a)
                iz = ((pts[:, 2] - self.z0) / CELL).astype(int); ix = ((pts[:, 0] - self.x0) / CELL).astype(int)
                ok = (iz >= 0) & (iz < self.nz) & (ix >= 0) & (ix < self.nx)
                iz, ix, y = iz[ok], ix[ok], pts[ok, 1]
                ground = (up > .7) & (y < LOW + .13)
                if ground.any():
                    gz, gx, gy = iz[ground], ix[ground], y[ground]
                    higher = gy > self.ground_y[gz, gx]
                    self.ground_y[gz[higher], gx[higher]] = gy[higher]; self.ground[gz[higher], gx[higher]] = mat_id
                block = ~ground & (y >= LOW) & (y < HIGH)
                if mat in CANOPY: block &= False
                self.low[iz[block], ix[block]] = True
                if name in RUINS: self.ruin[iz[block], ix[block]] = True
                if mat == 'Hedge': self.hedge[iz[block], ix[block]] = True
                over = y >= HIGH
                if mat in CANOPY: self.canopy[iz[over], ix[over]] = True
                elif name not in NO_COVER: self.roof[iz[over], ix[over]] = True

    def dilate(self, mask, steps):
        out = mask.copy()
        for _ in range(steps):
            grown = out.copy()
            grown[1:] |= out[:-1]; grown[:-1] |= out[1:]; grown[:, 1:] |= out[:, :-1]; grown[:, :-1] |= out[:, 1:]
            out = grown
        return out

    def distance(self, mask, limit):
        """Cells (4-connected steps) to the nearest True cell, capped at limit."""
        dist = np.full(mask.shape, limit, np.int16); front = mask.copy(); dist[front] = 0
        for step in range(1, limit):
            grown = self.dilate(front, 1); new = grown & (dist == limit); dist[new] = step; front = grown
        return dist

    def derive(self, outline):
        zc, xc = np.mgrid[0:self.nz, 0:self.nx]
        self.xs = self.x0 + (xc + .5) * CELL; self.zs = self.z0 + (zc + .5) * CELL
        # Even-odd test against the traced boundary, and distance to it.
        inside = np.zeros(self.xs.shape, bool); edge = np.full(self.xs.shape, np.inf)
        for (ax, az), (bx, bz) in zip(outline, outline[1:] + outline[:1]):
            crosses = ((az > self.zs) != (bz > self.zs)) & (self.xs < (bx - ax) * (self.zs - az) / (bz - az + 1e-12) + ax)
            inside ^= crosses
            dx, dz = bx - ax, bz - az
            t = np.clip(((self.xs - ax) * dx + (self.zs - az) * dz) / (dx * dx + dz * dz), 0, 1)
            edge = np.minimum(edge, np.hypot(self.xs - (ax + t * dx), self.zs - (az + t * dz)))
        self.inside, self.edge = inside, edge
        # Under a roof but well inside its footprint is indoors; the eaves band outside walls is not.
        self.indoor = self.roof & ~self.dilate(~self.roof, 3)
        self.wall = self.distance(self.low, 8)
        names = np.array(self.materials + [''])
        self.ground_name = names[self.ground]
        self.free = np.isin(self.ground_name, list(SOFT | PAVED)) & ~self.low & ~self.indoor & (self.ground_y < .2)
        gz, gx = np.gradient(self.wall.astype(np.float32))
        self.normal_x, self.normal_z = gx, gz  # points away from the nearest obstacle
        # Concave corners: obstacles on two perpendicular sides within half a metre.
        near = self.low
        sides = [np.roll(near, k, axis) for axis in (0, 1) for k in (2, -2)]
        self.corner = (sides[0] | sides[1]) & (sides[2] | sides[3]) & (self.wall <= 2)

    def at(self, x, z):
        iz, ix = self.cell(x, z)
        if 0 <= iz < self.nz and 0 <= ix < self.nx: return iz, ix
        return None

    def mask_cells(self, mask):
        iz, ix = np.nonzero(mask)
        return list(zip(iz.tolist(), ix.tolist()))

# ---------------------------------------------------------------- placement

class Placer:
    def __init__(self, types, surface, spawns, hardpoints):
        self.rng = random.Random(SEED)
        self.types, self.s = types, surface
        self.spawns, self.hardpoints = spawns, hardpoints
        self.grid = {}  # 2 m buckets of (x, z, r, role)

    def pick(self, roles, tags=None):
        options = [t for t in self.types if t.role in roles and (not tags or t.tags & set(tags))]
        return self.rng.choice(options)

    def neighbours(self, x, z):
        bx, bz = int(math.floor(x / 2)), int(math.floor(z / 2))
        for i in (-2, -1, 0, 1, 2):
            for j in (-2, -1, 0, 1, 2):
                yield from self.grid.get((bx + i, bz + j), ())

    def clear(self, x, z, reach):
        """Ground is free here and around a footprint of `reach` metres."""
        for dx, dz in ((0, 0), (reach, 0), (-reach, 0), (0, reach), (0, -reach)):
            c = self.s.at(x + dx, z + dz)
            if c is None or not self.s.free[c]: return False
        return True

    def fits(self, t, x, z, scale, *, max_height, overlap=.6, yaw=None):
        r = t.radius * scale
        if t.height * scale > max_height: return False
        c = self.s.at(x, z)
        if c is None or not self.s.free[c]: return False
        if t.role == 'strip':  # follow the wall base for the full length, so doorways stay clear
            a = math.radians(yaw); ux, uz = math.cos(a), -math.sin(a); half = t.half_length(scale)
            for k in (-1, -.5, .5, 1):
                cc = self.s.at(x + ux * half * k, z + uz * half * k)
                if cc is None or not self.s.free[cc] or not (WALL_BAND[0] <= self.s.wall[cc] <= WALL_BAND[1] + 1): return False
        elif not self.clear(x, z, r * (.25 if t.role in FLAT_ROLES else .4)):
            return False
        if any(math.dist((x, z), p) < 2.5 + r for p in self.spawns): return False
        if t.height * scale > PLAY_HEIGHT_LIMIT and self.s.inside[c] and any(math.dist((x, z), p) < 3.5 + r for p in self.hardpoints):
            return False
        for px, pz, pr, role in self.neighbours(x, z):
            limit = (r + pr) * (overlap if t.role not in FLAT_ROLES and role not in FLAT_ROLES else .35)
            if math.dist((x, z), (px, pz)) < limit: return False
        return True

    def add(self, t, x, z, scale, yaw=None, **checks):
        yaw = self.rng.uniform(0, 360) if yaw is None else yaw
        if not self.fits(t, x, z, scale, yaw=yaw, **checks): return False
        y = float(self.s.ground_y[self.s.at(x, z)]) + t.origin_height(scale)
        t.instances.append((x, y, z, yaw, scale))
        self.grid.setdefault((int(math.floor(x / 2)), int(math.floor(z / 2))), []).append((x, z, t.radius * scale, t.role))
        return True

    def try_add(self, roles, x, z, *, tags=None, scale=None, tries=4, **checks):
        for _ in range(tries):
            t = self.pick(roles, tags)
            s = scale or self.rng.uniform(*SCALE[t.role])
            if self.add(t, x, z, s, **checks): return True
        return False

    def scatter(self, mask, count, place):
        """Visit `count` random cells of `mask` (jittered within the cell) and try to place there."""
        cells = self.s.mask_cells(mask)
        self.rng.shuffle(cells)
        placed = 0
        for iz, ix in cells[:count]:
            x = self.s.x0 + (ix + self.rng.random()) * CELL; z = self.s.z0 + (iz + self.rng.random()) * CELL
            placed += bool(place(x, z, (iz, ix)))
        return placed

    def wall_yaw(self, cell):
        """Yaw that lays a strip's long axis along the nearest wall."""
        nx, nz = float(self.s.normal_x[cell]), float(self.s.normal_z[cell])
        if abs(nx) + abs(nz) < 1e-6: return self.rng.uniform(0, 360)
        tx, tz = -nz, nx
        return math.degrees(math.atan2(-tz, tx)) + self.rng.choice([0, 180]) + self.rng.uniform(-4, 4)

    def colony(self, x, z, roles):
        """Species form colonies a few metres across instead of mixing at random."""
        species = ['Tall Grass', 'Nettle', 'Brush', 'Clover', 'Dock', 'Bramble', 'Creeper', 'Broadleaf']
        k = (int(math.floor(x / 7.0)) * 7919 + int(math.floor(z / 7.0)) * 104729) % len(species)
        options = [t for t in self.types if t.role in roles and species[k] in t.name]
        return self.rng.choice(options) if options and self.rng.random() < .75 else self.pick(roles)

    # --- zones -------------------------------------------------------------------------

    def verges(self):
        """Beyond the boundary: unmown verges, densest against the walls, hedges and fences."""
        s = self.s
        out = ~s.inside & s.free
        near = out & (s.edge > .7) & (s.edge < 2.4) & (s.ground_name != 'Field')
        behind = out & (s.edge >= 2.4) & (s.edge < 3.8) & (s.ground_name != 'Field')
        field_margin = out & (s.ground_name == 'Field') & self.s.dilate(s.ground_name != 'Field', 4) & (s.edge < 12)

        def patch(x, z, c, scale):
            t = self.colony(x, z, {'patch'})
            return self.add(t, x, z, self.rng.uniform(*scale), max_height=9, overlap=.36)
        # A continuous band against the boundary: mowing stops a couple of metres short of it.
        self.scatter(near, 3200, lambda x, z, c: patch(x, z, c, (.55, .85)) if self.rng.random() < .85
                     else self.try_add({'shrub', 'weed'}, x, z, max_height=9, overlap=.45))

        def clump(x, z, c):  # the band frays into the odd clump, never loose specks on the lawn
            if not patch(x, z, c, (.5, .7)): return False
            for _ in range(self.rng.randint(1, 3)):
                a = self.rng.uniform(0, math.tau); d = self.rng.uniform(.5, 1.0)
                self.try_add({'weed', 'shrub'}, x + d * math.cos(a), z + d * math.sin(a), tries=2, max_height=9, overlap=.45)
            return True
        self.scatter(behind, 160, clump)
        tall = [t for t in self.types if t.role == 'patch' and ('Tall Grass' in t.name or 'Nettle' in t.name)]
        self.scatter(field_margin, 700, lambda x, z, c: self.add(self.rng.choice(tall), x, z, self.rng.uniform(.6, .9),
                                                                   max_height=9, overlap=.4))

    def boundary_inside(self):
        """Inside the boundary walls: litter strips, weeds and the odd shrub that nobody clears."""
        s = self.s
        band = s.inside & s.free & (s.edge < 1.3) & (s.wall >= WALL_BAND[0]) & (s.wall <= WALL_BAND[1])

        def place(x, z, c):
            roll = self.rng.random()
            if roll < .4:
                return self.add(self.pick({'strip'}), x, z, self.rng.uniform(.8, 1.05), yaw=self.wall_yaw(c), max_height=.2)
            if roll < .85:
                return self.try_add({'weed'}, x, z, max_height=.6, overlap=.5)
            return self.try_add({'shrub'}, x, z, scale=self.rng.uniform(.55, .72), max_height=1.0, overlap=.5)
        self.scatter(band, 1400, place)

    def wall_bases(self):
        """Along buildings, low walls, kerbs and abandoned vehicles; tufts collect in the corners."""
        s = self.s
        band = s.inside & s.free & (s.edge >= 1.3) & (s.wall >= WALL_BAND[0]) & (s.wall <= WALL_BAND[1])
        shaded = s.canopy | s.dilate(s.roof, 2)

        def place(x, z, c):
            tags = ['shade', 'wet'] if shaded[c] else None
            if s.corner[c] and self.rng.random() < .7:
                if self.rng.random() < .2:
                    return self.try_add({'shrub'}, x, z, tags=tags, scale=self.rng.uniform(.55, .7), max_height=1.0, overlap=.5)
                return self.try_add({'weed'}, x, z, tags=tags, max_height=.6, overlap=.45)
            if self.rng.random() < .55:
                return self.add(self.pick({'strip'}, tags), x, z, self.rng.uniform(.8, 1.05), yaw=self.wall_yaw(c), max_height=.2)
            return self.try_add({'weed'}, x, z, tags=tags, max_height=.6, overlap=.5)
        self.scatter(band, 3200, place)

    def ruins(self):
        """Rubble and the roofless shed have been left to nature the longest."""
        s = self.s
        area = s.inside & s.free & s.dilate(s.ruin, 10) & (s.wall >= 1)

        def place(x, z, c):
            roll = self.rng.random()
            if roll < .3:
                t = self.rng.choice([t for t in self.types if t.role == 'patch' and t.height < 1.05])
                return self.add(t, x, z, self.rng.uniform(.45, .6), max_height=.65, overlap=.45)
            if roll < .55:
                return self.try_add({'shrub'}, x, z, scale=self.rng.uniform(.55, .7), max_height=.95, overlap=.5)
            return self.try_add({'weed', 'mat'}, x, z, tags=['shade', 'sun'], max_height=.6, overlap=.45)
        self.scatter(area, 900, place)

    def under_trees(self):
        """Shade and leaf litter keep the ground under the trees soft and green."""
        s = self.s
        shade = s.canopy & s.free & (s.wall >= 1)
        self.scatter(shade, 1600, lambda x, z, c: self.try_add({'weed', 'mat'}, x, z, tags=['shade'],
                                                               max_height=.55 if s.inside[c] else 9, overlap=.5))

    def open_ground(self):
        """Meadow weeds on the grass yards, sparse rosettes on dirt, the odd weed in paving cracks."""
        s = self.s
        open_inside = s.inside & s.free & (s.wall > WALL_BAND[1]) & (s.edge >= 1.3) & ~s.canopy
        grass = open_inside & (s.ground_name == 'Grass')
        dirt = open_inside & (s.ground_name == 'Dirt')
        paved = open_inside & np.isin(s.ground_name, list(PAVED))
        self.scatter(grass, int(grass.sum() * CELL * CELL * .22),
                     lambda x, z, c: self.try_add({'weed', 'mat'}, x, z, max_height=.5, overlap=.55))
        self.scatter(dirt, int(dirt.sum() * CELL * CELL * .06),
                     lambda x, z, c: self.try_add({'weed'}, x, z, tags=['dry', 'crack'], max_height=.45, overlap=.6))

        def crack(x, z, c):
            if not self.try_add({'weed'}, x, z, tags=['crack'], scale=self.rng.uniform(.6, .95), max_height=.4, overlap=.6):
                return False
            heading = self.rng.uniform(0, math.tau)
            for step in range(self.rng.randint(0, 2)):  # often two or three along the same crack
                d = .35 + .3 * step
                self.try_add({'weed'}, x + d * math.cos(heading), z + d * math.sin(heading), tags=['crack'],
                             scale=self.rng.uniform(.5, .8), tries=2, max_height=.4, overlap=.3)
            return True
        self.scatter(paved, int(paved.sum() * CELL * CELL * .012), crack)


SCALE = {'patch': (.5, .78), 'shrub': (.62, .9), 'weed': (.75, 1.15), 'mat': (.65, 1.0), 'strip': (.8, 1.05)}


def main():
    types, asset_id, object_id, mesh_path = load_types()
    scene_text, scene_newline = read_text(SCENE)
    head, blocks = scene_blocks(scene_text)
    previous = [b for b in blocks if b.split('\t')[2] == '0' and b.split('\t')[4] == FOLIAGE_NAME]
    kept = [b for b in blocks if b not in previous]
    previous_id = int(previous[0].split('\t')[1]) if previous else None

    surface = Surface(boundary_polygon(), scene_meshes(kept))
    placer = Placer(types, surface, markers(kept, 'Spawn ') + markers(kept, 'Player'), markers(kept, 'Hardpoint '))
    placer.verges(); placer.boundary_inside(); placer.ruins(); placer.wall_bases(); placer.under_trees(); placer.open_ground()

    materials = dusty_materials(mesh_path, types)
    entity_id = write_scene(SCENE, previous, kept, head, scene_newline, FOLIAGE_NAME, types, asset_id, object_id, materials,
                            previous_id=previous_id, cell_size=CELL_SIZE)
    write_palette(types, asset_id, object_id, materials)
    used = [t for t in types if t.instances]
    cells = sum(len({(math.floor(x / CELL_SIZE), math.floor(z / CELL_SIZE)) for x, _, z, _, _ in t.instances}) * len(t.parts)
                for t in used)
    print(f'Placed {summary(types)} as entity {entity_id}; about {cells} instanced draws before culling.')


if __name__ == '__main__':
    main()
