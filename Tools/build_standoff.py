"""Generate a Standoff-inspired Hardpoint village with native LPGM geometry.

Reference: user-supplied Black Ops II "Standoff" screenshots, aerial view and minimap. The plan is
traced from the 2048x2048 minimap in pixel coordinates and converted with S metres per pixel (scale
from the minimap's tank outline); north (top of the minimap) is -Z. All geometry, textures and audio are generated here
procedurally; nothing is copied from the original game.
Run: python Tools/build_standoff.py   (needs Pillow + numpy). Rebuilds Standoff assets only.
"""
from pathlib import Path
from collections import defaultdict
import io, math, random, re, struct, uuid, wave
import numpy as np
from PIL import Image
import standoff_textures as tex

ROOT = Path(__file__).resolve().parents[1]; A = ROOT / 'Assets'; generated = []
S = .08
def P(px, py, y=0.0): return ((px - 1020) * S, y, (py - 1020) * S)
def R(px0, py0, px1, py1):
    """Pixel rectangle -> (centre x, centre z, width x, depth z)."""
    x0, _, z0 = P(px0, py0); x1, _, z1 = P(px1, py1)
    return ((x0 + x1) / 2, (z0 + z1) / 2, abs(x1 - x0), abs(z1 - z0))

def save(rel, data):
    p = A / rel; p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(data if isinstance(data, bytes) else data.encode())
    Path(str(p) + '.plutometa').write_text('PLUTOASSET\t1\nID\t' + uuid.uuid5(uuid.NAMESPACE_URL, 'cod/standoff/' + rel).hex + '\nIMPORTER_VERSION\t1\n')
    generated.append(p)
def vec(v): return ','.join(f'{x:.5f}' for x in v)
def comp(i, n, ps): return f'COMPONENT\t{i}\t{n}\t1\n' + ''.join(f'PROPERTY\t{k}\t{t}\t{v}\t0\n' for k, t, v in ps) + 'END_COMPONENT\n'

# ------------------------------------------------------------------ materials
# name: (roughness, metallic, metres per texture repeat or None for 0..1 decal UVs, extra)
MATS = {
    'Asphalt': (.92, 0, 7), 'Cobble': (.85, 0, 2.2), 'Concrete': (.9, 0, 4), 'ConcreteDark': (.92, 0, 4), 'Grass': (.95, 0, 5),
    'Dirt': (.95, 0, 5), 'Field': (.95, 0, 9), 'Stone': (.9, 0, 3), 'StoneDark': (.9, 0, 3), 'Brick': (.88, 0, 2.6),
    'PlasterCream': (.92, 0, 4), 'PlasterYellow': (.92, 0, 4), 'PlasterWhite': (.92, 0, 4), 'PlasterGrey': (.92, 0, 4),
    'PlasterOchre': (.92, 0, 4), 'InteriorWall': (.9, 0, 3), 'Slate': (.75, 0, 2.5), 'Terracotta': (.8, 0, 2.5),
    'Corrugated': (.6, .55, 3), 'CorrugatedBlue': (.55, .5, 3), 'CanopyMetal': (.45, .6, 3), 'Planks': (.9, 0, 2.4),
    'PlanksDark': (.9, 0, 2.4), 'PicketWhite': (.85, 0, 1.6), 'Hay': (.95, 0, 1.4), 'VanBlue': (.4, .3, 3),
    'VanDark': (.4, .3, 3), 'SedanBrown': (.45, .3, 3), 'TankGreen': (.7, .3, 3), 'TractorRed': (.5, .2, 3),
    'ColumnBlue': (.45, .4, 3), 'Rubber': (.9, 0, 2), 'Steel': (.45, .8, 2), 'DarkMetal': (.5, .7, 2), 'Glass': (.08, .2, 3),
    'YellowPaint': (.7, 0, 2), 'DumpsterGreen': (.6, .3, 2), 'WhitePaint': (.6, 0, 2), 'RedPaint': (.6, 0, 2),
    'Leaves': (.9, 0, 3), 'LeavesDark': (.9, 0, 3), 'Hedge': (.95, 0, 2), 'Bark': (.95, 0, 2), 'Forest': (1, 0, 30), 'ForestFar': (1, 0, 60),
    'StoreFloor': (.55, 0, 2.4), 'Ceiling': (.9, 0, 2.4),
}
DECALS = {
    'WindowShutter': (.4, 0), 'WindowGreen': (.4, 0), 'WindowPlain': (.4, 0), 'WindowBlue': (.4, 0), 'WindowBarred': (.4, 0),
    'WindowBroken': (.4, 0), 'DoorWood': (.8, 0), 'DoorBlue': (.6, 0), 'DoorWhite': (.6, 0), 'DoorGreen': (.7, 0),
    'Garage': (.6, .4), 'Fascia': (.6, 0), 'LogoU': (.4, 0), 'SignBakery': (.7, 0), 'SignMarket': (.7, 0), 'SignBrick': (.7, 0),
    'SignCafe': (.7, 0), 'PosterA': (.8, 0), 'PosterB': (.8, 0), 'PosterC': (.8, 0), 'LetterH': (.6, 0), 'LetterO': (.6, 0),
    'LetterT': (.6, 0), 'LetterE': (.6, 0), 'LetterL': (.6, 0), 'Fridge': (.2, 0), 'ShelfGoods': (.8, 0), 'LightPanel': (.5, 0),
    'Cardboard': (.9, 0), 'Flags': (.8, 0), 'Embers': (.9, 0),
}
EMISSION = {'Fridge': '2.5,2.6,2.6', 'LightPanel': '9,9,8.5', 'LogoU': '1,1,1', 'Embers': '14,9,6'}

def build_materials():
    for name in list(MATS) + list(DECALS):
        albedo, height, emission = tex.GENERATORS[name](name)
        def png(arr):
            b = io.BytesIO(); Image.fromarray(arr).save(b, format='PNG'); return b.getvalue()
        save(f'Textures/Standoff/{name}.png', png(albedo))
        rough, metal = (MATS[name][:2] if name in MATS else DECALS[name])
        lines = ['Color=1,1,1,1', 'SurfaceType=Standard', 'AlphaMode=Opaque', 'CastsShadow=true', 'TwoSided=true',
                 f'Metallic={metal}', f'Roughness={rough}', f'AlbedoTexture=project://Textures/Standoff/{name}.png']
        if height is not None:
            save(f'Textures/Standoff/{name}_N.png', png(tex.normal_map(height, 3.0)))
            lines.append(f'NormalTexture=project://Textures/Standoff/{name}_N.png')
        if emission is not None:
            save(f'Textures/Standoff/{name}_E.png', png(emission))
            lines += [f'EmissionTexture=project://Textures/Standoff/{name}_E.png', f'Emission={EMISSION.get(name, "1,1,1")}']
        lines.append('ShaderGraph=engine://builtin/shadergraph/default-lit')
        save(f'Materials/Standoff/{name}.plutomaterial', '\n'.join(lines) + '\n')
    # See-through storefront glazing.
    save('Materials/Standoff/WindowGlass.plutomaterial', 'Color=0.55,0.65,0.7,0.22\nSurfaceType=Standard\nAlphaMode=Blend\nCastsShadow=false\nTwoSided=true\nMetallic=0.1\nRoughness=0.05\nShaderGraph=engine://builtin/shadergraph/default-lit\n')

# ------------------------------------------------------------------ geometry core
meshes = defaultdict(lambda: defaultdict(list)); colliders = 0
nextid = 120000; scene = ''

def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def norm(a):
    l = math.sqrt(dot(a, a)); return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-9 else (0, 1, 0)
def yawrot(p, yaw):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); return (p[0] * c + p[2] * s, p[1], -p[0] * s + p[2] * c)
def face_dir(yaw): return (math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw)))

def project_uv(m, p, n):
    scale = MATS[m][2] if m in MATS else 1
    ax = max(range(3), key=lambda k: abs(n[k]))
    if ax == 1: return (p[0] / scale, p[2] / scale)
    if ax == 0: return ((-p[2] if n[0] > 0 else p[2]) / scale, -p[1] / scale)
    return ((p[0] if n[2] > 0 else -p[0]) / scale, -p[1] / scale)

def poly(g, m, pts, uvs=None, face=None):
    """Planar convex polygon; `face` is the intended outward normal (winding is fixed to match)."""
    n = (0.0, 0.0, 0.0)
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n = (n[0] + (a[1] - b[1]) * (a[2] + b[2]), n[1] + (a[2] - b[2]) * (a[0] + b[0]), n[2] + (a[0] - b[0]) * (a[1] + b[1]))
    if dot(n, n) < 1e-12: return
    n = norm(n)
    if face is not None and dot(n, face) < 0:
        pts = pts[::-1]; uvs = uvs[::-1] if uvs else None; n = mul(n, -1)
    if uvs is None:
        uvs = [project_uv(m, p, n) for p in pts] if m in MATS else [(0, 0), (1, 0), (1, 1), (0, 1)][:len(pts)]
    # Per-polygon tangent from the first triangle's UV gradient.
    e1, e2 = sub(pts[1], pts[0]), sub(pts[2], pts[0])
    du1, dv1 = uvs[1][0] - uvs[0][0], uvs[1][1] - uvs[0][1]
    du2, dv2 = uvs[2][0] - uvs[0][0], uvs[2][1] - uvs[0][1]
    det = du1 * dv2 - du2 * dv1
    if abs(det) < 1e-10: t, w = norm(cross((0, 1, 0), n) if abs(n[1]) < .9 else (1, 0, 0)), 1.0
    else:
        r = 1 / det
        t = mul(sub(mul(e1, dv2), mul(e2, dv1)), r); b = mul(sub(mul(e2, du1), mul(e1, du2)), r)
        t = norm(sub(t, mul(n, dot(n, t)))); w = -1.0 if dot(cross(n, t), b) < 0 else 1.0
    out = meshes[g][m]
    for j in range(1, len(pts) - 1):
        for k in (0, j, j + 1):
            out.append((*pts[k], *n, *uvs[k], *t, w, 0., 0., -1, -1, -1, -1, 0., 0., 0., 0.))

def collider(pos, size, yaw=0, name='Collision'):
    global colliders
    i = entity(name, pos, (0, yaw, 0), size)
    add_scene(comp(i, 'ColliderComponent', [('Shape', 6, '0\t5\tBox\tSphere\tCapsule\tTerrain\tMesh'), ('Size', 3, '1,1,1'), ('Is Trigger', 4, 'false'), ('Blocks Audio', 4, 'true')]))
    colliders += 1

BOX_FACES = [((1, 0, 0), (0, 0, 1), (0, 1, 0)), ((-1, 0, 0), (0, 0, -1), (0, 1, 0)), ((0, 1, 0), (1, 0, 0), (0, 0, -1)),
             ((0, -1, 0), (1, 0, 0), (0, 0, 1)), ((0, 0, 1), (-1, 0, 0), (0, 1, 0)), ((0, 0, -1), (1, 0, 0), (0, 1, 0))]
def box(g, m, pos, size, yaw=0, solid=False, render=True, bottom=True, top=True):
    if solid: collider(pos, size, yaw, g + ' collision')
    if not render: return
    for n, u, v in BOX_FACES:
        if (n[1] < 0 and not bottom) or (n[1] > 0 and not top): continue
        pts = []
        for a, b in ((-1, 1), (1, 1), (1, -1), (-1, -1)):
            local = tuple((n[k] + a * u[k] + b * v[k]) * size[k] / 2 for k in range(3))
            pts.append(add(pos, yawrot(local, yaw)))
        poly(g, m, pts, None if m in MATS else [(0, 0), (1, 0), (1, 1), (0, 1)], yawrot(n, yaw))

def decal(g, m, center, yaw, w, h, off=.04):
    """Front-facing quad (normal = face_dir(yaw)) with 0..1 UVs, v=0 at the top."""
    n = face_dir(yaw); r = (math.cos(math.radians(yaw)), 0, -math.sin(math.radians(yaw))); c = add(center, mul(n, off))
    tl = add(sub(c, mul(r, w / 2)), (0, h / 2, 0)); tr = add(add(c, mul(r, w / 2)), (0, h / 2, 0))
    br = add(add(c, mul(r, w / 2)), (0, -h / 2, 0)); bl = add(sub(c, mul(r, w / 2)), (0, -h / 2, 0))
    poly(g, m, [tl, tr, br, bl], [(0, 0), (1, 0), (1, 1), (0, 1)], n)

def basis(d):
    d = norm(d); helper = (0, 1, 0) if abs(d[1]) < .9 else (1, 0, 0)
    u = norm(cross(d, helper)); return d, u, cross(u, d)

def cylinder(g, m, a, b, r, seg=12, caps=True, r2=None):
    d, u, v = basis(sub(b, a)); r2 = r if r2 is None else r2
    ring = lambda c, rr, k: add(c, add(mul(u, math.cos(k * math.tau / seg) * rr), mul(v, math.sin(k * math.tau / seg) * rr)))
    length = math.dist(a, b); circ = math.tau * r
    for k in range(seg):
        p = [ring(a, r, k), ring(a, r, k + 1), ring(b, r2, k + 1), ring(b, r2, k)]
        mid = norm(add(sub(p[0], a), sub(p[1], a)))
        uvs = [(k / seg * circ / 2, 0), ((k + 1) / seg * circ / 2, 0), ((k + 1) / seg * circ / 2, -length / 2), (k / seg * circ / 2, -length / 2)]
        poly(g, m, p, uvs if m in MATS else None, mid)
    if caps:
        for c, rr, f in ((a, r, mul(d, -1)), (b, r2, d)):
            if rr > 1e-4: poly(g, m, [ring(c, rr, k) for k in range(seg)], None, f)

