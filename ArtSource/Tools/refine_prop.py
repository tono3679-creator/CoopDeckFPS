"""A prop (e.g. a weapon) rebuilt from Meshy's high-resolution sculpt, keeping the prepared prop's frame, grip origin,
sockets and colours (run inside Blender, headless). Prop counterpart of refine_character.py's rebuild.

    blender -b --factory-startup -P refine_prop.py -- --name FireRifle \
        --prepared ArtSource/FireRifle/Export/FireRifle.glb --highres ArtSource/FireRifle/Meshy/FireRifle_highres.glb \
        --forward=-X --socket Muzzle=0,-0.605,0.137 --socket LeftHandGrip=0,-0.401,0.071 \
        --esrgan <folder with realesrgan-ncnn-vulkan.exe> --faces 30000 --size 4096 \
        --model-out <dir> --texture-out <dir> --glb-out <dir> [--check <dir>]

  1. the sculpt is turned like prepare_prop_for_unity.py did (--forward to Blender's -Y) and fitted onto the prepared
     prop by bounding box: same length, same place, so the grip stays at the origin
  2. quadric decimation to --faces; hard edges above --sharp-angle (crisp panels), smooth elsewhere; even UVs
  3. colour: the prepared prop's atlas, dilated and upscaled 4x with Real-ESRGAN (kept as <glb-out>/<name>_atlas_x4.png),
     baked through the prepared prop's own UVs (selected to active, small cage for the thin fins); normals from the sculpt
  4. sockets (prepared Blender coordinates, front = -Y) become empties; FBX turned 180 degrees for Unity's +Z, as before
Outputs: <model-out>/<name>.fbx, <texture-out>/T_<name>_BaseColor.png and T_<name>_Normal.png, <glb-out>/<name>_HQ.glb
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

AXES = {"X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)), "Y": Vector((0, 1, 0)),
        "-Y": Vector((0, -1, 0)), "Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--name", required=True)
    p.add_argument("--prepared", required=True)
    p.add_argument("--highres", required=True)
    p.add_argument("--forward", default="-X", choices=list(AXES))
    p.add_argument("--socket", action="append", default=[], help="Name=x,y,z in the prepared prop (Blender, front -Y)")
    p.add_argument("--esrgan", required=True)
    p.add_argument("--esrgan-model", default="realesrgan-x4plus-anime")
    p.add_argument("--faces", type=int, default=30000)
    p.add_argument("--size", type=int, default=4096)
    p.add_argument("--sharp-angle", type=float, default=40.0)
    p.add_argument("--model-out", required=True)
    p.add_argument("--texture-out", required=True)
    p.add_argument("--glb-out", required=True)
    p.add_argument("--check")
    a = p.parse_args(argv)
    for k in ("prepared", "highres", "esrgan", "model_out", "texture_out", "glb_out", "check"):
        if getattr(a, k):
            setattr(a, k, os.path.abspath(getattr(a, k)))
    return a


def coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    m = np.array(obj.matrix_world)
    return co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]


def import_mesh(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    mesh = next(o for o in new if o.type == "MESH")
    for o in new:
        if o is not mesh:
            bpy.data.objects.remove(o, do_unlink=True)
    world = mesh.matrix_world.copy()
    mesh.parent = None
    mesh.matrix_world = world
    rc.select_only(mesh)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return mesh


def fit_sculpt(high, prepared, forward):
    """Turns the sculpt's front to -Y, then scales and moves it onto the prepared prop's bounding box."""
    align = AXES[forward].rotation_difference(Vector((0, -1, 0))).to_matrix().to_4x4()
    high.data.transform(align)
    high.data.update()
    h, p = coords(high), coords(prepared)
    s = (p[:, 1].max() - p[:, 1].min()) / (h[:, 1].max() - h[:, 1].min())
    t = (p.min(0) + p.max(0)) / 2 - s * (h.min(0) + h.max(0)) / 2
    high.data.transform(Matrix.Translation(Vector(t)) @ Matrix.Scale(s, 4))
    high.data.update()
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromObject(prepared, bpy.context.evaluated_depsgraph_get())
    sample = coords(high)[np.random.default_rng(0).choice(len(high.data.vertices), 20000, replace=False)]
    d = np.array([(bvh.find_nearest(Vector(q))[0] - Vector(q)).length for q in sample])
    print(f"  sculpt fitted: scale {s:.4f}; distance to the prepared prop mean {d.mean() * 1000:.2f} mm, "
          f"95% {np.percentile(d, 95) * 1000:.2f} mm")


def decimate(high, faces, sharp_angle):
    prop = high.copy()
    prop.data = high.data.copy()
    bpy.context.scene.collection.objects.link(prop)
    dec = prop.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, faces / len(high.data.polygons))
    dec.use_collapse_triangulate = True
    rc.select_only(prop)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.mesh.delete_loose()
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.set_sharpness_by_angle(angle=math.radians(sharp_angle))
    bpy.ops.object.mode_set(mode="OBJECT")
    if prop.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth(keep_sharp_edges=True)
    print(f"  decimated {len(high.data.polygons)} -> {len(prop.data.polygons)} triangles, {len(prop.data.vertices)} vertices")
    return prop


def unwrap(prop, size, margin=8):
    me = prop.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    rc.select_only(prop)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=margin / size, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    dens, used = rc.texel_density(me, me.uv_layers["UVMap"], size)
    print(f"  UVs: texel density 5/50/95% {dens[5]:.0f}/{dens[50]:.0f}/{dens[95]:.0f} texels/m at {size}, {used:.1%} used")


def upscaled_atlas(prepared, args):
    """The prepared prop's own atlas, dilated (island colours pushed into the empty space) and upscaled 4x."""
    _, node = base_color_node(prepared)
    img = node.image
    size = img.size[0]
    valid = rc.rasterize(rc.uv_triangles(prepared.data, prepared.data.uv_layers.active), size)
    rgb = rc.image_array(img)[..., :3]
    dilated = os.path.join(args.glb_out, f"_{args.name}_atlas_dilated.png")
    rc.save_rgb(rc.push_pull_fill(rgb, valid), dilated, img.colorspace_settings.name)
    out = os.path.join(args.glb_out, f"{args.name}_atlas_x4.png")
    rc.upscale(SimpleNamespace(inp=dilated, out=out, esrgan=args.esrgan, esrgan_model=args.esrgan_model, tile=512))
    os.remove(dilated)
    return out


