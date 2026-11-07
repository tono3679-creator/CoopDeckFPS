"""Builds every Fox Gunner clip and exports them for Unity.

    blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P ArtSource/Tools/fox_anim/build_clips.py -- \
        [--only Name,Name] [--preview <dir>] [--no-export]

Output:
  Assets/_Game/Animations/FoxGunner/A_FoxGunner_Set.fbx    armature + one take per clip (turned to Unity's +Z)
  Assets/_Game/Animations/FoxGunner/A_FoxGunner_Clips.json clip table: length, loop, layer, locomotion speed and
                                                           direction, LeftHandIK / CastHand curves (30 Hz)
  ArtSource/FoxGunner/Animation/FoxGunner_Clips.blend      the authored actions, for inspection in Blender
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402

import fox_clips  # noqa: E402
from fox_common import ANIM_DIR, ANIM_FBX_DIR, TURN_FOR_UNITY, bake_rig_transform, export_fbx, script_args  # noqa: E402
from fox_motion import Rig, new_action, reach_report  # noqa: E402

PREFIX = "A_FoxGunner_"
SET_FBX = os.path.join(ANIM_FBX_DIR, "A_FoxGunner_Set.fbx")
CLIP_JSON = os.path.join(ANIM_FBX_DIR, "A_FoxGunner_Clips.json")
CURVE_HZ = 30


def parse():
    p = argparse.ArgumentParser()
    p.add_argument("--only")
    p.add_argument("--preview")
    p.add_argument("--no-export", action="store_true")
    return p.parse_args(script_args())


def compress(samples, tol=1e-3):
    """Drops samples that lie on the line between their neighbours."""
    if len(samples) <= 2:
        return samples
    out = [samples[0]]
    for i in range(1, len(samples) - 1):
        (t0, v0), (t1, v1), (t2, v2) = out[-1], samples[i], samples[i + 1]
        guess = v0 + (v2 - v0) * (t1 - t0) / max(1e-9, t2 - t0)
        if abs(guess - v1) > tol:
            out.append(samples[i])
    out.append(samples[-1])
    return out


def curve_entry(name, samples, step):
    """{"name", "times", "values"}: Unity's JsonUtility reads this shape (no nested arrays)."""
    picked = samples[::step]
    if (len(samples) - 1) % step:
        picked.append(samples[-1])
    keys = compress(picked)
    return {"name": name, "times": [k[0] for k in keys], "values": [k[1] for k in keys]}


def main():
    args = parse()
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    sc = bpy.context.scene
    sc.render.fps = fox_clips.FPS
    sc.render.fps_base = 1.0
    rig = Rig(arm)
    fox_clips.RIG[0] = rig
    only = set(args.only.split(",")) if args.only else None
    table = []
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)

    for spec in fox_clips.CLIPS:
        if only and spec.name not in only:
            continue
        name = PREFIX + spec.name
        act = new_action(arm, name)
        n = spec.frames
        ik, cast = [], []
        worst = 0.0
        for f in range(n + 1):
            t = spec.seconds * f / n
            u = f / n
            ctl = spec.fn(t, u)
            D = rig.solve(ctl)
            worst = max([worst] + list(reach_report(rig, D, ctl).values()))
            rig.key(D, f)
            ik.append((round(f / fox_clips.FPS, 4), round(float(ctl.left_ik), 4)))  # clip time in Unity
            cast.append((round(f / fox_clips.FPS, 4), round(float(ctl.cast), 4)))
        act.frame_range = (0, n)
        step = max(1, round(fox_clips.FPS / CURVE_HZ))
        entry = {
            "name": name, "take": spec.name, "seconds": round(n / fox_clips.FPS, 4), "frames": n, "fps": fox_clips.FPS,
            "loop": spec.loop, "layer": spec.layer, "speed": round(spec.speed * spec.seconds / (n / fox_clips.FPS), 4), "direction": list(spec.direction),
            "cycle": round(n / fox_clips.FPS, 4) if spec.cycle else 0.0, "notes": spec.notes,
            "curves": [curve_entry("LeftHandIK", ik, step), curve_entry("CastHand", cast, step)],
        }
        table.append(entry)
        print(f"  {name}: {n + 1} frames, loop={spec.loop}, layer={spec.layer}, worst reach miss {worst * 100:.1f} cm")

    arm.animation_data.action = None
    os.makedirs(ANIM_DIR, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups next to the outputs
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ANIM_DIR, "FoxGunner_Clips.blend"), copy=True)

    if args.preview:
        from fox_render import VIEWS, Renderer, contact_sheet
        r = Renderer(arm, res=(360, 440))
        for entry in table:
            act = bpy.data.actions[entry["name"]]
            arm.animation_data.action = act
            paths = []
            count = 8
            for k in range(count):
                f = round(entry["frames"] * (k / count if entry["loop"] else k / (count - 1)))
                sc.frame_set(f)
                r.attach_rifle()
                for view in ("right", "q34"):
                    eye, target, scale = VIEWS[view]
                    p = os.path.join(args.preview, f"_p_{view}_{k}.png")
                    r.shot(p, eye, target, scale)
                    paths.append(p)
            ordered = [p for view in ("right", "q34") for p in paths if f"_p_{view}_" in p]
            contact_sheet(ordered, os.path.join(args.preview, entry["name"] + ".png"), count)
        arm.animation_data.action = None

    if args.no_export:
        print("BUILD_DONE (no export)")
        return

    for m in meshes:
        bpy.data.objects.remove(m, do_unlink=True)
    for o in [o for o in bpy.data.objects if o.type not in ("ARMATURE",)]:
        bpy.data.objects.remove(o, do_unlink=True)
    bake_rig_transform(arm, [], TURN_FOR_UNITY)
    sc.frame_start = 0
    sc.frame_end = max(e["frames"] for e in table)
    os.makedirs(ANIM_FBX_DIR, exist_ok=True)
    export_anim_fbx(SET_FBX, arm)
    with open(CLIP_JSON, "w", encoding="utf-8") as fh:
        json.dump({"character": "FoxGunner", "fps": fox_clips.FPS, "clips": table}, fh, indent=1)
    print(f"  wrote {CLIP_JSON} ({len(table)} clips)")
    print("BUILD_DONE")


def export_anim_fbx(path, arm):
    # An empty named like the model's mesh object keeps two top-level nodes, so Unity keeps "Armature" as a child
    # of the file root exactly like FoxGunner.fbx (a lone armature would become the root and the copied avatar's
    # hierarchy would not match).
    stand_in = bpy.data.objects.new("FoxGunner_Body", None)
    bpy.context.scene.collection.objects.link(stand_in)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    stand_in.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", use_space_transform=True, bake_space_transform=False,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        armature_nodetype="NULL", use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode="STRIP")
    print(f"  wrote {path}")


main()
