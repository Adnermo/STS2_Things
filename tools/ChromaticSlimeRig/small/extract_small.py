"""Extract v4 Small Chromatic Slime (crimson, amber, azure) onto a unified 410x380 canvas.
Produces zero-ghosting bare gel domes, 3-layer hardware-clipped cyclops eyes (socket + iris + eyelid),
and 3 independent 100%-rigid crown pieces (crest_l, crest_c, crest_r) per strain.
"""
from __future__ import annotations

import json
import math
import os
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
PARTS = HERE / "parts"
PREV = HERE / "prev"
SRC_DIR = HERE.parents[2] / "source_assets" / "monsters" / "chromatic_slime" / "v4_cutouts"

CANVAS_W, CANVAS_H = 410, 380
ORIGIN = (205, 356)
SKINS = ("crimson", "amber", "azure")


def poly_mask(shape: tuple[int, int], pts: list[tuple[float, float]]) -> np.ndarray:
    m = np.zeros(shape, dtype=np.uint8)
    arr = np.round(np.asarray(pts, dtype=np.float32)).astype(np.int32)
    cv2.fillPoly(m, [arr], 255, lineType=cv2.LINE_AA)
    return m


def fill_external_holes(mask_bool: np.ndarray) -> np.ndarray:
    u8 = (mask_bool.astype(np.uint8)) * 255
    u8 = cv2.morphologyEx(u8, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))
    cnts, _ = cv2.findContours(u8, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    out = np.zeros_like(u8)
    for c in cnts:
        if cv2.contourArea(c) >= 18:
            cv2.drawContours(out, [c], -1, 255, thickness=cv2.FILLED)
    return out > 0


def catmull_rom_y(ctrl: list[tuple[float, float]], xs: np.ndarray) -> np.ndarray:
    pts = np.asarray(ctrl, dtype=np.float64)
    # Clamped Catmull-Rom spline evaluated along x
    ext = np.vstack([pts[0] - (pts[1] - pts[0]), pts, pts[-1] + (pts[-1] - pts[-2])])
    dense_x = []
    dense_y = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        t = np.linspace(0.0, 1.0, 48, endpoint=(i == len(ext) - 3))
        t2, t3 = t * t, t * t * t
        xy = 0.5 * (
            (2.0 * p1[None, :])
            + (-p0 + p2)[None, :] * t[:, None]
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3)[None, :] * t2[:, None]
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3)[None, :] * t3[:, None]
        )
        dense_x.append(xy[:, 0])
        dense_y.append(xy[:, 1])
    dx = np.concatenate(dense_x)
    dy = np.concatenate(dense_y)
    order = np.argsort(dx)
    return np.interp(xs, dx[order], dy[order])


def harmonic_fill(rgb: np.ndarray, hole: np.ndarray, valid: np.ndarray, iters: int = 320) -> np.ndarray:
    """Smooth harmonic Laplace solver inside `hole`, anchored by `valid & ~hole` pixels."""
    out = rgb.astype(np.float32).copy()
    seed = valid & ~hole
    # Initialize hole from nearest clean boundary via OpenCV Telea on valid bbox
    init_u8 = np.clip(out, 0, 255).astype(np.uint8)
    inpaint_mask = (hole | ~valid).astype(np.uint8) * 255
    # Restrict Telea to dilated hole so outer empty background doesn't slow it down
    dil = cv2.dilate(hole.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))) > 0
    telea_mask = (hole.astype(np.uint8)) * 255
    # Fill ~valid neighbors with nearest valid color first so Telea never samples black transparent bg
    dist_inv, labels = cv2.distanceTransformWithLabels(
        (~seed).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL
    )
    sy, sx = np.where(seed)
    # labels are 1-based indices into row-major order of zero pixels (~(~seed) == seed)
    flat_rgb = out[seed]
    nearest_rgb = flat_rgb[np.clip(labels - 1, 0, len(flat_rgb) - 1)]
    work = np.where(seed[..., None], out, nearest_rgb)
    telea = cv2.inpaint(np.clip(work, 0, 255).astype(np.uint8), telea_mask, 7, cv2.INPAINT_TELEA).astype(np.float32)
    out[hole] = telea[hole]

    # Jacobi harmonic relaxation inside hole (neumann boundary at outside-dome ~valid)
    upd = hole.copy()
    dom = valid | hole
    for _ in range(iters):
        up = np.roll(out, -1, axis=0)
        dn = np.roll(out, 1, axis=0)
        lf = np.roll(out, -1, axis=1)
        rt = np.roll(out, 1, axis=1)
        m_up = np.roll(dom, -1, axis=0)[..., None]
        m_dn = np.roll(dom, 1, axis=0)[..., None]
        m_lf = np.roll(dom, -1, axis=1)[..., None]
        m_rt = np.roll(dom, 1, axis=1)[..., None]
        wsum = m_up.astype(np.float32) + m_dn.astype(np.float32) + m_lf.astype(np.float32) + m_rt.astype(np.float32)
        avg = (up * m_up + dn * m_dn + lf * m_lf + rt * m_rt) / np.maximum(wsum, 1.0)
        out[upd] = avg[upd]
    return np.clip(out, 0, 255)


def save_cropped(name: str, rgba: np.ndarray, meta: dict[str, dict[str, int]], pad: int = 2) -> None:
    ys, xs = np.where(rgba[..., 3] > 2)
    if len(xs) == 0:
        raise RuntimeError(f"Empty part: {name}")
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(rgba.shape[1], int(xs.max()) + 1 + pad)
    y1 = min(rgba.shape[0], int(ys.max()) + 1 + pad)
    crop = rgba[y0:y1, x0:x1]
    Image.fromarray(crop).save(PARTS / f"{name}.png", optimize=True)
    meta[name] = {"x": x0, "y": y0, "w": int(x1 - x0), "h": int(y1 - y0)}


def make_shadow(meta: dict[str, dict[str, int]]) -> np.ndarray:
    """Shared ~330x52 dry-chalk charcoal oval centered around ORIGIN=(205, 356)."""
    canvas = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = float(ORIGIN[0]), float(ORIGIN[1] - 3)
    rx, ry = 164.0, 24.5
    r = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2)
    # Dry-chalk soft stepped falloff
    rng = np.random.default_rng(42)
    grain = cv2.GaussianBlur(rng.normal(0.0, 1.0, (CANVAS_H, CANVAS_W)).astype(np.float32), (5, 5), 1.1)
    r_noisy = r + 0.032 * grain
    alpha = np.clip((1.0 - r_noisy) / 0.34, 0.0, 1.0)
    alpha = (alpha ** 1.25) * 0.56
    canvas[..., 0] = 20
    canvas[..., 1] = 18
    canvas[..., 2] = 26
    canvas[..., 3] = np.round(alpha * 255.0).astype(np.uint8)
    save_cropped("shadow", canvas, meta, pad=2)
    return canvas