def blob(g, m, c, radii, seed, rough=.25, seg=9, rings=6):
    rnd = random.Random(seed)
    jitter = {(i, k): 1 + rnd.uniform(-rough, rough) for i in range(rings + 1) for k in range(seg)}
    def pt(i, k):
        th = math.pi * i / rings; ph = math.tau * (k % seg) / seg; j = jitter[(i, k % seg)] if 0 < i < rings else 1
        return (c[0] + radii[0] * j * math.sin(th) * math.cos(ph), c[1] + radii[1] * j * math.cos(th), c[2] + radii[2] * j * math.sin(th) * math.sin(ph))
    for i in range(rings):
        for k in range(seg):
            q = [pt(i, k), pt(i, k + 1), pt(i + 1, k + 1), pt(i + 1, k)]
            centre = mul(add(q[0], q[2]), .5)
            if i == 0: poly(g, m, [q[0], q[2], q[3]], None, sub(centre, c))
            elif i == rings - 1: poly(g, m, [q[0], q[1], q[2]], None, sub(centre, c))
            else: poly(g, m, q, None, sub(centre, c))

def beam(g, m, a, b, w, h=None):
    d, u, v = basis(sub(b, a)); h = w if h is None else h
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    ring = lambda c: [add(c, add(mul(u, x * w / 2), mul(v, y * h / 2))) for x, y in corners]
    ra, rb = ring(a), ring(b)
    for k in range(4):
        q = [ra[k], ra[(k + 1) % 4], rb[(k + 1) % 4], rb[k]]
        poly(g, m, q, None, sub(mul(add(q[0], q[1]), .5), a))

# ------------------------------------------------------------------ scene helpers
def add_scene(text):
    global scene; scene += text
def entity(name, pos=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), parent=0):
    global nextid
    i = nextid; nextid += 1; add_scene(f'ENTITY\t{i}\t{parent}\t1\t{name}\t{vec(pos)}\t{vec(rot)}\t{vec(scale)}\n'); return i

def point_light(name, pos, color, intensity, rng, shadows=False):
    i = entity(name, pos)
    add_scene(comp(i, 'LightComponent', [('LightType', 6, '0\t3\tPoint\tDirectional\tSpot'), ('Color', 3, vec(color)), ('Intensity', 0, intensity), ('Range', 0, rng), ('CastsShadows', 4, str(shadows).lower())]))

def particles(name, pos, asset):
    i = entity(name, pos)
    add_scene(comp(i, 'ParticleSystemComponent', [('ParticleSystemAsset', 2, 'project://Particles/Standoff/' + asset + '.plutoparticles')]))

def sound(name, pos, clip, volume, max_distance, spatial=True, min_distance=2):
    i = entity(name, pos)
    add_scene(comp(i, 'SoundEmitterComponent', [('Clip', 2, 'project://Sounds/Ambient/' + clip), ('PlayOnAwake', 4, 'true'), ('Looping', 4, 'true'),
        ('Spatialized', 4, str(spatial).lower()), ('Volume', 0, volume), ('Pitch', 0, 1), ('MinDistance', 0, min_distance), ('MaxDistance', 0, max_distance),
        ('Rolloff', 0, 1), ('OcclusionStrength', 0, 1), ('AirAbsorptionStrength', 0, 1), ('LowPassStrength', 0, 0)]))

AUDIO_PRESETS = {'Custom': 0, 'Cave': 1, 'Forest': 2, 'City': 3, 'Field': 4, 'Room': 5, 'Hall': 6, 'Underwater': 7}
def audio_volume(name, centre, size, preset, priority, wet, decay, density=.35, diffusion=.6, echo=0, delay=.12, feedback=.15, lowpass=0, blend=2):
    i = entity(name, centre)
    add_scene(comp(i, 'AudioEnvironmentVolumeComponent', [
        ('Preset', 6, f'{AUDIO_PRESETS[preset]}\t8\tCustom\tCave\tForest\tCity\tField\tRoom\tHall\tUnderwater'), ('Shape', 6, '0\t2\tBox\tSphere'),
        ('Size', 3, vec(size)), ('Radius', 0, 5), ('Blend Distance', 0, blend), ('Priority', 1, priority), ('Reverb Wet', 0, wet), ('Reverb Decay', 0, decay),
        ('Reverb Density', 0, density), ('Reverb Diffusion', 0, diffusion), ('Echo Wet', 0, echo), ('Echo Delay', 0, delay), ('Echo Feedback', 0, feedback),
        ('Low Pass', 0, lowpass), ('Gain', 0, 1)]))

def ambient_volume(name, centre, size, spacing):
    i = entity(name, centre)
    add_scene(comp(i, 'AmbientLightVolumeComponent', [('Size', 3, vec(size)), ('Probe Spacing', 0, spacing), ('Surface Clearance', 0, .02),
        ('Probe Budget', 1, 4096), ('Direction Count', 1, 128)]))

# ------------------------------------------------------------------ architecture
FLOOR = 3.2
def gable_roof(g, m, cx, cz, w, d, y0, rise, yaw=0, axis='x', over=.45, gable=None, thick=.12):
    """Ridge parallel to local x (axis 'x') or z. `gable` material fills the end triangles."""
    if axis == 'z':
        w, d = d, w; yaw += 90
    L = lambda x, y, z: add((cx, 0, cz), yawrot((x, y, z), yaw))
    hx, hz = w / 2 + over, d / 2 + over
    eave = y0 - over * rise / (d / 2)
    slope_len = math.hypot(hz, rise + (y0 - eave))
    for side in (-1, 1):
        q = [L(-hx, eave, side * hz), L(hx, eave, side * hz), L(hx, y0 + rise, 0), L(-hx, y0 + rise, 0)]
        uvs = [(-hx / 2.5 * side, slope_len / 2.5), (hx / 2.5 * side, slope_len / 2.5), (hx / 2.5 * side, 0), (-hx / 2.5 * side, 0)]
        poly(g, m, q, uvs, yawrot((0, 1, side * rise / (d / 2)), yaw))
        # Fascia board along the eave gives the roof visible thickness.
        poly(g, 'PlanksDark', [L(-hx, eave, side * hz), L(hx, eave, side * hz), L(hx, eave - .22, side * hz), L(-hx, eave - .22, side * hz)], None, yawrot((0, 0, side), yaw))
    for end in (-1, 1):
        poly(g, gable or 'PlasterWhite', [L(end * w / 2, y0, -d / 2), L(end * w / 2, y0, d / 2), L(end * w / 2, y0 + rise, 0)], None, yawrot((end, 0, 0), yaw))
        poly(g, 'PlanksDark', [L(end * hx, eave, -hz), L(end * hx, y0 + rise + .05, 0), L(end * hx, y0 + rise - .2, 0), L(end * hx, eave - .2, -hz)], None, yawrot((end, 0, 0), yaw))
        poly(g, 'PlanksDark', [L(end * hx, eave, hz), L(end * hx, y0 + rise + .05, 0), L(end * hx, y0 + rise - .2, 0), L(end * hx, eave - .2, hz)], None, yawrot((end, 0, 0), yaw))
    beam(g, 'PlanksDark', L(-hx, y0 + rise + .02, 0), L(hx, y0 + rise + .02, 0), .22, .16)

def hip_roof(g, m, cx, cz, w, d, y0, rise, yaw=0, over=.35):
    L = lambda x, y, z: add((cx, 0, cz), yawrot((x, y, z), yaw))
    hx, hz = w / 2 + over, d / 2 + over; top = y0 + rise
    ridge = max(0, (w - d) / 2)
    a, b = L(-ridge, top, 0), L(ridge, top, 0)
    c = [L(-hx, y0 - .1, -hz), L(hx, y0 - .1, -hz), L(hx, y0 - .1, hz), L(-hx, y0 - .1, hz)]
    poly(g, m, [c[3], c[2], b, a], None, yawrot((0, 1, 1), yaw)); poly(g, m, [c[0], c[1], b, a], None, yawrot((0, 1, -1), yaw))
    poly(g, m, [c[0], c[3], a], None, yawrot((-1, 1, 0), yaw)); poly(g, m, [c[1], c[2], b], None, yawrot((1, 1, 0), yaw))

def spire(g, m, cx, cz, w, y0, rise):
    a = (cx, y0 + rise, cz); h = w / 2
    c = [(cx - h, y0, cz - h), (cx + h, y0, cz - h), (cx + h, y0, cz + h), (cx - h, y0, cz + h)]
    for k in range(4): poly(g, m, [c[k], c[(k + 1) % 4], a], None, sub(mul(add(c[k], c[(k + 1) % 4]), .5), (cx, y0, cz)))

def facade(g, cx, cz, w, d, yaw, side, floors, mat, spacing=3.2, door=None, door_mat='DoorWood', skip=(), y0=0, wh=1.55, ww=1.05, sill='Concrete'):
    """Window decals across one facade. side: 'S' (+z), 'N' (-z), 'E' (+x), 'W' (-x) in local space."""
    length = w if side in 'NS' else d
    normal_local = {'S': (0, 0, 1), 'N': (0, 0, -1), 'E': (1, 0, 0), 'W': (-1, 0, 0)}[side]
    along = {'S': (-1, 0, 0), 'N': (1, 0, 0), 'E': (0, 0, -1), 'W': (0, 0, 1)}[side]
    half = (d if side in 'NS' else w) / 2
    fyaw = yaw + {'S': 0, 'N': 180, 'E': 90, 'W': -90}[side]
    count = max(1, int(length / spacing))
    for f in range(floors):
        for k in range(count):
            t = (k + .5) / count * length - length / 2
            if (f, k) in skip: continue
            local = add(mul(normal_local, half), mul(along, -t))
            base = add((cx, 0, cz), yawrot(local, yaw))
            if f == 0 and door is not None and k in door:
                decal(g, door_mat, (base[0], y0 + 1.15, base[2]), fyaw, 1.2, 2.3)
                box(g, 'Stone', add((base[0], y0 + .06, base[2]), mul(face_dir(fyaw), .25)), (1.6, .14, .5), fyaw)
                continue
            y = y0 + f * FLOOR + 1.75
            decal(g, mat, (base[0], y, base[2]), fyaw, ww, wh)
            box(g, sill, add((base[0], y - wh / 2 - .04, base[2]), mul(face_dir(fyaw), .1)), (ww + .2, .08, .22), fyaw)

def house(g, px0, py0, px1, py1, floors, wall, roof='Slate', ridge='x', rise=None, windows='WindowPlain', doors=None,
          roof_type='gable', yaw=0, plinth='StoneDark', solid=True, gable=None, sides='NSEW', y0=0, extra_height=.3):
    cx, cz, w, d = R(px0, py0, px1, py1)
    if yaw: w, d = (w, d)
    H = floors * FLOOR + extra_height
    box(g, wall, (cx, y0 + H / 2, cz), (w, H, d), yaw, solid, bottom=False)
    box(g, plinth, (cx, y0 + .35, cz), (w + .12, .7, d + .12), yaw, bottom=False)
    box(g, 'Concrete', (cx, y0 + H - .12, cz), (w + .18, .24, d + .18), yaw, bottom=False)
    for f in range(1, floors):
        box(g, 'Concrete', (cx, y0 + f * FLOOR + .1, cz), (w + .08, .12, d + .08), yaw, bottom=False, top=False)
    rise = rise if rise is not None else (d if ridge == 'x' else w) * .32
    if roof_type == 'gable': gable_roof(g, roof, cx, cz, w, d, y0 + H, rise, yaw, ridge, gable=gable or wall)
    elif roof_type == 'hip': hip_roof(g, roof, cx, cz, w, d, y0 + H, rise, yaw)
    else: box(g, roof, (cx, y0 + H + .15, cz), (w + .3, .3, d + .3), yaw)
    for side in sides:
        facade(g, cx, cz, w, d, yaw, side, floors, windows, door=(doors or {}).get(side), y0=y0)
    return cx, cz, w, d, H

def wall_run(g, m, a, b, y0, h, t=.35, openings=(), solid=True, cap=None):
    """Straight wall from a to b (world x,z) with door-style openings [(start_m, end_m, top_y)]."""
    dx, dz = b[0] - a[0], b[1] - a[1]; L = math.hypot(dx, dz); yaw = math.degrees(math.atan2(dx, dz))
    ux, uz = dx / L, dz / L
    cuts = sorted(openings); pos = 0; pieces = []
    for s0, s1, top in cuts:
        if s0 > pos: pieces.append((pos, s0, y0, h))
        if top < y0 + h: pieces.append((s0, s1, top, y0 + h - top))
        pos = s1
    if pos < L: pieces.append((pos, L, y0, h))
    for s0, s1, base, hh in pieces:
        if s1 - s0 < .01: continue
        mid = (s0 + s1) / 2
        box(g, m, (a[0] + ux * mid, base + hh / 2, a[1] + uz * mid), (t, hh, s1 - s0), yaw, solid, bottom=False)
    if cap: box(g, cap, ((a[0] + b[0]) / 2, y0 + h + .06, (a[1] + b[1]) / 2), (t + .1, .12, L + .1), yaw, bottom=False)

def stone_wall(g, pts, h=1.7, pillars=True):
    for (p0, p1) in zip(pts, pts[1:]):
        a, b = P(*p0), P(*p1)
        L = math.dist((a[0], a[2]), (b[0], b[2]))
        wall_run(g, 'Stone', (a[0], a[2]), (b[0], b[2]), 0, h, .45, cap='Concrete')
        if pillars:
            n = max(1, int(L / 4))
            for k in range(n + 1):
                t = k / n
                box(g, 'StoneDark', (a[0] + (b[0] - a[0]) * t, (h + .3) / 2, a[2] + (b[2] - a[2]) * t), (.62, h + .3, .62), bottom=False)
                box(g, 'Concrete', (a[0] + (b[0] - a[0]) * t, h + .36, a[2] + (b[2] - a[2]) * t), (.74, .12, .74), bottom=False)

