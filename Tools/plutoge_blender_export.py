"""Small deterministic exporter for PlutoGE LPGM v5 / LPGC v5.

Uses Blender mesh triangles and baked bone transforms. The binary layout matches
engine/assets/src/AssetManager.cpp and MeshVertexData (88 bytes). No engine source
or importer installation is modified. All generated vertices have one rigid bone
influence; hands and weapon parts share one animated skeleton.
"""
import math
import struct
from pathlib import Path
import bpy
from mathutils import Matrix, Vector

CONVERSION = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))


class Writer:
    def __init__(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        self.file = path.open('wb')

    def pod(self, fmt, *values):
        self.file.write(struct.pack('<' + fmt, *values))

    def string(self, value):
        encoded = value.encode('utf-8')
        self.pod('Q', len(encoded))
        self.file.write(encoded)

    def matrix(self, matrix):
        self.pod('16f', *(matrix[row][column] for column in range(4) for row in range(4)))

    def close(self):
        self.file.close()


def engine_matrix(matrix):
    return CONVERSION @ matrix @ CONVERSION.inverted()


def bind_matrices(rig):
    return [engine_matrix(bone.matrix_local) for bone in rig.data.bones]


def export_mesh(path, rig, material_references, source):
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    bones = list(rig.data.bones)
    indices_by_name = {bone.name: i for i, bone in enumerate(bones)}
    globals_ = bind_matrices(rig)
    vertices, indices, submeshes = [], [], []
    objects = [obj for obj in bpy.data.objects if obj.type == 'MESH' and 'joint' in obj]
    for material_index, reference in enumerate(material_references):
        start = len(indices)
        points = []
        for obj in objects:
            if obj['material_index'] != material_index:
                continue
            mesh = obj.data
            mesh.calc_loop_triangles()
            joint = indices_by_name[obj['joint']]
            normal_matrix = (CONVERSION @ obj.matrix_world).to_3x3().inverted().transposed()
            for triangle in mesh.loop_triangles:
                for loop_id in triangle.loops:
                    loop = mesh.loops[loop_id]
                    position = CONVERSION @ obj.matrix_world @ mesh.vertices[loop.vertex_index].co
                    normal = (normal_matrix @ triangle.normal).normalized()
                    tangent = normal.cross(Vector((0, 1, 0)))
                    if tangent.length < .001:
                        tangent = normal.cross(Vector((1, 0, 0)))
                    tangent.normalize()
                    uv = mesh.uv_layers.active.data[loop_id].uv if mesh.uv_layers.active else (0, 0)
                    vertices.append((*position, *normal, *uv, *tangent, 1., 0., 0., joint, 0, 0, 0, 1., 0., 0., 0.))
                    points.append(position)
                    indices.append(len(vertices) - 1)
        if points:
            centre = sum(points, Vector()) / len(points)
            radius = max((point - centre).length for point in points)
            submeshes.append((start, len(indices) - start, material_index, centre, radius))
    out = Writer(path)
    out.pod('IIQ', 0x4d47504c, 5, len(vertices))
    for vertex in vertices:
        out.pod('14f4i4f', *vertex)
    out.pod('Q', len(indices))
    out.pod(f'{len(indices)}I', *indices)
    out.pod('Q', len(submeshes))
    for start, count, material, centre, radius in submeshes:
        out.pod('IIIi4f', start, count, material, -1, *centre, radius)
        out.string(f'Material_{material}')
        out.pod('Q', 0)
    out.pod('BQ', 0, len(bones))
    for index, bone in enumerate(bones):
        parent = indices_by_name[bone.parent.name] if bone.parent else -1
        local = globals_[parent].inverted() @ globals_[index] if parent >= 0 else globals_[index]
        out.string(bone.name)
        out.pod('ii', index, parent)
        out.matrix(local)
        out.matrix(globals_[index].inverted())
        out.matrix(Matrix.Identity(4))
    out.pod('Q', 0)  # humanoid mappings: deliberately a viewmodel rig
    out.pod('Q', len(bones))
    for index, bone in enumerate(bones):
        parent = indices_by_name[bone.parent.name] if bone.parent else -1
        local = globals_[parent].inverted() @ globals_[index] if parent >= 0 else globals_[index]
        out.string(bone.name)
        out.pod('i', parent)
        out.matrix(local)
    out.pod('Q', len(material_references))
    for reference in material_references:
        out.string(reference)
    out.string(source)
    out.string('')
    out.pod('QBBB', 0, 0, 0, 0)
    out.close()
    return {'vertices': len(vertices), 'triangles': len(indices) // 3, 'submeshes': len(submeshes), 'bones': len(bones)}


def export_clip(path, rig, action, duration, events=()):
    rig.animation_data.action = action
    bones = list(rig.data.bones)
    bone_indices = {bone.name: i for i, bone in enumerate(bones)}
    globals_ = bind_matrices(rig)
    steps = round(duration * 24)
    times = [index * duration / steps for index in range(steps + 1)]
    samples = [[] for _ in bones]
    for time in times:
        frame = time * 24
        bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
        for index, bone in enumerate(bones):
            pose = rig.pose.bones[bone.name]
            local = pose.parent.matrix.inverted() @ pose.matrix if pose.parent else pose.matrix
            position, rotation, scale = engine_matrix(local).decompose()
            samples[index].append(((*position, 0.), (rotation.x, rotation.y, rotation.z, rotation.w), (*scale, 0.)))
    out = Writer(path)
    out.pod('II', 0x4347504c, 5)
    out.string(action.name)
    out.pod('fiQ', duration, len(bones) * 3, len(bones) * 3)
    for index, bone in enumerate(bones):
        parent = bone_indices[bone.parent.name] if bone.parent else -1
        bind = globals_[parent].inverted() @ globals_[index] if parent >= 0 else globals_[index]
        for channel in range(3):
            out.pod('iii', index, index, parent)
            out.string(bone.name)
            out.pod('B', 1)
            out.matrix(bind)
            out.pod('B', 1)
            out.matrix(globals_[index])
            out.pod('IIQ', channel, 0, len(times))
            out.pod(f'{len(times)}f', *times)
            out.pod('Q', len(times))
            for sample in samples[index]:
                out.pod('4f', *sample[channel])
    out.pod('Q', len(events))
    for time, name in events:
        out.pod('f', time)
        out.string(name)
        out.string('')
        out.pod('fi', 0., 0)
    out.close()
