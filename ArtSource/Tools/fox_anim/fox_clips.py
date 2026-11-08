"""Clip registry: every Fox Gunner motion as a function of time -> Controls.

Each clip becomes an Action named A_FoxGunner_<Name>. Loops repeat exactly (last frame = first frame).
Locomotion speeds are the ground speeds the Unity blend tree is built with (exported to A_FoxGunner_Clips.json).

Layers (Unity):
  Base       full body: idle, walk/run/sprint, jump/fall/land, dash, slide
  UpperBody  spine/arms/head override: reload, ADS, equip, inspect, skills, interactions, heavy hit
  Additive   upper-body additive: fire recoil, light hit
  Reaction   full body override: knockback, downed, revived, death
"""
import math

from mathutils import Matrix, Vector

from fox_gait import RUN, SPRINT, WALK, Gait, gait_controls, pelvis_height
from fox_motion import (FWD, RIGHT, UP, Controls, Foot, Hand, bump, c, clamp01, decay, ease_in, ease_out, lerp, rot,
                        smooth, smoother, spring)
from fox_stance import Stance, base_controls, hold_rifle, rs, rs_dir

FPS = 60
CLIPS = []
RIG = [None]  # set by the builder before clips run


class ClipSpec:
    def __init__(self, name, seconds, fn, loop=False, layer="Base", speed=0.0, direction=(0.0, 0.0),
                 cycle=0.0, notes=""):
        self.name = name
        self.seconds = seconds
        self.fn = fn
        self.loop = loop
        self.layer = layer
        self.speed = speed
        self.direction = direction
        self.cycle = cycle
        self.notes = notes

    @property
    def frames(self):
        return max(2, int(round(self.seconds * FPS)))


def clip(name, seconds, loop=False, layer="Base", notes=""):
    def deco(fn):
        CLIPS.append(ClipSpec(name, seconds, fn, loop, layer, notes=notes))
        return fn
    return deco


ST = Stance()


# ====================================================================== interpolation helpers

def track(t, keys):
    """Catmull-Rom through (time, value) keys; values may be floats or Vectors. Flat before/after."""
    if t <= keys[0][0]:
        return keys[0][1]
    if t >= keys[-1][0]:
        return keys[-1][1]
    for i in range(len(keys) - 1):
        t0, v0 = keys[i]
        t1, v1 = keys[i + 1]
        if t0 <= t <= t1:
            break
    s = (t - t0) / max(1e-6, t1 - t0)
    vp = keys[i - 1][1] if i > 0 else v0
    vn = keys[i + 2][1] if i + 2 < len(keys) else v1
    tp = keys[i - 1][0] if i > 0 else t0 - (t1 - t0)
    tn = keys[i + 2][0] if i + 2 < len(keys) else t1 + (t1 - t0)
    # non-uniform tangents, scaled to this segment; clamped to zero at holds (equal neighbours)
    m0 = (v1 - vp) * ((t1 - t0) / max(1e-6, t1 - tp))
    m1 = (vn - v0) * ((t1 - t0) / max(1e-6, tn - t0))
    if i == 0:
        m0 = m0 * 0.0
    if i + 2 >= len(keys):
        m1 = m1 * 0.0
    s2, s3 = s * s, s * s * s
    return v0 * (2 * s3 - 3 * s2 + 1) + m0 * (s3 - 2 * s2 + s) + v1 * (-2 * s3 + 3 * s2) + m1 * (s3 - s2)


def eased(t, keys):
    """Piecewise smoothstep between keys (holds exactly, no overshoot)."""
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            return v0 + (v1 - v0) * smooth((t - t0) / max(1e-6, t1 - t0))
    return keys[-1][1]


def V(*a):
    return Vector(a)


# ====================================================================== pose helpers

def breathing(ctl, t, period=3.2, amount=1.0):
    """Chest rise and shoulder lift on top of a pose. Returns the breath phase (-1..1)."""
    b = math.sin(2 * math.pi * t / period)
    ctl.spine = [s @ rot(pitch=-0.6 * b * amount) for s in ctl.spine]
    for side in ("Left", "Right"):
        ctl.clavicle[side] = ctl.clavicle[side] @ rot(roll=(1.2 if side == "Left" else -1.2) * b * amount)
    return b


def idle_controls(t, period=3.2):
    ctl = base_controls(ST)
    b = breathing(ctl, t, period)
    w = math.sin(2 * math.pi * t / period * 0.5)  # weight shift, half as fast as breathing
    ctl.hips_offset = ctl.hips_offset + c(0.008 * w, 0.0, -0.002 * (1 - b))
    ctl.hips_rot = ctl.hips_rot @ rot(roll=0.8 * w)
    grip = Vector(ST.grip) + Vector((0.002 * w, 0.0, 0.004 * b))
    hold_rifle(ctl, ST, ST.weapon_matrix(grip=grip, r3=ST.rifle_rotation() @ rot(pitch=0.4 * b)))
    ctl.head_aim = rot(pitch=ST.head_pitch + 0.6 * b, yaw=0.8 * math.sin(2 * math.pi * t / period * 0.5 + 1.0))
    return ctl


def weapon_at(grip_delta=(0, 0, 0), pitch=0.0, yaw=0.0, roll=0.0, grip=None):
    """Rifle frame relative to the stance (pitch = muzzle up > 0)."""
    g = (Vector(ST.grip) + Vector(grip_delta)) if grip is None else Vector(grip)
    return ST.weapon_matrix(grip=g, r3=ST.rifle_rotation() @ rot(pitch=-pitch, yaw=yaw, roll=roll))


def on_rifle_left(weapon):
    return Hand(rs(weapon, ST.left_palm), rs_dir(weapon, ST.left_along), rs_dir(weapon, ST.left_thumb), ST.left_elbow)


def hand_c(palm, along, thumb, elbow):
    """Left/right hand goal in character space."""
    return Hand(c(*palm), c(*along), c(*thumb), c(*elbow))


def hand_rs(weapon, palm, along, thumb, elbow):
    return Hand(rs(weapon, palm), rs_dir(weapon, along), rs_dir(weapon, thumb), c(*elbow))


def blend_hand(a, b, t):
    if t <= 0:
        return a
    if t >= 1:
        return b
    return Hand(a.palm.lerp(b.palm, t), a.along.lerp(b.along, t), a.thumb.lerp(b.thumb, t), a.elbow.lerp(b.elbow, t))


class LeftTrack:
    """Keyed left-hand path. A key is (time, goal, curl) where goal is 'rifle' or a function weapon -> Hand.
    Positions use Catmull-Rom through the keys; LeftHandIK is 1 on 'rifle' keys."""

    def __init__(self, keys):
        self.keys = keys

    def apply(self, ctl, t, weapon):
        hands, iks, curls = [], [], []
        for kt, goal, curl in self.keys:
            if goal == "rifle":
                hands.append((kt, on_rifle_left(weapon)))
                iks.append((kt, 1.0))
                curls.append((kt, ST.curl_left if curl is None else curl))
            else:
                hands.append((kt, goal(weapon)))
                iks.append((kt, 0.0))
                curls.append((kt, 0.2 if curl is None else curl))
        palm = track(t, [(k, h.palm) for k, h in hands])
        along = track(t, [(k, h.along.normalized()) for k, h in hands])
        thumb = track(t, [(k, h.thumb.normalized()) for k, h in hands])
        elbow = eased(t, [(k, h.elbow) for k, h in hands])
        ctl.hands["Left"] = Hand(palm, along, thumb, elbow)
        ctl.left_ik = eased(t, iks)
        ctl.curl["Left"] = eased(t, curls)
        ctl.thumb["Left"] = lerp(0.15, 0.5, ctl.curl["Left"])
        ctl.cast = 1.0 - ctl.left_ik


