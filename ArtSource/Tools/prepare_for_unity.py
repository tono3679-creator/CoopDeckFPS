"""Turn a Meshy rigged character into Unity-ready files (run inside Blender, headless).

    blender -b --factory-startup -P prepare_for_unity.py -- \
        --name FoxGunner --model Character_output.glb \
        --anim Walk=Animation_Walking_withSkin.glb --anim Run=Animation_Running_withSkin.glb \
        --model-out <dir> --anim-out <dir> --texture-out <dir> --glb-out <dir> \
        [--texture generate_base_color.png]

Model (<name>.fbx / <name>.glb, texture T_<name>_BaseColor.png):
  - drops meshes that are not skinned (Blender's glTF importer adds an "Icosphere" bone shape)
  - --texture: use this image as the base color instead of the one inside the GLB (Meshy's
    rigged GLB carries a JPEG copy; the generate task's PNG is the same image, uncompressed)
  - welds vertices split at UV seams and replaces the faceted normals with smooth ones,
    so cel shading and the outline pass stay clean
  - moves the rig so the hips stand over the origin (pivot between the feet, matching the
    CharacterController), applies the 0.01 armature scale (1 unit = 1 m) and removes the
    one-frame bind action
  - turns the FBX 180 degrees so the character faces Unity's +Z (the GLB keeps glTF's +Z front)
Animations (A_<name>_<Clip>.fbx): armature + one baked clip, same clean-up as the model
(location keys are rescaled with the armature so the motion keeps its size).
"""
import argparse
import os
import sys

import bpy
from mathutils import Matrix, Vector

FPS = 30  # Meshy animations are sampled at 30 fps


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--name", required=True)
    p.add_argument("--model", required=True)
    p.add_argument("--anim", action="append", default=[], help="Clip=path.glb")
    p.add_argument("--model-out", required=True)
    p.add_argument("--anim-out", required=True)
    p.add_argument("--texture-out", required=True)
    p.add_argument("--glb-out", required=True)
    p.add_argument("--texture", help="PNG to use as base color instead of the GLB's own image")
    return p.parse_args(argv)


def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.render.fps_base = 1.0


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    skinned = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups
               and any(m.type == "ARMATURE" for m in o.modifiers)]
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o not in skinned]:
        print(f"  removed stray mesh '{o.name}' ({len(o.data.vertices)} verts)")
        bpy.data.objects.remove(o, do_unlink=True)
    return arm, skinned


def iter_fcurves(action):
    if hasattr(action, "layers") and action.layers:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    yield from bag.fcurves
    elif hasattr(action, "fcurves"):
        yield from action.fcurves


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def bake_rig_transform(arm, meshes, world):
    """Applies a world matrix to the armature and its meshes and bakes it into rest pose and vertices.
    Pose-bone keys are relative to the rest bones, so rotations and offsets carry the motion along;
    only a scale changes the size of location keys, which the caller handles."""
    for m in meshes:
        m_world = m.matrix_world.copy()
        m.parent = None
        m.matrix_world = world @ m_world
    arm.matrix_world = world @ arm.matrix_world
    for obj in [arm, *meshes]:
        select_only(obj)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for m in meshes:
        m.parent = arm
        m.matrix_parent_inverse = Matrix.Identity(4)
        for mod in m.modifiers:
            if mod.type == "ARMATURE":
                mod.object = arm


def normalize_rig(arm, meshes):
    """Hips over the origin, armature scale applied (1 unit = 1 m); returns the applied offset."""
    root = arm.data.bones.get("Hips") or next(b for b in arm.data.bones if b.parent is None)
    hips = arm.matrix_world @ root.head_local
    offset = Vector((-hips.x, -hips.y, 0.0))
    scale = arm.scale.x

    # Pose-bone locations are in armature units; shrink them with the armature.
    if abs(scale - 1.0) > 1e-6:
        for action in bpy.data.actions:
            for fc in iter_fcurves(action):
                if fc.data_path.endswith(".location"):
                    for k in fc.keyframe_points:
                        k.co.y *= scale
                        k.handle_left.y *= scale
                        k.handle_right.y *= scale
                    fc.update()

    bake_rig_transform(arm, meshes, Matrix.Translation(offset))
    return offset, scale


# Meshy characters face Blender's -Y. Unity reads the FBX axis header and keeps that facing, which
# lands on Unity's -Z; the player and every Humanoid clip face +Z, so the rig is turned around for FBX.
TURN_FOR_UNITY = Matrix.Rotation(3.141592653589793, 4, "Z")


def clean_mesh(mesh_obj):
    select_only(mesh_obj)
    before = len(mesh_obj.data.vertices)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth()
    print(f"  welded {before} -> {len(mesh_obj.data.vertices)} vertices, smooth normals")