def base_color_node(obj):
    for slot in obj.material_slots:
        if slot.material and slot.material.node_tree:
            for node in slot.material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    return slot.material, node
    raise RuntimeError("no base color texture on " + obj.name)


def main():
    args = parse()
    for d in (args.model_out, args.texture_out, args.glb_out):
        os.makedirs(d, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    prepared = import_mesh(args.prepared)
    material, _ = base_color_node(prepared)
    high = import_mesh(args.highres)
    fit_sculpt(high, prepared, args.forward)
    prop = decimate(high, args.faces, args.sharp_angle)
    prop.data.materials.clear()
    prop.data.materials.append(material)
    unwrap(prop, args.size)

    atlas = upscaled_atlas(prepared, args)
    base_out = os.path.join(args.texture_out, f"T_{args.name}_BaseColor.png")
    normal_out = os.path.join(args.texture_out, f"T_{args.name}_Normal.png")
    covered = rc.rasterize(rc.uv_triangles(prop.data, prop.data.uv_layers["UVMap"]), args.size)
    rgb, hit = rc.bake_surface_image(prepared, prepared.data.uv_layers.active.name, atlas, prop, args.size,
                                     cage=0.002, ray=0.004)
    rgb, filled = rc.fill_within(rgb, hit, covered)
    print(f"  colour: {np.mean(~hit[covered]):.3%} of texels missed after a wider search, "
          f"{int((covered & ~filled).sum())} left after filling inside the islands")
    rgb = rc.push_pull_fill(rgb, (covered & filled) | (~covered & hit))
    rc.save_rgb(rgb, base_out)
    rc.bake_normal_image(prop, high, SimpleNamespace(size=args.size, cage=0.002, ray=0.004, max_lean=75.0), normal_out)

    if args.check:
        check(prepared, prop, base_out, args.check)

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
    _, node = base_color_node(prop)
    node.image = bpy.data.images.load(base_out)
    node.image.pack()
    rc.select_only(prop)
    bpy.ops.export_scene.gltf(filepath=os.path.join(args.glb_out, f"{args.name}_HQ.glb"), export_format="GLB",
                              use_selection=True, export_yup=True)

    turn = Matrix.Rotation(math.pi, 4, "Z")  # Unity keeps the FBX's front on -Z: turn around, as prepare_prop does
    prop.data.transform(turn)
    for e in sockets:
        e.location = turn @ e.location
    bpy.ops.object.select_all(action="DESELECT")
    for o in [prop, *sockets]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = prop
    path = os.path.join(args.model_out, f"{args.name}.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="OFF", use_tspace=False, bake_anim=False,
                             path_mode="STRIP", embed_textures=False)
    print(f"  wrote {path}")
    print("REFINE_PROP_DONE")


def check(prepared, prop, base_png, out_dir):
    """Workbench: the prepared prop next to the rebuilt one (texture), from three sides."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_specular_highlight = True
    sc.render.resolution_x, sc.render.resolution_y = 1200, 600
    mat = bpy.data.materials.new("CheckNew")
    mat.use_nodes = True
    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(base_png, check_existing=True)
    mat.node_tree.nodes.active = t
    keep = list(prop.data.materials)
    prop.data.materials.clear()
    prop.data.materials.append(mat)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = True
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    views = {"side": (Vector((0.9, -0.15, 0.1)), Vector((0, -0.15, 0.05)), 40),
             "scope": (Vector((0.25, -0.45, 0.3)), Vector((0, -0.4, 0.1)), 40),
             "rear": (Vector((0.35, 0.45, 0.25)), Vector((0, 0.0, 0.05)), 40)}
    for name, (loc, target, lens) in views.items():
        cam.location = loc
        cam.rotation_euler = (target - loc).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        for label, obj in (("old", prepared), ("new", prop)):
            prepared.hide_render = obj is not prepared
            prop.hide_render = obj is not prop
            sc.render.filepath = os.path.join(out_dir, f"prop_{name}_{label}.png")
            bpy.ops.render.render(write_still=True)
    prop.data.materials.clear()
    for m in keep:
        prop.data.materials.append(m)


if __name__ == "__main__":
    main()
