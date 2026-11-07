"""Hand-modelled first-person hands for the Fox Gunner (Blender Python, no external assets).

Built in each hand's rest frame from the body rig (wrist at the origin, a = toward the finger tips, t = toward
the thumb, w = back of the hand), so the hand fits the same grip placement as the third-person body:
  - organic surface from metaballs: palm, wrist cuff, four fingers (3 phalanges, knuckle bulges, tapered,
    slightly splayed), thumb (metacarpal + 2 phalanges); converted to a mesh and reduced to ~7k triangles
  - fingerless glove: palm, back and first finger joints in the body's black leather, fingers and thumb tip in
    skin, an orange knuckle plate; colours come from flat UV points on the body texture (same material and
    surface mask)
  - finger poses are rigid per phalanx with blended joints; the mesh is baked in its gripping pose and the left
    hand keeps an "Open" shape key (palm thrust for casts)
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

# canonical hand frame: x = a (to the finger tips), y = t (thumb side), z = w (back of the hand)
A, T, W = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

# finger name: base (s, t, w), splay (deg, + toward the thumb), segment lengths, base radius, tip radius
FINGERS = {
    "Index": ((0.060, 0.024, -0.001), 4.0, (0.032, 0.021, 0.018), 0.0090, 0.0074),
    "Middle": ((0.062, 0.007, 0.000), 0.0, (0.035, 0.023, 0.019), 0.0092, 0.0076),
    "Ring": ((0.060, -0.010, -0.001), -4.0, (0.032, 0.021, 0.018), 0.0088, 0.0073),
    "Pinky": ((0.055, -0.026, -0.003), -9.0, (0.025, 0.017, 0.016), 0.0080, 0.0068),
}
THUMB = ((0.012, 0.025, -0.008), (0.60, 0.75, -0.25), (0.030, 0.024, 0.020), 0.0108, 0.0084)
# the thumb bends across the palm toward the fingers (opposition), not straight down
THUMB_AXIS = Vector((-0.6, 0.0, -0.8)).normalized()

# curl angles (deg) per joint: MCP / PIP / DIP for fingers, CMC / MCP / IP for the thumb (+ toward the palm)
POSES = {
    "open": {"Index": (6, 6, 4), "Middle": (6, 6, 4), "Ring": (8, 7, 5), "Pinky": (10, 8, 6), "Thumb": (0, 4, 4),
             "spread": 1.6},
    "pistol": {"Index": (38, 42, 18), "Middle": (72, 88, 42), "Ring": (76, 90, 44), "Pinky": (80, 88, 44),
               "Thumb": (34, 26, 22), "spread": 0.6},
    "handguard": {"Index": (40, 66, 38), "Middle": (44, 72, 42), "Ring": (48, 74, 42), "Pinky": (52, 76, 44),
                  "Thumb": (18, 14, 10), "spread": 0.7},
}
UV_NAME = "UVMap"
GLOVE_FINGER = 0.38    # glove covers this share of the first phalanx
GLOVE_THUMB = 0.2      # ... and of the thumb's first phalanx (beyond the metacarpal)
FIELD = 1.0 / 0.575    # metaball radius per surface radius (stiffness 2, threshold 0.6)


def finger_chain(name, pose, open_spread=1.0):
    """Joint positions (canonical) and per-segment rotations for a finger in a pose."""
    if name == "Thumb":
        base, direction, lengths, r0, r1 = THUMB
        d = Vector(direction).normalized()
        side = THUMB_AXIS
        angles = pose["Thumb"]
    else:
        base, splay, lengths, r0, r1 = FINGERS[name]
        sp = splay * pose.get("spread", 1.0)
        d = Matrix.Rotation(math.radians(sp), 3, "Z") @ A
        side = Matrix.Rotation(math.radians(sp), 3, "Z") @ T  # curl about the finger's lateral axis
        angles = pose[name]
    points = [Vector(base)]
    rots = []
    total = Matrix.Identity(3)
    for length, angle in zip(lengths, angles):
        total = Matrix.Rotation(math.radians(angle), 3, side) @ total
        rots.append(total.copy())
        points.append(points[-1] + total @ d * length)
    return points, rots, (r0, r1)


def build_metaballs(pose_name="open"):
    pose = POSES[pose_name]
    mb = bpy.data.metaballs.new("hand_mb")
    mb.resolution = 0.0016
    mb.render_resolution = 0.0016
    mb.threshold = 0.6
    obj = bpy.data.objects.new("hand_mb", mb)
    bpy.context.scene.collection.objects.link(obj)

    def ball(p, r, stiff=2.0):
        e = mb.elements.new(type="BALL")
        e.co = p
        e.radius = r * FIELD
        e.stiffness = stiff

    def capsule(p0, p1, r, stiff=2.0):
        e = mb.elements.new(type="CAPSULE")
        mid = (p0 + p1) / 2
        e.co = mid
        e.radius = r * FIELD
        e.size_x = (p1 - p0).length / 2
        e.rotation = A.rotation_difference((p1 - p0).normalized())
        e.stiffness = stiff

    def ellipsoid(p, sx, sy, sz, stiff=2.0):
        e = mb.elements.new(type="ELLIPSOID")
        e.co = p
        e.radius = 1.0 * FIELD
        e.size_x, e.size_y, e.size_z = sx, sy, sz
        e.stiffness = stiff

    # palm: a flattened rounded block made of overlapping ellipsoids, thicker at the heel
    ellipsoid(Vector((0.030, 0.000, -0.002)), 0.034, 0.038, 0.013)
    ellipsoid(Vector((0.048, -0.004, 0.000)), 0.020, 0.036, 0.011)
    ellipsoid(Vector((0.014, 0.014, -0.006)), 0.016, 0.018, 0.012)   # thumb pad
    ellipsoid(Vector((0.016, -0.022, -0.004)), 0.016, 0.014, 0.011)  # heel, pinky side
    # glove cuff around the wrist (slides under the forearm bracer)
    capsule(Vector((-0.006, 0.0, -0.001)), Vector((0.008, 0.0, -0.001)), 0.0180)
    for name in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
        pts, _, (r0, r1) = finger_chain(name, pose)
        n = len(pts) - 1
        for i in range(n):
            ra = r0 + (r1 - r0) * i / n
            rb = r0 + (r1 - r0) * (i + 1) / n
            capsule(pts[i], pts[i + 1], (ra + rb) / 2)
            ball(pts[i], ra * 1.04, 1.6)  # knuckle bulge
        ball(pts[-1] - (pts[-1] - pts[-2]).normalized() * r1 * 0.4, r1 * 0.98, 2.0)  # rounded tip
    return obj


def to_mesh(obj, name, target_faces):
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    mesh_obj = bpy.context.view_layer.objects.active
    mesh_obj.name = name
    # marching-cubes output: relax the small steps, then subdivide once for a smooth, dense surface
    smooth = mesh_obj.modifiers.new("Relax", "SMOOTH")
    smooth.factor = 0.5
    smooth.iterations = 3
    bpy.ops.object.modifier_apply(modifier="Relax")
    sub = mesh_obj.modifiers.new("Subdivide", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1
    bpy.ops.object.modifier_apply(modifier="Subdivide")
    faces = len(mesh_obj.data.polygons)
    if faces > target_faces:
        dec = mesh_obj.modifiers.new("Decimate", "DECIMATE")
        dec.ratio = target_faces / faces
        bpy.ops.object.modifier_apply(modifier="Decimate")
    bpy.ops.object.shade_smooth()
    for mb in list(bpy.data.metaballs):
        if mb.users == 0:
            bpy.data.metaballs.remove(mb)
    return mesh_obj


def knuckle_plate():
    """A rounded orange plate on the back of the glove, over the knuckles (separate shell)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(0.022, 0.050, 0.006), verts=bm.verts)
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.0025, segments=3, affect="EDGES")
    bmesh.ops.translate(bm, vec=(0.046, 0.0, 0.0125), verts=bm.verts)
    me = bpy.data.meshes.new("plate")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("plate", me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ---------------------------------------------------------------- skinning to phalanges and posing

def segment_table(pose_name):
    """[(owner, index, p0, p1, rotation)] for every phalanx in a pose (rotation: open -> pose is computed later)."""
    table = []
    for name in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
        pts, rots, radii = finger_chain(name, POSES[pose_name])
        for i in range(len(pts) - 1):
            table.append((name, i, pts[i], pts[i + 1], rots[i], radii))
    return table


def closest_param(p, a, b):
    ab = b - a
    t = (p - a).dot(ab) / max(1e-12, ab.length_squared)
    return t, (a + ab * max(0.0, min(1.0, t)) - p).length


def classify(points):
    """Per vertex: list of (segment key, weight); palm = None. Uses the open pose."""
    open_table = segment_table("open")
    weights = []
    for p in points:
        best = None
        for name, i, a, b, _, radii in open_table:
            t, d = closest_param(p, a, b)
            r = radii[0] + (radii[1] - radii[0]) * (i + 0.5) / 3
            if t < -0.25 or d > r * 1.9:
                continue
            score = d - (0.002 if 0 <= t <= 1 else 0.0)
            if best is None or score < best[0]:
                best = (score, name, i, t, (b - a).length)
        if best is None:
            weights.append([(None, 1.0)])
            continue
        _, name, i, t, length = best
        blend = 0.0055 / length  # joint softening, in segment-length units
        if t < blend:  # near the proximal joint: share with the parent (previous phalanx or the palm)
            k = 0.5 + 0.5 * t / blend if t > -blend else 0.0
            parent = (name, i - 1) if i > 0 else None
            weights.append([((name, i), max(0.0, min(1.0, k))), (parent, 1.0 - max(0.0, min(1.0, k)))])
        elif t > 1 - blend and i < 2:
            k = 0.5 + 0.5 * (1 - t) / blend
            weights.append([((name, i), k), ((name, i + 1), 1.0 - k)])
        else:
            weights.append([((name, i), 1.0)])
    return weights


def pose_points(points, weights, pose_name):
    """Moves open-pose points into a pose: each phalanx is a rigid transform from its open placement."""
    open_t = {(n, i): (a, r) for n, i, a, b, r, _ in segment_table("open")}
    pose_t = {(n, i): (a, r) for n, i, a, b, r, _ in segment_table(pose_name)}
    xf = {}
    for key, (a0, r0) in open_t.items():
        a1, r1 = pose_t[key]
        rot = r1 @ r0.inverted()
        xf[key] = (a0, a1, rot)
    out = []
    for p, ws in zip(points, weights):
        q = Vector()
        for key, w in ws:
            if key is None:
                q += p * w
            else:
                a0, a1, rot = xf[key]
                q += (a1 + rot @ (p - a0)) * w
        out.append(q)
    return out


# ---------------------------------------------------------------- colours (UV points on the body texture)

def region(p, weights, open_segments):
    """'glove' or 'skin' for a vertex of the open-pose hand."""
    keys = [k for k, w in weights if w > 0.5 and k is not None]
    if not keys:
        return "glove"  # palm, back, wrist cuff
    name, i = keys[0]
    if name == "Thumb":
        if i == 0:
            return "glove"
        if i == 1:
            a, b = open_segments[(name, 1)]
            return "glove" if closest_param(p, a, b)[0] < GLOVE_THUMB else "skin"
        return "skin"
    if i == 0:
        a, b = open_segments[(name, 0)]
        return "glove" if closest_param(p, a, b)[0] < GLOVE_FINGER else "skin"
    return "skin"


def assign_uvs(mesh_obj, uv_points, regions_per_vertex=None, plate=False):
    me = mesh_obj.data
    # exactly one UV layer named like the body's ("UVMap"), so joining with the forearms keeps one layer
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name=UV_NAME)
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        # one colour per face (the majority of its corners): a clean glove edge instead of blended UVs between
        # far-apart texture points, which would smear random colours along the boundary
        if plate:
            key = "accent"
        else:
            corner = [regions_per_vertex[me.loops[li].vertex_index] for li in poly.loop_indices]
            key = max(set(corner), key=corner.count)
        for li in poly.loop_indices:
            uv[li].uv = uv_points[key]


