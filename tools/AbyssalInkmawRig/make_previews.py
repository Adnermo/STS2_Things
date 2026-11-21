#!/usr/bin/env python3
"""
Step 3: Compile native Godot 4.5.1 SpineSprite renders into:
  1. abyssal_inkmaw_stage_idle.gif          (Full 3-layer bridge stage idle loop)
  2. abyssal_inkmaw_ink_and_phase2.gif      (Ink Spray torrent + Phase 2 breach + enraged idle)
  3. abyssal_inkmaw_tentacles_actions.gif   (Bridge Tentacles emerge -> heavy_slam -> whip_lash -> sever + recoil)
  4. abyssal_inkmaw_keyframes_audit.jpg     (3x3 static keyframe audit grid)
  5. abyssal_inkmaw_stage_aspects.jpg       (16:9 / 21:9 / 4:3 multi-aspect framing + HP bar & hand safe-zone audit)
  6. abyssal_inkmaw_ink_card_mockup.png     (Ink-Blackened Blind Card visual concept: Normal -> Blackened -> Washed)
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BRAIN = Path(r"C:\Users\adner\.gemini\antigravity\brain\630b9439-8513-49e2-8959-201a92e08ca5")
REPO = Path(r"D:\Things\Things-Workspace\STS2_Things")
FRAMES_ROOT = BRAIN / "scratch" / "native_abyssal_frames"


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

    sample_n = min(10, len(frames_rgb))
    sample_strip = Image.new("RGB", (frames_rgb[0].width, frames_rgb[0].height * sample_n))
    step = max(1, len(frames_rgb) // sample_n)
    for i in range(sample_n):
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


def overlay_combat_hud_guides(im: Image.Image, label: str) -> Image.Image:
    """Draw realistic STS2 combat HUD placement guides (centered 0.67x player, HP bars, hand-card safe zone)."""
    w, h = im.size
    out = im.convert("RGBA")
    ov = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    f_sm = load_font(18)
    f_md = load_font(22)
    f_lg = load_font(28)

    cx = w // 2
    cy = h // 2
    floor_y = h - 308  # Centered 0.67x player feet cleanly on dead-flat stone bridge deck (Y=772, deck=720..921!)

    # 1) Simulated top status bar (64px)
    d.rectangle((0, 0, w, 62), fill=(12, 16, 24, 150))
    d.line((0, 62, w, 62), fill=(90, 115, 130, 140), width=2)
    d.text((22, 14), f"{label}  ({w}x{h})", fill=(235, 245, 250, 240), font=f_lg)

    # 2) Boss Intent + Boss HP bar (cleanly over open lake at y=568, 52px ABOVE centered player head!)
    boss_bar_w = 270
    boss_bar_y = cy + 28
    d.rounded_rectangle((cx - boss_bar_w // 2, boss_bar_y, cx + boss_bar_w // 2, boss_bar_y + 18), radius=6,
                        fill=(25, 22, 32, 225), outline=(210, 195, 165, 225), width=2)
    d.rounded_rectangle((cx - boss_bar_w // 2 + 2, boss_bar_y + 2, cx + boss_bar_w // 2 - 2, boss_bar_y + 16), radius=4,
                        fill=(198, 52, 58, 240))
    d.text((cx - 88, boss_bar_y + 20), "渊墨巨蛸 480/480", fill=(235, 240, 248, 245), font=f_sm)

    # 3) Centered 0.67x Scaled Player silhouette standing on bridge deck + HP bar (ZERO overlap with Boss or Hand!)
    ph = 150  # 0.67x vanilla hero height (~224 * 0.67)
    pw = 92
    px0, py0 = cx - pw // 2, floor_y - ph
    # Subtle ground shadow ellipse on stone deck
    d.ellipse((cx - 58, floor_y - 14, cx + 58, floor_y + 12), fill=(8, 10, 16, 150))
    # Stylized hero silhouette badge
    d.rounded_rectangle((px0, py0, px0 + pw, floor_y), radius=24,
                        fill=(72, 158, 120, 145), outline=(170, 245, 210, 235), width=3)
    d.text((cx - 40, py0 + 46), "玩家 0.67x", fill=(245, 255, 250, 250), font=f_sm)
    d.text((cx - 34, py0 + 72), "站于桥心", fill=(210, 245, 230, 240), font=f_sm)
    # Player HP bar at floor_y + 12 (y = 784 -> 800, leaving 58px clean clearance above hand cards!)
    pbar_y = floor_y + 12
    d.rounded_rectangle((cx - 72, pbar_y, cx + 72, pbar_y + 16), radius=5,
                        fill=(22, 24, 30, 230), outline=(225, 225, 230, 220), width=2)
    d.rounded_rectangle((cx - 70, pbar_y + 2, cx + 54, pbar_y + 14), radius=4, fill=(215, 62, 62, 240))
    d.text((cx - 44, pbar_y + 18), "玩家 72/80", fill=(235, 245, 240, 240), font=f_sm)

    # 4) Left & Right Water-Emerging Tentacle HP bars (cleanly along bridge parapet flanks at cx ± 470, y = 668!)
    for tx, tname, hp_txt, col in [
        (cx - 470, "重砸巨腕", "85/85", (210, 74, 72, 240)),
        (cx + 470, "墨腺触腕", "70/70", (175, 68, 185, 240)),
    ]:
        tbar_y = cy + 128
        d.rounded_rectangle((tx - 78, tbar_y, tx + 78, tbar_y + 16), radius=5,
                            fill=(22, 24, 30, 230), outline=(215, 208, 225, 220), width=2)
        d.rounded_rectangle((tx - 76, tbar_y + 2, tx + 76, tbar_y + 14), radius=4, fill=col)
        d.text((tx - 64, tbar_y + 18), f"{tname} {hp_txt}", fill=(235, 236, 245, 240), font=f_sm)

    # 5) Bottom Hand-Card Fan Safe Zone (y = 858..1080) — visibly clear of ALL HP bars!
    hand_top = h - 222
    d.rectangle((0, hand_top, w, h), fill=(12, 14, 22, 110))
    for x in range(0, w, 28):
        d.line((x, hand_top, min(w, x + 16), hand_top), fill=(125, 220, 215, 200), width=3)
    for k in range(-2, 3):
        card_cx = cx + k * 142
        card_y = hand_top + 18 + abs(k) * 14
        d.rounded_rectangle((card_cx - 64, card_y, card_cx + 64, card_y + 190), radius=10,
                            fill=(18, 20, 30, 165), outline=(120, 200, 210, 175), width=2)
    d.text((cx - 190, hand_top + 8), "手牌扇区安全线 (y=858) — 与玩家/触腕血条零遮挡", fill=(160, 245, 235, 245), font=f_md)

    out.alpha_composite(ov)
    return out.convert("RGB")


def make_ink_card_mockup(out_path: Path) -> None:
    """Create a polished 3-state visual concept mockup for 【墨浊覆牌】 (Normal -> Ink-Blackened -> Wave-Washed)."""
    W, H = 1720, 700
    canvas = Image.new("RGB", (W, H), (16, 20, 28))
    cd = ImageDraw.Draw(canvas)
    f_title = load_font(28)
    f_sub = load_font(18)
    f_card_t = load_font(22)
    f_card_b = load_font(17)
    f_cost = load_font(30)
    f_tip = load_font(16)

    art_src = REPO / "source_assets/cards/things_collision_ironclad_fogmog_v8_imagegen.png"
    if not art_src.is_file():
        art_src = REPO / "images/cards/things_leech_parasite.png"
    card_art = Image.open(art_src).convert("RGB").resize((244, 184), Image.Resampling.LANCZOS)

    cd.text((40, 22), "【墨浊覆牌】手牌染黑机制视觉方案预览（完全遮盖卡面/卡名/费用/描述/悬停提示，可盲打，断触腕即冲净）",
            fill=(232, 242, 248), font=load_font(25))

    def draw_base_card() -> Image.Image:
        cw, ch = 300, 422
        c = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        d = ImageDraw.Draw(c)
        d.rounded_rectangle((6, 6, cw - 6, ch - 6), radius=18, fill=(58, 28, 30, 255), outline=(145, 118, 86, 255), width=5)
        d.rounded_rectangle((14, 14, cw - 14, ch - 14), radius=13, fill=(82, 34, 34, 255), outline=(42, 20, 22, 255), width=2)
        d.rounded_rectangle((28, 22, cw - 28, 60), radius=8, fill=(138, 124, 102, 255), outline=(45, 34, 28, 255), width=3)
        d.text((104, 28), "重击冲锋", fill=(250, 245, 232, 255), font=f_card_t)
        c.paste(card_art, (28, 66))
        d.rectangle((28, 66, 272, 250), outline=(45, 32, 26, 255), width=3)
        d.rounded_rectangle((106, 238, 194, 266), radius=10, fill=(142, 128, 106, 255), outline=(40, 30, 24, 255), width=2)
        d.text((132, 242), "攻击", fill=(32, 24, 18, 255), font=f_card_b)
        d.rounded_rectangle((24, 274, cw - 24, ch - 24), radius=10, fill=(52, 30, 30, 255), outline=(112, 92, 68, 255), width=2)
        d.text((48, 306), "造成 14 点伤害。", fill=(242, 236, 222, 255), font=f_card_b)
        d.text((48, 336), "获得 8 点格挡。", fill=(242, 236, 222, 255), font=f_card_b)
        d.ellipse((2, 2, 66, 66), fill=(198, 56, 46, 255), outline=(235, 202, 128, 255), width=4)
        d.text((24, 16), "2", fill=(255, 248, 220, 255), font=f_cost)
        return c

    c_norm = draw_base_card()

    # 2) Rich flowing viscous abyssal squid-ink card coat (procedural domain-warped FBM liquid ink shader preview!)
    cw, ch = c_norm.size
    ox, oy = 18, 18
    iw, ih = cw + 36, ch + 68
    yy, xx = np.mgrid[0:ih, 0:iw].astype(np.float32)
    u, v = (xx - ox) / cw, (yy - oy) / ch
    # Smooth organic swirling ink flow texture (simulating our Godot 4.5.1 CanvasItem ink shader!)
    swirl = (
        0.45 * np.sin(u * 7.2 + np.cos(v * 6.5) * 1.8)
        + 0.35 * np.cos(v * 9.5 - np.sin(u * 8.1) * 1.6)
        + 0.20 * np.sin((u + v) * 14.0 + np.cos((u - v) * 11.0))
    )
    sheen = np.clip((swirl - 0.15) / 0.85, 0.0, 1.0)
    teal_vein = np.exp(-((swirl - 0.52) / 0.11) ** 2)

    # Mask card interior + dripping ink blobs hanging below card rim
    mask_im = Image.new("L", (iw, ih), 0)
    md = ImageDraw.Draw(mask_im)
    md.rounded_rectangle((ox + 2, oy + 2, ox + cw - 2, oy + ch - 2), radius=20, fill=255)
    # Organic billowing drips off bottom & sides
    drips = [
        (ox + 42, oy + ch - 4, 22, 46), (ox + 114, oy + ch - 2, 26, 58),
        (ox + 194, oy + ch - 6, 18, 36), (ox + 258, oy + ch - 4, 24, 50),
        (ox + 12, oy + 145, 20, 38), (ox + cw - 10, oy + 260, 20, 42),
    ]
    for (dx, dy, rw, rh) in drips:
        md.ellipse((dx - rw // 2, dy - rh // 2, dx + rw // 2, dy + rh // 2), fill=255)
    # Detached falling ink drops below
    for (dx, dy, r) in [(ox + 114, oy + ch + 44, 7), (ox + 258, oy + ch + 38, 6), (ox + 42, oy + ch + 34, 5)]:
        md.ellipse((dx - r, dy - r, dx + r, dy + r), fill=255)

    m_arr = np.array(mask_im, dtype=np.uint8)
    import cv2
    inner_m = cv2.erode(m_arr, np.ones((9, 9), np.uint8)) > 128
    rim_m = (m_arr > 128) & (~inner_m)

    ink_arr = np.zeros((ih, iw, 4), dtype=np.uint8)
    # Deep pitch-black ink core + dark indigo viscous oil billows + subtle abyssal teal shimmer!
    ink_arr[:, :, 0] = np.clip(7 + sheen * 32 + teal_vein * 38, 0, 255).astype(np.uint8)
    ink_arr[:, :, 1] = np.clip(6 + sheen * 28 + teal_vein * 92, 0, 255).astype(np.uint8)
    ink_arr[:, :, 2] = np.clip(14 + sheen * 66 + teal_vein * 108, 0, 255).astype(np.uint8)
    ink_arr[:, :, 3] = np.where(m_arr > 128, 252, 0)
    # Crisp dark-indigo outer gouache sticker rim
    ink_arr[rim_m, 0] = 48
    ink_arr[rim_m, 1] = 42
    ink_arr[rim_m, 2] = 84
    ink_arr[rim_m, 3] = 255

    ink_coat = Image.fromarray(ink_arr, mode="RGBA")
    idraw = ImageDraw.Draw(ink_coat)
    # Energy orb replaced by glossy pitch-black ink blot with ghostly cyan '?'
    idraw.ellipse((ox + 1, oy + 1, ox + 67, oy + 67), fill=(10, 8, 20, 255), outline=(92, 152, 168, 235), width=4)
    idraw.text((ox + 24, oy + 16), "?", fill=(155, 228, 232, 240), font=f_cost)
    # Subtle glowing horizontal cephalopod eye watermark in center of blind ink card
    idraw.arc((ox + 92, oy + 166, ox + 208, oy + 234), start=200, end=340, fill=(105, 192, 202, 165), width=4)
    idraw.arc((ox + 92, oy + 178, ox + 208, oy + 246), start=20, end=160, fill=(105, 192, 202, 165), width=4)
    idraw.rounded_rectangle((ox + 126, oy + 201, ox + 174, oy + 211), radius=4, fill=(155, 240, 235, 190))

    # 3) Wave-Washed Clean Transition (when player severs ANY bridge tentacle!)
    wash_ov = Image.new("RGBA", (iw, ih), (0, 0, 0, 0))
    wash_ov.alpha_composite(c_norm, (ox, oy))
    ink_w = np.array(ink_coat, dtype=np.uint8)
    diag = (xx * 0.72 + yy * 0.68) - 258.0 + 20.0 * np.sin(xx * 0.045)
    keep_ink = np.clip((0.0 - diag) / 26.0, 0.0, 1.0)
    ink_w[:, :, 3] = (ink_w[:, :, 3].astype(np.float32) * keep_ink).astype(np.uint8)
    wash_ov.alpha_composite(Image.fromarray(ink_w, mode="RGBA"))
    foam_band = np.exp(-0.5 * ((diag + 2.0) / 16.0) ** 2)
    f_arr = np.zeros_like(ink_w)
    f_arr[:, :, 0], f_arr[:, :, 1], f_arr[:, :, 2] = 155, 238, 232
    f_arr[:, :, 3] = np.where(m_arr > 0, np.clip(foam_band * 235, 0, 255), 0).astype(np.uint8)
    wash_ov.alpha_composite(Image.fromarray(f_arr, mode="RGBA"))

    # Spacious 3-column layout with ZERO overlap between HoverTip and Arrow 2!
    cols = [
        (56, "1. 正常手牌", "费用 / 卡名 / 卡图 / 描述正常显示", c_norm, False),
        (500, "2. 【墨浊覆牌】完全染黑", "所有牌面信息被流动渊墨吞没 · 仍可盲打", ink_coat, True),
        (1235, "3. 【断腕冲净】浪潮洗净", "斩断任意桥上触腕触发浪花，全手牌瞬间洗净", wash_ov, False),
    ]
    for x, head, sub, im_c, show_tip in cols:
        cd.text((x, 86), head, fill=(160, 238, 232) if show_tip else (235, 240, 246), font=f_title)
        cd.text((x, 124), sub, fill=(175, 188, 202), font=f_sub)
        px = x + 10 if show_tip else x + (340 - im_c.width) // 2
        py = 166
        canvas.paste(im_c.convert("RGB"), (px, py), im_c.split()[3])
        if show_tip:
            tx, ty = px + im_c.width + 18, 244
            cd.rounded_rectangle((tx, ty, tx + 240, ty + 158), radius=10, fill=(22, 24, 34), outline=(92, 152, 168), width=2)
            cd.text((tx + 14, ty + 12), "墨浊覆牌", fill=(155, 232, 228), font=f_sub)
            cd.text((tx + 14, ty + 46), "此牌已被渊墨彻底覆盖，", fill=(218, 224, 234), font=f_tip)
            cd.text((tx + 14, ty + 70), "隐藏牌面，仍可按原费盲打。", fill=(218, 224, 234), font=f_tip)
            cd.text((tx + 14, ty + 100), "斩断桥上任意触腕，或", fill=(165, 222, 218), font=f_tip)
            cd.text((tx + 14, ty + 124), "回合结束时立即冲净。", fill=(165, 222, 218), font=f_tip)

    # Clean well-spaced arrows (zero overlap!)
    for ax in (428, 1152):
        cd.polygon([(ax, 360), (ax + 26, 360), (ax + 26, 348), (ax + 48, 370), (ax + 26, 392), (ax + 26, 380), (ax, 380)],
                   fill=(115, 205, 210))

    canvas.save(out_path, optimize=True)
    print(f"Wrote {out_path}")


def main() -> None:
    specs = [
        ("seq_1_stage_idle", "abyssal_inkmaw_stage_idle.gif"),
        ("seq_2_ink_and_phase2", "abyssal_inkmaw_ink_and_phase2.gif"),
        ("seq_3_tentacles_actions", "abyssal_inkmaw_tentacles_actions.gif"),
    ]
    loaded: dict[str, list[Image.Image]] = {}
    for folder_name, gif_name in specs:
        frames = save_gif(FRAMES_ROOT / folder_name, BRAIN / gif_name, fps=18, max_w=800)
        loaded[folder_name] = frames
        print(f"Wrote {gif_name} ({len(frames)} native Godot Spine frames)")

    # Build 3x3 Extreme-Pose Keyframes Audit Board
    font = load_font(20)
    # Seq 2 total = 0.30 + 2.00 + 3.60 = 5.90s; Seq 3 total = 1.25 + 1.40 + 1.40 + 1.15 + 1.50 = 6.70s
    picks = [
        ("seq_1_stage_idle", 0.12, "1. 半身潜渊完整躯干·湖面浪花与12骨水中触腕"),
        ("seq_1_stage_idle", 0.64, "2. 硬件眼眶裁切·1:1刚体眼睑平滑下移眨眼"),
        ("seq_2_ink_and_phase2", 0.160, "3. 柱状高压墨炮冲出屏底·起手锁定左桥横扫"),
        ("seq_2_ink_and_phase2", 0.222, "4. 柱状水柱轰过桥心·触发 ink_hit_player 爆冠上Buff"),
        ("seq_2_ink_and_phase2", 0.278, "5. 压环沿柱身高速下冲·横扫至桥右并飞溅墨箭"),
        ("seq_2_ink_and_phase2", 0.563, "6. 二阶段【暗潮狂涌】·头冠与肩斑荧光暴燃"),
        ("seq_3_tentacles_actions", 0.052, "7. 硬件裁切1:1无拉伸破水钻出·手绘海浪与飞珠"),
        ("seq_3_tentacles_actions", 0.295, "8. 左侧12骨纯旋转S型蓄力鞭抽重砸"),
        ("seq_3_tentacles_actions", 0.848, "9. 古桥破土爆裂·4铰链翻翘石板+5旋转弹跳碎石"),
    ]
    cw, ch = 480, 270
    board = Image.new("RGB", (cw * 3 + 64, (ch + 44) * 3 + 36), (16, 20, 28))
    draw = ImageDraw.Draw(board)
    for idx, (seq_key, ratio, title) in enumerate(picks):
        seq = loaded[seq_key]
        fi = min(len(seq) - 1, max(0, round(ratio * (len(seq) - 1))))
        thumb = seq[fi].resize((cw, ch), Image.Resampling.LANCZOS)
        gx = 16 + (idx % 3) * (cw + 16)
        gy = 16 + (idx // 3) * (ch + 44)
        board.paste(thumb, (gx, gy))
        draw.text((gx + 8, gy + ch + 8), title, fill=(228, 238, 245), font=font)
    board.save(BRAIN / "abyssal_inkmaw_keyframes_audit.jpg", quality=93)
    print("Wrote abyssal_inkmaw_keyframes_audit.jpg")

    # Build Multi-Aspect (16:9, 21:9, 4:3) Framing + HUD/Safe-Zone Verification Board
    asp_dir = FRAMES_ROOT / "aspects"
    im16 = overlay_combat_hud_guides(Image.open(asp_dir / "aspect_16x9.png"), "16:9 主战斗构图")
    im21 = overlay_combat_hud_guides(Image.open(asp_dir / "aspect_21x9.png"), "21:9 超宽屏无缝延展")
    im43 = overlay_combat_hud_guides(Image.open(asp_dir / "aspect_4x3.png"), "4:3 窄屏无裁切安全区")

    # Top row: 16:9 (1280x720); Bottom row: 21:9 (800x338) + 4:3 (452x338)
    aw, ah = 1320, 1110
    aboard = Image.new("RGB", (aw, ah), (14, 18, 26))
    aboard.paste(im16.resize((1280, 720), Image.Resampling.LANCZOS), (20, 20))
    aboard.paste(im21.resize((812, 342), Image.Resampling.LANCZOS), (20, 752))
    aboard.paste(im43.resize((456, 342), Image.Resampling.LANCZOS), (844, 752))
    aboard.save(BRAIN / "abyssal_inkmaw_stage_aspects.jpg", quality=93)
    print("Wrote abyssal_inkmaw_stage_aspects.jpg")

    # Build Ink-Blackened Card Concept Mockup
    make_ink_card_mockup(BRAIN / "abyssal_inkmaw_ink_card_mockup.png")


if __name__ == "__main__":
    main()