def upper_from_idle(t, weapon, spine=(0, 0, 0), head=(0, 0), period=3.2):
    """Idle legs and pelvis, with an extra spine rotation (pitch, yaw, roll) and head (pitch, yaw)."""
    ctl = idle_controls(t, period)
    p, y, r = spine
    ctl.spine = [s @ rot(pitch=p * w, yaw=y * w, roll=r * w) for s, w in zip(ctl.spine, (0.3, 0.35, 0.35))]
    ctl.head_aim = rot(pitch=ST.head_pitch + head[0], yaw=head[1])
    hold_rifle(ctl, ST, weapon)
    return ctl


def stance_feet(ctl, lift_l=0.0, lift_r=0.0):
    for side, (r, f, yaw) in (("Left", ST.left_foot), ("Right", ST.right_foot)):
        ctl.feet[side] = Foot(c(r, f, 0.0), yaw=yaw, lift=lift_l if side == "Left" else lift_r)


def set_body(ctl, drop=0.0, pitch=0.0, yaw=0.0, roll=0.0, shift=(0, 0)):
    """Pelvis relative to the stance: drop (m, down > 0), rotation in degrees, shift (right, forward) m."""
    ctl.hips_offset = c(ST.hips_shift.x + shift[0], ST.hips_shift.y + shift[1], ST.hips_shift.z - ST.crouch - drop)
    ctl.hips_rot = rot(pitch=ST.hips_pitch + pitch, yaw=ST.hips_yaw + yaw, roll=roll)


def set_spine(ctl, pitch=0.0, yaw=0.0, roll=0.0):
    ctl.spine = [rot(pitch=(ST.spine_pitch + pitch) * w, yaw=(ST.spine_yaw + yaw) * w, roll=(ST.spine_roll + roll) * w)
                 for w in (0.3, 0.35, 0.35)]


def ground_feet(ctl, rig, weights=(1.0, 1.0)):
    """Lower the pelvis if a planted leg would over-straighten (shared with locomotion)."""
    ctl.hips_offset.z = pelvis_height(rig, ctl.feet, {"Left": weights[0], "Right": weights[1]}, ctl.hips_rot,
                                      ctl.hips_offset)


# ====================================================================== 01-10 locomotion

@clip("Combat_Idle", 3.2, loop=True, notes="breathing and weight shift; the base pose every clip shares")
def combat_idle(t, u):
    return idle_controls(t)


def _gait_clip(name, base, direction, extra=None):
    params = dict(base)
    params.update(extra or {})
    g = Gait(direction=direction, **params)

    def fn(t, u, g=g):
        return gait_controls(RIG[0], ST, g, t)

    CLIPS.append(ClipSpec(name, g.cycle, fn, loop=True, speed=g.speed, direction=direction, cycle=g.cycle))


def _center_clip():
    """Still stance with the walk's cycle length: the centre of the locomotion blend tree, so slow speeds shrink
    the stride without stretching the cycle (standing still plays Combat_Idle in its own state)."""
    seconds = WALK["cycle"]

    def fn(t, u):
        return idle_controls(0.0)

    CLIPS.append(ClipSpec("Locomotion_Center", seconds, fn, loop=True, cycle=seconds,
                          notes="blend-tree centre (not one of the requested motions)"))


_center_clip()
_gait_clip("Walk_Forward", WALK, (0.0, 1.0))
_gait_clip("Walk_Backward", WALK, (0.0, -1.0), dict(lean=2, heel_strike=4, toe_off=10))
_gait_clip("Walk_Left", WALK, (-1.0, 0.0), dict(heel_strike=4, toe_off=12, width=0.12))
_gait_clip("Walk_Right", WALK, (1.0, 0.0), dict(heel_strike=4, toe_off=12, width=0.12))
_gait_clip("Run_Forward", RUN, (0.0, 1.0))
_gait_clip("Run_Backward", RUN, (0.0, -1.0), dict(lean=-2, counter_lean=0.0, heel_strike=0, toe_off=14,
                                                  stance_center=0.05, lift=0.15))
_gait_clip("Run_Left", RUN, (-1.0, 0.0), dict(heel_strike=2, toe_off=22, lift=0.17))
_gait_clip("Run_Right", RUN, (1.0, 0.0), dict(heel_strike=2, toe_off=22, lift=0.17))
_gait_clip("Sprint", SPRINT, (0.0, 1.0))


# ====================================================================== 11-16 air, dash, slide

def air_feet(ctl, left, right, pitch_l=-15.0, pitch_r=-15.0):
    """Feet relative to the floor under the hips (in-place clip; the controller carries the height)."""
    ctl.feet["Left"] = Foot(c(left[0], left[1], 0.0), lift=left[2], pitch=pitch_l, yaw=-5.0)
    ctl.feet["Right"] = Foot(c(right[0], right[1], 0.0), lift=right[2], pitch=pitch_r, yaw=-15.0)


@clip("Jump_Start", 0.22, notes="take-off: the motor launches at once, so this is the push, not a wind-up")
def jump_start(t, u):
    ctl = idle_controls(0.0)
    drop = lerp(0.10, -0.02, smooth(u / 0.7))
    set_body(ctl, drop=drop, pitch=lerp(10, 2, smooth(u)))
    set_spine(ctl, pitch=lerp(6, -2, smooth(u)))
    push = smooth(u / 0.45)
    leave = smooth((u - 0.35) / 0.65)
    ctl.feet["Left"] = Foot(c(-0.12, lerp(0.07, 0.04, u), 0.0), lift=lerp(0, 0.10, leave), pitch=lerp(0, -40, push),
                            yaw=-8)
    ctl.feet["Right"] = Foot(c(0.13, lerp(-0.09, -0.12, u), 0.0), lift=lerp(0, 0.05, leave), pitch=lerp(0, -45, push),
                             yaw=-25)
    w = weapon_at((0, 0, lerp(-0.03, 0.01, smooth(u))), pitch=lerp(-2, 3, smooth(u)))
    hold_rifle(ctl, ST, w)
    ground_feet(ctl, RIG[0], (1 - leave, 1 - leave))
    return ctl


@clip("Jump_Loop", 0.6, loop=True, notes="rising / apex, knees tucked")
def jump_loop(t, u):
    ctl = idle_controls(t, period=0.6)
    s = math.sin(2 * math.pi * u)
    set_body(ctl, drop=0.0, pitch=4 + 1.5 * s)
    set_spine(ctl, pitch=-1 - 1.0 * s)
    air_feet(ctl, (-0.11, 0.10 + 0.01 * s, 0.24 + 0.012 * s), (0.13, -0.08 - 0.01 * s, 0.17 - 0.01 * s), -25, -30)
    hold_rifle(ctl, ST, weapon_at((0, 0, 0.012 + 0.004 * s), pitch=2))
    return ctl