def transform_to_hand(mesh_obj, frame, wrist):
    """Canonical (a, t, w) -> armature space of the body's rest hand (shape keys move along)."""
    m = frame.to_4x4()
    m.translation = wrist
    mesh_obj.data.transform(m, shape_keys=True)
    if frame.determinant() < 0:  # the left hand's frame is mirrored: keep the faces pointing out
        mesh_obj.data.flip_normals()
    mesh_obj.data.update()


def build_hand(side, frame, wrist, uv_points, pose_name, keep_open):
    """One finished hand mesh in armature space: the gripping pose, plus an 'Open' shape key if asked."""
    mb = build_metaballs("open")
    hand = to_mesh(mb, f"FPHand_{side}", 9000)
    me = hand.data
    open_pts = [v.co.copy() for v in me.vertices]
    weights = classify(open_pts)
    open_segments = {(n, i): (a, b) for n, i, a, b, _, _ in segment_table("open")}
    regions = [region(p, w, open_segments) for p, w in zip(open_pts, weights)]
    posed = pose_points(open_pts, weights, pose_name)
    for v, p in zip(me.vertices, posed):
        v.co = p
    assign_uvs(hand, uv_points, regions)
    plate = knuckle_plate()  # rides the back of the hand (palm), same in every pose
    assign_uvs(plate, uv_points, plate=True)
    if keep_open:
        hand.shape_key_add(name="Basis", from_mix=False)
        key = hand.shape_key_add(name="Open", from_mix=False)
        for i, p in enumerate(open_pts):
            key.data[i].co = p
        plate.shape_key_add(name="Basis", from_mix=False)
        plate.shape_key_add(name="Open", from_mix=False)
    bpy.ops.object.select_all(action="DESELECT")
    plate.select_set(True)
    hand.select_set(True)
    bpy.context.view_layer.objects.active = hand
    bpy.ops.object.join()
    transform_to_hand(hand, frame, wrist)
    print(f"  {side} hand: {len(hand.data.vertices)} vertices, {len(hand.data.polygons)} faces, pose '{pose_name}'"
          + (", shape key 'Open'" if keep_open else ""))
    return hand


