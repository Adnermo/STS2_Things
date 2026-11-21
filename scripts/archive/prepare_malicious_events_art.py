"""Package the 5 remade vanilla-authentic malicious choice-event illustrations
into exact 3440x1616 RGBA STS2 event portraits (`images/events/things_*.png`),
preserving razor-sharp Mega Crit chiseled facets, compact isolated diorama scale,
and ~80% ultra-dark nocturnal negative space for Godot DefaultEventLayout
(2662x1251 @ (-371,-79) in 1920x1080).
"""
from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
EVENT_OUT = ROOT / "images" / "events"
ARCHIVE = ROOT / "source_assets" / "events_malicious_v2_20261120"
BRAIN = Path(r"C:\Users\adner\.gemini\antigravity\brain\72a88296-2a0f-46df-b54d-099dc639d4aa")

EV_W, EV_H = 3440, 1616

SPECS = [
    {
        "slug": "things_lending_vine",
        "raw": BRAIN / "things_lending_vine_v2_alt_1795180382448.jpg",
        "mode": "crop",
        "v_bias": 0.44,
        "shift_x_frac": 0.01,
        "right_tint": (5.0, 12.0, 10.0),
        "right_darken": 0.12,
    },
    {
        "slug": "things_overstuffed_brood",
        "raw": BRAIN / "things_overstuffed_brood_v2_1795180575415.jpg",
        "mode": "zoom_out",
        "scale": 0.90,
        "dx_frac": 0.04,
        "dy_frac": 0.03,
        "right_tint": (5.0, 7.0, 12.0),
        "right_darken": 0.14,
    },
    {
        "slug": "things_scales_of_vanity",
        "raw": BRAIN / "things_scales_of_vanity_v2_1795180325313.jpg",
        "mode": "top_anchored_pad",
        "scale": 0.90,
        "dx_frac": 0.105,
        "right_tint": (9.0, 11.0, 16.0),
        "right_darken": 0.12,
    },
    {
        "slug": "things_wall_crack_ledger",
        "raw": BRAIN / "things_wall_crack_ledger_v2_1795180276565.jpg",
        "mode": "zoom_out",
        "scale": 0.92,
        "dx_frac": 0.055,
        "dy_frac": 0.04,
        "right_tint": (9.0, 12.0, 20.0),
        "right_darken": 0.10,
    },
    {
        "slug": "things_spiked_door",
        "raw": BRAIN / "things_spiked_door_v2_remake_1795180627944.jpg",
        "mode": "zoom_out",
        "scale": 0.88,
        "dx_frac": 0.055,
        "dy_frac": 0.06,
        "right_tint": (8.0, 10.0, 15.0),
        "right_darken": 0.10,
    },
]


