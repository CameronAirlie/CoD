"""Generate the Pluto Ring racetrack scene and its assets.

Usage: python Tools/racetrack/build_racetrack.py [--project <CoD root>]

Writes Assets/Scenes/Racetrack.plutoscene plus the Racetrack materials and
textures, and registers new assets in CoDplutoproject.plutoproject. The car,
follow camera, speedometer canvas, sky and sun are copied from car_scene so
the racetrack always uses the current vehicle setup. Re-running regenerates
the scene; existing .plutometa IDs are preserved.
"""

from __future__ import annotations

import argparse
import math
import random
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import track_layout as layout  # noqa: E402

SOURCE_SCENE = "Assets/Scenes/car_scene.plutoscene"
OUTPUT_SCENE = "Assets/Scenes/Racetrack.plutoscene"
CAR_ID = 18691
COPIED_ROOTS = {18689: "Directional Light", 18710: "Sky", 18793: "UI", 20087: "Camera", 20093: "AudioVolume"}
START_LINE_S = 150.0
CHECKPOINT_COUNT = 8
GRID_BEHIND_LINE = 40.0
CENTRE_OF_MASS_Z = -0.55  # ~58% front static load for this wheelbase

# ---------------------------------------------------------------- formatting


def f(value: float) -> str:
    return f"{value:.6f}"


def v3(x: float, y: float, z: float) -> str:
    return f"{f(x)},{f(y)},{f(z)}"


class SceneWriter:
    def __init__(self) -> None:
        self.lines: list[str] = ["SCENE\t1"]
        self.next_id = 30000

    def allocate(self) -> int:
        self.next_id += 1
        return self.next_id

    def entity(self, name: str, position=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0),
               parent: int = 0, tags: tuple[str, ...] = (), active: bool = True) -> int:
        entity_id = self.allocate()
        self.lines.append(f"ENTITY\t{entity_id}\t{parent}\t{1 if active else 0}\t{name}\t"
                          f"{v3(*position)}\t{v3(*rotation)}\t{v3(*scale)}")
        if tags:
            self.lines.append(f"TAGS\t{entity_id}\t{len(tags)}\t" + "\t".join(tags))
        return entity_id

    def component(self, entity_id: int, kind: str, properties: list[str]) -> None:
        self.lines.append(f"COMPONENT\t{entity_id}\t{kind}\t1")
        self.lines.extend(properties)
        self.lines.append("END_COMPONENT")

    def collider(self, entity_id: int, shape: int, size=(1.0, 1.0, 1.0), blocks_audio: bool = True) -> None:
        self.component(entity_id, "ColliderComponent", [
            "PROPERTY\tShape\t6\t%d\t5\tBox\tSphere\tCapsule\tTerrain\tMesh" % shape,
            f"PROPERTY\tCenter\t3\t{v3(0, 0, 0)}\t0",
            f"PROPERTY\tSize\t3\t{v3(*size)}\t0",
            "PROPERTY\tRadius\t0\t0.500000\t0",
            "PROPERTY\tHeight\t0\t2.000000\t0",
            "PROPERTY\tIs Trigger\t4\tfalse\t0",
            "PROPERTY\tSurface Asset\t2\t\t0",
            f"PROPERTY\tBlocks Audio\t4\t{'true' if blocks_audio else 'false'}\t0",
        ])

    def mesh(self, entity_id: int, mesh_reference: str, material: str, static: bool = True) -> None:
        model = mesh_reference
        self.component(entity_id, "MeshComponent", [
            f"PROPERTY\tStatic\t4\t{'true' if static else 'false'}\t0",
            "PROPERTY\tVisible\t4\ttrue\t0",
            "PROPERTY\tSubmeshIndex\t1\t-1\t0",
            "PROPERTY\tSubmeshCount\t1\t1\t0",
            f"PROPERTY\tModelAssetId\t2\t{model}\t0",
            f"PROPERTY\tModelObjectId\t2\t{1 if model else 0}\t0",
            f"PROPERTY\tMeshAssetReference\t2\t{mesh_reference}\t0",
            "PROPERTY\tUseGeneratedLods\t4\tfalse\t0",
            f"PROPERTY\tMeshPositionOffset\t3\t{v3(0, 0, 0)}\t0",
            f"PROPERTY\tPivotOffset\t3\t{v3(0, 0, 0)}\t0",
            f"PROPERTY\tMeshRotationOffset\t3\t{v3(0, 0, 0)}\t0",
            f"PROPERTY\tMaterialSlots.0.MaterialAsset\t2\t{material}\t0",
        ])

    def primitive(self, name: str, primitive: str, material: str, position, scale, rotation=(0.0, 0.0, 0.0),
                  parent: int = 0, collider: bool = False, tags: tuple[str, ...] = ()) -> int:
        entity_id = self.entity(name, position, rotation, scale, parent, tags)
        if collider:
            self.collider(entity_id, 0)
        self.mesh(entity_id, f"engine://builtin/mesh/{primitive}", material)
        return entity_id

    def spline(self, name: str, points, width: float, thickness: float, material: str, closed: bool,
               uv_metres: float, guardrail: float = 0.0, collision_samples: int = 4) -> int:
        entity_id = self.entity(name, tags=("ground",))
        self.collider(entity_id, 4)
        self.mesh(entity_id, "", material)
        properties = [
            f"PROPERTY\tWidth\t0\t{f(width)}\t0",
            f"PROPERTY\tThickness\t0\t{f(thickness)}\t0",
            f"PROPERTY\tGuardrailHeight\t0\t{f(guardrail)}\t0",
            "PROPERTY\tLodCount\t1\t3\t0",
            "PROPERTY\tSamplesPerSegment\t1\t8\t0",
            f"PROPERTY\tCollisionSamplesPerSegment\t1\t{collision_samples}\t0",
            "PROPERTY\tMaxChordError\t0\t0.050000\t0",
            "PROPERTY\tMaxTangentAngleDegrees\t0\t4.000000\t0",
            f"PROPERTY\tUvMetersPerTile\t0\t{f(uv_metres)}\t0",
            f"PROPERTY\tClosed\t4\t{'true' if closed else 'false'}\t0",
            "PROPERTY\tGenerateMesh\t4\ttrue\t0",
            "PROPERTY\tGenerateCollision\t4\ttrue\t0",
            f"PROPERTY\tMaterialAsset\t2\t{material}\t0",
            f"PROPERTY\tPointCount\t1\t{len(points)}\t0",
        ]
        for index, (position, roll) in enumerate(points):
            properties.append(f"PROPERTY\tPoints.{index}\t3\t{v3(*position)}\t0")
            properties.append(f"PROPERTY\tPointRotations.{index}\t3\t{v3(0.0, 0.0, roll)}\t0")
        self.component(entity_id, "SplineComponent", properties)
        return entity_id

    def text(self) -> str:
        return "\n".join(self.lines) + "\n"