def sample_uv_points(body, arm):
    """UV points on the body texture for the glove leather, the skin of the fingers and an orange accent,
    taken from the body's own hand/forearm vertices (the median colour of each kind)."""
    from fox_segment import dominant_groups, vertex_colours
    col = vertex_colours(body)
    dom = dominant_groups(body)
    me = body.data
    uvl = me.uv_layers.active.data
    first_loop = {}
    for poly in me.polygons:
        for li in poly.loop_indices:
            first_loop.setdefault(me.loops[li].vertex_index, li)
    hand_groups = {s + p for s in ("Left", "Right") for p in ("Hand", "ForeArm", "Fingers1", "Fingers2", "Thumb")}
    kinds = {"glove": [], "skin": [], "accent": []}
    for v in me.vertices:
        if v.index not in first_loop:
            continue
        h, s, val = col[v.index]
        if 0.04 < h < 0.12 and s > 0.75 and val > 0.85:  # orange accents anywhere on the outfit (boots, emblems)
            kinds["accent"].append((h, v.index))
        if dom[v.index] not in hand_groups:
            continue
        if val < 0.16:
            kinds["glove"].append((val, v.index))
        elif dom[v.index].endswith(("Fingers1", "Fingers2")) and 0.02 < h < 0.11 and 0.25 < s < 0.75 and val > 0.45:
            kinds["skin"].append((h, v.index))
    # Pick, for each kind, the candidate whose texture neighbourhood is the most uniform: a flat UV point next to
    # a colour edge shows specks once the texture is filtered or mip-mapped.
    import numpy as np
    img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, :, :3]
    radius = 10  # texels (2048 texture): covers the lower mips too

    def spread(uv):
        x, y = int(uv[0] * w), int(uv[1] * h)
        if x < radius or y < radius or x >= w - radius or y >= h - radius:
            return 9.0
        patch = px[y - radius:y + radius + 1, x - radius:x + radius + 1]
        return float(patch.std(axis=(0, 1)).sum())

    out = {}
    for kind, items in kinds.items():
        if not items:
            raise RuntimeError(f"no {kind} colour found on the body's hands")
        items.sort()
        middle = items[len(items) // 4: max(len(items) // 4 + 1, 3 * len(items) // 4)]  # typical colours only
        best = min(middle, key=lambda it: spread(uvl[first_loop[it[1]]].uv))
        vi = best[1]
        out[kind] = uvl[first_loop[vi]].uv.copy()
        print(f"  {kind}: {len(items)} samples, uv {tuple(round(c, 4) for c in out[kind])}, "
              f"hsv {tuple(round(c, 2) for c in col[vi])}, neighbourhood spread {spread(out[kind]):.3f}")
    return out
