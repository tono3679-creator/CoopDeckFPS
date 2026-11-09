"""Adds the bones the Meshy auto-rig lacks to the Lightning Rabbit and skins them (run inside Blender, headless).

    blender -b --factory-startup -P rabbit_rig.py -- [--check <dir>] [--export]

Input : ArtSource/LightningRabbit/Export/LightningRabbit_HQ.glb (rebuild_body.py output: 160k triangles, 24 Meshy bones)
Output: ArtSource/LightningRabbit/Animation/LightningRabbit_Rig.blend (authoring rig, the rabbit faces -Y)
        --export: ArtSource/LightningRabbit/Export/Unity/LightningRabbit.fbx with LOD0 (160k) / LOD1 / LOD2 meshes
        (named <body>_LOD0..2 so Unity builds the LOD group), turned to Unity's +Z
        --check : posed deformation renders (fist, elbows, knees, shoulders, ears, scarf, tail, twist)

New bones (the 24 Meshy bones and their rest pose are unchanged, so the Humanoid map still fits):
  <Side>HandThumb1-3, <Side>HandIndex1-3, ...Middle, ...Ring, ...Pinky   on <Side>Hand; one bone per phalanx
       (Thumb1 = metacarpal, as Unity's Humanoid thumb proximal). The sculpt's fingers are separate, so each digit
       bends on its own.
  Ear_L1-3 / Ear_R1-3   on Head, base to tip (procedural sway in Unity; clips do not key them)
  Tail01-02             on Hips (the pom-pom)
  Scarf_F1-4 / Scarf_B1-4   on Spine (UpperChest): the front and back scarf tails hanging on the right side
  Weapon, IK_Hand_L/R, IK_Elbow_L/R   the same weapon frame and IK goals as the Fox Gunner (clips key them)
Skin weights: each new chain takes its region with smooth blends at every joint; the root of each chain blends into
its parent (ears into the head, the tail and the scarf tails into the pelvis / upper chest instead of Meshy's thigh
and arm weights, which would drag them with every stride).
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rabbit_common import (ANIM_DIR, RIG_BLEND, STAGING, TURN_FOR_UNITY, import_rabbit, script_args,  # noqa: E402
                           select_only, smoothstep)
from rabbit_segment import FINGER_NAMES, ear_axis, report, segment  # noqa: E402

DIGIT_BONE = {"Thumb": "Thumb", "Index": "Index", "Middle": "Middle", "Ring": "Ring", "Little": "Pinky"}
EAR_BONES, TAIL_BONES, SCARF_BONES = 3, 2, 4
WEAPON_REST_GRIP = Vector((-0.12, -0.30, 0.95))  # rest placement only; clips move the bone
ELBOW_HINT = 0.15
LOD_FACES = (40000, 15000)


# ---------------------------------------------------------------- geometry helpers

def chain_points(co, ids, start, end, count):
    """count+1 joints from start to end, each moved to the centroid of the region's slice there (follows curves)."""
    p = co[list(ids)]
    axis = (end - start) / np.linalg.norm(end - start)
    length = np.linalg.norm(end - start)
    t = (p - start) @ axis
    pts = [start]
    for k in range(1, count):
        tk = length * k / count
        sel = np.abs(t - tk) < length / (2 * count)
        pts.append(p[sel].mean(0) if sel.sum() > 5 else start + axis * tk)
    pts.append(end)
    return [np.array(x) for x in pts]


