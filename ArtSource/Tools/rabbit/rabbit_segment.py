"""Finds the ear, tail, scarf-ribbon and finger regions of the Lightning Rabbit body and the joint positions for the
bones the Meshy auto-rig lacks.

The rebuilt body is one closed, connected mesh (decimated from the sculpt), so regions come from position, the base
colour texture, the original skin weights and connectivity:
  ears    - above the skull, per side, grown from the tip; the headset (dark colours at the left ear root) stays on the
            head
  tail    - the pom-pom behind the hips, grown from its farthest point through fur colours
  ribbons - the two scarf tails hanging on the right side: scarf-yellow pieces below the knot that are longer than
            25 cm (pouches and soles are yellow too), split where the two tails part
  fingers - per hand, cuts across the hand at increasing distances from the wrist split off one digit after another
            (the sculpt's fingers are separate past the webbing); each digit's axis is fitted to its free part and
            extended back to the knuckle, and the webbing goes to the nearest digit

    blender -b --factory-startup -P rabbit_segment.py -- --debug <dir>     (paints the regions and renders them)
"""
import os
import sys

import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rabbit_common import (adjacency, bone_world, components, coords, grow, import_rabbit,  # noqa: E402
                           script_args, vertex_hsv)

EAR_BASE_Z = 1.37            # the ears rise from the skull above this height
HEADSET_TOP_Z = 1.42         # dark headset parts reach this high at the left ear root
TAIL_ZONE = dict(y_min=0.09, z_min=0.56, z_max=0.80, x_abs=0.11)
FINGER_NAMES = ("Index", "Middle", "Ring", "Little")
WEB_BACK = 0.18              # knuckle (MCP) behind the webbing, as a share of the free finger length
THUMB_BACK = 0.55            # thumb base (CMC) behind its free part, as a share of the free thumb length


def dominant_groups(body):
    names = {g.index: g.name for g in body.vertex_groups}
    return np.array([names[max(v.groups, key=lambda g: g.weight).group] if v.groups else ""
                     for v in body.data.vertices])


def is_dark(hsv):
    return hsv[:, 2] < 0.38


def is_scarf_yellow(hsv):
    return (hsv[:, 0] > 0.08) & (hsv[:, 0] < 0.17) & (hsv[:, 1] > 0.45) & (hsv[:, 2] > 0.55)


def is_fur(hsv):
    return (hsv[:, 1] < 0.22) & (hsv[:, 2] > 0.6)


def principal_axis(points, toward):
    c = points.mean(0)
    _, _, vt = np.linalg.svd(points - c, full_matrices=False)
    d = vt[0]
    return c, d if np.dot(d, toward) > 0 else -d


def centreline(points, axis_origin, axis_dir, count):
    """`count` points along the region: centroids of slices across the principal axis."""
    t = (points - axis_origin) @ axis_dir
    edges = np.linspace(t.min(), t.max(), count + 1)
    out = []
    for k in range(count):
        sel = (t >= edges[k]) & (t <= edges[k + 1])
        out.append(points[sel].mean(0) if sel.any() else axis_origin + axis_dir * (edges[k] + edges[k + 1]) / 2)
    return np.array(out), t.min(), t.max()


# ---------------------------------------------------------------- ears, tail, ribbons

def find_ears(co, hsv, adj, dom):
    """{+1: left ear ids, -1: right ear ids}: grown down from each tip above the skull (head-weighted vertices)."""
    ears = {}
    head = np.isin(dom, ("Head", "head_end"))
    for side in (+1, -1):
        cand = (co[:, 2] > EAR_BASE_Z) & (side * co[:, 0] > 0.02) & head
        headset = is_dark(hsv) & (co[:, 2] < HEADSET_TOP_Z) & (side * co[:, 0] > 0.09)
        idx = np.where(cand)[0]
        tip = idx[np.argmax(co[idx, 2])]
        ok = cand & ~headset
        ears[side] = grow([tip], adj, lambda i: bool(ok[i]))
    return ears


