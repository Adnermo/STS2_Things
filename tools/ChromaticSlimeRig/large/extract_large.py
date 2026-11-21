"""Seamless Multi-Layer Part Decomposition for Large Chromatic Slime (v4 cutout 1082x635).

Extracts 21 clean RGBA layers + parts/meta.json and renders prev/parts_audit.jpg:
  1. shadow.png              — Soft dry-chalk charcoal ground contact shadow (~1040x86 oval)
  2. drop_near.png           — Detached front-right floor slime droplet (erased from body_dome)
  3. drop_far.png            — Detached far-right floor slime droplet (erased from body_dome)
  4. crimson_shard_l.png     — Rigid left angled crimson ruby crystal
  5. crimson_shard_tall.png  — Rigid tall central crimson ruby horn
  6. crimson_shard_r.png     — Rigid right crimson ruby crystal + front faceted pip
  7. amber_goo_cap.png       — Deformable golden-amber dorsal goo cap (boulders cleanly inpainted out!)
  8. amber_rock_l.png        — Rigid upper-left weathered sandstone boulder
  9. amber_rock_main.png     — Rigid massive central sandstone boulder (with hidden overlap behind rock_r)
 10. amber_rock_r.png        — Rigid right sandstone boulder + small front stone chip
 11. azure_froth_main.png    — Main seafoam-cyan bubbling cluster on right flank
 12. azure_froth_spores.png  — Outer/floating satellite seafoam-cyan spore bubbles
 13. body_dome.png           — Clean bare multi-tiered slate-teal gel mound + marbled terracotta brow bed
                               + 4 hanging belly drips + 3 iris-free dark crescent eye socket cavities
 14. inner_gel.png           — Soft inner volumetric gel mass (alpha ~ 95) for delayed slosh parallax
 15. iris_main.png           — Unscaled flat ochre-gold main iris (padded upward under upper lid)
 16. iris_up.png             — Unscaled flat ochre-gold upper-mid iris (padded upward under upper lid)
 17. iris_rt.png             — Unscaled flat ochre-gold right iris (padded upward under upper lid)
 18. lid_main.png            — Unscaled slate-teal gouache main upper eyelid
 19. lid_up.png              — Unscaled slate-teal gouache upper-mid eyelid
 20. lid_rt.png              — Unscaled slate-teal gouache right eyelid
 21. prism_glow.png          — Soft tri-zone refractive prismatic glow overlay (additive slot)
"""
from __future__ import annotations

import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
CUTOUT = HERE.parents[2] / "source_assets" / "monsters" / "chromatic_slime" / "v4_cutouts" / "large.png"
PARTS_DIR = HERE / "parts"
PREV_DIR = HERE / "prev"

# Exact eye socket clipping polygons (shared with rigdef.py CLIPS)
EYE_CLIP_MAIN = [
    (244, 285), (275, 290), (318, 294), (355, 295), (383, 296),
    (382, 309), (373, 326), (357, 341), (336, 350), (309, 353),
    (281, 346), (258, 328), (246, 305), (244, 292),
]
EYE_CLIP_UP = [
    (412, 255), (424, 245), (442, 234), (460, 223), (462, 234),
    (457, 249), (447, 259), (433, 265), (419, 263), (412, 259),
]
EYE_CLIP_RT = [
    (470, 319), (485, 309), (508, 297), (533, 284), (536, 295),
    (533, 308), (524, 320), (508, 325), (487, 325), (473, 324),
]


def poly_mask(shape: tuple[int, int], pts: list[tuple[float, float]]) -> np.ndarray:
    m = np.zeros(shape, dtype=np.uint8)
    arr = np.round(np.asarray(pts, dtype=np.float32)).astype(np.int32)
    cv2.fillPoly(m, [arr], 255, lineType=cv2.LINE_AA)
    return m


def feather_mask(mask_u8: np.ndarray, dilate_px: int = 0, blur_px: float = 1.2) -> np.ndarray:
    m = mask_u8.copy()
    if dilate_px > 0:
        k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * dilate_px + 1, 2 * dilate_px + 1))
        m = cv2.dilate(m, k)
    elif dilate_px < 0:
        k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * (-dilate_px) + 1, 2 * (-dilate_px) + 1))
        m = cv2.erode(m, k)
    if blur_px > 0:
        m = cv2.GaussianBlur(m.astype(np.float32), (0, 0), blur_px)
    return np.clip(m / 255.0, 0.0, 1.0)


def save_trimmed(name: str, rgba: np.ndarray, meta: dict, pad: int = 2, fixed_box: tuple[int, int, int, int] | None = None) -> None:
    H, W = rgba.shape[:2]
    if fixed_box is not None:
        x0, y0, x1, y1 = fixed_box
    else:
        ys, xs = np.where(rgba[..., 3] > 1)
        if len(xs) == 0:
            raise RuntimeError(f"Empty part: {name}")
        x0 = max(0, int(xs.min()) - pad)
        y0 = max(0, int(ys.min()) - pad)
        x1 = min(W, int(xs.max()) + 1 + pad)
        y1 = min(H, int(ys.max()) + 1 + pad)
    crop = rgba[y0:y1, x0:x1].copy()
    # Zero out RGB on fully transparent pixels to keep PNG clean
    zero = crop[..., 3] == 0
    crop[zero] = 0
    Image.fromarray(crop).save(PARTS_DIR / f"{name}.png", optimize=True)
    meta[name] = {"x": int(x0), "y": int(y0), "w": int(x1 - x0), "h": int(y1 - y0)}


def extract_sprite(src: np.ndarray, mask_f: np.ndarray) -> np.ndarray:
    out = src.copy()
    out[..., 3] = np.clip(np.round(src[..., 3].astype(np.float32) * mask_f), 0, 255).astype(np.uint8)
    return out


def alpha_over(dst: np.ndarray, src: np.ndarray) -> np.ndarray:
    """Standard Porter-Duff src over dst in float RGBA [0..255]."""
    sa = src[..., 3:4].astype(np.float32) / 255.0
    da = dst[..., 3:4].astype(np.float32) / 255.0
    out_a = sa + da * (1.0 - sa)
    out_rgb = np.where(
        out_a > 1e-6,
        (src[..., :3].astype(np.float32) * sa + dst[..., :3].astype(np.float32) * da * (1.0 - sa)) / np.maximum(out_a, 1e-6),
        0.0,
    )
    res = np.zeros_like(dst)
    res[..., :3] = np.clip(np.round(out_rgb), 0, 255).astype(np.uint8)
    res[..., 3] = np.clip(np.round(out_a[..., 0] * 255.0), 0, 255).astype(np.uint8)
    return res


