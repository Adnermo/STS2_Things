"""Build Crystal-Sphere-style full-screen hand-painted minigame backdrops and tactile beveled tile
assets for Underdocks Bilge Grate (things_bilge_mines) and Overgrowth Rune Loom (things_moss_rune_loom).

All textures use genuine Mega Crit hand-painted gouache assets (bilge_grate_minigame_bg,
overgrown_rune_loom_final, hungry_for_mushrooms.png, and vanilla divine_button.png) with 4x supersampled
anti-aliased icons and zero programmatic flat boxes.
"""
from __future__ import annotations

import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "images" / "events" / "minigames"
ARCHIVE_DIR = ROOT / "source_assets" / "events_act1_minigames_20261119" / "minigame_ui"
NATIVE_CS = ROOT.parent / "STS2-V111" / "images" / "events" / "crystal_sphere"
NATIVE_EV = ROOT.parent / "STS2-V111" / "images" / "events"

BILGE_BG_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\bilge_grate_minigame_bg_1795098519445.jpg")
LOOM_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\overgrown_rune_loom_final_1795095315423.jpg")

W, H = 1920, 1080
PIT_X0, PIT_Y0, PIT_X1, PIT_Y1 = 500, 215, 1130, 845  # 630 x 630 exact square


def smoothstep(edge0: float, edge1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def build_bilge_backdrop() -> tuple[Image.Image, dict]:
    src = Image.open(BILGE_BG_RAW).convert("RGB")
    sw, sh = src.size  # 1376 x 768

    sx = (PIT_X1 - PIT_X0) / 410.0
    sy = (PIT_Y1 - PIT_Y0) / 403.0
    scaled_w = int(round(sw * sx))
    scaled_h = int(round(sh * sy))
    scaled = src.resize((scaled_w, scaled_h), Image.Resampling.LANCZOS)

    pit_sx0 = int(round(484 * sx))
    pit_sy0 = int(round(190 * sy))
    cy0 = pit_sy0 - PIT_Y0

    canvas = Image.new("RGB", (W, H), (6, 20, 26))
    left_src_end = int(round(412 * sx))
    left_dest_end = PIT_X0 - (pit_sx0 - left_src_end)
    left_strip = scaled.crop((0, cy0, left_src_end, cy0 + H)).resize((left_dest_end, H), Image.Resampling.LANCZOS)
    canvas.paste(left_strip, (0, 0))

    avail_right_w = min(scaled_w - left_src_end, W - left_dest_end)
    mid_strip = scaled.crop((left_src_end, cy0, left_src_end + avail_right_w, cy0 + H))
    canvas.paste(mid_strip, (left_dest_end, 0))

    filled_end = left_dest_end + avail_right_w
    if filled_end < W:
        rem = W - filled_end
        tail = canvas.crop((filled_end - 80, 0, filled_end, H)).resize((rem, H), Image.Resampling.LANCZOS)
        canvas.paste(tail, (filled_end, 0))

    arr = np.asarray(canvas, dtype=np.float32)
    xs = np.arange(W, dtype=np.float32)[None, :]
    ys = np.arange(H, dtype=np.float32)[:, None]

    right_mask = smoothstep(1230.0, 1490.0, xs)
    arr *= (1.0 - 0.56 * right_mask[:, :, None])
    bl_mask = (1.0 - smoothstep(260.0, 760.0, xs)) * smoothstep(880.0, 1050.0, ys)
    arr *= (1.0 - 0.42 * bl_mask[:, :, None])
    tl_mask = (1.0 - smoothstep(280.0, 680.0, xs)) * (1.0 - smoothstep(40.0, 165.0, ys))
    arr *= (1.0 - 0.45 * tl_mask[:, :, None])

    out = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB").convert("RGBA")
    out_path = OUT_DIR / "things_bilge_sweep_bg.png"
    out.save(out_path, format="PNG")
    return out, {"file": out_path.name, "size": list(out.size), "pit": [PIT_X0, PIT_Y0, PIT_X1, PIT_Y1]}


def build_rune_loom_backdrop(bilge_bg: Image.Image) -> dict:
    """Build a genuine Mega Crit painterly Overgrowth Rune Loom full-screen backdrop:
    - Base woodland sanctuary built seamlessly from our Mega Crit Overgrowth forest art (giant
      gnarled mossy tree trunk, twisting roots, hanging vines, deep pine-emerald nocturnal right zone,
      with the old tilted foreground slab cleanly cropped/masked away so there is ZERO double-slab!).
    - Center-left orthographic carved stone altar frame (x=395..1225, y=105..955) re-toned directly
      from the authentic Mega Crit chipped-bevel hand-painted frame & swirling vortex cavity of
      bilge_grate_minigame_bg into warm weathered Overgrowth mossy limestone + dark emerald swirl,
      adorned with genuine hand-painted thorny briar vines, glowing golden amber root-cup, and pale
      shelf-mushrooms extracted cleanly from overgrown_rune_loom_final!
    """
    raw_loom = Image.open(LOOM_RAW).convert("RGB")
    rw, rh = raw_loom.size  # 1376 x 768

    # 1. Build clean unbroken Overgrowth forest sanctuary backdrop (NO tilted slab peeking out!)
    # Left gnarled trunk + misty Overgrowth canopy from upper-left & right forest of LOOM_RAW
    # Expand the deep mossy forest background cleanly around our altar
    forest = Image.new("RGB", (W, H), (7, 19, 12))
    # Right + Upper forest expanse (x = 46%..100% of LOOM_RAW has zero slab — pure lush mossy forest, roots & hanging vines!)
    right_forest = raw_loom.crop((int(rw * 0.48), 0, rw, rh)).resize((int(W * 0.68), H), Image.Resampling.LANCZOS)
    forest.paste(right_forest, (W - right_forest.width, 0))
    # Left gnarled giant tree trunk + dark moss floor (x = 0%..16.5% of LOOM_RAW has zero slab — pure giant trunk & moss!)
    left_trunk = raw_loom.crop((0, 0, int(rw * 0.165), rh)).resize((460, H), Image.Resampling.LANCZOS)
    forest.paste(left_trunk, (0, 0))
    # Bridge the narrow seam x=420..650 (mostly hidden behind the heavy stone altar!) with feathered blend of mossy roots
    mid_canopy = raw_loom.crop((int(rw * 0.12), 0, int(rw * 0.56), int(rh * 0.34))).resize((980, 460), Image.Resampling.LANCZOS).convert("RGBA")
    mm = Image.new("L", mid_canopy.size, 255)
    md = ImageDraw.Draw(mm)
    for i in range(90):
        a = int(255 * i / 90)
        md.line([(i, 0), (i, mid_canopy.height)], fill=a)
        md.line([(mid_canopy.width - 1 - i, 0), (mid_canopy.width - 1 - i, mid_canopy.height)], fill=a)
    for j in range(110):
        a = int(255 * (1.0 - j / 110))
        md.line([(0, mid_canopy.height - 1 - j), (mid_canopy.width, mid_canopy.height - 1 - j)], fill=min(255, a))
    mid_canopy.putalpha(mm)
    forest_rgba = forest.convert("RGBA")
    forest_rgba.alpha_composite(mid_canopy, (290, 0))

    # Bottom mossy grass floor from bottom of LOOM_RAW (y = 75%..100% has lush Overgrowth moss & dark fern silhouettes)
    bot_moss = raw_loom.crop((int(rw * 0.05), int(rh * 0.74), int(rw * 0.95), rh)).resize((W, 300), Image.Resampling.LANCZOS).convert("RGBA")
    bm = Image.new("L", bot_moss.size, 255)
    bd = ImageDraw.Draw(bm)
    for j in range(120):
        bd.line([(0, j), (W, j)], fill=int(255 * j / 120))
    bot_moss.putalpha(bm)
    forest_rgba.alpha_composite(bot_moss, (0, H - 300))

    # Dim right UI column & HUD corners smoothly (matching bilge_sweep_bg!)
    f_arr = np.asarray(forest_rgba.convert("RGB"), dtype=np.float32)
    xs = np.arange(W, dtype=np.float32)[None, :]
    ys = np.arange(H, dtype=np.float32)[:, None]
    right_mask = smoothstep(1230.0, 1500.0, xs)
    f_arr *= (1.0 - 0.54 * right_mask[:, :, None])
    bl_mask = (1.0 - smoothstep(260.0, 760.0, xs)) * smoothstep(880.0, 1050.0, ys)
    f_arr *= (1.0 - 0.42 * bl_mask[:, :, None])
    tl_mask = (1.0 - smoothstep(280.0, 680.0, xs)) * (1.0 - smoothstep(40.0, 165.0, ys))
    f_arr *= (1.0 - 0.45 * tl_mask[:, :, None])
    forest_rgba = Image.fromarray(np.uint8(np.clip(np.rint(f_arr), 0, 255)), mode="RGB").convert("RGBA")

    # 2. Extract the authentic Mega Crit hand-painted chunky square rim + swirling cavity from bilge_bg
    #    (x = 396..1234, y = 110..950) and re-grade its rusted iron/teal paint into weathered mossy
    #    Overgrowth carved stone & deep forest-emerald vortex!
    fx0, fy0, fx1, fy1 = 396, 110, 1234, 950
    frame_crop = bilge_bg.crop((fx0, fy0, fx1, fy1)).convert("RGBA")
    fc_arr = np.asarray(frame_crop, dtype=np.float32)
    fw, fh = frame_crop.size  # 838 x 840

    # Compute local coordinates relative to pit inside frame_crop
    l_xs = np.arange(fw, dtype=np.float32)[None, :] + fx0
    l_ys = np.arange(fh, dtype=np.float32)[:, None] + fy0
    in_pit = (l_xs >= PIT_X0) & (l_xs <= PIT_X1) & (l_ys >= PIT_Y0) & (l_ys <= PIT_Y1)

    r, g, b = fc_arr[:, :, 0], fc_arr[:, :, 1], fc_arr[:, :, 2]
    lum = 0.299 * r + 0.587 * g + 0.114 * b

    # Inside the pit: shift swirling dark-teal water vortex -> deep Overgrowth dark-emerald moss-slate vortex!
    pit_r = np.clip(lum * 0.50, 0, 255)
    pit_g = np.clip(lum * 1.12, 0, 255)
    pit_b = np.clip(lum * 0.66, 0, 255)

    # On the heavy chipped frame rim: shift rusted-iron orange/blue -> weathered warm Overgrowth limestone & moss!
    # Keep warm amber rim highlights on the left side (where the sap-cup glows!) and mossy olive-grey elsewhere
    warmth = np.clip((r - b) / 90.0, 0.0, 1.0)
    rim_r = np.clip(lum * (0.96 + 0.20 * warmth), 0, 255)
    rim_g = np.clip(lum * (1.02 + 0.05 * warmth), 0, 255)
    rim_b = np.clip(lum * (0.76 - 0.10 * warmth), 0, 255)

    out_fc = np.empty_like(fc_arr)
    out_fc[:, :, 0] = np.where(in_pit, pit_r, rim_r)
    out_fc[:, :, 1] = np.where(in_pit, pit_g, rim_g)
    out_fc[:, :, 2] = np.where(in_pit, pit_b, rim_b)

    # Clean alpha mask around the outer chipped stone rim (feathered 10px edge so it sits organically on the moss!)
    dx = np.maximum(0.0, np.maximum((fx0 + 18) - l_xs, l_xs - (fx1 - 18)))
    dy = np.maximum(0.0, np.maximum((fy0 + 18) - l_ys, l_ys - (fy1 - 18)))
    edge_dist = np.hypot(dx, dy)
    out_fc[:, :, 3] = np.clip((1.0 - smoothstep(0.0, 16.0, edge_dist)) * 255.0, 0, 255)
    stone_altar = Image.fromarray(np.uint8(np.rint(out_fc)), mode="RGBA")

    # 2b. Erase all circular iron rivets & bottom iron bracket tabs on the re-toned altar rim
    #     by feather-cloning adjacent clean weathered limestone directly from the same rim bar!
    def clone_stone_disc(img: Image.Image, gx: int, gy: int, sx_off: int, sy_off: int, radius: int) -> None:
        cx, cy = gx - fx0, gy - fy0
        r = radius
        patch = img.crop((cx + sx_off - r, cy + sy_off - r, cx + sx_off + r, cy + sy_off + r))
        mask = Image.new("L", (2 * r, 2 * r), 0)
        ImageDraw.Draw(mask).ellipse([3, 3, 2 * r - 3, 2 * r - 3], fill=255)
        mask = mask.filter(ImageFilter.GaussianBlur(max(4, r // 4)))
        img.paste(patch, (cx - r, cy - r), mask)

    def clone_stone_rect(img: Image.Image, gx0: int, gy0: int, gx1: int, gy1: int, src_gx0: int, src_gy0: int) -> None:
        w, h = gx1 - gx0, gy1 - gy0
        patch = img.crop((src_gx0 - fx0, src_gy0 - fy0, src_gx0 - fx0 + w, src_gy0 - fy0 + h))
        mask = Image.new("L", (w, h), 0)
        ImageDraw.Draw(mask).rounded_rectangle([6, 6, w - 6, h - 6], radius=12, fill=255)
        mask = mask.filter(ImageFilter.GaussianBlur(6))
        img.paste(patch, (gx0 - fx0, gy0 - fy0), mask)

    # Erase the two bottom metal strap brackets with matching clean bottom limestone slab
    clone_stone_rect(stone_altar, 502, 872, 598, 944, 710, 868)
    clone_stone_rect(stone_altar, 1028, 872, 1122, 944, 710, 868)

    # Erase every circular iron rivet head along top, left, right, and bottom bars
    for (gx, gy, dx_off, dy_off, rad) in [
        (444, 146, 0, 68, 28),
        (544, 128, -54, 6, 24),
        (966, 144, 88, 0, 28),
        (1178, 146, 0, 68, 28),
        (441, 381, 0, 88, 28),
        (439, 668, 0, -92, 28),
        (439, 895, 0, -70, 28),
        (1181, 384, 0, 86, 28),
        (1183, 668, 0, -92, 28),
        (1180, 895, 0, -70, 28),
        (649, 898, 92, 0, 28),
    ]:
        clone_stone_disc(stone_altar, gx, gy, dx_off, dy_off, rad)

    # Contact drop shadow under the stone altar
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle([fx0 + 16, fy0 + 20, fx1 + 26, fy1 + 26], radius=24, fill=(2, 10, 5, 200))
    shadow = shadow.filter(ImageFilter.GaussianBlur(22))
    forest_rgba = Image.alpha_composite(forest_rgba, shadow)
    forest_rgba.alpha_composite(stone_altar, (fx0, fy0))

    # 3. Overlay genuine hand-painted Overgrowth thorny briar vines, shelf-mushrooms, and the SINGLE
    #    glowing golden wooden sap-cup directly from overgrown_rune_loom_final!
    #    (Placed right over the former barnacle clusters so the altar looks 100% pure Overgrowth!)
    # (a) Top thorny emerald briar crest from LOOM_RAW (x=27%..42%, y=27%..38%)
    briar_top = raw_loom.crop((int(rw * 0.27), int(rh * 0.265), int(rw * 0.43), int(rh * 0.39))).convert("RGBA")
    briar_top = briar_top.resize((270, 145), Image.Resampling.LANCZOS)
    bt_arr = np.asarray(briar_top, dtype=np.float32)
    bt_mask = Image.new("L", briar_top.size, 0)
    btd = ImageDraw.Draw(bt_mask)
    btd.ellipse([18, 14, briar_top.width - 18, briar_top.height - 14], fill=255)
    bt_mask = bt_mask.filter(ImageFilter.GaussianBlur(14))
    # Suppress pale/warm stone pixels inside the briar curl so only rich emerald briars remain
    b_m = np.asarray(bt_mask, dtype=np.float32)
    is_stone = ((bt_arr[:, :, 0] > bt_arr[:, :, 1] + 2) & (bt_arr[:, :, 0] > 68)) | (
        (bt_arr[:, :, 0] > 95) & (bt_arr[:, :, 1] > 95) & (bt_arr[:, :, 2] < bt_arr[:, :, 1] + 5) & (bt_arr[:, :, 0] > bt_arr[:, :, 2] + 10)
    )
    b_m = np.where(is_stone, b_m * 0.03, b_m)
    briar_top.putalpha(Image.fromarray(np.uint8(np.clip(b_m, 0, 255)), mode="L"))
    forest_rgba.alpha_composite(briar_top, (525, 72))

    # (b) Right shelf-mushrooms from LOOM_RAW
    mush_r = raw_loom.crop((int(rw * 0.43), int(rh * 0.46), int(rw * 0.51), int(rh * 0.58))).convert("RGBA")
    mush_r = mush_r.resize((155, 130), Image.Resampling.LANCZOS)
    mr_mask = Image.new("L", mush_r.size, 0)
    ImageDraw.Draw(mr_mask).ellipse([16, 14, mush_r.width - 16, mush_r.height - 14], fill=255)
    mr_mask = mr_mask.filter(ImageFilter.GaussianBlur(12))
    mush_r.putalpha(mr_mask)
    forest_rgba.alpha_composite(mush_r, (1120, 265))

    # (c) Bottom & lower-left shelf-mushrooms from LOOM_RAW
    mush_b = raw_loom.crop((int(rw * 0.25), int(rh * 0.65), int(rw * 0.325), int(rh * 0.75))).convert("RGBA")
    mush_b = mush_b.resize((150, 120), Image.Resampling.LANCZOS)
    mb_mask = Image.new("L", mush_b.size, 0)
    ImageDraw.Draw(mb_mask).ellipse([16, 14, mush_b.width - 16, mush_b.height - 14], fill=255)
    mb_mask = mb_mask.filter(ImageFilter.GaussianBlur(12))
    mush_b.putalpha(mb_mask)
    forest_rgba.alpha_composite(mush_b, (870, 845))
    forest_rgba.alpha_composite(mush_b.transpose(Image.Transpose.FLIP_LEFT_RIGHT), (365, 705))

    # (d) SINGLE iconic glowing golden wooden sap-cup nestled at upper-left beside the altar
    #     Tightly masked around the carved wooden bowl & amber drip so zero stone corner leaks!
    cup_crop = raw_loom.crop((int(rw * 0.145), int(rh * 0.40), int(rw * 0.228), int(rh * 0.535))).convert("RGBA")
    cup_crop = cup_crop.resize((198, 194), Image.Resampling.LANCZOS)
    c_mask = Image.new("L", cup_crop.size, 0)
    ImageDraw.Draw(c_mask).ellipse([24, 18, cup_crop.width - 20, cup_crop.height - 26], fill=255)
    c_mask = c_mask.filter(ImageFilter.GaussianBlur(16))
    cup_crop.putalpha(c_mask)
    forest_rgba.alpha_composite(cup_crop, (198, 228))

    # Soft warm golden-amber sap radiance cast from the sap-cup onto the left stone rim
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([140, 160, 560, 560], fill=(246, 178, 56, 62))
    glow = glow.filter(ImageFilter.GaussianBlur(65))
    forest_rgba = Image.alpha_composite(forest_rgba, glow)

    out_path = OUT_DIR / "things_rune_loom_bg.png"
    forest_rgba.save(out_path, format="PNG")
    return {"file": out_path.name, "size": list(forest_rgba.size), "pit": [PIT_X0, PIT_Y0, PIT_X1, PIT_Y1]}


def build_square_beveled_tile_from_divine_button() -> Image.Image:
    src = Image.open(NATIVE_CS / "divine_button.png").convert("RGBA")
    bbox = src.getbbox()
    if bbox:
        src = src.crop(bbox)
    sw, sh = src.size
    b = min(44, sw // 3, sh // 3)
    S = 256
    mid = S - 2 * b
    tile = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    tile.paste(src.crop((0, 0, b, b)), (0, 0))
    tile.paste(src.crop((sw - b, 0, sw, b)), (S - b, 0))
    tile.paste(src.crop((0, sh - b, b, sh)), (0, S - b))
    tile.paste(src.crop((sw - b, sh - b, sw, sh)), (S - b, S - b))
    tile.paste(src.crop((b, 0, sw - b, b)).resize((mid, b), Image.Resampling.LANCZOS), (b, 0))
    tile.paste(src.crop((b, sh - b, sw - b, sh)).resize((mid, b), Image.Resampling.LANCZOS), (b, S - b))
    tile.paste(src.crop((0, b, b, sh - b)).resize((b, mid), Image.Resampling.LANCZOS), (0, b))
    tile.paste(src.crop((sw - b, b, sw, sh - b)).resize((b, mid), Image.Resampling.LANCZOS), (S - b, b))
    tile.paste(src.crop((b, b, sw - b, sh - b)).resize((mid, mid), Image.Resampling.LANCZOS), (b, b))
    return tile


def build_tile_and_icon_assets() -> list[dict]:
    base_tile = build_square_beveled_tile_from_divine_button()
    results = []

    # 1. Underdocks Bilge Grate Cover Tile (4x supersampled rivets on Mega Crit divine_button brushwork)
    bilge_tile = base_tile.copy()
    arr = np.asarray(bilge_tile, dtype=np.float32)
    arr[:, :, 0] *= 0.72
    arr[:, :, 1] *= 0.86
    arr[:, :, 2] *= 0.92
    bilge_tile = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGBA")
    rivets_hi = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rivets_hi)
    for rx, ry in [(136, 136), (888, 136), (136, 888), (888, 888)]:
        rd.ellipse([rx - 40, ry - 40, rx + 40, ry + 40], fill=(10, 24, 30, 225))
        rd.ellipse([rx - 28, ry - 28, rx + 28, ry + 28], fill=(66, 110, 122, 245))
        rd.ellipse([rx - 20, ry - 24, rx + 8, ry + 4], fill=(128, 178, 190, 230))
    bilge_tile = Image.alpha_composite(bilge_tile, rivets_hi.resize((256, 256), Image.Resampling.LANCZOS))
    p1 = OUT_DIR / "things_bilge_tile_cover.png"
    bilge_tile.save(p1, format="PNG")
    results.append({"file": p1.name, "size": list(bilge_tile.size)})

    # 2. Underdocks Glint Hatch Tile (4x supersampled faceted golden star gleam)
    glint_tile = bilge_tile.copy()
    g_arr = np.asarray(glint_tile, dtype=np.float32)
    ys, xs = np.mgrid[0:256, 0:256].astype(np.float32)
    dist_tr = np.hypot((xs - 204.0) / 145.0, (ys - 52.0) / 145.0)
    warm = np.clip(1.0 - dist_tr, 0.0, 1.0) ** 1.6
    alpha_mask = (g_arr[:, :, 3] > 20).astype(np.float32)
    g_arr[:, :, 0] = np.clip(g_arr[:, :, 0] + 95.0 * warm * alpha_mask, 0, 255)
    g_arr[:, :, 1] = np.clip(g_arr[:, :, 1] + 72.0 * warm * alpha_mask, 0, 255)
    g_arr[:, :, 2] = np.clip(g_arr[:, :, 2] + 15.0 * warm * alpha_mask, 0, 255)
    glint_tile = Image.fromarray(np.uint8(np.rint(g_arr)), mode="RGBA")
    star_hi = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    sd = ImageDraw.Draw(star_hi)
    cx, cy = 796, 228
    sd.ellipse([cx - 120, cy - 120, cx + 120, cy + 120], fill=(246, 206, 98, 125))
    star_hi = star_hi.filter(ImageFilter.GaussianBlur(38))
    sd2 = ImageDraw.Draw(star_hi)
    sd2.polygon([
        (cx, cy - 120), (cx + 30, cy - 30), (cx + 120, cy), (cx + 30, cy + 30),
        (cx, cy + 120), (cx - 30, cy + 30), (cx - 120, cy), (cx - 30, cy - 30),
    ], fill=(255, 224, 122, 255))
    sd2.polygon([
        (cx, cy - 64), (cx + 16, cy - 16), (cx + 64, cy), (cx + 16, cy + 16),
        (cx, cy + 64), (cx - 16, cy + 16), (cx - 64, cy), (cx - 16, cy - 16),
    ], fill=(255, 252, 232, 255))
    glint_tile = Image.alpha_composite(glint_tile, star_hi.resize((256, 256), Image.Resampling.LANCZOS))
    p2 = OUT_DIR / "things_bilge_tile_glint.png"
    glint_tile.save(p2, format="PNG")
    results.append({"file": p2.name, "size": list(glint_tile.size)})

    # 3. Overgrowth Carved Stone Rune Tablet (CLEAN neutral painterly stone directly from divine_button
    #    brushwork with subtle horizontal/diagonal gouache texture — ZERO diagonal tile-seam artifacts!)
    b_arr = np.asarray(base_tile, dtype=np.float32)
    b_lum = 0.299 * b_arr[:, :, 0] + 0.587 * b_arr[:, :, 1] + 0.114 * b_arr[:, :, 2]
    # Lift divine_button's authentic painted gouache strokes & chamfered bevels cleanly to neutral limestone
    stone_val = np.clip(b_lum * 2.02, 52, 244)
    rune_arr = np.zeros_like(b_arr)
    rune_arr[:, :, 0] = np.clip(stone_val * 0.99, 0, 255)
    rune_arr[:, :, 1] = np.clip(stone_val * 1.02, 0, 255)
    rune_arr[:, :, 2] = np.clip(stone_val * 0.95, 0, 255)
    rune_arr[:, :, 3] = b_arr[:, :, 3]
    rune_tile = Image.fromarray(np.uint8(np.rint(rune_arr)), mode="RGBA")
    p3 = OUT_DIR / "things_rune_tile_stone.png"
    rune_tile.save(p3, format="PNG")
    results.append({"file": p3.name, "size": list(rune_tile.size)})

    # 4. 4x Supersampled Mega Crit faceted Warning Flag Icon (512 -> 128 RGBA)
    flag_hi = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    fd = ImageDraw.Draw(flag_hi)
    fd.polygon([(148, 72), (192, 72), (192, 434), (148, 434)], fill=(18, 12, 14, 235))
    fd.polygon([(98, 412), (262, 412), (288, 456), (76, 456)], fill=(22, 16, 14, 240))
    fd.polygon([(108, 404), (252, 404), (268, 442), (92, 442)], fill=(198, 152, 74, 255))
    fd.polygon([(114, 404), (242, 404), (242, 418), (114, 418)], fill=(246, 212, 126, 255))
    fd.polygon([(156, 62), (184, 62), (184, 416), (156, 416)], fill=(214, 188, 134, 255))
    fd.polygon([(156, 62), (170, 62), (170, 416), (156, 416)], fill=(250, 236, 196, 255))
    fd.polygon([(180, 86), (434, 168), (380, 200), (416, 234), (180, 300)], fill=(162, 34, 42, 255))
    fd.polygon([(180, 86), (434, 168), (304, 192), (180, 184)], fill=(232, 72, 66, 255))
    fd.polygon([(180, 86), (332, 136), (258, 148), (180, 138)], fill=(254, 136, 108, 255))
    fd.polygon([(224, 168), (294, 192), (224, 222), (242, 192)], fill=(252, 218, 114, 255))
    flag = flag_hi.resize((128, 128), Image.Resampling.LANCZOS)
    p4 = OUT_DIR / "things_bilge_flag_icon.png"
    flag.save(p4, format="PNG")
    results.append({"file": p4.name, "size": list(flag.size)})

    # 5. 4x Supersampled Mega Crit faceted Iron Grappling Pry-Hook Icon (512 -> 128 RGBA)
    hook_hi = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hook_hi)
    hd.polygon([(78, 434), (124, 464), (368, 186), (324, 156)], fill=(28, 20, 18, 235))
    hd.polygon([(92, 424), (120, 446), (354, 184), (326, 162)], fill=(132, 92, 60, 255))
    hd.polygon([(92, 424), (106, 434), (340, 174), (326, 162)], fill=(196, 150, 102, 255))
    # Iron hook head (crisp angular Mega Crit curved claw)
    hd.polygon([
        (302, 174), (382, 76), (446, 94), (458, 168), (402, 232),
        (362, 206), (402, 156), (380, 134), (336, 192),
    ], fill=(66, 98, 110, 255))
    hd.polygon([
        (312, 168), (382, 76), (434, 92), (394, 136), (336, 184),
    ], fill=(168, 218, 228, 255))
    # Brass binding collar
    hd.polygon([(276, 198), (328, 148), (362, 182), (310, 232)], fill=(184, 138, 62, 255))
    hd.polygon([(276, 198), (328, 148), (344, 164), (292, 214)], fill=(242, 202, 114, 255))
    hook = hook_hi.resize((128, 128), Image.Resampling.LANCZOS)
    p5 = OUT_DIR / "things_bilge_probe_icon.png"
    hook.save(p5, format="PNG")
    results.append({"file": p5.name, "size": list(hook.size)})

    return results


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    ARCHIVE_DIR.mkdir(parents=True, exist_ok=True)

    bilge_img, bilge_info = build_bilge_backdrop()
    loom_info = build_rune_loom_backdrop(bilge_img)
    tile_infos = build_tile_and_icon_assets()

    report = {
        "bilge_backdrop": bilge_info,
        "loom_backdrop": loom_info,
        "tiles_and_icons": tile_infos,
    }
    (ARCHIVE_DIR / "ui_assets_manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
