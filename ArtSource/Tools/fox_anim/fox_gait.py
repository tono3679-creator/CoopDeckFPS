"""In-place locomotion cycles with planted feet.

The stance foot moves backward at exactly the ground speed (relative to the hips), rolling heel -> flat ->
ball, so when Unity plays the clip at the authored speed the planted foot does not slide. The pelvis is
lowered automatically wherever a planted leg would otherwise over-straighten.

Phase: the left foot touches down at phase 0, the right at 0.5.
"""
import math

from mathutils import Vector

from fox_motion import FWD, RIGHT, UP, Controls, Foot, c, clamp01, rot, smooth
from fox_stance import Stance, hold_rifle


class Gait:
    def __init__(self, **kw):
        self.direction = (0.0, 1.0)   # (right, forward) unit
        self.speed = 1.5              # m/s
        self.cycle = 0.56             # s, two steps
        self.duty = 0.6               # stance share of the cycle per foot
        self.lift = 0.07              # swing foot height
        self.lift_peak = 0.45         # where in the swing the foot is highest (0..1)
        self.heel_strike = 14.0       # toes up at touchdown (deg)
        self.toe_off = 26.0           # heel up at lift-off (deg)
        self.stance_center = 0.0      # stance travel centre along the motion, from the hip (m)
        self.width = 0.105            # half distance between the feet
        self.crouch = 0.05            # nominal pelvis drop
        self.bob = 0.018              # pelvis bob amplitude
        self.running = False          # bob phase: low at mid-stance when running
        self.lean = 6.0               # pelvis pitch forward
        self.counter_lean = 0.5       # share of the lean the spine takes back
        self.pelvis_yaw = 6.0         # cyclic pelvis twist
        self.pelvis_roll = 3.0        # swing-side hip drop
        self.sway = 0.012             # side shift toward the stance foot
        self.strafe_yaw = 30.0        # pelvis turned toward a sideways motion (right)
        self.strafe_yaw_left = 22.0   # ... and toward the left (the bladed chest already faces right)
        self.rifle_bob = 0.35         # share of the pelvis bob the rifle follows
        self.rifle_drop = 0.0         # rifle lowered (m) e.g. for sprint
        self.rifle_pitch = 0.0        # extra rifle pitch (muzzle up > 0)
        self.rifle_yaw = 0.0
        self.rifle_roll = 0.0
        self.grip_shift = Vector((0, 0, 0))  # extra (right, forward, up) for the rifle grip
        self.chest_yaw = None         # world yaw of the chest (None: the stance blade)
        self.chest_yaw_left = -22.0   # chest yaw while strafing left
        self.knee_out = 0.18
        self.head_pitch = None
        self.arm_pump = 0.0           # left hand swings off the rifle (sprint) 0..1
        for k, v in kw.items():
            setattr(self, k, v)


def lerp_f(a, b, t):
    return a + (b - a) * t


def hermite(x0, m0, x1, m1, s):
    s2, s3 = s * s, s * s * s
    return (2 * s3 - 3 * s2 + 1) * x0 + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * x1 + (s3 - s2) * m1


def softmin(a, b, k=0.015):
    """Smooth minimum, so the pelvis clamp has no kinks."""
    h = clamp01(0.5 + 0.5 * (b - a) / k)
    return b + (a - b) * h - k * h * (1 - h)


