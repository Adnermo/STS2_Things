"""Align and package the 3 upgraded Mega Crit lineless painted 1920x1080 minigame backdrops.

1. things_clam_claw_bg.png:
   Inner underwater pit opening aligned to exact (500, 215) .. (1130, 845) (630x630).
2. things_forest_plinko_bg.png:
   Inner hollow ironwood cavity aligned to exact (500, 215) .. (1130, 845) (630x630).
3. things_bronze_cauldron_bg.png:
   COLOSSAL bronze tripod cauldron centered at exact (760, 540), inner basin diameter ~684px
   (matching CauldronPlayfield 720x720 at (400, 180), CauldronInnerRadius = 324px),
   right ear-handle cleanly left of right HUD panel (x = 1364..1872).
"""
from __future__ import annotations

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

BRAIN = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa")
OUT_DIR = Path(r"d:\Things\Things-Workspace\STS2_Things\images\events\minigames")
SCRATCH = BRAIN / "scratch"
SCRATCH.mkdir(parents=True, exist_ok=True)

W, H = 1920, 1080


def smooth_edge_extend(arr: np.ndarray, pad_left: int = 0, pad_right: int = 0, pad_top: int = 0, pad_bottom: int = 0) -> np.ndarray:
    """Extend borders cleanly using median-blended edge strips (no duplicated props!)."""
    out = arr
    if pad_top > 0:
        strip = np.median(out[:6, :, :], axis=0, keepdims=True)
        top_block = np.repeat(strip, pad_top, axis=0)
        # Gentle vertical darkening toward outer edge
        grad = np.linspace(0.86, 1.0, pad_top, dtype=np.float32)[:, None, None]
        out = np.concatenate([top_block * grad, out], axis=0)
    if pad_bottom > 0:
        strip = np.median(out[-6:, :, :], axis=0, keepdims=True)
        bot_block = np.repeat(strip, pad_bottom, axis=0)
        grad = np.linspace(1.0, 0.86, pad_bottom, dtype=np.float32)[:, None, None]
        out = np.concatenate([out, bot_block * grad], axis=0)
    if pad_left > 0:
        strip = np.median(out[:, :6, :], axis=1, keepdims=True)
        left_block = np.repeat(strip, pad_left, axis=1)
        grad = np.linspace(0.86, 1.0, pad_left, dtype=np.float32)[None, :, None]
        out = np.concatenate([left_block * grad, out], axis=1)
    if pad_right > 0:
        strip = np.median(out[:, -16:, :], axis=1, keepdims=True)
        right_block = np.repeat(strip, pad_right, axis=1)
        grad = np.linspace(1.0, 0.82, pad_right, dtype=np.float32)[None, :, None]
        out = np.concatenate([out, right_block * grad], axis=1)
    return np.clip(out, 0, 255).astype(np.uint8)


def build_clam_claw_bg() -> Image.Image:
    src = Image.open(BRAIN / "clam_claw_minigame_bg_v2_1795112775531.jpg").convert("RGB")
    sw, sh = src.size  # 1376 x 768
    # Measured inner pit opening: x=466..910 (w=444), y=162..608 (h=446)
    # Outer verdigris frame left edge: x=355, right edge: x=1022
    # Scale the verdigris frame + pit isotropically so inner 444x446 -> exact 630x630 at (500, 215):
    sy = 630.0 / 446.0  # 1.412556
    sx = 630.0 / 444.0  # 1.418919
    scaled_h = int(round(sh * sy))  # 1085

    # Split into 3 horizontal zones so left lantern/winch fit 100% unclipped in x=0..342 while frame stays isotropic!
    frame_l_src = 355
    frame_r_src = 1022
    frame_l_dst = int(round(500 - (466 - frame_l_src) * sx))  # ~342
    frame_r_dst = int(round(1130 + (frame_r_src - 910) * sx))  # ~1289

    left_crop = src.crop((0, 0, frame_l_src, sh)).resize((frame_l_dst, scaled_h), Image.Resampling.LANCZOS)
    mid_crop = src.crop((frame_l_src, 0, frame_r_src, sh)).resize((frame_r_dst - frame_l_dst, scaled_h), Image.Resampling.LANCZOS)
    right_w = int(round((sw - frame_r_src) * sx))
    right_crop = src.crop((frame_r_src, 0, sw, sh)).resize((right_w, scaled_h), Image.Resampling.LANCZOS)

    stitched_w = frame_l_dst + (frame_r_dst - frame_l_dst) + right_w
    stitched = Image.new("RGB", (stitched_w, scaled_h))
    stitched.paste(left_crop, (0, 0))
    stitched.paste(mid_crop, (frame_l_dst, 0))
    stitched.paste(right_crop, (frame_r_dst, 0))

    arr = np.array(stitched, dtype=np.float32)
    # Align y: src y=162 -> scaled y = 229; we want y=215 => crop top 14px, pad bottom as needed
    top_src_y = int(round(162 * sy))  # 229
    dy = top_src_y - 215  # 14
    if dy > 0:
        arr = arr[dy:, :, :]
    elif dy < 0:
        arr = smooth_edge_extend(arr, pad_top=-dy)

    if arr.shape[0] < H:
        arr = smooth_edge_extend(arr, pad_bottom=H - arr.shape[0])
    else:
        arr = arr[:H, :, :]

    if arr.shape[1] < W:
        arr = smooth_edge_extend(arr, pad_right=W - arr.shape[1])
    else:
        arr = arr[:, :W, :]

    # Gently darken far right HUD area (x=1330..1920) for super crisp plaque legibility
    xs = np.arange(W, dtype=np.float32)
    hud_shade = 1.0 - 0.24 * np.clip((xs - 1310.0) / 260.0, 0.0, 1.0)
    arr = np.clip(arr * hud_shade[None, :, None], 0, 255).astype(np.uint8)

    out = Image.fromarray(arr, mode="RGB").convert("RGBA")
    out.save(OUT_DIR / "things_clam_claw_bg.png", optimize=True)
    print(f"Saved things_clam_claw_bg.png: {out.size}")

    dbg = out.copy()
    d = ImageDraw.Draw(dbg)
    d.rectangle([500, 215, 1130, 845], outline=(255, 220, 60, 255), width=3)
    d.rectangle([1364, 52, 1872, 1028], outline=(90, 220, 255, 255), width=2)
    dbg.save(SCRATCH / "verify_clam_claw_bg_v2.png")
    return out


