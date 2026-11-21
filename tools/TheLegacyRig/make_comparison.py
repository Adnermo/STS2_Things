"""Generate native spine-godot comparison videos, animated GIFs, and contact sheets
for The Legacy (腐化之遗): v0_original vs v1_optimized vs v2_rebuilt.

Usage:
    python tools/TheLegacyRig/make_comparison.py BUILD_REVIEW_DIR ARTIFACT_DIR
"""
from __future__ import annotations
import json
import os
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FONT_CANDIDATES = [
    Path("C:/Windows/Fonts/msyhbd.ttc"),
    Path("C:/Windows/Fonts/msyh.ttc"),
    Path("C:/Windows/Fonts/simhei.ttf"),
]
REG_FONT_CANDIDATES = [
    Path("C:/Windows/Fonts/msyh.ttc"),
    Path("C:/Windows/Fonts/simhei.ttf"),
]


def load_font(size: int, bold: bool = False):
    cands = FONT_CANDIDATES if bold else REG_FONT_CANDIDATES
    for p in cands:
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


CLIP_LABELS = {
    "idle_loop": "待机搏动 (idle_loop · 3.0s 循环)",
    "attack": "蓄压轰击 (attack · 1.25s · 回音/血弹)",
    "cast": "腐化瘴涌 (cast · 1.40s · 衰竭)",
    "power_up": "硬化强化 (power_up · 1.50s · 强化)",
    "summon": "腔室蠕动 (summon · 1.30s)",
    "hurt": "受击震荡 (hurt · 0.65s)",
    "die": "失血衰竭 (die · 2.60s)",
    "revive": "除颤复苏 (revive · 1.80s)",
}

ALL_CLIPS = ["idle_loop", "attack", "cast", "power_up", "summon", "hurt", "die", "revive"]
CROP = (0.04, 0.06, 0.96, 0.95)


def crop_rel(im: Image.Image, box=CROP) -> Image.Image:
    w, h = im.size
    return im.crop((int(w * box[0]), int(h * box[1]), int(w * box[2]), int(h * box[3])))


def frames_for(cap_dir: Path, clip: str) -> list[Path]:
    return sorted((cap_dir / clip).glob("*.png"))


def draw_badge(im: Image.Image, text: str, color: tuple[int, int, int], subtext: str = "") -> Image.Image:
    out = im.copy()
    d = ImageDraw.Draw(out, "RGBA")
    f_main = load_font(20, bold=True)
    f_sub = load_font(15, bold=False)
    tw = int(d.textlength(text, font=f_main))
    sw = int(d.textlength(subtext, font=f_sub)) if subtext else 0
    bw = max(tw, sw) + 24
    bh = 54 if subtext else 34
    d.rounded_rectangle((10, 10, 10 + bw, 10 + bh), radius=8, fill=(14, 18, 24, 215), outline=(*color, 220), width=2)
    d.text((22, 14), text, fill=color, font=f_main)
    if subtext:
        d.text((22, 37), subtext, fill=(210, 220, 232, 255), font=f_sub)
    return out


