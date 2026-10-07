"""Validate an offline soldier UV/LOD migration against its preserved v5 source."""
import argparse
import io
import math
import struct
from pathlib import Path


def read_mesh(path):
    stream = io.BytesIO(Path(path).read_bytes())

    def unpack(fmt):
        size = struct.calcsize('<' + fmt)
        data = stream.read(size)
        if len(data) != size:
            raise ValueError('Truncated mesh')
        return struct.unpack('<' + fmt, data)

    def string():
        return stream.read(unpack('Q')[0]).decode('utf-8')

    magic, version, count = unpack('IIQ')
    if (magic, version) != (0x4D47504C, 5):
        raise ValueError('Expected native mesh version 5')
    vertices = [unpack('14f4i4f') for _ in range(count)]
    indices = unpack(f'{unpack("Q")[0]}I')
    submeshes = []
    for _ in range(unpack('Q')[0]):
        header = unpack('IIIi4f')
        name = string()
        lods = [unpack('IIff') for _ in range(unpack('Q')[0])]
        submeshes.append((header, name, lods))
    rig_start = stream.tell()
    unpack('B')
    for _ in range(unpack('Q')[0]):
        string()
        unpack('ii48f')
    for _ in range(unpack('Q')[0]):
        unpack('B')
        string()
        unpack('i3fBf')
    for _ in range(unpack('Q')[0]):
        string()
        unpack('i16f')
    rig_end = stream.tell()
    references = [string() for _ in range(unpack('Q')[0])]
    return vertices, indices, submeshes, stream.getvalue()[rig_start:rig_end], references


def validate(original, prepared):
    before, old_indices, old_submeshes, old_rig, old_refs = read_mesh(original)
    after, indices, submeshes, rig, refs = read_mesh(prepared)
    if len(before) != len(after) or old_rig != rig or old_refs != refs:
        raise ValueError('Vertex count, rig, animation nodes or material bindings changed')
    if indices[:len(old_indices)] != old_indices:
        raise ValueError('LOD0 indices changed')
    if len(submeshes) != len(old_submeshes):
        raise ValueError('Submesh count changed')
    for old, new in zip(before, after):
        if old[:7] != new[:7] or old[12:] != new[12:]:
            raise ValueError('Positions, normals, U, secondary UVs or skin influences changed')
        if not math.isclose(new[7], 1 - old[7], abs_tol=2e-7):
            raise ValueError('Primary V was not converted exactly once')
        if not all(math.isfinite(value) for value in new):
            raise ValueError('Nonfinite vertex payload')
        if not math.isclose(sum(v * v for v in new[8:11]), 1, abs_tol=1e-4):
            raise ValueError('Invalid regenerated tangent')
    for old, new in zip(old_submeshes, submeshes):
        if old[0][:4] != new[0][:4] or old[1] != new[1]:
            raise ValueError('Submesh identity or material slot changed')
        center, radius = new[0][4:7], new[0][7]
        previous = new[0][1]
        for offset, count, _, _ in new[2]:
            if count % 3 or count > previous or offset + count > len(indices):
                raise ValueError('Invalid or increasing LOD range')
            if any(index >= len(after) for index in indices[offset:offset + count]):
                raise ValueError('LOD index outside vertex buffer')
            if any(math.dist(after[index][:3], center) > radius + 1e-5
                   for index in indices[offset:offset + count]):
                raise ValueError('Submesh bound excludes geometry')
            previous = count
    totals = [sum(s[2][min(lod, len(s[2]) - 1)][1] // 3 for s in submeshes) for lod in range(4)]
    if not totals[-1] < totals[0]:
        raise ValueError('LOD generation did not reduce triangles')
    print(f'Validated UV conversion, tangent space, rig, materials and LOD0 preservation; triangles: {totals}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('original', type=Path)
    parser.add_argument('prepared', type=Path)
    args = parser.parse_args()
    validate(args.original, args.prepared)