def build_forest_plinko_bg() -> Image.Image:
    src = Image.open(BRAIN / "overgrown_plinko_minigame_bg_v2_1795112592384.jpg").convert("RGB")
    sw, sh = src.size  # 1376 x 768
    # Measured inner cavity: x=371..760 (w=389), y=163..576 (h=413)
    sx = 630.0 / 389.0  # 1.6195
    sy = 630.0 / 413.0  # 1.5254
    # Keep proportions super clean: scale full image by (sx, sy) -> 2228 x 1171
    scaled_w = int(round(sw * sx))
    scaled_h = int(round(sh * sy))
    scaled = src.resize((scaled_w, scaled_h), Image.Resampling.LANCZOS)

    # Inner top-left at (371*sx, 163*sy) = (601, 249) -> crop offset = (101, 34) so cavity lands at EXACT (500, 215)!
    # Check left chalice: chalice spans x=80..270 in src -> 130..437 in scaled -> after -101 crop, lands at x=29..336 (100% UNCLIPPED!)
    ox = int(round(371 * sx)) - 500  # 101
    oy = int(round(163 * sy)) - 215  # 34
    cropped = scaled.crop((ox, oy, ox + W, oy + H))

    arr = np.array(cropped, dtype=np.float32)
    xs = np.arange(W, dtype=np.float32)
    hud_shade = 1.0 - 0.18 * np.clip((xs - 1310.0) / 260.0, 0.0, 1.0)
    arr = np.clip(arr * hud_shade[None, :, None], 0, 255).astype(np.uint8)

    out = Image.fromarray(arr, mode="RGB").convert("RGBA")
    out.save(OUT_DIR / "things_forest_plinko_bg.png", optimize=True)
    print(f"Saved things_forest_plinko_bg.png: {out.size}")

    dbg = out.copy()
    d = ImageDraw.Draw(dbg)
    d.rectangle([500, 215, 1130, 845], outline=(255, 220, 60, 255), width=3)
    d.rectangle([1364, 52, 1872, 1028], outline=(90, 220, 255, 255), width=2)
    dbg.save(SCRATCH / "verify_forest_plinko_bg_v2.png")
    return out


def build_bronze_cauldron_bg() -> Image.Image:
    src = Image.open(BRAIN / "bronze_tripod_scramble_bg_v3_1795112607848.jpg").convert("RGB")
    sw, sh = src.size  # 1376 x 768
    # Measured inner basin: x=362..828 (cx=595, w=466), y=138..623 (cy=380.5, h=485)
    # Outer rim: top=59, bottom=708 (outer_h=649), right_handle_tip=971
    # Target colossal cauldron: center at EXACT (760, 540), scale=1.42 isotropic ->
    #   inner diameter ~ 675px (radius ~ 337px), outer height ~ 922px (y=79..1001)!
    #   right ear-handle tip ~ 971*1.42 - 85 = 1294px (70px clear of right HUD at x=1364)!
    #   left brazier ~ 75*1.42 - 85 = +21px (100% unclipped)!
    scale = 1.42
    scaled_w = int(round(sw * scale))  # 1954
    scaled_h = int(round(sh * scale))  # 1091
    scaled = src.resize((scaled_w, scaled_h), Image.Resampling.LANCZOS)

    cx_scaled = int(round(595 * scale))  # 845
    cy_scaled = int(round(380.5 * scale))  # 540
    ox = cx_scaled - 760  # 85
    oy = cy_scaled - 540  # 0

    arr = np.array(scaled, dtype=np.float32)
    # Crop left ox=85, top oy=0
    arr = arr[max(0, oy):, max(0, ox):, :]
    if oy < 0:
        arr = smooth_edge_extend(arr, pad_top=-oy)
    if arr.shape[0] < H:
        arr = smooth_edge_extend(arr, pad_bottom=H - arr.shape[0])
    else:
        arr = arr[:H, :, :]
    if arr.shape[1] < W:
        arr = smooth_edge_extend(arr, pad_right=W - arr.shape[1])
    else:
        arr = arr[:, :W, :]

    out = Image.fromarray(arr.astype(np.uint8), mode="RGB").convert("RGBA")
    out.save(OUT_DIR / "things_bronze_cauldron_bg.png", optimize=True)
    print(f"Saved things_bronze_cauldron_bg.png: {out.size}")

    dbg = out.copy()
    d = ImageDraw.Draw(dbg)
    # Giant cauldron playfield: 720x720 at (400, 180) centered at (760, 540), inner placement circle r=322
    d.rectangle([400, 180, 1120, 900], outline=(100, 255, 160, 255), width=2)
    d.ellipse([760 - 322, 540 - 322, 760 + 322, 540 + 322], outline=(255, 220, 60, 255), width=3)
    d.rectangle([1364, 52, 1872, 1028], outline=(90, 220, 255, 255), width=2)
    dbg.save(SCRATCH / "verify_bronze_cauldron_bg_v3.png")
    return out


def main() -> None:
    build_clam_claw_bg()
    build_forest_plinko_bg()
    build_bronze_cauldron_bg()


if __name__ == "__main__":
    main()
