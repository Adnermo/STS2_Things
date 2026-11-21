"""Generate native spine-godot 4.5.1 visual comparison media for Version A Deep Polish:
- v1_pass1 (Scheme A Pass 1: left weeds frozen) vs v1_optimized (Scheme A Pass 2 Deep-Polished: active!)
- Standalone full-stage showcase MP4 of v1_optimized
- Left-side flora 2.2x close-up animated GIF + 6 per-clip comparison GIFs + keyframe sheet.
"""
from __future__ import annotations
import json
import os
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FONT_BOLD = [Path("C:/Windows/Fonts/msyhbd.ttc"), Path("C:/Windows/Fonts/msyh.ttc"), Path("C:/Windows/Fonts/simhei.ttf")]
FONT_REG = [Path("C:/Windows/Fonts/msyh.ttc"), Path("C:/Windows/Fonts/simhei.ttf")]


def load_font(size: int, bold: bool = False):
    for p in (FONT_BOLD if bold else FONT_REG):
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


CLIP_LABELS = {
    "idle_loop": "待机搏动 (idle_loop · 3.0s 循环)",
    "attack": "蓄压轰击 (attack · 1.25s · 前冲推开左侧海草)",
    "cast": "腐化瘴涌 (cast · 1.40s · 径向气浪吹拂)",
    "power_up": "硬化强化 (power_up · 1.50s · 等长肌张力震颤)",
    "summon": "腔室蠕动 (summon · 1.30s · 右至左蠕动波)",
    "hurt": "受击震荡 (hurt · 0.65s · 钝击尾流右甩)",
    "die": "失血衰竭 (die · 2.60s · 震颤枯萎与无缝塌缩)",
    "revive": "除颤复苏 (revive · 1.80s · 三阶电击充血)",
}
ALL_CLIPS = ["idle_loop", "attack", "cast", "power_up", "summon", "hurt", "die", "revive"]
CROP_MAIN = (0.04, 0.06, 0.96, 0.95)
CROP_LEFT_ZOOM = (0.14, 0.20, 0.56, 0.88)


def crop_rel(im: Image.Image, box=CROP_MAIN) -> Image.Image:
    w, h = im.size
    return im.crop((int(w * box[0]), int(h * box[1]), int(w * box[2]), int(h * box[3])))


def frames_for(cap_dir: Path, clip: str) -> list[Path]:
    return sorted((cap_dir / clip).glob("*.png"))


def draw_badge(im: Image.Image, text: str, color: tuple[int, int, int], subtext: str = "") -> Image.Image:
    out = im.copy()
    d = ImageDraw.Draw(out, "RGBA")
    f_main = load_font(19, bold=True)
    f_sub = load_font(14, bold=False)
    tw = int(d.textlength(text, font=f_main))
    sw = int(d.textlength(subtext, font=f_sub)) if subtext else 0
    bw = max(tw, sw) + 24
    bh = 52 if subtext else 34
    d.rounded_rectangle((10, 10, 10 + bw, 10 + bh), radius=8, fill=(14, 18, 24, 215), outline=(*color, 220), width=2)
    d.text((22, 13), text, fill=color, font=f_main)
    if subtext:
        d.text((22, 36), subtext, fill=(210, 220, 232, 255), font=f_sub)
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