def smooth_inpaint_rgb(rgb_u8: np.ndarray, hole_mask_u8: np.ndarray, radius: int = 9, blur_sigma: float = 2.2) -> np.ndarray:
    """Inpaint RGB across hole_mask_u8 inside tight ROI for fast, clean flat gouache planes."""
    ys, xs = np.where(hole_mask_u8 > 0)
    if len(xs) == 0:
        return rgb_u8.copy()
    H, W = rgb_u8.shape[:2]
    pad = max(28, radius * 3)
    x0, y0 = max(0, int(xs.min()) - pad), max(0, int(ys.min()) - pad)
    x1, y1 = min(W, int(xs.max()) + 1 + pad), min(H, int(ys.max()) + 1 + pad)
    sub_rgb = rgb_u8[y0:y1, x0:x1]
    sub_m = hole_mask_u8[y0:y1, x0:x1]
    inp = cv2.inpaint(sub_rgb, sub_m, radius, cv2.INPAINT_TELEA)
    soft = cv2.GaussianBlur(inp.astype(np.float32), (0, 0), blur_sigma)
    inner = feather_mask(sub_m, dilate_px=-1, blur_px=2.0)[..., None]
    blended = sub_rgb.astype(np.float32) * (1.0 - inner) + (0.42 * inp.astype(np.float32) + 0.58 * soft) * inner
    out = rgb_u8.copy()
    out[y0:y1, x0:x1] = np.clip(np.round(blended), 0, 255).astype(np.uint8)
    return out


def build_shadow(W: int, meta: dict) -> np.ndarray:
    """Soft dry-chalk charcoal ground contact shadow (~1040x86 oval)."""
    sw, sh = 1040, 86
    sx, sy = 24, 548
    yy, xx = np.mgrid[0:sh, 0:sw].astype(np.float32)
    # Multi-lobe contact shadow hugging main puddle + right puddle extension
    d1 = ((xx - 488.0) / 468.0) ** 2 + ((yy - 45.0) / 36.0) ** 2
    d2 = ((xx - 760.0) / 245.0) ** 2 + ((yy - 43.0) / 28.0) ** 2
    d = np.minimum(d1, d2)
    falloff = np.clip(1.0 - d, 0.0, 1.0) ** 1.65
    # Subtle gouache chalk grain (deterministic)
    rng = np.random.default_rng(4204)
    grain = cv2.GaussianBlur(rng.normal(0.0, 1.0, (sh, sw)).astype(np.float32), (0, 0), 3.5)
    alpha = np.clip(falloff * (104.0 + 12.0 * grain), 0.0, 112.0).astype(np.uint8)
    sh_im = np.zeros((sh, sw, 4), dtype=np.uint8)
    sh_im[..., 0] = 20
    sh_im[..., 1] = 26
    sh_im[..., 2] = 33
    sh_im[..., 3] = alpha
    Image.fromarray(sh_im).save(PARTS_DIR / "shadow.png", optimize=True)
    meta["shadow"] = {"x": sx, "y": sy, "w": sw, "h": sh}
    return sh_im


