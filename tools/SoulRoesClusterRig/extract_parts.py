"""Deterministic part extraction & procedural bioluminescent layer synthesis for ThingsSoulRoes.

Reads high-res individual roe spheres from ../../source_assets/monsters/ai_cutout_parts/soul_roes/part_000..011.png
(READ-ONLY), strips grey/dark matting fringes from semi-transparent pixels, applies 6+ pass RGB edge dilation on
transparent pixels so Linear texture filtering has zero dark halo, and synthesizes:
- brood_0..5, sat_0..5 (12 high-res cleaned roe spheres)
- brood_glow_0..5, sat_glow_0..5 (12 yolk-matched additive bioluminescent overlays)
- trail_0..5 (6 painterly Mega Crit gouache golden slipstream comet wakes)
- beckon_aura + beckon_ring (warm sun-gold / amber brood luminescence + expanding halo ring)
- intangible_aura + intangible_shimmer (blinding pearlescent aqua-white-gold SoulFysh spectral luminescence)
- shadow (soft underwater ambient contact shadow)
"""
import json
import math
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from rigdef import HUB_CANVAS, ORB_SPECS, ORIGIN

HERE = Path(__file__).resolve().parent
SRC_DIR = HERE.parents[1] / "source_assets" / "monsters" / "ai_cutout_parts" / "soul_roes"
PARTS_DIR = HERE / "parts"