def place_on_canvas(raw: np.ndarray) -> tuple[np.ndarray, int, int]:
    ys, xs = np.where(raw[..., 3] > 8)
    cx_raw = 0.5 * (int(xs.min()) + int(xs.max()))
    ox = int(round(ORIGIN[0] - cx_raw))
    oy = int(ORIGIN[1] - int(ys.max()))
    placed = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    h, w = raw.shape[:2]
    placed[oy : oy + h, ox : ox + w] = raw
    return placed, ox, oy


def extract_cyclops_eye(
    placed: np.ndarray, skin: str, ox: int, oy: int
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, list[list[float]]]:
    """Return (socket_mask, iris_mask_raw, iris_rgba, eyelid_rgba, clip_poly)."""
    rgb = placed[..., :3].astype(np.float32)
    a = placed[..., 3] > 32
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W]

    # Tight eye ROI in normalized 410x380 coordinates
    roi = (xx >= 86) & (xx <= 230) & (yy >= 210) & (yy <= 290) & a
    iris_roi = (xx >= 115) & (xx <= 184) & (yy >= 220) & (yy <= 264) & a

    if skin == "crimson":
        is_iris = iris_roi & (rgb[..., 0] > 165) & (rgb[..., 1] > 118) & ((rgb[..., 0] - rgb[..., 2]) > 50)
        is_dark = roi & (rgb[..., 0] < 112) & (rgb[..., 1] < 44)
        lid_top_col = np.array([175.0, 74.0, 64.0], dtype=np.float32)
        lid_rim_col = np.array([118.0, 38.0, 40.0], dtype=np.float32)
    elif skin == "amber":
        is_iris = iris_roi & (rgb[..., 0] > 178) & (rgb[..., 1] > 138) & (rgb[..., 2] > 48)
        is_dark = roi & (rgb[..., 0] < 126) & (rgb[..., 1] < 88) & (yy >= 224)
        lid_top_col = np.array([192.0, 119.0, 31.0], dtype=np.float32)
        lid_rim_col = np.array([138.0, 82.0, 20.0], dtype=np.float32)
    else:  # azure
        is_iris = iris_roi & (rgb[..., 0] > 132) & (rgb[..., 1] > 118)
        is_dark = roi & (rgb[..., 1] < 106) & (rgb[..., 2] < 112) & (yy >= 221)
        lid_top_col = np.array([64.0, 149.0, 151.0], dtype=np.float32)
        lid_rim_col = np.array([34.0, 92.0, 96.0], dtype=np.float32)

    is_iris = fill_external_holes(is_iris)
    sock_raw = is_iris | is_dark
    num, lbls, stats, _ = cv2.connectedComponentsWithStats(sock_raw.astype(np.uint8))
    best = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    socket_mask = fill_external_holes(lbls == best)

    # Smooth crescent clipping polygon (~24 vertices) slightly inset (0.5px) so clip hugs socket rim cleanly
    sock_u8 = (socket_mask.astype(np.uint8)) * 255
    sock_blur = cv2.GaussianBlur(sock_u8, (5, 5), 0.9)
    cnts, _ = cv2.findContours((sock_blur >= 120).astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cnt = max(cnts, key=cv2.contourArea)[:, 0, :].astype(np.float64)
    # Uniform arc-length resampling to 26 vertices for clean hardware clipping mesh
    diffs = np.diff(np.vstack([cnt, cnt[:1]]), axis=0)
    seg_len = np.hypot(diffs[:, 0], diffs[:, 1])
    cum = np.concatenate([[0.0], np.cumsum(seg_len)])
    targets = np.linspace(0.0, cum[-1], 26, endpoint=False)
    px = np.interp(targets, cum, np.append(cnt[:, 0], cnt[0, 0]))
    py = np.interp(targets, cum, np.append(cnt[:, 1], cnt[0, 1]))
    clip_poly = [[round(float(x), 1), round(float(y), 1)] for x, y in zip(px, py)]

    # Build clean unscaled ochre-gold iris padded upward by 15px under the upper lid!
    iy, ix = np.where(is_iris)
    icx = 0.5 * (float(ix.min()) + float(ix.max()))
    icy_bot = float(iy.max())
    irx = 0.5 * (float(ix.max()) - float(ix.min())) + 0.8
    # Fit full circle/ellipse passing through the lower semi-disc & extending 15px above upper brow
    iry_down = icy_bot - float(iy.min())
    icy = float(iy.min()) + 2.0  # center right near upper socket brow line!
    iry_low = icy_bot - icy
    iry_high = 19.5  # generous upward pad under upper lid so downward/sideways gaze never gaps!

    iris_rgba = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    dy_norm = np.where(yy >= icy, (yy - icy) / max(iry_low, 1.0), (yy - icy) / iry_high)
    r_ell = np.sqrt(((xx - icx) / irx) ** 2 + dy_norm ** 2)
    iris_alpha = np.clip((1.0 - r_ell) / 0.045, 0.0, 1.0)

    # Paint flat matte ochre-gold matching original iris + subtle warm gouache edge bevel
    core_cols = rgb[is_iris & (r_ell < 0.78)]
    base_iris_col = np.median(core_cols, axis=0) if len(core_cols) else np.array([212.0, 163.0, 75.0])
    rim_iris_col = np.clip(base_iris_col * np.array([0.94, 0.96, 1.12]), 0, 255)
    rim_t = np.clip((r_ell - 0.76) / 0.24, 0.0, 1.0)[..., None]
    synth_col = base_iris_col[None, None, :] * (1.0 - 0.35 * rim_t) + rim_iris_col[None, None, :] * (0.35 * rim_t)
    # Preserve authentic original pixels inside core of real iris, blend smoothly into upward pad
    real_inner = cv2.erode(is_iris.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))) > 0
    blend_real = cv2.GaussianBlur(real_inner.astype(np.float32), (5, 5), 1.2)[..., None]
    final_iris_rgb = synth_col * (1.0 - blend_real) + rgb * blend_real
    iris_rgba[..., :3] = np.clip(np.round(final_iris_rgb), 0, 255).astype(np.uint8)
    iris_rgba[..., 3] = np.round(iris_alpha * 255.0).astype(np.uint8)

    # Build clean unscaled matte gouache upper eyelid resting JUST ABOVE the upper socket line at t=0!
    # +40px wider on BOTH left and right of `eye_clip` socket with ZERO alpha fade inside socket width
    # so even under heavy horizontal fluid shear (-65px..+30px), the eyelid NEVER shows a side edge!
    sy, sx = np.where(socket_mask)
    sock_x0, sock_x1 = int(sx.min()), int(sx.max())
    pad_w = 40  # > +36px on both left and right of socket!
    sx0, sx1 = max(2, sock_x0 - pad_w), min(CANVAS_W - 3, sock_x1 + pad_w)
    x_cols = np.arange(sx0, sx1 + 1)
    y_top = np.zeros_like(x_cols, dtype=np.float32)
    for idx_x, x_val in enumerate(x_cols):
        col_ys = np.where(socket_mask[:, x_val])[0]
        if len(col_ys):
            y_top[idx_x] = float(col_ys.min())
        else:
            near_x = int(np.clip(x_val, sock_x0, sock_x1))
            y_top[idx_x] = float(np.where(socket_mask[:, near_x])[0].min())
    # Keep resting lower lid rim strictly above the highest socket point when shifted horizontally,
    # while preserving subtle natural brow contour inside socket
    y_sock_min = float(sy.min())
    y_top_smooth = cv2.GaussianBlur(y_top.reshape(1, -1), (1, 19), 3.6).ravel()
    y_top_smooth = np.minimum(y_top_smooth - 1.6, y_sock_min + 0.8) - 1.4

    eyelid_rgba = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    lid_h = 86.0  # Generous height so full blink covers 100% of socket with zero top or bottom gap!
    for idx_x, x_val in enumerate(x_cols):
        yb = y_top_smooth[idx_x]
        yt = yb - lid_h
        col_y = np.arange(CANVAS_H, dtype=np.float32)
        a_bot = np.clip((yb - col_y + 0.9) / 1.3, 0.0, 1.0)
        a_top = np.clip((col_y - yt + 0.9) / 1.6, 0.0, 1.0)
        # Strictly 100% opaque across socket + 37px margin on BOTH sides; 2px AA only at +/-40px outer edge
        dist_edge = float(min(x_val - sx0, sx1 - x_val))
        a_side = np.clip((dist_edge + 0.5) / 2.0, 0.0, 1.0)
        a_col = a_bot * a_top * a_side
        active = a_col > 0.01
        if not np.any(active):
            continue
        rim_band = np.clip(1.0 - (yb - col_y) / 3.5, 0.0, 1.0)[:, None]
        shade = (1.0 + 0.03 * np.clip((yb - col_y) / lid_h, 0.0, 1.0))[:, None]
        c_col = np.clip(lid_top_col[None, :] * shade * (1.0 - 0.55 * rim_band) + lid_rim_col[None, :] * (0.55 * rim_band), 0, 255)
        eyelid_rgba[active, x_val, :3] = np.round(c_col[active]).astype(np.uint8)
        eyelid_rgba[active, x_val, 3] = np.round(a_col[active] * 255.0).astype(np.uint8)

    return socket_mask, is_iris, iris_rgba, eyelid_rgba, clip_poly


