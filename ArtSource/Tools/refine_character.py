"""Smoother shading and an evenly detailed texture for a prepared character body (run inside Blender, headless).

Stage after prepare_for_unity.py; reads its GLB and writes <name>_HQ.glb (same rig, mesh and skin weights).

  1. dilate: island colours are pushed into the empty atlas space so the upscaler sees no hard island edges
       blender -b --factory-startup -P refine_character.py -- dilate --model FoxGunner.glb --out atlas_dilated.png
  2. upscale the dilated atlas 4x with Real-ESRGAN (realesrgan-ncnn-vulkan, BSD-3, downloaded separately, not in
     the repo), in overlapping tiles so an integrated GPU's memory is enough:
       blender ... -- upscale --model FoxGunner.glb --esrgan <dir with realesrgan-ncnn-vulkan.exe> \
           --in atlas_dilated.png --out atlas_x4.png [--esrgan-model realesrgan-x4plus-anime]
  3. bake:
       blender -b --factory-startup -P refine_character.py -- bake --model FoxGunner.glb --upscaled atlas_x4.png \
           --size 4096 --glb-out FoxGunner_HQ.glb --texture-out T_FoxGunner_BaseColor.png [--check <dir>]
     - UV islands are rescaled to one texel density and repacked: the same islands (no new seams), so every part of
       the body gets the same texture detail instead of Meshy's 3x spread
     - colour is re-baked from the upscaled atlas through the old UVs (exact UV-to-UV transfer, supersampled)
     - normals are kept as they are by default (--normal-smooth 0): on Meshy meshes smoothing folds the shading;
       positions, topology and skin weights are untouched
     - --check renders the old and new body side by side (texture and plain grey) for review
  4. normalmap: tangent-space normal map baked from Meshy's high-resolution mesh (fit by bounding box) onto the
     refined body, so the low mesh shades like the high-res one (crisp goggles, belt, boots, scarf folds) without
     more vertices; Unity must compute MikkTSpace tangents from the same normals:
       blender ... -- normalmap --model FoxGunner_HQ.glb --highres FoxGunner_highres.glb --size 4096 \
           --out T_FoxGunner_Normal.png [--check <dir>]
  5. rebuild (used for Fox Gunner instead of 3-4): a new body decimated from the high-res mesh (--faces, quadric
     collapse), even smart-project UVs, colour baked from the prepared body through its own UVs and the upscaled
     atlas, normal map from the high-res mesh, skin weights transferred from the prepared body; same armature:
       blender ... -- rebuild --model FoxGunner.glb --highres FoxGunner_highres.glb --upscaled atlas_x4.png \
           --faces 40000 --size 4096 --margin 6 --texture-out T_FoxGunner_BaseColor.png \
           --normal-out T_FoxGunner_Normal.png --glb-out FoxGunner_HQ.glb [--check <dir>]
"""
import argparse
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("mode", choices=("dilate", "upscale", "bake", "normalmap", "rebuild"))
    p.add_argument("--highres", help="Meshy's high-resolution GLB (normalmap, rebuild)")
    p.add_argument("--faces", type=int, default=40000, help="rebuild: triangles of the new body")
    p.add_argument("--normal-out", help="rebuild: normal map PNG")
    p.add_argument("--cage", type=float, default=0.004, help="metres the rays start outside the low surface")
    p.add_argument("--ray", type=float, default=0.008, help="metres the rays search past the cage")
    p.add_argument("--max-lean", type=float, default=75.0, help="degrees; steeper baked normals are misses")
    p.add_argument("--model", required=True)
    p.add_argument("--in", dest="inp")
    p.add_argument("--out")
    p.add_argument("--esrgan", help="folder with realesrgan-ncnn-vulkan.exe and models/")
    p.add_argument("--esrgan-model", default="realesrgan-x4plus-anime")
    p.add_argument("--tile", type=int, default=512)
    p.add_argument("--upscaled")
    p.add_argument("--size", type=int, default=4096)
    p.add_argument("--margin", type=int, default=8, help="texels between islands in the new layout")
    p.add_argument("--glb-out")
    p.add_argument("--texture-out")
    p.add_argument("--normal-smooth", type=int, default=0, help="normal smoothing passes (0: keep; on Meshy meshes smoothing folds the shading)")
    p.add_argument("--check")
    args = p.parse_args(argv)
    for k in ("model", "inp", "out", "esrgan", "upscaled", "glb_out", "texture_out", "check", "highres", "normal_out"):
        if getattr(args, k):
            setattr(args, k, os.path.abspath(getattr(args, k)))  # Blender saves relative paths from the drive root
    return args


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def import_body(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    for o in list(bpy.data.objects):
        if o.type == "MESH" and not o.vertex_groups:
            bpy.data.objects.remove(o, do_unlink=True)  # the importer's bone-shape icosphere
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    return arm, body


def base_color_node(body):
    for slot in body.material_slots:
        if slot.material and slot.material.node_tree:
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    return slot.material, node
    raise RuntimeError("no base color texture")


def image_array(img):
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)


