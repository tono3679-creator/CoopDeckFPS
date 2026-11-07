"""Debug render of the region segmentation (tail magenta, ears cyan/blue, fingers yellow, thumb green).

blender -b --factory-startup -P debug_segment.py -- <out_dir>
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from fox_common import fresh_scene, import_fox, script_args  # noqa: E402
from fox_segment import (dominant_groups, find_ears, find_hand_parts, find_tail, neighbours,  # noqa: E402
                         tail_centerline, vertex_colours)

TAIL_GUESS = [(0, 0.10, 0.74), (0, 0.25, 0.66), (0, 0.40, 0.52), (0, 0.50, 0.35), (0, 0.52, 0.18)]


def main():
    out = script_args()[0]
    fresh_scene()
    arm, body = import_fox()
    col = vertex_colours(body)
    adj = neighbours(body)
    dom = dominant_groups(body)
    tail = find_tail(body, col, adj, dom)
    ear_l = find_ears(body, dom, +1)
    ear_r = find_ears(body, dom, -1)
    f_l, t_l, _ = find_hand_parts(body, arm, dom, "Left")
    f_r, t_r, _ = find_hand_parts(body, arm, dom, "Right")
    print("COUNTS tail", len(tail), "ears", len(ear_l), len(ear_r), "fingers", len(f_l), len(f_r), "thumb", len(t_l), len(t_r))
    cl = tail_centerline(body, tail, TAIL_GUESS)
    print("CENTER", [tuple(round(c, 3) for c in p) for p in cl])
    me = body.data
    for name, s in (("earL", ear_l), ("earR", ear_r)):
        ps = [me.vertices[i].co for i in s]
        print("EAR", name, "z", round(min(p.z for p in ps), 3), round(max(p.z for p in ps), 3),
              "x", round(min(p.x for p in ps), 3), round(max(p.x for p in ps), 3),
              "y", round(min(p.y for p in ps), 3), round(max(p.y for p in ps), 3))
        top = max(ps, key=lambda p: p.z)
        print("EARTIP", name, tuple(round(c, 3) for c in top))
    for name, s in (("thumbR", t_r), ("fingR", f_r), ("thumbL", t_l), ("fingL", f_l)):
        vals = list(s.values())
        if vals:
            print("HAND", name, len(vals), "s", round(min(v[0] for v in vals), 3), round(max(v[0] for v in vals), 3),
                  "u", round(min(v[1] for v in vals), 3), round(max(v[1] for v in vals), 3),
                  "w", round(min(v[2] for v in vals), 3), round(max(v[2] for v in vals), 3))

    attr = me.color_attributes.new("dbg", "FLOAT_COLOR", "POINT")
    for v in me.vertices:
        i = v.index
        c = (0.55, 0.55, 0.55, 1)
        if i in tail:
            c = (1, 0, 1, 1)
        elif i in ear_l:
            c = (0, 1, 1, 1)
        elif i in ear_r:
            c = (0, 0.4, 1, 1)
        elif i in f_l or i in f_r:
            c = (1, 1, 0, 1)
        elif i in t_l or i in t_r:
            c = (0, 1, 0, 1)
        attr.data[i].color = c
    me.color_attributes.active_color = attr

    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "VERTEX"
    cd = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    cd.type = "ORTHO"

    def shot(name, loc, target, scale, rx=700, ry=700):
        sc.render.resolution_x = rx
        sc.render.resolution_y = ry
        cam.location = Vector(loc)
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        cd.ortho_scale = scale
        sc.render.filepath = os.path.join(out, name + ".png")
        bpy.ops.render.render(write_still=True)

    shot("seg_side", (3, 0.15, 0.8), (0, 0.15, 0.8), 1.9, 700, 900)
    shot("seg_back", (0, 3, 0.8), (0, 0, 0.8), 1.9, 700, 900)
    shot("seg_head", (0.6, 1.6, 1.6), (0, 0, 1.40), 0.8)
    shot("seg_handR", (-0.65, -0.05, 2), (-0.65, -0.05, 1.0), 0.35)
    shot("seg_handRb", (-0.65, -0.6, 0.7), (-0.65, -0.05, 1.0), 0.35)


main()