@clip("Fall", 0.6, loop=True, notes="legs reach for the floor; tail balance is procedural in Unity")
def fall(t, u):
    ctl = idle_controls(t, period=0.6)
    s = math.sin(2 * math.pi * u)
    set_body(ctl, drop=0.0, pitch=-3 + 1.5 * s, roll=1.5 * s)
    set_spine(ctl, pitch=-2)
    air_feet(ctl, (-0.12, 0.08 + 0.025 * s, 0.07 + 0.02 * s), (0.14, -0.05 - 0.025 * s, 0.05 - 0.02 * s), -18, -22)
    hold_rifle(ctl, ST, weapon_at((0, 0, 0.02 + 0.005 * s), pitch=4 + s))
    ctl.head_aim = rot(pitch=ST.head_pitch + 4)
    return ctl


@clip("Land", 0.4, notes="knees absorb the impact, then back to the stance")
def land(t, u):
    ctl = idle_controls(0.0)
    k = smooth(t / 0.07) if t < 0.07 else math.exp(-((t - 0.07) / 0.12) ** 2)
    set_body(ctl, drop=0.11 * k, pitch=10 * k)
    set_spine(ctl, pitch=9 * k)
    stance_feet(ctl)
    hold_rifle(ctl, ST, weapon_at((0, 0.01 * k, -0.045 * k), pitch=-5 * k))
    ctl.head_aim = rot(pitch=ST.head_pitch + 6 * k)
    ground_feet(ctl, RIG[0])
    return ctl


def _dash(name, direction):
    dr, df = direction

    def fn(t, u):
        ctl = idle_controls(0.0)
        a = bump(t, 0.0, 0.05, 0.17, 0.32)
        lean_p = 20 * df if df > 0 else 16 * df
        lean_r = 18 * dr
        turn = -22 * dr if dr else 0.0
        set_body(ctl, drop=0.07 * a, pitch=lean_p * a, roll=lean_r * a, yaw=turn * a, shift=(-0.04 * dr * a, -0.04 * df * a))
        set_spine(ctl, pitch=-0.4 * lean_p * a, yaw=-turn * a, roll=-0.5 * lean_r * a)
        # lead foot glides along the dash, trailing foot pushes away from it
        lead, trail = ("Right", "Left") if (dr > 0 or (dr == 0 and df < 0)) else ("Left", "Right")
        base = {"Left": ST.left_foot, "Right": ST.right_foot}
        for side in ("Left", "Right"):
            r0, f0, yaw0 = base[side]
            if side == lead:
                off = (0.22 * dr, 0.24 * df)
                ctl.feet[side] = Foot(c(r0 + off[0] * a, f0 + off[1] * a, 0.0), lift=0.05 * a, pitch=8 * a * max(df, 0) - 10 * a * max(-df, 0), yaw=yaw0)
            else:
                off = (-0.22 * dr, -0.28 * df)
                ctl.feet[side] = Foot(c(r0 + off[0] * a, f0 + off[1] * a, 0.0), lift=0.02 * a, pitch=-35 * a, yaw=yaw0)
        hold_rifle(ctl, ST, weapon_at((0.0, -0.03 * a, -0.02 * a), pitch=-6 * a, roll=-6 * dr * a))
        ctl.head_aim = rot(pitch=ST.head_pitch - 2 * a)
        ground_feet(ctl, RIG[0], (1 - a * 0.7, 1 - a * 0.7))
        return ctl

    CLIPS.append(ClipSpec(name, 0.32, fn, loop=False, layer="Base", direction=direction,
                          notes="0.16 s dash at 19 m/s plus recovery"))


_dash("Dash_Forward", (0.0, 1.0))
_dash("Dash_Backward", (0.0, -1.0))
_dash("Dash_Left", (-1.0, 0.0))
_dash("Dash_Right", (1.0, 0.0))


@clip("Slide", 1.0, notes="drop into a feet-first slide, hold, get up (no slide mechanic yet: hook only)")
def slide(t, u):
    ctl = idle_controls(0.0)
    a = eased(t, [(0.0, 0.0), (0.18, 1.0), (0.72, 1.0), (1.0, 0.0)])
    set_body(ctl, drop=0.40 * a, pitch=-18 * a, yaw=10 * a, roll=-6 * a)
    set_spine(ctl, pitch=16 * a, yaw=-8 * a)
    ctl.feet["Left"] = Foot(c(lerp(-0.13, -0.06, a), lerp(0.07, 0.50, a), 0.0), pitch=28 * a, yaw=-8)
    ctl.feet["Right"] = Foot(c(lerp(0.13, 0.20, a), lerp(-0.09, 0.02, a), 0.0), pitch=-30 * a, yaw=lerp(-28, -70, a),
                             knee=c(0.9, 0.2, 0.3) * a + c(0.15, 1, 0) * (1 - a))
    hold_rifle(ctl, ST, weapon_at((-0.04 * a, -0.03 * a, -0.15 * a), pitch=8 * a))
    ctl.head_aim = rot(pitch=ST.head_pitch - 4 * a)
    ground_feet(ctl, RIG[0])
    return ctl


# ====================================================================== 17-23 rifle

@clip("Rifle_Idle", 3.2, loop=True, layer="UpperBody", notes="same pose as Combat_Idle (upper body)")
def rifle_idle(t, u):
    return idle_controls(t)


@clip("Rifle_Fire", 0.16, layer="Additive", notes="additive recoil; restarted on every shot (600 rpm)")
def rifle_fire(t, u):
    k = smooth(t / 0.022) if t < 0.022 else math.exp(-(t - 0.022) * 26) * (1 - 0.0)
    ctl = idle_controls(0.0)
    ctl.spine = [s @ rot(pitch=-1.6 * k * w * 3) for s, w in zip(ctl.spine, (0.2, 0.35, 0.45))]
    ctl.clavicle["Right"] = rot(yaw=-3 * k)
    hold_rifle(ctl, ST, weapon_at((0.0, -0.028 * k, 0.006 * k), pitch=3.2 * k))
    ctl.head_aim = rot(pitch=ST.head_pitch - 0.8 * k)
    return ctl


RELOAD = LeftTrack([
    (0.00, "rifle", None),
    (0.16, lambda w: hand_rs(w, (-0.07, 0.10, -0.05), (0.45, 0.85, -0.2), (-0.2, 0.2, 0.95), (-0.6, 0.0, -0.8)), 0.4),
    (0.32, lambda w: hand_rs(w, (-0.035, 0.165, -0.035), (0.55, 0.8, 0.1), (-0.3, 0.2, 0.9), (-0.6, 0.0, -0.8)), 0.85),
    (0.50, lambda w: hand_rs(w, (-0.07, 0.15, -0.20), (0.4, 0.85, 0.2), (-0.3, 0.2, 0.9), (-0.6, -0.2, -0.75)), 0.9),
    (0.72, lambda w: hand_c((-0.15, 0.08, 0.86), (0.3, 0.4, -0.85), (0.2, 0.9, 0.1), (-0.8, -0.4, -0.3)), 0.9),
    (0.86, lambda w: hand_c((-0.15, 0.09, 0.88), (0.3, 0.5, -0.8), (0.2, 0.9, 0.1), (-0.8, -0.4, -0.3)), 0.95),
    (1.08, lambda w: hand_rs(w, (-0.05, 0.165, -0.14), (0.5, 0.8, 0.25), (-0.3, 0.2, 0.9), (-0.6, -0.1, -0.8)), 0.9),
    (1.24, lambda w: hand_rs(w, (-0.035, 0.165, -0.025), (0.55, 0.8, 0.1), (-0.3, 0.2, 0.9), (-0.6, 0.0, -0.8)), 0.9),
    (1.32, lambda w: hand_rs(w, (-0.035, 0.165, -0.045), (0.55, 0.8, 0.1), (-0.3, 0.2, 0.9), (-0.6, 0.0, -0.8)), 0.5),
    (1.44, lambda w: hand_rs(w, (-0.06, 0.08, 0.075), (0.3, 0.9, -0.3), (0.2, 0.3, 0.9), (-0.6, 0.0, -0.8)), 0.1),
    (1.70, "rifle", None),
])