def main() -> None:
    PARTS_DIR.mkdir(parents=True, exist_ok=True)
    PREV_DIR.mkdir(parents=True, exist_ok=True)
    # Clean stale part PNGs so only current v4 parts exist
    for old in PARTS_DIR.glob("*.png"):
        old.unlink()

    src = np.array(Image.open(CUTOUT).convert("RGBA"))
    H, W = src.shape[:2]
    assert (W, H) == (1082, 635), f"Unexpected cutout size: {W}x{H}"
    rgb = src[..., :3].astype(np.float32)
    al = src[..., 3]
    meta: dict[str, dict] = {}

    # -------------------------------------------------------------------------
    # 1. Ground Shadow
    # -------------------------------------------------------------------------
    shadow_crop = build_shadow(W, meta)

    # -------------------------------------------------------------------------
    # 2. Detached Floor Slime Droplets (drop_near & drop_far)
    # -------------------------------------------------------------------------
    num_cc, cc_labels, cc_stats, _ = cv2.connectedComponentsWithStats((al > 4).astype(np.uint8))
    drop_near_mask = np.zeros((H, W), dtype=np.uint8)
    drop_far_mask = np.zeros((H, W), dtype=np.uint8)
    for cid in range(1, num_cc):
        cx, cy, cw, ch, area = cc_stats[cid]
        if 850 <= cx <= 935 and cy >= 570:
            drop_near_mask[cc_labels == cid] = 255
        elif cx >= 1000 and cy >= 530:
            drop_far_mask[cc_labels == cid] = 255
    drop_near_mask = cv2.dilate(drop_near_mask, np.ones((3, 3), np.uint8))
    drop_far_mask = cv2.dilate(drop_far_mask, np.ones((3, 3), np.uint8))

    drop_near_rgba = extract_sprite(src, (drop_near_mask > 0).astype(np.float32))
    drop_far_rgba = extract_sprite(src, (drop_far_mask > 0).astype(np.float32))
    save_trimmed("drop_near", drop_near_rgba, meta)
    save_trimmed("drop_far", drop_far_rgba, meta)

    # -------------------------------------------------------------------------
    # 3. 3 Rigid Crimson Ruby Crystal Shards
    # -------------------------------------------------------------------------
    # Chromatic ruby mask: deep crimson/magenta facets have B >= G - 3 and R - G > 30 (terracotta has G > B + 7!)
    in_crimson_zone = poly_mask(
        (H, W),
        [(184, 42), (402, 42), (402, 232), (184, 232)],
    ) > 0
    ruby_raw = (
        in_crimson_zone
        & (al > 12)
        & (rgb[..., 2] >= rgb[..., 1] - 3.0)
        & (rgb[..., 0] > 42.0)
        & ((rgb[..., 0] - rgb[..., 1]) > 28.0)
    )
    # Also include any outer-silhouette pixel above the smooth terracotta brow dome
    ruby_u8 = (ruby_raw * 255).astype(np.uint8)
    ruby_u8 = cv2.morphologyEx(ruby_u8, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))

    # Partition the 3 crystal regions cleanly along their anatomical facet boundaries
    reg_shard_l = poly_mask(
        (H, W),
        [(184, 152), (242, 158), (257, 180), (258, 212), (235, 229), (184, 229)],
    )
    reg_shard_tall = poly_mask(
        (H, W),
        [(226, 42), (316, 42), (318, 146), (292, 182), (255, 174), (242, 158), (226, 128)],
    )
    reg_shard_r = poly_mask(
        (H, W),
        [
            (328, 66), (402, 66), (402, 170), (362, 172), (362, 198),
            (313, 198), (313, 160), (330, 152),
        ],
    )
    # Keep only the two ruby components inside reg_shard_r (main right horn + front ruby pip, not terracotta bumps)
    m_shard_l = cv2.bitwise_and(ruby_u8, reg_shard_l)
    m_shard_tall = cv2.bitwise_and(ruby_u8, reg_shard_tall)
    m_shard_r = cv2.bitwise_and(ruby_u8, reg_shard_r)

    # Remove tiny stray specks < 40px
    def keep_large(m_u8: np.ndarray, min_area: int = 50) -> np.ndarray:
        n, lb, st, _ = cv2.connectedComponentsWithStats((m_u8 > 127).astype(np.uint8))
        out = np.zeros_like(m_u8)
        for i in range(1, n):
            if st[i, 4] >= min_area:
                out[lb == i] = 255
        return out

    m_shard_l = keep_large(m_shard_l, 200)
    m_shard_tall = keep_large(m_shard_tall, 400)
    m_shard_r = keep_large(m_shard_r, 150)

    crimson_l_rgba = extract_sprite(src, feather_mask(m_shard_l, dilate_px=1, blur_px=0.7))
    crimson_tall_rgba = extract_sprite(src, feather_mask(m_shard_tall, dilate_px=1, blur_px=0.7))
    crimson_r_rgba = extract_sprite(src, feather_mask(m_shard_r, dilate_px=1, blur_px=0.7))

    save_trimmed("crimson_shard_l", crimson_l_rgba, meta)
    save_trimmed("crimson_shard_tall", crimson_tall_rgba, meta)
    save_trimmed("crimson_shard_r", crimson_r_rgba, meta)

    # -------------------------------------------------------------------------
    # 4. 3 Rigid Sandstone Boulders + Deformable amber_goo_cap
    # -------------------------------------------------------------------------
    in_amber_rock_zone = poly_mask(
        (H, W),
        [(486, 12), (712, 12), (712, 162), (610, 162), (545, 118), (486, 102)],
    ) > 0
    # Sandstone rock is warm grey-tan: R > B + 6 and (R - G) < 35 (amber goo has R - G >= 40; teal has B > R)
    rock_raw = (
        in_amber_rock_zone
        & (al > 12)
        & (rgb[..., 0] > rgb[..., 2] + 5.0)
        & ((rgb[..., 0] - rgb[..., 1]) < 35.5)
    )
    rock_u8 = keep_large((rock_raw * 255).astype(np.uint8), 180)
    rock_u8 = cv2.morphologyEx(rock_u8, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))

    # Partition into amber_rock_l, amber_rock_main, amber_rock_r (+ small front chip)
    reg_rock_l = poly_mask(
        (H, W),
        [(486, 38), (562, 38), (558, 69), (546, 96), (486, 98)],
    )
    reg_rock_r = poly_mask(
        (H, W),
        [
            (672, 98), (710, 98), (710, 158), (614, 158),
            (614, 125), (651, 125), (656, 109),
        ],
    )
    reg_rock_main = poly_mask(
        (H, W),
        [
            (562, 12), (694, 12), (694, 99), (671, 99),
            (656, 110), (652, 134), (585, 136), (547, 94), (558, 69),
        ],
    )

    m_rock_l = keep_large(cv2.bitwise_and(rock_u8, reg_rock_l), 200)
    m_rock_main = keep_large(cv2.bitwise_and(rock_u8, reg_rock_main), 400)
    m_rock_r = keep_large(cv2.bitwise_and(rock_u8, reg_rock_r), 150)

    amber_rock_l_rgba = extract_sprite(src, feather_mask(m_rock_l, dilate_px=1, blur_px=0.7))
    amber_rock_r_rgba = extract_sprite(src, feather_mask(m_rock_r, dilate_px=1, blur_px=0.7))

    # Build amber_rock_main with a small shaded sandstone overlap behind amber_rock_r (652..678, 98..128)
    amber_rock_main_rgba = extract_sprite(src, feather_mask(m_rock_main, dilate_px=1, blur_px=0.7))
    overlap_patch = poly_mask(
        (H, W),
        [(650, 96), (680, 98), (676, 124), (650, 129)],
    )
    ov_f = feather_mask(overlap_patch, dilate_px=0, blur_px=1.2)
    under_mask = (ov_f > 0.02) & (amber_rock_main_rgba[..., 3] < 240)
    # Shaded dark sandstone color matching lower-right facet of amber_rock_main ([111, 96, 73])
    ov_layer = np.zeros_like(src)
    ov_layer[..., 0] = 110
    ov_layer[..., 1] = 95
    ov_layer[..., 2] = 73
    ov_layer[..., 3] = np.round(ov_f * 255.0).astype(np.uint8)
    amber_rock_main_rgba = alpha_over(ov_layer, amber_rock_main_rgba)
    _ = under_mask

    save_trimmed("amber_rock_l", amber_rock_l_rgba, meta)
    save_trimmed("amber_rock_main", amber_rock_main_rgba, meta)
    save_trimmed("amber_rock_r", amber_rock_r_rgba, meta)

    # Construct clean deformable amber_goo_cap.png (x=428..730, y=72..246)
    # Strictly exclude all sandstone rock pixels + 4px rim so zero tan rock ghosting enters amber_goo_cap!
    all_rocks_u8 = cv2.bitwise_or(cv2.bitwise_or(m_rock_l, m_rock_main), m_rock_r)
    rock_excl = cv2.dilate(all_rocks_u8, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))

    in_amber_cap_zone = poly_mask(
        (H, W),
        [
            (428, 72), (505, 76), (568, 82), (636, 98), (688, 128),
            (728, 186), (728, 246), (620, 246), (510, 195), (428, 132),
        ],
    ) > 0
    goo_vis = (
        in_amber_cap_zone
        & (al > 12)
        & (rock_excl == 0)
        & ((rgb[..., 0] - rgb[..., 1]) > 38.0)
        & ((rgb[..., 1] - rgb[..., 2]) > 36.0)
        & (rgb[..., 0] > 120.0)
    )
    goo_vis_u8 = keep_large((goo_vis * 255).astype(np.uint8), 140)

    # Smooth golden cushion dome tucked just below rock sky-silhouettes so moving rocks reveal pure golden goo!
    goo_under_rocks = poly_mask(
        (H, W),
        [
            (486, 86), (518, 80), (558, 83), (618, 96), (668, 118),
            (694, 140), (696, 168), (600, 178), (486, 132),
        ],
    )
    goo_cap_mask_u8 = cv2.bitwise_or(goo_vis_u8, goo_under_rocks)

    # Inpaint rock footprint inside goo_cap_mask_u8 using ONLY pure golden-amber goo pixels!
    rock_hole = cv2.bitwise_and(goo_cap_mask_u8, cv2.bitwise_not(goo_vis_u8))
    rock_hole = cv2.dilate(rock_hole, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))
    goo_canvas_rgb = src[..., :3].copy()
    non_goo = goo_vis_u8 == 0
    goo_canvas_rgb[non_goo] = np.array([187, 117, 32], dtype=np.uint8)
    goo_inp_rgb = smooth_inpaint_rgb(goo_canvas_rgb, rock_hole, radius=13, blur_sigma=3.4)
    # Subtle warm amber contact shading right under the 3 boulders (still rich golden-amber, NOT grey-brown!)
    rock_shadow_f = feather_mask(all_rocks_u8, dilate_px=1, blur_px=6.0) * (rock_hole > 0).astype(np.float32)
    goo_rgb_f = goo_inp_rgb.astype(np.float32)
    goo_rgb_f = goo_rgb_f * (1.0 - 0.14 * rock_shadow_f[..., None]) + np.array([162.0, 96.0, 22.0]) * (0.14 * rock_shadow_f[..., None])

    amber_goo_rgba = np.zeros_like(src)
    amber_goo_rgba[..., :3] = np.clip(np.round(goo_rgb_f), 0, 255).astype(np.uint8)
    goo_alpha_f = feather_mask(goo_cap_mask_u8, dilate_px=0, blur_px=0.85)
    orig_goo_edge = (goo_vis_u8 > 0) & (rock_hole == 0)
    amber_goo_rgba[..., 3] = np.where(
        orig_goo_edge,
        np.minimum(al, np.round(goo_alpha_f * 255.0).astype(np.uint8)),
        np.round(goo_alpha_f * 255.0).astype(np.uint8),
    )
    save_trimmed("amber_goo_cap", amber_goo_rgba, meta)

    # -------------------------------------------------------------------------
    # 5. 2 Independent Seafoam-Cyan Froth Layers (azure_froth_main & azure_froth_spores)
    # -------------------------------------------------------------------------
    in_azure_zone = poly_mask(
        (H, W),
        [(694, 318), (876, 318), (876, 476), (750, 476), (694, 438)],
    ) > 0
    froth_raw = (
        in_azure_zone
        & (al > 10)
        & (rgb[..., 1] > 127.0)
        & (rgb[..., 2] > 122.0)
        & ((rgb[..., 1] - rgb[..., 0]) > 42.0)
    )
    # Also include the two detached outer spore components completely (including their dark rims!)
    for cid in range(1, num_cc):
        cx, cy, cw, ch, area = cc_stats[cid]
        if cx >= 835 and 330 <= cy <= 385:
            froth_raw |= (cc_labels == cid)

    froth_u8 = keep_large((froth_raw * 255).astype(np.uint8), 30)
    n_f, f_lbl, f_st, _ = cv2.connectedComponentsWithStats((froth_u8 > 127).astype(np.uint8))
    # Largest component is azure_froth_main; the 7 smaller satellite bubbles are azure_froth_spores!
    main_fid = max(range(1, n_f), key=lambda i: f_st[i, 4])
    m_froth_main = ((f_lbl == main_fid) * 255).astype(np.uint8)
    m_froth_spores = (((f_lbl > 0) & (f_lbl != main_fid)) * 255).astype(np.uint8)

    # Dilate by 1.5px to capture the crisp painted teal bubble rim around every bubble
    froth_main_f = feather_mask(m_froth_main, dilate_px=2, blur_px=0.8)
    froth_spores_f = feather_mask(m_froth_spores, dilate_px=2, blur_px=0.8)
    # Prevent overlap between main and spores
    froth_spores_f = np.clip(froth_spores_f * (1.0 - froth_main_f), 0.0, 1.0)

    azure_main_rgba = extract_sprite(src, froth_main_f)
    azure_spores_rgba = extract_sprite(src, froth_spores_f)
    save_trimmed("azure_froth_main", azure_main_rgba, meta)
    save_trimmed("azure_froth_spores", azure_spores_rgba, meta)

    # -------------------------------------------------------------------------
    # 6. Clean Bare body_dome.png (Zero Ghosting + 3 Iris-Free Dark Eye Sockets)
    # -------------------------------------------------------------------------
    body_rgb = src[..., :3].copy()
    body_al = al.copy().astype(np.float32)

    # 6a. Completely erase drop_near & drop_far from body_dome!
    body_al[drop_near_mask > 0] = 0.0
    body_al[drop_far_mask > 0] = 0.0

    # 6b. Trim crystal horn tips that protrude into empty sky above the smooth terracotta brow dome,
    #     and inpaint embedded crystal bases with smooth terracotta-carmine gouache!
    brow_curve = [(x, int(round(121.0 + 0.0068 * ((x - 352.0) ** 2)))) for x in range(402, 204, -8)]
    sky_above_crimson_brow = poly_mask(
        (H, W),
        [(180, 35), (402, 35)] + brow_curve + [(180, brow_curve[-1][1])],
    )
    brow_dome_fill = poly_mask(
        (H, W),
        brow_curve + [(218, 195), (392, 185)],
    )
    body_al = np.maximum(body_al, feather_mask(brow_dome_fill, dilate_px=0, blur_px=0.9) * 255.0)
    body_al *= 1.0 - feather_mask(sky_above_crimson_brow, dilate_px=0, blur_px=1.1)

    all_crimson_u8 = cv2.bitwise_or(cv2.bitwise_or(m_shard_l, m_shard_tall), m_shard_r)
    crimson_hole = cv2.dilate(all_crimson_u8, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    # Seed sky/ruby pixels inside the crimson zone with warm terracotta-carmine [172, 84, 64] before Telea inpaint
    brow_seed = body_rgb.copy()
    bad_brow = (crimson_hole > 0) | (al < 180)
    brow_seed[bad_brow] = np.array([172, 84, 64], dtype=np.uint8)
    brow_inp = smooth_inpaint_rgb(brow_seed, crimson_hole, radius=12, blur_sigma=3.2)
    brow_blend = feather_mask(crimson_hole, dilate_px=1, blur_px=2.0)[..., None]
    body_rgb = np.clip(
        np.round(body_rgb.astype(np.float32) * (1.0 - brow_blend) + brow_inp.astype(np.float32) * brow_blend),
        0, 255,
    ).astype(np.uint8)

    # 6c. Trim amber rocks + upper amber goo above the smooth slate-teal dorsal hump contour,
    #     and paint clean slate-teal gel underneath amber_goo_cap!
    sky_above_teal_hump = poly_mask(
        (H, W),
        [
            (425, 10), (745, 10), (745, 224),
            (720, 222), (704, 186), (674, 148), (626, 118),
            (566, 98), (504, 92), (456, 98), (425, 106),
        ],
    )
    body_al *= 1.0 - feather_mask(sky_above_teal_hump, dilate_px=0, blur_px=1.1)

    amber_footprint = cv2.bitwise_or(goo_cap_mask_u8, all_rocks_u8)
    amber_hole = cv2.dilate(amber_footprint, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    teal_seed = body_rgb.copy()
    bad_hump = (amber_hole > 0) | (al < 180)
    teal_seed[bad_hump] = np.array([72, 104, 110], dtype=np.uint8)
    hump_inp = smooth_inpaint_rgb(teal_seed, amber_hole, radius=14, blur_sigma=4.0)
    hump_blend = feather_mask(amber_hole, dilate_px=1, blur_px=2.2)[..., None]
    body_rgb = np.clip(
        np.round(body_rgb.astype(np.float32) * (1.0 - hump_blend) + hump_inp.astype(np.float32) * hump_blend),
        0, 255,
    ).astype(np.uint8)

    # 6d. Trim outer seafoam froth bubbles protruding past the smooth right-flank curve,
    #     and inpaint interior froth bubbles on body_dome with clean slate-teal flank gel!
    sky_right_of_flank = poly_mask(
        (H, W),
        [
            (748, 305), (885, 305), (885, 472),
            (852, 466), (836, 430), (812, 386), (780, 348),
        ],
    )
    body_al *= 1.0 - feather_mask(sky_right_of_flank, dilate_px=0, blur_px=1.1)

    all_froth_u8 = cv2.bitwise_or(m_froth_main, m_froth_spores)
    froth_hole = cv2.dilate(all_froth_u8, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    flank_seed = body_rgb.copy()
    bad_flank = (froth_hole > 0) | (al < 180)
    flank_seed[bad_flank] = np.array([72, 102, 108], dtype=np.uint8)
    flank_inp = smooth_inpaint_rgb(flank_seed, froth_hole, radius=13, blur_sigma=3.5)
    # Subtle dark slate rim along the right-flank silhouette so bare body_dome matches artist's rim lighting
    flank_rim = feather_mask(sky_right_of_flank, dilate_px=5, blur_px=3.0) * (body_al > 40).astype(np.float32) * (froth_hole > 0).astype(np.float32)
    flank_inp_f = flank_inp.astype(np.float32) * (1.0 - 0.22 * flank_rim[..., None]) + np.array([50.0, 72.0, 77.0]) * (0.22 * flank_rim[..., None])
    flank_blend = feather_mask(froth_hole, dilate_px=1, blur_px=2.0)[..., None]
    body_rgb = np.clip(
        np.round(body_rgb.astype(np.float32) * (1.0 - flank_blend) + flank_inp_f * flank_blend),
        0, 255,
    ).astype(np.uint8)

    # -------------------------------------------------------------------------
    # 7. 3 Independent Unscaled Irises + Clean Dark Eye Sockets + 3 Gouache Eyelids
    # -------------------------------------------------------------------------
    eye_specs = [
        # (suffix, box, clip_poly, iris_ellipse(cx, cy, rx, ry, angle), lid_box(x0, y0, x1, y1), lid_bot_pts)
        (
            "main",
            (238, 276, 388, 356),
            EYE_CLIP_MAIN,
            (304.0, 297.5, 30.0, 29.5, 0.0),
            (184, 180, 446, 300),
            [(184, 281), (244, 285), (275, 290), (318, 294), (355, 295), (383, 296), (446, 297)],
        ),
        (
            "up",
            (406, 214, 466, 268),
            EYE_CLIP_UP,
            (432.5, 238.0, 12.5, 14.5, -26.0),
            (366, 150, 506, 260),
            [(366, 265), (412, 255), (424, 245), (442, 234), (460, 223), (506, 208)],
        ),
        (
            "rt",
            (464, 274, 540, 328),
            EYE_CLIP_RT,
            (495.5, 302.0, 15.5, 17.5, -22.0),
            (424, 210, 580, 324),
            [(424, 326), (470, 319), (485, 309), (508, 297), (533, 284), (580, 270)],
        ),
    ]

    for suf, (ex0, ey0, ex1, ey1), clip_poly, (icx, icy, irx, iry, iang), (lx0, ly0, lx1, ly1), lid_pts in eye_specs:
        sub_rgb = src[ey0:ey1, ex0:ex1, :3].astype(np.float32)
        iris_m = (sub_rgb[..., 0] > 112.0) & ((sub_rgb[..., 0] - sub_rgb[..., 2]) > 22.0)
        sock_m = (sub_rgb[..., 1] < 80.0) & (sub_rgb[..., 0] < 58.0)

        # Paint over ALL iris + anti-aliased transition pixels on body_dome with pure dark socket gouache!
        iris_full_u8 = np.zeros((H, W), dtype=np.uint8)
        iris_full_u8[ey0:ey1, ex0:ex1] = (iris_m * 255).astype(np.uint8)
        iris_hole = cv2.dilate(iris_full_u8, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11)))
        clip_u8 = poly_mask((H, W), clip_poly)
        iris_hole = cv2.bitwise_and(iris_hole, cv2.dilate(clip_u8, np.ones((3, 3), np.uint8)))

        sock_mean = sub_rgb[sock_m].mean(axis=0)
        sock_seed = body_rgb.copy()
        non_sock = np.ones((H, W), dtype=bool)
        non_sock[ey0:ey1, ex0:ex1] = ~sock_m
        sock_seed[non_sock] = np.round(sock_mean).astype(np.uint8)
        sock_inp = smooth_inpaint_rgb(sock_seed, iris_hole, radius=9, blur_sigma=2.4)
        ib = feather_mask(iris_hole, dilate_px=0, blur_px=1.0)[..., None]
        body_rgb = np.clip(
            np.round(body_rgb.astype(np.float32) * (1.0 - ib) + sock_inp.astype(np.float32) * ib),
            0, 255,
        ).astype(np.uint8)

        # Build clean unscaled flat ochre-gold iris sprite (padded upward under upper lid!)
        iris_rgba = np.zeros((H, W, 4), dtype=np.uint8)
        ell_m = np.zeros((H, W), dtype=np.uint8)
        cv2.ellipse(
            ell_m,
            (int(round(icx * 16)), int(round(icy * 16))),
            (int(round(irx * 16)), int(round(iry * 16))),
            iang, 0, 360, 255, -1, lineType=cv2.LINE_AA, shift=4,
        )
        # Union with exact original visible iris pixels inside socket so setup pose is 100% pixel-exact!
        vis_iris_u8 = cv2.bitwise_and(iris_full_u8, clip_u8)
        full_iris_u8 = cv2.bitwise_or(ell_m, vis_iris_u8)
        # Fill flat gouache ochre-gold [219, 169, 77] + subtle darker upper-lid rim shading, preserving original visible pixels
        iris_col = np.zeros((H, W, 3), dtype=np.float32)
        iris_col[...] = np.array([218.0, 168.0, 77.0], dtype=np.float32)
        orig_keep = feather_mask(vis_iris_u8, dilate_px=-1, blur_px=0.8)[..., None]
        iris_col = iris_col * (1.0 - orig_keep) + src[..., :3].astype(np.float32) * orig_keep
        iris_rgba[..., :3] = np.clip(np.round(iris_col), 0, 255).astype(np.uint8)
        iris_rgba[..., 3] = full_iris_u8
        save_trimmed(f"iris_{suf}", iris_rgba, meta)

        # Parallel-band upper eyelid sprite: generously wide (+55px each side, clipped by hardware eye_clip_*)!
        band_h = {"main": 88, "up": 64, "rt": 66}[suf]
        lid_top_pts = [(px, py - band_h) for px, py in lid_pts]
        lid_poly = lid_top_pts + list(reversed(lid_pts))
        lid_m = poly_mask((H, W), lid_poly)
        yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
        border_fade = np.ones((H, W), dtype=np.float32)

        # Clean flat STS2 gouache slate-teal value plane for upper eyelid + crisp dark lower lash crease
        v_grad = np.clip((yy - ly0) / max(float(ly1 - ly0), 1.0), 0.0, 1.0)[..., None]
        lid_base = (1.0 - v_grad) * np.array([83.0, 116.0, 122.0]) + v_grad * np.array([73.0, 104.0, 110.0])
        crease_line = np.zeros((H, W), dtype=np.uint8)
        cv2.polylines(
            crease_line,
            [np.round(np.asarray(lid_pts, dtype=np.float32)).astype(np.int32)],
            False, 255, thickness=2, lineType=cv2.LINE_AA,
        )
        crease_f = feather_mask(crease_line, dilate_px=0, blur_px=0.8)[..., None]
        lid_rgb_f = lid_base * (1.0 - 0.36 * crease_f) + np.array([44.0, 66.0, 71.0]) * (0.36 * crease_f)

        lid_rgba = np.zeros((H, W, 4), dtype=np.uint8)
        lid_rgba[..., :3] = np.clip(np.round(lid_rgb_f), 0, 255).astype(np.uint8)
        lid_rgba[..., 3] = np.clip(np.round(lid_m.astype(np.float32) * border_fade), 0, 255).astype(np.uint8)
        save_trimmed(f"lid_{suf}", lid_rgba, meta)

    body_dome_rgba = np.zeros((H, W, 4), dtype=np.uint8)
    body_dome_rgba[..., :3] = body_rgb
    body_dome_rgba[..., 3] = np.clip(np.round(body_al), 0, 255).astype(np.uint8)
    save_trimmed("body_dome", body_dome_rgba, meta)

    # -------------------------------------------------------------------------
    # 8. Soft Inner Volumetric Gel Mass (inner_gel.png, alpha ~ 95, away from eye sockets!)
    # -------------------------------------------------------------------------
    inner_m = cv2.erode((body_dome_rgba[..., 3] > 200).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (75, 75)))
    # Keep inner_gel out of the 3 eye sockets + 42px margin so sloshing never hazes the dark eye cavities!
    eye_guard = np.zeros((H, W), dtype=np.uint8)
    for cp in (EYE_CLIP_MAIN, EYE_CLIP_UP, EYE_CLIP_RT):
        eye_guard = cv2.bitwise_or(eye_guard, poly_mask((H, W), cp))
    eye_guard = cv2.dilate(eye_guard, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (85, 85)))
    inner_m = cv2.bitwise_and(inner_m, cv2.bitwise_not(eye_guard))
    inner_f = cv2.GaussianBlur(inner_m.astype(np.float32) / 255.0, (0, 0), 28.0)
    if inner_f.max() > 1e-6:
        inner_f = inner_f / inner_f.max()

    inner_rgba = np.zeros((H, W, 4), dtype=np.uint8)
    inner_col = cv2.GaussianBlur(body_dome_rgba[..., :3].astype(np.float32), (0, 0), 18.0) * np.array([0.80, 0.94, 0.98])
    inner_rgba[..., :3] = np.clip(np.round(inner_col), 0, 255).astype(np.uint8)
    inner_rgba[..., 3] = np.clip(np.round(inner_f * 95.0), 0, 95).astype(np.uint8)
    save_trimmed("inner_gel", inner_rgba, meta)

    # -------------------------------------------------------------------------
    # 9. Tri-Zone Prismatic Glow Overlay (prism_glow.png, matched to body_dome bbox!)
    # -------------------------------------------------------------------------
    bd = meta["body_dome"]
    glow_rgba = np.zeros((H, W, 4), dtype=np.uint8)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    w_crim = np.exp(-(((xx - 295.0) / 145.0) ** 2 + ((yy - 165.0) / 120.0) ** 2))
    w_ambr = np.exp(-(((xx - 585.0) / 165.0) ** 2 + ((yy - 155.0) / 130.0) ** 2))
    w_azur = np.exp(-(((xx - 770.0) / 155.0) ** 2 + ((yy - 385.0) / 135.0) ** 2))
    w_core = 0.35 * np.exp(-(((xx - 500.0) / 260.0) ** 2 + ((yy - 380.0) / 180.0) ** 2))
    g_rgb = (
        w_crim[..., None] * np.array([255.0, 95.0, 115.0])
        + w_ambr[..., None] * np.array([255.0, 190.0, 75.0])
        + w_azur[..., None] * np.array([110.0, 245.0, 235.0])
        + w_core[..., None] * np.array([150.0, 215.0, 220.0])
    )
    w_sum = np.maximum(w_crim + w_ambr + w_azur + w_core, 1e-4)[..., None]
    g_rgb = np.clip(g_rgb / w_sum, 0.0, 255.0)
    g_int = np.clip(w_crim + w_ambr + w_azur + w_core, 0.0, 1.0) * (body_dome_rgba[..., 3].astype(np.float32) / 255.0)
    glow_rgba[..., :3] = np.round(g_rgb).astype(np.uint8)
    glow_rgba[..., 3] = np.clip(np.round(g_int * 150.0), 0, 255).astype(np.uint8)
    save_trimmed(
        "prism_glow",
        glow_rgba,
        meta,
        fixed_box=(bd["x"], bd["y"], bd["x"] + bd["w"], bd["y"] + bd["h"]),
    )

    # -------------------------------------------------------------------------
    # 9b. 3 Hand-Painted Matte-Gouache Flying Projectiles (proj_crimson, proj_amber, proj_azure)
    # -------------------------------------------------------------------------
    build_projectiles(meta)

    with open(PARTS_DIR / "meta.json", "w", encoding="utf-8", newline="\n") as fp:
        json.dump(meta, fp, indent=2)
    print(f"Saved {len(meta)} parts to {PARTS_DIR}")

    # -------------------------------------------------------------------------
    # 10. Render 4-Panel Audit Sheet (prev/parts_audit.jpg)
    # -------------------------------------------------------------------------
    render_parts_audit(src, meta)