def make_showcase_mp4(v1_dir: Path, out_path: Path, fps: float):
    sample = crop_rel(Image.open(frames_for(v1_dir, "idle_loop")[0]))
    pw = 1280
    ph = int(sample.height * pw / sample.width) // 2 * 2
    hdr = 48
    total_size = (pw, ph + hdr)

    def gen():
        f_hdr = load_font(21, bold=True)
        for clip in ALL_CLIPS:
            flist = frames_for(v1_dir, clip)
            loops = 2 if clip == "idle_loop" else 1
            for _ in range(loops):
                for i, fp in enumerate(flist):
                    canvas = Image.new("RGB", total_size, (14, 18, 24))
                    im = crop_rel(Image.open(fp).convert("RGB")).resize((pw, ph), Image.Resampling.LANCZOS)
                    im = draw_badge(im, "方案 A · 深度精修终版 (当前已启用)", (88, 224, 168),
                                    "沉重等长心肌收缩 · 全域海草波浪与冲击联动 · 零果冻回弹 · 零裁切穿帮")
                    canvas.paste(im, (0, hdr))
                    d = ImageDraw.Draw(canvas)
                    d.text((16, 10), f"腐化之遗 全动作实机舞台预览  |  {CLIP_LABELS[clip]}", fill=(240, 245, 250), font=f_hdr)
                    d.text((total_size[0] - 130, 10), f"t = {i / fps:.2f}s", fill=(190, 205, 220), font=f_hdr)
                    yield canvas

    return encode_mp4(out_path, total_size, fps, gen())


def make_before_after_mp4(pass1_dir: Path, pass2_dir: Path, out_path: Path, fps: float):
    sample = crop_rel(Image.open(frames_for(pass2_dir, "idle_loop")[0]))
    pw = 960
    ph = int(sample.height * pw / sample.width) // 2 * 2
    hdr = 48
    total_size = (pw * 2, ph + hdr)

    def gen():
        f_hdr = load_font(22, bold=True)
        for clip in ALL_CLIPS:
            f1 = frames_for(pass1_dir, clip)
            f2 = frames_for(pass2_dir, clip)
            n = max(len(f1), len(f2))
            for i in range(n):
                canvas = Image.new("RGB", total_size, (14, 18, 24))
                im1 = crop_rel(Image.open(f1[min(i, len(f1) - 1)]).convert("RGB")).resize((pw, ph), Image.Resampling.LANCZOS)
                im2 = crop_rel(Image.open(f2[min(i, len(f2) - 1)]).convert("RGB")).resize((pw, ph), Image.Resampling.LANCZOS)
                im1 = draw_badge(im1, "优化前 · 方案 A 初版", (190, 198, 212), "左侧高草与地草静止僵硬 · 管口边缘前景贴片遮挡")
                im2 = draw_badge(im2, "深度精修后 · 方案 A 终版 (已启用)", (88, 224, 168), "左侧海草后置内延+多节波浪联动 · 过阻尼沉重肌理 · 无缝塌缩")
                canvas.paste(im1, (0, hdr))
                canvas.paste(im2, (pw, hdr))
                d = ImageDraw.Draw(canvas)
                d.line([(pw, hdr), (pw, total_size[1])], fill=(65, 78, 96), width=2)
                d.text((16, 10), f"腐化之遗 方案 A 深度精修前后对比  |  {CLIP_LABELS[clip]}", fill=(240, 245, 250), font=f_hdr)
                d.text((total_size[0] - 130, 10), f"t = {i / fps:.2f}s", fill=(190, 205, 220), font=f_hdr)
                yield canvas

    return encode_mp4(out_path, total_size, fps, gen())