def ear_axis(co, ids):
    """Base (centre of the ear's lowest 3 cm) and tip (highest point) of an ear."""
    p = co[list(ids)]
    zmin = p[:, 2].min()
    base = p[p[:, 2] < zmin + 0.03].mean(0)
    tip = p[np.argmax(p[:, 2])]
    return base, tip


def find_tail(co, hsv, adj):
    z = TAIL_ZONE
    cand = (co[:, 1] > z["y_min"]) & (co[:, 2] > z["z_min"]) & (co[:, 2] < z["z_max"]) & (np.abs(co[:, 0]) < z["x_abs"])
    idx = np.where(cand)[0]
    tip = idx[np.argmax(co[idx, 1])]
    ok = cand & is_fur(hsv)
    return grow([tip], adj, lambda i: bool(ok[i]))


def find_ribbons(co, hsv, adj, knot_z):
    """The two scarf tails as id sets, front ribbon first. Pieces of scarf yellow on the right side below the knot
    that are longer than 25 cm; the piece holding both tails is split where they part."""
    cand = is_scarf_yellow(hsv) & (co[:, 0] < -0.06) & (co[:, 2] < knot_z)
    pieces = [c for c in components(np.where(cand)[0], adj) if np.ptp(co[c, 2]) > 0.25]
    if len(pieces) == 1:
        both = np.array(pieces[0])
        for z_split in np.arange(knot_z - 0.02, co[both, 2].min(), -0.01):
            low = [c for c in components(both[co[both, 2] < z_split], adj) if len(c) > 200]
            if len(low) >= 2:
                break
        else:
            raise RuntimeError("the scarf tails do not part")
        a, b = low[0], low[1]
        # the shared top goes to the nearer tail
        top = both[co[both, 2] >= z_split]
        ca, cb = co[a].mean(0), co[b].mean(0)
        to_a = np.linalg.norm(co[top] - ca, axis=1) < np.linalg.norm(co[top] - cb, axis=1)
        pieces = [list(a) + list(top[to_a]), list(b) + list(top[~to_a])]
    pieces = sorted(pieces, key=lambda c: co[c, 1].mean())   # front (smaller y) first
    ribbons = [set(int(i) for i in c) for c in pieces[:2]]
    # the lightning emblems and edge lines on the tails are not yellow: patches enclosed by a tail belong to it
    zone = (co[:, 0] < -0.04) & (co[:, 2] < knot_z)
    for _ in range(3):
        taken = set().union(*ribbons)
        others = [i for i in np.where(zone)[0] if int(i) not in taken]
        for comp in components(others, adj):
            if len(comp) > 1500:
                continue
            border = {n for v in comp for n in adj[v]} - set(comp)
            for r in ribbons:
                if border and len(border & r) / len(border) > 0.6:
                    r.update(comp)
                    break
    return ribbons


# ---------------------------------------------------------------- hands

def hand_frame(arm, side):
    """Wrist, a (toward the finger tips), front (character forward, the thumb side), up (back of the hand)."""
    wrist, tip = bone_world(arm, side + "Hand")
    a = (tip - wrist).normalized()
    front = Vector((0, -1, 0))
    front = (front - a * front.dot(a)).normalized()
    up = a.cross(front)
    if up.z < 0:
        up = -up
    return np.array(wrist), np.array(a), np.array(front), np.array(up)


