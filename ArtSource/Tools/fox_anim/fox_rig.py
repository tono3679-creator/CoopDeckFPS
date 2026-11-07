"""Adds the secondary bones the Meshy auto-rig lacks and writes the animation rig + the Unity model.

    blender -b --factory-startup -P fox_rig.py -- [--no-fbx] [--debug <dir>]

Input : ArtSource/FoxGunner/Export/FoxGunner.glb (prepare_for_unity.py output; the 24 Meshy bones)
Output: ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend  (authoring rig, fox faces -Y)
        Assets/_Game/Art/Characters/FoxGunner/Models/FoxGunner.fbx (turned to Unity's +Z, unless --no-fbx)

New bones (the 24 original bones and their rest pose are unchanged, so the Humanoid map still fits):
  Tail01-04            chain on Hips along the tail's centre line (procedural sway in Unity)
  Ear_L / Ear_R        on Head, base to tip (procedural twitch/flatten in Unity)
  <Side>Fingers1/2     on <Side>Hand, the four fingers curl together (fingerless gloves, chunky fingers)
  <Side>Thumb          on <Side>Hand
  Weapon               on Spine (UpperChest): the rifle's grip frame (bone Y = muzzle direction,
                       bone Z = rifle up). Clips animate it; hand IK in Unity follows it.
  IK_Hand_L / _R       on Weapon, rest = the hand bones: keyed to the authored hands (Animation Rigging targets)
  IK_Elbow_L / _R      on Spine: elbow hints, keyed behind the authored elbows
Skin weights: tail/ear/finger vertices get smooth gradients to the new bones; the root of each part
blends into the original weights so the seams do not tear.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from fox_common import (ANIM_DIR, MODEL_FBX, RIG_BLEND, TURN_FOR_UNITY, bake_rig_transform, export_fbx,  # noqa: E402
                        fresh_scene, import_fox, script_args, select_only, smoothstep)
from fox_segment import (dominant_groups, find_ears, find_hand_parts, find_tail, neighbours,  # noqa: E402
                         polyline_lengths, project_param, resample, tail_centerline, vertex_colours)

TAIL_GUESS = [(0, 0.10, 0.74), (0, 0.25, 0.66), (0, 0.40, 0.52), (0, 0.50, 0.35), (0, 0.52, 0.18)]
TAIL_BONES = 4
KNUCKLE = 0.058          # glove edge, metres from the wrist along the hand
FINGER_SPLIT = 0.045     # proximal finger segment length
WEAPON_REST_GRIP = Vector((-0.12, -0.30, 0.95))  # rest placement only; clips move the bone
ELBOW_HINT = 0.15        # elbow hint distance behind the elbow (elbows point back in the T-pose bend)


def parse():
    p = argparse.ArgumentParser()
    p.add_argument("--no-fbx", action="store_true")
    p.add_argument("--debug")
    return p.parse_args(script_args())


def smooth_polyline(pts, passes=3):
    pts = [p.copy() for p in pts]
    for _ in range(passes):
        pts = [pts[0]] + [(pts[i - 1] + pts[i] * 2 + pts[i + 1]) / 4 for i in range(1, len(pts) - 1)] + [pts[-1]]
    return pts


def build():
    args = parse()
    fresh_scene()
    arm, body = import_fox()
    me = body.data
    col = vertex_colours(body)
    adj = neighbours(body)
    dom = dominant_groups(body)

    # ---------------------------------------------------------------- regions
    tail = find_tail(body, col, adj, dom)
    center = smooth_polyline(tail_centerline(body, tail, TAIL_GUESS, iterations=1, bins=6))
    joints = resample(center, TAIL_BONES + 1)
    ears = {+1: find_ears(body, dom, +1), -1: find_ears(body, dom, -1)}
    hands = {}
    for side in ("Left", "Right"):
        fingers, thumb, frame = find_hand_parts(body, arm, dom, side, knuckle=KNUCKLE, thumb_front=0.03)
        thumb = {i: v for i, v in thumb.items() if v[2] < -0.012}  # the index root lies in the palm plane
        hands[side] = (fingers, thumb, frame)
    print(f"  regions: tail {len(tail)}, ears {len(ears[1])}/{len(ears[-1])}, "
          f"fingers {len(hands['Left'][0])}/{len(hands['Right'][0])}, thumbs {len(hands['Left'][1])}/{len(hands['Right'][1])}")
    print("  tail joints", [tuple(round(c, 3) for c in p) for p in joints])

    # ---------------------------------------------------------------- bones
    ear_axes = {}
    for side, verts in ears.items():
        ps = [me.vertices[i].co for i in verts]
        zmin = min(p.z for p in ps)
        tip = max(ps, key=lambda p: p.z)
        base_ps = [p for p in ps if p.z < zmin + 0.035]
        base = sum(base_ps, Vector()) / len(base_ps)
        ear_axes[side] = (base, tip.copy())

    finger_bones = {}
    for side, (fingers, thumb, (wrist, a, front, up)) in hands.items():
        near = [v for v in fingers.values() if v[0] < KNUCKLE + 0.02]
        u0 = sum(v[1] for v in near) / len(near)
        w0 = sum(v[2] for v in near) / len(near)
        smax = max(v[0] for v in fingers.values())
        f_head = wrist + a * KNUCKLE + front * u0 + up * w0
        f_mid = wrist + a * (KNUCKLE + FINGER_SPLIT) + front * u0 + up * w0
        f_tip = wrist + a * smax + front * u0 + up * w0
        tps = [me.vertices[i].co for i in thumb]
        t_base = wrist + a * 0.005 + front * 0.03 + up * -0.012
        t_tip = max(tps, key=lambda p: (p - t_base).length)
        finger_bones[side] = (f_head, f_mid, f_tip, t_base, t_tip.copy(), up)

    select_only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones

    def add(name, parent, head, tail, up_hint):
        b = eb.new(name)
        b.head, b.tail = head, tail
        b.align_roll(up_hint)
        b.parent = eb[parent]
        b.use_connect = False
        b.use_deform = True
        return b

    prev = "Hips"
    for k in range(TAIL_BONES):
        add(f"Tail{k + 1:02d}", prev, joints[k], joints[k + 1], Vector((0, 0, 1)))
        if k > 0:
            eb[f"Tail{k + 1:02d}"].use_connect = True
        prev = f"Tail{k + 1:02d}"
    for side, name in ((+1, "Ear_L"), (-1, "Ear_R")):
        base, tip = ear_axes[side]
        add(name, "Head", base, tip, Vector((0, -1, 0)))
    for side, (f_head, f_mid, f_tip, t_base, t_tip, up) in finger_bones.items():
        add(f"{side}Fingers1", f"{side}Hand", f_head, f_mid, up)
        add(f"{side}Fingers2", f"{side}Fingers1", f_mid, f_tip, up)
        eb[f"{side}Fingers2"].use_connect = True
        add(f"{side}Thumb", f"{side}Hand", t_base, t_tip, up)
    w = add("Weapon", "Spine", WEAPON_REST_GRIP, WEAPON_REST_GRIP + Vector((0, -0.12, 0)), Vector((0, 0, 1)))
    w.use_deform = False
    # IK goals for Unity's Animation Rigging: copies of the hands riding the rifle, and elbow hints on the chest.
    # Clips key them to the authored hands, so the IK reproduces the Blender arms exactly (no Humanoid drift)
    # and keeps both hands on the rifle while clips blend.
    for side, suffix in (("Left", "L"), ("Right", "R")):
        hand = eb[side + "Hand"]
        ik = eb.new("IK_Hand_" + suffix)
        ik.head, ik.tail, ik.roll = hand.head.copy(), hand.tail.copy(), hand.roll
        ik.parent = eb["Weapon"]
        ik.use_deform = False
        elbow = eb[side + "ForeArm"].head + Vector((0, ELBOW_HINT, 0))
        hint = add("IK_Elbow_" + suffix, "Spine", elbow, elbow + Vector((0, 0, 0.05)), Vector((0, -1, 0)))
        hint.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")

    # ---------------------------------------------------------------- weights
    groups = {g.name: g for g in body.vertex_groups}

    def group(name):
        if name not in groups:
            groups[name] = body.vertex_groups.new(name=name)
        return groups[name]

    new_weights = {}  # vertex -> {group: weight} for the new bones, plus 'keep' = share of the original weights

    lengths = polyline_lengths(joints)
    total = lengths[-1]
    for i in tail:
        s = project_param(joints, lengths, me.vertices[i].co) / total
        keep = 1.0 - smoothstep(0.0, 0.18, s)
        bw = {}
        for k in range(TAIL_BONES):
            c = (k + 0.5) / TAIL_BONES
            x = max(0.0, 1.0 - abs(s - c) * TAIL_BONES)
            if k == 0 and s < c:
                x = 1.0
            if k == TAIL_BONES - 1 and s > c:
                x = 1.0
            if x > 0:
                bw[f"Tail{k + 1:02d}"] = x
        norm = sum(bw.values())
        # The root blends into the pelvis, not into Meshy's original thigh weights: thigh-weighted tail vertices
        # were dragged by every stride and the tail looked stretched while running.
        extra = {n: (1 - keep) * v / norm for n, v in bw.items()}
        if keep > 0:
            extra["Hips"] = keep
        new_weights[i] = (0.0, extra)

    # The ring of body vertices around the tail root goes half to the pelvis too, so the seam does not tear when the
    # legs swing.
    root_ring = [me.vertices[i].co for i in tail if project_param(joints, lengths, me.vertices[i].co) / total < 0.12]
    for v in me.vertices:
        if v.index in tail or dom[v.index] not in ("LeftUpLeg", "RightUpLeg"):
            continue
        if v.co.y < 0.0 or v.co.z < 0.55:
            continue
        near = min(((v.co - p).length for p in root_ring), default=1.0)
        if near < 0.05:
            share = 0.5 * (1.0 - near / 0.05)
            new_weights[v.index] = (1.0 - share, {"Hips": share})

    for side, name in ((+1, "Ear_L"), (-1, "Ear_R")):
        base, tip = ear_axes[side]
        axis = tip - base
        for i in ears[side]:
            t = (me.vertices[i].co - base).dot(axis) / axis.length_squared
            we = smoothstep(-0.05, 0.3, t)
            new_weights[i] = (1 - we, {name: we})

    for side, (fingers, thumb, (wrist, a, front, up)) in hands.items():
        for i, (s, u, w_) in fingers.items():
            w2 = smoothstep(KNUCKLE + FINGER_SPLIT - 0.012, KNUCKLE + FINGER_SPLIT + 0.006, s)
            w1 = smoothstep(KNUCKLE - 0.014, KNUCKLE + 0.006, s) * (1 - w2)
            new_weights[i] = (1 - w1 - w2, {f"{side}Fingers1": w1, f"{side}Fingers2": w2})
        t_base, t_tip = finger_bones[side][3], finger_bones[side][4]
        axis = t_tip - t_base
        for i in thumb:
            t = (me.vertices[i].co - t_base).dot(axis) / axis.length_squared
            wt = smoothstep(0.05, 0.4, t)
            new_weights[i] = (1 - wt, {f"{side}Thumb": wt})

    names = {g.index: g.name for g in body.vertex_groups}
    for i, (keep, extra) in new_weights.items():
        v = me.vertices[i]
        old = {names[g.group]: g.weight for g in v.groups}
        old_sum = sum(old.values()) or 1.0
        mix = {n: keep * wt / old_sum for n, wt in old.items()}
        for n, wt in extra.items():
            mix[n] = mix.get(n, 0.0) + wt
        top = sorted(mix.items(), key=lambda x: -x[1])[:4]
        norm = sum(wt for _, wt in top) or 1.0
        for n in old:
            groups[n].remove([i])
        for n, wt in top:
            if wt > 1e-4:
                group(n).add([i], wt / norm, "REPLACE")
    print(f"  reweighted {len(new_weights)} vertices; bones now {len(arm.data.bones)}")

    add_first_person_grip(arm)

    if args.debug:
        debug_poses(arm, body, args.debug)

    os.makedirs(ANIM_DIR, exist_ok=True)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups next to the outputs
    bpy.ops.wm.save_as_mainfile(filepath=RIG_BLEND)
    print(f"  saved {RIG_BLEND}")

    if not args.no_fbx:
        bake_rig_transform(arm, [body], TURN_FOR_UNITY)
        export_fbx(MODEL_FBX, [arm, body], bake_anim=False)
    print("RIG_DONE")


# First-person left-hand grip on the handguard (rifle space: right, forward, up). The body's own left hand holds the
# magazine well (its arms are short); in first person the camera sits close enough for a classic handguard hold.
FP_LEFT_PALM = Vector((-0.064, 0.400, 0.092))
FP_LEFT_ALONG = Vector((0.15, 0.40, -0.90))     # wrist -> knuckles: down the left side, fingers wrap under
FP_LEFT_PALM_NORMAL = Vector((0.92, 0.0, 0.40))  # the palm presses on the left side (thumb over the top)


def add_first_person_grip(arm):
    """FP_Grip_L on the Weapon bone: rest = the first-person left-hand pose relative to the rifle in the shared stance.
    Clips never key it, so in Unity it rides the rifle and FirstPersonArms reads it as the left-hand goal."""
    from fox_motion import Rig
    from fox_stance import Stance, base_controls, rs, rs_dir
    rig = Rig(arm)
    st = Stance()
    weapon = base_controls(st).weapon
    along = rs_dir(weapon, FP_LEFT_ALONG).normalized()
    normal = rs_dir(weapon, FP_LEFT_PALM_NORMAL)
    normal = (normal - along * normal.dot(along)).normalized()
    thumb = normal.cross(along)  # left hand: back of the hand = -palm normal = -(along x thumb)
    rest = rig.hand_rest["Left"]
    hand_m = rig.hand_world_frame("Left", along, thumb)
    r_end = hand_m @ rest["m"].transposed()
    wrist = rs(weapon, FP_LEFT_PALM) - r_end @ rest["palm_off"]
    pose = Matrix.Translation(wrist) @ r_end.to_4x4() @ Matrix.Translation(-rig.head["LeftHand"]) @ rig.rest["LeftHand"]
    local = rig.rest["Weapon"] @ weapon.inverted() @ pose
    select_only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones.new("FP_Grip_L")
    eb.length = 0.05
    eb.matrix = local
    eb.parent = arm.data.edit_bones["Weapon"]
    eb.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"  FP_Grip_L at rifle-space palm {tuple(FP_LEFT_PALM)}")


def debug_poses(arm, body, out):
    """Bends the new bones and renders, to check the weights deform cleanly."""
    import math
    from mathutils import Euler
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    cd = bpy.data.cameras.new("dbgcam")
    cam = bpy.data.objects.new("dbgcam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    cd.type = "ORTHO"

    def shot(name, loc, target, scale, rx=700, ry=700):
        sc.render.resolution_x, sc.render.resolution_y = rx, ry
        cam.location = Vector(loc)
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        cd.ortho_scale = scale
        sc.render.filepath = os.path.join(out, name + ".png")
        bpy.ops.render.render(write_still=True)

    pose = arm.pose.bones
    for pb in pose:
        pb.rotation_mode = "XYZ"
    for k in range(1, TAIL_BONES + 1):
        pose[f"Tail{k:02d}"].rotation_euler = Euler((math.radians(-14), 0, math.radians(16)))
    pose["Ear_L"].rotation_euler = Euler((math.radians(-45), 0, 0))
    pose["Ear_R"].rotation_euler = Euler((math.radians(30), 0, math.radians(20)))
    for side in ("Left", "Right"):
        pose[f"{side}Fingers1"].rotation_euler = Euler((math.radians(70), 0, 0))
        pose[f"{side}Fingers2"].rotation_euler = Euler((math.radians(60), 0, 0))
        pose[f"{side}Thumb"].rotation_euler = Euler((math.radians(40), 0, 0))
    bpy.context.view_layer.update()
    shot("rig_side", (3, 0.15, 0.8), (0, 0.15, 0.8), 1.9, 700, 900)
    shot("rig_back", (0, 3, 0.8), (0, 0, 0.8), 1.9, 700, 900)
    shot("rig_head", (0.0, 2, 1.45), (0, 0, 1.40), 0.7)
    shot("rig_handR", (-0.65, -0.6, 0.8), (-0.65, -0.05, 1.0), 0.35)
    shot("rig_handL", (0.62, -0.6, 0.8), (0.62, -0.05, 1.0), 0.35)
    for pb in pose:
        pb.rotation_euler = Euler((0, 0, 0))


build()