def dilate_rgb(rgba: np.ndarray, passes: int = 8) -> np.ndarray:
    """Propagate known opaque/semi-opaque RGB into transparent pixels to prevent Linear filtering dark halos."""
    out = rgba.astype(np.float32).copy()
    known = out[..., 3] > 2.0
    if not known.any():
        return rgba
    col = out[..., :3].copy()
    k3 = np.ones((3, 3), np.uint8)
    for _ in range(passes):
        nxt = cv2.dilate(known.astype(np.uint8), k3) > 0
        s = cv2.blur(col * known[..., None], (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new = nxt & ~known
        col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = nxt
    out[..., :3] = col
    return np.clip(np.round(out), 0, 255).astype(np.uint8)


def clean_and_resize_orb(src_path: Path, target_diam: int) -> np.ndarray:
    """Clean grey cutout matting halo from part_000..011.png, resize in premultiplied space, and dilate RGB."""
    raw = np.array(Image.open(src_path).convert("RGBA"), dtype=np.float32)
    a_raw = raw[..., 3]

    # Keep only largest connected component above faint matting threshold
    fg = (a_raw >= 42.0).astype(np.uint8)
    num_labels, labels, stats, _ = cv2.connectedComponentsWithStats(fg, connectivity=8)
    if num_labels > 1:
        best = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        fg = (labels == best).astype(np.uint8)

    # Smooth anti-aliased edge ramp from signed distance so silhouette has zero stair-stepping or dust
    dist_in = cv2.distanceTransform(fg, cv2.DIST_L2, 5)
    dist_out = cv2.distanceTransform(1 - fg, cv2.DIST_L2, 5)
    sdf = dist_in - dist_out
    a_clean = np.clip((sdf + 0.85) / 1.75, 0.0, 1.0) * 255.0
    a_clean[a_clean < 6.0] = 0.0

    # Replace grey matting fringe on semi-transparent rim with pure interior golden membrane RGB (eroded 2px from a==255)
    k3 = np.ones((3, 3), np.uint8)
    pure_mask = cv2.erode((a_raw >= 250.0).astype(np.uint8) & fg, k3, iterations=2) > 0
    col = raw[..., :3].copy()
    known = pure_mask.copy()
    for _ in range(12):
        nxt = cv2.dilate(known.astype(np.uint8), k3) > 0
        s = cv2.blur(col * known[..., None], (3, 3))
        c = cv2.blur(known.astype(np.float32), (3, 3))
        new = nxt & ~known
        col[new] = s[new] / np.maximum(c[new, None], 1e-6)
        known = nxt

    # Tight crop to clean alpha before scaling to exact target_diam
    ys, xs = np.where(a_clean > 0)
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    col_c = col[y0:y1, x0:x1]
    a_c = a_clean[y0:y1, x0:x1]

    ch, cw = a_c.shape
    scale = target_diam / max(cw, ch)
    nw = max(8, int(round(cw * scale)))
    nh = max(8, int(round(ch * scale)))

    # Premultiplied Lanczos downsample prevents any border bleed, followed by 8-pass RGB edge dilation
    a01 = a_c / 255.0
    premul = np.concatenate([col_c * a01[..., None], a_c[..., None]], axis=-1)
    pm_img = [
        np.array(
            Image.fromarray(np.clip(premul[..., ch_i], 0, 255).astype(np.uint8)).resize((nw, nh), Image.LANCZOS),
            dtype=np.float32,
        )
        for ch_i in range(4)
    ]
    a_out = pm_img[3]
    a_out[a_out < 5.0] = 0.0
    rgb_out = np.stack([pm_img[c] / np.maximum(a_out / 255.0, 1e-3) for c in range(3)], axis=-1)

    pad = 4
    canvas = np.zeros((nh + 2 * pad, nw + 2 * pad, 4), dtype=np.float32)
    canvas[pad : pad + nh, pad : pad + nw, :3] = np.clip(rgb_out, 0, 255)
    canvas[pad : pad + nh, pad : pad + nw, 3] = np.clip(a_out, 0, 255)
    # Zero 2px outer border alpha strictly
    canvas[:2, :, 3] = 0
    canvas[-2:, :, 3] = 0
    canvas[:, :2, 3] = 0
    canvas[:, -2:, 3] = 0
    return dilate_rgb(canvas, passes=8)


def make_orb_glow(orb_rgba: np.ndarray, is_satellite: bool = False) -> np.ndarray:
    """Create yolk-aware warm golden + pearlescent additive glow padded cleanly with zero border clipping."""
    pad = 22
    oh, ow = orb_rgba.shape[:2]
    H, W = oh + 2 * pad, ow + 2 * pad
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    cx, cy = (W - 1) * 0.5, (H - 1) * 0.5
    R = min(ow, oh) * 0.47
    r_norm = np.hypot(xx - cx, yy - cy) / max(R, 1.0)

    # Extract warm yolk brilliance from the cleaned orb itself
    rgb = np.zeros((H, W, 3), np.float32)
    a_src = np.zeros((H, W), np.float32)
    rgb[pad : pad + oh, pad : pad + ow] = orb_rgba[..., :3].astype(np.float32)
    a_src[pad : pad + oh, pad : pad + ow] = orb_rgba[..., 3].astype(np.float32) / 255.0

    yolkness = np.clip((rgb[..., 0] - 0.65 * rgb[..., 2]) / 135.0, 0.0, 1.0) * a_src
    yolk_blur = cv2.GaussianBlur(yolkness, (0, 0), sigmaX=R * 0.22)

    core = np.exp(-((r_norm / 0.50) ** 2))
    halo = np.exp(-((r_norm / 0.94) ** 2))
    # Smooth taper strictly to 0 before image edge (r_norm >= 1.32)
    edge_fade = np.clip((1.32 - r_norm) / 0.34, 0.0, 1.0) ** 2

    # Calibrated additive intensity: illuminates yolk & rim warmly while preserving painted membrane detail!
    alpha = (0.62 * core + 0.48 * yolk_blur + 0.24 * halo) * edge_fade
    alpha = np.clip(alpha * (128.0 if is_satellite else 112.0), 0.0, 138.0)
    alpha[alpha < 2.0] = 0.0

    out = np.zeros((H, W, 4), np.float32)
    mix = np.clip(core * 1.15, 0.0, 1.0)
    if is_satellite:
        c_inner = np.array([255.0, 236.0, 148.0], np.float32)
        c_outer = np.array([255.0, 178.0, 44.0], np.float32)
    else:
        c_inner = np.array([255.0, 226.0, 124.0], np.float32)
        c_outer = np.array([246.0, 162.0, 36.0], np.float32)
    out[..., :3] = mix[..., None] * c_inner + (1.0 - mix[..., None]) * c_outer
    out[..., 3] = alpha
    return dilate_rgb(out, passes=8)


def make_comet_trail(variant: int) -> tuple[np.ndarray, tuple[int, int]]:
    """Synthesize a painterly Mega Crit gouache golden slipstream comet streak (head at anchor, tail +X right)."""
    W, H = 310, 132
    anchor_x, anchor_y = 66, 66  # Position in trail image that aligns with sat_k center
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    dx = xx - anchor_x  # -66 .. +244 (roe moves -X left, tail streams +X right)
    dy = yy - anchor_y  # -66 .. +66

    rng_phase = variant * 1.07
    # Subtle organic S-wave undulation along the slipstream wake
    u_tail = np.clip((dx + 18.0) / 238.0, 0.0, 1.0)
    wave = (5.5 * math.sin(rng_phase + 0.6) * np.sin(u_tail * math.pi * 1.7)
            + 3.0 * math.cos(rng_phase) * np.sin(u_tail * math.pi * 3.1)) * (u_tail ** 0.8)
    dy_w = dy - wave

    # Tapered half-width profile: cups roe (r~46) near dx=0, tapers to crisp brush tip at dx=+228
    half_w = 46.0 * ((1.0 - np.clip((dx + 24.0) / 258.0, 0.0, 1.0)) ** 0.72) + 2.5
    lat = np.abs(dy_w) / np.maximum(half_w, 1.5)

    # Bow crescent shockwave hugging leading left edge of the hurled roe (dx around -48..-10)
    r_head = np.hypot(dx - 4.0, dy * 0.96)
    bow_arc = (
        np.exp(-(((r_head - 52.0) / 9.5) ** 2))
        * np.clip((-dx + 18.0) / 48.0, 0.0, 1.0)
        * np.exp(-((dy / 50.0) ** 2))
    )

    # Three distinct painted gouache slipstream ribbons (center spine + upper/lower split wakes)
    long_gate = np.clip((dx + 34.0) / 28.0, 0.0, 1.0) * (np.clip((232.0 - dx) / 68.0, 0.0, 1.0) ** 1.25)
    ribbon_main = np.clip(1.18 - (lat ** 1.65), 0.0, 1.0) * long_gate

    split_u = np.exp(-(((dy_w - (22.0 * (1.0 - 0.45 * u_tail))) / (8.5 * (1.0 - 0.35 * u_tail) + 2.0)) ** 2))
    split_d = np.exp(-(((dy_w + (21.0 * (1.0 - 0.45 * u_tail))) / (8.5 * (1.0 - 0.35 * u_tail) + 2.0)) ** 2))
    ribbons_side = (split_u + split_d) * np.clip((dx - 6.0) / 36.0, 0.0, 1.0) * np.clip((205.0 - dx) / 55.0, 0.0, 1.0)

    # Two crisp trailing golden roe-gel droplets in the wake
    d1 = np.exp(-(((dx - (156.0 + 8.0 * (variant % 3))) / 14.0) ** 2 + ((dy_w - 9.0 * ((-1) ** variant)) / 6.2) ** 2))
    d2 = np.exp(-(((dx - (198.0 + 6.0 * (variant % 2))) / 10.5) ** 2 + ((dy_w + 7.0 * ((-1) ** variant)) / 4.8) ** 2))

    # Carve slight hollow right inside the orb center so trail frames & trails the roe rather than muddying its yolk
    orb_carve = 1.0 - 0.52 * np.exp(-((np.hypot(dx, dy) / 38.0) ** 2))

    raw_a = np.clip((0.88 * ribbon_main + 0.55 * ribbons_side + 0.92 * bow_arc + 0.72 * (d1 + d2)) * orb_carve, 0.0, 1.0)

    # Quantize subtly into 3 painterly Mega Crit gouache value/opacity planes with smooth anti-aliased edge
    core_plane = np.clip((raw_a - 0.52) / 0.24, 0.0, 1.0)
    mid_plane = np.clip((raw_a - 0.22) / 0.20, 0.0, 1.0)

    # Border safety envelope (strictly 0 on all 4 edges)
    bx = np.clip(np.minimum(xx, W - 1 - xx) / 10.0, 0.0, 1.0)
    by = np.clip(np.minimum(yy, H - 1 - yy) / 10.0, 0.0, 1.0)
    alpha = np.clip((0.72 * mid_plane + 0.28 * core_plane + 0.25 * raw_a) * bx * by * 246.0, 0.0, 246.0)
    alpha[alpha < 3.0] = 0.0

    c_deep = np.array([228.0, 142.0, 34.0], np.float32)   # Rich burnt-amber edge
    c_mid = np.array([255.0, 204.0, 68.0], np.float32)    # Warm sun-gold body
    c_hot = np.array([255.0, 248.0, 196.0], np.float32)   # Blazing pale-gold spine & bow arc

    rgb = (
        (1.0 - mid_plane)[..., None] * c_deep
        + (mid_plane - 0.65 * core_plane)[..., None] * c_mid
        + (0.65 * core_plane)[..., None] * c_hot
    )
    rgb = np.clip(rgb + bow_arc[..., None] * 42.0, 0.0, 255.0)

    out = np.zeros((H, W, 4), np.float32)
    out[..., :3] = rgb
    out[..., 3] = alpha
    return dilate_rgb(out, passes=8), (anchor_x, anchor_y)


def make_cluster_aura(mode: str) -> tuple[np.ndarray, tuple[int, int]]:
    """Build cluster-molded bioluminescent aura ('beckon' sun-gold or 'intangible' pearlescent aqua-white-gold)."""
    W, H = 500, 430
    ox = HUB_CANVAS[0] - W // 2
    oy = HUB_CANVAS[1] - H // 2
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    wx = xx + ox
    wy = yy + oy

    # Sum soft glow lobes centered on all 12 eggs so the illumination hugs the exact 12-egg cluster silhouette
    field = np.zeros((H, W), np.float32)
    for spec in ORB_SPECS.values():
        cx, cy = spec["center"]
        rad = spec["diam"] * 0.56
        d2 = ((wx - cx) / rad) ** 2 + ((wy - cy) / rad) ** 2
        field += np.exp(-d2 * 1.15)
    field = np.clip(field / 3.35, 0.0, 1.25)

    rx = (wx - HUB_CANVAS[0]) / (W * 0.46)
    ry = (wy - HUB_CANVAS[1]) / (H * 0.46)
    radial_env = np.clip(1.0 - np.hypot(rx, ry), 0.0, 1.0) ** 1.7

    alpha_f = np.clip(field * radial_env, 0.0, 1.0)
    core_f = np.clip((field - 0.55) / 0.65, 0.0, 1.0) * radial_env

    out = np.zeros((H, W, 4), np.float32)
    if mode == "beckon":
        # Intense warm sun-gold / molten amber brood luminescence (preserves yolk contour without white clip)
        c_outer = np.array([245.0, 138.0, 20.0], np.float32)
        c_inner = np.array([255.0, 212.0, 78.0], np.float32)
        out[..., :3] = core_f[..., None] * c_inner + (1.0 - core_f[..., None]) * c_outer
        out[..., 3] = np.clip(alpha_f * 108.0, 0.0, 118.0)
    else:
        # Pearlescent aqua-cyan-gold SoulFysh spectral luminescence (smooth radial blend, zero center pinch!)
        r_ang = np.hypot(wx - HUB_CANVAS[0], wy - HUB_CANVAS[1]) / 120.0
        ang_gate = np.clip(r_ang, 0.0, 1.0) ** 2
        angle = np.arctan2(wy - HUB_CANVAS[1], wx - HUB_CANVAS[0])
        irid = 0.5 + 0.5 * np.sin(angle * 2.0 + 0.7) * ang_gate
        c_aqua = np.array([76.0, 244.0, 234.0], np.float32)
        c_gold = np.array([186.0, 248.0, 212.0], np.float32)
        c_pearl = np.array([224.0, 255.0, 248.0], np.float32)
        rim_col = irid[..., None] * c_aqua + (1.0 - irid[..., None]) * c_gold
        out[..., :3] = core_f[..., None] * c_pearl + (1.0 - core_f[..., None]) * rim_col
        out[..., 3] = np.clip(alpha_f * 124.0, 0.0, 134.0)

    out[..., 3][out[..., 3] < 2.0] = 0.0
    out[:2, :, 3] = out[-2:, :, 3] = out[:, :2, 3] = out[:, -2:, 3] = 0.0
    return dilate_rgb(out, passes=8), (ox, oy)


def make_cluster_ring(mode: str) -> tuple[np.ndarray, tuple[int, int]]:
    """Build painterly expanding golden halo ring ('beckon_ring') or spectral caustic veil ('intangible_shimmer')."""
    W, H = 468, 416
    ox = HUB_CANVAS[0] - W // 2
    oy = HUB_CANVAS[1] - H // 2
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    dx = (xx - W * 0.5) / 186.0
    dy = (yy - H * 0.5) / 162.0
    r = np.hypot(dx, dy)
    ang = np.arctan2(dy, dx)

    edge_safe = np.clip((1.22 - r) / 0.22, 0.0, 1.0) ** 2
    out = np.zeros((H, W, 4), np.float32)

    if mode == "beckon_ring":
        # Painterly concentric golden halo ring framing the cluster perimeter cleanly
        wobble = 0.022 * np.sin(ang * 3.0 + 0.4) + 0.014 * np.cos(ang * 5.0 - 0.8)
        r_w = r + wobble
        ring_outer = np.exp(-(((r_w - 0.96) / 0.058) ** 2))
        ring_inner = 0.42 * np.exp(-(((r_w - 0.78) / 0.045) ** 2))
        arc_mod = 0.76 + 0.24 * np.sin(ang * 2.0 + 1.1)
        a = np.clip((ring_outer + ring_inner) * arc_mod * edge_safe * 172.0, 0.0, 182.0)
        hot = np.clip(ring_outer * 1.05, 0.0, 1.0)
        c_amber = np.array([248.0, 158.0, 32.0], np.float32)
        c_gold = np.array([255.0, 232.0, 128.0], np.float32)
        out[..., :3] = hot[..., None] * c_gold + (1.0 - hot[..., None]) * c_amber
        out[..., 3] = a
    else:
        # Pearlescent aqua-cyan-gold SoulFysh spectral caustic arcs + luminous soul motes
        caustic_1 = np.exp(-(((r - 0.92 + 0.028 * np.sin(ang * 4.0)) / 0.056) ** 2)) * (0.65 + 0.35 * np.cos(ang * 3.0 - 0.5))
        caustic_2 = 0.55 * np.exp(-(((r - 0.72 - 0.022 * np.cos(ang * 3.0)) / 0.062) ** 2)) * (0.65 + 0.35 * np.sin(ang * 2.0 + 0.9))
        motes = np.zeros_like(r)
        for k in range(8):
            ma = k * (2.0 * math.pi / 8.0) + 0.32
            mr = 0.86 + 0.09 * ((k % 3) - 1)
            mx, my = mr * math.cos(ma), mr * math.sin(ma)
            motes += np.exp(-(((dx - mx) / 0.046) ** 2 + ((dy - my) / 0.046) ** 2))
        a = np.clip((0.85 * caustic_1 + 0.60 * caustic_2 + 0.80 * motes) * edge_safe * 182.0, 0.0, 192.0)
        mix_aq = 0.62 + 0.38 * np.cos(ang * 2.0 - 0.4)
        c_aqua = np.array([92.0, 248.0, 238.0], np.float32)
        c_pearl = np.array([235.0, 255.0, 232.0], np.float32)
        out[..., :3] = mix_aq[..., None] * c_aqua + (1.0 - mix_aq[..., None]) * c_pearl
        out[..., 3] = a

    out[..., 3][out[..., 3] < 2.0] = 0.0
    out[:2, :, 3] = out[-2:, :, 3] = out[:, :2, 3] = out[:, -2:, 3] = 0.0
    return dilate_rgb(out, passes=8), (ox, oy)


def make_shadow() -> tuple[np.ndarray, tuple[int, int]]:
    """Soft ambient underwater contact shadow centered at ORIGIN = (320, 440)."""
    W, H = 380, 74
    ox = ORIGIN[0] - W // 2
    oy = ORIGIN[1] - H // 2
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    rx = (xx - W * 0.5) / (W * 0.44)
    ry = (yy - H * 0.5) / (H * 0.42)
    r = np.hypot(rx, ry)
    a = np.clip((1.0 - r ** 1.8), 0.0, 1.0) ** 1.6 * 190.0
    a[a < 2.0] = 0.0
    out = np.zeros((H, W, 4), np.float32)
    out[..., :3] = np.array([24.0, 22.0, 14.0], np.float32)
    out[..., 3] = a
    return dilate_rgb(out, passes=6), (ox, oy)


def main():
    PARTS_DIR.mkdir(parents=True, exist_ok=True)
    meta = {}

    def save_part(name: str, rgba: np.ndarray, pos_xy: tuple[int, int]):
        h, w = rgba.shape[:2]
        Image.fromarray(rgba).save(PARTS_DIR / f"{name}.png", optimize=True)
        meta[name] = {"x": int(pos_xy[0]), "y": int(pos_xy[1]), "w": int(w), "h": int(h)}

    # 1. Shadow
    sh_rgba, sh_xy = make_shadow()
    save_part("shadow", sh_rgba, sh_xy)

    # 2. 12 High-res Roe Spheres + 12 Yolk-Matched Additive Glows
    for orb_name, spec in ORB_SPECS.items():
        cx, cy = spec["center"]
        orb_rgba = clean_and_resize_orb(SRC_DIR / f"{spec['part']}.png", spec["diam"])
        oh, ow = orb_rgba.shape[:2]
        ox, oy = int(round(cx - ow * 0.5)), int(round(cy - oh * 0.5))
        save_part(orb_name, orb_rgba, (ox, oy))

        is_sat = orb_name.startswith("sat_")
        idx = orb_name.split("_")[1]
        glow_name = f"{'sat' if is_sat else 'brood'}_glow_{idx}"
        glow_rgba = make_orb_glow(orb_rgba, is_satellite=is_sat)
        gh, gw = glow_rgba.shape[:2]
        gx, gy = int(round(cx - gw * 0.5)), int(round(cy - gh * 0.5))
        save_part(glow_name, glow_rgba, (gx, gy))

    # 3. 6 Golden Comet Slipstream Trails (trail_0..5)
    for k in range(6):
        cx, cy = ORB_SPECS[f"sat_{k}"]["center"]
        tr_rgba, (ax, ay) = make_comet_trail(k)
        save_part(f"trail_{k}", tr_rgba, (cx - ax, cy - ay))

    # 4. Dual Bioluminescent Illumination Layers
    ba_rgba, ba_xy = make_cluster_aura("beckon")
    save_part("beckon_aura", ba_rgba, ba_xy)

    br_rgba, br_xy = make_cluster_ring("beckon_ring")
    save_part("beckon_ring", br_rgba, br_xy)

    ia_rgba, ia_xy = make_cluster_aura("intangible")
    save_part("intangible_aura", ia_rgba, ia_xy)

    is_rgba, is_xy = make_cluster_ring("intangible_shimmer")
    save_part("intangible_shimmer", is_rgba, is_xy)

    (PARTS_DIR / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8", newline="\n")
    print(f"Extracted {len(meta)} parts into {PARTS_DIR}")


if __name__ == "__main__":
    main()
