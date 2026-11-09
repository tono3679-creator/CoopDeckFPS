"""Surface mask and glow mask for the Lightning Rabbit (Phase 9), body and first-person arms.

    blender -b ArtSource/LightningRabbit/Animation/LightningRabbit_Rig.blend -P rabbit_look.py -- \
        [--out <dir>] [--check <dir>] [--no-fp]

Both masks are 2048 textures in the UVs of the texture they go with (the body's LODs share one UV set; the
first-person arms have their own):

  surface mask  RGBA = softness, smoothness, metallic, warmth for SG_Character_SoftToon, one preset per region
                (FoxGunnerLookFactory.Presets: fur, face, eye, cloth, leather, metal, glass). Each texel's region comes
                from its base colour, the body part of the triangle over it (its dominant skin bone) and where that
                triangle is on the body, the same way the fox's mask is made in Unity.
  glow mask     R = how strongly the texel glows: the cyan / blue-white lightning marks of the design (bolt icons on the
                gloves, boots, goggles and chest, the scarf's bolts, the battery tubes). Cyan texels plus the white
                cores they enclose (a morphological closing), soft at the edges. Unity's CH_GlowLines shader adds
                base colour x mask x HDR colour on a copy of the glowing triangles.

The mesh, rig, UVs and colours are not changed. Outputs (staging, copied into Assets by hand like the other staged
files): T_LightningRabbit_SurfaceMask.png, T_LightningRabbit_GlowMask.png, T_LightningRabbit_FPArms_SurfaceMask.png,
T_LightningRabbit_FPArms_GlowMask.png. --check renders the regions and the glow on the model.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import refine_character as rc  # noqa: E402
from rabbit_common import STAGING, script_args  # noqa: E402

MASK = 2048
FP_FBX = os.path.join(STAGING, "LightningRabbit_FPArms.fbx")
FP_BASE = os.path.join(STAGING, "T_LightningRabbit_FPArms_BaseColor.png")

# Region presets: the same numbers as FoxGunnerLookFactory.Presets (softness, smoothness, metallic, warmth).
REGIONS = ("Fur", "Face", "Eye", "Cloth", "Leather", "Metal", "Glass")
FUR, FACE, EYE, CLOTH, LEATHER, METAL, GLASS = range(7)
PRESETS = np.array([
    (0.85, 0.12, 0.0, 0.55),
    (1.0, 0.16, 0.0, 0.8),
    (0.5, 0.75, 0.0, 0.0),
    (0.6, 0.06, 0.0, 0.15),
    (0.3, 0.42, 0.0, 0.0),
    (0.15, 0.62, 0.85, 0.0),
    (0.2, 0.85, 0.0, 0.0),
], dtype=np.float32)
DEBUG = np.array([(1, 0.62, 0.2), (1, 0.72, 0.8), (0, 1, 1), (0.15, 0.35, 1), (0.3, 0.14, 0.04),
                  (0.85, 0.85, 0.9), (0.1, 0.9, 0.2)], dtype=np.float32)

PARTS = ("Head", "Ear", "Neck", "Torso", "Scarf", "Hips", "Tail", "UpperArm", "Forearm", "Hand", "Thigh", "Shin",
         "Foot")
P = {n: i for i, n in enumerate(PARTS)}
TONES = ("Glow", "Yellow", "Orange", "Pink", "Lens", "White", "Grey", "Dark", "Muted")
T = {n: i for i, n in enumerate(TONES)}


def parse():
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=STAGING)
    p.add_argument("--check")
    p.add_argument("--no-fp", action="store_true")
    return p.parse_args(script_args())


def part_of(bone):
    b = bone.lower()
    if b.startswith("ear"):
        return P["Ear"]
    if b.startswith("scarf"):
        return P["Scarf"]
    if b.startswith("tail"):
        return P["Tail"]
    if "forearm" in b:
        return P["Forearm"]
    if "head" in b and "hand" not in b:
        return P["Head"]
    if "neck" in b:
        return P["Neck"]
    if "spine" in b or "shoulder" in b:
        return P["Torso"]
    if "hips" in b:
        return P["Hips"]
    if "hand" in b or any(d in bone for d in ("Thumb", "Index", "Middle", "Ring", "Pinky")):
        return P["Hand"]
    if "upleg" in b:
        return P["Thigh"]
    if "foot" in b or "toe" in b:
        return P["Foot"]
    if b.endswith("leg"):
        return P["Shin"]
    if b.endswith("arm"):
        return P["UpperArm"]
    return P["Torso"]


# ---------------------------------------------------------------- image helpers

def hsv(rgb):
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = np.maximum(mx - mn, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6.0
    s = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    return h, s, mx


def disk(r):
    return [(dy, dx) for dy in range(-r, r + 1) for dx in range(-r, r + 1) if dy * dy + dx * dx <= r * r + r]


def shifted(a, dy, dx, fill):
    out = np.full_like(a, fill)
    h, w = a.shape
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = a[ys, xs]
    return out


def dilate(m, r):
    out = m.copy()
    for dy, dx in disk(r):
        out |= shifted(m, dy, dx, False)
    return out


def erode(m, r):
    out = m.copy()
    for dy, dx in disk(r):
        out &= shifted(m, dy, dx, True)
    return out


def blur(a, passes=1):
    for _ in range(passes):
        a = (a + shifted(a, 0, 1, 0) + shifted(a, 0, -1, 0) + shifted(a, 1, 0, 0) + shifted(a, -1, 0, 0)) / 5.0
    return a


def down2(a):
    h, w = a.shape[:2]
    return a.reshape(h // 2, 2, w // 2, 2, *a.shape[2:]).mean((1, 3))


def resize_to(a, size):
    while a.shape[0] > size:
        a = down2(a)
    if a.shape[0] < size:
        k = size // a.shape[0]
        a = np.repeat(np.repeat(a, k, 0), k, 1)
    return a


def save_gray(a, path):
    rc.save_rgb(np.repeat(np.clip(a, 0, 1)[..., None], 3, -1), path, "Non-Color")
    print(f"  wrote {path}")


def save_rgba(a, path):
    h, w, _ = a.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "STRAIGHT"
    img.pixels.foreach_set(np.clip(a, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    print(f"  wrote {path}")


# ---------------------------------------------------------------- glow

def glow_mask(rgb):
    """Glow strength (0-1) per texel of the base colour: cyan or blue-white tinted texels, the near-white cores they
    enclose, without single-texel noise; soft at the edges."""
    h, s, v = hsv(rgb)
    cyan = (h > 0.45) & (h < 0.62) & (((s > 0.14) & (v > 0.72)) | ((s > 0.35) & (v > 0.5)))
    cyan = dilate(erode(cyan, 1), 1)                       # opening: drop specks of bluish shading
    bright = v > 0.62
    core = erode(dilate(cyan, 4), 4) & bright              # closing: white cores inside the cyan shapes
    glow = (cyan | core).astype(np.float32)
    strength = np.clip((v - 0.5) / 0.35, 0.0, 1.0)         # dim cyan (shaded rims) glows less
    return blur(glow * strength, 1)


# ---------------------------------------------------------------- triangles in texture space

def triangle_ids(tris_uv, size):
    """Index of the triangle over each texel (-1 where none): the texel centres inside each UV triangle, about half a
    texel past its edges so thin triangles still claim texels. Triangles are processed in batches by their size."""
    p = tris_uv.astype(np.float64) * size - 0.5
    lo = np.floor(p.min(1)).astype(np.int64)
    hi = np.ceil(p.max(1)).astype(np.int64)
    ext = (hi - lo).max(1) + 1
    out = np.full((size, size), -1, np.int32)
    k = 2
    done = np.zeros(len(p), bool)
    while not done.all():
        sel = np.nonzero(~done & (ext <= k))[0]
        done[sel] = True
        step = max(1, 4_000_000 // (k * k))
        for c0 in range(0, len(sel), step):
            ch = sel[c0:c0 + step]
            a, b, cc = p[ch, 0], p[ch, 1], p[ch, 2]
            xs = lo[ch, 0, None, None] + np.arange(k)[None, None, :]
            ys = lo[ch, 1, None, None] + np.arange(k)[None, :, None]
            xs, ys = np.broadcast_arrays(xs, ys)
            d = (b[:, 1] - cc[:, 1]) * (a[:, 0] - cc[:, 0]) + (cc[:, 0] - b[:, 0]) * (a[:, 1] - cc[:, 1])
            ok = np.abs(d) > 1e-12
            d = np.where(ok, d, 1.0)[:, None, None]
            l1 = ((b[:, 1] - cc[:, 1])[:, None, None] * (xs - cc[:, 0, None, None])
                  + (cc[:, 0] - b[:, 0])[:, None, None] * (ys - cc[:, 1, None, None])) / d
            l2 = ((cc[:, 1] - a[:, 1])[:, None, None] * (xs - cc[:, 0, None, None])
                  + (a[:, 0] - cc[:, 0])[:, None, None] * (ys - cc[:, 1, None, None])) / d
            l3 = 1 - l1 - l2
            e = (-0.6 / np.maximum(np.sqrt(np.abs(d)), 1.0))
            inside = (l1 >= e) & (l2 >= e) & (l3 >= e) & ok[:, None, None]
            inside &= (xs >= 0) & (xs < size) & (ys >= 0) & (ys < size)
            idx = np.broadcast_to(ch[:, None, None], inside.shape)
            out[ys[inside], xs[inside]] = idx[inside]
        k *= 2
    return out


def triangle_parts(obj):
    """Body part (dominant skin bone over the three corners) and centroid of every loop triangle."""
    me = obj.data
    me.calc_loop_triangles()
    names = [g.name for g in obj.vertex_groups]
    gpart = np.array([part_of(n) for n in names])
    nv = len(me.vertices)
    score = np.zeros((nv, len(PARTS)), np.float32)
    for v in me.vertices:
        for g in v.groups:
            score[v.index, gpart[g.group]] += g.weight
    tv = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("vertices", tv)
    tv = tv.reshape(-1, 3)
    part = score[tv].sum(1).argmax(1)
    co = np.empty(nv * 3)
    me.vertices.foreach_get("co", co)
    m = np.array(obj.matrix_world)
    co = co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]
    return part, co[tv].mean(1)


# ---------------------------------------------------------------- regions

def tones(rgb, glow):
    h, s, v = hsv(rgb)
    t = np.full(h.shape, T["Muted"], np.int8)
    t[(s < 0.22) & (v >= 0.3)] = T["Grey"]
    t[(s < 0.24) & (v >= 0.66)] = T["White"]
    t[v < 0.3] = T["Dark"]
    t[(h > 0.5) & (h < 0.72) & (s >= 0.3) & (v >= 0.2) & (v < 0.75)] = T["Lens"]
    t[((h > 0.88) | (h < 0.035)) & (s >= 0.18) & (v >= 0.4)] = T["Pink"]
    t[(h >= 0.035) & (h < 0.09) & (s >= 0.45) & (v >= 0.45)] = T["Orange"]
    t[(h >= 0.09) & (h <= 0.18) & (s >= 0.4) & (v >= 0.45)] = T["Yellow"]
    t[glow > 0.35] = T["Glow"]
    return t


class Zones:
    """Places on the rest-pose body (Blender: the rabbit faces -Y, up is +Z, its left is +X). The numbers come from
    where each tone sits on the rebuilt body (head bone 1.065 m, eyes 1.13-1.18 m; hips bone 0.75 m, belt 0.70-0.775 m,
    bare midriff 0.77-0.84 m, mostly weighted to the thighs)."""

    def __init__(self, arm):
        w = arm.matrix_world
        b = arm.data.bones
        self.head = w @ b["Head"].head_local
        self.hips = w @ b["Hips"].head_local
        self.knee = w @ b["LeftLeg"].head_local
        self.waist = w @ b["Spine01"].head_local

    def face(self, p):
        """Front of the head from the chin to the brow: face fur, eyes, nose, cheek marks."""
        return ((p[:, 1] < self.head.y - 0.03) & (p[:, 2] > self.head.z + 0.005) & (p[:, 2] < self.head.z + 0.125)
                & (np.abs(p[:, 0]) < 0.09))

    def eyes(self, p):
        """Above the cheek marks (which are level with the nose)."""
        return self.face(p) & (p[:, 2] > self.head.z + 0.058) & (np.abs(p[:, 0]) > 0.012)

    def collar(self, p):
        """The scarf's collar under the chin carries head weights."""
        return p[:, 2] < self.head.z + 0.005

    def headset(self, p):
        """Headphones and goggles over the left ear."""
        return (p[:, 0] > 0.07) & (p[:, 2] > self.head.z + 0.12)

    def boot(self, p):
        return p[:, 2] < self.knee.z - 0.13

    def belt(self, p):
        return (p[:, 2] > self.hips.z - 0.05) & (p[:, 2] < self.hips.z + 0.025)

    def midriff(self, p):
        """The bare band between the belt and the crop top, all round; not the vest's front flaps."""
        band = (p[:, 2] > self.hips.z + 0.018) & (p[:, 2] < self.hips.z + 0.09)
        flaps = (np.abs(p[:, 0]) > 0.07) & (p[:, 1] < self.hips.y - 0.06)
        return band & ~flaps



