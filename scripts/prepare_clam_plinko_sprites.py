"""Extract authentic Mega Crit Sewer Clam jaw parts from vanilla sewer_clam.png and craft
4x supersampled Mega Crit painterly Plinko sprites (amber seed, briar peg, glow-shroom bumper).
"""
from __future__ import annotations

import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "images" / "events" / "minigames"
VANILLA_CLAM = ROOT.parent / "STS2-V111" / "animations" / "monsters" / "sewer_clam" / "sewer_clam.png"


def build_sewer_clam_jaws() -> list[dict]:
    src = Image.open(VANILLA_CLAM).convert("RGBA")
    results: list[dict] = []

    # 1. Mouth back interior throat from solid rounded 'mouth back' (bounds: 363, 127, 285, 203, rotate:90 -> 203x285 in PNG)
    mouth_raw = src.crop((363, 127, 566, 412))
    mouth_canvas = Image.new("RGBA", (200, 200), (0, 0, 0, 0))
    mouth_resized = mouth_raw.resize((172, 184), Image.Resampling.LANCZOS)
    mouth_canvas.alpha_composite(mouth_resized, (14, 8))
    p_mouth = OUT_DIR / "things_clam_mouth_back.png"
    mouth_canvas.save(p_mouth, format="PNG")
    results.append({"file": p_mouth.name, "size": list(mouth_canvas.size)})

    # 2. UPPER SHELL JAW (left hinged pincer shell: curves outward-left, purple-gold lip tucked flush behind inner scallops)
    top_shell = src.crop((392, 731, 554, 1017))
    top_lips = src.crop((340, 414, 472, 708))
    coral_sprig = src.crop((112, 718, 218, 865)).rotate(38, resample=Image.Resampling.BICUBIC, expand=True)

    upper = Image.new("RGBA", (168, 232), (0, 0, 0, 0))
    coral_small = coral_sprig.resize((58, 64), Image.Resampling.LANCZOS)
    upper.alpha_composite(coral_small, (6, 22))
    # Tuck wavy purple-gold mantle lip flush right along the inner biting edge of the upper shell (zero gap!)
    lip_u = top_lips.resize((62, 186), Image.Resampling.LANCZOS)
    upper.alpha_composite(lip_u, (64, 24))
    # Scalloped pale-lavender fluted shell arching cleanly down-left, overlapping the lip root seamlessly
    shell_u = top_shell.resize((116, 210), Image.Resampling.LANCZOS)
    upper.alpha_composite(shell_u, (22, 10))
    p_upper = OUT_DIR / "things_clam_shell_upper.png"
    upper.save(p_upper, format="PNG")
    results.append({"file": p_upper.name, "size": list(upper.size)})

    # 3. LOWER SHELL JAW (right hinged pincer shell: curves outward-right, purple-gold lip tucked flush behind inner scallops)
    bot_shell = src.crop((2, 4, 286, 112)).rotate(90, resample=Image.Resampling.BICUBIC, expand=True)
    bot_lips = src.crop((279, 114, 361, 411))
    coral_nub = src.crop((112, 718, 218, 865)).transpose(Image.Transpose.FLIP_LEFT_RIGHT).rotate(-32, resample=Image.Resampling.BICUBIC, expand=True)

    lower = Image.new("RGBA", (168, 232), (0, 0, 0, 0))
    coral_r = coral_nub.resize((50, 56), Image.Resampling.LANCZOS)
    lower.alpha_composite(coral_r, (112, 26))
    # Tuck wavy purple-gold lip flush behind the inner biting teeth of the lower shell (zero gap!)
    lip_l = bot_lips.resize((52, 186), Image.Resampling.LANCZOS)
    lower.alpha_composite(lip_l, (34, 22))
    # Scalloped pale-lavender fluted lower shell arching cleanly down-right
    shell_l = bot_shell.resize((104, 210), Image.Resampling.LANCZOS)
    lower.alpha_composite(shell_l, (44, 10))
    p_lower = OUT_DIR / "things_clam_shell_lower.png"
    lower.save(p_lower, format="PNG")
    results.append({"file": p_lower.name, "size": list(lower.size)})

    return results


