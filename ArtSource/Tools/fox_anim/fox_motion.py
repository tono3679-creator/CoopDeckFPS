"""Pose solver for authoring Fox Gunner clips in Blender Python.

A clip is a function frame -> Controls (pelvis, spine, head, rifle, hands, feet, finger curl). The solver
turns Controls into bone poses with forward kinematics for the spine chain and analytic two-bone IK for
the arms and legs, then keys every bone (matrix_basis) on an Action. No Blender constraints are used, so
the result is deterministic and exactly what the maths says.

Character space: c(r, f, u) = metres to the fox's right, forward, up. Blender world: right = -X,
forward = -Y, up = +Z (see fox_common).

Rotations in character space (angles in degrees):
  pitch > 0 tips the top forward, yaw > 0 turns the face to the fox's left, roll > 0 tips the top to the right.
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector

RIGHT = Vector((-1.0, 0.0, 0.0))
FWD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))


def c(r=0.0, f=0.0, u=0.0):
    """Character-space offset -> Blender world vector."""
    return Vector((-r, -f, u))


def rot(pitch=0.0, yaw=0.0, roll=0.0):
    """Character-space rotation (degrees), applied roll, then pitch, then yaw."""
    return (Matrix.Rotation(math.radians(yaw), 3, "Z")
            @ Matrix.Rotation(math.radians(pitch), 3, "X")
            @ Matrix.Rotation(math.radians(-roll), 3, "Y"))


def axis_angle(axis, degrees):
    return Matrix.Rotation(math.radians(degrees), 3, axis.normalized())


def about(point, r3):
    return Matrix.Translation(point) @ r3.to_4x4() @ Matrix.Translation(-point)


def frame(x, y):
    """Orthonormal 3x3 with columns (x, y', x cross y')."""
    x = x.normalized()
    y = (y - x * y.dot(x)).normalized()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed()


def slerp_m(r3, t):
    return Quaternion().slerp(r3.to_quaternion(), t).to_matrix()


def lerp(a, b, t):
    return a + (b - a) * t


def clamp01(x):
    return max(0.0, min(1.0, x))


def smooth(t):
    t = clamp01(t)
    return t * t * (3 - 2 * t)


def smoother(t):
    t = clamp01(t)
    return t * t * t * (t * (t * 6 - 15) + 10)


def ease_out(t, p=2.0):
    t = clamp01(t)
    return 1 - (1 - t) ** p


def ease_in(t, p=2.0):
    return clamp01(t) ** p


def bump(t, a, b, c_=None, d=None):
    """0 before a, rises to 1 at b, stays until c_, falls to 0 at d (smoothstep edges)."""
    if c_ is None:
        c_, d = b, b + (b - a)
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / max(1e-6, b - a))
    if t <= c_:
        return 1.0
    return 1.0 - smooth((t - c_) / max(1e-6, d - c_))


def spring(t, freq=2.0, damp=4.0):
    """Unit step response of a damped spring, 0 at t=0 settling to 1 (for overshoot and settle)."""
    if t <= 0:
        return 0.0
    w = 2 * math.pi * freq
    return 1.0 - math.exp(-damp * t) * (math.cos(w * t) + damp / w * math.sin(w * t))


def decay(t, freq=2.0, damp=4.0):
    """Damped oscillation starting at 1 (for shakes that settle)."""
    if t < 0:
        return 0.0
    return math.exp(-damp * t) * math.cos(2 * math.pi * freq * t)


class Hand:
    """Goal for one hand: palm point + hand axes in world space, or None to leave the arm to FK."""

    def __init__(self, palm, along, thumb, elbow):
        self.palm = palm      # world point the palm centre should touch
        self.along = along    # wrist -> knuckles
        self.thumb = thumb    # toward the thumb (hint, orthogonalised)
        self.elbow = elbow    # direction the elbow should point (pole)


class Foot:
    """Ground goal for one foot: plant point on the floor, lift, pitch (toes up > 0), yaw (left > 0)."""

    def __init__(self, plant, lift=0.0, pitch=0.0, yaw=0.0, toe=0.0, knee=None):
        self.plant = plant
        self.lift = lift
        self.pitch = pitch
        self.yaw = yaw
        self.toe = toe        # extra toe bend (toes up > 0) on top of keeping them flat
        self.knee = knee      # knee direction hint (world), default forward/out


class Controls:
    def __init__(self):
        self.hips_offset = Vector()            # world, from the rest hips
        self.hips_rot = Matrix.Identity(3)     # character-space rotation about the hips
        self.spine = [Matrix.Identity(3)] * 3  # Spine02, Spine01, Spine (each relative to its parent)
        self.head_aim = None                   # world rotation the head should end at (None: follow the neck)
        self.head_follow = 0.0                 # 0 = keep head_aim, 1 = let the head ride the spine
        self.neck = Matrix.Identity(3)
        self.head = Matrix.Identity(3)
        self.clavicle = {"Left": Matrix.Identity(3), "Right": Matrix.Identity(3)}
        self.weapon = Matrix.Identity(4)       # world frame of the rifle grip: columns right, muzzle, up, origin
        self.hands = {"Left": None, "Right": None}
        self.arm_fk = {"Left": None, "Right": None}  # (shoulder, elbow, wrist) rotations when no IK goal
        self.feet = {"Left": None, "Right": None}
        self.curl = {"Left": 0.8, "Right": 0.85}     # 0 = flat hand, 1 = fist
        self.thumb = {"Left": 0.5, "Right": 0.6}
        self.finger_spread = {"Left": 0.0, "Right": 0.0}
        self.left_ik = 1.0      # 1 = left hand on the rifle (exported as the LeftHandIK curve for Unity)
        self.cast = 0.0         # 1 = left hand is casting (exported as the CastHand curve; VFX timing)


class Rig:
    PALM_ALONG = 0.055
    PALM_IN = 0.014

    def __init__(self, arm):
        self.arm = arm
        bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.head = {b.name: b.head_local.copy() for b in bones}
        self.tail = {b.name: b.tail_local.copy() for b in bones}
        self.order = []
        seen = set()

        def visit(b):
            if b.name in seen:
                return
            if b.parent:
                visit(b.parent)
            seen.add(b.name)
            self.order.append(b.name)

        for b in bones:
            visit(b)
        self.hand_rest = {s: self._hand_frame(s) for s in ("Left", "Right")}
        foot = {}
        for s in ("Left", "Right"):
            ankle = self.head[s + "Foot"]
            ball = self.head[s + "ToeBase"]
            toe_tip = self.tail[s + "ToeBase"]
            heel = Vector((ankle.x, ankle.y + 0.045, 0.0))
            foot[s] = {
                "ankle": ankle, "ball": Vector((ball.x, ball.y, 0.0)), "heel": heel,
                "ref": Vector((ankle.x, ankle.y, 0.0)), "toe_tip": Vector((toe_tip.x, toe_tip.y, 0.0)),
                "ball_joint": ball,
            }
        self.foot_rest = foot

    # -------------------------------------------------------------- rest helpers

    def _hand_frame(self, side):
        wrist = self.head[side + "Hand"]
        a = (self.tail[side + "Hand"] - wrist).normalized()
        thumb = FWD - a * FWD.dot(a)
        thumb.normalize()
        sign = 1.0 if side == "Right" else -1.0
        up = a.cross(thumb) * sign  # back of the hand (points up in the T-pose for both hands)
        m = Matrix((a, thumb, up)).transposed()
        palm = wrist + a * self.PALM_ALONG - up * self.PALM_IN
        return {"m": m, "wrist": wrist, "palm_off": palm - wrist, "sign": sign}

    def hand_world_frame(self, side, along, thumb):
        sign = self.hand_rest[side]["sign"]
        a = along.normalized()
        t = (thumb - a * thumb.dot(a)).normalized()
        up = a.cross(t) * sign
        return Matrix((a, t, up)).transposed()

    def length(self, a, b):
        return (self.head[b] - self.head[a]).length

    # -------------------------------------------------------------- solve

    def solve(self, ctl):
        """Controls -> {bone: world delta matrix D} where pose = D @ rest."""
        D = {}
        H = self.head
        D["Hips"] = Matrix.Translation(ctl.hips_offset) @ about(H["Hips"], ctl.hips_rot)
        chain = ["Spine02", "Spine01", "Spine"]
        parent = "Hips"
        for name, r in zip(chain, ctl.spine):
            D[name] = D[parent] @ about(H[name], r)
            parent = name

        # neck + head: optionally hold a world aim so the head stays level while the body moves
        D["neck"] = D["Spine"] @ about(H["neck"], ctl.neck)
        if ctl.head_aim is not None:
            carried = D["neck"].to_3x3().normalized()
            need = carried.inverted() @ ctl.head_aim
            need = slerp_m(need, 1.0 - ctl.head_follow)
            D["neck"] = D["neck"] @ about(H["neck"], slerp_m(need, 0.35))
            carried2 = D["neck"].to_3x3().normalized()
            rest_head = carried2.inverted() @ ctl.head_aim
            rest_head = slerp_m(rest_head, 1.0 - ctl.head_follow)
            D["Head"] = D["neck"] @ about(H["Head"], rest_head @ ctl.head)
        else:
            D["Head"] = D["neck"] @ about(H["Head"], ctl.head)

        for side in ("Left", "Right"):
            D[side + "Shoulder"] = D["Spine"] @ about(H[side + "Shoulder"], ctl.clavicle[side])

        D["Weapon"] = ctl.weapon @ self.rest["Weapon"].inverted()

        self.elbows = {}
        for side in ("Left", "Right"):
            self._solve_arm(D, ctl, side)
            self._solve_fingers(D, ctl, side)
            self._solve_leg(D, ctl, side)
            self._solve_ik_goals(D, side)

        for name in self.order:  # everything else rides its parent
            if name not in D:
                p = self.parent[name]
                D[name] = D[p] if p else Matrix.Identity(4)
        return D

    def _solve_arm(self, D, ctl, side):
        up_b, lo_b, hand_b = side + "Arm", side + "ForeArm", side + "Hand"
        H = self.head
        root = D[side + "Shoulder"] @ H[up_b]
        goal = ctl.hands[side]
        if goal is None:
            fk = ctl.arm_fk[side] or (Matrix.Identity(3),) * 3
            D[up_b] = D[side + "Shoulder"] @ about(H[up_b], fk[0])
            D[lo_b] = D[up_b] @ about(H[lo_b], fk[1])
            D[hand_b] = D[lo_b] @ about(H[hand_b], fk[2])
            return
        rest = self.hand_rest[side]
        hand_m = self.hand_world_frame(side, goal.along, goal.thumb)
        r_end = hand_m @ rest["m"].transposed()
        wrist = goal.palm - r_end @ rest["palm_off"]
        l1 = self.length(up_b, lo_b)
        l2 = self.length(lo_b, hand_b)
        mid, end = two_bone(root, wrist, l1, l2, goal.elbow)
        e_rest = Vector((0.0, 1.0, 0.0))  # elbows point back when the T-pose arm bends forward
        e_new = elbow_dir(root, mid, end)
        r1 = frame(mid - root, e_new) @ frame(H[lo_b] - H[up_b], e_rest).transposed()
        r2 = frame(end - mid, e_new) @ frame(H[hand_b] - H[lo_b], e_rest).transposed()
        # share half of the wrist twist with the forearm (no twist bones on this rig)
        axis = (H[hand_b] - H[lo_b]).normalized()
        q_rel = r2.to_quaternion().inverted() @ r_end.to_quaternion()
        proj = axis * Vector(q_rel[1:]).dot(axis)
        q_tw = Quaternion((q_rel.w, proj.x, proj.y, proj.z))
        if q_tw.magnitude > 1e-6:
            q_tw.normalize()
            r2 = r2 @ Quaternion().slerp(q_tw, 0.5).to_matrix()
        D[up_b] = Matrix.Translation(root) @ r1.to_4x4() @ Matrix.Translation(-H[up_b])
        D[lo_b] = Matrix.Translation(mid) @ r2.to_4x4() @ Matrix.Translation(-H[lo_b])
        D[hand_b] = Matrix.Translation(end) @ r_end.to_4x4() @ Matrix.Translation(-H[hand_b])

    ELBOW_HINT = 0.15

    def _solve_ik_goals(self, D, side):
        """IK_Hand_* copy the solved hands (their rest equals the hand bones); IK_Elbow_* sit behind the elbows."""
        suffix = "L" if side == "Left" else "R"
        hand_ik = "IK_Hand_" + suffix
        if hand_ik not in self.head:
            return
        D[hand_ik] = D[side + "Hand"]
        lo_b = side + "ForeArm"
        root = D[side + "Arm"] @ self.head[side + "Arm"]
        mid = D[lo_b] @ self.head[lo_b]
        end = D[side + "Hand"] @ self.head[side + "Hand"]
        e = elbow_dir(root, mid, end)
        if e.dot(Vector((0, 1, 0))) < -0.95 and (mid - root).cross(end - mid).length < 1e-4:
            e = Vector((0, 1, 0))
        target = mid + e * self.ELBOW_HINT
        D["IK_Elbow_" + suffix] = Matrix.Translation(target - self.head["IK_Elbow_" + suffix])

    def _solve_fingers(self, D, ctl, side):
        H = self.head
        hand = side + "Hand"
        curl = ctl.curl[side]
        # fingers curl toward the palm: about the bone's own X axis (sign found by rendering)
        x1 = self.rest[side + "Fingers1"].to_3x3().col[0]
        sign = -1.0
        D[side + "Fingers1"] = D[hand] @ about(H[side + "Fingers1"], axis_angle(x1, sign * 75 * curl))
        x2 = self.rest[side + "Fingers2"].to_3x3().col[0]
        D[side + "Fingers2"] = D[side + "Fingers1"] @ about(H[side + "Fingers2"], axis_angle(x2, sign * 70 * curl))
        xt = self.rest[side + "Thumb"].to_3x3().col[0]
        D[side + "Thumb"] = D[hand] @ about(H[side + "Thumb"], axis_angle(xt, sign * 45 * ctl.thumb[side]))

    def _solve_leg(self, D, ctl, side):
        up_b, lo_b, foot_b, toe_b = side + "UpLeg", side + "Leg", side + "Foot", side + "ToeBase"
        H = self.head
        goal = ctl.feet[side]
        root = D["Hips"] @ H[up_b]
        fr = self.foot_rest[side]
        if goal is None:
            D[up_b] = D["Hips"]
            D[lo_b] = D["Hips"]
            D[foot_b] = D["Hips"]
            return
        yaw = Matrix.Rotation(math.radians(goal.yaw), 3, "Z")
        lateral = yaw @ (fr["ball"] - fr["heel"]).normalized().cross(UP)  # foot's own lateral axis
        pitch = axis_angle(lateral, -goal.pitch)  # toes up for positive pitch
        r_foot = pitch @ yaw
        plant = goal.plant + UP * goal.lift
        if goal.pitch >= 0:
            pivot_rest = fr["heel"]
        else:
            pivot_rest = fr["ball"]
        pivot = plant + yaw @ (pivot_rest - fr["ref"])
        ankle = pivot + r_foot @ (fr["ankle"] - pivot_rest)
        knee = goal.knee if goal.knee is not None else (yaw @ FWD + RIGHT * (0.15 if side == "Right" else -0.15))
        l1 = self.length(up_b, lo_b)
        l2 = self.length(lo_b, foot_b)
        mid, end = two_bone(root, ankle, l1, l2, knee)
        e_rest = elbow_dir(H[up_b], H[lo_b], H[foot_b])
        e_new = elbow_dir(root, mid, end)
        r1 = frame(mid - root, e_new) @ frame(H[lo_b] - H[up_b], e_rest).transposed()
        r2 = frame(end - mid, e_new) @ frame(H[foot_b] - H[lo_b], e_rest).transposed()
        D[up_b] = Matrix.Translation(root) @ r1.to_4x4() @ Matrix.Translation(-H[up_b])
        D[lo_b] = Matrix.Translation(mid) @ r2.to_4x4() @ Matrix.Translation(-H[lo_b])
        D[foot_b] = Matrix.Translation(end) @ r_foot.to_4x4() @ Matrix.Translation(-H[foot_b])
        # toes stay flat on the floor while the heel is up, plus any extra bend
        toe_bend = max(0.0, -goal.pitch) + goal.toe if goal.lift < 0.02 else goal.toe
        D[toe_b] = D[foot_b] @ about(fr["ball_joint"], axis_angle(lateral, toe_bend))

    # -------------------------------------------------------------- keying

    # Procedural in Unity (tail sway, ear twitch) or constant helpers: never keyed, so the Animator leaves them alone.
    UNKEYED_PREFIXES = ("Tail", "Ear_", "head_end", "headfront", "FP_Grip")

    def key(self, D, frame_no):
        pose = {n: D[n] @ self.rest[n] for n in self.order}
        for name in self.order:
            if name.startswith(self.UNKEYED_PREFIXES):
                continue
            pb = self.arm.pose.bones[name]
            p = self.parent[name]
            if p is None:
                basis = self.rest[name].inverted() @ pose[name]
            else:
                basis = (pose[p] @ self.rest[p].inverted() @ self.rest[name]).inverted() @ pose[name]
            loc, q, _ = basis.decompose()
            pb.rotation_mode = "QUATERNION"
            pb.location = loc
            pb.rotation_quaternion = q
            pb.scale = (1, 1, 1)
            pb.keyframe_insert("location", frame=frame_no, group=name)
            pb.keyframe_insert("rotation_quaternion", frame=frame_no, group=name)

    def world(self, D, name, point=None):
        """World position of a bone head (or a rest-space point carried by that bone) for a solved pose."""
        p = self.head[name] if point is None else point
        return D[name] @ p


def elbow_dir(root, mid, end):
    line = end - root
    if line.length < 1e-6:
        return Vector((0, 1, 0))
    t = (mid - root).dot(line) / line.length_squared
    off = mid - (root + line * t)
    if off.length < 1e-6:
        return Vector((0, 1, 0))
    return off.normalized()


def two_bone(root, target, l1, l2, pole):
    d = target - root
    dist = d.length
    dn = d.normalized() if dist > 1e-6 else Vector((0, 0, -1))
    reach = (l1 + l2) * 0.9995
    dist_c = max(abs(l1 - l2) + 1e-4, min(dist, reach))
    p = pole - dn * pole.dot(dn)
    if p.length < 1e-6:
        p = Vector((0, 1, 0)) - dn * dn.y
    p.normalize()
    cos_a = (l1 * l1 + dist_c * dist_c - l2 * l2) / (2 * l1 * dist_c)
    a = math.acos(max(-1.0, min(1.0, cos_a)))
    mid = root + dn * math.cos(a) * l1 + p * math.sin(a) * l1
    end = root + dn * dist_c
    return mid, end


def reach_report(rig, D, ctl):
    """Distance by which each IK goal is out of reach (0 = reached)."""
    out = {}
    for side in ("Left", "Right"):
        g = ctl.hands[side]
        if g is not None:
            hand = side + "Hand"
            got = D[hand] @ (rig.head[hand] + rig.hand_rest[side]["palm_off"])
            out[side + "Hand"] = (got - g.palm).length
    return out


def new_action(arm, name):
    act = bpy.data.actions.get(name)
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = act
    return act
