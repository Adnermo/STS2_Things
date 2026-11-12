"""Contact sheets and GIF/MP4 reels from render_native.gd captures (timing preserved)."""
from pathlib import Path
import argparse
import json
import os
import subprocess

from PIL import Image, ImageDraw, ImageFont

FONT_PATH = Path("C:/Windows/Fonts/msyh.ttc")


def font(size):
    return ImageFont.truetype(str(FONT_PATH), size) if FONT_PATH.exists() else ImageFont.load_default()


def frames_of(folder: Path):
    return sorted(folder.glob("*.png"))


def crop_box(image, crop):
    if not crop:
        return image
    w, h = image.size
    x0, y0, x1, y1 = crop
    return image.crop((int(x0 * w), int(y0 * h), int(x1 * w), int(y1 * h)))


def sheet(root: Path, clip: str, out: Path, columns=6, times=None, step=None, width=320, crop=None, label=""):
    report = json.loads((root / "report.json").read_text(encoding="utf-8"))
    fps = report["fps"]
    files = frames_of(root / clip)
    if times:
        picks = [min(len(files) - 1, round(t * fps)) for t in times]
    else:
        step = step or max(1, len(files) // (columns * 3))
        picks = list(range(0, len(files), step))
        if picks[-1] != len(files) - 1:
            picks.append(len(files) - 1)
    tiles = []
    for index in picks:
        image = crop_box(Image.open(files[index]).convert("RGB"), crop)
        scale = width / image.width
        image = image.resize((width, int(image.height * scale)), Image.Resampling.LANCZOS)
        draw = ImageDraw.Draw(image)
        draw.rectangle((0, 0, 92, 22), fill=(0, 0, 0))
        stem = files[index].stem
        stamp = int(stem[:-2]) / 1000 if stem.endswith("ms") else index / fps
        draw.text((4, 2), f"{stamp:.2f}s", fill="white", font=font(15))
        tiles.append(image)
    rows = (len(tiles) + columns - 1) // columns
    th = tiles[0].height
    canvas = Image.new("RGB", (columns * width, rows * th + 30), (24, 26, 30))
    ImageDraw.Draw(canvas).text((8, 4), f"{label}{clip}", fill="#e9eef0", font=font(18))
    for i, tile in enumerate(tiles):
        canvas.paste(tile, ((i % columns) * width, 30 + (i // columns) * th))
    out.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out, quality=90)
    return out


LABELS = {
    "idle_front": "待机", "idle_front_angry": "愤怒待机", "alternating_jabs": "左右连拳",
    "alternating_jabs_angry": "左右连拳 · 愤怒", "central_slam": "双掌拍地", "central_slam_angry": "双掌拍地 · 愤怒",
    "double_fist_crush": "双拳锤砸", "front_sweep": "横扫", "front_sweep_right_angry": "横扫 · 右臂愤怒",
    "card_snatch": "探掌窃牌", "card_snatch_right": "探掌窃牌 · 右臂", "grab_player": "抓取",
    "grab_player_right_angry": "抓取 · 右臂愤怒", "grab_hold": "握住俘虏（新增循环）",
    "grab_hold_right_angry": "握住俘虏 · 右臂愤怒", "grab_slam": "抓取重摔", "hit_recoil": "受击（叠加在待机上）",
    "weak_enter": "断臂倒伏", "weak_idle": "核心暴露", "weak_attack": "倒伏反击", "weak_recover": "苏醒",
    "main_angry": "二阶段变身", "hide": "死亡沉落",
}


def reel(root: Path, clips, out: Path, width=960, crop=None, title=""):
    import imageio_ffmpeg
    report = json.loads((root / "report.json").read_text(encoding="utf-8"))
    fps = report["fps"]
    first = crop_box(Image.open(frames_of(root / clips[0])[0]).convert("RGB"), crop)
    height = int(first.height * width / first.width) // 2 * 2
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24",
           "-s", f"{width}x{height}", "-r", str(fps), "-i", "-", "-an", "-c:v", "libx264", "-preset", "medium",
           "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(out)]
    out.parent.mkdir(parents=True, exist_ok=True)
    process = subprocess.Popen(cmd, stdin=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    for clip in clips:
        for i, file in enumerate(frames_of(root / clip)):
            image = crop_box(Image.open(file).convert("RGB"), crop).resize((width, height), Image.Resampling.LANCZOS)
            draw = ImageDraw.Draw(image)
            draw.rectangle((0, 0, width, 28), fill=(0, 0, 0))
            draw.text((8, 3), f"{title}{LABELS.get(clip, '')}  {clip}", fill="white", font=font(18))
            draw.text((width - 90, 3), f"{i / fps:.2f}s", fill="#bbbbbb", font=font(18))
            process.stdin.write(image.tobytes())
    process.stdin.close()
    assert process.wait() == 0, process.stderr.read().decode("utf-8", "replace")
    return out


def compare(before: Path, after: Path, clips, out: Path, width=640, crop=(0.0, 0.0, 1.0, 0.82)):
    """Side-by-side before/after reel of renders with identical timing."""
    import imageio_ffmpeg
    fps = json.loads((after / "report.json").read_text(encoding="utf-8"))["fps"]
    sample = crop_box(Image.open(frames_of(after / clips[0])[0]).convert("RGB"), crop)
    height = int(sample.height * width / sample.width) // 2 * 2
    size = (width * 2, height + 34)
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24",
           "-s", f"{size[0]}x{size[1]}", "-r", str(fps), "-i", "-", "-an", "-c:v", "libx264", "-preset", "medium",
           "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(out)]
    out.parent.mkdir(parents=True, exist_ok=True)
    process = subprocess.Popen(cmd, stdin=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    for clip in clips:
        old, new = frames_of(before / clip), frames_of(after / clip)
        for i in range(max(len(old), len(new))):
            canvas = Image.new("RGB", size, (16, 18, 22))
            for col, files in enumerate((old, new)):
                image = crop_box(Image.open(files[min(i, len(files) - 1)]).convert("RGB"), crop)
                canvas.paste(image.resize((width, height), Image.Resampling.LANCZOS), (col * width, 34))
            draw = ImageDraw.Draw(canvas)
            draw.text((10, 6), f"重做前 · {LABELS.get(clip, clip)}", fill="#c9c9c9", font=font(19))
            draw.text((width + 10, 6), f"重做后 · {LABELS.get(clip, clip)}", fill="#ffffff", font=font(19))
            draw.text((size[0] - 80, 6), f"{i / fps:.2f}s", fill="#9aa4aa", font=font(19))
            process.stdin.write(canvas.tobytes())
    process.stdin.close()
    assert process.wait() == 0, process.stderr.read().decode("utf-8", "replace")
    return out


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("clips")
    parser.add_argument("out", type=Path)
    parser.add_argument("--columns", type=int, default=6)
    parser.add_argument("--step", type=int)
    parser.add_argument("--times")
    parser.add_argument("--width", type=int, default=320)
    parser.add_argument("--crop", help="x0,y0,x1,y1 fractions")
    parser.add_argument("--label", default="")
    parser.add_argument("--reel", action="store_true")
    args = parser.parse_args()
    crop = [float(v) for v in args.crop.split(",")] if args.crop else None
    clips = args.clips.split(",")
    if args.reel:
        print(reel(args.root, clips, args.out, width=args.width, crop=crop, title=args.label))
    else:
        times = [float(v) for v in args.times.split(",")] if args.times else None
        for clip in clips:
            target = args.out if len(clips) == 1 else args.out.with_name(f"{args.out.stem}-{clip}{args.out.suffix}")
            print(sheet(args.root, clip, target, args.columns, times, args.step, args.width, crop, args.label))