def clean_socket_interior(body_rgb: np.ndarray, socket_mask: np.ndarray, is_iris: np.ndarray, skin: str) -> np.ndarray:
    """Replace the yellow iris inside `body_rgb`'s eye socket with clean dark socket interior shading."""
    out = body_rgb.astype(np.float32).copy()
    # Dilate iris mask by 3px inside socket so ZERO yellow fringe remains
    iris_hole = (cv2.dilate(is_iris.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7))) > 0) & socket_mask
    # Also catch any bright yellow-ish transition pixels inside socket
    if skin == "crimson":
        extra = socket_mask & ((out[..., 1] > 42) | (out[..., 0] > 112))
    elif skin == "amber":
        extra = socket_mask & ((out[..., 1] > 86) | (out[..., 0] > 125))
    else:
        extra = socket_mask & ((out[..., 0] > 58) | (out[..., 1] > 104))
    # Keep thin 1.5px outer socket rim intact, clean interior yellow
    sock_inner = cv2.erode(socket_mask.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))) > 0
    hole = (iris_hole | (extra & sock_inner)) & socket_mask
    clean_dark = socket_mask & ~hole

    # Row-by-row horizontal interpolation across clean dark socket pixels (preserves vertical socket shading!)
    ys = np.unique(np.where(hole)[0])
    for y in ys:
        hx = np.where(hole[y])[0]
        cx = np.where(clean_dark[y])[0]
        if len(cx) >= 3:
            for ch in range(3):
                out[y, hx, ch] = np.interp(hx, cx, out[y, cx, ch])
    # Fallback harmonic smoothing inside hole for silky vertical continuity
    out = harmonic_fill(out, hole, socket_mask, iters=90)
    return out


