"""Shared helpers for the Lightning Rabbit rig (run inside Blender 5.x, headless).

Coordinates (Blender, before the FBX turn): the rabbit faces -Y, its right hand is at -X, up is +Z, 1 unit = 1 m,
origin on the ground under the hips (prepare_for_unity.py output, rebuilt by rebuild_body.py).
"""
import colorsys
import math
import os
import sys
from collections import deque

import bpy
import numpy as np
from mathutils import Matrix, Vector

FPS = 30
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SRC = os.path.join(REPO, "ArtSource", "LightningRabbit")
HQ_GLB = os.path.join(SRC, "Export", "LightningRabbit_HQ.glb")
ANIM_DIR = os.path.join(SRC, "Animation")
RIG_BLEND = os.path.join(ANIM_DIR, "LightningRabbit_Rig.blend")
STAGING = os.path.join(SRC, "Export", "Unity")       # FBX for Unity before Phase 8 moves it into Assets
TURN_FOR_UNITY = Matrix.Rotation(math.pi, 4, "Z")    # Unity reads the FBX axis header; the rig itself is turned
FORWARD, RIGHT, UP = Vector((0, -1, 0)), Vector((-1, 0, 0)), Vector((0, 0, 1))


def script_args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def import_rabbit(path=HQ_GLB):
    """Armature and body from the rebuilt GLB, with the vertices the GLB split at UV seams welded again (UVs live on
    loops, so welding keeps them) and smooth normals."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps, sc.render.fps_base = FPS, 1.0
    bpy.ops.import_scene.gltf(filepath=path)
    for o in list(bpy.data.objects):
        if o.type == "MESH" and not o.vertex_groups:
            bpy.data.objects.remove(o, do_unlink=True)  # the importer's bone-shape icosphere
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    arm.animation_data_clear()
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    select_only(body)
    before = len(body.data.vertices)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=1e-6)
    bpy.ops.object.mode_set(mode="OBJECT")
    if body.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth()
    print(f"  imported {os.path.basename(path)}: welded {before} -> {len(body.data.vertices)} vertices, "
          f"{len(arm.data.bones)} bones")
    return arm, body


def coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    m = np.array(obj.matrix_world)
    return co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]


def bone_world(arm, name):
    b = arm.data.bones[name]
    return arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local


def vertex_hsv(body):
    """HSV (0-1) of the base colour texture under each vertex (first loop), as an (n, 3) array."""
    me = body.data
    img = None
    for slot in body.material_slots:
        for n in slot.material.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                img = n.image
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    vi = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", vi)
    first = np.full(len(me.vertices), -1, dtype=np.int64)
    first[vi[::-1]] = np.arange(len(vi))[::-1]
    u = uv[first]
    x = np.clip((u[:, 0] * w).astype(int), 0, w - 1)
    y = np.clip((u[:, 1] * h).astype(int), 0, h - 1)
    rgb = px[y, x, :3]
    return np.array([colorsys.rgb_to_hsv(*c) for c in rgb])


def adjacency(body):
    me = body.data
    e = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", e)
    e = e.reshape(-1, 2)
    adj = [[] for _ in me.vertices]
    for a, b in e:
        adj[a].append(b)
        adj[b].append(a)
    return adj


def components(indices, adj):
    """Connected components of the subgraph on `indices` (iterable of vertex ids); largest first."""
    inside = set(int(i) for i in indices)
    seen, out = set(), []
    for s in inside:
        if s in seen:
            continue
        comp, q = [], deque([s])
        seen.add(s)
        while q:
            v = q.popleft()
            comp.append(v)
            for n in adj[v]:
                if n in inside and n not in seen:
                    seen.add(n)
                    q.append(n)
        out.append(comp)
    return sorted(out, key=len, reverse=True)


def grow(seeds, adj, allowed):
    """Flood fill from seeds through vertices for which allowed(i) is true."""
    seen = set(int(s) for s in seeds)
    q = deque(seen)
    while q:
        v = q.popleft()
        for n in adj[v]:
            if n not in seen and allowed(n):
                seen.add(n)
                q.append(n)
    return seen


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, dtype=float) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)