def chain_param(points, joints):
    """Distance along the joint polyline of each point's projection (metres from the first joint), and the cumulative
    joint distances."""
    seg_len = [np.linalg.norm(joints[k + 1] - joints[k]) for k in range(len(joints) - 1)]
    cum = np.concatenate([[0.0], np.cumsum(seg_len)])
    best_d = np.full(len(points), np.inf)
    best_t = np.zeros(len(points))
    for k in range(len(joints) - 1):
        a, b = joints[k], joints[k + 1]
        ab = b - a
        L2 = float(ab @ ab)
        raw = ((points - a) @ ab) / L2
        tt = np.clip(raw, 0.0, 1.0)
        if k == 0:
            tt = np.minimum(raw, 1.0)              # before the root: negative distances
        if k == len(joints) - 2:
            tt = np.maximum(tt, 0.0) if k else tt  # past the tip: clamp on the last bone
        proj = a + np.outer(tt, ab)
        d = np.linalg.norm(points - proj, axis=1)
        better = d < best_d
        best_d[better] = d[better]
        best_t[better] = cum[k] + tt[better] * seg_len[k]
    return best_t, cum, best_d


def chain_weights(t, cum, root_blend, joint_blend):
    """Per point: (share left to the parent, (n, bones) weights). root_blend=(a, b) metres around the root;
    joint_blend = half width (metres) of the blend at each inner joint."""
    n = len(cum) - 1
    into = smoothstep(root_blend[0], root_blend[1], t)
    w = np.zeros((len(t), n))
    for k in range(n):
        lo = smoothstep(cum[k] - joint_blend, cum[k] + joint_blend, t) if k > 0 else np.ones(len(t))
        hi = 1.0 - smoothstep(cum[k + 1] - joint_blend, cum[k + 1] + joint_blend, t) if k < n - 1 else np.ones(len(t))
        w[:, k] = lo * hi
    w /= np.maximum(w.sum(1, keepdims=True), 1e-9)
    return 1.0 - into, w * into[:, None]


def smooth_weights(body, adj, region, prefix, passes=4, rate=0.5):
    """Laplacian smoothing of the weights of the groups whose names start with `prefix` (a hand and its digits) on the
    vertices of `region`; neighbours outside the region are read but kept. Re-limited to 4 bones and normalised."""
    me = body.data
    names = [g.name for g in body.vertex_groups if g.name.startswith(prefix)]
    idx = {n: body.vertex_groups[n].index for n in names}
    col = {g: k for k, g in enumerate(idx.values())}
    region = sorted(region)
    touched = sorted(set(region) | {n for v in region for n in adj[v]})
    pos = {v: k for k, v in enumerate(touched)}
    W = np.zeros((len(touched), len(names)))
    other = [dict() for _ in touched]   # weights of groups outside the hand (kept as they are)
    for v in touched:
        for g in me.vertices[v].groups:
            if g.group in col:
                W[pos[v], col[g.group]] = g.weight
            else:
                other[pos[v]][g.group] = g.weight
    rows = np.array([pos[v] for v in region])
    nbrs = [np.array([pos[n] for n in adj[v]]) for v in region]
    for _ in range(passes):
        avg = np.array([W[nb].mean(0) for nb in nbrs])
        W[rows] = (1 - rate) * W[rows] + rate * avg
    for k, v in enumerate(region):
        mix = {g: w for g, w in other[pos[v]].items()}
        for n, c in zip(names, range(len(names))):
            if W[pos[v], c] > 1e-4:
                mix[idx[n]] = W[pos[v], c]
        top = sorted(mix.items(), key=lambda x: -x[1])[:4]
        norm = sum(w for _, w in top) or 1.0
        for g in list(me.vertices[v].groups):
            body.vertex_groups[g.group].remove([v])
        for g, w in top:
            body.vertex_groups[g].add([v], w / norm, "REPLACE")


# ---------------------------------------------------------------- build

