"""Extract & package Mega Crit Crystal-Sphere style loot & Plinko sprites from white-ground sheets.

Uses border-seeded flood-fill (preserving interior bright highlights/parchment) +
color-decontamination anti-aliased alpha edges so every sprite has a razor-clean
transparent RGBA silhouette around its Mega Crit slate-grey die-cut sticker rim.
"""
from __future__ import annotations

from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image

BRAIN = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa")
OUT_DIR = Path(r"d:\Things\Things-Workspace\STS2_Things\images\events\minigames")
SCRATCH = BRAIN / "scratch"
SCRATCH.mkdir(parents=True, exist_ok=True)
OUT_DIR.mkdir(parents=True, exist_ok=True)

SHEET_A = BRAIN / "sts2_treasure_loot_icons_grid_1795112449620.jpg"
SHEET_B = BRAIN / "sts2_loot_minigame_spritesheet_1795112474286.jpg"
SHEET_C = BRAIN / "overgrown_plinko_trio_sheet_v3_1795112628742.jpg"


def extract_white_bg_rgba(crop: Image.Image, punch_interior_white: bool = False) -> Image.Image:
    """Border-seeded white-background removal with zero halo on slate-grey sticker border."""
    rgb = np.array(crop.convert("RGB"), dtype=np.float32)
    h, w, _ = rgb.shape

    # Distance from pure white (255, 255, 255)
    dist_white = np.sqrt(np.sum((255.0 - rgb) ** 2, axis=2))

    # Flood-fill exterior white pixels from all 4 borders (threshold generous enough to catch JPEG compression noise ~25)
    is_bg_seed = dist_white < 34.0
    bg = np.zeros((h, w), dtype=bool)
    q: deque[tuple[int, int]] = deque()

    for x in range(w):
        if is_bg_seed[0, x] and not bg[0, x]:
            bg[0, x] = True
            q.append((0, x))
        if is_bg_seed[h - 1, x] and not bg[h - 1, x]:
            bg[h - 1, x] = True
            q.append((h - 1, x))
    for y in range(h):
        if is_bg_seed[y, 0] and not bg[y, 0]:
            bg[y, 0] = True
            q.append((y, 0))
        if is_bg_seed[y, w - 1] and not bg[y, w - 1]:
            bg[y, w - 1] = True
            q.append((y, w - 1))

    while q:
        cy, cx = q.popleft()
        for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
            if 0 <= ny < h and 0 <= nx < w and not bg[ny, nx] and is_bg_seed[ny, nx]:
                bg[ny, nx] = True
                q.append((ny, nx))

    if punch_interior_white:
        # Remove enclosed pure-white background pockets trapped inside grey sticker loops (e.g. anvil gap, rope loop)
        bg |= dist_white < 18.0

    # Also detect 1-2px anti-aliased fringe immediately bordering `bg` where pixel blends toward white
    # Dilate bg by 2px to find boundary ring
    bg_d1 = bg.copy()
    bg_d1[1:, :] |= bg[:-1, :]
    bg_d1[:-1, :] |= bg[1:, :]
    bg_d1[:, 1:] |= bg[:, :-1]
    bg_d1[:, :-1] |= bg[:, 1:]

    bg_d2 = bg_d1.copy()
    bg_d2[1:, :] |= bg_d1[:-1, :]
    bg_d2[:-1, :] |= bg_d1[1:, :]
    bg_d2[:, 1:] |= bg_d1[:, :-1]
    bg_d2[:, :-1] |= bg_d1[:, 1:]

    fringe = bg_d2 & (~bg)

    alpha = np.where(bg, 0.0, 255.0).astype(np.float32)

    # On fringe pixels bordering white background, compute smooth alpha based on distance from white
    # and un-premultiply white contamination against the Mega Crit slate-grey rim (#767a80, dist_white ~ 230).
    # Pixels with dist_white <= 34 are 0; dist_white >= 155 are 255 (solid rim).
    fringe_alpha = np.clip((dist_white - 30.0) / (155.0 - 30.0), 0.0, 1.0)
    alpha = np.where(fringe, fringe_alpha * 255.0, alpha)

    # Decontaminate white bleed on partially transparent fringe pixels:
    # C_obs = a * C_fg + (1 - a) * 255  =>  C_fg = (C_obs - (1 - a) * 255) / a
    out_rgb = rgb.copy()
    a_norm = alpha / 255.0
    semi = fringe & (a_norm > 0.02) & (a_norm < 0.995)
    for ch in range(3):
        c_obs = rgb[:, :, ch]
        c_fg = (c_obs - (1.0 - a_norm) * 255.0) / np.maximum(a_norm, 0.15)
        out_rgb[:, :, ch] = np.where(semi, np.clip(c_fg, 0.0, 255.0), c_obs)

    # Zero out tiny stray transparent noise
    alpha = np.where(alpha < 10.0, 0.0, alpha)

    rgba = np.dstack([out_rgb, alpha]).astype(np.uint8)
    return Image.fromarray(rgba, mode="RGBA")


