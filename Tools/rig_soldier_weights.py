"""Rebind the review soldier in Blender after adjusting its rest bones.
Run in Blender with Soldier_Body_Review and Soldier_Rig present.
This is a first-pass distance skinning aid, not a substitute for weight painting.
"""
import bpy
import numpy as np
import time

obj = bpy.data.objects["Soldier_Body_Review"]
rig = bpy.data.objects["Soldier_Rig"]
a = np.array([v.co[:] for v in obj.data.vertices], dtype=np.float64)
bones = [b for b in rig.data.bones if b.use_deform]
names = [b.name for b in bones]
distance = np.empty((len(a), len(bones)))
for j, bone in enumerate(bones):
    head, tail = np.array(bone.head_local), np.array(bone.tail_local)
    direction = tail - head
    u = np.clip(((a - head) @ direction) / (direction @ direction), .02, .98)
    distance[:, j] = np.linalg.norm(a - (head + u[:, None] * direction), axis=1)
x, y, z = a.T
ax = np.abs(x)
for j, name in enumerate(names):
    mask = np.ones(len(a), dtype=bool)
    side = 1 if name.endswith(".L") or name.startswith("Left") else -1 if name.endswith(".R") or name.startswith("Right") else 0
    if side:
        mask &= x * side > -.015
    if any(word in name for word in ["Arm", "Shoulder"]):
        mask &= (z > .98) & (ax > .155)
    elif name.startswith("Hand"):
        mask &= (z < 1.19) & (z > .90) & (ax > .32) & (y < .02)
    elif any(name.startswith(f) for f in ["Thumb", "Index", "Middle", "Ring", "Little"]):
        mask &= (z < 1.105) & (z > .90) & (ax > .34) & (y < -.12)
    elif any(word in name for word in ["Leg", "Foot", "Toes"]):
        mask &= z < 1.03
        if "UpperLeg" in name:
            mask &= z > .38
        if "LowerLeg" in name:
            mask &= z < .72
        if "Foot" in name or "Toes" in name:
            mask &= z < .25
    elif name == "Head":
        mask &= z > 1.49
    elif name == "Neck":
        mask &= (z > 1.42) & (ax < .16)
    elif name == "Hips":
        mask &= (z > .72) & (z < 1.12) & (ax < .28)
    else:
        mask &= (z > .91) & (ax < .29)
    distance[~mask, j] = 100
weights = 1 / np.power(distance + .025, 4)
indices = np.argpartition(weights, -4, axis=1)[:, -4:]
selected = np.take_along_axis(weights, indices, axis=1)
selected /= selected.sum(axis=1, keepdims=True)

# Classify connected pieces after welding seams. Clothing, face and gloves keep
# smooth weights. Small separate pouches and head-mounted gear move rigidly.
parent = list(range(len(a)))
def find(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i
for edge in obj.data.edges:
    first, second = map(find, edge.vertices)
    if first != second:
        parent[second] = first
components = {}
for i in range(len(a)):
    components.setdefault(find(i), []).append(i)
rigid = 0
for ids in components.values():
    points = a[ids]
    center = points.mean(axis=0)
    extent = points.max(axis=0) - points.min(axis=0)
    if len(ids) < 2000 and max(extent) < .24 and not (abs(center[0]) > .33 and center[2] > .9 and center[1] < .02):
        if center[2] > 1.56:
            chosen = "Head"
        elif abs(center[0]) < .22 and 1.10 < center[2] < 1.42:
            chosen = "Chest"
        elif abs(center[0]) > .17 and .63 < center[2] < .92:
            chosen = "LeftUpperLeg" if center[0] > 0 else "RightUpperLeg"
        else:
            continue
        index = names.index(chosen)
        indices[ids, :] = index
        selected[ids, :] = 0
        selected[ids, 0] = 1
        rigid += len(ids)
obj.vertex_groups.clear()
for bone in bones:
    obj.vertex_groups.new(name=bone.name)
choices = [i.identifier for i in bpy.types.VertexGroup.bl_rna.functions["add"].parameters["type"].enum_items]
replace = next(x for x in choices if x == "REPLACE")
start = time.time()
for vertex in range(len(a)):
    for index, weight in zip(indices[vertex], selected[vertex]):
        if weight > 1e-5:
            obj.vertex_groups[int(index)].add([vertex], float(weight), replace)
obj.parent = rig
modifier = next(m for m in obj.modifiers if m.type == "ARMATURE")
modifier.object = rig
modifier.use_deform_preserve_volume = False
obj["SkinningNotes"] = "Heat binding failed on disconnected equipment. Anatomically restricted distance binding with up to four influences. Small detached gear rigidly bound. Review shoulders, knees and curled fingers."
print("Bound", len(a), "vertices to", len(bones), "bones; rigid gear vertices", rigid, "seconds", round(time.time() - start, 2))
print("Unweighted", sum(not any(g.weight > 1e-6 for g in v.groups) for v in obj.data.vertices))
