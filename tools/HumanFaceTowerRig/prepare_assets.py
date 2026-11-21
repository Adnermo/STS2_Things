#!/usr/bin/env python3
"""Prepare maintainable source parts & layered room textures for Human Face Tower (人面塔)."""
from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
BRAIN = Path(r"C:\Users\adner\.gemini\antigravity\brain\630b9439-8513-49e2-8959-201a92e08ca5")

SRC_DIR = ROOT / "source_assets" / "monsters" / "human_face_tower"
MASTERS_DIR = SRC_DIR / "masters"
PARTS_DIR = SRC_DIR / "parts"
ROOM_DIR = ROOT / "images" / "rooms" / "human_face_tower"

for d in (MASTERS_DIR, PARTS_DIR, ROOM_DIR):
    d.mkdir(parents=True, exist_ok=True)


def write_godot_png_import(png_path: Path) -> None:
    """Write deterministic Godot 4.5 lossless Texture2D .import sidecar."""
    rel_res = "res://" + png_path.relative_to(ROOT).as_posix()
    digest = hashlib.md5(rel_res.encode("utf-8")).hexdigest()
    uid_seed = hashlib.sha1(rel_res.encode("utf-8")).hexdigest()[:12]
    ctex = f"res://.godot/imported/{png_path.name}-{digest}.ctex"
    content = f"""[remap]

importer="texture"
type="CompressedTexture2D"
uid="uid://hft{uid_seed}"
path="{ctex}"
metadata={{
"vram_texture": false
}}

[deps]

source_file="{rel_res}"
dest_files=["{ctex}"]

[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
"""
    png_path.with_suffix(".png.import").write_text(content, encoding="utf-8")


def remove_magenta_bg(img: Image.Image, *, erode_px: int = 1, blur_alpha: float = 0.6) -> Image.Image:
    """Exterior-connected + pure-magenta-hole chroma key with fringe-only despill."""
    rgba = np.asarray(img.convert("RGBA"), dtype=np.float32).copy()
    r, g, b = rgba[:, :, 0], rgba[:, :, 1], rgba[:, :, 2]
    dist_to_ff00ff = np.sqrt((255.0 - r) ** 2 + g**2 + (255.0 - b) ** 2)
    magenta_excess = np.minimum(r, b) - g
    is_bg_candidate = ((dist_to_ff00ff < 115.0) | (magenta_excess > 62.0)).astype(np.uint8)

    _, labels = cv2.connectedComponents(is_bg_candidate, connectivity=8)
    border_labels = (
        set(np.unique(labels[0, :]))
        | set(np.unique(labels[-1, :]))
        | set(np.unique(labels[:, 0]))
        | set(np.unique(labels[:, -1]))
    )
    border_labels.discard(0)
    pure_magenta_holes = (dist_to_ff00ff < 72.0) & (r > 205.0) & (g < 48.0) & (b > 205.0)
    exterior_bg = np.isin(labels, list(border_labels)) | pure_magenta_holes

    fg_mask = (~exterior_bg).astype(np.uint8) * 255
    if erode_px > 0:
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * erode_px + 1, 2 * erode_px + 1))
        fg_mask = cv2.erode(fg_mask, kernel, iterations=1)

    alpha_img = Image.fromarray(fg_mask, mode="L")
    if blur_alpha > 0:
        alpha_img = alpha_img.filter(ImageFilter.GaussianBlur(radius=blur_alpha))
    final_alpha = np.asarray(alpha_img, dtype=np.float32) / 255.0

    dilated_bg = cv2.dilate(exterior_bg.astype(np.uint8), np.ones((7, 7), np.uint8), iterations=1) > 0
    fringe_zone = dilated_bg & (~exterior_bg)
    rb_avg = 0.5 * (r + b)
    spill = np.maximum(0.0, rb_avg - (g + 10.0))
    despill_w = np.where(fringe_zone, 0.90, 0.0)

    out = np.zeros_like(rgba, dtype=np.uint8)
    out[:, :, 0] = np.clip(r - spill * despill_w, 0.0, 255.0).astype(np.uint8)
    out[:, :, 1] = np.clip(g + spill * despill_w * 0.20, 0.0, 255.0).astype(np.uint8)
    out[:, :, 2] = np.clip(b - spill * despill_w, 0.0, 255.0).astype(np.uint8)
    out[:, :, 3] = np.where(final_alpha < 0.04, 0, (final_alpha * 255.0)).astype(np.uint8)
    return Image.fromarray(out, mode="RGBA")


def crop_tight(rgba: Image.Image, box: tuple[int, int, int, int], pad: int = 4) -> Image.Image:
    sub = rgba.crop(box)
    bbox = sub.getbbox()
    if bbox is None:
        return sub
    return sub.crop(
        (
            max(0, bbox[0] - pad),
            max(0, bbox[1] - pad),
            min(sub.width, bbox[2] + pad),
            min(sub.height, bbox[3] + pad),
        )
    )


def soft_perimeter_alpha(rgba: Image.Image, feather_px: float = 9.0) -> Image.Image:
    arr = np.asarray(rgba, dtype=np.float32).copy()
    mask = (arr[:, :, 3] > 50).astype(np.uint8)
    dist = cv2.distanceTransform(mask, cv2.DIST_L2, 5)
    t = np.clip(dist / feather_px, 0.0, 1.0)
    s = t * t * (3.0 - 2.0 * t)
    arr[:, :, 3] *= s
    return Image.fromarray(arr.astype(np.uint8), mode="RGBA")


