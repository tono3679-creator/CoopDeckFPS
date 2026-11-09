"""Lightning Rabbit clips: the Fox Gunner clip set (fox_anim/fox_clips.py, read only) re-solved on the rabbit rig, with
the rabbit's own stance and timing.

What changes for the rabbit (the "quick and light" brief):
  - body scale: character-space distances scaled by the shoulder-height ratio (rabbit_motion.install_scale)
  - stance: the rabbit's arms are 18% shorter than the fox's, so the rifle sits closer to the chest
  - locomotion: same ground speeds as the game (walk 1.5, run 6.5, sprint 9.5 m/s) with quicker, springier steps
    (shorter cycles, higher swing, more bob)
  - Dash_*: the body drops lower and leans harder into the dash
  - Land: absorbs the impact in 70% of the fox's time
  - Skill_QuickCast: the fox's own (already a sharp 0.12 s strike); the short arm stops a few cm short of its palm
  - weapon: the Lightning SMG (rabbit_weapon): its stance hands, the reload's magazine path, the inspect touch and
    the aim-down-sights height replace the Fire Rifle's numbers in fox_clips
Ears, tail and the scarf tails are not in the clips: Unity moves them with springs (idle twitch, trailing while
running, lag when jumping, reacting to casts).
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fox_anim"))
import fox_clips as fc  # noqa: E402
import fox_gait  # noqa: E402
import fox_stance  # noqa: E402
from fox_clips import ClipSpec  # noqa: E402
from fox_gait import Gait, gait_controls  # noqa: E402
from fox_motion import bump, rot  # noqa: E402
from fox_stance import Stance  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from rabbit_motion import install_scale  # noqa: E402
from rabbit_weapon import HANDS, INSPECT_KEYS, RELOAD_KEYS  # noqa: E402

FPS = fc.FPS

# Stance with the Lightning SMG for the shorter arms (character space before the body scale; see rabbit_motion).
# The hands come from rabbit_weapon (right on the pistol grip, left on the vertical foregrip). The rabbit's big
# head, scarf and chest fur fill the space in front of the shoulders, so the SMG is not shouldered: it sits at chest
# height on the right with the right elbow out, and the stock tucks under the right arm. Picked by a grid search
# (rabbit_check.py sweep: both hands on, the stock and receiver clear of the chest, the scarf and the forearms).
# The muzzle turns 12 deg inward so the short left arm keeps some slack on the foregrip while the body moves, and
# the SMG sits at belly height so dashes and landings (the head drops forward) do not bring the cheek fur onto it.
STANCE = dict(grip=(0.18, 0.22, 0.88), rifle_yaw=12.0, rifle_pitch=-2.0, spine_yaw=-20.0, hips_yaw=-16.0)
RIGHT_ELBOW = (1.0, -0.1, -0.1)   # elbow hint (right, forward, up): out to the side
# Aim down sights: the rabbit cannot sight down the SMG: with the rear sight at the eye the stock goes through its
# cheek and head (big head and cheek fur, short neck). Its ADS is a raised ready instead: the SMG comes up beside
# the right cheek (sight line 4-5 cm below the eye, 17 cm right of the head centre), pointing straight ahead, the
# highest pose with no clipping and both hands on (search with the solver's clearance test). The short left arm
# needs the chest turned a little further right and the left shoulder forward (the fox turns its chest +6 deg).
ADS_GRIP = (0.03, 0.06, 0.16)
ADS_RIFLE_YAW = 0.0         # the SMG points straight ahead when aiming (the stance turns it 12 deg inward)
ADS_SPINE_YAW = -12.0
ADS_LEFT_CLAVICLE_YAW = -12.0

# Quicker, springier steps at the game's speeds (fox values in fox_gait.WALK/RUN/SPRINT)
GAIT_TWEAKS = {
    "Walk": dict(cycle=0.52, lift=0.085, bob=0.02),
    "Run": dict(cycle=0.42, lift=0.23, bob=0.036, lean=13),
    # The fox's sprint carry tips the muzzle 22 deg down; with the rabbit's stock under the right arm that levers the
    # stock end up into the upper arm (21 mm). 13 deg is the middle of the clear range (10-16 deg, rabbit_check scan).
    "Sprint": dict(cycle=0.38, lift=0.29, bob=0.04, lean=22, crouch=0.08, rifle_pitch=-13),
}
GAIT_EXTRA = {   # per-clip extras from fox_clips._gait_clip calls
    "Walk_Backward": dict(lean=2, heel_strike=4, toe_off=10),
    "Walk_Left": dict(heel_strike=4, toe_off=12, width=0.12),
    "Walk_Right": dict(heel_strike=4, toe_off=12, width=0.12),
    "Run_Backward": dict(lean=-2, counter_lean=0.0, heel_strike=0, toe_off=14, stance_center=0.05, lift=0.17),
    "Run_Left": dict(heel_strike=2, toe_off=22, lift=0.19),
    "Run_Right": dict(heel_strike=2, toe_off=22, lift=0.19),
}
GAIT_BASE = {"Walk": fox_gait.WALK, "Run": fox_gait.RUN, "Sprint": fox_gait.SPRINT}
# The short left arm: strafing left keeps the chest bladed (the fox un-blades it to -22 deg) and the SMG sits 2 cm
# nearer the left shoulder when strafing left or backpedalling (rabbit_motion.RabbitRig.solve pulls in the rest).
NEAR_LEFT = Vector((-0.02, -0.01, 0.0))
REACH_TWEAKS = {
    "Walk_Left": dict(chest_yaw_left=-36.0, grip_shift=NEAR_LEFT),
    "Run_Left": dict(chest_yaw_left=-36.0, grip_shift=NEAR_LEFT),
    "Walk_Backward": dict(grip_shift=NEAR_LEFT),
    "Run_Backward": dict(grip_shift=NEAR_LEFT),
}
LAND_TIME = 0.7
DASH_DROP = 0.06       # extra pelvis drop at the dash peak (m)
DASH_LEAN = 10.0       # extra forward lean (deg)


def _gait_spec(spec):
    kind = spec.name.split("_")[0]
    params = dict(GAIT_BASE[kind])
    params.update(GAIT_TWEAKS[kind])
    params.update(GAIT_EXTRA.get(spec.name, {}))
    params.update(REACH_TWEAKS.get(spec.name, {}))
    g = Gait(direction=spec.direction, **params)

    def fn(t, u, g=g):
        return gait_controls(fc.RIG[0], fc.ST, g, t)

    return ClipSpec(spec.name, g.cycle, fn, loop=True, speed=g.speed, direction=spec.direction, cycle=g.cycle,
                    notes=spec.notes)


def _dash_spec(spec):
    base = spec.fn

    def fn(t, u):
        ctl = base(t, u)
        a = bump(t, 0.0, 0.05, 0.17, 0.32)
        dr, df = spec.direction
        ctl.hips_offset = ctl.hips_offset + fc.c(0.0, 0.0, -DASH_DROP * a)
        ctl.hips_rot = ctl.hips_rot @ rot(pitch=DASH_LEAN * a * (1.0 if df >= 0 else 0.5))
        fc.ground_feet(ctl, fc.RIG[0], (1 - a * 0.7, 1 - a * 0.7))
        return ctl

    return ClipSpec(spec.name, spec.seconds, fn, spec.loop, spec.layer, direction=spec.direction,
                    notes=spec.notes + "; rabbit: lower and leaning harder")


def _land_spec(spec):
    base = spec.fn

    def fn(t, u):
        return base(t / LAND_TIME, u)

    return ClipSpec(spec.name, spec.seconds * LAND_TIME, fn, spec.loop, spec.layer,
                    notes=spec.notes + f"; rabbit: {LAND_TIME:.0%} of the fox's time")


# Where a fox clip swings its rifle toward its own face or chest, the same move puts the SMG's stock into the
# rabbit's head (big head, short neck). These clips keep their timing but scale the gun's offset from the stance
# (right, forward, up) and its turn; the solver's clearance push handles what is left. Tuned with rabbit_check.py.
GUN_SCALE = {
    "Rifle_Inspect": dict(grip=(0.5, -0.5, 0.0), turn=0.6),   # held out in front, turned less (fox: pulled to the face)
    "Slide": dict(grip=(1.0, -1.0, 1.5), turn=1.0),           # pushed forward and lower while the body drops
    "Dash_Forward": dict(grip=(1.0, -2.0, 3.0), turn=1.5),    # dashes thrust the gun forward and down (fox: pulled in)
    "Dash_Backward": dict(grip=(1.0, -2.0, 3.0), turn=1.5),
    "Dash_Left": dict(grip=(1.0, -2.0, 3.0), turn=1.5),
    "Dash_Right": dict(grip=(1.0, -2.0, 3.0), turn=1.5),
}


def _gun_scaled(spec, grip=(1.0, 1.0, 1.0), turn=1.0):
    """The clip with fox_clips.weapon_at's offset from the stance scaled per axis and its angles by turn."""
    base = spec.fn

    def fn(t, u):
        orig = fc.weapon_at

        def weapon_at(grip_delta=(0, 0, 0), pitch=0.0, yaw=0.0, roll=0.0, grip=None, k=grip):
            delta = tuple(g * s for g, s in zip(grip_delta, k))
            return orig(delta, pitch * turn, yaw * turn, roll * turn, grip=grip)

        fc.weapon_at = weapon_at
        try:
            return base(t, u)
        finally:
            fc.weapon_at = orig

    return ClipSpec(spec.name, spec.seconds, fn, spec.loop, spec.layer, speed=spec.speed, direction=spec.direction,
                    cycle=spec.cycle, notes=spec.notes + "; rabbit: the gun kept clear of the head")