def foot_phase(g, p, forwardness, backwardness):
    """Plant offset along the motion (m), lift (m), pitch (deg) and planted weight at foot phase p in [0, 1)."""
    ds = g.duty * g.cycle * g.speed  # how far the planted foot travels under the body
    if p < g.duty:
        s = p / g.duty
        x = ds * (0.5 - s) + g.stance_center
        lift = 0.0
        # the pelvis clamp eases off at the end of the push so the toe peels off instead of the hips dipping
        planted = 1.0 - 0.65 * smooth((s - 0.75) / 0.25)
        fwd_pitch = (g.heel_strike * (1 - smooth(s / 0.18)) if s < 0.18 else 0.0) - g.toe_off * smooth((s - 0.55) / 0.45)
        back_pitch = -10.0 * (1 - smooth(s / 0.22)) + 6.0 * smooth((s - 0.7) / 0.3)
    else:
        s = (p - g.duty) / (1 - g.duty)
        planted = 0.35 * (1.0 - smooth(s / 0.08))  # ...and lets go entirely early in the swing
        m = -g.speed * (1 - g.duty) * g.cycle
        x = hermite(-ds * 0.5, m, ds * 0.5, m, s) + g.stance_center
        # rises quickly after lift-off, peaks at lift_peak, lowers softly to touchdown
        if s < g.lift_peak:
            lift = g.lift * math.sin(0.5 * math.pi * s / g.lift_peak) ** 1.2
        else:
            lift = g.lift * math.cos(0.5 * math.pi * (s - g.lift_peak) / (1 - g.lift_peak)) ** 1.6
        fwd_pitch = -g.toe_off * (1 - smooth(s / 0.35)) + g.heel_strike * smooth((s - 0.55) / 0.45) - 10 * math.sin(math.pi * s) * (1 if g.running else 0.4)
        back_pitch = 6.0 * (1 - smooth(s / 0.3)) - 10.0 * smooth((s - 0.6) / 0.4)
    pitch = fwd_pitch * forwardness + back_pitch * backwardness
    return x, lift, pitch, planted


def gait_controls(rig, st: Stance, g: Gait, t):
    ctl = Controls()
    phase = (t / g.cycle) % 1.0
    move = Vector(g.direction).normalized()
    side_amt = move.x
    forwardness = max(0.0, move.y)
    backwardness = max(0.0, -move.y)
    turn = -(g.strafe_yaw if side_amt > 0 else g.strafe_yaw_left) * side_amt  # pelvis faces the motion
    if move.y < 0:
        turn = -turn * 0.5  # backpedalling diagonally: hips turn less, the other way

    # pelvis cycle: L touchdown at 0, R at 0.5
    two = 2 * math.pi * phase
    if g.running:
        bob = -g.bob * math.cos(2 * (two - math.pi * g.duty))  # low at mid-stance
    else:
        bob = g.bob * math.cos(2 * (two - math.pi * g.duty))   # high at mid-stance
    cyc_yaw = -g.pelvis_yaw * math.cos(two) * (forwardness + backwardness * 0.6 + abs(side_amt) * 0.4)
    roll = g.pelvis_roll * math.sin(two - math.pi * g.duty * 0.5)
    sway = g.sway * math.sin(two - math.pi * g.duty * 0.5)
    hips_yaw = turn + cyc_yaw

    # feet
    turn_m = rot(yaw=turn * 0.85)
    feet = {}
    weights = {}
    for side, offset, r0 in (("Left", 0.0, -g.width), ("Right", 0.5, g.width)):
        p = (phase + offset) % 1.0
        x, lift, pitch, planted = foot_phase(g, p, forwardness, backwardness)
        neutral = turn_m @ c(r0, 0.0, 0.0)
        plant = neutral + c(move.x * x, move.y * x, 0.0)
        plant.z = 0.0
        knee = turn_m @ (FWD + RIGHT * (g.knee_out if side == "Right" else -g.knee_out))
        feet[side] = Foot(plant, lift=lift, pitch=pitch, yaw=turn * 0.85, knee=knee)
        weights[side] = planted
    ctl.feet = feet

    hips_rot = rot(pitch=g.lean, yaw=hips_yaw, roll=roll)
    nominal = -g.crouch + bob
    hips_offset = c(sway, 0.0, nominal)
    hips_offset.z = pelvis_height(rig, feet, weights, hips_rot, hips_offset)
    ctl.hips_offset = hips_offset
    ctl.hips_rot = hips_rot

    chest_yaw = (st.hips_yaw + st.spine_yaw) if g.chest_yaw is None else g.chest_yaw
    if side_amt < 0:  # strafing left: un-blade the chest so the spine twist stays human
        chest_yaw = lerp_f(chest_yaw, g.chest_yaw_left, -side_amt)
    spine_yaw = chest_yaw - hips_yaw
    spine_pitch = st.spine_pitch - g.lean * g.counter_lean
    ctl.spine = [rot(pitch=spine_pitch * w, yaw=spine_yaw * w, roll=-roll * 0.8 * w) for w in (0.3, 0.35, 0.35)]
    ctl.head_aim = rot(pitch=st.head_pitch if g.head_pitch is None else g.head_pitch)

    # rifle: stable in character space, riding a fraction of the pelvis motion
    grip = Vector(st.grip) + g.grip_shift + Vector((sway * 0.3, 0.0, bob * g.rifle_bob - g.rifle_drop))
    r3 = rot(pitch=-(st.rifle_pitch + g.rifle_pitch) + 1.2 * math.sin(2 * two) * (g.bob / 0.03) * 0.3,
             yaw=st.rifle_yaw + g.rifle_yaw + 0.8 * math.sin(two),
             roll=st.rifle_roll + g.rifle_roll + 1.0 * math.sin(two))
    weapon = st.weapon_matrix(grip=grip, r3=r3)
    hold_rifle(ctl, st, weapon)
    return ctl


