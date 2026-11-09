"""Cuts views out of a design sheet, upscales them with Real-ESRGAN and squares them for Meshy (run inside Blender).

    blender -b --factory-startup -P crop_reference.py -- --sheet sheet.png --out <dir> --esrgan <dir with exe> \
        --view side=440,770,820,952 --view front=850,765,910,940 ... [--scale 4] [--margin 0.06]

Each --view is name=x0,y0,x1,y1 in sheet pixels (top-left origin), a loose box around one drawing. The box is
tightened to the drawing (pixels that differ from the box's border colour), cut with a small margin, upscaled with
realesrgan-x4plus-anime (BSD-3, not in the repo), padded to a square with the background colour and written as
<out>/<name>.png. The tightened boxes are printed for the record.
"""
import argparse
import json
import os
import subprocess
import sys

import bpy
import numpy as np


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--sheet", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--esrgan", required=True)
    p.add_argument("--model", default="realesrgan-x4plus-anime")
    p.add_argument("--view", action="append", required=True)
    p.add_argument("--margin", type=float, default=0.06)
    p.add_argument("--threshold", type=float, default=0.08)
    return p.parse_args(argv)


def load(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1, :, :3].copy()   # top row first


def save(rgb, path):
    h, w, _ = rgb.shape
    img = bpy.data.images.new("out", w, h, alpha=False)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = rgb
    img.pixels.foreach_set(rgba[::-1].ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    a = parse()
    os.makedirs(a.out, exist_ok=True)
    sheet = load(a.sheet)
    exe = os.path.join(a.esrgan, "realesrgan-ncnn-vulkan.exe")
    record = {}
    for spec in a.view:
        name, box = spec.split("=")
        x0, y0, x1, y1 = (int(v) for v in box.split(","))
        crop = sheet[y0:y1, x0:x1]
        border = np.concatenate([crop[0], crop[-1], crop[:, 0], crop[:, -1]])
        bg = np.median(border, axis=0)
        mask = np.linalg.norm(crop - bg, axis=2) > a.threshold
        rows, cols = np.where(mask.sum(1) > 1)[0], np.where(mask.sum(0) > 1)[0]
        t, b, l, r = rows[0], rows[-1] + 1, cols[0], cols[-1] + 1
        m = int(round(a.margin * max(b - t, r - l)))
        t, b, l, r = max(0, t - m), min(crop.shape[0], b + m), max(0, l - m), min(crop.shape[1], r + m)
        tight = crop[t:b, l:r]
        small = os.path.join(a.out, f"_{name}_small.png")
        big = os.path.join(a.out, f"_{name}_x4.png")
        save(tight, small)
        res = subprocess.run([exe, "-i", small, "-o", big, "-n", a.model, "-s", "4", "-t", "64",
                              "-m", os.path.join(a.esrgan, "models"), "-f", "png"], capture_output=True, text=True)
        if res.returncode != 0 or not os.path.exists(big):
            raise RuntimeError(f"Real-ESRGAN failed on {name}: {res.stderr[-400:]}")
        up = load(big)
        h, w, _ = up.shape
        side = max(h, w)
        sq = np.empty((side, side, 3), dtype=np.float32)
        sq[:] = bg
        oy, ox = (side - h) // 2, (side - w) // 2
        sq[oy:oy + h, ox:ox + w] = up
        save(sq, os.path.join(a.out, f"{name}.png"))
        os.remove(small)
        os.remove(big)
        record[name] = {"sheet_box": [int(x0 + l), int(y0 + t), int(x0 + r), int(y0 + b)], "drawing_px": [int(r - l), int(b - t)],
                        "square_px": side, "background": [round(float(v), 4) for v in bg]}
        print(f"CROP {name}: {json.dumps(record[name])}")
    with open(os.path.join(a.out, "crops.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=1)


main()
