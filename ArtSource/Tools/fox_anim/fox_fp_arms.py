"""First-person arms for the view model: Meshy high-resolution forearms + hand-modelled gloved hands.

    blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P ArtSource/Tools/fox_anim/fox_fp_arms.py -- [--check <dir>]

Output: Assets/_Game/Art/Characters/FoxGunner/Models/FoxGunner_FPArms.fbx (Generic; faces Unity's +Z like the body)

  1. cut the arms from the game body (vertices skinned mostly to arm/hand/finger bones, minus the top of the upper
     arm): this low cut defines the region and the skin weights
  2. fit Meshy's high-resolution mesh (2.3M faces, no UVs) onto the body, keep the forearm faces lying on the low cut
     up to the wrist, decimate, take weights from the low cut and UVs from the whole body (each face inside one
     UV-continuous patch, see body_uvs), so the bracer, armband and skin match the body
  3. new hands from fox_fp_hands.py (metaball surface, 3 phalanges per finger, fingerless glove), built in the body's
     rest hand frames so they fit the same grip: right hand around the pistol grip, left hand around the handguard
     with an "Open" shape key (palm thrust for casts); both rigid on their Hand bone
  4. armature with Root + per side Arm, ForeArm, Hand (rest = the body's), the body's material
--check renders the arms holding the Fire Rifle in the shared stance (before export).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

from fox_common import FOX_SRC, REPO, TURN_FOR_UNITY, bake_rig_transform, export_fbx, script_args, select_only  # noqa: E402
from fox_fp_hands import build_hand, sample_uv_points  # noqa: E402

OUT = os.path.join(REPO, "Assets", "_Game", "Art", "Characters", "FoxGunner", "Models", "FoxGunner_FPArms.fbx")
HIGHRES = os.path.join(FOX_SRC, "Meshy", "01_generate", "FoxGunner_highres.glb")
ARM_PARTS = ("Arm", "ForeArm", "Hand")
SKIN_PARTS = ("Arm", "ForeArm", "Hand", "Fingers1", "Fingers2", "Thumb")
UPPER_ARM_CUT = 0.3     # drop the top 30% of the upper arm (the shoulder never shows in first person)
WRIST_CUT = 0.004       # forearm faces end here (metres past the wrist along the hand); the new glove cuff overlaps
FOREARM_FACES = 14000   # both forearms together
KEEP_DISTANCE = 0.012
CHART_RADIUS = 0.03     # a forearm face takes its UVs from the UV-continuous body patch within this distance
HAND_POSE = {"Right": ("pistol", False), "Left": ("handguard", True)}
# first-person left grip shift (rifle space: right, forward, up): FirstPersonArms.leftGripShift, Unity (0, 0.035, 0.23)
FP_LEFT_SHIFT = Vector((0.0, 0.23, 0.035))


def build_armature(src):
    data = bpy.data.armatures.new("FPArms_Rig")
    arm = bpy.data.objects.new("FPArms", data)
    bpy.context.scene.collection.objects.link(arm)
    select_only(arm)
    bpy.ops.object.mode_set(mode="EDIT")
    root = data.edit_bones.new("Root")
    root.head, root.tail = Vector((0, 0, 0)), Vector((0, 0, 0.1))
    for side in ("Left", "Right"):
        parent = root
        for part in ARM_PARTS:
            b = src[side + part]
            eb = data.edit_bones.new(b.name)
            eb.length = b.length
            eb.matrix = b.matrix_local.copy()
            eb.parent = parent
            eb.use_connect = False
            parent = eb
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def delete_vertices(obj, mask):
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.vertices.foreach_set("select", mask.astype(bool))
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="VERT")
    bpy.ops.object.mode_set(mode="OBJECT")


def coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    m = np.array(obj.matrix_world)
    return co @ m[:3, :3].T + m[:3, 3]


def low_cut(src_body, src_bones):
    names = [s + p for s in ("Left", "Right") for p in SKIN_PARTS]
    body = src_body.copy()
    body.data = src_body.data.copy()
    body.name = body.data.name = "FPArms_Low"
    bpy.context.scene.collection.objects.link(body)
    groups = {g.index: g.name for g in body.vertex_groups}
    me = body.data
    keep = np.zeros(len(me.vertices), dtype=bool)
    for v in me.vertices:
        total = sum(g.weight for g in v.groups) or 1.0
        ok = sum(g.weight for g in v.groups if groups[g.group] in names) / total >= 0.6
        if ok:
            dominant = groups[max(v.groups, key=lambda g: g.weight).group]
            for side in ("Left", "Right"):
                a, b = src_bones[side + "Arm"].head_local, src_bones[side + "ForeArm"].head_local
                t = (v.co - a).dot(b - a) / (b - a).length_squared
                if dominant == side + "Arm" and t < UPPER_ARM_CUT:
                    ok = False
        keep[v.index] = ok
    delete_vertices(body, ~keep)
    # fingers and thumb fold into the hand (the forearm only needs Arm / ForeArm / Hand)
    for side in ("Left", "Right"):
        hand = body.vertex_groups.get(side + "Hand")
        for part in ("Fingers1", "Fingers2", "Thumb"):
            g = body.vertex_groups.get(side + part)
            if g is None:
                continue
            for v in me.vertices:
                for e in v.groups:
                    if e.group == g.index and e.weight > 0:
                        hand.add([v.index], e.weight, "ADD")
            body.vertex_groups.remove(g)
    for g in list(body.vertex_groups):
        if g.name not in [s + p for s in ("Left", "Right") for p in ARM_PARTS]:
            body.vertex_groups.remove(g)
    select_only(body)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    for m in list(body.modifiers):
        body.modifiers.remove(m)
    body.parent = None
    print(f"  low cut: {len(me.vertices)} vertices, {len(me.polygons)} faces")
    return body


def hand_axis_param(points, src_bones, side):
    """Distance past the wrist along the hand, for each point (numpy)."""
    b = src_bones[side + "Hand"]
    wrist = np.array(b.head_local)
    axis = np.array((b.tail_local - b.head_local).normalized())
    return (points - wrist) @ axis


def body_uvs(source, high):
    """UVs for the high-res forearm from the game body, each face inside one UV-continuous patch of the body.

    The source is the whole body, not the low cut: the cut drops every body triangle touching the removed top of the
    upper arm, and near the elbow those are large skin triangles, so faces over that hole would land on its border.
    Per-corner nearest-face interpolation (what Data Transfer does) breaks at UV seams: the corners of a face that
    straddles a seam land on both sides of it, and the face stretches across the atlas (black/foreign texels on the
    skin). Seams also cut through a single island (an arm unrolled into a strip has both ends in one island), so
    staying on one island is not enough. Each face therefore picks the body triangle nearest its centre and maps its
    corners onto the patch reachable from there across UV-continuous edges within CHART_RADIUS (a disc on the arm,
    never wrapping round it)."""
    import bmesh
    from mathutils.geometry import barycentric_transform, closest_point_on_tri, intersect_point_tri_2d
    bm = bmesh.new()
    bm.from_mesh(source.data)
    bm.transform(source.matrix_world)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    uv = bm.loops.layers.uv.active
    tris = [[(l.vert.co.copy(), l[uv].uv.to_3d()) for l in f.loops] for f in bm.faces]
    bm.free()

    # adjacency across edges whose UVs match on both sides (by position: glTF splits vertices at seams and normals)
    def key(co):
        return tuple(round(x, 5) for x in co)

    edges = {}
    for t, corners in enumerate(tris):
        for i in range(3):
            (pa, ua), (pb, ub) = corners[i], corners[(i + 1) % 3]
            ka, kb = key(pa), key(pb)
            if kb < ka:
                ka, kb, ua, ub = kb, ka, ub, ua
            edges.setdefault((ka, kb), []).append((t, ua, ub))
    adj = [[] for _ in tris]
    for sides in edges.values():
        for i in range(len(sides)):
            for j in range(i + 1, len(sides)):
                (s, ua, ub), (t, va, vb) = sides[i], sides[j]
                if (ua - va).length < 1e-5 and (ub - vb).length < 1e-5:
                    adj[s].append(t)
                    adj[t].append(s)

    def distance(co, t):
        (a, _), (b, _), (c, _) = tris[t]
        return (closest_point_on_tri(co, a, b, c) - co).length

    every = BVHTree.FromPolygons([p for corners in tris for p, _ in corners],
                                 [(3 * t, 3 * t + 1, 3 * t + 2) for t in range(len(tris))])
    charts = {}

    def chart(t):
        if t not in charts:
            centre = sum((p for p, _ in tris[t]), Vector()) / 3
            seen, todo = {t}, [t]
            while todo:
                for n in adj[todo.pop()]:
                    if n not in seen and distance(centre, n) < CHART_RADIUS:
                        seen.add(n)
                        todo.append(n)
            charts[t] = list(seen)
        return charts[t]

    def project(cos, patch):
        uvs, used = [], set()
        for co in cos:
            best = min(patch, key=lambda n: distance(co, n))
            (a, ua), (b, ub), (c, uc) = tris[best]
            uvs.append(barycentric_transform(closest_point_on_tri(co, a, b, c), a, b, c, ua, ub, uc).xy)
            used.add(best)
        return uvs, used

    def covered(uvs, patch):
        """Every point along the face's UV edges (about every 2 texels at 4K) lies on one of the patch's UV
        triangles, i.e. the face does not cross a gap between islands."""
        flat = [[u.xy for _, u in tris[n]] for n in patch]
        for i in range(len(uvs)):
            a, b = uvs[i - 1], uvs[i]
            steps = max(2, int((b - a).length / 0.0005))
            for s in range(steps + 1):
                q = a.lerp(b, s / steps)
                if not any(intersect_point_tri_2d(q, *f) for f in flat):
                    return False
        return True

    def stretch(t, cos, uvs):
        """Longest UV edge of the face against the source triangle's texel density (1 = undistorted)."""
        (a, ua), (b, ub), (c, uc) = tris[t]
        area3 = (b - a).cross(c - a).length
        if area3 < 1e-12:
            return 1.0
        scale = math.sqrt((ub - ua).cross(uc - ua).length / area3)
        return max((uvs[i] - uvs[i - 1]).length / max((cos[i] - cos[i - 1]).length * scale, 1e-9)
                   for i in range(len(cos)))

    me = high.data
    layer = me.uv_layers["UVMap"]
    hw = high.matrix_world
    clamped = single = stretched = 0
    for p in me.polygons:
        t = every.find_nearest(hw @ p.center)[2]
        patch = chart(t)
        cos = [hw @ me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
        uvs, used = project(cos, patch)
        clamped += any(every.find_nearest(co)[2] not in patch for co in cos)
        if len(used) > 1 and (stretch(t, cos, uvs) > 3.0 or not covered(uvs, patch)):
            # the corners sit on both sides of a seam that ends inside the patch, or the face would cross a gap in
            # the atlas: keep the whole face on one triangle
            uvs, _ = project(cos, [t])
            single += 1
            stretched += stretch(t, cos, uvs) > 3.0
        for li, u in zip(p.loop_indices, uvs):
            layer.data[li].uv = u
    print(f"  forearm UVs: {len(charts)} body patches, {clamped} faces kept off a seam, "
          f"{single} faces mapped onto a single triangle, {stretched} still stretched > 3x")


def high_forearms(low, src_body, src_bones):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=HIGHRES)
    high = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
    high.parent = None
    hco = coords(high)
    bco = coords(src_body)
    hmin, hmax, bmin, bmax = hco.min(0), hco.max(0), bco.min(0), bco.max(0)
    s = (bmax[2] - bmin[2]) / (hmax[2] - hmin[2])
    t = np.array([(bmin[0] + bmax[0]) / 2 - s * (hmin[0] + hmax[0]) / 2,
                  (bmin[1] + bmax[1]) / 2 - s * (hmin[1] + hmax[1]) / 2,
                  bmin[2] - s * hmin[2]])
    high.matrix_world = Matrix.Translation(Vector(t)) @ Matrix.Scale(s, 4) @ high.matrix_world
    select_only(high)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    co = coords(high)
    lco = coords(low)
    pad = 0.03
    inside = np.all((co > lco.min(0) - pad) & (co < lco.max(0) + pad), axis=1) & (np.abs(co[:, 0]) > 0.12)
    for side in ("Left", "Right"):
        on_side = (co[:, 0] > 0) if side == "Left" else (co[:, 0] < 0)
        inside &= ~(on_side & (hand_axis_param(co, src_bones, side) > WRIST_CUT))
    bvh = BVHTree.FromObject(low, bpy.context.evaluated_depsgraph_get())
    keep = np.zeros(len(co), dtype=bool)
    for i in np.nonzero(inside)[0]:
        if bvh.find_nearest(Vector(co[i]), KEEP_DISTANCE)[0] is not None:
            keep[i] = True
    delete_vertices(high, ~keep)
    faces = len(high.data.polygons)
    if faces > FOREARM_FACES:
        dec = high.modifiers.new("Decimate", "DECIMATE")
        dec.ratio = FOREARM_FACES / faces
        select_only(high)
        bpy.ops.object.modifier_apply(modifier="Decimate")
    select_only(high)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0003)
    bpy.ops.mesh.delete_loose()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth()

    me = high.data
    me.materials.clear()  # drop the high-res import's own material: one slot, the body's
    for slot in low.material_slots:
        me.materials.append(slot.material)
    for g in low.vertex_groups:
        high.vertex_groups.new(name=g.name)
    dt = high.modifiers.new("Transfer", "DATA_TRANSFER")
    dt.object = low
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    select_only(high)
    bpy.ops.object.modifier_apply(modifier="Transfer")
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    me.uv_layers.new(name="UVMap")  # one UV layer, same name as the hands' (joined below)
    body_uvs(src_body, high)
    bpy.data.objects.remove(low, do_unlink=True)
    print(f"  forearms (high-res fit, scale {s:.4f}): {len(me.vertices)} vertices, {len(me.polygons)} faces")
    return high


