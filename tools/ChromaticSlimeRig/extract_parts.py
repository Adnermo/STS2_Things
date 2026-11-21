"""Multi-Layer Vanilla-STS2 Gouache Part Decomposition for Chromatic Slime Family:
- Large (1120x720, ORIGIN=(560, 650)):
    shadow, body_dome (with negative-space dripping tendril arches & solid dark eye sockets),
    inner_gel (opaque-gouache middle slime parallax layer), prism_glow (subtle opalescent sheen),
    core_crimson (100% rigid crystal cluster), core_amber + core_amber_chips (100% rigid stone slabs),
    azure_bubbles (effervescent seafoam bubbles),
    iris_top/mid/low + lid_top/mid/low (unscaled sx=1, sy=1!) + eye_rim_mask (almond socket mask).
- Medium (440x420, ORIGIN=(220, 386), skins: crimson, amber, azure):
    shadow (shared), <skin>_crest_back, <skin>_body, <skin>_inner_blob, <skin>_aura,
    <skin>_pupils, <skin>_eyelids (unscaled sx=1, sy=1!), <skin>_eye_rim, <skin>_crest_front.
- Small (380x320, ORIGIN=(190, 290), skins: crimson, amber, azure):
    shadow (shared), <skin>_body, <skin>_inner_blob, <skin>_aura,
    <skin>_pupils, <skin>_eyelids (unscaled sx=1, sy=1!), <skin>_eye_rim, <skin>_crest.
"""
from __future__ import annotations

import json
import shutil
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SRC_DIR = HERE.parents[1] / "source_assets" / "monsters" / "chromatic_slime"
AUDIT_OUT = Path(r"C:\Users\adner\.gemini\antigravity\brain\732928de-fcd4-425a-8915-c7ad2bcbf79e\scratch\sts2_slime_parts_audit.jpg")


def poly_mask(pts: list[tuple[float, float]], shape: tuple[int, int], feather: float = 0.0) -> np.ndarray:
    m = np.zeros(shape, dtype=np.float32)
    cv2.fillPoly(m, [np.array(pts, dtype=np.int32)], 1.0)
    if feather > 0:
        m = cv2.GaussianBlur(m, (0, 0), feather)
    return m