def _ads_controls(t, a, period=3.2):
    """fox_clips.ads_controls with the rabbit's chest turn and left shoulder (Rifle_ADS_Enter/Idle/Exit call it)."""
    w = fc.weapon_at(tuple(x * a for x in ADS_GRIP), pitch=2 * a, yaw=(ADS_RIFLE_YAW - fc.ST.rifle_yaw) * a)
    ctl = fc.upper_from_idle(t, w, spine=(-3 * a, ADS_SPINE_YAW * a, 0), head=(9 * a, 2 * a), period=period)
    ctl.clavicle["Right"] = rot(roll=-4 * a)
    ctl.clavicle["Left"] = rot(yaw=ADS_LEFT_CLAVICLE_YAW * a)
    return ctl


# Downed / Revived / Death: the fox lays its rifle across its own lap and props itself on a long left arm. On the
# rabbit the SMG would end up in the thighs and the left palm 6 cm above the floor, so these poses get the rabbit's
# own SMG placement and prop (character space before the body scale, like fox_clips; picked by rabbit_check.py).
DOWNED_GRIP = (0.22, 0.18, 0.42)          # SMG held on the right, muzzle down (fox: across the lap (0.10, 0.22, 0.30))
DOWNED_ROT = (-15.0, 25.0, 0.0)           # pitch, yaw, roll (fox: 8, 62, -8)
DOWNED_PROP = (-0.19, 0.0, 0.07)          # left palm on the floor, nearer the hip (fox: (-0.27, -0.10, 0.03))
DEATH_GRIP = (0.36, 0.05, 0.035)          # SMG on its side on the floor by the right hand (fox: (0.36, 0.05, 0.035))
DEATH_ROT = (0.0, 95.0, 88.0)             # (fox: 0, 95, 88)
DEATH_RIGHT = (0.30, -0.02, 0.06)         # the rabbit lets go: right palm on the floor beside the hip
DEATH_ARC = (0.08, 0.06, 0.12)            # (right, forward, up) the SMG swings out by mid-fall: the grip cleared
                                          # the right thigh (8-11 mm for 3 frames); worst left 7 mm, tail fur at landing


