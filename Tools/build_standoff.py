"""Generate a Standoff-inspired Hardpoint village with native LPGM geometry.

Reference: user-supplied Black Ops II "Standoff" screenshots and aerial view. The plan is traced
from the aerial in pixel coordinates (1053 x 603 display crop) and converted with S metres per
pixel; north (top of the aerial) is -Z. All geometry, textures and audio are generated here
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
S = .105
def P(px, py, y=0.0): return ((px - 526) * S, y, (py - 300) * S)
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
def build_ground():
    g = 'Ground'
    box(g, 'Grass', (0, -.5, 0), (420, 1, 420), render=False, solid=True)
    for i in range(-5, 5):
        for j in range(-5, 5):
            x0, z0 = i * 40, j * 40
            poly('Terrain grass', 'Grass', [(x0, 0, z0), (x0 + 40, 0, z0), (x0 + 40, 0, z0 + 40), (x0, 0, z0 + 40)], None, (0, 1, 0))
    # Furrowed field (top-left of the aerial) continuing north beyond the boundary.
    poly('Farm field', 'Field', [P(-60, -260, .02), P(275, -260, .02), P(265, 95, .02), P(-60, 70, .02)], None, (0, 1, 0))
    poly('Farm field', 'Field', [P(560, -420, .02), P(900, -420, .02), P(900, -140, .02), P(560, -140, .02)], None, (0, 1, 0))
    # Worn dirt track curving down the west meadow.
    path = [(-40, 160), (40, 190), (110, 240), (160, 300), (190, 345)]
    for (a, b) in zip(path, path[1:]):
        ax, _, az = P(*a); bx, _, bz = P(*b); dx, dz = bx - ax, bz - az; l = math.hypot(dx, dz); nx, nz = -dz / l * 1.7, dx / l * 1.7
        poly('Farm field', 'Dirt', [(ax - nx, .015, az - nz), (bx - nx, .015, bz - nz), (bx + nx, .015, bz + nz), (ax + nx, .015, az + nz)], None, (0, 1, 0))
    # Asphalt road network traced from the aerial.
    for r in [(330, 40, 565, 195), (455, 195, 510, 425), (180, 355, 720, 425), (700, 140, 770, 470), (565, 145, 700, 205),
              (-60, 335, 180, 385), (215, 130, 255, 355), (290, -40, 335, 140), (700, 470, 770, 650), (600, 40, 700, 145)]:
        ground_rect('Roads', 'Asphalt', *r, .03)
    ground_rect('Roads', 'Concrete', 615, 425, 955, 650, .035)
    ground_rect('Roads', 'Concrete', 770, 190, 958, 205, .035)
    # Cobbled sidewalks and yards.
    for r in [(515, 195, 700, 300), (510, 300, 520, 500), (330, 0, 565, 40), (245, 145, 452, 350), (680, 300, 700, 500), (430, 425, 510, 460)]:
        ground_rect('Sidewalks', 'Cobble', *r, .05)
    ground_rect('Yards', 'Dirt', 100, 340, 215, 540, .025)
    ground_rect('Yards', 'Dirt', 230, 390, 380, 600, .028)
    ground_rect('Yards', 'Dirt', 780, 30, 930, 200, .025)
    # Yellow-painted kerbs like the reference streets.
    for a, b in [((330, 195), (455, 195)), ((510, 195), (565, 195)), ((455, 195), (455, 355)), ((510, 205), (510, 355)),
                 ((510, 355), (700, 355)), ((255, 355), (455, 355)), ((180, 425), (430, 425)), ((510, 425), (615, 425)),
                 ((700, 205), (700, 355)), ((770, 140), (770, 200)), ((565, 145), (565, 195)), ((330, 40), (330, 140))]:
        curb('Kerbs', a, b)

def build_compound():
    g = 'Chimney house'
    cx, cz, w, d, H = house(g, 290, 205, 440, 290, 2, 'PlasterCream', 'Slate', 'x', 2.8, 'WindowGreen', {'N': (1, 3), 'S': (2,)}, gable='PlasterCream')
    # Tall brick chimney and the covered porch with blue doors.
    x, _, z = P(305, 222); box(g, 'Brick', (x, (H + 6) / 2, z), (1.2, H + 6, 1.2), solid=True); box(g, 'Concrete', (x, H + 6.1, z), (1.45, .25, 1.45))
    x0, _, z0 = P(305, 205)
    for k in range(5):
        px = 305 + k * 30; x, _, z = P(px, 191); beam(g, 'PlanksDark', (x, 0, z), (x, 2.9, z), .16)
    pcx, pcz, pw, pd = R(295, 186, 435, 205)
    poly(g, 'Corrugated', [(pcx - pw / 2, 2.95, pcz - pd / 2), (pcx + pw / 2, 2.95, pcz - pd / 2), (pcx + pw / 2, 3.45, pcz + pd / 2), (pcx - pw / 2, 3.45, pcz + pd / 2)], None, (0, 1, -.3))
    box(g, 'Concrete', (pcx, .1, pcz), (pw, .2, pd), solid=True)
    for px in (340, 400): x, _, z = P(px, 205); decal(g, 'DoorBlue', (x, 1.15, z), 180, 1.2, 2.3, .05)
    # Lower rear wing and wooden shed.
    house('Chimney house wing', 345, 290, 440, 340, 1, 'PlasterCream', 'Slate', 'z', 2.2, 'WindowGreen', {'S': (1,)}, sides='SEW')
    house('Compound shed', 250, 285, 300, 340, 1, 'Planks', 'Corrugated', 'z', 1.4, 'WindowBroken', {'E': (0,)}, plinth='Concrete')
    stone_wall('Compound walls', [(242, 140), (242, 240)]); stone_wall('Compound walls', [(242, 275), (242, 350), (380, 350)])
    stone_wall('Compound walls', [(242, 140), (330, 140)]); stone_wall('Compound walls', [(372, 140), (452, 140), (452, 205)])
    stone_wall('Compound walls', [(452, 245), (452, 350), (420, 350)])
    dumpster('Compound props', 335, 175, 0); dumpster('Compound props', 360, 175, 5)
    crates('Compound props', 425, 170, 90, 5, 3)
    hay_stack('Compound props', 270, 255, 10, [(0, 0, 0, 0), (1.2, 0, 0, 3), (.6, 1, 0, -4), (0, 0, .7, 0)])
    tree('Compound trees', 395, 315, 1.15, 11)

def build_church():
    g = 'Church tower'
    cx, cz, w, d = R(520, 198, 558, 238); top = 15
    # Tower shell with a blown-out upper east corner where the fire burns.
    for side, (nx, nz) in enumerate(((0, 1), (0, -1), (1, 0), (-1, 0))):
        if (nx, nz) == (0, 1):
            box(g, 'StoneDark', (cx, 5, cz + d / 2 - .3), (w, 10, .6), solid=True)
            box(g, 'StoneDark', (cx - w / 2 + .5, 12.5, cz + d / 2 - .3), (1.0, 5, .6))
            box(g, 'StoneDark', (cx + w / 2 - .35, 11.0, cz + d / 2 - .3), (.7, 2.0, .6))
            box(g, 'StoneDark', (cx + .3, 14.4, cz + d / 2 - .3), (w - .6, 1.2, .6))
        else:
            box(g, 'StoneDark', (cx + nx * (w / 2 - .3), top / 2, cz + nz * (d / 2 - .3)), (.6 if nx else w, top, d if nx else .6), solid=True)
    for y in (4.5, 9.5): box(g, 'Concrete', (cx, y, cz), (w + .25, .25, d + .25), top=False, bottom=False)
    box(g, 'Concrete', (cx, top + .15, cz), (w + .5, .3, d + .5))
    spire(g, 'Slate', cx, cz, w + .3, top + .3, 5.5)
    box(g, 'Steel', (cx, top + 6.2, cz), (.06, 1.4, .06)); box(g, 'Steel', (cx, top + 6.4, cz), (.6, .06, .06))
    box(g, 'PlanksDark', (cx, 10, cz), (w - 1.2, .25, d - 1.2))
    for yaw, (dx, dz) in ((90, (1, 0)), (180, (0, -1)), (-90, (-1, 0))):
        decal(g, 'WindowBroken', (cx + dx * w / 2, 12.2, cz + dz * d / 2), yaw, .9, 1.8, .03)
    decal(g, 'DoorWood', (cx - w / 2, 1.3, cz), -90, 1.5, 2.6, .03)
    # Charred interior, debris pile below the breach.
    box(g, 'Rubber', (cx, 12.3, cz + d / 2 - .7), (w - .6, 4.5, .1))
    rnd2 = random.Random(5)
    for k in range(9):
        a = (cx + rnd2.uniform(-1.6, 1.6), 10.15, cz + rnd2.uniform(-.4, 1.6)); b = add(a, (rnd2.uniform(-1.4, 1.4), rnd2.uniform(.1, 1.6), rnd2.uniform(-.6, .6)))
        beam(g, 'Embers', a, b, rnd2.uniform(.14, .26))
    box(g, 'Embers', (cx, 10.16, cz + .4), (w - 1.3, .06, d - 1.3))
    rnd = random.Random(7)
    for k in range(26):
        x = cx + rnd.uniform(-2.5, 2.5); z = cz + d / 2 + rnd.uniform(.3, 3.2); s = rnd.uniform(.25, .7)
        box('Church rubble', rnd.choice(['StoneDark', 'Stone', 'Slate', 'PlanksDark']), (x, s / 2 * .8, z), (s, s * .8, s * 1.2), rnd.uniform(0, 90))
    # Stone presbytery with steep roof and dormers (reference shot 1).
    g = 'Church house'
    cx, cz, w, d, H = house(g, 562, 205, 700, 293, 2, 'Stone', 'Slate', 'x', 4.2, 'WindowShutter', {'W': (0,), 'S': (1,)}, gable='Stone')
    for px in (600, 660):
        x, _, z = P(px, 293)
        box(g, 'Stone', (x, H + 1.2, z - .9), (1.8, 2.0, 1.4)); gable_roof(g, 'Slate', x, z - .9, 1.8, 1.6, H + 2.2, .8, 0, 'z', .15)
        decal(g, 'WindowPlain', (x, H + 1.2, z - .2), 0, .8, 1.1)
    stone_wall('Church walls', [(515, 245), (515, 300)])
    stone_wall('Church walls', [(515, 300), (560, 300)])
    for a, b in (((515, 245), (515, 300)), ((515, 300), (560, 300))):
        ax, _, az = P(*a); bx, _, bz = P(*b); n = int(math.hypot(bx - ax, bz - az) / .18)
        for k in range(n):
            t = (k + .5) / n; beam('Church walls', 'DarkMetal', (ax + (bx - ax) * t, 1.75, az + (bz - az) * t), (ax + (bx - ax) * t, 2.55, az + (bz - az) * t), .025)
        beam('Church walls', 'DarkMetal', (ax, 2.45, az), (bx, 2.45, bz), .04)
    for py in (232, 245):
        x, _, z = P(515, py); box('Church walls', 'StoneDark', (x, 1.3, z), (.7, 2.6, .7), solid=True); blob('Church walls', 'Stone', (x, 2.8, z), (.3, .3, .3), f'cap{py}', .05, 8, 5)
    sedan('Church street', 482, 228, 165)
    van('Church street', 488, 315, 172)
    jersey('Church street', 528, 340, 0, 3); jersey('Church street', 548, 340, 90, 2)
    tree('Church trees', 708, 300, 1.25, 21)

def build_bakery():
    g = 'Bakery'
    # Two blocks bridged by an upper floor: a covered east-west passage runs underneath.
    for (r, doors) in [((522, 305, 680, 380), {'W': (1,)}), ((522, 410, 680, 500), {'E': (1,)})]:
        cx, cz, w, d = R(*r)
        box(g, 'PlasterOchre', (cx, 3.4, cz), (w, 6.8, d), solid=True, bottom=False)
        box(g, 'StoneDark', (cx, .35, cz), (w + .12, .7, d + .12), bottom=False)
        for side in 'NSEW': facade(g, cx, cz, w, d, 0, side, 2, 'WindowShutter', door=doors.get(side))
    cx, cz, w, d = R(522, 380, 680, 410)
    box(g, 'PlasterOchre', (cx, 5.1, cz), (w, 3.4, d))
    box(g, 'InteriorWall', (cx, 3.35, cz), (w, .1, d))
    for side in 'WE': facade(g, cx, cz, w, d, 0, side, 1, 'WindowShutter', y0=3.4)
    cx, cz, w, d = R(522, 305, 680, 500)
    box(g, 'Concrete', (cx, 6.7, cz), (w + .2, .25, d + .2), bottom=False)
    gable_roof(g, 'Slate', cx, cz, w, d, 6.8, 4.0, 0, 'z', gable='PlasterOchre')
    x, _, z = P(522, 340); decal(g, 'SignBakery', (x, 4.6, z), -90, 5, 1.25, .06)
    x, _, z = P(522, 455); decal(g, 'SignCafe', (x, 4.6, z), -90, 4, 1.0, .06)
    for px in (560, 620):
        x, _, z = P(px, 500); decal(g, 'PosterB' if px == 560 else 'PosterA', (x, 1.6, z), 0, 1.3, 1.0, .05)
    point_light('Bakery passage light', (cx, 3.0, cz), (1, .85, .65), 6, 8)
    crates('Bakery props', 540, 520, 0, 6, 5); crates('Bakery props', 662, 290, 90, 3, 6)

def build_market():
    g = 'Market'
    cx, cz, w, d, H = house(g, 570, -10, 725, 135, 2, 'PlasterWhite', 'Slate', 'x', 4.0, 'WindowShutter', {'S': (1, 3), 'W': (1,)}, gable='PlasterWhite')
    x, _, z = P(648, 135); decal(g, 'SignMarket', (x, 4.2, z), 0, 6, 1.5, .06)
    for k, px in enumerate((590, 610, 690, 708)):
        x, _, z = P(px, 135); decal(g, ['PosterA', 'PosterB', 'PosterC', 'PosterA'][k], (x, 1.5, z), 0, 1.2, .9, .05)
    x, _, z = P(705, 135); box(g, 'Steel', (x, 5.1, z + .3), (.8, .5, .5))
    crates('Market props', 630, 150, 0, 6, 8); crates('Market props', 710, 150, 0, 4, 9)
    van('Market props', 655, 178, 90, 'VanDark')
    # Lattice windmill and outbuilding in the field behind (reference shot 4).
    g = 'Windmill'
    x, _, z = P(660, -175)
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
    beam(g, 'Steel', (x, 13.4, z - .2), (x, 13.4, z - 2.6), .08); poly(g, 'Steel', [(x, 13.0, z - 2.0), (x, 14.2, z - 2.0), (x, 14.0, z - 3.4), (x, 13.2, z - 3.4)], None, (1, 0, 0))
    house('North farmstead', 780, -330, 900, -250, 1, 'PlasterCream', 'Terracotta', 'x', 2.0, 'WindowPlain', solid=False)

def build_barn():
    g = 'Ruined barn'
    cx, cz, w, d = R(800, 50, 900, 190)
    x0, z0, x1, z1 = cx - w / 2, cz - d / 2, cx + w / 2, cz + d / 2
    wall_run(g, 'Planks', (x0, z0), (x1, z0), 0, 4.2, .2)
    wall_run(g, 'Planks', (x0, z0), (x0, z1), 0, 4.2, .2, [(4, 7, 4.2)])
    wall_run(g, 'Planks', (x1, z0), (x1, z1), 0, 2.4, .2, [(9, 12, 2.4)])
    wall_run(g, 'Planks', (x0, z1), (x1, z1), 0, 3.0, .2, [(3.5, 7, 3.0)])
    box(g, 'StoneDark', (cx, .25, cz), (w + .2, .5, d + .2), render=True, bottom=False, top=False)
    # Collapsed roof: one surviving pitch, the other fallen into the barn.
    poly(g, 'Corrugated', [(x0 - .3, 4.2, z0 - .3), (x1 + .3, 4.2, z0 - .3), (x1 + .3, 6.3, cz - 1), (x0 - .3, 6.3, cz - 1)], None, (0, 1, -1))
    poly(g, 'Corrugated', [(x0 + 1, .4, cz + 2), (x0 + 6, .5, cz + 2.5), (x0 + 6, 3.6, cz - .5), (x0 + 1, 3.3, cz - 1)], None, (0, 1, .5))
    for k in range(6):
        x = x0 + 1 + k * 1.8; beam(g, 'PlanksDark', (x, 4.2, z0), (x, 6.3, cz - 1), .14)
        if k % 2: beam(g, 'PlanksDark', (x, 6.3, cz - 1), (x + .6, 1.2, z1 - 1.5), .14)
    hay_stack('Barn props', 860, 160, 0, [(0, 0, 0, 0), (1.2, 0, 0, 0), (0, 1, 0, 8), (0, 0, .6, 0), (1.2, 0, .6, 0)])
    tractor('Barn props', 830, 95, -30)
    wood_fence('Barn fence', (775, 20), (940, 20)); wood_fence('Barn fence', (940, 20), (940, 205))

def build_store():
    g = 'Store'
    x0, _, z0 = P(782, 205); x1, _, z1 = P(946, 400)
    W, D = x1 - x0, z1 - z0; cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    H1, H2 = 3.9, 3.3
    # Raised slab, storefront with real openings and glazing, side and rear doors.
    box(g, 'StoreFloor', (cx, .075, cz), (W, .15, D), solid=True, bottom=False)
    front = [(1.6, 5.4, 2.8), (7.2, 10.2, H1), (11.9, 15.7, 2.8)]
    wall_run(g, 'PlasterGrey', (x0, z1), (x1, z1), 0, H1, .3, [(a, b, t) for a, b, t in front if b - a < 3.5], True)
    box(g, 'PlasterGrey', (x0 + 8.7, H1 - .55, z1), (3.0, 1.1, .3))  # render-only lintel keeps the door navigable
    for a, b, t in front:
        if b - a >= 3:
            box(g, 'WindowGlass', (x0 + (a + b) / 2, .95 + 1.0, z1), (b - a, 2.0, .05), solid=True)
            box(g, 'PlasterGrey', (x0 + (a + b) / 2, .45, z1), (b - a, .9, .3))
    wall_run(g, 'PlasterGrey', (x0, z0), (x0, z1), 0, H1, .3, [(12.6, 15.4, H1)])
    box(g, 'PlasterGrey', (x0, H1 - .6, z0 + 14.0), (.3, 1.2, 2.8))
    wall_run(g, 'PlasterGrey', (x1, z0), (x1, z1), 0, H1, .3)
    wall_run(g, 'PlasterGrey', (x0, z0), (x1, z0), 0, H1, .3, [(12.8, 15.6, 2.7)])
    # Upper storey over the rear half, reached by the interior stair.
    zmid = z0 + 9.5
    box(g, 'ConcreteDark', (cx, H1 + .1, (z0 + zmid - 3.4) / 2), (W, .2, zmid - 3.4 - z0), solid=True)
    box(g, 'ConcreteDark', ((x0 + 4.2 + x1) / 2, H1 + .1, zmid - 1.7), (x1 - x0 - 4.2, .2, 3.4), solid=True)
    box(g, 'ConcreteDark', (x0 + .6, H1 + .1, zmid - 1.7), (1.2, .2, 3.4), solid=True)
    box(g, 'Ceiling', (cx, H1 - .02, (z0 + zmid) / 2 + 1.8), (W - .4, .02, zmid - z0 - 3.6))
    box(g, 'Ceiling', (cx, H1 - .02, (zmid + z1) / 2), (W - .4, .02, z1 - zmid - .4))
    box(g, 'ConcreteDark', (cx, H1 + .1, (zmid + z1) / 2), (W, .2, z1 - zmid), bottom=False)
    for a, b in (((x0, z0), (x1, z0)), ((x0, z0), (x0, zmid)), ((x1, z0), (x1, zmid)), ((x0, zmid), (x1, zmid))):
        wall_run(g, 'PlasterGrey', a, b, H1 + .2, H2, .3, [(1.2, 4.2, H1 + .2 + H2)] if a == (x0, zmid) else [])
    box(g, 'ConcreteDark', (cx, H1 + H2 + .3, (z0 + zmid) / 2), (W + .3, .3, zmid - z0 + .3))
    gable_roof(g, 'CanopyMetal', cx, (z0 + zmid) / 2, W, zmid - z0, H1 + H2 + .45, 2.2, 0, 'z', gable='PlasterGrey')
    poly(g, 'CanopyMetal', [(x0 - .3, H1 + .45, z1 + .3), (x1 + .3, H1 + .45, z1 + .3), (x1 + .3, H1 + 1.3, zmid), (x0 - .3, H1 + 1.3, zmid)], None, (0, 1, .2))
    facade(g, cx, (z0 + zmid) / 2, W, zmid - z0, 0, 'N', 1, 'WindowBarred', y0=H1 + .2, spacing=4)
    facade(g, cx, (z0 + zmid) / 2, W, zmid - z0, 0, 'E', 1, 'WindowPlain', y0=H1 + .2, spacing=4)
    facade(g, cx, (z0 + zmid) / 2, W, zmid - z0, 0, 'W', 1, 'WindowPlain', y0=H1 + .2, spacing=4)
    facade(g, cx, (z0 + zmid) / 2, W, zmid - z0, 0, 'S', 1, 'WindowPlain', y0=H1 + .2, spacing=4)
    box(g, 'Steel', (x0 + 3, H1 + .75, z1 - 2), (1.2, .9, .8)); box(g, 'Steel', (x0 + 8, H1 + .75, z1 - 2), (1.2, .9, .8))
    # Yellow fascia band with the U logo above the storefront (reference shot 5).
    decal(g, 'Fascia', (cx, H1 - .45, z1 + .15), 0, W, 1.0, .02)
    decal(g, 'LogoU', (x0 - .15, 2.9, z0 + 7), -90, 1.4, 1.4, .02)
    # Interior: lining, chiller wall, aisles, counter, stair to the upper office.
    g = 'Store interior'
    for (a, b, yaw) in (((x0 + .16, z0), (x0 + .16, z1), 90), ((x1 - .16, z0), (x1 - .16, z1), -90)):
        poly(g, 'InteriorWall', [(a[0], .15, a[1]), (b[0], .15, b[1]), (b[0], H1, b[1]), (a[0], H1, a[1])], None, face_dir(yaw))
    poly(g, 'InteriorWall', [(x0, .15, z0 + .16), (x1, .15, z0 + .16), (x1, H1, z0 + .16), (x0, H1, z0 + .16)], None, (0, 0, 1))
    for k in range(6):
        z = z0 + 2.2 + k * 2.3
        box(g, 'DarkMetal', (x1 - .55, 1.3, z), (.8, 2.3, 2.2), solid=True)
        decal(g, 'Fridge', (x1 - .95, 1.25, z), -90, 2.1, 2.1, .02)
    for k in range(3):
        x = x0 + 6.4 + k * 3.1
        box(g, 'DarkMetal', (x, .85, z0 + 10), (1.1, 1.4, 6.5), solid=True)
        for s in (-90, 90): decal(g, 'ShelfGoods', (x + (.56 if s == 90 else -.56), .95, z0 + 10), s, 6.3, 1.2, .01)
    box(g, 'WhitePaint', (x0 + 3.2, .65, z1 - 3), (3.2, 1.0, .8), solid=True); box(g, 'Concrete', (x0 + 3.2, 1.18, z1 - 3), (3.4, .08, 1.0))
    decal(g, 'LogoU', (x0 + 3.2, .65, z1 - 2.6), 0, .8, .8, .02)
    box(g, 'DarkMetal', (x0 + 3.2, 2.2, z1 - 3.6), (3.0, 1.2, .3)); decal(g, 'ShelfGoods', (x0 + 3.2, 2.2, z1 - 3.45), 0, 2.9, 1.1, .01)
    for z in (z0 + 7.5, z0 + 13.5): box(g, 'Concrete', (cx, H1 / 2, z), (.6, H1, .6), solid=True)
    steps = 14
    for k in range(steps):
        y = (k + 1) * (H1 + .2) / steps; z = zmid + 1.6 - k * (5.0 / steps)
        box(g, 'Concrete', (x0 + 2.7, y - .1, z), (3.0, .2 + (y if k == 0 else .1), .4))
    # One smooth ramp collider under the visual steps: 0.4 m navmesh sampling aliases on
    # individual treads and reads them as impassable 0.5 m+ jumps.
    run, rise = 5.0, H1 + .2 - .15
    i = entity('Store stair collision', (x0 + 2.7, .15 + rise / 2 - .1, zmid + 1.6 + .18 - run / 2), (math.degrees(math.atan2(rise, run)), 0, 0), (3.0, .2, math.hypot(run, rise) + .3))
    add_scene(comp(i, 'ColliderComponent', [('Shape', 6, '0	5	Box	Sphere	Capsule	Terrain	Mesh'), ('Size', 3, '1,1,1'), ('Is Trigger', 4, 'false'), ('Blocks Audio', 4, 'true')]))
    beam(g, 'Steel', (x0 + 4.25, 1, zmid + 1.6), (x0 + 4.25, H1 + 1.1, zmid - 3.4), .06)
    box(g, 'Concrete', (x0 + 2.7, H1 + .1, zmid - 4.2), (3.0, .2, 1.6), solid=True)
    for k in range(4):
        x = x0 + 3 + k * 3.8
        for z in (z0 + 11.5, z0 + 16.5):
            box(g, 'LightPanel', (x, H1 - .06, z), (1.2, .04, .6))
    for k, pos in enumerate([(x0 + 4, 3.3, z0 + 13), (x0 + 12, 3.3, z0 + 13), (x0 + 6, 3.3, z0 + 17), (x0 + 13, 3.3, z0 + 17.5)]):
        point_light(f'Store light {k}', pos, (.85, .93, 1), 7, 9)
    point_light('Store upstairs light', (cx, H1 + 2.8, z0 + 5), (1, .9, .75), 5, 9)
    box(g, 'PlanksDark', (x0 + 9, H1 + .65, z0 + 4), (2.2, .1, 1.1), solid=True)
    crates('Store interior', 840, 225, 0, 4, 12)
    sound('Store chiller hum', (x1 - 1, 1.5, z0 + 9), 'chiller_hum.wav', .35, 12, True, 1)

def build_gas_station():
    g = 'Gas canopy'
    x0, _, z0 = P(640, 440); x1, _, z1 = P(865, 592)
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; W, D = x1 - x0, z1 - z0
    # Barrel-vaulted canopy along X with the sweeping end arch (reference shot 5).
    segs = 14; prof = lambda t: (cz - D / 2 + D * t, 6.2 + 1.6 * math.sin(math.pi * t))
    for k in range(segs):
        za, ya = prof(k / segs); zb, yb = prof((k + 1) / segs)
        poly(g, 'CanopyMetal', [(x0, ya, za), (x1, ya, za), (x1, yb, zb), (x0, yb, zb)], None, (0, 1, 0))
        poly(g, 'CanopyMetal', [(x0, ya - .25, za), (x1, ya - .25, za), (x1, yb - .25, zb), (x0, yb - .25, zb)], None, (0, -1, 0))
    for x in (x0, x1):
        for k in range(segs):
            za, ya = prof(k / segs); zb, yb = prof((k + 1) / segs)
            poly(g, 'ColumnBlue', [(x, ya - .25, za), (x, ya, za), (x, yb, zb), (x, yb - .25, zb)], None, (1 if x == x1 else -1, 0, 0))
    for x in (x0 + 2, x0 + W * .5, x1 - 2):
        for k in range(segs):
            za, ya = prof(k / segs); zb, yb = prof((k + 1) / segs); beam(g, 'ColumnBlue', (x, ya - .35, za), (x, yb - .35, zb), .25, .3)
    # Free-standing end arch sweeping from roof height down to the forecourt.
    arc = [(x1 + 1.0 + 6 * (1 - math.cos(t * math.pi / 2)), 7.6 * math.sin((1 - t) * math.pi / 2) + .1) for t in [i / 10 for i in range(11)]]
    for (xa, ya), (xb, yb) in zip(arc, arc[1:]):
        for zz in (z0 + 1, z1 - 1): beam(g, 'CanopyMetal', (xa, ya, zz), (xb, yb, zz), .35, .5)
    for k in range(6): beam(g, 'Steel', (arc[k][0], arc[k][1], z0 + 1), (arc[k][0], arc[k][1], z1 - 1), .08)
    for x in (x0 + 2, x0 + W * .5, x1 - 2):
        for z in (z0 + 2.2, z1 - 2.2):
            box(g, 'ColumnBlue', (x, 3.2, z), (.45, 6.4, .45), solid=True)
    for k in range(6):
        for z in (cz - 3, cz + 3): box(g, 'LightPanel', (x0 + 3 + k * 3.6, 7.45 + (-.2 if abs(z - cz) > 2 else 0), z), (1.0, .04, .35))
    # Pump islands.
    for x in (x0 + 7, x0 + 15):
        box(g, 'Concrete', (x, .1, cz), (1.6, .2, 6.5), solid=True)
        for dz in (-1.5, 1.5):
            box(g, 'WhitePaint', (x, .95, cz + dz), (.7, 1.5, .5), solid=True); box(g, 'ColumnBlue', (x, 1.75, cz + dz), (.75, .15, .55))
            decal(g, 'LogoU', (x + .36, 1.35, cz + dz), 90, .35, .35, .01); decal(g, 'LogoU', (x - .36, 1.35, cz + dz), -90, .35, .35, .01)
            beam(g, 'Rubber', (x + .36, 1.2, cz + dz + .15), (x + .5, .5, cz + dz + .3), .04)
    # Bunting strung across the canopy.
    for z in (z0 + 3.5, cz, z1 - 3.5):
        prev = None
        for k in range(29):
            t = k / 28; p = (x0 + 1 + (W - 2) * t, 5.6 - .5 * 4 * t * (1 - t), z)
            if prev: beam(g, 'Rubber', prev, p, .015)
            if k % 2 == 0 and k < 28:
                q = (p[0] + .3, p[1] - .45, z)
                poly('Gas bunting', 'Flags', [p, (p[0] + .6, p[1] - .03, z), q], [((k // 2 % 8) / 8, 0), ((k // 2 % 8 + 1) / 8, 0), ((k // 2 % 8 + .5) / 8, 1)], (0, 0, 1))
            prev = p
    # Price sign pylon.
    x, _, z = P(945, 600)
    box(g, 'ColumnBlue', (x, 3, z), (.35, 6, .35), solid=True); box(g, 'YellowPaint', (x, 6.6, z), (1.8, 1.8, .3))
    decal(g, 'LogoU', (x, 6.6, z + .16), 0, 1.5, 1.5, .01); decal(g, 'LogoU', (x, 6.6, z - .16), 180, 1.5, 1.5, .01)
    van('Gas props', 662, 470, 180)
    crates('Gas props', 760, 420, 0, 5, 14)
    hay_stack('Gas props', 735, 330, 90, [(0, 0, 0, 0), (1.2, 0, 0, 2), (0, 0, .58, 0), (1.2, 0, .58, -3), (.6, 1, .3, 10)])
    van('Gas props', 735, 282, 0)
    dumpster('Gas props', 900, 190, 0); dumpster('Gas props', 925, 190, 3)
    # Long brick warehouse along the east edge (reference shot 5, right).
    cx2, cz2, w2, d2, H = house('Brick warehouse', 958, 240, 1050, 640, 2, 'Brick', 'Slate', 'z', 3.0, 'WindowBarred', {'W': (2, 7)}, gable='Brick')
    x, _, z = P(958, 400); decal('Brick warehouse', 'SignBrick', (x, 5.1, z), -90, 5, 1.25, .06)

def build_south():
    house('South warehouse', 232, 400, 375, 585, 1, 'PlasterGrey', 'CorrugatedBlue', 'z', 2.6, 'WindowBarred', extra_height=1.8, gable='Corrugated')
    x, _, z = P(375, 470); decal('South warehouse', 'Garage', (x, 1.6, z), 90, 3.8, 3.2, .05)
    house('Hotel', 400, 460, 492, 550, 1, 'PlasterYellow', 'Terracotta', 'x', 2.0, 'WindowBlue', {'N': (1,)}, extra_height=.6)
    cx, cz, w, d = R(400, 460, 492, 550)
    box('Hotel', 'Steel', (cx, 6.3, cz), (8.2, 1.5, .08))
    for k, letter in enumerate('HOTEL'):
        x = cx - 3.2 + k * 1.6
        decal('Hotel', 'Letter' + letter, (x, 6.3, cz), 0, 1.4, 1.4, .06)
        decal('Hotel', 'Letter' + letter, (2 * cx - x, 6.3, cz), 180, 1.4, 1.4, .06)
        beam('Hotel', 'Steel', (x, 4.6, cz), (x, 5.6, cz), .06)
    box('Hotel', 'WhitePaint', (cx + 2.5, 2.4, cz - d / 2 - .35), (.9, .6, .6))
    van('Hotel props', 440, 395, 92)
    crates('Hotel props', 505, 452, 0, 4, 15)
    house('South cottage', 520, 540, 620, 610, 1, 'PlasterCream', 'Terracotta', 'x', 2.0, 'WindowGreen', {'N': (1,)})
    tank('South road', 290, 368, 90, -20)
    tank('Plaza', 448, 162, 125, 40)

def build_west():
    house('West cottage', 118, 372, 200, 448, 1, 'PlasterCream', 'Slate', 'z', 2.2, 'WindowPlain', {'E': (1,)}, gable='PlasterCream')
    house('West cottage 2', 118, 458, 200, 530, 1, 'PlasterYellow', 'Slate', 'z', 2.2, 'WindowGreen', {'E': (0,)}, gable='PlasterYellow')
    for a, b in [((100, 345), (100, 545)), ((100, 545), (210, 545)), ((210, 545), (210, 470)), ((210, 430), (210, 400))]:
        fence_picket('West fences', a, b)
    tractor('West yard', 182, 342, 30)
    hay_stack('West yard', 140, 350, 0, [(0, 0, 0, 0), (1.2, 0, 0, 0), (.6, 1, 0, 6)])
    wood_fence('Field fence', (-60, 95), (265, 95))
    for k, (px, py, s) in enumerate([(230, 112, 1.2), (150, 255, 1.3), (70, 215, 1.0), (40, 330, 1.2), (195, 565, 1.1), (60, 470, 1.3), (20, 600, 1.2), (330, 620, 1.0)]):
        tree('West trees', px, py, s, 30 + k)
    # Water tower beyond the field (reference shot 3).
    g = 'Water tower'
    x, _, z = P(390, -165)
    for a in range(4):
        ang = a * math.tau / 4 + math.pi / 4; beam(g, 'Steel', (x + math.cos(ang) * 2.6, 0, z + math.sin(ang) * 2.6), (x + math.cos(ang) * 1.8, 11, z + math.sin(ang) * 1.8), .22)
    for y in (3.5, 7.5): cylinder(g, 'Steel', (x, y, z), (x, y + .12, z), 2.4, 8)
    cylinder(g, 'PlasterGrey', (x, 11, z), (x, 14.5, z), 2.6, 16); cylinder(g, 'Slate', (x, 14.5, z), (x, 16, z), 2.8, 16, True, .2)
    house('North cottage', 370, -40, 460, 20, 1, 'PlasterWhite', 'Terracotta', 'x', 2.0, 'WindowGreen', {'S': (1,)})
    house('North house', 200, -150, 290, -80, 2, 'PlasterOchre', 'Slate', 'x', 2.6, 'WindowShutter', solid=False)

def build_plaza():
    g = 'Plaza monument'
    x, _, z = P(505, 158)
    cylinder(g, 'Stone', (x, 0, z), (x, .45, z), 2.6, 16); cylinder(g, 'Concrete', (x, .45, z), (x, .55, z), 2.7, 16)
    collider((x, .3, z), (4.6, .6, 4.6))
    box(g, 'StoneDark', (x, 1.6, z), (1.4, 2.1, 1.4), solid=True); box(g, 'Concrete', (x, 2.75, z), (1.7, .2, 1.7))
    blob(g, 'DarkMetal', (x, 3.6, z), (.35, .75, .3), 'statue', .1, 8, 6); blob(g, 'DarkMetal', (x, 4.55, z), (.22, .25, .22), 'head', .05, 8, 5)
    beam(g, 'DarkMetal', (x + .2, 4.1, z), (x + .7, 4.9, z - .2), .12)
    x, _, z = P(400, 180)
    cylinder(g, 'Concrete', (x, 0, z), (x, .5, z), 1.8, 14); blob(g, 'Hedge', (x, .7, z), (1.6, .6, 1.6), 'planter', .2)
    collider((x, .25, z), (3.4, .5, 3.4))
    for k, (px, py, yaw) in enumerate([(340, 190, 0), (460, 120, 90), (565, 185, 180), (515, 370, 270), (513, 255, 270), (700, 250, 270), (770, 420, 270), (240, 385, 0), (620, 425, 0), (180, 330, 0)]):
        lamp('Street lamps', px, py, yaw)
    for k, (px, py) in enumerate([(-40, 330), (90, 330), (200, 330), (330, 335), (470, 340), (610, 345), (695, 300), (695, 180), (775, 30), (775, 120)]):
        pole('Power lines', px, py)
    wires('Power lines', list(range(6))); wires('Power lines', [5, 6, 7, 8, 9][::-1])
    jersey('Plaza', 360, 60, 90, 3); jersey('Plaza', 590, 210, 10, 3)
    crates('Plaza', 345, 115, 90, 4, 18)

def barrel(g, x, z, paint, tipped=False):
    if tipped: cylinder(g, paint, (x - .45, .3, z), (x + .45, .3, z), .3, 10)
    else:
        cylinder(g, paint, (x, 0, z), (x, .9, z), .3, 10)
        for y in (.3, .6): cylinder(g, 'DarkMetal', (x, y, z), (x, y + .03, z), .31, 10, False)

def build_clutter():
    rnd = random.Random(99)
    for px, py, paint in [(512, 205, 'RedPaint'), (515, 214, 'DumpsterGreen'), (700, 215, 'ColumnBlue'), (452, 420, 'RedPaint'),
                          (775, 205, 'DumpsterGreen'), (950, 430, 'ColumnBlue'), (380, 395, 'DumpsterGreen'), (820, 160, 'RedPaint')]:
        x, _, z = P(px, py); barrel('Clutter', x, z, paint, rnd.random() < .3)
        collider((x, .45, z), (.6, .9, .6))
    for px, py in [(300, 345), (520, 505), (690, 360), (930, 240), (210, 390)]:
        x, _, z = P(px, py)
        for k in range(rnd.randint(2, 4)):
            cylinder('Clutter', 'Rubber', (x + k * .1, k * .25, z), (x + k * .1, k * .25 + .24, z), .38, 12)
    for px, py, yaw in [(560, 300, 15), (610, 520, 70), (705, 370, 0), (958, 330, 90), (330, 380, 30), (860, 420, 10)]:
        x, _, z = P(px, py)
        for k in range(rnd.randint(1, 3)):
            box('Clutter', 'Planks', (x, .07 + k * .14, z), (1.2, .12, 1.0), yaw + rnd.uniform(-6, 6))
    # Broken masonry and paper litter along the streets near the burning tower.
    for k in range(60):
        px, py = rnd.uniform(450, 720), rnd.uniform(190, 430)
        x, _, z = P(px, py); sz = rnd.uniform(.08, .3)
        box('Street debris', rnd.choice(['Stone', 'StoneDark', 'Concrete', 'Brick']), (x, sz * .35, z), (sz, sz * .7, sz * 1.3), rnd.uniform(0, 90))
    # Rooftop clutter: AC units and satellite dishes like the reference facades.
    for px, py, y in [(430, 300, 1.8), (660, 300, 4.8), (600, 140, 4.6), (240, 480, 3.5)]:
        x, _, z = P(px, py); box('Rooftop details', 'WhitePaint', (x, y, z + .3), (.9, .6, .5)); box('Rooftop details', 'DarkMetal', (x, y, z + .56), (.6, .45, .02))
    for px, py, y in [(310, 250, 8.4), (650, 60, 9.6), (1000, 300, 9.2), (585, 245, 9.6)]:
        x, _, z = P(px, py)
        cylinder('Rooftop details', 'Steel', (x, y, z), (x, y + .5, z), .03, 6)
        cylinder('Rooftop details', 'WhitePaint', (x, y + .5, z), (x + .12, y + .6, z + .25), .45, 12, True, .05)
        beam('Rooftop details', 'Steel', (x, y, z), (x, y + 2.2, z), .03)
        for k in range(4): beam('Rooftop details', 'Steel', (x - .6, y + 1.4 + k * .22, z), (x + .6, y + 1.4 + k * .22, z), .02)
    # Flower boxes under ground-floor windows of the main street houses.
    for px, py, yaw in [(300, 205, 180), (360, 205, 180), (420, 205, 180), (590, 293, 0), (640, 293, 0)]:
        x, _, z = P(px, py); off = mul(face_dir(yaw), .25)
        box('Clutter', 'PlanksDark', (x + off[0], .95, z + off[2]), (1.1, .25, .3), yaw); blob('Clutter', 'Hedge', (x + off[0], 1.15, z + off[2]), (.5, .2, .15), f'fb{px}', .3, 7, 4)

def build_backdrop():
    # Mountain ring with forested slopes (all reference views show wooded ranges).
    g = 'Mountains'
    rnd = random.Random(42); seg = 144
    raw = [80 + 40 * math.sin(k * .19) + 22 * math.sin(k * .53 + 1) + rnd.uniform(-6, 6) for k in range(seg)]
    heights = [(raw[k - 1] + 2 * raw[k] + raw[(k + 1) % seg]) / 4 for k in range(seg)]
    rings = [(220, 0), (270, .2), (330, .5), (400, .8), (480, 1.0), (570, .9)]
    def pt(k, r):
        a = k % seg * math.tau / seg; radius, f = rings[r]; return (math.cos(a) * radius, heights[k % seg] * f - 1, math.sin(a) * radius * .85)
    for k in range(seg):
        for r in range(len(rings) - 1):
            q = [pt(k, r), pt(k + 1, r), pt(k + 1, r + 1), pt(k, r + 1)]
            poly(g, 'Forest' if r < 2 else 'ForestFar', q, None, (-q[0][0], 300, -q[0][2]))
    # Treelines just past the playable boundary.
    rnd = random.Random(3)
    for k in range(70):
        a = rnd.random() * math.tau; r = rnd.uniform(78, 140)
        x, z = math.cos(a) * r, math.sin(a) * r * .75
        if rnd.random() < .5: conifer('Treeline', x, z, rnd.uniform(9, 15), k)
        else:
            s = rnd.uniform(1.2, 1.8); rad = 3 * s
            cylinder('Treeline', 'Bark', (x, 0, z), (x, 5 * s, z), .3 * s, 6, False)
            blob('Treeline', 'LeavesDark', (x, 6.5 * s, z), (rad, rad * .9, rad), f'bg{k}', .3)
    # Distant village rooftops to the east/south.
    for k, (px, py) in enumerate([(1150, 250), (1180, 420), (1120, 650), (900, 760), (600, 780), (300, 760), (-150, 520), (-170, 250)]):
        house('Distant houses', px, py, px + 80, py + 60, 1 + k % 2, ['PlasterCream', 'PlasterWhite', 'PlasterOchre'][k % 3], ['Slate', 'Terracotta'][k % 2], 'x', 2.4, 'WindowPlain', solid=False)
    # Perimeter: invisible collision and hedgerows.
    for (cx, cz, w, d) in [(0, -42, 130, 1), (0, 38, 130, 1), (-62, -2, 1, 82), (59, -2, 1, 82)]:
        box('Boundary', 'Hedge', (cx, 3, cz), (w, 6, d), render=False, solid=True)
    for a, b in [((-60, -120), (-60, 650)), ((-60, 650), (1090, 650))]:
        ax, _, az = P(*a); bx, _, bz = P(*b)
        L = math.hypot(bx - ax, bz - az); n = int(L / 3)
        for k in range(n):
            t = (k + .5) / n; blob('Hedgerow', 'Hedge', (ax + (bx - ax) * t, .9, az + (bz - az) * t), (1.9, 1.3, 1.9), f'h{a}{k}', .25, 7, 4)

def build_effects():
    # Burning breach in the church tower: flames, embers, a heavy black smoke column.
    cx, cz, w, d = R(520, 198, 558, 238)
    fire = (cx + .2, 11.6, cz + d / 2 - .6)
    particles('Tower fire', fire, 'TowerFire')
    particles('Tower fire core', add(fire, (-.3, -.4, .4)), 'TowerFireCore')
    particles('Tower embers', add(fire, (0, .4, 0)), 'Embers')
    particles('Tower smoke', add(fire, (0, 1.2, .2)), 'TowerSmoke')
    point_light('Tower fire light', add(fire, (0, 0, 1.4)), (1, .55, .2), 22, 16)
    sound('Tower fire crackle', fire, 'fire_crackle.wav', .9, 45, True, 3)
    # Distant fires and smouldering wreckage seen in reference shots 2 and 5.
    for name, pos in [('Distant smoke east', P(1250, -350, 2)), ('Distant smoke north', P(100, -700, 4))]:
        particles(name, pos, 'DistantSmoke')
    x, _, z = P(930, 120)
    particles('Barn smoulder', (x, .3, z), 'Smoulder')
    x, _, z = P(305, 222)
    particles('Chimney smoke', (x, 12.6, z), 'ChimneySmoke')
    sound('Village wind', (0, 6, 0), 'wind_loop.wav', .28, 400, False, 50)

def build_volumes():
    # Ambient light: coarse exterior volume plus dense interiors (smaller cells take priority).
    ambient_volume('Ambient Light Volume Exterior', (-1, 7, -2), (124, 16, 80), 3.0)
    x0, _, z0 = P(782, 205); x1, _, z1 = P(946, 400)
    ambient_volume('Ambient Light Volume Store', ((x0 + x1) / 2, 3.7, (z0 + z1) / 2), (x1 - x0, 7.6, z1 - z0), .9)
    cx, cz, w, d = R(522, 380, 680, 410); ambient_volume('Ambient Light Volume Bakery Passage', (cx, 1.8, cz), (w, 3.5, d), .6)
    cx, cz, w, d = R(800, 50, 900, 190); ambient_volume('Ambient Light Volume Barn', (cx, 2.6, cz), (w, 5.2, d), 1.0)
    cx, cz, w, d = R(640, 440, 865, 592); ambient_volume('Ambient Light Volume Gas Canopy', (cx, 4, cz), (w, 8, d), 1.5)
    # Audio environments.
    audio_volume('Audio Exterior Field', (-1, 10, -2), (140, 30, 90), 'Field', 0, .05, .45, .15, .35)
    audio_volume('Audio Village Streets', (-3, 6, 0), (34, 14, 30), 'City', 1, .14, .9, .3, .6, .05, .09, .12)
    x0, _, z0 = P(782, 205); x1, _, z1 = P(946, 400)
    audio_volume('Audio Store Interior', ((x0 + x1) / 2, 3.7, (z0 + z1) / 2), (x1 - x0, 7.6, z1 - z0), 'Room', 3, .3, .8, .5, .7, 0, .1, .1, .05, 1)
    cx, cz, w, d = R(522, 380, 680, 410); audio_volume('Audio Bakery Passage', (cx, 1.8, cz), (w, 3.6, d), 'Hall', 3, .35, 1.4, .5, .7, .1, .07, .2, 0, 1)
    cx, cz, w, d = R(800, 50, 900, 190); audio_volume('Audio Barn', (cx, 2.6, cz), (w, 5.2, d), 'Room', 2, .2, .7, .4, .5, 0, .1, .1, .1, 1.5)
    cx, cz, w, d = R(640, 440, 865, 592); audio_volume('Audio Gas Canopy', (cx, 4, cz), (w, 8, d), 'Custom', 2, .16, .9, .35, .5, .08, .1, .15, 0, 2)
    cx, cz, w, d = R(520, 198, 558, 238); audio_volume('Audio Church Tower', (cx, 7.5, cz), (w, 15, d), 'Hall', 3, .45, 2.2, .6, .8, .15, .15, .25, 0, 1)

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
    spawn = P(60, 420, 1.2)
    scene = re.sub(r'(^ENTITY\t1\t0\t1\tPlayer\t)[^\t]+\t[^\t]+', r'\g<1>' + vec(spawn) + '\t0,-90,0', scene, flags=re.M)
    names = ['Gas Station', 'Store', 'Church', 'Plaza', 'Farm']
    scene = scene.replace('Hardpoint Courtyard|Hardpoint West|Hardpoint East', '|'.join('Hardpoint ' + n for n in names))
    for key, val in [('hardpointRadius', '4.0'), ('botFallbackSpawnMinimumDistance', '10'), ('botFallbackSpawnRadius', '40')]:
        scene = re.sub(r'(^PROPERTY\t' + key + r'\t0\t)[^\t]+', r'\g<1>' + val, scene, flags=re.M)
    for key, v in [('Bounds Minimum', '-62,-1,-43'), ('Bounds Maximum', '60,12,39'), ('Cell Size', '0.4')]:
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
    build_ground(); build_compound(); build_church(); build_bakery(); build_market(); build_barn(); build_store()
    build_gas_station(); build_south(); build_west(); build_plaza(); build_clutter(); build_backdrop(); build_effects(); build_volumes()
    for name, (px, py) in [('North West', (60, 140)), ('North East', (760, 30)), ('South East', (900, 560)), ('South West', (60, 420))]:
        entity('Spawn ' + name, P(px, py, 1))
    for n, (name, (px, py, y)) in enumerate(zip(names, [(752, 515, .2), (832, 335, .3), (482, 268, .2), (430, 95, .2), (170, 310, .2)])):
        i = entity('Hardpoint ' + name, P(px, py, y))
        marker = byid[50101].replace('50101', str(i + 1)).replace('50100', str(i))
        marker = marker.replace('PROPERTY\tsiteIndex\t1\t0', 'PROPERTY\tsiteIndex\t1\t' + str(n))
        add_scene(marker); global nextid; nextid += 1
    triangles = sum(export(g, parts) for g, parts in meshes.items() if any(parts.values()))
    scene = bake_ambient(scene)
    save('Scenes/Standoff.plutoscene', scene)
    manifest = ROOT / 'CoDplutoproject.plutoproject'; lines = manifest.read_text().splitlines()
    kinds = {'.plutoscene': 'Scene', '.plutomesh': 'Mesh', '.plutomaterial': 'Material', '.png': 'Texture', '.plutoparticles': 'Particle System', '.wav': 'Audio'}
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
