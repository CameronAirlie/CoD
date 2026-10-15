"""Bake top-down outline minimaps from each playable scene's solid box colliders.

Run after a map's geometry changes:  python Tools/build_minimaps.py
Writes Assets/UI/Minimaps/<Scene>.tga (native RmlUi TGA), registers them for export, and
regenerates Assets/Scripts/MinimapLayouts.cs with the world square each image covers.
North is world -Z and east is +X, matching TacticalUi.Heading. Previews go to Tools/Native/UI.
"""
from pathlib import Path
import json, math, re, struct, uuid
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'Assets'
OUT = ASSETS / 'UI/Minimaps'
PREVIEW = ROOT / 'Tools/Native/UI'
SCENES = ['Main', 'Shipment', 'Standoff', 'Foundry', 'Terminal']  # MapCatalog playable maps
SIZE = 1024          # Delivered texture edge in pixels.
SUPERSAMPLE = 2      # Drawn larger, then filtered down for smooth outlines.
PADDING = 4.0        # World metres around the playable area.
FLOOR_THICKNESS = 1.05 # Slabs this thin, or flat relative to their footprint, are walkable surfaces.
FLOOR_RATIO = .12
tokens = json.loads((ASSETS / 'UI/theme.tokens.json').read_text())

def rgb(token): value = tokens[token].lstrip('#'); return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))
def vector(text): return np.array([float(v) for v in text.split(',')], dtype=float)

def rotation(euler_degrees):
    x, y, z = np.radians(euler_degrees)
    rx = np.array([[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]])
    ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    rz = np.array([[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]])
    return rx @ ry @ rz  # Engine Rx * Ry * Rz convention.

def parse(scene_path):
    """Returns world-space box corner sets and the navigation bounds of one scene."""
    entities, colliders, nav = {}, [], None
    current = None
    for line in scene_path.read_text(encoding='utf-8', errors='replace').splitlines():
        parts = line.split('\t')
        if parts[0] == 'ENTITY':
            entities[int(parts[1])] = dict(parent=int(parts[2]), enabled=parts[3] == '1',
                position=vector(parts[5]), rotation=vector(parts[6]), scale=vector(parts[7]))
        elif parts[0] == 'COMPONENT':
            current = dict(entity=int(parts[1]), type=parts[2], properties={})
        elif parts[0] == 'PROPERTY' and current is not None:
            current['properties'][parts[1]] = parts[3]
        elif parts[0] == 'END_COMPONENT' and current is not None:
            if current['type'] == 'ColliderComponent': colliders.append(current)
            elif current['type'] == 'NavigationMeshComponent':
                nav = (vector(current['properties']['Bounds Minimum']), vector(current['properties']['Bounds Maximum']))
            current = None

    def world(entity_id):
        entity = entities[entity_id]
        local = np.eye(4)
        local[:3, :3] = rotation(entity['rotation']) * entity['scale']
        local[:3, 3] = entity['position']
        if entity['parent'] in entities:
            parent, enabled = world(entity['parent'])
            return parent @ local, enabled and entity['enabled']
        return local, entity['enabled']

    boxes = []
    for collider in colliders:
        props = collider['properties']
        if props.get('Shape', '0') != '0' or props.get('Is Trigger') == 'true' or collider['entity'] not in entities: continue
        matrix, enabled = world(collider['entity'])
        if not enabled: continue
        half = vector(props.get('Size', '1,1,1')) * .5
        centre = vector(props.get('Center', '0,0,0'))
        corners = np.array([[centre[0] + sx * half[0], centre[1] + sy * half[1], centre[2] + sz * half[2], 1]
            for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)])
        boxes.append((matrix @ corners.T).T[:, :3])
    return boxes, nav

def hull(points):
    points = sorted(set(map(tuple, np.round(points, 4))))
    if len(points) <= 2: return points
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in points:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0: lower.pop()
        lower.append(p)
    for p in reversed(points):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0: upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]

