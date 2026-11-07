"""Shared helpers for the Fox Gunner animation pipeline (run inside Blender 5.x, headless).

Coordinate conventions (Blender, before the FBX turn): the fox faces -Y, its right is -X, up is +Z,
1 unit = 1 m, origin on the ground under the hips. The FBX export turns the rig 180 degrees about Z so
Unity sees +Z forward (same as prepare_for_unity.py).
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
FOX_SRC = os.path.join(REPO, "ArtSource", "FoxGunner")
EXPORT_GLB = os.path.join(FOX_SRC, "Export", "FoxGunner_HQ.glb")  # refine_character.py output (even 4K texture)
ANIM_DIR = os.path.join(FOX_SRC, "Animation")
RIG_BLEND = os.path.join(ANIM_DIR, "FoxGunner_AnimRig.blend")
RIFLE_GLB = os.path.join(REPO, "ArtSource", "FireRifle", "Export", "FireRifle.glb")
MODEL_FBX = os.path.join(REPO, "Assets", "_Game", "Art", "Characters", "FoxGunner", "Models", "FoxGunner.fbx")
ANIM_FBX_DIR = os.path.join(REPO, "Assets", "_Game", "Animations", "FoxGunner")

FORWARD = Vector((0.0, -1.0, 0.0))
RIGHT = Vector((-1.0, 0.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))

# Meshy turns the character for glTF; Unity reads the FBX axis header, so the rig itself is turned for FBX.
TURN_FOR_UNITY = Matrix.Rotation(math.pi, 4, "Z")


def script_args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.render.fps_base = 1.0
    return sc


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def import_fox():
    bpy.ops.import_scene.gltf(filepath=EXPORT_GLB)
    for o in list(bpy.data.objects):
        if o.type == "MESH" and not o.vertex_groups:
            bpy.data.objects.remove(o, do_unlink=True)  # the importer's bone-shape icosphere
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    arm.animation_data_clear()
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    return arm, body


def smoothstep(e0, e1, x):
    if e0 == e1:
        return 1.0 if x >= e1 else 0.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def export_fbx(path, objects, bake_anim, all_actions=False):
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
        bake_anim_use_all_actions=all_actions, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode="STRIP", embed_textures=False)
    print(f"  wrote {path}")


def bake_rig_transform(arm, meshes, world):
    """Applies a world matrix to the armature (and meshes) and bakes it into rest pose and vertices.
    Pose keys are bone-local, so the motion turns with the rig."""
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
