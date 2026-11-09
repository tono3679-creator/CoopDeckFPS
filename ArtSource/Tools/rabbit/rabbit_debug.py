"""Debug renders for the rabbit rig: regions painted as vertex colours, and posed close-ups (Workbench, headless)."""
import os

import bpy
import numpy as np
from mathutils import Vector

VIEWS = {"front": Vector((0, -1, 0)), "back": Vector((0, 1, 0)), "right": Vector((-1, 0, 0)),
         "left": Vector((1, 0, 0)), "top": Vector((0, 0.001, 1)), "q": Vector((-0.6, -1, 0.35))}


def paint(body, regions, base=(0.55, 0.55, 0.55)):
    """regions: list of (vertex ids, rgb). Later entries win."""
    me = body.data
    col = np.tile(np.array(base + (1.0,), dtype=np.float32), (len(me.vertices), 1))
    for ids, rgb in regions:
        ids = np.fromiter(ids, dtype=np.int64)
        if len(ids):
            col[ids, :3] = rgb
    name = "Debug"
    if name in me.color_attributes:
        me.color_attributes.remove(me.color_attributes[name])
    attr = me.color_attributes.new(name, "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", col.ravel())
    me.color_attributes.active_color = attr


def _setup(res, colour_type):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.image_settings.file_format = "PNG"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = colour_type
    sh.single_color = (0.75, 0.75, 0.75)
    sh.show_cavity = colour_type == "SINGLE"
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.22, 0.22, 0.25)
    cam = bpy.data.objects.get("DebugCam")
    if cam is None:
        cam = bpy.data.objects.new("DebugCam", bpy.data.cameras.new("DebugCam"))
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.clip_start, cam.data.clip_end = 0.01, 30.0
    sc.camera = cam
    return cam


def render_sheet(out_png, shots, res=420, colour_type="VERTEX"):
    """shots: list of (centre Vector, size m, view name). One row; saved as PNG."""
    cam = _setup(res, colour_type)
    tmp = out_png + ".tmp.png"
    tiles = []
    for centre, size, view in shots:
        d = VIEWS[view] if isinstance(view, str) else view
        cam.data.ortho_scale = size
        cam.location = centre + d.normalized() * 6.0
        up = "Y" if abs(d.normalized().z) < 0.9 else "X"
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", up).to_euler()
        bpy.context.scene.render.filepath = tmp
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(tmp, check_existing=False)
        tiles.append(np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4))
        bpy.data.images.remove(img)
    sheet = np.concatenate(tiles, axis=1)
    out = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = out_png
    out.file_format = "PNG"
    out.save()
    os.remove(tmp)
    return out_png


def stack(pngs, out_png):
    """Stacks sheets of the same width vertically (first on top)."""
    arrs = []
    for p in pngs:
        img = bpy.data.images.load(p, check_existing=False)
        arrs.append(np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4))
        bpy.data.images.remove(img)
    width = max(a.shape[1] for a in arrs)
    arrs = [np.pad(a, ((0, 0), (0, width - a.shape[1]), (0, 0))) for a in arrs]
    sheet = np.concatenate(arrs[::-1], axis=0)
    out = bpy.data.images.new("stack", sheet.shape[1], sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = out_png
    out.file_format = "PNG"
    out.save()
    for p in pngs:
        os.remove(p)