def smoothstep(e0: float, e1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def sha256_file(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest().upper()


def sample_edge_color(img: Image.Image, side: str) -> tuple[int, int, int]:
    arr = np.asarray(img, dtype=np.float32)
    if side == "left":
        patch = arr[:, :16, :]
    elif side == "right":
        patch = arr[:, -24:, :]
    elif side == "top":
        patch = arr[:16, :, :]
    else:
        patch = arr[-16:, :, :]
    med = np.median(patch.reshape(-1, 3), axis=0)
    return (int(med[0]), int(med[1]), int(med[2]))


def extend_edges_smooth(img: Image.Image, target_w: int, target_h: int, dx: int, dy: int) -> Image.Image:
    """Place `img` at (dx, dy) on a (target_w, target_h) canvas, seamlessly extending
    dark outer strips with gentle seam feathering."""
    sw, sh = img.size
    h_strip = Image.new("RGB", (target_w, sh), sample_edge_color(img, "right"))
    if dx > 0:
        strip = img.crop((0, 0, min(20, sw), sh)).resize((dx, sh), Image.Resampling.LANCZOS)
        h_strip.paste(strip, (0, 0))
    paste_x = max(0, dx)
    src_x0 = max(0, -dx)
    src_x1 = min(sw, target_w - dx)
    if src_x1 > src_x0:
        h_strip.paste(img.crop((src_x0, 0, src_x1, sh)), (paste_x, 0))
    right_start = dx + sw
    if right_start < target_w:
        strip = img.crop((max(0, sw - 64), 0, sw, sh)).resize((target_w - right_start, sh), Image.Resampling.LANCZOS)
        h_strip.paste(strip, (right_start, 0))

    canvas = Image.new("RGB", (target_w, target_h), sample_edge_color(img, "bottom"))
    if dy > 0:
        strip = h_strip.crop((0, 0, target_w, min(18, sh))).resize((target_w, dy), Image.Resampling.LANCZOS)
        canvas.paste(strip, (0, 0))
    paste_y = max(0, dy)
    src_y0 = max(0, -dy)
    src_y1 = min(sh, target_h - dy)
    if src_y1 > src_y0:
        canvas.paste(h_strip.crop((0, src_y0, target_w, src_y1)), (0, paste_y))
    bot_start = dy + sh
    if bot_start < target_h:
        strip = h_strip.crop((0, max(0, sh - 18), target_w, sh)).resize((target_w, target_h - bot_start), Image.Resampling.LANCZOS)
        canvas.paste(strip, (0, bot_start))

    arr = np.asarray(canvas, dtype=np.float32)
    blurred = np.asarray(canvas.filter(ImageFilter.GaussianBlur(radius=16)), dtype=np.float32)
    ys = np.arange(target_h, dtype=np.float32)[:, None]
    xs = np.arange(target_w, dtype=np.float32)[None, :]
    seam_mask = np.zeros((target_h, target_w), dtype=np.float32)
    if dy > 0:
        seam_mask = np.maximum(seam_mask, np.exp(-((ys - dy) / 24.0) ** 2))
    if bot_start < target_h:
        seam_mask = np.maximum(seam_mask, np.exp(-((ys - bot_start) / 24.0) ** 2))
    if dx > 0:
        seam_mask = np.maximum(seam_mask, np.exp(-((xs - dx) / 32.0) ** 2))
    if right_start < target_w:
        seam_mask = np.maximum(seam_mask, np.exp(-((xs - right_start) / 40.0) ** 2))
    arr = arr * (1.0 - 0.65 * seam_mask[:, :, None]) + blurred * (0.65 * seam_mask[:, :, None])
    return Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB")


def process_one(spec: dict) -> dict:
    src = Image.open(spec["raw"]).convert("RGB")
    sw, sh = src.size
    mode = spec["mode"]

    if mode == "crop":
        target_h = int(round(sw * EV_H / EV_W))
        max_top = max(0, sh - target_h)
        top = int(round(max_top * spec["v_bias"]))
        cropped = src.crop((0, top, sw, top + target_h)).resize((EV_W, EV_H), Image.Resampling.LANCZOS)
        shift_px = int(round(EV_W * float(spec.get("shift_x_frac", 0.0))))
        if shift_px != 0:
            base = extend_edges_smooth(cropped, EV_W, EV_H, shift_px, 0)
        else:
            base = cropped
    elif mode == "top_anchored_pad":
        # Anchors y=0 strictly so diagonal ceiling light shafts remain razor-straight,
        # and gently dissolves the lower floor apron into deep midnight-slate before bottom pad
        # so zero vertical stretch kinks ever appear!
        scale = float(spec["scale"])
        nh = int(round(EV_H * scale))
        nw = int(round(nh * sw / sh))
        scaled = src.resize((nw, nh), Image.Resampling.LANCZOS)
        bg_col = np.array(sample_edge_color(scaled, "right"), dtype=np.float32)
        s_arr = np.asarray(scaled, dtype=np.float32)
        s_ys = np.arange(nh, dtype=np.float32)[:, None] / float(nh)
        bot_fade = smoothstep(0.87, 0.99, s_ys)
        s_arr = s_arr * (1.0 - bot_fade[:, :, None]) + bg_col[None, None, :] * bot_fade[:, :, None]
        scaled_clean = Image.fromarray(np.uint8(np.clip(np.rint(s_arr), 0, 255)), mode="RGB")
        dx = int(round(EV_W * float(spec["dx_frac"])))
        base = extend_edges_smooth(scaled_clean, EV_W, EV_H, dx, 0)
    else:
        scale = float(spec["scale"])
        nh = int(round(EV_H * scale))
        nw = int(round(nh * sw / sh))
        scaled = src.resize((nw, nh), Image.Resampling.LANCZOS)
        dx = int(round(EV_W * float(spec["dx_frac"])))
        dy = int(round(EV_H * float(spec["dy_frac"])))
        base = extend_edges_smooth(scaled, EV_W, EV_H, dx, dy)

    arr = np.asarray(base, dtype=np.float32)
    xs = np.arange(EV_W, dtype=np.float32)[None, :] / EV_W
    right_mask = smoothstep(0.49, 0.66, xs)
    darken = float(spec["right_darken"])
    tint = np.array(spec["right_tint"], dtype=np.float32)
    arr = arr * (1.0 - darken * right_mask[:, :, None]) + tint * (0.12 * right_mask[:, :, None])

    out = Image.fromarray(np.uint8(np.clip(np.rint(arr), 0, 255)), mode="RGB").convert("RGBA")
    out_path = EVENT_OUT / f"{spec['slug']}.png"
    out.save(out_path, format="PNG")

    archive_raw = ARCHIVE / "raw" / spec["raw"].name
    archive_raw.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(spec["raw"], archive_raw)

    return {
        "slug": spec["slug"],
        "file": str(out_path.relative_to(ROOT)),
        "size": list(out.size),
        "sha256": sha256_file(out_path),
    }


def main() -> int:
    EVENT_OUT.mkdir(parents=True, exist_ok=True)
    ARCHIVE.mkdir(parents=True, exist_ok=True)
    results = [process_one(spec) for spec in SPECS]
    (ARCHIVE / "manifest.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    print(json.dumps(results, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
