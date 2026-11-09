"""The Lightning SMG as the rabbit holds it: hand placements in rifle space and the preview model (Blender Python).

Rifle space rs(right, muzzle, up), metres from the pistol-grip origin of LightningSMG (the same origin Unity uses;
fox_anim/fox_stance.py). Measured on ArtSource/LightningSMG/Export/LightningSMG_HQ.glb (1 cm slices):
  pistol grip   17 deg slant (bottom back), 3.4 cm wide, 6 cm front to back, from 4 cm below the origin to 5 cm above
  foregrip      vertical post under the barrel, centre 23.3 cm ahead (LeftHandGrip socket), 14 deg slant like the
                pistol grip, 3 cm wide, 4.2 cm front to back, from 3.4 cm below the origin to 4.5 cm above
  magazine      in front of the trigger guard: centre 14-16 cm ahead, from the well (1 cm below the origin) to 8.2 cm
                below it, 3 cm wide, leaning forward at the bottom
  sights        rear sight fin 6-8 cm ahead and front sight 22-25 cm ahead, sight line about 15 cm above the origin
  stock         butt 11-15 cm behind the origin; muzzle 37 cm ahead, 9.7 cm up (Muzzle socket)

The Fire Rifle numbers in fox_anim are replaced in fox_clips by rabbit_clips.setup: the stance hands (Stance), the
reload's left-hand path (RELOAD), the inspect touch (INSPECT) and the aim-down-sights offset (ADS_GRIP).
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fox_anim"))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from fox_render import Renderer  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
SMG_GLB = os.path.join(REPO, "ArtSource", "LightningSMG", "Export", "LightningSMG_HQ.glb")

FOREGRIP = Vector((0.0, 0.233, 0.014))    # LeftHandGrip socket
MUZZLE = Vector((0.0, 0.37, 0.097))       # Muzzle socket
SIGHT_UP = 0.15                           # sight line above the origin

# Both hands hold a pistol-style grip: palm on the outer side, fingers round the front, thumb up the grip. The
# rabbit's gloved palms are thick, so the palm points sit 2-2.7 cm off the grips' sides (rabbit_check.py sweep:
# the grips sink no more than about 1 cm into the hands, fingers wrap without passing through).
HANDS = dict(
    right_palm=Vector((0.035, -0.03, 0.01)),
    right_along=Vector((-0.22, 0.84, -0.5)),
    right_thumb=Vector((-0.3, 0.45, 0.85)),
    left_palm=Vector((-0.042, 0.195, 0.005)),
    left_along=Vector((0.25, 0.84, -0.45)),
    left_thumb=Vector((0.3, 0.4, 0.85)),
    curl_right=0.8,
    curl_left=0.8,
)


def _hand(palm, along, thumb, elbow):
    return lambda w: _hand_rs(w, palm, along, thumb, elbow)


def _hand_rs(w, palm, along, thumb, elbow):
    import fox_clips as fc
    return fc.hand_rs(w, palm, along, thumb, elbow)


def _hand_c(palm, along, thumb, elbow):
    import fox_clips as fc
    return lambda w: fc.hand_c(palm, along, thumb, elbow)


# Rifle_Reload (1.7 s, the Fire Rifle's timing): off the foregrip, grab the magazine from the left, pull it down,
# drop it at the belt, bring a new one up, seat it, slap it, rack the charging handle on the left of the receiver.
RELOAD_KEYS = [
    (0.00, "rifle", None),
    (0.16, _hand((-0.065, 0.12, -0.07), (0.45, 0.85, -0.2), (-0.2, 0.2, 0.95), (-0.6, 0.0, -0.8)), 0.4),
    (0.32, _hand((-0.032, 0.145, -0.055), (0.55, 0.8, 0.1), (-0.3, 0.2, 0.9), (-0.6, 0.0, -0.8)), 0.85),
    (0.50, _hand((-0.07, 0.14, -0.22), (0.4, 0.85, 0.2), (-0.3, 0.2, 0.9), (-0.6, -0.2, -0.75)), 0.9),
    (0.72, _hand_c((-0.15, 0.08, 0.86), (0.3, 0.4, -0.85), (0.2, 0.9, 0.1), (-0.8, -0.4, -0.3)), 0.9),
    (0.86, _hand_c((-0.15, 0.09, 0.88), (0.3, 0.5, -0.8), (0.2, 0.9, 0.1), (-0.8, -0.4, -0.3)), 0.95),
    (1.08, _hand((-0.05, 0.15, -0.17), (0.5, 0.8, 0.25), (-0.3, 0.2, 0.9), (-0.6, -0.1, -0.8)), 0.9),
    (1.24, _hand((-0.032, 0.145, -0.05), (0.55, 0.8, 0.1), (-0.3, 0.2, 0.9), (-0.6, 0.0, -0.8)), 0.9),
    (1.32, _hand((-0.032, 0.145, -0.068), (0.55, 0.8, 0.1), (-0.3, 0.2, 0.9), (-0.6, 0.0, -0.8)), 0.5),
    (1.44, _hand((-0.058, 0.065, 0.085), (0.3, 0.9, -0.3), (0.2, 0.3, 0.9), (-0.6, 0.0, -0.8)), 0.1),
    (1.70, "rifle", None),
]

# Rifle_Inspect: the left hand touches the top of the receiver behind the rear sight (lower than the Fire Rifle)
INSPECT_KEYS = [
    (0.00, "rifle", None),
    (0.62, "rifle", None),
    (0.74, _hand((-0.04, 0.02, 0.135), (0.2, 0.8, -0.55), (0.8, 0.2, 0.3), (-0.6, -0.1, -0.8)), 0.55),
    (0.84, _hand((-0.032, 0.02, 0.12), (0.2, 0.8, -0.55), (0.8, 0.2, 0.3), (-0.6, -0.1, -0.8)), 0.6),
    (0.96, "rifle", None),
    (2.40, "rifle", None),
]


def smg_surface():
    """The SMG's closed surface in rifle space as (vertices, triangles), for the solver's clearance test. The glTF
    importer splits vertices at UV seams; they are merged again so the surface is closed."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=SMG_GLB)
    new = [o for o in bpy.data.objects if o not in before]
    index, verts, faces = {}, [], []
    for o in new:
        if o.type != "MESH":
            continue
        m = o.matrix_world
        remap = []
        for v in o.data.vertices:
            p = m @ v.co
            key = tuple(round(c * 1e5) for c in p)
            if key not in index:
                index[key] = len(verts)
                verts.append(Vector((-p.x, -p.y, p.z)))
            remap.append(index[key])
        for poly in o.data.polygons:
            ids = [remap[i] for i in poly.vertices]
            # mirroring x and y keeps the handedness, so the winding (outward normals) is unchanged
            faces.extend((ids[0], ids[k], ids[k + 1]) for k in range(1, len(ids) - 1))
    for o in new:
        data = o.data if o.type == "MESH" else None
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            bpy.data.meshes.remove(data)
    return verts, faces


class SMGRenderer(Renderer):
    """fox_render.Renderer with the Lightning SMG on the Weapon bone instead of the Fire Rifle."""

    def _import_rifle(self):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=SMG_GLB)
        new = [o for o in bpy.data.objects if o not in before]
        prop = next(o for o in new if o.type == "MESH")
        prop.parent = None
        return prop