# ------------------------------------------------------------- track frames


class TrackCurve:
    """Evaluates the Catmull-Rom curve that SplineComponent builds from the samples."""

    def __init__(self, samples: list[layout.Sample], lap: float) -> None:
        self.samples = samples
        self.lap = lap
        self.points = [(p.x, p.y, p.z) for p in samples]

    def _segment(self, s: float):
        count = len(self.points)
        u = (s % self.lap) / self.lap * count
        index = int(math.floor(u)) % count
        return index, u - math.floor(u)

    def position(self, s: float):
        i, t = self._segment(s)
        n = len(self.points)
        return layout.catmull_rom(self.points[(i - 1) % n], self.points[i], self.points[(i + 1) % n],
                                  self.points[(i + 2) % n], t)

    def tangent(self, s: float):
        a = self.position(s - 0.5)
        b = self.position(s + 0.5)
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        length = math.sqrt(dx * dx + dy * dy + dz * dz) or 1.0
        return dx / length, dy / length, dz / length

    def bank(self, s: float) -> float:
        i, t = self._segment(s)
        n = len(self.samples)
        return self.samples[i].bank * (1.0 - t) + self.samples[(i + 1) % n].bank * t

    def frame(self, s: float):
        """Centre, flat tangent heading (deg) and banked right vector."""
        centre = self.position(s)
        tx, _, tz = self.tangent(s)
        flat = math.hypot(tx, tz) or 1.0
        right = (-tz / flat, 0.0, tx / flat)
        roll = math.radians(self.bank(s))
        banked_right = (right[0] * math.cos(roll), -math.sin(roll), right[2] * math.cos(roll))
        return centre, (tx / flat, tz / flat), banked_right

    def offset(self, s: float, lateral: float, lift: float = 0.0):
        centre, _, right = self.frame(s)
        return (centre[0] + right[0] * lateral, centre[1] + right[1] * lateral + lift,
                centre[2] + right[2] * lateral)

    def yaw(self, s: float) -> float:
        """Entity yaw whose forward (-Z) points along the racing direction."""
        _, (dx, dz), _ = self.frame(s)
        return math.degrees(math.atan2(-dx, -dz))

    def dense(self, step: float = 4.0):
        count = int(self.lap / step)
        return [self.position(self.lap * k / count) for k in range(count)]


