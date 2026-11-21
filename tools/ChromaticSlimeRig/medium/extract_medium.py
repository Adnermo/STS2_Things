"""Surgical per-strain multi-layer decomposition for Medium Chromatic Slime (v4 cutouts -> medium/parts/).

Produces onto unified 440x410 canvas (ORIGIN = (220, 386)):
- Shared: shadow.png
- Common per-skin ({crimson,amber,azure}):
  - {skin}_body.png        (clean gouache gel dome; Crimson retains socket back-collars for harpoon extrusion!)
  - {skin}_inner_blob.png  (subsurface gel belly core, masked clear of eye sockets)
  - {skin}_aura.png        (additive glow silhouette)
  - {skin}_iris_main.png, {skin}_lid_main.png (unscaled main eye inside hardware CLIPS eye_clip_main)
  - {skin}_iris_sec.png,  {skin}_lid_sec.png  (unscaled secondary eye inside hardware CLIPS eye_clip_sec)
  - {skin}_proj_1.png,    {skin}_proj_2.png   (hand-painted Mega Crit matte-gouache flying projectiles!)
- Bespoke 100%-rigid strain slots:
  - Crimson: c_shard_back (behind body, +48px root), c_shard_tall_l, c_shard_tall_r, c_shard_low_l, c_shard_low_r
             (pure faceted ruby crystals with +40px subsurface harpoon shafts!) + c_collars (front meniscus socket lips!)
  - Amber:   a_stone_top, a_stone_flank_r, a_stone_l, a_stone_cheek
  - Azure:   z_froth_crown, z_froth_left, z_froth_puddle
- Hardware clipping polygons: parts/eye_clips.json
- Visual audit sheet: prev/parts_audit.jpg
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
SRC_DIR = HERE.parents[2] / "source_assets" / "monsters" / "chromatic_slime" / "v4_cutouts"
PARTS_DIR = HERE / "parts"
PREV_DIR = HERE / "prev"

CW, CH = 440, 410
ORIGIN = (220, 386)


def clamp_f(v: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return lo if v < lo else (hi if v > hi else v)


def load_placed(skin: str) -> tuple[np.ndarray, int, int, np.ndarray]:
    """Load raw v4 cutout and place bottom-aligned at y=386, horizontally centered at x=220."""
    raw = np.array(Image.open(SRC_DIR / f"medium_{skin}.png").convert("RGBA"))
    ys, xs = np.where(raw[..., 3] > 8)
    ox = ORIGIN[0] - int(round(0.5 * (xs.min() + xs.max())))
    oy = ORIGIN[1] - int(ys.max())
    canvas = np.zeros((CH, CW, 4), dtype=np.uint8)
    h, w = raw.shape[:2]
    canvas[oy : oy + h, ox : ox + w] = raw
    return canvas, ox, oy, raw


def poly_mask(shape: tuple[int, int], pts: list[tuple[float, float]], offset: tuple[int, int] = (0, 0)) -> np.ndarray:
    m = np.zeros(shape, dtype=np.uint8)
    arr = np.round(np.asarray(pts, dtype=np.float64) + np.asarray(offset, dtype=np.float64)).astype(np.int32)
    cv2.fillPoly(m, [arr], 1, lineType=cv2.LINE_AA)
    return m > 0


def circles_mask(shape: tuple[int, int], circles: list[tuple[float, float, float]], offset: tuple[int, int] = (0, 0)) -> np.ndarray:
    m = np.zeros(shape, dtype=np.uint8)
    ox, oy = offset
    for cx, cy, r in circles:
        cv2.circle(m, (int(round(cx + ox)), int(round(cy + oy))), int(round(r)), 1, -1, lineType=cv2.LINE_AA)
    return m > 0


def ellipse_mask(
    shape: tuple[int, int],
    cx: float,
    cy: float,
    rx: float,
    ry: float,
    angle: float = 0.0,
    offset: tuple[int, int] = (0, 0),
) -> np.ndarray:
    m = np.zeros(shape, dtype=np.uint8)
    ox, oy = offset
    cv2.ellipse(
        m,
        (int(round(cx + ox)), int(round(cy + oy))),
        (int(round(rx)), int(round(ry))),
        angle,
        0,
        360,
        1,
        -1,
        lineType=cv2.LINE_AA,
    )
    return m > 0


def largest_cc(mask: np.ndarray) -> np.ndarray:
    u = mask.astype(np.uint8)
    num, labels, stats, _ = cv2.connectedComponentsWithStats(u)
    if num <= 1:
        return mask
    best = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    return labels == best


def min_area_cc(mask: np.ndarray, min_area: int = 24) -> np.ndarray:
    u = mask.astype(np.uint8)
    num, labels, stats, _ = cv2.connectedComponentsWithStats(u)
    out = np.zeros_like(mask, dtype=bool)
    for i in range(1, num):
        if stats[i, cv2.CC_STAT_AREA] >= min_area:
            out |= labels == i
    return out


def fill_holes(mask: np.ndarray) -> np.ndarray:
    u = mask.astype(np.uint8) * 255
    cnts, _ = cv2.findContours(u, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    out = np.zeros_like(u)
    cv2.drawContours(out, cnts, -1, 255, -1)
    return out > 0


def harmonic_inpaint(rgb: np.ndarray, valid_seed: np.ndarray, target_fill: np.ndarray, iters: int = 420) -> np.ndarray:
    """Smooth Laplace/harmonic PDE solver initialized from pure valid_seed boundary (zero boundary stain bleed)."""
    out = rgb.astype(np.float32).copy()
    solve = target_fill & ~valid_seed
    if not solve.any() or not valid_seed.any():
        return rgb.copy()

    _, labels = cv2.distanceTransformWithLabels((~valid_seed).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)
    seed_coords = np.column_stack(np.where(valid_seed))
    idx = np.clip(labels[solve] - 1, 0, len(seed_coords) - 1)
    nearest_yx = seed_coords[idx]
    out[solve] = out[nearest_yx[:, 0], nearest_yx[:, 1]]

    domain = valid_seed | solve
    k = np.array([[0.05, 0.20, 0.05], [0.20, 0.0, 0.20], [0.05, 0.20, 0.05]], dtype=np.float32)
    dom_f = domain.astype(np.float32)
    w_sum = np.maximum(cv2.filter2D(dom_f, -1, k, borderType=cv2.BORDER_REPLICATE), 1e-5)

    for _ in range(iters):
        masked = out * dom_f[..., None]
        avg = cv2.filter2D(masked, -1, k, borderType=cv2.BORDER_REPLICATE) / w_sum[..., None]
        out[solve] = avg[solve]

    return np.clip(np.round(out), 0, 255).astype(np.uint8)


def soft_extract(canvas: np.ndarray, hard_mask: np.ndarray, feather_px: float = 0.75) -> np.ndarray:
    out = np.zeros_like(canvas)
    m = hard_mask.astype(np.float32)
    if feather_px > 0:
        m = cv2.GaussianBlur(m, (3, 3), feather_px)
        m = np.clip((m - 0.14) / 0.86, 0.0, 1.0)
    a = np.round(canvas[..., 3].astype(np.float32) * m).astype(np.uint8)
    keep = a > 2
    out[keep, :3] = canvas[keep, :3]
    out[keep, 3] = a[keep]
    return out


def save_trimmed(parts_meta: dict, name: str, rgba: np.ndarray, pad: int = 2) -> None:
    ys, xs = np.where(rgba[..., 3] > 2)
    if len(xs) == 0:
        raise RuntimeError(f"Empty part: {name}")
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(CW, int(xs.max()) + 1 + pad)
    y1 = min(CH, int(ys.max()) + 1 + pad)
    crop = rgba[y0:y1, x0:x1]
    Image.fromarray(crop).save(PARTS_DIR / f"{name}.png", optimize=True)
    parts_meta[name] = {"x": x0, "y": y0, "w": int(x1 - x0), "h": int(y1 - y0)}


def paint_faceted_shaft_under(
    shard_rgba: np.ndarray,
    p_start: tuple[float, float],
    p_end: tuple[float, float],
    half_w_start: float,
    half_w_end: float,
    col_light: tuple[int, int, int],
    col_dark: tuple[int, int, int],
    col_ridge: tuple[int, int, int],
    offset: tuple[int, int],
) -> None:
    """Paint a crisp 2-plane matte-gouache faceted crystal root UNDER existing opaque pixels of shard_rgba."""
    ox, oy = offset
    x0, y0 = p_start[0] + ox, p_start[1] + oy
    x1, y1 = p_end[0] + ox, p_end[1] + oy
    dx, dy = x1 - x0, y1 - y0
    L = max(math.hypot(dx, dy), 1e-5)
    nx, ny = -dy / L, dx / L

    p_l0 = (x0 + nx * half_w_start, y0 + ny * half_w_start)
    p_l1 = (x1 + nx * half_w_end, y1 + ny * half_w_end)
    p_r0 = (x0 - nx * half_w_start, y0 - ny * half_w_start)
    p_r1 = (x1 - nx * half_w_end, y1 - ny * half_w_end)
    p_c0 = (x0, y0)
    p_c1 = (x1, y1)

    buf = np.zeros((CH, CW, 4), dtype=np.uint8)
    m_left = np.zeros((CH, CW), dtype=np.uint8)
    m_right = np.zeros((CH, CW), dtype=np.uint8)
    cv2.fillPoly(m_left, [np.round([p_l0, p_c0, p_c1, p_l1]).astype(np.int32)], 255, lineType=cv2.LINE_AA)
    cv2.fillPoly(m_right, [np.round([p_c0, p_r0, p_r1, p_c1]).astype(np.int32)], 255, lineType=cv2.LINE_AA)

    tot_a = np.clip(m_left.astype(np.int32) + m_right.astype(np.int32), 0, 255).astype(np.uint8)
    w_l = m_left.astype(np.float32) / np.maximum(m_left.astype(np.float32) + m_right.astype(np.float32), 1.0)
    rgb = (
        w_l[..., None] * np.array(col_light, np.float32)
        + (1.0 - w_l[..., None]) * np.array(col_dark, np.float32)
    )
    # Crisp central crystal bevel highlight line
    ridge = np.zeros((CH, CW), dtype=np.uint8)
    cv2.line(ridge, (int(round(x0)), int(round(y0))), (int(round(x1)), int(round(y1))), 255, 2, lineType=cv2.LINE_AA)
    rm = (ridge.astype(np.float32) / 255.0) * 0.45
    rgb = rgb * (1.0 - rm[..., None]) + np.array(col_ridge, np.float32) * rm[..., None]

    buf[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    buf[..., 3] = tot_a

    # Alpha-under composite: existing shard pixels stay 100% untouched on top!
    a_top = shard_rgba[..., 3:4].astype(np.float32) / 255.0
    a_bot = buf[..., 3:4].astype(np.float32) / 255.0
    a_out = a_top + a_bot * (1.0 - a_top)
    safe = np.maximum(a_out, 1e-5)
    rgb_out = (shard_rgba[..., :3].astype(np.float32) * a_top + buf[..., :3].astype(np.float32) * a_bot * (1.0 - a_top)) / safe
    shard_rgba[..., :3] = np.clip(np.round(rgb_out), 0, 255).astype(np.uint8)
    shard_rgba[..., 3] = np.clip(np.round(a_out[..., 0] * 255.0), 0, 255).astype(np.uint8)


def build_projectiles_for_skin(skin: str, parts_meta: dict) -> None:
    """Create 2 crisp Mega Crit lineless matte-gouache projectile sprites per strain:
    - Positioned around setup launch origins proj_1_b=(150, 236) and proj_2_b=(168, 210), pointing LEFT (-X)!
    """
    for idx, (cx, cy) in ((1, (150, 236)), (2, (168, 210))):
        im = np.zeros((CH, CW, 4), dtype=np.uint8)

        def fill_poly(pts_rel: list[tuple[float, float]], col: tuple[int, int, int], alpha: int = 255) -> None:
            layer = np.zeros((CH, CW), dtype=np.uint8)
            arr = np.round(np.asarray(pts_rel, np.float64) + np.array([cx, cy], np.float64)).astype(np.int32)
            cv2.fillPoly(layer, [arr], alpha, lineType=cv2.LINE_AA)
            a = layer.astype(np.float32)[..., None] / 255.0
            a_dst = im[..., 3:4].astype(np.float32) / 255.0
            a_out = a + a_dst * (1.0 - a)
            im[..., :3] = np.clip(
                (np.array(col, np.float32) * a + im[..., :3].astype(np.float32) * a_dst * (1.0 - a))
                / np.maximum(a_out, 1e-5),
                0,
                255,
            ).astype(np.uint8)
            im[..., 3] = np.clip(np.round(a_out[..., 0] * 255.0), 0, 255).astype(np.uint8)

        def fill_circ(dx: float, dy: float, r: float, col: tuple[int, int, int], alpha: int = 255) -> None:
            layer = np.zeros((CH, CW), dtype=np.uint8)
            cv2.circle(layer, (int(round(cx + dx)), int(round(cy + dy))), int(round(r)), alpha, -1, lineType=cv2.LINE_AA)
            a = layer.astype(np.float32)[..., None] / 255.0
            a_dst = im[..., 3:4].astype(np.float32) / 255.0
            a_out = a + a_dst * (1.0 - a)
            im[..., :3] = np.clip(
                (np.array(col, np.float32) * a + im[..., :3].astype(np.float32) * a_dst * (1.0 - a))
                / np.maximum(a_out, 1e-5),
                0,
                255,
            ).astype(np.uint8)
            im[..., 3] = np.clip(np.round(a_out[..., 0] * 255.0), 0, 255).astype(np.uint8)

        if skin == "crimson":
            # Sharp faceted ruby-crystal harpoon splinter + trailing crimson-rose gouache gel wake!
            sc = 1.0 if idx == 1 else 1.16
            # Trailing terracotta-crimson gel wake
            fill_poly([(-18 * sc, -7 * sc), (46 * sc, -2 * sc), (54 * sc, 0), (44 * sc, 4 * sc), (-18 * sc, 8 * sc)], (178, 72, 66), 235)
            fill_poly([(-14 * sc, -4 * sc), (34 * sc, -1 * sc), (-14 * sc, 2 * sc)], (216, 126, 128), 220)
            # Primary faceted dark-ruby spearhead
            fill_poly([(-48 * sc, 0), (-14 * sc, -12 * sc), (8 * sc, -6 * sc), (14 * sc, 0), (8 * sc, 7 * sc), (-14 * sc, 12 * sc)], (76, 22, 36), 255)
            # Upper bevel light plane
            fill_poly([(-48 * sc, 0), (-14 * sc, -12 * sc), (8 * sc, -6 * sc), (-6 * sc, -1 * sc)], (146, 56, 74), 255)
            # Crisp pale-rose edge highlight
            fill_poly([(-46 * sc, 0), (-15 * sc, -10 * sc), (-12 * sc, -7 * sc), (-38 * sc, 0)], (218, 146, 158), 255)
            if idx == 2:
                # Offset secondary shard splinter for Hit 2 barrage!
                fill_poly([(-30, 14), (-8, 8), (16, 12), (-6, 19)], (88, 26, 40), 255)
                fill_poly([(-30, 14), (-8, 8), (6, 11), (-10, 14)], (162, 68, 86), 255)

        elif skin == "amber":
            # Angular weathered-sandstone shock-chunk + warm honey-amber splatter wake!
            if idx == 1:
                fill_poly([(-10, -9), (42, -4), (48, 2), (36, 9), (-10, 10)], (196, 130, 36), 235)
                fill_poly([(-6, -5), (26, -2), (-6, 3)], (244, 196, 102), 230)
                # Faceted sandstone chunk
                fill_poly([(-38, -2), (-22, -17), (4, -15), (14, -2), (6, 14), (-20, 16), (-36, 8)], (92, 84, 64), 255)
                fill_poly([(-38, -2), (-22, -17), (4, -15), (-4, -2), (-24, 2)], (152, 134, 102), 255)
                fill_poly([(-22, -17), (2, -15), (-4, -8), (-20, -10)], (188, 170, 134), 255)
            else:
                # Ground-skimming sandstone shockwave wedge + amber chips!
                fill_poly([(-42, 6), (-16, -12), (12, -8), (38, 6), (20, 14), (-32, 14)], (188, 122, 32), 240)
                fill_poly([(-34, 4), (-14, -14), (8, -10), (14, 4), (-12, 10)], (102, 92, 70), 255)
                fill_poly([(-34, 4), (-14, -14), (6, -10), (-6, -1)], (160, 142, 108), 255)
                fill_poly([(18, -6), (30, -12), (38, -3), (26, 3)], (138, 122, 92), 255)

        else:
            # Authentic LeafSlime M style luminous seafoam-cyan & deep-teal teardrop acid glob!
            sc = 1.0 if idx == 1 else 0.88
            # Outer deep sea-teal rim teardrop
            fill_poly(
                [(-34 * sc, 0), (-22 * sc, -14 * sc), (-4 * sc, -15 * sc), (26 * sc, -6 * sc), (46 * sc, 0), (24 * sc, 8 * sc), (-4 * sc, 15 * sc), (-24 * sc, 13 * sc)],
                (48, 118, 122),
                255,
            )
            fill_circ(-16 * sc, 0, 15 * sc, (56, 134, 138), 255)
            # Inner bright aqua-seafoam body
            fill_poly(
                [(-29 * sc, 0), (-18 * sc, -11 * sc), (-2 * sc, -11 * sc), (34 * sc, 0), (-2 * sc, 11 * sc), (-18 * sc, 10 * sc)],
                (108, 212, 206),
                255,
            )
            fill_circ(-15 * sc, -1 * sc, 11.5 * sc, (124, 226, 220), 255)
            # Crisp pale-mint specular crescent & trailing froth bubbles
            fill_circ(-18 * sc, -4 * sc, 5.5 * sc, (204, 250, 246), 255)
            fill_circ(8 * sc, -5 * sc, 5.0 * sc, (156, 238, 234), 255)
            fill_circ(24 * sc, 4 * sc, 3.5 * sc, (140, 230, 226), 245)
            if idx == 2:
                fill_circ(48 * sc, 2 * sc, 4.5 * sc, (118, 218, 214), 240)

        save_trimmed(parts_meta, f"{skin}_proj_{idx}", im)


def extract_eyes_for_skin(
    skin: str,
    canvas: np.ndarray,
    ox: int,
    oy: int,
    parts_meta: dict,
    eye_clips: dict,
) -> tuple[np.ndarray, np.ndarray]:
    """Segment main & secondary eyes, clean irises from body, build full irises + angled gouache lids + clip polys."""
    eye_rois_cut = {
        "crimson": {"main": (86, 172, 213, 256), "sec": (211, 132, 279, 185)},
        "amber": {"main": (74, 170, 212, 243), "sec": (238, 137, 274, 168)},
        "azure": {"main": (74, 148, 213, 219), "sec": (235, 168, 283, 193)},
    }[skin]

    body_clean_rgb = canvas[..., :3].copy()
    all_sockets_mask = np.zeros((CH, CW), dtype=bool)
    ell9 = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))

    for which in ("main", "sec"):
        cx0, cy0, cx1, cy1 = eye_rois_cut[which]
        x0, y0, x1, y1 = cx0 + ox, cy0 + oy, cx1 + ox, cy1 + oy
        sub = canvas[y0:y1, x0:x1]
        r = sub[..., 0].astype(np.float32)
        g = sub[..., 1].astype(np.float32)
        b = sub[..., 2].astype(np.float32)
        a = sub[..., 3] > 128

        if skin == "crimson":
            is_dark = a & (r < 114) & (g < 46)
            is_iris = a & (r > 138) & (g > 94) & ((r - b) > 52)
        elif skin == "amber":
            is_dark = a & (r < 115) & (g < 79)
            y_cut_min = (183 if which == "main" else 144) - cy0
            yy = np.arange(y1 - y0)[:, None]
            is_iris = a & (yy >= y_cut_min) & (r > 162) & (g > 122) & (b > 50)
        else:
            is_dark = a & (g < 97) & (b < 105)
            is_iris = a & (r > 132) & (g > 108) & ((r - b) > 42)

        is_iris = fill_holes(largest_cc(is_iris))
        sock = cv2.morphologyEx((is_dark | is_iris).astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0
        sock = fill_holes(largest_cc(sock))

        sock_full = np.zeros((CH, CW), dtype=bool)
        sock_full[y0:y1, x0:x1] = sock
        iris_full = np.zeros((CH, CW), dtype=bool)
        iris_full[y0:y1, x0:x1] = is_iris
        all_sockets_mask |= sock_full

        iris_dil = cv2.dilate(iris_full.astype(np.uint8), ell9) > 0
        dark_seed = sock_full & ~iris_dil
        pad = 8
        rx0, ry0, rx1, ry1 = max(0, x0 - pad), max(0, y0 - pad), min(CW, x1 + pad), min(CH, y1 + pad)
        body_clean_rgb[ry0:ry1, rx0:rx1] = harmonic_inpaint(
            body_clean_rgb[ry0:ry1, rx0:rx1],
            dark_seed[ry0:ry1, rx0:rx1],
            (sock_full & iris_dil)[ry0:ry1, rx0:rx1],
            iters=260,
        )

        cnts, _ = cv2.findContours(sock_full.astype(np.uint8) * 255, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        cnt = max(cnts, key=cv2.contourArea)
        eps = 1.15 if which == "main" else 0.80
        approx = cv2.approxPolyDP(cnt, eps, True).reshape(-1, 2)
        if len(approx) < 8:
            approx = cv2.approxPolyDP(cnt, 0.45, True).reshape(-1, 2)
        eye_clips[f"eye_clip_{which}"][skin] = [[float(p[0]), float(p[1])] for p in approx]

        iys, ixs = np.where(iris_full)
        icx = float(0.5 * (ixs.min() + ixs.max()))
        irx = max(5.0, 0.5 * (ixs.max() - ixs.min()) + 0.8)
        iry = max(4.5, min(irx * 0.92, float(iys.max() - iys.min()) * 0.78 + 3.5))
        icy = float(iys.max()) - iry + 0.5

        yy_g, xx_g = np.mgrid[0:CH, 0:CW]
        ell = (((xx_g - icx) / irx) ** 2 + ((yy_g - icy) / iry) ** 2) <= 1.0
        upper_cap = ell & ~sock_full
        full_iris_mask = iris_full | upper_cap | (ell & sock_full & (yy_g <= iys.min() + 3))

        iris_rgb = canvas[..., :3].copy()
        rx0i = max(0, int(icx - irx) - 6)
        ry0i = max(0, int(icy - iry) - 6)
        rx1i = min(CW, int(icx + irx) + 7)
        ry1i = min(CH, int(icy + iry) + 7)
        iris_seed = cv2.erode(iris_full.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        if not iris_seed.any():
            iris_seed = iris_full
        iris_rgb[ry0i:ry1i, rx0i:rx1i] = harmonic_inpaint(
            iris_rgb[ry0i:ry1i, rx0i:rx1i],
            iris_seed[ry0i:ry1i, rx0i:rx1i],
            (full_iris_mask & ~iris_seed)[ry0i:ry1i, rx0i:rx1i],
            iters=160,
        )
        top_shade = np.clip((float(iys.min() + 2) - yy_g.astype(np.float32)) / max(iry, 1.0), 0.0, 1.0)
        shade_mul = 1.0 - 0.16 * top_shade[..., None]
        iris_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
        m_blur = cv2.GaussianBlur(full_iris_mask.astype(np.float32), (3, 3), 0.75)
        iris_a = np.clip(m_blur * 255.0, 0, 255).astype(np.uint8)
        iris_rgba[..., :3] = np.clip(iris_rgb.astype(np.float32) * shade_mul, 0, 255).astype(np.uint8)
        iris_rgba[iris_full, :3] = canvas[iris_full, :3]
        iris_rgba[..., 3] = iris_a
        save_trimmed(parts_meta, f"{skin}_iris_{which}", iris_rgba)

        sys_y, sxs_x = np.where(sock_full)
        sx_min, sx_max = int(sxs_x.min()), int(sxs_x.max())
        xs_top = np.arange(sx_min, sx_max + 1)
        ys_top = np.array([int(np.where(sock_full[:, x])[0].min()) for x in xs_top], dtype=np.float32)
        if len(xs_top) >= 7:
            k_sm = min(11, (len(xs_top) // 2) * 2 + 1)
            ys_top_sm = cv2.GaussianBlur(ys_top.reshape(1, -1), (k_sm, 1), 2.2).ravel()
        else:
            ys_top_sm = ys_top
        slope, intercept = np.polyfit(xs_top.astype(np.float32), ys_top_sm, 1)

        margin_x = 46 if which == "main" else 36
        lid_h = 96 if which == "main" else 72
        lx0, lx1 = max(0, sx_min - margin_x), min(CW - 1, sx_max + margin_x)

        brow_band = np.zeros((CH, CW), dtype=bool)
        for x in xs_top:
            yt = int(round(ys_top_sm[x - sx_min]))
            y_hi = max(0, yt - 18)
            y_lo = max(0, yt - 5)
            if y_lo > y_hi:
                brow_band[y_hi:y_lo, x] = canvas[y_hi:y_lo, x, 3] > 200
        if brow_band.any():
            base_col = np.median(canvas[brow_band, :3], axis=0).astype(np.float32)
        else:
            base_col = np.array([164, 70, 62] if skin == "crimson" else [185, 119, 33] if skin == "amber" else [69, 143, 146], np.float32)
        crease_col = base_col * 0.54

        lid_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
        for x in range(lx0, lx1 + 1):
            if sx_min <= x <= sx_max:
                yb = float(ys_top_sm[x - sx_min]) - 2.0
            else:
                yb = float(slope * x + intercept) - 2.0
            yt_top = yb - lid_h
            yi0 = max(0, int(math.floor(yt_top)))
            yi1 = min(CH - 1, int(math.ceil(yb)))
            for y in range(yi0, yi1 + 1):
                dist_from_bot = yb - y
                if dist_from_bot < 0.0:
                    continue
                aa = min(1.0, dist_from_bot / 1.2)
                u = clamp_f(dist_from_bot / max(lid_h, 1.0))
                col = base_col * (0.95 + 0.10 * u)
                if dist_from_bot < 2.8:
                    t_c = clamp_f(dist_from_bot / 2.8)
                    col = crease_col * (1.0 - t_c) + col * t_c
                lid_rgba[y, x, :3] = np.clip(col, 0, 255).astype(np.uint8)
                lid_rgba[y, x, 3] = int(round(255.0 * aa))

        save_trimmed(parts_meta, f"{skin}_lid_{which}", lid_rgba)

    return body_clean_rgb, all_sockets_mask


def decompose_crimson(canvas: np.ndarray, ox: int, oy: int, parts_meta: dict, eye_clips: dict) -> None:
    """3-Layer Harpoon Socket Decomposition for Crimson:
    - crimson_body.png: smooth dome WITH pale-rose meniscus collars & dark ruby socket wells intact!
    - crimson_c_shard_*: pure faceted dark-ruby crystals (zero pink collar!) + 40px extruded subsurface harpoon shafts!
    - crimson_c_collars.png: front meniscus collar lips + inward gel pockets so crystals retract -12px or harpoon +34px cleanly!
    """
    body_rgb, sockets_mask = extract_eyes_for_skin("crimson", canvas, ox, oy, parts_meta, eye_clips)
    a_full = canvas[..., 3] > 8
    r = canvas[..., 0].astype(np.float32)
    g = canvas[..., 1].astype(np.float32)
    b = canvas[..., 2].astype(np.float32)

    # Outer silhouette exclusion removes protruding crystal shafts ABOVE/OUTSIDE the pale-rose meniscus collars!
    # Notice the pale-rose meniscus collars stay INSIDE dome_with_collars_mask!
    protruding_crystals_only = (
        poly_mask((CH, CW), [(45, 0), (144, 0), (144, 79), (122, 86), (95, 96), (84, 108), (45, 86)], (ox, oy))
        | poly_mask((CH, CW), [(143, 0), (196, 0), (196, 73), (178, 76), (160, 82), (143, 85)], (ox, oy))
        | poly_mask((CH, CW), [(252, 35), (350, 35), (350, 140), (304, 134), (294, 114), (274, 96), (252, 90)], (ox, oy))
        | poly_mask((CH, CW), [(0, 166), (47, 174), (45, 200), (43, 222), (0, 222)], (ox, oy))
        | poly_mask((CH, CW), [(344, 160), (396, 160), (396, 226), (348, 222), (344, 198)], (ox, oy))
    )
    dome_mask = fill_holes(largest_cc(a_full & ~protruding_crystals_only))

    # Pure dark-ruby crystal predicate inside collar zones (separates dark ruby R<138, G<72 from pale-rose collar G>80, B>82!):
    is_dark_ruby_in_collar = a_full & (g < 74) & (b >= g - 3.0) & (r < 142)

    # 1. c_shard_back (behind body! +48px downward faceted ruby shaft so dorsal harpoon shoots +35px with zero gap!)
    roi_back = poly_mask((CH, CW), [(145, 16), (193, 16), (193, 83), (170, 78), (145, 85)], (ox, oy))
    m_back = a_full & roi_back & ~dome_mask
    shard_back_rgba = soft_extract(canvas, m_back, feather_px=0.4)
    paint_faceted_shaft_under(
        shard_back_rgba,
        p_start=(169, 74),
        p_end=(173, 126),
        half_w_start=17.5,
        half_w_end=13.5,
        col_light=(102, 34, 49),
        col_dark=(68, 20, 32),
        col_ridge=(142, 54, 72),
        offset=(ox, oy),
    )
    save_trimmed(parts_meta, "crimson_c_shard_back", shard_back_rgba)

    # 2..5. Pure faceted ruby crystals (excluding pale-rose collars!) + 40px subsurface harpoon shafts!
    front_defs = {
        "c_shard_tall_l": {
            "roi": [(50, 8), (138, 8), (142, 98), (132, 121), (108, 125), (94, 114), (88, 92), (50, 68)],
            "pure_zone": [(50, 8), (138, 8), (136, 86), (86, 98), (50, 72)],
            "shafts": [((114, 106), (137, 145), 18.5, 14.0)],
        },
        "c_shard_tall_r": {
            "roi": [(254, 42), (336, 42), (336, 108), (298, 134), (272, 134), (255, 118), (256, 94)],
            "pure_zone": [(258, 42), (336, 42), (336, 108), (296, 116), (266, 96)],
            "shafts": [((277, 117), (255, 149), 16.5, 12.5)],
        },
        "c_shard_low_l": {
            "roi": [(4, 168), (58, 174), (60, 198), (56, 218), (42, 220), (4, 214)],
            "pure_zone": [(4, 168), (46, 174), (44, 218), (4, 214)],
            "shafts": [((48, 198), (89, 206), 12.5, 9.5)],
        },
        "c_shard_low_r": {
            "roi": [
                (328, 164), (390, 164), (390, 218), (348, 220), (330, 214),
                (324, 252), (298, 256), (277, 248), (276, 226), (292, 216), (324, 214), (328, 182),
            ],
            "pure_zone": [(346, 164), (390, 164), (390, 218), (346, 218)],
            "shafts": [
                ((340, 197), (309, 207), 12.0, 9.0),
                ((296, 236), (268, 244), 10.5, 8.0),
            ],
        },
    }

    all_pure_crystals = np.zeros((CH, CW), dtype=bool)
    for sname, cfg in front_defs.items():
        roi = poly_mask((CH, CW), cfg["roi"], (ox, oy))
        pzone = poly_mask((CH, CW), cfg["pure_zone"], (ox, oy))
        m = roi & a_full & (pzone | ~dome_mask | is_dark_ruby_in_collar)
        m = cv2.morphologyEx(m.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8)) > 0
        m = fill_holes(min_area_cc(m, min_area=32)) & a_full & roi
        all_pure_crystals |= m

        shard_rgba = soft_extract(canvas, m, feather_px=0.45)
        for p0, p1, hw0, hw1 in cfg["shafts"]:
            paint_faceted_shaft_under(
                shard_rgba,
                p_start=p0,
                p_end=p1,
                half_w_start=hw0,
                half_w_end=hw1,
                col_light=(108, 36, 52),
                col_dark=(70, 21, 34),
                col_ridge=(148, 58, 76),
                offset=(ox, oy),
            )
        save_trimmed(parts_meta, f"crimson_{sname}", shard_rgba)

    # Fill crystal interiors inside dome_mask on crimson_body.png:
    # Deep ruby-maroon socket shadow right inside the pale-rose collars so sockets look hollow if crystals retract deeply!
    crystal_inside_dome = dome_mask & (all_pure_crystals | m_back)
    body_inpainted = body_rgb.copy()
    # Smoothly shade socket wells dark rose-maroon [68, 22, 34] blending toward collar inner rim
    well_dil = cv2.dilate(crystal_inside_dome.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
    dist_w = cv2.distanceTransform(well_dil.astype(np.uint8), cv2.DIST_L2, 5)
    t_well = np.clip(dist_w / 6.0, 0.0, 1.0)[..., None]
    well_col = np.array([64, 20, 31], np.float32) * t_well + np.array([148, 86, 94], np.float32) * (1.0 - t_well)
    upd = dome_mask & well_dil & ~sockets_mask
    body_inpainted[upd] = np.clip(np.round(well_col[upd]), 0, 255).astype(np.uint8)
    # Clean top dorsal scar where c_shard_back touched top rim
    back_scar = dome_mask & cv2.dilate(m_back.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
    if back_scar.any():
        seed_top = dome_mask & ~back_scar & ~well_dil & ~sockets_mask
        body_inpainted = harmonic_inpaint(body_inpainted, seed_top, back_scar, iters=200)

    body_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
    dome_aa = cv2.GaussianBlur(dome_mask.astype(np.float32), (3, 3), 0.65)
    orig_edge = a_full & ~cv2.dilate(protruding_crystals_only.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
    alpha_out = np.clip(dome_aa * 255.0, 0, 255).astype(np.uint8)
    alpha_out[orig_edge] = canvas[orig_edge, 3]
    alpha_out[~dome_mask & ~orig_edge] = np.minimum(alpha_out[~dome_mask & ~orig_edge], canvas[~dome_mask & ~orig_edge, 3])
    keep = alpha_out > 2
    body_rgba[keep, :3] = body_inpainted[keep]
    body_rgba[keep, 3] = alpha_out[keep]
    save_trimmed(parts_meta, "crimson_body", body_rgba)

    # Build crimson_c_collars.png: front meniscus collar lips + inward gel pockets (weighted 100% to body, drawn over shards & under eyes!)
    # Each pocket starts strictly along the front/inward lip of the pale-rose meniscus collar (leaving the crystal's outward exit 100% open!)
    front_pockets = (
        # tall_l front-lower pink lip + inward pocket
        poly_mask((CH, CW), [(85, 114), (102, 121), (126, 119), (146, 98), (168, 146), (118, 166), (86, 142)], (ox, oy))
        # tall_r front-lower pink lip + inward pocket
        | poly_mask((CH, CW), [(248, 108), (266, 126), (290, 133), (304, 130), (284, 168), (232, 150)], (ox, oy))
        # low_l front-right pink lip + inward cheek pocket
        | poly_mask((CH, CW), [(46, 174), (53, 186), (54, 200), (50, 215), (44, 224), (102, 226), (102, 174)], (ox, oy))
        # low_r front-left pink lips + inward right-flank pocket
        | poly_mask((CH, CW), [(336, 172), (334, 198), (344, 220), (324, 256), (255, 258), (255, 212), (296, 172)], (ox, oy))
    ) & dome_mask & ~all_pure_crystals

    collars_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
    # Feather ONLY the deep interior gel edge (keep socket lip edge crisp!)
    lip_zone = cv2.dilate(all_pure_crystals.astype(np.uint8), np.ones((11, 11), np.uint8)) > 0
    m_col_f = cv2.GaussianBlur(front_pockets.astype(np.float32), (5, 5), 1.2)
    m_col_f[front_pockets & lip_zone] = 1.0
    col_a = np.round(body_rgba[..., 3].astype(np.float32) * np.clip(m_col_f, 0.0, 1.0)).astype(np.uint8)
    keep_c = col_a > 2
    collars_rgba[keep_c, :3] = body_rgba[keep_c, :3]
    # Restore original crisp painted collar lip RGB from raw canvas where untouched by crystal/eyes!
    raw_lip = keep_c & ~all_pure_crystals & ~sockets_mask & (canvas[..., 3] > 220)
    collars_rgba[raw_lip, :3] = canvas[raw_lip, :3]
    collars_rgba[keep_c, 3] = col_a[keep_c]
    save_trimmed(parts_meta, "crimson_c_collars", collars_rgba)

    build_inner_and_aura("crimson", body_rgba, sockets_mask, parts_meta, core_tint=(215, 96, 86), aura_tint=(235, 92, 96))
    build_projectiles_for_skin("crimson", parts_meta)


def decompose_amber(canvas: np.ndarray, ox: int, oy: int, parts_meta: dict, eye_clips: dict) -> None:
    body_rgb, sockets_mask = extract_eyes_for_skin("amber", canvas, ox, oy, parts_meta, eye_clips)
    a_full = canvas[..., 3] > 8
    hsv = cv2.cvtColor(canvas[..., :3], cv2.COLOR_RGB2HSV)
    s = hsv[..., 1].astype(np.float32)
    r = canvas[..., 0].astype(np.float32)

    gloss_zone = poly_mask((CH, CW), [(82, 96), (178, 92), (186, 158), (76, 160)], (ox, oy))
    eye_guard = cv2.dilate(sockets_mask.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0

    rock_region = poly_mask(
        (CH, CW),
        [
            (5, 10), (394, 10), (394, 240), (370, 248), (320, 252), (295, 274), (222, 274),
            (222, 198), (288, 196), (288, 158), (205, 135), (165, 102), (82, 102), (56, 160), (48, 224), (5, 224),
        ],
        (ox, oy),
    )
    is_rock = a_full & rock_region & ~gloss_zone & ~eye_guard & ((s < 135) | (r < 116))
    is_rock = cv2.morphologyEx(is_rock.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)) > 0
    is_rock = cv2.morphologyEx(is_rock.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0
    is_rock = fill_holes(is_rock) & a_full

    roi_cheek = poly_mask((CH, CW), [(222, 200), (298, 200), (298, 275), (222, 275)], (ox, oy))
    m_cheek = fill_holes(largest_cc(is_rock & roi_cheek))
    save_trimmed(parts_meta, "amber_a_stone_cheek", soft_extract(canvas, m_cheek, feather_px=0.55))

    upper_rock = is_rock & ~roi_cheek
    poly_left_slab = poly_mask(
        (CH, CW),
        [(0, 0), (170, 0), (186, 36), (194, 54), (174, 84), (170, 110), (95, 120), (65, 230), (0, 230)],
        (ox, oy),
    )
    poly_flank_r = poly_mask(
        (CH, CW),
        [(284, 156), (318, 135), (348, 122), (399, 116), (399, 224), (366, 248), (320, 252), (284, 252)],
        (ox, oy),
    )

    m_stone_l = largest_cc(upper_rock & poly_left_slab)
    m_stone_flank_r = largest_cc(upper_rock & poly_flank_r)
    m_stone_top = largest_cc(upper_rock & ~m_stone_l & ~m_stone_flank_r)

    save_trimmed(parts_meta, "amber_a_stone_l", soft_extract(canvas, m_stone_l, feather_px=0.55))
    save_trimmed(parts_meta, "amber_a_stone_flank_r", soft_extract(canvas, m_stone_flank_r, feather_px=0.55))

    top_underlap = cv2.dilate(m_stone_top.astype(np.uint8), np.ones((15, 15), np.uint8)).astype(bool) & (m_stone_l | m_stone_flank_r)
    stone_top_rgba = soft_extract(canvas, m_stone_top | top_underlap, feather_px=0.55)
    stone_top_rgba[top_underlap, :3] = np.clip(canvas[top_underlap, :3].astype(np.float32) * 0.68, 0, 255).astype(np.uint8)
    stone_top_rgba[top_underlap, 3] = 255
    save_trimmed(parts_meta, "amber_a_stone_top", stone_top_rgba)

    safe_outer = cv2.erode(a_full.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
    visible_gel = a_full & ~(m_stone_l | m_stone_top | m_stone_flank_r)
    dome_ext = circles_mask((CH, CW), [(198, 182, 146), (152, 165, 112), (244, 172, 110)], (ox, oy)) & safe_outer
    bare_dome_mask = fill_holes(largest_cc(visible_gel | dome_ext))

    ell15 = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))
    rock_scar = cv2.dilate((m_stone_l | m_stone_top | m_stone_flank_r | m_cheek).astype(np.uint8), ell15) > 0
    valid_gel = visible_gel & ~rock_scar & ~sockets_mask & ((s >= 148) | gloss_zone)
    eye_snap = body_rgb[sockets_mask].copy()
    body_inpainted = harmonic_inpaint(body_rgb, valid_gel, bare_dome_mask & ~valid_gel, iters=520)

    ext_only = bare_dome_mask & (~visible_gel | rock_scar) & ~m_cheek
    if ext_only.any():
        dist_in = cv2.distanceTransform(bare_dome_mask.astype(np.uint8), cv2.DIST_L2, 5)
        rim_factor = np.clip(0.84 + 0.16 * np.minimum(dist_in / 20.0, 1.0), 0.84, 1.0)
        body_inpainted[ext_only] = np.clip(body_inpainted[ext_only].astype(np.float32) * rim_factor[ext_only, None], 0, 255).astype(np.uint8)
    body_inpainted[sockets_mask] = eye_snap

    body_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
    dome_aa = cv2.GaussianBlur(bare_dome_mask.astype(np.float32), (3, 3), 0.7)
    orig_gel_edge = visible_gel & ~cv2.dilate((m_stone_l | m_stone_top | m_stone_flank_r).astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
    alpha_out = np.clip(dome_aa * 255.0, 0, 255).astype(np.uint8)
    alpha_out[orig_gel_edge] = canvas[orig_gel_edge, 3]
    keep = alpha_out > 2
    body_rgba[keep, :3] = body_inpainted[keep]
    body_rgba[keep, 3] = alpha_out[keep]
    save_trimmed(parts_meta, "amber_body", body_rgba)

    build_inner_and_aura("amber", body_rgba, sockets_mask, parts_meta, core_tint=(242, 168, 52), aura_tint=(255, 196, 72))
    build_projectiles_for_skin("amber", parts_meta)


def decompose_azure(canvas: np.ndarray, ox: int, oy: int, parts_meta: dict, eye_clips: dict) -> None:
    body_rgb, sockets_mask = extract_eyes_for_skin("azure", canvas, ox, oy, parts_meta, eye_clips)
    a_full = canvas[..., 3] > 8
    g = canvas[..., 1].astype(np.float32)
    b = canvas[..., 2].astype(np.float32)

    crown_geom = circles_mask(
        (CH, CW),
        [
            (170, 54, 17.5),
            (213, 47, 31.5),
            (263, 44, 30.5),
            (306, 30, 7.5),
            (308, 52, 11.5),
            (241, 80, 24.5),
            (285, 81, 24.5),
            (300, 112, 19.0),
            (209, 94, 12.0),
            (257, 119, 18.0),
            (196, 66, 18.0),
        ],
        (ox, oy),
    )
    roi_crown = poly_mask(
        (CH, CW),
        [(150, 10), (330, 10), (330, 136), (282, 136), (275, 142), (236, 140), (234, 108), (195, 108), (192, 82), (150, 72)],
        (ox, oy),
    )
    m_crown = a_full & roi_crown & (crown_geom | (g > 154) | (b > 156))
    m_crown = fill_holes(cv2.morphologyEx(m_crown.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0) & a_full
    save_trimmed(parts_meta, "azure_z_froth_crown", soft_extract(canvas, m_crown, feather_px=0.6))

    left_geom = circles_mask(
        (CH, CW),
        [
            (61, 108, 12.0),
            (46, 126, 20.5),
            (44, 152, 16.5),
            (58, 170, 8.5),
            (26, 198, 18.5),
        ],
        (ox, oy),
    )
    roi_left = poly_mask((CH, CW), [(4, 92), (76, 92), (72, 142), (68, 180), (48, 220), (4, 220)], (ox, oy))
    m_left = a_full & roi_left & (left_geom | (g > 154))
    m_left = fill_holes(cv2.morphologyEx(m_left.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0) & a_full
    save_trimmed(parts_meta, "azure_z_froth_left", soft_extract(canvas, m_left, feather_px=0.6))

    puddle_geom = circles_mask(
        (CH, CW),
        [
            (284, 244, 22.0),
            (266, 269, 25.0),
            (309, 269, 21.5),
        ],
        (ox, oy),
    )
    roi_puddle = poly_mask((CH, CW), [(236, 220), (334, 220), (334, 296), (236, 296)], (ox, oy))
    m_puddle = a_full & roi_puddle & (puddle_geom | (g > 150))
    m_puddle = fill_holes(cv2.morphologyEx(m_puddle.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)) > 0) & a_full
    save_trimmed(parts_meta, "azure_z_froth_puddle", soft_extract(canvas, m_puddle, feather_px=0.6))

    smooth_dome_core = (
        ellipse_mask((CH, CW), cx=186, cy=176, rx=130, ry=124, angle=0.0, offset=(ox, oy))
        | poly_mask((CH, CW), [(38, 212), (345, 200), (374, 232), (350, 272), (288, 281), (236, 285), (90, 288), (22, 255)], (ox, oy))
    )
    protruding_bubbles = (
        poly_mask((CH, CW), [(155, 0), (340, 0), (340, 132), (308, 132), (294, 108), (272, 82), (240, 62), (195, 50), (155, 49)], (ox, oy))
        | poly_mask((CH, CW), [(0, 92), (68, 96), (52, 136), (42, 182), (30, 216), (0, 216)], (ox, oy))
        | poly_mask((CH, CW), [(238, 284), (290, 281), (334, 271), (334, 310), (238, 310)], (ox, oy))
    )
    safe_outer = cv2.erode(a_full.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
    on_froth_edge = m_crown | m_left | m_puddle
    raw_bare = (a_full & ~protruding_bubbles) | (smooth_dome_core & safe_outer & ~protruding_bubbles)
    bare_dome_mask = fill_holes(largest_cc((raw_bare & ~on_froth_edge) | (raw_bare & on_froth_edge & safe_outer)))

    ell13 = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13))
    froth_scar = cv2.dilate((m_crown | m_left | m_puddle).astype(np.uint8), ell13) > 0
    valid_gel = bare_dome_mask & ~froth_scar & ~sockets_mask & (g < 158)
    eye_snap = body_rgb[sockets_mask].copy()
    body_inpainted = harmonic_inpaint(body_rgb, valid_gel, bare_dome_mask & ~valid_gel, iters=480)

    reconstructed = bare_dome_mask & froth_scar
    if reconstructed.any():
        dist_in = cv2.distanceTransform(bare_dome_mask.astype(np.uint8), cv2.DIST_L2, 5)
        rim_mul = np.clip(0.80 + 0.20 * np.minimum(dist_in / 16.0, 1.0), 0.80, 1.0)
        body_inpainted[reconstructed] = np.clip(
            body_inpainted[reconstructed].astype(np.float32) * rim_mul[reconstructed, None], 0, 255
        ).astype(np.uint8)
    body_inpainted[sockets_mask] = eye_snap

    body_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
    dome_aa = cv2.GaussianBlur(bare_dome_mask.astype(np.float32), (3, 3), 0.7)
    orig_edge = bare_dome_mask & ~cv2.dilate(on_froth_edge.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
    alpha_out = np.clip(dome_aa * 255.0, 0, 255).astype(np.uint8)
    alpha_out[orig_edge] = canvas[orig_edge, 3]
    keep = alpha_out > 2
    body_rgba[keep, :3] = body_inpainted[keep]
    body_rgba[keep, 3] = alpha_out[keep]
    save_trimmed(parts_meta, "azure_body", body_rgba)

    build_inner_and_aura("azure", body_rgba, sockets_mask, parts_meta, core_tint=(96, 214, 208), aura_tint=(110, 242, 232))
    build_projectiles_for_skin("azure", parts_meta)


def build_inner_and_aura(
    skin: str,
    body_rgba: np.ndarray,
    sockets_mask: np.ndarray,
    parts_meta: dict,
    core_tint: tuple[int, int, int],
    aura_tint: tuple[int, int, int],
) -> None:
    body_a = body_rgba[..., 3] > 20
    ys, xs = np.where(body_a)
    cx = 0.5 * (xs.min() + xs.max())
    cy = 0.50 * ys.min() + 0.50 * ys.max()
    rx = 0.28 * (xs.max() - xs.min())
    ry = 0.24 * (ys.max() - ys.min())

    yy, xx = np.mgrid[0:CH, 0:CW]
    r2 = ((xx - cx) / max(rx, 1.0)) ** 2 + ((yy - cy) / max(ry, 1.0)) ** 2
    blob = np.clip(1.0 - r2, 0.0, 1.0) ** 2.1

    sock_clear = cv2.dilate(sockets_mask.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (29, 29))).astype(np.float32)
    sock_clear = cv2.GaussianBlur(sock_clear, (25, 25), 7.0)
    inner_in = cv2.erode(body_a.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (19, 19))).astype(np.float32)
    inner_in = cv2.GaussianBlur(inner_in, (19, 19), 5.5)

    inner_alpha = np.round(46.0 * blob * inner_in * (1.0 - np.clip(sock_clear, 0.0, 1.0))).astype(np.uint8)
    inner_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
    keep_i = inner_alpha > 2
    inner_rgba[keep_i, :3] = np.array(core_tint, dtype=np.uint8)
    inner_rgba[keep_i, 3] = inner_alpha[keep_i]
    save_trimmed(parts_meta, f"{skin}_inner_blob", inner_rgba)

    aura_f = cv2.GaussianBlur(body_a.astype(np.float32), (31, 31), 9.5)
    aura_alpha = np.round(132.0 * np.clip(aura_f, 0.0, 1.0)).astype(np.uint8)
    aura_rgba = np.zeros((CH, CW, 4), dtype=np.uint8)
    keep_a = aura_alpha > 3
    aura_rgba[keep_a, :3] = np.array(aura_tint, dtype=np.uint8)
    aura_rgba[keep_a, 3] = aura_alpha[keep_a]
    save_trimmed(parts_meta, f"{skin}_aura", aura_rgba)


def build_shared_shadow(parts_meta: dict) -> None:
    yy, xx = np.mgrid[0:CH, 0:CW]
    cx, cy = float(ORIGIN[0]), float(ORIGIN[1] - 2)
    rx, ry = 178.0, 25.0
    d2 = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
    falloff = np.clip(1.0 - d2, 0.0, 1.0) ** 1.45
    ang = np.arctan2(yy - cy, xx - cx)
    chalk = 0.94 + 0.06 * np.sin(7.0 * ang + 0.5) * np.cos(11.0 * ang)
    a = np.round(118.0 * falloff * chalk).astype(np.uint8)
    sh = np.zeros((CH, CW, 4), dtype=np.uint8)
    keep = a > 2
    sh[keep, :3] = np.array([18, 20, 27], dtype=np.uint8)
    sh[keep, 3] = a[keep]
    save_trimmed(parts_meta, "shadow", sh)


def composite_part(dst: np.ndarray, parts_meta: dict, pk: str, dx: int = 0, dy: int = 0, clip_poly: list | None = None) -> None:
    if pk not in parts_meta:
        return
    m = parts_meta[pk]
    im = np.array(Image.open(PARTS_DIR / f"{pk}.png").convert("RGBA"))
    layer = np.zeros((CH, CW, 4), dtype=np.uint8)
    x0, y0 = m["x"] + dx, m["y"] + dy
    h, w = im.shape[:2]
    sx0, sy0 = max(0, -x0), max(0, -y0)
    dx0, dy0 = max(0, x0), max(0, y0)
    cw = min(w - sx0, CW - dx0)
    ch = min(h - sy0, CH - dy0)
    if cw <= 0 or ch <= 0:
        return
    layer[dy0 : dy0 + ch, dx0 : dx0 + cw] = im[sy0 : sy0 + ch, sx0 : sx0 + cw]
    if clip_poly is not None:
        cm = np.zeros((CH, CW), dtype=np.uint8)
        cv2.fillPoly(cm, [np.round(np.asarray(clip_poly)).astype(np.int32)], 255, lineType=cv2.LINE_AA)
        layer[..., 3] = (layer[..., 3].astype(np.uint16) * cm.astype(np.uint16) // 255).astype(np.uint8)
    a = layer[..., 3:4].astype(np.float32) / 255.0
    dst[:] = np.clip(dst * (1.0 - a) + layer[..., :3] * a, 0, 255).astype(np.uint8)


def render_parts_audit(raw_canvases: dict[str, np.ndarray], parts_meta: dict, eye_clips: dict) -> None:
    """Render 5-column x 3-row audit sheet including harpoon extrusion test & hand-painted projectiles!"""
    PREV_DIR.mkdir(parents=True, exist_ok=True)
    rows = []
    for sk in ("crimson", "amber", "azure"):
        bg0 = np.full((CH, CW, 3), (32, 36, 44), dtype=np.uint8)
        rc = raw_canvases[sk]
        ra = rc[..., 3:4].astype(np.float32) / 255.0
        bg0[:] = np.clip(bg0 * (1.0 - ra) + rc[..., :3] * ra, 0, 255).astype(np.uint8)
        cv2.putText(bg0, f"{sk}: v4 Raw Cutout", (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (240, 240, 240), 2)

        # Col 1: Reassembled Setup
        bg1 = np.full((CH, CW, 3), (32, 36, 44), dtype=np.uint8)
        composite_part(bg1, parts_meta, "shadow")
        if sk == "crimson":
            composite_part(bg1, parts_meta, "crimson_c_shard_back")
        composite_part(bg1, parts_meta, f"{sk}_body")
        composite_part(bg1, parts_meta, f"{sk}_inner_blob")
        if sk == "crimson":
            for pk in ("crimson_c_shard_tall_r", "crimson_c_shard_tall_l", "crimson_c_shard_low_r", "crimson_c_shard_low_l", "crimson_c_collars"):
                composite_part(bg1, parts_meta, pk)
        composite_part(bg1, parts_meta, f"{sk}_iris_main", clip_poly=eye_clips["eye_clip_main"][sk])
        composite_part(bg1, parts_meta, f"{sk}_lid_main", dy=0, clip_poly=eye_clips["eye_clip_main"][sk])
        composite_part(bg1, parts_meta, f"{sk}_iris_sec", clip_poly=eye_clips["eye_clip_sec"][sk])
        composite_part(bg1, parts_meta, f"{sk}_lid_sec", dy=0, clip_poly=eye_clips["eye_clip_sec"][sk])
        if sk == "amber":
            for pk in ("amber_a_stone_top", "amber_a_stone_flank_r", "amber_a_stone_l", "amber_a_stone_cheek"):
                composite_part(bg1, parts_meta, pk)
        elif sk == "azure":
            for pk in ("azure_z_froth_crown", "azure_z_froth_left", "azure_z_froth_puddle"):
                composite_part(bg1, parts_meta, pk)
        cv2.putText(bg1, f"{sk}: Reassembled Setup", (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (160, 255, 180), 2)

        # Col 2: Bare Gel Dome
        bg2 = np.full((CH, CW, 3), (32, 36, 44), dtype=np.uint8)
        composite_part(bg2, parts_meta, "shadow")
        composite_part(bg2, parts_meta, f"{sk}_body")
        cv2.putText(bg2, f"{sk}: Bare Gel Dome", (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (255, 220, 140), 2)

        # Col 3: +32px Harpoon Extrusion / Blast-Door Clamp / Blink Test
        bg3 = np.full((CH, CW, 3), (32, 36, 44), dtype=np.uint8)
        composite_part(bg3, parts_meta, "shadow")
        if sk == "crimson":
            composite_part(bg3, parts_meta, "crimson_c_shard_back", dx=-4, dy=-32)
        composite_part(bg3, parts_meta, f"{sk}_body")
        composite_part(bg3, parts_meta, f"{sk}_inner_blob")
        if sk == "crimson":
            composite_part(bg3, parts_meta, "crimson_c_shard_tall_r", dx=18, dy=-26)
            composite_part(bg3, parts_meta, "crimson_c_shard_tall_l", dx=-17, dy=-28)
            composite_part(bg3, parts_meta, "crimson_c_shard_low_r", dx=22, dy=-8)
            composite_part(bg3, parts_meta, "crimson_c_shard_low_l", dx=-32, dy=-6)
            composite_part(bg3, parts_meta, "crimson_c_collars")
        composite_part(bg3, parts_meta, f"{sk}_iris_main", clip_poly=eye_clips["eye_clip_main"][sk])
        composite_part(bg3, parts_meta, f"{sk}_lid_main", dy=62, clip_poly=eye_clips["eye_clip_main"][sk])
        composite_part(bg3, parts_meta, f"{sk}_iris_sec", clip_poly=eye_clips["eye_clip_sec"][sk])
        composite_part(bg3, parts_meta, f"{sk}_lid_sec", dy=46, clip_poly=eye_clips["eye_clip_sec"][sk])
        if sk == "amber":
            composite_part(bg3, parts_meta, "amber_a_stone_top", dx=-11, dy=13)
            composite_part(bg3, parts_meta, "amber_a_stone_flank_r", dx=-12, dy=6)
            composite_part(bg3, parts_meta, "amber_a_stone_l", dx=5, dy=12)
            composite_part(bg3, parts_meta, "amber_a_stone_cheek", dx=-10, dy=-9)
        elif sk == "azure":
            composite_part(bg3, parts_meta, "azure_z_froth_crown", dy=-14)
            composite_part(bg3, parts_meta, "azure_z_froth_left", dy=-12)
            composite_part(bg3, parts_meta, "azure_z_froth_puddle", dy=-10)
        cv2.putText(bg3, f"{sk}: +32px Extrude & Blink", (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (180, 220, 255), 2)

        # Col 4: Rigid Parts + Hand-Painted Projectiles (proj_1 & proj_2)
        bg4 = np.full((CH, CW, 3), (24, 28, 36), dtype=np.uint8)
        if sk == "crimson":
            composite_part(bg4, parts_meta, "crimson_c_shard_back", dy=-18)
            composite_part(bg4, parts_meta, "crimson_c_shard_tall_l", dx=-24, dy=-18)
            composite_part(bg4, parts_meta, "crimson_c_shard_tall_r", dx=24, dy=-18)
            composite_part(bg4, parts_meta, "crimson_c_shard_low_l", dx=-10, dy=12)
            composite_part(bg4, parts_meta, "crimson_c_shard_low_r", dx=18, dy=12)
        elif sk == "amber":
            composite_part(bg4, parts_meta, "amber_a_stone_top", dx=10, dy=-16)
            composite_part(bg4, parts_meta, "amber_a_stone_flank_r", dx=16, dy=4)
            composite_part(bg4, parts_meta, "amber_a_stone_l", dx=-14, dy=-12)
            composite_part(bg4, parts_meta, "amber_a_stone_cheek", dx=6, dy=10)
        else:
            composite_part(bg4, parts_meta, "azure_z_froth_crown", dx=8, dy=-16)
            composite_part(bg4, parts_meta, "azure_z_froth_left", dx=-14, dy=-4)
            composite_part(bg4, parts_meta, "azure_z_froth_puddle", dx=8, dy=6)
        # Draw proj_1 & proj_2 cleanly in bottom-left showcase box
        cv2.rectangle(bg4, (10, 326), (255, 402), (38, 44, 56), -1)
        composite_part(bg4, parts_meta, f"{sk}_proj_1", dx=-75, dy=118)
        composite_part(bg4, parts_meta, f"{sk}_proj_2", dx=35, dy=166)
        cv2.putText(bg4, f"{sk}: Parts + proj_1/2", (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (255, 180, 220), 2)

        rows.append(np.hstack([bg0, bg1, bg2, bg3, bg4]))

    sheet = np.vstack(rows)
    out_path = PREV_DIR / "parts_audit.jpg"
    Image.fromarray(sheet).save(out_path, quality=92)
    print(f"Saved audit sheet: {out_path} ({sheet.shape[1]}x{sheet.shape[0]})")


def main() -> None:
    PARTS_DIR.mkdir(parents=True, exist_ok=True)
    for old_png in PARTS_DIR.glob("*.png"):
        old_png.unlink()

    parts_meta: dict[str, dict] = {}
    eye_clips: dict[str, dict[str, list[list[float]]]] = {"eye_clip_main": {}, "eye_clip_sec": {}}
    raw_canvases: dict[str, np.ndarray] = {}

    build_shared_shadow(parts_meta)

    for sk, fn in (("crimson", decompose_crimson), ("amber", decompose_amber), ("azure", decompose_azure)):
        canvas, ox, oy, _ = load_placed(sk)
        raw_canvases[sk] = canvas
        fn(canvas, ox, oy, parts_meta, eye_clips)

    (PARTS_DIR / "meta.json").write_text(json.dumps(parts_meta, indent=2), encoding="utf-8", newline="\n")
    (PARTS_DIR / "eye_clips.json").write_text(json.dumps(eye_clips, indent=2), encoding="utf-8", newline="\n")
    print(f"Extracted {len(parts_meta)} PNG parts -> {PARTS_DIR}")
    render_parts_audit(raw_canvases, parts_meta, eye_clips)


if __name__ == "__main__":
    main()
