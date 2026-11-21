#!/usr/bin/env python3
"""
Step 1 (v3 — AAA Submerged Colossal Kraken + Hand-Painted Vanilla-STS2 Multi-Part VFX):
- Uses sts2-monster-art 3px inward chroma-green decontamination (zero green fringe).
- Archives all raw masters into STS2_Things/source_assets/monsters/abyssal_inkmaw/masters/.
- Extracts & dissects:
  1. Background plates (`abyssal_inkmaw_bg_water.png`, `abyssal_inkmaw_bridge.png`, `abyssal_inkmaw_fg_frame.png`)
  2. Half-submerged full-upper-body Front-Facing Kraken (`parts_body/`) + exact 20-vertex hardware eye-socket
     clipping polygons (`eye_clip_l`, `eye_clip_r`) + rigid unstretched sliding upper eyelids!
  3. Hand-painted subterranean lake water-splash & wave VFX pieces (`parts_vfx_water/`)
  4. Hand-painted shattered ancient stone-bridge fracture, 4 hinged flagstones, 5 rubble shards & dust (`parts_vfx_breach/`)
  5. Hand-painted high-pressure sweeping obsidian ink stream, curling clouds & 6 splatter drops (`parts_vfx_ink/`)
  6. Hero & background tentacles (`parts_crushing/`, `parts_ink/`, `parts_body/bg_tentacle_*`)
"""
from __future__ import annotations

import json
import math
import shutil
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, r"C:\Users\adner\.gemini\config\skills\sts2-monster-art\scripts")
import extract_monster_rgba as ema

ART_DIR = Path(r"C:\Users\adner\.gemini\antigravity\brain\630b9439-8513-49e2-8959-201a92e08ca5")
REPO = Path(r"D:\Things\Things-Workspace\STS2_Things")
MASTERS_DIR = REPO / "source_assets/monsters/abyssal_inkmaw/masters"
ROOMS_DIR = REPO / "images/rooms/abyssal_inkmaw"
RIG_ROOT = REPO / "tools/AbyssalInkmawRig"

RAW_FILES = {
    "stage_master": ART_DIR / "abyssal_inkmaw_stage_v4_a_1795097380393.jpg",
    "body": ART_DIR / "abyssal_inkmaw_bighead_complete_a_1795097365762.jpg",
    "hero_tentacles": ART_DIR / "kraken_tentacles_spritesheet_v3_1795087164515.jpg",
    "hero_crushing_v2": ART_DIR / "abyssal_crushing_v2_raw_1795144503561.jpg",
    "hero_ink_v2": ART_DIR / "abyssal_ink_v2_raw_1795144547366.jpg",
    "small_spike": ART_DIR / "abyssal_small_spike_raw_1795144412965.jpg",
    "small_shell": ART_DIR / "abyssal_small_shell_raw_1795144441980.jpg",
    "small_gland": ART_DIR / "abyssal_small_gland_raw_1795144461371.jpg",
    "vfx_slam": ART_DIR / "abyssal_slam_vfx_v2_raw_1795166226448.jpg",
    "bg_tentacles": ART_DIR / "kraken_tentacles_sheet_v3_1795087071562.jpg",
    "vfx_water": ART_DIR / "sts2_depths_water_splash_vfx_sheet_v2_1795089894304.jpg",
    "vfx_water_ripples": ART_DIR / "abyssal_water_ripples_v2_raw_1795175878388.jpg",
    "vfx_breach": ART_DIR / "abyssal_bridge_breach_v2_raw_1795153405650.jpg",
    "vfx_ink": ART_DIR / "abyssal_ink_spray_vfx_sheet_v2_1795089801718.jpg",
    "vfx_ink_cannon": ART_DIR / "hydro_ink_cannon_vfx_sheet_v2_1795093142044.jpg" if (ART_DIR / "hydro_ink_cannon_vfx_sheet_v2_1795093142044.jpg").exists() else ART_DIR / "hydro_ink_cannon_vfx_sheet_1795093142044.jpg",
}


def extract_green_rgba(img: Image.Image) -> Image.Image:
    """Use sts2-monster-art's exact chroma-green BFS + 3px inward color decontamination."""
    rgba, _ = ema.extract_rgba_from_chroma(img, chroma_mode="green")
    return rgba


def extract_soft_green_rgba(img: Image.Image) -> Image.Image:
    """Extra gentle chroma-key despill for soft misty dust/ink-wisp VFX so zero green tint survives."""
    rgba = extract_green_rgba(img)
    arr = np.array(rgba, dtype=np.float32)
    r, g, b, a = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2], arr[:, :, 3]
    # Suppress any residual green dominance in semi-transparent wispy dust/spray edges
    rb_max = np.maximum(r, b)
    excess_g = np.maximum(0.0, g - rb_max - 8.0)
    # Reduce alpha gently where pixels were mostly chroma-green haze, and despill green channel
    arr[:, :, 1] = np.minimum(g, rb_max + 8.0)
    arr[:, :, 3] = np.clip(a - excess_g * 2.2, 0.0, 255.0)
    return Image.fromarray(arr.astype(np.uint8), mode="RGBA")


def crop_tight(im: Image.Image, pad: int = 8) -> Image.Image:
    bbox = im.getbbox()
    if bbox is None:
        return im
    x0 = max(0, bbox[0] - pad)
    y0 = max(0, bbox[1] - pad)
    x1 = min(im.width, bbox[2] + pad)
    y1 = min(im.height, bbox[3] + pad)
    return im.crop((x0, y0, x1, y1))


def clean_small_islands(im: Image.Image, min_area: int = 400) -> Image.Image:
    arr = np.array(im, dtype=np.uint8)
    mask = (arr[:, :, 3] > 20).astype(np.uint8)
    num, labels, stats, _ = cv2.connectedComponentsWithStats(mask, connectivity=8)
    keep = np.zeros_like(mask)
    for i in range(1, num):
        if stats[i, cv2.CC_STAT_AREA] >= min_area:
            keep[labels == i] = 1
    arr[:, :, 3] = np.where(keep > 0, arr[:, :, 3], 0)
    return Image.fromarray(arr, mode="RGBA")


def split_two_halves(im: Image.Image) -> tuple[Image.Image, Image.Image]:
    im = clean_small_islands(im, min_area=1500)
    mid = im.width // 2
    left = crop_tight(im.crop((0, 0, mid, im.height)), pad=12)
    right = crop_tight(im.crop((mid, 0, im.width, im.height)), pad=12)
    return left, right


def extract_components(im: Image.Image, min_area: int = 180, merge_dilate: int = 0) -> list[tuple[Image.Image, tuple[int, int, int, int]]]:
    """Extract disconnected RGBA sub-sprites from a region, optionally dilating mask slightly to group nearby droplets."""
    arr = np.array(im, dtype=np.uint8)
    mask = (arr[:, :, 3] > 24).astype(np.uint8)
    if merge_dilate > 0:
        work = cv2.dilate(mask, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (merge_dilate * 2 + 1, merge_dilate * 2 + 1)))
    else:
        work = mask
    num, labels, stats, _ = cv2.connectedComponentsWithStats(work, connectivity=8)
    out = []
    for i in range(1, num):
        x, y, w, h, area = stats[i]
        # Measure real un-dilated alpha area inside this component
        sub_lbl = (labels[y:y + h, x:x + w] == i)
        real_area = int((mask[y:y + h, x:x + w] & sub_lbl).sum())
        if real_area < min_area:
            continue
        sub = arr[y:y + h, x:x + w].copy()
        sub[:, :, 3] = np.where(sub_lbl, sub[:, :, 3], 0)
        cim = crop_tight(Image.fromarray(sub, mode="RGBA"), pad=6)
        out.append((cim, (int(x), int(y), int(w), int(h))))
    return out


def resample_closed_polygon(contour: np.ndarray, n_pts: int = 20) -> list[list[float]]:
    """Resample an OpenCV closed contour uniformly to `n_pts` CCW vertices for Spine 4.2 clipping."""
    pts = contour.reshape(-1, 2).astype(np.float64)
    # Close loop
    closed = np.vstack([pts, pts[0:1]])
    segs = np.hypot(np.diff(closed[:, 0]), np.diff(closed[:, 1]))
    arc = np.concatenate([[0.0], np.cumsum(segs)])
    total = arc[-1]
    if total < 1e-3:
        return [[float(p[0]), float(p[1])] for p in pts[:n_pts]]
    s = np.linspace(0.0, total, n_pts, endpoint=False)
    xs = np.interp(s, arc, closed[:, 0])
    ys = np.interp(s, arc, closed[:, 1])
    return [[round(float(x), 2), round(float(y), 2)] for x, y in zip(xs, ys)]