def pelvis_height(rig, feet, weights, hips_rot, hips_offset):
    """Lowers the pelvis (hips_offset.z) where a planted leg would over-straighten; weights fade the clamp."""
    from fox_motion import about
    nominal = hips_offset.z
    z = nominal
    for side in ("Left", "Right"):
        f = feet.get(side)
        w = weights.get(side, 1.0)
        if f is None or w <= 0.0:
            continue
        ankle = rig_ankle(rig, side, f)
        hip = about(rig.head["Hips"], hips_rot) @ rig.head[side + "UpLeg"] + hips_offset
        lmax = 0.975 * (rig.length(side + "UpLeg", side + "Leg") + rig.length(side + "Leg", side + "Foot"))
        dxy = math.hypot(hip.x - ankle.x, hip.y - ankle.y)
        if dxy < lmax:
            limit = nominal + (ankle.z + math.sqrt(lmax * lmax - dxy * dxy) - hip.z)
            z = min(z, nominal + (softmin(nominal, limit, 0.02) - nominal) * w)
    return z


def rig_ankle(rig, side, f):
    """Ankle position the leg solver will aim for (same maths as Rig._solve_leg)."""
    from fox_motion import axis_angle
    fr = rig.foot_rest[side]
    yaw = rot(yaw=f.yaw)
    lateral = yaw @ (fr["ball"] - fr["heel"]).normalized().cross(UP)
    r_foot = axis_angle(lateral, -f.pitch) @ yaw
    plant = f.plant + UP * f.lift
    pivot_rest = fr["heel"] if f.pitch >= 0 else fr["ball"]
    pivot = plant + yaw @ (pivot_rest - fr["ref"])
    return pivot + r_foot @ (fr["ankle"] - pivot_rest)


# Locomotion sets: (walk, run, sprint). Speeds in m/s; the Unity blend tree uses the same numbers.
WALK = dict(speed=1.5, cycle=0.56, duty=0.6, lift=0.075, heel_strike=14, toe_off=24, crouch=0.05, bob=0.016,
            lean=5, pelvis_yaw=7, pelvis_roll=3, sway=0.014, strafe_yaw=45, strafe_yaw_left=40)
RUN = dict(speed=6.5, cycle=0.46, duty=0.21, lift=0.20, lift_peak=0.38, heel_strike=6, toe_off=34,
           stance_center=-0.05, crouch=0.06, bob=0.03, running=True, lean=12, counter_lean=0.6, pelvis_yaw=11,
           pelvis_roll=4, sway=0.008, strafe_yaw=65, strafe_yaw_left=55, rifle_bob=0.3, knee_out=0.12)
SPRINT = dict(speed=9.5, cycle=0.42, duty=0.2, lift=0.26, lift_peak=0.36, heel_strike=3, toe_off=40,
              stance_center=-0.08, crouch=0.07, bob=0.035, running=True, lean=20, counter_lean=0.45,
              pelvis_yaw=14, pelvis_roll=4, sway=0.006, rifle_bob=0.25, knee_out=0.1,
              rifle_drop=0.07, rifle_pitch=-22, rifle_yaw=18, rifle_roll=-10, grip_shift=Vector((0.02, -0.05, 0.0)))
