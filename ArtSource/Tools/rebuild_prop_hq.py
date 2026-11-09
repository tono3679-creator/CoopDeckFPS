"""refine_prop.py for props whose Meshy texture is already 4K (run inside Blender, headless).

    blender -b --factory-startup -P rebuild_prop_hq.py -- <refine_prop.py arguments> [--lod 30000]

refine_prop.py (shared, unchanged) upscales the prepared prop's atlas 4x with Real-ESRGAN: right for a 2K Meshy
texture (8K result), but a 4K texture would become 16K (about 3 GB in memory). Here the atlas step is replaced: the
4K atlas is dilated, upscaled 4x tile by tile with Real-ESRGAN and each tile is averaged 2x2 on the way out, so the
bake source is 8K (the upscaler's clean line art at twice the original texel density). Everything else is
refine_prop.py's own code: fit the sculpt, decimate with hard edges, even UVs, colour and normal bakes, sockets, FBX.

--lod N also writes <model-out>/<name>.fbx with two meshes, <name>_Body_LOD0 (the --faces mesh) and
<name>_Body_LOD1 (decimated to N triangles, same UVs and textures), so Unity builds a LOD group: first person always
sees LOD0, other players' weapons switch to LOD1 at a distance.
"""
import math
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import refine_character as rc  # noqa: E402
import refine_prop as rp  # noqa: E402


def upscale_to_double(inp, out, esrgan, model, tile=512, overlap=32):
    """Real-ESRGAN x4 per tile, each tile averaged 2x2: the output is twice the input size."""
    img = bpy.data.images.load(inp)
    colorspace = img.colorspace_settings.name
    a = rc.image_array(img)[..., :3]
    h, w, _ = a.shape
    out_a = np.zeros((h * 2, w * 2, 3), dtype=np.float32)
    exe = os.path.join(esrgan, "realesrgan-ncnn-vulkan.exe")
    models = os.path.join(esrgan, "models")
    with tempfile.TemporaryDirectory() as tmp:
        src, dst = os.path.join(tmp, "in.png"), os.path.join(tmp, "out.png")
        for y0 in range(0, h, tile):
            for x0 in range(0, w, tile):
                y1, x1 = min(y0 + tile, h), min(x0 + tile, w)
                py0, px0 = max(y0 - overlap, 0), max(x0 - overlap, 0)
                py1, px1 = min(y1 + overlap, h), min(x1 + overlap, w)
                rc.save_rgb(a[py0:py1, px0:px1], src, colorspace)
                r = subprocess.run([exe, "-i", src, "-o", dst, "-n", model, "-s", "4", "-t", "64", "-m", models,
                                    "-f", "png"], capture_output=True, text=True)
                if r.returncode != 0 or not os.path.exists(dst):
                    raise RuntimeError(f"Real-ESRGAN failed on tile {x0},{y0}: {r.stderr[-400:]}")
                t4 = rc.image_array(bpy.data.images.load(dst, check_existing=False))[..., :3]
                t2 = t4.reshape(t4.shape[0] // 2, 2, t4.shape[1] // 2, 2, 3).mean(axis=(1, 3))
                cy, cx = (y0 - py0) * 2, (x0 - px0) * 2
                out_a[y0 * 2:y1 * 2, x0 * 2:x1 * 2] = t2[cy:cy + (y1 - y0) * 2, cx:cx + (x1 - x0) * 2]
                os.remove(dst)
                for im in list(bpy.data.images):
                    if im.name.startswith(("out", "in")):
                        bpy.data.images.remove(im)
    rc.save_rgb(out_a, out, colorspace)
    bpy.data.images.remove(img)
    print(f"  upscaled {w}x{h} -> x4 -> averaged to {w * 2}x{h * 2} -> {out}")


def upscaled_atlas_double(prepared, args):
    _, node = rp.base_color_node(prepared)
    img = node.image
    size = img.size[0]
    valid = rc.rasterize(rc.uv_triangles(prepared.data, prepared.data.uv_layers.active), size)
    rgb = rc.image_array(img)[..., :3]
    dilated = os.path.join(args.glb_out, f"_{args.name}_atlas_dilated.png")
    rc.save_rgb(rc.push_pull_fill(rgb, valid), dilated, img.colorspace_settings.name)
    out = os.path.join(args.glb_out, f"{args.name}_atlas_x2.png")
    if os.path.exists(out) and os.path.getmtime(out) > os.path.getmtime(dilated) - 1e9 and os.environ.get("REUSE_ATLAS"):
        print(f"  reusing {out}")
    else:
        upscale_to_double(dilated, out, args.esrgan, args.esrgan_model)
    os.remove(dilated)
    return out


def write_lod_fbx(name, model_out, lod_faces):
    """Re-exports <name>.fbx with LOD0 (the rebuilt prop) and LOD1 (decimated copy) plus the sockets."""
    prop = bpy.data.objects[f"{name}_Body"]
    sockets = [o for o in bpy.data.objects if o.type == "EMPTY"]
    lod1 = prop.copy()
    lod1.data = prop.data.copy()
    bpy.context.scene.collection.objects.link(lod1)
    d = lod1.modifiers.new("Decimate", "DECIMATE")
    d.ratio = lod_faces / len(prop.data.polygons)
    d.use_collapse_triangulate = True
    rc.select_only(lod1)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    prop.name = prop.data.name = f"{name}_Body_LOD0"
    lod1.name = lod1.data.name = f"{name}_Body_LOD1"
    for o in (prop, lod1):
        print(f"  {o.name}: {len(o.data.polygons)} triangles, {len(o.data.vertices)} vertices")
    bpy.ops.object.select_all(action="DESELECT")
    for o in [prop, lod1, *sockets]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = prop
    path = os.path.join(model_out, f"{name}.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="OFF", use_tspace=False, bake_anim=False,
                             path_mode="STRIP", embed_textures=False)
    print(f"  wrote {path} (LOD0 + LOD1)")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    lod = None
    if "--lod" in argv:
        k = argv.index("--lod")
        lod = int(argv[k + 1])
        del argv[k:k + 2]
    sys.argv = [sys.argv[0], "--"] + argv
    rp.upscaled_atlas = upscaled_atlas_double     # the only change to refine_prop's flow
    rp.main()
    if lod:
        args = rp.parse()
        write_lod_fbx(args.name, args.model_out, lod)


if __name__ == "__main__":
    main()
