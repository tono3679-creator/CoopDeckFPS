"""Renders the Lightning Rabbit's stance or clips with the Lightning SMG and prints the hands' reach misses.

    blender -b ArtSource/LightningRabbit/Animation/LightningRabbit_Rig.blend -P rabbit_check.py -- <out_dir> \
        stance [key=value ...] [--close]
    blender -b ... -P rabbit_check.py -- <out_dir> <Clip>[,<Clip>...] [frames=8] [view=right+q34] [key=value ...]

key=value overrides a Stance number after rabbit_clips.setup (e.g. grip=(0.08,0.17,0.93) spine_yaw=-22).
Writes <out_dir>/stance.png (stance_close.png with --close, stance_hands.png with --hands) or clip_<Clip>.png (one
row per view). Every pose also prints CONTACT: how deep the SMG goes into the body (by part of the SMG) and the body
into the SMG (hands vs the rest), in millimetres, so a stock in the chest or fingers through the grip show as numbers.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "fox_anim"))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

import fox_clips as fc  # noqa: E402
from fox_motion import new_action, reach_report  # noqa: E402
from fox_render import CLOSE_VIEWS, VIEWS, contact_sheet  # noqa: E402
from fox_stance import base_controls  # noqa: E402
from rabbit_clips import setup  # noqa: E402
from rabbit_common import script_args  # noqa: E402
from rabbit_motion import RabbitRig, body_scale  # noqa: E402
from rabbit_weapon import SMGRenderer, smg_surface  # noqa: E402


# close-ups of both hands on the SMG (the rabbit faces -Y, its right is -X; the grip is about 0.86 m up)
HAND_VIEWS = {
    "h_right": ((-2.0, -0.28, 0.9), (0.0, -0.28, 0.9), 0.6),
    "h_left": ((2.0, -0.28, 0.9), (0.0, -0.28, 0.9), 0.6),
    "h_under": ((-0.9, -1.4, 0.35), (-0.03, -0.26, 0.88), 0.55),
    "h_top": ((-0.03, -0.25, 3.0), (-0.03, -0.25, 0.0), 0.62),
}


def smg_part(f, u):
    if f < -0.045:
        return "stock"
    if f < 0.045 and u < 0.06:
        return "grip"
    if 0.09 <= f < 0.19 and u < 0.0:
        return "magazine"
    if 0.19 <= f < 0.28 and u < 0.05:
        return "foregrip"
    return "receiver"


RAYS = [Vector(d) for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))]


def inside(bvh, q):
    """Ray vote: a point is inside when at least 4 of 6 axis rays first leave through a back face. The nearest-face
    test alone is fooled between layered clothing (scarf, straps over the jacket), whose inner faces point inward."""
    votes = 0
    for d in RAYS:
        loc, nor, _, _ = bvh.ray_cast(q, d, 1.0)
        if loc is not None and d.dot(nor) > 0:
            votes += 1
    return votes >= 4


class Contact:
    """Interpenetration between the posed body and the SMG: a point is inside when it lies behind the nearest face
    within 6 cm and the ray vote agrees; the depth is the distance to that face."""

    def __init__(self, body, prop):
        self.body, self.prop = body, prop
        n = len(prop.data.vertices)
        v = np.empty(n * 3)
        prop.data.vertices.foreach_get("co", v)
        self.prop_local = v.reshape(-1, 3)
        # rifle space of the prepared prop: (right, muzzle, up) = (-x, -y, z)
        self.parts = [smg_part(-y, z) for x, y, z in self.prop_local]
        self.prop_bvh = BVHTree.FromPolygons([tuple(p) for p in self.prop_local],
                                             [tuple(p.vertices) for p in prop.data.polygons])
        lo, hi = self.prop_local.min(0) - 0.01, self.prop_local.max(0) + 0.01
        self.prop_box = (lo, hi)
        names = [g.name for g in body.vertex_groups]
        dom = np.full(len(body.data.vertices), -1)
        best = np.zeros(len(body.data.vertices))
        for vtx in body.data.vertices:
            for g in vtx.groups:
                if g.weight > best[vtx.index]:
                    best[vtx.index], dom[vtx.index] = g.weight, g.group
        self.group = [names[d] if d >= 0 else "?" for d in dom]

    def measure(self):
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.body.evaluated_get(dg)
        mesh = ev.to_mesh()
        bv = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", bv)
        bv = bv.reshape(-1, 3)
        mesh_faces = [tuple(p.vertices) for p in mesh.polygons]
        body_bvh = BVHTree.FromPolygons([tuple(p) for p in bv], mesh_faces)
        ev.to_mesh_clear()
        to_body = self.body.matrix_world.inverted() @ self.prop.matrix_world
        out = {}
        for p, part in zip(self.prop_local, self.parts):
            q = to_body @ Vector(p)
            loc, nor, face, dist = body_bvh.find_nearest(q, 0.06)
            if loc is not None and (q - loc).dot(nor) < 0 and inside(body_bvh, q):
                key = f"smg {part} in {self.group[mesh_faces[face][0]]}"
                out[key] = max(out.get(key, 0.0), dist)
        to_prop = self.prop.matrix_world.inverted() @ self.body.matrix_world
        m = np.array(to_prop)
        pp = bv @ m[:3, :3].T + m[:3, 3]
        lo, hi = self.prop_box
        near = np.where(np.all((pp > lo) & (pp < hi), axis=1))[0]
        for i in near:
            q = Vector(pp[i])
            loc, nor, _, dist = self.prop_bvh.find_nearest(q, 0.03)
            if loc is not None and (q - loc).dot(nor) < 0 and inside(self.prop_bvh, q):
                key = f"{self.group[i]} in smg"
                out[key] = max(out.get(key, 0.0), dist)
        return {k: round(v * 1000, 1) for k, v in sorted(out.items())}


def region(group):
    side = "R" if group.startswith("Right") else "L" if group.startswith("Left") else ""
    if "Thumb" in group:
        return side + " thumb"
    if "Hand" in group and group[-1].isdigit():
        return side + " fingers"
    if group.endswith("Hand"):
        return side + " palm"
    if "ForeArm" in group:
        return side + " forearm"
    return "body"


def summarize(c):
    """Contact report folded to (SMG part, body region) -> worst mm."""
    out = {}
    for k, v in c.items():
        if k.startswith("smg "):
            part, grp = k[4:].split(" in ")
            key = f"{part}>{region(grp)}"
        else:
            key = f"{region(k[:-7])}>smg"
        out[key] = max(out.get(key, 0.0), v)
    return " ".join(f"{k} {v:.0f}" for k, v in sorted(out.items()))


def main():
    args = script_args()
    out, what = args[0], args[1]
    os.makedirs(out, exist_ok=True)
    opts = {}
    for kv in [a for a in args[2:] if "=" in a and what != "sweep"]:
        k, v = kv.split("=", 1)
        opts[k] = v
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rig = RabbitRig(arm)
    clips = {s.name: s for s in setup(rig, body_scale(arm))}
    base = {k: getattr(fc.ST, k) for k in vars(fc.ST)}

    def apply(overrides):
        for k, v in base.items():
            setattr(fc.ST, k, v.copy() if hasattr(v, "copy") else v)
        for k, v in overrides.items():
            if k not in ("PALM_IN", "PALM_ALONG") and not hasattr(fc.ST, k):
                continue
            val = eval(v, {"Vector": Vector})
            val = Vector(val) if isinstance(val, tuple) else val
            if k in ("PALM_IN", "PALM_ALONG"):
                setattr(rig, k, val)
                rig.hand_rest = {s: rig._hand_frame(s) for s in ("Left", "Right")}
            elif hasattr(fc.ST, k):
                setattr(fc.ST, k, val)

    apply(opts)
    new_action(arm, "check")
    r = SMGRenderer(arm, res=(420, 520) if what != "stance" else (640, 720))
    body = next(o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups)
    if "--no-clear" not in sys.argv:
        rig.use_clearance(body, *smg_surface())
    contact = Contact(body, r.prop)
    if "--xray" in sys.argv:   # see-through body: where the SMG goes inside
        r.sc.display.shading.show_xray = True
        r.sc.display.shading.xray_alpha = 0.35

    def pose(ctl):
        D = rig.solve(ctl)
        rep = reach_report(rig, D, ctl)
        rig.key(D, 1)
        bpy.context.scene.frame_set(1)
        r.attach_rifle()
        return rep

    if what == "scan":
        # every clip (or only=A,B), frames=N samples each, no renders: the worst SMG-body clipping per clip, leaving
        # out the hands and forearms (they hold the grips)
        count = int(opts.get("frames", 8))
        only = set(opts["only"].split(",")) if "only" in opts else None
        for spec in clips.values():
            if only and spec.name not in only:
                continue
            worst, at = {}, {}
            for k in range(count):
                u = k / count if spec.loop else k / max(1, count - 1)
                pose(spec.fn(u * spec.seconds, u))
                for key, v in contact.measure().items():
                    part, _, grp = key.partition(" in ")
                    reg = region(grp if part.startswith("smg ") else part)
                    if reg != "body":
                        continue
                    smg_in_body = part.startswith("smg ")
                    name = f"{part[4:]}>{grp}" if smg_in_body else "body>smg"
                    if v > worst.get(name, 0.0):
                        worst[name], at[name] = v, round(u * spec.seconds, 2)
            bad = {k: f"{v:.0f}mm@{at[k]}s" for k, v in sorted(worst.items(), key=lambda t: -t[1]) if v >= 5.0}
            print(f"SCAN {spec.name}: {bad if bad else 'clear'}", flush=True)
        print("CHECK_DONE")
        return

    if what == "timeline":
        # one clip (clip=Name), every step seconds (step=0.05): the worst SMG-body contact at each time, hands and
        # forearms left out, so a short clipping can be seen in full (the scan only keeps the worst per part)
        spec = clips[opts["clip"]]
        step = float(opts.get("step", 0.05))
        t0, t1 = float(opts.get("from", 0.0)), float(opts.get("to", spec.seconds))
        t = t0
        while t <= t1 + 1e-6:
            pose(spec.fn(t, t / spec.seconds))
            worst = {}
            for key, v in contact.measure().items():
                part, _, grp = key.partition(" in ")
                if region(grp if part.startswith("smg ") else part) != "body":
                    continue
                name = f"{part[4:]}>{grp}" if part.startswith("smg ") else "body>smg"
                worst[name] = max(worst.get(name, 0.0), v)
            top = sorted(worst.items(), key=lambda kv: -kv[1])[:3]
            print(f"TIMELINE {spec.name} {t:.2f}s " + (", ".join(f"{k} {v:.0f}mm" for k, v in top) if top else "clear"),
                  flush=True)
            t += step
        print("CHECK_DONE")
        return

    if what == "sweep":
        # args after "sweep": one candidate per argument, key=value pairs separated by spaces (no renders)
        for cand in [a for a in args[2:] if "=" in a]:
            ov = dict(kv.split("=", 1) for kv in cand.split())
            apply(ov)
            rep = pose(base_controls(fc.ST))
            print("SWEEP", cand, "| reach", {k: round(v * 100, 1) for k, v in rep.items()}, "cm |",
                  summarize(contact.measure()))
        print("CHECK_DONE")
        return

    if what == "stance":
        rep = pose(base_controls(fc.ST))
        print("REACH", {k: round(v, 4) for k, v in rep.items()})
        print("CONTACT mm", contact.measure())
        views, name = VIEWS, "stance.png"
        if "--close" in sys.argv:
            views, name = CLOSE_VIEWS, "stance_close.png"
        if "--hands" in sys.argv:
            views, name = HAND_VIEWS, "stance_hands.png"
        paths = []
        for view, (eye, target, scale) in views.items():
            p = os.path.join(out, f"_v_{view}.png")
            r.shot(p, eye, target, scale)
            paths.append(p)
        contact_sheet(paths, os.path.join(out, name), 3 if len(paths) > 4 else 2)
        print("CHECK_DONE")
        return

    count = int(opts.get("frames", 8))
    views = opts.get("view", "right+q34").split("+")
    for name in what.split(","):
        spec = clips[name]
        paths, worst, touch = [], {}, {}
        for view in views:
            for k in range(count):
                u = k / count if spec.loop else k / max(1, count - 1)
                ctl = spec.fn(u * spec.seconds, u)
                for key, v in pose(ctl).items():
                    on = ctl.left_ik if key == "LeftHand" else getattr(ctl, "right_on_gun", 1.0)
                    if on > 0.99:
                        worst[key] = max(worst.get(key, 0.0), v)
                if view == views[0]:
                    for key, v in contact.measure().items():
                        touch[key] = max(touch.get(key, 0.0), v)
                eye, target, scale = {**VIEWS, **CLOSE_VIEWS, **HAND_VIEWS}[view]
                p = os.path.join(out, f"_f_{name}_{view}_{k}.png")
                r.shot(p, eye, target, scale)
                paths.append(p)
        contact_sheet(paths, os.path.join(out, f"clip_{name}.png"), count)
        print("CLIP", name, "reach on the SMG", {k: round(v, 3) for k, v in worst.items()}, "contact mm", touch)
    print("CHECK_DONE")


main()