def prepare_bg_plates() -> tuple[Image.Image, Image.Image, Image.Image]:
    """
    Build unified 32:15 (2048x960) wide-lake + EXPANDED BROAD STONE QUAY boss stage plates from a SINGLE
    lineless matte gouache master (`sts2-background-art` v2026-11-19):
      - Calibrated Depths atmospheric dark grade (median luma ~36-40 matching Act 2 rooms)
      - Raises the ancient stone bridge curb cleanly from y=594 -> y=548 (57.1%) and expands the flat
        wet basalt stone combat deck from 198px -> 300px (y=578..878) while pushing the front curb drop
        edge down to y=878..960 behind the hand fan, providing +34% more vertical stone room for
        4 players, outer small tentacles, Phase 2 floor breaches, and full bridge-slam impact craters!
      - Pixel-accurate weathered stone bridge parapet separation (`abyssal_inkmaw_bridge.png`)
      - Seamless deep-water continuation below curb (`abyssal_inkmaw_bg_water.png`)
      - Subtle bottom-only dark repoussoir (`abyssal_inkmaw_fg_frame.png`, y >= 89.5%)
    """
    ROOMS_DIR.mkdir(parents=True, exist_ok=True)
    raw = Image.open(RAW_FILES["stage_master"]).convert("RGB")
    tw, th = 2048, 960
    sw, sh = raw.size
    target_h = int(round(sw * th / tw))  # 645px for 1376x768
    y0 = 84  # Shifts cavern ceiling & aqueduct horizon smoothly upward by 27px so the broader shore stays balanced!
    crop = raw.crop((0, y0, sw, y0 + target_h)).resize((tw, th), Image.Resampling.LANCZOS)

    # Calibrate luminance cleanly into Act 2 Depths sweet spot (median luma ~37-39, matching cave_riverbend_00)
    arr_f = np.array(crop, dtype=np.float32) / 255.0
    graded_f = np.power(np.clip(arr_f, 0.0, 1.0), 1.30) * 0.87
    src_graded = np.clip(graded_f * 255.0, 0, 255).astype(np.uint8)

    # Trace natural stone curb top in src_graded (at y0=84, natural curb is around y ~ 567)
    search_y0, search_y1 = 540, 595
    r_band = src_graded[search_y0:search_y1, :, 0].astype(np.float32)
    b_band = src_graded[search_y0:search_y1, :, 2].astype(np.float32)
    stone_score = r_band - 0.35 * np.maximum(0.0, b_band - r_band)
    src_curb = np.full(tw, 567, dtype=np.float32)
    for x in range(tw):
        col = stone_score[:, x]
        d = np.diff(cv2.GaussianBlur(col.reshape(-1, 1), (1, 5), 0).ravel())
        src_curb[x] = float(search_y0 + int(np.argmax(d)))
    pad_s = np.pad(src_curb, 7, mode="edge")
    src_curb_med = np.array([np.median(pad_s[i:i + 15]) for i in range(tw)], dtype=np.float32)
    src_curb_y = int(round(float(np.median(src_curb_med))))

    # Compose the EXPANDED BROAD STONE QUAY (`new_curb_y = 548`, 1:1 unstretched top & front curbs, 300px flat basalt deck!)
    NEW_CURB_Y = 548
    TOP_CURB_H = 30
    SRC_DECK_Y0 = src_curb_y + TOP_CURB_H
    SRC_DECK_Y1 = min(th - 88, SRC_DECK_Y0 + 198)
    NEW_DECK_Y0 = NEW_CURB_Y + TOP_CURB_H   # 578
    NEW_DECK_Y1 = 878                       # 300px deep flat basalt combat shore!
    FRONT_H = th - NEW_DECK_Y1              # 82px crisp 1:1 front curb tucked behind hand cards

    graded = np.zeros_like(src_graded)
    # 1) Cavern ceiling, aqueduct & wide open lake (100% unstretched 1:1!)
    graded[:NEW_CURB_Y, :, :] = src_graded[:NEW_CURB_Y, :, :]
    # 2) Weathered stone top curb (100% unstretched 1:1!)
    graded[NEW_CURB_Y:NEW_DECK_Y0, :, :] = src_graded[src_curb_y:SRC_DECK_Y0, :, :]
    # 3) Broad expanded flat wet basalt stone deck (preserves exact perspective mortar grooves!)
    deck_pil = Image.fromarray(src_graded[SRC_DECK_Y0:SRC_DECK_Y1, :, :], mode="RGB").resize(
        (tw, NEW_DECK_Y1 - NEW_DECK_Y0), Image.Resampling.LANCZOS
    )
    deck_arr = np.array(deck_pil, dtype=np.float32)
    # Subtle deterministic matte-gouache micro-grain so expanded slabs retain crisp hand-painted stone tooth
    rng_g = np.random.default_rng(20261120)
    grain = cv2.GaussianBlur(rng_g.normal(0.0, 1.35, (deck_arr.shape[0], tw)).astype(np.float32), (3, 3), 0)[:, :, None]
    graded[NEW_DECK_Y0:NEW_DECK_Y1, :, :] = np.clip(deck_arr + grain, 0, 255).astype(np.uint8)
    # 4) Crisp 1:1 weathered front stone curb along bottom edge (behind hand fan!)
    graded[NEW_DECK_Y1:th, :, :] = src_graded[SRC_DECK_Y1:SRC_DECK_Y1 + FRONT_H, :, :]

    luma_all = 0.299 * graded[:, :, 0] + 0.587 * graded[:, :, 1] + 0.114 * graded[:, :, 2]
    print(f"STAGE_MASTER_GRADED: size={tw}x{th}, median_luma={np.median(luma_all):.1f}, p90={np.percentile(luma_all, 90):.1f}")

    # Shift traced chipped curb contour cleanly by (NEW_CURB_Y - src_curb_y) so chipped stone edge stays pixel-exact!
    curb_y_smooth = cv2.GaussianBlur((src_curb_med + (NEW_CURB_Y - src_curb_y)).reshape(1, -1), (9, 1), 0).ravel()
    print(f"BRIDGE_CURB_Y_2048: min={curb_y_smooth.min():.1f}, med={np.median(curb_y_smooth):.1f}, max={curb_y_smooth.max():.1f}")

    # 1) Bridge RGBA plate (transparent strictly above weathered stone curb top edge, anti-aliased 2px)
    yy = np.arange(th, dtype=np.float32)[:, None]
    alpha_bridge = np.clip((yy - (curb_y_smooth[None, :] - 0.5)) / 2.0, 0.0, 1.0)
    bridge_arr = np.dstack([graded, np.round(alpha_bridge * 255.0).astype(np.uint8)])
    bridge_plate = Image.fromarray(bridge_arr, mode="RGBA")
    bridge_plate.save(ROOMS_DIR / "abyssal_inkmaw_bridge.png", optimize=True)

    # 2) Continuous wide-lake background plate (identical above curb; seamless dark water below curb for camera shake)
    bg_arr = graded.copy()
    water_src_h = 140
    for x in range(tw):
        yc = int(math.floor(curb_y_smooth[x])) - 2
        src_top = max(0, yc - water_src_h)
        src_strip = graded[src_top:yc, x, :].astype(np.float32)  # shape (L, 3)
        L = src_strip.shape[0]
        if L < 8:
            continue
        rem = th - yc
        idx = np.arange(rem)
        period = max(1, 2 * L - 2)
        m = idx % period
        m = np.where(m < L, L - 1 - m, m - L + 1)
        ext = src_strip[m]
        fade = np.linspace(1.0, 0.58, rem, dtype=np.float32)[:, None]
        bg_arr[yc:, x, :] = np.clip(ext * fade, 0, 255).astype(np.uint8)
    seam_min = int(curb_y_smooth.min()) - 4
    bg_arr[seam_min:, :, :] = cv2.GaussianBlur(bg_arr[seam_min:, :, :], (15, 7), 0)
    bg_plate = Image.fromarray(bg_arr, mode="RGB")
    bg_plate.save(ROOMS_DIR / "abyssal_inkmaw_bg_water.png", optimize=True)

    # 3) Subtle dark abyssal bottom-edge repoussoir strictly along y >= 89.5%
    fg_arr = np.zeros((th, tw, 4), dtype=np.uint8)
    y_start = int(th * 0.895)
    for y in range(y_start, th):
        t = (y - y_start) / max(1, th - 1 - y_start)
        alpha = int(round((t ** 1.65) * 200))
        fg_arr[y, :, 0] = 8
        fg_arr[y, :, 1] = 11
        fg_arr[y, :, 2] = 17
        fg_arr[y, :, 3] = alpha
    fg_plate = Image.fromarray(fg_arr, mode="RGBA")
    fg_plate.save(ROOMS_DIR / "abyssal_inkmaw_fg_frame.png", optimize=True)
    return bg_plate, bridge_plate, fg_plate