def bake(scene):
    boxes, nav = parse(ASSETS / f'Scenes/{scene}.plutoscene')
    footprints = []
    for corners in boxes:
        height = corners[:, 1].max() - corners[:, 1].min()
        footprint = hull(corners[:, [0, 2]])
        extent = np.ptp(np.array(footprint), axis=0) if len(footprint) > 2 else np.zeros(2)
        if extent.max() < .05: continue
        footprints.append((footprint, height <= FLOOR_THICKNESS or height < FLOOR_RATIO * extent.min(), extent))
    obstacles = [f for f, floor, _ in footprints if not floor]
    points = np.array([p for f in obstacles for p in f])
    low, high = points.min(axis=0), points.max(axis=0)
    if nav is not None:  # Navigation bounds describe the playable area; outer scenery is cropped.
        low, high = np.maximum(low, nav[0][[0, 2]]), np.minimum(high, nav[1][[0, 2]])
    centre = (low + high) * .5
    span = float(max(high - low) + PADDING * 2)
    canvas = SIZE * SUPERSAMPLE

    def pixel(p): return ((p[0] - centre[0]) / span + .5) * canvas, ((p[1] - centre[1]) / span + .5) * canvas
    floor_mask, solid_mask = Image.new('L', (canvas, canvas)), Image.new('L', (canvas, canvas))
    floor_draw, solid_draw = ImageDraw.Draw(floor_mask), ImageDraw.Draw(solid_mask)
    for footprint, floor, extent in footprints:
        if floor and extent.min() > span * .9: continue  # A ground plane fills the frame; the panel already does.
        (floor_draw if floor else solid_draw).polygon([pixel(p) for p in footprint], fill=255)
    stroke = SUPERSAMPLE * 2 + 1
    edge = np.clip(np.asarray(solid_mask.filter(ImageFilter.MaxFilter(stroke)), dtype=np.int16) -
        np.asarray(solid_mask.filter(ImageFilter.MinFilter(3)), dtype=np.int16), 0, 255) / 255
    solid, floor = np.asarray(solid_mask) / 255, np.asarray(floor_mask) / 255
    layers = [(floor * (1 - solid), rgb('elevated'), .55), (solid, rgb('border'), .92), (edge, rgb('secondary'), .95)]
    colour, alpha = np.zeros((canvas, canvas, 3)), np.zeros((canvas, canvas))
    for coverage, tint, opacity in layers:  # Straight-alpha "over" composition.
        a = coverage * opacity
        out_alpha = a + alpha * (1 - a)
        colour = (np.array(tint)[None, None, :] * a[..., None] + colour * (alpha * (1 - a))[..., None]) / np.maximum(out_alpha, 1e-6)[..., None]
        alpha = out_alpha
    image = Image.fromarray(np.dstack([colour, alpha * 255]).round().clip(0, 255).astype(np.uint8), 'RGBA')
    image = image.resize((SIZE, SIZE), Image.LANCZOS)
    OUT.mkdir(parents=True, exist_ok=True)
    blue, green, red, a = image.split()[2], image.split()[1], image.split()[0], image.split()[3]
    pixels = Image.merge('RGBA', (blue, green, red, a)).tobytes()  # TGA stores BGRA.
    header = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, SIZE, SIZE, 32, 40)  # Top-left origin.
    path = OUT / f'{scene}.tga'
    path.write_bytes(header + pixels)
    PREVIEW.mkdir(parents=True, exist_ok=True)
    preview = Image.new('RGBA', image.size, rgb('panel') + (255,)); preview.alpha_composite(image)
    preview.save(PREVIEW / f'minimap-{scene}.png')
    meta = Path(str(path) + '.plutometa')
    if not meta.exists():
        meta.write_text(f'PLUTOASSET\t1\nID\t{uuid.uuid5(uuid.NAMESPACE_URL, "cod/ui/" + path.relative_to(ASSETS).as_posix()).hex}\nIMPORTER_VERSION\t1\n')
    print(f'{scene}: {len(obstacles)} obstacles, {span:.1f} m square centred at {centre[0]:.2f}, {centre[1]:.2f}')
    return scene, float(centre[0]), float(centre[1]), span

layouts = [bake(scene) for scene in SCENES]
rows = '\n'.join(f'        new("{s}", "Minimaps/{s}.tga", {x:.3f}f, {z:.3f}f, {span:.3f}f),' for s, x, z, span in layouts)
script = ASSETS / 'Scripts/MinimapLayouts.cs'
script.write_text(f'''// <auto-generated> Tools/build_minimaps.py. Rebake instead of editing by hand. </auto-generated>
namespace CoD.Scripts;

public static partial class MinimapLayouts
{{
    private static readonly MinimapLayout[] Baked =
    [
{rows}
    ];
}}
''')
if not Path(str(script) + '.plutometa').exists():
    Path(str(script) + '.plutometa').write_text(f'PLUTOASSET\t1\nID\t{uuid.uuid5(uuid.NAMESPACE_URL, "cod/scripts/MinimapLayouts.cs").hex}\nIMPORTER_VERSION\t1\n')

manifest = ROOT / 'CoDplutoproject.plutoproject'
lines = manifest.read_text().splitlines()
# Images chosen at runtime by scene, and the marker icon used only in generated markup.
for relative in [f'UI/Minimaps/{scene}.tga' for scene in SCENES] + ['UI/Icons/map-arrow.tga']:
    reference = f'project://{relative}'
    size = (ASSETS / relative).stat().st_size
    lines = [l for l in lines if not l.startswith(f'ASSET\t{reference}\t')] + [f'ASSET\t{reference}\t{size}\tTexture']
    include = f'BUILD_ALWAYS_INCLUDE\t{reference}'
    if include not in lines:  # Dependency scanning cannot see runtime-selected references.
        existing = [i for i, l in enumerate(lines) if l.startswith('BUILD_ALWAYS_INCLUDE')]
        lines.insert(next((i for i in existing if lines[i] > include), existing[-1] + 1), include)
manifest.write_text('\n'.join(lines) + '\n')
print('Baked tactical minimaps and registered them for export.')