def trim_and_fit(im: Image.Image, target_w: int, target_h: int, margin: int = 8) -> Image.Image:
    alpha = np.array(im.getchannel("A"))
    ys, xs = np.where(alpha > 20)
    if len(xs) == 0:
        raise RuntimeError("Empty sprite after extraction!")
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    cropped = im.crop((x0, y0, x1, y1))

    cw, ch = cropped.size
    avail_w = target_w - 2 * margin
    avail_h = target_h - 2 * margin
    scale = min(avail_w / cw, avail_h / ch)
    nw = max(1, int(round(cw * scale)))
    nh = max(1, int(round(ch * scale)))
    resized = cropped.resize((nw, nh), Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    px = (target_w - nw) // 2
    py = (target_h - nh) // 2
    canvas.paste(resized, (px, py), resized)
    return canvas


def slice_grid(path: Path, cols: int, rows: int, names_sizes: list[tuple[str, int, int]]) -> list[tuple[str, Image.Image]]:
    sheet = Image.open(path).convert("RGB")
    sw, sh = sheet.size
    cell_w = sw / cols
    cell_h = sh / rows
    results: list[tuple[str, Image.Image]] = []
    idx = 0
    for r in range(rows):
        for c in range(cols):
            name, tw, th = names_sizes[idx]
            idx += 1
            x0 = int(round(c * cell_w))
            y0 = int(round(r * cell_h))
            x1 = int(round((c + 1) * cell_w))
            y1 = int(round((r + 1) * cell_h))
            cell = sheet.crop((x0, y0, x1, y1))
            punch = name in ("things_loot_upgrade_anvil.png", "things_loot_scrap_bauble.png")
            rgba = extract_white_bg_rgba(cell, punch_interior_white=punch)
            fitted = trim_and_fit(rgba, tw, th, margin=6 if tw <= 128 else 10)
            out_path = OUT_DIR / name
            fitted.save(out_path, optimize=True)
            print(f"Saved {out_path.name}: {fitted.size}")
            results.append((name, fitted))
    return results


def main() -> None:
    all_sprites: list[tuple[str, Image.Image]] = []
    all_sprites += slice_grid(
        SHEET_A,
        cols=3,
        rows=2,
        names_sizes=[
            ("things_loot_relic_rare.png", 256, 256),
            ("things_loot_relic_uncommon.png", 256, 256),
            ("things_loot_relic_common.png", 256, 256),
            ("things_loot_gold_pouch.png", 256, 256),
            ("things_loot_gold_ingots.png", 256, 256),
            ("things_loot_scrap_bauble.png", 256, 256),
        ],
    )
    all_sprites += slice_grid(
        SHEET_B,
        cols=3,
        rows=2,
        names_sizes=[
            ("things_loot_potion_emerald.png", 256, 256),
            ("things_loot_potion_amber.png", 256, 256),
            ("things_loot_card_scroll.png", 256, 256),
            ("things_loot_upgrade_anvil.png", 256, 256),
            ("things_loot_maxhp_heart.png", 256, 256),
            ("things_plinko_bowl.png", 256, 200),
        ],
    )
    all_sprites += slice_grid(
        SHEET_C,
        cols=3,
        rows=1,
        names_sizes=[
            ("things_plinko_peg.png", 96, 96),
            ("things_plinko_shroom.png", 128, 128),
            ("things_plinko_seed.png", 128, 128),
        ],
    )

    # Build dark-background contact sheet to verify zero white halo!
    cols = 5
    rows = 3
    cell = 160
    sheet = Image.new("RGBA", (cols * cell, rows * cell), (10, 22, 28, 255))
    for i, (_, sp) in enumerate(all_sprites):
        r, c = divmod(i, cols)
        thumb = sp.copy()
        thumb.thumbnail((132, 132), Image.Resampling.LANCZOS)
        ox = c * cell + (cell - thumb.width) // 2
        oy = r * cell + (cell - thumb.height) // 2
        sheet.alpha_composite(thumb, (ox, oy))
    preview_path = SCRATCH / "master_sprites_contact_sheet.png"
    sheet.save(preview_path)
    print(f"Saved preview contact sheet: {preview_path}")


if __name__ == "__main__":
    main()
