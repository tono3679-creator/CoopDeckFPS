"""Builds every Lightning Rabbit clip and exports them for Unity (run inside Blender on the rig file).

    blender -b ArtSource/LightningRabbit/Animation/LightningRabbit_Rig.blend -P rabbit_build_clips.py -- \
        [--only Name,Name] [--preview <dir>] [--no-export]

Output (staging until the Unity import, Phase 8):
  ArtSource/LightningRabbit/Export/Unity/A_LightningRabbit_Set.fbx    armature + one take per clip (Unity's +Z)
  ArtSource/LightningRabbit/Export/Unity/A_LightningRabbit_Clips.json clip table (same format as the Fox Gunner's)
  ArtSource/LightningRabbit/Animation/LightningRabbit_Clips.blend     the authored actions, for inspection
The solver reports, per clip, how far the worst hand goal was out of reach (0 cm = both hands exactly on the SMG).
Previews (--preview) show the Lightning SMG on the Weapon bone.
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "fox_anim"))
import bpy  # noqa: E402

from fox_common import bake_rig_transform  # noqa: E402
from fox_motion import new_action, reach_report  # noqa: E402
from rabbit_clips import FPS, setup, stride_report  # noqa: E402
from rabbit_common import ANIM_DIR, STAGING, TURN_FOR_UNITY, script_args  # noqa: E402
from rabbit_motion import RabbitRig, body_scale  # noqa: E402
from rabbit_weapon import smg_surface  # noqa: E402

PREFIX = "A_LightningRabbit_"
SET_FBX = os.path.join(STAGING, "A_LightningRabbit_Set.fbx")
CLIP_JSON = os.path.join(STAGING, "A_LightningRabbit_Clips.json")
CURVE_HZ = 30
HOLDS_RIFLE_TOL = 0.005   # metres; a clip whose left hand is on the rifle must reach it


def parse():
    p = argparse.ArgumentParser()
    p.add_argument("--only")
    p.add_argument("--preview")
    p.add_argument("--no-export", action="store_true")
    return p.parse_args(script_args())


def compress(samples, tol=1e-3):
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
    sc.render.fps, sc.render.fps_base = FPS, 1.0
    rig = RabbitRig(arm)
    rig.use_clearance(next(m for m in meshes if m.vertex_groups), *smg_surface())
    scale = body_scale(arm)
    clips = setup(rig, scale)
    print(f"  body scale {scale:.3f} (rabbit shoulders / fox shoulders)")
    for line in stride_report(clips):
        print("  " + line)
    only = set(args.only.split(",")) if args.only else None
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)

    table, misses = [], {}
    for spec in clips:
        if only and spec.name not in only:
            continue
        name = PREFIX + spec.name
        act = new_action(arm, name)
        n = spec.frames
        ik, cast = [], []
        worst, worst_on_rifle, pulled, pushed = 0.0, 0.0, 0.0, 0.0
        for f in range(n + 1):
            t = spec.seconds * f / n
            ctl = spec.fn(t, f / n)
            D = rig.solve(ctl)
            pulled, pushed = max(pulled, rig.assist), max(pushed, rig.pushed)
            rep = reach_report(rig, D, ctl)
            worst = max([worst] + list(rep.values()))
            on_rifle = ([rep.get("RightHand", 0.0)] if getattr(ctl, "right_on_gun", 1.0) > 0.99 else []) + \
                ([rep.get("LeftHand", 0.0)] if ctl.left_ik > 0.99 else [])
            worst_on_rifle = max([worst_on_rifle] + on_rifle)
            rig.key(D, f)
            ik.append((round(f / FPS, 4), round(float(ctl.left_ik), 4)))
            cast.append((round(f / FPS, 4), round(float(ctl.cast), 4)))
        act.frame_range = (0, n)
        step = max(1, round(FPS / CURVE_HZ))
        table.append({
            "name": name, "take": spec.name, "seconds": round(n / FPS, 4), "frames": n, "fps": FPS,
            "loop": spec.loop, "layer": spec.layer,
            "speed": round(spec.speed * spec.seconds / (n / FPS), 4), "direction": list(spec.direction),
            "cycle": round(n / FPS, 4) if spec.cycle else 0.0, "notes": spec.notes,
            "curves": [curve_entry("LeftHandIK", ik, step), curve_entry("CastHand", cast, step)],
        })
        misses[spec.name] = worst_on_rifle
        flag = "" if worst_on_rifle <= HOLDS_RIFLE_TOL else "  <-- hands off the SMG"
        print(f"  {name}: {n + 1} frames, loop={spec.loop}, layer={spec.layer}, worst reach miss "
              f"{worst * 100:.1f} cm (on the SMG {worst_on_rifle * 100:.1f} cm, SMG pulled in up to "
              f"{pulled * 100:.1f} cm, pushed out of the body up to {pushed * 100:.1f} cm){flag}")

    arm.animation_data.action = None
    os.makedirs(ANIM_DIR, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ANIM_DIR, "LightningRabbit_Clips.blend"), copy=True)
    bad = [k for k, v in misses.items() if v > HOLDS_RIFLE_TOL]
    print(f"  clips with a hand off the rifle: {bad if bad else 'none'}")

    if args.preview:
        preview(arm, table, args.preview)

    if args.no_export:
        print("BUILD_DONE (no export)")
        return

    for m in meshes:
        bpy.data.objects.remove(m, do_unlink=True)
    for o in [o for o in bpy.data.objects if o.type != "ARMATURE"]:
        bpy.data.objects.remove(o, do_unlink=True)
    bake_rig_transform(arm, [], TURN_FOR_UNITY)
    sc.frame_start = 0
    sc.frame_end = max(e["frames"] for e in table)
    os.makedirs(STAGING, exist_ok=True)
    export_anim_fbx(SET_FBX, arm)
    with open(CLIP_JSON, "w", encoding="utf-8") as fh:
        json.dump({"character": "LightningRabbit", "fps": FPS, "clips": table}, fh, indent=1)
    print(f"  wrote {CLIP_JSON} ({len(table)} clips)")
    print("BUILD_DONE")


def preview(arm, table, out_dir):
    from fox_render import VIEWS, contact_sheet
    from rabbit_weapon import SMGRenderer
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    r = SMGRenderer(arm, res=(360, 440))
    for entry in table:
        arm.animation_data.action = bpy.data.actions[entry["name"]]
        paths = []
        count = 8
        for k in range(count):
            f = round(entry["frames"] * (k / count if entry["loop"] else k / (count - 1)))
            sc.frame_set(f)
            r.attach_rifle()
            for view in ("right", "q34"):
                eye, target, scale = VIEWS[view]
                p = os.path.join(out_dir, f"_p_{view}_{k}.png")
                r.shot(p, eye, target, scale)
                paths.append(p)
        ordered = [p for view in ("right", "q34") for p in paths if f"_p_{view}_" in p]
        contact_sheet(ordered, os.path.join(out_dir, entry["name"] + ".png"), count)
    arm.animation_data.action = None


def export_anim_fbx(path, arm):
    # A stand-in named like the model's first mesh keeps "Armature" a child of the file root, exactly like the
    # model FBX, so the copied Humanoid avatar's hierarchy matches (see fox_anim/build_clips.py).
    stand_in = bpy.data.objects.new("LightningRabbit_Body_LOD0", None)
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
