"""Renders a weapon the way the first-person view model shows it, to judge jaggies and texture detail (Blender).

    blender -b --factory-startup -P fp_view_check.py -- --model a.glb [--model b.glb] --out <dir> \
        [--grip-offset 0.15,0.20,0.39] [--fov 70] [--res 1920x1080] [--crop x0,y0,x1,y1 ...]

The weapon is in prepared-prop coordinates (Blender: front -Y, up +Z, origin at the pistol grip). The eye sits so the
grip is right/down/ahead of it by --grip-offset metres (the game's view-model layout), looking straight ahead with a
vertical field of view of --fov degrees. Each model gives <out>/fp_<name>.png at the game resolution (Workbench,
texture colour, 8x anti-aliasing like MSAA) and, per --crop, a 1:1 cut of that region stacked under the full frame
(<out>/fp_crops.png, one column per model) so texture texels and edge steps are seen at their real size.
"""
import argparse
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--model", action="append", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--grip-offset", default="0.15,0.20,0.39")
    p.add_argument("--fov", type=float, default=70.0)
    p.add_argument("--res", default="1920x1080")
    p.add_argument("--crop", action="append", default=[])
    p.add_argument("--aa", default="8")
    return p.parse_args(argv)


def load_px(path):
    img = bpy.data.images.load(path, check_existing=False)
    a = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    bpy.data.images.remove(img)
    return a[::-1]  # top row first


def save_px(a, path):
    img = bpy.data.images.new("o", a.shape[1], a.shape[0], alpha=True)
    img.pixels = a[::-1].ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    a = parse()
    os.makedirs(a.out, exist_ok=True)
    w, h = (int(v) for v in a.res.split("x"))
    right, down, ahead = (float(v) for v in a.grip_offset.split(","))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.display.render_aa = a.aa
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_specular_highlight = False
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.45, 0.5, 0.58)
    cam = bpy.data.objects.new("FPCam", bpy.data.cameras.new("FPCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle = math.radians(a.fov)
    cam.data.clip_start = 0.02
    # character right is -X for a prop facing -Y; the eye is left of, above and behind the grip
    cam.location = Vector((right, ahead, down))
    cam.rotation_euler = (Vector((right, 0.0, down)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    groups = []
    for path in a.model:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        groups.append([o for o in bpy.data.objects if o not in before and o.type == "MESH"])
    columns = []
    for path, objs in zip(a.model, groups):
        for other in groups:
            for o in other:
                o.hide_render = other is not objs
        name = os.path.splitext(os.path.basename(path))[0]
        full = os.path.join(a.out, f"fp_{name}.png")
        sc.render.filepath = full
        bpy.ops.render.render(write_still=True)
        px = load_px(full)
        cuts = []
        for spec in a.crop:
            x0, y0, x1, y1 = (int(v) for v in spec.split(","))
            cuts.append(px[y0:y1, x0:x1])
        if cuts:
            width = max(c.shape[1] for c in cuts)
            cuts = [np.pad(c, ((0, 4), (0, width - c.shape[1]), (0, 0)), constant_values=1.0) for c in cuts]
            columns.append(np.concatenate(cuts, axis=0))
        print(f"FP {name} -> {full}")
    if columns:
        hmax = max(c.shape[0] for c in columns)
        columns = [np.pad(c, ((0, hmax - c.shape[0]), (0, 6), (0, 0)), constant_values=1.0) for c in columns]
        save_px(np.concatenate(columns, axis=1), os.path.join(a.out, "fp_crops.png"))


main()
