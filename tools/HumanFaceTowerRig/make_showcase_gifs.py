#!/usr/bin/env python3
"""Pack native Godot 4.5.1 SpineSprite rendered frame sequences into GIFs & keyframe audit boards."""
from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

BRAIN = Path(r"C:\Users\adner\.gemini\antigravity\brain\630b9439-8513-49e2-8959-201a92e08ca5")
FRAMES_ROOT = BRAIN / "scratch" / "native_tower_frames"


def load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for candidate in (
        r"C:\Windows\Fonts\msyhbd.ttc",
        r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\simhei.ttf",
    ):
        if Path(candidate).is_file():
            return ImageFont.truetype(candidate, size=size)
    return ImageFont.load_default()


def save_gif(folder: Path, out_gif: Path, *, fps: int = 18, max_w: int = 800) -> list[Image.Image]:
    files = sorted(folder.glob("*.png"))
    if not files:
        raise RuntimeError(f"No rendered frames found in {folder}")
    frames_rgb: list[Image.Image] = []
    for p in files:
        im = Image.open(p).convert("RGB")
        if im.width > max_w:
            nh = round(im.height * (max_w / im.width))
            im = im.resize((max_w, nh), Image.Resampling.LANCZOS)
        frames_rgb.append(im)

    # Global adaptive palette from evenly spaced sample frames to prevent flicker on static stone chamber
    sample_strip = Image.new("RGB", (frames_rgb[0].width, frames_rgb[0].height * min(8, len(frames_rgb))))
    step = max(1, len(frames_rgb) // 8)
    for i in range(min(8, len(frames_rgb))):
        sample_strip.paste(frames_rgb[min(len(frames_rgb) - 1, i * step)], (0, i * frames_rgb[0].height))
    palette_img = sample_strip.quantize(colors=256, method=Image.Quantize.MEDIANCUT)

    pal_frames = [f.quantize(palette=palette_img, dither=Image.Dither.FLOYDSTEINBERG) for f in frames_rgb]
    pal_frames[0].save(
        out_gif,
        save_all=True,
        append_images=pal_frames[1:],
        duration=round(1000 / fps),
        loop=0,
        optimize=False,
    )
    return frames_rgb


def main() -> None:
    specs = [
        ("seq_1_rotation_cycle", "tower_native_rotation_cycle.gif"),
        ("seq_2_wrath_and_lament", "tower_native_wrath_lament.gif"),
        ("seq_3_bastion_and_mockery", "tower_native_bastion_mockery.gif"),
        ("seq_4_shatter_and_combo", "tower_native_shatter_and_combo.gif"),
    ]
    loaded: dict[str, list[Image.Image]] = {}
    for folder_name, gif_name in specs:
        frames = save_gif(FRAMES_ROOT / folder_name, BRAIN / gif_name, fps=20, max_w=800)
        loaded[folder_name] = frames
        print(f"Wrote {gif_name} ({len(frames)} native Godot Spine frames)")

    # Build a 4x3 Extreme-Pose & Transition Keyframe Audit Sheet for instant static inspection!
    font = load_font(20)
    picks = [
        ("seq_1_rotation_cycle", 0.010, "1. 嗔怒面驻场·四壁柱圆筒透视贴合"),
        ("seq_1_rotation_cycle", 0.142, "2. 3D曲面浮雕随壁碾转+轨缝扬尘"),
        ("seq_1_rotation_cycle", 0.214, "3. 万钧撞栓硬停·巨震回弹与冲击尘浪"),
        ("seq_2_wrath_and_lament", 0.040, "4. 嗔怒面·双排石齿张口怒吼前扑"),
        ("seq_2_wrath_and_lament", 0.707, "5. 悲叹面·风化悲鸣窄泪槽脉动"),
        ("seq_3_bastion_and_mockery", 0.049, "6. 缄默面·万钧崩落菱栓锁面闪光"),
        ("seq_3_bastion_and_mockery", 0.151, "7. 缄默面·石栓碎裂崩飞(破栓)"),
        ("seq_3_bastion_and_mockery", 0.224, "8. 缄默面·立体石唇虚弱态"),
        ("seq_3_bastion_and_mockery", 0.268, "9. 缄默面·双栓向内合拢重锁"),
        ("seq_3_bastion_and_mockery", 0.727, "10. 诡笑面·石瞳左掠+疤痕紫芒"),
        ("seq_4_shatter_and_combo", 0.088, "11. 单面击破·碎石与烟尘迸射崩毁"),
        ("seq_4_shatter_and_combo", 0.883, "12. 满周期共鸣·全塔震颤咆哮"),
    ]
    cw, ch = 460, 259
    board = Image.new("RGB", (cw * 3 + 64, (ch + 44) * 4 + 36), (20, 25, 24))
    draw = ImageDraw.Draw(board)
    for idx, (seq_key, ratio, title) in enumerate(picks):
        seq = loaded[seq_key]
        f_i = min(len(seq) - 1, max(0, round(ratio * (len(seq) - 1))))
        thumb = seq[f_i].resize((cw, ch), Image.Resampling.LANCZOS)
        gx = 16 + (idx % 3) * (cw + 16)
        gy = 16 + (idx // 3) * (ch + 44)
        board.paste(thumb, (gx, gy))
        draw.text((gx + 8, gy + ch + 8), title, fill=(225, 234, 222), font=font)

    board.save(BRAIN / "tower_native_keyframes_audit.jpg", quality=93)
    print("Wrote tower_native_keyframes_audit.jpg")

    # Build a dedicated 6-frame Left-Edge Physical Exit & Right-Edge Entry Strip (verifying zero sudden pop!)
    rot_seq = loaded["seq_1_rotation_cycle"]
    edge_picks = [
        (26, "帧26: 嗔怒面随凹壁碾至左侧"),
        (28, "帧28: 左半脸实体滑出左边界"),
        (29, "帧29: 右眼与石齿滑入前景左岩后"),
        (30, "帧30: 最右颊边缘实体滑出屏幕外"),
        (32, "帧32: 纯石砖过渡段(零残影突消)"),
        (36, "帧36: 悲叹面自右侧边界实体滑入"),
    ]
    ew, eh = 460, 259
    eboard = Image.new("RGB", (ew * 3 + 64, (eh + 44) * 2 + 36), (20, 25, 24))
    edraw = ImageDraw.Draw(eboard)
    for idx, (fi, label) in enumerate(edge_picks):
        thumb = rot_seq[min(len(rot_seq) - 1, fi)].resize((ew, eh), Image.Resampling.LANCZOS)
        gx = 16 + (idx % 3) * (ew + 16)
        gy = 16 + (idx // 3) * (eh + 44)
        eboard.paste(thumb, (gx, gy))
        edraw.text((gx + 8, gy + eh + 8), label, fill=(225, 234, 222), font=font)
    eboard.save(BRAIN / "tower_left_exit_audit.jpg", quality=93)
    print("Wrote tower_left_exit_audit.jpg")


if __name__ == "__main__":
    main()