# ------------------------------------------------------------------ assets


def ensure_meta(path: Path) -> None:
    meta = path.with_name(path.name + ".plutometa")
    if not meta.exists():
        meta.write_text(f"PLUTOASSET\t1\nID\t{uuid.uuid4().hex}\nIMPORTER_VERSION\t1\n", encoding="utf-8")


def write_textures(root: Path) -> list[Path]:
    from PIL import Image, ImageDraw

    folder = root / "Assets/Textures/Racetrack"
    folder.mkdir(parents=True, exist_ok=True)
    rng = random.Random(7)

    kerb = Image.new("RGB", (64, 256))
    pixels = kerb.load()
    for y in range(256):
        base = (196, 28, 32) if y < 128 else (232, 232, 228)
        for x in range(64):
            grain = rng.randint(-10, 10)
            # Darker worn edge along the track side of the kerb.
            wear = -38 if x < 4 or x > 59 else 0
            pixels[x, y] = tuple(max(0, min(255, c + grain + wear)) for c in base)
    kerb_path = folder / "Kerb.png"
    kerb.save(kerb_path)

    checker = Image.new("RGB", (256, 256), (238, 238, 238))
    draw = ImageDraw.Draw(checker)
    draw.rectangle((0, 0, 127, 127), fill=(18, 18, 18))
    draw.rectangle((128, 128, 255, 255), fill=(18, 18, 18))
    checker_path = folder / "Checker.png"
    checker.save(checker_path)

    for path in (kerb_path, checker_path):
        ensure_meta(path)
    return [kerb_path, checker_path]


MATERIAL_TEMPLATE = """Color={color},1
SurfaceType=Standard
AlphaMode=Opaque
AlphaCutoff=0.5
CastsShadow=true
TwoSided={two_sided}
UvScale={uv}
Metallic={metallic}
Roughness={roughness}
EmissionTexture=
EmissionChannelMask=false
EmissionRed=1,0,0,1
EmissionGreen=0,1,0,1
EmissionBlue=0,0,1,1
EmissionTexCoord=0
Emission={emission}
Subsurface=0
SubsurfaceColor=1,0.35,0.2
SubsurfaceRadius=1
Transmission=0
Ior=1.45
Thickness=0.01
AttenuationColor=1,1,1
AttenuationDistance=1
FlipNormalY=false
AlbedoTexture={albedo}
NormalTexture={normal}
MetallicTexture=
MetallicTextureChannel=0
RoughnessTexture=
RoughnessTextureChannel=0
ShaderGraph=engine://builtin/shadergraph/default-lit
"""

