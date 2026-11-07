"""Workbench renders for checking poses and clips (contact sheets), with the Fire Rifle on the Weapon bone."""
import math
import os

import bpy
from mathutils import Matrix, Vector

from fox_common import RIFLE_GLB

PROP_FIX = Matrix.Rotation(math.pi, 4, "Z")  # rifle GLB forward (-Y) onto the Weapon bone's +Y


class Renderer:
    def __init__(self, arm, res=(640, 720)):
        self.arm = arm
        sc = bpy.context.scene
        self.sc = sc
        sc.render.engine = "BLENDER_WORKBENCH"
        sc.display.shading.light = "STUDIO"
        sc.display.shading.color_type = "TEXTURE"
        sc.display.shading.show_shadows = False
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.film_transparent = False
        world = bpy.data.worlds.new("w") if not sc.world else sc.world
        sc.world = world
        cd = bpy.data.cameras.new("checkcam")
        self.cam = bpy.data.objects.new("checkcam", cd)
        sc.collection.objects.link(self.cam)
        sc.camera = self.cam
        cd.type = "ORTHO"
        self.prop = self._import_rifle()
        self._ground()

    def _import_rifle(self):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=RIFLE_GLB)
        new = [o for o in bpy.data.objects if o not in before]
        rifle = next(o for o in new if o.type == "MESH")
        rifle.parent = None
        return rifle

    def _ground(self):
        bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
        g = bpy.context.active_object
        mat = bpy.data.materials.new("ground")
        mat.diffuse_color = (0.32, 0.33, 0.36, 1)
        g.data.materials.append(mat)
        # 0.25 m grid lines so foot sliding is visible
        for i in range(-12, 13):
            for axis in (0, 1):
                bpy.ops.mesh.primitive_plane_add(size=1, location=(i * 0.25 if axis == 0 else 0, i * 0.25 if axis == 1 else 0, 0.001))
                ln = bpy.context.active_object
                ln.scale = (0.004, 3, 1) if axis == 0 else (3, 0.004, 1)
                lm = bpy.data.materials.get("line") or bpy.data.materials.new("line")
                lm.diffuse_color = (0.22, 0.23, 0.26, 1)
                ln.data.materials.append(lm)
        self.sc.display.shading.color_type = "TEXTURE"

    def attach_rifle(self, offset=None):
        pb = self.arm.pose.bones["Weapon"]
        m = self.arm.matrix_world @ pb.matrix @ PROP_FIX
        if offset is not None:
            m = Matrix.Translation(offset) @ m
        self.prop.matrix_world = m

    def shot(self, path, eye, target, scale):
        self.cam.location = Vector(eye)
        self.cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
        self.cam.data.ortho_scale = scale
        self.sc.render.filepath = path
        bpy.ops.render.render(write_still=True)


VIEWS = {
    # name: (eye, target, ortho scale)  fox faces -Y, its right is -X
    "front": ((0.0, -4.0, 0.85), (0.0, 0.0, 0.85), 1.9),
    "right": ((-4.0, 0.0, 0.85), (0.0, 0.0, 0.85), 1.9),
    "left": ((4.0, 0.0, 0.85), (0.0, 0.0, 0.85), 1.9),
    "q34": ((-2.6, -3.0, 1.6), (0.0, 0.0, 0.8), 1.9),
    "top": ((0.0, -0.15, 4.0), (0.0, -0.15, 0.0), 1.6),
    "back34": ((2.4, 3.0, 1.5), (0.0, 0.0, 0.8), 1.9),
}


def contact_sheet(paths, out, cols):
    """Packs equal-size PNGs into one sheet (Blender image API, no PIL)."""
    imgs = [bpy.data.images.load(p) for p in paths]
    w, h = imgs[0].size
    rows = (len(imgs) + cols - 1) // cols
    sheet = bpy.data.images.new("sheet", w * cols, h * rows)
    px = [0.0] * (w * cols * h * rows * 4)
    for k, im in enumerate(imgs):
        src = im.pixels[:]
        cx, cy = k % cols, rows - 1 - k // cols
        for y in range(h):
            row0 = ((cy * h + y) * w * cols + cx * w) * 4
            px[row0:row0 + w * 4] = src[y * w * 4:(y + 1) * w * 4]
    sheet.pixels[:] = px
    sheet.filepath_raw = out
    sheet.file_format = "PNG"
    sheet.save()
    for im in imgs:
        bpy.data.images.remove(im)
    for p in paths:
        try:
            os.remove(p)
        except OSError:
            pass

CLOSE_VIEWS = {
    "c_right": ((-3.0, -0.15, 1.0), (0.0, -0.15, 1.0), 0.75),
    "c_top": ((0.0, -0.2, 3.0), (0.0, -0.2, 0.0), 0.8),
    "c_front": ((0.0, -3.0, 1.0), (0.0, 0.0, 1.0), 0.75),
    "c_low": ((-1.2, -2.0, 0.55), (0.0, -0.2, 0.95), 0.75),
}
