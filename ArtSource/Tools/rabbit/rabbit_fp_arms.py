"""First-person arms cut from the Lightning Rabbit's high-resolution sculpt: its own furred arms, gloves and fingers.

    blender -b ArtSource/LightningRabbit/Animation/LightningRabbit_Rig.blend -P rabbit_fp_arms.py -- \
        --esrgan <dir with realesrgan-ncnn-vulkan.exe> [--faces 40000] [--size 4096] [--check <dir>] [--out <dir>]

The fox's procedure (fox_anim/fox_fp_arms.py, which runs on import and is not changed) on the rabbit:
  1. region: the rigged body's arm vertices (from a quarter down the upper arm to the finger tips) and the sculpt's
     faces within 12 mm of them (the sculpt fitted to the prepared body by bounding box, as refine_character.py does);
     pieces under 500 vertices dropped; quadric decimation to --faces, even UVs at --size
  2. colour from the prepared body through Meshy's UVs and its 4K atlas upscaled to 8K (Real-ESRGAN x4, averaged 2x2:
     rebuild_prop_hq.upscale_to_double), normals from the full sculpt. No surface mask yet (the rabbit's part mask is
     Phase 9; Unity uses a neutral mask until then)
  3. skin: every arm, hand and finger weight from the rigged body (it already has five fingers with three joints
     each); the bones keep the body's rest, so the arms have the rabbit's own proportions
  4. bones named for FirstPersonArms: Root; per side Arm, ForeArm, Hand, Thumb/Index/Middle/Ring/Pinky 1-3 (the body's
     <Side>Hand<Digit>1-3), and the non-deforming markers Palm and PalmOut (Unity curls the fingers toward PalmOut)

Outputs (staging, copied into Assets in Phase 8): <out>/LightningRabbit_FPArms.fbx,
T_LightningRabbit_FPArms_BaseColor.png, T_LightningRabbit_FPArms_Normal.png. --check renders the arms unposed and
with the fingers curled.
"""
import argparse
import math
import os
import sys
from types import SimpleNamespace

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "fox_anim"))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

import refine_character as rc  # noqa: E402
from fox_common import bake_rig_transform, export_fbx  # noqa: E402
from rabbit_common import SRC, STAGING, TURN_FOR_UNITY, script_args, select_only  # noqa: E402
from rebuild_prop_hq import upscale_to_double  # noqa: E402

HIGHRES = os.path.join(SRC, "Meshy", "01_generate", "LightningRabbit_highres.glb")
PREPARED = os.path.join(SRC, "Export", "LightningRabbit.glb")
ATLAS_X2 = os.path.join(SRC, "Export", "LightningRabbit_atlas_x2.png")

ARM_PARTS = ("Arm", "ForeArm", "Hand")
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
UPPER_ARM_CUT = 0.25        # keep the upper arm from a quarter of the way down (the shoulder never shows)
KEEP_DISTANCE = 0.012       # sculpt vertices this close to the body's arms belong to them
CRUMB = 500                 # pieces with fewer vertices are bits of the torso caught by the distance test
# palm markers: the rabbit's gloved palm surface is about 3 cm under the hand bone (fox 1.4 cm)
PALM_ALONG, PALM_IN, PALM_OUT = 0.055, 0.03, 0.03


def parse():
    p = argparse.ArgumentParser()
    p.add_argument("--esrgan", required=True)
    p.add_argument("--esrgan-model", default="realesrgan-x4plus-anime")
    p.add_argument("--faces", type=int, default=40000)
    p.add_argument("--size", type=int, default=4096)
    p.add_argument("--check")
    p.add_argument("--out", default=STAGING)
    return p.parse_args(script_args())


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


def arm_groups(side):
    return [side + p for p in ARM_PARTS] + [f"{side}Hand{d}{j}" for d in DIGITS for j in (1, 2, 3)]


# ---------------------------------------------------------------- 1. geometry