def main():
    args = script_args()
    check = args[args.index("--check") + 1] if "--check" in args else None
    src_arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    src_body = next(o for o in bpy.data.objects if o.type == "MESH")
    src = src_arm.data.bones
    material = src_body.material_slots[0].material

    from fox_motion import Rig
    rig = Rig(src_arm)
    uv_points = sample_uv_points(src_body, src_arm)
    arm = build_armature(src)
    forearms = high_forearms(low_cut(src_body, src), src_body, src)

    hands = []
    for side in ("Right", "Left"):
        pose, open_key = HAND_POSE[side]
        rest = rig.hand_rest[side]
        hand = build_hand(side, rest["m"], rest["wrist"], uv_points, pose, open_key)
        hand.data.materials.clear()
        hand.data.materials.append(material)
        g = hand.vertex_groups.new(name=side + "Hand")
        g.add(list(range(len(hand.data.vertices))), 1.0, "REPLACE")
        hands.append(hand)

    # join into the left hand (it carries the shape keys; the others take the basis for "Open")
    left = hands[1]
    bpy.ops.object.select_all(action="DESELECT")
    for o in (forearms, hands[0], left):
        o.select_set(True)
    bpy.context.view_layer.objects.active = left
    bpy.ops.object.join()
    arms = left
    arms.name = arms.data.name = "FoxGunner_FPArms"
    arms.parent = arm
    arms.matrix_parent_inverse = Matrix.Identity(4)
    for m in list(arms.modifiers):
        arms.modifiers.remove(m)
    mod = arms.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    print(f"  first-person arms: {len(arms.data.vertices)} vertices, {len(arms.data.polygons)} faces, "
          f"shape keys {[k.name for k in arms.data.shape_keys.key_blocks]}")

    if check:
        render_check(rig, arm, arms, check)

    for o in [o for o in bpy.data.objects if o not in (arm, arms)]:
        bpy.data.objects.remove(o, do_unlink=True)
    bake_rig_transform(arm, [arms], TURN_FOR_UNITY)
    export_fbx(OUT, [arm, arms], bake_anim=False)
    print("FP_ARMS_DONE")