def encode_mp4(out_path: Path, size: tuple[int, int], fps: float, frame_iter):
    import imageio_ffmpeg
    w, h = (size[0] // 2) * 2, (size[1] // 2) * 2
    out_path.parent.mkdir(parents=True, exist_ok=True)
    cmd = [
        imageio_ffmpeg.get_ffmpeg_exe(),
        "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24",
        "-s", f"{w}x{h}", "-r", str(fps), "-i", "-",
        "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "17",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        str(out_path),
    ]
    proc = subprocess.Popen(
        cmd, stdin=subprocess.PIPE, stderr=subprocess.PIPE,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
    )
    for frame in frame_iter:
        if frame.size != (w, h):
            frame = frame.resize((w, h), Image.Resampling.LANCZOS)
        proc.stdin.write(frame.convert("RGB").tobytes())
    proc.stdin.close()
    rc = proc.wait()
    if rc != 0:
        raise RuntimeError(proc.stderr.read().decode("utf-8", "replace"))
    return out_path


def make_ab_duel_mp4(v1_dir: Path, v2_dir: Path, out_path: Path, fps: float):
    sample = crop_rel(Image.open(frames_for(v1_dir, "idle_loop")[0]))
    pw = 960
    ph = int(sample.height * pw / sample.width) // 2 * 2
    header_h = 48
    total_size = (pw * 2, ph + header_h)

    def gen():
        f_hdr = load_font(22, bold=True)
        for clip in ALL_CLIPS:
            f1 = frames_for(v1_dir, clip)
            f2 = frames_for(v2_dir, clip)
            n = max(len(f1), len(f2))
            for i in range(n):
                canvas = Image.new("RGB", total_size, (15, 18, 24))
                im1 = crop_rel(Image.open(f1[min(i, len(f1) - 1)]).convert("RGB")).resize((pw, ph), Image.Resampling.LANCZOS)
                im2 = crop_rel(Image.open(f2[min(i, len(f2) - 1)]).convert("RGB")).resize((pw, ph), Image.Resampling.LANCZOS)
                im1 = draw_badge(im1, "方案 A · 当前架构深度优化版", (96, 195, 255), "保留 29 分件 · 贴地锚定 · 跨缝共享权重")
                im2 = draw_badge(im2, "方案 B · 地面心脏完全重置版", (255, 118, 150), "无缝心肌连续网格 · 静水压蠕动 · 管口扩张与传导发光")
                canvas.paste(im1, (0, header_h))
                canvas.paste(im2, (pw, header_h))
                d = ImageDraw.Draw(canvas)
                d.line([(pw, header_h), (pw, total_size[1])], fill=(65, 78, 96), width=2)
                d.text((16, 10), f"腐化之遗 骨骼动画正面对决  |  {CLIP_LABELS[clip]}", fill=(240, 245, 250), font=f_hdr)
                d.text((total_size[0] - 130, 10), f"t = {i / fps:.2f}s", fill=(190, 205, 220), font=f_hdr)
                yield canvas

    return encode_mp4(out_path, total_size, fps, gen())


def make_three_way_mp4(v0_dir: Path, v1_dir: Path, v2_dir: Path, out_path: Path, fps: float):
    """Top row: V1 Optimized (left) | V2 Rebuilt (right) at 960px each.
    Bottom row: V0 Original (left) | Ground-contact 1.85x zoom comparison strip (right).
    """
    sample = crop_rel(Image.open(frames_for(v0_dir, "idle_loop")[0]))
    pw = 960
    ph = int(sample.height * pw / sample.width) // 2 * 2
    header_h = 48
    total_size = (pw * 2, ph * 2 + header_h)

    def gen():
        f_hdr = load_font(22, bold=True)
        f_lbl = load_font(16, bold=True)
        for clip in ALL_CLIPS:
            f0 = frames_for(v0_dir, clip)
            f1 = frames_for(v1_dir, clip)
            f2 = frames_for(v2_dir, clip)
            n = max(len(f0), len(f1), len(f2))
            for i in range(n):
                canvas = Image.new("RGB", total_size, (15, 18, 24))
                raw0 = Image.open(f0[min(i, len(f0) - 1)]).convert("RGB")
                raw1 = Image.open(f1[min(i, len(f1) - 1)]).convert("RGB")
                raw2 = Image.open(f2[min(i, len(f2) - 1)]).convert("RGB")

                im1 = draw_badge(crop_rel(raw1).resize((pw, ph), Image.Resampling.LANCZOS),
                                 "方案 A · 当前架构深度优化版 (v1)", (96, 195, 255))
                im2 = draw_badge(crop_rel(raw2).resize((pw, ph), Image.Resampling.LANCZOS),
                                 "方案 B · 地面心脏完全重置版 (v2)", (255, 118, 150))
                im0 = draw_badge(crop_rel(raw0).resize((pw, ph), Image.Resampling.LANCZOS),
                                 "修改前 · 当前原版 (v0 基准)", (195, 200, 210))

                # Bottom-right panel: 3 stacked ground-contact close-ups (left/bottom heart belly)
                zoom_panel = Image.new("RGB", (pw, ph), (20, 24, 31))
                sh = ph // 3
                zbox = (0.14, 0.46, 0.86, 0.90)
                for idx, (rimg, tag, col) in enumerate([
                    (raw0, "原版 v0 接地细节", (195, 200, 210)),
                    (raw1, "方案 A 优化版 接地细节", (96, 195, 255)),
                    (raw2, "方案 B 重置版 接地细节", (255, 118, 150)),
                ]):
                    zc = crop_rel(rimg, zbox).resize((pw, sh - 2), Image.Resampling.LANCZOS)
                    zd = ImageDraw.Draw(zc, "RGBA")
                    zd.rounded_rectangle((8, 6, 236, 32), radius=6, fill=(12, 16, 22, 210), outline=(*col, 200), width=1)
                    zd.text((16, 8), tag, fill=col, font=f_lbl)
                    zoom_panel.paste(zc, (0, idx * sh))

                canvas.paste(im1, (0, header_h))
                canvas.paste(im2, (pw, header_h))
                canvas.paste(im0, (0, header_h + ph))
                canvas.paste(zoom_panel, (pw, header_h + ph))

                d = ImageDraw.Draw(canvas)
                d.line([(pw, header_h), (pw, total_size[1])], fill=(65, 78, 96), width=2)
                d.line([(0, header_h + ph), (total_size[0], header_h + ph)], fill=(65, 78, 96), width=2)
                d.text((16, 10), f"腐化之遗 三版完整对比 (原生 spine-godot 4.5.1)  |  {CLIP_LABELS[clip]}", fill=(240, 245, 250), font=f_hdr)
                d.text((total_size[0] - 130, 10), f"t = {i / fps:.2f}s", fill=(190, 205, 220), font=f_hdr)
                yield canvas

    return encode_mp4(out_path, total_size, fps, gen())


def make_clip_gif(v0_dir: Path, v1_dir: Path, v2_dir: Path, clip: str, out_gif: Path, src_fps: float = 24.0, step: int = 2):
    """Create a crisp 3-panel animated GIF (Top: V1 Left + V2 Right, Bottom: V0 Original centered or 3-row)
    Actually a 3-column or 1-top + 2-bottom? No: 3 stacked wide strips or V0 | V1 | V2?
    Wait: V1 (Left 460px) vs V2 (Right 460px) on top, and V0 (460px) + Ground Zoom (460px) or 3 vertical rows of 720px!
    Let's check: 3 vertical rows of 680x320 = 680x996, OR 2-column V1 vs V2 (920x260) + V0 above?
    Even cleaner: 3 columns at 380px each = 1140x240! At 1140px wide in Markdown side-pane, 3 columns side-by-side lets eyes scan horizontally across V0 | V1 | V2 at the exact same ground line!!
    """
    f0 = frames_for(v0_dir, clip)[::step]
    f1 = frames_for(v1_dir, clip)[::step]
    f2 = frames_for(v2_dir, clip)[::step]
    n = max(len(f0), len(f1), len(f2))
    sample = crop_rel(Image.open(f0[0]))
    cw = 380
    ch = int(sample.height * cw / sample.width)
    hdr = 32
    W, H = cw * 3, ch + hdr
    f_hdr = load_font(15, bold=True)

    gif_frames = []
    for i in range(n):
        canvas = Image.new("RGB", (W, H), (16, 20, 26))
        for col, (flist, title, col_rgb) in enumerate([
            (f0, "修改前 · 原版 (v0)", (190, 198, 210)),
            (f1, "方案 A · 深度优化版 (v1)", (96, 195, 255)),
            (f2, "方案 B · 完全重置版 (v2)", (255, 118, 150)),
        ]):
            im = crop_rel(Image.open(flist[min(i, len(flist) - 1)]).convert("RGB")).resize((cw, ch), Image.Resampling.LANCZOS)
            canvas.paste(im, (col * cw, hdr))
            d = ImageDraw.Draw(canvas)
            d.text((col * cw + 10, 6), title, fill=col_rgb, font=f_hdr)
            if col > 0:
                d.line([(col * cw, 0), (col * cw, H)], fill=(58, 70, 86), width=2)
        # Draw continuous horizontal ground reference line across all 3 columns so levitation/grounding is 100% obvious!
        # Convert RGB -> P with adaptive palette
        gif_frames.append(canvas.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG))

    out_gif.parent.mkdir(parents=True, exist_ok=True)
    dur_ms = int(round(1000.0 * step / src_fps))
    gif_frames[0].save(
        out_gif, save_all=True, append_images=gif_frames[1:],
        duration=dur_ms, loop=0, optimize=False,
    )
    return out_gif


