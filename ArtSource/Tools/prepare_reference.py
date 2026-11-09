"""Turn a character turnaround image into a square reference PNG for Meshy (run inside Blender, headless).

    blender -b --factory-startup -P prepare_reference.py -- --in front.webp --out rabbit_front.png [--margin 0.03]

- reads PNG/JPEG/WebP (Blender's image loader)
- takes the background colour from the image border (median) and finds the character's silhouette
- pads the image to a square with that colour, centred on the silhouette horizontally, keeping the
  original pixels (no resampling) so small details such as fingers keep their resolution
- prints the silhouette box in source pixels, which is used to measure proportions (ear tips, head, feet)
"""
import argparse
import json
import sys

import bpy
import numpy as np


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="inp", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--margin", type=float, default=0.03, help="free space around the silhouette, share of the square side")
    p.add_argument("--threshold", type=float, default=0.06, help="colour distance from the background that counts as character")
    return p.parse_args(argv)


def load_rgb(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[::-1, :, :3].copy()  # Blender stores rows bottom-up


def save_rgb(rgb, path):
    h, w, _ = rgb.shape
    img = bpy.data.images.new("ref_out", w, h, alpha=False)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = rgb
    img.pixels.foreach_set(rgba[::-1].ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def main():
    a = parse()
    rgb = load_rgb(a.inp)
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    bg = np.median(border, axis=0)
    mask = np.linalg.norm(rgb - bg, axis=2) > a.threshold
    rows = np.where(mask.sum(axis=1) > 2)[0]
    cols = np.where(mask.sum(axis=0) > 2)[0]
    top, bottom, left, right = int(rows[0]), int(rows[-1]), int(cols[0]), int(cols[-1])

    sil_h, sil_w = bottom - top + 1, right - left + 1
    side = int(np.ceil(max(sil_h, sil_w) / (1.0 - 2.0 * a.margin)))
    canvas = np.empty((side, side, 3), dtype=np.float32)
    canvas[:] = bg
    # place the source so the silhouette is centred
    cy = (top + bottom) / 2.0
    cx = (left + right) / 2.0
    oy = int(round(side / 2.0 - cy))
    ox = int(round(side / 2.0 - cx))
    sy0, sx0 = max(0, -oy), max(0, -ox)
    sy1, sx1 = min(h, side - oy), min(w, side - ox)
    canvas[sy0 + oy:sy1 + oy, sx0 + ox:sx1 + ox] = rgb[sy0:sy1, sx0:sx1]
    save_rgb(canvas, a.out)
    print("REFERENCE " + json.dumps({
        "in": a.inp, "out": a.out, "source_size": [w, h], "square": side,
        "background_rgb": [round(float(c), 4) for c in bg],
        "silhouette_px": {"top": top, "bottom": bottom, "left": left, "right": right, "height": sil_h, "width": sil_w},
    }))


main()