MATERIALS = {
    "Asphalt": dict(color="0.36,0.36,0.38", uv="4,4", roughness=0.86,
                    albedo="project://Textures/Standoff/Asphalt.png", normal="project://Textures/Standoff/Asphalt_N.png"),
    "Runoff": dict(color="0.78,0.78,0.76", uv="8,8", roughness=0.9, two_sided="true",
                   albedo="project://Textures/Standoff/ConcreteDark.png", normal="project://Textures/Standoff/ConcreteDark_N.png"),
    "Kerb": dict(color="1,1,1", uv="1,1", roughness=0.6, albedo="project://Textures/Racetrack/Kerb.png"),
    "Checker": dict(color="1,1,1", uv="1,10", roughness=0.7, albedo="project://Textures/Racetrack/Checker.png"),
    "Grass": dict(color="0.36,0.42,0.26", uv="400,400", roughness=1.0,
                  albedo="project://whispy-grass-meadow-unity/wispy-grass-meadow_albedo.png",
                  normal="project://whispy-grass-meadow-unity/wispy-grass-meadow_normal-ogl.png"),
    "Concrete": dict(color="0.85,0.85,0.83", uv="2,2", roughness=0.92,
                     albedo="project://Textures/Standoff/Concrete.png", normal="project://Textures/Standoff/Concrete_N.png"),
    "SeatsBlue": dict(color="0.08,0.24,0.62", roughness=0.55),
    "SeatsRed": dict(color="0.66,0.10,0.08", roughness=0.55),
    "Metal": dict(color="0.17,0.18,0.20", metallic=0.8, roughness=0.35),
    "Banner": dict(color="0.42,0.22,0.95", roughness=0.4, emission="1.2,0.6,3.5"),
    "StartLight": dict(color="1,0.08,0.04", roughness=0.3, emission="40,2,1"),
    "Glass": dict(color="0.14,0.22,0.30", metallic=0.3, roughness=0.08, emission="0.15,0.3,0.45"),
    "TreeTrunk": dict(color="0.28,0.19,0.12", roughness=0.95),
    "TreeCanopy": dict(color="0.06,0.15,0.05", roughness=0.9),
    "TreeCanopyDark": dict(color="0.03,0.09,0.035", roughness=0.9),
}


def write_materials(root: Path) -> list[Path]:
    folder = root / "Assets/Materials/Racetrack"
    folder.mkdir(parents=True, exist_ok=True)
    written = []
    for name, spec in MATERIALS.items():
        path = folder / f"{name}.plutomaterial"
        path.write_text(MATERIAL_TEMPLATE.format(
            color=spec["color"], uv=spec.get("uv", "1,1"), metallic=spec.get("metallic", 0),
            roughness=spec["roughness"], emission=spec.get("emission", "0,0,0"),
            albedo=spec.get("albedo", ""), normal=spec.get("normal", ""),
            two_sided=spec.get("two_sided", "false")), encoding="utf-8")
        ensure_meta(path)
        written.append(path)
    return written


def material(name: str) -> str:
    return f"project://Materials/Racetrack/{name}.plutomaterial"


# ------------------------------------------------------------ source scene


def read_entity_blocks(path: Path) -> list[tuple[int, int, list[str]]]:
    blocks: list[tuple[int, int, list[str]]] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("ENTITY\t"):
            fields = line.split("\t")
            blocks.append((int(fields[1]), int(fields[2]), [line]))
        elif blocks and not line.startswith("SCENE"):
            blocks[-1][2].append(line)
    return blocks


def with_transform(line: str, position=None, rotation=None) -> str:
    fields = line.split("\t")
    if position is not None:
        fields[5] = v3(*position)
    if rotation is not None:
        fields[6] = v3(*rotation)
    return "\t".join(fields)


def copy_source_entities(writer: SceneWriter, root: Path, curve: TrackCurve) -> None:
    blocks = read_entity_blocks(root / SOURCE_SCENE)
    parents = {entity_id: parent for entity_id, parent, _ in blocks}

    def under_car(entity_id: int) -> bool:
        while entity_id:
            if entity_id == CAR_ID:
                return True
            entity_id = parents.get(entity_id, 0)
        return False

    grid_s = START_LINE_S - GRID_BEHIND_LINE
    centre = curve.position(grid_s)
    yaw = curve.yaw(grid_s)
    _, (dx, dz), _ = curve.frame(grid_s)
    for entity_id, _, lines in blocks:
        if not (entity_id in COPIED_ROOTS or under_car(entity_id)):
            continue
        lines = list(lines)
        if entity_id == CAR_ID:
            lines[0] = with_transform(lines[0], (centre[0], centre[1] + 1.3, centre[2]), (0.0, yaw, 0.0))
        elif entity_id == 20087:
            lines[0] = with_transform(lines[0], (centre[0] - dx * 14.0, centre[1] + 5.0, centre[2] - dz * 14.0),
                                      (0.0, yaw, 0.0))
        elif lines[0].split("\t")[4] == "CentreOfMass":
            fields = lines[0].split("\t")
            x, y, _ = (float(value) for value in fields[5].split(","))
            fields[5] = v3(x, y, CENTRE_OF_MASS_Z)
            lines[0] = "\t".join(fields)
        for index, line in enumerate(lines):
            if line.startswith("PROPERTY\tVelocity\t") or line.startswith("PROPERTY\tAngular Velocity\t"):
                fields = line.split("\t")
                fields[3] = v3(0, 0, 0)
                lines[index] = "\t".join(fields)
            elif line.startswith("PROPERTY\tsuspensionDiagnostics\t"):
                lines[index] = "PROPERTY\tsuspensionDiagnostics\t4\tfalse\t0"
        writer.lines.extend(lines)


