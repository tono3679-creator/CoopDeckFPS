"""First-person arms cut from Meshy's high-resolution sculpt: the fox's own arms, gloves and fingers.

    blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P ArtSource/Tools/fox_anim/fox_fp_arms.py -- \
        [--faces 40000] [--size 4096] [--atlas <upscaled atlas>] [--check <dir>]

Outputs (Assets/_Game/Art/Characters/FoxGunner/):
  Models/FoxGunner_FPArms.fbx   Root; per side Arm, ForeArm, Hand, Index/Middle/Ring/Pinky 1-3, Thumb 1-3, and the
                                non-deforming markers Palm and PalmOut (Unity curls the fingers toward PalmOut)
  Textures/T_FoxGunner_FPArms_BaseColor.png, T_FoxGunner_FPArms_Normal.png, T_FoxGunner_FPArms_SurfaceMask.png

  1. region: the rigged body's arm vertices (from a quarter down the upper arm to the finger tips), and the sculpt's
     faces within 12 mm of them (the sculpt fitted to the prepared body by bounding box, as refine_character.py does)
  2. quadric decimation to --faces: the sculpt's separate fingers, rivets, cuff rings and buckles stay; even UVs
  3. textures, so the arms look exactly like the body: colour from the prepared body through its own UVs and the
     Real-ESRGAN atlas (as the rebuilt body), normals from the full sculpt, surface regions from the body's mask
  4. skin: Arm / ForeArm / Hand weights from the rigged body; the four fingers (side by side across the hand) are split
     out and each gets three joints, the thumb three. The bones keep the body's rest, so the arms have the fox's own
     proportions (no stretching).
--check renders the arms unposed and with the fingers curled (texture), before export.
"""
import argparse
import math
import os
import sys
from types import SimpleNamespace

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

import refine_character as rc  # noqa: E402
from fox_common import FOX_SRC, REPO, TURN_FOR_UNITY, bake_rig_transform, export_fbx, select_only  # noqa: E402
from fox_segment import hand_frame  # noqa: E402

ART = os.path.join(REPO, "Assets", "_Game", "Art", "Characters", "FoxGunner")
OUT_FBX = os.path.join(ART, "Models", "FoxGunner_FPArms.fbx")
OUT_BASE = os.path.join(ART, "Textures", "T_FoxGunner_FPArms_BaseColor.png")
OUT_NORMAL = os.path.join(ART, "Textures", "T_FoxGunner_FPArms_Normal.png")
OUT_MASK = os.path.join(ART, "Textures", "T_FoxGunner_FPArms_SurfaceMask.png")
BODY_MASK = os.path.join(ART, "Textures", "T_FoxGunner_SurfaceMask.png")
HIGHRES = os.path.join(FOX_SRC, "Meshy", "01_generate", "FoxGunner_highres.glb")
PREPARED = os.path.join(FOX_SRC, "Export", "FoxGunner.glb")
ATLAS = os.path.join(FOX_SRC, "Export", "FoxGunner_atlas_x4.png")

ARM_PARTS = ("Arm", "ForeArm", "Hand")
BODY_PARTS = ("Arm", "ForeArm", "Hand", "Fingers1", "Fingers2", "Thumb")
UPPER_ARM_CUT = 0.25        # keep the upper arm from a quarter of the way down (the shoulder never shows)
KEEP_DISTANCE = 0.012       # sculpt vertices this close to the body's arms belong to them
FINGERS = ("Index", "Middle", "Ring", "Pinky")  # thumb side first
KNUCKLE = 0.058             # glove edge over the knuckles, metres from the wrist along the hand (fox_rig.KNUCKLE)
FINGER_JOINTS = (0.0, 0.45, 0.75)   # phalanx starts as fractions of the finger length from the knuckle
THUMB_JOINTS = (0.0, 0.4, 0.7)
BLEND = 0.004               # metres either side of a joint where both bones move a vertex
PALM_ALONG, PALM_IN, PALM_OUT = 0.055, 0.014, 0.03


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--faces", type=int, default=40000)
    p.add_argument("--size", type=int, default=4096)
    p.add_argument("--mask-size", type=int, default=2048)
    p.add_argument("--atlas", default=ATLAS)
    p.add_argument("--check")
    args = p.parse_args(argv)
    args.atlas = os.path.abspath(args.atlas)
    if args.check:
        args.check = os.path.abspath(args.check)
    return args


