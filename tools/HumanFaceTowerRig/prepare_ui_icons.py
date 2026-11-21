#!/usr/bin/env python3
"""Generate official STS2_Things map icons, run-history icons, and power icons for 人面塔."""
from __future__ import annotations

import hashlib
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
MASTERS_DIR = ROOT / "source_assets" / "monsters" / "human_face_tower" / "masters"

MAP_DIR = ROOT / "images" / "map"
HIST_DIR = ROOT / "images" / "ui" / "run_history"
POWERS_DIR = ROOT / "images" / "powers"
ATLAS_SPRITES_DIR = ROOT / "images" / "atlases" / "power_atlas.sprites"

for d in (MAP_DIR, HIST_DIR, POWERS_DIR, ATLAS_SPRITES_DIR):
    d.mkdir(parents=True, exist_ok=True)


def write_godot_png_import(png_path: Path) -> None:
    rel_res = "res://" + png_path.relative_to(ROOT).as_posix()
    digest = hashlib.md5(rel_res.encode("utf-8")).hexdigest()
    uid_seed = hashlib.sha1(rel_res.encode("utf-8")).hexdigest()[:12]
    ctex = f"res://.godot/imported/{png_path.name}-{digest}.ctex"
    png_path.with_suffix(".png.import").write_text(
        f"""[remap]

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
""",
        encoding="utf-8",
    )


def remove_magenta_bg(img: Image.Image) -> Image.Image:
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

    fg_mask = cv2.erode((~exterior_bg).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)))
    alpha_img = Image.fromarray(fg_mask, mode="L").filter(ImageFilter.GaussianBlur(radius=0.5))
    final_alpha = np.asarray(alpha_img, dtype=np.float32) / 255.0

    dilated_bg = cv2.dilate(exterior_bg.astype(np.uint8), np.ones((7, 7), np.uint8), iterations=1) > 0
    fringe_zone = dilated_bg & (~exterior_bg)
    spill = np.maximum(0.0, 0.5 * (r + b) - (g + 10.0))
    despill_w = np.where(fringe_zone, 0.90, 0.0)

    out = np.zeros_like(rgba, dtype=np.uint8)
    out[:, :, 0] = np.clip(r - spill * despill_w, 0.0, 255.0).astype(np.uint8)
    out[:, :, 1] = np.clip(g + spill * despill_w * 0.20, 0.0, 255.0).astype(np.uint8)
    out[:, :, 2] = np.clip(b - spill * despill_w, 0.0, 255.0).astype(np.uint8)
    out[:, :, 3] = np.where(final_alpha < 0.04, 0, (final_alpha * 255.0)).astype(np.uint8)
    return Image.fromarray(out, mode="RGBA")


def build_map_and_history_icons() -> None:
    raw = Image.open(MASTERS_DIR / "boss_map_icon_v2_master.jpg")
    gray = np.asarray(raw.convert("L"), dtype=np.float32)
    ink = np.clip((235.0 - gray) / 165.0, 0.0, 1.0)
    ys, xs = np.where(ink > 0.12)
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    cropped_ink = Image.fromarray((ink[y0:y1, x0:x1] * 255.0).astype(np.uint8), mode="L")

    def render_white_alpha_pair(tw: int, th: int, pad: int, dilate_r: int) -> tuple[Image.Image, Image.Image]:
        max_w, max_h = tw - 2 * pad, th - 2 * pad
        sc = min(max_w / cropped_ink.width, max_h / cropped_ink.height)
        nw, nh = round(cropped_ink.width * sc), round(cropped_ink.height * sc)
        resized = cropped_ink.resize((nw, nh), Image.Resampling.LANCZOS)
        ca = np.zeros((th, tw), dtype=np.uint8)
        ox, oy = (tw - nw) // 2, (th - nh) // 2
        ca[oy : oy + nh, ox : ox + nw] = np.asarray(resized, dtype=np.uint8)

        icon = np.full((th, tw, 4), 255, dtype=np.uint8)
        icon[:, :, 3] = ca

        fg = (ca > 36).astype(np.uint8) * 255
        flood = fg.copy()
        hm = np.zeros((th + 2, tw + 2), dtype=np.uint8)
        cv2.floodFill(flood, hm, (0, 0), 255)
        filled = cv2.bitwise_or(fg, cv2.bitwise_not(flood))
        kern = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * dilate_r + 1, 2 * dilate_r + 1))
        dil = cv2.GaussianBlur(cv2.dilate(filled, kern, iterations=1), (3, 3), 0.6)
        outline = np.full((th, tw, 4), 255, dtype=np.uint8)
        outline[:, :, 3] = dil
        return Image.fromarray(icon, mode="RGBA"), Image.fromarray(outline, mode="RGBA")

    # 352 x 300 Boss Map Node Icons
    m_icon, m_out = render_white_alpha_pair(352, 300, pad=18, dilate_r=4)
    p_map = MAP_DIR / "human_face_tower_boss_icon.png"
    p_map_o = MAP_DIR / "human_face_tower_boss_icon_outline.png"
    m_icon.save(p_map)
    m_out.save(p_map_o)
    write_godot_png_import(p_map)
    write_godot_png_import(p_map_o)

    # 88 x 88 Run History Icons
    h_icon, h_out = render_white_alpha_pair(88, 88, pad=6, dilate_r=2)
    for stem in ("things_human_face_tower_boss_encounter", "human_face_tower_boss"):
        ph = HIST_DIR / f"{stem}.png"
        pho = HIST_DIR / f"{stem}_outline.png"
        h_icon.save(ph)
        h_out.save(pho)
        write_godot_png_import(ph)
        write_godot_png_import(pho)


def build_power_icons() -> None:
    specs = [
        ("things_tower_pivot_power", "power_tower_pivot_master.jpg"),
        ("things_wrath_glare_power", "power_wrath_glare_master.jpg"),
        ("things_lament_sigh_power", "power_lament_sigh_master.jpg"),
        ("things_bastion_seal_power", "power_bastion_seal_master.jpg"),
        ("things_mockery_echo_power", "power_mockery_echo_master.jpg"),
    ]
    for slug, master_name in specs:
        rgba = remove_magenta_bg(Image.open(MASTERS_DIR / master_name))
        bbox = rgba.getbbox()
        assert bbox is not None
        cropped = rgba.crop(bbox)
        big = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
        sc = min(228.0 / cropped.width, 228.0 / cropped.height)
        nw, nh = round(cropped.width * sc), round(cropped.height * sc)
        big.alpha_composite(cropped.resize((nw, nh), Image.Resampling.LANCZOS), ((256 - nw) // 2, (256 - nh) // 2))

        p_big = POWERS_DIR / f"{slug}.png"
        p_pack = POWERS_DIR / f"{slug}_packed.png"
        big.save(p_big)
        big.resize((64, 64), Image.Resampling.LANCZOS).save(p_pack)
        write_godot_png_import(p_big)
        write_godot_png_import(p_pack)

        (ATLAS_SPRITES_DIR / f"{slug}.tres").write_text(
            f"""[gd_resource type="AtlasTexture" load_steps=2 format=3]
[ext_resource type="Texture2D" path="res://images/powers/{slug}_packed.png" id="1"]
[resource]
atlas = ExtResource("1")
region = Rect2(0, 0, 64, 64)
""",
            encoding="utf-8",
        )


def main() -> None:
    build_map_and_history_icons()
    build_power_icons()
    print("UI map, run-history, and power icons written successfully.")


if __name__ == "__main__":
    main()