def extend_crimson_crest_root(
    orig_part: np.ndarray,
    full_m: np.ndarray,
    slot_n: str,
    body_keep_mask: np.ndarray,
) -> np.ndarray:
    """Paint a clean +24px downward subsurface dark-ruby faceted root shaft UNDER existing crystal pixels."""
    axes_inward = {
        "crest_l": (0.38, 0.925),   # down-right into dome (matches outward up-left harpoon axis!)
        "crest_c": (0.14, 0.990),   # straight down into center dome
        "crest_r": (-0.48, 0.877),  # down-left into dome (matches outward up-right harpoon axis!)
    }
    ax_x, ax_y = axes_inward[slot_n]
    px_x, px_y = -ax_y, ax_x  # perpendicular width axis

    ys, xs = np.where(full_m)
    cx, cy = float(xs.mean()), float(ys.mean())
    s_pts = (xs - cx) * ax_x + (ys - cy) * ax_y
    w_pts = (xs - cx) * px_x + (ys - cy) * px_y
    s_max = float(np.percentile(s_pts, 98.5))

    # Measure crystal shaft width just above its base rim so root continues crystal planes seamlessly
    band = (s_pts >= s_max - 22.0) & (s_pts <= s_max - 5.0)
    if np.sum(band) < 12:
        band = s_pts >= np.percentile(s_pts, 65.0)
    w_min = float(np.percentile(w_pts[band], 4.0))
    w_max = float(np.percentile(w_pts[band], 96.0))
    w_mid = 0.5 * (w_min + w_max)

    def sw_to_xy(s_val: float, w_val: float) -> tuple[float, float]:
        return (cx + s_val * ax_x + w_val * px_x, cy + s_val * ax_y + w_val * px_y)

    # 5-vertex polygonal faceted crystal root extending +26px along inward axis (>= +24px downward!)
    root_len = 26.5
    poly_pts = [
        sw_to_xy(s_max - 12.0, w_min * 0.88),
        sw_to_xy(s_max - 12.0, w_max * 0.88),
        sw_to_xy(s_max + root_len * 0.62, w_max * 0.72),
        sw_to_xy(s_max + root_len, w_mid * 0.15),
        sw_to_xy(s_max + root_len * 0.64, w_min * 0.72),
    ]
    root_a = poly_mask((CANVAS_H, CANVAS_W), poly_pts).astype(np.float32) / 255.0
    root_a *= body_keep_mask.astype(np.float32)

    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    s_grid = (xx - cx) * ax_x + (yy - cy) * ax_y
    w_grid = (xx - cx) * px_x + (yy - cy) * px_y

    # 3 crisp lineless matte-gouache dark-ruby crystal facets + central bevel ridge
    col_left = np.array([124.0, 28.0, 38.0], dtype=np.float32)
    col_ridge = np.array([156.0, 44.0, 52.0], dtype=np.float32)
    col_right = np.array([86.0, 17.0, 27.0], dtype=np.float32)
    col_subsurf = np.array([148.0, 48.0, 46.0], dtype=np.float32)

    t_side = np.clip((w_grid - (w_mid - 1.5)) / 3.0, 0.0, 1.0)[..., None]
    base_facet = col_left[None, None, :] * (1.0 - t_side) + col_right[None, None, :] * t_side
    ridge_w = np.clip(1.0 - np.abs(w_grid - (w_mid - 1.5)) / 2.4, 0.0, 1.0)[..., None]
    root_rgb = base_facet * (1.0 - 0.75 * ridge_w) + col_ridge[None, None, :] * (0.75 * ridge_w)

    # Subtle subsurface crimson-gel warmth on bottommost 5px chisel tip while staying 100% opaque
    tip_t = np.clip((s_grid - (s_max + root_len - 6.0)) / 6.0, 0.0, 1.0)[..., None]
    root_rgb = root_rgb * (1.0 - 0.36 * tip_t) + col_subsurf[None, None, :] * (0.36 * tip_t)

    # Composite original crystal RGBA strictly OVER root RGBA so 100% of original crystal pixels stay intact!
    a_top = orig_part[..., 3:4].astype(np.float32) / 255.0
    a_bot = root_a[..., None]
    out_a = a_top + a_bot * (1.0 - a_top)
    safe_a = np.maximum(out_a, 1e-6)
    out_rgb = (orig_part[..., :3].astype(np.float32) * a_top + root_rgb * a_bot * (1.0 - a_top)) / safe_a

    out = np.zeros_like(orig_part)
    out[..., :3] = np.clip(np.round(out_rgb), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(out_a[..., 0] * 255.0), 0, 255).astype(np.uint8)
    return out


