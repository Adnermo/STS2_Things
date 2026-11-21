"""Assemble MP4, WebP, GIF, and 10-clip keyframe contact sheet from native Godot/Spine Scale-Peeler captures."""
from __future__ import annotations

import json
import subprocess
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
VISUALS = ROOT / "build/scale_peeler/visuals"
MOTION = VISUALS / "motion"
FRAMES = MOTION / "frames"
PEAKS = MOTION / "peaks"
OUT = ROOT / "build/scale_peeler/showcase"

FONT_BOLD = "C:/Windows/Fonts/msyhbd.ttc"
FONT_REG = "C:/Windows/Fonts/msyh.ttc"


def load_font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


def draw_hud_overlay(
    image: Image.Image,
    entry: dict,
    total_frames: int,
    font_title: ImageFont.FreeTypeFont,
    font_sub: ImageFont.FreeTypeFont,
) -> Image.Image:
    base = image.convert("RGBA")
    overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    # Frosted cavern-slate HUD card tucked cleanly into top-left cavern ceiling.
    box = (24, 20, 520, 114)
    draw.rounded_rectangle(box, radius=12, fill=(14, 20, 30, 195), outline=(92, 196, 224, 175), width=2)

    title_text = f"刮甲石蚓  ·  {entry['LabelZh']} ({entry['Clip']})"
    plate_count = sum(1 for threshold in (1, 6, 12, 20) if entry["WyrmBlock"] >= threshold)
    sub_text = f"格挡 {entry['WyrmBlock']} · 背甲 {plate_count}/4 · 玩家护甲 {entry['PlayerBlock']}   t={entry['ClipTime']:.2f}s"

    draw.text((40, 32), title_text, font=font_title, fill=(241, 236, 222, 255))
    draw.text((40, 72), sub_text, font=font_sub, fill=(143, 225, 246, 255))

    # Subtle bottom timeline progress strip.
    bar_x0, bar_x1, bar_y = 24, base.width - 24, base.height - 14
    progress = (entry["Index"] + 1) / max(1, total_frames)
    draw.rounded_rectangle((bar_x0, bar_y, bar_x1, bar_y + 4), radius=2, fill=(255, 255, 255, 45))
    draw.rounded_rectangle(
        (bar_x0, bar_y, int(bar_x0 + (bar_x1 - bar_x0) * progress), bar_y + 4),
        radius=2,
        fill=(94, 224, 250, 230),
    )

    return Image.alpha_composite(base, overlay).convert("RGB")


def build_contact_sheet(manifest: dict, font_title: ImageFont.FreeTypeFont, font_sub: ImageFont.FreeTypeFont) -> Path:
    clip_order = [
        ("idle_loop", "待机 · idle_loop"),
        ("summon", "破土 · summon"),
        ("attack", "啃咬 · attack"),
        ("slam", "重砸 · slam"),
        ("cast", "噬甲 · cast + VFX"),
        ("flurry", "掘石连击 · flurry"),
        ("power_up", "蓄能 · power_up"),
        ("hurt", "受击 · hurt"),
        ("die", "倒地 · die"),
        ("revive", "复苏 · revive"),
    ]
    tile_w, tile_h = 384, 300
    header_h = 56
    cols, rows = 5, 2
    sheet = Image.new("RGB", (cols * tile_w, header_h + rows * tile_h), "#121822")
    draw = ImageDraw.Draw(sheet)

    draw.text(
        (20, 12),
        "刮甲石蚓 (Scale-Peeler Wyrm) · 原生 Godot / Spine 4.2 全 10 组骨骼动画总览",
        font=font_title,
        fill="#f0e8d2",
    )

    for idx, (clip, label) in enumerate(clip_order):
        peak_path = PEAKS / f"{clip}.png"
        if not peak_path.is_file():
            raise FileNotFoundError(f"Missing peak frame: {peak_path}")

        with Image.open(peak_path) as raw:
            src = raw.convert("RGB")
            # Aspect-matched 985x680 crop (~368:254) keeps uniform Wyrm scale & square pixels across all 10 panels.
            crop_box = (830, 310, 1815, 990)
            tile = src.crop(crop_box).resize((tile_w - 16, tile_h - 46), Image.Resampling.LANCZOS)

        col, row = idx % cols, idx // cols
        x0 = col * tile_w + 8
        y0 = header_h + row * tile_h + 6
        sheet.paste(tile, (x0, y0))
        draw.rectangle((x0, y0, x0 + tile_w - 17, y0 + tile_h - 47), outline="#4b6c80", width=2)
        draw.text((x0 + 6, y0 + tile_h - 40), label, font=font_sub, fill="#8fe1f6")

    out_path = OUT / "scale_peeler_animation_sheet.jpg"
    sheet.save(out_path, quality=94)
    return out_path