def make_left_closeup_gif(pass1_dir: Path, pass2_dir: Path, out_gif: Path, src_fps: float = 24.0):
    """2.2x close-up of the left side across idle_loop (first 1.5s beat) + attack (1.25s) so left grass motion & clean tube mouths are unmistakable."""
    seq = [("idle_loop", 0, 36, "待机搏动左侧特写"), ("attack", 0, 30, "攻击前冲左侧特写")]
    sample = crop_rel(Image.open(frames_for(pass2_dir, "idle_loop")[0]), CROP_LEFT_ZOOM)
    cw = 420
    ch = int(sample.height * cw / sample.width)
    hdr = 34
    W, H = cw * 2, ch + hdr
    f_hdr = load_font(15, bold=True)

    gif_frames = []
    for clip, s0, s1, tag in seq:
        f1 = frames_for(pass1_dir, clip)[s0:s1:2]
        f2 = frames_for(pass2_dir, clip)[s0:s1:2]
        for i in range(min(len(f1), len(f2))):
            canvas = Image.new("RGB", (W, H), (15, 19, 25))
            im1 = crop_rel(Image.open(f1[i]).convert("RGB"), CROP_LEFT_ZOOM).resize((cw, ch), Image.Resampling.LANCZOS)
            im2 = crop_rel(Image.open(f2[i]).convert("RGB"), CROP_LEFT_ZOOM).resize((cw, ch), Image.Resampling.LANCZOS)
            canvas.paste(im1, (0, hdr))
            canvas.paste(im2, (cw, hdr))
            d = ImageDraw.Draw(canvas)
            d.line([(cw, 0), (cw, H)], fill=(65, 78, 96), width=2)
            d.text((10, 7), f"方案 A 初版 · {tag} (左草僵硬/管口遮挡)", fill=(195, 202, 214), font=f_hdr)
            d.text((cw + 10, 7), f"方案 A 深度精修 · {tag} (自然摆动/后置无缝)", fill=(88, 224, 168), font=f_hdr)
            gif_frames.append(canvas.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG))

    out_gif.parent.mkdir(parents=True, exist_ok=True)
    gif_frames[0].save(out_gif, save_all=True, append_images=gif_frames[1:], duration=int(round(2000.0 / src_fps)), loop=0)
    return out_gif


def make_pair_gif(pass1_dir: Path, pass2_dir: Path, clip: str, out_gif: Path, src_fps: float = 24.0, step: int = 2):
    f1 = frames_for(pass1_dir, clip)[::step]
    f2 = frames_for(pass2_dir, clip)[::step]
    n = max(len(f1), len(f2))
    sample = crop_rel(Image.open(f2[0]))
    cw = 520
    ch = int(sample.height * cw / sample.width)
    hdr = 32
    W, H = cw * 2, ch + hdr
    f_hdr = load_font(15, bold=True)

    gif_frames = []
    for i in range(n):
        canvas = Image.new("RGB", (W, H), (16, 20, 26))
        for col, (flist, title, col_rgb) in enumerate([
            (f1, "优化前 · 方案 A 初版", (190, 198, 210)),
            (f2, "深度精修后 · 方案 A 终版 (当前启用)", (88, 224, 168)),
        ]):
            im = crop_rel(Image.open(flist[min(i, len(flist) - 1)]).convert("RGB")).resize((cw, ch), Image.Resampling.LANCZOS)
            canvas.paste(im, (col * cw, hdr))
            d = ImageDraw.Draw(canvas)
            d.text((col * cw + 12, 6), title, fill=col_rgb, font=f_hdr)
            if col > 0:
                d.line([(col * cw, 0), (col * cw, H)], fill=(58, 70, 86), width=2)
        gif_frames.append(canvas.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG))

    out_gif.parent.mkdir(parents=True, exist_ok=True)
    gif_frames[0].save(out_gif, save_all=True, append_images=gif_frames[1:], duration=int(round(1000.0 * step / src_fps)), loop=0)
    return out_gif


def main():
    review_dir = Path(sys.argv[1])
    artifact_dir = Path(sys.argv[2])
    cap = review_dir / "captures"
    p1 = cap / "v1_pass1"
    p2 = cap / "v1_optimized"
    fps = float(json.loads((p2 / "report.json").read_text(encoding="utf-8"))["fps"])

    print("Saved:", make_showcase_mp4(p2, review_dir / "腐化之遗-方案A深度精修版全动作预览.mp4", fps))
    print("Saved:", make_before_after_mp4(p1, p2, review_dir / "腐化之遗-方案A深度优化前后对比.mp4", fps))
    print("Saved:", make_left_closeup_gif(p1, p2, artifact_dir / "the_legacy_left_weed_closeup.gif", fps))

    for clip in ("idle_loop", "attack", "cast", "power_up", "hurt", "die"):
        step = 2 if clip != "die" else 3
        print("Saved:", make_pair_gif(p1, p2, clip, artifact_dir / f"the_legacy_v1_polish_{clip}.gif", src_fps=fps, step=step))


if __name__ == "__main__":
    main()
