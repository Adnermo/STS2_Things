"""Extract & paint clean 340x340 single-body assets for ThingsSoulRoe (`soul_roe_1`, `soul_roe_2`, `soul_roe_3`).

- Pure single-roe creature (ZERO orbiting micro-roes!).
- De-spills royal-blue [2, 100, 249] / cyan edge fringes from AI cutout parts using exact unmixing + interior RGB propagation.
- Normalizes main roe onto 340x340 canvas with ORIGIN=(170, 210), MAIN_CENTER=(170, 140), main outer diameter=152px.
- Paints single-body kinetic & spectral VFX layers:
  * `body_trail`: ~236x124px lineless Mega Crit golden-amber slipstream comet wake behind the streaking roe (`SLOT_DEFAULT_ALPHA = 0.0`)
  * `impact_flare`: crisp luminous golden shock-crescent & splash droplets at the front-left impact edge (`SLOT_DEFAULT_ALPHA = 0.0`)
  * `core_beckon_glow` + `beckon_halo_ring`: warm sun-gold / molten amber Beckon lantern illumination (`SLOT_DEFAULT_ALPHA = 0.0`)
  * `core_intangible_glow` + `intangible_shimmer`: pearlescent SoulFysh aqua-white-gold Intangible spectral illumination (`SLOT_DEFAULT_ALPHA = 0.0`)
"""
from __future__ import annotations

import json
import math
import shutil
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
PARTS_ROOT = HERE / "parts"
PREV_DIR = HERE / "prev"

REPO_ROOT = HERE.parents[1]
CUTOUT_DIR = REPO_ROOT / "source_assets" / "monsters" / "ai_cutout_parts" / "soul_roe_variants"
BLUE_SHEET = REPO_ROOT / "source_assets" / "monsters" / "generated_parts" / "soul_roe_variants" / "parts_sheet_v4_blue.png"
COMPONENTS_JSON = CUTOUT_DIR / "components.json"

CANVAS_W, CANVAS_H = 340, 340
ORIGIN = (170, 210)
MAIN_CENTER = (170, 140)
IMPACT_ANCHOR = (96, 140)  # Leading left membrane contact point (170 - 74, 140)
SCALE_MAIN = 152.0 / 266.0  # Normalizes ~266px membrane_ring to 152px (radius=76px, bottom=216, top=64)

VARIANT_SPECS = {
    1: dict(name="soul_roe_1", label="v1 (Figure-8 Drift)"),
    2: dict(name="soul_roe_2", label="v2 (Counter-Sway Float)"),
    3: dict(name="soul_roe_3", label="v3 (Cradle Buoyancy)"),
}

BG_BLUE = np.array([2.0, 100.0, 249.0], dtype=np.float32)