def render_check(rig, fp_arm, arms, out):
    """The arms posed like the stance, gripping the rifle: right hand on the pistol grip, left on the handguard
    (rifle moved back by the first-person grip shift for the left shot)."""
    from fox_render import PROP_FIX, Renderer
    from fox_stance import Stance, base_controls
    st = Stance()
    ctl = base_controls(st)
    D = rig.solve(ctl)
    for pb in fp_arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    # first-person left hand on FP_Grip_L (the body's arm cannot reach that far: slide the whole arm forward)
    grip = ctl.weapon @ rig.rest["Weapon"].inverted() @ rig.rest["FP_Grip_L"]
    reached = D["LeftHand"] @ rig.rest["LeftHand"]
    slide = Matrix.Translation(grip.translation - reached.translation)
    for side in ("Left", "Right"):
        for part in ARM_PARTS:
            name = side + part
            m = D[name] @ rig.rest[name]
            if side == "Left":
                m = grip if part == "Hand" else slide @ m
            fp_arm.pose.bones[name].matrix = m
            bpy.context.view_layer.update()
    src_body = [o for o in bpy.data.objects if o.type == "MESH" and o is not arms]
    for o in src_body:
        o.hide_render = True
    r = Renderer(fp_arm, res=(900, 700))
    shift = Matrix.Translation(-FP_LEFT_SHIFT)
    shots = {
        "right": (ctl.weapon @ PROP_FIX, ((-0.6, -0.15, 1.15), (-0.1, -0.2, 0.93))),
        "right_under": (ctl.weapon @ PROP_FIX, ((-0.3, -0.5, 0.6), (-0.1, -0.2, 0.93))),
        "left": (ctl.weapon @ PROP_FIX, ((0.45, -0.85, 1.12), (-0.08, -0.62, 0.97))),
        "left_under": (ctl.weapon @ PROP_FIX, ((0.15, -0.95, 0.62), (-0.08, -0.62, 0.97))),
        "left_front": (ctl.weapon @ PROP_FIX, ((-0.1, -1.25, 0.9), (-0.08, -0.62, 0.97))),
    }
    # the view-model camera in rifle space, measured from the Unity first-person capture
    from fox_stance import rs
    fp_eye = rs(ctl.weapon, (-0.20, -0.33, 0.21))  # from the Unity capture: right hand at (0.24, -0.23, 0.35) in camera space
    fp_target = rs(ctl.weapon, (-0.10, 0.67, 0.21))
    shots["fp_view"] = (ctl.weapon @ PROP_FIX, (tuple(fp_eye), tuple(fp_target)))
    for name, (rifle_m, (eye, target)) in shots.items():
        r.prop.matrix_world = rifle_m
        r.cam.data.type = "PERSP"
        r.cam.data.lens = 35
        if name == "fp_view":
            r.cam.data.sensor_fit = "VERTICAL"
            r.cam.data.angle = math.radians(55.0)
            r.sc.render.resolution_x, r.sc.render.resolution_y = 1280, 720
        r.cam.location = Vector(eye)
        r.cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
        r.sc.render.filepath = os.path.join(out, f"fpcheck_{name}.png")
        bpy.ops.render.render(write_still=True)
    for pb in fp_arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()


main()
