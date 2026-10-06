"""Blender exporter for weighted bot meshes; shares the engine's v5 clip writer."""
from pathlib import Path
import bpy
from mathutils import Matrix, Vector
from plutoge_blender_export import Writer, CONVERSION, engine_matrix, bind_matrices, export_clip

HUMANOID = ['Hips','Spine','Chest','UpperChest','Neck','Head','LeftShoulder','UpperArm.L','LowerArm.L','Hand.L','RightShoulder','UpperArm.R','LowerArm.R','Hand.R','LeftUpperLeg','LeftLowerLeg','Foot.L','LeftToes','RightUpperLeg','RightLowerLeg','Foot.R','RightToes']

def export_weighted_mesh(path, objects, rig, references, source):
    bones = list(rig.data.bones) if rig else []
    lookup = {b.name:i for i,b in enumerate(bones)}
    globals_ = bind_matrices(rig) if rig else []
    vertices, indices, submeshes = [], [], []
    unique = {}
    for material in range(len(references)):
        start = len(indices)
        points = []
        for obj in objects:
            mesh = obj.data
            mesh.calc_loop_triangles()
            normals = (CONVERSION @ obj.matrix_world).to_3x3().inverted().transposed()
            groups = {g.index:lookup[g.name] for g in obj.vertex_groups if g.name in lookup}
            for tri in mesh.loop_triangles:
                if tri.material_index != material: continue
                for loop_index in tri.loops:
                    loop = mesh.loops[loop_index]
                    vertex = mesh.vertices[loop.vertex_index]
                    p = CONVERSION @ obj.matrix_world @ vertex.co
                    n = (normals @ mesh.corner_normals[loop_index].vector).normalized()
                    tangent = n.cross(Vector((0,1,0)))
                    if tangent.length < .001: tangent = n.cross(Vector((1,0,0)))
                    tangent.normalize()
                    uv = mesh.uv_layers.active.data[loop_index].uv if mesh.uv_layers.active else (0,0)
                    influences = sorted([(groups[g.group],g.weight) for g in vertex.groups if g.group in groups and g.weight>0], key=lambda item:-item[1])[:4]
                    if rig and not influences: raise ValueError(f'Unweighted vertex {vertex.index}')
                    total = sum(w for _,w in influences)
                    joints = [j for j,_ in influences] + [0]*(4-len(influences))
                    weights = [w/total for _,w in influences] + [0.]*(4-len(influences))
                    packed = (*p,*n,*uv,*tangent,1.,0.,0.,*joints,*weights)
                    index = unique.get(packed)
                    if index is None:
                        index = len(vertices); unique[packed] = index; vertices.append(packed)
                    indices.append(index); points.append(p)
        if points:
            centre = sum(points,Vector())/len(points)
            submeshes.append((start,len(indices)-start,material,centre,max((p-centre).length for p in points)))
    out = Writer(Path(path))
    out.pod('IIQ',0x4d47504c,5,len(vertices))
    for v in vertices: out.pod('14f4i4f',*v)
    out.pod('Q',len(indices)); out.pod(f'{len(indices)}I',*indices)
    out.pod('Q',len(submeshes))
    for start,count,material,centre,radius in submeshes:
        out.pod('IIIi4f',start,count,material,-1,*centre,radius); out.string(f'Material_{material}'); out.pod('Q',0)
    out.pod('BQ',0,len(bones))
    for i,b in enumerate(bones):
        parent = lookup[b.parent.name] if b.parent else -1
        local = globals_[parent].inverted()@globals_[i] if parent>=0 else globals_[i]
        out.string(b.name); out.pod('ii',i,parent); out.matrix(local); out.matrix(globals_[i].inverted()); out.matrix(Matrix.Identity(4))
    mappings = [(i,lookup[n]) for i,n in enumerate(HUMANOID) if n in lookup]
    out.pod('Q',len(mappings))
    for humanoid,joint in mappings:
        out.pod('B',humanoid); out.string(bones[joint].name); out.pod('i3fBf',joint,0.,0.,0.,int(humanoid==0),1.)
    out.pod('Q',len(bones))
    for i,b in enumerate(bones):
        parent = lookup[b.parent.name] if b.parent else -1
        out.string(b.name); out.pod('i',parent); out.matrix(globals_[parent].inverted()@globals_[i] if parent>=0 else globals_[i])
    out.pod('Q',len(references))
    for ref in references: out.string(ref)
    out.string(source); out.string(''); out.pod('QBBB',0,0,0,0); out.close()
    return dict(vertices=len(vertices),triangles=len(indices)//3,joints=len(bones),submeshes=len(submeshes))
