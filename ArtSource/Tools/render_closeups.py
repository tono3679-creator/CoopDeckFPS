"""Close-up renders of a character's hands, head and other parts (run inside Blender, headless).

    blender -b --factory-startup -P render_closeups.py -- --model a.glb [--model b.glb] --out <dir> \
        [--region hands] [--region head] [--region name=x,y,z,size] [--res 640]

Each model is rendered on its own row; each region gives one sheet <out>/closeup_<region>.png with two rows
per model: the texture, then plain grey with cavity shading (shape only, for fingers and seams).
Coordinates are Blender's after the glTF import (the character faces -Y, its right hand is at -X).
Models are fitted to the first model's bounding box so the same region frames each of them.
"""
import argparse
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

FRONT, BACK = Vector((0, -1, 0)), Vector((0, 1, 0))
UP, DOWN = Vector((0, 0, 1)), Vector((0, 0, -1))
RIGHT_SIDE, LEFT_SIDE = Vector((-1, 0, 0)), Vector((1, 0, 0))   # the character's right / left
Q_FRONT_RIGHT = Vector((-0.7, -1, 0.3))


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--model", action="append", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--region", action="append", default=[])
    p.add_argument("--res", type=int, default=640)
    p.add_argument("--grey-only", action="store_true", help="skip the textured row (shape-only models)")
    return p.parse_args(argv)


def import_model(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in [o for o in new if o.type == "MESH" and o.name.startswith("Icosphere")]:
        bpy.data.objects.remove(o, do_unlink=True)
        new.remove(o)
    return [o for o in new if o.type == "MESH"]


def world_verts(objs):
    out = []
    for o in objs:
        me = o.data
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        m = np.array(o.matrix_world, dtype=np.float32)
        out.append(co @ m[:3, :3].T + m[:3, 3])
    return np.concatenate(out)


def fit_to(objs, ref_min, ref_max):
    v = world_verts(objs)
    lo, hi = v.min(axis=0), v.max(axis=0)
    s = (ref_max[2] - ref_min[2]) / (hi[2] - lo[2])
    for o in objs:
        if o.parent is None:
            o.scale = o.scale * s
    bpy.context.view_layer.update()
    v = world_verts(objs)
    lo, hi = v.min(axis=0), v.max(axis=0)
    shift = (ref_min + ref_max) / 2 - (lo + hi) / 2
    for o in objs:
        if o.parent is None:
            o.location = o.location + Vector(shift.tolist())
    bpy.context.view_layer.update()


def regions_for(verts, specs):
    lo, hi = verts.min(axis=0), verts.max(axis=0)
    height = hi[2] - lo[2]
    out = []
    for spec in specs:
        if spec == "hands":
            for name, sel, outward in (("hand_R", verts[:, 0] < lo[0] + 0.09 * height, RIGHT_SIDE),
                                       ("hand_L", verts[:, 0] > hi[0] - 0.09 * height, LEFT_SIDE)):
                pts = verts[sel]
                c = (pts.min(axis=0) + pts.max(axis=0)) / 2
                size = float((pts.max(axis=0) - pts.min(axis=0)).max()) * 1.35
                out.append((name, Vector(c.tolist()), size, [FRONT, UP, DOWN, BACK, outward]))
        elif spec == "head":
            sel = verts[:, 2] > hi[2] - 0.42 * height
            pts = verts[sel & (np.abs(verts[:, 0]) < 0.25 * height)]
            c = (pts.min(axis=0) + pts.max(axis=0)) / 2
            size = float((pts.max(axis=0) - pts.min(axis=0))[[0, 2]].max()) * 1.1
            out.append(("head", Vector(c.tolist()), size, [FRONT, RIGHT_SIDE, BACK, LEFT_SIDE, Q_FRONT_RIGHT]))
        else:
            name, vals = spec.split("=")
            x, y, z, size = (float(t) for t in vals.split(","))
            out.append((name, Vector((x, y, z)), size, [FRONT, RIGHT_SIDE, BACK, LEFT_SIDE, UP]))
    return out


def setup(res):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.show_object_outline = False
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.22, 0.22, 0.25)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def shot(cam, target, direction, size, path, textured):
    sh = bpy.context.scene.display.shading
    sh.color_type = "TEXTURE" if textured else "SINGLE"
    sh.single_color = (0.75, 0.75, 0.75)
    sh.show_cavity = not textured
    sh.cavity_type = "BOTH"
    cam.data.ortho_scale = size
    cam.data.clip_start = 0.01
    cam.data.clip_end = 20.0
    cam.location = target + direction.normalized() * 5.0
    up = "Y" if abs(direction.z) < 0.9 else "X"
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", up).to_euler()
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path, check_existing=False)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    bpy.data.images.remove(img)
    return px


def save(sheet, path):
    img = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    img.pixels = sheet.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def main():
    a = parse()
    os.makedirs(a.out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    groups = [import_model(p) for p in a.model]
    ref = world_verts(groups[0])
    ref_min, ref_max = ref.min(axis=0), ref.max(axis=0)
    for g in groups[1:]:
        fit_to(g, ref_min, ref_max)
    regions = regions_for(ref, a.region or ["hands", "head"])
    cam = setup(a.res)
    tmp = os.path.join(a.out, "_tmp.png")
    for name, centre, size, dirs in regions:
        rows = []
        for i, g in enumerate(groups):
            for other in groups:
                for o in other:
                    o.hide_render = other is not g
            for textured in ((False,) if a.grey_only else (True, False)):
                rows.append(np.concatenate([shot(cam, centre, d, size, tmp, textured) for d in dirs], axis=1))
        save(np.concatenate(rows[::-1], axis=0), os.path.join(a.out, f"closeup_{name}.png"))  # pixel rows are bottom-up
        print(f"CLOSEUP {name} centre={tuple(round(c, 3) for c in centre)} size={size:.3f}")
    os.remove(tmp)


main()