@clip("Rifle_Reload", 1.7, layer="UpperBody", notes="energy cell swap; WD_AssaultRifle.reloadTime = 1.7 s")
def rifle_reload(t, u):
    tilt = eased(t, [(0.0, 0.0), (0.2, 1.0), (1.3, 1.0), (1.62, 0.0)])
    bump_in = bump(t, 1.18, 1.25, 1.25, 1.36)
    w = weapon_at((-0.02 * tilt, -0.05 * tilt, 0.03 * tilt + 0.012 * bump_in), pitch=9 * tilt + 2 * bump_in,
                  yaw=8 * tilt, roll=-22 * tilt)
    ctl = upper_from_idle(t, w, spine=(3 * tilt, 4 * tilt, 0), head=(10 * tilt, 6 * tilt))
    RELOAD.apply(ctl, t, w)
    return ctl


ADS_GRIP = (-0.03, 0.05, 0.125)


def ads_controls(t, a, period=3.2):
    w = weapon_at(tuple(x * a for x in ADS_GRIP), pitch=2 * a)
    ctl = upper_from_idle(t, w, spine=(-3 * a, 6 * a, 0), head=(9 * a, 2 * a), period=period)
    ctl.clavicle["Right"] = rot(roll=-4 * a)
    return ctl


@clip("Rifle_ADS_Enter", 0.18, layer="UpperBody", notes="rifle up to the eye (no ADS input in the game yet: hook only)")
def ads_enter(t, u):
    return ads_controls(0.0, ease_out(u, 2.5))


@clip("Rifle_ADS_Idle", 2.4, loop=True, layer="UpperBody", notes="aim-down-sights hold (added so ADS can loop)")
def ads_idle(t, u):
    ctl = ads_controls(t, 1.0, period=2.4)
    breathing(ctl, t, 2.4, 0.5)
    return ctl


@clip("Rifle_ADS_Exit", 0.18, layer="UpperBody", notes="back to the shared rifle pose")
def ads_exit(t, u):
    return ads_controls(0.0, 1.0 - ease_in(u, 1.6))


EQUIP = LeftTrack([
    (0.00, lambda w: hand_c((-0.22, 0.02, 0.86), (0.1, 0.3, -0.95), (0.0, 1.0, 0.0), (-0.9, -0.3, -0.2)), 0.35),
    (0.30, lambda w: hand_c((-0.10, 0.24, 0.98), (0.6, 0.7, 0.0), (0.0, 0.0, 1.0), (-0.6, 0.0, -0.8)), 0.3),
    (0.42, "rifle", None),
    (0.60, "rifle", None),
])


@clip("Rifle_Equip", 0.6, layer="UpperBody", notes="rifle swings up from low on the right; plays on spawn/revive")
def rifle_equip(t, u):
    a = spring(t - 0.05, 2.6, 7.0) if t > 0.05 else 0.0
    a = clamp01(a) if t < 0.3 else a
    w = weapon_at(grip=(lerp(0.20, ST.grip.x, a), lerp(0.02, ST.grip.y, a), lerp(0.74, ST.grip.z, a)),
                  pitch=lerp(-75, 0, a), yaw=lerp(-20, 0, a), roll=lerp(25, 0, a))
    ctl = upper_from_idle(t, w, spine=(lerp(6, 0, a), lerp(-8, 0, a), 0), head=(lerp(12, 0, a), lerp(-10, 0, a)))
    EQUIP.apply(ctl, t, w)
    return ctl


INSPECT = LeftTrack([
    (0.00, "rifle", None),
    (0.62, "rifle", None),
    (0.74, lambda w: hand_rs(w, (-0.05, 0.03, 0.22), (0.2, 0.8, -0.55), (0.8, 0.2, 0.3), (-0.6, -0.1, -0.8)), 0.55),
    (0.84, lambda w: hand_rs(w, (-0.04, 0.03, 0.205), (0.2, 0.8, -0.55), (0.8, 0.2, 0.3), (-0.6, -0.1, -0.8)), 0.6),
    (0.96, "rifle", None),
    (2.40, "rifle", None),
])


@clip("Rifle_Inspect", 2.4, layer="UpperBody", notes="idle flourish: look at both sides of the rifle")
def rifle_inspect(t, u):
    side = eased(t, [(0.0, 0.0), (0.4, 1.0), (1.0, 1.0), (1.45, -1.0), (1.9, -1.0), (2.35, 0.0)])
    near = eased(t, [(0.0, 0.0), (0.4, 1.0), (1.9, 1.0), (2.35, 0.0)])
    roll = -48 * side if side > 0 else -40 * side
    w = weapon_at((-0.04 * near, -0.07 * near, 0.06 * near), pitch=6 * near, yaw=22 * side, roll=roll)
    ctl = upper_from_idle(t, w, spine=(4 * near, 6 * side, 0), head=(16 * near, 14 * side))
    INSPECT.apply(ctl, t, w)
    return ctl


# ====================================================================== 24-30 skill casts (left hand)

def cast_rifle(a, drop=0.04):
    """The right hand keeps the rifle (lowered a little, canted out) while the left hand casts."""
    return weapon_at((0.02 * a, -0.03 * a, -drop * a), pitch=-9 * a, yaw=-4 * a, roll=10 * a)


PALM_FWD = dict(along=(0.0, 0.25, 1.0), thumb=(1.0, 0.0, 0.0), elbow=(-0.4, -0.2, -0.9))


def palm_forward(palm):
    return lambda w: hand_c(palm, PALM_FWD["along"], PALM_FWD["thumb"], PALM_FWD["elbow"])


def palm_strike(palm, along=(0.0, 0.55, 0.85)):
    """Left palm pushed out flat at the target: fingers up and forward, thumb in, elbow down."""
    return lambda w: hand_c(palm, along, (1.0, 0.0, 0.0), (-0.5, -0.1, -0.85))