def make_spit_glob(skin: str, meta: dict[str, dict[str, int]]) -> np.ndarray:
    """Hand-painted lineless Mega Crit matte-gouache projectile centered around (138, 224) pointing -X."""
    canvas = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    rgb = np.zeros((CANVAS_H, CANVAS_W, 3), dtype=np.float32)
    alpha = np.zeros((CANVAS_H, CANVAS_W), dtype=np.float32)
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)

    def paint_mask(m_u8: np.ndarray, col: tuple[float, float, float], opacity: float = 1.0) -> None:
        nonlocal rgb, alpha
        a_src = (m_u8.astype(np.float32) / 255.0) * opacity
        c_arr = np.array(col, dtype=np.float32)[None, None, :]
        out_a = a_src + alpha * (1.0 - a_src)
        safe = np.maximum(out_a, 1e-6)
        rgb = (c_arr * a_src[..., None] + rgb * (alpha * (1.0 - a_src))[..., None]) / safe[..., None]
        alpha = out_a

    def ell_mask(cx_e: float, cy_e: float, rx_e: float, ry_e: float, deg: float = 0.0) -> np.ndarray:
        th = math.radians(deg)
        ct, st = math.cos(th), math.sin(th)
        dx = xx - cx_e
        dy = yy - cy_e
        u = (dx * ct + dy * st) / rx_e
        v = (-dx * st + dy * ct) / ry_e
        r = np.sqrt(u * u + v * v)
        return np.clip((1.0 - r) / (1.15 / max(min(rx_e, ry_e), 2.0)), 0.0, 1.0) * 255.0

    if skin == "crimson":
        # Crisp faceted ruby needle-shard + 2 trailing crimson droplets pointing left (-X)
        shard_outer = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(95, 224), (117, 211), (149, 214), (159, 224), (147, 234), (116, 237)],
        )
        shard_lower = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(95, 224), (159, 224), (147, 234), (116, 237)],
        )
        shard_rear = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(140, 224), (149, 214), (159, 224), (147, 234)],
        )
        shard_ridge = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(98, 224), (121, 220.5), (151, 224), (121, 226.5)],
        )
        shard_chip = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(101, 222), (117, 212.5), (132, 214), (116, 216)],
        )
        paint_mask(shard_outer, (194, 48, 58))
        paint_mask(np.minimum(shard_outer, shard_lower), (112, 20, 32))
        paint_mask(np.minimum(shard_outer, shard_rear), (144, 30, 42))
        paint_mask(np.minimum(shard_outer, shard_ridge), (238, 104, 100))
        paint_mask(np.minimum(shard_outer, shard_chip), (246, 144, 134))

        # 2 trailing crimson gel droplets
        d1 = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(161, 218), (165, 213), (173, 214), (179, 217.5), (172, 222), (164, 222)],
        )
        d1_hi = poly_mask((CANVAS_H, CANVAS_W), [(163, 217), (166, 214.5), (173, 215.5), (168, 218)])
        paint_mask(d1, (176, 52, 52))
        paint_mask(np.minimum(d1, d1_hi), (228, 102, 94))

        d2 = ell_mask(182.0, 229.0, 5.8, 3.6, deg=-6.0)
        d2_hi = ell_mask(180.5, 227.8, 3.0, 1.6, deg=-6.0)
        paint_mask(d2, (156, 42, 46))
        paint_mask(np.minimum(d2, d2_hi), (218, 92, 86))

    elif skin == "amber":
        # Chunky angular sandstone pebble + warm amber resin glaze & droplets
        amber_wake = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(138, 212), (156, 216), (162, 224), (154, 234), (138, 236)],
        )
        paint_mask(amber_wake, (202, 128, 28))
        paint_mask(np.minimum(amber_wake, ell_mask(151.0, 219.0, 6.0, 3.2, -12.0)), (242, 184, 68))

        rock_outer = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(104, 225), (112, 210), (130, 206), (147, 212), (152, 225), (143, 238), (117, 239)],
        )
        rock_mid = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(124, 221), (147, 212), (152, 225), (134, 230)],
        )
        rock_bot = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(104, 225), (124, 221), (134, 230), (152, 225), (143, 238), (117, 239)],
        )
        rock_bevel = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(106, 224), (113, 211.5), (130, 207.5), (142, 211.5), (130, 211), (115, 214.5)],
        )
        paint_mask(rock_outer, (176, 155, 114))
        paint_mask(np.minimum(rock_outer, rock_mid), (144, 124, 88))
        paint_mask(np.minimum(rock_outer, rock_bot), (104, 88, 62))
        paint_mask(np.minimum(rock_outer, rock_bevel), (210, 190, 146))

        # 2 trailing warm amber resin droplets
        r1 = ell_mask(168.5, 217.5, 7.2, 4.8, deg=-8.0)
        r1_sh = ell_mask(169.5, 219.5, 6.5, 3.4, deg=-8.0)
        r1_hi = ell_mask(166.5, 215.6, 3.6, 1.8, deg=-10.0)
        paint_mask(r1, (202, 128, 28))
        paint_mask(np.minimum(r1, r1_sh), (150, 88, 18), opacity=0.65)
        paint_mask(np.minimum(r1, r1_hi), (246, 194, 78))

        r2 = ell_mask(181.5, 229.5, 5.2, 3.6, deg=6.0)
        r2_hi = ell_mask(180.2, 228.2, 2.5, 1.4, deg=6.0)
        paint_mask(r2, (192, 118, 24))
        paint_mask(np.minimum(r2, r2_hi), (242, 186, 72))

    else:  # azure
        # Plump seafoam-cyan acid globule + 2 trailing bubbles (authentic LeafSlime spit!)
        tail_poly = poly_mask(
            (CANVAS_H, CANVAS_W),
            [(119, 208.5), (142, 214), (156, 222.5), (142, 232.5), (119, 239.5)],
        )
        head_ell = ell_mask(120.5, 224.0, 16.5, 15.5)
        glob_m = np.maximum(head_ell, tail_poly.astype(np.float32))
        # Smooth teardrop join
        glob_m = cv2.GaussianBlur(glob_m, (3, 3), 0.7)
        glob_m = np.clip((glob_m - 96.0) / 38.0, 0.0, 1.0) * 255.0

        glob_sh = np.maximum(
            ell_mask(124.5, 228.5, 15.5, 12.5),
            poly_mask((CANVAS_H, CANVAS_W), [(122, 224), (156, 222.5), (142, 232.5), (119, 239.5)]).astype(np.float32),
        )
        glob_core = ell_mask(120.0, 221.5, 11.2, 8.8, deg=-8.0)
        glob_spec = ell_mask(113.5, 215.5, 5.4, 2.8, deg=-26.0)

        paint_mask(glob_m, (62, 168, 170))
        paint_mask(np.minimum(glob_m, glob_sh), (36, 116, 122), opacity=0.82)
        paint_mask(np.minimum(glob_m, glob_core), (118, 224, 216))
        paint_mask(np.minimum(glob_m, glob_spec), (218, 252, 248))

        # 2 trailing round froth bubbles
        for bx, by, br in [(165.5, 217.0, 7.4), (180.0, 229.0, 5.2)]:
            bm = ell_mask(bx, by, br, br)
            bi = ell_mask(bx - 0.18 * br, by - 0.18 * br, 0.66 * br, 0.62 * br)
            bh = ell_mask(bx - 0.34 * br, by - 0.34 * br, 0.28 * br, 0.22 * br, deg=-25.0)
            paint_mask(bm, (68, 180, 182))
            paint_mask(np.minimum(bm, bi), (144, 234, 226))
            paint_mask(np.minimum(bm, bh), (226, 253, 249))

    canvas[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    canvas[..., 3] = np.clip(np.round(alpha * 255.0), 0, 255).astype(np.uint8)
    save_cropped(f"{skin}_spit_glob", canvas, meta, pad=2)
    return canvas


def extract_strain(skin: str, meta: dict[str, dict[str, int]], eye_clips: dict[str, list[list[float]]]) -> dict[str, np.ndarray]:
    raw = np.array(Image.open(SRC_DIR / f"small_{skin}.png").convert("RGBA"))
    placed, ox, oy = place_on_canvas(raw)
    rgb = placed[..., :3].astype(np.float32)
    alpha = placed[..., 3].astype(np.float32)
    has_pix = alpha > 12
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W]

    def shift_pts(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
        return [(x + ox, y + oy) for x, y in pts]

    def circle_union(circles: list[tuple[float, float, float]]) -> np.ndarray:
        m = np.zeros((CANVAS_H, CANVAS_W), dtype=np.uint8)
        for cx_r, cy_r, rad in circles:
            cv2.circle(
                m,
                (int(round(cx_r + ox)), int(round(cy_r + oy))),
                int(round(rad)),
                255,
                thickness=cv2.FILLED,
                lineType=cv2.LINE_AA,
            )
        return m > 0

    # 1. Segment 3 rigid crown pieces (crest_l, crest_c, crest_r) in raw coords -> shifted onto 410x380
    if skin == "crimson":
        horn_col = (yy < (164 + oy)) & ((rgb[..., 0] < 144) | (rgb[..., 2] > rgb[..., 1] * 0.93) | (rgb[..., 1] < 55))
        p_l = poly_mask((CANVAS_H, CANVAS_W), shift_pts([(74, 105), (86, 68), (108, 68), (139, 98), (140, 146), (118, 153), (86, 152), (74, 130)])) > 0
        p_c = poly_mask((CANVAS_H, CANVAS_W), shift_pts([(152, 55), (188, 12), (212, 12), (235, 54), (233, 116), (192, 137), (157, 116)])) > 0
        p_r = poly_mask((CANVAS_H, CANVAS_W), shift_pts([(241, 126), (262, 79), (298, 72), (318, 80), (320, 126), (292, 161), (244, 152)])) > 0
        m_l = fill_external_holes(has_pix & p_l & horn_col)
        m_c = fill_external_holes(has_pix & p_c & horn_col) & ~m_l
        m_r = fill_external_holes(has_pix & p_r & horn_col) & ~m_c
        dome_ctrl = shift_pts([(46, 206), (62, 168), (77, 145), (112, 116), (146, 101), (192, 96), (238, 101), (274, 120), (307, 152), (324, 184), (340, 224)])
        dome_x_range = (56 + ox, 336 + ox)
        rim_col = np.array([154.0, 60.0, 52.0], dtype=np.float32)
    elif skin == "amber":
        rock_col = (yy < (166 + oy)) & ((rgb[..., 0] - rgb[..., 2]) < 70)
        p_l = poly_mask((CANVAS_H, CANVAS_W), shift_pts([(100, 90), (116, 50), (152, 38), (179, 48), (177, 78), (150, 96), (104, 92)])) > 0
        p_c = poly_mask((CANVAS_H, CANVAS_W), shift_pts([(172, 62), (202, 12), (258, 14), (307, 52), (305, 98), (272, 113), (212, 113), (175, 82)])) > 0
        p_r = poly_mask((CANVAS_H, CANVAS_W), shift_pts([(232, 144), (258, 124), (274, 126), (286, 96), (312, 93), (326, 118), (314, 150), (274, 162), (236, 154)])) > 0
        m_r = fill_external_holes(has_pix & p_r & rock_col)
        m_c = fill_external_holes(has_pix & p_c & rock_col) & ~m_r
        m_l = fill_external_holes(has_pix & p_l & rock_col) & ~m_c & ~m_r
        dome_ctrl = shift_pts([(52, 174), (72, 134), (102, 92), (140, 71), (174, 64), (218, 67), (260, 82), (296, 110), (318, 144), (336, 180)])
        dome_x_range = (94 + ox, 338 + ox)
        rim_col = np.array([162.0, 100.0, 26.0], dtype=np.float32)
    else:  # azure — spherical circle-union masks so every bubble cluster has round curved edges (zero straight knife cuts!)
        dome_ctrl = shift_pts([(56, 146), (72, 112), (98, 78), (134, 55), (160, 48), (192, 46), (230, 54), (268, 74), (298, 105), (316, 138), (330, 168)])
        dome_y_all = catmull_rom_y(dome_ctrl, np.arange(CANVAS_W, dtype=np.float64))
        above_dome = yy < (dome_y_all[None, :] - 1.5)
        bub_col = (yy < (144 + oy)) & ((rgb[..., 1] > 157) | (rgb[..., 0] > 82) | above_dome)
        c_r = circle_union([(257, 119, 17.5), (304, 114, 21.0)])
        c_c = circle_union([(264, 41, 31.0), (246, 80, 24.5), (287, 81, 24.0), (303, 31, 10.5), (307, 57, 12.0)])
        c_l = circle_union([(172, 44, 16.0), (207, 50, 31.5), (203, 99, 13.5)])
        m_r = fill_external_holes(has_pix & c_r & bub_col)
        m_c = fill_external_holes(has_pix & c_c & bub_col) & ~m_r
        m_l = fill_external_holes(has_pix & c_l & bub_col) & ~m_c & ~m_r
        dome_x_range = (150 + ox, 334 + ox)
        rim_col = np.array([46.0, 118.0, 122.0], dtype=np.float32)

    # Extract each rigid crown piece with rounded underlap extension where tucked behind higher-Z neighbors
    # Draw order is crest_l -> crest_c -> crest_r
    crest_parts: dict[str, np.ndarray] = {}
    crest_masks: dict[str, np.ndarray] = {}
    for slot_n, mask_own, mask_over in [
        ("crest_l", m_l, m_c | m_r),
        ("crest_c", m_c, m_r),
        ("crest_r", m_r, np.zeros_like(m_r)),
    ]:
        under_rad = 15 if skin == "azure" else 11
        underlap = (cv2.dilate(mask_own.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (under_rad, under_rad))) > 0) & mask_over
        full_m = mask_own | underlap
        soft_m = cv2.GaussianBlur(full_m.astype(np.float32), (3, 3), 0.65)
        part_a = np.minimum(alpha, soft_m * 255.0)
        part_rgb = rgb.copy()
        if np.any(underlap):
            part_rgb = harmonic_fill(part_rgb, underlap, full_m, iters=70)
        part = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
        part[..., :3] = np.clip(np.round(part_rgb), 0, 255).astype(np.uint8)
        part[..., 3] = np.clip(np.round(part_a), 0, 255).astype(np.uint8)
        crest_parts[slot_n] = part
        crest_masks[slot_n] = full_m

    # 2. Extract 3-layer cyclops eye (socket_mask, iris, eyelid, clip_poly)
    socket_mask, is_iris, iris_rgba, eyelid_rgba, clip_poly = extract_cyclops_eye(placed, skin, ox, oy)
    eye_clips[skin] = clip_poly
    save_cropped(f"{skin}_iris", iris_rgba, meta, pad=2)
    save_cropped(f"{skin}_eyelid", eyelid_rgba, meta, pad=2)

    # 3. Reconstruct zero-ghosting bare gel dome (`{skin}_body.png`)
    dome_y = catmull_rom_y(dome_ctrl, np.arange(CANVAS_W, dtype=np.float64))
    all_crest = m_l | m_c | m_r
    # Dilate crown footprint generously so 100% of dark contact shadows / cyan bubble halos / stone fringes vanish!
    dil_sz = 17 if skin == "crimson" else 13
    crest_hole = cv2.dilate(all_crest.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (dil_sz, dil_sz))) > 0
    if skin == "crimson":
        # Remove dark horn-base pocket shadows & warm-dark transition around all 3 ruby horns
        near_horn = cv2.dilate(all_crest.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (23, 23))) > 0
        pocket = near_horn & has_pix & ((rgb[..., 0] < 160) | (rgb[..., 1] < 64))
        crest_hole = crest_hole | pocket

    # Build smooth bare dome alpha & strip any disconnected floating specks outside dome!
    x_in_dome = (xx >= dome_x_range[0]) & (xx <= dome_x_range[1]) & (yy < 236)
    dome_edge_a = np.clip((yy.astype(np.float32) - dome_y[None, :] + 1.0) / 1.8, 0.0, 1.0) * 255.0
    body_alpha = alpha.copy()
    modify_top = x_in_dome & (crest_hole | (yy < dome_y[None, :] + 4.0))
    body_alpha = np.where(modify_top, dome_edge_a, body_alpha)
    # Keep strictly the main connected body component so zero stray specks can survive!
    num_b, lbl_b, stats_b, _ = cv2.connectedComponentsWithStats((body_alpha > 4.0).astype(np.uint8))
    if num_b > 1:
        main_b = 1 + int(np.argmax(stats_b[1:, cv2.CC_STAT_AREA]))
        keep_b = cv2.dilate((lbl_b == main_b).astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        body_alpha = np.where(keep_b, body_alpha, 0.0)
    body_valid = body_alpha > 16.0

    # Paint +24px downward subsurface dark-ruby faceted root shafts UNDER crimson_crest_{l,c,r}
    if skin == "crimson":
        body_keep = (
            cv2.erode((body_alpha > 200).astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11))) > 0
        ) & ~(
            cv2.dilate(socket_mask.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))) > 0
        )
        for slot_n in ("crest_l", "crest_c", "crest_r"):
            crest_parts[slot_n] = extend_crimson_crest_root(
                crest_parts[slot_n], crest_masks[slot_n], slot_n, body_keep | crest_masks[slot_n]
            )
    for slot_n in ("crest_l", "crest_c", "crest_r"):
        save_cropped(f"{skin}_{slot_n}", crest_parts[slot_n], meta, pad=2)

    # Harmonic gouache fill across crest_hole inside body_valid (excluding eye socket so dark eye stays untouched!)
    fill_zone = crest_hole & body_valid & ~socket_mask
    body_rgb = harmonic_fill(rgb, fill_zone, body_valid & ~socket_mask, iters=220)

    # Add subtle authentic STS2 gouache rim shading along newly reconstructed top dome edge (within 3.5px of dome_y)
    dist_top = yy.astype(np.float32) - dome_y[None, :]
    rim_zone = fill_zone & (dist_top >= -1.0) & (dist_top <= 3.5)
    rim_w = (np.clip(1.0 - dist_top / 3.5, 0.0, 1.0) * 0.28)[..., None]
    body_rgb = np.where(rim_zone[..., None], body_rgb * (1.0 - rim_w) + rim_col[None, None, :] * rim_w, body_rgb)

    # Clean yellow iris out of eye socket interior in body_rgb
    body_rgb = clean_socket_interior(body_rgb, socket_mask, is_iris, skin)

    body_rgba = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    body_rgba[..., :3] = np.clip(np.round(body_rgb), 0, 255).astype(np.uint8)
    body_rgba[..., 3] = np.clip(np.round(body_alpha), 0, 255).astype(np.uint8)
    save_cropped(f"{skin}_body", body_rgba, meta, pad=2)

    # 4. Build translucent inner gel core (`{skin}_inner_blob.png`) — strictly clear of eye socket (+16px margin)!
    erode_body = cv2.erode((body_alpha > 180).astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (45, 39)))
    eye_keepout = cv2.dilate(socket_mask.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))) > 0
    blob_seed = (erode_body > 0) & ~eye_keepout & (yy < 334)
    blob_soft = cv2.GaussianBlur(blob_seed.astype(np.float32), (45, 45), 14.0)
    blob_soft *= (~eye_keepout).astype(np.float32)
    blob_soft = cv2.GaussianBlur(blob_soft, (17, 17), 5.0)
    if blob_soft.max() > 1e-4:
        blob_soft = blob_soft / blob_soft.max()
    inner_rgba = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    core_tint = np.clip(body_rgb * np.array([0.88, 0.82, 0.85]), 0, 255)
    inner_rgba[..., :3] = np.round(core_tint).astype(np.uint8)
    inner_rgba[..., 3] = np.round(blob_soft * 46.0).astype(np.uint8)
    save_cropped(f"{skin}_inner_blob", inner_rgba, meta, pad=2)

    # 5. Build soft additive aura (`{skin}_aura.png`) matching bare body silhouette
    aura_soft = cv2.GaussianBlur((body_alpha / 255.0).astype(np.float32), (31, 31), 9.5)
    aura_rgba = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    aura_col = np.clip(body_rgb * 1.18 + 22.0, 0, 255)
    aura_rgba[..., :3] = np.round(aura_col).astype(np.uint8)
    aura_rgba[..., 3] = np.clip(np.round(aura_soft * 220.0), 0, 255).astype(np.uint8)
    save_cropped(f"{skin}_aura", aura_rgba, meta, pad=2)

    # 6. Build hand-painted matte-gouache projectile (`{skin}_spit_glob.png`) around (138, 224)
    spit_glob_rgba = make_spit_glob(skin, meta)

    return {
        "placed": placed,
        "body": body_rgba,
        "inner_blob": inner_rgba,
        "iris": iris_rgba,
        "eyelid": eyelid_rgba,
        "crest_l": crest_parts["crest_l"],
        "crest_c": crest_parts["crest_c"],
        "crest_r": crest_parts["crest_r"],
        "spit_glob": spit_glob_rgba,
        "clip_poly": np.asarray(clip_poly, dtype=np.float32),
    }