def dilate_rgb_into_transparent(rgba: np.ndarray, passes: int = 8) -> np.ndarray:
    """Propagate clean edge RGB outward into alpha==0 pixels so bilinear filtering NEVER dark-halos."""
    out = rgba.astype(np.float32).copy()
    known = out[..., 3] > 2.0
    if not np.any(known):
        return rgba
    col = out[..., :3].copy()
    col[~known] = 0.0
    for _ in range(passes):
        k = cv2.dilate(known.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        s = cv2.blur(col * known[..., None].astype(np.float32), (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new = k & ~known
        if np.any(new):
            col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = k
    out[..., :3] = np.clip(np.round(col), 0, 255)
    return out.astype(np.uint8)


def save_cropped(out_dir: Path, name: str, rgba: np.ndarray, meta: dict[str, dict[str, int]], pad: int = 2) -> None:
    ys, xs = np.where(rgba[..., 3] > 1)
    if len(xs) == 0:
        raise RuntimeError(f"Empty part: {name}")
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(rgba.shape[1], int(xs.max()) + 1 + pad)
    y1 = min(rgba.shape[0], int(ys.max()) + 1 + pad)
    crop = dilate_rgb_into_transparent(rgba[y0:y1, x0:x1], passes=8)
    Image.fromarray(crop).save(out_dir / f"{name}.png", optimize=True)
    meta[name] = {"x": x0, "y": y0, "w": int(x1 - x0), "h": int(y1 - y0)}


def keep_main_component(alpha_u8: np.ndarray, min_area: int = 24) -> np.ndarray:
    num, lbl, stats, _ = cv2.connectedComponentsWithStats((alpha_u8 > 4).astype(np.uint8))
    if num <= 1:
        return alpha_u8
    keep = np.zeros_like(alpha_u8, dtype=bool)
    max_area = int(stats[1:, cv2.CC_STAT_AREA].max())
    for idx in range(1, num):
        if stats[idx, cv2.CC_STAT_AREA] >= max(min_area, int(0.08 * max_area)):
            keep |= (lbl == idx)
    keep = cv2.dilate(keep.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
    return np.where(keep, alpha_u8, 0).astype(np.uint8)


def clean_solid_golden_part(cut_rgba: np.ndarray, erode_px: int = 5) -> np.ndarray:
    """Strip royal-blue/cyan edge spill from opaque golden parts (cytoplasm, nucleus, membrane_ring)."""
    rgb = cut_rgba[..., :3].astype(np.float32)
    a = keep_main_component(cut_rgba[..., 3])

    pure = (a >= 245) & ((rgb[..., 0] - rgb[..., 2]) > 18.0)
    if erode_px > 0:
        ker = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * erode_px + 1, 2 * erode_px + 1))
        eroded = cv2.erode(pure.astype(np.uint8), ker) > 0
        if np.sum(eroded) > 64:
            pure = eroded

    _, labels = cv2.distanceTransformWithLabels((~pure).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)
    flat_rgb = rgb[pure]
    nearest_rgb = flat_rgb[np.clip(labels - 1, 0, len(flat_rgb) - 1)]
    clean_rgb = np.where(pure[..., None], rgb, nearest_rgb)
    fringe = (a > 0) & ~pure
    blur_rgb = cv2.GaussianBlur(clean_rgb, (3, 3), 0.8)
    clean_rgb = np.where(fringe[..., None], blur_rgb, clean_rgb)

    a_f = np.clip((a.astype(np.float32) - 14.0) / 241.0, 0.0, 1.0) * 255.0
    out = np.zeros_like(cut_rgba)
    out[..., :3] = np.clip(np.round(clean_rgb), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(a_f), 0, 255).astype(np.uint8)
    return dilate_rgb_into_transparent(out, passes=8)


def unmix_rear_glow(blue_crop: np.ndarray) -> np.ndarray:
    """Unmix warm sun-gold rear_glow directly from parts_sheet_v4_blue.png (zero grey-blue ring!)."""
    rgb = blue_crop.astype(np.float32)
    a_raw = np.clip((rgb[..., 0] - BG_BLUE[0] - 3.0) / 220.0, 0.0, 1.0)
    a_smooth = cv2.GaussianBlur(a_raw, (7, 7), 1.8)
    safe_a = np.maximum(a_raw[..., None], 0.18)
    fg = (rgb - BG_BLUE[None, None, :] * (1.0 - a_raw[..., None])) / safe_a
    warm_floor = np.array([228.0, 198.0, 74.0], dtype=np.float32)
    w_core = np.clip((a_raw - 0.15) / 0.55, 0.0, 1.0)[..., None]
    fg_clean = warm_floor[None, None, :] * (1.0 - w_core) + np.clip(fg, 0.0, 255.0) * w_core
    fg_clean[..., 2] = np.minimum(fg_clean[..., 2], fg_clean[..., 1] * 0.52)

    out = np.zeros((*blue_crop.shape[:2], 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(fg_clean), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round((a_smooth ** 1.12) * 215.0), 0, 255).astype(np.uint8)
    return dilate_rgb_into_transparent(out, passes=8)


def unmix_veins_highlight(blue_crop: np.ndarray) -> np.ndarray:
    """Recover crisp, luminous cream-gold membrane veins & star highlights from parts_sheet_v4_blue.png."""
    rgb = blue_crop.astype(np.float32)
    a_raw = np.clip((rgb[..., 0] - BG_BLUE[0] - 6.0) / 236.0, 0.0, 1.0)
    a_boost = np.clip(a_raw * 1.24, 0.0, 1.0)
    lum = np.clip(a_raw, 0.0, 1.0)[..., None]
    col_soft = np.array([242.0, 222.0, 136.0], dtype=np.float32)
    col_peak = np.array([255.0, 251.0, 224.0], dtype=np.float32)
    fg = col_soft[None, None, :] * (1.0 - lum) + col_peak[None, None, :] * lum

    out = np.zeros((*blue_crop.shape[:2], 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(fg), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(a_boost * 245.0), 0, 255).astype(np.uint8)
    return dilate_rgb_into_transparent(out, passes=8)


def place_scaled_centered(part_rgba: np.ndarray, cx: float, cy: float, scale: float) -> np.ndarray:
    """Resample premultiplied-safe RGBA and center at (cx, cy) on 340x340 canvas."""
    h, w = part_rgba.shape[:2]
    nw = max(2, int(round(w * scale)))
    nh = max(2, int(round(h * scale)))

    f = part_rgba.astype(np.float32)
    a = f[..., 3:4] / 255.0
    prem = np.concatenate([f[..., :3] * a, f[..., 3:4]], axis=-1)
    resized_prem = cv2.resize(prem, (nw, nh), interpolation=cv2.INTER_AREA if scale < 1.0 else cv2.INTER_CUBIC)
    ra = np.clip(resized_prem[..., 3:4] / 255.0, 0.0, 1.0)
    rgb = np.where(ra > 1e-4, resized_prem[..., :3] / np.maximum(ra, 1e-4), 0.0)
    resized = np.zeros((nh, nw, 4), dtype=np.uint8)
    resized[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    resized[..., 3] = np.clip(np.round(resized_prem[..., 3]), 0, 255).astype(np.uint8)
    resized = dilate_rgb_into_transparent(resized, passes=6)

    ys, xs = np.where(resized[..., 3] > 16)
    if len(xs):
        pcx = 0.5 * (float(xs.min()) + float(xs.max()))
        pcy = 0.5 * (float(ys.min()) + float(ys.max()))
    else:
        pcx, pcy = 0.5 * nw, 0.5 * nh

    ox = int(round(cx - pcx))
    oy = int(round(cy - pcy))
    canvas = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)

    sx0 = max(0, -ox)
    sy0 = max(0, -oy)
    sx1 = min(nw, CANVAS_W - ox)
    sy1 = min(nh, CANVAS_H - oy)
    if sx1 > sx0 and sy1 > sy0:
        canvas[oy + sy0 : oy + sy1, ox + sx0 : ox + sx1] = resized[sy0:sy1, sx0:sx1]
    return canvas


def make_shadow() -> np.ndarray:
    """Soft underwater sea-floor shadow beneath floating single roe around ORIGIN=(170, 210)."""
    canvas = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = float(ORIGIN[0]), float(ORIGIN[1] + 8.0)
    rx, ry = 68.0, 12.5
    r = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2)
    rng = np.random.default_rng(107)
    grain = cv2.GaussianBlur(rng.normal(0.0, 1.0, (CANVAS_H, CANVAS_W)).astype(np.float32), (5, 5), 1.1)
    alpha = np.clip((1.0 - (r + 0.03 * grain)) / 0.42, 0.0, 1.0) ** 1.35 * 0.44
    canvas[..., 0] = 16
    canvas[..., 1] = 20
    canvas[..., 2] = 30
    canvas[..., 3] = np.round(alpha * 255.0).astype(np.uint8)
    return canvas


def save_body_trail(out_dir: Path, variant: int, meta: dict[str, dict[str, int]]) -> None:
    """Paint a rich ~236x124 lineless Mega Crit golden-amber slipstream comet wake behind the 152px roe.
    Anchored at `MAIN_CENTER = (170, 140)` and streaming rightward (+X) up to +222px behind the -X streaking roe!
    """
    W_BUF, H_BUF = 264, 134
    hx, hy = 34.0, 67.0  # Maps to MAIN_CENTER=(170, 140)
    yy, xx = np.mgrid[0:H_BUF, 0:W_BUF].astype(np.float32)
    dx = xx - hx  # -34 .. +230 (roe trailing right rim is at dx = +76!)
    dy = yy - hy

    # Subtle per-variant organic wake undulation
    ph = (0.0, 0.9, -0.8)[variant - 1]
    wave = 3.2 * np.sin(np.clip((dx - 40.0) / 48.0, 0.0, 4.2) + ph) * np.clip((dx - 35.0) / 140.0, 0.0, 1.0)
    dy_w = dy - wave

    # Outer slipstream envelope: hugs inside roe's 76px radius (half_h=58px at dx=0..50) and tapers crisply to dx=+216px!
    u_tail = np.clip((dx + 22.0) / 238.0, 0.0, 1.0)
    half_h = np.where(
        dx < 0.0,
        56.0 * np.sqrt(np.clip(1.0 - (dx / 26.0) ** 2, 0.0, 1.0)),
        56.0 * ((1.0 - np.clip(dx / 218.0, 0.0, 1.0)) ** 0.72),
    )
    r_outer = np.abs(dy_w) / np.maximum(half_h, 0.8)
    outer_a = np.clip((1.0 - r_outer) / 0.28, 0.0, 1.0) * ((1.0 - u_tail) ** 0.78) * (dx > -26.0) * (dx < 218.0)

    # Sculpted twin slipstream ribbons (upper & lower membrane shear wakes) + blazing central yolk jet!
    rib_off = 0.58 * half_h
    rib_u = np.clip((1.0 - np.abs(dy_w + rib_off) / np.maximum(0.22 * half_h, 1.2)) / 0.45, 0.0, 1.0)
    rib_l = np.clip((1.0 - np.abs(dy_w - rib_off) / np.maximum(0.22 * half_h, 1.2)) / 0.45, 0.0, 1.0)
    ribbons = np.maximum(rib_u, rib_l) * ((1.0 - np.clip((dx - 30.0) / 172.0, 0.0, 1.0)) ** 0.9) * (dx > 10.0)

    core_h = 0.44 * half_h
    r_core = np.abs(dy_w) / np.maximum(core_h, 0.7)
    core_a = np.clip((1.0 - r_core) / 0.34, 0.0, 1.0) * ((1.0 - np.clip((dx + 12.0) / 176.0, 0.0, 1.0)) ** 0.95) * (dx > -18.0)

    # 3 crisp lineless golden gouache trailing droplets in the slipstream wake
    d1 = np.clip((1.0 - np.hypot((dx - 166.0) / 13.5, (dy_w + 11.0) / 5.6)) / 0.22, 0.0, 1.0) * 0.90
    d2 = np.clip((1.0 - np.hypot((dx - 192.0) / 10.5, (dy_w - 8.5) / 4.4)) / 0.24, 0.0, 1.0) * 0.84
    d3 = np.clip((1.0 - np.hypot((dx - 214.0) / 7.5, (dy_w + 2.5) / 3.2)) / 0.26, 0.0, 1.0) * 0.74
    drops = np.clip(d1 + d2 + d3, 0.0, 1.0)

    tot_a = np.clip(outer_a * 0.72 + ribbons * 0.46 + core_a * 0.56 + drops, 0.0, 1.0)

    col_deep_amber = np.array([228.0, 142.0, 22.0], dtype=np.float32)
    col_warm_gold = np.array([252.0, 206.0, 68.0], dtype=np.float32)
    col_ivory_core = np.array([255.0, 248.0, 186.0], dtype=np.float32)

    m_mid = np.clip(ribbons + 0.65 * outer_a, 0.0, 1.0)[..., None]
    m_hot = np.clip(core_a + 0.75 * drops, 0.0, 1.0)[..., None]
    rgb = col_deep_amber[None, None, :] * (1.0 - 0.65 * m_mid) + col_warm_gold[None, None, :] * (0.65 * m_mid)
    rgb = rgb * (1.0 - 0.82 * m_hot) + col_ivory_core[None, None, :] * (0.82 * m_hot)

    buf = np.zeros((H_BUF, W_BUF, 4), dtype=np.uint8)
    buf[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    buf[..., 3] = np.clip(np.round(tot_a * 250.0), 0, 255).astype(np.uint8)
    buf = dilate_rgb_into_transparent(buf, passes=8)

    wx0 = int(round(MAIN_CENTER[0] - hx))
    wy0 = int(round(MAIN_CENTER[1] - hy))
    Image.fromarray(buf).save(out_dir / "body_trail.png", optimize=True)
    meta["body_trail"] = {"x": wx0, "y": wy0, "w": W_BUF, "h": H_BUF}


def save_impact_flare(out_dir: Path, meta: dict[str, dict[str, int]]) -> None:
    """Paint a crisp lineless golden shock-crescent & radial splash anchored at `IMPACT_ANCHOR = (96, 140)`."""
    W_BUF, H_BUF = 132, 168
    hx, hy = 72.0, 84.0  # Maps to IMPACT_ANCHOR=(96, 140)
    yy, xx = np.mgrid[0:H_BUF, 0:W_BUF].astype(np.float32)
    dx = xx - hx
    dy = yy - hy

    # Curved front shock-wave crescent bowing outward to the left (-X)
    x_arc = -10.0 + 0.0115 * (dy ** 2)
    dist_arc = np.abs(dx - x_arc)
    span_y = np.clip(1.0 - (np.abs(dy) / 68.0) ** 1.6, 0.0, 1.0)
    cres_core = np.clip(1.0 - dist_arc / 6.5, 0.0, 1.0) ** 1.25 * span_y
    cres_glow = np.exp(-((dist_arc / 15.0) ** 2)) * span_y * 0.72

    # 5 crisp radial impact spikes / splash droplets fanning left & up/down
    spikes = np.zeros_like(dx)
    for ang_deg, length, width in [(-180.0, 48.0, 5.5), (-142.0, 54.0, 5.0), (142.0, 54.0, 5.0), (-108.0, 44.0, 4.2), (108.0, 44.0, 4.2)]:
        rad = math.radians(ang_deg)
        ca, sa = math.cos(rad), math.sin(rad)
        s_along = dx * ca + dy * sa
        s_perp = -dx * sa + dy * ca
        u_s = np.clip(s_along / length, 0.0, 1.0)
        w_loc = width * (1.0 - 0.78 * u_s) * np.clip(s_along / 6.0, 0.0, 1.0)
        sp_a = np.clip((1.0 - np.abs(s_perp) / np.maximum(w_loc, 0.5)) / 0.35, 0.0, 1.0) * (1.0 - u_s ** 1.4) * (s_along > 0.0)
        spikes = np.maximum(spikes, sp_a)

    center_flash = np.exp(-((np.hypot(dx + 6.0, dy * 0.72) / 26.0) ** 2)) * 0.78
    tot_a = np.clip(cres_core + cres_glow + 0.90 * spikes + center_flash, 0.0, 1.0)

    col_amber = np.array([248.0, 178.0, 38.0], dtype=np.float32)
    col_white_gold = np.array([255.0, 251.0, 206.0], dtype=np.float32)
    hot = np.clip(cres_core + 0.75 * spikes + 0.55 * center_flash, 0.0, 1.0)[..., None]
    rgb = col_amber[None, None, :] * (1.0 - hot) + col_white_gold[None, None, :] * hot

    buf = np.zeros((H_BUF, W_BUF, 4), dtype=np.uint8)
    buf[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    buf[..., 3] = np.clip(np.round(tot_a * 248.0), 0, 255).astype(np.uint8)
    buf = dilate_rgb_into_transparent(buf, passes=8)

    wx0 = int(round(IMPACT_ANCHOR[0] - hx))
    wy0 = int(round(IMPACT_ANCHOR[1] - hy))
    Image.fromarray(buf).save(out_dir / "impact_flare.png", optimize=True)
    meta["impact_flare"] = {"x": wx0, "y": wy0, "w": W_BUF, "h": H_BUF}


def make_core_beckon_glow(cyto_canvas: np.ndarray, nuc_canvas: np.ndarray) -> np.ndarray:
    """Warm sun-gold / rich amber inner lantern illumination for Beckon (`cast`)."""
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = float(MAIN_CENTER[0]), float(MAIN_CENTER[1])
    r = np.hypot(xx - cx, yy - cy)

    cyto_a = cv2.GaussianBlur(cyto_canvas[..., 3].astype(np.float32) / 255.0, (25, 25), 7.5)
    nuc_a = cv2.GaussianBlur(nuc_canvas[..., 3].astype(np.float32) / 255.0, (19, 19), 5.5)
    radial = np.exp(-((r / 68.0) ** 2))

    alpha = np.clip(0.58 * cyto_a + 0.74 * nuc_a + 0.34 * radial, 0.0, 1.0)
    alpha *= np.clip((94.0 - r) / 26.0, 0.0, 1.0)

    col_outer = np.array([248.0, 162.0, 26.0], dtype=np.float32)
    col_inner = np.array([255.0, 242.0, 146.0], dtype=np.float32)
    t_in = np.clip(nuc_a * 1.15, 0.0, 1.0)[..., None]
    rgb = col_outer[None, None, :] * (1.0 - t_in) + col_inner[None, None, :] * t_in

    out = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(alpha * 250.0), 0, 255).astype(np.uint8)
    return out


def make_beckon_halo_ring() -> np.ndarray:
    """Luminous warm sun-gold resonant halo ring (`beckon_halo_ring`) bound to 4-bone zero-scale halo cage."""
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = float(MAIN_CENTER[0]), float(MAIN_CENTER[1])
    dx, dy = xx - cx, yy - cy
    r = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)

    r_ring = 84.0 + 1.4 * np.sin(3.0 * ang + 0.6) + 0.9 * np.cos(5.0 * ang - 0.4)
    dist = np.abs(r - r_ring)
    ring_core = np.clip((1.0 - dist / 4.8), 0.0, 1.0) ** 1.3
    ring_soft = np.exp(-((dist / 11.5) ** 2)) * 0.68
    alpha = np.clip(ring_core + ring_soft, 0.0, 1.0)

    col_amber = np.array([246.0, 178.0, 42.0], dtype=np.float32)
    col_bright = np.array([255.0, 246.0, 182.0], dtype=np.float32)
    mix = ring_core[..., None]
    rgb = col_amber[None, None, :] * (1.0 - mix) + col_bright[None, None, :] * mix

    out = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(alpha * 242.0), 0, 255).astype(np.uint8)
    return out


def make_core_intangible_glow(cyto_canvas: np.ndarray, ring_canvas: np.ndarray, nuc_canvas: np.ndarray) -> np.ndarray:
    """Pearlescent moonstone aqua-white-gold SoulFysh spectral glow for Intangible (`power_up`)."""
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = float(MAIN_CENTER[0]), float(MAIN_CENTER[1])
    r = np.hypot(xx - cx, yy - cy)

    cyto_a = cv2.GaussianBlur(cyto_canvas[..., 3].astype(np.float32) / 255.0, (29, 29), 8.5)
    ring_a = cv2.GaussianBlur(ring_canvas[..., 3].astype(np.float32) / 255.0, (15, 15), 4.2)
    nuc_a = cv2.GaussianBlur(nuc_canvas[..., 3].astype(np.float32) / 255.0, (17, 17), 4.8)
    corona = np.exp(-(((r - 74.0) / 16.0) ** 2)) * 0.55

    alpha = np.clip(0.50 * cyto_a + 0.68 * ring_a + 0.76 * nuc_a + corona, 0.0, 1.0)
    alpha *= np.clip((102.0 - r) / 24.0, 0.0, 1.0)

    col_aqua = np.array([122.0, 244.0, 238.0], dtype=np.float32)
    col_pearl = np.array([240.0, 255.0, 248.0], dtype=np.float32)
    col_gold = np.array([255.0, 248.0, 198.0], dtype=np.float32)

    w_mid = np.clip(ring_a + 0.6 * cyto_a, 0.0, 1.0)[..., None]
    w_core = np.clip(nuc_a * 1.15, 0.0, 1.0)[..., None]
    rgb = col_aqua[None, None, :] * (1.0 - 0.65 * w_mid) + col_pearl[None, None, :] * (0.65 * w_mid)
    rgb = rgb * (1.0 - 0.65 * w_core) + col_gold[None, None, :] * (0.65 * w_core)

    out = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(alpha * 248.0), 0, 255).astype(np.uint8)
    return out


def make_intangible_shimmer() -> np.ndarray:
    """Pearlescent aqua-white-gold spectral shimmer ring (`intangible_shimmer`) bound to `halo_n/s/w/e`."""
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = float(MAIN_CENTER[0]), float(MAIN_CENTER[1])
    dx, dy = xx - cx, yy - cy
    r = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)

    # Ethereal double-rippled SoulFysh spectral veil ring (distinct silhouette & hue from beckon_halo_ring!)
    r_inner = 80.5 + 2.2 * np.sin(4.0 * ang - 0.5)
    r_outer = 90.0 + 2.6 * np.cos(6.0 * ang + 0.8)
    band1 = np.clip(1.0 - np.abs(r - r_inner) / 4.2, 0.0, 1.0) ** 1.25
    band2 = np.clip(1.0 - np.abs(r - r_outer) / 3.4, 0.0, 1.0) ** 1.35 * (0.55 + 0.45 * np.sin(3.0 * ang + 1.1) ** 2)
    mist = np.exp(-(((r - 85.0) / 13.0) ** 2)) * 0.62
    alpha = np.clip(band1 + 0.85 * band2 + mist, 0.0, 1.0)

    col_cyan = np.array([112.0, 238.0, 236.0], dtype=np.float32)
    col_pearl_gold = np.array([244.0, 255.0, 232.0], dtype=np.float32)
    mix = np.clip(band1 + 0.65 * band2, 0.0, 1.0)[..., None]
    rgb = col_cyan[None, None, :] * (1.0 - mix) + col_pearl_gold[None, None, :] * mix

    out = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(np.round(alpha * 244.0), 0, 255).astype(np.uint8)
    return out