# ------------------------------------------------------------ track pieces


def build_track(writer: SceneWriter, curve: TrackCurve) -> None:
    samples = curve.samples
    track_points = [((p.x, p.y, p.z), p.bank) for p in samples]
    writer.spline("Track Surface", track_points, layout.TRACK_WIDTH, 0.4, material("Asphalt"),
                  closed=True, uv_metres=20.0)
    # The bed sits 10 cm below the asphalt and carries the concrete walls.
    bed_points = [((p.x, p.y - 0.10, p.z), p.bank) for p in samples]
    writer.spline("Run-off and Barriers", bed_points, layout.BED_WIDTH, 0.6, material("Runoff"),
                  closed=True, uv_metres=layout.BED_WIDTH, guardrail=1.3, collision_samples=4)

    kerb_offset = layout.TRACK_WIDTH * 0.5 + layout.KERB_WIDTH * 0.5 - 0.35
    for segment in layout.SEGMENTS:
        if segment.kind != "A" or not segment.kerbs:
            continue
        inside = 1.0 if segment.angle > 0.0 else -1.0
        runs = [
            ("Inside", inside, segment.start_s - 8.0, segment.end_s + 8.0),
            ("Exit", -inside, segment.start_s + segment.length * 0.45, segment.end_s + 30.0),
        ]
        for label, side, start, end in runs:
            count = max(int((end - start) / 4.0), 3)
            points = []
            for k in range(count + 1):
                s = start + (end - start) * k / count
                points.append((curve.offset(s, side * kerb_offset, 0.045), curve.bank(s)))
            writer.spline(f"Kerb {segment.name} {label}", points, layout.KERB_WIDTH, 0.3, material("Kerb"),
                          closed=False, uv_metres=3.0, collision_samples=2)


def build_pillars(writer: SceneWriter, curve: TrackCurve) -> None:
    group = writer.entity("Viaduct Pillars")
    dense = curve.dense(6.0)
    bed_half = layout.BED_WIDTH * 0.5
    spacing = 26.0
    s = 0.0
    while s < curve.lap:
        for lateral in (-bed_half + 2.0, 0.0, bed_half - 2.0):
            top = curve.offset(s, lateral, -0.10 - 0.6)
            if top[1] < 1.4:
                continue
            # Never stand a pillar on another road: skip where a lower part of
            # the circuit passes within its bed width.
            blocked = any(math.hypot(top[0] - p[0], top[2] - p[2]) < bed_half + 5.0 and p[1] < top[1] - 2.0
                          for p in dense)
            if blocked:
                continue
            writer.primitive("Pillar", "cylinder", material("Concrete"), (top[0], top[1] * 0.5, top[2]),
                             (2.0, top[1], 2.0), parent=group)
        s += spacing