# Quick cast: chamber, strike, recoil. The palm is pulled back to the left ribs (fingers cupped, palm turned in),
# then driven straight out at shoulder height by the chest twisting right, leaning in and the left shoulder
# reaching forward: it ends 17 cm further out than the hand on the handguard (the old thrust only moved sideways).
# Full reach at 0.12 s is when the projectile leaves the palm (CardDefinition.projectile.launchDelay); the shot
# kicks the hand back and the wrist up, it settles, holds and returns to the rifle.
QUICK_STRIKE = 0.12
QUICK = LeftTrack([
    (0.00, "rifle", None),
    (0.055, lambda w: hand_c((-0.19, 0.13, 0.93), (0.2, 0.9, 0.4), (0.0, 0.0, 1.0), (-0.6, -0.6, -0.5)), 0.55),
    (QUICK_STRIKE, palm_strike((-0.13, 0.54, 1.00)), -0.08),
    (0.155, palm_strike((-0.13, 0.50, 1.025), along=(0.0, 0.4, 0.92)), 0.0),
    (0.21, palm_strike((-0.13, 0.525, 1.005)), 0.06),
    (0.30, palm_strike((-0.13, 0.515, 1.0)), 0.12),
    (0.45, "rifle", None),
    (0.50, "rifle", None),
])


def quick_drive(t):
    """Body drive of the quick cast: -0.3 at the chamber, 1 at full reach, a dip as the shot kicks, 0 at the end."""
    return track(t, [(0.0, 0.0), (0.05, -0.3), (QUICK_STRIKE, 1.0), (0.16, 0.82), (0.22, 0.92), (0.30, 0.88),
                     (0.46, 0.0), (0.50, 0.0)])


@clip("Skill_QuickCast", 0.5, layer="UpperBody", notes="left palm strike; fireball / ice lance / thunderbolt")
def skill_quick(t, u):
    a = eased(t, [(0.0, 0.0), (0.06, 1.0), (0.32, 1.0), (0.46, 0.0)])
    d = quick_drive(t)
    # the rifle stays nearly where it is (the first-person hand is placed relative to it); the right hand only
    # dips and cants it a little as the chest turns
    w = weapon_at((0.01 * a, -0.015 * a - 0.01 * max(0.0, d), -0.015 * a), pitch=-3 * a, yaw=-2 * a, roll=4 * a + 2 * max(0.0, d))
    ctl = upper_from_idle(t, w, spine=(12 * d, -20 * d, 0), head=(0, 0))
    ctl.clavicle["Left"] = rot(yaw=-18 * d)
    ctl.clavicle["Right"] = rot(roll=-3 * max(0.0, d))
    QUICK.apply(ctl, t, w)
    return ctl


CHARGE_POSE = lambda w: hand_c((-0.21, 0.10, 0.93), (0.35, 0.9, 0.1), (-0.9, 0.2, 0.3), (-0.8, -0.5, -0.3))


@clip("Skill_Charged_Start", 0.35, layer="UpperBody", notes="left hand gathers energy at the hip (palm up)")
def skill_charge_start(t, u):
    a = eased(t, [(0.0, 0.0), (0.3, 1.0), (0.35, 1.0)])
    w = cast_rifle(a, 0.03)
    ctl = upper_from_idle(t, w, spine=(4 * a, -10 * a, 0), head=(2 * a, 4 * a))
    LeftTrack([(0.0, "rifle", None), (0.28, CHARGE_POSE, 0.25), (0.35, CHARGE_POSE, 0.3)]).apply(ctl, t, w)
    return ctl


@clip("Skill_Charged_Loop", 0.8, loop=True, layer="UpperBody", notes="holding the charge, trembling hand")
def skill_charge_loop(t, u):
    w = cast_rifle(1.0, 0.03)
    ctl = upper_from_idle(t, w, spine=(4, -10 + 1.5 * math.sin(2 * math.pi * u), 0), head=(2, 4), period=0.8)
    tremble = c(0.004 * math.sin(2 * math.pi * 12 * u), 0.0, 0.004 * math.sin(2 * math.pi * 10 * u + 1))
    circle = c(0.012 * math.cos(2 * math.pi * u), 0.012 * math.sin(2 * math.pi * u), 0.0)
    h = CHARGE_POSE(w)
    ctl.hands["Left"] = Hand(h.palm + tremble + circle, h.along, h.thumb, h.elbow)
    ctl.left_ik = 0.0
    ctl.cast = 1.0
    ctl.curl["Left"] = 0.3 + 0.1 * math.sin(2 * math.pi * 2 * u)
    ctl.thumb["Left"] = 0.2
    return ctl


@clip("Skill_Charged_Release", 0.45, layer="UpperBody", notes="charged thrust forward, torso uncoils")
def skill_charge_release(t, u):
    a = eased(t, [(0.0, 1.0), (0.26, 1.0), (0.42, 0.0)])
    thrust = bump(t, 0.0, 0.09, 0.16, 0.36)
    w = cast_rifle(a, 0.03)
    ctl = upper_from_idle(t, w, spine=(4 * (1 - thrust) + 3 * thrust, lerp(-10, 18, thrust) * a, 0), head=(0, -5 * thrust))
    ctl.clavicle["Left"] = rot(yaw=8 * thrust)
    LeftTrack([
        (0.00, CHARGE_POSE, 0.3),
        (0.09, palm_forward((-0.17, 0.39, 0.98)), 0.0),
        (0.20, palm_forward((-0.17, 0.36, 0.97)), 0.05),
        (0.42, "rifle", None),
        (0.45, "rifle", None),
    ]).apply(ctl, t, w)
    return ctl


AREA = LeftTrack([
    (0.00, "rifle", None),
    (0.18, lambda w: hand_c((-0.14, 0.22, 1.30), (0.1, 0.5, 0.85), (1.0, 0.0, 0.0), (-0.8, 0.1, -0.5)), 0.2),
    (0.30, lambda w: hand_c((-0.11, 0.36, 0.74), (0.1, 1.0, -0.35), (1.0, 0.0, 0.0), (-0.6, 0.0, 0.8)), 0.0),
    (0.44, lambda w: hand_c((-0.11, 0.35, 0.76), (0.1, 1.0, -0.35), (1.0, 0.0, 0.0), (-0.6, 0.0, 0.8)), 0.05),
    (0.66, "rifle", None),
    (0.70, "rifle", None),
])


@clip("Skill_AreaCast", 0.7, layer="UpperBody", notes="left hand raised, slams a field onto the ground ahead")
def skill_area(t, u):
    a = eased(t, [(0.0, 0.0), (0.12, 1.0), (0.46, 1.0), (0.66, 0.0)])
    slam = bump(t, 0.2, 0.3, 0.42, 0.62)
    raise_ = bump(t, 0.02, 0.18, 0.18, 0.3)
    w = cast_rifle(a, 0.06)
    ctl = upper_from_idle(t, w, spine=(22 * slam - 5 * raise_, 8 * a, 0), head=(12 * slam - 4 * raise_, 0))
    AREA.apply(ctl, t, w)
    return ctl


BUFF = LeftTrack([
    (0.00, "rifle", None),
    (0.18, lambda w: hand_c((0.00, 0.13, 1.02), (0.55, -0.1, 0.8), (0.0, 0.3, 1.0), (-0.9, -0.1, -0.4)), 0.15),
    (0.30, lambda w: hand_c((0.00, 0.12, 1.03), (0.55, -0.1, 0.8), (0.0, 0.3, 1.0), (-0.9, -0.1, -0.4)), 0.25),
    (0.44, lambda w: hand_c((-0.30, 0.16, 1.24), (-0.4, 0.2, 0.9), (0.6, 0.6, 0.2), (-0.9, -0.3, -0.2)), 0.0),
    (0.66, "rifle", None),
    (0.70, "rifle", None),
])


