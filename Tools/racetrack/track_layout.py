"""Centreline layout for the Pluto Ring racetrack.

The circuit is a figure-of-eight described with exact turtle primitives
(straights and constant-radius arcs) so corner radii are deliberate. Two
straights are solved for closure. Elevation and banking are keyed by arc
length, and the result is resampled into evenly spaced Catmull-Rom control
points, matching how SplineComponent interpolates them.

Conventions (PlutoGE): Y up. Heading 0 travels +X; positive arc angles turn
towards the spline's right vector cross(tangent, up), i.e. towards +Z from a
+X heading. A positive spline point roll (rotation Z) lowers the right edge,
so right-hand corners bank with positive roll.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

TRACK_WIDTH = 20.0
KERB_WIDTH = 2.2
RUNOFF_EACH_SIDE = 13.0
BED_WIDTH = TRACK_WIDTH + 2.0 * RUNOFF_EACH_SIDE
BRIDGE_HEIGHT = 9.0
SURFACE_LIFT = 0.20  # track surface above the ground plane on flat sections


@dataclass
class Segment:
    name: str
    kind: str  # "S" straight or "A" arc
    length: float = 0.0
    radius: float = 0.0
    angle: float = 0.0  # degrees, positive = right turn
    bank: float = 0.0  # degrees, magnitude; sign follows the turn
    free: bool = False
    kerbs: bool = False
    start_s: float = 0.0
    end_s: float = 0.0
    start_heading: float = 0.0


# Lap order, starting where the start/finish straight passes beneath the bridge.
SEGMENTS: list[Segment] = [
    Segment("Pit Straight", "S", length=330.0),
    Segment("T1 Pluto Hairpin", "A", radius=34.0, angle=120.0, kerbs=True),
    Segment("Hairpin Exit", "S", length=150.0),
    Segment("T2 Esses Left", "A", radius=95.0, angle=-55.0, kerbs=True),
    Segment("T3 Esses Right", "A", radius=95.0, angle=95.0, kerbs=True),
    Segment("T4 Esses Left", "A", radius=110.0, angle=-40.0, kerbs=True),
    Segment("Carousel Approach", "S", length=120.0),
    Segment("T5 Carousel", "A", radius=125.0, angle=140.0, bank=7.0, kerbs=True),
    Segment("Hill Climb", "S", length=210.0),
    Segment("T6 Climb Kink", "A", radius=150.0, angle=10.0),
    Segment("Bridge", "S", free=True),
    Segment("T7 Drop", "A", radius=70.0, angle=-80.0, bank=5.0, kerbs=True),
    Segment("Back Straight", "S", length=110.0),
    Segment("T8 Kink", "A", radius=260.0, angle=-14.0),
    Segment("Back Straight End", "S", length=120.0),
    Segment("T9 Last Hairpin", "A", radius=36.0, angle=-176.0, kerbs=True),
    Segment("Final Straight", "S", free=True),
]


def _direction(heading_deg: float) -> tuple[float, float]:
    radians = math.radians(heading_deg)
    return math.cos(radians), math.sin(radians)


def _walk(segments: list[Segment], free_lengths: tuple[float, float]):
    """Return end position and heading, assigning free lengths in order."""
    free_iter = iter(free_lengths)
    x = z = heading = s = 0.0
    for segment in segments:
        segment.start_s = s
        segment.start_heading = heading
        if segment.kind == "S":
            if segment.free:
                segment.length = next(free_iter)
            dx, dz = _direction(heading)
            x += dx * segment.length
            z += dz * segment.length
            s += segment.length
        else:
            sign = 1.0 if segment.angle > 0.0 else -1.0
            # Right of the heading is (-dz, dx); the centre is on the turn side.
            dx, dz = _direction(heading)
            cx = x + (-dz) * segment.radius * sign
            cz = z + dx * segment.radius * sign
            new_heading = heading + segment.angle
            ndx, ndz = _direction(new_heading)
            x = cx - (-ndz) * segment.radius * sign
            z = cz - ndx * segment.radius * sign
            heading = new_heading
            segment.length = abs(math.radians(segment.angle)) * segment.radius
            s += segment.length
        segment.end_s = s
    return x, z, heading, s


def solve_layout(segments: list[Segment] = SEGMENTS) -> float:
    """Solve the two free straights so the lap closes. Returns lap length."""
    free = [segment for segment in segments if segment.free]
    if len(free) != 2:
        raise ValueError("exactly two free straights are required")
    base = _walk(segments, (0.0, 0.0))
    # End position is linear in each free straight's length.
    ax = _walk(segments, (1.0, 0.0))
    bx = _walk(segments, (0.0, 1.0))
    a = (ax[0] - base[0], ax[1] - base[1])
    b = (bx[0] - base[0], bx[1] - base[1])
    target = (-base[0], -base[1])
    det = a[0] * b[1] - a[1] * b[0]
    if abs(det) < 1e-6:
        raise ValueError("free straights are parallel")
    la = (target[0] * b[1] - target[1] * b[0]) / det
    lb = (a[0] * target[1] - a[1] * target[0]) / det
    if la < 20.0 or lb < 20.0:
        raise ValueError(f"layout does not close with positive straights: {la:.1f}, {lb:.1f}")
    end = _walk(segments, (la, lb))
    heading_error = (end[2] + 180.0) % 360.0 - 180.0
    if abs(end[0]) > 1e-3 or abs(end[1]) > 1e-3 or abs(heading_error) > 1e-6:
        raise ValueError(f"closure failed: {end}")
    return end[3]


def _pose_at(segments: list[Segment], s: float):
    """Exact turtle position (x, z), heading and signed curvature at arc length s."""
    for segment in segments:
        if segment.start_s - 1e-9 <= s <= segment.end_s + 1e-9:
            break
    else:
        segment = segments[-1]
    # Re-walk to the segment start (segments are few; this stays cheap).
    x = z = 0.0
    for previous in segments:
        if previous is segment:
            break
        heading = previous.start_heading
        if previous.kind == "S":
            dx, dz = _direction(heading)
            x += dx * previous.length
            z += dz * previous.length
        else:
            sign = 1.0 if previous.angle > 0.0 else -1.0
            dx, dz = _direction(heading)
            cx = x + (-dz) * previous.radius * sign
            cz = z + dx * previous.radius * sign
            ndx, ndz = _direction(heading + previous.angle)
            x = cx - (-ndz) * previous.radius * sign
            z = cz - ndx * previous.radius * sign
    local = min(max(s - segment.start_s, 0.0), segment.length)
    heading = segment.start_heading
    if segment.kind == "S":
        dx, dz = _direction(heading)
        return x + dx * local, z + dz * local, heading, 0.0, segment
    sign = 1.0 if segment.angle > 0.0 else -1.0
    dx, dz = _direction(heading)
    cx = x + (-dz) * segment.radius * sign
    cz = z + dx * segment.radius * sign
    swept = math.degrees(local / segment.radius) * sign
    ndx, ndz = _direction(heading + swept)
    return (cx - (-ndz) * segment.radius * sign,
            cz - ndx * segment.radius * sign,
            heading + swept, sign / segment.radius, segment)


def _smoothstep(t: float) -> float:
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def _segment(name: str) -> Segment:
    return next(segment for segment in SEGMENTS if segment.name == name)


def bridge_crossing_s() -> float:
    """Arc length where the bridge passes over the start/finish straight (z = 0)."""
    bridge = _segment("Bridge")
    x, z, heading, _, _ = _pose_at(SEGMENTS, bridge.start_s)
    dz = _direction(heading)[1]
    return bridge.start_s + (0.0 - z) / dz


def elevation_keys() -> list[tuple[float, float]]:
    """(arc length, height) keys; heights blend with smoothstep between keys."""
    carousel = _segment("T5 Carousel")
    back = _segment("Back Straight")
    crossing = bridge_crossing_s()
    plateau_start = crossing - 95.0
    plateau_end = crossing + 55.0
    climb_start = carousel.start_s + carousel.length * 0.45
    return [
        (0.0, 0.0),
        (climb_start, 0.0),
        ((climb_start + plateau_start) * 0.5, BRIDGE_HEIGHT * 0.55),
        (plateau_start, BRIDGE_HEIGHT),
        (plateau_end, BRIDGE_HEIGHT),
        (back.start_s + back.length * 0.5, BRIDGE_HEIGHT * 0.25),
        (back.end_s + 40.0, 0.0),
    ]


def height_at(s: float, lap_length: float, keys: list[tuple[float, float]]) -> float:
    s %= lap_length
    for (s0, y0), (s1, y1) in zip(keys, keys[1:] + [(lap_length, keys[0][1])]):
        if s0 <= s <= s1:
            return y0 + (y1 - y0) * _smoothstep((s - s0) / max(s1 - s0, 1e-6))
    return keys[0][1]


def bank_at(s: float, lap_length: float, ramp: float = 35.0) -> float:
    """Signed roll in degrees, ramped in and out across each banked arc."""
    s %= lap_length
    for segment in SEGMENTS:
        if segment.kind != "A" or segment.bank <= 0.0:
            continue
        inside = min(s - segment.start_s, segment.end_s - s)
        if inside > -ramp:
            weight = _smoothstep((inside + ramp) / (2.0 * ramp))
            if weight > 0.0:
                return segment.bank * weight * (1.0 if segment.angle > 0.0 else -1.0)
    return 0.0


def bank_lift(height: float, bank: float) -> float:
    """Raise banked ground-level corners so the low edge of the bed clears the ground."""
    low_edge_drop = BED_WIDTH * 0.5 * math.sin(math.radians(abs(bank)))
    return max(low_edge_drop * 1.05 - height, 0.0)


@dataclass
class Sample:
    s: float
    x: float
    y: float
    z: float
    heading: float
    curvature: float
    bank: float
    segment: Segment = field(repr=False)

    @property
    def tangent(self) -> tuple[float, float, float]:
        dx, dz = _direction(self.heading)
        return dx, 0.0, dz

    @property
    def right(self) -> tuple[float, float, float]:
        """Banked right vector, matching the SplineComponent cross-section."""
        dx, dz = _direction(self.heading)
        flat_right = (-dz, 0.0, dx)
        roll = math.radians(self.bank)
        return (flat_right[0] * math.cos(roll), -math.sin(roll), flat_right[2] * math.cos(roll))

    def offset(self, lateral: float, lift: float = 0.0) -> tuple[float, float, float]:
        rx, ry, rz = self.right
        return self.x + rx * lateral, self.y + ry * lateral + lift, self.z + rz * lateral


def build_samples(spacing: float = 10.0) -> tuple[list[Sample], float]:
    lap = solve_layout()
    keys = elevation_keys()
    count = max(int(round(lap / spacing)), 16)
    samples = []
    for index in range(count):
        s = lap * index / count
        x, z, heading, curvature, segment = _pose_at(SEGMENTS, s)
        height = height_at(s, lap, keys)
        bank = bank_at(s, lap)
        samples.append(Sample(s, x, height + bank_lift(height, bank) + SURFACE_LIFT, z,
                              heading, curvature, bank, segment))
    return samples, lap


def catmull_rom(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return tuple(0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)
                 for a, b, c, d in zip(p0, p1, p2, p3))


def dense_curve(points: list[tuple[float, float, float]], per_segment: int = 8, closed: bool = True):
    curve = []
    n = len(points)
    last = n if closed else n - 1
    for i in range(last):
        p0 = points[(i - 1) % n] if closed or i > 0 else points[0]
        p1 = points[i]
        p2 = points[(i + 1) % n]
        p3 = points[(i + 2) % n] if closed or i + 2 < n else points[-1]
        for k in range(per_segment):
            curve.append(catmull_rom(p0, p1, p2, p3, k / per_segment))
    return curve


def report() -> None:
    samples, lap = build_samples()
    print(f"lap length {lap:.1f} m, {len(samples)} control points")
    for segment in SEGMENTS:
        detail = (f"R={segment.radius:.0f} {segment.angle:+.0f}deg bank {segment.bank:.0f}"
                  if segment.kind == "A" else "")
        print(f"  {segment.name:<20} {segment.start_s:7.1f}-{segment.end_s:7.1f}  len {segment.length:6.1f} {detail}")

    curve = dense_curve([(p.x, p.y, p.z) for p in samples])
    # Minimum radius on the interpolated curve.
    min_radius = float("inf")
    for a, b, c in zip(curve, curve[1:] + curve[:1], curve[2:] + curve[:2]):
        ab = math.dist((a[0], a[2]), (b[0], b[2]))
        bc = math.dist((b[0], b[2]), (c[0], c[2]))
        ca = math.dist((c[0], c[2]), (a[0], a[2]))
        area = abs((b[0] - a[0]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[0] - a[0])) * 0.5
        if area > 1e-6:
            min_radius = min(min_radius, ab * bc * ca / (4.0 * area))
    print(f"min interpolated radius {min_radius:.1f} m")

    # Clearance between parts of the circuit that are far apart along the lap.
    worst_overlap = None
    stride = 3
    coarse = curve[::stride]
    per = len(coarse)
    for i, a in enumerate(coarse):
        for j in range(i + 1, per):
            along = min(j - i, per - (j - i)) * (lap / per)
            if along < BED_WIDTH * 2.5:
                continue
            horizontal = math.dist((a[0], a[2]), (coarse[j][0], coarse[j][2]))
            if horizontal < BED_WIDTH + 4.0:
                vertical = abs(a[1] - coarse[j][1])
                if worst_overlap is None or vertical < worst_overlap[0]:
                    worst_overlap = (vertical, horizontal, a, coarse[j])
    if worst_overlap:
        vertical, horizontal, a, b = worst_overlap
        print(f"closest crossing: vertical {vertical:.2f} m at horizontal {horizontal:.1f} m "
              f"({a[0]:.0f},{a[1]:.1f},{a[2]:.0f}) vs ({b[0]:.0f},{b[1]:.1f},{b[2]:.0f})")
    xs = [p.x for p in samples]
    zs = [p.z for p in samples]
    print(f"extent x [{min(xs):.0f}, {max(xs):.0f}] z [{min(zs):.0f}, {max(zs):.0f}]")


if __name__ == "__main__":
    report()