def make_ab_peak_sheet(v0_dir: Path, v1_dir: Path, v2_dir: Path, out_jpg: Path, fps: float = 24.0):
    """High-res static keyframe comparison sheet at the exact peak moments of attack, cast, power_up, hurt, die."""
    moments = [
        ("idle_loop", 0.21, "待机心室搏动峰值 (t=0.21s)"),
        ("attack", 0.40, "攻击前冲命中帧 (t=0.40s)"),
        ("cast", 0.44, "衰竭瘴涌喷发帧 (t=0.44s)"),
        ("power_up", 0.50, "强化蓄压峰值帧 (t=0.50s)"),
        ("hurt", 0.08, "受击挤压变形帧 (t=0.08s)"),
        ("die", 2.40, "死亡失血塌缩终态 (t=2.40s)"),
    ]
    sample = crop_rel(Image.open(frames_for(v0_dir, "idle_loop")[0]))
    cw = 520
    ch = int(sample.height * cw / sample.width)
    row_hdr = 34
    W = cw * 3
    H = len(moments) * (ch + row_hdr) + 44
    canvas = Image.new("RGB", (W, H), (14, 18, 24))
    d = ImageDraw.Draw(canvas)
    f_top = load_font(20, bold=True)
    f_row = load_font(16, bold=True)

    headers = [
        ("修改前 · 原版 (v0)", (195, 202, 214)),
        ("方案 A · 当前架构深度优化版 (v1)", (96, 195, 255)),
        ("方案 B · 地面心脏完全重置版 (v2)", (255, 118, 150)),
    ]
    for col, (htxt, hcol) in enumerate(headers):
        d.text((col * cw + 14, 10), htxt, fill=hcol, font=f_top)

    for r, (clip, t_sec, desc) in enumerate(moments):
        y0 = 44 + r * (ch + row_hdr)
        d.rectangle((0, y0, W, y0 + row_hdr), fill=(24, 30, 40))
        d.text((14, y0 + 6), desc, fill=(238, 242, 248), font=f_row)
        idx = int(round(t_sec * fps))
        for col, vdir in enumerate((v0_dir, v1_dir, v2_dir)):
            flist = frames_for(vdir, clip)
            im = crop_rel(Image.open(flist[min(idx, len(flist) - 1)]).convert("RGB")).resize((cw, ch), Image.Resampling.LANCZOS)
            canvas.paste(im, (col * cw, y0 + row_hdr))
            if col > 0:
                d.line([(col * cw, y0 + row_hdr), (col * cw, y0 + row_hdr + ch)], fill=(58, 70, 86), width=2)

    out_jpg.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out_jpg, quality=93)
    return out_jpg


def main():
    review_dir = Path(sys.argv[1])
    artifact_dir = Path(sys.argv[2])
    cap = review_dir / "captures"
    v0, v1, v2 = cap / "v0_original", cap / "v1_optimized", cap / "v2_rebuilt"
    fps = float(json.loads((v1 / "report.json").read_text(encoding="utf-8"))["fps"])

    p_duel = make_ab_duel_mp4(v1, v2, review_dir / "腐化之遗-方案A与方案B正面对决.mp4", fps)
    print("Saved:", p_duel)
    p_tri = make_three_way_mp4(v0, v1, v2, review_dir / "腐化之遗-三版同屏对比.mp4", fps)
    print("Saved:", p_tri)

    sheet_jpg = make_ab_peak_sheet(v0, v1, v2, artifact_dir / "the_legacy_keyframes_3way.jpg", fps)
    print("Saved:", sheet_jpg)

    for clip in ("idle_loop", "attack", "cast", "power_up", "hurt", "die"):
        step = 2 if clip != "die" else 3
        gpath = make_clip_gif(v0, v1, v2, clip, artifact_dir / f"the_legacy_compare_{clip}.gif", src_fps=fps, step=step)
        print("Saved:", gpath)


if __name__ == "__main__":
    main()