def build(arm, body, seg):
    co = seg["co"]
    chains = []   # (bone names, joints, parent, ids, root_blend, joint_blend, parent_group or None, falloff)

    for side, suffix in ((+1, "L"), (-1, "R")):
        ids = seg["ears"][side]
        base, tip = ear_axis(co, ids)
        joints = chain_points(co, ids, base, tip, EAR_BONES)
        chains.append(([f"Ear_{suffix}{k + 1}" for k in range(EAR_BONES)], joints, "Head", ids, (0.0, 0.045), 0.025,
                       None, None))

    tail = seg["tail"]
    p = co[list(tail)]
    root = p[p[:, 1] < np.percentile(p[:, 1], 15)].mean(0)
    end = p[np.argmax(p[:, 1])]
    chains.append((["Tail01", "Tail02"], chain_points(co, tail, root, end, TAIL_BONES), "Hips", tail, (-0.01, 0.02),
                   0.015, "Hips", None))

    for ids, label in zip(seg["ribbons"], ("F", "B")):
        p = co[list(ids)]
        top = p[p[:, 2] > p[:, 2].max() - 0.03].mean(0)
        bottom = p[p[:, 2] < p[:, 2].min() + 0.03].mean(0)
        joints = chain_points(co, ids, top, bottom, SCARF_BONES)
        chains.append(([f"Scarf_{label}{k + 1}" for k in range(SCARF_BONES)], joints, "Spine", ids, (0.0, 0.07), 0.035,
                       "Spine", None))

    shares = {}   # chain index -> {vertex: share} for vertices split between chains (the webbing)
    for side, (digits, frame, web, _) in seg["hands"].items():
        for name, dg in digits.items():
            bones = [f"{side}Hand{DIGIT_BONE[name]}{k + 1}" for k in range(3)]
            if name == "Thumb":
                chains.append((bones, dg["joints"], side + "Hand", dg["ids"], (0.0, 0.03), 0.007, None,
                               (dg["joints"][0], dg["axis"], 0.012, 0.02)))
            else:
                mine = {i: sh[name] for i, sh in web.items() if name in sh}
                chains.append((bones, dg["joints"], side + "Hand", dg["ids"] | set(mine), (-0.008, 0.012), 0.007,
                               None, None))
                shares[len(chains) - 1] = mine

    # ---------------------------------------------------------------- bones
    select_only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    hand_up = {side: Vector(h[1][3]) for side, h in seg["hands"].items()}

    def add(name, parent, head, tail, up_hint, connect=False):
        b = eb.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        b.align_roll(up_hint)
        b.parent = eb[parent]
        b.use_connect = connect
        b.use_deform = True
        return b

    for bones, joints, parent, _, _, _, _, _ in chains:
        if "Hand" in bones[0]:
            up = hand_up["Left" if bones[0].startswith("Left") else "Right"]
        elif bones[0].startswith("Scarf") or bones[0].startswith("Tail"):
            up = Vector((-1, 0, 0))
        else:
            up = Vector((0, -1, 0))
        prev = parent
        for k, name in enumerate(bones):
            add(name, prev, joints[k], joints[k + 1], up, connect=k > 0)
            prev = name
    w = add("Weapon", "Spine", WEAPON_REST_GRIP, WEAPON_REST_GRIP + Vector((0, -0.12, 0)), Vector((0, 0, 1)))
    w.use_deform = False
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
    me = body.data
    groups = {g.name: g for g in body.vertex_groups}
    names = {g.index: g.name for g in body.vertex_groups}

    def group(name):
        if name not in groups:
            groups[name] = body.vertex_groups.new(name=name)
            names[groups[name].index] = name
        return groups[name]

    new = {}   # vertex -> [(share, keep share of the current weights, {group: weight}, parent group or None)]
    for ci, (bones, joints, parent, ids, root_blend, joint_blend, parent_group, falloff) in enumerate(chains):
        ids = np.array(sorted(ids))
        share = shares.get(ci, {})
        t, cum, dist = chain_param(co[ids], joints)
        keep, w = chain_weights(t, cum, root_blend, joint_blend)
        if falloff is not None:   # thumb: the thenar pad blends out with the distance from the thumb's axis
            origin, axis, r_in, r_out = falloff
            r = co[ids] - origin
            radial = np.linalg.norm(r - np.outer(r @ axis, axis), axis=1)
            fade = 1.0 - smoothstep(r_in, r_out, radial) * (t < cum[1])
            w *= fade[:, None]
            keep = 1.0 - w.sum(1)
        for k, i in enumerate(ids):
            extra = {bones[b]: float(w[k, b]) for b in range(len(bones)) if w[k, b] > 1e-4}
            new.setdefault(int(i), []).append((share.get(int(i), 1.0), float(keep[k]), extra, parent_group))

    for i, parts in new.items():
        total_share = sum(p[0] for p in parts)
        keep = sum(p[0] * p[1] for p in parts) / total_share
        extra = {}
        for sh, _, ex, _ in parts:
            for n, wt in ex.items():
                extra[n] = extra.get(n, 0.0) + sh * wt / total_share
        parent_group = parts[0][3]
        v = me.vertices[i]
        old = {names[g.group]: g.weight for g in v.groups}
        if parent_group is not None:
            old = {parent_group: 1.0}           # tail / scarf tails: the kept share goes to the pelvis / upper chest
        total = sum(old.values()) or 1.0
        mix = {n: keep * wt / total for n, wt in old.items()}
        for n, wt in extra.items():
            mix[n] = mix.get(n, 0.0) + wt
        top = sorted(mix.items(), key=lambda x: -x[1])[:4]
        norm = sum(wt for _, wt in top) or 1.0
        for g in list(v.groups):
            body.vertex_groups[g.group].remove([i])
        for n, wt in top:
            if wt > 1e-4:
                group(n).add([i], wt / norm, "REPLACE")
    for side, (_, _, _, band) in seg["hands"].items():
        smooth_weights(body, seg["adj"], band, prefix=side + "Hand", passes=4)
    unweighted = sum(1 for v in me.vertices if not v.groups)
    print(f"  reweighted {len(new)} vertices; bones {len(arm.data.bones)} "
          f"({sum(1 for b in arm.data.bones if b.use_deform)} deform), unweighted {unweighted}")
    return chains