def decide(part, tone, p, z):
    """Region per texel from its part, tone and position (all arrays of the used texels)."""
    r = np.full(part.shape, FUR, np.int8)

    def put(sel, region):
        r[sel] = region

    glow, yellow, orange, pink = tone == T["Glow"], tone == T["Yellow"], tone == T["Orange"], tone == T["Pink"]
    lens, white, grey, dark, muted = (tone == T["Lens"], tone == T["White"], tone == T["Grey"], tone == T["Dark"],
                                      tone == T["Muted"])
    furry = white | grey | muted

    head = part == P["Head"]
    face, eyes, headset = z.face(p), z.eyes(p), z.headset(p)
    put(head & furry, FUR)                                        # hair
    put(head & face & (furry | pink | yellow | orange | dark), FACE)   # face fur, nose, cheek bolts, mouth line
    put(head & eyes & (yellow | dark | orange), EYE)              # irises, pupils, lash lines
    put(head & ~face & dark, LEATHER)                             # goggle strap round the back
    put(head & headset & ~white, METAL)                           # headphone shell, frames
    put(head & headset & dark, LEATHER)                           # ear pads
    put(head & ~face & (lens | glow), GLASS)                      # lenses, battery tube
    put(head & z.collar(p) & ~(white | grey | glow), CLOTH)      # scarf collar (white there is neck fur)

    ear = part == P["Ear"]
    put(ear & furry, FUR)
    put(ear & ~furry, FACE)                                       # pink inner ear and its painted bolts

    neck = part == P["Neck"]
    put(neck & furry, FUR)
    put(neck & ~furry, CLOTH)

    torso = part == P["Torso"]
    put(torso, CLOTH)                                             # crop top, vest, scarf collar, sleeves
    put(torso & z.midriff(p) & furry, FUR)
    put(torso & (orange | lens), METAL)                           # vest grommets
    put(torso & glow, GLASS)

    put(part == P["Scarf"], CLOTH)
    put((part == P["Scarf"]) & glow, GLASS)
    put(part == P["Tail"], FUR)

    hips = (part == P["Hips"]) | (part == P["Thigh"])
    belt = z.belt(p)
    put(hips, CLOTH)                                              # trousers, vest flaps, pouches
    put(hips & dark & belt, LEATHER)
    put(hips & (yellow | orange) & belt, LEATHER)                 # pouches hang from the belt
    put(hips & grey, METAL)                                       # clips, battery caps
    put(hips & white & belt & (p[:, 1] < z.hips.y), METAL)       # buckle
    put(hips & (lens | glow), GLASS)
    put(hips & furry & z.midriff(p), FUR)                         # bare midriff
    put(hips & white & (p[:, 1] > z.hips.y + 0.05) & (p[:, 2] > z.hips.z - 0.12), FUR)   # tail root

    arm = part == P["UpperArm"]
    put(arm & furry, FUR)
    put(arm & (yellow | orange), CLOTH)                           # sleeve cuffs
    put(arm & dark, LEATHER)                                      # arm bands
    band = arm & (yellow | dark)
    if band.any():                                                # the vest's short sleeves end at the arm band
        sleeve = np.percentile(np.abs(p[band, 0]), 10)
        put(arm & furry & (np.abs(p[:, 0]) < sleeve), CLOTH)

    hand = (part == P["Forearm"]) | (part == P["Hand"])
    put(hand & furry, FUR)
    put(hand & dark, LEATHER)                                     # gloves, wrist guards
    put(hand & (yellow | orange), LEATHER)
    put(hand & grey & (tone != T["White"]), METAL)                # glove plate rims
    put(hand & (lens | glow), GLASS)

    leg = (part == P["Shin"]) | (part == P["Foot"])
    boot = z.boot(p)
    put(leg & ~boot & dark, CLOTH)                                # trouser legs
    put(leg & ~boot & (yellow | orange), CLOTH)
    put(leg & ~boot & furry, FUR)                                 # ankles above the boots
    put(leg & boot, LEATHER)                                      # boots, soles
    put(leg & boot & grey, METAL)                                 # shin plates
    put((part == P["Foot"]) & (grey | white), METAL)              # toe caps
    put(leg & (lens | glow), GLASS)
    put(leg & boot & orange, GLASS)                               # boot lamps
    return r


