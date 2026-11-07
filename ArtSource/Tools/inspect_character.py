"""Inspect a rigged character for use in Unity (run inside Blender, headless).

    blender -b --factory-startup -P inspect_character.py -- \
        --model rigged.glb [--fbx rigged.fbx] [--anim walking.glb ...] --out <dir>

Writes <out>/report.json (mesh, skin, armature, UV, texture and scale checks)
and <out>/views.png (front / right / back / left / 3-quarter renders), plus
<out>/anim_<name>.png (a few poses from each animation file) for deformation checks.
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--model", required=True)
    p.add_argument("--fbx")
    p.add_argument("--anim", action="append", default=[])
    p.add_argument("--out", required=True)
    return p.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_any(path):
    before = set(bpy.data.objects)
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        raise ValueError(f"unsupported: {path}")
    new = [o for o in bpy.data.objects if o not in before]
    # Blender's glTF importer adds an "Icosphere" as the bones' display shape; it is not in the file.
    shapes = {pb.custom_shape for o in new if o.type == "ARMATURE" for pb in o.pose.bones if pb.custom_shape}
    for o in shapes:
        o.hide_render = True
    return [o for o in new if o not in shapes]


def world_bounds(objs):
    mins = Vector((math.inf,) * 3)
    maxs = Vector((-math.inf,) * 3)
    deps = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(deps)
        me = ev.to_mesh()
        mw = o.matrix_world
        for v in me.vertices:
            w = mw @ v.co
            mins = Vector(map(min, mins, w))
            maxs = Vector(map(max, maxs, w))
        ev.to_mesh_clear()
    return mins, maxs


def mesh_report(o):
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    non_manifold = sum(1 for e in bm.edges if not e.is_manifold)
    boundary = sum(1 for e in bm.edges if e.is_boundary)
    loose_verts = sum(1 for v in bm.verts if not v.link_edges)
    degenerate = sum(1 for f in bm.faces if f.calc_area() < 1e-10)
    # Connected parts (islands of geometry).
    seen = set()
    parts = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]
        seen.add(v.index)
        count = 0
        while stack:
            cur = stack.pop()
            count += 1
            for e in cur.link_edges:
                nb = e.other_vert(cur)
                if nb.index not in seen:
                    seen.add(nb.index)
                    stack.append(nb)
        parts.append(count)
    # Vertices closer than 0.1 mm (would weld on "merge by distance").
    from mathutils.kdtree import KDTree
    kd = KDTree(len(bm.verts))
    for v in bm.verts:
        kd.insert(v.co, v.index)
    kd.balance()
    near_dupes = 0
    for v in bm.verts:
        if len(kd.find_range(v.co, 0.0001)) > 1:
            near_dupes += 1
    bm.free()

    # Skin weights.
    group_names = {g.index: g.name for g in o.vertex_groups}
    unweighted = 0
    max_infl = 0
    over4 = 0
    for v in me.vertices:
        ws = [g.weight for g in v.groups if g.weight > 1e-4]
        if not ws:
            unweighted += 1
        max_infl = max(max_infl, len(ws))
        if len(ws) > 4:
            over4 += 1

    uv_layers = [uv.name for uv in me.uv_layers]
    uv_out_of_range = 0
    if me.uv_layers:
        uvs = np.empty(len(me.loops) * 2, dtype=np.float32)
        me.uv_layers.active.data.foreach_get("uv", uvs)
        uvs = uvs.reshape(-1, 2)
        uv_out_of_range = int(np.sum((uvs < -0.001) | (uvs > 1.001)) // 1)

    mats = []
    for slot in o.material_slots:
        m = slot.material
        if not m:
            continue
        info = {"name": m.name, "images": []}
        if m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    img = n.image
                    links = [l.to_socket.name for l in n.outputs["Color"].links]
                    info["images"].append({
                        "name": img.name, "size": list(img.size),
                        "colorspace": img.colorspace_settings.name,
                        "alpha_mode": img.alpha_mode, "feeds": links,
                    })
            bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf:
                info["metallic"] = round(bsdf.inputs["Metallic"].default_value, 3)
                info["roughness"] = round(bsdf.inputs["Roughness"].default_value, 3)
        info["blend_method"] = getattr(m, "surface_render_method", None)
        mats.append(info)

    return {
        "name": o.name,
        "vertices": len(me.vertices),
        "triangles": tris,
        "polygons": len(me.polygons),
        "non_manifold_edges": non_manifold,
        "boundary_edges": boundary,
        "loose_vertices": loose_verts,
        "degenerate_faces": degenerate,
        "connected_parts": len(parts),
        "largest_parts": sorted(parts, reverse=True)[:8],
        "near_duplicate_vertices": near_dupes,
        "vertex_groups": len(group_names),
        "unweighted_vertices": unweighted,
        "max_influences": max_infl,
        "vertices_over_4_influences": over4,
        "uv_layers": uv_layers,
        "uv_coords_outside_0_1": uv_out_of_range,
        "materials": mats,
        "object_scale": [round(s, 4) for s in o.scale],
        "object_rotation_deg": [round(math.degrees(r), 2) for r in o.rotation_euler],
        "parent": o.parent.name if o.parent else None,
    }


def armature_report(a):
    bones = a.data.bones
    roots = [b.name for b in bones if b.parent is None]

    def depth(b):
        d = 0
        while b.parent:
            b = b.parent
            d += 1
        return d

    mw = a.matrix_world
    head_pos = {b.name: [round(x, 3) for x in (mw @ b.head_local)] for b in bones}
    return {
        "name": a.name,
        "bone_count": len(bones),
        "roots": roots,
        "max_depth": max((depth(b) for b in bones), default=0),
        "bones": [{"name": b.name, "parent": b.parent.name if b.parent else None,
                   "head_world": head_pos[b.name]} for b in bones],
        "object_scale": [round(s, 4) for s in a.scale],
        "object_rotation_deg": [round(math.degrees(r), 2) for r in a.rotation_euler],
    }


def scene_summary(objs):
    meshes = [o for o in objs if o.type == "MESH"]
    arms = [o for o in objs if o.type == "ARMATURE"]
    mins, maxs = world_bounds(meshes)
    size = maxs - mins
    return {
        "objects": [{"name": o.name, "type": o.type} for o in objs],
        "bounds_min": [round(x, 4) for x in mins],
        "bounds_max": [round(x, 4) for x in maxs],
        "size_xyz_m": [round(x, 4) for x in size],
        "height_m": round(size.z, 4),
        "feet_z": round(mins.z, 4),
        "meshes": [mesh_report(o) for o in meshes],
        "armatures": [armature_report(a) for a in arms],
        "actions": [{"name": act.name, "frame_range": [round(f, 2) for f in act.frame_range]}
                    for act in bpy.data.actions],
    }


def setup_render(res_x, res_y):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x = res_x
    sc.render.resolution_y = res_y
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = "PNG"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_object_outline = True
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.22, 0.22, 0.25)
    sc.world = sc.world or bpy.data.worlds.new("World")


def make_camera():
    cam_data = bpy.data.cameras.new("InspectCam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("InspectCam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam


def aim(cam, target, direction, dist):
    cam.location = target + direction.normalized() * dist
    rot = (target - cam.location).to_track_quat("-Z", "Y")
    cam.rotation_euler = rot.to_euler()


def render_views(objs, out_png, views, res=(512, 768)):
    meshes = [o for o in objs if o.type == "MESH"]
    mins, maxs = world_bounds(meshes)
    center = (mins + maxs) / 2
    height = (maxs - mins).z
    width = max((maxs - mins).x, (maxs - mins).y)
    setup_render(*res)
    cam = make_camera()
    cam.data.ortho_scale = max(height, width * res[1] / res[0]) * 1.08
    tiles = []
    tmp = out_png + ".tmp.png"
    for direction in views:
        aim(cam, center, direction, 10.0)
        bpy.context.scene.render.filepath = tmp
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(tmp, check_existing=False)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
        tiles.append(px)
        bpy.data.images.remove(img)
    sheet = np.concatenate(tiles, axis=1)
    out = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = out_png
    out.file_format = "PNG"
    out.save()
    os.remove(tmp)
    bpy.data.objects.remove(cam)


FRONT = Vector((0, -1, 0))   # glTF +Z forward becomes Blender -Y
RIGHT = Vector((1, 0, 0))
BACK = Vector((0, 1, 0))
LEFT = Vector((-1, 0, 0))
THREE_Q = Vector((0.7, -1, 0.35))


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)
    report = {}

    reset_scene()
    objs = import_any(args.model)
    report["model"] = {"file": os.path.basename(args.model), **scene_summary(objs)}
    render_views(objs, os.path.join(args.out, "views.png"), [FRONT, RIGHT, BACK, LEFT, THREE_Q])

    if args.fbx:
        reset_scene()
        objs = import_any(args.fbx)
        s = scene_summary(objs)
        report["fbx"] = {"file": os.path.basename(args.fbx),
                         "height_m": s["height_m"], "size_xyz_m": s["size_xyz_m"],
                         "objects": s["objects"],
                         "armatures": [{k: a[k] for k in ("name", "bone_count", "object_scale", "object_rotation_deg")}
                                       for a in s["armatures"]],
                         "meshes": [{k: m[k] for k in ("name", "triangles", "object_scale", "materials")}
                                    for m in s["meshes"]],
                         "actions": s["actions"]}

    report["animations"] = []
    for path in args.anim:
        reset_scene()
        objs = import_any(path)
        name = os.path.splitext(os.path.basename(path))[0]
        acts = [{"name": a.name, "frame_range": [round(f, 2) for f in a.frame_range]} for a in bpy.data.actions]
        arm = next((o for o in objs if o.type == "ARMATURE"), None)
        start, end = (bpy.data.actions[0].frame_range if bpy.data.actions else (1, 1))
        # Root travel over the clip (in-place or not).
        travel = None
        if arm:
            hips = next((b for b in arm.pose.bones if "hip" in b.name.lower()), arm.pose.bones[0])
            bpy.context.scene.frame_set(int(start))
            p0 = arm.matrix_world @ hips.head
            bpy.context.scene.frame_set(int(end))
            p1 = arm.matrix_world @ hips.head
            travel = [round(x, 3) for x in (p1 - p0)]
        report["animations"].append({"file": os.path.basename(path), "actions": acts,
                                     "hips_travel_m": travel,
                                     "fps": bpy.context.scene.render.fps})
        # Render 3 poses across the clip, side and front.
        tiles_path = os.path.join(args.out, f"anim_{name}.png")
        frames = [start + (end - start) * t for t in (0.0, 0.25, 0.5)]
        poses = []
        for f in frames:
            bpy.context.scene.frame_set(int(round(f)))
            tmp = tiles_path + f".{int(f)}.png"
            render_views(objs, tmp, [FRONT, RIGHT], res=(384, 576))
            poses.append(tmp)
        imgs = []
        for pth in poses:
            im = bpy.data.images.load(pth, check_existing=False)
            imgs.append(np.array(im.pixels[:], dtype=np.float32).reshape(im.size[1], im.size[0], 4))
            bpy.data.images.remove(im)
            os.remove(pth)
        sheet = np.concatenate(imgs, axis=1)
        out = bpy.data.images.new("animsheet", sheet.shape[1], sheet.shape[0], alpha=True)
        out.pixels = sheet.ravel()
        out.filepath_raw = tiles_path
        out.file_format = "PNG"
        out.save()

    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print("INSPECT_DONE", os.path.join(args.out, "report.json"))


if __name__ == "__main__":
    main()
