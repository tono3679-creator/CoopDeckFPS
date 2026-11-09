"""A hard-surface weapon rebuilt from Meshy's sculpt for first-person close-ups, with the surface maps the weapon
shader reads (run inside Blender, headless). Same frame, grip origin, sockets and colour source as refine_prop.py,
whose helpers it calls; what it adds:

  - shape: the sculpt decimated to --faces with hard edges above --sharp-angle, small bevels (--bevel metres, two
    segments) on the creases above --bevel-angle where the decimation folded the sculpt's rounded edges, and
    face-area weighted normals so flat armour panels shade flat (no smudges from thin triangles)
  - --lod N: an LOD1 of N triangles in the same FBX (<name>_Body_LOD0 / _LOD1) for other players' weapons
  - T_<name>_Surface.png (linear, --size): R convex edges and B concave creases (curvature of the sculpt's normals,
    measured per texel), G ambient occlusion;
    the shader turns R into edge highlights (the "bevel" light catch) and G/B into panel-gap shading
  - T_<name>_MaterialMask.png (linear, --mask-size): R white paint, G coloured paint (--paint-hue), B dark polymer,
    A bare metal; whatever remains is the accent (--accent-hue). Classified from the base colour. Fire Rifle: red
    paint and gold (the defaults). Lightning SMG: --paint-hue 0.055-0.175 (yellow-orange) --accent-hue 0.44-0.62 (cyan glass)

    blender -b --factory-startup -P rebuild_weapon_hq.py -- --name FireRifle \
        --prepared ArtSource/FireRifle/Export/FireRifle.glb --highres ArtSource/FireRifle/Meshy/FireRifle_highres.glb \
        --atlas ArtSource/FireRifle/Export/FireRifle_atlas_x4.png --forward=-X \
        --socket Muzzle=0,-0.605,0.137 --socket LeftHandGrip=0,-0.401,0.071 \
        --faces 60000 --bevel 0.0012 --bevel-angle 50 --lod 30000 --size 4096 \
        --model-out Assets/_Game/Art/Weapons/FireRifle/Models --texture-out Assets/_Game/Art/Weapons/FireRifle/Textures \
        --glb-out ArtSource/FireRifle/Export [--check <dir>]

--atlas is the prepared prop's atlas already upscaled (refine_prop.py's <glb-out>/<name>_atlas_x4.png, or
rebuild_prop_hq.py's _atlas_x2.png for a 4K Meshy texture); without it pass --esrgan to make it again (4x).
"""
import argparse
import math
import os
import sys
from types import SimpleNamespace

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import refine_character as rc  # noqa: E402
import refine_prop as rp  # noqa: E402


def hue_range(text):
    lo, hi = (float(x) for x in text.split("-"))
    return lo, hi


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--name", required=True)
    p.add_argument("--prepared", required=True)
    p.add_argument("--highres", required=True)
    p.add_argument("--atlas")
    p.add_argument("--esrgan")
    p.add_argument("--esrgan-model", default="realesrgan-x4plus-anime")
    p.add_argument("--forward", default="-X", choices=list(rp.AXES))
    p.add_argument("--socket", action="append", default=[])
    p.add_argument("--faces", type=int, default=60000)
    p.add_argument("--sharp-angle", type=float, default=40.0)
    p.add_argument("--bevel", type=float, default=0.0012)
    p.add_argument("--bevel-angle", type=float, default=50.0)
    p.add_argument("--lod", type=int, default=0)
    p.add_argument("--size", type=int, default=4096)
    p.add_argument("--mask-size", type=int, default=2048)
    p.add_argument("--ao-distance", type=float, default=0.012)
    p.add_argument("--ao-samples", type=int, default=32)
    p.add_argument("--edge-low", type=float, default=100.0, help="curvature (1/m) where edge highlights start")
    p.add_argument("--edge-high", type=float, default=400.0, help="curvature (1/m) of a full edge highlight")
    p.add_argument("--paint-hue", type=hue_range, default=(0.935, 0.0475), help="hue range lo-hi of the coloured paint (G)")
    p.add_argument("--accent-hue", type=hue_range, default=(0.0475, 0.185), help="hue range lo-hi of the accent (the rest)")
    p.add_argument("--model-out", required=True)
    p.add_argument("--texture-out", required=True)
    p.add_argument("--glb-out", required=True)
    p.add_argument("--check")
    a = p.parse_args(argv)
    for k in ("prepared", "highres", "atlas", "esrgan", "model_out", "texture_out", "glb_out", "check"):
        if getattr(a, k):
            setattr(a, k, os.path.abspath(getattr(a, k)))
    if not a.atlas and not a.esrgan:
        p.error("pass --atlas (an existing upscaled atlas) or --esrgan")
    return a


