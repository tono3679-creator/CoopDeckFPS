"""How smooth a body decimated from Meshy's high-res mesh is at several triangle counts (run inside Blender, headless).

    blender -b --factory-startup -P decimation_study.py -- --model <prepared body .glb> --highres <highres .glb> \
        --faces 40000 --faces 80000 ... --out <dir>

For each count the high-res mesh (fitted to the prepared body, as refine_character.py rebuild does) is decimated by
quadric collapse and measured:
  - faceting: the angle between the normals of neighbouring triangles (median / 90% / 99%, and the share of edges over
    20 degrees). With smooth shading this is what shows as polygon edges on silhouettes and in the light-dark border.
  - detail kept: distance from high-res sample points to the decimated surface (mean / 99% / max, millimetres), per
    body part (head and ears, hands, the rest).
Writes <out>/study.json and <out>/body_<faces>.glb (shape only) for render_closeups.py.
"""
import argparse
import json
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import refine_character as rc  # noqa: E402  (shared helpers; nothing runs on import)


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--model", required=True)
    p.add_argument("--highres", required=True)
    p.add_argument("--faces", type=int, action="append", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--samples", type=int, default=120000)
    return p.parse_args(argv)


def decimate_copy(high, faces):
    new = high.copy()
    new.data = high.data.copy()
    new.name = new.data.name = f"Body_{faces}"
    bpy.context.scene.collection.objects.link(new)
    dec = new.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, faces / len(new.data.polygons))
    dec.use_collapse_triangulate = True
    rc.select_only(new)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.mesh.delete_loose()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth()
    return new


def faceting(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    ang = np.array([e.calc_face_angle(0.0) for e in bm.edges if len(e.link_faces) == 2], dtype=np.float64)
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    bm.free()
    ang = np.degrees(ang)
    return {"median_deg": round(float(np.median(ang)), 2), "p90_deg": round(float(np.percentile(ang, 90)), 2),
            "p99_deg": round(float(np.percentile(ang, 99)), 2), "edges_over_20deg": round(float(np.mean(ang > 20)), 4),
            "open_edges": open_edges}


def world_coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    m = np.array(obj.matrix_world)
    return co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]


def parts(points, lo, hi):
    """Head and ears / hands / rest, by position (T pose; the character faces -Y, height along +Z)."""
    h = hi[2] - lo[2]
    head = points[:, 2] > lo[2] + 0.72 * h
    hands = np.abs(points[:, 0]) > 0.5 * (hi[0] - lo[0]) / 2 + 0.18 * h
    return {"head_ears": head, "hands": hands & ~head, "rest": ~head & ~hands}


def detail(obj, samples, labels):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    d = np.array([(tree.find_nearest(p)[3] or 0.0) for p in samples]) * 1000.0
    bm.free()
    out = {}
    for name, sel in labels.items():
        v = d[sel]
        out[name] = {"mean_mm": round(float(v.mean()), 3), "p99_mm": round(float(np.percentile(v, 99)), 3),
                     "max_mm": round(float(v.max()), 2)}
    return out


def export_shape(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    obj.data.materials.clear()
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_animations=False,
                              export_skins=False, export_yup=True)


def main():
    a = parse()
    os.makedirs(a.out, exist_ok=True)
    arm, body = rc.import_body(a.model)
    high = rc.import_highres_fitted(a.highres, body)
    hco = world_coords(high)
    lo, hi = hco.min(0), hco.max(0)
    rng = np.random.default_rng(1)
    samples = hco[rng.choice(len(hco), size=min(a.samples, len(hco)), replace=False)]
    labels = parts(samples, lo, hi)
    report = {"highres": {"triangles": len(high.data.polygons), "vertices": len(high.data.vertices),
                          "faceting": faceting(high)},
              "samples": {k: int(v.sum()) for k, v in labels.items()}}
    export_shape(high, os.path.join(a.out, "body_highres.glb"))
    body.hide_set(True)
    for faces in sorted(a.faces):
        new = decimate_copy(high, faces)
        entry = {"triangles": len(new.data.polygons), "vertices": len(new.data.vertices),
                 "faceting": faceting(new), "detail": detail(new, samples, labels)}
        report[str(faces)] = entry
        print(f"STUDY {faces}: {json.dumps(entry)}")
        export_shape(new, os.path.join(a.out, f"body_{faces}.glb"))
        bpy.data.objects.remove(new, do_unlink=True)
    with open(os.path.join(a.out, "study.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    print("STUDY highres:", json.dumps(report["highres"]))


main()