def extract_all() -> None:
    # Clean any stale parts/out/pkg directories so ZERO legacy `sat_*` files remain anywhere!
    for clean_dir in (PARTS_ROOT, HERE / "out", HERE / "pkg"):
        if clean_dir.exists():
            shutil.rmtree(clean_dir)
    PARTS_ROOT.mkdir(parents=True, exist_ok=True)
    PREV_DIR.mkdir(parents=True, exist_ok=True)

    comps = json.loads(COMPONENTS_JSON.read_text(encoding="utf-8"))
    comp_map = {(c["variant"], c["semantic"]): c for c in comps}
    blue_sheet = np.array(Image.open(BLUE_SHEET).convert("RGB"))
    shadow_canvas = make_shadow()
    halo_ring = make_beckon_halo_ring()
    intang_shimmer = make_intangible_shimmer()

    for v in (1, 2, 3):
        v_name = VARIANT_SPECS[v]["name"]
        out_dir = PARTS_ROOT / v_name
        out_dir.mkdir(parents=True, exist_ok=True)
        meta: dict[str, dict[str, int]] = {}

        def get_blue_crop(sem: str) -> np.ndarray:
            x0, y0, x1, y1 = comp_map[(v, sem)]["source_bbox"]
            return blue_sheet[y0:y1, x0:x1]

        def get_cutout(sem: str) -> np.ndarray:
            p = REPO_ROOT / comp_map[(v, sem)]["path"]
            return np.array(Image.open(p).convert("RGBA"))

        rear_clean = unmix_rear_glow(get_blue_crop("rear_glow"))
        cyto_clean = clean_solid_golden_part(get_cutout("cytoplasm"), erode_px=5)
        nuc_clean = clean_solid_golden_part(get_cutout("nucleus"), erode_px=4)
        ring_clean = clean_solid_golden_part(get_cutout("membrane_ring"), erode_px=2)
        veins_clean = unmix_veins_highlight(get_blue_crop("veins_highlight"))

        rear_canvas = place_scaled_centered(rear_clean, MAIN_CENTER[0], MAIN_CENTER[1], SCALE_MAIN * 1.36)
        cyto_canvas = place_scaled_centered(cyto_clean, MAIN_CENTER[0], MAIN_CENTER[1], SCALE_MAIN)
        nuc_canvas = place_scaled_centered(nuc_clean, MAIN_CENTER[0], MAIN_CENTER[1], SCALE_MAIN)
        ring_canvas = place_scaled_centered(ring_clean, MAIN_CENTER[0], MAIN_CENTER[1], SCALE_MAIN)
        veins_canvas = place_scaled_centered(veins_clean, MAIN_CENTER[0], MAIN_CENTER[1], SCALE_MAIN)

        # Authentic semi-translucent golden roe jelly (~88% outer wall, ~78% inner albumen around yolk)
        yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
        r_main = np.hypot(xx - MAIN_CENTER[0], yy - MAIN_CENTER[1]) / 74.0
        trans_curve = 0.78 + 0.12 * np.clip(r_main, 0.0, 1.0)
        cyto_canvas[..., 3] = np.clip(np.round(cyto_canvas[..., 3].astype(np.float32) * trans_curve), 0, 255).astype(np.uint8)

        # Soft 3.5px yolk edge feather so nucleus floats smoothly inside liquid albumen
        nuc_soft_a = cv2.GaussianBlur(nuc_canvas[..., 3].astype(np.float32), (5, 5), 1.3)
        nuc_canvas[..., 3] = np.minimum(nuc_canvas[..., 3], np.clip(np.round(nuc_soft_a * 1.04), 0, 255).astype(np.uint8))

        save_cropped(out_dir, "shadow", shadow_canvas, meta, pad=2)
        save_cropped(out_dir, "rear_glow", rear_canvas, meta, pad=2)
        save_body_trail(out_dir, v, meta)
        save_cropped(out_dir, "cytoplasm", cyto_canvas, meta, pad=2)
        save_cropped(out_dir, "nucleus", nuc_canvas, meta, pad=2)
        save_cropped(out_dir, "membrane_ring", ring_canvas, meta, pad=2)
        save_cropped(out_dir, "veins_highlight", veins_canvas, meta, pad=2)

        beckon_glow = make_core_beckon_glow(cyto_canvas, nuc_canvas)
        intang_glow = make_core_intangible_glow(cyto_canvas, ring_canvas, nuc_canvas)
        save_cropped(out_dir, "core_beckon_glow", beckon_glow, meta, pad=2)
        save_cropped(out_dir, "beckon_halo_ring", halo_ring, meta, pad=2)
        save_cropped(out_dir, "core_intangible_glow", intang_glow, meta, pad=2)
        save_cropped(out_dir, "intangible_shimmer", intang_shimmer, meta, pad=2)
        save_impact_flare(out_dir, meta)

        meta["_rig_info"] = {
            "variant": v,
            "name": v_name,
            "canvas": [CANVAS_W, CANVAS_H],
            "origin": list(ORIGIN),
            "main_center": list(MAIN_CENTER),
            "impact_anchor": list(IMPACT_ANCHOR),
        }
        (out_dir / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8", newline="\n")
        print(f"Extracted {v_name}: {len(meta) - 1} single-body parts")


if __name__ == "__main__":
    extract_all()