def build_start_area(writer: SceneWriter, curve: TrackCurve) -> list[tuple[float, float, float, float]]:
    """Gantry, start line, grandstands and pit building. Returns keep-out boxes (x0, z0, x1, z1)."""
    centre, (dx, dz), right = curve.frame(START_LINE_S)
    yaw = curve.yaw(START_LINE_S)
    flat_right = (-dz, dx)
    group = writer.entity("Start Finish")

    # Line: u runs along the travel direction (2 m), v across the track (20 m).
    writer.primitive("Start Line", "cube", material("Checker"), (centre[0], centre[1] + 0.012, centre[2]),
                     (2.0, 0.02, layout.TRACK_WIDTH), rotation=(0.0, yaw + 90.0, 0.0), parent=group)

    leg_offset = layout.BED_WIDTH * 0.5 + 2.5
    for side in (-1.0, 1.0):
        writer.primitive("Gantry Leg", "cube", material("Metal"),
                         (centre[0] + flat_right[0] * leg_offset * side, 5.5, centre[2] + flat_right[1] * leg_offset * side),
                         (1.6, 11.0, 1.6), rotation=(0.0, yaw, 0.0), parent=group)
    beam_y = 10.0
    writer.primitive("Gantry Beam", "cube", material("Metal"), (centre[0], beam_y, centre[2]),
                     (2.0, 2.4, leg_offset * 2.0 + 1.6), rotation=(0.0, yaw + 90.0, 0.0), parent=group)
    for facing in (-1.0, 1.0):
        writer.primitive("Gantry Banner", "cube", material("Banner"),
                         (centre[0] + dx * 1.05 * facing, beam_y, centre[2] + dz * 1.05 * facing),
                         (0.12, 1.8, 32.0), rotation=(0.0, yaw + 90.0, 0.0), parent=group)
    for light in range(5):
        lateral = (light - 2) * 2.6
        writer.primitive("Start Light", "sphere", material("StartLight"),
                         (centre[0] - dx * 1.2 + flat_right[0] * lateral, beam_y - 1.7,
                          centre[2] - dz * 1.2 + flat_right[1] * lateral),
                         (1.0, 1.0, 1.0), parent=group)

    # Grandstands face the pit straight from its outside (-right) edge.
    keep_out = []
    stands = writer.entity("Grandstands")
    stand_length, gap = 56.0, 8.0
    first = 30.0
    base = layout.BED_WIDTH * 0.5 + 6.0
    for stand in range(4):
        along = first + stand * (stand_length + gap) + stand_length * 0.5 - START_LINE_S
        cx, cz = centre[0] + dx * along, centre[2] + dz * along
        seats = material("SeatsBlue" if stand % 2 == 0 else "SeatsRed")
        for tier in range(6):
            depth = base + tier * 2.4 + 1.2
            height = 1.0 + tier * 1.1
            writer.primitive("Stand Tier", "cube", seats,
                             (cx - flat_right[0] * depth, height * 0.5, cz - flat_right[1] * depth),
                             (stand_length, height, 2.4), rotation=(0.0, yaw + 90.0, 0.0), parent=stands,
                             collider=True)
        back = base + 6 * 2.4 + 0.5
        writer.primitive("Stand Back Wall", "cube", material("Concrete"),
                         (cx - flat_right[0] * back, 5.5, cz - flat_right[1] * back),
                         (stand_length, 11.0, 1.0), rotation=(0.0, yaw + 90.0, 0.0), parent=stands, collider=True)
        roof = base + 6 * 2.4 * 0.5
        writer.primitive("Stand Roof", "cube", material("Metal"),
                         (cx - flat_right[0] * roof, 11.2, cz - flat_right[1] * roof),
                         (stand_length + 2.0, 0.4, 6 * 2.4 + 3.0), rotation=(0.0, yaw + 90.0, 0.0), parent=stands)
    x0 = centre[0] + dx * (first - START_LINE_S) - 4
    x1 = centre[0] + dx * (first + 4 * (stand_length + gap) - START_LINE_S) + 4
    keep_out.append((min(x0, x1), -(base + 20.0), max(x0, x1), -base + 2.0))

    # Pit building on the infield side of the straight.
    pit = writer.entity("Pit Building")
    pit_offset = layout.BED_WIDTH * 0.5 + 12.0
    pit_length = 200.0
    along = 140.0 - START_LINE_S
    px, pz = centre[0] + dx * along + flat_right[0] * pit_offset, centre[2] + dz * along + flat_right[1] * pit_offset
    writer.primitive("Pit Garage Block", "cube", material("Concrete"), (px, 3.5, pz), (pit_length, 7.0, 16.0),
                     rotation=(0.0, yaw + 90.0, 0.0), parent=pit, collider=True)
    writer.primitive("Pit Window Strip", "cube", material("Glass"),
                     (px - flat_right[0] * 8.05, 5.0, pz - flat_right[1] * 8.05), (pit_length - 6.0, 2.2, 0.1),
                     rotation=(0.0, yaw + 90.0, 0.0), parent=pit)
    writer.primitive("Pit Roof", "cube", material("Metal"), (px, 7.2, pz), (pit_length + 4.0, 0.4, 19.0),
                     rotation=(0.0, yaw + 90.0, 0.0), parent=pit)
    writer.primitive("Race Control Tower", "cube", material("Glass"), (px + dx * 40.0, 11.0, pz + dz * 40.0),
                     (14.0, 8.0, 12.0), rotation=(0.0, yaw, 0.0), parent=pit)
    keep_out.append((px - pit_length * 0.5 - 6, pz - 14, px + pit_length * 0.5 + 6, pz + 14))
    return keep_out