@clip("Skill_SelfBuff", 0.7, layer="UpperBody", notes="palm to chest, then an outward sweep (shield / buff / heal)")
def skill_buff(t, u):
    a = eased(t, [(0.0, 0.0), (0.12, 1.0), (0.48, 1.0), (0.66, 0.0)])
    power = bump(t, 0.16, 0.28, 0.36, 0.55)
    w = cast_rifle(a, 0.03)
    ctl = upper_from_idle(t, w, spine=(-6 * power, 6 * a, 0), head=(-8 * power, 0))
    BUFF.apply(ctl, t, w)
    return ctl


ULT = LeftTrack([
    (0.00, "rifle", None),
    (0.30, lambda w: hand_c((-0.04, 0.16, 0.98), (0.8, 0.3, 0.3), (0.0, 0.0, 1.0), (-0.9, 0.0, -0.4)), 0.95),
    (0.52, lambda w: hand_c((-0.19, 0.13, 1.38), (-0.1, 0.1, 1.0), (1.0, 0.0, 0.0), (-0.9, 0.2, -0.1)), 0.0),
    (1.10, lambda w: hand_c((-0.18, 0.14, 1.37), (-0.1, 0.1, 1.0), (1.0, 0.0, 0.0), (-0.9, 0.2, -0.1)), 0.05),
    (1.50, "rifle", None),
    (1.60, "rifle", None),
])


@clip("Skill_Ultimate", 1.6, layer="UpperBody", notes="legendary cards: coil, rise, burst; tail fan is procedural")
def skill_ultimate(t, u):
    coil = bump(t, 0.0, 0.3, 0.3, 0.5)
    burst = eased(t, [(0.3, 0.0), (0.52, 1.0), (1.1, 1.0), (1.5, 0.0)])
    a = eased(t, [(0.0, 0.0), (0.25, 1.0), (1.3, 1.0), (1.55, 0.0)])
    shake = 0.003 * math.sin(2 * math.pi * 14 * t) * burst
    w = weapon_at((0.03 * a - 0.03 * coil, -0.06 * coil - 0.02 * burst, 0.05 * burst + shake),
                  pitch=lerp(-15, 38, burst) * a, yaw=-6 * a, roll=8 * a)
    ctl = upper_from_idle(t, w, spine=(18 * coil - 12 * burst, -6 * coil + 6 * burst, 0), head=(14 * coil - 16 * burst, 0))
    ULT.apply(ctl, t, w)
    return ctl


# ====================================================================== 31-37 hits, down, death

@clip("Hit_Light", 0.3, layer="Additive", notes="additive flinch; the aim barely moves")
def hit_light(t, u):
    k = smooth(t / 0.04) if t < 0.04 else math.exp(-(t - 0.04) * 11)
    ctl = idle_controls(0.0)
    ctl.spine = [s @ rot(pitch=-4 * k * w * 3, yaw=3 * k * w * 3) for s, w in zip(ctl.spine, (0.2, 0.35, 0.45))]
    ctl.clavicle["Left"] = rot(roll=4 * k)
    ctl.clavicle["Right"] = rot(roll=-4 * k)
    hold_rifle(ctl, ST, weapon_at((0, -0.01 * k, 0.008 * k), pitch=1.5 * k))
    ctl.head_aim = rot(pitch=ST.head_pitch - 6 * k, yaw=5 * k)
    return ctl


HEAVY = LeftTrack([
    (0.00, "rifle", None),
    (0.06, "rifle", None),
    (0.16, lambda w: hand_c((-0.34, 0.06, 1.02), (-0.6, 0.3, 0.6), (0.0, 1.0, 0.2), (-0.8, -0.4, -0.3)), 0.2),
    (0.30, lambda w: hand_c((-0.30, 0.10, 1.00), (-0.5, 0.4, 0.6), (0.0, 1.0, 0.2), (-0.8, -0.4, -0.3)), 0.3),
    (0.52, "rifle", None),
    (0.60, "rifle", None),
])


@clip("Hit_Heavy", 0.6, layer="UpperBody", notes="big stagger of the upper body; legs keep moving")
def hit_heavy(t, u):
    k = smooth(t / 0.08) if t < 0.08 else math.exp(-((t - 0.08) / 0.22) ** 2)
    w = weapon_at((0.0, -0.04 * k, 0.05 * k), pitch=20 * k, yaw=8 * k, roll=-8 * k)
    ctl = upper_from_idle(t, w, spine=(-14 * k, 10 * k, 6 * k), head=(-16 * k, 8 * k))
    HEAVY.apply(ctl, t, w)
    return ctl


@clip("Knockback", 1.1, layer="Reaction", notes="blown back, airborne, skid landing, recover (motion from ForceReceiver)")
def knockback(t, u):
    ctl = idle_controls(0.0)
    air = eased(t, [(0.0, 0.0), (0.08, 1.0), (0.40, 1.0), (0.48, 0.0)])
    skid = eased(t, [(0.40, 0.0), (0.50, 1.0), (0.66, 1.0), (1.05, 0.0)])
    arch = eased(t, [(0.0, 0.0), (0.06, 1.0), (0.4, 0.7), (0.5, 0.0)])
    set_body(ctl, drop=-0.06 * air + 0.20 * skid, pitch=-18 * arch + 18 * skid, roll=4 * air)
    set_spine(ctl, pitch=-14 * arch + 14 * skid, yaw=6 * arch)
    fl = (lerp(-0.13, -0.12, air), lerp(0.07, 0.28, air) + 0.06 * skid, 0.18 * air)
    fr = (lerp(0.13, 0.14, air), lerp(-0.09, 0.18, air) - 0.14 * skid, 0.12 * air)
    ctl.feet["Left"] = Foot(c(fl[0], fl[1], 0.0), lift=fl[2], pitch=10 * air, yaw=-8)
    ctl.feet["Right"] = Foot(c(fr[0], fr[1], 0.0), lift=fr[2], pitch=6 * air - 25 * skid, yaw=-28)
    w = weapon_at((0.02 * air, -0.06 * air, 0.06 * air - 0.05 * skid), pitch=30 * arch - 12 * skid, roll=-10 * air)
    hold_rifle(ctl, ST, w)
    LeftTrack([
        (0.00, "rifle", None),
        (0.10, lambda w_: hand_c((-0.36, 0.05, 1.08), (-0.7, 0.2, 0.5), (0.0, 1.0, 0.1), (-0.9, -0.3, -0.2)), 0.2),
        (0.42, lambda w_: hand_c((-0.30, 0.20, 0.70), (-0.2, 0.6, -0.8), (0.9, 0.3, 0.0), (-0.9, -0.3, 0.2)), 0.6),
        (0.62, lambda w_: hand_c((-0.30, 0.22, 0.72), (-0.2, 0.6, -0.8), (0.9, 0.3, 0.0), (-0.9, -0.3, 0.2)), 0.6),
        (0.95, "rifle", None),
        (1.10, "rifle", None),
    ]).apply(ctl, t, w)
    ctl.head_aim = rot(pitch=ST.head_pitch - 14 * arch + 8 * skid)
    ground_feet(ctl, RIG[0], (1 - air, 1 - air))
    return ctl