def save_base_color(mesh_obj, path, override=None):
    """Writes the base color PNG and points the material at it (the GLB export embeds it)."""
    for slot in mesh_obj.material_slots:
        if not slot.material or not slot.material.node_tree:
            continue
        for node in slot.material.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                img = node.image
                if override:
                    with open(override, "rb") as src, open(path, "wb") as dst:
                        dst.write(src.read())
                else:
                    data = img.packed_file.data if img.packed_file else None
                    if data and data[:8] == b"\x89PNG\r\n\x1a\n":
                        with open(path, "wb") as f:
                            f.write(data)  # the original PNG bytes, no re-encode
                    else:
                        copy = img.copy()
                        copy.filepath_raw = path
                        copy.file_format = "PNG"
                        copy.save()
                node.image = bpy.data.images.load(path)
                node.image.pack()
                print(f"  texture {node.image.size[0]}x{node.image.size[1]} -> {path}")
                return
    raise RuntimeError("no base color texture found")


def export_fbx(path, objects, bake_anim):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", use_space_transform=True, bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="OFF", use_tspace=False,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        armature_nodetype="NULL", use_armature_deform_only=False,
        bake_anim=bake_anim, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode="STRIP", embed_textures=False)
    print(f"  wrote {path}")


def prepare_model(args):
    fresh_scene()
    arm, meshes = import_glb(args.model)
    if len(meshes) != 1:
        raise RuntimeError(f"expected one skinned mesh, got {len(meshes)}")
    body = meshes[0]
    arm.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)

    clean_mesh(body)
    offset, scale = normalize_rig(arm, meshes)
    print(f"  moved by {tuple(round(v, 3) for v in offset)}, applied armature scale {scale}")

    arm.name = "Armature"
    arm.data.name = f"{args.name}_Rig"
    body.name = f"{args.name}_Body"
    body.data.name = f"{args.name}_Body"
    if body.material_slots and body.material_slots[0].material:
        body.material_slots[0].material.name = f"M_{args.name}"

    tex = os.path.join(args.texture_out, f"T_{args.name}_BaseColor.png")
    save_base_color(body, tex, args.texture)

    # GLB first: glTF's own convention is front = +Z, which the exporter maps from Blender's -Y.
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(args.glb_out, f"{args.name}.glb"),
                              export_format="GLB", use_selection=True, export_animations=False,
                              export_skins=True, export_yup=True)
    print(f"  wrote {os.path.join(args.glb_out, args.name + '.glb')}")

    bake_rig_transform(arm, [body], TURN_FOR_UNITY)
    export_fbx(os.path.join(args.model_out, f"{args.name}.fbx"), [arm, body], bake_anim=False)


def prepare_anim(args, clip, path):
    fresh_scene()
    arm, meshes = import_glb(path)
    for m in meshes:
        bpy.data.objects.remove(m, do_unlink=True)
    action = arm.animation_data.action if arm.animation_data else None
    if action is None:
        raise RuntimeError(f"{path}: no action")
    start, end = action.frame_range
    sc = bpy.context.scene

    probe = arm.pose.bones.get("LeftFoot") or arm.pose.bones[0]
    mid = int(round((start + end) / 2))
    sc.frame_set(mid)
    before = arm.matrix_world @ probe.head

    offset, scale = normalize_rig(arm, [])
    bake_rig_transform(arm, [], TURN_FOR_UNITY)

    # The probe foot must land exactly where the moved and turned original would be.
    sc.frame_set(mid)
    after = arm.matrix_world @ probe.head
    error = (after - TURN_FOR_UNITY @ (before + offset)).length
    print(f"  {clip}: frames {start:.1f}-{end:.1f}, foot error after rescale and turn {error * 1000:.2f} mm")
    if error > 0.002:
        raise RuntimeError(f"{clip}: motion changed by {error:.4f} m while normalizing")

    action.name = f"A_{args.name}_{clip}"
    sc.frame_start = int(round(start))
    sc.frame_end = int(round(end))
    export_fbx(os.path.join(args.anim_out, f"A_{args.name}_{clip}.fbx"), [arm], bake_anim=True)


def main():
    args = parse_args()
    for d in (args.model_out, args.anim_out, args.texture_out, args.glb_out):
        os.makedirs(d, exist_ok=True)
    print("[model]")
    prepare_model(args)
    for spec in args.anim:
        clip, path = spec.split("=", 1)
        print(f"[anim {clip}]")
        prepare_anim(args, clip, path)
    print("PREPARE_DONE")


if __name__ == "__main__":
    main()