# ---------------------------------------------------------------- posed checks

def pose_rotate(arm, name, axis, deg):
    pb = arm.pose.bones[name]
    m = pb.matrix.copy()
    h = m.translation.copy()
    r = Matrix.Rotation(math.radians(deg), 4, Vector(axis).normalized())
    pb.matrix = Matrix.Translation(h) @ r @ Matrix.Translation(-h) @ m
    bpy.context.view_layer.update()


def bend_toward(arm, name, toward, deg):
    pb = arm.pose.bones[name]
    d = (pb.tail - pb.head).normalized()
    k = d.cross(Vector(toward))
    if k.length < 1e-6:
        return
    pose_rotate(arm, name, k, deg)


def local_axis(arm, name, axis):
    pb = arm.pose.bones[name]
    return (pb.matrix.to_3x3() @ Vector(axis)).normalized()


def reset_pose(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def pose_arms_down(arm, deg=65):
    for side in ("Left", "Right"):
        bend_toward(arm, side + "Arm", (0, 0, -1), deg)


def pose_fist(arm, curl=(55, 80, 50), thumb=(20, 30, 30)):
    for side in ("Left", "Right"):
        for dname in FINGER_NAMES:
            for k in range(3):
                b = f"{side}Hand{DIGIT_BONE[dname]}{k + 1}"
                pose_rotate(arm, b, local_axis(arm, b, (1, 0, 0)), -curl[k])
        for k in range(3):
            b = f"{side}HandThumb{k + 1}"
            pose_rotate(arm, b, local_axis(arm, b, (1, 0, 0)), -thumb[k])


def check(arm, body, out):
    from rabbit_debug import render_sheet, stack
    os.makedirs(out, exist_ok=True)
    rows = []

    def row(tag, shots):
        rows.append(render_sheet(os.path.join(out, f"_{tag}.png"), shots, res=380, colour_type="SINGLE"))

    full = Vector((0, 0.05, 0.85))
    # 1. relaxed: arms down, elbows a little bent, a fist
    reset_pose(arm)
    pose_arms_down(arm, 65)
    for side in ("Left", "Right"):
        bend_toward(arm, side + "ForeArm", (0, -1, 0), 30)
    pose_fist(arm)
    row("a_relaxed", [(full, 1.9, "front"), (full, 1.9, "right"), (full, 1.9, "back"),
                      (Vector((-0.2, -0.05, 0.62)), 0.32, "front"), (Vector((-0.2, -0.05, 0.62)), 0.32, "right")])
    # 2. fists close up
    reset_pose(arm)
    pose_fist(arm)
    hand = Vector((-0.57, 0.0, 0.93))
    row("b_fist", [(hand, 0.2, "front"), (hand, 0.2, "top"), (hand, 0.2, Vector((0, 0.001, -1))),
                   (hand, 0.2, "back"), (hand, 0.2, "right")])
    thumb = arm.pose.bones["RightHandThumb2"].head.copy()
    row("b_thumb", [(thumb, 0.11, "front"), (thumb, 0.11, Vector((-0.5, -1, -0.6))), (thumb, 0.11, Vector((0, 0.001, -1))),
                    (thumb, 0.11, "right"), (thumb, 0.11, Vector((0.3, -1, 0.4)))])
    reset_pose(arm)
    row("b_rest", [(thumb, 0.11, "front"), (thumb, 0.11, Vector((-0.5, -1, -0.6))), (thumb, 0.11, Vector((0, 0.001, -1))),
                   (thumb, 0.11, "right"), (thumb, 0.11, Vector((0.3, -1, 0.4)))])
    # 3. deep elbow bend and shoulder down
    reset_pose(arm)
    pose_arms_down(arm, 50)
    for side in ("Left", "Right"):
        bend_toward(arm, side + "ForeArm", (0, -1, 0), 125)
    row("c_elbow", [(full, 1.9, "front"), (Vector((-0.27, 0.0, 0.85)), 0.4, "right"),
                    (Vector((-0.27, 0.0, 0.85)), 0.4, "back"), (Vector((-0.2, 0.0, 0.95)), 0.4, "front"),
                    (Vector((-0.2, 0.0, 0.95)), 0.4, "top")])
    # 4. legs: hip flexion and a deep knee bend
    reset_pose(arm)
    bend_toward(arm, "RightUpLeg", (0, -1, 0), 75)
    bend_toward(arm, "RightLeg", (0, 1, 0), 110)
    bend_toward(arm, "LeftUpLeg", (0, 1, 0), 25)
    row("d_legs", [(Vector((0, 0, 0.6)), 1.3, "right"), (Vector((-0.12, -0.1, 0.55)), 0.5, "right"),
                   (Vector((-0.12, -0.1, 0.55)), 0.5, "front"), (Vector((0, 0.05, 0.7)), 0.6, "back"),
                   (Vector((-0.12, -0.1, 0.55)), 0.5, "left")])
    # 5. ears back, scarf tails swung back, tail up, a turned head and a twisted spine
    reset_pose(arm)
    for suffix in ("L", "R"):
        for k in range(1, EAR_BONES + 1):
            bend_toward(arm, f"Ear_{suffix}{k}", (0, 1, 0), 22)
    for label in ("F", "B"):
        for k in range(1, SCARF_BONES + 1):
            bend_toward(arm, f"Scarf_{label}{k}", (0, 1, 0), 14)
    bend_toward(arm, "Tail01", (0, 0, 1), 30)
    pose_rotate(arm, "Spine01", (0, 0, 1), 25)
    pose_rotate(arm, "Head", (0, 0, 1), 35)
    row("e_secondary", [(full, 1.9, "front"), (full, 1.9, "right"), (Vector((0, 0.05, 1.45)), 0.6, "right"),
                        (Vector((-0.15, 0.1, 0.75)), 0.8, "right"), (Vector((0, 0.1, 0.7)), 0.35, "right")])
    reset_pose(arm)
    stack(rows, os.path.join(out, "deform_check.png"))
    print(f"  check renders -> {os.path.join(out, 'deform_check.png')}")

    # close-ups at a higher resolution: a grip around a pistol grip's thickness (shape and texture), a full fist, and
    # the shoulders with the arms down
    rows = []
    hand = arm.pose.bones["RightHandMiddle1"].head.copy()
    pose_fist(arm, curl=(45, 60, 35), thumb=(15, 25, 20))
    rows.append(render_sheet(os.path.join(out, "_h_GRIP.png"), [
        (hand, 0.13, "front"), (hand, 0.13, "top"), (hand, 0.13, Vector((0, 0.001, -1))), (hand, 0.13, "back"),
        (hand, 0.13, "right")], res=520, colour_type="SINGLE"))
    reset_pose(arm)
    pose_fist(arm)
    for colour in ("SINGLE", "TEXTURE"):
        rows.append(render_sheet(os.path.join(out, f"_h_{colour}.png"), [
            (hand, 0.13, "front"), (hand, 0.13, "top"), (hand, 0.13, Vector((0, 0.001, -1))), (hand, 0.13, "back"),
            (hand, 0.13, "right")], res=520, colour_type=colour))
    reset_pose(arm)
    pose_arms_down(arm, 70)
    for side in ("Left", "Right"):
        bend_toward(arm, side + "ForeArm", (0, -1, 0), 20)
    sh = Vector((-0.17, 0.0, 0.93))
    rows.append(render_sheet(os.path.join(out, "_s.png"), [
        (sh, 0.38, "front"), (sh, 0.38, "right"), (sh, 0.38, "back"), (Vector((0, 0.0, 0.93)), 0.6, "top"),
        (sh, 0.38, Vector((-0.6, -1, 0.5)))], res=520, colour_type="SINGLE"))
    reset_pose(arm)
    stack(rows, os.path.join(out, "deform_closeups.png"))


# ---------------------------------------------------------------- export

def make_lods(body):
    """LOD1/LOD2: copies of the skinned body decimated by quadric collapse (UVs and weights are interpolated)."""
    lods = [body]
    for k, faces in enumerate(LOD_FACES, start=1):
        c = body.copy()
        c.data = body.data.copy()
        bpy.context.scene.collection.objects.link(c)
        mods = [(m.name, m.object) for m in c.modifiers if m.type == "ARMATURE"]
        for m in list(c.modifiers):
            c.modifiers.remove(m)
        d = c.modifiers.new("Decimate", "DECIMATE")
        d.ratio = faces / len(body.data.polygons)
        d.use_collapse_triangulate = True
        select_only(c)
        bpy.ops.object.modifier_apply(modifier="Decimate")
        bpy.ops.object.vertex_group_limit_total(limit=4)
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
        for name, obj in mods:
            c.modifiers.new(name, "ARMATURE").object = obj
        lods.append(c)
    base = body.name
    for k, o in enumerate(lods):
        o.name = o.data.name = f"{base}_LOD{k}"
        print(f"  {o.name}: {len(o.data.polygons)} triangles, {len(o.data.vertices)} vertices, "
              f"unweighted {sum(1 for v in o.data.vertices if not v.groups)}")
    return lods


def export(arm, body):
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fox_anim"))
    from fox_common import bake_rig_transform, export_fbx   # shared FBX settings (read only)
    lods = make_lods(body)
    os.makedirs(STAGING, exist_ok=True)
    bake_rig_transform(arm, lods, TURN_FOR_UNITY)
    export_fbx(os.path.join(STAGING, "LightningRabbit.fbx"), [arm, *lods], bake_anim=False)


def main():
    args = script_args()
    arm, body = import_rabbit()
    seg = segment(arm, body)
    report(seg)
    build(arm, body, seg)
    if "--check" in args:
        check(arm, body, args[args.index("--check") + 1])
    os.makedirs(ANIM_DIR, exist_ok=True)
    reset_pose(arm)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=RIG_BLEND)
    print(f"  saved {RIG_BLEND}")
    if "--export" in args:
        export(arm, body)
    print("RIG_DONE")


if __name__ == "__main__":
    main()