def arm_region(body, bones):
    """The rigged body's arm vertices, as a separate unrigged mesh in world space."""
    names = set(arm_groups("Left") + arm_groups("Right"))
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
    arms.name = arms.data.name = "LightningRabbit_FPArms"
    bpy.context.scene.collection.objects.link(arms)
    delete_vertices(arms, ~near)
    select_only(arms)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00001)
    bpy.ops.mesh.delete_loose()
    bpy.ops.object.mode_set(mode="OBJECT")
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
        if len(comp) < CRUMB:
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


# ---------------------------------------------------------------- 2. textures

def atlas_x2(prepared, args):
    """Meshy's 4K atlas, dilated over the UV gaps and upscaled to 8K (kept beside the prepared body for reuse)."""
    if os.path.exists(ATLAS_X2):
        print(f"  reusing {ATLAS_X2}")
        return ATLAS_X2
    node = next(n for n in prepared.material_slots[0].material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    img = node.image
    size = img.size[0]
    valid = rc.rasterize(rc.uv_triangles(prepared.data, prepared.data.uv_layers.active), size)
    rgb = rc.image_array(img)[..., :3]
    dilated = os.path.join(os.path.dirname(ATLAS_X2), "_LightningRabbit_atlas_dilated.png")
    rc.save_rgb(rc.push_pull_fill(rgb, valid), dilated, img.colorspace_settings.name)
    upscale_to_double(dilated, ATLAS_X2, args.esrgan, args.esrgan_model)
    os.remove(dilated)
    return ATLAS_X2


def bake_textures(arms, prepared, high, args, base_png, normal_png):
    covered = rc.rasterize(rc.uv_triangles(arms.data, arms.data.uv_layers["UVMap"]), args.size)
    rgb, hit = rc.bake_surface_image(prepared, prepared.data.uv_layers.active.name, atlas_x2(prepared, args), arms,
                                     args.size, cage=0.004, ray=0.008)
    rgb, filled = rc.fill_within(rgb, hit, covered)
    print(f"  colour: {np.mean(~hit[covered]):.3%} of texels missed after a wider search, "
          f"{int((covered & ~filled).sum())} left after filling inside the islands")
    rgb = rc.push_pull_fill(rgb, (covered & filled) | (~covered & hit))
    rc.save_rgb(rgb, base_png)
    ns = SimpleNamespace(size=args.size, cage=0.004, ray=0.008, max_lean=75.0)
    rc.bake_normal_image(arms, high, ns, normal_png)


# ---------------------------------------------------------------- 3-4. skin and bones

def transfer_weights(arms, body):
    for side in ("Left", "Right"):
        for name in arm_groups(side):
            arms.vertex_groups.new(name=name)
    dt = arms.modifiers.new("Weights", "DATA_TRANSFER")
    dt.object = body
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    select_only(arms)
    bpy.ops.object.modifier_apply(modifier="Weights")
    for g in list(arms.vertex_groups):   # torso, neck or scarf weights near the cut go to the arms' own bones
        if g.name not in arm_groups("Left") + arm_groups("Right"):
            arms.vertex_groups.remove(g)
    for side in ("Left", "Right"):       # body names -> FirstPersonArms names (LeftHandIndex1 -> LeftIndex1)
        for d in DIGITS:
            for j in (1, 2, 3):
                arms.vertex_groups[f"{side}Hand{d}{j}"].name = f"{side}{d}{j}"
    select_only(arms)
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def hand_axes(bones, side):
    """Wrist, along (toward the finger tips) and back-of-hand axes in the rest pose (palm down in the T-pose)."""
    b = bones[side + "Hand"]
    wrist, a = b.head_local.copy(), (b.tail_local - b.head_local).normalized()
    front = Vector((0, -1, 0))
    front = (front - a * front.dot(a)).normalized()
    up = a.cross(front)
    if up.z < 0:
        up = -up
    return wrist, a, up


def build_rig(src_bones):
    data = bpy.data.armatures.new("FPArms_Rig")
    rig = bpy.data.objects.new("FPArms", data)
    bpy.context.scene.collection.objects.link(rig)
    select_only(rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = data.edit_bones
    root = eb.new("Root")
    root.head, root.tail = Vector((0, 0, 0)), Vector((0, 0, 0.1))

    def copy(src_name, name, parent):
        b = src_bones[src_name]
        e = eb.new(name)
        e.length = b.length
        e.matrix = b.matrix_local.copy()
        e.parent = parent
        e.use_connect = False
        return e

    for side in ("Left", "Right"):
        parent = root
        for part in ARM_PARTS:
            parent = copy(side + part, side + part, parent)
        hand = parent
        for d in DIGITS:
            p = hand
            for j in (1, 2, 3):
                p = copy(f"{side}Hand{d}{j}", f"{side}{d}{j}", p)
        wrist, a, up = hand_axes(src_bones, side)
        palm = wrist + a * PALM_ALONG - up * PALM_IN
        for name, head in ((side + "Palm", palm), (side + "PalmOut", palm - up * PALM_OUT)):
            e = eb.new(name)
            e.head, e.tail = head, head + a * 0.02
            e.align_roll(up)
            e.parent = hand
            e.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


# ---------------------------------------------------------------- check renders

def render_check(rig, arms, base_png, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    mat = bpy.data.materials.new("CheckTex")

    t = mat.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(base_png, check_existing=True)
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
    views = {"hand_top": (hand + Vector((-0.04, -0.14, 0.22)), hand + Vector((-0.06, 0, 0)), 40),
             "hand_front": (hand + Vector((-0.1, -0.28, 0.02)), hand + Vector((-0.06, 0, 0)), 40),
             "arm": (hand + Vector((0.25, -0.6, 0.15)), hand + Vector((0.1, 0, 0)), 32)}
    for pose_name in ("rest", "grip"):
        for side in ("Left", "Right"):
            for d in DIGITS:
                for j in (1, 2, 3):
                    pb = rig.pose.bones[f"{side}{d}{j}"]
                    pb.rotation_mode = "XYZ"
                    curl = 0.0 if pose_name == "rest" else ((14, 30, 28) if d == "Thumb" else (68, 82, 48))[j - 1]
                    pb.rotation_euler = (-math.radians(curl), 0, 0)   # the body rig's rolls: -X curls toward the palm
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
    os.makedirs(args.out, exist_ok=True)
    out_fbx = os.path.join(args.out, "LightningRabbit_FPArms.fbx")
    base_png = os.path.join(args.out, "T_LightningRabbit_FPArms_BaseColor.png")
    normal_png = os.path.join(args.out, "T_LightningRabbit_FPArms_Normal.png")
    rig_arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups)
    src_bones = rig_arm.data.bones
    material = body.material_slots[0].material

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=PREPARED)   # colour source: Meshy's UVs and atlas
    prepared = next(o for o in bpy.data.objects if o not in before and o.type == "MESH" and o.material_slots)
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
    bake_textures(arms, prepared, high, args, base_png, normal_png)

    transfer_weights(arms, body)
    rig = build_rig(src_bones)
    arms.parent = rig
    arms.matrix_parent_inverse = Matrix.Identity(4)
    for m in list(arms.modifiers):
        arms.modifiers.remove(m)
    arms.modifiers.new("Armature", "ARMATURE").object = rig
    unweighted = sum(1 for v in arms.data.vertices if not v.groups)
    print(f"  first-person arms: {len(arms.data.vertices)} vertices, {len(arms.data.polygons)} triangles, "
          f"{len(rig.data.bones)} bones, {unweighted} unweighted vertices")

    if args.check:
        render_check(rig, arms, base_png, os.path.abspath(args.check))
        arms.data.materials.clear()
        arms.data.materials.append(material)

    for o in [o for o in bpy.data.objects if o not in (rig, arms)]:
        bpy.data.objects.remove(o, do_unlink=True)
    bake_rig_transform(rig, [arms], TURN_FOR_UNITY)
    export_fbx(out_fbx, [rig, arms], bake_anim=False)
    print("FP_ARMS_DONE")


main()