def build_trees(writer: SceneWriter, curve: TrackCurve, keep_out) -> int:
    rng = random.Random(1987)
    dense = curve.dense(8.0)
    xs = [p[0] for p in dense]
    zs = [p[2] for p in dense]
    margin = 140.0
    clear = layout.BED_WIDTH * 0.5 + 12.0
    group = writer.entity("Trees")
    cell = 44.0
    planted = 0
    x = min(xs) - margin
    while x < max(xs) + margin:
        z = min(zs) - margin
        while z < max(zs) + margin:
            tx = x + rng.uniform(-cell * 0.4, cell * 0.4)
            tz = z + rng.uniform(-cell * 0.4, cell * 0.4)
            z += cell
            if rng.random() > 0.62:
                continue
            if any(x0 <= tx <= x1 and z0 <= tz <= z1 for x0, z0, x1, z1 in keep_out):
                continue
            # Elevated sections still shade the ground below; keep the whole
            # bed footprint plus a verge clear, regardless of height.
            if min(math.hypot(tx - p[0], tz - p[2]) for p in dense) < clear:
                continue
            scale = rng.uniform(0.75, 1.35)
            tree = writer.entity("Tree", (tx, 0.0, tz), (0.0, rng.uniform(0, 360), 0.0), (scale, scale, scale),
                                 parent=group)
            writer.primitive("Trunk", "cylinder", material("TreeTrunk"), (0.0, 2.5, 0.0), (0.8, 5.0, 0.8), parent=tree)
            canopy = material("TreeCanopy" if rng.random() < 0.6 else "TreeCanopyDark")
            writer.primitive("Canopy", "sphere", canopy, (0.0, 8.5, 0.0), (6.5, 10.0, 6.5), parent=tree)
            planted += 1
        x += cell
    return planted


def build_checkpoints(writer: SceneWriter, curve: TrackCurve) -> None:
    group = writer.entity("Checkpoints")
    for index in range(CHECKPOINT_COUNT):
        s = START_LINE_S + curve.lap * index / CHECKPOINT_COUNT
        writer.entity(f"Checkpoint_{index}", curve.position(s), (0.0, curve.yaw(s), 0.0), parent=group)


def build_race_director(writer: SceneWriter) -> None:
    entity_id = writer.entity("Race Director")
    writer.component(entity_id, "ScriptComponent", [
        "PROPERTY\tSource\t2\tCoD.Scripts.RaceLapTimer\t0",
        f"PROPERTY\tvehicle\t9\t{CAR_ID}\t0",
        "PROPERTY\tcheckpointPrefix\t2\tCheckpoint_\t0",
        f"PROPERTY\tcheckpointCount\t1\t{CHECKPOINT_COUNT}\t0",
        f"PROPERTY\tgateHalfWidth\t0\t{f(layout.BED_WIDTH * 0.5 + 1.0)}\t0",
        "PROPERTY\tgateHalfHeight\t0\t6.000000\t0",
        "PROPERTY\trespawnHeight\t0\t1.600000\t0",
        "PROPERTY\tdocumentPath\t2\tUI/race-hud.rml\t0",
        "PROPERTY\tinputMappingAsset\t2\tproject://Input/car.plutoinput\t0",
        "PROPERTY\tmessageDuration\t0\t2.500000\t0",
    ])
    writer.component(entity_id, "CanvasComponent", [
        "PROPERTY\tEnabled\t4\ttrue\t0",
        "PROPERTY\tRenderMode\t6\t0\t4\tScreen Space\tWorld Screen Space\tScreen Space Camera\tWorld Space",
        "PROPERTY\tScaleMode\t6\t0\t3\tConstantPixels\tScaleWithScreenSize\tConstantPhysicalSize",
        "PROPERTY\tScaleFactor\t0\t1.000000\t0",
        "PROPERTY\tReferenceResolution\t7\t1920.000000,1080.000000\t0",
        "PROPERTY\tMatchWidthOrHeight\t0\t0.500000\t0",
        "PROPERTY\tScreenMatchMode\t6\t0\t3\tMatchWidthOrHeight\tExpand\tShrink",
        "PROPERTY\tSortingOrder\t1\t1\t0",
        "PROPERTY\tWorldSizeMode\t6\t0\t3\tWorldUnits\tConstantScreenSize\tDistanceScaled",
        "PROPERTY\tFaceCamera\t4\ttrue\t0",
        "PROPERTY\tBackend\t6\t1\t2\tLegacy Native\tRmlUi",
        "PROPERTY\tContentSource\t6\t0\t2\tRML Document\tText",
        "PROPERTY\tDocumentPath\t2\tproject://UI/race-hud.rml\t0",
    ])


