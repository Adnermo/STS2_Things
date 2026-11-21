#!/usr/bin/env python3
"""
Builds ALL FOUR AAA zero-cheap-stretch Spine 4.2.0 weighted-mesh & hardware-clipping rigs for
渊墨巨蛸 (Abyssal Inkmaw):
  1. abyssal_inkmaw            — Colossal Half-Submerged Front-Facing Kraken Upper Body
                                 (14-bone anatomical torso/shoulders/2-bone undulating fins/pulsing siphons,
                                  hardware eye-socket clipping polygons + 1:1 rigid sliding eyelids,
                                  hand-painted lake waterline waves, AND 6 misty 12-bone aquatic tentacles!)
  2. abyssal_ink_beam          — Dedicated Foreground High-Pressure Sweeping Hydro-Ink Jet
                                 (hardware nozzle-to-tail beam clipping, continuous -44°->+44° left-to-right
                                  sweep across the stone bridge, counter-rotating ground splash clouds,
                                  6 parabolic ballistic ink droplets, and exact center `ink_hit_player` event!)
  3. abyssal_crushing_tentacle — Left Hero 12-Bone Pure-Rotational FK Crushing Arm
                                 (hardware waterline/chasm `emerge_clip` for true 1:1 unstretched serpentine
                                  emergence + Track-1 dual-mode `base_water` multi-bone breaking waves &
                                  parabolic droplets vs `base_floor` 4 hinged flagstones + 5 bouncing shards!)
  4. abyssal_ink_tentacle      — Right Hero 12-Bone Pure-Rotational FK Ink-Sac Whip Arm
                                 (identical hardware `emerge_clip` + Track-1 dual-mode hand-painted VFX +
                                  additive cyan-teal obsidian ink-blister glow!)
"""
from __future__ import annotations

import heapq
import json
import math
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

REPO = Path(r"D:\Things\Things-Workspace\STS2_Things")
RIG_ROOT = REPO / "tools/AbyssalInkmawRig"
ANIM_ROOT = REPO / "STS2_Things/animations/monsters"
ART_DIR = Path(r"C:\Users\adner\.gemini\antigravity\brain\630b9439-8513-49e2-8959-201a92e08ca5")

TAU = 2.0 * math.pi
FPS = 60


# ==============================================================================
# Math, Easing & Parabolic Ballistics Helpers
# ==============================================================================
def clamp(x: float, a: float = 0.0, b: float = 1.0) -> float:
    return a if x < a else (b if x > b else x)