def _downed_pose(ctl, t, breath=0.0):
    """fox_clips.downed_pose with the rabbit's SMG placement and left-hand prop (Downed_*, Revived, Death use it)."""
    b = breath
    fc.set_body(ctl, drop=fc.SIT_DROP - 0.005 * b, pitch=-24 + 1.5 * b, yaw=8, roll=-6, shift=(0.0, -0.05))
    fc.set_spine(ctl, pitch=34 - 2.5 * b, yaw=-4, roll=4)
    ctl.feet["Left"] = fc.Foot(fc.c(-0.17, 0.40, 0.0), pitch=12, yaw=12, knee=fc.c(-0.5, 0.4, 1.0))
    ctl.feet["Right"] = fc.Foot(fc.c(0.15, 0.30, 0.0), pitch=4, yaw=-30, knee=fc.c(0.5, 0.4, 1.0))
    g = Vector(DOWNED_GRIP) + Vector((0.0, 0.0, 0.004 * b))
    w = fc.ST.weapon_matrix(grip=g, r3=rot(pitch=DOWNED_ROT[0], yaw=DOWNED_ROT[1], roll=DOWNED_ROT[2]))
    fox_stance.hold_rifle(ctl, fc.ST, w)
    ctl.hands["Left"] = fc.hand_c(DOWNED_PROP, (-0.2, -0.6, -0.75), (0.0, 0.8, -0.6), (-0.7, -0.6, 0.2))
    ctl.left_ik = 0.0
    ctl.curl["Left"] = 0.25
    ctl.head_aim = rot(pitch=24 - 3 * b, yaw=6)
    ctl.head_follow = 0.3