def wall_slot_bounds(xs: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Exact measured pixel bounds of the concave stone wall band in 1920x1080 stage coordinates."""
    dx_top = np.abs(xs - 960.0) / 860.0
    dx_bot = np.abs(xs - 960.0) / 800.0
    y_top = 385.0 - 242.0 * np.power(dx_top, 2.06)
    y_bot = 690.0 + 32.0 * np.power(dx_bot, 2.00)
    return y_top, y_bot


def build_room_layers(arena_master: Path) -> None:
    """Split 1920x1080 arena into bg_chamber, fg_frame (cornice + sill + boulders), and light_shaft."""
    arena = Image.open(arena_master).convert("RGBA").resize((1920, 1080), Image.Resampling.LANCZOS)
    arr = np.asarray(arena, dtype=np.float32)
    yy, xx = np.mgrid[0:1080, 0:1920].astype(np.float32)
    y_top, y_bot = wall_slot_bounds(xx)

    # 1. Background chamber (darkened recessed track groove where the Spine cylinder sits)
    in_slot = (yy >= y_top - 6.0) & (yy <= y_bot + 6.0)
    bg_arr = arr.copy()
    bg_arr[in_slot, 0] *= 0.28
    bg_arr[in_slot, 1] *= 0.30
    bg_arr[in_slot, 2] *= 0.29
    bg_img = Image.fromarray(np.clip(bg_arr, 0, 255).astype(np.uint8), mode="RGBA")
    bg_out = ROOM_DIR / "tower_bg_chamber.png"
    bg_img.save(bg_out)
    write_godot_png_import(bg_out)

    # 2. Foreground architectural frame (overhanging cornice + stepped track sill & plaza + foreground dark boulders)
    # Keep ONLY the solid foreground rock silhouettes bounded by their exact hand-painted slope envelopes!
    luma = 0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]
    left_env = np.where(xx <= 96.0, 504.0 + 0.25 * xx, 528.0 + 5.2 * (xx - 96.0))
    right_env = 578.0 + 2.12 * np.maximum(0.0, 1919.0 - xx)
    left_rock = (xx < 182.0) & (yy > left_env) & (luma < 50.0)
    right_rock = (xx > 1765.0) & (yy > right_env) & (luma < 50.0)
    rock_cand = (left_rock | right_rock | (yy > 888.0)).astype(np.uint8)
    rock_cand = cv2.morphologyEx(rock_cand * 255, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
    rock_cand = cv2.morphologyEx(rock_cand, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (19, 19)))
    _, rlabels = cv2.connectedComponents((rock_cand > 127).astype(np.uint8), connectivity=8)
    keep_ids = {int(rlabels[1079, 0]), int(rlabels[1079, 1919])} - {0}
    boulders_u8 = (np.isin(rlabels, list(keep_ids)).astype(np.uint8)) * 255
    # Fill interior holes inside the foreground boulders
    flood = boulders_u8.copy()
    hmask = np.zeros((1082, 1922), dtype=np.uint8)
    cv2.floodFill(flood, hmask, (960, 200), 255)
    boulders_filled = cv2.bitwise_or(boulders_u8, cv2.bitwise_not(flood))
    boulders = cv2.GaussianBlur(boulders_filled, (5, 5), 1.1).astype(np.float32) / 255.0

    # Smooth 3px anti-aliased architectural lip at y_top (cornice overhang) and y_bot (stepped sill)
    above_cornice = np.clip((y_top - yy + 1.5) / 3.0, 0.0, 1.0)
    below_sill = np.clip((yy - y_bot + 1.5) / 3.0, 0.0, 1.0)
    frame_alpha = np.clip(np.maximum.reduce([above_cornice, below_sill, boulders]), 0.0, 1.0)

    # Subtle cornice overhang shadow cast downward (14px soft shadow band just below y_top)
    overhang_shadow = np.clip(1.0 - np.maximum(0.0, yy - y_top) / 16.0, 0.0, 1.0) * (yy >= y_top) * (1.0 - boulders)
    shadow_alpha = overhang_shadow * 0.48

    fg_arr = np.zeros((1080, 1920, 4), dtype=np.float32)
    combined_a = np.clip(frame_alpha + shadow_alpha * (1.0 - frame_alpha), 0.0, 1.0)
    # RGB comes from arena on opaque frame, dark slate shadow on overhang shadow fringe
    fg_rgb = arr[:, :, :3] * frame_alpha[:, :, None] + np.array([16.0, 21.0, 19.0])[None, None, :] * (
        shadow_alpha * (1.0 - frame_alpha)
    )[:, :, None]
    safe_div = np.maximum(combined_a[:, :, None], 1e-4)
    fg_arr[:, :, :3] = np.where(combined_a[:, :, None] > 0.005, fg_rgb / safe_div, 0.0)
    fg_arr[:, :, 3] = combined_a * 255.0
    fg_out = ROOM_DIR / "tower_fg_frame.png"
    Image.fromarray(np.clip(fg_arr, 0, 255).astype(np.uint8), mode="RGBA").save(fg_out)
    write_godot_png_import(fg_out)

    # 3. Soft volumetric skylight shaft overlay (drapes gentle cool-warm dust beam across centered face)
    beam_x = np.exp(-0.5 * ((xx - 960.0) / 250.0) ** 2)
    beam_y = np.clip(1.0 - np.abs(yy - 460.0) / 420.0, 0.0, 1.0)
    beam_a = beam_x * beam_y * 0.14
    light_arr = np.zeros((1080, 1920, 4), dtype=np.uint8)
    light_arr[:, :, 0] = 228
    light_arr[:, :, 1] = 236
    light_arr[:, :, 2] = 218
    light_arr[:, :, 3] = np.clip(beam_a * 255.0, 0, 255).astype(np.uint8)
    light_out = ROOM_DIR / "tower_light_shaft.png"
    Image.fromarray(light_arr, mode="RGBA").save(light_out)
    write_godot_png_import(light_out)

    # 4. Seamless foreground stone & cave-boulder bottom skirt (1920x180) so lifting the chamber stage
    #    above the player's hand cards never exposes clear-color, seam steps, or mirror-diamond artifacts.
    skirt_h = 180
    sy, sx = np.mgrid[0:skirt_h, 0:1920].astype(np.float32)
    strip_u8 = np.clip(arr[1080 - skirt_h : 1080, :, :3], 0, 255).astype(np.uint8)
    strip_luma = (
        0.299 * strip_u8[:, :, 0].astype(np.float32)
        + 0.587 * strip_u8[:, :, 1].astype(np.float32)
        + 0.114 * strip_u8[:, :, 2].astype(np.float32)
    )

    # Separate strip into pure-boulder (floor inpainted away) and pure-floor (boulders inpainted away)
    # BEFORE vertical reflection: preserves C1 brushstroke continuity across y=1079 while eliminating floating rock diamonds!
    non_rock_mask = cv2.dilate(((strip_luma > 56.0).astype(np.uint8)) * 255, np.ones((5, 5), np.uint8))
    rock_only_above = cv2.inpaint(strip_u8, non_rock_mask, 18, cv2.INPAINT_TELEA)
    rock_refl = rock_only_above[::-1].astype(np.float32)

    is_rock_mask = cv2.dilate(((strip_luma <= 62.0).astype(np.uint8)) * 255, np.ones((7, 7), np.uint8))
    floor_only_above = cv2.inpaint(strip_u8, is_rock_mask, 22, cv2.INPAINT_TELEA)
    floor_refl_raw = floor_only_above[::-1].astype(np.float32)
    floor_refl_soft = cv2.GaussianBlur(floor_refl_raw, (31, 31), 9.5)
    soft_mix = np.clip(sy / 14.0, 0.0, 1.0)[:, :, None]
    floor_refl = floor_refl_raw * (1.0 - soft_mix) + floor_refl_soft * soft_mix

    # Gently damp contrast & darken downward into natural foreground cave shadow (exact 1.0 at sy=0)
    floor_atten = np.exp(-sy / 72.0)[:, :, None]
    floor_cont = floor_refl * floor_atten + np.array([16.0, 19.0, 20.0])[None, None, :] * (1.0 - floor_atten)
    rock_atten = np.exp(-sy / 95.0)[:, :, None]
    rock_cont = rock_refl * rock_atten + np.array([12.0, 15.0, 16.0])[None, None, :] * (1.0 - rock_atten)

    # Every rock column at y=1079 stays rock and widens outward with sy (gravity-grounded boulder bases, never pinching!)
    seam_luma = strip_luma[-1]
    seam_rock_bin = (seam_luma < 60.0).astype(np.uint8)
    dist_from_rock = cv2.distanceTransform(1 - seam_rock_bin, cv2.DIST_L2, 5)[0 if seam_rock_bin.ndim == 1 else slice(None)]
    edge_wobble = 3.5 * np.sin(sx / 29.0 + sy / 17.0) + 2.0 * np.cos(sx / 13.0 - sy / 11.0)
    widen_px = sy * 0.85 + edge_wobble * np.clip(sy / 8.0, 0.0, 1.0)
    rock_mask = np.clip(1.0 - (dist_from_rock - widen_px) / 2.2, 0.0, 1.0)
    # Exact soft rock/floor transition on sy=0 matching y=1079
    rock_mask[0] = np.clip((64.0 - seam_luma) / 14.0, 0.0, 1.0)

    skirt_rgb = floor_cont * (1.0 - rock_mask[:, :, None]) + rock_cont * rock_mask[:, :, None]
    skirt_rgb[0, :, :] = arr[1079, :, :3]

    skirt_rgba = np.zeros((skirt_h, 1920, 4), dtype=np.uint8)
    skirt_rgba[:, :, :3] = np.clip(skirt_rgb, 0.0, 255.0).astype(np.uint8)
    skirt_rgba[:, :, 3] = 255
    skirt_out = ROOM_DIR / "tower_bottom_skirt.png"
    Image.fromarray(skirt_rgba, mode="RGBA").save(skirt_out)
    write_godot_png_import(skirt_out)


def build_spine_parts(faces_master: Path, wall_master: Path, arena_master: Path) -> None:
    """Slice and prepare overlap-safe Spine parts for the 360° wall ring and all 4 articulated faces."""
    arena_1080 = Image.open(arena_master).convert("RGBA").resize((1920, 1080), Image.Resampling.LANCZOS)
    faces_rgba = remove_magenta_bg(Image.open(faces_master), erode_px=1, blur_alpha=0.6)
    fw, fh = faces_rgba.size

    wrath_raw = crop_tight(faces_rgba, (0, 0, fw // 2, fh // 2), pad=2).resize((540, 324), Image.Resampling.LANCZOS)
    lament_raw = crop_tight(faces_rgba, (fw // 2, 0, fw, fh // 2), pad=2).resize((540, 324), Image.Resampling.LANCZOS)
    bastion_raw = crop_tight(faces_rgba, (0, fh // 2, fw // 2, fh), pad=2).resize((540, 324), Image.Resampling.LANCZOS)
    mockery_raw = crop_tight(faces_rgba, (fw // 2, fh // 2, fw, fh), pad=2).resize((540, 324), Image.Resampling.LANCZOS)

    # Color-grade all faces gently once so their ambient value & contrast match tower_interior_arena_v1
    def clean_moss_stone_rgb(a: np.ndarray) -> np.ndarray:
        """Eliminate any trace of warm/magenta chroma-key spill on grey-green stone."""
        r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
        # Only suppress pixels where both R and B exceed G (magenta/pink cast), preserving golden/ochre highlights where R > G > B!
        pink_cast = np.maximum(0.0, np.minimum(r, b) - g + 2.0)
        a[:, :, 0] = np.clip(r - 0.85 * pink_cast, 0, 255)
        a[:, :, 1] = np.clip(g + 0.25 * pink_cast, 0, 255)
        a[:, :, 2] = np.clip(b - 0.90 * pink_cast, 0, 255)
        return a

    def match_arena_stone(im: Image.Image, *, feather_px: float = 9.0) -> Image.Image:
        a = clean_moss_stone_rgb(np.asarray(im, dtype=np.float32).copy())
        a[:, :, 0] = np.clip(a[:, :, 0] * 0.91, 0, 255)
        a[:, :, 1] = np.clip(a[:, :, 1] * 0.94, 0, 255)
        a[:, :, 2] = np.clip(a[:, :, 2] * 0.91, 0, 255)
        out_im = Image.fromarray(a.astype(np.uint8), mode="RGBA")
        return soft_perimeter_alpha(out_im, feather_px=feather_px) if feather_px > 0 else out_im

    wrath = match_arena_stone(wrath_raw)
    lament = match_arena_stone(lament_raw)
    bastion = match_arena_stone(bastion_raw)
    mockery = match_arena_stone(mockery_raw)

    wall_rgba = remove_magenta_bg(Image.open(wall_master), erode_px=1, blur_alpha=0.6)
    ww, wh = wall_rgba.size
    pilaster_l = crop_tight(wall_rgba, (0, 0, int(ww * 0.12), int(wh * 0.50)), pad=0)
    pilaster_r = crop_tight(wall_rgba, (int(ww * 0.88), 0, ww, int(wh * 0.50)), pad=0)
    wall_panel = crop_tight(wall_rgba, (int(ww * 0.12), 0, int(ww * 0.88), int(wh * 0.50)), pad=0)
    shattered_raw = crop_tight(wall_rgba, (0, int(wh * 0.50), int(ww * 0.66), wh), pad=0)
    clamp_split_l = match_arena_stone(crop_tight(wall_rgba, (int(ww * 0.67), int(wh * 0.76), int(ww * 0.835), wh), pad=2))
    clamp_split_r = match_arena_stone(crop_tight(wall_rgba, (int(ww * 0.835), int(wh * 0.76), ww, wh), pad=2))

    # --- Build seamless 90° Wall Sector Strip (1600 x 360) with 5 EQUAL 18.0° (320px) architectural modules! ---
    # Exact cylindrical geometry R_CYL = 1720 * sin(theta):
    #   - Viewport edges x=0..1920 sit at +-34.2°, so Face 0 (+-7.76°) slides 100% OFF-SCREEN past x=0 at phi=-42°
    #     while STILL 100% OPAQUE (ZERO on-screen alpha fade or sudden pop!), and Face 1 enters past x=1920 at phi=-48°!
    #   - 5 Pilasters per 90° (44px = 2.475° each):
    #       Pil 0 wrap: [1578..1600] + [0..22] (-45.0°)
    #       Pil 1:      [298..342] (-27.0° -> projects to x=146..212 at rest, matching arena_master!)
    #       Pil 2:      [618..662] (-9.0°  -> projects to x=654..728 at rest, matching arena_master!)
    #       Pil 3:      [938..982] (+9.0°  -> projects to x=1192..1266 at rest, matching arena_master!)
    #       Pil 4:      [1258..1302] (+27.0° -> projects to x=1708..1774 at rest, matching arena_master!)
    #   - 5 Equal Masonry Bays (276px = 15.525° each):
    #       Bay 0 [22..298], Bay 1 [342..618], Central Face Bay 2 [662..938], Bay 3 [982..1258], Bay 4 [1302..1578]
    sec_w, sec_h = 1600, 360
    bay_w, pil_w = 276, 44
    arena_np = np.asarray(arena_1080, dtype=np.float32)

    def unwrap_cyl_bay(theta0_deg: float, theta1_deg: float, out_w: int, out_h: int, *, top_inset: float = 8.0) -> np.ndarray:
        """Rectify curved cylindrical bay from arena_master accounting for R=1720*sin(theta) & 3-point dome tilt."""
        thetas = np.radians(np.linspace(theta0_deg, theta1_deg, out_w, dtype=np.float32))[None, :]
        v = np.linspace(0.0, 1.0, out_h, dtype=np.float32)[:, None]
        sin_t = np.sin(thetas)
        map_x = (960.0 + 1720.0 * sin_t + (v - 0.45) * 38.0 * sin_t).astype(np.float32)
        yt, yb = wall_slot_bounds(map_x)
        yt_brick = yt + top_inset
        yb_brick = yb - 1.0
        map_y = (yt_brick + v * (yb_brick - yt_brick)).astype(np.float32)
        rect = cv2.remap(arena_np, map_x, map_y, interpolation=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REFLECT_101)
        rect[:, :, 3] = 255.0
        return rect

    def normalize_bay_lighting(bay_rgba: np.ndarray, target_rgb: np.ndarray, ref_row_luma: np.ndarray) -> np.ndarray:
        """Remove stationary skylight & cornice-groove falloff while preserving crisp hand-painted brick mortar."""
        rgb = bay_rgba[:, :, :3].copy()
        col_luma = rgb.mean(axis=(0, 2)).astype(np.float32)[None, :]
        col_smooth = cv2.GaussianBlur(col_luma, (71, 1), 24.0)[:, :, None]
        leveled = rgb * (float(col_smooth.mean()) / np.maximum(col_smooth, 1.0))
        row_luma = leveled.mean(axis=(1, 2)).astype(np.float32)[:, None]
        row_smooth = cv2.GaussianBlur(row_luma, (1, 51), 16.0)[:, :, None]
        leveled *= ref_row_luma / np.maximum(row_smooth, 1.0)
        cur_mean = leveled.mean(axis=(0, 1), keepdims=True)
        matched = np.clip(leveled * (target_rgb / np.maximum(cur_mean, 1.0)), 0, 255)
        return matched

    raw_center = unwrap_cyl_bay(-7.7, 7.7, bay_w, sec_h, top_inset=8.0)
    raw_left = unwrap_cyl_bay(-25.5, -10.5, bay_w, sec_h, top_inset=34.0)
    raw_right = unwrap_cyl_bay(10.5, 25.5, bay_w, sec_h, top_inset=34.0)
    pil_l_tex = unwrap_cyl_bay(-10.2, -7.8, pil_w, sec_h, top_inset=8.0)
    pil_r_tex = unwrap_cyl_bay(7.8, 10.2, pil_w, sec_h, top_inset=8.0)

    target_stone_rgb = (raw_center[:, :, :3].mean(axis=(0, 1), keepdims=True) * 0.90).astype(np.float32)
    center_row_ref = cv2.GaussianBlur(raw_center[:, :, :3].mean(axis=(1, 2)).astype(np.float32)[:, None], (1, 51), 16.0)[:, :, None]
    bay_center_rgb = normalize_bay_lighting(raw_center, target_stone_rgb, center_row_ref)
    # Build all 4 flanking bays with 100% aligned horizontal stone courses (y untouched!) + varied horizontal
    # brick stagger & low-frequency organic cavern moss tint from raw_left / raw_right -> zero wavy mortar!
    moss_tint_l = cv2.GaussianBlur(normalize_bay_lighting(raw_left, target_stone_rgb, center_row_ref), (55, 55), 18.0) / np.maximum(
        cv2.GaussianBlur(bay_center_rgb, (55, 55), 18.0), 8.0
    )
    moss_tint_r = cv2.GaussianBlur(normalize_bay_lighting(raw_right, target_stone_rgb, center_row_ref), (55, 55), 18.0) / np.maximum(
        cv2.GaussianBlur(bay_center_rgb, (55, 55), 18.0), 8.0
    )
    moss_tint_l = np.clip(0.35 + 0.65 * moss_tint_l, 0.86, 1.14)
    moss_tint_r = np.clip(0.35 + 0.65 * moss_tint_r, 0.86, 1.14)

    bay_left_rgb = np.clip(bay_center_rgb[:, ::-1, :] * moss_tint_l, 0, 255)
    bay_right_rgb = np.clip(
        cv2.resize(bay_center_rgb[:, 20:276, :], (bay_w, sec_h), interpolation=cv2.INTER_CUBIC) * moss_tint_r,
        0,
        255,
    )
    bay_outer_l = np.clip(
        cv2.resize(bay_center_rgb[:, 0:254, :][:, ::-1, :], (bay_w, sec_h), interpolation=cv2.INTER_CUBIC) * moss_tint_r,
        0,
        255,
    )
    bay_outer_r = np.clip(
        cv2.resize(bay_center_rgb[:, 12:266, :], (bay_w, sec_h), interpolation=cv2.INTER_CUBIC) * moss_tint_l,
        0,
        255,
    )

    pil_l_rgb = np.clip(pil_l_tex[:, :, :3] * 0.91, 0, 255)
    pil_r_rgb = np.clip(pil_r_tex[:, :, :3] * 0.91, 0, 255)
    pil_mid_rgb = 0.5 * (pil_l_rgb + pil_r_rgb)

    sec_np = np.zeros((sec_h, sec_w, 4), dtype=np.float32)
    sec_np[:, :, 3] = 255.0
    sec_np[:, 22:298, :3] = bay_outer_l
    sec_np[:, 342:618, :3] = bay_left_rgb
    sec_np[:, 662:938, :3] = bay_center_rgb  # Clean 7-course stone bay (each face mesh spans [662..938] seamlessly!)
    sec_np[:, 982:1258, :3] = bay_right_rgb
    sec_np[:, 1302:1578, :3] = bay_outer_r

    yy_s, xx_s = np.mgrid[0:sec_h, 0:sec_w].astype(np.float32)

    # Mount all 5 pilasters (including wrap-around pilaster at [1578..1600] + [0..22]) with subtle cast side-shadows!
    pil_spans = [(298, 342, pil_l_rgb), (618, 662, pil_l_rgb), (938, 982, pil_r_rgb), (1258, 1302, pil_r_rgb)]
    for px0, px1, _ in pil_spans:
        pshad = 0.22 * np.exp(-0.5 * ((xx_s - px0) / 8.0) ** 2) * (xx_s < px0) + 0.22 * np.exp(
            -0.5 * ((xx_s - px1) / 8.0) ** 2
        ) * (xx_s > px1)
        sec_np[:, :, :3] *= (1.0 - pshad)[:, :, None]
    # Wrap-around pilaster cast shadows at x=22 and x=1578
    wrap_shad = 0.22 * np.exp(-0.5 * ((xx_s - 22.0) / 8.0) ** 2) * (xx_s > 22.0) + 0.22 * np.exp(
        -0.5 * ((xx_s - 1578.0) / 8.0) ** 2
    ) * (xx_s < 1578.0)
    sec_np[:, :, :3] *= (1.0 - wrap_shad)[:, :, None]

    for px0, px1, ptex in pil_spans:
        sec_np[:, px0:px1, :3] = ptex
    sec_np[:, 1578:1600, :3] = pil_mid_rgb[:, 0:22, :]
    sec_np[:, 0:22, :3] = pil_mid_rgb[:, 22:44, :]

    sec_np[:, :, 3] = 255.0
    sector_intact = Image.fromarray(np.clip(sec_np, 0, 255).astype(np.uint8), mode="RGBA")
    sector_intact.save(PARTS_DIR / "wall_sector_intact.png")

    # Shattered sector: collapsed blind rubble socket torn organically out of central bay [662..938]!
    sector_shattered = sector_intact.copy()
    shat_resized = shattered_raw.resize((272, 282), Image.Resampling.LANCZOS)
    shat_arr = clean_moss_stone_rgb(np.asarray(shat_resized, dtype=np.float32).copy())
    sh_luma = 0.299 * shat_arr[:, :, 0] + 0.587 * shat_arr[:, :, 1] + 0.114 * shat_arr[:, :, 2]
    pit_keep = np.clip((68.0 - sh_luma) / 34.0, 0.0, 1.0)[:, :, None]
    outer_ring = sh_luma > 72.0
    sh_mean = shat_arr[:, :, :3][outer_ring].mean(axis=0).reshape(1, 1, 3)
    bay_mean = bay_center_rgb.mean(axis=(0, 1), keepdims=True)
    sh_matched = np.clip(shat_arr[:, :, :3] * (bay_mean / np.maximum(sh_mean, 1.0)), 0, 255)
    shat_arr[:, :, :3] = sh_matched * (1.0 - 0.80 * pit_keep) + shat_arr[:, :, :3] * 0.86 * (0.80 * pit_keep)
    yy_sh, xx_sh = np.mgrid[0:282, 0:272].astype(np.float32)
    r_sh = np.sqrt(((xx_sh - 136.0) / 130.0) ** 2 + ((yy_sh - 141.0) / 134.0) ** 2)
    t_sh = np.clip((1.0 - r_sh) / 0.30, 0.0, 1.0)
    shat_arr[:, :, 3] *= t_sh * t_sh * (3.0 - 2.0 * t_sh)
    sector_shattered.alpha_composite(Image.fromarray(np.clip(shat_arr, 0, 255).astype(np.uint8), mode="RGBA"), (664, 28))
    shat_np = np.asarray(sector_shattered, dtype=np.float32).copy()
    for px0, px1, ptex in pil_spans:
        shat_np[:, px0:px1, :3] = ptex
    sector_shattered = Image.fromarray(np.clip(shat_np, 0, 255).astype(np.uint8), mode="RGBA")
    sector_shattered.save(PARTS_DIR / "wall_sector_shattered.png")

    # =========================================================================
    # FUSE ALL 4 SOVEREIGN FACES DIRECTLY INTO THE 7-COURSE MASONRY BAY (540x360)
    # =========================================================================
    # Following the mod's approved 「人面柱」 DNA (assembled_disc.png):
    #   1) Strip the isolated head's outer cylindrical drum rims (top slice rim, convex side walls, bottom rim)
    #      so ZERO plaque/shield border exists — brows, eyes, nose, cheeks & mouth emerge directly from flat bricks!
    #   2) Harmonize stone hue to the exact central bay [662..938, 0..360] while preserving bold 3D chiseled
    #      relief contrast, deep eye/mouth cavities, and mineral accents (ochre crack, moss tears, gold keystone).
    #   3) Carve continuous 3D-bulged 7-course brick mortar joints & stone block bevels right across the forehead,
    #      temples, brow plates, nose bridge, cheekbones, and chin (protecting eyeballs/pupils) so the wall IS the face!
    center_bay_with_shad = sec_np[:, 662:938, :3].copy()  # (360, 276, 3) including flanking pilaster cast shadows
    base_wall_540 = cv2.resize(center_bay_with_shad, (540, 360), interpolation=cv2.INTER_CUBIC)
    wall_mean = base_wall_540.mean(axis=(0, 1), keepdims=True)
    wall_std = base_wall_540.std(axis=(0, 1), keepdims=True)

    # Extract crisp 7-course brick mortar lines & mossy stone grain from base_wall_540
    wall_smooth = cv2.GaussianBlur(base_wall_540, (27, 27), 8.0)
    brick_ratio = np.clip(base_wall_540 / np.maximum(wall_smooth, 8.0), 0.62, 1.32)
    yy_b, xx_b = np.mgrid[0:360, 0:540].astype(np.float32)

    # Radially bulge brick mortar courses outward over the protruding 3D nose/brow/cheek relief (+16px stone warp!)
    rx_n = (xx_b - 270.0) / 215.0
    ry_n = (yy_b - 184.0) / 136.0
    bulge = np.exp(-1.35 * (rx_n * rx_n + ry_n * ry_n))
    warp_x = (xx_b - (xx_b - 270.0) * 0.095 * bulge).astype(np.float32)
    warp_y = (yy_b - (yy_b - 184.0) * 0.110 * bulge).astype(np.float32)
    bulged_brick_ratio = cv2.remap(
        brick_ratio, warp_x, warp_y, interpolation=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REFLECT_101
    )
    mortar_luma_ratio = bulged_brick_ratio.mean(axis=2)
    mortar_groove = np.clip((0.955 - mortar_luma_ratio) / 0.115, 0.0, 1.0)
    mortar_lip_down = np.zeros_like(mortar_groove)
    mortar_lip_down[3:, :] = mortar_groove[:-3, :] * (1.0 - mortar_groove[3:, :])

    # Outer 12px bay-edge alpha feather (where pixels are pure base_wall_540) -> 0.00% lighting seam on cylinder!
    edge_fx = np.clip(np.minimum(xx_b, 539.0 - xx_b) / 12.0, 0.0, 1.0)
    edge_fy = np.clip(np.minimum(yy_b, 359.0 - yy_b) / 12.0, 0.0, 1.0)
    bay_edge_alpha = (edge_fx * edge_fx * (3.0 - 2.0 * edge_fx)) * (edge_fy * edge_fy * (3.0 - 2.0 * edge_fy))

    def sstep(x: np.ndarray) -> np.ndarray:
        c = np.clip(x, 0.0, 1.0)
        return c * c * (3.0 - 2.0 * c)

    # Prepare Bastion's raw 324p clamp mask (tight rounded brackets!) & 3D-sculpted unbolted stone lips + cheek mortise sockets!
    b_raw324 = np.asarray(bastion, dtype=np.uint8).copy()
    cy3, cx3 = np.mgrid[0:84, 0:292].astype(np.float32)
    bar_band = (np.abs(cy3 - 39.5) <= 14.5) & (cx3 >= 24.0) & (cx3 <= 268.0)
    left_br = ((np.maximum(0.0, np.abs(cx3 - 29.0) - 9.0) / 5.5) ** 2 + (np.maximum(0.0, np.abs(cy3 - 39.5) - 18.5) / 6.0) ** 2) <= 1.0
    right_br = ((np.maximum(0.0, np.abs(cx3 - 263.0) - 9.0) / 5.5) ** 2 + (np.maximum(0.0, np.abs(cy3 - 39.5) - 18.5) / 6.0) ** 2) <= 1.0
    diamond_m = (np.abs(cx3 - 145.0) / 33.5 + np.abs(cy3 - 41.0) / 38.5) <= 1.0
    clamp_mask_sub324 = cv2.GaussianBlur(((bar_band | left_br | right_br | diamond_m).astype(np.uint8)) * 255, (5, 5), 1.0)

    inpaint_box324 = ((cy3 >= 5) & (cy3 <= 75) & (cx3 >= 4) & (cx3 <= 288)) | diamond_m
    inpaint_sub = cv2.dilate(inpaint_box324.astype(np.uint8) * 255, np.ones((9, 9), np.uint8), iterations=1)
    full_mask_324 = np.zeros((324, 540), dtype=np.uint8)
    full_mask_324[194:278, 122:414] = inpaint_sub
    # Smooth multi-scale normalized convolution fill (zero horizontal Navier-Stokes streak banding!)
    valid_w = (1.0 - (full_mask_324.astype(np.float32) / 255.0))[:, :, None]
    src_f = b_raw324[:, :, :3].astype(np.float32)
    num_coarse = cv2.GaussianBlur(src_f * valid_w, (0, 0), 18.0)
    den_coarse = np.maximum(cv2.GaussianBlur(valid_w[:, :, 0], (0, 0), 18.0)[:, :, None], 1e-4)
    fill_coarse = num_coarse / den_coarse
    num_mid = cv2.GaussianBlur(src_f * valid_w, (0, 0), 7.5)
    den_mid = np.maximum(cv2.GaussianBlur(valid_w[:, :, 0], (0, 0), 7.5)[:, :, None], 1e-4)
    fill_mid = np.where(den_mid > 0.18, num_mid / den_mid, fill_coarse)
    blend_m324 = (cv2.GaussianBlur(full_mask_324, (19, 19), 5.5).astype(np.float32) / 255.0)[:, :, None]
    clean_rgb324 = src_f * (1.0 - blend_m324) + (0.65 * fill_mid + 0.35 * fill_coarse) * blend_m324
    # Subtle isotropic stone pore micro-grain (zero horizontal forehead/brick line ghosting!)
    rng_stone = np.random.default_rng(20261115)
    pore_noise = cv2.GaussianBlur(rng_stone.normal(0.0, 2.2, (84, 292)).astype(np.float32), (3, 3), 0.7)[:, :, None]
    clean_rgb324[194:278, 122:414, :] += pore_noise * blend_m324[194:278, 122:414, :]

    # Sculpt 3D volumetric chiseled stone upper & lower lips in 324p space (maps to y540 = 244.25!)
    yy324, xx324 = np.mgrid[0:324, 0:540].astype(np.float32)
    lip_u = (xx324 - 269.0) / 86.0
    lip_span = 1.0 - sstep((np.abs(lip_u) - 0.74) / 0.26)
    seam_y324 = 234.5 - 2.4 * np.cos(np.clip(lip_u, -1.0, 1.0) * np.pi) + 1.6 * np.exp(-0.5 * (lip_u / 0.18) ** 2)
    dy_seam = yy324 - seam_y324

    # Upper lip sunlit ridge + shaded overhanging upper lip plane
    up_ridge = np.exp(-0.5 * ((dy_seam + 7.8) / 3.2) ** 2) * lip_span * (1.0 - 0.30 * np.exp(-0.5 * (lip_u / 0.15) ** 2))
    up_under = np.exp(-0.5 * ((dy_seam + 2.4) / 2.4) ** 2) * lip_span
    philtrum_sh = np.exp(-0.5 * ((xx324 - 269.0) / 7.0) ** 2) * sstep((yy324 - 208.0) / 6.0) * (1.0 - sstep((yy324 - 228.0) / 5.0))
    # Lower lip overhead-lit top plane + rounded labiomental chin groove shadow + subtle corner dimples
    lo_top_lit = np.exp(-0.5 * ((dy_seam - 4.8) / 3.0) ** 2) * (1.0 - sstep((np.abs(lip_u) - 0.66) / 0.26))
    lo_chin_sh = np.exp(-0.5 * ((dy_seam - 13.8) / 4.5) ** 2) * (1.0 - sstep((np.abs(lip_u) - 0.70) / 0.28))
    corner_dimple = (
        np.exp(-0.5 * (((np.abs(xx324 - 269.0) - 76.0) / 6.5) ** 2 + ((yy324 - 236.5) / 5.0) ** 2))
    )
    cleft_dark = np.exp(-0.5 * (dy_seam / 1.8) ** 2) * (1.0 - sstep((np.abs(lip_u) - 0.84) / 0.16))

    sculpt_delta = (
        14.0 * up_ridge
        - 21.0 * up_under
        - 9.0 * philtrum_sh
        + 18.0 * lo_top_lit
        - 19.0 * lo_chin_sh
        - 16.0 * corner_dimple
    )
    clean_rgb324 = np.clip(clean_rgb324 + sculpt_delta[:, :, None], 0, 255)
    clean_rgb324 = clean_rgb324 * (1.0 - 0.72 * cleft_dark[:, :, None]) + np.array([28.0, 34.0, 30.0])[None, None, :] * (
        0.72 * cleft_dark[:, :, None]
    )

    unbolted_324_arr = b_raw324.copy()
    unbolted_324_arr[:, :, :3] = np.clip(clean_rgb324, 0, 255).astype(np.uint8)
    bastion_unbolted_324 = Image.fromarray(unbolted_324_arr, mode="RGBA")

    # Map Bastion's clamp mask into 540x360 bay space ([30:326, 18:522] = 504x296)
    clamp_canvas_324 = np.zeros((324, 540), dtype=np.uint8)
    clamp_canvas_324[194:278, 122:414] = clamp_mask_sub324
    clamp_sub_296 = cv2.resize(clamp_canvas_324, (504, 296), interpolation=cv2.INTER_LINEAR)
    clamp_mask_540 = np.zeros((360, 540), dtype=np.float32)
    clamp_mask_540[30:326, 18:522] = clamp_sub_296.astype(np.float32) / 255.0

    # Eye-socket & feature protection masks so brick mortar joints never slice through eyeballs, teeth, or lips!
    eye_l = np.exp(-0.5 * (((xx_b - 168.0) / 56.0) ** 2 + ((yy_b - 156.0) / 24.0) ** 2))
    eye_r = np.exp(-0.5 * (((xx_b - 372.0) / 56.0) ** 2 + ((yy_b - 156.0) / 24.0) ** 2))
    eye_protect = np.clip(np.maximum(eye_l, eye_r) * 1.15, 0.0, 1.0)

    wrath_teeth_prot = (1.0 - sstep((np.abs(xx_b - 270.0) - 122.0) / 18.0)) * sstep((yy_b - 214.0) / 8.0) * (
        1.0 - sstep((yy_b - 290.0) / 8.0)
    )
    lament_mouth_prot = (1.0 - sstep((np.abs(xx_b - 269.0) - 94.0) / 16.0)) * sstep((yy_b - 224.0) / 8.0) * (
        1.0 - sstep((yy_b - 286.0) / 8.0)
    )
    bastion_lips_prot = (1.0 - sstep((np.abs(xx_b - 269.0) - 118.0) / 18.0)) * sstep((yy_b - 226.0) / 8.0) * (
        1.0 - sstep((yy_b - 272.0) / 8.0)
    )
    mockery_feat_prot = (1.0 - sstep((np.abs(xx_b - 286.0) - 142.0) / 20.0)) * sstep((yy_b - 196.0) / 10.0) * (
        1.0 - sstep((yy_b - 254.0) / 10.0)
    )

    def fuse_face_into_masonry_bay(
        face_324: Image.Image,
        *,
        face_kind: str,
        protect_mortar: np.ndarray | None = None,
    ) -> Image.Image:
        """Sculpt a 540x360 full-bay stone relief where facial features grow directly out of the 7 brick courses."""
        f_sub = np.asarray(face_324.resize((504, 296), Image.Resampling.LANCZOS), dtype=np.float32)
        f_canvas = np.zeros((360, 540, 4), dtype=np.float32)
        f_canvas[30:326, 18:522] = f_sub

        raw_rgb = f_canvas[:, :, :3].copy()
        raw_a = f_canvas[:, :, 3] / 255.0
        sil_u8 = (raw_a > 0.25).astype(np.uint8)
        dist_in = cv2.distanceTransform(sil_u8, cv2.DIST_L2, 5)

        # 1. Lift convex-drum right-side shadow from raw cutout so lighting is overhead-planar like the wall
        f_luma0 = 0.299 * raw_rgb[:, :, 0] + 0.587 * raw_rgb[:, :, 1] + 0.114 * raw_rgb[:, :, 2]
        inner_cavity = sstep((74.0 - f_luma0) / 34.0) * sstep((dist_in - 30.0) / 18.0)
        right_drum_lift = 1.0 + 0.20 * sstep((xx_b - 340.0) / 130.0) * (1.0 - inner_cavity)
        lifted_rgb = np.clip(raw_rgb * right_drum_lift[:, :, None], 0, 255)

        chroma = lifted_rgb.max(axis=2) - lifted_rgb.min(axis=2)
        accent_keep = sstep((chroma - 20.0) / 18.0) * sstep((dist_in - 18.0) / 16.0)

        core_sample = (dist_in > 42.0) & (f_luma0 > 62.0) & (chroma < 26.0)
        f_mean = lifted_rgb[core_sample].mean(axis=0).reshape(1, 1, 3)
        f_std = np.maximum(lifted_rgb[core_sample].std(axis=0).reshape(1, 1, 3), 8.0)

        # Keep bold 3D sculptural relief contrast (like assembled_disc.png!) while matching the wall's mossy stone hue!
        target_std = np.maximum(wall_std * 1.92, f_std * 0.84)
        quarry_rgb = (lifted_rgb - f_mean) * (target_std / f_std) + (wall_mean * 1.02)
        keep_orig = np.maximum(0.84 * inner_cavity, 0.90 * accent_keep)[:, :, None]
        quarry_rgb = np.clip(quarry_rgb * (1.0 - keep_orig) + lifted_rgb * 0.92 * keep_orig, 0, 255)

        # 2. Full-expression anatomical relief mask: 100% solid across entire brows, eyes, nose, cheeks & mouth,
        #    while STILL melting to 0.0 before the unwanted top drum rim (y<56), side drum walls & bottom rim!
        nx = (xx_b - 270.0) / 220.0
        ny = np.where(yy_b < 188.0, (yy_b - 188.0) / 130.0, (yy_b - 188.0) / 128.0)
        r_anat = np.power(np.abs(nx) ** 2.6 + np.abs(ny) ** 2.6, 1.0 / 2.6)
        anat_w = 1.0 - sstep((r_anat - 0.68) / 0.30)

        if face_kind == "wrath":
            crack_lobe = np.exp(-0.5 * ((xx_b - 266.0) / 48.0) ** 2) * sstep((yy_b - 44.0) / 16.0) * sstep((128.0 - yy_b) / 24.0)
            jaw_lobe = (1.0 - sstep((np.abs(xx_b - 270.0) - 140.0) / 42.0)) * sstep((yy_b - 190.0) / 24.0) * (1.0 - sstep((yy_b - 290.0) / 24.0))
            anat_w = np.maximum.reduce([anat_w, 0.92 * crack_lobe, 0.98 * jaw_lobe])
        elif face_kind == "lament":
            tear_l = np.exp(-0.5 * ((xx_b - 131.0) / 22.0) ** 2) * sstep((yy_b - 148.0) / 20.0) * (1.0 - sstep((yy_b - 310.0) / 16.0))
            tear_r = np.exp(-0.5 * ((xx_b - 415.0) / 22.0) ** 2) * sstep((yy_b - 148.0) / 20.0) * (1.0 - sstep((yy_b - 310.0) / 16.0))
            anat_w = np.maximum.reduce([anat_w, 0.92 * tear_l, 0.92 * tear_r])
        elif face_kind in ("bastion_bolted", "bastion_unbolted"):
            clamp_lobe = (1.0 - sstep((np.abs(xx_b - 270.0) - 142.0) / 34.0)) * sstep((yy_b - 196.0) / 18.0) * (1.0 - sstep((yy_b - 280.0) / 22.0))
            anat_w = np.maximum(anat_w, 0.98 * clamp_lobe)
        elif face_kind == "mockery":
            smirk_lobe = (1.0 - sstep((np.abs(xx_b - 276.0) - 152.0) / 38.0)) * sstep((yy_b - 190.0) / 22.0) * (1.0 - sstep((yy_b - 286.0) / 24.0))
            anat_w = np.maximum(anat_w, 0.96 * smirk_lobe)

        perim_safe = sstep((dist_in - 14.0) / 30.0)
        blend_w = (anat_w * perim_safe)[:, :, None]

        # 3. Overhead relief ambient shadow cast onto bricks directly beneath cheekbones & chin
        relief_mass = (anat_w * perim_safe).astype(np.float32)
        shifted_down = np.zeros_like(relief_mass)
        shifted_down[14:, :] = relief_mass[:-14, :]
        under_ao = cv2.GaussianBlur(np.maximum(0.0, shifted_down - relief_mass * 0.88), (25, 25), 7.5)
        shaded_wall = base_wall_540 * (1.0 - 0.25 * under_ao[:, :, None])

        # 4. Blend sculpted stone features out of shaded_wall + carve continuous 7-course brick mortar across the face!
        fused = shaded_wall * (1.0 - blend_w) + quarry_rgb * blend_w

        prot = protect_mortar if protect_mortar is not None else np.zeros((360, 540), dtype=np.float32)
        no_mortar = np.clip(
            np.maximum.reduce([0.95 * inner_cavity, 0.90 * eye_protect, 0.88 * accent_keep, 0.95 * prot]),
            0.0,
            1.0,
        )
        mortar_w = (1.0 - no_mortar) * (1.0 - 0.18 * blend_w[:, :, 0])
        mortar_mul = 1.0 + (bulged_brick_ratio - 1.0) * mortar_w[:, :, None] * 1.18
        fused = fused * mortar_mul
        crease_boost = blend_w[:, :, 0] * (1.0 - no_mortar)
        fused *= (1.0 - 0.15 * mortar_groove * crease_boost)[:, :, None]
        fused += (9.0 * mortar_lip_down * crease_boost)[:, :, None]
        fused = np.clip(fused, 0, 255)

        out = np.zeros((360, 540, 4), dtype=np.uint8)
        out[:, :, :3] = fused.astype(np.uint8)
        out[:, :, 3] = np.clip(bay_edge_alpha * 255.0, 0, 255).astype(np.uint8)
        return Image.fromarray(out, mode="RGBA")

    wrath = fuse_face_into_masonry_bay(wrath, face_kind="wrath", protect_mortar=wrath_teeth_prot)
    lament = fuse_face_into_masonry_bay(lament, face_kind="lament", protect_mortar=lament_mouth_prot)
    bastion_bolted_fused = fuse_face_into_masonry_bay(bastion, face_kind="bastion_bolted", protect_mortar=clamp_mask_540)
    bastion_unbolted_fused = fuse_face_into_masonry_bay(
        bastion_unbolted_324, face_kind="bastion_unbolted", protect_mortar=bastion_lips_prot
    )
    mockery = fuse_face_into_masonry_bay(mockery, face_kind="mockery", protect_mortar=mockery_feat_prot)

    part_layout: dict[str, dict[str, object]] = {}

    def save_sub_part(full_540: np.ndarray, name: str, box: tuple[int, int, int, int]) -> None:
        x0, y0, x1, y1 = box
        sub = np.clip(full_540[y0:y1, x0:x1], 0, 255).astype(np.uint8)
        Image.fromarray(sub, mode="RGBA").save(PARTS_DIR / f"{name}.png")
        part_layout[name] = {"sub_box": [x0, y0, x1, y1], "size": [x1 - x0, y1 - y0]}

    # =========================================================================
    # FACE 0: WRATH (嗔怒面) — Continuous Cheeks/Lips + Dual-Row Teeth Along Exact Bite Seam (y≈257)
    # =========================================================================
    # Zero rectangular cheek cuts & zero black censor bar:
    #   - wrath_upper.png keeps the ENTIRE 540x360 face, cheeks, upper lip, mouth corners, lower lip & chin intact,
    #     opening an alpha aperture strictly inside the inner lip rim!
    #   - wrath_teeth_upper.png & wrath_teeth_lower.png separate the 4 upper & 4 lower stone teeth right along the
    #     measured inter-dental bite groove y_bite(x) ≈ 256..258, with shaded stone roots tucked behind the lips!
    #   - wrath_throat_bg.png provides a deep sculpted stone oral cavity behind both tooth rows & lateral corners!
    w_arr = np.asarray(wrath, dtype=np.float32)
    w_luma = 0.299 * w_arr[:, :, 0] + 0.587 * w_arr[:, :, 1] + 0.114 * w_arr[:, :, 2]

    y_lip_top = (
        224.2
        - 5.6 * (np.sin(np.clip((xx_b - 154.0) / 232.0, 0.0, 1.0) * np.pi) ** 2)
        + 4.4 * np.exp(-0.5 * ((xx_b - 270.0) / 34.0) ** 2)
        + 7.5 * (np.clip((np.abs(xx_b - 270.0) - 96.0) / 30.0, 0.0, 1.5) ** 2)
    )
    y_lip_bot = (
        285.2
        + 1.6 * np.sin(np.clip((xx_b - 144.0) / 252.0, 0.0, 1.0) * np.pi)
        - 3.2 * (np.clip((np.abs(xx_b - 270.0) - 104.0) / 26.0, 0.0, 1.5) ** 2)
    )
    mouth_half_w = 113.0 + 13.0 * np.clip((yy_b - 222.0) / 62.0, 0.0, 1.0)
    mouth_in_x = sstep((mouth_half_w - np.abs(xx_b - 270.0)) / 2.2)
    mouth_in_y = sstep((yy_b - y_lip_top) / 1.8) * sstep((y_lip_bot - yy_b) / 1.8)
    wrath_mouth_aperture = mouth_in_x * mouth_in_y

    wrath_upper = w_arr.copy()
    wrath_upper[:, :, 3] = w_arr[:, :, 3] * (1.0 - wrath_mouth_aperture)
    Image.fromarray(np.clip(wrath_upper, 0, 255).astype(np.uint8), mode="RGBA").save(PARTS_DIR / "wrath_upper.png")

    # Deep sculpted stone oral gullet behind teeth & inner lip corners
    throat_w = np.zeros((360, 540, 4), dtype=np.float32)
    gullet_rx = (xx_b - 270.0) / 138.0
    gullet_ry = (yy_b - 256.0) / 58.0
    gullet_a = 1.0 - sstep((np.sqrt(gullet_rx * gullet_rx + gullet_ry * gullet_ry) - 0.80) / 0.18)
    # Match lateral corner tone (~35,42,37) near x=152/388 and deepen to cavernous slate (15,19,17) in central throat
    depth_core = np.exp(-0.5 * (((xx_b - 270.0) / 86.0) ** 2 + ((yy_b - 256.0) / 28.0) ** 2))
    rib_wave = 0.08 * np.cos((xx_b - 270.0) * (2.0 * np.pi / 48.0)) * depth_core
    throat_w[:, :, 0] = 35.0 - 20.0 * depth_core + 18.0 * rib_wave
    throat_w[:, :, 1] = 42.0 - 23.0 * depth_core + 20.0 * rib_wave
    throat_w[:, :, 2] = 37.0 - 20.0 * depth_core + 18.0 * rib_wave
    throat_w[:, :, 3] = gullet_a * 255.0
    save_sub_part(throat_w, "wrath_throat_bg", (126, 192, 414, 324))

    # Exact inter-dental bite groove y_bite(x) ≈ 256..258 & sculpted 4-tooth horizontal span
    y_bite = 257.4 - 1.6 * (((xx_b - 270.0) / 96.0) ** 2)
    # Outer left/right tooth borders follow painted stone tooth silhouette (excluding dark lateral mouth corners!)
    teeth_span_x = sstep((105.0 - np.abs(xx_b - 270.5)) / 2.2)
    side_corner_zone = np.abs(xx_b - 270.5) > 92.0
    side_tooth_keep = np.where(side_corner_zone, sstep((w_luma - 54.0) / 16.0), 1.0)
    teeth_horiz_a = teeth_span_x * side_tooth_keep

    # Upper teeth row: visible crown [y_lip_top .. y_bite] + 20px upward shaded stone tooth roots behind upper lip!
    t_up = np.zeros((360, 540, 4), dtype=np.float32)
    sample_y_up = np.where(yy_b < y_lip_top + 2.0, np.clip(y_lip_top + 3.5, 0.0, 359.0), yy_b).astype(np.float32)
    up_rgb = cv2.remap(w_arr[:, :, :3], xx_b, sample_y_up, interpolation=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT_101)
    root_t_up = np.clip((y_lip_top + 2.0 - yy_b) / 20.0, 0.0, 1.0)
    up_root_shade = 1.0 - 0.48 * (root_t_up**0.85)
    bite_bevel_up = 1.0 - 0.16 * np.exp(-0.5 * ((yy_b - (y_bite - 0.8)) / 1.1) ** 2)
    t_up[:, :, :3] = up_rgb * (up_root_shade * bite_bevel_up)[:, :, None]
    up_vert_a = sstep((yy_b - (y_lip_top - 19.0)) / 4.0) * sstep((y_bite + 0.45 - yy_b) / 1.2)
    t_up[:, :, 3] = teeth_horiz_a * up_vert_a * 255.0
    save_sub_part(t_up, "wrath_teeth_upper", (158, 198, 384, 264))

    # Lower teeth row: visible crown [y_bite .. y_lip_bot] + 22px downward shaded stone tooth roots behind lower lip!
    t_lo = np.zeros((360, 540, 4), dtype=np.float32)
    sample_y_lo = np.where(yy_b > y_lip_bot - 2.0, np.clip(y_lip_bot - 3.5, 0.0, 359.0), yy_b).astype(np.float32)
    lo_rgb = cv2.remap(w_arr[:, :, :3], xx_b, sample_y_lo, interpolation=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT_101)
    root_t_lo = np.clip((yy_b - (y_lip_bot - 2.0)) / 22.0, 0.0, 1.0)
    lo_root_shade = 1.0 - 0.50 * (root_t_lo**0.85)
    bite_bevel_lo = 1.0 - 0.14 * np.exp(-0.5 * ((yy_b - (y_bite + 0.8)) / 1.1) ** 2)
    t_lo[:, :, :3] = lo_rgb * (lo_root_shade * bite_bevel_lo)[:, :, None]
    lo_vert_a = sstep((yy_b - (y_bite - 0.45)) / 1.2) * sstep((y_lip_bot + 21.0 - yy_b) / 4.5)
    t_lo[:, :, 3] = teeth_horiz_a * lo_vert_a * 255.0
    save_sub_part(t_lo, "wrath_teeth_lower", (158, 250, 384, 314))

    # Remove obsolete wrath_jaw_lower.png if left over from earlier iteration
    old_jaw = PARTS_DIR / "wrath_jaw_lower.png"
    if old_jaw.exists():
        old_jaw.unlink()

    # Exact painted forehead ochre-mineral crack glow (zero hand-drawn line offset!)
    crack_roi = (xx_b >= 198.0) & (xx_b <= 346.0) & (yy_b >= 34.0) & (yy_b <= 130.0)
    ochre_sig = np.clip(((w_arr[:, :, 0] - w_arr[:, :, 2]) - 15.0) / 20.0, 0.0, 1.0) * sstep(
        (w_arr[:, :, 0] - w_arr[:, :, 1] + 2.0) / 12.0
    )
    ochre_core = (ochre_sig * crack_roi.astype(np.float32)).astype(np.float32)
    ochre_halo = cv2.GaussianBlur(ochre_core, (13, 13), 3.4)
    ochre_a = np.clip(0.85 * ochre_core + 0.70 * ochre_halo, 0.0, 1.0)
    w_glow = np.zeros((360, 540, 4), dtype=np.float32)
    w_glow[:, :, 0] = 248.0
    w_glow[:, :, 1] = 128.0 + 48.0 * ochre_core
    w_glow[:, :, 2] = 48.0 + 28.0 * ochre_core
    w_glow[:, :, 3] = ochre_a * 240.0
    save_sub_part(w_glow, "wrath_crack_glow", (192, 30, 352, 136))

    # =========================================================================
    # FACE 1: LAMENT (悲叹面) — True Wailing Mouth Cavity + Exact Moss Tear Channel Glow
    # =========================================================================
    lam_arr = np.asarray(lament, dtype=np.float32)
    lam_luma = 0.299 * lam_arr[:, :, 0] + 0.587 * lam_arr[:, :, 1] + 0.114 * lam_arr[:, :, 2]
    lam_mouth_roi = (xx_b >= 180.0) & (xx_b <= 358.0) & (yy_b >= 229.0) & (yy_b <= 283.0)
    lam_dark_raw = ((lam_luma < 61.0) & lam_mouth_roi).astype(np.uint8) * 255
    lam_dark_eroded = cv2.erode(lam_dark_raw, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)), iterations=1)
    lam_aperture = cv2.GaussianBlur(lam_dark_eroded, (5, 5), 1.3).astype(np.float32) / 255.0

    lam_face_out = lam_arr.copy()
    lam_face_out[:, :, 3] = lam_arr[:, :, 3] * (1.0 - lam_aperture)
    Image.fromarray(np.clip(lam_face_out, 0, 255).astype(np.uint8), mode="RGBA").save(PARTS_DIR / "lament_face.png")

    lam_throat = np.zeros((360, 540, 4), dtype=np.float32)
    lt_rx = (xx_b - 269.0) / 98.0
    lt_ry = (yy_b - 258.0) / 42.0
    lt_a = 1.0 - sstep((np.sqrt(lt_rx * lt_rx + lt_ry * lt_ry) - 0.80) / 0.18)
    lt_deep = np.exp(-0.5 * (((xx_b - 269.0) / 58.0) ** 2 + ((yy_b - 256.0) / 20.0) ** 2))
    lam_throat[:, :, 0] = 36.0 - 19.0 * lt_deep
    lam_throat[:, :, 1] = 43.0 - 22.0 * lt_deep
    lam_throat[:, :, 2] = 38.0 - 19.0 * lt_deep
    lam_throat[:, :, 3] = lt_a * 255.0
    save_sub_part(lam_throat, "lament_throat_bg", (164, 212, 376, 304))

    # Extract exact painted green moss tear channels on left (x≈128..146) & right (x≈398..418) cheeks
    # Use horizontal difference-of-Gaussians so ONLY the narrow olive-moss tear streaks light up (zero corridor box!)
    moss_raw = lam_arr[:, :, 1] - 0.50 * lam_arr[:, :, 0] - 0.50 * lam_arr[:, :, 2]
    moss_bg = cv2.GaussianBlur(moss_raw, (51, 1), 15.0)
    luma_dip = np.maximum(0.0, cv2.GaussianBlur(lam_luma, (45, 1), 13.0) - lam_luma)
    streak_sig = np.clip(((moss_raw - moss_bg) * 1.4 + 0.12 * luma_dip - 2.2) / 7.5, 0.0, 1.0)
    tear_env_x = np.maximum(
        np.exp(-0.5 * ((xx_b - 135.0) / 13.0) ** 2),
        np.exp(-0.5 * ((xx_b - 409.0) / 13.0) ** 2),
    )
    tear_env_y = sstep((yy_b - 150.0) / 14.0) * (1.0 - sstep((yy_b - 306.0) / 14.0))
    tear_core = streak_sig * tear_env_x * tear_env_y
    tear_halo = cv2.GaussianBlur(tear_core, (13, 13), 3.2)
    tear_a = np.clip(0.90 * tear_core + 0.62 * tear_halo, 0.0, 1.0)
    l_glow = np.zeros((360, 540, 4), dtype=np.float32)
    l_glow[:, :, 0] = 96.0 + 42.0 * tear_core
    l_glow[:, :, 1] = 224.0 + 24.0 * tear_core
    l_glow[:, :, 2] = 184.0 + 32.0 * tear_core
    l_glow[:, :, 3] = tear_a * 225.0
    save_sub_part(l_glow, "lament_tear_glow", (108, 142, 438, 326))

    # =========================================================================
    # FACE 2: BASTION (缄默面) — Sculpted Stone Lips + Mortises + Exact Split Clamp & Keystone Glow
    # =========================================================================
    bas_u_arr = np.asarray(bastion_unbolted_fused, dtype=np.float32)
    # Exact mapped lip cleft at y540 = 30 + 234.5 * (296/324) = 244.24 (aligned to 0.01px with sculpted seam_y324!)
    b_lip_u = (xx_b - 269.8) / 80.3
    b_seam_y = 244.24 - 2.19 * np.cos(np.clip(b_lip_u, -1.0, 1.0) * np.pi) + 1.46 * np.exp(-0.5 * (b_lip_u / 0.18) ** 2)
    b_cleft_a = np.exp(-0.5 * ((yy_b - b_seam_y) / 1.45) ** 2) * (1.0 - sstep((np.abs(b_lip_u) - 0.78) / 0.16)) * 0.88
    bas_u_out = bas_u_arr.copy()
    bas_u_out[:, :, 3] = bas_u_arr[:, :, 3] * (1.0 - b_cleft_a)
    Image.fromarray(np.clip(bas_u_out, 0, 255).astype(np.uint8), mode="RGBA").save(PARTS_DIR / "bastion_unbolted_face.png")

    bas_throat = np.zeros((360, 540, 4), dtype=np.float32)
    bt_rx = (xx_b - 269.0) / 96.0
    bt_ry = (yy_b - 251.0) / 24.0
    bt_a = 1.0 - sstep((np.sqrt(bt_rx * bt_rx + bt_ry * bt_ry) - 0.80) / 0.18)
    bas_throat[:, :, 0] = 24.0
    bas_throat[:, :, 1] = 29.0
    bas_throat[:, :, 2] = 26.0
    bas_throat[:, :, 3] = bt_a * 255.0
    save_sub_part(bas_throat, "bastion_throat_bg", (166, 224, 374, 280))

    # Extract intact clamp bar + golden diamond directly from bastion_bolted_fused at sub_box (130, 206, 406, 286)
    b_fused_arr = np.asarray(bastion_bolted_fused, dtype=np.uint8).copy()
    clamp_box = (130, 206, 406, 286)
    clamp_sub540 = b_fused_arr[206:286, 130:406].copy()  # (80, 276, 4)
    clamp_a_sub = np.clip(clamp_mask_540[206:286, 130:406] * 255.0, 0, 255).astype(np.uint8)
    clamp_sub540[:, :, 3] = np.minimum(clamp_sub540[:, :, 3], clamp_a_sub)
    Image.fromarray(clamp_sub540, mode="RGBA").save(PARTS_DIR / "bastion_clamp_intact.png")
    part_layout["bastion_clamp_intact"] = {"sub_box": list(clamp_box), "size": [276, 80]}

    # Split clamp into exact left & right halves through the golden keystone center (x_local=136 -> x540=266)
    # and record their exact 540x360 sub_boxes so Spine fracture/lock handoff has <0.5px zero-jump alignment!
    cy5, cx5 = np.mgrid[0:80, 0:276].astype(np.float32)
    fracture_x = 136.0 + 5.5 * np.sin(cy5 * 0.24) - 3.5 * np.cos(cy5 * 0.12)
    left_half_a = np.clip((fracture_x - cx5 + 0.8) / 1.6, 0.0, 1.0)
    right_half_a = np.clip((cx5 - fracture_x + 0.8) / 1.6, 0.0, 1.0)
    seam_edge = np.exp(-0.5 * ((cx5 - fracture_x) / 1.6) ** 2)

    cl_l = clamp_sub540.astype(np.float32).copy()
    cl_l[:, :, :3] = np.clip(cl_l[:, :, :3] * (1.0 - 0.25 * seam_edge[:, :, None]) + 28.0 * seam_edge[:, :, None], 0, 255)
    cl_l[:, :, 3] *= left_half_a
    cl_l_im = Image.fromarray(cl_l.astype(np.uint8), mode="RGBA")
    lb = cl_l_im.getbbox() or (0, 0, 144, 80)
    lb_pad = (max(0, lb[0] - 2), max(0, lb[1] - 2), min(276, lb[2] + 2), min(80, lb[3] + 2))
    cl_l_crop = cl_l_im.crop(lb_pad)
    cl_l_crop.save(PARTS_DIR / "bastion_clamp_split_l.png")
    part_layout["bastion_clamp_split_l"] = {
        "sub_box": [130 + lb_pad[0], 206 + lb_pad[1], 130 + lb_pad[2], 206 + lb_pad[3]],
        "size": list(cl_l_crop.size),
    }

    cl_r = clamp_sub540.astype(np.float32).copy()
    cl_r[:, :, :3] = np.clip(cl_r[:, :, :3] + 34.0 * seam_edge[:, :, None], 0, 255)
    cl_r[:, :, 3] *= right_half_a
    cl_r_im = Image.fromarray(cl_r.astype(np.uint8), mode="RGBA")
    rb = cl_r_im.getbbox() or (132, 0, 276, 80)
    rb_pad = (max(0, rb[0] - 2), max(0, rb[1] - 2), min(276, rb[2] + 2), min(80, rb[3] + 2))
    cl_r_crop = cl_r_im.crop(rb_pad)
    cl_r_crop.save(PARTS_DIR / "bastion_clamp_split_r.png")
    part_layout["bastion_clamp_split_r"] = {
        "sub_box": [130 + rb_pad[0], 206 + rb_pad[1], 130 + rb_pad[2], 206 + rb_pad[3]],
        "size": list(cl_r_crop.size),
    }

    # Facet-locked golden keystone glow extracted directly from the golden diamond on bastion_bolted_fused!
    bf_f = b_fused_arr.astype(np.float32)
    gold_roi = (xx_b >= 232.0) & (xx_b <= 300.0) & (yy_b >= 216.0) & (yy_b <= 284.0)
    gold_sig = np.clip(((bf_f[:, :, 0] - bf_f[:, :, 2]) - 20.0) / 26.0, 0.0, 1.0) * gold_roi.astype(np.float32)
    gold_halo = cv2.GaussianBlur(gold_sig, (15, 15), 3.8)
    gold_a = np.clip(0.88 * gold_sig + 0.58 * gold_halo, 0.0, 1.0)
    b_glow = np.zeros((360, 540, 4), dtype=np.float32)
    b_glow[:, :, 0] = 255.0
    b_glow[:, :, 1] = 204.0 + 38.0 * gold_sig
    b_glow[:, :, 2] = 74.0 + 56.0 * gold_sig
    b_glow[:, :, 3] = gold_a * 235.0
    save_sub_part(b_glow, "bastion_keystone_glow", (220, 206, 312, 286))

    # =========================================================================
    # FACE 3: MOCKERY (诡笑面) — Articulated Sclera + Moving Stone Pupils + Parting Grin + Scar Glow
    # =========================================================================
    m_arr = np.asarray(mockery, dtype=np.float32)
    m_luma = 0.299 * m_arr[:, :, 0] + 0.587 * m_arr[:, :, 1] + 0.114 * m_arr[:, :, 2]

    # Exact almond eye openings strictly inside Mockery's dark upper lash creases & lower lid rims
    y_top_L = 144.6 + 5.4 * sstep((xx_b - 126.0) / 72.0) - 1.4 * sstep((xx_b - 202.0) / 20.0)
    y_bot_L = 145.0 + 20.4 * (np.maximum(0.0, np.sin(np.clip((xx_b - 125.0) / 98.0, 0.0, 1.0) * np.pi)) ** 0.76)
    eye_ap_L = (
        sstep((xx_b - 128.0) / 2.0)
        * sstep((221.0 - xx_b) / 2.0)
        * sstep((yy_b - (y_top_L + 1.8)) / 1.2)
        * sstep(((y_bot_L - 0.8) - yy_b) / 1.2)
    )

    y_top_R = 148.6 - 1.5 * sstep((xx_b - 344.0) / 36.0) - 8.2 * sstep((xx_b - 380.0) / 48.0)
    y_bot_R = 149.4 + 9.8 * (np.maximum(0.0, np.sin(np.clip((xx_b - 344.0) / 90.0, 0.0, 1.0) * np.pi)) ** 0.76) - 2.4 * sstep(
        (xx_b - 410.0) / 24.0
    )
    eye_ap_R = (
        sstep((xx_b - 347.0) / 2.0)
        * sstep((432.0 - xx_b) / 2.0)
        * sstep((yy_b - (y_top_R + 1.8)) / 1.2)
        * sstep(((y_bot_R - 0.8) - yy_b) / 1.2)
    )
    eye_ap = np.clip(np.maximum(eye_ap_L, eye_ap_R), 0.0, 1.0)

    # Sly grin inner cleft aperture
    grin_roi = (xx_b >= 156.0) & (xx_b <= 424.0) & (yy_b >= 198.0) & (yy_b <= 248.0)
    grin_dark = ((m_luma < 66.0) & grin_roi).astype(np.uint8) * 255
    grin_ap = (cv2.GaussianBlur(grin_dark, (3, 3), 0.8).astype(np.float32) / 255.0) * 0.88

    mock_face_out = m_arr.copy()
    mock_face_out[:, :, 3] = m_arr[:, :, 3] * (1.0 - np.clip(np.maximum(eye_ap, grin_ap), 0.0, 1.0))
    Image.fromarray(np.clip(mock_face_out, 0, 255).astype(np.uint8), mode="RGBA").save(PARTS_DIR / "mockery_face.png")

    # Behind-grin dark oral shadow
    m_throat = np.zeros((360, 540, 4), dtype=np.float32)
    mt_rx = (xx_b - 290.0) / 142.0
    mt_ry = (yy_b - 226.0) / 34.0
    mt_a = 1.0 - sstep((np.sqrt(mt_rx * mt_rx + mt_ry * mt_ry) - 0.82) / 0.16)
    m_throat[:, :, 0] = 28.0
    m_throat[:, :, 1] = 34.0
    m_throat[:, :, 2] = 30.0
    m_throat[:, :, 3] = mt_a * 255.0
    save_sub_part(m_throat, "mockery_throat_bg", (142, 190, 438, 264))

    # Stone eyeball sclera layer (inpainted cleanly across dark pupils so pupils can dart freely without leaving holes!)
    pupil_inpaint_m = (((m_luma < 86.0) & (eye_ap > 0.05)).astype(np.uint8)) * 255
    pupil_inpaint_m = cv2.dilate(pupil_inpaint_m, np.ones((7, 7), np.uint8), iterations=1)
    sclera_inpainted = cv2.inpaint(m_arr[:, :, :3].astype(np.uint8), pupil_inpaint_m, inpaintRadius=9, flags=cv2.INPAINT_TELEA).astype(
        np.float32
    )
    sclera_smooth = cv2.GaussianBlur(sclera_inpainted, (7, 7), 1.8)
    # Subtle upper-eyelid ambient occlusion shadow along top of sclera
    lid_ao_L = np.exp(-0.5 * ((yy_b - y_top_L) / 3.5) ** 2)
    lid_ao_R = np.exp(-0.5 * ((yy_b - y_top_R) / 3.5) ** 2)
    sclera_rgb = sclera_smooth * (1.0 - 0.24 * np.maximum(lid_ao_L, lid_ao_R)[:, :, None])
    eye_socket_pad = (
        cv2.dilate((eye_ap > 0.04).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (19, 15)), iterations=1).astype(
            np.float32
        )
        / 255.0
    )
    eye_socket_pad = cv2.GaussianBlur(eye_socket_pad, (7, 7), 1.6)
    m_sclera = np.zeros((360, 540, 4), dtype=np.float32)
    m_sclera[:, :, :3] = np.clip(sclera_rgb, 0, 255)
    m_sclera[:, :, 3] = eye_socket_pad * 255.0
    save_sub_part(m_sclera, "mockery_eye_sclera", (116, 126, 446, 178))

    # Articulated stone pupils layer: complete sculpted dark-slate stone irises tucked cleanly behind the upper lids!
    # Measured rest centers: Left pupil (197.5, 155.5) rx=18.5 ry=10.8; Right pupil (416.5, 147.2) rx=16.5 ry=9.8
    r_pup_L = np.sqrt(((xx_b - 197.5) / 18.5) ** 2 + ((yy_b - 155.5) / 10.8) ** 2)
    r_pup_R = np.sqrt(((xx_b - 416.5) / 16.5) ** 2 + ((yy_b - 147.2) / 9.8) ** 2)
    pup_L_a = 1.0 - sstep((r_pup_L - 0.86) / 0.16)
    pup_R_a = 1.0 - sstep((r_pup_R - 0.86) / 0.16)
    pup_a = np.maximum(pup_L_a, pup_R_a)
    r_min = np.minimum(r_pup_L, r_pup_R)
    iris_rim = np.exp(-0.5 * ((r_min - 0.76) / 0.14) ** 2)
    spec_dot = np.maximum(
        np.exp(-0.5 * (((xx_b - 191.0) / 3.0) ** 2 + ((yy_b - 153.5) / 2.2) ** 2)),
        np.exp(-0.5 * (((xx_b - 410.5) / 2.8) ** 2 + ((yy_b - 145.5) / 2.0) ** 2)),
    )
    m_pupils = np.zeros((360, 540, 4), dtype=np.float32)
    m_pupils[:, :, 0] = 37.0 + 18.0 * iris_rim + 26.0 * spec_dot
    m_pupils[:, :, 1] = 43.0 + 20.0 * iris_rim + 28.0 * spec_dot
    m_pupils[:, :, 2] = 39.0 + 18.0 * iris_rim + 26.0 * spec_dot
    m_pupils[:, :, 3] = pup_a * 255.0
    save_sub_part(m_pupils, "mockery_pupils", (142, 126, 446, 178))

    # Exact painted amethyst under-eye scar glow + subtle 4-point arcane glint on the scar elbow (365, 184)
    scar_roi = (xx_b >= 338.0) & (xx_b <= 398.0) & (yy_b >= 154.0) & (yy_b <= 216.0)
    purp_sig = np.clip((0.5 * (m_arr[:, :, 0] + m_arr[:, :, 2]) - m_arr[:, :, 1] - 4.0) / 14.0, 0.0, 1.0) * scar_roi.astype(
        np.float32
    )
    purp_halo = cv2.GaussianBlur(purp_sig, (13, 13), 3.2)
    # Crisp tiny 4-point glint centered right on the scar bend (365, 184), NOT floating off on the cheek!
    gx_s = (xx_b - 365.0) * 0.92 + (yy_b - 184.0) * 0.39
    gy_s = -(xx_b - 365.0) * 0.39 + (yy_b - 184.0) * 0.92
    glint = np.exp(-0.5 * (gx_s / 12.0) ** 2) * np.exp(-0.5 * (gy_s / 2.0) ** 2) + np.exp(
        -0.5 * (gx_s / 2.0) ** 2
    ) * np.exp(-0.5 * (gy_s / 12.0) ** 2)
    glint = np.clip(glint, 0.0, 1.0)
    scar_a = np.clip(0.90 * purp_sig + 0.65 * purp_halo + 0.78 * glint, 0.0, 1.0)
    m_glow = np.zeros((360, 540, 4), dtype=np.float32)
    m_glow[:, :, 0] = 196.0 + 48.0 * glint
    m_glow[:, :, 1] = 114.0 + 82.0 * glint
    m_glow[:, :, 2] = 255.0
    m_glow[:, :, 3] = scar_a * 235.0
    save_sub_part(m_glow, "mockery_spark_glow", (328, 148, 412, 224))

    (PARTS_DIR / "part_layout.json").write_text(json.dumps(part_layout, indent=2), encoding="utf-8")

    # =========================================================================
    # AUTOMATED REST-POSE RECONSTRUCTION AUDIT
    # =========================================================================
    def composite_layers(layers: list[tuple[str, tuple[int, int, int, int] | None]]) -> np.ndarray:
        canvas = Image.new("RGBA", (540, 360), (0, 0, 0, 0))
        for pname, box in layers:
            im = Image.open(PARTS_DIR / f"{pname}.png").convert("RGBA")
            if box is None:
                canvas.alpha_composite(im, (0, 0))
            else:
                canvas.alpha_composite(im, (box[0], box[1]))
        return np.asarray(canvas, dtype=np.float32)

    wrath_rest = composite_layers(
        [
            ("wrath_throat_bg", tuple(part_layout["wrath_throat_bg"]["sub_box"])),
            ("wrath_teeth_lower", tuple(part_layout["wrath_teeth_lower"]["sub_box"])),
            ("wrath_teeth_upper", tuple(part_layout["wrath_teeth_upper"]["sub_box"])),
            ("wrath_upper", None),
        ]
    )
    lament_rest = composite_layers(
        [
            ("lament_throat_bg", tuple(part_layout["lament_throat_bg"]["sub_box"])),
            ("lament_face", None),
        ]
    )
    bastion_rest = composite_layers(
        [
            ("bastion_throat_bg", tuple(part_layout["bastion_throat_bg"]["sub_box"])),
            ("bastion_unbolted_face", None),
            ("bastion_clamp_intact", tuple(part_layout["bastion_clamp_intact"]["sub_box"])),
        ]
    )
    mockery_rest = composite_layers(
        [
            ("mockery_throat_bg", tuple(part_layout["mockery_throat_bg"]["sub_box"])),
            ("mockery_eye_sclera", tuple(part_layout["mockery_eye_sclera"]["sub_box"])),
            ("mockery_pupils", tuple(part_layout["mockery_pupils"]["sub_box"])),
            ("mockery_face", None),
        ]
    )
    wrath_mae = float(np.abs(wrath_rest[:, :, :3] - w_arr[:, :, :3]).mean())
    lament_mae = float(np.abs(lament_rest[:, :, :3] - lam_arr[:, :, :3]).mean())
    bastion_mae = float(np.abs(bastion_rest[:, :, :3] - b_fused_arr[:, :, :3].astype(np.float32)).mean())
    mockery_mae = float(np.abs(mockery_rest[:, :, :3] - m_arr[:, :, :3]).mean())
    print(
        f"Rest-pose reconstruction MAE (px 0..255): wrath={wrath_mae:.2f}, lament={lament_mae:.2f}, "
        f"bastion={bastion_mae:.2f}, mockery={mockery_mae:.2f}"
    )

    # --- 10 Chiseled Stone Fracture Chunks for Face Shatter Explosion ---
    rng = np.random.default_rng(20261115)
    donor = np.asarray(sector_intact, dtype=np.uint8)
    for i in range(10):
        cw, ch = int(rng.integers(58, 96)), int(rng.integers(46, 78))
        sx = int(rng.integers(40, sec_w - cw - 40))
        sy = int(rng.integers(30, sec_h - ch - 30))
        patch = Image.fromarray(donor[sy : sy + ch, sx : sx + cw].copy(), mode="RGBA")
        mask = Image.new("L", (cw, ch), 0)
        angles = np.sort(rng.uniform(0, 2 * np.pi, size=6))
        pts = [
            (
                float(cw * 0.5 + np.cos(a) * cw * rng.uniform(0.34, 0.48)),
                float(ch * 0.5 + np.sin(a) * ch * rng.uniform(0.34, 0.48)),
            )
            for a in angles
        ]
        ImageDraw.Draw(mask).polygon(pts, fill=255)
        # Chiseled bright top bevel + dark bottom bevel
        bevel = ImageDraw.Draw(patch)
        bevel.line(pts[:3], fill=(166, 176, 160, 220), width=3)
        bevel.line(pts[3:] + [pts[0]], fill=(40, 48, 43, 235), width=3)
        patch.putalpha(mask.filter(ImageFilter.GaussianBlur(radius=0.7)))
        patch.save(PARTS_DIR / f"rubble_chip_{i}.png")

    # --- Stylized MegaCrit Cartoon Stone Dust Cloud (dust_puff.png) for Heavy Grinding Sill Track & Stop-Block Slam ---
    dust = Image.new("RGBA", (196, 104), (0, 0, 0, 0))
    ddraw = ImageDraw.Draw(dust)
    # Dark slate-moss base shadow lobes
    for cx_p, cy_p, rx_p, ry_p in [(42, 66, 32, 22), (82, 62, 42, 28), (124, 62, 40, 27), (158, 68, 28, 20)]:
        ddraw.ellipse([cx_p - rx_p, cy_p - ry_p, cx_p + rx_p, cy_p + ry_p], fill=(64, 72, 65, 205))
    # Mid-tone mossy stone dust body
    for cx_p, cy_p, rx_p, ry_p in [(44, 58, 29, 21), (80, 48, 38, 28), (120, 46, 39, 29), (154, 58, 26, 19), (98, 60, 34, 22)]:
        ddraw.ellipse([cx_p - rx_p, cy_p - ry_p, cx_p + rx_p, cy_p + ry_p], fill=(128, 136, 121, 225))
    # Warm sunlit ochre-grey top billowing highlights
    for cx_p, cy_p, rx_p, ry_p in [(48, 49, 20, 13), (82, 36, 28, 17), (120, 35, 29, 18), (150, 49, 18, 12)]:
        ddraw.ellipse([cx_p - rx_p, cy_p - ry_p, cx_p + rx_p, cy_p + ry_p], fill=(172, 178, 158, 215))
    dust = soft_perimeter_alpha(dust.filter(ImageFilter.GaussianBlur(radius=1.8)), feather_px=6.0)
    dust.save(PARTS_DIR / "dust_puff.png")

    print("Room textures & Spine parts prepared successfully.")


def main() -> None:
    masters = {
        "arena_master.jpg": BRAIN / "tower_interior_arena_v1_1794701048496.jpg",
        "faces_v2_master.jpg": BRAIN / "faces_v2_all_four_1794701431235.jpg",
        "wall_parts_v2_master.jpg": BRAIN / "tower_wall_parts_v2_1794701459905.jpg",
        "boss_map_icon_v2_master.jpg": BRAIN / "boss_map_icon_v2_1794701426103.jpg",
        "power_tower_pivot_master.jpg": BRAIN / "power_tower_pivot_1794701183933.jpg",
        "power_wrath_glare_master.jpg": BRAIN / "power_wrath_glare_v2_1794701447904.jpg",
        "power_lament_sigh_master.jpg": BRAIN / "power_lament_sigh_1794701221306.jpg",
        "power_bastion_seal_master.jpg": BRAIN / "power_bastion_seal_1794701240563.jpg",
        "power_mockery_echo_master.jpg": BRAIN / "power_mockery_echo_v2_1794701471184.jpg",
    }
    for dst_name, src_path in masters.items():
        dst_path = MASTERS_DIR / dst_name
        if src_path.is_file() and (not dst_path.is_file() or src_path.resolve() != dst_path.resolve()):
            shutil.copy2(src_path, dst_path)

    import sys

    build_room_layers(MASTERS_DIR / "arena_master.jpg")
    if "--room-only" in sys.argv:
        print("Room layers updated (--room-only).")
        return
    build_spine_parts(
        MASTERS_DIR / "faces_v2_master.jpg",
        MASTERS_DIR / "wall_parts_v2_master.jpg",
        MASTERS_DIR / "arena_master.jpg",
    )


if __name__ == "__main__":
    main()