def majority(regions, passes=2):
    """A texel that at most one of its 8 neighbours shares, while 5 or more agree on another region, joins them."""
    for _ in range(passes):
        counts = np.zeros((len(REGIONS),) + regions.shape, np.int8)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy or dx:
                    s = shifted(regions, dy, dx, -1)
                    for k in range(len(REGIONS)):
                        counts[k] += s == k
        own = np.take_along_axis(counts, np.clip(regions, 0, None)[None].astype(np.int64), 0)[0]
        best = counts.argmax(0)
        flip = (regions >= 0) & (own <= 1) & (counts.max(0) >= 5) & (best != regions)
        regions = np.where(flip, best, regions).astype(np.int8)
        print(f"  majority filter: {int(flip.sum())} texels joined their neighbours")
    return regions


def grow(regions, passes=4):
    """Regions grow into texels no triangle uses, so filtering and mips do not bleed at UV seams."""
    for _ in range(passes):
        nxt = regions.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            s = shifted(regions, dy, dx, -1)
            take = (nxt < 0) & (s >= 0)
            nxt[take] = s[take]
        regions = nxt
    return regions


def build_masks(obj, base_rgb, z, name, out):
    """Region map, surface mask and glow mask of one mesh in its own UVs. Returns (regions, glow) at MASK size."""
    me = obj.data
    tris = rc.uv_triangles(me, me.uv_layers.active)
    ids = triangle_ids(tris, MASK)
    part, centre = triangle_parts(obj)
    used = ids >= 0
    print(f"  {name}: {len(tris)} triangles, {used.mean():.1%} of the {MASK} mask used")

    glow_full = glow_mask(base_rgb)
    glow = resize_to(glow_full, MASK)
    rgb = resize_to(base_rgb, MASK)
    tone = tones(rgb, glow)
    regions = np.full((MASK, MASK), -1, np.int8)
    t = ids[used]
    regions[used] = decide(part[t], tone[used], centre[t], z)
    regions = grow(majority(regions))
    shares = ", ".join(f"{REGIONS[k]} {np.mean(regions[used] == k):.1%}" for k in range(len(REGIONS)))
    print(f"  {name} regions: {shares}")

    surface = np.zeros((MASK, MASK, 4), np.float32)
    set_ = regions >= 0
    surface[set_] = PRESETS[regions[set_]]
    # Blender images are stored bottom row first, like UV v: no flip needed.
    save_rgba(surface, os.path.join(out, f"T_{name}_SurfaceMask.png"))
    glow_out = np.where(dilate(used, 2), glow, 0.0)
    save_gray(glow_out, os.path.join(out, f"T_{name}_GlowMask.png"))
    glowing = np.unique(ids[(glow_out > 0.05) & used])
    print(f"  {name} glow: {np.mean(glow_out[used] > 0.05):.2%} of used texels, {len(glowing)} triangles under it")
    return regions, glow_out


