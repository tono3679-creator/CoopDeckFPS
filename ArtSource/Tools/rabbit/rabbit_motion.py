"""The Fox Gunner pose solver applied to the Lightning Rabbit (Blender Python).

The solver in fox_anim/fox_motion.py measures everything from the rig (bone lengths, rest frames, feet), so the same
maths drives the rabbit. Only two things differ:
  - fingers: the rabbit has every finger with three joints (<Side>Hand<Digit>1-3) instead of the fox's two-bone
    four-finger block, so the curl goes to each phalanx; the right index stays straighter (trigger finger)
  - bones driven procedurally in Unity are never keyed: ears, tail and the scarf tails
  - reach assist: the arms are 18% shorter, so where a fox clip moves the body away from the gun (strafing, hits,
    falls, the downed poses) a hand can end up short of its grip. Then the gun, with both grips, is pulled toward
    that hand by the shortfall and the arms are solved again, as a person draws the gun in with a straight arm.
  - clearance (use_clearance): the rabbit's big head, cheek fur and chest sit close to the SMG's stock, so where a
    fox clip leans the body onto the gun or tilts the gun up (dashes, landings, knockback, inspect) the SMG would
    go into the body. Then the gun, with both grips, is pushed out by the depth plus 5 mm and the arms are solved
    again. Head, neck, chest, shoulders, upper arms, hips and legs are tested rigidly per bone against the rest
    mesh, both ways round (SMG points in the body and body points in the SMG).
    The build reports how far the gun was pulled and pushed in each clip.

Body scale: the fox clips place the pelvis, feet and free hands in metres for the fox (shoulders at 1.06 m). The
rabbit's shoulders are at 0.986 m, so character-space distances are scaled by their ratio (install_scale). Points on
the rifle stay in rifle space (same rifle), and the hands still meet the rifle exactly (the solver reports misses).
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fox_anim"))
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

import fox_motion  # noqa: E402
from fox_motion import Rig, about, axis_angle  # noqa: E402

FOX_SHOULDER_Z = 1.06        # LeftArm head of the Fox Gunner rig (ArtSource/FoxGunner/Inspection/report.json)

# body parts the SMG must stay out of (the forearms and hands hold it; ears, tail and scarf tails move in Unity)
CLEAR_BONES = ("Head", "headfront", "neck", "Spine02", "Spine01", "Spine", "Hips", "LeftShoulder", "RightShoulder",
               "LeftArm", "RightArm", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg")
CLEAR_MARGIN = 0.006
CLEAR_SPACING = 0.012     # body points tested against the SMG: one per 1.2 cm cube of each region
RAYS = [Vector(d) for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))]


def _inside(bvh, q, reach=0.08):
    """(depth, outward normal) when q is inside the closed surface in bvh: behind the nearest face and at least 4 of
    6 axis rays leaving through back faces (layered clothing fools the nearest face alone); None otherwise."""
    loc, nor, _, dist = bvh.find_nearest(q, reach)
    if loc is None or (q - loc).dot(nor) >= 0:
        return None
    votes = 0
    for d in RAYS:
        hit, hn, _, _ = bvh.ray_cast(q, d, 1.0)
        votes += hit is not None and d.dot(hn) > 0
    return (dist, nor) if votes >= 4 else None


def _thin(points, spacing):
    keys = {}
    for i, p in enumerate(points):
        keys.setdefault(tuple(np.floor(p / spacing).astype(int)), i)
    return np.array(sorted(keys.values()))


class Clearance:
    """Keeps the SMG out of the body, both ways round: SMG surface points inside a body region, and body points
    inside the SMG (fur spikes and the scarf poke between SMG points). Each CLEAR_BONES region (vertices whose
    strongest weight is that bone) is treated as rigid with its bone and tested in the bone's rest space."""

    def __init__(self, body, smg_verts, smg_faces):
        me = body.data
        co = np.empty(len(me.vertices) * 3)
        me.vertices.foreach_get("co", co)
        mw = np.array(body.matrix_world)
        co = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
        names = [g.name for g in body.vertex_groups]
        dom = np.full(len(me.vertices), -1)
        best = np.zeros(len(me.vertices))
        for v in me.vertices:
            for g in v.groups:
                if g.weight > best[v.index]:
                    best[v.index], dom[v.index] = g.weight, g.group
        tri = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get("vertex_index", tri)
        tri = tri.reshape(-1, 3)          # the body is all triangles
        verts = [tuple(p) for p in co]
        self.parts = []
        for bone in CLEAR_BONES:
            if bone not in names:
                continue
            mask = dom == names.index(bone)
            faces = tri[mask[tri].all(axis=1)]
            if len(faces) < 20:
                continue
            used = co[mask]
            self.parts.append((bone, BVHTree.FromPolygons(verts, [tuple(f) for f in faces]),
                               used.min(axis=0) - 0.01, used.max(axis=0) + 0.01, used[_thin(used, CLEAR_SPACING)]))
        sv = np.array([tuple(p) for p in smg_verts])          # rifle space
        self.smg_bvh = BVHTree.FromPolygons([tuple(p) for p in sv], [tuple(f) for f in smg_faces])
        self.smg_lo, self.smg_hi = sv.min(axis=0), sv.max(axis=0)
        self.points = sv[_thin(sv, CLEAR_SPACING)]

    def push(self, D, weapon):
        """World shift that moves the SMG out of the body (zero vector when it is clear)."""
        w = np.array(weapon)
        w_inv = np.array(weapon.inverted())
        pw = self.points @ w[:3, :3].T + w[:3, 3]
        deepest, out = 0.0, Vector()
        for bone, bvh, lo, hi, body_pts in self.parts:
            m = np.array(D[bone].inverted())
            pr = pw @ m[:3, :3].T + m[:3, 3]
            for i in np.where(np.all((pr > lo) & (pr < hi), axis=1))[0]:
                hit = _inside(bvh, Vector(pr[i]))
                if hit and hit[0] > deepest:
                    deepest = hit[0]
                    out = (D[bone].to_3x3() @ hit[1]).normalized() * (hit[0] + CLEAR_MARGIN)
            # body points of this region in rifle space
            to_rifle = w_inv @ np.array(D[bone])
            br = body_pts @ to_rifle[:3, :3].T + to_rifle[:3, 3]
            for i in np.where(np.all((br > self.smg_lo) & (br < self.smg_hi), axis=1))[0]:
                hit = _inside(self.smg_bvh, Vector(br[i]))
                if hit and hit[0] > deepest:
                    deepest = hit[0]
                    out = -(weapon.to_3x3() @ hit[1]).normalized() * (hit[0] + CLEAR_MARGIN)
        return out


