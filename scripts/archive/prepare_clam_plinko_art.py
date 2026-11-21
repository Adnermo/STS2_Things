"""Package approved Clam Claw & Forest Plinko master paintings into:
1. 3440x1616 RGBA overscan-safe event portraits (`images/events/things_clam_claw.png`, `things_forest_plinko.png`)
   with pulled-back focal framing inside x=19%..44%, y=22%..76% and calm nocturnal right prose zones.
2. 1920x1080 RGBA Crystal-Sphere-style minigame backdrops (`images/events/minigames/things_clam_claw_bg.png`,
   `things_forest_plinko_bg.png`) aligned to the exact (500, 215, 1130, 845) 630x630 playfield pit.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
EVENT_OUT = ROOT / "images" / "events"
MINI_OUT = ROOT / "images" / "events" / "minigames"
ARCHIVE = ROOT / "source_assets" / "events_act1_clam_plinko_20261120"

CLAM_EV_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\clam_claw_crane_event_v2_1795105534986.jpg")
PLINKO_EV_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\overgrown_plinko_event_v2_1795104854390.jpg")
CLAM_BG_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\clam_claw_crane_bg_1795104853721.jpg")
PLINKO_BG_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\overgrown_plinko_bg_v2_1795105071289.jpg")

EV_W, EV_H = 3440, 1616
BG_W, BG_H = 1920, 1080


def smoothstep(e0: float, e1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def sha256_file(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest().upper()


def build_event_portrait(src_path: Path, out_name: str, scale_factor: float, target_cx: float, target_cy: float, bg_rgb: tuple[int, int, int]) -> dict:
    """Place a 16:9 event diorama cleanly into 3440x1616 RGBA with seamless continuous
    edge extension so the focal subject sits completely inside the DefaultEventLayout (-371, -79)
    overscan-safe left stage (x=560..1600, y=250..1380) and right UI zone stays calm dark shadow.
    """
    src = Image.open(src_path).convert("RGB")
    sw, sh = src.size

    # Step 1: Build unbroken continuous nocturnal backdrop in 3440x1616 using soft edge-stretched background
    # Sample calm dark right & outer regions of src for seamless biome-true color matching
    canvas = Image.new("RGB", (EV_W, EV_H), bg_rgb)
    # Soft ambient full-bleed background from src blurred + darkened so no seam can ever exist
    ambient = src.resize((EV_W, EV_H), Image.Resampling.LANCZOS).filter(ImageFilter.GaussianBlur(42))
    amb_arr = np.asarray(ambient, dtype=np.float32) * 0.48 + np.array(bg_rgb, dtype=np.float32) * 0.52
    canvas = Image.fromarray(np.uint8(np.clip(np.rint(amb_arr), 0, 255)), mode="RGB").convert("RGBA")

    # Step 2: Place scaled diorama cleanly with generous feathered perimeter so edges melt into continuous world
    pw = int(round(EV_W * scale_factor))
    ph = int(round(pw * sh / sw))
    placed = src.resize((pw, ph), Image.Resampling.LANCZOS).convert("RGBA")

    # Feather all 4 outer edges smoothly (more generous on right & top/bottom)
    pxs = np.linspace(0.0, 1.0, pw, dtype=np.float32)[None, :]
    pys = np.linspace(0.0, 1.0, ph, dtype=np.float32)[:, None]
    alpha = (
        smoothstep(0.0, 0.08, pxs)
        * (1.0 - smoothstep(0.76, 0.99, pxs))
        * smoothstep(0.0, 0.11, pys)
        * (1.0 - smoothstep(0.87, 0.99, pys))
    )
    p_arr = np.asarray(placed, dtype=np.float32)
    p_arr[:, :, 3] = np.clip(alpha * 255.0, 0.0, 255.0)
    placed_feathered = Image.fromarray(np.uint8(np.rint(p_arr)), mode="RGBA")

    dx = int(round(EV_W * target_cx - pw * 0.36))
    dy = int(round(EV_H * target_cy - ph * 0.50))
    canvas.alpha_composite(placed_feathered, (dx, dy))

    # Step 3: Calm, hue-preserving nocturnal shadow falloff on the right prose/options zone (x >= 47%)
    arr = np.asarray(canvas.convert("RGB"), dtype=np.float32)
    xs = np.arange(EV_W, dtype=np.float32)[None, :] / EV_W
    right_dim = smoothstep(0.45, 0.62, xs)
    # Keep subtle dark biome color, gently suppress contrast in prose area (proseL ~ 12..24)
    arr = arr * (1.0 - 0.52 * right_dim[:, :, None]) + np.array(bg_rgb, dtype=np.float32) * (0.30 * right_dim[:, :, None])

    out = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB").convert("RGBA")
    out_path = EVENT_OUT / out_name
    out.save(out_path, format="PNG")
    return {
        "file": str(out_path.relative_to(ROOT)),
        "size": list(out.size),
        "sha256": sha256_file(out_path),
    }


def build_minigame_bg(src_path: Path, out_name: str, right_dim_strength: float) -> dict:
    src = Image.open(src_path).convert("RGB").resize((BG_W, BG_H), Image.Resampling.LANCZOS)
    arr = np.asarray(src, dtype=np.float32)
    xs = np.arange(BG_W, dtype=np.float32)[None, :]
    ys = np.arange(BG_H, dtype=np.float32)[:, None]

    # Subtle Crystal-Sphere calm right-column & HUD-corner dimming (never touches the 500..1130 pit!)
    right_mask = smoothstep(1235.0, 1495.0, xs)
    arr *= (1.0 - right_dim_strength * right_mask[:, :, None])
    bl_mask = (1.0 - smoothstep(260.0, 740.0, xs)) * smoothstep(885.0, 1055.0, ys)
    arr *= (1.0 - 0.40 * bl_mask[:, :, None])
    tl_mask = (1.0 - smoothstep(280.0, 680.0, xs)) * (1.0 - smoothstep(38.0, 160.0, ys))
    arr *= (1.0 - 0.42 * tl_mask[:, :, None])

    out = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB").convert("RGBA")
    out_path = MINI_OUT / out_name
    out.save(out_path, format="PNG")
    return {
        "file": str(out_path.relative_to(ROOT)),
        "size": list(out.size),
        "sha256": sha256_file(out_path),
    }


def main() -> int:
    EVENT_OUT.mkdir(parents=True, exist_ok=True)
    MINI_OUT.mkdir(parents=True, exist_ok=True)
    ARCHIVE.mkdir(parents=True, exist_ok=True)

    manifest = {
        "clam_claw_event": build_event_portrait(
            CLAM_EV_RAW, "things_clam_claw.png",
            scale_factor=0.66, target_cx=0.31, target_cy=0.51, bg_rgb=(7, 23, 28)),
        "forest_plinko_event": build_event_portrait(
            PLINKO_EV_RAW, "things_forest_plinko.png",
            scale_factor=0.66, target_cx=0.33, target_cy=0.50, bg_rgb=(6, 20, 13)),
        "clam_claw_bg": build_minigame_bg(CLAM_BG_RAW, "things_clam_claw_bg.png", right_dim_strength=0.50),
        "forest_plinko_bg": build_minigame_bg(PLINKO_BG_RAW, "things_forest_plinko_bg.png", right_dim_strength=0.48),
    }
    (ARCHIVE / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
