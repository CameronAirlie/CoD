"""Repair native Terminal sign UVs without regenerating scenes or geometry."""
from pathlib import Path
import argparse
import struct

SIGN_MATERIALS = {'Airport', 'Departures', 'Gate', 'Burger', 'Books', 'Beauty', 'Airline', 'CargoLabel'}


def sign_uvs(points, normal):
    # Texture rows start at the top. Viewed from outside a vertical face,
    # screen-right is normal cross world-up.
    horizontal = [p[0] * normal[2] - p[2] * normal[0] for p in points]
    left, right = min(horizontal), max(horizontal)
    bottom, top = min(p[1] for p in points), max(p[1] for p in points)
    if right - left < 1e-6 or top - bottom < 1e-6:
        raise ValueError('Degenerate sign face')
    return [((x - left) / (right - left), (top - p[1]) / (top - bottom))
            for x, p in zip(horizontal, points)]


def repair(data):
    data = bytearray(data)
    magic, version, count = struct.unpack_from('<IIQ', data)
    if magic != 0x4d47504c or version != 5:
        raise ValueError('Expected LPGM v5')
    stride = struct.calcsize('<14f4i4f')
    cursor = 16 + count * stride

    def read(fmt):
        nonlocal cursor
        values = struct.unpack_from('<' + fmt, data, cursor)
        cursor += struct.calcsize('<' + fmt)
        return values

    def string():
        nonlocal cursor
        length, = read('Q')
        value = data[cursor:cursor + length].decode()
        cursor += length
        return value

    indices, = read('Q')
    cursor += indices * 4
    subcount, = read('Q')
    subs = []
    for _ in range(subcount):
        start, size, material, bone, *_ = read('IIIi4f')
        string()
        bones, = read('Q')
        if bones or bone != -1:
            raise ValueError('Unexpected skinned Terminal mesh')
        subs.append((start, size, material))
    skinned, bones, clips, embedded = read('BQQQ')
    if skinned or bones or clips or embedded:
        raise ValueError('Unexpected embedded mesh data')
    materials, = read('Q')
    refs = [string() for _ in range(materials)]
    faces = 0
    for start, size, material in subs:
        if Path(refs[material]).stem not in SIGN_MATERIALS:
            continue
        if size % 6:
            raise ValueError('Sign geometry must contain quad triangle pairs')
        for first in range(start, start + size, 6):
            vertices = [struct.unpack_from('<8f', data, 16 + i * stride) for i in range(first, first + 6)]
            normal = vertices[0][3:6]
            if abs(normal[1]) > 0.5:
                continue
            if any(any(abs(v[k + 3] - normal[k]) > 1e-5 for k in range(3)) for v in vertices):
                raise ValueError('Sign triangles do not share a face normal')
            uvs = sign_uvs([v[:3] for v in vertices], normal)
            for i, uv in zip(range(first, first + 6), uvs):
                struct.pack_into('<2f', data, 16 + i * stride + 24, *uv)
            faces += 1
    return bytes(data), faces


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    pending = []
    total = 0
    for path in sorted((Path(__file__).resolve().parents[1] / 'Assets/Meshes/Terminal').glob('*.plutomesh')):
        original = path.read_bytes()
        fixed, faces = repair(original)
        total += faces
        if fixed != original:
            pending.append((path, fixed))
    for path, fixed in pending:
        print(('Needs repair: ' if args.check else 'Repaired: ') + path.name)
        if not args.check:
            path.write_bytes(fixed)
    print(f'{total} vertical sign faces checked; {len(pending)} meshes ' + ('need repair.' if args.check else 'updated.'))
    raise SystemExit(1 if args.check and pending else 0)