def make_chalk_shadow(w: int, h: int, max_alpha: float = 112.0) -> Image.Image:
    """Authentic STS2 dry-brush chalky charcoal ground shadow (matches leaf_slime_m & slimed_berserker)."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    rx, ry = w * 0.46, h * 0.39
    cx, cy = w * 0.5, h * 0.5
    d = np.hypot((x - cx) / rx, (y - cy) / ry)
    rng = np.random.default_rng(107)
    # Multi-scale chalk tooth noise along outer shadow edge
    n1 = cv2.GaussianBlur(rng.normal(0.0, 1.0, (h, w)).astype(np.float32), (0, 0), 2.2) * 0.14
    n2 = cv2.GaussianBlur(rng.normal(0.0, 1.0, (h, w)).astype(np.float32), (0, 0), 0.9) * 0.06
    d_noisy = d + n1 + n2
    alpha = np.clip(1.0 - np.clip((d_noisy - 0.32) / 0.66, 0.0, 1.0) ** 1.45, 0.0, 1.0) * max_alpha
    alpha[d > 0.99] = 0.0
    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[..., 0] = 16
    out[..., 1] = 19
    out[..., 2] = 26
    out[..., 3] = np.clip(np.round(alpha), 0, 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def save_part(dest_dir: Path, name: str, rgba: np.ndarray, meta: dict, mask: np.ndarray | None = None, pad: int = 5) -> None:
    arr = rgba.copy()
    if mask is not None:
        arr[..., 3] = np.clip(np.round(arr[..., 3].astype(np.float32) * mask), 0, 255).astype(np.uint8)
    ys, xs = np.where(arr[..., 3] > 2)
    if len(xs) == 0:
        raise ValueError(f"Empty extracted part: {name}")
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(arr.shape[1], int(xs.max()) + 1 + pad)
    y1 = min(arr.shape[0], int(ys.max()) + 1 + pad)
    crop = arr[y0:y1, x0:x1]
    Image.fromarray(crop, "RGBA").save(dest_dir / f"{name}.png")
    meta[name] = {"x": x0, "y": y0, "w": int(x1 - x0), "h": int(y1 - y0)}


def inpaint_gel(base_rgba: np.ndarray, hole_mask: np.ndarray, dilate_px: int = 7) -> np.ndarray:
    """Inpaint smooth gouache gel underneath extracted rigid crystals/stones without dark edge bleed."""
    out = base_rgba.copy()
    hole = cv2.dilate((hole_mask > 0.15).astype(np.uint8), np.ones((dilate_px, dilate_px), np.uint8)) > 0
    opaque = out[..., 3] > 180
    hole &= (out[..., 3] > 15)
    if not hole.any():
        return out
    valid = opaque & (~hole)
    med_col = np.median(out[valid, :3], axis=0).astype(np.uint8) if valid.any() else np.array([75, 84, 98], np.uint8)
    rgb = out[..., :3].copy()
    rgb[~opaque] = med_col
    rep = cv2.inpaint(rgb, hole.astype(np.uint8) * 255, 11, cv2.INPAINT_TELEA)
    soft = cv2.GaussianBlur(rep, (7, 7), 0)
    blend = cv2.GaussianBlur(hole.astype(np.float32), (0, 0), 1.8)[..., None]
    out[..., :3] = np.clip(out[..., :3].astype(np.float32) * (1.0 - blend) + soft.astype(np.float32) * blend, 0, 255).astype(np.uint8)
    return out


def make_inner_blob(body_rgba: np.ndarray, tint_boost: tuple[float, float, float], shrink_px: int, alpha_mul: float = 0.42) -> np.ndarray:
    """Synthesize Mega Crit style nested inner gouache blob (`middleblob` / `middle slime`) inside body."""
    h, w = body_rgba.shape[:2]
    solid = (body_rgba[..., 3] > 200).astype(np.uint8)
    dist = cv2.distanceTransform(solid, cv2.DIST_L2, 5)
    rng = np.random.default_rng(73)
    tooth = cv2.GaussianBlur(rng.normal(0.0, 1.0, (h, w)).astype(np.float32), (0, 0), 3.0) * 3.2
    # Keep away from outer rim & bottom puddle rim so inner blob floats volumetrically in the core
    yy, _ = np.mgrid[0:h, 0:w]
    ys_s = np.where(solid > 0)[0]
    y_bot = float(ys_s.max()) if len(ys_s) else h
    bot_fade = np.clip((y_bot - 18.0 - yy) / 28.0, 0.0, 1.0)
    core_m = np.clip((dist + tooth - shrink_px) / 7.5, 0.0, 1.0) * bot_fade
    out = np.zeros_like(body_rgba)
    base_f = body_rgba[..., :3].astype(np.float32)
    out[..., 0] = np.clip(base_f[..., 0] * tint_boost[0] + 14.0, 0, 255).astype(np.uint8)
    out[..., 1] = np.clip(base_f[..., 1] * tint_boost[1] + 16.0, 0, 255).astype(np.uint8)
    out[..., 2] = np.clip(base_f[..., 2] * tint_boost[2] + 14.0, 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(core_m * 255.0 * alpha_mul, 0, 255).astype(np.uint8)
    return out


# ==============================================================================
# 1. LARGE CHROMATIC SLIME (1120 x 720, ORIGIN = (560, 650))
# ==============================================================================
def build_large_parts() -> None:
    pdir = HERE / "large" / "parts"
    if pdir.exists():
        shutil.rmtree(pdir)
    pdir.mkdir(parents=True, exist_ok=True)

    W, H = 1120, 720
    ox, oy = 4, 4
    src_im = Image.open(SRC_DIR / "sts2_large_cut.png").convert("RGBA")
    raw = np.zeros((H, W, 4), dtype=np.uint8)
    raw[oy : oy + src_im.height, ox : ox + src_im.width] = np.array(src_im)

    meta: dict[str, dict[str, int]] = {}
    rgb = raw[..., :3].astype(np.float32)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    a_src = raw[..., 3].astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:H, 0:W]

    # 1a. Ground shadow centered at ORIGIN=(560, 650)
    sh = make_chalk_shadow(1020, 116, max_alpha=108.0)
    sh.save(pdir / "shadow.png")
    meta["shadow"] = {"x": 560 - 510, "y": 650 - 54, "w": 1020, "h": 116}

    # 1b. Rigid Crimson Garnet Crystal Cluster (`core_crimson`) — upper-left lobe
    crim_zone = poly_mask(
        [(194, 70), (268, 76), (348, 138), (372, 188), (354, 276), (272, 304), (218, 254), (192, 170)],
        (H, W),
    )
    is_crim = (crim_zone > 0.5) & ((r - b) > 22.0) & ((r - g) > 18.0) & (a_src > 0.08)
    crim_m = cv2.dilate(is_crim.astype(np.uint8), np.ones((5, 5), np.uint8), iterations=1).astype(np.float32)
    crim_m = cv2.GaussianBlur(crim_m, (0, 0), 0.85) * (a_src > 0.02)
    save_part(pdir, "core_crimson", raw, meta, mask=crim_m)

    # 1c. Rigid Amber Calcite Slabs (`core_amber` main slab + `core_amber_chips` 5 surrounding stones)
    amb_zone = poly_mask(
        [(488, 338), (648, 336), (732, 412), (722, 535), (618, 578), (492, 530)],
        (H, W),
    )
    is_amb = (amb_zone > 0.5) & ((r - b) > 28.0) & (g > 72.0) & (a_src > 0.2)
    amb_all = cv2.morphologyEx(is_amb.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    main_slab_box = poly_mask([(516, 368), (618, 364), (664, 422), (612, 526), (522, 522)], (H, W)) > 0.5
    m_amb_main = cv2.GaussianBlur(cv2.dilate((amb_all & main_slab_box).astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.float32), (0, 0), 0.75)
    m_amb_chips = cv2.GaussianBlur(cv2.dilate((amb_all & (~main_slab_box)).astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.float32), (0, 0), 0.75)
    save_part(pdir, "core_amber", raw, meta, mask=m_amb_main)
    save_part(pdir, "core_amber_chips", raw, meta, mask=m_amb_chips)

    # 1d. Effervescent Seafoam Acid Bubbles (`azure_bubbles`) on upper-right crater lobe
    az_zone = poly_mask(
        [(672, 48), (795, 48), (885, 155), (898, 275), (765, 285), (675, 185)],
        (H, W),
    )
    # Capture crisp bright seafoam spherical bubbles while leaving crater rim painted on lobe
    is_bub = (az_zone > 0.5) & ((g - r) > 14.0) & (g > 138.0) & (a_src > 0.2)
    bub_m = cv2.GaussianBlur(cv2.dilate(is_bub.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.float32), (0, 0), 0.85) * (a_src > 0.02)
    save_part(pdir, "azure_bubbles", raw, meta, mask=bub_m)

    # 1e. 3 Independent Sunken Hooded Eyes (Unscaled sx=1, sy=1 eyelids + movable ochre irises!)
    # Coordinates in (1120, 720) with (+4, +4) offset applied from grid_new_large.jpg:
    EYE_SPECS = [
        # (tag, almond_hole_pts, iris_center_rx_ry, lid_rgb)
        (
            "top",
            [(424, 116), (442, 104), (468, 104), (488, 117), (468, 129), (442, 128)],
            (456, 116, 17, 10),
            (72, 82, 98),
        ),
        (
            "mid",
            [(508, 166), (528, 157), (556, 162), (572, 176), (550, 185), (524, 181)],
            (541, 171, 17, 10),
            (68, 78, 94),
        ),
        (
            "low",
            [(434, 229), (450, 216), (472, 214), (484, 224), (468, 237), (446, 238)],
            (459, 226, 14, 9),
            (70, 80, 96),
        ),
    ]

    all_holes = np.zeros((H, W), dtype=np.float32)
    rim_zone = np.zeros((H, W), dtype=np.float32)

    for tag, h_pts, (icx, icy, irx, iry), lid_rgb in EYE_SPECS:
        hm = poly_mask(h_pts, (H, W), feather=0.65)
        all_holes = np.maximum(all_holes, hm)
        # Generous feathered patch around eye for eye_rim_mask
        rz = np.exp(-(((xx - icx) / 76.0) ** 4 + ((yy - (icy - 8.0)) / 58.0) ** 4))
        rim_zone = np.maximum(rim_zone, np.clip((rz - 0.08) / 0.32, 0.0, 1.0))

        # Movable flat matte ochre iris (matches authentic SlimedBerserker flat iris!)
        iris = np.zeros((H, W, 4), dtype=np.uint8)
        im_mask = np.zeros((H, W), dtype=np.uint8)
        cv2.ellipse(im_mask, (icx, icy), (irx, iry), 0, 0, 360, 255, -1, lineType=cv2.LINE_AA)
        ia = cv2.GaussianBlur(im_mask.astype(np.float32) / 255.0, (0, 0), 0.6)
        # Subtle gouache ochre shading across iris
        grad_y = np.clip((icy + iry - yy) / max(1.0, 2.0 * iry), 0.0, 1.0)
        iris[..., 0] = np.clip(186 + 34 * grad_y, 0, 255).astype(np.uint8)
        iris[..., 1] = np.clip(142 + 30 * grad_y, 0, 255).astype(np.uint8)
        iris[..., 2] = np.clip(76 + 18 * grad_y, 0, 255).astype(np.uint8)
        iris[..., 3] = np.clip(ia * 255.0, 0, 255).astype(np.uint8)
        save_part(pdir, f"iris_{tag}", iris, meta)

        # Unscaled sliding upper slime-fold eyelid (`lid_top/mid/low`, sx=1, sy=1!)
        # Sits just above the almond opening at rest (dy=0), slides -22px down in Spine space to close!
        lid = np.zeros((H, W, 4), dtype=np.uint8)
        xs_h = [p[0] for p in h_pts]
        ys_h = [p[1] for p in h_pts]
        lx0, lx1 = min(xs_h) - 8, max(xs_h) + 8
        ly_bot = min(ys_h) + 2
        ly_top = ly_bot - 34
        l_poly = [(lx0, ly_top), (lx1, ly_top), (lx1, ly_bot + 1), (lx0, ly_bot + 1)]
        lm = poly_mask(l_poly, (H, W), feather=0.6)
        lid[..., 0] = lid_rgb[0]
        lid[..., 1] = lid_rgb[1]
        lid[..., 2] = lid_rgb[2]
        # Dark lower lid crease so closed blink reads crisply as a slime fold
        crease = (yy >= ly_bot - 2) & (lm > 0.1)
        lid[crease, :3] = np.clip(np.array(lid_rgb) * 0.56, 0, 255).astype(np.uint8)
        lid[..., 3] = np.clip(lm * 255.0, 0, 255).astype(np.uint8)
        save_part(pdir, f"lid_{tag}", lid, meta)

    # Build `eye_rim_mask`: original painted slime folds around the 3 eyes with almond holes punched out!
    rim_rgba = raw.copy()
    # Darken 1.5px inner rim of almond holes so irises & lids sit recessed inside dark sockets
    inner_edge = (all_holes > 0.08) & (all_holes < 0.72)
    rim_rgba[inner_edge, :3] = np.clip(rim_rgba[inner_edge, :3].astype(np.float32) * 0.62, 0, 255).astype(np.uint8)
    rim_alpha = rim_zone * (1.0 - all_holes) * a_src
    save_part(pdir, "eye_rim_mask", rim_rgba, meta, mask=rim_alpha)

    # 1f. Clean Reconstructed `body_dome`
    #     - Trim protruding crimson crystal spikes outside the left gel lobe silhouette so tilting core_crimson never double-ghosts!
    #     - Inpaint smooth slate-indigo gel behind submerged crimson bases & amber slabs!
    #     - Fill the 3 eye socket interiors with dark recessed slate (#242934) behind iris_*!
    body_clean = raw.copy()
    protruding_crim = poly_mask([(190, 68), (295, 75), (344, 152), (265, 250), (190, 235)], (H, W), feather=0.8)
    # Subtract protruding open-air crystal needles from body alpha while keeping rounded shoulder lobe
    lobe_keep = poly_mask([(268, 206), (348, 162), (392, 265), (235, 340)], (H, W), feather=1.5)
    kill_spikes = np.clip(protruding_crim * (1.0 - lobe_keep), 0.0, 1.0)
    body_clean[..., 3] = np.clip(body_clean[..., 3].astype(np.float32) * (1.0 - kill_spikes), 0, 255).astype(np.uint8)

    # Also trim top-most protruding seafoam bubble bumps right on top edge (y < 72, x = 700..775)
    top_bub_trim = (yy < 70) & (xx > 695) & (xx < 785) & (bub_m > 0.4)
    body_clean[top_bub_trim, 3] = 0

    # Inpaint smooth slate gel behind crimson base & amber slabs
    body_clean = inpaint_gel(body_clean, (crim_m > 0.25) | (amb_all > 0), dilate_px=9)

    # Paint dark recessed socket backing behind the 3 almond eye holes on body_dome
    sock_fill = cv2.dilate((all_holes > 0.1).astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
    body_clean[sock_fill, 0] = 36
    body_clean[sock_fill, 1] = 41
    body_clean[sock_fill, 2] = 52

    save_part(pdir, "body_dome", body_clean, meta)

    # 1g. Nested Volumetric `inner_gel` (`middle slime` parallax core) & subtle `prism_glow`
    inner_gel = make_inner_blob(body_clean, (1.12, 1.18, 1.24), shrink_px=42, alpha_mul=0.34)
    save_part(pdir, "inner_gel", inner_gel, meta)

    prism = np.zeros_like(raw)
    lum = rgb.mean(axis=2)
    shimmer = np.clip((lum - 108.0) / 85.0, 0.0, 1.0) * (body_clean[..., 3] > 180)
    shimmer = cv2.GaussianBlur(shimmer.astype(np.float32), (15, 15), 0)
    prism[..., 0] = np.clip(rgb[..., 0] * 1.15 + 25, 0, 255).astype(np.uint8)
    prism[..., 1] = np.clip(rgb[..., 1] * 1.20 + 28, 0, 255).astype(np.uint8)
    prism[..., 2] = np.clip(rgb[..., 2] * 1.25 + 32, 0, 255).astype(np.uint8)
    prism[..., 3] = np.clip(shimmer * 185.0, 0, 255).astype(np.uint8)
    save_part(pdir, "prism_glow", prism, meta)

    (pdir / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")


# ==============================================================================
# 2. MEDIUM CHROMATIC SLIMES (440 x 420, ORIGIN = (220, 386), 3 Skins)
# ==============================================================================
def build_medium_parts() -> None:
    pdir = HERE / "medium" / "parts"
    if pdir.exists():
        shutil.rmtree(pdir)
    pdir.mkdir(parents=True, exist_ok=True)

    W, H = 440, 420
    ground_y = 392
    meta: dict[str, dict[str, int]] = {}

    sh = make_chalk_shadow(356, 56, max_alpha=104.0)
    sh.save(pdir / "shadow.png")
    meta["shadow"] = {"x": 220 - 178, "y": 386 - 26, "w": 356, "h": 56}

    yy, xx = np.mgrid[0:H, 0:W]

    for strain in ("crimson", "amber", "azure"):
        im = Image.open(SRC_DIR / f"sts2_medium_{strain}.png").convert("RGBA")
        ox = (W - im.width) // 2
        oy = ground_y - im.height
        canv = np.zeros((H, W, 4), dtype=np.uint8)
        canv[oy : oy + im.height, ox : ox + im.width] = np.array(im)

        rgb = canv[..., :3].astype(np.float32)
        r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        a_src = canv[..., 3].astype(np.float32) / 255.0

        # Smooth underlying rounded gel dome envelope (separates protruding top crystals/stones/droplets!)
        if strain == "crimson":
            # Dome crest starts around y=126 at center; 9 garnet splinters captured cleanly via exact shard polygons!
            dome_env = (((xx - 216.0) / 152.0) ** 2 + ((yy - 274.0) / 146.0) ** 2) <= 1.0
            dome_env |= (yy >= 254)
            splinter_polys = [
                # Front/left splinters (x < 228 -> crest_front)
                [(36, 150), (62, 152), (104, 186), (92, 214), (42, 176)],               # far-left lower spike
                [(96, 108), (124, 114), (156, 188), (128, 208), (104, 156)],            # front-left spike + meniscus
                [(126, 26), (168, 42), (196, 156), (158, 166), (138, 92)],              # tall left-crown spire
                [(198, 156), (222, 156), (224, 186), (198, 188)],                       # center small nub
                # Rear/right splinters (x >= 228 -> crest_back / flank)
                [(212, 82), (246, 86), (242, 132), (210, 130)],                         # rear-crown short spire
                [(248, 128), (308, 48), (336, 56), (328, 108), (284, 186), (246, 176)], # tall right-crown spire
                [(308, 162), (376, 128), (394, 136), (378, 168), (326, 220)],           # upper-right flank spike
                [(266, 208), (294, 204), (296, 236), (268, 240)],                       # mid-right small shard
                [(314, 224), (378, 218), (382, 236), (322, 264)],                       # lower-right flank spike
            ]
            m_front_raw = np.zeros((H, W), dtype=np.float32)
            m_back_raw = np.zeros((H, W), dtype=np.float32)
            for idx_s, sp in enumerate(splinter_polys):
                pm = poly_mask(sp, (H, W), feather=0.75) * (a_src > 0.04)
                if idx_s in (0, 1, 2, 3, 7, 8):
                    m_front_raw = np.maximum(m_front_raw, pm)
                else:
                    m_back_raw = np.maximum(m_back_raw, pm)
            # Catch any remaining pixel outside dome_env above y=275
            extra_out = ((~dome_env) & (a_src > 0.04) & (yy < 275)).astype(np.float32)
            m_back = np.maximum(m_back_raw, extra_out * (xx >= 228))
            m_front = np.maximum(m_front_raw, extra_out * (xx < 228))

            body_clean = canv.copy()
            body_clean[(~dome_env) & (yy < 285), 3] = 0
            body_clean = inpaint_gel(body_clean, ((m_back + m_front) > 0.22) & dome_env, dilate_px=8)

            eye_holes = [
                [(94, 244), (108, 241), (128, 255), (124, 263), (102, 255)],
                [(158, 261), (195, 250), (236, 243), (222, 261), (186, 267)],
            ]
            pupils_spec = [(111, 252, 8, 5, (226, 174, 146)), (198, 256, 14, 6, (228, 176, 148))]
            lid_col = (128, 52, 56)
            inner_tint = (1.16, 1.12, 1.10)

        elif strain == "amber":
            # Dome envelope underneath the 6 faceted amber-calcite armor slabs
            dome_env = (((xx - 218.0) / 156.0) ** 2 + ((yy - 272.0) / 146.0) ** 2) <= 1.0
            dome_env |= (yy >= 258)
            slab_polys = [
                [(176, 72), (242, 68), (274, 96), (266, 142), (212, 146), (176, 118)],   # top crown slab
                [(102, 112), (146, 114), (138, 148), (82, 198), (74, 166)],              # left brow slab
                [(148, 142), (198, 138), (204, 186), (168, 198), (144, 174)],            # front-mid stone
                [(218, 152), (252, 148), (256, 182), (226, 188)],                        # small center pebble
                [(274, 98), (328, 118), (336, 162), (294, 166), (272, 136)],             # upper-right slab
                [(288, 162), (356, 158), (372, 214), (344, 256), (292, 224)],            # right flank shield slab
            ]
            m_front_raw = np.zeros((H, W), dtype=np.float32)
            m_back_raw = np.zeros((H, W), dtype=np.float32)
            for idx_p, sp in enumerate(slab_polys):
                pm = poly_mask(sp, (H, W), feather=0.8) * (a_src > 0.04)
                if idx_p in (0, 4, 5):
                    m_back_raw = np.maximum(m_back_raw, pm)
                else:
                    m_front_raw = np.maximum(m_front_raw, pm)
            m_back = m_back_raw
            m_front = m_front_raw

            body_clean = canv.copy()
            body_clean[(~dome_env) & (yy < 258), 3] = 0
            body_clean = inpaint_gel(body_clean, ((m_back + m_front) > 0.25) & dome_env, dilate_px=7)

            eye_holes = [
                [(88, 246), (116, 243), (144, 247), (124, 256), (98, 254)],
                [(180, 249), (214, 247), (248, 251), (226, 262), (194, 260)],
            ]
            pupils_spec = [(116, 249, 10, 5, (234, 214, 166)), (212, 254, 11, 5, (234, 214, 166))]
            lid_col = (168, 122, 54)
            inner_tint = (1.14, 1.14, 1.08)

        else:  # azure
            # Detached floating droplets -> crest_back; frothy seafoam cap -> crest_front!
            detached_m = (yy < 104) & (a_src > 0.03)
            cap_poly = poly_mask(
                [(96, 192), (118, 112), (186, 86), (262, 86), (322, 132), (344, 198), (282, 184), (206, 186), (138, 188)],
                (H, W),
                feather=1.2,
            )
            froth_m = cap_poly * ((g > 152.0) | (yy < 156)).astype(np.float32) * (a_src > 0.04)
            m_back = cv2.GaussianBlur(detached_m.astype(np.float32), (0, 0), 0.5)
            m_front = cv2.GaussianBlur(froth_m, (0, 0), 0.9) * (1.0 - m_back)

            body_clean = canv.copy()
            body_clean[yy < 104, 3] = 0
            # Round off top of slate-teal gel dome smoothly under the froth cap
            dome_env = (((xx - 218.0) / 155.0) ** 2 + ((yy - 266.0) / 146.0) ** 2) <= 1.0
            dome_env |= (yy >= 175)
            body_clean[(~dome_env) & (yy < 175), 3] = 0

            eye_holes = [
                [(96, 247), (120, 244), (144, 249), (132, 261), (104, 259)],
                [(178, 250), (214, 246), (250, 251), (236, 266), (192, 265)],
            ]
            pupils_spec = [(119, 250, 9, 4, (216, 234, 218)), (211, 253, 11, 5, (216, 234, 218))]
            lid_col = (88, 126, 124)
            inner_tint = (1.12, 1.18, 1.16)

        # Save crest_back & crest_front
        save_part(pdir, f"{strain}_crest_back", canv, meta, mask=m_back)
        save_part(pdir, f"{strain}_crest_front", canv, meta, mask=m_front)

        # Build 3-layer unscaled eye system (pupils + eyelids + eye_rim mask)
        holes_m = np.zeros((H, W), dtype=np.float32)
        rim_z = np.zeros((H, W), dtype=np.float32)
        pup_img = np.zeros((H, W, 4), dtype=np.uint8)
        lid_img = np.zeros((H, W, 4), dtype=np.uint8)

        for h_pts, (pcx, pcy, prx, pry, pcol) in zip(eye_holes, pupils_spec):
            hm = poly_mask(h_pts, (H, W), feather=0.55)
            holes_m = np.maximum(holes_m, hm)
            rz = np.exp(-(((xx - pcx) / 62.0) ** 4 + ((yy - (pcy - 8.0)) / 46.0) ** 4))
            rim_z = np.maximum(rim_z, np.clip((rz - 0.08) / 0.30, 0.0, 1.0))

            pm = np.zeros((H, W), dtype=np.uint8)
            cv2.ellipse(pm, (pcx, pcy), (prx, pry), 0, 0, 360, 255, -1, lineType=cv2.LINE_AA)
            pa = cv2.GaussianBlur(pm.astype(np.float32) / 255.0, (0, 0), 0.5)
            take = pa > (pup_img[..., 3].astype(np.float32) / 255.0)
            pup_img[take, 0] = pcol[0]
            pup_img[take, 1] = pcol[1]
            pup_img[take, 2] = pcol[2]
            pup_img[..., 3] = np.maximum(pup_img[..., 3], np.clip(pa * 255.0, 0, 255).astype(np.uint8))

            xs_h = [p[0] for p in h_pts]
            ys_h = [p[1] for p in h_pts]
            lx0, lx1 = min(xs_h) - 6, max(xs_h) + 6
            ly_bot = min(ys_h) + 2
            ly_top = ly_bot - 22
            lm = poly_mask([(lx0, ly_top), (lx1, ly_top), (lx1, ly_bot + 1), (lx0, ly_bot + 1)], (H, W), feather=0.55)
            ltake = lm > (lid_img[..., 3].astype(np.float32) / 255.0)
            lid_img[ltake, 0] = lid_col[0]
            lid_img[ltake, 1] = lid_col[1]
            lid_img[ltake, 2] = lid_col[2]
            crease = (yy >= ly_bot - 1) & (lm > 0.1)
            lid_img[crease, :3] = np.clip(np.array(lid_col) * 0.55, 0, 255).astype(np.uint8)
            lid_img[..., 3] = np.maximum(lid_img[..., 3], np.clip(lm * 255.0, 0, 255).astype(np.uint8))

        # Paint dark recessed socket backing onto body_clean behind holes_m
        sock_fill = cv2.dilate((holes_m > 0.1).astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
        sock_col = (58, 22, 26) if strain == "crimson" else ((78, 52, 20) if strain == "amber" else (36, 58, 60))
        body_clean[sock_fill, 0] = sock_col[0]
        body_clean[sock_fill, 1] = sock_col[1]
        body_clean[sock_fill, 2] = sock_col[2]

        rim_rgba = canv.copy()
        rim_alpha = rim_z * (1.0 - holes_m) * (body_clean[..., 3] > 120).astype(np.float32)

        save_part(pdir, f"{strain}_body", body_clean, meta)
        save_part(pdir, f"{strain}_inner_blob", make_inner_blob(body_clean, inner_tint, shrink_px=26, alpha_mul=0.36), meta)
        save_part(pdir, f"{strain}_pupils", pup_img, meta)
        save_part(pdir, f"{strain}_eyelids", lid_img, meta)
        save_part(pdir, f"{strain}_eye_rim", rim_rgba, meta, mask=rim_alpha)

        # Restrained gouache rim shimmer for cast / power_up / spawn
        aura = np.zeros_like(canv)
        lum = rgb.mean(axis=2)
        hi = cv2.GaussianBlur((np.clip((lum - 115.0) / 85.0, 0.0, 1.0) * (body_clean[..., 3] > 160)).astype(np.float32), (11, 11), 0)
        aura[..., :3] = canv[..., :3]
        aura[..., 3] = np.clip(hi * 175.0, 0, 255).astype(np.uint8)
        save_part(pdir, f"{strain}_aura", aura, meta)

    (pdir / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")


# ==============================================================================
# 3. SMALL CHROMATIC SLIMES (380 x 320, ORIGIN = (190, 290), 3 Skins)
# ==============================================================================
def build_small_parts() -> None:
    pdir = HERE / "small" / "parts"
    if pdir.exists():
        shutil.rmtree(pdir)
    pdir.mkdir(parents=True, exist_ok=True)

    W, H = 380, 320
    ground_y = 296
    meta: dict[str, dict[str, int]] = {}

    sh = make_chalk_shadow(304, 46, max_alpha=98.0)
    sh.save(pdir / "shadow.png")
    meta["shadow"] = {"x": 190 - 152, "y": 290 - 21, "w": 304, "h": 46}

    yy, xx = np.mgrid[0:H, 0:W]

    for strain in ("crimson", "amber", "azure"):
        im = Image.open(SRC_DIR / f"sts2_small_{strain}.png").convert("RGBA")
        ox = (W - im.width) // 2
        oy = ground_y - im.height
        canv = np.zeros((H, W, 4), dtype=np.uint8)
        canv[oy : oy + im.height, ox : ox + im.width] = np.array(im)

        rgb = canv[..., :3].astype(np.float32)
        r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        a_src = canv[..., 3].astype(np.float32) / 255.0

        if strain == "crimson":
            # 3 sharp garnet crystal splinters on crown
            dome_env = (((xx - 188.0) / 122.0) ** 2 + ((yy - 214.0) / 114.0) ** 2) <= 1.0
            dome_env |= (yy >= 205)
            cryst_polys = [
                [(102, 54), (132, 62), (144, 118), (118, 126), (102, 92)],
                [(174, 14), (218, 28), (226, 86), (206, 122), (174, 112)],
                [(232, 82), (272, 78), (268, 118), (238, 144), (224, 132)],
            ]
            cm = np.zeros((H, W), dtype=np.float32)
            for cp in cryst_polys:
                cm = np.maximum(cm, poly_mask(cp, (H, W), feather=0.75))
            cm *= (a_src > 0.04)

            body_clean = canv.copy()
            body_clean[(~dome_env) & (yy < 165), 3] = 0
            body_clean = inpaint_gel(body_clean, (cm > 0.25) & dome_env, dilate_px=7)

            # Unscaled closed-blink gouache patch (hidden at rest behind open slit or alpha-driven? Wait:
            # Using our zero-scale sliding lid + eye_rim mask works identically on Small too!)
            eye_holes = [
                [(88, 174), (102, 184), (106, 198), (94, 194)],
                [(156, 198), (182, 186), (196, 186), (176, 201)],
            ]
            lid_col = (132, 58, 62)
            inner_tint = (1.15, 1.12, 1.10)

        elif strain == "amber":
            # 2 chunky amber-calcite stone caps
            dome_env = (((xx - 190.0) / 138.0) ** 2 + ((yy - 208.0) / 116.0) ** 2) <= 1.0
            dome_env |= (yy >= 195)
            stone_polys = [
                [(78, 86), (148, 68), (172, 88), (152, 142), (94, 146), (78, 124)],
                [(192, 66), (258, 76), (294, 116), (288, 166), (242, 162), (190, 112)],
            ]
            cm = np.zeros((H, W), dtype=np.float32)
            for sp in stone_polys:
                cm = np.maximum(cm, poly_mask(sp, (H, W), feather=0.8))
            cm *= (a_src > 0.04)

            body_clean = canv.copy()
            body_clean[(~dome_env) & (yy < 168), 3] = 0
            body_clean = inpaint_gel(body_clean, (cm > 0.25) & dome_env, dilate_px=7)

            eye_holes = [
                [(96, 194), (145, 206), (198, 206), (222, 204), (196, 214), (142, 214)],
            ]
            lid_col = (166, 122, 54)
            inner_tint = (1.14, 1.14, 1.08)

        else:  # azure
            cap_poly = poly_mask(
                [(118, 118), (132, 42), (258, 42), (274, 134), (232, 138), (186, 124), (146, 124)],
                (H, W),
                feather=1.0,
            )
            cm = cap_poly * ((g > 148.0) | (yy < 106)).astype(np.float32) * (a_src > 0.03)

            body_clean = canv.copy()
            body_clean[yy < 74, 3] = 0
            dome_env = (((xx - 190.0) / 128.0) ** 2 + ((yy - 208.0) / 112.0) ** 2) <= 1.0
            dome_env |= (yy >= 145)
            body_clean[(~dome_env) & (yy < 145), 3] = 0

            eye_holes = [
                [(94, 174), (108, 168), (110, 196), (94, 192)],
                [(166, 182), (186, 178), (192, 212), (168, 208)],
            ]
            lid_col = (82, 126, 124)
            inner_tint = (1.12, 1.18, 1.16)

        save_part(pdir, f"{strain}_crest", canv, meta, mask=cm)

        # Build unscaled gouache blink lid + eye_rim mask
        holes_m = np.zeros((H, W), dtype=np.float32)
        rim_z = np.zeros((H, W), dtype=np.float32)
        lid_img = np.zeros((H, W, 4), dtype=np.uint8)
        for h_pts in eye_holes:
            hm = poly_mask(h_pts, (H, W), feather=0.55)
            holes_m = np.maximum(holes_m, hm)
            xs_h = [p[0] for p in h_pts]
            ys_h = [p[1] for p in h_pts]
            cx_e, cy_e = sum(xs_h) * 1.0 / len(xs_h), sum(ys_h) * 1.0 / len(ys_h)
            rz = np.exp(-(((xx - cx_e) / 60.0) ** 4 + ((yy - (cy_e - 8.0)) / 44.0) ** 4))
            rim_z = np.maximum(rim_z, np.clip((rz - 0.08) / 0.30, 0.0, 1.0))

            lx0, lx1 = min(xs_h) - 6, max(xs_h) + 6
            ly_bot = min(ys_h)
            ly_top = ly_bot - 22
            lm = poly_mask([(lx0, ly_top), (lx1, ly_top), (lx1, ly_bot + 1), (lx0, ly_bot + 1)], (H, W), feather=0.55)
            ltake = lm > (lid_img[..., 3].astype(np.float32) / 255.0)
            lid_img[ltake, 0] = lid_col[0]
            lid_img[ltake, 1] = lid_col[1]
            lid_img[ltake, 2] = lid_col[2]
            crease = (yy >= ly_bot - 1) & (lm > 0.1)
            lid_img[crease, :3] = np.clip(np.array(lid_col) * 0.58, 0, 255).astype(np.uint8)
            lid_img[..., 3] = np.maximum(lid_img[..., 3], np.clip(lm * 255.0, 0, 255).astype(np.uint8))

        rim_rgba = canv.copy()
        rim_alpha = rim_z * (1.0 - holes_m) * (body_clean[..., 3] > 120).astype(np.float32)

        save_part(pdir, f"{strain}_body", body_clean, meta)
        save_part(pdir, f"{strain}_inner_blob", make_inner_blob(body_clean, inner_tint, shrink_px=22, alpha_mul=0.34), meta)
        save_part(pdir, f"{strain}_eyelids", lid_img, meta)
        save_part(pdir, f"{strain}_eye_rim", rim_rgba, meta, mask=rim_alpha)

        aura = np.zeros_like(canv)
        lum = rgb.mean(axis=2)
        hi = cv2.GaussianBlur((np.clip((lum - 115.0) / 85.0, 0.0, 1.0) * (body_clean[..., 3] > 160)).astype(np.float32), (9, 9), 0)
        aura[..., :3] = canv[..., :3]
        aura[..., 3] = np.clip(hi * 170.0, 0, 255).astype(np.uint8)
        save_part(pdir, f"{strain}_aura", aura, meta)

    (pdir / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")


# ==============================================================================
# 4. RENDER COMPREHENSIVE REASSEMBLY, BARE-DOME & UNSCALED-BLINK AUDIT SHEET
# ==============================================================================
def build_audit_sheet() -> None:
    sheet = Image.new("RGB", (1860, 1080), (24, 28, 36))
    draw = ImageDraw.Draw(sheet)

    # Top-left: Large Reassembled vs Bare Dome (without minerals) vs Unscaled Blink (+22px lid slide)
    l_meta = json.loads((HERE / "large" / "parts" / "meta.json").read_text())
    l_order = [
        "shadow", "body_dome", "inner_gel",
        "iris_top", "iris_mid", "iris_low",
        "lid_top", "lid_mid", "lid_low",
        "eye_rim_mask",
        "core_crimson", "core_amber_chips", "core_amber", "azure_bubbles",
    ]
    for col_i, (lbl, hide_cores, blink_dy) in enumerate([
        ("Large: Reassembled (14 Parts)", False, 0),
        ("Large: Clean Bare Gel Dome (0 Ghosting!)", True, 0),
        ("Large: Unscaled Eyelid Blink (sx=1, sy=1)", False, 25),
    ]):
        canv = Image.new("RGBA", (1120, 720), (34, 39, 50, 255))
        for p in l_order:
            if hide_cores and p in ("core_crimson", "core_amber", "core_amber_chips", "azure_bubbles"):
                continue
            im = Image.open(HERE / "large" / "parts" / f"{p}.png").convert("RGBA")
            mx, my = l_meta[p]["x"], l_meta[p]["y"]
            if p.startswith("lid_"):
                my += blink_dy
            canv.alpha_composite(im, (mx, my))
        th = canv.convert("RGB").resize((590, 379), Image.Resampling.LANCZOS)
        sheet.paste(th, (20 + col_i * 610, 38))
        draw.text((26 + col_i * 610, 12), lbl, fill=(140, 242, 226))

    # Middle row: Medium 3 Strains (Reassembled + Bare Dome + Unscaled Blink)
    m_meta = json.loads((HERE / "medium" / "parts" / "meta.json").read_text())
    for s_idx, strain in enumerate(["crimson", "amber", "azure"]):
        for mode_i, (m_lbl, hide_crest, lid_dy) in enumerate([
            ("Full", False, 0),
            ("BareGel", True, 0),
            ("Blink", False, 16),
        ]):
            canv = Image.new("RGBA", (440, 420), (34, 39, 50, 255))
            slots = ["shadow"]
            if not hide_crest:
                slots.append(f"{strain}_crest_back")
            slots += [f"{strain}_body", f"{strain}_inner_blob", f"{strain}_pupils", f"{strain}_eyelids", f"{strain}_eye_rim"]
            if not hide_crest:
                slots.append(f"{strain}_crest_front")
            for p in slots:
                im = Image.open(HERE / "medium" / "parts" / f"{p}.png").convert("RGBA")
                mx, my = m_meta[p]["x"], m_meta[p]["y"]
                if p.endswith("_eyelids"):
                    my += lid_dy
                canv.alpha_composite(im, (mx, my))
            th = canv.convert("RGB").resize((192, 183), Image.Resampling.LANCZOS)
            px = 20 + (s_idx * 3 + mode_i) * 202
            sheet.paste(th, (px, 462))
            draw.text((px + 4, 438), f"Med {strain} [{m_lbl}]", fill=(242, 226, 165))

    # Bottom row: Small 3 Strains (Reassembled + Bare Dome + Unscaled Blink)
    s_meta = json.loads((HERE / "small" / "parts" / "meta.json").read_text())
    for s_idx, strain in enumerate(["crimson", "amber", "azure"]):
        for mode_i, (m_lbl, hide_crest, lid_dy) in enumerate([
            ("Full", False, 0),
            ("BareGel", True, 0),
            ("Blink", False, 18),
        ]):
            canv = Image.new("RGBA", (380, 320), (34, 39, 50, 255))
            slots = ["shadow", f"{strain}_body", f"{strain}_inner_blob", f"{strain}_eyelids", f"{strain}_eye_rim"]
            if not hide_crest:
                slots.append(f"{strain}_crest")
            for p in slots:
                im = Image.open(HERE / "small" / "parts" / f"{p}.png").convert("RGBA")
                mx, my = s_meta[p]["x"], s_meta[p]["y"]
                if p.endswith("_eyelids"):
                    my += lid_dy
                canv.alpha_composite(im, (mx, my))
            th = canv.convert("RGB").resize((192, 162), Image.Resampling.LANCZOS)
            px = 20 + (s_idx * 3 + mode_i) * 202
            sheet.paste(th, (px, 700))
            draw.text((px + 4, 676), f"Sml {strain} [{m_lbl}]", fill=(165, 232, 242))

    AUDIT_OUT.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(AUDIT_OUT, quality=93)
    print("Saved parts audit:", AUDIT_OUT)


if __name__ == "__main__":
    build_large_parts()
    build_medium_parts()
    build_small_parts()
    build_audit_sheet()