def smoothstep(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def world_coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    m = np.array(obj.matrix_world)
    return co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]


def delete_vertices(obj, mask):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(mask)[0]], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()


def dominant(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    return [names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in obj.data.vertices]


# ---------------------------------------------------------------- 1-2. geometry

def arm_region(body, bones):
    """The rigged body's arm vertices, as a separate unrigged mesh in world space."""
    names = {s + p for s in ("Left", "Right") for p in BODY_PARTS}
    groups = {g.index: g.name for g in body.vertex_groups}
    keep = np.zeros(len(body.data.vertices), dtype=bool)
    for v in body.data.vertices:
        total = sum(g.weight for g in v.groups) or 1.0
        if sum(g.weight for g in v.groups if groups[g.group] in names) / total < 0.6:
            continue
        dom = groups[max(v.groups, key=lambda g: g.weight).group]
        ok = True
        for side in ("Left", "Right"):
            if dom == side + "Arm":
                a, b = bones[side + "Arm"].head_local, bones[side + "ForeArm"].head_local
                ok = (Vector(v.co) - a).dot(b - a) / (b - a).length_squared >= UPPER_ARM_CUT
        keep[v.index] = ok
    low = body.copy()
    low.data = body.data.copy()
    low.name = low.data.name = "ArmRegion"
    bpy.context.scene.collection.objects.link(low)
    for m in list(low.modifiers):
        low.modifiers.remove(m)
    world = body.matrix_world.copy()
    low.parent = None
    low.matrix_world = world
    delete_vertices(low, ~keep)
    return low


def sculpt_arms(high, low, faces):
    """The sculpt's faces lying on the body's arms, decimated to `faces` triangles."""
    co = world_coords(high)
    lco = world_coords(low)
    pad = 0.03
    inside = np.all((co > lco.min(0) - pad) & (co < lco.max(0) + pad), axis=1) & (np.abs(co[:, 0]) > 0.1)
    bvh = BVHTree.FromObject(low, bpy.context.evaluated_depsgraph_get())
    near = np.zeros(len(co), dtype=bool)
    for i in np.nonzero(inside)[0]:
        if bvh.find_nearest(Vector(co[i]), KEEP_DISTANCE)[0] is not None:
            near[i] = True
    arms = high.copy()
    arms.data = high.data.copy()
    arms.name = arms.data.name = "FoxGunner_FPArms"
    bpy.context.scene.collection.objects.link(arms)
    delete_vertices(arms, ~near)
    select_only(arms)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.mesh.delete_loose()
    bpy.ops.object.mode_set(mode="OBJECT")
    # drop crumbs (bits of the torso or the armband edge caught by the distance test)
    bm = bmesh.new()
    bm.from_mesh(arms.data)
    bm.verts.ensure_lookup_table()
    seen = np.zeros(len(bm.verts), dtype=bool)
    crumbs = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack, comp = [v], []
        seen[v.index] = True
        while stack:
            x = stack.pop()
            comp.append(x.index)
            for e in x.link_edges:
                y = e.other_vert(x)
                if not seen[y.index]:
                    seen[y.index] = True
                    stack.append(y)
        if len(comp) < 500:
            crumbs.extend(comp)
    bm.free()
    mask = np.zeros(len(arms.data.vertices), dtype=bool)
    mask[crumbs] = True
    delete_vertices(arms, mask)
    before = len(arms.data.polygons)
    dec = arms.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, faces / before)
    dec.use_collapse_triangulate = True
    select_only(arms)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    if arms.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth()
    print(f"  sculpt arms: {before} -> {len(arms.data.polygons)} triangles, {len(arms.data.vertices)} vertices "
          f"({len(crumbs)} crumb vertices dropped)")
    return arms