# degrees at curl = 1 for (base, middle, tip) joints
FINGER_CURL = {"Index": (68, 82, 48), "Middle": (74, 88, 52), "Ring": (78, 90, 52), "Pinky": (82, 90, 52)}
THUMB_CURL = (14, 30, 28)    # at thumb = 1 (Thumb1 is the metacarpal, it moves little)
TRIGGER_INDEX = 0.55         # share of the curl for the right index (rests along the trigger guard)


class RabbitRig(Rig):
    UNKEYED_PREFIXES = Rig.UNKEYED_PREFIXES + ("Scarf_",)
    ASSIST_STEPS = 10
    clearance = None
    assist = pushed = moved = 0.0

    def use_clearance(self, body, smg_verts, smg_faces):
        """Turns on the clearance test: body mesh (rest pose) and the SMG surface in rifle space."""
        self.clearance = Clearance(body, smg_verts, smg_faces)

    def solve(self, ctl):
        """Rig.solve with the reach assist and the clearance push. The gun follows a hand that falls short of its
        grip (the left hand only by the share it is on the gun, LeftHandIK; the right hand likewise when a clip sets
        ctl.right_on_gun, as Death does when the rabbit lets go) and is pushed out of the body. Leaves the pull and
        the push in self.assist and self.pushed, and the net move of the gun in self.moved (metres)."""
        D = super().solve(ctl)
        start = ctl.weapon.translation.copy() if ctl.weapon is not None else None
        pulled, pushed = Vector(), Vector()
        right_on = getattr(ctl, "right_on_gun", 1.0)
        for _ in range(self.ASSIST_STEPS):
            shift = Vector()
            for side, on_gun in (("Right", right_on), ("Left", ctl.left_ik)):
                goal = ctl.hands[side]
                if goal is None or ctl.weapon is None or on_gun <= 0.0:
                    continue
                hand = side + "Hand"
                got = D[hand] @ (self.head[hand] + self.hand_rest[side]["palm_off"])
                miss = (got - goal.palm) * on_gun
                if miss.length > shift.length:
                    shift = miss
            push = self.clearance.push(D, ctl.weapon) if self.clearance and ctl.weapon is not None else Vector()
            if shift.length < 2e-4 and push.length < 2e-4:
                break
            move = shift + push
            ctl.weapon = Matrix.Translation(move) @ ctl.weapon
            for side, on_gun in (("Right", right_on), ("Left", ctl.left_ik)):
                if ctl.hands[side] is not None and on_gun > 0.0:
                    ctl.hands[side].palm = ctl.hands[side].palm + move * on_gun
            pulled += shift
            pushed += push
            D = super().solve(ctl)
        self.assist, self.pushed = pulled.length, pushed.length
        self.moved = (ctl.weapon.translation - start).length if start is not None else 0.0
        return D

    def _solve_fingers(self, D, ctl, side):
        H = self.head
        hand = side + "Hand"
        curl = ctl.curl[side]
        for digit, angles in FINGER_CURL.items():
            k = curl * (TRIGGER_INDEX if (side == "Right" and digit == "Index") else 1.0)
            parent = hand
            for j, deg in enumerate(angles):
                b = f"{side}Hand{digit}{j + 1}"
                x = self.rest[b].to_3x3().col[0]
                D[b] = D[parent] @ about(H[b], axis_angle(x, -deg * k))
                parent = b
        parent = hand
        for j, deg in enumerate(THUMB_CURL):
            b = f"{side}HandThumb{j + 1}"
            x = self.rest[b].to_3x3().col[0]
            D[b] = D[parent] @ about(H[b], axis_angle(x, -deg * ctl.thumb[side]))
            parent = b


def body_scale(arm):
    z = (arm.matrix_world @ arm.data.bones["LeftArm"].head_local).z
    return z / FOX_SHOULDER_Z


def install_scale(scale, modules):
    """Character-space offsets c(r, f, u) are scaled for the rabbit in every module that builds controls."""
    def c(r=0.0, f=0.0, u=0.0):
        return Vector((-r * scale, -f * scale, u * scale))
    for m in modules:
        m.c = c
    fox_motion.c_rabbit = c
    return c