SIT_DROP = 0.56


def downed_pose(ctl, t, breath=0.0):
    """Sitting on the floor, knees up, left hand propping, rifle across the lap."""
    b = breath
    set_body(ctl, drop=SIT_DROP - 0.005 * b, pitch=-24 + 1.5 * b, yaw=8, roll=-6, shift=(0.0, -0.05))
    set_spine(ctl, pitch=34 - 2.5 * b, yaw=-4, roll=4)
    ctl.feet["Left"] = Foot(c(-0.17, 0.40, 0.0), pitch=12, yaw=12, knee=c(-0.5, 0.4, 1.0))
    ctl.feet["Right"] = Foot(c(0.15, 0.30, 0.0), pitch=4, yaw=-30, knee=c(0.5, 0.4, 1.0))
    w = ST.weapon_matrix(grip=Vector((0.10, 0.22, 0.30 + 0.004 * b)), r3=rot(pitch=8, yaw=62, roll=-8))
    ctl.weapon = w
    from fox_stance import hold_rifle as _hold
    _hold(ctl, ST, w)
    ctl.hands["Left"] = hand_c((-0.27, -0.10, 0.03), (-0.2, -0.6, -0.75), (0.0, 0.8, -0.6), (-0.7, -0.6, 0.2))
    ctl.left_ik = 0.0
    ctl.curl["Left"] = 0.25
    ctl.head_aim = rot(pitch=24 - 3 * b, yaw=6)
    ctl.head_follow = 0.3


@clip("Downed_Start", 1.0, layer="Reaction", notes="knees buckle, drop to sitting, left hand catches the fall")
def downed_start(t, u):
    stand = idle_controls(0.0)
    sit = Controls()
    downed_pose(sit, 0.0)
    a = eased(t, [(0.0, 0.0), (0.25, 0.25), (0.62, 1.0), (1.0, 1.0)])
    settle = 0.02 * math.exp(-((t - 0.66) / 0.06) ** 2)
    ctl = mix_controls(stand, sit, a)
    ctl.hips_offset = ctl.hips_offset + c(0, 0, -settle)
    return ctl


@clip("Downed_Loop", 2.4, loop=True, layer="Reaction", notes="waiting for a revive: heavy breathing, head low")
def downed_loop(t, u):
    ctl = Controls()
    downed_pose(ctl, t, math.sin(2 * math.pi * 2 * u))
    return ctl


@clip("Revived", 1.2, layer="Reaction", notes="push up from the floor, rise through a crouch to the stance")
def revived(t, u):
    sit = Controls()
    downed_pose(sit, 0.0)
    crouch = idle_controls(0.0)
    set_body(crouch, drop=0.22, pitch=22)
    set_spine(crouch, pitch=18)
    crouch.feet["Left"] = Foot(c(-0.14, 0.18, 0.0), pitch=0, yaw=-8)
    crouch.feet["Right"] = Foot(c(0.14, -0.12, 0.0), pitch=-30, yaw=-28)
    crouch.hands["Left"] = hand_c((-0.20, 0.28, 0.28), (0.0, 0.5, -0.85), (0.9, 0.2, 0.0), (-0.8, -0.2, 0.3))
    crouch.left_ik = 0.0
    stand = idle_controls(t)
    a = eased(t, [(0.0, 0.0), (0.45, 1.0)])
    b = eased(t, [(0.45, 0.0), (1.1, 1.0)])
    ctl = mix_controls(mix_controls(sit, crouch, a), stand, b)
    ground_feet(ctl, RIG[0])
    return ctl


@clip("Death", 1.6, layer="Reaction", notes="from the downed pose: the arm gives way, falls back, rifle slips")
def death(t, u):
    sit = Controls()
    downed_pose(sit, 0.0)
    lie = Controls()
    set_body(lie, drop=0.66, pitch=-72, yaw=14, roll=-10, shift=(0.0, -0.12))
    set_spine(lie, pitch=6, yaw=-6, roll=6)
    lie.feet["Left"] = Foot(c(-0.20, 0.55, 0.0), pitch=40, yaw=30, knee=c(-0.6, 0.3, 1.0))
    lie.feet["Right"] = Foot(c(0.18, 0.62, 0.0), pitch=45, yaw=-40, knee=c(0.6, 0.3, 1.0))
    w = ST.weapon_matrix(grip=Vector((0.36, 0.05, 0.035)), r3=rot(pitch=0, yaw=95, roll=88))
    lie.weapon = w
    hold_rifle(lie, ST, w)
    lie.hands["Left"] = hand_c((-0.34, -0.02, 0.06), (-0.6, -0.2, -0.1), (0.0, 0.3, 1.0), (-0.6, 0.2, 0.7))
    lie.left_ik = 0.0
    lie.curl["Right"] = 0.4
    lie.curl["Left"] = 0.35
    lie.head_aim = rot(pitch=-60, yaw=20, roll=-15)
    lie.head_follow = 0.2
    a = eased(t, [(0.0, 0.0), (0.35, 0.12), (0.95, 1.0)])
    bounce = 0.015 * math.exp(-((t - 1.0) / 0.07) ** 2)
    ctl = mix_controls(sit, lie, a)
    ctl.hips_offset = ctl.hips_offset + c(0, 0, bounce)
    return ctl


# ====================================================================== 38-42 interactions (upper body)

@clip("Pickup", 0.7, layer="UpperBody", notes="left hand snags an item at hip height and stows it (hook only)")
def pickup(t, u):
    bend = eased(t, [(0.0, 0.0), (0.24, 1.0), (0.36, 1.0), (0.6, 0.0)])
    w = weapon_at((0.02 * bend, -0.04 * bend, -0.06 * bend), pitch=-22 * bend, yaw=-8 * bend, roll=8 * bend)
    ctl = upper_from_idle(t, w, spine=(26 * bend, 10 * bend, 0), head=(14 * bend, 6 * bend))
    LeftTrack([
        (0.00, "rifle", None),
        (0.24, lambda w_: hand_c((-0.10, 0.32, 0.68), (0.1, 0.6, -0.8), (0.9, 0.3, 0.1), (-0.8, -0.3, 0.1)), 0.15),
        (0.32, lambda w_: hand_c((-0.10, 0.31, 0.68), (0.1, 0.6, -0.8), (0.9, 0.3, 0.1), (-0.8, -0.3, 0.1)), 0.95),
        (0.48, lambda w_: hand_c((-0.17, 0.05, 0.84), (0.3, 0.5, -0.8), (0.2, 0.9, 0.1), (-0.8, -0.4, -0.3)), 0.9),
        (0.66, "rifle", None),
        (0.70, "rifle", None),
    ]).apply(ctl, t, w)
    return ctl


