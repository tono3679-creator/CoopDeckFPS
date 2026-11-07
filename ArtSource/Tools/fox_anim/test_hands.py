"""Debug render of the hand-modelled first-person hands in their poses (canonical frame).

blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P test_hands.py -- <out_dir>
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from fox_common import script_args  # noqa: E402
from fox_fp_hands import build_hand, sample_uv_points  # noqa: E402

out = script_args()[0]
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
uvp = sample_uv_points(body, arm)
mat = body.material_slots[0].material
body.hide_render = True
arm.hide_render = True
hands = []
for k, pose in enumerate(("open", "pistol", "handguard")):
    h = build_hand("Test", Matrix.Identity(3), Vector((0.0, k * 0.25, 2.0)), uvp, pose, keep_open=False)
    h.data.materials.append(mat)
    hands.append(h)
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x, sc.render.resolution_y = 1500, 520
cd = bpy.data.cameras.new("c")
cam = bpy.data.objects.new("c", cd)
sc.collection.objects.link(cam)
sc.camera = cam
cd.type = "ORTHO"
cd.ortho_scale = 0.78
views = {
    "back": (Vector((0.07, 0.25, 2.6)), Vector((0.07, 0.25, 2.0)), Vector((0, 1, 0))),
    "side": (Vector((0.07, -0.6, 2.0)), Vector((0.07, 0.25, 2.0)), Vector((0, 0, 1))),
    "q34": (Vector((0.45, -0.4, 2.35)), Vector((0.07, 0.25, 2.0)), Vector((0, 0, 1))),
}
for name, (eye, target, _up) in views.items():
    cam.location = eye
    cam.rotation_euler = (target - eye).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(out, f"hands_{name}.png")
    bpy.ops.render.render(write_still=True)
print("HANDS_DONE")
