"""Turn a Meshy prop (e.g. a weapon) into Unity-ready files (run inside Blender, headless).

    blender -b --factory-startup -P prepare_prop_for_unity.py -- \
        --name FireRifle --model FireRifle_mesh.glb --texture FireRifle_mesh_base_color.png \
        --forward=-X --length 0.85 --pivot=0.40,0,-0.22 \
        --socket Muzzle=-0.957,0,0.088 --socket LeftHandGrip=-0.50,0,-0.06 \
        --model-out <dir> --texture-out <dir> --glb-out <dir>

Coordinates for --pivot / --socket are in the source GLB as Blender imports it (Z up, metres).
  - welds vertices split at UV seams; smooth normals with hard edges above --sharp-angle
    (hard-surface panels stay crisp under cel shading; Unity stores averaged normals for the outline)
  - turns the prop so --forward points to the front, scales it to --length along that axis and
    moves --pivot (the hand grip) to the origin
  - sockets become empties next to the mesh, so Unity gets child transforms (muzzle, left hand)
  - GLB keeps glTF's front (+Z); the FBX is turned 180 degrees to face Unity's +Z
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

AXES = {"X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)), "Y": Vector((0, 1, 0)),
        "-Y": Vector((0, -1, 0)), "Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}


def vec(text):
    return Vector(tuple(float(v) for v in text.split(",")))


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--name", required=True)
    p.add_argument("--model", required=True)
    p.add_argument("--texture", help="PNG to use as base color instead of the GLB's own image")
    p.add_argument("--forward", default="-X", choices=list(AXES), help="where the front (muzzle) points in the source")
    p.add_argument("--length", type=float, required=True, help="size along the forward axis, metres")
    p.add_argument("--pivot", type=vec, default=Vector((0, 0, 0)), help="source point that becomes the origin")
    p.add_argument("--socket", action="append", default=[], help="Name=x,y,z in source coordinates")
    p.add_argument("--sharp-angle", type=float, default=40.0)
    p.add_argument("--model-out", required=True)
    p.add_argument("--texture-out", required=True)
    p.add_argument("--glb-out", required=True)
    return p.parse_args(argv)


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def main():
    args = parse_args()
    for d in (args.model_out, args.texture_out, args.glb_out):
        os.makedirs(d, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=args.model)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"expected one mesh, got {len(meshes)}")
    body = meshes[0]
    for o in [o for o in bpy.data.objects if o is not body]:
        bpy.data.objects.remove(o, do_unlink=True)
    body.parent = None

    # Bake the import transform, then weld and set the normals.
    select_only(body)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    before = len(body.data.vertices)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.set_sharpness_by_angle(angle=math.radians(args.sharp_angle))
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth(keep_sharp_edges=True)
    print(f"  welded {before} -> {len(body.data.vertices)} vertices, hard edges above {args.sharp_angle} deg")

    # Front → -Y (Blender's front), grip at the origin, scaled to the requested length.
    forward = AXES[args.forward]
    along = [v.co.dot(forward) for v in body.data.vertices]
    extent = max(along) - min(along)
    scale = args.length / extent
    align = forward.rotation_difference(Vector((0, -1, 0))).to_matrix().to_4x4()
    to_front = Matrix.Scale(scale, 4) @ align @ Matrix.Translation(-args.pivot)
    body.data.transform(to_front)
    body.data.update()
    print(f"  length {extent:.3f} -> {args.length:.3f} m (x{scale:.4f}), forward {args.forward} -> front")

    sockets = []
    for spec in args.socket:
        name, coords = spec.split("=", 1)
        empty = bpy.data.objects.new(name, None)
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.05
        bpy.context.scene.collection.objects.link(empty)
        empty.location = to_front @ vec(coords)
        sockets.append(empty)
        print(f"  socket {name} at {tuple(round(c, 3) for c in empty.location)} (Blender, front = -Y)")

    body.name = f"{args.name}_Body"
    body.data.name = f"{args.name}_Body"
    material = body.material_slots[0].material if body.material_slots else None
    if material:
        material.name = f"M_{args.name}"
        for node in material.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                tex = os.path.join(args.texture_out, f"T_{args.name}_BaseColor.png")
                if args.texture:
                    with open(args.texture, "rb") as src, open(tex, "wb") as dst:
                        dst.write(src.read())
                else:
                    copy = node.image.copy()
                    copy.filepath_raw = tex
                    copy.file_format = "PNG"
                    copy.save()
                node.image = bpy.data.images.load(tex)
                node.image.pack()
                print(f"  texture -> {tex}")
                break

    # GLB first (glTF front is +Z, which the exporter maps from Blender's -Y).
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(args.glb_out, f"{args.name}.glb"), export_format="GLB",
                              use_selection=True, export_yup=True)
    print(f"  wrote {os.path.join(args.glb_out, args.name + '.glb')}")

    # Unity keeps the FBX's front on -Z; everything in the game faces +Z, so turn around for the FBX.
    turn = Matrix.Rotation(math.pi, 4, "Z")
    body.data.transform(turn)
    for empty in sockets:
        empty.location = turn @ empty.location
    bpy.ops.object.select_all(action="DESELECT")
    for o in [body, *sockets]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    path = os.path.join(args.model_out, f"{args.name}.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="OFF", use_tspace=False, bake_anim=False,
                             path_mode="STRIP", embed_textures=False)
    print(f"  wrote {path}")
    print("PREPARE_DONE")


if __name__ == "__main__":
    main()