def smoothstep(x: float) -> float:
    x = clamp(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def smootherstep(x: float) -> float:
    x = clamp(x, 0.0, 1.0)
    return x * x * x * (x * (x * 6.0 - 15.0) + 10.0)


def seg(t: float, t0: float, t1: float) -> float:
    return clamp((t - t0) / (t1 - t0)) if t1 > t0 else (1.0 if t >= t0 else 0.0)


def bump(t: float, t0: float, t1: float, t2: float) -> float:
    return smoothstep(seg(t, t0, t1)) * (1.0 - smoothstep(seg(t, t1, t2)))


def hold(t: float, t0: float, t1: float, t2: float, t3: float) -> float:
    return smoothstep(seg(t, t0, t1)) * (1.0 - smoothstep(seg(t, t2, t3)))


def ease_out_cubic(u: float) -> float:
    u = clamp(u)
    return 1.0 - (1.0 - u) ** 3


def ease_out_back(u: float, s: float = 1.45) -> float:
    u = clamp(u)
    t = u - 1.0
    return 1.0 + (s + 1.0) * (t ** 3) + s * (t ** 2)


def ease_in_back(u: float, s: float = 1.25) -> float:
    u = clamp(u)
    return (s + 1.0) * (u ** 3) - s * (u ** 2)


def spring_osc(dt: float, decay: float, freq_hz: float) -> float:
    if dt <= 0.0:
        return 0.0
    return math.exp(-decay * dt) * math.sin(TAU * freq_hz * dt)


def apply_ease(u: float, mode: str) -> float:
    u = clamp(u)
    if mode == "smooth":
        return smoothstep(u)
    if mode == "smoother":
        return smootherstep(u)
    if mode == "in_quad":
        return u * u
    if mode == "in_cubic":
        return u * u * u
    if mode == "out_quad":
        return 1.0 - (1.0 - u) ** 2
    if mode == "out_cubic":
        return ease_out_cubic(u)
    if mode == "out_back":
        return ease_out_back(u, 1.35)
    if mode == "in_back":
        return ease_in_back(u, 1.15)
    if mode == "snap_slam":
        # Accelerating whip crack that snaps decisively into the stone bridge surface
        return (u / 0.42) ** 2.1 * 0.34 if u < 0.42 else 0.34 + 0.66 * (1.0 - (1.0 - (u - 0.42) / 0.58) ** 2.6)
    return u


def eval_scalar_keyframes(keyframes: list[tuple[float, float, str]], t_q: float) -> float:
    if t_q <= keyframes[0][0]:
        return keyframes[0][1]
    if t_q >= keyframes[-1][0]:
        return keyframes[-1][1]
    for k in range(len(keyframes) - 1):
        t0, v0, _ = keyframes[k]
        t1, v1, mode = keyframes[k + 1]
        if t0 <= t_q <= t1:
            u = (t_q - t0) / max(1e-6, t1 - t0)
            return v0 + (v1 - v0) * apply_ease(u, mode)
    return keyframes[-1][1]


def eval_cascaded_fk(
    keyframes: list[tuple[float, list[float], str]],
    t: float,
    n_bones: int,
    *,
    dur: float,
    lag_fn,
    ripple_fn=None,
) -> list[float]:
    """Evaluate a multi-bone FK world-angle timeline with true per-joint overlapping action & follow-through!"""
    # Taper phase lag cleanly to 0 at start & end so every action blends seamlessly into rest/idle!
    edge_env = smoothstep(seg(t, 0.0, 0.07)) * (1.0 - smoothstep(seg(t, dur - 0.14, dur)))
    out: list[float] = []
    t_min, t_max = keyframes[0][0], keyframes[-1][0]
    for i in range(n_bones):
        u = i / max(1, n_bones - 1)
        t_q = clamp(t - lag_fn(t, u) * edge_env, t_min, t_max)
        if t_q <= t_min:
            ang = keyframes[0][1][i]
        elif t_q >= t_max:
            ang = keyframes[-1][1][i]
        else:
            ang = keyframes[-1][1][i]
            for k in range(len(keyframes) - 1):
                t0, p0, _ = keyframes[k]
                t1, p1, mode = keyframes[k + 1]
                if t0 <= t_q <= t1:
                    w = apply_ease((t_q - t0) / max(1e-6, t1 - t0), mode)
                    ang = p0[i] + (p1[i] - p0[i]) * w
                    break
        if ripple_fn is not None:
            ang += ripple_fn(t, u) * edge_env
        out.append(ang)
    return out


def hexcol(r: float, g: float, b: float, a: float) -> str:
    return "".join(f"{int(round(clamp(v) * 255)):02x}" for v in (r, g, b, a))


def eval_parabolic_arc(
    t: float,
    t_launch: float,
    life: float,
    vx: float,
    vy: float,
    g: float = 980.0,
    floor_y: float | None = None,
    bounce_restitution: float = 0.36,
    spin_deg_per_s: float | None = None,
) -> tuple[float, float, float, float]:
    """
    True gravity projectile trajectory returning (rot_deg, x, y, alpha).
    If `floor_y` is provided, computes a crisp physical ground bounce!
    If `spin_deg_per_s` is None, aligns rotation cleanly with the instantaneous velocity tangent!
    """
    dt = t - t_launch
    if dt < 0.0 or dt > life:
        return (0.0, 0.0, -40.0, 0.0)
    x = vx * dt
    y = vy * dt - 0.5 * g * dt * dt
    cur_vx = vx
    cur_vy = vy - g * dt
    if floor_y is not None and y < floor_y:
        # Solve exact impact time t_hit where vy*th - 0.5*g*th^2 = floor_y
        disc = max(0.0, vy * vy - 2.0 * g * floor_y)
        t_hit = (vy + math.sqrt(disc)) / g
        dt2 = max(0.0, dt - t_hit)
        vy_impact = abs(vy - g * t_hit) * bounce_restitution
        vx2 = vx * 0.62
        x = vx * t_hit + vx2 * dt2
        y = max(floor_y, floor_y + vy_impact * dt2 - 0.5 * g * dt2 * dt2)
        cur_vx = vx2
        cur_vy = vy_impact - g * dt2
    if spin_deg_per_s is not None:
        rot = spin_deg_per_s * dt
    else:
        # Align teardrop tail opposite velocity direction (sprite points UP by default)
        rot = math.degrees(math.atan2(cur_vy, cur_vx)) - 90.0
    fade_in = smoothstep(seg(dt, 0.0, min(0.06, life * 0.12)))
    fade_out = 1.0 - smoothstep(seg(dt, life * 0.72, life))
    return (rot, x, y, clamp(fade_in * fade_out, 0.0, 1.0))


# ==============================================================================
# Geodesic Medial-Axis Centerline & Anti-Bleed Tentacle Skinning (12 Bones!)
# ==============================================================================
def extract_tentacle_centerline(im: Image.Image, n_bones: int = 12) -> tuple[np.ndarray, np.ndarray]:
    arr = np.array(im.convert("RGBA"), dtype=np.uint8)
    H, W = arr.shape[:2]
    fg = (arr[:, :, 3] > 28).astype(np.uint8)
    ds = 2
    hs, ws = max(8, H // ds), max(8, W // ds)
    fg_s = (cv2.resize(fg, (ws, hs), interpolation=cv2.INTER_AREA) > 0).astype(np.uint8)
    num, labels, stats, _ = cv2.connectedComponentsWithStats(fg_s, connectivity=8)
    best_lbl = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    fg_s = (labels == best_lbl).astype(np.uint8)
    dist_s = cv2.distanceTransform(fg_s, cv2.DIST_L2, 5)
    ys, xs = np.where(fg_s > 0)
    y_max = int(ys.max())
    root_band = (fg_s > 0) & (np.arange(hs)[:, None] >= y_max - max(3, int(hs * 0.04)))
    rb_ys, rb_xs = np.where(root_band)
    root_idx = int(np.argmax(dist_s[rb_ys, rb_xs]))
    ry, rx = int(rb_ys[root_idx]), int(rb_xs[root_idx])

    INF = 1e18
    geo = np.full((hs, ws), INF, dtype=np.float64)
    geo[ry, rx] = 0.0
    pq = [(0.0, ry, rx)]
    nbrs = [(-1, 0, 1.0), (1, 0, 1.0), (0, -1, 1.0), (0, 1, 1.0),
            (-1, -1, 1.4142), (-1, 1, 1.4142), (1, -1, 1.4142), (1, 1, 1.4142)]
    while pq:
        d, cy, cx = heapq.heappop(pq)
        if d > geo[cy, cx]:
            continue
        for dy, dx, step in nbrs:
            ny, nx = cy + dy, cx + dx
            if 0 <= ny < hs and 0 <= nx < ws and fg_s[ny, nx]:
                nd = d + step
                if nd < geo[ny, nx]:
                    geo[ny, nx] = nd
                    heapq.heappush(pq, (nd, ny, nx))

    finite = np.isfinite(geo) & (geo < INF * 0.5)
    fy, fx = np.where(finite)
    tip_i = int(np.argmax(geo[fy, fx]))
    ty, tx = int(fy[tip_i]), int(fx[tip_i])

    max_d = float(dist_s.max()) + 1e-6
    cost_map = 1.0 / (0.25 + (dist_s / max_d) ** 1.65)
    rcost = np.full((hs, ws), INF, dtype=np.float64)
    parent = np.full((hs, ws, 2), -1, dtype=np.int32)
    rcost[ry, rx] = 0.0
    pq = [(0.0, ry, rx)]
    while pq:
        d, cy, cx = heapq.heappop(pq)
        if d > rcost[cy, cx]:
            continue
        if cy == ty and cx == tx:
            break
        for dy, dx, step in nbrs:
            ny, nx = cy + dy, cx + dx
            if 0 <= ny < hs and 0 <= nx < ws and fg_s[ny, nx]:
                nd = d + step * cost_map[ny, nx]
                if nd < rcost[ny, nx]:
                    rcost[ny, nx] = nd
                    parent[ny, nx, 0] = cy
                    parent[ny, nx, 1] = cx
                    heapq.heappush(pq, (nd, ny, nx))

    path = []
    cy, cx = ty, tx
    while cy >= 0 and cx >= 0:
        path.append((cx * ds + ds * 0.5, cy * ds + ds * 0.5))
        py, px = int(parent[cy, cx, 0]), int(parent[cy, cx, 1])
        cy, cx = py, px
    path.reverse()
    path_arr = np.array(path, dtype=np.float64)

    if len(path_arr) >= 9:
        kernel = np.exp(-0.5 * (np.arange(-6, 7) / 2.6) ** 2)
        kernel /= kernel.sum()
        sx = np.convolve(np.pad(path_arr[:, 0], 6, mode="edge"), kernel, mode="valid")
        sy = np.convolve(np.pad(path_arr[:, 1], 6, mode="edge"), kernel, mode="valid")
        sx[0], sy[0] = path_arr[0, 0], path_arr[0, 1]
        sx[-1], sy[-1] = path_arr[-1, 0], path_arr[-1, 1]
        path_arr = np.column_stack([sx, sy])

    segs = np.hypot(np.diff(path_arr[:, 0]), np.diff(path_arr[:, 1]))
    arc = np.concatenate([[0.0], np.cumsum(segs)])
    total_len = arc[-1]
    sample_s = np.linspace(0.0, total_len, n_bones + 1)
    pts = np.column_stack([
        np.interp(sample_s, arc, path_arr[:, 0]),
        np.interp(sample_s, arc, path_arr[:, 1]),
    ])

    dense_n = 260
    dense_s = np.linspace(0.0, total_len, dense_n)
    dense_x = np.interp(dense_s, arc, path_arr[:, 0]) / ds
    dense_y = np.interp(dense_s, arc, path_arr[:, 1]) / ds
    dense_u = np.linspace(0.0, float(n_bones - 1), dense_n)

    u_s = np.full((hs, ws), -1.0, dtype=np.float32)
    dist_u = np.full((hs, ws), INF, dtype=np.float64)
    pq = []
    for k in range(dense_n):
        ix = int(clamp(round(dense_x[k]), 0, ws - 1))
        iy = int(clamp(round(dense_y[k]), 0, hs - 1))
        if fg_s[iy, ix]:
            dist_u[iy, ix] = 0.0
            u_s[iy, ix] = float(dense_u[k])
            heapq.heappush(pq, (0.0, iy, ix, float(dense_u[k])))
    while pq:
        d, cy, cx, uv = heapq.heappop(pq)
        if d > dist_u[cy, cx]:
            continue
        for dy, dx, step in nbrs:
            ny, nx = cy + dy, cx + dx
            if 0 <= ny < hs and 0 <= nx < ws and fg_s[ny, nx]:
                nd = d + step
                if nd < dist_u[ny, nx]:
                    dist_u[ny, nx] = nd
                    u_s[ny, nx] = uv
                    heapq.heappush(pq, (nd, ny, nx, uv))

    known = u_s >= 0.0
    for _ in range(6):
        dil_k = cv2.dilate(known.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        new = dil_k & (~known)
        if not new.any():
            break
        num_v = cv2.blur(np.where(known, u_s, 0.0), (3, 3))
        den_v = cv2.blur(known.astype(np.float32), (3, 3))
        u_s[new] = num_v[new] / np.maximum(den_v[new], 1e-6)
        known = dil_k

    u_s = np.where(u_s >= 0.0, u_s, 0.0)
    u_map = cv2.resize(u_s, (W, H), interpolation=cv2.INTER_LINEAR)
    return pts, u_map


# ==============================================================================
# Spine 4.2.0 Atlas, Weighted Mesh, Hardware Clipping & Export Infrastructure
# ==============================================================================
def pack_atlas(name: str, images: dict[str, Image.Image], out_dir: Path) -> tuple[str, int, int]:
    PAD = 2

    def try_pack(pw: int, ph: int):
        rects = {}
        x = y = shelf = 0
        for key in sorted(images, key=lambda k: -images[k].height):
            w, h = images[key].width + 2 * PAD, images[key].height + 2 * PAD
            if x + w > pw:
                x = 0
                y += shelf
                shelf = 0
            if y + h > ph:
                return None
            rects[key] = (x + PAD, y + PAD)
            x += w
            shelf = max(shelf, h)
        return rects

    rects = None
    PW = PH = 1024
    for PW, PH in [(1024, 1024), (2048, 1024), (2048, 1536), (2048, 2048), (4096, 2048), (4096, 4096)]:
        rects = try_pack(PW, PH)
        if rects is not None:
            break
    assert rects is not None, f"Atlas overflow for {name}"

    page = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
    for key, (rx, ry) in rects.items():
        page.paste(images[key], (rx, ry))

    P = np.array(page, dtype=np.float32)
    known = P[:, :, 3] > 0
    col = P[:, :, :3].copy()
    for _ in range(6):
        k = cv2.dilate(known.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        new = k & (~known)
        if not new.any():
            break
        s = cv2.blur(col * known[:, :, None], (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = k
    P[:, :, :3] = col

    out_dir.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.clip(P, 0, 255).astype(np.uint8), mode="RGBA").save(out_dir / f"{name}.png", optimize=True)

    lines = [f"{name}.png", f"size: {PW},{PH}", "format: RGBA8888", "filter: Linear,Linear", "repeat: none"]
    for key, (rx, ry) in rects.items():
        w, h = images[key].size
        lines += [key, "  rotate: false", f"  xy: {rx}, {ry}", f"  size: {w}, {h}", f"  orig: {w}, {h}", "  offset: 0, 0", "  index: -1"]
    atlas_text = "\n".join(lines) + "\n"
    (out_dir / f"{name}.atlas").write_text(atlas_text, encoding="utf-8", newline="\n")
    spatlas = {
        "source_path": f"res://STS2_Things/animations/monsters/{name}/{name}.atlas",
        "atlas_data": atlas_text,
        "normal_texture_prefix": "n",
        "specular_texture_prefix": "s",
    }
    (out_dir / f"{name}.spatlas").write_text(json.dumps(spatlas, indent=2), encoding="utf-8", newline="\n")
    return atlas_text, PW, PH


def write_skel_data_tres(name: str, out_dir: Path, default_mix: float = 0.14) -> None:
    tres = (
        '[gd_resource type="SpineSkeletonDataResource" load_steps=3 format=3]\n'
        f'[ext_resource type="SpineAtlasResource" path="res://STS2_Things/animations/monsters/{name}/{name}.spatlas" id="1"]\n'
        f'[ext_resource type="SpineSkeletonFileResource" path="res://STS2_Things/animations/monsters/{name}/{name}.spjson" id="2"]\n'
        '[resource]\n'
        'atlas_res = ExtResource("1")\n'
        'skeleton_file_res = ExtResource("2")\n'
        f'default_mix = {default_mix}\n'
    )
    (out_dir / f"{name}_skel_data.tres").write_text(tres, encoding="utf-8", newline="\n")


def build_grid_mesh(im: Image.Image, spacing: int = 16) -> tuple[np.ndarray, np.ndarray, list[int]]:
    al = np.array(im.convert("RGBA"))[:, :, 3]
    h, w = al.shape
    xs = sorted(set([int(v) for v in np.arange(0, w, spacing)] + [w]))
    ys = sorted(set([int(v) for v in np.arange(0, h, spacing)] + [h]))
    occ = cv2.dilate((al > 0).astype(np.uint8), np.ones((3, 3), np.uint8))
    vid = {}
    verts = []
    tris = []

    def V(i: int, j: int) -> int:
        if (i, j) not in vid:
            vid[(i, j)] = len(verts)
            verts.append((xs[i], ys[j]))
        return vid[(i, j)]

    for j in range(len(ys) - 1):
        for i in range(len(xs) - 1):
            if occ[ys[j]:ys[j + 1], xs[i]:xs[i + 1]].any():
                a, b, c, d = V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1)
                tris += [a, b, c, a, c, d]

    verts_arr = np.array(verts, dtype=np.float64)
    uvs = np.column_stack([verts_arr[:, 0] / w, verts_arr[:, 1] / h])
    return verts_arr, uvs, tris


def encode_weighted_vertices(
    world_spine_pts: np.ndarray,
    weights_per_vert: list[list[tuple[int, float]]],
    bone_setup_spine: dict[int, tuple[float, float]],
) -> list:
    out = []
    for p, wl in zip(world_spine_pts, weights_per_vert):
        out.append(len(wl))
        for b_idx, w in wl:
            bx, by = bone_setup_spine[b_idx]
            out += [b_idx, round(float(p[0] - bx), 2), round(float(p[1] - by), 2), round(float(w), 4)]
    return out


def reduce_keys(times: list[float], vals: list[tuple[float, ...]], eps: tuple[float, ...]) -> list[int]:
    keep = [0]
    i = 0
    n = len(times)
    while i < n - 1:
        j = i + 2
        while j < n:
            ok = True
            t0, t1 = times[i], times[j]
            for m in range(i + 1, j):
                u = (times[m] - t0) / (t1 - t0)
                for q in range(len(vals[0])):
                    if abs(vals[i][q] + (vals[j][q] - vals[i][q]) * u - vals[m][q]) > eps[q]:
                        ok = False
                        break
                if not ok:
                    break
            if not ok:
                break
            j += 1
        i = j - 1
        keep.append(i)
    return keep


class SpineBuilder:
    def __init__(self, name: str):
        self.name = name
        self.bones: list[dict] = []
        self.bone_idx: dict[str, int] = {}
        self.bone_world: dict[int, tuple[float, float]] = {}
        self.slots: list[dict] = []
        self.attachments: dict[str, dict] = {}
        self.events: dict[str, dict] = {}
        self.animations: dict[str, dict] = {}

    def add_bone(self, name: str, parent: str | None, wx: float, wy: float) -> int:
        idx = len(self.bones)
        if parent is None:
            lx, ly = wx, wy
        else:
            pidx = self.bone_idx[parent]
            px, py = self.bone_world[pidx]
            lx, ly = wx - px, wy - py
        b = {"name": name}
        if parent is not None:
            b["parent"] = parent
        if abs(lx) > 1e-3:
            b["x"] = round(float(lx), 2)
        if abs(ly) > 1e-3:
            b["y"] = round(float(ly), 2)
        self.bones.append(b)
        self.bone_idx[name] = idx
        self.bone_world[idx] = (float(wx), float(wy))
        return idx

    def add_slot_mesh(
        self,
        slot_name: str,
        region_name: str,
        default_bone: str,
        im: Image.Image,
        world_spine_pts: np.ndarray,
        uvs: np.ndarray,
        tris: list[int],
        weights_per_vert: list[list[tuple[int, float]]],
        color: str | None = None,
        blend: str | None = None,
    ) -> None:
        sl = {"name": slot_name, "bone": default_bone, "attachment": slot_name}
        if color is not None:
            sl["color"] = color
        if blend is not None:
            sl["blend"] = blend
        self.slots.append(sl)
        att = {
            "type": "mesh",
            "path": region_name,
            "uvs": [round(float(v), 5) for v in uvs.flatten()],
            "triangles": tris,
            "vertices": encode_weighted_vertices(world_spine_pts, weights_per_vert, self.bone_world),
            "hull": 0,
            "width": im.width,
            "height": im.height,
        }
        self.attachments[slot_name] = {slot_name: att}

    def add_rigid_sprite(
        self,
        slot_name: str,
        region_name: str,
        bone_name: str,
        im: Image.Image,
        anchor_px: tuple[float, float],
        scale: float = 1.0,
        color: str | None = None,
        blend: str | None = None,
        spacing: int = 44,
    ) -> None:
        """Mount a rigid 1-bone hand-painted sprite centered around `anchor_px` in image coords at `bone_name`'s world origin."""
        bidx = self.bone_idx[bone_name]
        bx, by = self.bone_world[bidx]
        v_px, uvs, tris = build_grid_mesh(im, spacing=spacing)
        wpts = np.column_stack([
            bx + (v_px[:, 0] - anchor_px[0]) * scale,
            by + (anchor_px[1] - v_px[:, 1]) * scale,
        ])
        self.add_slot_mesh(slot_name, region_name, bone_name, im, wpts, uvs, tris, [[(bidx, 1.0)] for _ in range(len(v_px))], color=color, blend=blend)

    def add_clipping_slot(
        self,
        slot_name: str,
        end_slot_name: str,
        default_bone: str,
        world_poly: np.ndarray,
        weights_per_vert: list[list[tuple[int, float]]],
    ) -> None:
        """Add a true Spine 4.2.0 hardware clipping polygon slot (weighted to deform with any bone set!)."""
        self.slots.append({"name": slot_name, "bone": default_bone, "attachment": slot_name})
        self.attachments[slot_name] = {
            slot_name: {
                "type": "clipping",
                "end": end_slot_name,
                "vertexCount": int(len(world_poly)),
                "vertices": encode_weighted_vertices(world_poly, weights_per_vert, self.bone_world),
            }
        }

    def add_event(self, event_name: str) -> None:
        self.events[event_name] = {}

    def sample_animation(
        self,
        anim_name: str,
        duration: float,
        loop: bool,
        eval_fn,
        events: list[tuple[float, str]] | None = None,
    ) -> None:
        n_steps = max(2, int(round(duration * FPS)))
        times = [round(i * duration / n_steps, 5) for i in range(n_steps + 1)]
        frames_bones = []
        frames_slots = []
        for i, t in enumerate(times):
            tt = 0.0 if (loop and i == n_steps) else t
            bp, sc = eval_fn(tt)
            frames_bones.append(bp)
            frames_slots.append(sc)

        all_bone_names = set()
        for bp in frames_bones:
            all_bone_names.update(bp.keys())
        all_slot_names = set()
        for sc in frames_slots:
            all_slot_names.update(sc.keys())

        bone_tls = {}
        for bname in sorted(all_bone_names):
            rots = [(bp.get(bname, (0.0, 0.0, 0.0, 1.0, 1.0))[0],) for bp in frames_bones]
            trs = [bp.get(bname, (0.0, 0.0, 0.0, 1.0, 1.0))[1:3] for bp in frames_bones]
            scs = [bp.get(bname, (0.0, 0.0, 0.0, 1.0, 1.0))[3:5] for bp in frames_bones]
            tl = {}
            if any(abs(v[0]) > 1e-3 for v in rots):
                ks = reduce_keys(times, rots, (0.08,))
                tl["rotate"] = [{"time": round(times[k], 4), "value": round(rots[k][0], 3)} for k in ks]
            if any(abs(v[0]) > 1e-3 or abs(v[1]) > 1e-3 for v in trs):
                ks = reduce_keys(times, trs, (0.08, 0.08))
                tl["translate"] = [{"time": round(times[k], 4), "x": round(trs[k][0], 2), "y": round(trs[k][1], 2)} for k in ks]
            if any(abs(v[0] - 1.0) > 1e-4 or abs(v[1] - 1.0) > 1e-4 for v in scs):
                ks = reduce_keys(times, scs, (0.001, 0.001))
                tl["scale"] = [{"time": round(times[k], 4), "x": round(scs[k][0], 4), "y": round(scs[k][1], 4)} for k in ks]
            if tl:
                for arr in tl.values():
                    if arr[0]["time"] == 0:
                        del arr[0]["time"]
                bone_tls[bname] = tl

        slot_tls = {}
        for sname in sorted(all_slot_names):
            cols = [sc.get(sname, (1.0, 1.0, 1.0, 1.0)) for sc in frames_slots]
            ks = reduce_keys(times, cols, (0.012, 0.012, 0.012, 0.012))
            arr = [{"time": round(times[k], 4), "color": hexcol(*cols[k])} for k in ks]
            if arr[0]["time"] == 0:
                del arr[0]["time"]
            slot_tls[sname] = {"rgba": arr}

        anim_obj: dict = {}
        if bone_tls:
            anim_obj["bones"] = bone_tls
        if slot_tls:
            anim_obj["slots"] = slot_tls
        if events:
            anim_obj["events"] = [{"time": round(et, 4), "name": en} for et, en in events]
            for _, en in events:
                self.add_event(en)
        self.animations[anim_name] = anim_obj

    def export(self, atlas_images: dict[str, Image.Image], out_dir: Path, default_mix: float = 0.14) -> None:
        _, pw, ph = pack_atlas(self.name, atlas_images, out_dir)
        spjson = {
            "skeleton": {
                "hash": f"sts2_things_{self.name}_v3",
                "spine": "4.2.0",
                "x": -960,
                "y": -540,
                "width": 1920,
                "height": 1080,
                "images": "./",
            },
            "bones": self.bones,
            "slots": self.slots,
            "skins": [{"name": "default", "attachments": self.attachments}],
            "animations": self.animations,
        }
        if self.events:
            spjson["events"] = self.events
        (out_dir / f"{self.name}.spjson").write_text(json.dumps(spjson, separators=(",", ":")), encoding="utf-8", newline="\n")
        write_skel_data_tres(self.name, out_dir, default_mix=default_mix)
        print(f"EXPORTED {self.name}: atlas={pw}x{ph}, bones={len(self.bones)}, slots={len(self.slots)}, anims={list(self.animations.keys())}")


# ==============================================================================
# Helper: 12-Bone Pure-Rotational Geodesic-Skinned FK Tentacle Chain
# ==============================================================================
def compute_chain_kinematics(joint_world: np.ndarray) -> tuple[list[float], list[float]]:
    """Compute unwrapped setup world angles (deg) and segment lengths for an FK tentacle chain."""
    n = len(joint_world) - 1
    angs: list[float] = []
    lens: list[float] = []
    for i in range(n):
        dx = float(joint_world[i + 1, 0] - joint_world[i, 0])
        dy = float(joint_world[i + 1, 1] - joint_world[i, 1])
        lens.append(math.hypot(dx, dy))
        a = math.degrees(math.atan2(dy, dx))
        if i > 0:
            while a - angs[-1] > 180.0:
                a -= 360.0
            while a - angs[-1] < -180.0:
                a += 360.0
        angs.append(a)
    return angs, lens


def solve_fk_world_angles(
    bone_names: list[str],
    rest_angs: list[float],
    target_angs: list[float],
) -> dict[str, tuple[float, float, float, float, float]]:
    """Convert exact desired world-space bone angles into exact Spine relative local delta rotations."""
    n = len(bone_names)
    cum = [target_angs[i] - rest_angs[i] for i in range(n)]
    out: dict[str, tuple[float, float, float, float, float]] = {}
    for i, bn in enumerate(bone_names):
        dr = cum[0] if i == 0 else (cum[i] - cum[i - 1])
        out[bn] = (dr, 0.0, 0.0, 1.0, 1.0)
    return out


def eval_fk_world_points(
    root_xy: tuple[float, float],
    seg_lens: list[float],
    world_angs: list[float],
) -> list[tuple[float, float]]:
    """Forward kinematics simulator returning exact Spine world (x, y) of every joint 0..n."""
    pts = [(float(root_xy[0]), float(root_xy[1]))]
    x, y = float(root_xy[0]), float(root_xy[1])
    for L, a in zip(seg_lens, world_angs):
        rad = math.radians(a)
        x += L * math.cos(rad)
        y += L * math.sin(rad)
        pts.append((x, y))
    return pts


def mirror_world_angles(angs: list[float]) -> list[float]:
    """Mirror Left-facing (+X slam) world angles cleanly across the vertical 90° axis for Right-mounted (-X slam) limbs."""
    out: list[float] = []
    for i, a in enumerate(angs):
        ma = 180.0 - a
        if i > 0:
            while ma - out[-1] > 180.0:
                ma -= 360.0
            while ma - out[-1] < -180.0:
                ma += 360.0
        out.append(ma)
    return out


def resample_angles(ctrl: list[float], n: int) -> list[float]:
    """Smoothly resample a control-angle list onto exactly `n` bones."""
    xs = np.linspace(0.0, 1.0, len(ctrl))
    tq = np.linspace(0.0, 1.0, n)
    return [float(v) for v in np.interp(tq, xs, ctrl)]


def blend_angles(a0: list[float], a1: list[float], w: float) -> list[float]:
    w = clamp(w, 0.0, 1.0)
    return [x + (y - x) * w for x, y in zip(a0, a1)]


def add_fk_tentacle_ex(
    sb: SpineBuilder,
    prefix: str,
    parent_bone: str,
    region_name: str,
    im: Image.Image,
    pts_px: np.ndarray,
    u_map: np.ndarray,
    root_world_xy: tuple[float, float],
    scale: float = 1.0,
    mirror_x: bool = False,
    base_tilt_deg: float = 0.0,
    spacing: int = 16,
    slot_color: str | None = None,
    glow_im: Image.Image | None = None,
    glow_region_name: str | None = None,
) -> tuple[list[str], list[float], list[float], list[int]]:
    n_bones = len(pts_px) - 1
    rx0, ry0 = pts_px[0, 0], pts_px[0, 1]
    cos_t = math.cos(math.radians(base_tilt_deg))
    sin_t = math.sin(math.radians(base_tilt_deg))

    def px_to_spine(px: np.ndarray) -> np.ndarray:
        dx = (px[:, 0] - rx0) * (-scale if mirror_x else scale)
        dy = (ry0 - px[:, 1]) * scale
        wx = root_world_xy[0] + dx * cos_t - dy * sin_t
        wy = root_world_xy[1] + dx * sin_t + dy * cos_t
        return np.column_stack([wx, wy])

    joint_world = px_to_spine(pts_px)
    rest_angs, seg_lens = compute_chain_kinematics(joint_world)
    bone_names = []
    b_indices = []
    prev = parent_bone
    for i in range(n_bones):
        bn = f"{prefix}_{i}"
        idx = sb.add_bone(bn, prev, joint_world[i, 0], joint_world[i, 1])
        bone_names.append(bn)
        b_indices.append(idx)
        prev = bn

    bind_mesh_to_fk_chain(
        sb, prefix, region_name, bone_names[0], b_indices, im, pts_px, u_map,
        root_world_xy=root_world_xy, scale=scale, mirror_x=mirror_x, base_tilt_deg=base_tilt_deg,
        spacing=spacing, slot_color=slot_color,
    )
    if glow_im is not None and glow_region_name is not None:
        bind_mesh_to_fk_chain(
            sb, f"{prefix}_glow", glow_region_name, bone_names[0], b_indices, glow_im, pts_px, u_map,
            root_world_xy=root_world_xy, scale=scale, mirror_x=mirror_x, base_tilt_deg=base_tilt_deg,
            spacing=spacing, slot_color="ffffff00", blend="additive",
        )
    return bone_names, rest_angs, seg_lens, b_indices


def bind_mesh_to_fk_chain(
    sb: SpineBuilder,
    slot_name: str,
    region_name: str,
    default_bone: str,
    b_indices: list[int],
    im: Image.Image,
    pts_px: np.ndarray,
    u_map: np.ndarray,
    *,
    root_world_xy: tuple[float, float],
    scale: float = 1.0,
    mirror_x: bool = False,
    base_tilt_deg: float = 0.0,
    spacing: int = 16,
    slot_color: str | None = None,
    blend: str | None = None,
) -> None:
    """Bind any sprite's anti-bleed geodesic u_map mesh cleanly onto an existing FK bone chain."""
    n_bones = len(b_indices)
    rx0, ry0 = pts_px[0, 0], pts_px[0, 1]
    cos_t = math.cos(math.radians(base_tilt_deg))
    sin_t = math.sin(math.radians(base_tilt_deg))

    verts_px, uvs, tris = build_grid_mesh(im, spacing=spacing)
    if mirror_x:
        tris = [tris[k + o] for k in range(0, len(tris), 3) for o in (0, 2, 1)]
    dx = (verts_px[:, 0] - rx0) * (-scale if mirror_x else scale)
    dy = (ry0 - verts_px[:, 1]) * scale
    world_pts = np.column_stack([
        root_world_xy[0] + dx * cos_t - dy * sin_t,
        root_world_xy[1] + dx * sin_t + dy * cos_t,
    ])

    H, W = u_map.shape
    weights_list = []
    sigma_u = 0.85
    for vx, vy in verts_px:
        ix = int(clamp(round(vx), 0, W - 1))
        iy = int(clamp(round(vy), 0, H - 1))
        u = float(u_map[iy, ix])
        dists = [(abs(u - k), k) for k in range(n_bones)]
        dists.sort()
        top = dists[:4]
        raw_w = [(b_indices[k], math.exp(-0.5 * (d / sigma_u) ** 2)) for d, k in top]
        raw_w = [(bi, w) for bi, w in raw_w if w > 0.015]
        s_w = sum(w for _, w in raw_w)
        weights_list.append([(bi, w / s_w) for bi, w in raw_w])

    sb.add_slot_mesh(slot_name, region_name, default_bone, im, world_pts, uvs, tris, weights_list, color=slot_color, blend=blend)


def add_fk_tentacle(
    sb: SpineBuilder,
    prefix: str,
    parent_bone: str,
    region_name: str,
    im: Image.Image,
    pts_px: np.ndarray,
    u_map: np.ndarray,
    root_world_xy: tuple[float, float],
    scale: float = 1.0,
    mirror_x: bool = False,
    base_tilt_deg: float = 0.0,
    spacing: int = 16,
    slot_color: str | None = None,
    glow_im: Image.Image | None = None,
    glow_region_name: str | None = None,
) -> list[str]:
    bone_names, _, _, _ = add_fk_tentacle_ex(
        sb, prefix, parent_bone, region_name, im, pts_px, u_map, root_world_xy,
        scale=scale, mirror_x=mirror_x, base_tilt_deg=base_tilt_deg, spacing=spacing,
        slot_color=slot_color, glow_im=glow_im, glow_region_name=glow_region_name,
    )
    return bone_names


def eval_fk_wave(
    bone_names: list[str],
    t: float,
    dur: float,
    cycles: float,
    phase: float,
    base_amp: float,
    tip_amp: float,
    wave_k: float = 5.0,
    bias_rot: float = 0.0,
    curl_tip: float = 0.0,
    root_ty: float = 0.0,
    sign: float = 1.0,
    harmonic2: float = 0.30,
    harmonic3: float = 0.11,
) -> dict[str, tuple[float, float, float, float, float]]:
    """Pure 100% arc-length-preserving multi-harmonic organic cephalopod FK traveling wave (ZERO scale stretching!)."""
    out = {}
    n = len(bone_names)
    omega = TAU * cycles / dur
    for i, bn in enumerate(bone_names):
        u = i / max(1, n - 1)
        amp = base_amp + (tip_amp - base_amp) * (u ** 1.15)
        w1 = amp * math.sin(omega * t - wave_k * u + phase)
        # 2nd & 3rd integer-cycle overtones keep the loop 100% seamless while adding predatory tip agility!
        w2 = (amp * harmonic2 * (0.22 + 0.78 * (u ** 1.45))) * math.sin(2.0 * omega * t - wave_k * 1.65 * u + phase * 1.7 + 0.85)
        w3 = (amp * harmonic3 * (u ** 2.0)) * math.sin(3.0 * omega * t - wave_k * 2.15 * u + phase * 2.3 + 1.4)
        r = sign * (w1 + w2 + w3 + bias_rot * (0.20 if i == 0 else (0.12 * (1.0 - 0.25 * u))) + curl_tip * (u ** 1.6))
        out[bn] = (r, 0.0, root_ty if i == 0 else 0.0, 1.0, 1.0)
    return out


# ==============================================================================
# Shared Water VFX & Breach VFX Loaders
# ==============================================================================
def load_dir_images(folder: Path) -> dict[str, Image.Image]:
    meta = json.loads((folder / "meta.json").read_text(encoding="utf-8"))
    return {k: Image.open(folder / f"{k}.png").convert("RGBA") for k in meta if not k.startswith("_")}


# ==============================================================================
# RIG 1: COLOSSAL HALF-SUBMERGED KRAKEN TORSO + 6 MISTY 12-BONE LAKE TENTACLES
# ==============================================================================
def build_boss_body_rig(bg_tall_im, bg_tall_pts, bg_tall_u, bg_outer_im, bg_outer_pts, bg_outer_u, water_imgs):
    pdir = RIG_ROOT / "parts_body"
    meta = json.loads((pdir / "meta.json").read_text(encoding="utf-8"))
    extra = meta["_extra"]
    CW, CH = extra["canvas_w"], extra["canvas_h"]
    head_im = Image.open(pdir / "head_mantle.png").convert("RGBA")
    pupil_l_im = Image.open(pdir / "pupil_l.png").convert("RGBA")
    pupil_r_im = Image.open(pdir / "pupil_r.png").convert("RGBA")
    eyelid_l_im = Image.open(pdir / "eyelid_l.png").convert("RGBA")
    eyelid_r_im = Image.open(pdir / "eyelid_r.png").convert("RGBA")
    ink_glow_im = Image.open(pdir / "ink_glow.png").convert("RGBA")

    sb = SpineBuilder("abyssal_inkmaw")
    sb.add_bone("root", None, 0.0, 0.0)
    sb.add_bone("camera_shake", "root", 0.0, 0.0)
    sb.add_bone("water_rig", "root", 0.0, 0.0)
    water_rig_bid = sb.bone_idx["water_rig"]

    meniscus_back_im = water_imgs["meniscus_back"]
    meniscus_front_im = water_imgs["meniscus_front"]
    ripple_im = water_imgs["ripple_ring"]
    ripple_back_im = water_imgs["ripple_back"]
    ripple_front_im = water_imgs["ripple_front"]
    ripple_b_im = water_imgs["ripple_b_ring"]
    ripple_b_back_im = water_imgs["ripple_b_back"]
    ripple_b_front_im = water_imgs["ripple_b_front"]
    water_glint_im = water_imgs["water_glint"]
    crest_l_im = water_imgs["crest_l"]
    crest_r_im = water_imgs["crest_r"]

    # 1) 6 Receded Atmospheric 12-Bone Aquatic Background Tentacles deep inside the WIDENED lake!
    # Far misty shore sits at y_spine = +181 (Y=355px); bridge curb sits at y_spine = -126 (Y=666px).
    # All 6 tentacles emerge cleanly at y_spine = -42..-66 (~230-255px BELOW far shore, 60-85px BEHIND bridge)!
    bg_specs = [
        ("bg_t_l3", "bg_tentacle_tall",  bg_tall_im,  bg_tall_pts,  bg_tall_u,  (-775.0, -44.0), 0.50, False, 24.0, "6b7b94ff", 1.0, 0.4, 4.4, 10.5,  1.0),
        ("bg_t_r3", "bg_tentacle_tall",  bg_tall_im,  bg_tall_pts,  bg_tall_u,  ( 775.0, -44.0), 0.50, True,  -24.0, "6b7b94ff", 1.0, 2.6, 4.4, 10.5, -1.0),
        ("bg_t_l2", "bg_tentacle_outer", bg_outer_im, bg_outer_pts, bg_outer_u, (-615.0, -68.0), 0.58, True,  18.0, "7c8da8ff", 1.0, 1.8, 4.8, 11.5,  1.0),
        ("bg_t_r2", "bg_tentacle_outer", bg_outer_im, bg_outer_pts, bg_outer_u, ( 615.0, -68.0), 0.58, False, -18.0, "7c8da8ff", 1.0, 4.3, 4.8, 11.5, -1.0),
        ("bg_t_l1", "bg_tentacle_tall",  bg_tall_im,  bg_tall_pts,  bg_tall_u,  (-305.0, -56.0), 0.64, False,  9.0, "8d9db8ff", 2.0, 0.9, 4.0, 10.0,  1.0),
        ("bg_t_r1", "bg_tentacle_tall",  bg_tall_im,  bg_tall_pts,  bg_tall_u,  ( 305.0, -56.0), 0.64, True,   -9.0, "8d9db8ff", 2.0, 3.5, 4.0, 10.0, -1.0),
    ]
    # Subtle misty deep-lake broken ripple rings beneath the 4 outer background tentacles (alternating A/B variants!)
    for pref, rx, ry, rsc, r_reg, r_im in (
        ("bg_rip_l3", -775.0, -44.0, 0.56, "ripple_ring", ripple_im),
        ("bg_rip_r3",  775.0, -44.0, 0.56, "ripple_b_ring", ripple_b_im),
        ("bg_rip_l2", -615.0, -68.0, 0.64, "ripple_b_ring", ripple_b_im),
        ("bg_rip_r2",  615.0, -68.0, 0.64, "ripple_ring", ripple_im),
    ):
        sb.add_bone(pref, "water_rig", rx, ry)
        sb.add_rigid_sprite(pref, r_reg, pref, r_im,
                            (r_im.width * 0.5, r_im.height * 0.5), scale=rsc, color="7da4b855", spacing=54)

    # Hardware waterline clip for all 6 background tentacles so submerged roots never float below their water rings!
    bg_clip_poly = np.array([
        [-1300.0, -44.0],
        [ -700.0, -44.0],
        [ -680.0, -68.0],
        [ -450.0, -68.0],
        [ -420.0, -56.0],
        [  420.0, -56.0],
        [  450.0, -68.0],
        [  680.0, -68.0],
        [  700.0, -44.0],
        [ 1300.0, -44.0],
        [ 1300.0,  950.0],
        [-1300.0,  950.0],
    ], dtype=np.float64)
    sb.add_clipping_slot("bg_water_clip", "bg_t_r1", "water_rig", bg_clip_poly, [[(water_rig_bid, 1.0)] for _ in range(len(bg_clip_poly))])

    bg_chains = {}
    for (pref, reg, im, pts, umap, rxy, sc, mir, tilt, col, *_rest) in bg_specs:
        # Plant root 18px deeper below waterline clip so roots always emerge cleanly out of the water!
        bg_chains[pref] = add_fk_tentacle(
            sb, pref, "water_rig", reg, im, pts, umap, (rxy[0], rxy[1] - 18.0),
            scale=sc, mirror_x=mir, base_tilt_deg=tilt, spacing=18, slot_color=col
        )

    # 2) Refined 3-Tier Waterline BEHIND Kraken: ambient lake glint ribbons + tight meniscus collar + A/B broken ripples!
    BODY_SCALE = 0.65
    WATERLINE_SPINE_Y = -62.0
    WATERLINE_PX_Y = float(extra["waterline_px_y"])  # ~790px (leaves ~212px of organic curled lower tentacles submerged!)

    sb.add_bone("boss_glint_l", "water_rig", -310.0, WATERLINE_SPINE_Y + 6.0)
    sb.add_bone("boss_glint_r", "water_rig",  310.0, WATERLINE_SPINE_Y + 6.0)
    sb.add_rigid_sprite("boss_glint_l", "water_glint", "boss_glint_l", water_glint_im,
                        (water_glint_im.width * 0.5, water_glint_im.height * 0.5), scale=0.86, color="ffffff55", spacing=54)
    sb.add_rigid_sprite("boss_glint_r", "water_glint", "boss_glint_r", water_glint_im,
                        (water_glint_im.width * 0.5, water_glint_im.height * 0.5), scale=0.86, color="ffffff55", spacing=54)

    sb.add_bone("boss_meniscus_back", "water_rig", 0.0, WATERLINE_SPINE_Y + 2.0)
    sb.add_rigid_sprite("boss_meniscus_back", "meniscus_back", "boss_meniscus_back", meniscus_back_im,
                        (meniscus_back_im.width * 0.5, meniscus_back_im.height * 0.5), scale=1.22, color="ffffffcc", spacing=54)

    for idx_r, r_reg, r_im in (
        (1, "ripple_back", ripple_back_im),
        (2, "ripple_b_back", ripple_b_back_im),
    ):
        sb.add_bone(f"boss_ripple_back_{idx_r}", "water_rig", 0.0, WATERLINE_SPINE_Y + 2.0)
        sb.add_rigid_sprite(f"boss_ripple_back_{idx_r}", r_reg, f"boss_ripple_back_{idx_r}", r_im,
                            (r_im.width * 0.5, r_im.height * 0.5), scale=1.34, color="ffffff88", spacing=54)

    # 3) COLOSSAL COMPLETE-BASE BIG-HEAD KRAKEN (14-Bone Articulation + Curved Hardware Waterline Clip!)
    # Completely un-chopped organic lower tentacles (~212px submerged below WATERLINE_PX_Y) clipped cleanly along
    # a gentle curved elliptical lake meniscus (`boss_water_clip`, ending on `head_mantle` BEFORE `eye_clip_l/r`!).
    def body_px_to_spine(px_xy: tuple[float, float]) -> tuple[float, float]:
        return (
            (px_xy[0] - CW * 0.5) * BODY_SCALE,
            WATERLINE_SPINE_Y + (WATERLINE_PX_Y - px_xy[1]) * BODY_SCALE,
        )

    sb.add_bone("torso_base", "water_rig", *body_px_to_spine((640, 820)))
    sb.add_bone("shoulder_l", "torso_base", *body_px_to_spine((475, 675)))
    sb.add_bone("shoulder_r", "torso_base", *body_px_to_spine((805, 675)))
    sb.add_bone("siphon_l", "shoulder_l", *body_px_to_spine((412, 705)))
    sb.add_bone("siphon_r", "shoulder_r", *body_px_to_spine((868, 705)))
    sb.add_bone("arm_l", "torso_base", *body_px_to_spine((395, 845)))
    sb.add_bone("arm_cl", "torso_base", *body_px_to_spine((555, 845)))
    sb.add_bone("arm_cr", "torso_base", *body_px_to_spine((725, 845)))
    sb.add_bone("arm_r", "torso_base", *body_px_to_spine((885, 845)))

    sb.add_bone("neck_pivot", "torso_base", *body_px_to_spine((640, 610)))
    sb.add_bone("head_pivot", "neck_pivot", *body_px_to_spine((640, 440)))
    sb.add_bone("crown_mid", "head_pivot", *body_px_to_spine((640, 260)))
    sb.add_bone("crown_tip", "crown_mid", *body_px_to_spine((640, 105)))
    # Wide regal octopus lateral mantle fins (2-bone traveling-wave undulation!)
    sb.add_bone("fin_l_base", "head_pivot", *body_px_to_spine((430, 290)))
    sb.add_bone("fin_l_tip", "fin_l_base", *body_px_to_spine((305, 248)))
    sb.add_bone("fin_r_base", "head_pivot", *body_px_to_spine((850, 290)))
    sb.add_bone("fin_r_tip", "fin_r_base", *body_px_to_spine((975, 248)))
    sb.add_bone("brow_l", "head_pivot", *body_px_to_spine((540, 390)))
    sb.add_bone("brow_r", "head_pivot", *body_px_to_spine((740, 390)))
    sb.add_bone("face_c", "head_pivot", *body_px_to_spine((640, 445)))
    beak_px = tuple(extra["beak_notch"])
    sb.add_bone("beak_maw", "head_pivot", *body_px_to_spine(beak_px))

    # Skinning anchors for head_mantle & ink_glow (lower curled roots follow torso_base & arms organically!)
    skin_bone_defs = [
        ("torso_base", 185.0),
        ("shoulder_l", 135.0),
        ("shoulder_r", 135.0),
        ("siphon_l", 75.0),
        ("siphon_r", 75.0),
        ("arm_l", 135.0),
        ("arm_cl", 125.0),
        ("arm_cr", 125.0),
        ("arm_r", 135.0),
        ("neck_pivot", 110.0),
        ("head_pivot", 105.0),
        ("crown_mid", 90.0),
        ("crown_tip", 72.0),
        ("fin_l_base", 54.0),
        ("fin_l_tip", 46.0),
        ("fin_r_base", 54.0),
        ("fin_r_tip", 46.0),
        ("brow_l", 62.0),
        ("brow_r", 62.0),
        ("beak_maw", 78.0),
    ]
    skin_b_ids = [sb.bone_idx[bn] for bn, _ in skin_bone_defs]
    skin_b_pts = np.array([sb.bone_world[bi] for bi in skin_b_ids], dtype=np.float64)
    skin_rads = np.array([rad for _, rad in skin_bone_defs], dtype=np.float64)

    def eval_body_weights(sp_pts: np.ndarray) -> list[list[tuple[int, float]]]:
        d2 = np.sum((sp_pts[:, None, :] - skin_b_pts[None, :, :]) ** 2, axis=2)
        energy = d2 / (2.0 * skin_rads[None, :] ** 2)
        wt = np.exp(-(energy - energy.min(axis=1, keepdims=True)))
        wt /= wt.sum(axis=1, keepdims=True)
        out = []
        for row in wt:
            pairs = sorted([(skin_b_ids[k], float(row[k])) for k in range(len(row)) if row[k] > 0.025], key=lambda x: -x[1])[:4]
            sw = sum(w for _, w in pairs)
            out.append([(bi, w / sw) for bi, w in pairs])
        return out

    # Curved elliptical lake-surface hardware clip polygon for `head_mantle` (ZERO flat cut even on +45px breach or -460px die!)
    meniscus_xs = np.linspace(-480.0, 480.0, 17)
    meniscus_pts = [
        [float(x), float(WATERLINE_SPINE_Y - 16.0 * math.cos(math.pi * 0.5 * (x / 480.0)))]
        for x in meniscus_xs
    ]
    boss_clip_poly = np.array(meniscus_pts + [[480.0, 920.0], [-480.0, 920.0]], dtype=np.float64)
    sb.add_clipping_slot("boss_water_clip", "head_mantle", "water_rig", boss_clip_poly,
                         [[(water_rig_bid, 1.0)] for _ in range(len(boss_clip_poly))])

    hv_px, huvs, htris = build_grid_mesh(head_im, spacing=20)
    hw_pts = np.column_stack([
        (hv_px[:, 0] - CW * 0.5) * BODY_SCALE,
        WATERLINE_SPINE_Y + (WATERLINE_PX_Y - hv_px[:, 1]) * BODY_SCALE,
    ])
    sb.add_slot_mesh("head_mantle", "head_mantle", "torso_base", head_im, hw_pts, huvs, htris, eval_body_weights(hw_pts))

    # 4) HARDWARE EYE-SOCKET CLIPPING POLYGONS + TRACKING SLIT PUPILS + 1:1 RIGID SLIDING EYELIDS!
    # Zero eyelid texture squashing — eyelids stay at (1.0, 1.0) scale and slide cleanly down behind brow!
    for tag, pim, lim in (("l", pupil_l_im, eyelid_l_im), ("r", pupil_r_im, eyelid_r_im)):
        clip_px = np.array(extra["eye_clips"][tag], dtype=np.float64)
        clip_sp = np.column_stack([
            (clip_px[:, 0] - CW * 0.5) * BODY_SCALE,
            WATERLINE_SPINE_Y + (WATERLINE_PX_Y - clip_px[:, 1]) * BODY_SCALE,
        ])
        # Clip polygon bound rigidly to face_c so it moves in 100% exact lockstep with pupils & lids!
        face_bid = sb.bone_idx["face_c"]
        sb.add_clipping_slot(f"eye_clip_{tag}", f"eyelid_{tag}", "face_c", clip_sp, [[(face_bid, 1.0)] for _ in range(len(clip_sp))])

        pm = meta[f"pupil_{tag}"]
        p_wx, p_wy = body_px_to_spine((pm["x"] + pm["w"] * 0.5, pm["y"] + pm["h"] * 0.5))
        p_bid = sb.add_bone(f"pupil_{tag}", "face_c", p_wx, p_wy)
        pv, puvs, ptris = build_grid_mesh(pim, spacing=10)
        pw_pts = np.column_stack([
            (pv[:, 0] + pm["x"] - CW * 0.5) * BODY_SCALE,
            WATERLINE_SPINE_Y + (WATERLINE_PX_Y - (pv[:, 1] + pm["y"])) * BODY_SCALE,
        ])
        sb.add_slot_mesh(f"pupil_{tag}", f"pupil_{tag}", f"pupil_{tag}", pim, pw_pts, puvs, ptris, [[(p_bid, 1.0)] for _ in range(len(pv))])

        lm = meta[f"eyelid_{tag}"]
        # Setup pose = CLOSED over socket; idle slides rigid lid UP (+48px) cleanly out of eye_clip!
        l_wx, l_wy = body_px_to_spine((lm["x"] + lm["w"] * 0.5, lm["y"] + lm["h"] * 0.5))
        l_bid = sb.add_bone(f"eyelid_{tag}", "face_c", l_wx, l_wy)
        lv, luvs, ltris = build_grid_mesh(lim, spacing=14)
        lw_pts = np.column_stack([
            (lv[:, 0] + lm["x"] - CW * 0.5) * BODY_SCALE,
            WATERLINE_SPINE_Y + (WATERLINE_PX_Y - (lv[:, 1] + lm["y"])) * BODY_SCALE,
        ])
        sb.add_slot_mesh(f"eyelid_{tag}", f"eyelid_{tag}", f"eyelid_{tag}", lim, lw_pts, luvs, ltris, [[(l_bid, 1.0)] for _ in range(len(lv))])

    # 5) Additive Bioluminescent Crown + Shoulder Spots & Symmetrical Eye Flare
    gm = meta["ink_glow"]
    gv, guvs, gtris = build_grid_mesh(ink_glow_im, spacing=26)
    gw_pts = np.column_stack([
        (gv[:, 0] + gm["x"] - CW * 0.5) * BODY_SCALE,
        WATERLINE_SPINE_Y + (WATERLINE_PX_Y - (gv[:, 1] + gm["y"])) * BODY_SCALE,
    ])
    sb.add_slot_mesh("ink_glow", "ink_glow", "torso_base", ink_glow_im, gw_pts, guvs, gtris, eval_body_weights(gw_pts),
                     color="ffffff00", blend="additive")

    # 6) Refined FRONT Meniscus Collar + Staggered A/B Broken Surface Ripples Across Kraken's Waterline + Surge Waves!
    sb.add_bone("boss_meniscus_front", "water_rig", 0.0, WATERLINE_SPINE_Y + 7.0)
    sb.add_rigid_sprite("boss_meniscus_front", "meniscus_front", "boss_meniscus_front", meniscus_front_im,
                        (meniscus_front_im.width * 0.5, meniscus_front_im.height * 0.5), scale=1.22, color="fffffff0", spacing=54)

    for idx_r, r_reg, r_im in (
        (1, "ripple_front", ripple_front_im),
        (2, "ripple_b_front", ripple_b_front_im),
    ):
        sb.add_bone(f"boss_ripple_front_{idx_r}", "water_rig", 0.0, WATERLINE_SPINE_Y + 7.0)
        sb.add_rigid_sprite(f"boss_ripple_front_{idx_r}", r_reg, f"boss_ripple_front_{idx_r}", r_im,
                            (r_im.width * 0.5, r_im.height * 0.5), scale=1.34, color="ffffffc4", spacing=54)

    sb.add_bone("boss_wave_l", "water_rig", -225.0, WATERLINE_SPINE_Y + 8.0)
    sb.add_bone("boss_wave_r", "water_rig",  225.0, WATERLINE_SPINE_Y + 8.0)
    sb.add_bone("boss_spray_l", "water_rig", -210.0, WATERLINE_SPINE_Y + 6.0)
    sb.add_bone("boss_spray_r", "water_rig",  210.0, WATERLINE_SPINE_Y + 6.0)
    sb.add_rigid_sprite("boss_wave_l", "crest_l", "boss_wave_l", crest_l_im, (crest_l_im.width * 0.55, crest_l_im.height * 0.88), scale=0.86, color="ffffff00")
    sb.add_rigid_sprite("boss_wave_r", "crest_r", "boss_wave_r", crest_r_im, (crest_r_im.width * 0.45, crest_r_im.height * 0.88), scale=0.86, color="ffffff00")
    spray_im = water_imgs["spray_plume"]
    sb.add_rigid_sprite("boss_spray_l", "spray_plume", "boss_spray_l", spray_im, (spray_im.width * 0.5, spray_im.height * 0.90), scale=1.08, color="ffffff00")
    sb.add_rigid_sprite("boss_spray_r", "spray_plume", "boss_spray_r", spray_im, (spray_im.width * 0.5, spray_im.height * 0.90), scale=1.08, color="ffffff00")
    for di in range(1, 5):
        sb.add_bone(f"boss_drop_{di}", "water_rig", -190.0 if (di % 2 == 1) else 190.0, WATERLINE_SPINE_Y + 24.0)
        dim = water_imgs[f"drop_{di}"]
        sb.add_rigid_sprite(f"boss_drop_{di}", f"drop_{di}", f"boss_drop_{di}", dim, (dim.width * 0.5, dim.height * 0.5), scale=1.02, color="ffffff00")

    # ==========================================================================
    # ANIMATIONS FOR COLOSSAL HALF-SUBMERGED KRAKEN (ZERO CHEAP STRETCHING!)
    # ==========================================================================
    def add_bg_waves(bp: dict, t: float, dur: float, amp_scale: float = 1.0,
                     extra_bias: float = 0.0, extra_curl: float = 0.0, lift_y: float = 0.0):
        for (pref, *_m, cycles, phase, b_amp, t_amp, sign) in bg_specs:
            bp.update(eval_fk_wave(
                bg_chains[pref], t, dur, cycles=cycles, phase=phase,
                base_amp=b_amp * amp_scale, tip_amp=t_amp * amp_scale,
                wave_k=5.0, bias_rot=extra_bias, curl_tip=extra_curl, root_ty=lift_y, sign=sign,
            ))

    def eval_boss_waterline(bp: dict, sc: dict, t: float, dur: float, surge: float = 0.0):
        th = TAU * t / dur
        # 1) Tight hugging waterline meniscus collar breathing gently with Kraken buoyancy!
        sc_m = 1.0 + 0.032 * math.sin(th - 0.3) + surge * 0.085
        m_dx = math.sin(th + 0.65) * 3.0
        bp["boss_meniscus_back"] = (0.0, m_dx, 0.0, sc_m, sc_m * 0.68)
        sc["boss_meniscus_back"] = (1, 1, 1, clamp(0.76 + surge * 0.18, 0.0, 0.96))
        bp["boss_meniscus_front"] = (0.0, m_dx, 0.0, sc_m, sc_m * 0.68)
        sc["boss_meniscus_front"] = (1, 1, 1, clamp(0.90 + surge * 0.08, 0.0, 0.98))

        # 2) Staggered A/B delicate broken lake ripple rings with ease-out-quad fluid wave deceleration!
        for idx_r, ph in ((1, 0.0), (2, 0.5)):
            u = ((t / dur) + ph) % 1.0
            u_wave = 1.0 - (1.0 - u) ** 1.65
            sc_r = 0.80 + u_wave * (0.42 + surge * 0.24)
            env = math.sin(math.pi * (u ** 0.78))
            a_back = env * clamp(0.56 + surge * 0.30, 0.0, 0.92)
            a_front = env * clamp(0.74 + surge * 0.20, 0.0, 0.96)
            wobble_x = math.sin(th + idx_r * 2.1) * 2.8
            bp[f"boss_ripple_back_{idx_r}"] = (0.0, wobble_x, 0.0, sc_r, sc_r * 0.60)
            sc[f"boss_ripple_back_{idx_r}"] = (1, 1, 1, a_back)
            bp[f"boss_ripple_front_{idx_r}"] = (0.0, wobble_x, 0.0, sc_r, sc_r * 0.60)
            sc[f"boss_ripple_front_{idx_r}"] = (1, 1, 1, a_front)

        # 3) Calm flanking horizontal cavern-water glint ribbons gently drifting behind Kraken flanks!
        gl_u = math.sin(th + 0.5)
        gr_u = math.sin(th + 2.9)
        bp["boss_glint_l"] = (0.0, -14.0 * gl_u, 1.5 * math.sin(2.0 * th), 0.96 + 0.08 * gl_u, 0.76)
        bp["boss_glint_r"] = (0.0,  14.0 * gr_u, 1.5 * math.cos(2.0 * th), -(0.96 + 0.08 * gr_u), 0.76)
        sc["boss_glint_l"] = (0.86, 0.96, 1.0, clamp(0.32 + 0.12 * gl_u + surge * 0.14, 0.0, 0.58))
        sc["boss_glint_r"] = (0.86, 0.96, 1.0, clamp(0.32 + 0.12 * gr_u + surge * 0.14, 0.0, 0.58))

        # 4) Ultra-subtle misty atmospheric ripples on the 4 distant background tentacles
        for r_idx, pref in enumerate(("bg_rip_l3", "bg_rip_r3", "bg_rip_l2", "bg_rip_r2")):
            u = ((t / dur) + r_idx * 0.25) % 1.0
            rs = 0.88 + 0.18 * math.sin(TAU * u) + surge * 0.14
            flip_s = -1.0 if (r_idx % 2 == 1) else 1.0
            bp[pref] = (0.0, 0.0, 0.0, flip_s * rs, rs * 0.56)
            sc[pref] = (0.58, 0.78, 0.88, clamp(0.32 + 0.12 * math.sin(TAU * u) + surge * 0.18, 0.0, 0.62))
        # Dramatic curling ocean waves ONLY surge up during actual water-breach / phase2 / summon bursts (calm in idle!)
        w_l = math.sin(TAU * t / dur)
        w_r = math.sin(TAU * t / dur + 1.8)
        bp["boss_wave_l"] = (w_l * 6.0 + surge * 20.0, -surge * 55.0, -45.0 + surge * 65.0, 1.0, 1.0)
        bp["boss_wave_r"] = (-w_r * 6.0 - surge * 20.0, surge * 55.0, -45.0 + surge * 65.0, 1.0, 1.0)
        wave_a = clamp(smoothstep(seg(surge, 0.08, 0.45)) * 0.95, 0.0, 0.96)
        sc["boss_wave_l"] = (1, 1, 1, wave_a)
        sc["boss_wave_r"] = (1, 1, 1, wave_a)
        # Keep flanking spray plumes & ballistic droplets hidden unless driven by tidal_crash / phase2_surge!
        for spn in ("boss_spray_l", "boss_spray_r"):
            if spn not in sc:
                sc[spn] = (1, 1, 1, 0.0)
        for di in range(1, 5):
            if f"boss_drop_{di}" not in sc:
                sc[f"boss_drop_{di}"] = (1, 1, 1, 0.0)

    def eval_rigid_lids(bp: dict, close_01: float):
        # 0.0 = fully open (rigid lid slid +48px UP cleanly outside larger eye_clip); 1.0 = closed! ZERO scale stretch!
        ty = (1.0 - clamp(close_01, 0.0, 1.0)) * 48.0
        bp["eyelid_l"] = (0.0, 0.0, ty, 1.0, 1.0)
        bp["eyelid_r"] = (0.0, 0.0, ty, 1.0, 1.0)

    # --- 1. idle_loop (4.0s monumental breathing with bottom-to-top spine phase cascade, dual-harmonic fins & saccadic eyes!) ---
    def anim_idle(t: float):
        dur = 4.0
        th = TAU * t / dur
        bp, sc = {}, {"ink_glow": (1, 1, 1, 0.14 + 0.08 * math.sin(th - 0.5))}

        # True bottom-to-top aquatic buoyancy & breathing phase cascade!
        b_torso = math.sin(th)
        b_shldr = math.sin(th - 0.32)
        b_neck = math.sin(th - 0.68)
        b_head = math.sin(th - 1.05)
        b_crown = math.sin(th - 1.48)
        b_tip = math.sin(th - 1.92)
        sway_base = math.sin(th + 0.65)
        sway_head = math.sin(th + 0.15)
        sway_crown = math.sin(th - 0.45)

        # Dual-harmonic squid lateral mantle fins + asymmetric water-jet siphon puff!
        fin1 = math.sin(th - 0.45) + 0.28 * math.sin(2.0 * th - 0.90)
        fin2 = math.sin(th - 1.38) + 0.36 * math.sin(2.0 * th - 2.15)
        siph = (0.5 + 0.5 * math.sin(2.0 * th - 0.4)) ** 2.2 - 0.32

        # Submerged heavy buoyancy roll on torso_base -> ripples upward through shoulders, neck, head & crown!
        bp["torso_base"] = (sway_base * 0.85, sway_base * 3.5, b_torso * 5.0, 1.0, 1.0)
        bp["shoulder_l"] = (b_shldr * 2.8, -b_shldr * 5.5, b_shldr * 6.5, 1.0, 1.0)
        bp["shoulder_r"] = (-b_shldr * 2.8, b_shldr * 5.5, b_shldr * 6.5, 1.0, 1.0)
        bp["siphon_l"] = (siph * 7.5, -siph * 5.0, siph * 3.5, 1.0, 1.0)
        bp["siphon_r"] = (-siph * 7.5, siph * 5.0, siph * 3.5, 1.0, 1.0)
        bp["arm_cl"] = (sway_base * 2.2 + b_shldr * 1.2, sway_base * 4.0, b_shldr * 5.5, 1.0, 1.0)
        bp["arm_cr"] = (-sway_base * 2.2 - b_shldr * 1.2, -sway_base * 4.0, b_shldr * 5.5, 1.0, 1.0)

        bp["neck_pivot"] = (sway_head * 2.2, sway_head * 4.5, b_neck * 8.5, 1.0, 1.0)
        bp["head_pivot"] = (-sway_head * 1.8 + math.sin(2.0 * th) * 0.6, sway_head * 2.0, b_head * 5.5, 1.0, 1.0)
        bp["crown_mid"] = (sway_crown * 2.2, 0.0, b_crown * 5.5, 1.0, 1.0)
        bp["crown_tip"] = (sway_crown * 3.2, 0.0, b_tip * 4.5, 1.0, 1.0)
        # Dual-harmonic traveling-wave squid lateral mantle fins!
        bp["fin_l_base"] = (fin1 * 9.0, 0.0, b_head * 2.0, 1.0, 1.0)
        bp["fin_l_tip"] = (fin2 * 13.5, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (-fin1 * 9.0, 0.0, b_head * 2.0, 1.0, 1.0)
        bp["fin_r_tip"] = (-fin2 * 13.5, 0.0, 0.0, 1.0, 1.0)
        bp["brow_l"] = (b_head * 2.4 + math.sin(2.0 * th) * 0.7, 0.0, -b_head * 2.0, 1.0, 1.0)
        bp["brow_r"] = (-b_head * 2.4 - math.sin(2.0 * th) * 0.7, 0.0, -b_head * 2.0, 1.0, 1.0)

        # Predatory saccadic slit gaze (holds left -> darts right -> returns center) + crisp asymmetric blink!
        gx = eval_scalar_keyframes([
            (0.0, 0.0, "smooth"),
            (0.85, -4.6, "out_cubic"),
            (1.65, -4.4, "smooth"),
            (2.05, 4.8, "out_cubic"),
            (3.05, 4.4, "smooth"),
            (3.65, 0.0, "smoother"),
            (4.0, 0.0, "smooth"),
        ], t)
        gy = -0.6 + b_head * 1.4
        bp["pupil_l"] = (0.0, gx, gy, 1.0, 1.0)
        bp["pupil_r"] = (0.0, gx, gy, 1.0, 1.0)
        # Crisp fast-shut (0.09s), smooth-open (0.24s) organic blink!
        blink = smoothstep(seg(t, 2.36, 2.45)) * (1.0 - smootherstep(seg(t, 2.50, 2.74)))
        eval_rigid_lids(bp, blink)

        eval_boss_waterline(bp, sc, t, dur, surge=0.0)
        add_bg_waves(bp, t, dur, amp_scale=1.08)
        return bp, sc

    sb.sample_animation("idle_loop", 4.0, True, anim_idle)

    # --- 2. phase2_idle_loop (2.6s faster aggressive breathing cascade, pulsing glow, rapid dual-harmonic fins!) ---
    def anim_phase2_idle(t: float):
        dur = 2.6
        th = TAU * t / dur
        bp, sc = {}, {"ink_glow": (1, 1, 1, 0.65 + 0.28 * math.sin(th - 0.4))}
        b_torso = math.sin(th)
        b_shldr = math.sin(th - 0.35)
        b_neck = math.sin(th - 0.72)
        b_head = math.sin(th - 1.08)
        b_crown = math.sin(th - 1.52)
        sway = math.sin(th + 0.75)
        sway_lag = math.sin(th + 0.10)
        fin1 = math.sin(th - 0.40) + 0.32 * math.sin(2.0 * th - 0.85)
        fin2 = math.sin(th - 1.28) + 0.38 * math.sin(2.0 * th - 2.05)
        siph = (0.5 + 0.5 * math.sin(2.0 * th)) ** 2.0 - 0.30

        bp["torso_base"] = (sway * 1.3, sway * 5.0, 4.0 + b_torso * 7.5, 1.0, 1.0)
        bp["shoulder_l"] = (b_shldr * 4.2, -b_shldr * 8.0, 5.0 + b_shldr * 9.5, 1.0, 1.0)
        bp["shoulder_r"] = (-b_shldr * 4.2, b_shldr * 8.0, 5.0 + b_shldr * 9.5, 1.0, 1.0)
        bp["siphon_l"] = (siph * 11.0, -5.0, 4.0, 1.0, 1.0)
        bp["siphon_r"] = (-siph * 11.0, 5.0, 4.0, 1.0, 1.0)
        bp["arm_cl"] = (sway * 3.0, sway * 5.5, 4.0 + b_shldr * 7.0, 1.0, 1.0)
        bp["arm_cr"] = (-sway * 3.0, -sway * 5.5, 4.0 + b_shldr * 7.0, 1.0, 1.0)
        bp["neck_pivot"] = (sway_lag * 3.2, sway_lag * 7.5, 10.0 + b_neck * 12.0, 1.0, 1.0)
        bp["head_pivot"] = (-sway_lag * 2.4, sway_lag * 2.5, 5.0 + b_head * 6.5, 1.0, 1.0)
        bp["crown_mid"] = (sway_lag * 3.0, 0.0, b_crown * 6.5, 1.0, 1.0)
        bp["crown_tip"] = (sway_lag * 4.2, 0.0, math.sin(th - 1.95) * 5.5, 1.0, 1.0)
        bp["fin_l_base"] = (6.0 + fin1 * 13.5, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (fin2 * 18.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (-6.0 - fin1 * 13.5, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (-fin2 * 18.0, 0.0, 0.0, 1.0, 1.0)
        bp["brow_l"] = (5.5 + b_head * 2.8, 0.0, -3.5, 1.0, 1.0)
        bp["brow_r"] = (-5.5 - b_head * 2.8, 0.0, -3.5, 1.0, 1.0)
        bp["pupil_l"] = (0.0, math.sin(th) * 4.0, -1.6 + b_head * 0.9, 1.0, 1.0)
        bp["pupil_r"] = (0.0, math.sin(th) * 4.0, -1.6 + b_head * 0.9, 1.0, 1.0)
        eval_rigid_lids(bp, 0.28 + 0.06 * b_head)

        eval_boss_waterline(bp, sc, t, dur, surge=0.35)
        add_bg_waves(bp, t, dur, amp_scale=1.42, extra_bias=6.0, lift_y=10.0)
        return bp, sc

    sb.sample_animation("phase2_idle_loop", 2.6, True, anim_phase2_idle)

    # --- 3. ink_spray (2.05s V4 cinematic pressurized hydro-ink sweep: deep compression inhale -> violent kickback -> torqued L->R sweep -> upward flick cutoff -> panting spring settle!) ---
    def anim_ink_spray(t: float):
        dur = 2.05
        bp, sc = {}, {}
        # Stage 1 (0.00..0.52): Deep muscular inhale crouch aiming hard LEFT!
        inhale = bump(t, 0.0, 0.44, 0.56)
        intake_pulse = inhale * (0.62 + 0.38 * math.sin(TAU * 5.0 * t))
        # Stage 2 (0.52..0.66): Explosive pressurized ignition kickback!
        kickback = bump(t, 0.52, 0.57, 0.78)
        kick_lag = bump(t - 0.065, 0.52, 0.57, 0.80)
        # Stage 3 (0.54..1.54): Continuous relentless L->R sweep with torqued surge through center player (t=1.06s)!
        firing = hold(t, 0.52, 0.56, 1.48, 1.62)
        # Slight muscular acceleration across the center bridge deck (`t=1.06s` stays dead 0.50 -> exact 0° center hit!)
        u_raw = seg(t, 0.60, 1.52)
        sweep_u = clamp(smootherstep(u_raw) + 0.045 * math.sin(TAU * u_raw), 0.0, 1.0)
        aim_pre = -ease_out_cubic(seg(t, 0.06, 0.46))
        aim = (-1.0 + 2.0 * sweep_u) if t >= 0.56 else aim_pre
        # Stage 4 (1.50..2.05): Sharp upward mouth-snap cutoff flick + heavy panting spring settle!
        cutoff_flick = bump(t, 1.48, 1.58, 1.82)
        recover = smootherstep(seg(t, 1.54, 2.02))
        aim_final = aim * (1.0 - recover)
        t_lag = max(0.0, t - 0.075)
        u_lag = seg(t_lag, 0.60, 1.52)
        sw_lag = clamp(smootherstep(u_lag) + 0.045 * math.sin(TAU * u_lag), 0.0, 1.0)
        aim_lag = ((-1.0 + 2.0 * sw_lag) if t_lag >= 0.56 else -ease_out_cubic(seg(t_lag, 0.06, 0.46))) * (1.0 - smootherstep(seg(t_lag, 1.54, 2.02)))

        impact_jolt = bump(t, 1.02, 1.06, 1.24)
        tremor = (firing + impact_jolt * 1.05) * math.sin(TAU * 16.5 * t)
        sigh = spring_osc(t - 1.54, 5.2, 2.6) * (1.0 - smoothstep(seg(t, 1.92, 2.05)))

        bp["camera_shake"] = (0.0, tremor * (11.0 + impact_jolt * 11.0), tremor * (8.0 + impact_jolt * 9.0), 1.0, 1.0)
        # Whole torso plunges deeply into the lake on inhale, surges powerfully upward on ignition & drives through the sweep!
        bp["torso_base"] = (-aim_final * 3.2 + sigh * 1.6, aim_final * 10.0,
                            -inhale * 36.0 + kickback * 32.0 + firing * 12.0 - impact_jolt * 12.0 + cutoff_flick * 14.0 + sigh * 9.0, 1.0, 1.0)
        bp["shoulder_l"] = (inhale * 12.0 - kickback * 8.0 - firing * 5.0, inhale * 18.0 - firing * 14.0,
                            inhale * 24.0 + kickback * 16.0 + firing * 12.0 + sigh * 6.0, 1.0, 1.0)
        bp["shoulder_r"] = (-inhale * 12.0 + kickback * 8.0 + firing * 5.0, -inhale * 18.0 + firing * 14.0,
                            inhale * 24.0 + kickback * 16.0 + firing * 12.0 + sigh * 6.0, 1.0, 1.0)
        bp["siphon_l"] = (intake_pulse * 22.0 - firing * 14.0, intake_pulse * 16.0, intake_pulse * 10.0, 1.0, 1.0)
        bp["siphon_r"] = (-intake_pulse * 22.0 + firing * 14.0, -intake_pulse * 16.0, intake_pulse * 10.0, 1.0, 1.0)
        bp["neck_pivot"] = (-aim_final * 8.0 + tremor * 1.6, aim_final * 24.0,
                            inhale * 28.0 + kickback * 26.0 + firing * 10.0 - impact_jolt * 8.0 + cutoff_flick * 18.0 - sigh * 6.0, 1.0, 1.0)
        bp["head_pivot"] = (-aim_final * 9.5, aim_final * 12.0,
                            inhale * 16.0 + kickback * 20.0 - firing * 10.0 + cutoff_flick * 14.0, 1.0, 1.0)
        bp["crown_mid"] = (-aim_lag * 7.5 + sigh * 2.8, 0.0, inhale * 14.0 + kick_lag * 18.0 + cutoff_flick * 8.0, 1.0, 1.0)
        bp["crown_tip"] = (-aim_lag * 10.5 + sigh * 4.2, 0.0, inhale * 12.0 + kick_lag * 16.0 + cutoff_flick * 10.0, 1.0, 1.0)
        # Lateral mantle fins compress tightly during charge -> BLAST open backward on ignition -> ripple wildly through sweep!
        bp["fin_l_base"] = (-inhale * 14.0 + kick_lag * 32.0 + firing * 18.0 + tremor * 8.0 - sigh * 9.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (-inhale * 18.0 + kick_lag * 36.0 - firing * 16.0 - tremor * 9.5 - sigh * 13.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (inhale * 14.0 - kick_lag * 32.0 - firing * 18.0 - tremor * 8.0 + sigh * 9.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (inhale * 18.0 - kick_lag * 36.0 + firing * 16.0 + tremor * 9.5 + sigh * 13.0, 0.0, 0.0, 1.0, 1.0)
        bp["brow_l"] = (inhale * 11.0 + firing * 13.0, 0.0, -firing * 7.0, 1.0, 1.0)
        bp["brow_r"] = (-inhale * 11.0 - firing * 13.0, 0.0, -firing * 7.0, 1.0, 1.0)
        bp["pupil_l"] = (0.0, aim_final * 7.6, -3.5 * firing, 1.0, 1.0)
        bp["pupil_r"] = (0.0, aim_final * 7.6, -3.5 * firing, 1.0, 1.0)
        eval_rigid_lids(bp, clamp(inhale * 0.42 + firing * 0.46, 0.0, 0.58))

        sc["ink_glow"] = (1, 1, 1, clamp(0.18 + intake_pulse * 0.82 + firing * 0.88 + impact_jolt * 0.2, 0.0, 1.0))
        eval_boss_waterline(bp, sc, t, dur, surge=clamp(kickback * 1.1 + firing * 0.82, 0.0, 1.15))
        add_bg_waves(bp, t, dur, amp_scale=1.1 + inhale * 0.5 + firing * 1.05,
                     extra_bias=-inhale * 32.0 + kickback * 38.0 + firing * 34.0,
                     extra_curl=inhale * 14.0 - firing * 16.0, lift_y=-inhale * 18.0 + kickback * 26.0 + firing * 26.0)
        return bp, sc

    sb.sample_animation("ink_spray", 2.05, False, anim_ink_spray, events=[(0.56, "ink_fire_start"), (1.06, "ink_hit_player")])

    # --- 3b. tidal_crash【暗潮压境】 (1.65s deep watery crouch -> cascaded headbutt crash -> buoyant spring rebound!) ---
    def anim_tidal_crash(t: float):
        dur = 1.65
        bp, sc = {}, {}
        coil = bump(t, 0.0, 0.46, 0.64)
        lunge = bump(t, 0.54, 0.78, 1.28)
        lunge_lag = bump(t - 0.065, 0.54, 0.78, 1.32)
        impact = bump(t, 0.74, 0.80, 1.12)
        tremor = spring_osc(t - 0.78, 6.2, 12.5)
        bob = spring_osc(t - 0.82, 4.2, 2.2) * (1.0 - smoothstep(seg(t, 1.48, 1.65)))

        bp["camera_shake"] = (0.0, tremor * 22.0, tremor * 18.0, 1.0, 1.0)
        bp["torso_base"] = (tremor * 2.5, 0.0, -coil * 58.0 + lunge * 42.0 - impact * 26.0 + bob * 14.0, 1.0, 1.0)
        bp["shoulder_l"] = (-coil * 8.0 + lunge * 13.0, coil * 16.0 - lunge * 24.0, -coil * 18.0 + lunge * 30.0 + bob * 8.0, 1.0, 1.0)
        bp["shoulder_r"] = (coil * 8.0 - lunge * 13.0, -coil * 16.0 + lunge * 24.0, -coil * 18.0 + lunge * 30.0 + bob * 8.0, 1.0, 1.0)
        bp["siphon_l"] = (coil * 16.0 - lunge * 14.0, 0.0, lunge * 12.0, 1.0, 1.0)
        bp["siphon_r"] = (-coil * 16.0 + lunge * 14.0, 0.0, lunge * 12.0, 1.0, 1.0)
        bp["neck_pivot"] = (tremor * 2.2, 0.0, coil * 22.0 - lunge * 38.0 - bob * 9.0, 1.0, 1.0)
        bp["head_pivot"] = (tremor * 1.8, 0.0, coil * 18.0 - lunge_lag * 32.0 - bob * 7.0, 1.0, 1.0)
        bp["crown_mid"] = (0.0, 0.0, coil * 14.0 - lunge_lag * 18.0 + bob * 6.0, 1.0, 1.0)
        bp["crown_tip"] = (0.0, 0.0, coil * 12.0 - lunge_lag * 16.0 + bob * 8.0, 1.0, 1.0)
        # Lateral fins flare high on coil, then WHIP downward 0.065s after headbutt slapping the lake!
        bp["fin_l_base"] = (coil * 28.0 - lunge_lag * 26.0 + tremor * 8.0 - bob * 10.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (coil * 32.0 - lunge_lag * 34.0 - bob * 14.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (-coil * 28.0 + lunge_lag * 26.0 - tremor * 8.0 + bob * 10.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (-coil * 32.0 + lunge_lag * 34.0 + bob * 14.0, 0.0, 0.0, 1.0, 1.0)
        bp["brow_l"] = (coil * 8.0 + lunge * 13.0, 0.0, -lunge * 7.5, 1.0, 1.0)
        bp["brow_r"] = (-coil * 8.0 - lunge * 13.0, 0.0, -lunge * 7.5, 1.0, 1.0)
        bp["pupil_l"] = (0.0, 0.0, coil * 2.2 - lunge * 3.8, 1.0, 1.0)
        bp["pupil_r"] = (0.0, 0.0, coil * 2.2 - lunge * 3.8, 1.0, 1.0)
        eval_rigid_lids(bp, clamp(coil * 0.35 + lunge * 0.45, 0.0, 0.52))
        sc["ink_glow"] = (1, 1, 1, clamp(0.18 + coil * 0.55 + impact * 0.85, 0.0, 0.96))

        plume = bump(t, 0.72, 0.86, 1.38)
        bp["boss_spray_l"] = (18.0 + tremor * 3.0, -plume * 30.0, plume * 66.0, 1.0, 1.0)
        bp["boss_spray_r"] = (-18.0 - tremor * 3.0, plume * 30.0, plume * 66.0, 1.0, 1.0)
        sc["boss_spray_l"] = (1, 1, 1, plume * 0.94)
        sc["boss_spray_r"] = (1, 1, 1, plume * 0.94)
        b_drops = [
            (0.75, 0.70, -180.0, 520.0),
            (0.77, 0.74,  195.0, 550.0),
            (0.79, 0.66,  -85.0, 610.0),
            (0.81, 0.68,   95.0, 580.0),
        ]
        for idx_d, (tl, lf, vx, vy) in enumerate(b_drops, 1):
            rd, dx, dy, da = eval_parabolic_arc(t, tl, lf, vx, vy, g=1220.0)
            bp[f"boss_drop_{idx_d}"] = (rd, dx, dy, 1.0, 1.0)
            sc[f"boss_drop_{idx_d}"] = (1, 1, 1, da)

        eval_boss_waterline(bp, sc, t, dur, surge=clamp(lunge * 1.15, 0.0, 1.15))
        add_bg_waves(bp, t, dur, amp_scale=1.15 + lunge * 1.15,
                     extra_bias=-coil * 40.0 + lunge * 52.0, extra_curl=coil * 20.0 - lunge * 24.0,
                     lift_y=-coil * 34.0 + lunge * 52.0)
        return bp, sc

    sb.sample_animation("tidal_crash", 1.65, False, anim_tidal_crash, events=[(0.78, "tidal_hit")])

    # --- 4. summon_tentacle (1.60s commanding abyssal beckon: deep crouch -> towering mantle flare & 6-arm skyward surge!) ---
    def anim_summon(t: float):
        dur = 1.60
        bp, sc = {}, {}
        dip = bump(t, 0.0, 0.42, 0.76)
        surge = bump(t, 0.46, 0.74, 1.38)
        surge_lag = bump(t - 0.07, 0.46, 0.74, 1.42)
        bob = spring_osc(t - 0.74, 4.5, 2.5) * (1.0 - smoothstep(seg(t, 1.44, 1.60)))
        bp["torso_base"] = (0.0, 0.0, -dip * 38.0 + surge * 26.0 + bob * 10.0, 1.0, 1.0)
        bp["neck_pivot"] = (0.0, 0.0, -dip * 22.0 + surge * 34.0 + bob * 6.0, 1.0, 1.0)
        bp["head_pivot"] = (0.0, 0.0, -dip * 12.0 + surge_lag * 22.0, 1.0, 1.0)
        bp["crown_mid"] = (0.0, 0.0, surge_lag * 16.0, 1.0, 1.0)
        bp["crown_tip"] = (0.0, 0.0, surge_lag * 14.0, 1.0, 1.0)
        bp["shoulder_l"] = (-dip * 7.0 + surge * 10.0, -surge * 18.0, -dip * 18.0 + surge * 22.0, 1.0, 1.0)
        bp["shoulder_r"] = (dip * 7.0 - surge * 10.0, surge * 18.0, -dip * 18.0 + surge * 22.0, 1.0, 1.0)
        bp["fin_l_base"] = (-dip * 12.0 + surge_lag * 26.0 + bob * 8.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (-dip * 14.0 + surge_lag * 30.0 + bob * 11.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (dip * 12.0 - surge_lag * 26.0 - bob * 8.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (dip * 14.0 - surge_lag * 30.0 - bob * 11.0, 0.0, 0.0, 1.0, 1.0)
        bp["brow_l"] = (surge * 8.0, 0.0, -surge * 3.0, 1.0, 1.0)
        bp["brow_r"] = (-surge * 8.0, 0.0, -surge * 3.0, 1.0, 1.0)
        eval_rigid_lids(bp, dip * 0.55)
        sc["ink_glow"] = (1, 1, 1, clamp(0.16 + surge * 0.68, 0.0, 0.92))
        eval_boss_waterline(bp, sc, t, dur, surge=surge * 1.05)
        add_bg_waves(bp, t, dur, amp_scale=1.15 + surge * 1.05, extra_bias=-dip * 20.0 + surge * 44.0,
                     extra_curl=-surge * 14.0, lift_y=-dip * 32.0 + surge * 54.0)
        return bp, sc

    sb.sample_animation("summon_tentacle", 1.60, False, anim_summon, events=[(0.70, "summon_emerge")])

    # --- 5. phase2_surge (2.10s dramatic underwater sink -> towering breach roar + cascading fin whip & spring settle!) ---
    def anim_phase2_surge(t: float):
        dur = 2.10
        bp, sc = {}, {}
        dive = bump(t, 0.0, 0.50, 0.80)
        breach = bump(t, 0.64, 0.98, 1.92)
        breach_lag = bump(t - 0.08, 0.64, 0.98, 1.96)
        tremor = spring_osc(t - 0.90, 4.2, 10.5)
        bob = spring_osc(t - 0.98, 3.6, 2.0) * (1.0 - smoothstep(seg(t, 1.92, 2.10)))

        bp["camera_shake"] = (0.0, tremor * 18.0, tremor * 15.0, 1.0, 1.0)
        bp["torso_base"] = (tremor * 2.2, 0.0, -dive * 98.0 + breach * 28.0 + bob * 12.0, 1.0, 1.0)
        bp["neck_pivot"] = (0.0, 0.0, -dive * 24.0 + breach * 50.0 + bob * 8.0, 1.0, 1.0)
        bp["head_pivot"] = (tremor * 1.5, 0.0, -dive * 12.0 + breach_lag * 22.0, 1.0, 1.0)
        bp["crown_mid"] = (0.0, 0.0, breach_lag * 18.0, 1.0, 1.0)
        bp["crown_tip"] = (0.0, 0.0, breach_lag * 16.0, 1.0, 1.0)
        bp["shoulder_l"] = (breach * 10.0, -breach * 18.0, breach * 26.0, 1.0, 1.0)
        bp["shoulder_r"] = (-breach * 10.0, breach * 18.0, breach * 26.0, 1.0, 1.0)
        bp["fin_l_base"] = (-dive * 16.0 + breach_lag * 28.0 + tremor * 8.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (-dive * 18.0 + breach_lag * 32.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (dive * 16.0 - breach_lag * 28.0 - tremor * 8.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (dive * 18.0 - breach_lag * 32.0, 0.0, 0.0, 1.0, 1.0)
        bp["brow_l"] = (breach * 11.0, 0.0, -breach * 4.5, 1.0, 1.0)
        bp["brow_r"] = (-breach * 11.0, 0.0, -breach * 4.5, 1.0, 1.0)
        bp["pupil_l"] = (0.0, tremor * 2.5, -1.0, 1.0, 1.0)
        bp["pupil_r"] = (0.0, tremor * 2.5, -1.0, 1.0, 1.0)
        eval_rigid_lids(bp, dive * 0.85)

        sc["ink_glow"] = (1, 1, 1, clamp(0.15 + breach * 1.0 + seg(t, 1.5, 2.1) * 0.55, 0.0, 1.0))
        plume = bump(t, 0.80, 1.00, 1.65)
        bp["boss_spray_l"] = (20.0 + tremor * 2.5, -plume * 34.0, plume * 78.0, 1.0, 1.0)
        bp["boss_spray_r"] = (-20.0 - tremor * 2.5, plume * 34.0, plume * 78.0, 1.0, 1.0)
        sc["boss_spray_l"] = (1, 1, 1, plume * 0.96)
        sc["boss_spray_r"] = (1, 1, 1, plume * 0.96)
        for idx_d, (tl, lf, vx, vy) in enumerate([
            (0.84, 0.72, -195.0, 560.0),
            (0.87, 0.74,  210.0, 580.0),
            (0.90, 0.68,  -95.0, 640.0),
            (0.93, 0.70,  110.0, 620.0),
        ], 1):
            rd, dx, dy, da = eval_parabolic_arc(t, tl, lf, vx, vy, g=1200.0)
            bp[f"boss_drop_{idx_d}"] = (rd, dx, dy, 1.0, 1.0)
            sc[f"boss_drop_{idx_d}"] = (1, 1, 1, da)
        eval_boss_waterline(bp, sc, t, dur, surge=clamp(breach * 1.2, 0.0, 1.2))
        add_bg_waves(bp, t, dur, amp_scale=1.25 + breach * 1.15, extra_bias=breach * 44.0 - dive * 24.0,
                     extra_curl=-breach * 16.0, lift_y=-dive * 68.0 + breach * 58.0)
        return bp, sc

    sb.sample_animation("phase2_surge", 2.10, False, anim_phase2_surge, events=[(0.95, "surge_roar")])

    # --- 6. hurt (0.65s snappy rotational whiplash with trailing crown & fins!) ---
    def anim_hurt(t: float):
        dur = 0.65
        bp, sc = {}, {"ink_glow": (1, 1, 1, 0.12)}
        hit = bump(t, 0.0, 0.11, 0.60)
        hit_lag = bump(t - 0.055, 0.0, 0.11, 0.62)
        vib = spring_osc(t, 7.2, 11.5)
        bp["torso_base"] = (-hit * 2.5, hit * 8.0, -hit * 14.0, 1.0, 1.0)
        bp["neck_pivot"] = (hit * -7.5 + vib * 3.8, hit * 16.0 + vib * 8.0, -hit * 18.0, 1.0, 1.0)
        bp["head_pivot"] = (hit_lag * -7.0 + vib * 2.5, 0.0, -hit_lag * 10.0, 1.0, 1.0)
        bp["crown_mid"] = (hit_lag * -5.5, 0.0, -hit_lag * 6.0, 1.0, 1.0)
        bp["crown_tip"] = (hit_lag * -7.5, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_base"] = (-hit_lag * 22.0 + vib * 6.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (-hit_lag * 26.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (hit_lag * 22.0 - vib * 6.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (hit_lag * 26.0, 0.0, 0.0, 1.0, 1.0)
        bp["pupil_l"] = (0.0, vib * 4.0, 2.5 * hit, 1.0, 1.0)
        bp["pupil_r"] = (0.0, vib * 4.0, 2.5 * hit, 1.0, 1.0)
        eval_rigid_lids(bp, 0.0)
        eval_boss_waterline(bp, sc, t, dur, surge=hit * 0.55)
        add_bg_waves(bp, t, dur, amp_scale=1.05 + hit * 0.95, extra_bias=-hit * 28.0 + vib * 12.0)
        return bp, sc

    sb.sample_animation("hurt", 0.65, False, anim_hurt)

    # --- 7. die (2.20s massive heavy sink down into the subterranean abyss!) ---
    def anim_die(t: float):
        dur = 2.20
        bp, sc = {}, {}
        sink = smootherstep(seg(t, 0.25, 2.05))
        fade = 1.0 - smoothstep(seg(t, 1.45, 2.18))
        shudder = math.exp(-4.0 * t) * math.sin(TAU * 8.0 * t) * (1.0 - sink)
        bp["torso_base"] = (shudder * 3.0 - sink * 10.0, shudder * 10.0, -sink * 460.0, 1.0, 1.0)
        bp["head_pivot"] = (-sink * 14.0, 0.0, -sink * 35.0, 1.0, 1.0)
        bp["fin_l_base"] = (-sink * 24.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (sink * 24.0, 0.0, 0.0, 1.0, 1.0)
        eval_rigid_lids(bp, smoothstep(seg(t, 0.1, 0.65)))
        eval_boss_waterline(bp, sc, t, dur, surge=(1.0 - sink) * 0.5)
        add_bg_waves(bp, t, dur, amp_scale=1.0 - sink * 0.7, extra_bias=-sink * 42.0, extra_curl=sink * 22.0, lift_y=-sink * 340.0)
        for sn in ("head_mantle", "pupil_l", "pupil_r", "eyelid_l", "eyelid_r"):
            sc[sn] = (1, 1, 1, fade)
        sc["ink_glow"] = (1, 1, 1, 0.0)
        for pref in bg_chains:
            sc[pref] = (0.78, 0.84, 0.94, fade)
        return bp, sc

    sb.sample_animation("die", 2.20, False, anim_die)

    # --- 8. charge_loop (2.00s menacing pressurized abyssal ink charge-up loop!) ---
    def anim_charge_loop(t: float):
        dur = 2.00
        bp, sc = {}, {}
        cyc = TAU * t / dur
        swell = 0.5 + 0.5 * math.sin(cyc - 0.6)
        tremor = math.sin(TAU * 4.0 * t) * 1.4
        bp["torso_base"] = (tremor * 0.45, math.sin(cyc * 0.5) * 4.0, -24.0 + swell * 26.0, 1.0 + swell * 0.042, 1.0 - swell * 0.024)
        bp["neck_pivot"] = (swell * 3.8 + tremor * 0.5, 0.0, -10.0 + swell * 18.0, 1.0, 1.0)
        bp["head_pivot"] = (-4.5 + swell * 5.2, 0.0, swell * 12.0, 1.02 + swell * 0.025, 0.99 - swell * 0.015)
        bp["crown_mid"] = (swell * 4.5 + tremor * 0.6, 0.0, swell * 8.0, 1.0, 1.0)
        bp["crown_tip"] = (swell * 6.0 - tremor * 0.8, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_base"] = (-12.0 - swell * 14.0 + tremor * 1.2, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (-14.0 - swell * 16.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (12.0 + swell * 14.0 - tremor * 1.2, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (14.0 + swell * 16.0, 0.0, 0.0, 1.0, 1.0)
        bp["pupil_l"] = (0.0, 2.5, -2.0 + swell * 3.0, 1.06, 1.06)
        bp["pupil_r"] = (0.0, -2.5, -2.0 + swell * 3.0, 1.06, 1.06)
        eval_rigid_lids(bp, 0.14 + swell * 0.08)
        sc["ink_glow"] = (0.52, 0.98, 1.0, 0.54 + swell * 0.42)
        eval_boss_waterline(bp, sc, t, dur, surge=0.35 + swell * 0.35)
        add_bg_waves(bp, t, dur, amp_scale=1.25 + swell * 0.45, extra_bias=14.0 + swell * 16.0,
                     extra_curl=-10.0 - swell * 8.0, lift_y=12.0 + swell * 22.0)
        return bp, sc

    sb.sample_animation("charge_loop", 2.00, True, anim_charge_loop)

    # --- 9. charge_pulse (1.15s deep abyssal inhale & pressure surge during INK_CHARGE turn!) ---
    def anim_charge_pulse(t: float):
        dur = 1.15
        bp, sc = {}, {}
        dip = bump(t, 0.0, 0.28, 0.56)
        surge = bump(t, 0.34, 0.64, 1.12)
        tremor = math.sin(TAU * 6.5 * t) * surge * 2.2
        bp["torso_base"] = (tremor * 0.6, 0.0, -dip * 42.0 + surge * 54.0, 1.0 + surge * 0.075 - dip * 0.03, 1.0 - surge * 0.035 + dip * 0.03)
        bp["neck_pivot"] = (-dip * 4.5 + surge * 6.0, 0.0, -dip * 18.0 + surge * 28.0, 1.0, 1.0)
        bp["head_pivot"] = (-dip * 5.0 + surge * 7.5 + tremor * 0.5, 0.0, surge * 18.0, 1.0 + surge * 0.04, 1.0 - surge * 0.02)
        bp["crown_mid"] = (surge * 7.5, 0.0, surge * 10.0, 1.0, 1.0)
        bp["crown_tip"] = (surge * 10.0 - tremor, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_base"] = (dip * 10.0 - surge * 26.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (dip * 12.0 - surge * 28.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (-dip * 10.0 + surge * 26.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (-dip * 12.0 + surge * 28.0, 0.0, 0.0, 1.0, 1.0)
        bp["pupil_l"] = (0.0, 2.5, surge * 4.0, 1.08, 1.08)
        bp["pupil_r"] = (0.0, -2.5, surge * 4.0, 1.08, 1.08)
        eval_rigid_lids(bp, dip * 0.35 + surge * 0.12)
        sc["ink_glow"] = (0.58, 1.0, 1.0, clamp(0.45 + surge * 0.55, 0.0, 1.0))
        eval_boss_waterline(bp, sc, t, dur, surge=0.35 + surge * 0.85)
        add_bg_waves(bp, t, dur, amp_scale=1.2 + surge * 0.85, extra_bias=-dip * 18.0 + surge * 34.0,
                     extra_curl=-surge * 14.0, lift_y=-dip * 28.0 + surge * 46.0)
        return bp, sc

    sb.sample_animation("charge_pulse", 1.15, False, anim_charge_pulse)

    # --- 10. stun_loop (2.20s exhausted post-spray dazed droop!) ---
    def anim_stun_loop(t: float):
        dur = 2.20
        bp, sc = {}, {}
        cyc = TAU * t / dur
        sway = math.sin(cyc)
        bob = math.sin(2.0 * cyc - 0.5)
        bp["torso_base"] = (sway * 4.5 - 3.0, sway * 10.0, -86.0 + bob * 9.0, 1.02, 0.96)
        bp["neck_pivot"] = (-7.5 + sway * 5.2, sway * 7.0, -24.0 + bob * 5.0, 1.0, 1.0)
        bp["head_pivot"] = (-9.0 + sway * 6.0, 0.0, -14.0, 1.0, 1.0)
        bp["crown_mid"] = (-6.5 + sway * 4.5, 0.0, -8.0, 1.0, 1.0)
        bp["crown_tip"] = (-8.5 + math.sin(cyc - 0.7) * 5.5, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_base"] = (18.0 + sway * 5.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_l_tip"] = (22.0 + math.sin(cyc - 0.5) * 6.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_base"] = (-18.0 + sway * 5.0, 0.0, 0.0, 1.0, 1.0)
        bp["fin_r_tip"] = (-22.0 + math.sin(cyc - 0.5) * 6.0, 0.0, 0.0, 1.0, 1.0)
        bp["pupil_l"] = (0.0, sway * 5.5, -2.5 + bob * 2.0, 0.94, 0.94)
        bp["pupil_r"] = (0.0, -sway * 5.5, -2.5 - bob * 2.0, 0.94, 0.94)
        eval_rigid_lids(bp, 0.58 + 0.08 * bob)
        sc["ink_glow"] = (0.4, 0.7, 0.8, 0.03)
        eval_boss_waterline(bp, sc, t, dur, surge=0.05)
        add_bg_waves(bp, t, dur, amp_scale=0.45, extra_bias=-32.0, extra_curl=18.0, lift_y=-74.0)
        return bp, sc

    sb.sample_animation("stun_loop", 2.20, True, anim_stun_loop)

    atlas_imgs = {
        "head_mantle": head_im,
        "pupil_l": pupil_l_im,
        "pupil_r": pupil_r_im,
        "eyelid_l": eyelid_l_im,
        "eyelid_r": eyelid_r_im,
        "ink_glow": ink_glow_im,
        "bg_tentacle_tall": bg_tall_im,
        "bg_tentacle_outer": bg_outer_im,
        "meniscus_back": meniscus_back_im,
        "meniscus_front": meniscus_front_im,
        "ripple_ring": ripple_im,
        "ripple_back": ripple_back_im,
        "ripple_front": ripple_front_im,
        "ripple_b_ring": ripple_b_im,
        "ripple_b_back": ripple_b_back_im,
        "ripple_b_front": ripple_b_front_im,
        "water_glint": water_glint_im,
        "crest_l": crest_l_im,
        "crest_r": crest_r_im,
        "spray_plume": spray_im,
    }
    for di in range(1, 5):
        atlas_imgs[f"drop_{di}"] = water_imgs[f"drop_{di}"]
    sb.export(atlas_imgs, ANIM_ROOT / "abyssal_inkmaw", default_mix=0.16)


# ==============================================================================
# RIG 2: FOREGROUND OFF-SCREEN CYLINDRICAL HYDRO-INK CANNON (`abyssal_ink_beam`)
# V4 Upgrade:
#   - Mouth-locked `beam_pivot` tracking Boss beak recoil & sweep motion 1:1
#   - 8-bone flexible hydrodynamic FK column (`beam_seg_0..7`) with dynamic Coriolis
#     sweep-lag curvature & high-speed hydraulic turbulence wave (NO rigid straight stick!)
#   - Pointed chevron liquid cutoff + fast tapering shutoff thread + nozzle sputter drops
#     (ZERO flat paper-scissor cutoff!)
#   - Continuous bridge-scraping `sweep_splash` riding exact stream-floor intersection
#     LEFT -> CENTER (`ink_hit_player` @ 1.06s!) -> RIGHT + explosive `player_impact`!
# ==============================================================================
def build_ink_beam_rig(ink_imgs: dict[str, Image.Image], beak_spine_y: float):
    sb = SpineBuilder("abyssal_ink_beam")
    sb.add_bone("root", None, 0.0, 0.0)
    sb.add_bone("beam_pivot", "root", 0.0, beak_spine_y)

    # 2 clipping-edge bones along beam axis so the hydro-cannon shoots out & tapers shut cleanly!
    c_top = sb.add_bone("clip_top", "beam_pivot", 0.0, beak_spine_y)
    c_bot = sb.add_bone("clip_bot", "beam_pivot", 0.0, beak_spine_y)

    # 8-bone flexible FK chain down the 1340px hydro-ink column!
    n_seg = 8
    col_len = 1340.0
    seg_len = col_len / n_seg
    seg_bones: list[str] = []
    seg_bids: list[int] = []
    prev_b = "beam_pivot"
    for k in range(n_seg):
        bn = f"beam_seg_{k}"
        bid = sb.add_bone(bn, prev_b, 0.0, beak_spine_y - k * seg_len)
        seg_bones.append(bn)
        seg_bids.append(bid)
        prev_b = bn

    sb.add_bone("nozzle_ring", "beam_pivot", 0.0, beak_spine_y - 14.0)
    # 5 staggered foaming pressure shock-rings mounted on `root` and steered along exact curved FK centerline!
    for ri in range(1, 6):
        sb.add_bone(f"shock_ring_{ri}", "root", 0.0, beak_spine_y - 30.0)
    # 8 high-speed ink comet streaks peeling off flanks of the curved jet!
    for si in range(1, 9):
        sb.add_bone(f"streak_{si}", "root", 0.0, beak_spine_y - 40.0)
    # Continuous bridge-deck scraping splash crown riding LEFT -> CENTER -> RIGHT across the expanded stone deck!
    # (`BeamOverlay` sits at Godot y = -24, so Spine y = -252 hits Godot y = +228 right across player feet on the broad stone deck!)
    sb.add_bone("sweep_splash", "root", 0.0, -242.0)
    # Explosive radial ink splash crown + 4 ballistic splatter droplets right on the centered player!
    sb.add_bone("player_impact", "root", 0.0, -248.0)
    for di in range(1, 5):
        sb.add_bone(f"impact_drop_{di}", "root", 0.0, -242.0)
    # 3 nozzle shutoff sputter droplets so beam cutoff ends organically like wet pressurized ink!
    for sp_i in range(1, 4):
        sb.add_bone(f"sputter_drop_{sp_i}", "root", 0.0, beak_spine_y - 20.0)

    col_im = ink_imgs["beam_column"]
    noz_im = ink_imgs["nozzle_ring"]
    shk_im = ink_imgs["shock_ring"]
    imp_im = ink_imgs["impact_crown"]

    # Strictly convex tapered liquid spearhead/dome clipping hexagon (100% Spine-safe, ZERO flat scissor edge!)
    W_CLIP = 640.0
    clip_poly = np.array([
        [-W_CLIP, beak_spine_y + 22.0],
        [    0.0, beak_spine_y + 68.0],
        [ W_CLIP, beak_spine_y + 22.0],
        [ W_CLIP, beak_spine_y + 16.0],
        [    0.0, beak_spine_y - 26.0],
        [-W_CLIP, beak_spine_y + 16.0],
    ], dtype=np.float64)
    clip_wts = [
        [(c_top, 1.0)], [(c_top, 1.0)], [(c_top, 1.0)],
        [(c_bot, 1.0)], [(c_bot, 1.0)], [(c_bot, 1.0)],
    ]
    sb.add_clipping_slot("beam_clip", "shock_ring_5", "beam_pivot", clip_poly, clip_wts)

    # Flexible 8-bone geodesic-weighted mesh for the lush 1340px hydro-ink column!
    v_px, uvs, tris = build_grid_mesh(col_im, spacing=26)
    col_sx = 1.24
    col_sy = col_len / max(1.0, col_im.height - 12.0)
    w_pts = np.column_stack([
        (v_px[:, 0] - col_im.width * 0.50) * col_sx,
        beak_spine_y - (v_px[:, 1] - 12.0) * col_sy,
    ])
    col_weights = []
    for _, wy in w_pts:
        u_seg = clamp((beak_spine_y - wy) / seg_len, 0.0, float(n_seg - 1))
        dists = sorted([(abs(u_seg - k), k) for k in range(n_seg)])[:3]
        raw_w = [(seg_bids[k], math.exp(-0.5 * (d / 0.85) ** 2)) for d, k in dists]
        s_w = sum(w for _, w in raw_w)
        col_weights.append([(bi, w / s_w) for bi, w in raw_w])
    sb.add_slot_mesh("beam_column", "beam_column", "beam_seg_0", col_im, w_pts, uvs, tris, col_weights, color="ffffff00")

    # 5 staggered foaming pressure shock-rings inside beam_clip riding down the curved FK centerline!
    for ri in range(1, 6):
        sb.add_rigid_sprite(f"shock_ring_{ri}", "shock_ring", f"shock_ring_{ri}", shk_im,
                            (shk_im.width * 0.5, shk_im.height * 0.5), scale=0.92, color="ffffff00")

    # Swirling nozzle vortex collar welded right at Kraken beak throat!
    sb.add_rigid_sprite("nozzle_ring", "nozzle_ring", "nozzle_ring", noz_im,
                        (noz_im.width * 0.5, noz_im.height * 0.5), scale=1.02, color="ffffff00")

    # 8 high-speed obsidian ink comet streaks
    for si in range(1, 9):
        sim = ink_imgs.get(f"streak_{((si - 1) % 3) + 1}", ink_imgs["streak_1"])
        sb.add_rigid_sprite(f"streak_{si}", f"streak_{((si - 1) % 3) + 1}", f"streak_{si}", sim,
                            (sim.width * 0.5, sim.height * 0.25), scale=0.92, color="ffffff00")

    # Continuous bridge-deck scraping splash crown!
    sb.add_rigid_sprite("sweep_splash", "impact_crown", "sweep_splash", imp_im,
                        (imp_im.width * 0.5, imp_im.height * 0.65), scale=0.92, color="ffffff00")

    # Massive radial ink splash crown + 4 ballistic droplets erupting over centered player when beam hits 0°!
    sb.add_rigid_sprite("player_impact", "impact_crown", "player_impact", imp_im,
                        (imp_im.width * 0.5, imp_im.height * 0.68), scale=1.04, color="ffffff00")
    for di in range(1, 5):
        sim = ink_imgs.get(f"streak_{((di - 1) % 3) + 1}", ink_imgs["streak_1"])
        sb.add_rigid_sprite(f"impact_drop_{di}", f"streak_{((di - 1) % 3) + 1}", f"impact_drop_{di}", sim,
                            (sim.width * 0.5, sim.height * 0.5), scale=0.72, color="ffffff00")
    for sp_i in range(1, 4):
        sim = ink_imgs.get(f"streak_{((sp_i - 1) % 3) + 1}", ink_imgs["streak_1"])
        sb.add_rigid_sprite(f"sputter_drop_{sp_i}", f"streak_{((sp_i - 1) % 3) + 1}", f"sputter_drop_{sp_i}", sim,
                            (sim.width * 0.5, sim.height * 0.5), scale=0.58, color="ffffff00")

    def eval_beam_kinematics(t: float):
        """Return mouth offset, base angle, per-segment local bends, and curved FK spine samples."""
        inhale = bump(t, 0.0, 0.44, 0.56)
        kickback = bump(t, 0.52, 0.57, 0.78)
        firing = hold(t, 0.52, 0.56, 1.48, 1.62)
        impact_jolt = bump(t, 1.02, 1.06, 1.24)
        cutoff_flick = bump(t, 1.48, 1.58, 1.82)

        def sweep_angle_deg(tt: float) -> float:
            u_r = seg(tt, 0.60, 1.52)
            sw = clamp(smootherstep(u_r) + 0.045 * math.sin(TAU * u_r), 0.0, 1.0)
            # Lead slightly during peak sweep velocity so the curving fluid hose slams dead-center (x=0) at t=1.06s!
            vel_lead = math.sin(math.pi * u_r) * 7.2
            return -48.0 + 96.0 * sw + vel_lead

        theta_deg = sweep_angle_deg(t)
        omega_deg = (sweep_angle_deg(min(2.05, t + 0.02)) - sweep_angle_deg(max(0.0, t - 0.02))) / 0.04

        u_raw = seg(t, 0.60, 1.52)
        sw_u = clamp(smootherstep(u_raw) + 0.045 * math.sin(TAU * u_raw), 0.0, 1.0)
        aim_pre = -ease_out_cubic(seg(t, 0.06, 0.46))
        aim = (-1.0 + 2.0 * sw_u) if t >= 0.56 else aim_pre
        aim_final = aim * (1.0 - smootherstep(seg(t, 1.54, 2.02)))

        # Welded 1:1 to Boss's moving dark beak mouth!
        mouth_dx = aim_final * 34.0
        mouth_dy = -inhale * 8.0 + kickback * 48.0 + firing * 12.0 - impact_jolt * 16.0 + cutoff_flick * 24.0

        tremor = (firing + impact_jolt * 0.6) * math.sin(TAU * 22.0 * t)
        pivot_rot = theta_deg + tremor * 1.6

        # Unmistakable fluid water-hose Coriolis sweep-lag + pressurized traveling hydraulic wave down the 8 segments!
        local_rots: list[float] = []
        for k in range(n_seg):
            if k == 0:
                local_rots.append(math.sin(TAU * 11.5 * t) * 2.0 * firing)
            else:
                uk = k / float(n_seg - 1)
                coriolis_lag = -0.056 * omega_deg * (0.55 + 0.45 * uk)
                turb = math.sin(TAU * 11.5 * t - 1.35 * k) * (2.2 + 2.8 * uk) * firing
                local_rots.append(coriolis_lag + turb)

        # Forward-kinematics world path of the curved hydro-ink centerline (`(x, y, world_rot_deg)` for k=0..n_seg)
        cx, cy = mouth_dx, beak_spine_y + mouth_dy
        cum_rot = pivot_rot
        pts_world = [(cx, cy, cum_rot)]
        for k in range(n_seg):
            cum_rot += local_rots[k]
            # Column points along -Y when rot=0; Spine CCW rotation +r rotates (0, -1) to (+sin r, -cos r)!
            rad = math.radians(cum_rot)
            cx += seg_len * math.sin(rad)
            cy -= seg_len * math.cos(rad)
            pts_world.append((cx, cy, cum_rot))
        return mouth_dx, mouth_dy, pivot_rot, local_rots, pts_world, firing, impact_jolt

    def sample_curve(pts_world: list[tuple[float, float, float]], dist: float) -> tuple[float, float, float]:
        idx_f = clamp(dist / seg_len, 0.0, float(n_seg) - 1e-4)
        i0 = int(idx_f)
        frac = idx_f - i0
        x0, y0, r0 = pts_world[i0]
        x1, y1, r1 = pts_world[i0 + 1]
        return x0 + (x1 - x0) * frac, y0 + (y1 - y0) * frac, r0 + (r1 - r0) * frac

    def anim_idle(t: float):
        sc = {s["name"]: (1, 1, 1, 0.0) for s in sb.slots if s["name"] != "beam_clip"}
        return {}, sc

    def anim_ink_spray(t: float):
        bp, sc = {}, {}
        mouth_dx, mouth_dy, pivot_rot, local_rots, pts_world, firing, impact_jolt = eval_beam_kinematics(t)
        pre_charge = bump(t, 0.24, 0.48, 0.56)

        bp["beam_pivot"] = (pivot_rot, mouth_dx, mouth_dy, 1.0, 1.0)

        # Smooth tapering width pulse on ignition & thin snapping liquid thread on cutoff (ZERO scissor-cut block!)
        ign_w = 0.72 + ease_out_cubic(seg(t, 0.52, 0.60)) * 0.28 + bump(t, 0.54, 0.58, 0.68) * 0.16
        hit_swell = impact_jolt * 0.14
        cut_pinch = 1.0 - smootherstep(seg(t, 1.46, 1.62)) * 0.78
        col_width_scale = (ign_w + hit_swell + firing * math.sin(TAU * 14.0 * t) * 0.04) * cut_pinch

        for k, bn in enumerate(seg_bones):
            if k == 0:
                bp[bn] = (local_rots[k], 0.0, 0.0, col_width_scale, 1.0)
            else:
                bp[bn] = (local_rots[k], 0.0, 0.0, 1.0, 1.0)

        # Fast supersonic ignition & snappy tapered tail sweep-out via chevron hardware beam_clip!
        ext_len = ease_out_cubic(seg(t, 0.52, 0.62)) * 1420.0
        cut_len = (seg(t, 1.48, 1.63) ** 1.6) * 1420.0
        bp["clip_top"] = (0.0, 0.0, -cut_len, 1.0, 1.0)
        bp["clip_bot"] = (0.0, 0.0, -ext_len, 1.0, 1.0)
        beam_a = hold(t, 0.51, 0.54, 1.50, 1.63)
        sc["beam_column"] = (1, 1, 1, beam_a * 0.99)

        # Pressurized vortex buildup at the beak throat -> roaring foaming collar while firing -> rapid snap shut!
        noz_a = clamp(pre_charge * 0.78 + hold(t, 0.50, 0.55, 1.46, 1.58) * 0.98, 0.0, 0.98)
        noz_sc = (0.48 + ease_out_cubic(seg(t, 0.24, 0.55)) * 0.62 + firing * math.sin(t * 38.0) * 0.08 + impact_jolt * 0.14) * (0.45 + 0.55 * cut_pinch)
        bp["nozzle_ring"] = (math.sin(t * 32.0) * 12.0, 0.0, 0.0, noz_sc, noz_sc * 0.90)
        sc["nozzle_ring"] = (1, 1, 1, noz_a)

        # 5 staggered foaming pressure shock-rings riding along the EXACT curved FK hydro-jet centerline!
        ring_period = 0.36
        for ri in range(1, 6):
            if 0.52 <= t <= 1.62:
                u = ((t - 0.52) / ring_period + (ri - 1) / 5.0) % 1.0
                dist = 40.0 + u * 1080.0
                rx, ry, rrot = sample_curve(pts_world, dist)
                r_sc = (0.64 + u * 0.58) * (0.65 + 0.35 * col_width_scale)
                ra = math.sin(math.pi * u) * firing * (0.78 if ri % 2 == 0 else 0.92)
                bp[f"shock_ring_{ri}"] = (rrot + math.sin(u * 14.0) * 5.0, rx, ry - (beak_spine_y - 30.0), r_sc, r_sc * 0.82)
                sc[f"shock_ring_{ri}"] = (1, 1, 1, ra)
            else:
                bp[f"shock_ring_{ri}"] = (0.0, 0.0, 0.0, 1.0, 1.0)
                sc[f"shock_ring_{ri}"] = (1, 1, 1, 0.0)

        # 8 high-speed ink comet streaks peeling off flanks of the curved jet!
        streak_specs = [
            (0.55, 0.36, -78.0), (0.63, 0.38,  84.0), (0.73, 0.36, -92.0), (0.83, 0.38,  88.0),
            (0.95, 0.36, -86.0), (1.06, 0.38,  94.0), (1.18, 0.36, -82.0), (1.30, 0.36,  86.0),
        ]
        for si, (t0, life, lat_off) in enumerate(streak_specs, 1):
            dt = t - t0
            if 0.0 <= dt <= life:
                u = dt / life
                dist = 55.0 + u * 980.0
                sx, sy, srot = sample_curve(pts_world, dist)
                rad = math.radians(srot)
                off = lat_off * (0.55 + u * 0.85)
                wx = sx + off * math.cos(rad)
                wy = sy + off * math.sin(rad)
                bp[f"streak_{si}"] = (srot + (-10.0 if lat_off < 0 else 10.0), wx, wy - (beak_spine_y - 40.0), 1.0, 1.0)
                sc[f"streak_{si}"] = (1, 1, 1, math.sin(math.pi * u) * 0.94)
            else:
                bp[f"streak_{si}"] = (0.0, 0.0, 0.0, 1.0, 1.0)
                sc[f"streak_{si}"] = (1, 1, 1, 0.0)

        # Continuous bridge-deck scraping splash (`sweep_splash`) riding exact jet-bridge intersection (y = -252 -> Godot y = +228)!
        bridge_y = -252.0
        hit_x = 0.0
        for k in range(len(pts_world) - 1):
            x0, y0, _ = pts_world[k]
            x1, y1, _ = pts_world[k + 1]
            if y0 >= bridge_y >= y1 and abs(y0 - y1) > 1e-3:
                f_y = (y0 - bridge_y) / (y0 - y1)
                hit_x = x0 + (x1 - x0) * f_y
                break
        # Dim sweep_splash gently right while peak `player_impact` takes center stage so the two crowns never double-stack!
        center_duck = 1.0 - hold(t, 1.04, 1.08, 1.26, 1.38) * 0.72
        sw_sp_a = hold(t, 0.56, 0.62, 1.48, 1.58) * 0.88 * center_duck
        sw_sp_sc = 0.86 + 0.14 * math.sin(TAU * 13.0 * t)
        bp["sweep_splash"] = (math.sin(TAU * 11.0 * t) * 9.0, hit_x, -10.0 + math.sin(TAU * 16.0 * t) * 5.0, sw_sp_sc, sw_sp_sc * 0.86)
        sc["sweep_splash"] = (1, 1, 1, sw_sp_a)

        # Explosive radial ink splash crown + 4 ballistic droplets erupting over centered player at exact `ink_hit_player` (t = 1.06s)!
        hp = seg(t, 1.04, 1.56)
        imp_a = hold(t, 1.04, 1.07, 1.28, 1.54)
        imp_sc = 0.72 + ease_out_cubic(seg(t, 1.04, 1.15)) * 0.56 + hp * 0.10
        bp["player_impact"] = (math.sin(hp * 10.0) * 6.0, 0.0, -16.0 + ease_out_cubic(seg(t, 1.04, 1.18)) * 22.0, imp_sc, imp_sc)
        sc["player_impact"] = (1, 1, 1, imp_a * 0.96)
        for di, (tl, lf, vx, vy) in enumerate([
            (1.05, 0.54, -265.0, 465.0),
            (1.06, 0.56,  275.0, 485.0),
            (1.07, 0.50, -155.0, 540.0),
            (1.08, 0.52,  165.0, 515.0),
        ], 1):
            rd, dx, dy, da = eval_parabolic_arc(t, tl, lf, vx, vy, g=1360.0)
            bp[f"impact_drop_{di}"] = (rd, dx, dy, 1.0, 1.0)
            sc[f"impact_drop_{di}"] = (1, 1, 1, da)

        # 3 wet nozzle shutoff sputter droplets flying outward right as the Kraken snaps its beak shut (t=1.48..1.88s)!
        for sp_i, (tl, lf, vx, vy) in enumerate([
            (1.48, 0.42,  210.0, -280.0),
            (1.51, 0.44,  125.0, -340.0),
            (1.54, 0.40,  265.0, -210.0),
        ], 1):
            rd, dx, dy, da = eval_parabolic_arc(t, tl, lf, vx, vy, g=1250.0)
            bp[f"sputter_drop_{sp_i}"] = (rd, mouth_dx + dx, mouth_dy + dy, 1.0, 1.0)
            sc[f"sputter_drop_{sp_i}"] = (1, 1, 1, da)

        return bp, sc

    sb.sample_animation("idle_hidden", 0.5, True, anim_idle)
    sb.sample_animation("ink_spray", 2.05, False, anim_ink_spray, events=[(1.06, "ink_hit_player")])

    atlas_imgs = {
        "beam_column": col_im,
        "nozzle_ring": noz_im,
        "shock_ring": shk_im,
        "impact_crown": imp_im,
    }
    for k in range(1, 4):
        atlas_imgs[f"streak_{k}"] = ink_imgs.get(f"streak_{k}", ink_imgs["streak_1"])
    sb.export(atlas_imgs, ANIM_ROOT / "abyssal_ink_beam", default_mix=0.06)


# ==============================================================================
# SHARED HELPER FOR RIG 3 & RIG 4: ZERO-STRETCH 14-BONE TRUE BRIDGE-SLAMMING HERO TENTACLES
# ==============================================================================
def build_hero_tentacle_rig(
    rig_name: str,
    tentacle_im: Image.Image,
    pts: np.ndarray,
    u_map: np.ndarray,
    water_imgs: dict[str, Image.Image],
    breach_imgs: dict[str, Image.Image],
    slam_imgs: dict[str, Image.Image],
    *,
    mirror_x: bool,
    base_tilt_deg: float,
    base_cx: float,
    is_ink_arm: bool,
    glow_im: Image.Image | None = None,
):
    sb = SpineBuilder(rig_name)
    sb.add_bone("root", None, 0.0, 0.0)
    sb.add_bone("emerge_rig", "root", 0.0, 0.0)
    sb.add_bone("fx_root", "root", base_cx, 0.0)

    # Pre-compute 14-bone setup kinematics & exact bridge-slam target poses!
    n_bones = len(pts) - 1
    root_xy = (0.0, -34.0)
    arm_scale = 0.63

    # Canonical LEFT-arm (+X bridge slam) target world-angle curves across 14 bones (V3 fluid organic poses):
    #   - Crouch: brief anticipatory dip & tight coil before exploding upward!
    #   - Windup (Axe Rear): leans HIGH & BACK into upper-left sky (+114°..+126° -> open club), huge intimidating silhouette!
    #   - Whip Drop: proximal drives fast down-right while distal trails overhead in a dramatic cracking S-whip!
    #   - Bridge Slam: SMOOTH unkinked muscular arch (max 28°/bone!) surging forward (+36px) over the stone curb
    #                  and slapping DEAD FLAT across center bridge deck!
    #   - Slap 1 (Ink Forehand): brisk mid-bridge whip-crack with active flicked-up glowing tip!
    #   - Heavy Peel-off: proximal pulls tall & upright FIRST while heavy club hangs/drags off the stone slabs!
    #   - Overshoot: elastic whip rebound slightly past upright before settling smoothly into idle!
    left_crouch_ctrl = [86.0, 90.0, 92.0, 88.0, 80.0, 66.0, 46.0, 22.0, -4.0, -30.0, -54.0, -72.0, -84.0, -90.0]
    left_wind_ctrl = [116.0, 124.0, 128.0, 124.0, 112.0, 94.0, 70.0, 44.0, 18.0, -2.0, -14.0, -22.0, -26.0, -28.0]
    left_wind2_ctrl = [120.0, 130.0, 136.0, 132.0, 120.0, 102.0, 78.0, 50.0, 22.0, 0.0, -14.0, -24.0, -28.0, -30.0]
    left_drop_ctrl = [60.0, 32.0, 4.0, -14.0, -16.0, 4.0, 30.0, 56.0, 76.0, 86.0, 84.0, 72.0, 54.0, 34.0]
    left_slam_ctrl = [60.0, 40.0, 18.0, -6.0, -30.0, -52.0, -62.0, -56.0, -36.0, -16.0, -2.0, 2.0, 1.0, -1.0]
    left_slap1_ctrl = [66.0, 46.0, 24.0, 0.0, -24.0, -46.0, -54.0, -46.0, -24.0, -4.0, 10.0, 16.0, 12.0, 6.0]
    left_peel_ctrl = [84.0, 78.0, 64.0, 44.0, 20.0, -4.0, -22.0, -32.0, -32.0, -24.0, -12.0, 0.0, 12.0, 22.0]
    left_over_ctrl = [98.0, 102.0, 104.0, 102.0, 96.0, 86.0, 70.0, 50.0, 26.0, 2.0, -16.0, -28.0, -34.0, -36.0]
    left_flare_ctrl = [104.0, 108.0, 110.0, 106.0, 96.0, 80.0, 58.0, 34.0, 10.0, -12.0, -28.0, -40.0, -48.0, -52.0]
    left_guard_ctrl = [102.0, 98.0, 90.0, 76.0, 54.0, 26.0, -6.0, -38.0, -70.0, -100.0, -126.0, -144.0, -154.0, -160.0]
    left_bolster_ctrl = [96.0, 100.0, 102.0, 98.0, 88.0, 72.0, 52.0, 30.0, 8.0, -14.0, -32.0, -46.0, -54.0, -58.0]

    def orient_pose(ctrl: list[float]) -> list[float]:
        arr = resample_angles(ctrl, n_bones)
        return mirror_world_angles(arr) if mirror_x else arr

    ang_crouch = orient_pose(left_crouch_ctrl)
    ang_wind = orient_pose(left_wind_ctrl)
    ang_wind2 = orient_pose(left_wind2_ctrl)
    ang_drop = orient_pose(left_drop_ctrl)
    ang_slam = orient_pose(left_slam_ctrl)
    ang_slap1 = orient_pose(left_slap1_ctrl)
    ang_peel = orient_pose(left_peel_ctrl)
    ang_over = orient_pose(left_over_ctrl)
    ang_flare = orient_pose(left_flare_ctrl)
    ang_guard = orient_pose(left_guard_ctrl)
    ang_bolster = orient_pose(left_bolster_ctrl)

    # --- BACK WATER & BREACH VFX BONES (BEHIND TENTACLE BASE) ---
    sb.add_bone("w_meniscus_back", "fx_root", base_cx, 2.0)
    for r_i in (1, 2):
        sb.add_bone(f"w_ripple_back_{r_i}", "fx_root", base_cx, 6.0)
    sb.add_bone("w_rear_wave", "fx_root", base_cx, 8.0)
    sb.add_bone("w_spray_c", "fx_root", base_cx, 12.0)

    sb.add_bone("f_crater", "fx_root", base_cx, -6.0)
    sb.add_bone("f_slab_back", "fx_root", base_cx, 26.0)

    # Compute exact FK setup kinematics + forward lunge offset so Bridge Slam Impact VFX lands DEAD UNDER the club!
    slam_lunge_x = -18.0 if mirror_x else 18.0
    slam_lunge_y = -2.0
    rx0_tmp, ry0_tmp = pts[0, 0], pts[0, 1]
    cos_t, sin_t = math.cos(math.radians(base_tilt_deg)), math.sin(math.radians(base_tilt_deg))
    dx_tmp = (pts[:, 0] - rx0_tmp) * (-arm_scale if mirror_x else arm_scale)
    dy_tmp = (ry0_tmp - pts[:, 1]) * arm_scale
    j_preview = np.column_stack([root_xy[0] + dx_tmp * cos_t - dy_tmp * sin_t, root_xy[1] + dx_tmp * sin_t + dy_tmp * cos_t])
    _, seg_lens_preview = compute_chain_kinematics(j_preview)
    slam_fk = eval_fk_world_points((root_xy[0] + slam_lunge_x, root_xy[1] + slam_lunge_y), seg_lens_preview, ang_slam)
    impact_pt = slam_fk[min(len(slam_fk) - 1, int(round(n_bones * 0.80)))]
    imp_x, imp_y = impact_pt[0], impact_pt[1] - 4.0
    print(f"HERO_SLAM_FK {rig_name}: arch_peak_y={max(p[1] for p in slam_fk):.1f}, club_mid=({imp_x:.1f}, {imp_y:.1f}), tip=({slam_fk[-1][0]:.1f}, {slam_fk[-1][1]:.1f})")

    # --- BRIDGE SURFACE IMPACT VFX (UNDERNEATH SLAMMING CLUB: CRACKS + SHOCKWAVE RING!) ---
    sb.add_bone("slam_fx_root", "root", imp_x, imp_y)
    sb.add_bone("slam_cracks", "slam_fx_root", imp_x, imp_y)
    sb.add_bone("slam_shockwave", "slam_fx_root", imp_x, imp_y + 4.0)

    meniscus_back_im = water_imgs["meniscus_back"]
    meniscus_front_im = water_imgs["meniscus_front"]
    # Alternate A/B broken ripple variants between Left & Right hero tentacles so the lake NEVER looks copy-pasted!
    r1_b_reg, r1_b_im = ("ripple_b_back", water_imgs["ripple_b_back"]) if mirror_x else ("ripple_back", water_imgs["ripple_back"])
    r2_b_reg, r2_b_im = ("ripple_back", water_imgs["ripple_back"]) if mirror_x else ("ripple_b_back", water_imgs["ripple_b_back"])
    r1_f_reg, r1_f_im = ("ripple_b_front", water_imgs["ripple_b_front"]) if mirror_x else ("ripple_front", water_imgs["ripple_front"])
    r2_f_reg, r2_f_im = ("ripple_front", water_imgs["ripple_front"]) if mirror_x else ("ripple_b_front", water_imgs["ripple_b_front"])
    rear_wave_im = water_imgs["rear_wave"]
    spray_im = water_imgs["spray_plume"]
    crest_l_im = water_imgs["crest_l"]
    crest_r_im = water_imgs["crest_r"]

    # Hardware shore-line water clipping polygon: strictly convex rectangle keeping ALL water ripples/waves/crests
    # strictly in open lake ABOVE the raised stone bridge curb (`Godot y = +76.5`, water tentacle at `y = +32` => `Spine y >= -43.0`!)
    root_bid = sb.bone_idx["root"]
    WATER_CLIP_MIN_Y = -43.0
    water_shore_poly = np.array([
        [-1500.0, WATER_CLIP_MIN_Y],
        [ 1500.0, WATER_CLIP_MIN_Y],
        [ 1500.0, 1500.0],
        [-1500.0, 1500.0],
    ], dtype=np.float64)
    water_shore_wts = [[(root_bid, 1.0)] for _ in range(len(water_shore_poly))]
    sb.add_clipping_slot("water_back_clip", "w_spray_c", "root", water_shore_poly, water_shore_wts)

    sb.add_rigid_sprite("w_meniscus_back", "meniscus_back", "w_meniscus_back", meniscus_back_im,
                        (meniscus_back_im.width * 0.5, meniscus_back_im.height * 0.5), scale=0.44, color="ffffffcc")
    sb.add_rigid_sprite("w_ripple_back_1", r1_b_reg, "w_ripple_back_1", r1_b_im,
                        (r1_b_im.width * 0.5, r1_b_im.height * 0.5), scale=0.66, color="ffffff99")
    sb.add_rigid_sprite("w_ripple_back_2", r2_b_reg, "w_ripple_back_2", r2_b_im,
                        (r2_b_im.width * 0.5, r2_b_im.height * 0.5), scale=0.66, color="ffffff99")
    sb.add_rigid_sprite("w_rear_wave", "rear_wave", "w_rear_wave", rear_wave_im,
                        (rear_wave_im.width * 0.5, rear_wave_im.height * 0.92), scale=0.88, color="ffffffcc")
    sb.add_rigid_sprite("w_spray_c", "spray_plume", "w_spray_c", spray_im,
                        (spray_im.width * 0.5, spray_im.height * 0.90), scale=0.92, color="ffffff00")

    crater_im = breach_imgs["crater"]
    slab_back_im = breach_imgs["slab_back"]
    slab_l_im = breach_imgs["slab_l"]
    slab_r_im = breach_imgs["slab_r"]
    slab_front_im = breach_imgs["slab_front"]
    dust_im = breach_imgs["dust_cloud"]

    sb.add_rigid_sprite("f_crater", "crater", "f_crater", crater_im,
                        (crater_im.width * 0.5, crater_im.height * 0.52), scale=1.05, color="ffffff00")
    sb.add_rigid_sprite("f_slab_back", "slab_back", "f_slab_back", slab_back_im,
                        (slab_back_im.width * 0.5, slab_back_im.height * 0.85), scale=0.82, color="ffffff00")

    # Bridge impact cracks & shockwave ring rendered right ON the stone bridge surface beneath the slammed arm!
    cracks_im = slam_imgs["impact_cracks"]
    shock_im = slam_imgs["shockwave_ring"]
    sb.add_rigid_sprite("slam_cracks", "impact_cracks", "slam_cracks", cracks_im,
                        (cracks_im.width * 0.5, cracks_im.height * 0.52), scale=1.05, color="ffffff00")
    sb.add_rigid_sprite("slam_shockwave", "shockwave_ring", "slam_shockwave", shock_im,
                        (shock_im.width * 0.5, shock_im.height * 0.52), scale=1.12, color="ffffff00")

    # --- STRIKE-SIDE-OPEN HARDWARE `emerge_clip` POLYGON ---
    # Hugs the curved water/crater meniscus cleanly across the base trunk, while dropping wide open
    # down to y = -720 on the bridge-strike side so the slamming tentacle crosses OVER the stone curb
    # and slaps 100% UNCLIPPED directly onto the stone bridge surface!
    clip_end = "tentacle_glow" if is_ink_arm else "tentacle"
    if not mirror_x:
        # Left arm strikes RIGHT (+X): clip entire submerged base trunk [-1400 .. +112], drop open cleanly at x >= +122 -> +1500
        men_xs = np.linspace(-180.0, 112.0, 11)
        men_pts = [[float(base_cx + dx), float(-6.0 - 12.0 * math.cos(math.pi * 0.5 * clamp(dx / 180.0, -1.0, 1.0)))] for dx in men_xs]
        emerge_poly = np.array(
            [[-1400.0, -6.0]] + men_pts + [[base_cx + 122.0, -720.0], [1500.0, -720.0], [1500.0, 1500.0], [-1400.0, 1500.0]],
            dtype=np.float64,
        )
    else:
        # Right arm strikes LEFT (-X): drop open cleanly at x <= -122 -> -1500, clip entire submerged base trunk [-112 .. +1400]
        men_xs = np.linspace(-112.0, 180.0, 11)
        men_pts = [[float(base_cx + dx), float(-6.0 - 12.0 * math.cos(math.pi * 0.5 * clamp(dx / 180.0, -1.0, 1.0)))] for dx in men_xs]
        emerge_poly = np.array(
            [[-1500.0, -720.0], [base_cx - 122.0, -720.0]] + men_pts + [[1400.0, -6.0], [1400.0, 1500.0], [-1500.0, 1500.0]],
            dtype=np.float64,
        )
    sb.add_clipping_slot("emerge_clip", clip_end, "root", emerge_poly, [[(root_bid, 1.0)] for _ in range(len(emerge_poly))])

    # --- 14-BONE PURE-ROTATIONAL FK TENTACLE CHAIN ---
    bones, rest_angs, seg_lens, _ = add_fk_tentacle_ex(
        sb, "tentacle", "emerge_rig", "tentacle", tentacle_im, pts, u_map,
        root_world_xy=root_xy, scale=arm_scale, mirror_x=mirror_x, base_tilt_deg=base_tilt_deg, spacing=14,
        glow_im=glow_im if is_ink_arm else None,
        glow_region_name="ink_blister_glow" if is_ink_arm else None,
    )

    # --- FRONT BASE VFX (TIGHT MENISCUS COLLAR, A/B BROKEN RIPPLES, BREAKING CRESTS, HINGED SLABS, BOUNCING BREACH SHARDS) ---
    sb.add_clipping_slot("water_front_clip", "w_crest_r", "root", water_shore_poly, water_shore_wts)
    sb.add_bone("w_meniscus_front", "fx_root", base_cx, 4.0)
    sb.add_rigid_sprite("w_meniscus_front", "meniscus_front", "w_meniscus_front", meniscus_front_im,
                        (meniscus_front_im.width * 0.5, meniscus_front_im.height * 0.5), scale=0.44, color="ffffffea")
    sb.add_bone("w_ripple_front_1", "fx_root", base_cx, 6.0)
    sb.add_bone("w_ripple_front_2", "fx_root", base_cx, 6.0)
    sb.add_rigid_sprite("w_ripple_front_1", r1_f_reg, "w_ripple_front_1", r1_f_im,
                        (r1_f_im.width * 0.5, r1_f_im.height * 0.5), scale=0.66, color="ffffffc4")
    sb.add_rigid_sprite("w_ripple_front_2", r2_f_reg, "w_ripple_front_2", r2_f_im,
                        (r2_f_im.width * 0.5, r2_f_im.height * 0.5), scale=0.66, color="ffffffc4")
    sb.add_bone("w_crest_l", "fx_root", base_cx - 72.0, 4.0)
    sb.add_bone("w_crest_r", "fx_root", base_cx + 72.0, 4.0)
    sb.add_rigid_sprite("w_crest_l", "crest_l", "w_crest_l", crest_l_im, (crest_l_im.width * 0.72, crest_l_im.height * 0.88), scale=0.76, color="fffffff0")
    sb.add_rigid_sprite("w_crest_r", "crest_r", "w_crest_r", crest_r_im, (crest_r_im.width * 0.28, crest_r_im.height * 0.88), scale=0.76, color="fffffff0")

    for di in range(1, 5):
        sb.add_bone(f"w_drop_{di}", "fx_root", base_cx, 16.0)
        dim = water_imgs[f"drop_{di}"]
        sb.add_rigid_sprite(f"w_drop_{di}", f"drop_{di}", f"w_drop_{di}", dim, (dim.width * 0.5, dim.height * 0.5), scale=0.82, color="ffffff00")

    sb.add_bone("f_slab_l", "fx_root", base_cx - 102.0, -8.0)
    sb.add_bone("f_slab_r", "fx_root", base_cx + 102.0, -8.0)
    sb.add_bone("f_slab_front", "fx_root", base_cx, -42.0)
    sb.add_rigid_sprite("f_slab_l", "slab_l", "f_slab_l", slab_l_im, (slab_l_im.width * 0.72, slab_l_im.height * 0.80), scale=0.68, color="ffffff00")
    sb.add_rigid_sprite("f_slab_r", "slab_r", "f_slab_r", slab_r_im, (slab_r_im.width * 0.28, slab_r_im.height * 0.80), scale=0.68, color="ffffff00")
    sb.add_rigid_sprite("f_slab_front", "slab_front", "f_slab_front", slab_front_im, (slab_front_im.width * 0.5, slab_front_im.height * 0.58), scale=0.72, color="ffffff00")

    sb.add_bone("f_dust_l", "fx_root", base_cx - 58.0, -14.0)
    sb.add_bone("f_dust_r", "fx_root", base_cx + 58.0, -14.0)
    sb.add_rigid_sprite("f_dust_l", "dust_cloud", "f_dust_l", dust_im, (dust_im.width * 0.5, dust_im.height * 0.6), scale=0.85, color="ffffff00")
    sb.add_rigid_sprite("f_dust_r", "dust_cloud", "f_dust_r", dust_im, (dust_im.width * 0.5, dust_im.height * 0.6), scale=0.85, color="ffffff00")

    for si in range(1, 6):
        sb.add_bone(f"f_shard_{si}", "fx_root", base_cx, 8.0)
        sim = breach_imgs[f"shard_{si}"]
        sb.add_rigid_sprite(f"f_shard_{si}", f"shard_{si}", f"f_shard_{si}", sim, (sim.width * 0.5, sim.height * 0.5), scale=1.05, color="ffffff00")

    # --- FRONT BRIDGE-SLAM IMPACT VFX (BURSTING UP OVER THE SLAMMED CLUB: DUST PLUMES + CHIPS + INK SLASH!) ---
    sb.add_bone("slam_dust_l", "slam_fx_root", imp_x - 36.0, imp_y + 10.0)
    sb.add_bone("slam_dust_r", "slam_fx_root", imp_x + 36.0, imp_y + 10.0)
    sd_l_im = slam_imgs["dust_l"]
    sd_r_im = slam_imgs["dust_r"]
    sb.add_rigid_sprite("slam_dust_l", "dust_l", "slam_dust_l", sd_l_im, (sd_l_im.width * 0.72, sd_l_im.height * 0.86), scale=0.95, color="ffffff00")
    sb.add_rigid_sprite("slam_dust_r", "dust_r", "slam_dust_r", sd_r_im, (sd_r_im.width * 0.28, sd_r_im.height * 0.86), scale=0.95, color="ffffff00")

    for ci in range(1, 5):
        sb.add_bone(f"slam_chip_{ci}", "slam_fx_root", imp_x, imp_y + 12.0)
        cim = slam_imgs[f"chip_{ci}"]
        sb.add_rigid_sprite(f"slam_chip_{ci}", f"chip_{ci}", f"slam_chip_{ci}", cim, (cim.width * 0.5, cim.height * 0.5), scale=1.10, color="ffffff00")

    if is_ink_arm:
        sb.add_bone("slam_ink_slash", "slam_fx_root", imp_x, imp_y + 24.0)
        isl_im = slam_imgs["ink_slash"]
        sb.add_rigid_sprite("slam_ink_slash", "ink_slash", "slam_ink_slash", isl_im,
                            (isl_im.width * 0.5, isl_im.height * 0.58), scale=1.18, color="ffffff00")

    WATER_SLOTS = ("w_meniscus_back", "w_meniscus_front", "w_ripple_back_1", "w_ripple_back_2", "w_ripple_front_1", "w_ripple_front_2", "w_rear_wave", "w_spray_c", "w_crest_l", "w_crest_r", "w_drop_1", "w_drop_2", "w_drop_3", "w_drop_4")
    FLOOR_SLOTS = ("f_crater", "f_slab_back", "f_slab_l", "f_slab_r", "f_slab_front", "f_dust_l", "f_dust_r", "f_shard_1", "f_shard_2", "f_shard_3", "f_shard_4", "f_shard_5")
    SLAM_SLOTS = ("slam_cracks", "slam_shockwave", "slam_dust_l", "slam_dust_r", "slam_chip_1", "slam_chip_2", "slam_chip_3", "slam_chip_4") + (("slam_ink_slash",) if is_ink_arm else ())

    def hide_slam_fx(sc: dict):
        for sn in SLAM_SLOTS:
            sc[sn] = (1, 1, 1, 0.0)

    def eval_bridge_slam_impact(bp: dict, sc: dict, t: float, t_hit: float, *, intensity: float = 1.0, show_ink: bool = False):
        """Instant-punch bridge-surface impact: rigid sunken basalt crater + cool sea-mist shockwave + damp dust plumes + chips!"""
        dt = t - t_hit
        if dt < -0.015 or dt > 0.90:
            hide_slam_fx(sc)
            return
        # Sunken wet-basalt crater snaps rigidly into stone in exact floor perspective (ZERO curb overlap or stretching!)
        cr_a = hold(t, t_hit - 0.010, t_hit + 0.010, t_hit + 0.54, t_hit + 0.85) * 0.96 * clamp(intensity, 0.58, 1.0)
        bp["slam_cracks"] = (0.0, 0.0, 0.0, 0.94 * intensity, 0.56 * intensity)
        sc["slam_cracks"] = (1, 1, 1, cr_a)

        # Sleek cool sea-mist shockwave ring expands briskly flat across the stone deck!
        sw_u = seg(dt, 0.0, 0.28)
        sw_sc = (0.48 + ease_out_cubic(sw_u) * 1.02) * intensity
        sw_a = hold(t, t_hit - 0.010, t_hit + 0.014, t_hit + 0.10, t_hit + 0.28) * 0.94
        bp["slam_shockwave"] = (0.0, 0.0, 0.0, sw_sc, sw_sc * 0.68)
        sc["slam_shockwave"] = (1, 1, 1, sw_a)

        # Twin hand-painted gouache stone dust plumes blast out FAST (ease_out_cubic!) from under the club!
        du = seg(dt, 0.0, 0.54)
        du_fast = ease_out_cubic(du)
        d_sc = (0.76 + du_fast * 0.52) * intensity
        d_a = hold(t, t_hit - 0.005, t_hit + 0.025, t_hit + 0.20, t_hit + 0.54) * 0.90
        bp["slam_dust_l"] = (du_fast * 22.0, -du_fast * 135.0 * intensity, ease_out_cubic(seg(dt, 0.0, 0.36)) * 34.0, d_sc, d_sc)
        bp["slam_dust_r"] = (-du_fast * 22.0, du_fast * 135.0 * intensity, ease_out_cubic(seg(dt, 0.0, 0.36)) * 34.0, d_sc, d_sc)
        sc["slam_dust_l"] = (1, 1, 1, d_a)
        sc["slam_dust_r"] = (1, 1, 1, d_a)

        # 4 independent angular dark-slate stone chips launching up on gravity arcs with stone-bridge bounce!
        chips_spec = [
            (t_hit + 0.00, 0.62, -225.0 * intensity, 455.0 * intensity,  450.0),
            (t_hit + 0.01, 0.66,  235.0 * intensity, 480.0 * intensity, -470.0),
            (t_hit + 0.02, 0.58, -120.0 * intensity, 520.0 * intensity,  380.0),
            (t_hit + 0.02, 0.60,  140.0 * intensity, 495.0 * intensity, -400.0),
        ]
        for ci, (tl, lf, vx, vy, spin) in enumerate(chips_spec, 1):
            rc, cx, cy, ca = eval_parabolic_arc(t, tl, lf, vx, vy, g=1520.0, floor_y=-14.0, bounce_restitution=0.36, spin_deg_per_s=spin)
            bp[f"slam_chip_{ci}"] = (rc, cx, cy, 1.0, 1.0)
            sc[f"slam_chip_{ci}"] = (1, 1, 1, ca)

        if is_ink_arm:
            if show_ink:
                iu = seg(dt, 0.0, 0.34)
                iu_fast = ease_out_cubic(iu)
                i_sc = (0.68 + iu_fast * 0.78) * intensity
                i_a = hold(t, t_hit - 0.01, t_hit + 0.015, t_hit + 0.14, t_hit + 0.34) * 0.98
                bp["slam_ink_slash"] = (-20.0 + iu_fast * 36.0, -iu_fast * 44.0, iu_fast * 30.0, i_sc, i_sc)
                sc["slam_ink_slash"] = (1, 1, 1, i_a)
            else:
                sc["slam_ink_slash"] = (1, 1, 1, 0.0)

    def eval_burst_fx(bp: dict, sc: dict, t: float, t0: float, *, include_floor_debris: bool = True, water_visible_in_track0: bool = False):
        curl = bump(t, t0, t0 + 0.24, t0 + 0.78)
        bp["w_rear_wave"] = (curl * 8.0, 0.0, curl * 26.0, 1.0, 1.0)
        bp["w_crest_l"] = (curl * 24.0, -curl * 28.0, curl * 18.0, 1.0, 1.0)
        bp["w_crest_r"] = (-curl * 24.0, curl * 28.0, curl * 18.0, 1.0, 1.0)
        bp["w_spray_c"] = (0.0, 0.0, curl * 48.0, 1.0, 1.0)
        for idx_d, (tl, lf, vx, vy) in enumerate([
            (t0 + 0.02, 0.62, -165.0, 430.0),
            (t0 + 0.06, 0.66,  175.0, 460.0),
            (t0 + 0.10, 0.58, -95.0,  510.0),
            (t0 + 0.14, 0.60,  110.0, 480.0),
        ], 1):
            r_d, dx, dy, da = eval_parabolic_arc(t, tl, lf, vx, vy, g=1180.0)
            bp[f"w_drop_{idx_d}"] = (r_d, dx, dy, 1.0, 1.0)
            if water_visible_in_track0:
                sc[f"w_drop_{idx_d}"] = (1, 1, 1, da)

        if include_floor_debris:
            heave = smootherstep(seg(t, t0, t0 + 0.24))
            overshoot = bump(t, t0 + 0.12, t0 + 0.26, t0 + 0.52)
            bp["f_slab_l"] = (heave * 5.0 + overshoot * 12.0, -heave * 6.0, heave * 4.0 + overshoot * 9.0, 1.0, 1.0)
            bp["f_slab_r"] = (-heave * 12.0 - overshoot * 16.0, heave * 8.0, heave * 8.0 + overshoot * 11.0, 1.0, 1.0)
            bp["f_slab_front"] = (-overshoot * 8.0, 0.0, -heave * 4.0 + overshoot * 10.0, 1.0, 1.0)
            bp["f_slab_back"] = (overshoot * 10.0, 0.0, heave * 6.0 + overshoot * 10.0, 1.0, 1.0)
            dp = seg(t, t0 + 0.04, t0 + 0.85)
            bp["f_dust_l"] = (dp * 55.0, -dp * 95.0, smoothstep(dp) * 24.0, 1.0, 1.0)
            bp["f_dust_r"] = (-dp * 55.0, dp * 95.0, smoothstep(dp) * 24.0, 1.0, 1.0)
            for idx_s, (tl, lf, vx, vy, spin) in enumerate([
                (t0 + 0.02, 0.82, -195.0, 440.0,  340.0),
                (t0 + 0.04, 0.85,  210.0, 470.0, -390.0),
                (t0 + 0.06, 0.78, -110.0, 520.0,  460.0),
                (t0 + 0.08, 0.80,  125.0, 500.0, -420.0),
                (t0 + 0.10, 0.76,  -45.0, 390.0,  310.0),
            ], 1):
                r_s, sx, sy, _sa = eval_parabolic_arc(t, tl, lf, vx, vy, g=1320.0, floor_y=-24.0, bounce_restitution=0.38, spin_deg_per_s=spin)
                bp[f"f_shard_{idx_s}"] = (r_s, sx, sy, 1.0, 1.0)

    # --- TRACK 1 MODE ANIMATIONS ---
    def anim_base_water(t: float):
        dur = 2.6
        th = TAU * t / dur
        bp, sc = {}, {}
        # Tight hugging meniscus collar breathing organically around the emerging tentacle trunk
        m_flip = -1.0 if mirror_x else 1.0
        sc_m = 1.0 + 0.04 * math.sin(th + (1.3 if mirror_x else 0.0))
        bp["w_meniscus_back"] = (0.0, 0.0, 0.0, m_flip * sc_m, sc_m * 0.64)
        sc["w_meniscus_back"] = (1, 1, 1, 0.78)
        bp["w_meniscus_front"] = (0.0, 0.0, 0.0, m_flip * sc_m, sc_m * 0.64)
        sc["w_meniscus_front"] = (1, 1, 1, 0.90)
        # 2 staggered delicate broken ripple sets with ease-out-quad fluid wave deceleration!
        ph_off = 0.28 if mirror_x else 0.0
        for idx_r, ph in ((1, 0.0), (2, 0.5)):
            u = ((t / dur) + ph + ph_off) % 1.0
            u_wave = 1.0 - (1.0 - u) ** 1.65
            sc_r = 0.72 + u_wave * 0.44
            env = math.sin(math.pi * (u ** 0.76))
            sx_flip = (-1.0 if (mirror_x and idx_r == 1) else 1.0)
            wob = math.sin(th + idx_r * 2.2) * 2.0
            bp[f"w_ripple_back_{idx_r}"] = (0.0, wob, 0.0, sx_flip * sc_r, sc_r * 0.54)
            sc[f"w_ripple_back_{idx_r}"] = (1, 1, 1, env * 0.54)
            bp[f"w_ripple_front_{idx_r}"] = (0.0, wob, 0.0, sx_flip * sc_r, sc_r * 0.54)
            sc[f"w_ripple_front_{idx_r}"] = (1, 1, 1, env * 0.72)
        for wn in ("w_rear_wave", "w_crest_l", "w_crest_r", "w_spray_c", "w_drop_1", "w_drop_2", "w_drop_3", "w_drop_4"):
            sc[wn] = (1, 1, 1, 0.0)
        for fn in FLOOR_SLOTS:
            sc[fn] = (1, 1, 1, 0.0)
        return bp, sc

    def anim_base_water_burst(t: float):
        dur = 0.95
        bp, sc = {}, {}
        m_flip = -1.0 if mirror_x else 1.0
        surge_m = bump(t, 0.0, 0.22, 0.85)
        sc_m = 1.0 + surge_m * 0.14
        bp["w_meniscus_back"] = (0.0, 0.0, 0.0, m_flip * sc_m, sc_m * 0.68)
        sc["w_meniscus_back"] = (1, 1, 1, 0.88)
        bp["w_meniscus_front"] = (0.0, 0.0, 0.0, m_flip * sc_m, sc_m * 0.68)
        sc["w_meniscus_front"] = (1, 1, 1, 0.96)
        for idx_r, ph in ((1, 0.0), (2, 0.35)):
            u = clamp((t / dur) + ph * 0.3, 0.0, 1.0)
            u_wave = 1.0 - (1.0 - u) ** 1.60
            sc_r = 0.76 + u_wave * 0.56
            env = math.sin(math.pi * (u ** 0.72))
            sx_flip = (-1.0 if (mirror_x and idx_r == 1) else 1.0)
            bp[f"w_ripple_back_{idx_r}"] = (0.0, 0.0, 0.0, sx_flip * sc_r, sc_r * 0.56)
            sc[f"w_ripple_back_{idx_r}"] = (1, 1, 1, env * 0.74)
            bp[f"w_ripple_front_{idx_r}"] = (0.0, 0.0, 0.0, sx_flip * sc_r, sc_r * 0.56)
            sc[f"w_ripple_front_{idx_r}"] = (1, 1, 1, env * 0.92)
        eval_burst_fx(bp, sc, t, 0.02, include_floor_debris=False, water_visible_in_track0=True)
        crest_a = bump(t, 0.0, 0.24, 0.88)
        sc["w_rear_wave"] = (1, 1, 1, crest_a * 0.92)
        sc["w_crest_l"] = (1, 1, 1, crest_a * 0.96)
        sc["w_crest_r"] = (1, 1, 1, crest_a * 0.96)
        sc["w_spray_c"] = (1, 1, 1, bump(t, 0.02, 0.22, 0.68) * 0.94)
        for fn in FLOOR_SLOTS:
            sc[fn] = (1, 1, 1, 0.0)
        return bp, sc

    def anim_base_floor(t: float):
        bp, sc = {}, {}
        bp["f_slab_l"] = (5.0, -6.0, 4.0, 1.0, 1.0)
        bp["f_slab_r"] = (-12.0, 8.0, 8.0, 1.0, 1.0)
        bp["f_slab_front"] = (0.0, 0.0, -4.0, 1.0, 1.0)
        bp["f_slab_back"] = (0.0, 0.0, 6.0, 1.0, 1.0)
        for wn in WATER_SLOTS:
            sc[wn] = (1, 1, 1, 0.0)
        for sn in ("f_crater", "f_slab_back", "f_slab_l", "f_slab_r", "f_slab_front"):
            sc[sn] = (1, 1, 1, 1.0)
        for sn in ("f_dust_l", "f_dust_r", "f_shard_1", "f_shard_2", "f_shard_3", "f_shard_4", "f_shard_5"):
            sc[sn] = (1, 1, 1, 0.0)
        return bp, sc

    def anim_base_floor_burst(t: float):
        bp, sc = {}, {}
        for wn in WATER_SLOTS:
            sc[wn] = (1, 1, 1, 0.0)
        eval_burst_fx(bp, sc, t, 0.04, include_floor_debris=True, water_visible_in_track0=False)
        ca = smoothstep(seg(t, 0.0, 0.14))
        for sn in ("f_crater", "f_slab_back", "f_slab_l", "f_slab_r", "f_slab_front"):
            sc[sn] = (1, 1, 1, ca)
        dust_a = bump(t, 0.04, 0.24, 0.82) * 0.52
        sc["f_dust_l"] = (1, 1, 1, dust_a)
        sc["f_dust_r"] = (1, 1, 1, dust_a)
        for idx_s, tl in enumerate((0.06, 0.08, 0.10, 0.12, 0.14), 1):
            sc[f"f_shard_{idx_s}"] = (1, 1, 1, hold(t, tl, tl + 0.04, tl + 0.65, tl + 0.84))
        return bp, sc

    # --- TRACK 0 ACTION ANIMATIONS (V3 BIOMECHANICAL OVERLAPPING ACTION & FOLLOW-THROUGH!) ---
    sign = -1.0 if mirror_x else 1.0
    dir_x = -1.0 if mirror_x else 1.0

    def anim_idle(t: float):
        dur = 2.6 if is_ink_arm else 3.0
        bp = eval_fk_wave(bones, t, dur, cycles=1.0, phase=1.4 if is_ink_arm else 0.2,
                          base_amp=4.2 if is_ink_arm else 3.4, tip_amp=13.5 if is_ink_arm else 10.5,
                          wave_k=5.4 if is_ink_arm else 4.6, sign=sign, harmonic2=0.34, harmonic3=0.14)
        # Subtle living aquatic bobbing on base root so tentacle breathes in the lake/crater!
        bob_y = math.sin(TAU * t / dur - 0.4) * (6.5 if is_ink_arm else 5.5)
        bob_x = math.sin(TAU * t / dur + 0.9) * (3.5 * dir_x)
        bp["emerge_rig"] = (0.0, bob_x, bob_y, 1.0, 1.0)
        sc = {"tentacle_glow": (1, 1, 1, 0.26 + 0.20 * math.sin(TAU * t / dur) + 0.06 * math.sin(2.0 * TAU * t / dur))} if is_ink_arm else {}
        hide_slam_fx(sc)
        return bp, sc

    def anim_emerge(t: float):
        dur = 0.95
        # Explosive water/stone burst with towering +34px overshoot peak + cascading whip uncoil + damped settle!
        rise_y = eval_scalar_keyframes([
            (0.00, -620.0, "smooth"),
            (0.56, 36.0, "out_cubic"),
            (0.76, -12.0, "smoother"),
            (0.95, 0.0, "smoother"),
        ], t)
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            t_b = clamp(t - 0.12 * (u ** 1.2), 0.0, dur)
            r_b = ease_out_cubic(seg(t_b, 0.0, 0.62))
            whip_snap = spring_osc(t_b - 0.50, 6.0, 2.8) * (1.0 - smoothstep(seg(t, 0.84, 0.95)))
            uncoil = (1.0 - r_b) * (11.0 * math.sin(5.2 * u - 1.1) + 8.5 * u)
            bp[bn] = (sign * (uncoil + whip_snap * (4.0 + 9.0 * u)), 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, 0.0, rise_y, 1.0, 1.0)
        sc = {"tentacle_glow": (1, 1, 1, smoothstep(seg(t, 0.05, 0.55)) * 0.42)} if is_ink_arm else {}
        hide_slam_fx(sc)
        return bp, sc

    def anim_hurt(t: float):
        dur = 0.55
        # True root-to-tip whiplash recoil shockwave!
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            t_b = max(0.0, t - 0.095 * (u ** 1.15))
            hit = bump(t_b, 0.0, 0.10, 0.44)
            vib = spring_osc(t_b, 8.2, 11.5) * (1.0 - smoothstep(seg(t, 0.45, 0.55)))
            bp[bn] = (sign * (-hit * (7.5 + 10.5 * u) + vib * (3.5 + 7.0 * u)), 0.0, 0.0, 1.0, 1.0)
        kick_x = -dir_x * bump(t, 0.0, 0.09, 0.42) * 20.0
        kick_y = -bump(t, 0.0, 0.09, 0.42) * 16.0
        bp["emerge_rig"] = (0.0, kick_x, kick_y, 1.0, 1.0)
        sc = {"tentacle_glow": (1, 1, 1, 0.1)} if is_ink_arm else {}
        hide_slam_fx(sc)
        return bp, sc

    def anim_retreat_die(t: float):
        dur = 1.10
        # Sharp death convulsion upward -> traveling S-spasm -> limp gravity plunge into abyss!
        dy = eval_scalar_keyframes([
            (0.00, 0.0, "smooth"),
            (0.16, 32.0, "out_cubic"),
            (0.32, 10.0, "smooth"),
            (1.06, -650.0, "in_cubic"),
        ], t)
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            t_b = max(0.0, t - 0.08 * u)
            spasm = bump(t_b, 0.0, 0.16, 0.44) * math.sin(TAU * 2.2 * t_b - 5.5 * u)
            limp = smootherstep(seg(t_b, 0.28, 1.04))
            bp[bn] = (sign * (spasm * (9.0 + 13.0 * u) + limp * (7.0 * math.sin(4.8 * u) + 9.0 * u)), 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, 0.0, dy, 1.0, 1.0)
        sc = {"tentacle_glow": (1, 1, 1, 0.0)} if is_ink_arm else {}
        hide_slam_fx(sc)
        return bp, sc

    sb.sample_animation("base_water", 2.6, True, anim_base_water)
    sb.sample_animation("base_water_burst", 0.95, False, anim_base_water_burst)
    sb.sample_animation("base_floor", 0.5, True, anim_base_floor)
    sb.sample_animation("base_floor_burst", 1.00, False, anim_base_floor_burst)
    sb.sample_animation("idle_loop", 2.6 if is_ink_arm else 3.0, True, anim_idle)
    sb.sample_animation("emerge", 0.95, False, anim_emerge)
    sb.sample_animation("hurt", 0.55, False, anim_hurt)
    sb.sample_animation("retreat_die", 1.10, False, anim_retreat_die)

    if not is_ink_arm:
        # LEFT CRUSHING TENTACLE — V3 FLUID 6-STAGE OVERHEAD AXE-SLAM WITH TRUE PER-JOINT WHIP CASCADE (`attack_hit` @ 0.68s)
        slam_kf = [
            (0.00, rest_angs, "smooth"),
            (0.13, ang_crouch, "out_quad"),     # Anticipatory compression dip into water!
            (0.45, ang_wind, "out_cubic"),      # Towering open overhead axe windup!
            (0.51, ang_wind, "smooth"),         # Tense apex anticipation hang (ripple adds live strain!)
            (0.60, ang_drop, "in_quad"),        # Base whips forward-down while distal club lags overhead!
            (0.675, ang_slam, "snap_slam"),     # Club crashes FLAT across center bridge deck right at 0.68s!
            (0.84, ang_slam, "smooth"),         # 0.16s heavy pinned bridge hitstop (damped impact tremor!)
            (1.14, ang_peel, "out_quad"),       # Base lifts FIRST while heavy tip drags sticky across stones!
            (1.42, ang_over, "out_cubic"),      # Elastic upright overshoot rebound!
            (1.65, rest_angs, "smoother"),      # Smooth glide back into living idle!
        ]
        slam_rx = [
            (0.00, 0.0, "smooth"),
            (0.13, -10.0 * dir_x, "out_quad"),
            (0.45, -24.0 * dir_x, "out_cubic"),
            (0.51, -26.0 * dir_x, "smooth"),
            (0.675, slam_lunge_x, "snap_slam"),
            (0.84, slam_lunge_x, "smooth"),
            (1.14, 10.0 * dir_x, "smooth"),
            (1.42, -8.0 * dir_x, "out_cubic"),
            (1.65, 0.0, "smoother"),
        ]
        slam_ry = [
            (0.00, 0.0, "smooth"),
            (0.13, -22.0, "out_quad"),
            (0.45, 58.0, "out_cubic"),
            (0.51, 62.0, "smooth"),
            (0.60, 26.0, "in_quad"),
            (0.675, slam_lunge_y, "snap_slam"),
            (0.84, slam_lunge_y - 3.0, "smooth"),
            (1.14, 16.0, "out_quad"),
            (1.42, 14.0, "out_cubic"),
            (1.65, 0.0, "smoother"),
        ]

        def slam_lag(t: float, u: float) -> float:
            # Sinuous tip drag on windup & peel-off, crisp 0.026s tip-slap follow-through on bridge contact!
            if t < 0.50:
                return 0.105 * (u ** 1.25)
            if t < 0.68:
                # Tightens smoothly to 0.026s distal whip-slap right at impact!
                w = smoothstep(seg(t, 0.50, 0.66))
                return ((1.0 - w) * 0.095 + w * 0.026) * (u ** 1.35)
            if t < 0.84:
                return 0.022 * (u ** 1.5)
            return 0.145 * (u ** 1.20)

        def slam_ripple(t: float, u: float) -> float:
            # Apex muscle tremor + post-impact traveling fleshy shockwave down the club!
            apex_trem = bump(t, 0.40, 0.48, 0.55) * math.sin(TAU * 16.0 * t - 3.0 * u) * (1.2 + 2.8 * u)
            hit_dt = t - (0.675 + 0.024 * (u ** 1.4))
            hit_shock = spring_osc(hit_dt, 14.5, 19.0) * (1.0 - smoothstep(seg(t, 0.84, 0.94))) * (1.6 + 4.8 * u)
            settle_wave = spring_osc(t - 1.32, 6.0, 2.6) * (2.5 * (u ** 1.3))
            return sign * (apex_trem + hit_shock + settle_wave)

        def anim_heavy_slam(t: float):
            cur = eval_cascaded_fk(slam_kf, t, n_bones, dur=1.65, lag_fn=slam_lag, ripple_fn=slam_ripple)
            bp = solve_fk_world_angles(bones, rest_angs, cur)
            bp["emerge_rig"] = (0.0, eval_scalar_keyframes(slam_rx, t), eval_scalar_keyframes(slam_ry, t), 1.0, 1.0)
            sc: dict = {}
            eval_bridge_slam_impact(bp, sc, t, 0.68, intensity=1.22, show_ink=False)
            return bp, sc

        guard_kf = [
            (0.00, rest_angs, "smooth"),
            (0.16, ang_flare, "out_quad"),      # Anticipation flare outward before clamping into armor coil!
            (0.42, ang_guard, "out_cubic"),     # Heavy cascading coil lock-in!
            (0.86, ang_guard, "smooth"),        # Living defensive heave (driven by ripple!)
            (1.10, ang_over, "out_cubic"),      # Springy upright uncoil overshoot!
            (1.25, rest_angs, "smoother"),
        ]
        guard_ry = [
            (0.00, 0.0, "smooth"),
            (0.16, 18.0, "out_quad"),
            (0.42, 34.0, "out_cubic"),
            (0.62, 22.0, "smoother"),
            (0.86, 30.0, "smoother"),
            (1.10, 12.0, "out_cubic"),
            (1.25, 0.0, "smoother"),
        ]

        def anim_guard_coil(t: float):
            cur = eval_cascaded_fk(
                guard_kf, t, n_bones, dur=1.25,
                lag_fn=lambda _t, u: 0.09 * (u ** 1.2),
                ripple_fn=lambda tt, u: sign * (
                    spring_osc(tt - 0.40, 10.0, 8.5) * (2.0 + 4.5 * u)
                    + bump(tt, 0.36, 0.62, 0.90) * math.sin(TAU * 2.2 * tt - 4.0 * u) * (2.5 + 3.5 * u)
                ),
            )
            bp = solve_fk_world_angles(bones, rest_angs, cur)
            bp["emerge_rig"] = (0.0, -dir_x * bump(t, 0.24, 0.56, 0.96) * 14.0, eval_scalar_keyframes(guard_ry, t), 1.0, 1.0)
            sc: dict = {}
            hide_slam_fx(sc)
            return bp, sc

        sb.sample_animation("heavy_slam", 1.65, False, anim_heavy_slam, events=[(0.68, "attack_hit")])
        sb.sample_animation("guard_coil", 1.25, False, anim_guard_coil)
    else:
        # RIGHT INK-GLAND WHIP TENTACLE — V3 CONTRASTING 2-HIT BULLWHIP COMBO (`hit_1` @ 0.46s, `hit_2` @ 0.92s)
        whip_kf = [
            (0.00, rest_angs, "smooth"),
            (0.09, ang_crouch, "out_quad"),     # Snappy dip anticipation
            (0.28, ang_wind, "out_cubic"),      # Compact whip-back load
            (0.37, ang_drop, "in_quad"),        # Whip S-curve crack!
            (0.455, ang_slap1, "snap_slam"),    # HIT 1: Fast forehand mid-bridge whip-lash with flicked tip!
            (0.52, ang_slap1, "smooth"),        # Brief crisp 0.065s hitstop
            (0.75, ang_wind2, "out_cubic"),     # Dramatic HIGH figure-8 overhead windup (bigger than Hit 1!)
            (0.84, ang_drop, "in_quad"),        # Full-power descending S-whip!
            (0.915, ang_slam, "snap_slam"),     # HIT 2: Full-extension deep center-bridge crushing whip-slam!
            (1.06, ang_slam, "smooth"),         # 0.145s heavy pinned bridge hitstop!
            (1.30, ang_peel, "out_quad"),       # Sucker-dragging base-first peel-off!
            (1.48, ang_over, "out_cubic"),      # Elastic upright whip overshoot!
            (1.60, rest_angs, "smoother"),
        ]
        whip_rx = [
            (0.00, 0.0, "smooth"),
            (0.09, -8.0 * dir_x, "out_quad"),
            (0.28, -22.0 * dir_x, "out_cubic"),
            (0.455, slam_lunge_x * 0.78, "snap_slam"),
            (0.52, slam_lunge_x * 0.72, "smooth"),
            (0.75, -30.0 * dir_x, "out_cubic"),
            (0.915, slam_lunge_x, "snap_slam"),
            (1.06, slam_lunge_x, "smooth"),
            (1.30, 8.0 * dir_x, "smooth"),
            (1.48, -8.0 * dir_x, "out_cubic"),
            (1.60, 0.0, "smoother"),
        ]
        whip_ry = [
            (0.00, 0.0, "smooth"),
            (0.09, -18.0, "out_quad"),
            (0.28, 44.0, "out_cubic"),
            (0.455, 4.0, "snap_slam"),
            (0.52, 2.0, "smooth"),
            (0.75, 66.0, "out_cubic"),
            (0.84, 24.0, "in_quad"),
            (0.915, slam_lunge_y, "snap_slam"),
            (1.06, slam_lunge_y - 3.0, "smooth"),
            (1.30, 16.0, "out_quad"),
            (1.48, 12.0, "out_cubic"),
            (1.60, 0.0, "smoother"),
        ]

        def whip_lag(t: float, u: float) -> float:
            # Faster nimble bullwhip cascade on Strike 1, huge looping overhead drag on Rebound, sticky peel on Recovery!
            if t < 0.34:
                return 0.085 * (u ** 1.25)
            if t < 0.52:
                return 0.032 * (u ** 1.45)
            if t < 0.78:
                return 0.115 * (u ** 1.20)
            if t < 1.06:
                return 0.028 * (u ** 1.45)
            return 0.140 * (u ** 1.20)

        def whip_ripple(t: float, u: float) -> float:
            h1 = spring_osc(t - (0.455 + 0.022 * u), 18.0, 22.0) * (1.0 - smoothstep(seg(t, 0.52, 0.60))) * (1.5 + 4.0 * u)
            h2 = spring_osc(t - (0.915 + 0.024 * u), 14.5, 20.0) * (1.0 - smoothstep(seg(t, 1.06, 1.16))) * (1.8 + 5.2 * u)
            settle = spring_osc(t - 1.40, 6.5, 3.0) * (2.6 * (u ** 1.3))
            return sign * (h1 + h2 + settle)

        def anim_whip_lash(t: float):
            cur = eval_cascaded_fk(whip_kf, t, n_bones, dur=1.60, lag_fn=whip_lag, ripple_fn=whip_ripple)
            bp = solve_fk_world_angles(bones, rest_angs, cur)
            bp["emerge_rig"] = (0.0, eval_scalar_keyframes(whip_rx, t), eval_scalar_keyframes(whip_ry, t), 1.0, 1.0)
            sc: dict = {}
            if t < 0.88:
                eval_bridge_slam_impact(bp, sc, t, 0.46, intensity=0.86, show_ink=True)
            else:
                eval_bridge_slam_impact(bp, sc, t, 0.92, intensity=1.18, show_ink=True)
            glow_a = clamp(0.28 + bump(t, 0.34, 0.46, 0.62) * 0.72 + bump(t, 0.78, 0.92, 1.24) * 0.72, 0.0, 1.0)
            sc["tentacle_glow"] = (1, 1, 1, glow_a)
            return bp, sc

        bolster_kf = [
            (0.00, rest_angs, "smooth"),
            (0.22, ang_crouch, "out_quad"),     # Coils low to pressurize ink glands!
            (0.62, ang_bolster, "out_cubic"),   # Surges towering high in undulating wave!
            (0.96, ang_over, "smoother"),
            (1.30, rest_angs, "smoother"),
        ]
        bolster_ry = [
            (0.00, 0.0, "smooth"),
            (0.22, -22.0, "out_quad"),
            (0.62, 52.0, "out_cubic"),
            (0.96, 18.0, "smoother"),
            (1.30, 0.0, "smoother"),
        ]

        def anim_ink_bolster(t: float):
            pulse = bump(t, 0.28, 0.44, 0.60) * 0.6 + bump(t, 0.50, 0.64, 1.04)
            cur = eval_cascaded_fk(
                bolster_kf, t, n_bones, dur=1.30,
                lag_fn=lambda _t, u: 0.11 * (u ** 1.15),
                ripple_fn=lambda tt, u: sign * math.sin(TAU * 3.0 * tt - 5.2 * u) * bump(tt, 0.24, 0.62, 1.12) * (3.0 + 7.0 * u),
            )
            bp = solve_fk_world_angles(bones, rest_angs, cur)
            bp["emerge_rig"] = (0.0, 0.0, eval_scalar_keyframes(bolster_ry, t), 1.0, 1.0)
            sc: dict = {"tentacle_glow": (1, 1, 1, clamp(0.26 + pulse * 0.74, 0.0, 1.0))}
            hide_slam_fx(sc)
            return bp, sc

        sb.sample_animation("whip_lash", 1.60, False, anim_whip_lash, events=[(0.46, "hit_1"), (0.92, "hit_2")])
        sb.sample_animation("ink_bolster", 1.30, False, anim_ink_bolster, events=[(0.62, "ink_pulse")])

    # --- SHARED HERO TENTACLE `charge_buff` (1.25s) & `stun_loop` (2.00s) ---
    # Channeling surge leaning inward toward Boss during INK_CHARGE turn!
    ang_charge_peak = [rest_angs[i] + sign * (14.0 + 18.0 * (i / (n_bones - 1))) for i in range(n_bones)]
    ang_charge_squat = [rest_angs[i] - sign * (10.0 + 12.0 * (i / (n_bones - 1))) for i in range(n_bones)]
    charge_buff_kf = [
        (0.00, rest_angs, "smooth"),
        (0.22, ang_charge_squat, "out_quad"),
        (0.58, ang_charge_peak, "out_cubic"),
        (0.94, rest_angs, "smoother"),
        (1.25, rest_angs, "smoother"),
    ]
    charge_buff_ry = [
        (0.00, 0.0, "smooth"),
        (0.22, -20.0, "out_quad"),
        (0.58, 46.0, "out_cubic"),
        (0.94, 14.0, "smoother"),
        (1.25, 0.0, "smoother"),
    ]

    def anim_charge_buff(t: float):
        pulse = bump(t, 0.24, 0.58, 1.06)
        cur = eval_cascaded_fk(
            charge_buff_kf, t, n_bones, dur=1.25,
            lag_fn=lambda _t, u: 0.105 * (u ** 1.15),
            ripple_fn=lambda tt, u: sign * math.sin(TAU * 3.2 * tt - 5.0 * u) * pulse * (3.2 + 7.5 * u),
        )
        bp = solve_fk_world_angles(bones, rest_angs, cur)
        bp["emerge_rig"] = (0.0, dir_x * pulse * 24.0, eval_scalar_keyframes(charge_buff_ry, t), 1.0, 1.0)
        sc: dict = {"tentacle_glow": (1, 1, 1, clamp(0.28 + pulse * 0.72, 0.0, 1.0))} if is_ink_arm else {}
        hide_slam_fx(sc)
        return bp, sc

    def anim_hero_stun_loop(t: float):
        dur = 2.00
        cyc = TAU * t / dur
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            droop = -sign * (10.0 + 18.0 * (u ** 1.15))
            daze = sign * math.sin(cyc - 3.6 * u) * (2.5 + 5.0 * u)
            bp[bn] = (droop + daze, 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, math.sin(cyc) * 6.0 * dir_x, -56.0 + math.sin(2.0 * cyc) * 6.0, 1.0, 1.0)
        sc: dict = {"tentacle_glow": (1, 1, 1, 0.06)} if is_ink_arm else {}
        hide_slam_fx(sc)
        return bp, sc

    sb.sample_animation("charge_buff", 1.25, False, anim_charge_buff, events=[(0.58, "buff_channel")])
    sb.sample_animation("stun_loop", 2.00, True, anim_hero_stun_loop)

    atlas_imgs = {"tentacle": tentacle_im}
    if is_ink_arm and glow_im is not None:
        atlas_imgs["ink_blister_glow"] = glow_im
    atlas_imgs.update(water_imgs)
    atlas_imgs.update(breach_imgs)
    atlas_imgs.update(slam_imgs)
    sb.export(atlas_imgs, ANIM_ROOT / rig_name, default_mix=0.12)


# ==============================================================================
# RIG 5: SHARED 3-VARIANT BRIDGE-BREACHING SMALL TENTACLE (`abyssal_small_tentacle`)
# Track 2 selects variant (`skin_spike` Attack | `skin_shell` Guard Boss | `skin_gland` Buff Boss)
# Track 1 runs bridge fracture (`base_floor_burst` -> `base_floor`)
# Track 0 runs crisp bridge-slapping attacks (`slam_attack`, `double_slap`), `guard_brace`, `buff_pulse`, `emerge`, `hurt`, `retreat_die`!
# ==============================================================================
def build_small_tentacle_rig(
    small_imgs: dict[str, Image.Image],
    breach_imgs: dict[str, Image.Image],
    slam_imgs: dict[str, Image.Image],
):
    sb = SpineBuilder("abyssal_small_tentacle")
    sb.add_bone("root", None, 0.0, 0.0)
    sb.add_bone("emerge_rig", "root", 0.0, 0.0)
    sb.add_bone("fx_root", "root", 0.0, 0.0)

    n_bones = 10
    arm_scale = 0.62
    root_xy = (0.0, -22.0)

    spike_pts, spike_u = extract_tentacle_centerline(small_imgs["spike"], n_bones=n_bones)
    shell_pts, shell_u = extract_tentacle_centerline(small_imgs["shell"], n_bones=n_bones)
    gland_pts, gland_u = extract_tentacle_centerline(small_imgs["gland"], n_bones=n_bones)

    # Precompute small tentacle V3 organic poses:
    #   - Smooth unkinked arch (max 28°/bone!) surging +22px forward over crater rim and slapping DEAD FLAT onto stone bridge!
    #   - High open axe windup + anticipation crouch + distinct forehand flick + elastic overshoot!
    s_crouch = resample_angles([88.0, 92.0, 90.0, 80.0, 62.0, 36.0, 4.0, -28.0, -56.0, -74.0], n_bones)
    s_wind = resample_angles([118.0, 126.0, 128.0, 120.0, 102.0, 78.0, 48.0, 18.0, -8.0, -24.0], n_bones)
    s_wind2 = resample_angles([122.0, 132.0, 134.0, 124.0, 106.0, 80.0, 50.0, 20.0, -6.0, -22.0], n_bones)
    s_drop = resample_angles([60.0, 30.0, 2.0, -14.0, 4.0, 30.0, 56.0, 74.0, 72.0, 56.0], n_bones)
    s_slam = resample_angles([60.0, 32.0, 0.0, -30.0, -44.0, -34.0, -14.0, -1.0, 1.0, -1.0], n_bones)
    s_slap1 = resample_angles([66.0, 40.0, 10.0, -18.0, -34.0, -24.0, -2.0, 10.0, 16.0, 10.0], n_bones)
    s_peel = resample_angles([84.0, 74.0, 56.0, 30.0, 4.0, -18.0, -26.0, -20.0, -6.0, 10.0], n_bones)
    s_over = resample_angles([100.0, 104.0, 102.0, 92.0, 76.0, 56.0, 32.0, 6.0, -16.0, -30.0], n_bones)
    s_flare = resample_angles([104.0, 108.0, 106.0, 96.0, 80.0, 56.0, 30.0, 6.0, -16.0, -34.0], n_bones)
    s_guard = resample_angles([104.0, 96.0, 80.0, 54.0, 18.0, -22.0, -64.0, -104.0, -138.0, -158.0], n_bones)
    s_buff = resample_angles([96.0, 102.0, 102.0, 94.0, 78.0, 58.0, 36.0, 14.0, -6.0, -22.0], n_bones)

    # Preview impact coordinate on stone bridge (including +20px forward crater lunge!)
    s_lunge_x, s_lunge_y = 20.0, 2.0
    rx0_s, ry0_s = spike_pts[0, 0], spike_pts[0, 1]
    j_prev = np.column_stack([root_xy[0] + (spike_pts[:, 0] - rx0_s) * arm_scale, root_xy[1] + (ry0_s - spike_pts[:, 1]) * arm_scale])
    _, lens_prev = compute_chain_kinematics(j_prev)
    slam_fk = eval_fk_world_points((root_xy[0] + s_lunge_x, root_xy[1] + s_lunge_y), lens_prev, s_slam)
    imp_pt = slam_fk[min(len(slam_fk) - 1, int(round(n_bones * 0.74)))]
    imp_x, imp_y = imp_pt[0], imp_pt[1] - 3.0
    print(f"SMALL_SLAM_FK: club_mid=({imp_x:.1f}, {imp_y:.1f}), tip=({slam_fk[-1][0]:.1f}, {slam_fk[-1][1]:.1f})")

    # Back crater & rear hinged slab
    sb.add_bone("f_crater", "fx_root", 0.0, -4.0)
    sb.add_bone("f_slab_back", "fx_root", 0.0, 18.0)
    sb.add_rigid_sprite("f_crater", "crater", "f_crater", breach_imgs["crater"],
                        (breach_imgs["crater"].width * 0.5, breach_imgs["crater"].height * 0.52), scale=0.68, color="ffffff00")
    sb.add_rigid_sprite("f_slab_back", "slab_back", "f_slab_back", breach_imgs["slab_back"],
                        (breach_imgs["slab_back"].width * 0.5, breach_imgs["slab_back"].height * 0.85), scale=0.54, color="ffffff00")

    # Under-club bridge-slap cracks & shockwave ring!
    sb.add_bone("slam_fx_root", "root", imp_x, imp_y)
    sb.add_bone("slam_cracks", "slam_fx_root", imp_x, imp_y)
    sb.add_bone("slam_shockwave", "slam_fx_root", imp_x, imp_y + 3.0)
    sb.add_rigid_sprite("slam_cracks", "impact_cracks", "slam_cracks", slam_imgs["impact_cracks"],
                        (slam_imgs["impact_cracks"].width * 0.5, slam_imgs["impact_cracks"].height * 0.52), scale=0.70, color="ffffff00")
    sb.add_rigid_sprite("slam_shockwave", "shockwave_ring", "slam_shockwave", slam_imgs["shockwave_ring"],
                        (slam_imgs["shockwave_ring"].width * 0.5, slam_imgs["shockwave_ring"].height * 0.52), scale=0.72, color="ffffff00")

    # Strike-side-open hardware `emerge_clip` (hugs small crater [-900..+46], opens wide down to -420 right past crater lip at +54!)
    root_bid = sb.bone_idx["root"]
    men_xs = np.linspace(-110.0, 46.0, 7)
    men_pts = [[float(dx), float(-4.0 - 8.0 * math.cos(math.pi * 0.5 * clamp(dx / 110.0, -1.0, 1.0)))] for dx in men_xs]
    emerge_poly = np.array(
        [[-900.0, -4.0]] + men_pts + [[54.0, -420.0], [900.0, -420.0], [900.0, 900.0], [-900.0, 900.0]],
        dtype=np.float64,
    )
    sb.add_clipping_slot("emerge_clip", "small_gland_glow", "root", emerge_poly, [[(root_bid, 1.0)] for _ in range(len(emerge_poly))])

    # Shared 10-bone pure-rotational FK chain (`small_spike` builds bones; `small_shell` & `small_gland` bind to same bones!)
    bones, rest_angs, _, b_indices = add_fk_tentacle_ex(
        sb, "small_spike", "emerge_rig", "spike", small_imgs["spike"], spike_pts, spike_u,
        root_world_xy=root_xy, scale=arm_scale, mirror_x=False, spacing=12,
    )
    bind_mesh_to_fk_chain(
        sb, "small_shell", "shell", bones[0], b_indices, small_imgs["shell"], shell_pts, shell_u,
        root_world_xy=root_xy, scale=arm_scale, mirror_x=False, spacing=12, slot_color="ffffff00",
    )
    bind_mesh_to_fk_chain(
        sb, "small_shell_sheen", "shell_sheen", bones[0], b_indices, small_imgs["shell_sheen"], shell_pts, shell_u,
        root_world_xy=root_xy, scale=arm_scale, mirror_x=False, spacing=12, slot_color="ffffff00", blend="additive",
    )
    bind_mesh_to_fk_chain(
        sb, "small_gland", "gland", bones[0], b_indices, small_imgs["gland"], gland_pts, gland_u,
        root_world_xy=root_xy, scale=arm_scale, mirror_x=False, spacing=12, slot_color="ffffff00",
    )
    bind_mesh_to_fk_chain(
        sb, "small_gland_glow", "gland_glow", bones[0], b_indices, small_imgs["gland_glow"], gland_pts, gland_u,
        root_world_xy=root_xy, scale=arm_scale, mirror_x=False, spacing=12, slot_color="ffffff00", blend="additive",
    )

    # Front hinged breach slabs + shards + breach dust
    sb.add_bone("f_slab_l", "fx_root", -66.0, -6.0)
    sb.add_bone("f_slab_r", "fx_root", 66.0, -6.0)
    sb.add_bone("f_slab_front", "fx_root", 0.0, -28.0)
    sb.add_rigid_sprite("f_slab_l", "slab_l", "f_slab_l", breach_imgs["slab_l"], (breach_imgs["slab_l"].width * 0.72, breach_imgs["slab_l"].height * 0.80), scale=0.45, color="ffffff00")
    sb.add_rigid_sprite("f_slab_r", "slab_r", "f_slab_r", breach_imgs["slab_r"], (breach_imgs["slab_r"].width * 0.28, breach_imgs["slab_r"].height * 0.80), scale=0.45, color="ffffff00")
    sb.add_rigid_sprite("f_slab_front", "slab_front", "f_slab_front", breach_imgs["slab_front"], (breach_imgs["slab_front"].width * 0.5, breach_imgs["slab_front"].height * 0.58), scale=0.48, color="ffffff00")

    sb.add_bone("f_dust_l", "fx_root", -38.0, -8.0)
    sb.add_bone("f_dust_r", "fx_root", 38.0, -8.0)
    sb.add_rigid_sprite("f_dust_l", "dust_cloud", "f_dust_l", breach_imgs["dust_cloud"], (breach_imgs["dust_cloud"].width * 0.5, breach_imgs["dust_cloud"].height * 0.6), scale=0.58, color="ffffff00")
    sb.add_rigid_sprite("f_dust_r", "dust_cloud", "f_dust_r", breach_imgs["dust_cloud"], (breach_imgs["dust_cloud"].width * 0.5, breach_imgs["dust_cloud"].height * 0.6), scale=0.58, color="ffffff00")

    for si in range(1, 6):
        sb.add_bone(f"f_shard_{si}", "fx_root", 0.0, 4.0)
        sim = breach_imgs[f"shard_{si}"]
        sb.add_rigid_sprite(f"f_shard_{si}", f"shard_{si}", f"f_shard_{si}", sim, (sim.width * 0.5, sim.height * 0.5), scale=0.72, color="ffffff00")

    # Front bridge-slap dust plumes, 3 bouncing chips, and buff/ink energy burst!
    sb.add_bone("slam_dust_l", "slam_fx_root", imp_x - 24.0, imp_y + 8.0)
    sb.add_bone("slam_dust_r", "slam_fx_root", imp_x + 24.0, imp_y + 8.0)
    sb.add_rigid_sprite("slam_dust_l", "dust_l", "slam_dust_l", slam_imgs["dust_l"], (slam_imgs["dust_l"].width * 0.72, slam_imgs["dust_l"].height * 0.86), scale=0.64, color="ffffff00")
    sb.add_rigid_sprite("slam_dust_r", "dust_r", "slam_dust_r", slam_imgs["dust_r"], (slam_imgs["dust_r"].width * 0.28, slam_imgs["dust_r"].height * 0.86), scale=0.64, color="ffffff00")
    for ci in range(1, 4):
        sb.add_bone(f"slam_chip_{ci}", "slam_fx_root", imp_x, imp_y + 8.0)
        cim = slam_imgs[f"chip_{ci}"]
        sb.add_rigid_sprite(f"slam_chip_{ci}", f"chip_{ci}", f"slam_chip_{ci}", cim, (cim.width * 0.5, cim.height * 0.5), scale=0.75, color="ffffff00")
    sb.add_bone("buff_burst", "root", 30.0, 210.0)
    sb.add_rigid_sprite("buff_burst", "ink_slash", "buff_burst", slam_imgs["ink_slash"],
                        (slam_imgs["ink_slash"].width * 0.5, slam_imgs["ink_slash"].height * 0.5), scale=0.82, color="ffffff00", blend="additive")

    SLAM_SLOTS = ("slam_cracks", "slam_shockwave", "slam_dust_l", "slam_dust_r", "slam_chip_1", "slam_chip_2", "slam_chip_3", "buff_burst")

    def hide_small_fx(sc: dict):
        for sn in SLAM_SLOTS:
            sc[sn] = (1, 1, 1, 0.0)
        sc["small_shell_sheen"] = (1, 1, 1, 0.0)
        sc["small_gland_glow"] = (1, 1, 1, 0.0)

    def eval_small_impact(bp: dict, sc: dict, t: float, t_hit: float, intensity: float = 1.0):
        dt = t - t_hit
        if dt < -0.015 or dt > 0.76:
            for sn in SLAM_SLOTS:
                sc[sn] = (1, 1, 1, 0.0)
            return
        sc["slam_cracks"] = (1, 1, 1, hold(t, t_hit - 0.010, t_hit + 0.010, t_hit + 0.44, t_hit + 0.72) * 0.95)
        bp["slam_cracks"] = (0.0, 0.0, 0.0, 0.94 * intensity, 0.62 * intensity)
        sw_u = seg(dt, 0.0, 0.25)
        sw_sc = (0.46 + ease_out_cubic(sw_u) * 0.92) * intensity
        bp["slam_shockwave"] = (0.0, 0.0, 0.0, sw_sc, sw_sc * 0.68)
        sc["slam_shockwave"] = (1, 1, 1, hold(t, t_hit - 0.010, t_hit + 0.014, t_hit + 0.10, t_hit + 0.25) * 0.94)
        du = seg(dt, 0.0, 0.46)
        du_fast = ease_out_cubic(du)
        d_sc = (0.74 + du_fast * 0.48) * intensity
        d_a = hold(t, t_hit - 0.005, t_hit + 0.022, t_hit + 0.16, t_hit + 0.46) * 0.88
        bp["slam_dust_l"] = (du_fast * 18.0, -du_fast * 88.0 * intensity, ease_out_cubic(seg(dt, 0.0, 0.32)) * 26.0, d_sc, d_sc)
        bp["slam_dust_r"] = (-du_fast * 18.0, du_fast * 88.0 * intensity, ease_out_cubic(seg(dt, 0.0, 0.32)) * 26.0, d_sc, d_sc)
        sc["slam_dust_l"] = (1, 1, 1, d_a)
        sc["slam_dust_r"] = (1, 1, 1, d_a)
        for ci, (tl, lf, vx, vy, spin) in enumerate([
            (t_hit, 0.50, -155.0 * intensity, 360.0 * intensity, 440.0),
            (t_hit + 0.01, 0.54, 165.0 * intensity, 385.0 * intensity, -460.0),
            (t_hit + 0.02, 0.46, 38.0 * intensity, 425.0 * intensity, 380.0),
        ], 1):
            rc, cx, cy, ca = eval_parabolic_arc(t, tl, lf, vx, vy, g=1420.0, floor_y=-10.0, bounce_restitution=0.35, spin_deg_per_s=spin)
            bp[f"slam_chip_{ci}"] = (rc, cx, cy, 1.0, 1.0)
            sc[f"slam_chip_{ci}"] = (1, 1, 1, ca)
        sc["buff_burst"] = (1, 1, 1, 0.0)

    # Track 1: Floor Breach Crater
    def anim_base_floor(t: float):
        bp = {
            "f_slab_l": (4.0, -4.0, 3.0, 1.0, 1.0),
            "f_slab_r": (-11.0, 6.0, 6.0, 1.0, 1.0),
            "f_slab_front": (0.0, 0.0, -3.0, 1.0, 1.0),
            "f_slab_back": (0.0, 0.0, 4.0, 1.0, 1.0),
        }
        sc = {sn: (1, 1, 1, 1.0) for sn in ("f_crater", "f_slab_back", "f_slab_l", "f_slab_r", "f_slab_front")}
        for sn in ("f_dust_l", "f_dust_r", "f_shard_1", "f_shard_2", "f_shard_3", "f_shard_4", "f_shard_5"):
            sc[sn] = (1, 1, 1, 0.0)
        return bp, sc

    def anim_base_floor_burst(t: float):
        heave = smootherstep(seg(t, 0.02, 0.24))
        over = bump(t, 0.12, 0.24, 0.48)
        bp = {
            "f_slab_l": (heave * 4.0 + over * 10.0, -heave * 4.0, heave * 3.0 + over * 6.0, 1.0, 1.0),
            "f_slab_r": (-heave * 11.0 - over * 14.0, heave * 6.0, heave * 6.0 + over * 8.0, 1.0, 1.0),
            "f_slab_front": (-over * 7.0, 0.0, -heave * 3.0 + over * 7.0, 1.0, 1.0),
            "f_slab_back": (over * 9.0, 0.0, heave * 4.0 + over * 7.0, 1.0, 1.0),
        }
        dp = seg(t, 0.04, 0.78)
        bp["f_dust_l"] = (dp * 48.0, -dp * 68.0, smoothstep(dp) * 18.0, 1.0, 1.0)
        bp["f_dust_r"] = (-dp * 48.0, dp * 68.0, smoothstep(dp) * 18.0, 1.0, 1.0)
        ca = smoothstep(seg(t, 0.0, 0.12))
        sc = {sn: (1, 1, 1, ca) for sn in ("f_crater", "f_slab_back", "f_slab_l", "f_slab_r", "f_slab_front")}
        da = bump(t, 0.04, 0.22, 0.76) * 0.54
        sc["f_dust_l"] = (1, 1, 1, da)
        sc["f_dust_r"] = (1, 1, 1, da)
        for idx_s, (tl, lf, vx, vy, spin) in enumerate([
            (0.02, 0.74, -140.0, 350.0,  380.0),
            (0.04, 0.76,  150.0, 375.0, -410.0),
            (0.06, 0.70,  -80.0, 410.0,  460.0),
            (0.08, 0.72,   90.0, 395.0, -420.0),
            (0.10, 0.68,  -30.0, 320.0,  310.0),
        ], 1):
            r_s, sx, sy, sa = eval_parabolic_arc(t, tl, lf, vx, vy, g=1320.0, floor_y=-18.0, bounce_restitution=0.38, spin_deg_per_s=spin)
            bp[f"f_shard_{idx_s}"] = (r_s, sx, sy, 1.0, 1.0)
            sc[f"f_shard_{idx_s}"] = (1, 1, 1, sa)
        return bp, sc

    # Track 2: 3 Clean Variant Skins (ONLY toggle the 3 base meshes so Track 0 glow pulses never conflict!)
    def make_skin_anim(active: str):
        def _fn(t: float):
            return {}, {
                "small_spike": (1, 1, 1, 1.0 if active == "spike" else 0.0),
                "small_shell": (1, 1, 1, 1.0 if active == "shell" else 0.0),
                "small_gland": (1, 1, 1, 1.0 if active == "gland" else 0.0),
            }
        return _fn

    # Track 0 Actions (V3 BIOMECHANICAL OVERLAPPING ACTION & DISTINCT 3-VARIANT PERSONALITIES!)
    def anim_idle(t: float):
        dur = 2.4
        bp = eval_fk_wave(bones, t, dur, cycles=1.0, phase=0.5, base_amp=4.4, tip_amp=14.5,
                          wave_k=4.8, sign=1.0, harmonic2=0.36, harmonic3=0.14)
        bob_y = math.sin(TAU * t / dur - 0.35) * 4.5
        bp["emerge_rig"] = (0.0, math.sin(TAU * t / dur + 0.8) * 2.5, bob_y, 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        return bp, sc

    def anim_emerge(t: float):
        dur = 0.85
        rise_y = eval_scalar_keyframes([
            (0.00, -390.0, "smooth"),
            (0.48, 28.0, "out_cubic"),
            (0.68, -10.0, "smoother"),
            (0.85, 0.0, "smoother"),
        ], t)
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            t_b = clamp(t - 0.10 * (u ** 1.2), 0.0, dur)
            r_b = ease_out_cubic(seg(t_b, 0.0, 0.54))
            whip_snap = spring_osc(t_b - 0.44, 6.5, 3.2) * (1.0 - smoothstep(seg(t, 0.74, 0.85)))
            bp[bn] = ((1.0 - r_b) * (11.0 * math.sin(4.8 * u - 1.0) + 8.0 * u) + whip_snap * (4.0 + 9.0 * u), 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, 0.0, rise_y, 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        return bp, sc

    sm_slam_kf = [
        (0.00, rest_angs, "smooth"),
        (0.09, s_crouch, "out_quad"),       # Snappy anticipation dip into crater!
        (0.29, s_wind, "out_cubic"),        # High open overhead windup!
        (0.37, s_drop, "in_quad"),          # S-whip snap acceleration!
        (0.455, s_slam, "snap_slam"),       # Flat bridge slap right at 0.46s!
        (0.56, s_slam, "smooth"),           # 0.105s crisp pinned hitstop!
        (0.86, s_peel, "out_quad"),         # Base-first sticky peel-off!
        (1.08, s_over, "out_cubic"),        # Elastic upright overshoot!
        (1.25, rest_angs, "smoother"),
    ]
    sm_slam_rx = [
        (0.00, 0.0, "smooth"),
        (0.09, -6.0, "out_quad"),
        (0.29, -16.0, "out_cubic"),
        (0.455, s_lunge_x, "snap_slam"),
        (0.56, s_lunge_x, "smooth"),
        (0.86, 6.0, "smooth"),
        (1.08, -6.0, "out_cubic"),
        (1.25, 0.0, "smoother"),
    ]
    sm_slam_ry = [
        (0.00, 0.0, "smooth"),
        (0.09, -14.0, "out_quad"),
        (0.29, 34.0, "out_cubic"),
        (0.455, s_lunge_y, "snap_slam"),
        (0.56, s_lunge_y - 2.0, "smooth"),
        (0.86, 10.0, "out_quad"),
        (1.08, 10.0, "out_cubic"),
        (1.25, 0.0, "smoother"),
    ]

    def sm_slam_lag(t: float, u: float) -> float:
        if t < 0.34:
            return 0.085 * (u ** 1.25)
        if t < 0.56:
            return 0.024 * (u ** 1.45)
        return 0.115 * (u ** 1.20)

    def anim_slam_attack(t: float):
        cur = eval_cascaded_fk(
            sm_slam_kf, t, n_bones, dur=1.25, lag_fn=sm_slam_lag,
            ripple_fn=lambda tt, u: (
                spring_osc(tt - (0.455 + 0.02 * u), 16.5, 22.0) * (1.0 - smoothstep(seg(tt, 0.56, 0.65))) * (1.5 + 4.4 * u)
                + spring_osc(tt - 1.00, 7.0, 3.0) * (2.5 * (u ** 1.3))
            ),
        )
        bp = solve_fk_world_angles(bones, rest_angs, cur)
        bp["emerge_rig"] = (0.0, eval_scalar_keyframes(sm_slam_rx, t), eval_scalar_keyframes(sm_slam_ry, t), 1.0, 1.0)
        sc: dict = {"small_shell_sheen": (1, 1, 1, 0.0), "small_gland_glow": (1, 1, 1, 0.0)}
        eval_small_impact(bp, sc, t, 0.46, intensity=1.00)
        return bp, sc

    sm_dbl_kf = [
        (0.00, rest_angs, "smooth"),
        (0.08, s_crouch, "out_quad"),
        (0.24, s_wind, "out_cubic"),
        (0.31, s_drop, "in_quad"),
        (0.375, s_slap1, "snap_slam"),      # HIT 1: Snappy forehand bridge slap (distinct from Hit 2!)
        (0.43, s_slap1, "smooth"),          # Quick elastic rebound (NO frozen statue!)
        (0.64, s_wind2, "out_cubic"),       # Wide high overhead figure-8 whip reload!
        (0.72, s_drop, "in_quad"),
        (0.795, s_slam, "snap_slam"),       # HIT 2: Full-power flat bridge slam!
        (0.92, s_slam, "smooth"),           # Heavy pinned hitstop!
        (1.16, s_peel, "out_quad"),         # Base-first peel-off!
        (1.34, s_over, "out_cubic"),        # Elastic upright overshoot!
        (1.45, rest_angs, "smoother"),
    ]
    sm_dbl_rx = [
        (0.00, 0.0, "smooth"),
        (0.08, -5.0, "out_quad"),
        (0.24, -14.0, "out_cubic"),
        (0.375, s_lunge_x * 0.78, "snap_slam"),
        (0.43, s_lunge_x * 0.70, "smooth"),
        (0.64, -20.0, "out_cubic"),
        (0.795, s_lunge_x, "snap_slam"),
        (0.92, s_lunge_x, "smooth"),
        (1.16, 6.0, "smooth"),
        (1.34, -5.0, "out_cubic"),
        (1.45, 0.0, "smoother"),
    ]
    sm_dbl_ry = [
        (0.00, 0.0, "smooth"),
        (0.08, -12.0, "out_quad"),
        (0.24, 28.0, "out_cubic"),
        (0.375, -6.0, "snap_slam"),
        (0.43, -4.0, "smooth"),
        (0.64, 42.0, "out_cubic"),
        (0.795, s_lunge_y, "snap_slam"),
        (0.92, s_lunge_y - 2.0, "smooth"),
        (1.16, 10.0, "out_quad"),
        (1.34, 8.0, "out_cubic"),
        (1.45, 0.0, "smoother"),
    ]

    def sm_dbl_lag(t: float, u: float) -> float:
        if t < 0.29:
            return 0.072 * (u ** 1.25)
        if t < 0.43:
            return 0.024 * (u ** 1.45)
        if t < 0.68:
            return 0.095 * (u ** 1.20)
        if t < 0.92:
            return 0.024 * (u ** 1.45)
        return 0.115 * (u ** 1.20)

    def anim_double_slap(t: float):
        cur = eval_cascaded_fk(
            sm_dbl_kf, t, n_bones, dur=1.45, lag_fn=sm_dbl_lag,
            ripple_fn=lambda tt, u: (
                spring_osc(tt - (0.375 + 0.018 * u), 18.0, 24.0) * (1.0 - smoothstep(seg(tt, 0.43, 0.50))) * (1.5 + 3.8 * u)
                + spring_osc(tt - (0.795 + 0.020 * u), 16.0, 22.0) * (1.0 - smoothstep(seg(tt, 0.92, 1.00))) * (1.8 + 4.8 * u)
                + spring_osc(tt - 1.26, 7.0, 3.2) * (2.5 * (u ** 1.3))
            ),
        )
        bp = solve_fk_world_angles(bones, rest_angs, cur)
        bp["emerge_rig"] = (0.0, eval_scalar_keyframes(sm_dbl_rx, t), eval_scalar_keyframes(sm_dbl_ry, t), 1.0, 1.0)
        sc: dict = {"small_shell_sheen": (1, 1, 1, 0.0), "small_gland_glow": (1, 1, 1, 0.0)}
        if t < 0.74:
            eval_small_impact(bp, sc, t, 0.38, intensity=0.80)
        else:
            eval_small_impact(bp, sc, t, 0.80, intensity=1.06)
        return bp, sc

    sm_guard_kf = [
        (0.00, rest_angs, "smooth"),
        (0.16, s_flare, "out_quad"),        # Anticipation chest-flare BEFORE clamping shut!
        (0.38, s_guard, "out_cubic"),       # Heavy cascading carapace spiral lock-in!
        (0.56, s_guard, "smooth"),          # Living armored surge peak (`guard_pulse`!)
        (0.84, s_guard, "smooth"),
        (1.06, s_over, "out_cubic"),        # Springy upright uncoil overshoot!
        (1.20, rest_angs, "smoother"),
    ]
    sm_guard_ry = [
        (0.00, 0.0, "smooth"),
        (0.16, 14.0, "out_quad"),
        (0.38, 22.0, "out_cubic"),
        (0.56, 10.0, "out_quad"),
        (0.84, 20.0, "smoother"),
        (1.06, 10.0, "out_cubic"),
        (1.20, 0.0, "smoother"),
    ]

    def anim_guard_brace(t: float):
        flash = bump(t, 0.34, 0.56, 0.96)
        cur = eval_cascaded_fk(
            sm_guard_kf, t, n_bones, dur=1.20,
            lag_fn=lambda _t, u: 0.085 * (u ** 1.2),
            ripple_fn=lambda tt, u: (
                spring_osc(tt - 0.36, 11.0, 9.5) * (2.2 + 4.5 * u)
                + bump(tt, 0.34, 0.56, 0.92) * math.sin(TAU * 2.5 * tt - 4.2 * u) * (2.8 + 4.2 * u)
            ),
        )
        bp = solve_fk_world_angles(bones, rest_angs, cur)
        bp["emerge_rig"] = (0.0, -bump(t, 0.22, 0.56, 0.94) * 10.0, eval_scalar_keyframes(sm_guard_ry, t), 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        sc["small_shell_sheen"] = (1, 1, 1, clamp(flash * 0.96, 0.0, 1.0))
        return bp, sc

    sm_buff_kf = [
        (0.00, rest_angs, "smooth"),
        (0.24, s_crouch, "out_quad"),       # Deep coiling pressure squat gathering cyan charge!
        (0.58, s_buff, "out_cubic"),        # Explosive serpentine whip-uncoil launching biolume pulse!
        (0.94, s_over, "smoother"),         # Graceful floating wave settle!
        (1.25, rest_angs, "smoother"),
    ]
    sm_buff_ry = [
        (0.00, 0.0, "smooth"),
        (0.24, -18.0, "out_quad"),
        (0.58, 38.0, "out_cubic"),
        (0.94, 14.0, "smoother"),
        (1.25, 0.0, "smoother"),
    ]

    def anim_buff_pulse(t: float):
        pulse = bump(t, 0.24, 0.58, 1.08)
        cur = eval_cascaded_fk(
            sm_buff_kf, t, n_bones, dur=1.25,
            lag_fn=lambda _t, u: 0.115 * (u ** 1.15),
            ripple_fn=lambda tt, u: math.sin(TAU * 3.4 * tt - 5.4 * u) * bump(tt, 0.22, 0.58, 1.12) * (3.5 + 8.5 * u),
        )
        bp = solve_fk_world_angles(bones, rest_angs, cur)
        bp["emerge_rig"] = (0.0, bump(t, 0.36, 0.62, 0.98) * 12.0, eval_scalar_keyframes(sm_buff_ry, t), 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        sc["small_gland_glow"] = (1, 1, 1, clamp(pulse * 0.98, 0.0, 1.0))
        bu = seg(t, 0.46, 0.98)
        bu_fast = ease_out_cubic(bu)
        b_sc = 0.45 + bu_fast * 1.02
        bp["buff_burst"] = (bu_fast * 62.0, bu_fast * 130.0, bu_fast * 48.0, b_sc, b_sc)
        sc["buff_burst"] = (0.55, 0.98, 1.0, bump(t, 0.46, 0.60, 0.98) * 0.94)
        return bp, sc

    def anim_hurt(t: float):
        dur = 0.48
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            t_b = max(0.0, t - 0.08 * (u ** 1.15))
            hit = bump(t_b, 0.0, 0.09, 0.40)
            vib = spring_osc(t_b, 9.0, 13.0) * (1.0 - smoothstep(seg(t, 0.38, 0.48)))
            bp[bn] = (-hit * (7.5 + 10.0 * u) + vib * (3.5 + 6.5 * u), 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, -bump(t, 0.0, 0.08, 0.38) * 14.0, -bump(t, 0.0, 0.08, 0.38) * 12.0, 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        return bp, sc

    def anim_retreat_die(t: float):
        dur = 0.95
        dy = eval_scalar_keyframes([
            (0.00, 0.0, "smooth"),
            (0.14, 24.0, "out_cubic"),
            (0.28, 8.0, "smooth"),
            (0.92, -410.0, "in_cubic"),
        ], t)
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            t_b = max(0.0, t - 0.07 * u)
            spasm = bump(t_b, 0.0, 0.14, 0.38) * math.sin(TAU * 2.5 * t_b - 5.0 * u)
            limp = smootherstep(seg(t_b, 0.24, 0.90))
            bp[bn] = (spasm * (8.5 + 12.0 * u) + limp * (6.5 * math.sin(4.5 * u) + 8.0 * u), 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, 0.0, dy, 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        return bp, sc

    def anim_small_stun_loop(t: float):
        dur = 1.80
        cyc = TAU * t / dur
        bp = {}
        for i, bn in enumerate(bones):
            u = i / (n_bones - 1)
            droop = -(9.0 + 17.0 * (u ** 1.15))
            daze = math.sin(cyc - 3.4 * u) * (2.2 + 4.8 * u)
            bp[bn] = (droop + daze, 0.0, 0.0, 1.0, 1.0)
        bp["emerge_rig"] = (0.0, math.sin(cyc) * 5.0, -46.0 + math.sin(2.0 * cyc) * 5.0, 1.0, 1.0)
        sc: dict = {}
        hide_small_fx(sc)
        return bp, sc

    sb.sample_animation("base_floor", 0.5, True, anim_base_floor)
    sb.sample_animation("base_floor_burst", 0.90, False, anim_base_floor_burst)
    sb.sample_animation("skin_spike", 0.2, True, make_skin_anim("spike"))
    sb.sample_animation("skin_shell", 0.2, True, make_skin_anim("shell"))
    sb.sample_animation("skin_gland", 0.2, True, make_skin_anim("gland"))
    sb.sample_animation("idle_loop", 2.4, True, anim_idle)
    sb.sample_animation("emerge", 0.85, False, anim_emerge)
    sb.sample_animation("slam_attack", 1.25, False, anim_slam_attack, events=[(0.46, "small_hit")])
    sb.sample_animation("double_slap", 1.45, False, anim_double_slap, events=[(0.38, "hit_1"), (0.80, "hit_2")])
    sb.sample_animation("guard_brace", 1.20, False, anim_guard_brace, events=[(0.56, "guard_pulse")])
    sb.sample_animation("buff_pulse", 1.25, False, anim_buff_pulse, events=[(0.58, "buff_pulse")])
    sb.sample_animation("stun_loop", 1.80, True, anim_small_stun_loop)
    sb.sample_animation("hurt", 0.48, False, anim_hurt)
    sb.sample_animation("retreat_die", 0.95, False, anim_retreat_die)

    atlas_imgs = {}
    atlas_imgs.update(small_imgs)
    atlas_imgs.update(breach_imgs)
    atlas_imgs.update(slam_imgs)
    sb.export(atlas_imgs, ANIM_ROOT / "abyssal_small_tentacle", default_mix=0.10)


def render_centerline_audit(items: list[tuple[str, Image.Image, np.ndarray]]) -> Path:
    n = len(items)
    W, H = 20 + n * 300 + 20, 440
    sheet = Image.new("RGB", (W, H), (22, 26, 36))
    d = ImageDraw.Draw(sheet)
    for idx, (label, im, pts) in enumerate(items):
        bx, by, bw, bh = 20 + idx * 300, 32, 284, 390
        d.rectangle((bx, by, bx + bw, by + bh), fill=(32, 38, 52), outline=(85, 105, 130), width=2)
        sc = min((bw - 24) / im.width, (bh - 24) / im.height)
        rw, rh = int(round(im.width * sc)), int(round(im.height * sc))
        ox, oy = bx + (bw - rw) // 2, by + (bh - rh) // 2
        r_im = im.resize((rw, rh), Image.Resampling.LANCZOS)
        sheet.paste(r_im, (ox, oy), r_im)
        s_pts = [(ox + x * sc, oy + y * sc) for x, y in pts]
        for k in range(len(s_pts) - 1):
            d.line((s_pts[k], s_pts[k + 1]), fill=(80, 255, 220), width=3)
        for k, (sx, sy) in enumerate(s_pts):
            col = (255, 90, 90) if k == 0 else ((255, 235, 80) if k == len(s_pts) - 1 else (110, 245, 255))
            d.ellipse((sx - 4, sy - 4, sx + 4, sy + 4), fill=col, outline=(10, 14, 22), width=1)
        d.text((bx + 8, 8), label, fill=(230, 240, 250))
    out_p = ART_DIR / "abyssal_inkmaw_centerlines_audit.jpg"
    sheet.save(out_p, quality=92)
    return out_p


def main() -> None:
    crushing_im = Image.open(RIG_ROOT / "parts_crushing/tentacle.png").convert("RGBA")
    ink_im = Image.open(RIG_ROOT / "parts_ink/tentacle.png").convert("RGBA")
    ink_glow_im = Image.open(RIG_ROOT / "parts_ink/ink_blister_glow.png").convert("RGBA")
    bg_tall_im = Image.open(RIG_ROOT / "parts_body/bg_tentacle_tall.png").convert("RGBA")
    bg_outer_im = Image.open(RIG_ROOT / "parts_body/bg_tentacle_outer.png").convert("RGBA")

    water_imgs = load_dir_images(RIG_ROOT / "parts_vfx_water")
    breach_imgs = load_dir_images(RIG_ROOT / "parts_vfx_breach")
    ink_imgs = load_dir_images(RIG_ROOT / "parts_vfx_ink")
    slam_imgs = load_dir_images(RIG_ROOT / "parts_vfx_slam")
    small_imgs = load_dir_images(RIG_ROOT / "parts_small")

    print("Tracing geodesic 14-bone Hero & 10-bone Small tentacle medial centerlines...")
    crushing_pts, crushing_u = extract_tentacle_centerline(crushing_im, n_bones=14)
    ink_pts, ink_u = extract_tentacle_centerline(ink_im, n_bones=14)
    bg_tall_pts, bg_tall_u = extract_tentacle_centerline(bg_tall_im, n_bones=12)
    bg_outer_pts, bg_outer_u = extract_tentacle_centerline(bg_outer_im, n_bones=12)
    sp_pts, _ = extract_tentacle_centerline(small_imgs["spike"], n_bones=10)
    sh_pts, _ = extract_tentacle_centerline(small_imgs["shell"], n_bones=10)
    gl_pts, _ = extract_tentacle_centerline(small_imgs["gland"], n_bones=10)

    audit_p = render_centerline_audit([
        ("Hero Crush v2 (14b)", crushing_im, crushing_pts),
        ("Hero Ink v2 (14b)", ink_im, ink_pts),
        ("Small Spike (10b)", small_imgs["spike"], sp_pts),
        ("Small Shell (10b)", small_imgs["shell"], sh_pts),
        ("Small Gland (10b)", small_imgs["gland"], gl_pts),
    ])
    print("CENTERLINES_AUDIT:", audit_p)

    body_meta = json.loads((RIG_ROOT / "parts_body/meta.json").read_text(encoding="utf-8"))
    beak_y_px = body_meta["_extra"]["beak_notch"][1]
    waterline_px_y = float(body_meta["_extra"]["waterline_px_y"])
    beak_spine_y = -62.0 + (waterline_px_y - beak_y_px) * 0.65 - 4.0

    build_boss_body_rig(bg_tall_im, bg_tall_pts, bg_tall_u, bg_outer_im, bg_outer_pts, bg_outer_u, water_imgs)
    build_ink_beam_rig(ink_imgs, beak_spine_y)
    build_hero_tentacle_rig(
        "abyssal_crushing_tentacle", crushing_im, crushing_pts, crushing_u, water_imgs, breach_imgs, slam_imgs,
        mirror_x=False, base_tilt_deg=-2.0, base_cx=0.0, is_ink_arm=False,
    )
    build_hero_tentacle_rig(
        "abyssal_ink_tentacle", ink_im, ink_pts, ink_u, water_imgs, breach_imgs, slam_imgs,
        mirror_x=True, base_tilt_deg=2.0, base_cx=0.0, is_ink_arm=True, glow_im=ink_glow_im,
    )
    build_small_tentacle_rig(small_imgs, breach_imgs, slam_imgs)


if __name__ == "__main__":
    main()