def main() -> None:
    manifest = json.loads((MOTION / "manifest.json").read_text(encoding="utf-8"))
    OUT.mkdir(parents=True, exist_ok=True)
    rendered_dir = OUT / "_labeled"
    rendered_dir.mkdir(parents=True, exist_ok=True)

    font_title = load_font(FONT_BOLD, 24)
    font_sub = load_font(FONT_REG, 19)
    font_sheet_title = load_font(FONT_BOLD, 24)
    font_sheet_label = load_font(FONT_REG, 18)

    entries = manifest["frames"]
    total = len(entries)
    fps = int(manifest["fps"])

    webp_frames: list[Image.Image] = []
    gif_frames: list[Image.Image] = []
    hud_preview_saved = False

    for idx, entry in enumerate(entries):
        src_path = FRAMES / entry["File"]
        with Image.open(src_path) as im:
            labeled = draw_hud_overlay(im, entry, total, font_title, font_sub)
        out_jpg = rendered_dir / f"demo_{idx:04d}.jpg"
        labeled.save(out_jpg, quality=95)
        if not hud_preview_saved and entry["Clip"] == "cast" and entry["ClipTime"] >= 0.72:
            labeled.save(OUT / "preview_hud_cast.jpg", quality=94)
            hud_preview_saved = True

        webp_frames.append(labeled.resize((960, 540), Image.Resampling.LANCZOS))
        if idx % 2 == 0:
            gif_frames.append(labeled.resize((640, 360), Image.Resampling.LANCZOS))

    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    mp4_path = OUT / "scale_peeler_demo.mp4"
    subprocess.run(
        [
            ffmpeg,
            "-y",
            "-framerate",
            str(fps),
            "-i",
            str(rendered_dir / "demo_%04d.jpg"),
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-crf",
            "18",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
            str(mp4_path),
        ],
        check=True,
    )

    webp_path = OUT / "scale_peeler_demo.webp"
    webp_frames[0].save(
        webp_path,
        save_all=True,
        append_images=webp_frames[1:],
        duration=int(round(1000 / fps)),
        loop=0,
        quality=86,
        method=4,
    )

    gif_path = OUT / "scale_peeler_demo.gif"
    palette = gif_frames[0].quantize(colors=160)
    quantized = [frame.quantize(palette=palette) for frame in gif_frames]
    quantized[0].save(
        gif_path,
        save_all=True,
        append_images=quantized[1:],
        duration=int(round(1000 / (fps / 2))),
        loop=0,
        optimize=False,
    )

    sheet_path = build_contact_sheet(manifest, font_sheet_title, font_sheet_label)

    # Clean intermediate numbered jpgs once encoded into video/webp/gif.
    for old in rendered_dir.glob("demo_*.jpg"):
        old.unlink()
    rendered_dir.rmdir()

    summary = {
        "renderer": manifest["renderer"],
        "fps": fps,
        "frames": total,
        "duration_seconds": manifest["duration_seconds"],
        "mp4": {"path": str(mp4_path), "bytes": mp4_path.stat().st_size},
        "webp": {"path": str(webp_path), "bytes": webp_path.stat().st_size},
        "gif": {"path": str(gif_path), "bytes": gif_path.stat().st_size},
        "contact_sheet": {"path": str(sheet_path), "bytes": sheet_path.stat().st_size},
    }
    (OUT / "showcase_report.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
