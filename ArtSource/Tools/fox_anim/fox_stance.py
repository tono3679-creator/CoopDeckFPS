"""The shared rifle stance: where the rifle sits, how both hands hold it, the bladed upper body.

Every clip starts from Stance and changes a few numbers, so all motions share one base pose
(Combat_Idle and Rifle_Idle are this pose with breathing).

Rifle space rs(x, y, z): metres to the rifle's right, toward the muzzle, and up, from the pistol grip
origin of PF_Weapon_FireRifle (the same origin Unity uses).
"""
import math

from mathutils import Matrix, Vector

from fox_motion import FWD, RIGHT, UP, Controls, Foot, Hand, c, rot


class Stance:
    """Tunable numbers of the base pose. Distances in metres, angles in degrees."""

    def __init__(self, **kw):
        # rifle grip origin, character space, and orientation
        self.grip = Vector((0.10, 0.20, 0.90))        # (right, forward, up)
        self.rifle_pitch = -2.0                        # muzzle up > 0
        self.rifle_yaw = 0.0                           # muzzle to the fox's left > 0
        self.rifle_roll = 0.0                          # cant to the right > 0
        # hands on the rifle (rifle space)
        self.right_palm = Vector((0.032, -0.03, 0.02))
        self.right_along = Vector((-0.22, 0.84, -0.5))
        self.right_thumb = Vector((-0.3, 0.45, 0.85))
        self.right_elbow = c(0.65, -0.35, -0.65)
        self.left_palm = Vector((-0.045, 0.17, 0.035))
        self.left_along = Vector((0.5, 0.8, 0.2))
        self.left_thumb = Vector((-0.2, 0.3, 0.95))
        self.left_elbow = c(-0.55, -0.1, -0.8)
        # body
        self.crouch = 0.035          # hips lowered
        self.hips_yaw = -14.0        # bladed: hips turned to the right
        self.hips_pitch = 6.0
        self.hips_shift = Vector((0.0, -0.01, 0.0))   # (right, forward, up) extra
        self.spine_yaw = -16.0       # extra chest turn (split over three spine bones)
        self.spine_pitch = 7.0
        self.spine_roll = 0.0
        self.head_pitch = 4.0        # looking slightly down the sights
        self.head_yaw = 0.0          # world: face forward
        # feet: plant points (right, forward) and yaw (left > 0)
        self.left_foot = (-0.13, 0.07, -8.0)
        self.right_foot = (0.13, -0.09, -28.0)
        self.curl_right = 0.9
        self.curl_left = 0.8
        for k, v in kw.items():
            setattr(self, k, v)

    def rifle_rotation(self):
        return rot(pitch=-self.rifle_pitch, yaw=self.rifle_yaw, roll=self.rifle_roll)

    def weapon_matrix(self, grip=None, r3=None):
        """World matrix of the Weapon bone: columns (right, muzzle, up) in world, origin at the grip."""
        g = c(*(grip if grip is not None else self.grip))
        r3 = r3 if r3 is not None else self.rifle_rotation()
        cols = Matrix((r3 @ RIGHT, r3 @ FWD, r3 @ UP)).transposed()
        m = cols.to_4x4()
        m.translation = g
        return m


def rs(weapon, v):
    """Rifle-space point -> world."""
    return weapon @ Vector(v)


def rs_dir(weapon, v):
    return weapon.to_3x3() @ Vector(v)


def hold_rifle(ctl, st, weapon, left=1.0, left_free=None):
    """Both hands on the rifle. left < 1 blends the left hand toward left_free (a Hand) for casts/reloads."""
    ctl.weapon = weapon
    ctl.hands["Right"] = Hand(rs(weapon, st.right_palm), rs_dir(weapon, st.right_along),
                              rs_dir(weapon, st.right_thumb), st.right_elbow)
    on = Hand(rs(weapon, st.left_palm), rs_dir(weapon, st.left_along), rs_dir(weapon, st.left_thumb), st.left_elbow)
    if left_free is None or left >= 0.999:
        ctl.hands["Left"] = on
    else:
        t = 1.0 - left
        ctl.hands["Left"] = Hand(on.palm.lerp(left_free.palm, t), on.along.lerp(left_free.along, t),
                                 on.thumb.lerp(left_free.thumb, t), on.elbow.lerp(left_free.elbow, t))
    ctl.curl["Right"] = st.curl_right
    ctl.curl["Left"] = st.curl_left


def base_controls(st, feet=True):
    """The idle stance as Controls (no breathing)."""
    ctl = Controls()
    ctl.hips_offset = c(st.hips_shift.x, st.hips_shift.y, st.hips_shift.z - st.crouch)
    ctl.hips_rot = rot(pitch=st.hips_pitch, yaw=st.hips_yaw)
    ctl.spine = [rot(pitch=st.spine_pitch * w, yaw=st.spine_yaw * w, roll=st.spine_roll * w) for w in (0.3, 0.35, 0.35)]
    ctl.head_aim = rot(pitch=st.head_pitch, yaw=st.head_yaw)
    hold_rifle(ctl, st, st.weapon_matrix())
    if feet:
        for side, (r, f, yaw) in (("Left", st.left_foot), ("Right", st.right_foot)):
            ctl.feet[side] = Foot(c(r, f, 0.0), yaw=yaw)
    return ctl