@clip("Interact", 0.8, layer="UpperBody", notes="press and turn something in front (hook only)")
def interact(t, u):
    a = eased(t, [(0.0, 0.0), (0.16, 1.0), (0.56, 1.0), (0.76, 0.0)])
    turn = eased(t, [(0.3, 0.0), (0.46, 1.0)])
    w = cast_rifle(a, 0.05)
    ctl = upper_from_idle(t, w, spine=(4 * a, 10 * a, 0), head=(4 * a, -4 * a))
    def press(depth, twist):
        def g(w_):
            along = c(0.0, 0.3, 1.0)
            thumb = (rot(pitch=0) @ c(math.cos(math.radians(twist)), 0.0, -math.sin(math.radians(twist))))
            return Hand(c(-0.14, 0.34 + depth, 0.94), along, thumb, c(-0.5, -0.2, -0.85))
        return g
    LeftTrack([
        (0.00, "rifle", None),
        (0.18, press(0.0, 0.0), 0.1),
        (0.28, press(0.035, 0.0), 0.2),
        (0.46, press(0.035, 60.0), 0.7),
        (0.56, press(0.0, 60.0), 0.5),
        (0.76, "rifle", None),
        (0.80, "rifle", None),
    ]).apply(ctl, t, w)
    return ctl


@clip("Revive_Ally", 1.2, loop=True, layer="UpperBody", notes="reaching down to a downed ally, channelling (loop)")
def revive_ally(t, u):
    p = math.sin(2 * math.pi * u)
    w = weapon_at((0.03, -0.05, -0.08), pitch=-30, yaw=-10, roll=12)
    ctl = upper_from_idle(t, w, spine=(26 + 1.5 * p, 10, 0), head=(16, 4), period=1.2)
    h = hand_c((-0.10, 0.34 + 0.01 * p, 0.71 + 0.012 * p), (0.1, 0.9, -0.4), (1.0, 0.0, 0.0), (-0.7, -0.2, 0.2))
    ctl.hands["Left"] = h
    ctl.left_ik = 0.0
    ctl.cast = 1.0
    ctl.curl["Left"] = 0.12 + 0.06 * math.sin(2 * math.pi * 2 * u)
    ctl.thumb["Left"] = 0.1
    return ctl


@clip("Use_Item", 0.8, layer="UpperBody", notes="take a gauge crystal from the belt and crush it")
def use_item(t, u):
    a = eased(t, [(0.0, 0.0), (0.14, 1.0), (0.6, 1.0), (0.78, 0.0)])
    squeeze = bump(t, 0.38, 0.44, 0.52, 0.6)
    w = cast_rifle(a, 0.04)
    ctl = upper_from_idle(t, w, spine=(3 * a, 8 * a, 0), head=(12 * a, 8 * a))
    shake = c(0.0, 0.0, 0.004 * math.sin(2 * math.pi * 20 * t) * squeeze)
    LeftTrack([
        (0.00, "rifle", None),
        (0.18, lambda w_: hand_c((-0.17, 0.05, 0.84), (0.3, 0.5, -0.8), (0.2, 0.9, 0.1), (-0.8, -0.4, -0.3)), 0.8),
        (0.38, lambda w_: Hand(c(-0.03, 0.28, 1.04) + shake, c(0.7, 0.4, 0.4), c(0.0, 0.3, 1.0), c(-0.8, -0.3, -0.5)), 0.6),
        (0.50, lambda w_: Hand(c(-0.03, 0.28, 1.04) + shake, c(0.7, 0.4, 0.4), c(0.0, 0.3, 1.0), c(-0.8, -0.3, -0.5)), 1.0),
        (0.58, lambda w_: hand_c((-0.06, 0.30, 1.06), (0.6, 0.5, 0.5), (0.0, 0.3, 1.0), (-0.8, -0.3, -0.5)), 0.05),
        (0.78, "rifle", None),
        (0.80, "rifle", None),
    ]).apply(ctl, t, w)
    return ctl


@clip("Emote", 2.0, layer="UpperBody", notes="wave to a teammate, rifle rested on the shoulder")
def emote(t, u):
    a = eased(t, [(0.0, 0.0), (0.3, 1.0), (1.6, 1.0), (1.95, 0.0)])
    w = weapon_at(grip=(lerp(ST.grip.x, 0.15, a), lerp(ST.grip.y, 0.14, a), lerp(ST.grip.z, 0.98, a)),
                  pitch=lerp(0, 62, a), yaw=lerp(0, -12, a), roll=lerp(0, -10, a))
    ctl = upper_from_idle(t, w, spine=(-3 * a, 14 * a, -3 * a), head=(-4 * a, -6 * a))
    wave = math.sin(2 * math.pi * 2.5 * (t - 0.35)) * bump(t, 0.3, 0.45, 1.4, 1.6)
    def g(w_):
        along = c(-0.15 + 0.45 * wave, 0.1, 1.0)
        return Hand(c(-0.27, 0.12, 1.36), along, c(1.0, 0.0, 0.0), c(-0.9, 0.1, -0.3))
    LeftTrack([
        (0.00, "rifle", None),
        (0.32, g, 0.0),
        (1.55, g, 0.0),
        (1.92, "rifle", None),
        (2.00, "rifle", None),
    ]).apply(ctl, t, w)
    ctl.head_aim = rot(pitch=ST.head_pitch - 4 * a, yaw=-6 * a, roll=-8 * a)
    return ctl


# ====================================================================== mixing whole poses

def mix_controls(a, b, t):
    """Blend two full Controls (used for transitions inside reaction clips)."""
    if t <= 0:
        return a
    if t >= 1:
        return b
    from fox_motion import slerp_m
    out = Controls()

    def mix_r(ra, rb):
        qa, qb = ra.to_quaternion(), rb.to_quaternion()
        return qa.slerp(qb, t).to_matrix()

    out.hips_offset = a.hips_offset.lerp(b.hips_offset, t)
    out.hips_rot = mix_r(a.hips_rot, b.hips_rot)
    out.spine = [mix_r(x, y) for x, y in zip(a.spine, b.spine)]
    out.head_aim = mix_r(a.head_aim or Matrix.Identity(3), b.head_aim or Matrix.Identity(3))
    out.head_follow = lerp(a.head_follow, b.head_follow, t)
    out.neck = mix_r(a.neck, b.neck)
    out.head = mix_r(a.head, b.head)
    for side in ("Left", "Right"):
        out.clavicle[side] = mix_r(a.clavicle[side], b.clavicle[side])
        ha, hb = a.hands[side], b.hands[side]
        out.hands[side] = blend_hand(ha, hb, t) if ha and hb else (hb if t > 0.5 else ha)
        fa, fb = a.feet[side], b.feet[side]
        if fa and fb:
            knee_a = fa.knee if fa.knee is not None else FWD
            knee_b = fb.knee if fb.knee is not None else FWD
            out.feet[side] = Foot(fa.plant.lerp(fb.plant, t), lerp(fa.lift, fb.lift, t), lerp(fa.pitch, fb.pitch, t),
                                  lerp(fa.yaw, fb.yaw, t), lerp(fa.toe, fb.toe, t), knee_a.lerp(knee_b, t))
        out.curl[side] = lerp(a.curl[side], b.curl[side], t)
        out.thumb[side] = lerp(a.thumb[side], b.thumb[side], t)
    la, lb = a.weapon.decompose(), b.weapon.decompose()
    m = la[1].slerp(lb[1], t).to_matrix().to_4x4()
    m.translation = la[0].lerp(lb[0], t)
    out.weapon = m
    out.left_ik = lerp(a.left_ik, b.left_ik, t)
    out.cast = lerp(a.cast, b.cast, t)
    return out