def unwrap(arms, size, margin=8):
    me = arms.data
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    select_only(arms)
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


# ---------------------------------------------------------------- 3. textures

def bake_textures(arms, prepared, body, high, args):
    covered = rc.rasterize(rc.uv_triangles(arms.data, arms.data.uv_layers["UVMap"]), args.size)
    rgb, hit = rc.bake_surface_image(prepared, prepared.data.uv_layers.active.name, args.atlas, arms, args.size,
                                     cage=0.004, ray=0.008)
    rgb, filled = rc.fill_within(rgb, hit, covered)
    print(f"  colour: {np.mean(~hit[covered]):.3%} of texels missed after a wider search, "
          f"{int((covered & ~filled).sum())} left after filling inside the islands")
    rgb = rc.push_pull_fill(rgb, (covered & filled) | (~covered & hit))
    rc.save_rgb(rgb, OUT_BASE)

    mcov = rc.rasterize(rc.uv_triangles(arms.data, arms.data.uv_layers["UVMap"]), args.mask_size)
    channels = []
    for alpha in (False, True):
        m, mhit = rc.bake_surface_image(body, body.data.uv_layers.active.name, BODY_MASK, arms, args.mask_size,
                                        cage=0.004, ray=0.008, colorspace="Non-Color", alpha=alpha)
        m, mfill = rc.fill_within(m, mhit, mcov)
        m = rc.push_pull_fill(m, (mcov & mfill) | (~mcov & mhit))
        channels.append(m)
    rgba = np.concatenate([channels[0], channels[1][..., :1]], axis=2)
    img = bpy.data.images.new("FPArmsMask", args.mask_size, args.mask_size, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = OUT_MASK
    img.file_format = "PNG"
    img.save()
    print(f"  surface mask {args.mask_size}x{args.mask_size} from the body's -> {OUT_MASK}")

    ns = SimpleNamespace(size=args.size, cage=0.004, ray=0.008, max_lean=75.0)
    rc.bake_normal_image(arms, high, ns, OUT_NORMAL)


# ---------------------------------------------------------------- 4. skin and bones

def transfer_weights(arms, body):
    for side in ("Left", "Right"):
        for part in BODY_PARTS:
            arms.vertex_groups.new(name=side + part)
    dt = arms.modifiers.new("Weights", "DATA_TRANSFER")
    dt.object = body
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    select_only(arms)
    bpy.ops.object.modifier_apply(modifier="Weights")


def find_digits(edges, hand, s, u, step=0.002, min_verts=20):
    """Labels the five digits of a hand by the mesh's own connectivity: a cut across the hand (s along the hand from
    the wrist bone) moves out from the wrist until the hand vertices beyond it fall apart into five pieces (the glove
    joins the digits before that; the wrist bone sits at a different depth in each hand, so no fixed distance works).
    Where two fingers touch in the sculpt, the cut that gives four pieces is used and the widest finger piece is split
    in two across the hand. u points across the hand toward the thumb. Returns (label per vertex: 0 = thumb,
    1-4 = index..pinky, -1 = rest) and the cut."""
    n = len(s)
    adj = [[] for _ in range(n)]
    for a, b in edges:
        if hand[a] and hand[b]:
            adj[a].append(b)
            adj[b].append(a)

    def pieces(cut):
        keep = hand & (s >= cut)
        comp = np.full(n, -1)
        sizes = []
        for start in np.nonzero(keep)[0]:
            if comp[start] >= 0:
                continue
            k = len(sizes)
            comp[start] = k
            stack, count = [start], 0
            while stack:
                x = stack.pop()
                count += 1
                for y in adj[x]:
                    if keep[y] and comp[y] < 0:
                        comp[y] = k
                        stack.append(y)
            sizes.append(count)
        return comp, [k for k, c in enumerate(sizes) if c >= min_verts]

    hs = s[hand]
    first = {}  # number of pieces -> the first cut giving it
    for cut in np.arange(np.percentile(hs, 5), hs.max(), step):
        comp, big = pieces(cut)
        first.setdefault(min(len(big), 5), cut)
        if len(big) >= 5:
            break
    if 5 in first:
        cut = first[5]
        comp, big = pieces(cut)
        big = sorted(big, key=lambda k: -(comp == k).sum())[:5]
    elif 4 in first:
        cut = first[4]
        comp, big = pieces(cut)
        thumb = max(big, key=lambda k: u[comp == k].mean())
        wide = max((k for k in big if k != thumb), key=lambda k: np.ptp(u[comp == k]))
        idx = np.nonzero(comp == wide)[0]
        c = np.percentile(u[idx], [25, 75])
        for _ in range(30):  # two-means across the hand
            near_hi = np.abs(u[idx] - c[1]) < np.abs(u[idx] - c[0])
            c = np.array([u[idx][~near_hi].mean(), u[idx][near_hi].mean()])
        new = comp.max() + 1
        comp[idx[near_hi]] = new
        big = big + [new]
        print(f"    two touching fingers split across the hand at u {c.mean() * 100:.1f} cm")
    else:
        raise RuntimeError(f"could not find the digits; pieces by cut: {first}")
    order = sorted(big, key=lambda k: -u[comp == k].mean())  # thumb side first
    remap = {k: i for i, k in enumerate(order)}
    label = np.array([remap.get(c, -1) for c in comp])
    return label, cut


def split_hand(arms, rig_arm, side):
    """Finger and thumb joints from the geometry, and new weights for their vertices. Returns the bones
    [(name, parent, head, tail)] in world space (rest), the weights {vertex: {group: weight}} and the back-of-hand axis."""
    wrist, a, front, up = hand_frame(rig_arm, side)
    wv, av, fv, uv = (np.array(x) for x in (wrist, a, front, up))
    co = world_coords(arms)
    rel = co - wv
    s, u, w = rel @ av, rel @ fv, rel @ uv
    dom = dominant(arms)
    hand = np.array([d in (side + "Hand", side + "Fingers1", side + "Fingers2", side + "Thumb") for d in dom])
    edges = np.empty(len(arms.data.edges) * 2, dtype=np.int64)
    arms.data.edges.foreach_get("vertices", edges)
    label, s_cut = find_digits(edges.reshape(-1, 2), hand, s, u)
    bones, weights = [], {}

    def joints(name, sel, base, tip_point, fractions, root_blend):
        axis = (tip_point - base) / np.linalg.norm(tip_point - base)
        t = (co - base) @ axis
        length = t[sel].max()
        heads = [base + axis * length * f for f in fractions] + [base + axis * length]
        for j in range(3):
            bones.append((f"{side}{name}{j + 1}", f"{side}Hand" if j == 0 else f"{side}{name}{j}",
                          Vector(heads[j]), Vector(heads[j + 1])))
        c2, c3 = (length * f for f in fractions[1:])
        for i in np.nonzero(sel)[0]:
            ti = t[i]
            wh = 1.0 - smoothstep(root_blend[0] * length, root_blend[1] * length, ti) if root_blend else \
                1.0 - smoothstep(-BLEND, BLEND, ti)
            w2 = smoothstep(c2 - BLEND, c2 + BLEND, ti)
            w3 = smoothstep(c3 - BLEND, c3 + BLEND, ti)
            rest = 1.0 - wh
            weights[i] = {side + "Hand": wh, f"{side}{name}1": rest * (1 - w2), f"{side}{name}2": rest * (w2 - w3),
                          f"{side}{name}3": rest * w3}
        print(f"  {side}{name}: {int(sel.sum())} vertices, length {length * 100:.1f} cm")

    print(f"  {side} hand: the digits leave the glove {s_cut * 100:.1f} cm past the wrist bone")
    for k, name in enumerate(("Thumb",) + FINGERS):  # digits come sorted thumb side first
        sel = label == k
        pts = co[sel]
        # base: the digit's cross-section where it leaves the glove; tip: its far end along its own direction
        sd = s[sel]
        base = pts[sd < sd.min() + 0.004].mean(0)
        centred = pts - pts.mean(0)
        axis = np.linalg.svd(centred, full_matrices=False)[2][0]
        if axis @ (pts.mean(0) - base) < 0:
            axis = -axis
        along = (pts - base) @ axis
        tip_point = pts[along > np.percentile(along, 90)].mean(0)
        if name == "Thumb":
            joints(name, sel, base, tip_point, THUMB_JOINTS, (0.0, 0.25))  # the thumb's root moves with the palm
        else:
            joints(name, sel, base, tip_point, FINGER_JOINTS, None)

    palm = wv + av * PALM_ALONG - uv * PALM_IN
    bones.append((f"{side}Palm", f"{side}Hand", Vector(palm), Vector(palm + av * 0.02)))
    out = palm - uv * PALM_OUT
    bones.append((f"{side}PalmOut", f"{side}Hand", Vector(out), Vector(out + av * 0.02)))
    return bones, weights, Vector(up)


def build_rig(src_bones, hand_bones):
    data = bpy.data.armatures.new("FPArms_Rig")
    rig = bpy.data.objects.new("FPArms", data)
    bpy.context.scene.collection.objects.link(rig)
    select_only(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = data.edit_bones
    root = eb.new("Root")
    root.head, root.tail = Vector((0, 0, 0)), Vector((0, 0, 0.1))
    for side in ("Left", "Right"):
        parent = root
        for part in ARM_PARTS:
            b = src_bones[side + part]
            e = eb.new(b.name)
            e.length = b.length
            e.matrix = b.matrix_local.copy()
            e.parent = parent
            e.use_connect = False
            parent = e
    for side, (bones, up) in hand_bones.items():
        for name, parent, head, tail in bones:
            e = eb.new(name)
            e.head, e.tail = head, tail
            e.align_roll(up)
            e.parent = eb[parent]
            e.use_connect = False
            e.use_deform = not name.endswith(("Palm", "PalmOut"))
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def apply_weights(arms, weights_by_side):
    for side, weights in weights_by_side.items():
        old = [arms.vertex_groups[g] for g in (side + "Fingers1", side + "Fingers2", side + "Thumb")]
        for i, ws in weights.items():
            for g in old:
                g.remove([int(i)])
            for name, value in ws.items():
                g = arms.vertex_groups.get(name) or arms.vertex_groups.new(name=name)
                g.add([int(i)], float(value), "REPLACE")
    # what the body's shared finger and thumb bones still move (the palm near the knuckles) moves with the hand
    for side in ("Left", "Right"):
        hand = arms.vertex_groups[side + "Hand"]
        for name in (side + "Fingers1", side + "Fingers2", side + "Thumb"):
            g = arms.vertex_groups[name]
            for v in arms.data.vertices:
                for e in v.groups:
                    if e.group == g.index and e.weight > 0:
                        hand.add([v.index], e.weight, "ADD")
            arms.vertex_groups.remove(g)
    for g in list(arms.vertex_groups):  # torso or neck weights near the cut go to the arms' own bones
        if not any(g.name.startswith(s) for s in ("Left", "Right")):
            arms.vertex_groups.remove(g)
    select_only(arms)
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


# ---------------------------------------------------------------- check renders

def render_check(rig, arms, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    mat = bpy.data.materials.new("CheckTex")
    mat.use_nodes = True
    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(OUT_BASE, check_existing=True)
    mat.node_tree.nodes.active = t
    arms.data.materials.clear()
    arms.data.materials.append(mat)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o is not arms
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    hand = rig.matrix_world @ rig.data.bones["RightHand"].head_local
    views = {"hand_top": (hand + Vector((-0.04, -0.14, 0.2)), hand + Vector((-0.06, 0, 0)), 45),
             "hand_front": (hand + Vector((-0.1, -0.25, 0.02)), hand + Vector((-0.06, 0, 0)), 45),
             "arm": (hand + Vector((0.25, -0.55, 0.15)), hand + Vector((0.12, 0, 0)), 35)}
    for pose_name in ("rest", "grip"):
        for side in ("Left", "Right"):
            for name in FINGERS + ("Thumb",):
                for j in range(1, 4):
                    pb = rig.pose.bones.get(f"{side}{name}{j}")
                    if pb is None:
                        continue
                    pb.rotation_mode = "XYZ"
                    curl = 0.0 if pose_name == "rest" else ((30, 35, 25) if name == "Thumb" else (60, 70, 45))[j - 1]
                    pb.rotation_euler = (-math.radians(curl), 0, 0)  # roll aligned to the back of the hand: negative curls toward the palm
        bpy.context.view_layer.update()
        for vname, (loc, target, lens) in views.items():
            cam.location = loc
            cam.rotation_euler = (target - loc).to_track_quat("-Z", "Y").to_euler()
            cam.data.lens = lens
            sc.render.filepath = os.path.join(out_dir, f"fparms_{pose_name}_{vname}.png")
            bpy.ops.render.render(write_still=True)
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()


# ---------------------------------------------------------------- main

def main():
    args = parse()
    rig_arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups and o.parent is rig_arm)
    src_bones = rig_arm.data.bones
    material = body.material_slots[0].material

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=PREPARED)  # colour source: Meshy's UVs and the upscaled atlas
    prepared = next(o for o in bpy.data.objects if o not in before and o.type == "MESH" and o.vertex_groups)
    for o in [o for o in bpy.data.objects if o not in before and o is not prepared]:
        bpy.data.objects.remove(o, do_unlink=True)
    world = prepared.matrix_world.copy()
    prepared.parent = None
    prepared.matrix_world = world
    for m in list(prepared.modifiers):
        prepared.modifiers.remove(m)

    high = rc.import_highres_fitted(HIGHRES, prepared)
    low = arm_region(body, src_bones)
    arms = sculpt_arms(high, low, args.faces)
    arms.data.materials.clear()
    arms.data.materials.append(material)
    unwrap(arms, args.size)
    bake_textures(arms, prepared, body, high, args)

    transfer_weights(arms, body)
    hand_bones, weights = {}, {}
    for side in ("Left", "Right"):
        bones, w, up = split_hand(arms, rig_arm, side)
        hand_bones[side] = (bones, up)
        weights[side] = w
    rig = build_rig(src_bones, hand_bones)
    apply_weights(arms, weights)
    arms.parent = rig
    arms.matrix_parent_inverse = Matrix.Identity(4)
    for m in list(arms.modifiers):
        arms.modifiers.remove(m)
    arms.modifiers.new("Armature", "ARMATURE").object = rig
    unweighted = sum(1 for v in arms.data.vertices if not v.groups)
    print(f"  first-person arms: {len(arms.data.vertices)} vertices, {len(arms.data.polygons)} triangles, "
          f"{len(rig.data.bones)} bones, {unweighted} unweighted vertices")

    if args.check:
        render_check(rig, arms, args.check)
        arms.data.materials.clear()
        arms.data.materials.append(material)

    for o in [o for o in bpy.data.objects if o not in (rig, arms)]:
        bpy.data.objects.remove(o, do_unlink=True)
    bake_rig_transform(rig, [arms], TURN_FOR_UNITY)
    export_fbx(OUT_FBX, [rig, arms], bake_anim=False)
    print("FP_ARMS_DONE")


main()