# ------------------------------------------------------------------ vehicles and props
def car_wheels(g, x, z, yaw, track, base, r, w=.28):
    for sx in (-1, 1):
        for sz in base:
            c = add((x, r, z), yawrot((sx * track / 2, 0, sz), yaw))
            off = yawrot((w / 2, 0, 0), yaw)
            cylinder(g, 'Rubber', sub(c, off), add(c, off), r, 12)
            cylinder(g, 'Steel', add(c, mul(off, 1.02)), add(c, mul(off, 1.08)), r * .55, 8)

def van(g, px, py, yaw, paint='VanBlue', solid=True):
    x, _, z = P(px, py)
    L = lambda p: add((x, 0, z), yawrot(p, yaw))
    box(g, paint, L((0, 1.45, -.45)), (1.95, 1.75, 3.9), yaw, solid)
    box(g, paint, L((0, .95, 1.75)), (1.95, .75, 1.3), yaw, solid)
    poly(g, 'Glass', [L((-.92, 1.33, 1.1)), L((.92, 1.33, 1.1)), L((.92, 2.25, 1.5 - .1)), L((-.92, 2.25, 1.5 - .1))], None, yawrot((0, .4, 1), yaw))
    poly(g, paint, [L((-.98, 1.33, 1.1)), L((-.98, 2.32, 1.42)), L((-.98, 2.32, 1.5)), L((-.98, 1.33, 2.4))], None, yawrot((-1, 0, 0), yaw))
    poly(g, paint, [L((.98, 1.33, 1.1)), L((.98, 2.32, 1.42)), L((.98, 2.32, 1.5)), L((.98, 1.33, 2.4))], None, yawrot((1, 0, 0), yaw))
    box(g, paint, L((0, 2.29, .55)), (1.95, .06, 1.9), yaw)
    for sx in (-1, 1):
        box(g, 'Glass', L((sx * .985, 1.85, .95)), (.03, .55, .9), yaw)
    box(g, 'Steel', L((0, .55, 2.42)), (2.0, .28, .12), yaw); box(g, 'Steel', L((0, .55, -2.42)), (2.0, .28, .12), yaw)
    box(g, 'DarkMetal', L((0, 1.0, 2.41)), (1.4, .35, .04), yaw)
    for sx in (-1, 1): box(g, 'WhitePaint', L((sx * .7, 1.05, 2.42)), (.3, .18, .03), yaw)
    box(g, 'Glass', L((0, 1.9, -2.41)), (1.6, .55, .04), yaw)
    car_wheels(g, x, z, yaw, 1.75, (-1.45, 1.6), .38)

def sedan(g, px, py, yaw, paint='SedanBrown'):
    x, _, z = P(px, py)
    L = lambda p: add((x, 0, z), yawrot(p, yaw))
    box(g, paint, L((0, .72, 0)), (1.75, .62, 4.6), yaw, True)
    box(g, paint, L((0, 1.32, -.25)), (1.6, .05, 1.8), yaw)
    for s in (-1, 1):
        poly(g, 'Glass', [L((-.8, 1.03, s * 1.35 - .25)), L((.8, 1.03, s * 1.35 - .25)), L((.78, 1.32, s * .9 - .25)), L((-.78, 1.32, s * .9 - .25))], None, yawrot((0, .5, s), yaw))
        poly(g, 'Glass', [L((s * .86, 1.03, -1.6)), L((s * .86, 1.03, 1.1)), L((s * .79, 1.31, .65)), L((s * .79, 1.31, -1.15))], None, yawrot((s, .3, 0), yaw))
    box(g, 'Steel', L((0, .5, 2.32)), (1.8, .18, .1), yaw); box(g, 'Steel', L((0, .5, -2.32)), (1.8, .18, .1), yaw)
    car_wheels(g, x, z, yaw, 1.55, (-1.4, 1.45), .33, .22)

def tank(g, px, py, yaw, turret_yaw=0):
    x, _, z = P(px, py)
    L = lambda p: add((x, 0, z), yawrot(p, yaw))
    box(g, 'TankGreen', L((0, 1.25, 0)), (2.3, .9, 6.0), yaw, True)
    poly(g, 'TankGreen', [L((-1.15, 1.7, 3.0)), L((1.15, 1.7, 3.0)), L((1.15, .9, 3.6)), L((-1.15, .9, 3.6))], None, yawrot((0, 1, 1), yaw))
    for s in (-1, 1):
        box(g, 'DarkMetal', L((s * 1.45, .62, 0)), (.65, 1.0, 6.4), yaw, True)
        box(g, 'TankGreen', L((s * 1.45, 1.2, 0)), (.75, .08, 6.6), yaw)
        for k in range(5):
            c = L((s * 1.45, .55, -2.4 + k * 1.2)); o = yawrot((.36, 0, 0), yaw)
            cylinder(g, 'DarkMetal', sub(c, o), add(c, o), .42, 10)
        for k in range(18):
            box(g, 'Rubber', L((s * 1.45, .05, -3.2 + k * .38)), (.7, .1, .2), yaw)
    t = yaw + turret_yaw
    T = lambda p: add(L((0, 1.7, -.4)), yawrot(p, t))
    blob(g, 'TankGreen', T((0, .2, 0)), (1.5, .55, 1.6), f'turret{px}', .04, 12, 5)
    box(g, 'TankGreen', T((0, .1, 0)), (2.6, .35, 2.8), t)
    cylinder(g, 'TankGreen', T((0, .45, 1.4)), T((0, .45, 2.2)), .22, 10)
    cylinder(g, 'DarkMetal', T((0, .45, 2.2)), T((0, .45, 5.9)), .1, 10)
    cylinder(g, 'DarkMetal', T((0, .45, 4.6)), T((0, .45, 5.0)), .15, 10)
    cylinder(g, 'TankGreen', T((.5, .75, -.3)), T((.5, 1.0, -.3)), .35, 10)
    box(g, 'DarkMetal', T((0, .55, -1.55)), (1.8, .5, .6), t)

def tractor(g, px, py, yaw):
    x, _, z = P(px, py)
    L = lambda p: add((x, 0, z), yawrot(p, yaw))
    box(g, 'TractorRed', L((0, 1.15, .7)), (.9, .8, 2.0), yaw, True)
    box(g, 'DarkMetal', L((0, .8, .2)), (.7, .5, 2.6), yaw)
    box(g, 'TractorRed', L((0, 1.0, -.9)), (1.2, .5, .9), yaw)
    box(g, 'Rubber', L((0, 1.55, -.9)), (.6, .25, .55), yaw)
    for sx in (-1, 1):
        for zz in (-1.5, -.3):
            beam(g, 'TractorRed', L((sx * .7, 1.2, zz)), L((sx * .7, 2.65, zz)), .08)
    box(g, 'TractorRed', L((0, 2.7, -.9)), (1.6, .1, 1.5), yaw)
    poly(g, 'Glass', [L((-.7, 1.5, -.3)), L((.7, 1.5, -.3)), L((.7, 2.6, -.3)), L((-.7, 2.6, -.3))], None, yawrot((0, 0, 1), yaw))
    cylinder(g, 'DarkMetal', L((.25, 1.5, 1.4)), L((.25, 2.6, 1.4)), .06, 8)
    for sx in (-1, 1):
        c = L((sx * .95, .8, -1.0)); o = yawrot((.25, 0, 0), yaw)
        cylinder(g, 'Rubber', sub(c, o), add(c, o), .8, 14); cylinder(g, 'TractorRed', add(c, mul(o, 1.02)), add(c, mul(o, 1.1)), .45, 10)
        c = L((sx * .7, .45, 1.45)); o = yawrot((.15, 0, 0), yaw)
        cylinder(g, 'Rubber', sub(c, o), add(c, o), .45, 12); cylinder(g, 'TractorRed', add(c, mul(o, 1.02)), add(c, mul(o, 1.1)), .25, 8)

def hay_stack(g, px, py, yaw, layout):
    x, _, z = P(px, py)
    for lx, ly, lz, ry in layout:
        c = add((x, 0, z), yawrot((lx, .32 + ly * .64, lz), yaw))
        box(g, 'Hay', c, (1.15, .62, .55), yaw + ry, ly == 0)
        box(g, 'Hay', add(c, (0, .31, 0)), (1.1, .03, .5), yaw + ry)