def uv_triangles(me, layer):
    me.calc_loop_triangles()
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    layer.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    loops = np.empty(len(me.loop_triangles) * 3, dtype=np.int32)
    me.loop_triangles.foreach_get("loops", loops)
    return uv[loops.reshape(-1, 3)]  # (T, 3, 2)


def rasterize(tris_uv, size):
    """Texels whose centre lies in any UV triangle (slightly conservative)."""
    mask = np.zeros((size, size), dtype=bool)
    p = tris_uv * size - 0.5  # texel-centre coordinates
    for t in p:
        x0, y0 = np.floor(t.min(0)).astype(int)
        x1, y1 = np.ceil(t.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        (ax, ay), (bx, by), (cx, cy) = t
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-12:
            continue
        l1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
        l2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
        l3 = 1 - l1 - l2
        e = -0.6 / max(abs(d) ** 0.5, 1.0)  # about half a texel outside each edge
        inside = (l1 >= e) & (l2 >= e) & (l3 >= e)
        mask[ys[inside], xs[inside]] = True
    return mask


def push_pull_fill(rgb, valid):
    """Fills invalid texels with the average of the nearest valid ones (image pyramid); valid texels are kept."""
    h, w, _ = rgb.shape
    c = rgb * valid[..., None]
    wt = valid.astype(np.float32)
    pyramid = [(c, wt)]
    while c.shape[0] > 1 and c.shape[1] > 1:
        hh, ww = c.shape[0] // 2, c.shape[1] // 2
        c = c[:hh * 2, :ww * 2].reshape(hh, 2, ww, 2, 3).sum((1, 3))
        wt = wt[:hh * 2, :ww * 2].reshape(hh, 2, ww, 2).sum((1, 3))
        pyramid.append((c, wt))
    c, wt = pyramid[-1]
    cur = c / np.maximum(wt, 1e-6)[..., None]
    for c, wt in reversed(pyramid[:-1]):
        up = np.repeat(np.repeat(cur, 2, 0), 2, 1)[:c.shape[0], :c.shape[1]]
        mean = c / np.maximum(wt, 1e-6)[..., None]
        cur = np.where((wt > 0)[..., None], mean, up)
    return cur


def save_rgb(rgb, path, colorspace="sRGB"):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((h, w, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def dilate(args):
    _, body = import_body(args.model)
    _, node = base_color_node(body)
    img = node.image
    size = img.size[0]
    tris = uv_triangles(body.data, body.data.uv_layers.active)
    valid = rasterize(tris, size)
    rgb = image_array(img)[..., :3]  # Blender gives display (sRGB) values for 8-bit sRGB images as linear floats
    filled = push_pull_fill(rgb, valid)
    save_rgb(filled, args.out, img.colorspace_settings.name)
    print(f"  dilated {size}x{size} atlas ({valid.mean():.1%} covered) -> {args.out}")


def upscale(args, scale=4, overlap=32):
    """Runs Real-ESRGAN tile by tile (each tile padded by `overlap` texels of its neighbours) and stitches the
    centres back together, so tile borders do not show."""
    import subprocess
    import tempfile
    img = bpy.data.images.load(args.inp)
    colorspace = img.colorspace_settings.name
    a = image_array(img)[..., :3]
    h, w, _ = a.shape
    out = np.zeros((h * scale, w * scale, 3), dtype=np.float32)
    exe = os.path.join(args.esrgan, "realesrgan-ncnn-vulkan.exe")
    models = os.path.join(args.esrgan, "models")
    with tempfile.TemporaryDirectory() as tmp:
        for y0 in range(0, h, args.tile):
            for x0 in range(0, w, args.tile):
                y1, x1 = min(y0 + args.tile, h), min(x0 + args.tile, w)
                py0, px0 = max(y0 - overlap, 0), max(x0 - overlap, 0)
                py1, px1 = min(y1 + overlap, h), min(x1 + overlap, w)
                src = os.path.join(tmp, "in.png")
                dst = os.path.join(tmp, "out.png")
                save_rgb(a[py0:py1, px0:px1], src, colorspace)
                r = subprocess.run([exe, "-i", src, "-o", dst, "-n", args.esrgan_model, "-s", str(scale),
                                    "-t", "64", "-m", models, "-f", "png"], capture_output=True, text=True)
                if r.returncode != 0 or not os.path.exists(dst):
                    raise RuntimeError(f"Real-ESRGAN failed on tile {x0},{y0}: {r.stderr[-400:]}")
                t = image_array(bpy.data.images.load(dst, check_existing=False))[..., :3]
                cy, cx = (y0 - py0) * scale, (x0 - px0) * scale
                out[y0 * scale:y1 * scale, x0 * scale:x1 * scale] = t[cy:cy + (y1 - y0) * scale, cx:cx + (x1 - x0) * scale]
                os.remove(dst)
                for im in list(bpy.data.images):
                    if im.filepath in (src, dst) or im.name.startswith("out"):
                        bpy.data.images.remove(im)
    save_rgb(out, args.out, colorspace)
    print(f"  upscaled {w}x{h} -> {w * scale}x{h * scale} with {args.esrgan_model} -> {args.out}")


# ---------------------------------------------------------------- normals

def smooth_normals(body, passes):
    """Smooth vertex normals softened over neighbouring vertices (gentler light falloff across the large
    triangles, no small dents); written as custom normals, positions untouched. Normals are not blended between
    separate pieces: a fur tuft lying on the head would bend the head's shading into patches."""
    me = body.data
    select_only(body)
    bpy.ops.object.shade_smooth()  # custom normals only show on smooth faces
    n = len(me.vertices)
    vn = np.empty(n * 3, dtype=np.float32)
    me.vertex_normals.foreach_get("vector", vn)  # smooth (face-weighted) vertex normals of the geometry
    vn = vn.reshape(-1, 3).astype(np.float64)

    edges = np.empty(len(me.edges) * 2, dtype=np.int32)
    me.edges.foreach_get("vertices", edges)
    edges = edges.reshape(-1, 2)

    # Only neighbours facing roughly the same way: thin two-sided pieces (fur tufts, ear rims) join front and back
    # faces at their rims, and averaging across a rim folds the shading.
    same_side = (vn[edges[:, 0]] * vn[edges[:, 1]]).sum(1) > math.cos(math.radians(60))
    e = edges[same_side]
    out = vn.copy()
    for _ in range(passes):
        acc = np.zeros_like(out)
        cnt = np.zeros(n)
        np.add.at(acc, e[:, 0], out[e[:, 1]])
        np.add.at(acc, e[:, 1], out[e[:, 0]])
        np.add.at(cnt, e[:, 0], 1)
        np.add.at(cnt, e[:, 1], 1)
        mean = acc / np.maximum(cnt, 1)[:, None]
        out = out + 0.5 * mean * (cnt > 0)[:, None]
        out /= np.linalg.norm(out, axis=1, keepdims=True)

    me.normals_split_custom_set_from_vertices([tuple(v) for v in out])
    turn = np.degrees(np.arccos(np.clip((out * vn).sum(1), -1, 1)))
    print(f"  normals: {passes} smoothing passes, mean change {turn.mean():.1f} deg, "
          f"99% {np.percentile(turn, 99):.1f} deg")


# ---------------------------------------------------------------- uniform texel density

def texel_density(me, layer, size):
    tris = uv_triangles(me, layer)
    me.calc_loop_triangles()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    vi = np.empty(len(me.loop_triangles) * 3, dtype=np.int32)
    me.loop_triangles.foreach_get("vertices", vi)
    p = co[vi.reshape(-1, 3)]
    a3 = 0.5 * np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=1)
    u = tris
    a2 = 0.5 * np.abs((u[:, 1, 0] - u[:, 0, 0]) * (u[:, 2, 1] - u[:, 0, 1]) - (u[:, 2, 0] - u[:, 0, 0]) * (u[:, 1, 1] - u[:, 0, 1]))
    ok = a3 > 1e-10
    dens = np.sqrt(a2[ok] / a3[ok]) * size
    w = a3[ok]
    order = np.argsort(dens)
    cw = np.cumsum(w[order]) / w.sum()
    return {q: float(dens[order][np.searchsorted(cw, q / 100)]) for q in (5, 50, 95)}, float(a2.sum())


def relayout(body, size, margin):
    me = body.data
    old = me.uv_layers.active
    old.name = "UVMap"
    src = me.uv_layers.new(name="Source", do_init=True)
    me.uv_layers.active = old
    old.active_render = True
    before, _ = texel_density(me, old, 2048)
    select_only(body)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=margin / size, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    after, used = texel_density(me, me.uv_layers["UVMap"], size)
    print(f"  texel density (texels/m, 5/50/95%): before at 2048 {before[5]:.0f}/{before[50]:.0f}/{before[95]:.0f}, "
          f"after at {size} {after[5]:.0f}/{after[50]:.0f}/{after[95]:.0f}; {used:.1%} of the new atlas used")
    return src


def bake_texture(body, source_png, size, out_png, margin):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 16  # supersampling inside each texel
    sc.cycles.use_denoising = False
    sc.render.bake.margin = margin
    sc.render.bake.margin_type = "EXTEND"
    sc.view_settings.view_transform = "Standard"

    src_img = bpy.data.images.load(source_png)
    target = bpy.data.images.new("Baked", size, size, alpha=False, float_buffer=False)
    target.colorspace_settings.name = "sRGB"

    mat = bpy.data.materials.new("BakeTransfer")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "Source"
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = src_img
    tex.interpolation = "Cubic"
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    tgt = nt.nodes.new("ShaderNodeTexImage")
    tgt.image = target
    nt.nodes.active = tgt

    originals = [s.material for s in body.material_slots]
    for s in body.material_slots:
        s.material = mat
    select_only(body)
    bpy.ops.object.bake(type="EMIT", use_clear=True)
    for s, m in zip(body.material_slots, originals):
        s.material = m
    target.filepath_raw = out_png
    target.file_format = "PNG"
    target.save()
    print(f"  baked {size}x{size} base color -> {out_png}")
    return out_png


def colour_check(body, old_png, new_png):
    """Mean colour of the body seen through the old and the new texture (should match)."""
    me = body.data
    res = {}
    for name, png, layer in (("old", old_png, "Source"), ("new", new_png, "UVMap")):
        img = bpy.data.images.load(png, check_existing=False)
        a = image_array(img)[..., :3]
        tris = uv_triangles(me, me.uv_layers[layer])
        c = tris.mean(1)
        x = np.clip((c[:, 0] * a.shape[1]).astype(int), 0, a.shape[1] - 1)
        y = np.clip((c[:, 1] * a.shape[0]).astype(int), 0, a.shape[0] - 1)
        res[name] = a[y, x].mean(0)
    print(f"  mean colour through the UVs: old {np.round(res['old'], 3)} new {np.round(res['new'], 3)}")


def render_check(old_body, body, old_png, new_png, out_dir):
    """Workbench renders (it shows the active UV map's image, not the node tree): the untouched copy with the old
    texture and normals next to the refined body, textured and plain grey."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.show_specular_highlight = True
    sc.render.resolution_x, sc.render.resolution_y = 1000, 1000
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    for o in (old_body, body):
        o.parent = None
        for m in list(o.modifiers):
            o.modifiers.remove(m)
    old_mat = bpy.data.materials.new("CheckOld")
    old_mat.use_nodes = True
    t = old_mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(old_png, check_existing=True)
    old_mat.node_tree.nodes.active = t
    for s in old_body.material_slots:
        s.material = old_mat
    _, node = base_color_node(body)
    node.image = bpy.data.images.load(new_png, check_existing=True)
    body.material_slots[0].material.node_tree.nodes.active = node
    zmax = max((body.matrix_world @ Vector(c)).z for c in body.bound_box)
    views = {
        "head": (Vector((0.3, -0.62, zmax - 0.2)), Vector((0, 0, zmax - 0.3)), 30),
        "torso": (Vector((0.35, -1.0, 1.05)), Vector((0, 0, 0.95)), 30),
        "back": (Vector((-0.6, 1.3, 1.0)), Vector((0, 0.1, 0.8)), 30),
        "tail": (Vector((0.9, 0.9, 0.75)), Vector((0.0, 0.25, 0.6)), 32),
        "face": (Vector((0.1, -0.45, zmax - 0.26)), Vector((0.0, 0.0, zmax - 0.31)), 70),
    }
    for vname, (loc, target, lens) in views.items():
        cam.location = loc
        cam.rotation_euler = (target - loc).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        for mode in ("TEXTURE", "SINGLE"):
            if mode == "SINGLE" and vname not in ("head", "tail"):
                continue
            sc.display.shading.color_type = mode
            sc.display.shading.single_color = (0.8, 0.8, 0.8)
            for label, obj in (("old", old_body), ("new", body)):
                old_body.hide_render = obj is not old_body
                body.hide_render = obj is not body
                suffix = "" if mode == "TEXTURE" else "_grey"
                sc.render.filepath = os.path.join(out_dir, f"check_{vname}{suffix}_{label}.png")
                bpy.ops.render.render(write_still=True)


def bake(args):
    arm, body = import_body(args.model)
    _, node = base_color_node(body)
    old_png = os.path.join(os.path.dirname(os.path.abspath(args.texture_out)), "_old_base_color.png")
    old = node.image.copy()
    old.filepath_raw = old_png
    old.file_format = "PNG"
    old.save()

    old_body = None
    if args.check:  # untouched copy (old UVs and normals) for the comparison renders
        old_body = body.copy()
        old_body.data = body.data.copy()
        bpy.context.scene.collection.objects.link(old_body)
    smooth_normals(body, args.normal_smooth)
    relayout(body, args.size, args.margin)
    bake_texture(body, args.upscaled, args.size, args.texture_out, args.margin)
    colour_check(body, old_png, args.texture_out)

    if args.check:
        render_check(old_body, body, old_png, args.texture_out, args.check)
        return  # check renders strip the rig; run again without --check to export

    os.remove(old_png)
    node.image = bpy.data.images.load(args.texture_out)
    node.image.pack()
    body.data.uv_layers.remove(body.data.uv_layers["Source"])
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.ops.export_scene.gltf(filepath=args.glb_out, export_format="GLB", use_selection=True,
                              export_animations=False, export_skins=True, export_yup=True)
    print(f"  wrote {args.glb_out}")


def import_highres_fitted(path, body):
    """Meshy's high-res mesh scaled and moved onto the body by bounding box (as fox_fp_arms.py does)."""
    def coords(obj):
        me = obj.data
        co = np.empty(len(me.vertices) * 3)
        me.vertices.foreach_get("co", co)
        m = np.array(obj.matrix_world)
        return co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    high = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
    for o in [o for o in bpy.data.objects if o not in before and o is not high]:
        bpy.data.objects.remove(o, do_unlink=True)
    high.parent = None
    hco, bco = coords(high), coords(body)
    hmin, hmax, bmin, bmax = hco.min(0), hco.max(0), bco.min(0), bco.max(0)
    s = (bmax[2] - bmin[2]) / (hmax[2] - hmin[2])
    t = np.array([(bmin[0] + bmax[0]) / 2 - s * (hmin[0] + hmax[0]) / 2,
                  (bmin[1] + bmax[1]) / 2 - s * (hmin[1] + hmax[1]) / 2, bmin[2] - s * hmin[2]])
    from mathutils import Matrix
    high.matrix_world = Matrix.Translation(Vector(t)) @ Matrix.Scale(s, 4) @ high.matrix_world
    select_only(high)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_smooth()
    high.data.materials.clear()
    print(f"  high-res: {len(high.data.polygons)} faces, fitted with scale {s:.4f}")
    return high


def normal_map(args):
    arm, body = import_body(args.model)
    body.parent = None
    for m in list(body.modifiers):
        body.modifiers.remove(m)  # rest pose; no armature deformation during the bake
    high = import_highres_fitted(args.highres, body)
    bake_normal_image(body, high, args, args.out)
    if args.check:
        normal_check(body, high, args.out, args.check)


def bake_normal_image(body, high, args, out_png):
    """Tangent-space normals of `high` baked onto `body`'s active UVs (selected to active)."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 4
    sc.cycles.use_denoising = False
    sc.render.bake.margin = 8
    sc.render.bake.margin_type = "EXTEND"
    target = bpy.data.images.new("BakedNormal", args.size, args.size, alpha=False, float_buffer=False)
    target.colorspace_settings.name = "Non-Color"

    mat = bpy.data.materials.new("BakeNormal")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = target
    mat.node_tree.nodes.active = node
    originals = [s.material for s in body.material_slots]
    for s in body.material_slots:
        s.material = mat

    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", use_selected_to_active=True,
                        cage_extrusion=args.cage, max_ray_distance=args.ray, use_clear=True)
    for s, m in zip(body.material_slots, originals):
        s.material = m

    # Texels whose ray hit the wrong surface (another layer of fur, the inside of a flap) lean almost sideways or
    # backwards and render as black specks: they fall back to the low mesh's own normal, blended in smoothly.
    a = image_array(target)[..., :3] * 2 - 1
    a /= np.maximum(np.linalg.norm(a, axis=2, keepdims=True), 1e-6)
    lean = np.degrees(np.arccos(np.clip(a[..., 2], -1, 1)))
    used = rasterize(uv_triangles(body.data, body.data.uv_layers.active), args.size)
    flat = np.array([0.0, 0.0, 1.0])
    w = np.clip((lean - args.max_lean + 10) / 10, 0, 1)[..., None]  # fades out over the last 10 degrees
    fixed = a * (1 - w) + flat * w
    fixed /= np.linalg.norm(fixed, axis=2, keepdims=True)
    px = np.ones((args.size, args.size, 4), dtype=np.float32)
    px[..., :3] = fixed * 0.5 + 0.5
    target.pixels.foreach_set(px.ravel())
    target.filepath_raw = out_png
    target.file_format = "PNG"
    target.save()
    print(f"  baked {args.size}x{args.size} tangent normals -> {out_png}; lean from the low mesh: "
          f"median {np.median(lean[used]):.1f} deg, 90% {np.percentile(lean[used], 90):.1f} deg; "
          f"{np.mean(lean[used] > args.max_lean):.1%} of texels over {args.max_lean:.0f} deg reset to flat")


# ---------------------------------------------------------------- rebuild from the high-res mesh

def fill_within(rgb, valid, covered, passes=64):
    """Fills covered-but-invalid texels from valid neighbours, growing inward; never crosses uncovered texels, so
    colour does not leak between UV islands."""
    rgb = rgb.copy()
    valid = valid & covered
    h, w = valid.shape
    for _ in range(passes):
        todo = covered & ~valid
        if not todo.any():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros((h, w), dtype=np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                src = np.zeros_like(valid)
                src[max(dy, 0):h + min(dy, 0), max(dx, 0):w + min(dx, 0)] = valid[max(-dy, 0):h + min(-dy, 0), max(-dx, 0):w + min(-dx, 0)]
                col = np.zeros_like(rgb)
                col[max(dy, 0):h + min(dy, 0), max(dx, 0):w + min(dx, 0)] = rgb[max(-dy, 0):h + min(-dy, 0), max(-dx, 0):w + min(-dx, 0)]
                acc += col * src[..., None]
                cnt += src
        grow = todo & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        valid = valid | grow
    return rgb, valid


def bake_surface_image(source, uv_name, image_path, target, size, cage, ray, colorspace="sRGB", alpha=False):
    """Bakes `source`'s image, seen through its UV map `uv_name`, onto `target`'s active UVs (selected to active), with a
    wider second search where the first rays missed. alpha=True bakes the image's alpha as grey. Returns
    (rgb HxWx3, hit HxW bool); texels no ray reached are not hits. The source's materials are restored afterwards."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 16
    sc.cycles.use_denoising = False
    sc.render.bake.margin = 0
    sc.view_settings.view_transform = "Standard"
    src = bpy.data.materials.new("BakeSource")
    src.use_nodes = True
    nt = src.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(image_path, check_existing=True)
    tex.image.colorspace_settings.name = colorspace
    tex.interpolation = "Cubic" if colorspace == "sRGB" else "Linear"
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = uv_name
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Alpha" if alpha else "Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    originals = [s.material for s in source.material_slots]
    for s in source.material_slots:
        s.material = src

    dst = bpy.data.materials.new("BakeTarget")
    dst.use_nodes = True
    tnode = dst.node_tree.nodes.new("ShaderNodeTexImage")
    dst.node_tree.nodes.active = tnode
    target_originals = list(target.data.materials)
    target.data.materials.clear()
    target.data.materials.append(dst)
    sentinel = np.array((1.0, 0.0, 1.0))

    def one(c, r):
        img = bpy.data.images.new("BakedSurface", size, size, alpha=False, float_buffer=False)
        img.colorspace_settings.name = colorspace
        px = np.ones((size, size, 4), dtype=np.float32)
        px[..., :3] = sentinel
        img.pixels.foreach_set(px.ravel())
        tnode.image = img
        bpy.ops.object.select_all(action="DESELECT")
        source.select_set(True)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.bake(type="EMIT", use_selected_to_active=True, cage_extrusion=c, max_ray_distance=r, use_clear=False)
        a = image_array(img)[..., :3]
        bpy.data.images.remove(img)
        return a, np.abs(a - sentinel).sum(2) > 0.01

    rgb, hit = one(cage, ray)
    if not hit.all():
        wide, wide_hit = one(cage * 3, ray * 4)
        take = ~hit & wide_hit
        rgb[take] = wide[take]
        hit |= wide_hit
    for s, m in zip(source.material_slots, originals):
        s.material = m
    target.data.materials.clear()
    for m in target_originals:
        target.data.materials.append(m)
    return rgb, hit


def rebuild(args):
    """A new body decimated from Meshy's high-res mesh (--faces triangles): the silhouette of the sculpt instead of
    Meshy's lumpy 15k remesh. Same armature; skin weights from the prepared body; even UVs; colour baked from the
    prepared body through its own UVs and the upscaled atlas; normal map baked from the high-res mesh."""
    arm, body = import_body(args.model)
    body_mat, body_node = base_color_node(body)
    high = import_highres_fitted(args.highres, body)  # armature space: prepare_for_unity applied every transform

    # 1. decimate (quadric collapse) a copy of the high-res mesh
    new = high.copy()
    new.data = high.data.copy()
    new.name = new.data.name = "Body_Rebuilt"
    bpy.context.scene.collection.objects.link(new)
    dec = new.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = args.faces / len(new.data.polygons)
    dec.use_collapse_triangulate = True
    select_only(new)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.mesh.delete_loose()
    bpy.ops.object.mode_set(mode="OBJECT")
    if new.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth()
    me = new.data
    print(f"  decimated {len(high.data.polygons)} -> {len(me.polygons)} triangles, {len(me.vertices)} vertices")

    # 2. even UVs: angle-based charts, scaled to one texel density, packed
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=args.margin / args.size, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    dens, used = texel_density(me, me.uv_layers["UVMap"], args.size)
    print(f"  UVs: texel density 5/50/95% {dens[5]:.0f}/{dens[50]:.0f}/{dens[95]:.0f} texels/m at {args.size}, "
          f"{used:.1%} of the atlas used")

    # 3. skin weights from the prepared body (interpolated over its nearest face)
    for g in body.vertex_groups:
        new.vertex_groups.new(name=g.name)
    dt = new.modifiers.new("Weights", "DATA_TRANSFER")
    dt.object = body
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    select_only(new)
    bpy.ops.object.modifier_apply(modifier="Weights")
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    world = new.matrix_world.copy()
    new.parent = arm
    new.matrix_world = world
    mod = new.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    unweighted = sum(1 for v in me.vertices if not v.groups)
    print(f"  skin weights from the prepared body: {len(new.vertex_groups)} groups, {unweighted} unweighted vertices")

    # 4. colour: the prepared body through its own UVs and the upscaled atlas, baked onto the new UVs
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 16
    sc.cycles.use_denoising = False
    sc.render.bake.margin = args.margin
    sc.render.bake.margin_type = "EXTEND"
    sc.view_settings.view_transform = "Standard"
    src = bpy.data.materials.new("BakeSource")
    src.use_nodes = True
    nt = src.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(args.upscaled)
    tex.interpolation = "Cubic"
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = body.data.uv_layers.active.name
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    for s in body.material_slots:
        s.material = src
    sentinel = np.array((1.0, 0.0, 1.0))
    dst = bpy.data.materials.new("BakeTarget")
    dst.use_nodes = True
    tnode = dst.node_tree.nodes.new("ShaderNodeTexImage")
    dst.node_tree.nodes.active = tnode
    me.materials.clear()
    me.materials.append(dst)

    def bake_colour(cage, ray):
        img = bpy.data.images.new("BakedColour", args.size, args.size, alpha=False, float_buffer=False)
        img.colorspace_settings.name = "sRGB"
        px = np.ones((args.size, args.size, 4), dtype=np.float32)
        px[..., :3] = sentinel  # texels no ray reaches keep this colour
        img.pixels.foreach_set(px.ravel())
        tnode.image = img
        bpy.ops.object.select_all(action="DESELECT")
        body.select_set(True)
        new.select_set(True)
        bpy.context.view_layer.objects.active = new
        bpy.ops.object.bake(type="EMIT", use_selected_to_active=True, cage_extrusion=cage, max_ray_distance=ray,
                            use_clear=False)
        a = image_array(img)[..., :3]
        return a, (np.abs(a - sentinel).sum(2) < 0.01) | (a.sum(2) < 1e-4)

    covered = rasterize(uv_triangles(me, me.uv_layers["UVMap"]), args.size)
    rgb, missed = bake_colour(args.cage, args.ray)
    first = np.mean(missed[covered])
    if missed[covered].any():  # a wider search where the body and the sculpt are further apart
        wide, wide_missed = bake_colour(args.cage * 3, args.ray * 4)
        take = missed & ~wide_missed
        rgb[take] = wide[take]
        missed &= wide_missed
    rgb, filled = fill_within(rgb, ~missed, covered)
    left = int((covered & ~filled).sum())
    print(f"  colour baked from the prepared body: {first:.2%} of texels missed, {np.mean(missed[covered]):.3%} "
          f"after a wider search, filled from their neighbours within the island ({left} left)")
    # Real colour: baked or filled inside the islands, and the bake margin (each island's own border colour) outside.
    # Everything else (whole-island misses, texels beyond the margin) takes the nearest real colour.
    rgb = push_pull_fill(rgb, (covered & filled) | (~covered & ~missed))
    save_rgb(rgb, args.texture_out)

    # 5. normal map from the high-res mesh
    bake_normal_image(new, high, args, args.normal_out)

    if args.check:
        rebuild_check(body, body_node.image, new, high, args.texture_out, args.normal_out, args.check)
        return  # check renders change materials; run again without --check to export

    # 6. export: the armature and the new body, under the prepared body's names and material
    bpy.data.objects.remove(body, do_unlink=True)
    bpy.data.objects.remove(high, do_unlink=True)
    new.name = new.data.name = "FoxGunner_Body"
    me.materials.clear()
    me.materials.append(body_mat)
    body_node.image = bpy.data.images.load(args.texture_out)
    body_node.image.pack()
    for n in body_mat.node_tree.nodes:  # one UV layer: every UV Map node reads it
        if n.type == "UVMAP":
            n.uv_map = "UVMap"
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    new.select_set(True)
    bpy.ops.export_scene.gltf(filepath=args.glb_out, export_format="GLB", use_selection=True,
                              export_animations=False, export_skins=True, export_yup=True)
    print(f"  wrote {args.glb_out}")


def rebuild_check(old_body, old_image, new, high, colour_png, normal_png, out_dir):
    """Old 15k body vs the rebuilt one: textured (Workbench) and grey with the normal map (Cycles)."""
    os.makedirs(out_dir, exist_ok=True)
    old_body.parent = None
    for o in (old_body, new):
        for m in list(o.modifiers):
            o.modifiers.remove(m)
    sc = bpy.context.scene
    engine = sc.render.engine
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_specular_highlight = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    old_mat = bpy.data.materials.new("OldTex")
    old_mat.use_nodes = True
    t = old_mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = old_image
    old_mat.node_tree.nodes.active = t
    for s in old_body.material_slots:
        s.material = old_mat
    new_mat = bpy.data.materials.new("NewTex")
    new_mat.use_nodes = True
    t2 = new_mat.node_tree.nodes.new("ShaderNodeTexImage")
    t2.image = bpy.data.images.load(colour_png, check_existing=True)
    new_mat.node_tree.nodes.active = t2
    new.data.materials.clear()
    new.data.materials.append(new_mat)
    high.hide_render = True
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    zmax = max((new.matrix_world @ Vector(c)).z for c in new.bound_box)
    views = {
        "head": (Vector((0.3, -0.62, zmax - 0.2)), Vector((0, 0, zmax - 0.3)), 30),
        "torso": (Vector((0.35, -1.0, 1.05)), Vector((0, 0, 0.95)), 30),
        "tail": (Vector((0.9, 0.9, 0.75)), Vector((0.0, 0.25, 0.6)), 32),
    }
    for vname, (loc, target, lens) in views.items():
        cam.location = loc
        cam.rotation_euler = (target - loc).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        for label, obj in (("old15k", old_body), ("new", new)):
            old_body.hide_render = obj is not old_body
            new.hide_render = obj is not new
            sc.render.filepath = os.path.join(out_dir, f"rebuild_{vname}_{label}.png")
            bpy.ops.render.render(write_still=True)
    old_body.hide_render = True
    new.hide_render = False
    high.hide_render = False
    sc.render.engine = engine
    normal_check(new, high, normal_png, out_dir)


def normal_check(body, high, normal_png, out_dir):
    """Cycles renders, plain grey under one sun: low mesh, low mesh + normal map, high-res."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.cycles.samples = 24
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("CheckWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.25, 0.27, 0.32, 1)
    sc.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    sc.collection.objects.link(sun)

    def grey(name, with_normal):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.7, 0.7, 0.7, 1)
        bsdf.inputs["Roughness"].default_value = 0.6
        if with_normal:
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = bpy.data.images.load(normal_png, check_existing=True)
            tex.image.colorspace_settings.name = "Non-Color"
            nmap = nt.nodes.new("ShaderNodeNormalMap")
            nmap.uv_map = body.data.uv_layers.active.name
            nt.links.new(tex.outputs["Color"], nmap.inputs["Color"])
            nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        return m

    plain, mapped = grey("Plain", False), grey("Mapped", True)
    high.data.materials.append(plain)
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    zmax = max((body.matrix_world @ Vector(c)).z for c in body.bound_box)
    views = {
        "head": (Vector((0.3, -0.62, zmax - 0.2)), Vector((0, 0, zmax - 0.3)), 30),
        "torso": (Vector((0.35, -1.0, 1.05)), Vector((0, 0, 0.95)), 30),
        "legs": (Vector((0.9, 0.9, 0.75)), Vector((0.0, 0.25, 0.6)), 32),
    }
    for vname, (loc, target, lens) in views.items():
        cam.location = loc
        cam.rotation_euler = (target - loc).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        for label, obj, mat in (("low", body, plain), ("low_normalmap", body, mapped), ("highres", high, plain)):
            body.hide_render = obj is not body
            high.hide_render = obj is not high
            if obj is body:
                for s in body.material_slots:
                    s.material = mat
            sc.render.filepath = os.path.join(out_dir, f"normal_{vname}_{label}.png")
            bpy.ops.render.render(write_still=True)


def main():
    args = parse()
    if args.mode == "dilate":
        dilate(args)
    elif args.mode == "upscale":
        upscale(args)
    elif args.mode == "normalmap":
        normal_map(args)
    elif args.mode == "rebuild":
        rebuild(args)
    else:
        bake(args)


if __name__ == "__main__":
    main()