# ---------------------------------------------------------------- check renders

def check_renders(obj, base_rgb, regions, glow, out_dir, name, shots):
    from rabbit_debug import render_sheet
    os.makedirs(out_dir, exist_ok=True)
    base = resize_to(base_rgb, MASK)
    dbg = np.where((regions >= 0)[..., None], DEBUG[np.clip(regions, 0, None)], 0.3)
    shade = base.mean(-1, keepdims=True)
    region_img = 0.65 * dbg + 0.35 * shade                       # keep the painted detail readable
    glow_img = base * (1 - 0.6 * np.clip(glow, 0, 1)[..., None]) + np.array([1.0, 0.0, 1.0]) * 0.9 * glow[..., None]
    mat = bpy.data.materials.new("LookCheck")
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    mat.node_tree.nodes.active = node
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o is not obj
    for tag, pic in (("regions", region_img), ("glow", glow_img), ("base", base)):
        path = os.path.join(out_dir, f"_{name}_{tag}_tex.png")
        rc.save_rgb(pic, path)
        node.image = bpy.data.images.load(path, check_existing=False)
        render_sheet(os.path.join(out_dir, f"{name}_{tag}.png"), shots, res=520, colour_type="TEXTURE")
        os.remove(path)


def body_shots(z):
    c = Vector((0, 0, 0.85))
    head = z.head + Vector((0, 0, 0.08))
    return [(c, 1.9, "front"), (c, 1.9, "back"), (c, 1.9, "right"), (head, 0.42, "front"), (head, 0.42, "q"),
            (z.waist + Vector((0, 0, -0.02)), 0.5, "front"), (Vector((0.1, 0, 0.2)), 0.45, "q")]