# ------------------------------------------------------------ registration

ASSET_TYPES = {".plutoscene": "Scene", ".plutomaterial": "Material", ".png": "Texture", ".cs": "Script",
               ".rml": "RML Document", ".rcss": "Unknown"}


def register(root: Path, files: list[Path], always_include: list[str]) -> None:
    project = root / "CoDplutoproject.plutoproject"
    lines = project.read_text(encoding="utf-8").splitlines()
    assets = root / "Assets"
    entries = {}
    for path in files:
        reference = "project://" + path.relative_to(assets).as_posix()
        entries[reference] = f"ASSET\t{reference}\t{path.stat().st_size}\t{ASSET_TYPES[path.suffix]}"

    # Replace existing entries in place and insert new ones before the first
    # alphabetically later entry, leaving every unrelated line untouched.
    result = list(lines)
    for reference, entry in entries.items():
        existing = next((i for i, line in enumerate(result)
                         if line.startswith("ASSET\t") and line.split("\t")[1] == reference), None)
        if existing is not None:
            result[existing] = entry
            continue
        asset_indices = [i for i, line in enumerate(result) if line.startswith("ASSET\t")]
        later = next((i for i in asset_indices if result[i].split("\t")[1] > reference), asset_indices[-1] + 1)
        result.insert(later, entry)

    includes = {line.split("\t")[1] for line in result if line.startswith("BUILD_ALWAYS_INCLUDE\t")}
    last_include = max(i for i, line in enumerate(result) if line.startswith("BUILD_ALWAYS_INCLUDE\t"))
    for reference in always_include:
        if reference not in includes:
            last_include += 1
            result.insert(last_include, f"BUILD_ALWAYS_INCLUDE\t{reference}")
    project.write_text("\n".join(result) + "\n", encoding="utf-8")


# --------------------------------------------------------------------- main


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2])
    root = parser.parse_args().project.resolve()

    samples, lap = layout.build_samples()
    curve = TrackCurve(samples, lap)

    textures = write_textures(root)
    materials = write_materials(root)

    writer = SceneWriter()
    ground = writer.entity("Ground", (-90.0, -0.5, 266.0), scale=(2400.0, 1.0, 2400.0), tags=("ground",))
    writer.collider(ground, 0)
    writer.mesh(ground, "engine://builtin/mesh/cube", material("Grass"))
    copy_source_entities(writer, root, curve)
    build_track(writer, curve)
    build_pillars(writer, curve)
    keep_out = build_start_area(writer, curve)
    trees = build_trees(writer, curve, keep_out)
    build_checkpoints(writer, curve)
    build_race_director(writer)

    scene_path = root / OUTPUT_SCENE
    scene_path.write_text(writer.text(), encoding="utf-8")
    ensure_meta(scene_path)

    ui = [root / "Assets/UI/race-hud.rml", root / "Assets/UI/race-hud.rcss"]
    scripts = [root / "Assets/Scripts/RaceLapTimer.cs"]
    for path in ui + scripts:
        ensure_meta(path)
    register(root, textures + materials + ui + scripts + [scene_path],
             ["project://UI/race-hud.rcss", "project://UI/race-hud.rml", "project://Scenes/Racetrack.plutoscene"])

    entities = sum(1 for line in writer.lines if line.startswith("ENTITY\t"))
    print(f"Racetrack: lap {lap:.0f} m, {len(samples)} control points, {trees} trees, "
          f"{entities} entities -> {scene_path.relative_to(root)}")


if __name__ == "__main__":
    main()
