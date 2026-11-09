"""A character's game body rebuilt from Meshy's high-res mesh, under the character's own name (run inside Blender).

    blender -b --factory-startup -P rebuild_body.py -- --name LightningRabbit --model <prepared .glb> \
        --highres <highres .glb> --upscaled <atlas on the prepared body's UVs> --faces 160000 --size 4096 --margin 6 \
        --texture-out T_<name>_BaseColor.png --normal-out T_<name>_Normal.png --glb-out <name>_HQ.glb

Runs refine_character.py's `rebuild` unchanged (decimate the sculpt, even UVs, skin weights from the prepared body,
colour and normal map baked) and then names the body <name>_Body instead of the Fox Gunner name the shared tool
writes. Prints the counts used by the docs and tests.
"""
import json
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import refine_character as rc  # noqa: E402


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    name = argv[argv.index("--name") + 1]
    del argv[argv.index("--name"):argv.index("--name") + 2]
    sys.argv = [sys.argv[0], "--", "rebuild"] + argv  # refine_character.parse keeps its own defaults
    args = rc.parse()
    rc.rebuild(args)

    body = next(o for o in bpy.data.objects if o.type == "MESH")
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body.name = body.data.name = f"{name}_Body"
    me = body.data
    stats = {"triangles": sum(len(p.vertices) - 2 for p in me.polygons), "vertices": len(me.vertices),
             "unweighted": sum(1 for v in me.vertices if not v.groups),
             "max_influences": max(len(v.groups) for v in me.vertices), "bones": len(arm.data.bones)}
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.ops.export_scene.gltf(filepath=args.glb_out, export_format="GLB", use_selection=True,
                              export_animations=False, export_skins=True, export_yup=True)
    print("REBUILD " + json.dumps(stats))


main()