def find_digits(co, adj, dom, arm, side, min_size=40):
    """Per hand: {digit: dict(ids, joints[4], free)} for Thumb, Index, Middle, Ring, Little.
    joints = knuckle (MCP; CMC for the thumb), the two middle joints and the tip, on the digit's axis."""
    wrist, a, front, up = hand_frame(arm, side)
    d = co - wrist
    s, u, w = d @ a, d @ front, d @ up
    hand = np.isin(dom, (side + "Hand", side + "ForeArm")) & (s > -0.02)

    # 1. free parts: walk the cut from the wrist outward; every piece that holds a single tip is a digit's free part
    pieces = {}   # tip vertex -> (ids, cut) at the lowest cut where the piece holds only that tip
    levels = []
    for cut in np.arange(0.03, s[hand].max(), 0.0025):
        comps = [np.array(c) for c in components(np.where(hand & (s > cut))[0], adj) if len(c) >= min_size]
        levels.append((cut, comps))
    tips = []
    for cut, comps in reversed(levels):           # far cuts hold single digits: collect their tips
        for c in comps:
            t = int(c[np.argmax(s[c] - 0.5 * w[c] * (u[c] > 0.03))])
            if all(np.linalg.norm(co[t] - co[o]) > 0.02 for o in tips):
                tips.append(t)
    for t in tips:
        best = None
        for cut, comps in levels:
            for c in comps:
                if t in set(c.tolist()) and sum(1 for o in tips if o in set(c.tolist())) == 1:
                    best = (c, cut)
                    break
            if best is not None:
                break
        if best is not None:
            pieces[t] = best
    if len(pieces) < 5:
        raise RuntimeError(f"{side}: found {len(pieces)} digits")
    # the thumb hangs below the palm and points forward
    thumb_tip = min(pieces, key=lambda t: w[t] - u[t])
    others = sorted((t for t in pieces if t != thumb_tip), key=lambda t: -u[t])[:4]   # index (front) first

    digits = {}
    for name, t in [("Thumb", thumb_tip)] + list(zip(FINGER_NAMES, others)):
        ids, cut = pieces[t]
        p = co[ids]
        toward = co[t] - wrist
        c, axis = principal_axis(p, toward)
        tproj = (p - c) @ axis
        base = p[tproj < tproj.min() + 0.15 * np.ptp(tproj)].mean(0)
        tip_pt = c + axis * tproj.max()
        free = float(np.linalg.norm(tip_pt - base))
        if name == "Thumb":
            root = base - axis * THUMB_BACK * free
            length = float(np.linalg.norm(tip_pt - root))
            joints = [root, root + axis * 0.42 * length, root + axis * 0.70 * length, tip_pt]
        else:
            root = base - axis * WEB_BACK * free
            length = float(np.linalg.norm(tip_pt - root))
            joints = [root, root + axis * 0.45 * length, root + axis * 0.75 * length, tip_pt]
        digits[name] = dict(ids=set(int(i) for i in ids), joints=[np.array(j) for j in joints], axis=axis,
                            free=free, cut=float(cut), length=length)

    # 2. webbing and knuckles: hand vertices past the knuckles are shared by the two nearest finger axes (inverse
    #    square distance), so neighbouring fingers pull the webbing between them evenly instead of tearing it
    taken = set().union(*(v["ids"] for v in digits.values()))
    web = {}
    for i in np.where(hand)[0]:
        if int(i) in taken:
            continue
        near = []
        for name in FINGER_NAMES:
            dg = digits[name]
            r = co[i] - dg["joints"][0]
            along = r @ dg["axis"]
            if along < -0.006:
                continue
            dist = np.linalg.norm(r - dg["axis"] * along)
            if dist < 0.02:
                near.append((dist, name))
        if near:
            near = sorted(near)[:2]
            inv = [1.0 / (d + 0.002) ** 2 for d, _ in near]
            web[int(i)] = {n: v / sum(inv) for (_, n), v in zip(near, inv)}
    # 3. thumb: the free part plus the vertices close to its axis back to the base (the thenar pad blends later)
    th = digits["Thumb"]
    for i in np.where(hand)[0]:
        if int(i) in taken:
            continue
        r = co[i] - th["joints"][0]
        along = r @ th["axis"]
        if 0.0 < along < th["length"]:
            if np.linalg.norm(r - th["axis"] * along) < 0.02 and int(i) not in web:
                th["ids"].add(int(i))
    # the band across the knuckles whose weights are smoothed after skinning
    mcp = [float((digits[n]["joints"][0] - wrist) @ a) for n in FINGER_NAMES]
    band = np.where(hand & (s > min(mcp) - 0.012) & (s < max(mcp) + 0.03))[0]
    return digits, (wrist, a, front, up), web, set(int(i) for i in band)


