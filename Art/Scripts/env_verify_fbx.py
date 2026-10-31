"""Re-import every environment FBX and compare size / pivot with the stats written at export.

Run:  blender -b -P Art/Scripts/env_verify_fbx.py
"""
import glob
import json
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import env_common as E  # noqa: E402

stats = {}
for f in glob.glob(os.path.join(E.STATS_DIR, "stats_*.json")):
    stats.update(json.load(open(f, encoding="utf-8")))
bad = 0
n = 0
for path in sorted(glob.glob(os.path.join(E.EXPORT_ROOT, "*", "*.fbx"))):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    name = os.path.splitext(os.path.basename(path))[0]
    n += 1
    if len(objs) != 1:
        print("FAIL objects", name, len(objs))
        bad += 1
        continue
    o = objs[0]
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    size = [max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)]
    zmin = min(p.z for p in pts)
    ref = stats.get(name)
    ok = ref and all(abs(a - b) < 0.05 + 0.01 * b for a, b in zip(size, ref["size_m"])) and zmin < 0.5 and \
        o.location.length < 1e-3 and len(o.data.polygons) == ref["tris"]
    if not ok:
        bad += 1
        print("FAIL", name, [round(s, 2) for s in size], ref, round(zmin, 2), tuple(o.location), len(o.data.polygons))
print(f"VERIFY {n} fbx, {bad} failed")