def build_plinko_sprites() -> list[dict]:
    results: list[dict] = []

    # 1. 4x Supersampled Glowing Golden Amber Seed (512 -> 128 RGBA)
    seed_hi = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    sd = ImageDraw.Draw(seed_hi)
    # Soft outer warm aura
    sd.ellipse([44, 44, 468, 468], fill=(246, 178, 42, 72))
    seed_hi = seed_hi.filter(ImageFilter.GaussianBlur(24))
    sd = ImageDraw.Draw(seed_hi)
    # Deep chestnut shadow rim
    sd.ellipse([88, 88, 424, 424], fill=(92, 42, 12, 255))
    # Rich amber body
    sd.ellipse([104, 102, 408, 406], fill=(228, 142, 24, 255))
    # Faceted angular Mega Crit light plane
    sd.polygon([
        (146, 140), (272, 108), (376, 168), (350, 286), (224, 346), (126, 256)
    ], fill=(252, 198, 62, 255))
    # Bright luminous inner seed kernel
    sd.polygon([
        (178, 160), (266, 134), (318, 184), (272, 248), (186, 244)
    ], fill=(255, 236, 138, 255))
    # Crisp chipped specular highlight
    sd.polygon([(184, 166), (236, 148), (250, 178), (196, 198)], fill=(255, 252, 230, 255))
    seed = seed_hi.resize((128, 128), Image.Resampling.LANCZOS)
    p_seed = OUT_DIR / "things_plinko_seed.png"
    seed.save(p_seed, format="PNG")
    results.append({"file": p_seed.name, "size": list(seed.size)})

    # 2. 4x Supersampled Chipped Ironwood Briar Peg (384 -> 96 RGBA)
    peg_hi = Image.new("RGBA", (384, 384), (0, 0, 0, 0))
    pd = ImageDraw.Draw(peg_hi)
    # Contact drop shadow
    pd.ellipse([64, 84, 336, 356], fill=(4, 14, 7, 195))
    # Dark bark outer bevel
    pd.polygon([
        (192, 48), (288, 84), (332, 180), (294, 286), (192, 330), (92, 286), (52, 184), (96, 86)
    ], fill=(34, 52, 34, 255))
    # Mid mossy ironwood face
    pd.polygon([
        (192, 74), (268, 104), (304, 184), (270, 266), (192, 300), (114, 266), (80, 184), (116, 104)
    ], fill=(104, 128, 90, 255))
    # Sunlit top-left chipped wood bevel
    pd.polygon([
        (192, 74), (266, 104), (236, 170), (144, 206), (84, 182), (116, 104)
    ], fill=(172, 194, 134, 255))
    # Warm brass sap pin in center
    pd.ellipse([156, 152, 228, 224], fill=(228, 182, 76, 255))
    pd.ellipse([166, 160, 204, 196], fill=(254, 232, 152, 255))
    peg = peg_hi.resize((96, 96), Image.Resampling.LANCZOS)
    p_peg = OUT_DIR / "things_plinko_peg.png"
    peg.save(p_peg, format="PNG")
    results.append({"file": p_peg.name, "size": list(peg.size)})

    # 3. 4x Supersampled Glowing Overgrowth Shroom Bumper (512 -> 128 RGBA)
    sh_hi = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    shd = ImageDraw.Draw(sh_hi)
    shd.ellipse([36, 36, 476, 476], fill=(82, 236, 136, 70))
    sh_hi = sh_hi.filter(ImageFilter.GaussianBlur(24))
    shd = ImageDraw.Draw(sh_hi)
    # Shadow rim
    shd.ellipse([72, 76, 440, 444], fill=(14, 48, 26, 245))
    # Faceted glowing cap
    shd.polygon([
        (256, 78), (382, 126), (432, 248), (386, 376), (256, 430), (126, 376), (80, 248), (130, 126)
    ], fill=(56, 182, 106, 255))
    shd.polygon([
        (256, 94), (366, 138), (344, 264), (168, 294), (98, 238), (140, 136)
    ], fill=(122, 238, 154, 255))
    # Bright golden-emerald mushroom spots
    for (sx, sy, sr) in [(186, 178, 36), (310, 198, 28), (240, 298, 32), (336, 316, 22), (160, 304, 20)]:
        shd.ellipse([sx - sr, sy - sr, sx + sr, sy + sr], fill=(246, 236, 146, 255))
        shd.ellipse([sx - sr // 2, sy - sr // 2, sx + sr // 4, sy + sr // 4], fill=(255, 254, 224, 255))
    shroom = sh_hi.resize((128, 128), Image.Resampling.LANCZOS)
    p_shroom = OUT_DIR / "things_plinko_shroom.png"
    shroom.save(p_shroom, format="PNG")
    results.append({"file": p_shroom.name, "size": list(shroom.size)})

    return results


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    report = {
        "sewer_clam_jaws": build_sewer_clam_jaws(),
        "plinko_sprites": build_plinko_sprites(),
    }
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
