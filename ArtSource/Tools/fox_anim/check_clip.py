"""Filmstrip renders of clips for review.

blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P check_clip.py -- <out_dir> <ClipName>[,<ClipName>...] [frames=8] [view=right]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402

import fox_clips  # noqa: E402
from fox_common import script_args  # noqa: E402
from fox_motion import Rig, new_action, reach_report  # noqa: E402
from fox_render import VIEWS, Renderer, contact_sheet  # noqa: E402


def main():
    args = script_args()
    out = args[0]
    names = args[1].split(",")
    opts = dict(a.split("=", 1) for a in args[2:] if "=" in a)
    count = int(opts.get("frames", 8))
    views = opts.get("view", "right").split("+")
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rig = Rig(arm)
    fox_clips.RIG[0] = rig
    r = Renderer(arm, res=(420, 520))
    specs = {s.name: s for s in fox_clips.CLIPS}
    for name in names:
        spec = specs[name]
        new_action(arm, "check_" + name)
        paths = []
        worst = {}
        for view in views:
            for k in range(count):
                u = k / count if spec.loop else k / max(1, count - 1)
                t = u * spec.seconds
                ctl = spec.fn(t, u)
                D = rig.solve(ctl)
                for key, v in reach_report(rig, D, ctl).items():
                    worst[key] = max(worst.get(key, 0.0), v)
                rig.key(D, 1)
                bpy.context.scene.frame_set(1)
                r.attach_rifle()
                eye, target, scale = VIEWS[view]
                p = os.path.join(out, f"_f_{name}_{view}_{k}.png")
                r.shot(p, eye, target, scale)
                paths.append(p)
        contact_sheet(paths, os.path.join(out, f"clip_{name}.png"), count)
        print("CLIP", name, "reach", {k: round(v, 3) for k, v in worst.items()})
    print("CHECK_DONE")


main()