def _death(t, u):
    """fox_clips.death (from the downed pose the arm gives way, falls back). The rabbit's arm cannot reach the fox's
    floor spot lying down, so the right hand lets go during the fall and the SMG lands on the floor by the hip."""
    sit = fc.Controls()
    _downed_pose(sit, 0.0)
    lie = fc.Controls()
    fc.set_body(lie, drop=0.66, pitch=-72, yaw=14, roll=-10, shift=(0.0, -0.12))
    fc.set_spine(lie, pitch=6, yaw=-6, roll=6)
    lie.feet["Left"] = fc.Foot(fc.c(-0.20, 0.55, 0.0), pitch=40, yaw=30, knee=fc.c(-0.6, 0.3, 1.0))
    lie.feet["Right"] = fc.Foot(fc.c(0.18, 0.62, 0.0), pitch=45, yaw=-40, knee=fc.c(0.6, 0.3, 1.0))
    w = fc.ST.weapon_matrix(grip=Vector(DEATH_GRIP), r3=rot(pitch=DEATH_ROT[0], yaw=DEATH_ROT[1], roll=DEATH_ROT[2]))
    fox_stance.hold_rifle(lie, fc.ST, w)
    lie.hands["Left"] = fc.hand_c((-0.34, -0.02, 0.06), (-0.6, -0.2, -0.1), (0.0, 0.3, 1.0), (-0.6, 0.2, 0.7))
    lie.hands["Right"] = fc.hand_c(DEATH_RIGHT, (0.6, -0.2, -0.1), (0.0, 0.3, 1.0), (0.6, 0.2, 0.7))
    lie.left_ik = 0.0
    lie.curl["Right"] = 0.4
    lie.curl["Left"] = 0.35
    lie.head_aim = rot(pitch=-60, yaw=20, roll=-15)
    lie.head_follow = 0.2
    a = fc.eased(t, [(0.0, 0.0), (0.35, 0.12), (0.95, 1.0)])
    bounce = 0.015 * math.exp(-((t - 1.0) / 0.07) ** 2)
    ctl = fc.mix_controls(sit, lie, a)
    if any(DEATH_ARC):
        # A straight blend from the lap to the floor cuts through the thighs: arc the SMG out on the way.
        ctl.weapon = Matrix.Translation(fc.c(*DEATH_ARC) * math.sin(math.pi * a)) @ ctl.weapon
    ctl.hips_offset = ctl.hips_offset + fc.c(0, 0, bounce)
    ctl.right_on_gun = 1.0 - a     # the hand slides off the SMG as the rabbit falls (rabbit_motion reach assist)
    return ctl


def setup(rig, scale):
    """Points fox_clips at the rabbit (rig, stance, body scale) and returns the rabbit's clip list."""
    install_scale(scale, (fc, fox_gait, fox_stance))
    fc.RIG[0] = rig
    fc.ST = Stance(**{**HANDS, **STANCE, "grip": Vector(STANCE["grip"]),
                      "right_elbow": fox_stance.c(*RIGHT_ELBOW)})
    fc.RELOAD = fc.LeftTrack(RELOAD_KEYS)
    fc.INSPECT = fc.LeftTrack(INSPECT_KEYS)
    fc.ADS_GRIP = ADS_GRIP
    fc.ads_controls = _ads_controls
    fc.downed_pose = _downed_pose
    fox_stance_st = fc.ST
    gait_names = {"Walk_Forward", "Walk_Backward", "Walk_Left", "Walk_Right", "Run_Forward", "Run_Backward",
                  "Run_Left", "Run_Right", "Sprint"}
    out = []
    for spec in fc.CLIPS:
        if spec.name in gait_names:
            out.append(_gait_spec(spec))
        elif spec.name.startswith("Dash_"):
            out.append(_dash_spec(spec))
        elif spec.name == "Land":
            out.append(_land_spec(spec))
        elif spec.name == "Death":
            out.append(ClipSpec(spec.name, spec.seconds, _death, spec.loop, spec.layer,
                                notes=spec.notes + "; rabbit: SMG on the floor by the right hand"))
        elif spec.name == "Locomotion_Center":
            walk = GAIT_TWEAKS["Walk"]["cycle"]
            out.append(ClipSpec(spec.name, walk, spec.fn, True, spec.layer, cycle=walk, notes=spec.notes))
        else:
            out.append(spec)
    out = [_gun_scaled(s, **GUN_SCALE[s.name]) if s.name in GUN_SCALE else s for s in out]
    assert fox_stance_st is fc.ST
    return out


def stride_report(clips):
    lines = []
    for s in clips:
        if s.speed > 0:
            lines.append(f"{s.name}: {s.speed:.1f} m/s, cycle {s.cycle:.3f} s, {2 / s.cycle:.2f} steps/s, "
                         f"stride {s.speed * s.cycle / 2:.2f} m")
    return lines



