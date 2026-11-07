"""Renders the base stance from several views and prints IK reach errors.

blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P check_stance.py -- <out_dir> [key=value ...]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from fox_common import script_args  # noqa: E402
from fox_motion import Rig, new_action, reach_report  # noqa: E402
from fox_render import CLOSE_VIEWS, VIEWS, Renderer, contact_sheet  # noqa: E402
from fox_stance import Stance, base_controls  # noqa: E402


def main():
    args = script_args()
    out = args[0]
    overrides = {}
    for kv in [a for a in args[1:] if "=" in a]:
        k, v = kv.split("=", 1)
        overrides[k] = eval(v, {"Vector": Vector})
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rig = Rig(arm)
    st = Stance(**overrides)
    ctl = base_controls(st)
    D = rig.solve(ctl)
    print("REACH", {k: round(v, 4) for k, v in reach_report(rig, D, ctl).items()})
    new_action(arm, "check")
    rig.key(D, 1)
    bpy.context.scene.frame_set(1)
    r = Renderer(arm)
    r.attach_rifle()
    paths = []
    views = VIEWS if "--close" not in sys.argv else CLOSE_VIEWS
    for name in views:
        eye, target, scale = views[name]
        p = os.path.join(out, f"_v_{name}.png")
        r.shot(p, eye, target, scale)
        paths.append(p)
    contact_sheet(paths, os.path.join(out, "stance_close.png" if "--close" in sys.argv else "stance.png"), 3 if len(paths) > 4 else 2)
    print("STANCE_DONE")


main()