# ---------------------------------------------------------------- shape

def edit(obj, *ops):
    rc.select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    for op in ops:
        op()
    bpy.ops.object.mode_set(mode="OBJECT")


def weighted_normals(obj, sharp_angle):
    """Hard edges above sharp_angle, then face-area weighted normals: big flat faces keep their own normal."""
    edit(obj, lambda: bpy.ops.mesh.set_sharpness_by_angle(angle=math.radians(sharp_angle)))
    if obj.data.has_custom_normals:
        rc.select_only(obj)
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    rc.select_only(obj)
    bpy.ops.object.shade_smooth(keep_sharp_edges=True)
    m = obj.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    m.mode = "FACE_AREA"
    m.weight = 50
    m.keep_sharp = True
    m.thresh = 0.01
    bpy.ops.object.modifier_apply(modifier=m.name)


def bevel_creases(obj, width, angle, sharp_angle):
    if width <= 0:
        return
    before = len(obj.data.polygons)
    rc.select_only(obj)
    m = obj.modifiers.new("Bevel", "BEVEL")
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    m.width = width
    m.segments = 2
    m.use_clamp_overlap = True
    m.harden_normals = False
    bpy.ops.object.modifier_apply(modifier=m.name)
    edit(obj, lambda: bpy.ops.mesh.quads_convert_to_tris(), lambda: bpy.ops.mesh.delete_loose())
    print(f"  bevel {width * 1000:.1f} mm on creases over {angle:.0f} deg: {before} -> {len(obj.data.polygons)} triangles")


def build_shape(high, args):
    prop = rp.decimate(high, args.faces, args.sharp_angle)
    bevel_creases(prop, args.bevel, args.bevel_angle, args.sharp_angle)
    weighted_normals(prop, args.sharp_angle)
    return prop


# ---------------------------------------------------------------- surface maps from the sculpt