def alpha_over(dst: np.ndarray, src: np.ndarray, dx: int = 0, dy: int = 0, clip_mask: np.ndarray | None = None) -> np.ndarray:
    out = dst.copy()
    h, w = src.shape[:2]
    shifted = np.zeros_like(src)
    x0_s, x1_s = max(0, -dx), min(w, w - dx)
    y0_s, y1_s = max(0, -dy), min(h, h - dy)
    if x1_s > x0_s and y1_s > y0_s:
        shifted[y0_s + dy : y1_s + dy, x0_s + dx : x1_s + dx] = src[y0_s:y1_s, x0_s:x1_s]
    a = shifted[..., 3:4].astype(np.float32) / 255.0
    if clip_mask is not None:
        a = a * clip_mask[..., None]
    out_rgb = out[..., :3].astype(np.float32) * (1.0 - a) + shifted[..., :3].astype(np.float32) * a
    out[..., :3] = np.clip(np.round(out_rgb), 0, 255).astype(np.uint8)
    return out


def render_parts_audit(shadow: np.ndarray, results: dict[str, dict[str, np.ndarray]]) -> None:
    PREV.mkdir(exist_ok=True)
    cols = 6
    rows = len(SKINS)
    tile_w, tile_h = CANVAS_W, CANVAS_H
    sheet = np.full((rows * tile_h, cols * tile_w, 3), (32, 36, 44), dtype=np.uint8)

    headers = [
        "1. Approved v4 Cutout",
        "2. Bare Body + Spit Glob",
        "3. Exploded Parts + Clip",
        "4. Reassembled (t=0)",
        "5. Gaze + Squint + Spit",
        "6. Shear Blink + +22px Shoot",
    ]
    for r_idx, skin in enumerate(SKINS):
        d = results[skin]
        clip_m = poly_mask((CANVAS_H, CANVAS_W), d["clip_poly"].tolist()).astype(np.float32) / 255.0

        def bg_tile() -> np.ndarray:
            t = np.full((tile_h, tile_w, 3), (34, 38, 46), dtype=np.uint8)
            cv2.line(t, (0, ORIGIN[1]), (tile_w, ORIGIN[1]), (70, 76, 90), 1)
            return t

        # Col 0: Original v4 cutout + shadow
        t0 = alpha_over(bg_tile(), shadow)
        t0 = alpha_over(t0, d["placed"])

        # Col 1: Bare gel body + hand-painted spit_glob preview upper-left
        t1 = alpha_over(bg_tile(), d["body"])
        t1 = alpha_over(t1, d["spit_glob"], dx=-72, dy=-128)

        # Col 2: Exploded parts + eye_clip cyan contour + spit_glob
        t2 = bg_tile()
        t2 = alpha_over(t2, d["inner_blob"], dx=-65, dy=20)
        t2 = alpha_over(t2, d["iris"], dx=-10, dy=0)
        t2 = alpha_over(t2, d["eyelid"], dx=0, dy=-30)
        t2 = alpha_over(t2, d["crest_l"], dx=-18, dy=-12)
        t2 = alpha_over(t2, d["crest_c"], dx=0, dy=-16)
        t2 = alpha_over(t2, d["crest_r"], dx=18, dy=-8)
        t2 = alpha_over(t2, d["spit_glob"], dx=-68, dy=95)
        cv2.polylines(t2, [np.round(d["clip_poly"]).astype(np.int32)], True, (80, 240, 255), 1, lineType=cv2.LINE_AA)

        # Col 3: Reassembled at t=0 (hardware eye_clip active, spit_glob alpha=0!)
        t3 = alpha_over(bg_tile(), shadow)
        t3 = alpha_over(t3, d["body"])
        t3 = alpha_over(t3, d["inner_blob"])
        t3 = alpha_over(t3, d["iris"], clip_mask=clip_m)
        t3 = alpha_over(t3, d["eyelid"], clip_mask=clip_m)
        t3 = alpha_over(t3, d["crest_l"])
        t3 = alpha_over(t3, d["crest_c"])
        t3 = alpha_over(t3, d["crest_r"])

        # Col 4: Fierce gaze + angry half-squint + launched spit_glob
        t4 = alpha_over(bg_tile(), shadow)
        t4 = alpha_over(t4, d["body"])
        t4 = alpha_over(t4, d["inner_blob"], dx=-8, dy=4)
        t4 = alpha_over(t4, d["iris"], dx=-10, dy=8, clip_mask=clip_m)
        t4 = alpha_over(t4, d["eyelid"], dx=-24, dy=22, clip_mask=clip_m)
        t4 = alpha_over(t4, d["crest_l"], dx=-4, dy=-3)
        t4 = alpha_over(t4, d["crest_c"], dx=0, dy=-5)
        t4 = alpha_over(t4, d["crest_r"], dx=4, dy=-2)
        t4 = alpha_over(t4, d["spit_glob"], dx=-78, dy=-58)

        # Col 5: Heavy horizontal eyelid shear (dx=-30, dy=+64) + +22px axial harpoon shoot (0 gap!)
        t5 = alpha_over(bg_tile(), shadow)
        t5 = alpha_over(t5, d["body"])
        t5 = alpha_over(t5, d["inner_blob"], dx=8, dy=-4)
        t5 = alpha_over(t5, d["iris"], clip_mask=clip_m)
        t5 = alpha_over(t5, d["eyelid"], dx=-30, dy=64, clip_mask=clip_m)
        t5 = alpha_over(t5, d["crest_l"], dx=-9, dy=-21)
        t5 = alpha_over(t5, d["crest_c"], dx=-3, dy=-24)
        t5 = alpha_over(t5, d["crest_r"], dx=11, dy=-19)

        for c_idx, tile in enumerate([t0, t1, t2, t3, t4, t5]):
            label = f"{skin.upper()} | {headers[c_idx]}"
            cv2.putText(tile, label, (10, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.46, (240, 244, 250), 1, lineType=cv2.LINE_AA)
            y0, x0 = r_idx * tile_h, c_idx * tile_w
            sheet[y0 : y0 + tile_h, x0 : x0 + tile_w] = tile

    out_path = PREV / "parts_audit.jpg"
    Image.fromarray(sheet).save(out_path, quality=92)
    print(f"Saved {out_path} ({sheet.shape[1]}x{sheet.shape[0]})")


def main() -> None:
    PARTS.mkdir(exist_ok=True)
    # Remove any legacy v3 part files so `parts/` stays 100% clean
    for old in PARTS.glob("*.png"):
        old.unlink()

    meta: dict[str, dict[str, int]] = {}
    eye_clips: dict[str, list[list[float]]] = {}
    shadow = make_shadow(meta)
    results = {skin: extract_strain(skin, meta, eye_clips) for skin in SKINS}

    (PARTS / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8", newline="\n")
    (PARTS / "eye_clips.json").write_text(json.dumps(eye_clips, indent=2), encoding="utf-8", newline="\n")
    render_parts_audit(shadow, results)
    print("Extracted parts:", len(meta), sorted(meta.keys()))


if __name__ == "__main__":
    main()