def dissect_body_parts(body_rgba: Image.Image) -> tuple[dict[str, tuple[Image.Image, tuple[int, int]]], dict]:
    """
    Dissect the complete, zero-flat-cut Front-Facing Big-Head Kraken onto a 1280x1060 canvas:
    - Preserves ALL 4 organically curled lower tentacles down to y ~ 1010 (ZERO flat chop!)
    - Records `waterline_px_y` (~77.5% down the body height) where the Kraken cleanly penetrates the lake surface,
      leaving ~22% (~215px) of complete curled lower tentacles submerged beneath the hardware `boss_water_clip`!
    - Extracts exact 20-vertex hardware eye-socket clipping polygons (`eye_clip_l`, `eye_clip_r`)
    - Extracts crisp detached horizontal slit pupils (`pupil_l`, `pupil_r`)
    - Paints TALL rigid 1:1 unstretched curved eyelids (`eyelid_l`, `eyelid_r`)
    - Auto-extracts bioluminescent crown & shoulder seafoam spots + fierce eyes into `ink_glow`
    """
    CW, CH = 1280, 1060
    tight = crop_tight(clean_small_islands(body_rgba, min_area=1500), pad=4)
    # Fit complete big-head Kraken cleanly inside 1140x960 with generous padding on all 4 sides!
    scale = min(1140.0 / tight.width, 960.0 / tight.height)
    nw, nh = int(round(tight.width * scale)), int(round(tight.height * scale))
    resized = tight.resize((nw, nh), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    ox = (CW - nw) // 2
    oy = 46
    canvas.paste(resized, (ox, oy), resized)
    waterline_px_y = oy + int(round(nh * 0.775))

    arr = np.array(canvas, dtype=np.uint8)
    rgb = arr[:, :, :3].astype(np.float32)
    alpha = arr[:, :, 3]
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]

    # Locate the two symmetrical pale-amber sclera eyes in the head zone (y: 260..580, x: 400..880)
    sclera_mask = (alpha > 200) & (r > 158) & (g > 134) & ((r + g - 2 * b) > 65)
    sclera_mask[:260, :] = False
    sclera_mask[580:, :] = False
    sclera_mask[:, :400] = False
    sclera_mask[:, 880:] = False

    num, labels, stats, centroids = cv2.connectedComponentsWithStats(sclera_mask.astype(np.uint8), connectivity=8)
    eye_comps = []
    for i in range(1, num):
        if stats[i, cv2.CC_STAT_AREA] >= 120:
            eye_comps.append((centroids[i][0], i, stats[i]))
    assert len(eye_comps) >= 2, f"Expected 2 sclera eyes, found {len(eye_comps)}"
    eye_comps = sorted(eye_comps, key=lambda t: -t[2][cv2.CC_STAT_AREA])[:2]
    eye_comps.sort(key=lambda t: t[0])

    head_clean = arr.copy()
    parts: dict[str, tuple[Image.Image, tuple[int, int]]] = {}
    extra_meta: dict = {"canvas_w": CW, "canvas_h": CH, "eye_boxes": {}, "eye_clips": {}}

    for tag, (_, cid, st) in zip(("l", "r"), eye_comps):
        x, y, w, h, _ = st
        extra_meta["eye_boxes"][tag] = [int(x), int(y), int(w), int(h)]
        ex0, ey0 = max(0, x - 6), max(0, y - 6)
        ex1, ey1 = min(CW, x + w + 6), min(CH, y + h + 6)
        sub_sclera = (labels[ey0:ey1, ex0:ex1] == cid)

        contours, _ = cv2.findContours(sub_sclera.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        hull = cv2.convexHull(contours[0])
        hull_mask = np.zeros_like(sub_sclera, dtype=np.uint8)
        cv2.drawContours(hull_mask, [hull], -1, 1, thickness=-1)

        # Extract smooth 20-vertex hardware Spine 4.2 clipping polygon hugging the exact sclera rim (expanded +1px)!
        clip_mask = cv2.dilate(hull_mask, np.ones((3, 3), np.uint8))
        c_cnts, _ = cv2.findContours(clip_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        clip_poly_local = resample_closed_polygon(c_cnts[0], n_pts=20)
        extra_meta["eye_clips"][tag] = [[round(px + ex0, 2), round(py + ey0, 2)] for px, py in clip_poly_local]

        # Capture ONLY interior horizontal slit pupil
        erode_k = 3 if w < 44 else 5
        inner_hull = cv2.erode(hull_mask, np.ones((erode_k, erode_k), np.uint8)) > 0
        sub_rgb = arr[ey0:ey1, ex0:ex1, :3]
        sub_lum = 0.299 * sub_rgb[:, :, 0] + 0.587 * sub_rgb[:, :, 1] + 0.114 * sub_rgb[:, :, 2]
        pupil_zone = inner_hull & (~sub_sclera) & (sub_lum < 120)
        p_num, p_lbl, p_st, _ = cv2.connectedComponentsWithStats(pupil_zone.astype(np.uint8), connectivity=8)
        if p_num > 1:
            best_p = 1 + int(np.argmax(p_st[1:, cv2.CC_STAT_AREA]))
            pupil_zone = (p_lbl == best_p)

        p_arr = np.zeros((ey1 - ey0, ex1 - ex0, 4), dtype=np.uint8)
        p_dil = cv2.dilate(pupil_zone.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        p_arr[p_dil, :3] = sub_rgb[p_dil]
        p_arr[pupil_zone, 3] = 255
        p_arr[p_dil & (~pupil_zone), 3] = 175
        p_im = Image.fromarray(p_arr, mode="RGBA")
        pb = p_im.getbbox()
        if pb is not None:
            p_crop = p_im.crop((max(0, pb[0] - 2), max(0, pb[1] - 2), min(p_im.width, pb[2] + 2), min(p_im.height, pb[3] + 2)))
            parts[f"pupil_{tag}"] = (p_crop, (ex0 + max(0, pb[0] - 2), ey0 + max(0, pb[1] - 2)))

        # Telea inpaint pupil cleanly back to smooth warm amber sclera gradient
        inpaint_mask = ((cv2.dilate(pupil_zone.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0) & inner_hull).astype(np.uint8) * 255
        sub_bgr = cv2.cvtColor(head_clean[ey0:ey1, ex0:ex1, :3], cv2.COLOR_RGB2BGR)
        inpainted_bgr = cv2.inpaint(sub_bgr, inpaint_mask, 4, cv2.INPAINT_TELEA)
        head_clean[ey0:ey1, ex0:ex1, :3] = cv2.cvtColor(inpainted_bgr, cv2.COLOR_BGR2RGB)

        # Paint a RIGID, UNSTRETCHED curved fleshy upper eyelid shutter (clipped cleanly by eye_clip!)
        # Because hardware eye_clip masks it strictly to the eye socket, it slides DOWNWARD at 1:1 scale (zero stretching!)
        lid_w = w + 30
        lid_h = h + 30
        lid_im = Image.new("RGBA", (lid_w, lid_h), (0, 0, 0, 0))
        ld = ImageDraw.Draw(lid_im)
        sy0, sy1 = max(0, y - 22), max(1, y - 5)
        brow_col = tuple(int(v) for v in np.median(arr[sy0:sy1, x:x + w, :3].reshape(-1, 3), axis=0))
        lid_top = tuple(min(255, int(v * 0.96)) for v in brow_col)
        lid_bot = tuple(max(12, int(v * 0.68)) for v in brow_col)
        lid_lash = tuple(max(8, int(v * 0.42)) for v in brow_col)
        # Solid matte gouache lid body with gently curved fleshy bottom lash rim
        ld.rectangle((0, 0, lid_w, lid_h - 8), fill=lid_top + (255,))
        ld.ellipse((-8, lid_h - 24, lid_w + 8, lid_h - 1), fill=lid_bot + (255,))
        ld.arc((-8, lid_h - 24, lid_w + 8, lid_h - 1), start=10, end=170, fill=lid_lash + (255,), width=5)
        parts[f"eyelid_{tag}"] = (lid_im, (x - 15, y - 14))

    parts["head_mantle"] = (Image.fromarray(head_clean, mode="RGBA"), (0, 0))

    # Build Phase-2 / Charge additive bioluminescent glow using ACTUAL painted seafoam-teal spots on crown + shoulders + eyes!
    glow = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for tag in ("l", "r"):
        x, y, w, h = extra_meta["eye_boxes"][tag]
        cx, cy = x + w // 2, y + h // 2
        gd.ellipse((cx - int(w * 0.95), cy - int(h * 1.15), cx + int(w * 0.95), cy + int(h * 1.15)), fill=(115, 240, 225, 165))
        gd.ellipse((cx - int(w * 0.48), cy - int(h * 0.55), cx + int(w * 0.48), cy + int(h * 0.55)), fill=(255, 240, 175, 235))
    # Auto-detect ALL painted seafoam-teal spots across crown AND massive upper shoulders (y < 720)!
    ring_mask = (alpha > 200) & (g > 98) & (b > 102) & ((g - r) > 5) & ((b - r) > 7)
    ring_mask[720:, :] = False
    ring_mask = cv2.dilate(ring_mask.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
    g_arr_pre = np.array(glow, dtype=np.uint8)
    g_arr_pre[ring_mask, 0] = np.maximum(g_arr_pre[ring_mask, 0], 100)
    g_arr_pre[ring_mask, 1] = np.maximum(g_arr_pre[ring_mask, 1], 242)
    g_arr_pre[ring_mask, 2] = np.maximum(g_arr_pre[ring_mask, 2], 232)
    g_arr_pre[ring_mask, 3] = np.maximum(g_arr_pre[ring_mask, 3], 215)
    glow = Image.fromarray(g_arr_pre, mode="RGBA").filter(ImageFilter.GaussianBlur(radius=9))
    body_mask = Image.fromarray(alpha, mode="L").filter(ImageFilter.MaxFilter(9))
    g_arr = np.array(glow, dtype=np.uint8)
    g_arr[:, :, 3] = (g_arr[:, :, 3].astype(np.float32) * (np.array(body_mask, dtype=np.float32) / 255.0)).astype(np.uint8)
    gb = Image.fromarray(g_arr, mode="RGBA").getbbox()
    glow_crop = Image.fromarray(g_arr, mode="RGBA").crop((max(0, gb[0] - 4), max(0, gb[1] - 4), min(CW, gb[2] + 4), min(CH, gb[3] + 4)))
    parts["ink_glow"] = (glow_crop, (max(0, gb[0] - 4), max(0, gb[1] - 4)))

    # Locate the central dark beak-arch cavity right beneath the front collar mantle (y: 470..610, x: 540..740)
    lum_all = 0.299 * r + 0.587 * g + 0.114 * b
    beak_mask = (alpha > 220) & (lum_all < 38)
    beak_mask[:470, :] = False
    beak_mask[610:, :] = False
    beak_mask[:, :540] = False
    beak_mask[:, 740:] = False
    by_s, bx_s = np.where(beak_mask)
    if len(bx_s) > 20:
        beak_cx, beak_cy = int(round(float(np.median(bx_s)))), int(round(float(np.median(by_s))))
    else:
        beak_cx, beak_cy = CW // 2, 540
    extra_meta["beak_notch"] = [beak_cx, beak_cy]
    extra_meta["body_bbox"] = list(canvas.getbbox())
    extra_meta["waterline_px_y"] = int(waterline_px_y)

    print("BODY_BBOX:", extra_meta["body_bbox"])
    print("WATERLINE_PX_Y:", extra_meta["waterline_px_y"])
    print("EYE_BOXES:", extra_meta["eye_boxes"])
    print("BEAK_NOTCH:", extra_meta["beak_notch"])
    return parts, extra_meta


def extract_chroma_magenta_rgba(img: Image.Image) -> Image.Image:
    """
    Orthogonal chroma-magenta (#FF00FF) extraction for delicate cool teal/cyan/sea-mist water ripples.
    Preserves razor-thin 1px painterly gouache wisps & dark petrol troughs with ZERO magenta/pink fringe!
    """
    from scipy import ndimage

    arr = np.array(img.convert("RGB"), dtype=np.float32)
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    # Cool teal/cyan water always has min(R, B) - G <= 0, whereas #FF00FF magenta has +255!
    mag = np.minimum(r, b) - g
    alpha = np.clip((110.0 - mag) / (110.0 - 6.0), 0.0, 1.0)
    alpha = np.where((mag > 110.0) & (r > 130.0) & (b > 130.0), 0.0, alpha)

    pure_fg = (alpha >= 0.985) & (mag <= 2.0)
    out_rgb = arr.copy()
    if pure_fg.any():
        _, (iy, ix) = ndimage.distance_transform_edt(~pure_fg, return_indices=True)
        decontam = (alpha > 0.0) & (~pure_fg)
        rim_w = np.clip((mag + 4.0) / 28.0, 0.0, 1.0)
        rim_w = np.where(alpha < 0.96, 1.0, rim_w)
        for c in range(3):
            out_rgb[:, :, c] = np.where(
                decontam,
                out_rgb[:, :, c] * (1.0 - rim_w) + arr[iy, ix, c] * rim_w,
                out_rgb[:, :, c],
            )
    # Hard anti-magenta clamp on all cool-water pixels so zero pink halo can ever survive
    out_rgb[:, :, 0] = np.minimum(out_rgb[:, :, 0], out_rgb[:, :, 1] + 4.0)
    out_rgb[:, :, 2] = np.minimum(out_rgb[:, :, 2], out_rgb[:, :, 1] + 38.0)

    rgba = np.zeros((arr.shape[0], arr.shape[1], 4), dtype=np.uint8)
    rgba[:, :, :3] = np.clip(np.round(out_rgb), 0, 255).astype(np.uint8)
    rgba[:, :, 3] = np.clip(np.round(alpha * 255.0), 0, 255).astype(np.uint8)
    return Image.fromarray(rgba, mode="RGBA")


def grade_lake_ripple_tone(im: Image.Image, luma_scale: float = 0.86, teal_cool: float = 1.0) -> Image.Image:
    """Harmonize hand-painted water ripples seamlessly onto our dark cool-teal subterranean lake palette."""
    arr = np.array(im.convert("RGBA"), dtype=np.float32)
    rgb = arr[:, :, :3]
    # Cool lake tint (slightly deeper R, rich petrol-teal G & B matching NEAR/MID lake)
    rgb[:, :, 0] *= luma_scale * (0.86 / teal_cool)
    rgb[:, :, 1] *= luma_scale * 0.98
    rgb[:, :, 2] *= luma_scale * 1.03
    arr[:, :, :3] = np.clip(rgb, 0.0, 255.0)
    return Image.fromarray(np.clip(np.round(arr), 0, 255).astype(np.uint8), mode="RGBA")


def split_ring_back_front(ring_im: Image.Image, split_frac: float = 0.50, feather_px: float = 8.0) -> tuple[Image.Image, Image.Image]:
    """Split an elliptical ripple/meniscus ring horizontally into upper rear arc and lower front lapping arc."""
    rw_r, rh_r = ring_im.size
    r_arr = np.array(ring_im, dtype=np.float32)
    split_row = rh_r * split_frac
    yy_r = np.arange(rh_r, dtype=np.float32)[:, None]
    w_front = np.clip((yy_r - (split_row - feather_px * 0.5)) / max(1.0, feather_px), 0.0, 1.0)
    w_back = 1.0 - w_front
    back_arr = r_arr.copy()
    back_arr[:, :, 3] *= w_back
    front_arr = r_arr.copy()
    front_arr[:, :, 3] *= w_front
    return (
        Image.fromarray(np.clip(back_arr, 0, 255).astype(np.uint8), mode="RGBA"),
        Image.fromarray(np.clip(front_arr, 0, 255).astype(np.uint8), mode="RGBA"),
    )


def extract_water_vfx() -> dict[str, tuple[Image.Image, tuple[int, int]]]:
    """
    Extract refined Mega Crit 2D lineless gouache subterranean lake ripples & meniscus pieces
    from `vfx_water_ripples` (magenta-keyed, 4 distinct variants!), paired with hand-painted
    breaking waves, spray plumes & ballistic water droplets from `vfx_water`.
    """
    def fit_w(im: Image.Image, target_w: int) -> Image.Image:
        sc = target_w / max(1, im.width)
        return im.resize((target_w, max(1, int(round(im.height * sc)))), Image.Resampling.LANCZOS)

    # 1) New refined lineless-gouache subterranean lake ripples & meniscus sheet (#FF00FF keyed!)
    rip_raw = Image.open(RAW_FILES["vfx_water_ripples"]).convert("RGB")
    rip_rgba = extract_chroma_magenta_rgba(rip_raw)
    RW, RH = rip_rgba.size
    rmx, rmy = int(RW * 0.49), int(RH * 0.50)

    meniscus_raw = crop_tight(clean_small_islands(rip_rgba.crop((0, 0, rmx, rmy)), 28), pad=6)
    ripple_a_raw = crop_tight(clean_small_islands(rip_rgba.crop((rmx, 0, RW, rmy)), 24), pad=6)
    ripple_b_raw = crop_tight(clean_small_islands(rip_rgba.crop((0, rmy, rmx, RH)), 24), pad=6)
    glint_raw = crop_tight(clean_small_islands(rip_rgba.crop((rmx, rmy, RW, RH)), 24), pad=6)

    meniscus_fit = grade_lake_ripple_tone(fit_w(meniscus_raw, 380), luma_scale=0.88)
    ripple_a_fit = grade_lake_ripple_tone(fit_w(ripple_a_raw, 420), luma_scale=0.84)
    ripple_b_fit = grade_lake_ripple_tone(fit_w(ripple_b_raw, 420), luma_scale=0.84)
    glint_fit = grade_lake_ripple_tone(fit_w(glint_raw, 360), luma_scale=0.80)

    men_back, men_front = split_ring_back_front(meniscus_fit, split_frac=0.50, feather_px=10.0)
    rip_a_back, rip_a_front = split_ring_back_front(ripple_a_fit, split_frac=0.50, feather_px=10.0)
    rip_b_back, rip_b_front = split_ring_back_front(ripple_b_fit, split_frac=0.50, feather_px=10.0)

    # 2) Breaking waves, spray plumes & ballistic droplets from `vfx_water` (harmonized onto dark teal lake)
    raw = Image.open(RAW_FILES["vfx_water"]).convert("RGB")
    rgba = extract_soft_green_rgba(raw)
    W, H = rgba.size
    mid_y = int(H * 0.48)
    c1, c2 = int(W * 0.36), int(W * 0.69)

    rear_wave = grade_lake_ripple_tone(crop_tight(clean_small_islands(rgba.crop((c1, 0, c2, mid_y)), 250), pad=6), luma_scale=0.90)
    spray_plume = grade_lake_ripple_tone(crop_tight(clean_small_islands(rgba.crop((c2, 0, W, mid_y)), 120), pad=6), luma_scale=0.92)

    b_c1, b_c2 = int(W * 0.35), int(W * 0.69)
    crest_l_raw = crop_tight(clean_small_islands(rgba.crop((0, mid_y, b_c1, H)), 200), pad=6)
    crest_l = grade_lake_ripple_tone(crest_l_raw.transpose(Image.Transpose.FLIP_LEFT_RIGHT), luma_scale=0.90)
    crest_r = grade_lake_ripple_tone(crop_tight(clean_small_islands(rgba.crop((b_c1, mid_y, b_c2, H)), 200), pad=6), luma_scale=0.90)

    drops_region = rgba.crop((b_c2, mid_y, W, H))
    drop_comps = extract_components(drops_region, min_area=180, merge_dilate=14)
    drop_comps.sort(key=lambda t: -(t[1][2] * t[1][3]))
    drop_comps = drop_comps[:4]
    drop_comps.sort(key=lambda t: (t[1][1] // 140, t[1][0]))

    out = {
        "meniscus_back": (men_back, (0, 0)),
        "meniscus_front": (men_front, (0, 0)),
        "ripple_ring": (ripple_a_fit, (0, 0)),
        "ripple_back": (rip_a_back, (0, 0)),
        "ripple_front": (rip_a_front, (0, 0)),
        "ripple_b_ring": (ripple_b_fit, (0, 0)),
        "ripple_b_back": (rip_b_back, (0, 0)),
        "ripple_b_front": (rip_b_front, (0, 0)),
        "water_glint": (glint_fit, (0, 0)),
        "rear_wave": (fit_w(rear_wave, 270), (0, 0)),
        "spray_plume": (fit_w(spray_plume, 230), (0, 0)),
        "crest_l": (fit_w(crest_l, 220), (0, 0)),
        "crest_r": (fit_w(crest_r, 220), (0, 0)),
    }
    for i, (dim, _) in enumerate(drop_comps, 1):
        out[f"drop_{i}"] = (fit_w(grade_lake_ripple_tone(dim, luma_scale=0.92), 62), (0, 0))
    print("WATER_VFX_PARTS:", {k: v[0].size for k, v in out.items()})
    return out


def match_bridge_basalt_tone(im: Image.Image, bridge_rgb_med: np.ndarray, *, bevel_lift: float = 1.0, soft_outer_fade: bool = False) -> Image.Image:
    """Calibrate fractured basalt breach stones 1:1 onto the actual dark wet blue-teal bridge floor palette!"""
    arr = np.array(im.convert("RGBA"), dtype=np.float32)
    alpha = arr[:, :, 3]
    mask = alpha > 200
    if not mask.any():
        return im
    rgb = arr[:, :, :3]
    # Measure non-pit stone body (exclude pitch-black chasm core luma < 24)
    lum = 0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]
    stone_mask = mask & (lum > 24.0)
    if stone_mask.any():
        src_med = np.median(rgb[stone_mask], axis=0)  # [R, G, B]
        target_med = bridge_rgb_med * bevel_lift
        # Blend 82% toward exact bridge deck color & value while preserving crisp bevel & dark crevice contrast!
        scale_rgb = np.clip(target_med / np.maximum(src_med, 1.0), 0.42, 1.18)
        eff_scale = 0.18 + 0.82 * scale_rgb
        rgb_graded = np.clip(rgb * eff_scale[None, None, :], 0.0, 255.0)
        # Keep crisp damp bevel highlights on torn basalt edges so upturned shards read cleanly against flat slabs
        lum_g = 0.299 * rgb_graded[:, :, 0] + 0.587 * rgb_graded[:, :, 1] + 0.114 * rgb_graded[:, :, 2]
        hi = np.clip((lum_g - 28.0) / 34.0, 0.0, 1.0)[:, :, None]
        rgb_graded = np.clip(rgb_graded + hi * np.array([12.0, 19.0, 23.0], dtype=np.float32)[None, None, :], 0.0, 255.0)
        arr[:, :, :3] = rgb_graded

    if soft_outer_fade:
        # Softly taper the outermost 7px rim of the floor fissure crater so jagged slabs melt naturally into the deck!
        dist = cv2.distanceTransform((alpha > 16).astype(np.uint8), cv2.DIST_L2, 5)
        rim_fade = np.clip(dist / 7.0, 0.0, 1.0)
        arr[:, :, 3] = np.clip(alpha * (0.28 + 0.72 * rim_fade), 0.0, 255.0)

    return Image.fromarray(np.clip(np.round(arr), 0, 255).astype(np.uint8), mode="RGBA")


def extract_breach_vfx(bridge_plate: Image.Image) -> dict[str, tuple[Image.Image, tuple[int, int]]]:
    """Extract bridge-matched dark wet blue-teal basalt fissure crater, 4 thin upturned slabs, 5 chips & dark dust."""
    # Sample actual flat bridge floor stone deck (y: 660..820 in 2048x960) for exact 1:1 tone lock!
    b_arr = np.array(bridge_plate.convert("RGBA"), dtype=np.float32)
    deck_slice = b_arr[660:820, 220:1820]
    deck_valid = deck_slice[:, :, 3] > 200
    bridge_rgb_med = np.median(deck_slice[deck_valid, :3], axis=0)
    print(f"BRIDGE_DECK_MEDIAN_RGB: ({bridge_rgb_med[0]:.1f}, {bridge_rgb_med[1]:.1f}, {bridge_rgb_med[2]:.1f})")

    raw = Image.open(RAW_FILES["vfx_breach"]).convert("RGB")
    rgba_crisp = extract_full_chroma_green_rgba(raw)
    rgba_soft = extract_soft_green_rgba(raw)
    W, H = rgba_crisp.size
    mid_y = int(H * 0.52)

    crater = crop_tight(clean_small_islands(rgba_crisp.crop((0, 0, int(W * 0.50), mid_y)), 400), pad=6)
    slab_l = crop_tight(clean_small_islands(rgba_crisp.crop((int(W * 0.50), 0, int(W * 0.745), mid_y)), 400), pad=6)
    slab_r = crop_tight(clean_small_islands(rgba_crisp.crop((int(W * 0.745), 0, W, mid_y)), 400), pad=6)

    slab_front = crop_tight(clean_small_islands(rgba_crisp.crop((0, mid_y, int(W * 0.31), H)), 400), pad=6)
    slab_back = crop_tight(clean_small_islands(rgba_crisp.crop((int(W * 0.31), mid_y, int(W * 0.54), H)), 400), pad=6)

    shards_region = rgba_crisp.crop((int(W * 0.54), mid_y, int(W * 0.74), H))
    shard_comps = extract_components(shards_region, min_area=90, merge_dilate=2)
    shard_comps.sort(key=lambda t: -(t[1][2] * t[1][3]))
    shard_comps = shard_comps[:5]

    dust_cloud = crop_tight(clean_small_islands(rgba_soft.crop((int(W * 0.74), mid_y, W, H)), 400), pad=6)

    # Lock every single basalt stone piece strictly onto the real bridge floor palette!
    crater = match_bridge_basalt_tone(crater, bridge_rgb_med, bevel_lift=1.06, soft_outer_fade=True)
    slab_l = match_bridge_basalt_tone(slab_l, bridge_rgb_med, bevel_lift=1.22)
    slab_r = match_bridge_basalt_tone(slab_r, bridge_rgb_med, bevel_lift=1.22)
    slab_front = match_bridge_basalt_tone(slab_front, bridge_rgb_med, bevel_lift=1.18)
    slab_back = match_bridge_basalt_tone(slab_back, bridge_rgb_med, bevel_lift=1.04)
    dust_cloud = match_bridge_basalt_tone(dust_cloud, bridge_rgb_med, bevel_lift=1.24)

    def fit_w(im: Image.Image, target_w: int) -> Image.Image:
        sc = target_w / max(1, im.width)
        return im.resize((target_w, max(1, int(round(im.height * sc)))), Image.Resampling.LANCZOS)

    out = {
        "crater": (fit_w(crater, 340), (0, 0)),
        "slab_l": (fit_w(slab_l, 148), (0, 0)),
        "slab_r": (fit_w(slab_r, 148), (0, 0)),
        "slab_front": (fit_w(slab_front, 168), (0, 0)),
        "slab_back": (fit_w(slab_back, 138), (0, 0)),
        "dust_cloud": (fit_w(dust_cloud, 195), (0, 0)),
    }
    for i, (sim, _) in enumerate(shard_comps, 1):
        sim_m = match_bridge_basalt_tone(sim, bridge_rgb_med, bevel_lift=1.15)
        out[f"shard_{i}"] = (fit_w(sim_m, max(30, min(56, int(sim.width * 0.42)))), (0, 0))
    print("BREACH_VFX_PARTS:", {k: v[0].size for k, v in out.items()})
    return out


def extract_ink_vfx() -> dict[str, tuple[Image.Image, tuple[int, int]]]:
    """
    Extract our brand-new HIGH-PRESSURE CYLINDRICAL HYDRO-INK CANNON COLUMN (extended seamlessly to ~1180px
    tall so it blasts completely OFF THE BOTTOM EDGE OF THE 1080p SCREEN at every sweep angle!),
    plus swirling nozzle vortex ring, foaming shockwave rings, radial impact burst crown, and high-speed ink streaks!
    """
    raw = Image.open(RAW_FILES["vfx_ink_cannon"]).convert("RGB")
    rgba = extract_soft_green_rgba(raw)
    W, H = rgba.size
    split_x = int(W * 0.515)

    # 1) LEFT HALF: Pure unbroken high-pressure cylindrical hydro-ink column!
    col_raw = crop_tight(clean_small_islands(rgba.crop((0, 0, split_x, H)), 180), pad=4)
    # Trim the sharp spear needle at the top (top 9%) and the bottom smoke-puff base (bottom 17%)
    cw_r, ch_r = col_raw.size
    col_core = col_raw.crop((0, int(ch_r * 0.095), cw_r, int(ch_r * 0.835)))
    # Narrow canvas cleanly around the central cylindrical shaft (discard wide bottom smoke side-wings)
    col_core = crop_tight(col_core, pad=8)
    # Scale cylinder to clean ~240px width (~700px tall natural segment), then cross-fade tile the mid-shaft
    # to produce a seamless 1180px-tall relentless cylindrical column that rockets off-screen at ±46°!
    seg_sc = 240.0 / max(1, col_core.width)
    seg_w = int(round(col_core.width * seg_sc))
    seg_h = int(round(col_core.height * seg_sc))
    seg_im = col_core.resize((seg_w, seg_h), Image.Resampling.LANCZOS)
    seg_arr = np.array(seg_im, dtype=np.float32)

    # Take top nozzle-to-shaft [0 : seg_h] + seamless lower shaft extension [int(seg_h*0.32) : seg_h] with 90px blend!
    ext_start = int(seg_h * 0.30)
    ext_arr = seg_arr[ext_start:, :, :]
    overlap = 96
    out_h = seg_h + ext_arr.shape[0] - overlap
    col_out = np.zeros((out_h, seg_w, 4), dtype=np.float32)
    col_out[:seg_h, :, :] = seg_arr
    # Smooth linear alpha cross-fade in the overlap region
    y_ov = seg_h - overlap
    w_blend = np.linspace(0.0, 1.0, overlap, dtype=np.float32)[:, None, None]
    col_out[y_ov:seg_h, :, :] = seg_arr[y_ov:seg_h, :, :] * (1.0 - w_blend) + ext_arr[:overlap, :, :] * w_blend
    col_out[seg_h:, :, :] = ext_arr[overlap:, :, :]
    beam_column = Image.fromarray(np.clip(col_out, 0, 255).astype(np.uint8), mode="RGBA")
    # Ensure exact 1180px height
    if beam_column.height < 1180:
        beam_column = beam_column.resize((beam_column.width, 1180), Image.Resampling.LANCZOS)

    # 2) RIGHT HALF: 4 cleanly stacked VFX rows!
    right_half = rgba.crop((split_x, 0, W, H))
    rw, rh = right_half.size
    nozzle_ring = crop_tight(clean_small_islands(right_half.crop((0, 0, rw, int(rh * 0.24))), 150), pad=6)
    shock_ring = crop_tight(clean_small_islands(right_half.crop((0, int(rh * 0.24), rw, int(rh * 0.49))), 150), pad=6)
    impact_crown = crop_tight(clean_small_islands(right_half.crop((0, int(rh * 0.49), rw, int(rh * 0.75))), 150), pad=6)
    streaks_zone = right_half.crop((int(rw * 0.14), int(rh * 0.75), rw, rh))
    streak_comps = extract_components(streaks_zone, min_area=120, merge_dilate=10)
    streak_comps.sort(key=lambda t: t[1][0])  # left-to-right

    def fit_w(im: Image.Image, target_w: int) -> Image.Image:
        sc = target_w / max(1, im.width)
        return im.resize((target_w, max(1, int(round(im.height * sc)))), Image.Resampling.LANCZOS)

    out = {
        "beam_column": (beam_column, (0, 0)),
        "nozzle_ring": (fit_w(nozzle_ring, 210), (0, 0)),
        "shock_ring": (fit_w(shock_ring, 260), (0, 0)),
        "impact_crown": (fit_w(impact_crown, 340), (0, 0)),
    }
    for i, (sim, _) in enumerate(streak_comps[:3], 1):
        out[f"streak_{i}"] = (fit_w(sim, 68 if i < 3 else 82), (0, 0))
    print("INK_CANNON_VFX_PARTS:", {k: v[0].size for k, v in out.items()})
    return out


def extract_full_chroma_green_rgba(img: Image.Image) -> Image.Image:
    """Full-canvas chroma-green extraction that ALSO cleanly removes interior green holes (rings, cracks, crevices)."""
    from scipy import ndimage

    arr = np.array(img.convert("RGB"), dtype=np.float32)
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    excess = g - np.maximum(r, b)
    bg_sure = (excess > 52.0) & (g > 125.0)
    near_bg = ndimage.binary_dilation(bg_sure, iterations=5)

    t = np.clip((52.0 - excess) / (52.0 - 12.0), 0.0, 1.0)
    alpha = np.where(near_bg, t, 1.0)
    alpha = np.where(bg_sure, 0.0, alpha)

    pure_fg = (alpha >= 0.99) & (~ndimage.binary_dilation(alpha < 0.92, iterations=3))
    out_rgb = arr.copy()
    if pure_fg.any():
        _, (iy, ix) = ndimage.distance_transform_edt(~pure_fg, return_indices=True)
        decontam = (alpha > 0.0) & (~pure_fg)
        rim_blend = np.clip((excess - 4.0) / 14.0, 0.0, 1.0)
        rim_blend = np.where(alpha < 0.96, 1.0, rim_blend)
        for c in range(3):
            out_rgb[:, :, c] = np.where(
                decontam,
                out_rgb[:, :, c] * (1.0 - rim_blend) + arr[iy, ix, c] * rim_blend,
                out_rgb[:, :, c],
            )
    # Suppress any lingering green tint on semi-transparent boundary pixels
    out_rgb[:, :, 1] = np.where(near_bg, np.minimum(out_rgb[:, :, 1], np.maximum(out_rgb[:, :, 0], out_rgb[:, :, 2]) + 10.0), out_rgb[:, :, 1])

    rgba = np.zeros((arr.shape[0], arr.shape[1], 4), dtype=np.uint8)
    rgba[:, :, :3] = np.clip(np.round(out_rgb), 0, 255).astype(np.uint8)
    rgba[:, :, 3] = np.clip(np.round(alpha * 255.0), 0, 255).astype(np.uint8)
    rgba[rgba[:, :, 3] < 6, :] = 0
    return Image.fromarray(rgba, mode="RGBA")


def extract_slam_vfx(bridge_plate: Image.Image) -> dict[str, tuple[Image.Image, tuple[int, int]]]:
    """Extract all 9 refined Bridge-Matched Dark Basalt Slam Impact VFX pieces (flush sunken basalt crater + sea-mist shockwave!)."""
    b_arr = np.array(bridge_plate.convert("RGBA"), dtype=np.float32)
    deck_slice = b_arr[600:860, 220:1820]
    deck_valid = deck_slice[:, :, 3] > 200
    bridge_rgb_med = np.median(deck_slice[deck_valid, :3], axis=0)

    raw = Image.open(RAW_FILES["vfx_slam"]).convert("RGB")
    rgba = extract_full_chroma_green_rgba(raw)
    W, H = rgba.size
    mid_y = int(H * 0.50)

    shockwave_ring = crop_tight(clean_small_islands(rgba.crop((0, 0, int(W * 0.54), mid_y)), 180), pad=6)
    impact_cracks = crop_tight(clean_small_islands(rgba.crop((int(W * 0.54), 0, W, mid_y)), 120), pad=6)

    dust_l = crop_tight(clean_small_islands(rgba.crop((0, mid_y, int(W * 0.28), H)), 250), pad=6)
    dust_r = crop_tight(clean_small_islands(rgba.crop((int(W * 0.28), mid_y, int(W * 0.55), H)), 250), pad=6)

    chips_region = rgba.crop((int(W * 0.55), mid_y, int(W * 0.75), H))
    chip_comps = extract_components(chips_region, min_area=120, merge_dilate=2)
    chip_comps.sort(key=lambda t: -(t[1][2] * t[1][3]))
    chip_comps = chip_comps[:4]

    ink_slash = crop_tight(clean_small_islands(rgba.crop((int(W * 0.74), mid_y, W, H)), 60), pad=6)

    # Lock sunken basalt slam crater, stone splinters & dust onto the exact dark wet blue-teal bridge floor!
    impact_cracks = match_bridge_basalt_tone(impact_cracks, bridge_rgb_med, bevel_lift=1.12, soft_outer_fade=True)
    dust_l = match_bridge_basalt_tone(dust_l, bridge_rgb_med, bevel_lift=1.35)
    dust_r = match_bridge_basalt_tone(dust_r, bridge_rgb_med, bevel_lift=1.35)

    def fit_w(im: Image.Image, target_w: int) -> Image.Image:
        sc = target_w / max(1, im.width)
        return im.resize((target_w, max(1, int(round(im.height * sc)))), Image.Resampling.LANCZOS)

    out: dict[str, tuple[Image.Image, tuple[int, int]]] = {
        "shockwave_ring": (fit_w(shockwave_ring, 430), (0, 0)),
        "impact_cracks": (fit_w(impact_cracks, 360), (0, 0)),
        "dust_l": (fit_w(dust_l, 225), (0, 0)),
        "dust_r": (fit_w(dust_r, 225), (0, 0)),
        "ink_slash": (fit_w(ink_slash, 270), (0, 0)),
    }
    for i, (cim, _) in enumerate(chip_comps, 1):
        cim_m = match_bridge_basalt_tone(cim, bridge_rgb_med, bevel_lift=1.22)
        out[f"chip_{i}"] = (fit_w(cim_m, max(32, min(54, int(cim.width * 0.44)))), (0, 0))
    print("SLAM_VFX_PARTS:", {k: v[0].size for k, v in out.items()})
    return out


def build_cyan_biolum_glow(im: Image.Image) -> Image.Image:
    """Auto-extract bright glowing sea-cyan blisters/veins/bulb for additive Spine glow overlays."""
    arr = np.array(im.convert("RGBA"), dtype=np.uint8)
    r, g, b, a = arr[:, :, 0].astype(np.float32), arr[:, :, 1].astype(np.float32), arr[:, :, 2].astype(np.float32), arr[:, :, 3]
    cyan_mask = (a > 180) & (g > 145) & (b > 145) & ((g + b) * 0.5 - r > 32)
    glow_arr = np.zeros_like(arr)
    glow_arr[cyan_mask, 0] = 95
    glow_arr[cyan_mask, 1] = 242
    glow_arr[cyan_mask, 2] = 238
    glow_arr[cyan_mask, 3] = 225
    glow = Image.fromarray(glow_arr, mode="RGBA").filter(ImageFilter.GaussianBlur(radius=7))
    # Keep glow strictly inside dilated silhouette so zero stray halo floats off edge
    sil = cv2.dilate((a > 40).astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
    ga = np.array(glow, dtype=np.uint8)
    ga[:, :, 3] = np.where(sil, ga[:, :, 3], 0)
    return Image.fromarray(ga, mode="RGBA")


def build_shell_plate_sheen(im: Image.Image) -> Image.Image:
    """Auto-extract pale weathered stone/ochre shield plates for defensive guard flash sheen."""
    arr = np.array(im.convert("RGBA"), dtype=np.uint8)
    r, g, b, a = arr[:, :, 0].astype(np.float32), arr[:, :, 1].astype(np.float32), arr[:, :, 2].astype(np.float32), arr[:, :, 3]
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    plate_mask = (a > 200) & (lum > 118)
    sheen = np.zeros_like(arr)
    sheen[plate_mask, 0] = 238
    sheen[plate_mask, 1] = 228
    sheen[plate_mask, 2] = 175
    sheen[plate_mask, 3] = 215
    return Image.fromarray(sheen, mode="RGBA").filter(ImageFilter.GaussianBlur(radius=5))


def normalize_tentacle_sprite(src_path: Path, target_h: int) -> Image.Image:
    """Cleanly key out #00FF00 (including any faint edge tint), crop tight, and scale to target_h."""
    raw = Image.open(src_path).convert("RGB")
    rgba = extract_full_chroma_green_rgba(raw)
    rgba = crop_tight(clean_small_islands(rgba, min_area=900), pad=8)
    sc = target_h / max(1, rgba.height)
    nw = max(1, int(round(rgba.width * sc)))
    return rgba.resize((nw, target_h), Image.Resampling.LANCZOS)


def save_parts_dir(out_dir: Path, named_parts: dict[str, tuple[Image.Image, tuple[int, int]]], extra_meta: dict | None = None) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    meta = {}
    for name, (im, (x, y)) in named_parts.items():
        im.save(out_dir / f"{name}.png", optimize=True)
        meta[name] = {"x": int(x), "y": int(y), "w": im.width, "h": im.height}
    if extra_meta:
        meta["_extra"] = extra_meta
    (out_dir / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")


def main() -> None:
    MASTERS_DIR.mkdir(parents=True, exist_ok=True)
    for key, src in RAW_FILES.items():
        shutil.copy2(src, MASTERS_DIR / f"{key}_master.jpg")

    bg_plate, bridge_plate, fg_plate = prepare_bg_plates()

    body_rgba = extract_green_rgba(Image.open(RAW_FILES["body"]))
    body_parts, body_extra = dissect_body_parts(body_rgba)

    # Redesigned tall, upright, slam-ready Hero Tentacles (v2)!
    crushing_scaled = normalize_tentacle_sprite(RAW_FILES["hero_crushing_v2"], target_h=840)
    ink_scaled = normalize_tentacle_sprite(RAW_FILES["hero_ink_v2"], target_h=840)
    ink_glow_im = build_cyan_biolum_glow(ink_scaled)

    # 3 Distinct Small Tentacle Variants normalized onto a unified 540px-tall upright strike frame!
    small_spike = normalize_tentacle_sprite(RAW_FILES["small_spike"], target_h=540)
    small_shell = normalize_tentacle_sprite(RAW_FILES["small_shell"], target_h=540)
    small_gland = normalize_tentacle_sprite(RAW_FILES["small_gland"], target_h=540)
    small_gland_glow = build_cyan_biolum_glow(small_gland)
    small_shell_sheen = build_shell_plate_sheen(small_shell)

    bg_t_rgba = extract_green_rgba(Image.open(RAW_FILES["bg_tentacles"]))
    bg_tall_im, bg_outer_im = split_two_halves(bg_t_rgba)

    water_vfx = extract_water_vfx()
    breach_vfx = extract_breach_vfx(bridge_plate)
    ink_vfx = extract_ink_vfx()
    slam_vfx = extract_slam_vfx(bridge_plate)

    body_parts["bg_tentacle_tall"] = (bg_tall_im, (0, 0))
    body_parts["bg_tentacle_outer"] = (bg_outer_im, (0, 0))
    save_parts_dir(RIG_ROOT / "parts_body", body_parts, extra_meta=body_extra)
    save_parts_dir(RIG_ROOT / "parts_vfx_water", water_vfx)
    save_parts_dir(RIG_ROOT / "parts_vfx_breach", breach_vfx)
    save_parts_dir(RIG_ROOT / "parts_vfx_ink", ink_vfx)
    save_parts_dir(RIG_ROOT / "parts_vfx_slam", slam_vfx)

    save_parts_dir(RIG_ROOT / "parts_crushing", {"tentacle": (crushing_scaled, (0, 0))})
    save_parts_dir(RIG_ROOT / "parts_ink", {"tentacle": (ink_scaled, (0, 0)), "ink_blister_glow": (ink_glow_im, (0, 0))})
    save_parts_dir(RIG_ROOT / "parts_small", {
        "spike": (small_spike, (0, 0)),
        "shell": (small_shell, (0, 0)),
        "gland": (small_gland, (0, 0)),
        "gland_glow": (small_gland_glow, (0, 0)),
        "shell_sheen": (small_shell_sheen, (0, 0)),
    })

    # Render dedicated v2 Tentacles + Slam VFX Parts Audit Sheet (checkerboard + dark UI)
    t_sheet = Image.new("RGB", (1840, 960), (20, 24, 34))
    td = ImageDraw.Draw(t_sheet)

    def paste_t(im: Image.Image, box: tuple[int, int, int, int], label: str, bg_col=None):
        x0, y0, w, h = box
        if bg_col is None:
            chk = Image.new("RGBA", (w, h))
            cd = ImageDraw.Draw(chk)
            for yy in range(0, h, 16):
                for xx in range(0, w, 16):
                    c = (205, 210, 216, 255) if ((xx // 16 + yy // 16) % 2 == 0) else (162, 168, 176, 255)
                    cd.rectangle((xx, yy, xx + 16, yy + 16), fill=c)
        else:
            chk = Image.new("RGBA", (w, h), bg_col + (255,))
        sc = min((w - 16) / max(1, im.width), (h - 28) / max(1, im.height))
        rw, rh = max(1, int(round(im.width * sc))), max(1, int(round(im.height * sc)))
        r_im = im.resize((rw, rh), Image.Resampling.LANCZOS)
        chk.paste(r_im, ((w - rw) // 2, (h - rh) // 2 + 8), r_im)
        t_sheet.paste(chk.convert("RGB"), (x0, y0))
        td.rectangle((x0, y0, x0 + w, y0 + h), outline=(85, 105, 128), width=2)
        td.text((x0 + 8, y0 + 5), label, fill=(235, 242, 250))

    paste_t(crushing_scaled, (20, 20, 340, 440), "Hero Crushing v2 (Left)")
    paste_t(ink_scaled, (380, 20, 340, 440), "Hero Ink Whip v2 (Right)")
    paste_t(small_spike, (740, 20, 340, 440), "Small Variant 1: Spike (Attack)")
    paste_t(small_shell, (1100, 20, 340, 440), "Small Variant 2: Shell (Guard)")
    paste_t(small_gland, (1460, 20, 360, 440), "Small Variant 3: Gland (Buff)")

    # Bottom row: all 9 slam VFX pieces on checkerboard + dark preview
    slam_keys = list(slam_vfx.keys())
    for idx_s, sk in enumerate(slam_keys[:9]):
        bx = 20 + idx_s * 200
        paste_t(slam_vfx[sk][0], (bx, 480, 190, 220), sk)
        paste_t(slam_vfx[sk][0], (bx, 715, 190, 220), sk + " (dark)", bg_col=(18, 22, 32))

    t_audit_path = ART_DIR / "abyssal_tentacles_v2_parts_audit.jpg"
    t_sheet.save(t_audit_path, quality=92)
    print("TENTACLES_V2_AUDIT:", t_audit_path)

    # Render comprehensive Parts + Hand-Painted VFX Dissection Audit Sheet
    sheet = Image.new("RGB", (1840, 1020), (20, 24, 34))
    d = ImageDraw.Draw(sheet)

    def paste_tile(im: Image.Image, box: tuple[int, int, int, int], label: str, bg_col=None):
        x0, y0, w, h = box
        if bg_col is None:
            chk = Image.new("RGBA", (w, h))
            cd = ImageDraw.Draw(chk)
            for yy in range(0, h, 16):
                for xx in range(0, w, 16):
                    c = (205, 210, 216, 255) if ((xx // 16 + yy // 16) % 2 == 0) else (162, 168, 176, 255)
                    cd.rectangle((xx, yy, xx + 16, yy + 16), fill=c)
        else:
            chk = Image.new("RGBA", (w, h), bg_col + (255,))
        sc = min((w - 16) / max(1, im.width), (h - 28) / max(1, im.height))
        rw, rh = max(1, int(round(im.width * sc))), max(1, int(round(im.height * sc)))
        r_im = im.resize((rw, rh), Image.Resampling.LANCZOS)
        chk.paste(r_im, ((w - rw) // 2, (h - rh) // 2 + 8), r_im)
        sheet.paste(chk.convert("RGB"), (x0, y0))
        d.rectangle((x0, y0, x0 + w, y0 + h), outline=(85, 105, 128), width=2)
        d.text((x0 + 8, y0 + 5), label, fill=(235, 242, 250))

    # Reconstruct full submerged torso with pupils + hardware clip polygons drawn on preview!
    body_recon = body_parts["head_mantle"][0].copy()
    for p_name in ("pupil_l", "pupil_r"):
        pim, (px, py) = body_parts[p_name]
        body_recon.alpha_composite(pim, (px, py))
    # Draw eye-socket clip polygons gently on a second copy to verify exact alignment
    body_clip_dbg = body_recon.copy()
    cdbg = ImageDraw.Draw(body_clip_dbg)
    for tag in ("l", "r"):
        poly = [tuple(p) for p in body_extra["eye_clips"][tag]]
        cdbg.polygon(poly, outline=(80, 255, 220, 255))

    paste_tile(body_recon, (20, 20, 560, 440), "1. Full Submerged Kraken Torso (Clean RGBA)")
    paste_tile(body_clip_dbg, (600, 20, 560, 440), "2. Eye-Socket Clip Polygons + Phase 2 Glow", bg_col=(18, 24, 34))
    paste_tile(body_parts["ink_glow"][0], (1180, 20, 310, 440), "3. Auto Crown+Shoulder Glow", bg_col=(14, 18, 28))
    paste_tile(ink_vfx["beam_column"][0], (1510, 20, 310, 440), "4. Off-Screen Cylindrical Ink Column", bg_col=(24, 28, 40))

    # Row 2: Hand-painted Water Splash, Stone Breach & Ink VFX mini-galleries!
    def pack_gallery(named: dict[str, tuple[Image.Image, tuple[int, int]]], cols: int = 3) -> Image.Image:
        keys = list(named.keys())
        rows = math.ceil(len(keys) / cols)
        cw, ch = 175, 145
        g = Image.new("RGBA", (cols * cw, rows * ch), (20, 26, 36, 255))
        gd = ImageDraw.Draw(g)
        for idx, k in enumerate(keys):
            im = named[k][0]
            sc = min((cw - 14) / max(1, im.width), (ch - 26) / max(1, im.height))
            rw, rh = max(1, int(round(im.width * sc))), max(1, int(round(im.height * sc)))
            r_im = im.resize((rw, rh), Image.Resampling.LANCZOS)
            gx, gy = (idx % cols) * cw, (idx // cols) * ch
            g.paste(r_im, (gx + (cw - rw) // 2, gy + (ch - rh) // 2 + 8), r_im)
            gd.text((gx + 6, gy + 4), k, fill=(185, 225, 230, 255))
        return g

    paste_tile(pack_gallery(water_vfx, 4), (20, 480, 580, 520), "5. Refined Lake Ripples, Meniscus & Splash Pieces", bg_col=(11, 30, 39))
    paste_tile(pack_gallery(breach_vfx, 4), (620, 480, 600, 520), "6. Hand-Painted Stone Breach + Hinged Slabs + Shards (11 parts)", bg_col=(16, 22, 32))
    paste_tile(pack_gallery(ink_vfx, 3), (1240, 480, 580, 520), "7. Hand-Painted Ink Jet + Billows + 6 Drops (9 parts)", bg_col=(16, 22, 32))

    audit_path = ART_DIR / "abyssal_inkmaw_parts_audit.jpg"
    sheet.save(audit_path, quality=92)
    print("AUDIT_SHEET:", audit_path)


if __name__ == "__main__":
    main()