def segment(arm, body):
    co = coords(body)
    hsv = vertex_hsv(body)
    adj = adjacency(body)
    dom = dominant_groups(body)
    knot_z = bone_world(arm, "Head")[0].z - 0.05
    ears = find_ears(co, hsv, adj, dom)
    tail = find_tail(co, hsv, adj)
    ribbons = find_ribbons(co, hsv, adj, knot_z)
    hands = {side: find_digits(co, adj, dom, arm, side) for side in ("Left", "Right")}
    return dict(co=co, hsv=hsv, adj=adj, dom=dom, ears=ears, tail=tail, ribbons=ribbons, hands=hands)


def report(seg):
    print(f"  ears L/R {len(seg['ears'][1])}/{len(seg['ears'][-1])}, tail {len(seg['tail'])}, "
          f"ribbons {[len(r) for r in seg['ribbons']]}")
    for side, (digits, _, web, band) in seg["hands"].items():
        print(f"  {side} hand: " + ", ".join(f"{n} {len(v['ids'])} (free {v['free'] * 100:.1f} cm from cut "
                                             f"{v['cut'] * 100:.1f}, length {v['length'] * 100:.1f})"
                                             for n, v in digits.items()) + f", webbing {len(web)}, knuckle band {len(band)}")


PALETTE = {"Thumb": (1, 0.3, 0.3), "Index": (1, 0.8, 0.2), "Middle": (0.3, 0.9, 0.3), "Ring": (0.3, 0.6, 1),
           "Little": (0.8, 0.3, 0.9)}


def debug(arm, body, seg, out):
    from rabbit_debug import paint, render_sheet, stack
    os.makedirs(out, exist_ok=True)
    regions = [(seg["ears"][1], (0.9, 0.2, 0.2)), (seg["ears"][-1], (0.2, 0.4, 0.95)), (seg["tail"], (0.2, 0.85, 0.3))]
    for r, rgb in zip(seg["ribbons"], ((1.0, 0.6, 0.0), (0.7, 0.3, 0.9))):
        regions.append((r, rgb))
    for digits, _, _, _ in seg["hands"].values():
        for n, dg in digits.items():
            regions.append((dg["ids"], PALETTE[n]))
    paint(body, regions)
    full = Vector((0, 0.05, 0.85))
    rows = [render_sheet(os.path.join(out, "_r1.png"), [(full, 1.9, v) for v in ("front", "right", "back", "left")]),
            render_sheet(os.path.join(out, "_r2.png"), [
                (Vector((0.0, 0.03, 1.45)), 0.55, "front"), (Vector((0.0, 0.03, 1.45)), 0.55, "back"),
                (Vector((-0.2, 0.1, 0.7)), 0.8, "right"), (Vector((-0.2, 0.1, 0.7)), 0.8, "back")]),
            render_sheet(os.path.join(out, "_r3.png"), [
                (Vector((-0.57, 0.0, 0.93)), 0.2, "top"), (Vector((-0.57, 0.0, 0.93)), 0.2, Vector((0, 0.001, -1))),
                (Vector((0.57, 0.0, 0.93)), 0.2, "top"), (Vector((0.57, 0.0, 0.93)), 0.2, Vector((0, 0.001, -1)))])]
    stack(rows, os.path.join(out, "segments.png"))


def main():
    args = script_args()
    arm, body = import_rabbit()
    seg = segment(arm, body)
    report(seg)
    if "--debug" in args:
        debug(arm, body, seg, args[args.index("--debug") + 1])


if __name__ == "__main__":
    main()