def _fill_aa_poly(canvas: np.ndarray, pts: list[tuple[float, float]], rgba: tuple[int, int, int, int]) -> None:
    H, W = canvas.shape[:2]
    SS = 4
    m = np.zeros((H * SS, W * SS), dtype=np.uint8)
    arr = np.round(np.asarray(pts, dtype=np.float32) * SS).astype(np.int32)
    cv2.fillPoly(m, [arr], 255, lineType=cv2.LINE_AA)
    ms = cv2.resize(m, (W, H), interpolation=cv2.INTER_AREA).astype(np.float32) / 255.0
    layer = np.zeros_like(canvas)
    layer[..., 0] = rgba[0]
    layer[..., 1] = rgba[1]
    layer[..., 2] = rgba[2]
    layer[..., 3] = np.clip(np.round(ms * rgba[3]), 0, 255).astype(np.uint8)
    canvas[:] = alpha_over(canvas, layer)


def _fill_aa_ellipse(
    canvas: np.ndarray,
    cx: float,
    cy: float,
    rx: float,
    ry: float,
    angle: float,
    rgba: tuple[int, int, int, int],
) -> None:
    H, W = canvas.shape[:2]
    SS = 4
    m = np.zeros((H * SS, W * SS), dtype=np.uint8)
    cv2.ellipse(
        m,
        (int(round(cx * SS)), int(round(cy * SS))),
        (max(1, int(round(rx * SS))), max(1, int(round(ry * SS)))),
        angle, 0, 360, 255, -1, lineType=cv2.LINE_AA,
    )
    ms = cv2.resize(m, (W, H), interpolation=cv2.INTER_AREA).astype(np.float32) / 255.0
    layer = np.zeros_like(canvas)
    layer[..., 0] = rgba[0]
    layer[..., 1] = rgba[1]
    layer[..., 2] = rgba[2]
    layer[..., 3] = np.clip(np.round(ms * rgba[3]), 0, 255).astype(np.uint8)
    canvas[:] = alpha_over(canvas, layer)