# ---------------------------------------------------------------- main

def main():
    args = parse()
    os.makedirs(args.out, exist_ok=True)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups)
    z = Zones(arm)
    img = bpy.data.images["T_LightningRabbit_BaseColor"]
    base = rc.image_array(img)[..., :3]
    regions, glow = build_masks(body, base, z, "LightningRabbit", args.out)
    if args.check:
        check_renders(body, base, regions, glow, args.check, "body", body_shots(z))

    if not args.no_fp:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=FP_FBX)
        arms = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
        fp_img = bpy.data.images.load(FP_BASE, check_existing=True)
        fp_base = rc.image_array(fp_img)[..., :3]
        # The arms were turned for Unity on export; their parts come from the bone names, their places from the
        # body zones are not needed (no face, belt or boots on the arms).
        regions, glow = build_masks(arms, fp_base, z, "LightningRabbit_FPArms", args.out)
        if args.check:
            co = np.array([arms.matrix_world @ v.co for v in arms.data.vertices])
            ctr, size = Vector((co.min(0) + co.max(0)) / 2), float((co.max(0) - co.min(0)).max()) * 1.05
            check_renders(arms, fp_base, regions, glow, args.check, "fparms",
                          [(ctr, size, "front"), (ctr, size, "back"), (ctr, size, "top")])
    print("LOOK_DONE")


if __name__ == "__main__":
    main()