def smoothstep(lo, hi, x):
    t = np.clip((x - lo) / (hi - lo), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def bake_to_image(target, size, bake_type, source=None, emit_position=False, **kwargs):
    """One Cycles bake into a float, linear image on `target`'s UVs; returns (rgb HxWx3, written HxW bool).
    source: bake selected-to-active from it; emit_position: emit the target's own object-space position."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.use_denoising = False
    sc.render.bake.margin = 0
    dst = bpy.data.materials.new("BakeTarget")
    dst.use_nodes = True
    nt = dst.node_tree
    tnode = nt.nodes.new("ShaderNodeTexImage")
    nt.nodes.active = tnode
    if emit_position:
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        emit = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(geo.outputs["Position"], emit.inputs["Color"])
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    originals = list(target.data.materials)
    target.data.materials.clear()
    target.data.materials.append(dst)
    img = bpy.data.images.new("Baked", size, size, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    sentinel = np.array((-7.0, 7.0, -7.0), dtype=np.float32)
    px = np.ones((size, size, 4), dtype=np.float32)
    px[..., :3] = sentinel
    img.pixels.foreach_set(px.ravel())
    tnode.image = img
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    if source is not None:
        source.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.bake(type=bake_type, use_selected_to_active=source is not None, use_clear=False, **kwargs)
    a = rc.image_array(img)[..., :3].copy()
    bpy.data.images.remove(img)
    target.data.materials.clear()
    for m in originals:
        target.data.materials.append(m)
    return a, np.abs(a - sentinel).sum(2) > 0.01


def shift(a, dy, dx):
    """a moved by (dy, dx) texels; the uncovered border repeats nothing (zeros)."""
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    out[max(dy, 0):h + min(dy, 0), max(dx, 0):w + min(dx, 0)] = a[max(-dy, 0):h + min(-dy, 0), max(-dx, 0):w + min(-dx, 0)]
    return out


def neighbour_masks(valid, position, max_step):
    """For each of the 8 neighbours: both texels covered and within max_step in 3D (the same surface patch)."""
    masks = {}
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx or dy:
                masks[(dy, dx)] = valid & shift(valid, dy, dx) & \
                    (np.linalg.norm(shift(position, dy, dx) - position, axis=2) < max_step)
    return masks


def island_blur(value, valid, masks, passes):
    """Box blur (3x3) that only mixes texels of the same surface patch (see neighbour_masks)."""
    v = np.where(valid, value, 0).astype(np.float32)
    for _ in range(passes):
        acc = v.copy()
        cnt = valid.astype(np.float32)
        for (dy, dx), ok in masks.items():
            acc += shift(v, dy, dx) * ok
            cnt += ok
        v = np.where(valid, acc / np.maximum(cnt, 1), 0)
    return v


def island_max(value, masks):
    out = value.copy()
    for (dy, dx), ok in masks.items():
        out = np.where(ok, np.maximum(out, shift(value, dy, dx)), out)
    return out


def surface_maps(prop, high, covered, args):
    """Per texel of the game mesh: R convex edges and B concave creases of the sculpt (curvature of its normals,
    measured between neighbouring texels of the same patch), G ambient occlusion of the game mesh (which is within
    0.1 mm of the sculpt). Returns HxWx3 in 0..1."""
    size = args.size
    pos, pos_ok = bake_to_image(prop, size, "EMIT", emit_position=True)
    nrm, nrm_ok = bake_to_image(prop, size, "NORMAL", source=high, normal_space="OBJECT",
                                cage_extrusion=0.002, max_ray_distance=0.004)
    flat, _ = bake_to_image(prop, size, "NORMAL", normal_space="OBJECT")
    nrm = np.where(nrm_ok[..., None], nrm, flat) * 2 - 1
    nrm /= np.maximum(np.linalg.norm(nrm, axis=2, keepdims=True), 1e-6)
    valid = covered & pos_ok
    texel = 1.0 / 3700.0  # metres per texel at the packed density (rp.unwrap prints it)
    step = 2              # measure across two texels: the normal map's own texel noise averages out
    ksum = np.zeros(valid.shape, dtype=np.float32)
    kcnt = np.zeros(valid.shape, dtype=np.float32)
    for dy, dx in ((0, step), (step, 0), (step, step), (step, -step)):
        for sy, sx in ((dy, dx), (-dy, -dx)):
            pj, nj, vj = shift(pos, sy, sx), shift(nrm, sy, sx), shift(valid, sy, sx)
            d = pj - pos
            dist2 = (d * d).sum(2)
            ok = valid & vj & (dist2 < (step * texel * 3.0) ** 2) & (dist2 > 1e-12)
            k = ((nj - nrm) * d).sum(2) / np.maximum(dist2, 1e-12)
            ksum += np.where(ok, k, 0)
            kcnt += ok
    kv = np.where(kcnt > 0, ksum / np.maximum(kcnt, 1), 0)
    print("  texel curvature (1/m) over the patches 50/90/99/99.9%: " +
          "/".join(f"{np.percentile(kv[valid], q):.0f}" for q in (50, 90, 99, 99.9)))
    masks = neighbour_masks(valid, pos, texel * 3.0)
    convex = smoothstep(args.edge_low, args.edge_high, kv).astype(np.float32)
    concave = smoothstep(args.edge_low, args.edge_high, -kv).astype(np.float32)
    convex = island_blur(island_max(convex, masks), valid, masks, 2)
    concave = island_blur(island_max(concave, masks), valid, masks, 2)

    world = bpy.context.scene.world or bpy.data.worlds.new("BakeWorld")
    bpy.context.scene.world = world
    world.light_settings.distance = args.ao_distance
    bpy.context.scene.cycles.samples = args.ao_samples
    # only the game mesh may occlude itself: the sculpt and the prepared prop lie on the same surface
    hidden = {o: o.hide_render for o in bpy.data.objects if o is not prop}
    for o in hidden:
        o.hide_render = True
    ao, ao_ok = bake_to_image(prop, size, "AO")
    for o, h in hidden.items():
        o.hide_render = h
    ao = np.where(ao_ok, ao[..., 0], 1.0).astype(np.float32)
    ao = island_blur(ao, valid, masks, 1)
    out = np.stack([convex, np.where(valid, ao, 1.0), concave], 2)
    print(f"  surface: convex edge share {np.mean(convex[valid] > 0.5):.1%}, concave {np.mean(concave[valid] > 0.5):.1%}, "
          f"AO mean {ao[valid].mean():.3f} (5% {np.percentile(ao[valid], 5):.3f})")
    return out, valid


# ---------------------------------------------------------------- material classes from the colour

def rgb_to_hsv(rgb):
    mx, mn = rgb.max(-1), rgb.min(-1)
    v = mx
    s = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6.0
    return h, s, v


def hue_band(h, lo, hi, soft=0.0125):
    """1 inside the hue range lo..hi (wrapping through red when lo > hi), easing out over `soft` on each side."""
    if lo <= hi:
        return smoothstep(lo - soft, lo + soft, h) * (1 - smoothstep(hi - soft, hi + soft, h))
    return np.maximum(1 - smoothstep(hi - soft, hi + soft, h), smoothstep(lo - soft, lo + soft, h))


def material_weights(rgb, paint_hue=(0.935, 0.0475), accent_hue=(0.0475, 0.185)):
    """Soft per-texel weights (white paint, coloured paint, dark polymer, bare metal, accent) from sRGB colour.
    The coloured paint and the accent are hue ranges: Fire Rifle red paint and gold (defaults); Lightning SMG
    yellow-orange paint and the cyan energy glass."""
    h, s, v = rgb_to_hsv(rgb)
    paint_band = hue_band(h, *paint_hue)
    white = smoothstep(0.55, 0.68, v) * (1 - smoothstep(0.24, 0.34, s))
    metal = (1 - smoothstep(0.16, 0.26, s)) * smoothstep(0.27, 0.35, v) * (1 - smoothstep(0.58, 0.68, v))
    dark = (1 - smoothstep(0.25, 0.35, v)) * (1 - smoothstep(0.55, 0.72, s) * paint_band)
    paint = paint_band * smoothstep(0.38, 0.52, s) * smoothstep(0.16, 0.26, v)
    accent = hue_band(h, *accent_hue) * smoothstep(0.3, 0.5, s) * smoothstep(0.3, 0.42, v)
    w = np.stack([white, paint, dark, metal, accent], -1) + 1e-4
    w[..., 2] += 2e-4  # nothing else: dark polymer
    return w / w.sum(-1, keepdims=True)


def blur3(a):
    p = np.pad(a, ((1, 1), (1, 1), (0, 0)), mode="edge")
    return sum(p[dy:dy + a.shape[0], dx:dx + a.shape[1]] for dy in range(3) for dx in range(3)) / 9.0


def save_rgba(rgba, path):
    h, w, _ = rgba.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "CHANNEL_PACKED"
    img.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def write_material_mask(base_png, covered, size, out_png, paint_hue, accent_hue):
    img = bpy.data.images.load(base_png, check_existing=False)
    img.colorspace_settings.name = "Non-Color"  # classify the stored (sRGB-encoded) values
    rgb = rc.image_array(img)[..., :3].astype(np.float32)
    bpy.data.images.remove(img)
    w = material_weights(rgb, paint_hue, accent_hue).astype(np.float32)
    f = w.shape[0] // size
    if f > 1:
        w = w.reshape(size, f, size, f, 5).mean((1, 3))
        covered = covered.reshape(size, f, size, f).any((1, 3))
    w = blur3(w)
    names = ("white paint", "coloured paint", "dark polymer", "bare metal", "accent")
    share = w[covered].mean(0)
    print("  material classes: " + ", ".join(f"{n} {x:.1%}" for n, x in zip(names, share)))
    save_rgba(w[..., :4], out_png)
    return share


# ---------------------------------------------------------------- LOD, export

def make_lod(prop, faces, sharp_angle):
    lod = prop.copy()
    lod.data = prop.data.copy()
    bpy.context.scene.collection.objects.link(lod)
    rc.select_only(lod)
    d = lod.modifiers.new("Decimate", "DECIMATE")
    d.ratio = faces / len(prop.data.polygons)
    d.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=d.name)
    weighted_normals(lod, sharp_angle)
    return lod


def export(prop, lod, sockets, args):
    turn = Matrix.Rotation(math.pi, 4, "Z")  # Unity keeps the FBX's front on -Z: turn around, as prepare_prop does
    meshes = [prop] + ([lod] if lod else [])
    for o in meshes:
        o.data.transform(turn)
    for e in sockets:
        e.location = turn @ e.location
    if lod:
        prop.name = prop.data.name = f"{args.name}_Body_LOD0"
        lod.name = lod.data.name = f"{args.name}_Body_LOD1"
    else:
        prop.name = prop.data.name = f"{args.name}_Body"
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes + sockets:
        o.select_set(True)
    bpy.context.view_layer.objects.active = prop
    path = os.path.join(args.model_out, f"{args.name}.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="OFF", use_tspace=False, bake_anim=False,
                             path_mode="STRIP", embed_textures=False)
    for o in meshes:
        print(f"  {o.name}: {len(o.data.polygons)} triangles, {len(o.data.vertices)} vertices")
    print(f"  wrote {path}")


def check_renders(prepared, prop, base_png, out_dir):
    """Workbench: the old prop and the rebuilt one, grey (shape) and textured, from the first-person eye."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.render_aa = "16"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.show_specular_highlight = True
    sh.background_type = "VIEWPORT"
    sh.background_color = (0.35, 0.38, 0.44)
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    mat = bpy.data.materials.new("CheckNew")
    mat.use_nodes = True
    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(base_png, check_existing=True)
    mat.node_tree.nodes.active = t
    keep = list(prop.data.materials)
    prop.data.materials.clear()
    prop.data.materials.append(mat)
    hidden = {o: o.hide_render for o in bpy.data.objects}
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle = math.radians(70)
    cam.data.clip_start = 0.01
    cam.location = Vector((0.15, 0.39, 0.20))
    cam.rotation_euler = (Vector((0.15, 0.0, 0.20)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = True
    for colour in ("SINGLE", "TEXTURE"):
        sh.color_type = colour
        sh.single_color = (0.72, 0.72, 0.74)
        for label, obj in (("old", prepared), ("new", prop)):
            obj.hide_render = False
            sc.render.filepath = os.path.join(out_dir, f"fp_{colour.lower()}_{label}.png")
            bpy.ops.render.render(write_still=True)
            obj.hide_render = True
    for o, h in hidden.items():
        o.hide_render = h
    bpy.data.objects.remove(cam, do_unlink=True)
    prop.data.materials.clear()
    for m in keep:
        prop.data.materials.append(m)


def main():
    args = parse()
    for d in (args.model_out, args.texture_out, args.glb_out):
        os.makedirs(d, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    prepared = rp.import_mesh(args.prepared)
    material, _ = rp.base_color_node(prepared)
    high = rp.import_mesh(args.highres)
    rp.fit_sculpt(high, prepared, args.forward)
    rc.select_only(high)
    bpy.ops.object.shade_smooth()

    prop = build_shape(high, args)
    prop.data.materials.clear()
    prop.data.materials.append(material)
    rp.unwrap(prop, args.size)
    covered = rc.rasterize(rc.uv_triangles(prop.data, prop.data.uv_layers["UVMap"]), args.size)

    # colour: the prepared prop's upscaled atlas through its own UVs (as refine_prop.py)
    atlas = args.atlas if args.atlas and os.path.exists(args.atlas) else rp.upscaled_atlas(prepared, args)
    base_out = os.path.join(args.texture_out, f"T_{args.name}_BaseColor.png")
    rgb, hit = rc.bake_surface_image(prepared, prepared.data.uv_layers.active.name, atlas, prop, args.size,
                                     cage=0.002, ray=0.004)
    rgb, filled = rc.fill_within(rgb, hit, covered)
    print(f"  colour: {np.mean(~hit[covered]):.3%} of texels missed after a wider search, "
          f"{int((covered & ~filled).sum())} left after filling inside the islands")
    rgb = rc.push_pull_fill(rgb, (covered & filled) | (~covered & hit))
    rc.save_rgb(rgb, base_out)

    # normals from the sculpt (tangent space of the weighted normals, MikkTSpace)
    normal_out = os.path.join(args.texture_out, f"T_{args.name}_Normal.png")
    rc.bake_normal_image(prop, high, SimpleNamespace(size=args.size, cage=0.002, ray=0.004, max_lean=75.0), normal_out)

    # edges, occlusion and creases of the sculpt
    srf, srf_valid = surface_maps(prop, high, covered, args)
    srf = rc.push_pull_fill(srf, srf_valid)  # gutters take the island colours (mip maps, filtering)
    surface_out = os.path.join(args.texture_out, f"T_{args.name}_Surface.png")
    rc.save_rgb(srf, surface_out, colorspace="Non-Color")
    print(f"  surface map -> {surface_out}")

    mask_out = os.path.join(args.texture_out, f"T_{args.name}_MaterialMask.png")
    write_material_mask(base_out, covered, args.mask_size, mask_out, args.paint_hue, args.accent_hue)

    if args.check:
        check_renders(prepared, prop, base_out, args.check)

    lod = make_lod(prop, args.lod, args.sharp_angle) if args.lod else None

    sockets = []
    for spec in args.socket:
        name, xyz = spec.split("=", 1)
        empty = bpy.data.objects.new(name, None)
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.05
        bpy.context.scene.collection.objects.link(empty)
        empty.location = Vector(tuple(float(v) for v in xyz.split(",")))
        sockets.append(empty)
    for o in (prepared, high):
        bpy.data.objects.remove(o, do_unlink=True)

    prop.name = prop.data.name = f"{args.name}_Body"
    _, node = rp.base_color_node(prop)
    node.image = bpy.data.images.load(base_out)
    node.image.pack()
    rc.select_only(prop)
    bpy.ops.export_scene.gltf(filepath=os.path.join(args.glb_out, f"{args.name}_HQ.glb"), export_format="GLB",
                              use_selection=True, export_yup=True)
    export(prop, lod, sockets, args)
    print("REBUILD_WEAPON_DONE")


if __name__ == "__main__":
    main()