def build_projectiles(meta: dict) -> None:
    """Paint 3 chunky lineless matte-gouache STS2 slime projectiles (156x90px -> ~66x38px at 0.42 game scale!)."""
    S = 1.86
    PW, PH = int(round(84 * S)), int(round(48 * S))  # 156 x 89

    def sc_pts(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
        return [(x * S, y * S) for x, y in pts]

    # 1. proj_crimson: Jagged carmine-gel shard glob (origin inside brow_crimson: x=192, y=136)
    pc = np.zeros((PH, PW, 4), dtype=np.uint8)
    _fill_aa_poly(
        pc,
        sc_pts([(13, 25), (20, 12), (35, 10), (53, 16), (69, 22), (72, 25), (54, 33), (34, 39), (19, 36)]),
        (118, 45, 38, 255),
    )
    _fill_aa_poly(
        pc,
        sc_pts([(15, 24), (21, 13), (35, 11), (52, 17), (66, 22), (50, 29), (33, 33), (19, 31)]),
        (170, 79, 62, 255),
    )
    _fill_aa_poly(
        pc,
        sc_pts([(20, 17), (31, 13), (45, 16), (38, 20), (24, 20)]),
        (210, 112, 86, 255),
    )
    _fill_aa_poly(pc, sc_pts([(3, 24), (17, 14), (33, 18), (30, 31), (15, 33)]), (74, 15, 30, 255))
    _fill_aa_poly(pc, sc_pts([(3, 24), (17, 14), (31, 19), (20, 25)]), (118, 31, 52, 255))
    _fill_aa_poly(pc, sc_pts([(4, 24), (17, 15), (22, 18), (11, 24)]), (214, 140, 150, 255))
    _fill_aa_poly(pc, sc_pts([(34, 15), (42, 10), (48, 16), (40, 21)]), (96, 22, 40, 255))
    _fill_aa_poly(pc, sc_pts([(35, 15), (42, 11), (44, 14)]), (196, 118, 128, 255))
    _fill_aa_ellipse(pc, 76.0 * S, 21.0 * S, 4.2 * S, 2.6 * S, -8.0, (158, 70, 56, 255))
    _fill_aa_ellipse(pc, 79.5 * S, 28.0 * S, 2.6 * S, 1.8 * S, 5.0, (132, 54, 45, 255))
    Image.fromarray(pc).save(PARTS_DIR / "proj_crimson.png", optimize=True)
    meta["proj_crimson"] = {"x": 192, "y": 136, "w": PW, "h": PH}

    # 2. proj_amber: Golden-amber stone-flecked glob (origin inside hump_amber: x=484, y=88)
    pa = np.zeros((PH, PW, 4), dtype=np.uint8)
    _fill_aa_poly(
        pa,
        sc_pts([(8, 25), (14, 13), (29, 9), (48, 14), (68, 22), (71, 25), (52, 34), (31, 40), (14, 36)]),
        (142, 82, 18, 255),
    )
    _fill_aa_poly(
        pa,
        sc_pts([(10, 24), (15, 14), (29, 10), (47, 15), (65, 23), (48, 30), (29, 34), (14, 31)]),
        (192, 122, 33, 255),
    )
    _fill_aa_ellipse(pa, 25.0 * S, 15.5 * S, 10.5 * S, 3.6 * S, -10.0, (240, 186, 80, 255))
    _fill_aa_poly(pa, sc_pts([(15, 24), (22, 16), (34, 17), (36, 26), (26, 31), (17, 29)]), (108, 93, 70, 255))
    _fill_aa_poly(pa, sc_pts([(16, 23), (22, 16), (33, 17), (29, 23), (19, 25)]), (158, 139, 109, 255))
    _fill_aa_poly(pa, sc_pts([(18, 20), (23, 17), (30, 18), (25, 20)]), (194, 176, 144, 255))
    _fill_aa_poly(pa, sc_pts([(39, 22), (45, 18), (51, 21), (49, 27), (41, 27)]), (122, 106, 80, 255))
    _fill_aa_poly(pa, sc_pts([(40, 22), (45, 18), (50, 21), (45, 23)]), (168, 149, 118, 255))
    _fill_aa_ellipse(pa, 76.0 * S, 24.0 * S, 4.5 * S, 2.8 * S, 0.0, (186, 116, 30, 255))
    _fill_aa_ellipse(pa, 75.0 * S, 23.0 * S, 2.4 * S, 1.2 * S, 0.0, (236, 180, 76, 255))
    Image.fromarray(pa).save(PARTS_DIR / "proj_amber.png", optimize=True)
    meta["proj_amber"] = {"x": 484, "y": 88, "w": PW, "h": PH}

    # 3. proj_azure: Bubbly seafoam-cyan acid glob (origin inside lobe_azure: x=676, y=326)
    pz = np.zeros((PH, PW, 4), dtype=np.uint8)
    _fill_aa_poly(
        pz,
        sc_pts([(8, 26), (14, 14), (30, 11), (49, 16), (67, 23), (69, 26), (50, 34), (30, 39), (14, 36)]),
        (52, 110, 114, 255),
    )
    _fill_aa_poly(
        pz,
        sc_pts([(10, 25), (15, 15), (30, 12), (48, 17), (64, 24), (47, 30), (29, 34), (14, 31)]),
        (88, 172, 169, 255),
    )
    for bx, by, br in [(19.0, 22.0, 8.2), (30.5, 18.0, 7.2), (31.0, 28.5, 6.2), (42.5, 22.5, 5.4)]:
        _fill_aa_ellipse(pz, bx * S, by * S, (br + 1.2) * S, (br + 1.0) * S, 0.0, (58, 124, 126, 255))
        _fill_aa_ellipse(pz, bx * S, by * S, br * S, br * 0.94 * S, 0.0, (118, 204, 198, 255))
        _fill_aa_ellipse(pz, (bx - br * 0.30) * S, (by - br * 0.30) * S, br * 0.36 * S, br * 0.28 * S, -20.0, (206, 250, 244, 255))
    for bx, by, br in [(73.5, 21.5, 3.8), (79.0, 27.5, 2.7), (66.5, 31.5, 2.5)]:
        _fill_aa_ellipse(pz, bx * S, by * S, (br + 0.8) * S, (br + 0.8) * S, 0.0, (58, 124, 126, 255))
        _fill_aa_ellipse(pz, bx * S, by * S, br * S, br * S, 0.0, (132, 216, 210, 255))
    Image.fromarray(pz).save(PARTS_DIR / "proj_azure.png", optimize=True)
    meta["proj_azure"] = {"x": 676, "y": 326, "w": PW, "h": PH}


def render_parts_audit(src: np.ndarray, meta: dict) -> None:
    H, W = src.shape[:2]
    CW, CH = W, H + 30

    def blank_panel() -> np.ndarray:
        p = np.zeros((CH, CW, 4), dtype=np.uint8)
        p[..., 0] = 30
        p[..., 1] = 34
        p[..., 2] = 42
        p[..., 3] = 255
        return p

    def place_part(canvas: np.ndarray, name: str, dx: int = 0, dy: int = 0, clip_poly: list | None = None) -> np.ndarray:
        im = np.array(Image.open(PARTS_DIR / f"{name}.png").convert("RGBA"))
        m = meta[name]
        layer = np.zeros((CH, CW, 4), dtype=np.uint8)
        x0, y0 = m["x"] + dx, m["y"] + dy
        x1, y1 = x0 + m["w"], y0 + m["h"]
        cx0, cy0 = max(0, x0), max(0, y0)
        cx1, cy1 = min(CW, x1), min(CH, y1)
        if cx1 > cx0 and cy1 > cy0:
            layer[cy0:cy1, cx0:cx1] = im[cy0 - y0 : cy1 - y0, cx0 - x0 : cx1 - x0]
        if clip_poly is not None:
            cm = poly_mask((CH, CW), clip_poly).astype(np.float32) / 255.0
            layer[..., 3] = np.round(layer[..., 3].astype(np.float32) * cm).astype(np.uint8)
        return alpha_over(canvas, layer)

    # Panel 1: Original v4 Cutout
    p1 = blank_panel()
    src_pad = np.zeros((CH, CW, 4), dtype=np.uint8)
    src_pad[:H, :W] = src
    p1 = alpha_over(p1, src_pad)

    # Panel 2: Full Reassembled Rig Parts (with hardware CLIPS simulated on eyes)
    draw_order_open = [
        ("shadow", 0, 0, None),
        ("drop_far", 0, 0, None),
        ("body_dome", 0, 0, None),
        ("inner_gel", 0, 0, None),
        ("amber_goo_cap", 0, 0, None),
        ("iris_main", 0, 0, EYE_CLIP_MAIN),
        ("lid_main", 0, 0, EYE_CLIP_MAIN),
        ("iris_up", 0, 0, EYE_CLIP_UP),
        ("lid_up", 0, 0, EYE_CLIP_UP),
        ("iris_rt", 0, 0, EYE_CLIP_RT),
        ("lid_rt", 0, 0, EYE_CLIP_RT),
        ("crimson_shard_l", 0, 0, None),
        ("crimson_shard_tall", 0, 0, None),
        ("crimson_shard_r", 0, 0, None),
        ("amber_rock_l", 0, 0, None),
        ("amber_rock_main", 0, 0, None),
        ("amber_rock_r", 0, 0, None),
        ("azure_froth_main", 0, 0, None),
        ("azure_froth_spores", 0, 0, None),
        ("drop_near", 0, 0, None),
    ]
    p2 = blank_panel()
    for n, dx, dy, cp in draw_order_open:
        p2 = place_part(p2, n, dx, dy, cp)

    # Panel 3: Bare body_dome + amber_goo_cap (verifying ZERO rock/crystal/froth/iris/drop ghosting!)
    p3 = blank_panel()
    p3 = place_part(p3, "shadow")
    p3 = place_part(p3, "body_dome")
    p3 = place_part(p3, "amber_goo_cap")

    # Panel 4: Full 3-Eye Closed Blink State + 3 Matte-Gouache Projectiles Showcase
    p4 = blank_panel()
    blink_shifts = {"lid_main": 60, "lid_up": 41, "lid_rt": 43}
    for n, dx, dy, cp in draw_order_open:
        p4 = place_part(p4, n, dx, dy + blink_shifts.get(n, 0), cp)
    p4 = place_part(p4, "proj_crimson", dx=-172, dy=-82)
    p4 = place_part(p4, "proj_amber", dx=-464, dy=52)
    p4 = place_part(p4, "proj_azure", dx=-656, dy=-98)

    titles = [
        "1. Original v4 Cutout (1082x635)",
        "2. Reassembled 20-Part Rig (Hardware Eye CLIPS)",
        "3. Bare body_dome + amber_goo_cap (0 Ghosting Audit)",
        "4. Full 3-Eye Closed Blink State (Unscaled Lids + CLIPS)",
    ]
    panels = []
    for im, title in zip([p1, p2, p3, p4], titles):
        rgb_p = im[..., :3].copy()
        cv2.putText(rgb_p, title, (18, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.85, (245, 245, 220), 2, cv2.LINE_AA)
        panels.append(rgb_p)

    top = np.concatenate([panels[0], panels[1]], axis=1)
    bot = np.concatenate([panels[2], panels[3]], axis=1)
    grid = np.concatenate([top, bot], axis=0)
    out_path = PREV_DIR / "parts_audit.jpg"
    Image.fromarray(grid).save(out_path, quality=92)
    print("Rendered", out_path)


if __name__ == "__main__":
    main()