def crates(g, px, py, yaw, n, seed):
    x, _, z = P(px, py); rnd = random.Random(seed)
    box(g, 'PlanksDark', (x, .07, z), (2.2, .14, 1.3), yaw, True)
    for k in range(n):
        lx = -.55 + (k % 2) * 1.1; ly = .14 + (k // 2) * .62; s = .55 + rnd.random() * .07
        c = add((x, 0, z), yawrot((lx, ly + s / 2, rnd.uniform(-.08, .08)), yaw))
        box(g, 'Cardboard', c, (s * 1.6, s, s * 1.8 if k % 3 else s * 1.4), yaw + rnd.uniform(-8, 8), k < 2)

def dumpster(g, px, py, yaw):
    x, _, z = P(px, py)
    box(g, 'DumpsterGreen', (x, .7, z), (1.9, 1.2, 1.1), yaw, True)
    poly(g, 'DumpsterGreen', [add((x, 0, z), yawrot(p, yaw)) for p in ((-1, 1.3, -.6), (1, 1.3, -.6), (1, 1.45, .6), (-1, 1.45, .6))], None, (0, 1, 0))
    for sx in (-.75, .75):
        for sz in (-.4, .4): cylinder(g, 'Rubber', add((x, 0, z), yawrot((sx, .12, sz), yaw)), add((x, 0, z), yawrot((sx, .02, sz), yaw)), .1, 6)

def jersey(g, px, py, yaw, length=3):
    x, _, z = P(px, py)
    L = lambda p: add((x, 0, z), yawrot(p, yaw))
    box(g, 'Concrete', L((0, .2, 0)), (.65, .4, length), yaw, True)
    box(g, 'Concrete', L((0, .7, 0)), (.3, .6, length), yaw)
    for s in (-1, 1):
        poly(g, 'Concrete', [L((s * .325, .4, -length / 2)), L((s * .325, .4, length / 2)), L((s * .15, .6, length / 2)), L((s * .15, .6, -length / 2))], None, yawrot((s, 1, 0), yaw))

def tree(g, px, py, size=1.0, seed=0, dark=False):
    x, _, z = P(px, py); rnd = random.Random(seed)
    h = 5.5 * size
    cylinder(g, 'Bark', (x, 0, z), (x + rnd.uniform(-.3, .3), h, z + rnd.uniform(-.3, .3)), .28 * size, 8, False, .16 * size)
    for k in range(14):
        a = rnd.random() * math.tau; rr = rnd.uniform(.3, 2.6) * size
        c = (x + math.cos(a) * rr, h + rnd.uniform(-1.4, 3.2) * size, z + math.sin(a) * rr)
        if k < 5: beam(g, 'Bark', (x, h * .6, z), c, .1 * size)
        rad = rnd.uniform(1.1, 1.9) * size
        blob(g, 'LeavesDark' if (dark or k % 4 == 0) else 'Leaves', c, (rad, rad * .85, rad), f'{seed}-{k}', .18, 10, 7)

def conifer(g, x, z, h, seed):
    cylinder(g, 'Bark', (x, 0, z), (x, h * .3, z), h * .03, 6, False)
    for k in range(4):
        y = h * (.22 + k * .19); r = h * (.26 - k * .055)
        cylinder(g, 'LeavesDark', (x, y, z), (x, y + h * .32, z), r, 8, True, .05)

def lamp(g, px, py, yaw):
    x, _, z = P(px, py)
    cylinder(g, 'Steel', (x, 0, z), (x, 7.2, z), .09, 8, False, .06)
    end = add((x, 7.0, z), yawrot((0, 0, 1.6), yaw))
    beam(g, 'Steel', (x, 7.1, z), end, .07)
    box(g, 'Steel', add(end, (0, -.1, 0)), (.35, .14, .6), yaw)
    box(g, 'LightPanel', add(end, (0, -.18, 0)), (.25, .02, .45), yaw)

POLES = []
def pole(g, px, py, h=8.5):
    x, _, z = P(px, py)
    cylinder(g, 'PlanksDark', (x, 0, z), (x, h, z), .14, 7, True, .11)
    box(g, 'PlanksDark', (x, h - .5, z), (1.8, .12, .12))
    for dx in (-.75, 0, .75): box(g, 'WhitePaint', (x + dx, h - .38, z), (.08, .16, .08))
    POLES.append((x, h - .32, z))

def wires(g, chain, sag=.9):
    for a, b in zip(chain, chain[1:]):
        pa, pb = POLES[a], POLES[b]
        for dx in (-.75, .75):
            prev = None
            for k in range(9):
                t = k / 8; p = (pa[0] + (pb[0] - pa[0]) * t + dx, pa[1] + (pb[1] - pa[1]) * t - sag * 4 * t * (1 - t), pa[2] + (pb[2] - pa[2]) * t)
                if prev: beam(g, 'Rubber', prev, p, .025)
                prev = p

def fence_picket(g, a, b, h=1.0):
    ax, _, az = P(*a); bx, _, bz = P(*b)
    L = math.hypot(bx - ax, bz - az); yaw = math.degrees(math.atan2(bx - ax, bz - az)); n = int(L / .16)
    for k in range(n):
        t = (k + .5) / n
        box(g, 'PicketWhite', (ax + (bx - ax) * t, h / 2, az + (bz - az) * t), (.04, h, .09), yaw, bottom=False)
    for y in (.3, .8): beam(g, 'PicketWhite', (ax, y, az), (bx, y, bz), .05)
    collider(((ax + bx) / 2, h / 2, (az + bz) / 2), (.1, h, L), yaw)

def wood_fence(g, a, b, h=1.3):
    ax, _, az = P(*a); bx, _, bz = P(*b)
    L = math.hypot(bx - ax, bz - az); n = max(1, int(L / 2.4))
    for k in range(n + 1):
        t = k / n; beam(g, 'PlanksDark', (ax + (bx - ax) * t, 0, az + (bz - az) * t), (ax + (bx - ax) * t, h, az + (bz - az) * t), .1)
    for y in (.4, .95): beam(g, 'Planks', (ax, y, az), (bx, y, bz), .07, .14)

def curb(g, a, b, paint=True):
    ax, _, az = P(*a); bx, _, bz = P(*b)
    L = math.hypot(bx - ax, bz - az); yaw = math.degrees(math.atan2(bx - ax, bz - az))
    box(g, 'YellowPaint' if paint else 'Concrete', ((ax + bx) / 2, .08, (az + bz) / 2), (.28, .16, L), yaw, bottom=False)

def ground_rect(g, m, px0, py0, px1, py1, y=.02):
    x0, _, z0 = P(px0, py0); x1, _, z1 = P(px1, py1)
    poly(g, m, [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], None, (0, 1, 0))

# ------------------------------------------------------------------ map content
# All coordinates below are pixels in the user's 2048x2048 Standoff minimap (north up).
# Playable boundary traced along the minimap's white outline.
BOUNDARY = [(1335, 318), (1268, 313), (1228, 389), (1215, 438), (1190, 489), (1131, 494), (1130, 516), (1048, 516), (1045, 450),
            (981, 450), (980, 484), (933, 484), (931, 476), (752, 477), (750, 492), (714, 498), (713, 597), (664, 598), (663, 739),
            (626, 740), (626, 836), (470, 838), (470, 941), (492, 943), (492, 987), (471, 990), (471, 1249), (501, 1251), (500, 1277),
            (471, 1278), (471, 1418), (502, 1420), (502, 1446), (477, 1447), (477, 1531), (498, 1533), (498, 1703), (509, 1731),
            (635, 1730), (636, 1526), (914, 1525), (970, 1498), (1083, 1498), (1084, 1416), (1076, 1414), (1077, 1318), (1226, 1322),
            (1249, 1303), (1250, 1275), (1501, 1275), (1502, 1179), (1520, 1178), (1540, 1161), (1573, 1160), (1578, 813), (1477, 811),
            (1477, 644), (1438, 642), (1439, 454), (1336, 451)]
INTERIORS = []   # (name, outline) of enterable buildings: volumes and navigation checks are derived from these.

def spans(poly, y):
    xs = []
    for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
        if min(y0, y1) <= y < max(y0, y1): xs.append(x0 + (y - y0) * (x1 - x0) / (y1 - y0))
    xs.sort(); return list(zip(xs[::2], xs[1::2]))
def inside(poly, x, y): return any(a <= x <= b for a, b in spans(poly, y))

def fill(g, m, outline, y, step=6, flip=False):
    """Scanline-filled planar polygon (concave allowed) at height y."""
    ys = [p[1] for p in outline]
    py = min(ys)
    while py < max(ys):
        for a, b in spans(outline, py + step / 2):
            x0, _, z0 = P(a, py); x1, _, z1 = P(b, min(py + step, max(ys)))
            poly_pts = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
            poly(g, m, poly_pts, None, (0, -1 if flip else 1, 0))
        py += step

def closed(g, rect, floors, wall, roof='Slate', ridge='x', rise=None, windows='WindowPlain', doors=None, gable=None, roof_type='gable', sides='NSEW', plinth='StoneDark', extra=.3):
    """Inaccessible (hatched) building block: solid collision, facade decals and a roof."""
    return house(g, *rect, floors, wall, roof, ridge, rise, windows, doors, roof_type, gable=gable, sides=sides, plinth=plinth, extra_height=extra)

def shell(name, outline, H, wall, doors, roofs, floor='Concrete', windows='WindowPlain', skip=(), lining='InteriorWall', light=(1, .9, .75), window_y=(1.75,), ceiling=3.4):
    """Enterable building. Walls follow the outline with full-height door gaps (render-only lintels),
    so the top-down navmesh sees the floor; roofs and ceilings are render-only for the same reason.
    doors: [(px, py, width_m)] on outline edges. roofs: [(rect, type, material, ridge, rise)]."""
    g = name
    INTERIORS.append((name, outline))
    world = [P(x, y) for x, y in outline]
    cx = sum(p[0] for p in world) / len(world); cz = sum(p[2] for p in world) / len(world)
    for k, ((ax, ay), (bx, by)) in enumerate(zip(outline, outline[1:] + outline[:1])):
        a, b = P(ax, ay), P(bx, by); L = math.dist((a[0], a[2]), (b[0], b[2]))
        if L < .05: continue
        ux, uz = (b[0] - a[0]) / L, (b[2] - a[2]) / L
        # Outward normal: the side whose sample point lies outside the outline.
        nx, nz = uz, -ux
        mx, mz = (a[0] + b[0]) / 2 + nx * .5, (a[2] + b[2]) / 2 + nz * .5
        if inside(outline, mx / S + 1020, mz / S + 1020): nx, nz = -nx, -nz
        fyaw = math.degrees(math.atan2(nx, nz))
        openings = []
        for dx, dy, w in doors:
            d = P(dx, dy); t = (d[0] - a[0]) * ux + (d[2] - a[2]) * uz
            off = abs((d[0] - a[0]) * uz - (d[2] - a[2]) * ux)
            if off < .6 and -.1 < t < L + .1: openings.append((max(0, t - w / 2), min(L, t + w / 2), H))
        if k not in skip:
            wall_run(g, wall, (a[0], a[2]), (b[0], b[2]), 0, H, .3, openings)
            pos = 0
            for s0, s1 in sorted((o[0], o[1]) for o in openings) + [(L, L)]:
                if s0 - pos > .05:
                    m = (pos + s0) / 2
                    box(g, 'StoneDark', (a[0] + ux * m + nx * .05, .3, a[2] + uz * m + nz * .05), (.42, .6, s0 - pos), math.degrees(math.atan2(ux, uz)), bottom=False)
                pos = s1
            for s0, s1, _ in openings:
                m = (s0 + s1) / 2; px_, pz_ = a[0] + ux * m, a[2] + uz * m
                box(g, wall, (px_, (2.5 + H) / 2, pz_), (.3, H - 2.5, s1 - s0), math.degrees(math.atan2(ux, uz)))
                decal(g, 'PlanksDark', (px_ + nx * .16, 2.55, pz_ + nz * .16), fyaw, s1 - s0 + .3, .18, 0)
            # Exterior windows between openings.
            count = int(L / 3.2)
            for i in range(count):
                t = (i + .5) * L / count
                if any(s0 - .9 < t < s1 + .9 for s0, s1, _ in openings): continue
                for wy in window_y:
                    if wy + .8 > H: continue
                    decal(g, windows, (a[0] + ux * t + nx * .16, wy, a[2] + uz * t + nz * .16), fyaw, 1.05, 1.55)
            # Interior lining. It is render-only, so it must leave the door gaps open: full height
            # between openings, and only above the 2.5 m lintel across each opening.
            ins = (-nx * .16, -nz * .16)
            def lining_quad(s0, s1, y0, y1):
                p0 = (a[0] + ux * s0 + ins[0], a[2] + uz * s0 + ins[1]); p1 = (a[0] + ux * s1 + ins[0], a[2] + uz * s1 + ins[1])
                poly(g, lining, [(p0[0], y0, p0[1]), (p1[0], y0, p1[1]), (p1[0], y1, p1[1]), (p0[0], y1, p0[1])], None, (-nx, 0, -nz))
            pos = 0
            for s0, s1 in sorted((o[0], o[1]) for o in openings) + [(L, L)]:
                if s0 - pos > .01: lining_quad(pos, s0, 0, ceiling)
                if s1 - s0 > .01 and ceiling > 2.5: lining_quad(s0, s1, 2.5, ceiling)
                pos = max(pos, s1)
    fill(g, floor, outline, .025)
    fill(g, 'Ceiling', outline, ceiling, flip=True)
    for rect, kind, mat, ridge, rise in roofs:
        rcx, rcz, rw, rd = R(*rect)
        box(g, 'Concrete', (rcx, H - .1, rcz), (rw + .2, .2, rd + .2), bottom=False)
        if kind == 'gable': gable_roof(g, mat, rcx, rcz, rw, rd, H, rise, 0, ridge, gable=wall)
        elif kind == 'hip': hip_roof(g, mat, rcx, rcz, rw, rd, H, rise)
        else: box(g, mat, (rcx, H + .1, rcz), (rw + .3, .25, rd + .3))
    point_light(name + ' light', (cx, ceiling - .4, cz), light, 6, max(6, math.sqrt(len(outline)) * 4))
    return cx, cz

def build_ground():
    g = 'Ground'
    box(g, 'Grass', (0, -.5, 0), (420, 1, 420), render=False, solid=True)
    for i in range(-5, 5):
        for j in range(-5, 5):
            x0, z0 = i * 40, j * 40
            poly('Terrain grass', 'Grass', [(x0, 0, z0), (x0 + 40, 0, z0), (x0 + 40, 0, z0 + 40), (x0, 0, z0 + 40)], None, (0, 1, 0))
    # Fields beyond the boundary.
    for r in [(560, 60, 1000, 440), (1460, 250, 1900, 620), (120, 1520, 470, 1950), (1120, 1560, 1600, 1950)]:
        x0, _, z0 = P(r[0], r[1], .02); x1, _, z1 = P(r[2], r[3])
        poly('Farm field', 'Field', [(x0, .02, z0), (x1, .02, z0), (x1, .02, z1), (x0, .02, z1)], None, (0, 1, 0))
    # Playable streets: asphalt across the whole traced footprint, overlays for yards and walks.
    fill('Roads', 'Asphalt', BOUNDARY, .03, 8)
    for r, m in [((474, 845, 556, 1525), 'Grass'), ((500, 1530, 632, 1728), 'Dirt'), ((1218, 330, 1332, 505), 'Dirt'),
                 ((583, 910, 673, 996), 'Dirt'), ((1326, 646, 1438, 705), 'Dirt'), ((800, 700, 925, 800), 'Cobble'),
                 ((930, 677, 1130, 697), 'Cobble'), ((955, 842, 973, 1162), 'Cobble'), ((1235, 1146, 1500, 1274), 'Concrete'),
                 ((1500, 1000, 1574, 1176), 'Concrete'), ((633, 1484, 912, 1522), 'Grass'), ((1085, 1325, 1225, 1415), 'Dirt')]:
        ground_rect('Yards', m, *r, .045)
    for a, b in [((930, 697), (1130, 697)), ((955, 842), (955, 1160)), ((800, 800), (925, 800)), ((1235, 1146), (1235, 1274))]:
        curb('Kerbs', a, b)

def build_boundary():
    # Invisible perimeter collision plus visible stone walls, hedges and fences along the white outline.
    rnd = random.Random(8)
    for k, ((ax, ay), (bx, by)) in enumerate(zip(BOUNDARY, BOUNDARY[1:] + BOUNDARY[:1])):
        a, b = P(ax, ay), P(bx, by); L = math.dist((a[0], a[2]), (b[0], b[2]))
        if L < .3: continue
        yaw = math.degrees(math.atan2(b[0] - a[0], b[2] - a[2]))
        box('Boundary', 'Hedge', ((a[0] + b[0]) / 2, 3, (a[2] + b[2]) / 2), (.6, 6, L + .3), yaw, solid=True, render=False)
        kind = k % 3 if L > 3 else 0
        if kind == 0: wall_run('Boundary walls', 'Stone', (a[0], a[2]), (b[0], b[2]), 0, 1.8, .45, solid=False, cap='Concrete')
        elif kind == 1:
            n = max(1, int(L / 2.2))
            for i in range(n):
                t = (i + .5) / n; blob('Boundary hedges', 'Hedge', (a[0] + (b[0] - a[0]) * t, 1.0, a[2] + (b[2] - a[2]) * t), (1.5, 1.4, 1.5), f'bh{k}-{i}', .22, 7, 4)
        else: wood_fence('Boundary fences', (ax, ay), (bx, by), 1.6)

def build_market():
    # North building: white two-storey market with red shutters; enterable ground floor.
    shell('Market', [(930, 505), (1028, 505), (1028, 538), (1130, 538), (1130, 677), (930, 677)], 6.6, 'PlasterWhite',
          [(1130, 575, 2.4), (1000, 677, 2.6), (930, 625, 2.4)],
          [((930, 538, 1130, 677), 'gable', 'Slate', 'x', 3.2), ((930, 505, 1028, 538), 'gable', 'Slate', 'x', 1.4)],
          floor='PlanksDark', windows='WindowShutter', window_y=(1.75, 4.95))
    closed('Market stair tower', (983, 452, 1046, 505), 2, 'PlasterWhite', 'Slate', 'x', 1.6, 'WindowShutter', roof_type='hip', sides='SEW')
    closed('Market annex', (821, 580, 930, 655), 1, 'Stone', 'Slate', 'x', 1.8, 'WindowBroken', {'W': (0,)}, gable='Stone')
    x, _, z = P(1060, 677); decal('Market', 'SignMarket', (x, 4.1, z + .17), 0, 6, 1.5, 0)
    for k, px in enumerate((950, 1040, 1080, 1110)):
        x, _, z = P(px, 677); decal('Market', ['PosterA', 'PosterB', 'PosterC', 'PosterA'][k], (x, 1.5, z + .18), 0, 1.2, .9, 0)
    # Interior: long shop counter, shelving and stock.
    g = 'Market interior'
    x0, _, z0 = P(1000, 610); x1, _, _ = P(1095, 610)
    box(g, 'PlanksDark', ((x0 + x1) / 2, .55, z0), (x1 - x0, 1.1, .7), solid=True); box(g, 'Concrete', ((x0 + x1) / 2, 1.12, z0), (x1 - x0 + .1, .06, .8))
    for px in (945, 965):
        x, _, z = P(px, 590); box(g, 'DarkMetal', (x, .9, z), (.6, 1.8, 4.5), solid=True); decal(g, 'ShelfGoods', (x + .31, .95, z), 90, 4.4, 1.6, 0)
    crates(g, 1105, 655, 0, 4, 31); crates(g, 960, 655, 90, 3, 32)
    crates('Market props', 1060, 705, 0, 6, 8); crates('Market props', 960, 712, 0, 4, 9)
    box('Market props', 'WhitePaint', P(1145, 641, .9), (.9, 1.8, 1.1), solid=True)
    dumpster('Market props', 778, 553, 10)
    x0, _, z0 = P(760, 636); x1, _, z1 = P(854, 660)
    wall_run('Market props', 'Concrete', (x0, z0), (x1, z1), 0, 1.1, .3, cap='Concrete')

def build_barn():
    closed('Barn loft', (1326, 455, 1438, 526), 2, 'Planks', 'Corrugated', 'x', 2.2, 'WindowBroken', gable='Planks', plinth='Concrete', sides='EW')
    shell('Barn', [(1326, 526), (1438, 526), (1438, 645), (1326, 645)], 4.2, 'Planks',
          [(1326, 568, 2.6), (1382, 645, 3.2)], [], floor='Dirt', windows='WindowBroken', skip=(0,), lining='Planks', ceiling=4.1)
    g = 'Barn'
    x0, _, z0 = P(1326, 526); x1, _, z1 = P(1438, 645); cz = (z0 + z1) / 2
    # Half-collapsed roof: one surviving pitch, the other fallen into the barn.
    poly(g, 'Corrugated', [(x0 - .3, 4.2, z0), (x1 + .3, 4.2, z0), (x1 + .3, 6.2, cz), (x0 - .3, 6.2, cz)], None, (0, 1, -1))
    poly(g, 'Corrugated', [(x0 + 1, .4, cz + 2.4), (x0 + 5, .5, cz + 2.8), (x0 + 5, 3.4, cz), (x0 + 1, 3.1, cz - .4)], None, (0, 1, .5))
    for k in range(5):
        x = x0 + 1 + k * 1.8; beam(g, 'PlanksDark', (x, 4.2, z1), (x, 6.2, cz), .14)
        beam(g, 'PlanksDark', (x, 4.2, z0), (x, 6.2, cz), .14)
    hay_stack('Barn props', 1415, 545, 0, [(0, 0, 0, 0), (1.2, 0, 0, 0), (0, 1, 0, 8), (0, 0, .6, 0), (1.2, 0, .6, 0)])
    hay_stack('Barn props', 1350, 610, 90, [(0, 0, 0, 0), (0, 1, 0, 4)])
    for a, b in [((1381, 649), (1381, 672)), ((1381, 690), (1381, 711))]: wood_fence('Barn fence', a, b)

def build_warehouse():
    # East L-shaped white warehouse (white roof in the aerial), enterable with two closed wings.
    shell('Warehouse', [(1346, 713), (1420, 713), (1420, 862), (1507, 862), (1507, 909), (1426, 909), (1426, 975), (1323, 975),
                        (1323, 877), (1391, 877), (1391, 766), (1346, 766)], 4.8, 'PlasterGrey',
          [(1346, 740, 2.2), (1380, 975, 2.6), (1507, 885, 2.2)],
          [((1346, 713, 1420, 877), 'gable', 'CanopyMetal', 'z', 1.8), ((1323, 877, 1507, 975), 'gable', 'CanopyMetal', 'x', 1.6)],
          windows='WindowBarred', skip=(4, 5, 8, 9))
    closed('Warehouse west wing', (1319, 766, 1391, 877), 1, 'PlasterGrey', 'CanopyMetal', 'z', 1.4, 'WindowBarred', roof_type='hip', sides='WN', extra=1.6)
    closed('Warehouse store room', (1426, 909, 1507, 975), 1, 'PlasterGrey', 'CanopyMetal', 'x', .1, 'WindowBarred', roof_type='flat', sides='E', extra=1.5)
    closed('Warehouse porch', (1328, 737, 1347, 766), 0, 'Concrete', 'Concrete', roof_type='flat', sides='', extra=.5)
    x, _, z = P(1420, 790); decal('Warehouse', 'SignBrick', (x + .17, 3.6, z), 90, 4.5, 1.1, 0)
    g = 'Warehouse interior'
    crates(g, 1360, 800, 0, 6, 41); crates(g, 1400, 940, 90, 5, 42); crates(g, 1470, 885, 0, 3, 43)
    for py in (735, 760):
        x, _, z = P(1395, py); box(g, 'DarkMetal', (x, 1.1, z), (3.5, 2.2, .6), solid=True); decal(g, 'ShelfGoods', (x, 1.1, z + .31), 0, 3.4, 1.9, 0)
    dumpster('Warehouse props', 1398, 985, 0)
    x, _, z = P(1510, 976); barrel('Warehouse props', x, z, 'RedPaint')

def build_church():
    # Centre block: stone presbytery and houses (closed), bell tower, enterable ground floor and bakery.
    closed('Presbytery', (974, 839, 1118, 944), 2, 'Stone', 'Slate', 'x', 3.6, 'WindowShutter', {'N': (1,), 'W': (0,)}, gable='Stone')
    closed('Presbytery east', (1118, 858, 1206, 927), 2, 'PlasterWhite', 'Slate', 'z', 2.4, 'WindowGreen', roof_type='hip')
    closed('Presbytery tower wing', (1118, 808, 1152, 858), 2, 'Stone', 'Slate', 'z', 2.0, 'WindowPlain', roof_type='hip', sides='NW')
    closed('Church side room', (1206, 873, 1228, 916), 1, 'Stone', 'Slate', 'z', 1.0, 'WindowPlain', sides='E')
    closed('Centre house', (1026, 944, 1124, 1030), 2, 'PlasterCream', 'Slate', 'z', 3.0, 'WindowGreen', {'W': (1,)}, gable='PlasterCream')
    closed('Centre east house', (1204, 950, 1251, 1045), 1, 'PlasterGrey', 'Slate', 'z', 1.6, 'WindowPlain', sides='NES')
    for px in (1010, 1080):
        x, _, z = P(px, 839)
        box('Presbytery', 'Stone', (x, 7.9, z + .9), (1.8, 2.0, 1.4)); gable_roof('Presbytery', 'Slate', x, z + .9, 1.8, 1.6, 8.9, .8, 0, 'z', .15)
        decal('Presbytery', 'WindowPlain', (x, 7.9, z + .19), 180, .8, 1.1, 0)
    build_tower((1152, 808, 1204, 858))
    shell('Church hall', [(1124, 927), (1204, 927), (1204, 1138), (1052, 1138), (1052, 1030), (1124, 1030)], 6.4, 'Stone',
          [(1204, 1095, 2.6), (1052, 1084, 2.4)],
          [((1052, 1030, 1204, 1138), 'gable', 'Slate', 'x', 3.0), ((1124, 927, 1204, 1030), 'gable', 'Slate', 'z', 2.6)],
          floor='PlanksDark', windows='WindowShutter', skip=(0, 2, 4, 5), window_y=(1.75, 4.8))
    g = 'Church hall interior'
    for a, b, gap in [((1160, 950), (1160, 1110), None), ((1098, 1030), (1098, 1072), None)]:
        x0, _, z0 = P(*a); x1, _, z1 = P(*b); wall_run(g, 'InteriorWall', (x0, z0), (x1, z1), 0, 3.4, .2)
    for px, py in [(1080, 1110), (1185, 960)]:
        x, _, z = P(px, py); box(g, 'PlanksDark', (x, .8, z), (2.2, .1, 1.1), solid=True)
        for dx in (-.9, .9):
            for dz in (-.4, .4): box(g, 'PlanksDark', (x + dx, .4, z + dz), (.08, .8, .08))
    crates(g, 1135, 1120, 0, 4, 51)
    # Bakery along the south side of the block.
    closed('Bakery', (963, 1140, 1185, 1231), 2, 'PlasterOchre', 'Slate', 'x', 3.4, 'WindowShutter', {'S': (2, 5)}, gable='PlasterOchre')
    x, _, z = P(1075, 1231); decal('Bakery', 'SignBakery', (x, 4.4, z + .2), 0, 5, 1.25, 0)
    x, _, z = P(1150, 1231); decal('Bakery', 'SignCafe', (x, 4.4, z + .2), 0, 3.5, .9, 0)
    for a, b in [((953, 1166), (953, 1222))]:
        x0, _, z0 = P(*a); x1, _, z1 = P(*b); wall_run('Bakery', 'Stone', (x0, z0), (x1, z1), 0, 1.6, .4, cap='Concrete')
    # Ruined stone shed west of the block and the low walls around it.
    closed('Ruined shed', (916, 996, 970, 1084), 1, 'StoneDark', 'Corrugated', 'z', 1.0, 'WindowBroken', gable='StoneDark', extra=-.4)
    for pts in [[(925, 886), (925, 808), (1037, 808)], [(925, 941), (925, 990)], [(925, 1094), (925, 1110), (939, 1135)], [(871, 1248), (871, 1229), (911, 1229), (911, 1219)]]:
        stone_wall('Low walls', pts, 1.2, pillars=False)

def build_tower(rect):
    g = 'Church tower'
    cx, cz, w, d = R(*rect); top = 16
    # Shell with a blown-out upper east face where the fire burns (faces the warehouse street).
    box(g, 'StoneDark', (cx - w / 2 + .3, top / 2, cz), (.6, top, d), solid=True)
    box(g, 'StoneDark', (cx, top / 2, cz - d / 2 + .3), (w, top, .6), solid=True)
    box(g, 'StoneDark', (cx, top / 2, cz + d / 2 - .3), (w, top, .6), solid=True)
    box(g, 'StoneDark', (cx + w / 2 - .3, 5.5, cz), (.6, 11, d), solid=True)
    box(g, 'StoneDark', (cx + w / 2 - .3, 13.5, cz - d / 2 + .5), (.6, 5, 1.0))
    box(g, 'StoneDark', (cx + w / 2 - .3, 12.0, cz + d / 2 - .35), (.6, 2.0, .7))
    box(g, 'StoneDark', (cx + w / 2 - .3, 15.4, cz + .3), (.6, 1.2, d - .6))
    for y in (5.0, 10.5): box(g, 'Concrete', (cx, y, cz), (w + .25, .25, d + .25), top=False, bottom=False)
    box(g, 'Concrete', (cx, top + .15, cz), (w + .5, .3, d + .5))
    spire(g, 'Slate', cx, cz, w + .3, top + .3, 5.5)
    box(g, 'Steel', (cx, top + 6.2, cz), (.06, 1.4, .06)); box(g, 'Steel', (cx, top + 6.4, cz), (.6, .06, .06))
    box(g, 'PlanksDark', (cx, 11, cz), (w - 1.2, .25, d - 1.2))
    for yaw, (dx, dz) in ((0, (0, 1)), (180, (0, -1)), (-90, (-1, 0))):
        decal(g, 'WindowBroken', (cx + dx * w / 2, 13.2, cz + dz * d / 2), yaw, .9, 1.8, .03)
    decal(g, 'DoorWood', (cx, 1.3, cz - d / 2), 180, 1.5, 2.6, .03)
    box(g, 'Rubber', (cx + w / 2 - .7, 13.3, cz), (.1, 4.5, d - .6))
    rnd = random.Random(5)
    for k in range(9):
        a = (cx + rnd.uniform(-.4, 1.6), 11.15, cz + rnd.uniform(-1.6, 1.6)); b = add(a, (rnd.uniform(-.6, .6), rnd.uniform(.1, 1.6), rnd.uniform(-1.4, 1.4)))
        beam(g, 'Embers', a, b, rnd.uniform(.14, .26))
    box(g, 'Embers', (cx + .4, 11.16, cz), (w - 1.3, .06, d - 1.3))
    rnd = random.Random(7)
    for k in range(26):
        x = cx + w / 2 + rnd.uniform(.3, 3.4); z = cz + rnd.uniform(-2.5, 2.5); s = rnd.uniform(.25, .7)
        box('Church rubble', rnd.choice(['StoneDark', 'Stone', 'Slate', 'PlanksDark']), (x, s / 2 * .8, z), (s, s * .8, s * 1.2), rnd.uniform(0, 90))
    global TOWER_FIRE
    TOWER_FIRE = (cx + w / 2 - .6, 12.6, cz)

def build_compound():
    # West-centre farmhouse with the tall chimney (hatched on the minimap) and its walled yard.
    closed('Chimney house', (673, 905, 786, 1112), 2, 'PlasterCream', 'Slate', 'z', 3.0, 'WindowGreen', {'E': (1, 4), 'N': (1,)}, gable='PlasterCream')
    closed('Chimney house wing', (578, 996, 673, 1089), 2, 'PlasterCream', 'Slate', 'x', 2.6, 'WindowGreen', {'S': (1,)}, sides='NSW', gable='PlasterCream')
    closed('Compound shed', (686, 882, 722, 905), 0, 'Planks', 'Corrugated', 'x', .5, 'WindowBroken', roof_type='gable', sides='N', plinth='Concrete', extra=2.4)
    x, _, z = P(700, 930); H = 2 * FLOOR + .3
    box('Chimney house', 'Brick', (x, (H + 6.5) / 2, z), (1.2, H + 6.5, 1.2), solid=True); box('Chimney house', 'Concrete', (x, H + 6.6, z), (1.45, .25, 1.45))
    # Porch with blue doors on the east face.
    x0, _, z0 = P(786, 960); x1, _, z1 = P(786, 1060)
    for t in (0, .5, 1): beam('Chimney house', 'PlanksDark', (x0 + 1.8, 0, z0 + (z1 - z0) * t), (x0 + 1.8, 2.9, z0 + (z1 - z0) * t), .16)
    poly('Chimney house', 'Corrugated', [(x0, 3.45, z0), (x0, 3.45, z1), (x0 + 2.0, 2.95, z1), (x0, 2.95, z0)][:3] + [(x0 + 2.0, 2.95, z0)], None, (.3, 1, 0))
    for pts in [[(583, 910), (630, 910)], [(649, 910), (673, 910)], [(583, 910), (583, 935)], [(583, 969), (583, 996)]]:
        stone_wall('Compound walls', pts)
    x0, _, z0 = P(508, 1076); x1, _, z1 = P(578, 1076)
    wood_fence('Compound walls', (508, 1076), (578, 1076)); cylinder('Compound walls', 'Concrete', (x0, 0, z0), (x0, 1.4, z0), .15, 8)
    hay_stack('Compound props', 600, 925, 10, [(0, 0, 0, 0), (1.2, 0, 0, 3), (.6, 1, 0, -4), (0, 0, .7, 0)])
    dumpster('Compound props', 783, 1115, 0)
    box('Compound props', 'DarkMetal', P(529, 876, .5), (3.6, 1.0, 1.6), solid=True); box('Compound props', 'YellowPaint', P(529, 876, 1.1), (3.0, .2, 1.4))

def build_south():
    # Blue-roofed warehouse + rear rooms (enterable) and the yellow hotel (enterable).
    shell('South warehouse', [(633, 1237), (792, 1237), (792, 1479), (675, 1479), (675, 1372), (633, 1372)], 5.2, 'PlasterGrey',
          [(633, 1300, 2.6), (765, 1237, 2.4), (792, 1415, 2.4), (720, 1479, 2.4)],
          [((633, 1237, 792, 1372), 'gable', 'CorrugatedBlue', 'z', 2.2), ((675, 1372, 792, 1479), 'gable', 'Slate', 'x', 2.0)],
          windows='WindowBarred')
    g = 'South warehouse interior'
    for a, b, ops in [((690, 1277), (792, 1277), [(2.4, 4.2, 3.4)]), ((690, 1277), (690, 1372), [(3.0, 5.2, 3.4)]),
                      ((675, 1372), (792, 1372), [(1.6, 4.4, 3.4)]), ((740, 1378), (740, 1479), [(3.6, 5.6, 3.4)])]:
        x0, _, z0 = P(*a); x1, _, z1 = P(*b); wall_run(g, 'InteriorWall', (x0, z0), (x1, z1), 0, 3.4, .2, ops)
    crates(g, 660, 1260, 0, 5, 61); crates(g, 770, 1450, 90, 4, 62)
    closed('South warehouse store', (642, 1392, 675, 1479), 1, 'Stone', 'Slate', 'z', 1.0, 'WindowPlain', sides='W')
    closed('South warehouse link', (792, 1277, 860, 1371), 1, 'Brick', 'Slate', 'x', 1.6, 'WindowBarred', sides='N', extra=1.0)
    x, _, z = P(633, 1340); decal('South warehouse', 'Garage', (x - .17, 1.8, z), -90, 3.6, 3.2, 0)
    shell('Hotel', [(860, 1274), (984, 1274), (984, 1371), (860, 1371)], 4.0, 'PlasterYellow',
          [(900, 1274, 2.4), (950, 1371, 2.4), (984, 1320, 2.2)],
          [((860, 1274, 984, 1371), 'gable', 'Terracotta', 'x', 2.0)], floor='StoreFloor', windows='WindowBlue')
    cx, cz, w, d = R(860, 1274, 984, 1371)
    box('Hotel', 'Steel', (cx, 7.1, cz), (8.2, 1.5, .08))
    for k, letter in enumerate('HOTEL'):
        x = cx - 3.2 + k * 1.6
        decal('Hotel', 'Letter' + letter, (x, 7.1, cz), 0, 1.4, 1.4, .06)
        decal('Hotel', 'Letter' + letter, (2 * cx - x, 7.1, cz), 180, 1.4, 1.4, .06)
        beam('Hotel', 'Steel', (x, 5.6, cz), (x, 6.4, cz), .06)
    box('Hotel', 'WhitePaint', (cx + 2.5, 2.6, cz - d / 2 - .35), (.9, .6, .6))
    stone_wall('Hotel yard', [(886, 1399), (886, 1427), (984, 1427), (984, 1371)], 1.4)
    # West edge: lean-to shed, bench and fence line.
    closed('Edge shed', (476, 1291, 498, 1405), 0, 'Planks', 'Corrugated', 'x', .3, 'WindowBroken', roof_type='flat', sides='E', plinth='Concrete', extra=2.6)
    box('Edge props', 'PlanksDark', P(519, 1340, .45), (2.2, .1, 1.4), solid=True)
    wood_fence('Edge props', (533, 1250), (533, 1450), 1.2)

def build_gas_station():
    # U store (enterable) with a forecourt canopy on its east side (minimap bottom-right).
    shell('Gas store', [(1275, 1150), (1390, 1150), (1390, 1264), (1275, 1264)], 4.0, 'PlasterGrey',
          [(1390, 1185, 2.6), (1310, 1150, 2.4)], [((1275, 1150, 1390, 1264), 'flat', 'ConcreteDark', 'x', 0)],
          floor='StoreFloor', windows='WindowPlain', lining='InteriorWall', light=(.85, .93, 1))
    closed('Gas store back room', (1237, 1136, 1275, 1239), 1, 'PlasterGrey', 'ConcreteDark', roof_type='flat', sides='W', extra=.5)
    g = 'Gas store interior'
    x0, _, z0 = P(1275, 1150); x1, _, z1 = P(1390, 1264)
    closed('Gas store toilets', (1328, 1224, 1383, 1264), 0, 'InteriorWall', 'Ceiling', roof_type='flat', sides='N', plinth='Concrete', extra=3.0)
    for k in range(4):
        x = x0 + 1.3 + k * 2.2
        box(g, 'DarkMetal', (x, 1.15, z0 + .55), (2.1, 2.3, .8), solid=True); decal(g, 'Fridge', (x, 1.15, z0 + .96), 0, 2.0, 2.1, .01)
    for k in range(2):
        x = x0 + 2.6 + k * 2.6
        box(g, 'DarkMetal', (x, .8, z0 + 4.2), (.9, 1.6, 3.4), solid=True)
        for s in (-90, 90): decal(g, 'ShelfGoods', (x + (.46 if s == 90 else -.46), .85, z0 + 4.2), s, 3.3, 1.4, .01)
    box(g, 'WhitePaint', (x0 + 1.6, .6, z1 - 1.8), (1.0, 1.0, 2.6), solid=True); box(g, 'Concrete', (x0 + 1.6, 1.13, z1 - 1.8), (1.2, .06, 2.8))
    decal(g, 'LogoU', (x0 + 2.12, .6, z1 - 1.8), 90, .8, .8, .01)
    for k in range(2):
        for j in range(2): box(g, 'LightPanel', (x0 + 2.5 + k * 4, 3.34, z0 + 2.5 + j * 4.5), (1.2, .04, .6))
    sound('Store chiller hum', (x0 + 4, 1.5, z0 + 1), 'chiller_hum.wav', .35, 12, True, 1)
    decal('Gas store', 'Fascia', (x1 + .17, 3.4, (z0 + z1) / 2), 90, z1 - z0, .9, 0)
    decal('Gas store', 'Fascia', ((x0 + x1) / 2, 3.4, z0 - .17), 180, x1 - x0, .9, 0)
    decal('Gas store', 'LogoU', (x0 - .17, 2.6, (z0 + z1) / 2), -90, 1.4, 1.4, 0)
    # Canopy: barrel vault running north-south, blue columns on the minimap's pillar marks.
    g = 'Gas canopy'
    cx0, _, cz0 = P(1392, 1144); cx1, _, cz1 = P(1500, 1270)
    W = cx1 - cx0; segs = 12; prof = lambda t: (cx0 + W * t, 5.6 + 1.3 * math.sin(math.pi * t))
    for k in range(segs):
        xa, ya = prof(k / segs); xb, yb = prof((k + 1) / segs)
        poly(g, 'CanopyMetal', [(xa, ya, cz0), (xb, yb, cz0), (xb, yb, cz1), (xa, ya, cz1)], None, (0, 1, 0))
        poly(g, 'CanopyMetal', [(xa, ya - .25, cz0), (xb, yb - .25, cz0), (xb, yb - .25, cz1), (xa, ya - .25, cz1)], None, (0, -1, 0))
        for zz in (cz0, cz1): poly(g, 'ColumnBlue', [(xa, ya - .25, zz), (xa, ya, zz), (xb, yb, zz), (xb, yb - .25, zz)], None, (0, 0, 1 if zz == cz1 else -1))
        for zz in (cz0 + 1.5, (cz0 + cz1) / 2, cz1 - 1.5): beam(g, 'ColumnBlue', (xa, ya - .35, zz), (xb, yb - .35, zz), .25, .3)
    for px, py in [(1456, 1146), (1455, 1205), (1453, 1266), (1400, 1146), (1400, 1266)]:
        box(g, 'ColumnBlue', P(px, py, 2.9), (.4, 5.8, .4), solid=True)
    for k in range(5):
        box(g, 'LightPanel', (cx0 + 1.2 + k * 1.7, 6.45 + (1.0 if 1 <= k <= 3 else 0), (cz0 + cz1) / 2), (.35, .04, 1.0))
    x, _, z = P(1455, 1205)
    box(g, 'Concrete', (x, .1, z), (1.4, .2, 4.4), solid=True)
    for dz in (-1.2, 1.2):
        box(g, 'WhitePaint', (x, .95, z + dz), (.6, 1.5, .5), solid=True); box(g, 'ColumnBlue', (x, 1.75, z + dz), (.65, .15, .55))
        decal(g, 'LogoU', (x + .31, 1.35, z + dz), 90, .3, .3, .01); decal(g, 'LogoU', (x - .31, 1.35, z + dz), -90, .3, .3, .01)
    for zz in (cz0 + 2, (cz0 + cz1) / 2, cz1 - 2):
        prev = None
        for k in range(17):
            t = k / 16; p = (cx0 + .5 + (W - 1) * t, 5.1 - .4 * 4 * t * (1 - t), zz)
            if prev: beam('Gas bunting', 'Rubber', prev, p, .015)
            if k % 2 == 0 and k < 16:
                poly('Gas bunting', 'Flags', [p, (p[0] + .5, p[1] - .03, zz), (p[0] + .25, p[1] - .4, zz)], [((k // 2 % 8) / 8, 0), ((k // 2 % 8 + 1) / 8, 0), ((k // 2 % 8 + .5) / 8, 1)], (0, 0, 1))
            prev = p
    x, _, z = P(1560, 1145)
    box(g, 'ColumnBlue', (x, 3, z), (.35, 6, .35), solid=True); box(g, 'YellowPaint', (x, 6.6, z), (.3, 1.8, 1.8))
    decal(g, 'LogoU', (x + .16, 6.6, z), 90, 1.5, 1.5, .01); decal(g, 'LogoU', (x - .16, 6.6, z), -90, 1.5, 1.5, .01)
    crates('Gas props', 1300, 1135, 0, 4, 14)
    hay_stack('Gas props', 1250, 1105, 0, [(0, 0, 0, 0), (1.2, 0, 0, 2), (0, 0, .58, 0), (1.2, 0, .58, -3), (.6, 1, .3, 10)])
    # Brick warehouse just beyond the east boundary (reference shot 5, right).
    house('Brick warehouse', 1600, 980, 1720, 1380, 2, 'Brick', 'Slate', 'z', 3.0, 'WindowBarred', {'W': (2, 6)}, gable='Brick')
    x, _, z = P(1600, 1150); decal('Brick warehouse', 'SignBrick', (x - .06, 5.1, z), -90, 5, 1.25, 0)

def build_plaza():
    g = 'Plaza monument'
    x, _, z = P(856, 760)
    cylinder(g, 'Stone', (x, 0, z), (x, .45, z), 1.6, 16); cylinder(g, 'Concrete', (x, .45, z), (x, .55, z), 1.7, 16)
    collider((x, .3, z), (3.0, .6, 3.0))
    box(g, 'StoneDark', (x, 1.6, z), (1.1, 2.1, 1.1), solid=True); box(g, 'Concrete', (x, 2.75, z), (1.4, .2, 1.4))
    blob(g, 'DarkMetal', (x, 3.6, z), (.35, .75, .3), 'statue', .1, 8, 6); blob(g, 'DarkMetal', (x, 4.55, z), (.22, .25, .22), 'head', .05, 8, 5)
    beam(g, 'DarkMetal', (x + .2, 4.1, z), (x + .7, 4.9, z - .2), .12)
    # Vehicles exactly where the minimap draws them.
    tank('Plaza', 768, 792, -147, 30); tank('West road', 616, 1191, -62, -25)
    van('Vehicles', 1255, 627, 7, 'VanDark'); sedan('Vehicles', 1044, 720, 90)
    sedan('Vehicles', 1310, 878, 0, 'VanBlue'); sedan('Vehicles', 795, 938, -5)
    van('Vehicles', 883, 1028, 21); van('Vehicles', 870, 1153, -21); van('Vehicles', 548, 1321, 0)
    tractor('Vehicles', 1215, 708, 63)
    x, _, z = P(1250, 726); box('Vehicles', 'PlanksDark', (x, .9, z), (2.2, .2, 3.4), 63, True)
    hay_stack('Vehicles', 1250, 726, 63, [(0, 1.5, 0, 0), (0, 1.5, .6, 0)])
    for px, py, yaw in [(900, 690, 0), (1180, 700, 0), (1280, 1000, 90), (1000, 1260, 180), (600, 1150, 90), (820, 1180, 270), (1460, 830, 270), (560, 900, 90)]:
        lamp('Street lamps', px, py, yaw)
    for px, py in [(560, 860), (800, 700), (1000, 700), (1240, 790), (1300, 1050), (1500, 1100), (540, 1200), (560, 1500)]:
        pole('Power lines', px, py)
    wires('Power lines', [0, 1, 2, 3, 4, 5]); wires('Power lines', [0, 6, 7])

def barrel(g, x, z, paint, tipped=False):
    if tipped: cylinder(g, paint, (x - .45, .3, z), (x + .45, .3, z), .3, 10)
    else:
        cylinder(g, paint, (x, 0, z), (x, .9, z), .3, 10)
        for y in (.3, .6): cylinder(g, 'DarkMetal', (x, y, z), (x, y + .03, z), .31, 10, False)

def build_clutter():
    rnd = random.Random(99)
    for px, py, paint in [(760, 580, 'RedPaint'), (1150, 705, 'DumpsterGreen'), (1300, 700, 'ColumnBlue'), (560, 985, 'RedPaint'),
                          (1000, 1290, 'DumpsterGreen'), (1470, 1010, 'ColumnBlue'), (1230, 1150, 'RedPaint'), (930, 1460, 'DumpsterGreen')]:
        x, _, z = P(px, py); barrel('Clutter', x, z, paint, rnd.random() < .3); collider((x, .45, z), (.6, .9, .6))
    for px, py in [(520, 1180), (1250, 1080), (1100, 1300), (640, 860)]:
        x, _, z = P(px, py)
        for k in range(rnd.randint(2, 4)): cylinder('Clutter', 'Rubber', (x + k * .1, k * .25, z), (x + k * .1, k * .25 + .24, z), .38, 12)
    for px, py, yaw in [(1100, 1280, 15), (620, 1520, 70), (1025, 1288, 30), (900, 830, 0), (1480, 1050, 90)]:
        x, _, z = P(px, py)
        for k in range(rnd.randint(1, 3)): box('Clutter', 'Planks', (x, .07 + k * .14, z), (1.2, .12, 1.0), yaw + rnd.uniform(-6, 6))
    for k in range(70):
        px, py = rnd.uniform(1180, 1320), rnd.uniform(760, 900)
        if inside([(1118, 808), (1206, 808), (1206, 930), (1118, 930)], px, py): continue
        x, _, z = P(px, py); sz = rnd.uniform(.08, .3)
        box('Street debris', rnd.choice(['Stone', 'StoneDark', 'Concrete', 'Brick']), (x, sz * .35, z), (sz, sz * .7, sz * 1.3), rnd.uniform(0, 90))
    for px, py, y in [(1080, 640, 6.8), (1460, 940, 5.2), (700, 1000, 6.9), (930, 1300, 4.5)]:
        x, _, z = P(px, py)
        cylinder('Rooftop details', 'Steel', (x, y, z), (x, y + .5, z), .03, 6)
        cylinder('Rooftop details', 'WhitePaint', (x, y + .5, z), (x + .12, y + .6, z + .25), .45, 12, True, .05)
        beam('Rooftop details', 'Steel', (x, y, z), (x, y + 2.2, z), .03)
        for k in range(4): beam('Rooftop details', 'Steel', (x - .6, y + 1.4 + k * .22, z), (x + .6, y + 1.4 + k * .22, z), .02)
    for k, (px, py, s) in enumerate([(520, 900, 1.1), (560, 1460, 1.2), (900, 1470, 1.0), (1230, 1090, .9), (1520, 1080, 1.0), (680, 1600, 1.2)]):
        tree('Trees', px, py, s, 30 + k)

def build_backdrop():
    g = 'Mountains'
    rnd = random.Random(42); seg = 144
    raw = [80 + 40 * math.sin(k * .19) + 22 * math.sin(k * .53 + 1) + rnd.uniform(-6, 6) for k in range(seg)]
    heights = [(raw[k - 1] + 2 * raw[k] + raw[(k + 1) % seg]) / 4 for k in range(seg)]
    rings = [(220, 0), (270, .2), (330, .5), (400, .8), (480, 1.0), (570, .9)]
    def pt(k, r):
        a = k % seg * math.tau / seg; radius, f = rings[r]; return (math.cos(a) * radius, heights[k % seg] * f - 1, math.sin(a) * radius)
    for k in range(seg):
        for r in range(len(rings) - 1):
            q = [pt(k, r), pt(k + 1, r), pt(k + 1, r + 1), pt(k, r + 1)]
            poly(g, 'Forest' if r < 2 else 'ForestFar', q, None, (-q[0][0], 300, -q[0][2]))
    rnd = random.Random(3)
    for k in range(90):
        a = rnd.random() * math.tau; r = rnd.uniform(68, 140)
        x, z = math.cos(a) * r, math.sin(a) * r
        if inside(BOUNDARY, x / S + 1020, z / S + 1020): continue
        if rnd.random() < .5: conifer('Treeline', x, z, rnd.uniform(9, 15), k)
        else:
            s = rnd.uniform(1.2, 1.8); rad = 3 * s
            cylinder('Treeline', 'Bark', (x, 0, z), (x, 5 * s, z), .3 * s, 6, False)
            blob('Treeline', 'LeavesDark', (x, 6.5 * s, z), (rad, rad * .9, rad), f'bg{k}', .3)
    for k, (px, py) in enumerate([(300, 700), (280, 1100), (300, 1400), (760, 1820), (1200, 1500), (1720, 700), (1700, 520), (850, 300), (1500, 250)]):
        house('Distant houses', px, py, px + 90, py + 70, 1 + k % 2, ['PlasterCream', 'PlasterWhite', 'PlasterOchre'][k % 3], ['Slate', 'Terracotta'][k % 2], 'x', 2.4, 'WindowPlain', solid=False)
    # Water tower and windmill beyond the north boundary (reference shots 3 and 4).
    g = 'Water tower'
    x, _, z = P(620, 330)
    for a in range(4):
        ang = a * math.tau / 4 + math.pi / 4; beam(g, 'Steel', (x + math.cos(ang) * 2.6, 0, z + math.sin(ang) * 2.6), (x + math.cos(ang) * 1.8, 11, z + math.sin(ang) * 1.8), .22)
    for y in (3.5, 7.5): cylinder(g, 'Steel', (x, y, z), (x, y + .12, z), 2.4, 8)
    cylinder(g, 'PlasterGrey', (x, 11, z), (x, 14.5, z), 2.6, 16); cylinder(g, 'Slate', (x, 14.5, z), (x, 16, z), 2.8, 16, True, .2)
    g = 'Windmill'
    x, _, z = P(1080, 230)
    for sx in (-1, 1):
        for sz in (-1, 1): beam(g, 'Steel', (x + sx * 1.6, 0, z + sz * 1.6), (x + sx * .25, 13, z + sz * .25), .1)
    for y in range(2, 13, 3):
        s = 1.6 - y / 13 * 1.35
        for a, b in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            beam(g, 'Steel', (x + a[0] * s, y, z + a[1] * s), (x + b[0] * s, y, z + b[1] * s), .06)
    hub = (x, 13.4, z + .5)
    for k in range(14):
        a = k * math.tau / 14
        poly(g, 'Steel', [hub, add(hub, (math.cos(a) * 2.6, math.sin(a) * 2.6, 0)), add(hub, (math.cos(a + .25) * 2.6, math.sin(a + .25) * 2.6, 0))], None, (0, 0, 1))

TOWER_FIRE = (0, 12, 0)
def build_effects():
    fire = TOWER_FIRE
    particles('Tower fire', fire, 'TowerFire')
    particles('Tower fire core', add(fire, (-.4, -.3, .3)), 'TowerFireCore')
    particles('Tower embers', add(fire, (0, .4, 0)), 'Embers')
    particles('Tower smoke', add(fire, (.2, 1.2, 0)), 'TowerSmoke')
    point_light('Tower fire light', add(fire, (1.4, 0, 0)), (1, .55, .2), 22, 16)
    sound('Tower fire crackle', fire, 'fire_crackle.wav', .9, 45, True, 3)
    for name, pos in [('Distant smoke east', (160, 2, -120)), ('Distant smoke north', (-60, 4, -220))]:
        particles(name, pos, 'DistantSmoke')
    x, _, z = P(1395, 600); particles('Barn smoulder', (x, .3, z), 'Smoulder')
    x, _, z = P(700, 930); particles('Chimney smoke', (x, 2 * FLOOR + 7.0, z), 'ChimneySmoke')
    sound('Village wind', (0, 6, 0), 'wind_loop.wav', .28, 400, False, 50)

def build_volumes():
    xs = [p[0] for p in BOUNDARY]; ys = [p[1] for p in BOUNDARY]
    x0, _, z0 = P(min(xs), min(ys)); x1, _, z1 = P(max(xs), max(ys))
    ambient_volume('Ambient Light Volume Exterior', ((x0 + x1) / 2, 8, (z0 + z1) / 2), (x1 - x0 + 4, 18, z1 - z0 + 4), 3.0)
    audio_volume('Audio Exterior Field', ((x0 + x1) / 2, 10, (z0 + z1) / 2), (x1 - x0 + 20, 30, z1 - z0 + 20), 'Field', 0, .05, .45, .15, .35)
    cx, cz, w, d = R(925, 800, 1260, 1240)
    audio_volume('Audio Village Streets', (cx, 6, cz), (w, 14, d), 'City', 1, .14, .9, .3, .6, .05, .09, .12)
    presets = {'Barn': ('Room', .2, .7), 'Church hall': ('Hall', .35, 1.4)}
    for name, outline in INTERIORS:
        px = [p[0] for p in outline]; py = [p[1] for p in outline]
        cx, cz, w, d = R(min(px), min(py), max(px), max(py))
        ambient_volume('Ambient Light Volume ' + name, (cx, 2.0, cz), (w, 4.0, d), .9)
        preset, wet, decay = presets.get(name, ('Room', .3, .8))
        audio_volume('Audio ' + name, (cx, 2.0, cz), (w, 4.0, d), preset, 3, wet, decay, .5, .7, 0, .1, .1, .05, 1)
    cx, cz, w, d = R(1392, 1144, 1500, 1270); audio_volume('Audio Gas Canopy', (cx, 3.5, cz), (w, 7, d), 'Custom', 2, .16, .9, .35, .5, .08, .1, .15, 0, 2)
    cx, cz, w, d = R(1152, 808, 1204, 858); audio_volume('Audio Church Tower', (cx, 8, cz), (w, 16, d), 'Hall', 3, .45, 2.2, .6, .8, .15, .15, .25, 0, 1)

# ------------------------------------------------------------------ particles & audio assets
FIRE_COMMON = 'ParticleSystemVersion=2\nPlayOnAwake=true\nLooping=true\nDuration=5\nSimulationSpace=World\nCollisionEnabled=false\nTrailsEnabled=false\n'
PARTICLES = {
    'TowerFire': FIRE_COMMON + 'MaxParticles=400\nStartLifetime=0.9\nLifetimeVariation=0.35\nStartSpeed=2.2\nSpeedVariation=0.6\nStartSize=1.3\nSizeVariation=0.4\n'
        'StartColor=4.5,1.5,0.25,0.9\nColorOverLifetimeEnabled=true\nEndColor=1.2,0.25,0.05,0\nSizeOverLifetimeEnabled=true\nEndSize=0.35\nGravityModifier=-0.25\n'
        'EmissionRateOverTime=90\nShape=Box\nShapeSize=1.0,0.6,3.0\nShapeRadius=0.8\nConeAngle=18\nTurbulenceStrength=1.5\nTurbulenceFrequency=1.2\n'
        'StartRotationVariation=180\nRotationSpeedVariation=90\nRenderShape=Quad\nRenderMode=Billboard\nWindVelocity=0.8,0,0.2\n'
        'MaterialAsset=project://Materials/Grenades/Particle.plutomaterial\n',
    'TowerFireCore': FIRE_COMMON + 'MaxParticles=200\nStartLifetime=0.5\nLifetimeVariation=0.3\nStartSpeed=1.2\nStartSize=0.8\nSizeVariation=0.3\n'
        'StartColor=6,3.2,0.9,1\nColorOverLifetimeEnabled=true\nEndColor=3,0.8,0.1,0\nSizeOverLifetimeEnabled=true\nEndSize=0.2\nGravityModifier=-0.2\n'
        'EmissionRateOverTime=70\nShape=Box\nShapeSize=0.8,0.3,2.4\nRenderShape=Quad\nRenderMode=Billboard\nStartRotationVariation=180\n'
        'MaterialAsset=project://Materials/Grenades/Particle.plutomaterial\n',
    'Embers': FIRE_COMMON + 'MaxParticles=200\nStartLifetime=2.6\nLifetimeVariation=0.5\nStartSpeed=2.5\nSpeedVariation=1.2\nStartSize=0.07\nSizeVariation=0.4\n'
        'StartColor=6,2.2,0.4,1\nColorOverLifetimeEnabled=true\nEndColor=2,0.4,0.05,0\nGravityModifier=-0.15\nEmissionRateOverTime=24\nShape=Cone\nConeAngle=35\n'
        'ShapeRadius=1.0\nTurbulenceStrength=2.5\nTurbulenceFrequency=1.5\nWindVelocity=1.2,0,0.3\nRenderShape=Quad\nRenderMode=Billboard\n'
        'MaterialAsset=project://Materials/Grenades/Particle.plutomaterial\n',
}
def smoke(color, rate, life, size, end_size, speed, wind, density, radius=.6, max_particles=600, cone=14):
    return (FIRE_COMMON + f'MaxParticles={max_particles}\nStartLifetime={life}\nLifetimeVariation=0.3\nStartSpeed={speed}\nSpeedVariation=0.3\nStartSize={size}\n'
        f'SizeVariation=0.35\nStartColor={color}\nColorOverLifetimeEnabled=true\nEndColor={color.rsplit(",", 1)[0]},0\nSizeOverLifetimeEnabled=true\nEndSize={end_size}\n'
        f'GravityModifier=0\nDrag=0.05\nBuoyancy=0.35\nWindVelocity={wind}\nTurbulenceStrength=1.2\nTurbulenceFrequency=0.35\nStartRotationVariation=180\n'
        f'FadeInFraction=0.12\nFadeOutFraction=0.35\nEmissionRateOverTime={rate}\nShape=Cone\nConeAngle={cone}\nShapeRadius={radius}\nRenderShape=Circle\n'
        f'RenderMode=Volumetric\nSmokeLightingEnabled=true\nSmokeLightingStrength=0.55\nSmokeAmbient=0.05\nVolumeDensity={density}\nVolumeNoiseStrength=0.8\n'
        'VolumeNoiseFrequency=2.5\nVolumeEdgeSoftness=6\nVolumeSelfShadow=4\nSoftParticlesEnabled=true\nSoftParticleDistance=1.5\nMaterialAsset=\n')
PARTICLES['TowerSmoke'] = smoke('0.07,0.065,0.06,0.9', 9, 12, 2.4, 9.0, 2.4, '2.2,0,0.6', .65)
PARTICLES['DistantSmoke'] = smoke('0.12,0.115,0.11,0.85', 5, 26, 14, 40, 4, '3,0,1', .35, 4, 300, 10)
PARTICLES['ChimneySmoke'] = smoke('0.5,0.5,0.5,0.45', 3, 8, .8, 4.0, 1.2, '1.5,0,0.4', .25, .25, 120, 8)
PARTICLES['Smoulder'] = smoke('0.2,0.19,0.18,0.6', 3, 9, 1.5, 6.0, .8, '1.4,0,0.4', .3, 1.0, 150, 25)

def build_particles():
    for name, text in PARTICLES.items(): save(f'Particles/Standoff/{name}.plutoparticles', text)

def write_wav(rel, samples, rate=44100):
    data = np.clip(samples, -1, 1)
    b = io.BytesIO()
    with wave.open(b, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate); w.writeframes((data * 32000).astype('<i2').tobytes())
    save(rel, b.getvalue())

def build_audio():
    rate = 44100; n = rate * 10; r = np.random.default_rng(5)
    def lowpass(x, a):
        y = np.empty_like(x); acc = 0.0
        for i in range(len(x)): acc += a * (x[i] - acc); y[i] = acc
        return y
    def loop(x, fade=rate // 2):
        """Crossfade the tail into the head so the clip loops without a seam."""
        t = np.linspace(0, 1, fade); head = x[:fade] * t + x[-fade:] * (1 - t); return np.concatenate([head, x[fade:-fade]])
    # Fire: low roar plus random crackles and pops.
    roar = lowpass(r.normal(0, 1, n), .02); roar /= np.abs(roar).max()
    crack = np.zeros(n)
    for _ in range(900):
        i = r.integers(0, n - 2000); l = r.integers(80, 1200); env = np.exp(-np.linspace(0, 8, l))
        crack[i:i + l] += r.normal(0, 1, l) * env * r.uniform(.1, .9)
    fire = roar * .55 * (0.8 + .2 * np.sin(np.linspace(0, 40, n))) + crack * .5
    write_wav('Sounds/Ambient/fire_crackle.wav', loop(fire / np.abs(fire).max() * .8))
    # Wind: slowly gusting filtered noise.
    w = lowpass(r.normal(0, 1, n), .01); gust = lowpass(r.normal(0, 1, n), .00002); gust = (gust - gust.min()) / (np.ptp(gust) + 1e-9)
    wind = w / np.abs(w).max() * (.35 + .65 * gust)
    write_wav('Sounds/Ambient/wind_loop.wav', loop(wind * .8))
    # Refrigeration hum.
    t = np.arange(n) / rate
    hum = .5 * np.sin(math.tau * 100 * t) + .25 * np.sin(math.tau * 200 * t) + .1 * np.sin(math.tau * 300 * t) + .05 * r.normal(0, 1, n)
    write_wav('Sounds/Ambient/chiller_hum.wav', loop(hum * .5))

# ------------------------------------------------------------------ export
def export(group, parts):
    verts = []; subs = []; refs = []
    for m, points in parts.items():
        if not points: continue
        start = len(verts); verts += points; refs.append('project://Materials/Standoff/' + m + '.plutomaterial'); subs.append((start, len(points), len(refs) - 1))
    data = bytearray()
    def pod(fmt, *vs): data.extend(struct.pack('<' + fmt, *vs))
    def string(s): b = s.encode(); pod('Q', len(b)); data.extend(b)
    pod('IIQ', 0x4d47504c, 5, len(verts))
    for v in verts: pod('14f4i4f', *v)
    pod('Q', len(verts)); pod(str(len(verts)) + 'I', *range(len(verts))); pod('Q', len(subs))
    for start, count, mi in subs:
        pts = np.array([v[:3] for v in verts[start:start + count]]); centre = pts.mean(0); radius = float(np.linalg.norm(pts - centre, axis=1).max())
        pod('IIIi4f', start, count, mi, -1, *centre, radius); string(group + ' ' + str(mi)); pod('Q', 0)
    pod('BQQQ', 0, 0, 0, 0); pod('Q', len(refs))
    for r in refs: string(r)
    string(''); string(''); pod('QBBB', 0, 0, 0, 0)
    rel = 'Meshes/Standoff/' + re.sub('[^a-zA-Z0-9_-]', '_', group) + '.plutomesh'; save(rel, bytes(data)); i = entity(group)
    add_scene(comp(i, 'MeshComponent', [('Static', 4, 'true'), ('Visible', 4, 'true'), ('MeshAssetReference', 2, 'project://' + rel), ('SubmeshIndex', 1, -1), ('UseGeneratedLods', 4, 'false')]))
    return len(verts) // 3

BAKER = Path('C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc18/tests/Release/PlutoGERenderSceneShot.exe')
def bake_ambient(text):
    """Bake every Ambient Light Volume headlessly and copy only the baked probe data back.
    Without the tool the volumes stay unbaked; use File > Bake Ambient Grid in the editor."""
    import subprocess
    if not BAKER.exists():
        print('Ambient volumes left unbaked: build PlutoGERenderSceneShot or bake in the editor.'); return text
    work = ROOT / 'Tools/Native'; work.mkdir(parents=True, exist_ok=True)
    source, baked = work / 'Standoff-unbaked.plutoscene', work / 'Standoff-baked.plutoscene'
    source.write_text(text)
    result = subprocess.run([str(BAKER), str(ROOT), str(source), '--bake-ambient', str(baked)], capture_output=True, text=True)
    if result.returncode != 0:
        print('Ambient bake failed:', result.stderr.strip()[-400:]); return text
    print(result.stdout.strip().splitlines()[0])
    data = {}
    for block in re.split(r'(?=^ENTITY\t)', baked.read_text(), flags=re.M):
        line = next((l for l in block.splitlines() if l.startswith('PROPERTY\tBaked Data\t')), None)
        if line: data[block.split('\t')[4]] = line
    def inject(match):
        block = match.group(0); name = block.split('\t')[4]
        return block.replace('PROPERTY\tDirection Count\t1\t128\t0\n', 'PROPERTY\tDirection Count\t1\t128\t0\n' + data[name] + '\n') if name in data else block
    return re.sub(r'^ENTITY\t\d+\t0\t1\tAmbient Light Volume[^\n]*\n(?:(?!ENTITY\t).*\n)*', inject, text, flags=re.M)

def main():
    global scene
    source = (A / 'Scenes/Main.plutoscene').read_text(); blocks = re.split(r'(?=^ENTITY\t)', source, flags=re.M)[1:]
    byid = {int(b.split('\t')[1]): b for b in blocks}; parents = {i: int(b.split('\t')[2]) for i, b in byid.items()}
    keep = {1, 5, 6, 707, 1784}
    def retained(i): return i in keep or (parents[i] != 0 and retained(parents[i]))
    scene = 'SCENE\t1\n' + ''.join(b for i, b in byid.items() if retained(i))
    spawn = P(565, 1650, 1.2)
    scene = re.sub(r'(^ENTITY\t1\t0\t1\tPlayer\t)[^\t]+\t[^\t]+', r'\g<1>' + vec(spawn) + '\t0,0,0', scene, flags=re.M)
    names = ['Gas Station', 'Market', 'Church', 'Courtyard', 'Warehouse']
    scene = scene.replace('Hardpoint Courtyard|Hardpoint West|Hardpoint East', '|'.join('Hardpoint ' + n for n in names))
    for key, val in [('hardpointRadius', '3.5'), ('botFallbackSpawnMinimumDistance', '10'), ('botFallbackSpawnRadius', '40')]:
        scene = re.sub(r'(^PROPERTY\t' + key + r'\t0\t)[^\t]+', r'\g<1>' + val, scene, flags=re.M)
    for key, v in [('Bounds Minimum', '-46,-1,-58'), ('Bounds Maximum', '46,14,58'), ('Cell Size', '0.4')]:
        scene = re.sub(r'(^PROPERTY\t' + key + r'\t\d\t)[^\t]+', r'\g<1>' + v, scene, flags=re.M)
    # Late-morning sun from the south-east, slightly hazy like the reference.
    el = math.radians(40); sun = vec(norm((-math.cos(el) * .7071, -math.sin(el), -math.cos(el) * .7071)))
    start = scene.index('ENTITY\t6\t0\t1\tDirectional Light'); end = scene.index('END_COMPONENT', start)
    scene = scene[:start] + re.sub(r'(PROPERTY\tDirection\t3\t)[^\t]+', r'\g<1>' + sun, scene[start:end], count=1) + scene[end:]
    # Softer, hazier daylight like the reference captures.
    scene = scene.replace('PROPERTY\tMie Strength\t0\t0.650000\t0', 'PROPERTY\tMie Strength\t0\t1.300000\t0', 1)
    scene = scene.replace('PROPERTY\tCoverage\t0\t0.500000\t0', 'PROPERTY\tCoverage\t0\t0.620000\t0', 1)
    start = scene.index('ENTITY\t6\t0\t1\tDirectional Light')
    scene = scene[:start] + scene[start:].replace('PROPERTY\tIntensity\t0\t20.000000\t0', 'PROPERTY\tIntensity\t0\t15.000000\t0', 1).replace(
        'PROPERTY\tColor\t3\t1.000000,0.900000,0.800000\t0', 'PROPERTY\tColor\t3\t1.000000,0.940000,0.860000\t0', 1)
    assert 'PROPERTY\tnavigationMesh\t9\t7243' in scene
    build_materials(); build_particles(); build_audio()
    # Meshes from earlier layouts would otherwise linger in the project.
    stale = A / 'Meshes/Standoff'
    if stale.exists():
        for f in stale.glob('*.plutomesh*'): f.unlink()
    build_ground(); build_boundary(); build_market(); build_barn(); build_warehouse(); build_church(); build_compound()
    build_south(); build_gas_station(); build_plaza(); build_clutter(); build_backdrop(); build_effects(); build_volumes()
    for name, (px, py) in [('North West', (820, 530)), ('North East', (1290, 420)), ('South East', (1530, 1000)), ('South West', (565, 1650))]:
        entity('Spawn ' + name, P(px, py, 1))
    for n, (name, (px, py, y)) in enumerate(zip(names, [(1425, 1205, .2), (1060, 645, .2), (1120, 1090, .2), (628, 952, .2), (740, 1320, .2)])):
        i = entity('Hardpoint ' + name, P(px, py, y))
        marker = byid[50101].replace('50101', str(i + 1)).replace('50100', str(i))
        marker = marker.replace('PROPERTY\tsiteIndex\t1\t0', 'PROPERTY\tsiteIndex\t1\t' + str(n))
        add_scene(marker); global nextid; nextid += 1
    # Interior points the navigation smoke test must reach (one per enterable building).
    for k, (px, py) in enumerate([(1380, 600), (1450, 890), (1180, 1000), (760, 1430), (920, 1320), (1365, 1175), (1060, 560)]):
        entity(f'Nav Check {k}', P(px, py, .2))
    triangles = sum(export(g, parts) for g, parts in meshes.items() if any(parts.values()))
    scene = bake_ambient(scene)
    save('Scenes/Standoff.plutoscene', scene)
    manifest = ROOT / 'CoDplutoproject.plutoproject'; lines = manifest.read_text().splitlines()
    kinds = {'.plutoscene': 'Scene', '.plutomesh': 'Mesh', '.plutomaterial': 'Material', '.png': 'Texture', '.plutoparticles': 'Particle System', '.wav': 'Audio'}
    lines = [l for l in lines if not (l.startswith('ASSET\tproject://Meshes/Standoff/') and not (A / l.split('\t')[1][len('project://'):]).exists())]
    for p in generated:
        ref = 'project://' + p.relative_to(A).as_posix(); entry = f'ASSET\t{ref}\t{p.stat().st_size}\t{kinds[p.suffix]}'
        existing = next((i for i, l in enumerate(lines) if l.startswith('ASSET\t' + ref + '\t')), None)
        if existing is None: lines.append(entry)
        else: lines[existing] = entry
    include = 'BUILD_ALWAYS_INCLUDE\tproject://Scenes/Standoff.plutoscene'
    if include not in lines:
        at = next(i for i, l in enumerate(lines) if l == 'BUILD_ALWAYS_INCLUDE\tproject://Scenes/Shipment.plutoscene') + 1
        lines.insert(at, include)
    manifest.write_text('\n'.join(lines) + '\n')
    print(f'Standoff: {len(meshes)} mesh batches, {triangles} triangles, {colliders} colliders, 5 hardpoints, 4 spawns; {len(generated)} assets.')

if __name__ == '__main__':
    main()
