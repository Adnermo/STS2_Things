"""Package Depths multiplayer Bronze Tripod Scramble (ThingsBronzeCauldron) art:
1. 3440x1616 RGBA event portrait (`images/events/things_bronze_cauldron.png`) from
   `bronze_tripod_scramble_v2_1795106141473.jpg` (already overscan-safe in x=21%..41%, y=26%..75%!).
2. 1920x1080 RGBA Crystal-Sphere minigame backdrop (`images/events/minigames/things_bronze_cauldron_bg.png`)
   cleanly scaled so the circular bronze tripod interior sits dead-centered inside (500, 215, 1130, 845)
   with the right bronze handle ending around x=1225 and pure calm Depths indigo shadow on the right!
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
EVENT_OUT = ROOT / "images" / "events"
MINI_OUT = ROOT / "images" / "events" / "minigames"
ARCHIVE = ROOT / "source_assets" / "events_depths_cauldron_20261120"

EV_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\bronze_tripod_scramble_v2_1795106141473.jpg")
BG_RAW = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa\bronze_tripod_scramble_bg_final_1795106378715.jpg")

EV_W, EV_H = 3440, 1616
BG_W, BG_H = 1920, 1080
PIT_CX, PIT_CY = 815.0, 530.0  # Center of (500, 215, 1130, 845)


def smoothstep(e0: float, e1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def sha256_file(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest().upper()


def build_event_portrait() -> dict:
    # bronze_tripod_scramble_v2 is 1376x768 (16:9) with focal cauldron already cleanly at x=21%..41%, y=26%..75%!
    # Map 16:9 -> 2.13:1 (3440x1616) by centered vertical crop (keeping 100% of y=9%..91% which safely encloses y=26%..75%!)
    src = Image.open(EV_RAW).convert("RGB")
    sw, sh = src.size
    target_h = int(round(sw * EV_H / EV_W))  # ~646
    top = (sh - target_h) // 2
    cropped = src.crop((0, top, sw, top + target_h)).resize((EV_W, EV_H), Image.Resampling.LANCZOS)

    arr = np.asarray(cropped, dtype=np.float32)
    xs = np.arange(EV_W, dtype=np.float32)[None, :] / EV_W
    # Ultra-gentle calm right prose darkening preserving indigo hue
    right_mask = smoothstep(0.47, 0.64, xs)
    arr = arr * (1.0 - 0.28 * right_mask[:, :, None]) + np.array([10.0, 14.0, 26.0], dtype=np.float32) * (0.18 * right_mask[:, :, None])

    out = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB").convert("RGBA")
    out_path = EVENT_OUT / "things_bronze_cauldron.png"
    out.save(out_path, format="PNG")
    return {"file": str(out_path.relative_to(ROOT)), "size": list(out.size), "sha256": sha256_file(out_path)}


def build_minigame_backdrop() -> dict:
    # In BG_RAW (1376x768):
    # Inner dark circular basin center sits at cx ~ 0.432*1376 = 595, cy ~ 0.495*768 = 380, inner radius ~ 256px.
    # Scale to 1.155 -> 1589x887 so inner cauldron basin fits cleanly centered at (815, 530) with radius ~296px!
    src = Image.open(BG_RAW).convert("RGB")
    sw, sh = src.size
    scale = 1.155
    nw, nh = int(round(sw * scale)), int(round(sh * scale))  # 1589 x 887
    scaled = src.resize((nw, nh), Image.Resampling.LANCZOS)

    dx = int(round(PIT_CX - 595.0 * scale))  # ~128
    dy = int(round(PIT_CY - 380.0 * scale))  # ~91

    # Seamlessly pad outer edges using ONLY outer wall/shadow strips (ZERO duplicated brazier!)
    # 1. Pad horizontally first to BG_W (pure stone wall strip 0..12 -> 0..dx, right indigo shadow nw-160..nw -> dx+nw..BG_W)
    h_strip = Image.new("RGB", (BG_W, nh), (10, 16, 28))
    if dx > 0:
        left_edge = scaled.crop((0, 0, 12, nh)).resize((dx, nh), Image.Resampling.LANCZOS)
        h_strip.paste(left_edge, (0, 0))
    h_strip.paste(scaled, (dx, 0))
    right_start = dx + nw
    if right_start < BG_W:
        right_edge = scaled.crop((nw - 160, 0, nw, nh)).resize((BG_W - right_start, nh), Image.Resampling.LANCZOS)
        h_strip.paste(right_edge, (right_start, 0))

    # 2. Pad vertically to BG_H using pure top cornice (0..22) & bottom stone ledge (nh-26..nh)
    canvas = Image.new("RGB", (BG_W, BG_H), (8, 12, 22))
    if dy > 0:
        top_edge = h_strip.crop((0, 0, BG_W, 20)).resize((BG_W, dy), Image.Resampling.LANCZOS)
        canvas.paste(top_edge, (0, 0))
    canvas.paste(h_strip, (0, dy))
    bot_start = dy + nh
    if bot_start < BG_H:
        bot_edge = h_strip.crop((0, nh - 24, BG_W, nh)).resize((BG_W, BG_H - bot_start), Image.Resampling.LANCZOS)
        canvas.paste(bot_edge, (0, bot_start))

    arr = np.asarray(canvas, dtype=np.float32)
    xs = np.arange(BG_W, dtype=np.float32)[None, :]
    ys = np.arange(BG_H, dtype=np.float32)[:, None]

    right_mask = smoothstep(1235.0, 1480.0, xs)
    arr *= (1.0 - 0.52 * right_mask[:, :, None])
    bl_mask = (1.0 - smoothstep(260.0, 740.0, xs)) * smoothstep(885.0, 1055.0, ys)
    arr *= (1.0 - 0.42 * bl_mask[:, :, None])
    tl_mask = (1.0 - smoothstep(280.0, 680.0, xs)) * (1.0 - smoothstep(38.0, 160.0, ys))
    arr *= (1.0 - 0.42 * tl_mask[:, :, None])

    out = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB").convert("RGBA")
    out_path = MINI_OUT / "things_bronze_cauldron_bg.png"
    out.save(out_path, format="PNG")
    return {"file": str(out_path.relative_to(ROOT)), "size": list(out.size), "sha256": sha256_file(out_path)}


def main() -> int:
    EVENT_OUT.mkdir(parents=True, exist_ok=True)
    MINI_OUT.mkdir(parents=True, exist_ok=True)
    ARCHIVE.mkdir(parents=True, exist_ok=True)
    report = {
        "event_portrait": build_event_portrait(),
        "minigame_backdrop": build_minigame_backdrop(),
    }
    (ARCHIVE / "manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
