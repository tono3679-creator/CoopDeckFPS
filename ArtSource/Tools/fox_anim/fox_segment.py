"""Finds the tail, ear and finger vertices of the Fox Gunner mesh (the Meshy auto-rig has no bones there).

The welded mesh is one connected piece, so regions are found from position, texture colour and the
original skin weights:
  tail   - flood fill from the tip through fur-coloured vertices behind the hips that Meshy weighted to
           the thighs (the red coat flap is cloth-coloured, the scarf ribbon is weighted to the head)
  ears   - head vertices above the ear base, left/right of the skull, behind the goggles
  hands  - per hand, vertices past the glove edge along the hand axis; the thumb sticks out forward
"""
import colorsys
from collections import deque

from mathutils import Vector


def vertex_colours(body):
    """HSV of the base colour texture under each vertex."""
    me = body.data
    img = None
    for n in me.materials[0].node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image:
            img = n.image
    w, h = img.size
    px = img.pixels[:]
    uvl = me.uv_layers.active.data
    col = [None] * len(me.vertices)
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            if col[vi] is None:
                uv = uvl[li].uv
                x = min(w - 1, max(0, int(uv[0] * w)))
                y = min(h - 1, max(0, int(uv[1] * h)))
                i = (y * w + x) * 4
                col[vi] = colorsys.rgb_to_hsv(px[i], px[i + 1], px[i + 2])
    return col


def neighbours(body):
    """Vertex adjacency, joining vertices that share a position (the GLB splits them at UV seams)."""
    me = body.data
    key = {}
    for v in me.vertices:
        key.setdefault((round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5)), []).append(v.index)
    adj = [set() for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        adj[a].add(b)
        adj[b].add(a)
    for group in key.values():
        for a in group:
            for b in group:
                if a != b:
                    adj[a].add(b)
    return adj


def dominant_groups(body):
    names = {g.index: g.name for g in body.vertex_groups}
    out = []
    for v in body.data.vertices:
        g = max(v.groups, key=lambda g: g.weight) if v.groups else None
        out.append(names[g.group] if g else None)
    return out


def is_fur(hsv):
    h, s, v = hsv
    cream = s < 0.45 and v > 0.72
    orange = 0.025 <= h <= 0.14 and s >= 0.45 and v > 0.62
    return cream or orange


TAIL_GROUPS = {"LeftUpLeg", "RightUpLeg", "Hips", "Spine02"}


def find_tail(body, col, adj, dom, min_y=0.07, max_z=0.86):
    me = body.data

    def ok(i):
        p = me.vertices[i].co
        return p.y > min_y and p.z < max_z and dom[i] in TAIL_GROUPS and is_fur(col[i])

    seed = max((v for v in me.vertices if ok(v.index)), key=lambda v: v.co.y)
    tail = {seed.index}
    queue = deque([seed.index])
    while queue:
        a = queue.popleft()
        for b in adj[a]:
            if b not in tail and ok(b):
                tail.add(b)
                queue.append(b)
    # Close holes left by darker texture inside the fur: vertices mostly surrounded by tail vertices.
    for _ in range(3):
        extra = set()
        for a in tail:
            for b in adj[a]:
                if b in tail or me.vertices[b].co.y <= min_y or dom[b] not in TAIL_GROUPS:
                    continue
                n = adj[b]
                if n and sum(1 for c in n if c in tail) >= 0.6 * len(n):
                    extra.add(b)
        tail |= extra
    return tail


def polyline_lengths(pts):
    lengths = [0.0]
    for a, b in zip(pts, pts[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    return lengths


def project_param(pts, lengths, p):
    """Arc length of the closest point of the polyline to p."""
    best, best_d = 0.0, 1e9
    for k, (a, b) in enumerate(zip(pts, pts[1:])):
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
        d = (a + ab * t - p).length_squared
        if d < best_d:
            best_d, best = d, lengths[k] + t * ab.length
    return best


def tail_centerline(body, tail, guess, iterations=4, bins=8):
    """Refines a guessed polyline to the centroids of slices of the tail vertices."""
    pts = [Vector(p) for p in guess]
    me = body.data
    root = pts[0].copy()
    for _ in range(iterations):
        lengths = polyline_lengths(pts)
        total = lengths[-1]
        sums = [Vector() for _ in range(bins)]
        counts = [0] * bins
        for i in tail:
            p = me.vertices[i].co
            s = project_param(pts, lengths, p) / total
            k = min(bins - 1, int(s * bins))
            sums[k] += p
            counts[k] += 1
        mids = [sums[k] / counts[k] for k in range(bins) if counts[k] > 0]
        far = max((me.vertices[i].co for i in tail), key=lambda p: (p - root).length)
        pts = [root] + mids + [far.copy()]
    return pts


def resample(pts, count):
    """count points at equal arc length along the polyline."""
    lengths = polyline_lengths(pts)
    total = lengths[-1]
    out = []
    for k in range(count):
        target = total * k / (count - 1)
        for j in range(len(pts) - 1):
            if lengths[j + 1] >= target - 1e-9:
                seg = lengths[j + 1] - lengths[j]
                t = 0.0 if seg < 1e-9 else (target - lengths[j]) / seg
                out.append(pts[j].lerp(pts[j + 1], t))
                break
    return out


def find_ears(body, dom, side, min_z=1.41, min_x=0.045, min_y=-0.055):
    """side = +1 for the fox's left ear (+X), -1 for the right ear."""
    me = body.data
    ear = set()
    for v in me.vertices:
        p = v.co
        if dom[v.index] != "head_end":
            continue
        if side * p.x < min_x or p.z < min_z or p.y < min_y:
            continue
        ear.add(v.index)
    return ear


def hand_frame(arm, side_name):
    """Wrist position and axes of a hand in the rest pose: a = toward the finger tips,
    front = toward the thumb (character forward), up = back of the hand (palm faces down in the T-pose)."""
    b = arm.data.bones[side_name + "Hand"]
    wrist = arm.matrix_world @ b.head_local
    tip = arm.matrix_world @ b.tail_local
    a = (tip - wrist).normalized()
    front = Vector((0, -1, 0))
    front = (front - a * front.dot(a)).normalized()
    up = a.cross(front)
    if up.z < 0:
        up = -up
    return wrist, a, front, up


def find_hand_parts(body, arm, dom, side_name, knuckle=0.058, thumb_front=0.03, thumb_max_s=0.085):
    me = body.data
    wrist, a, front, up = hand_frame(arm, side_name)
    fingers, thumb = {}, {}
    hand_groups = {side_name + "Hand", side_name + "ForeArm"}
    for v in me.vertices:
        if dom[v.index] not in hand_groups:
            continue
        d = v.co - wrist
        s, u, w = d.dot(a), d.dot(front), d.dot(up)
        if s < -0.02:
            continue
        if u > thumb_front and s < thumb_max_s and w < 0.012:
            thumb[v.index] = (s, u, w)
        elif s > knuckle - 0.01:
            fingers[v.index] = (s, u, w)
    return fingers, thumb, (wrist, a, front, up)
