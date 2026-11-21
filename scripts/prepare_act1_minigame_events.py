"""Convert and verify Act 1 minigame event portraits (3440x1616 RGBA, Alpha=255),
archive originals and prompts, render offline 1920x1080 native-font previews (zhs/eng),
and verify things_echoing_well.png SHA-256 remains untouched.
"""
from __future__ import annotations

import hashlib
import json
import shutil
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageOps

from native_event_preview import render_event

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = ROOT / "source_assets" / "events_act1_minigames_20261119"
TARGET_SIZE = (3440, 1616)
WELL_SHA256 = "9A3C655EB7933FF4205EF9BC2B24AE36BFD177009F385282FFAC6462D31FF54E"


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def prose_luma_stats(img: Image.Image) -> tuple[float, float, float]:
    # Right UI zone in 3440x1616: x = 1671..2705, y = 240..1360
    crop = img.convert("RGB").crop((1671, 240, 2705, 1360))
    arr = np.asarray(crop, dtype=np.float32)
    luma = 0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]
    return float(luma.mean()), float(np.percentile(luma, 95)), float(luma.std())


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: python prepare_act1_minigame_events.py <bilge_raw_path> <loom_raw_path>")
        return 2

    bilge_raw = Path(sys.argv[1])
    loom_raw = Path(sys.argv[2])
    for p in (bilge_raw, loom_raw):
        if not p.is_file():
            raise FileNotFoundError(f"Missing raw image: {p}")

    # Verify echoing well untouched
    well_path = ROOT / "images" / "events" / "things_echoing_well.png"
    well_hash = sha256_file(well_path)
    if well_hash != WELL_SHA256:
        raise RuntimeError(f"things_echoing_well.png hash mismatch: {well_hash} != {WELL_SHA256}")

    raw_dir = ARCHIVE / "raw"
    prev_dir = ARCHIVE / "previews"
    raw_dir.mkdir(parents=True, exist_ok=True)
    prev_dir.mkdir(parents=True, exist_ok=True)

    specs = [
        (
            "things_bilge_mines.png",
            "THINGS_BILGE_MINES",
            bilge_raw,
            (0.5, 0.24),
            ("SWEEP", "SWEEP_PAID", "SKIM_EDGE"),
            {"FreeProbes": 5, "PaidProbes": 7, "PaidGold": 25, "MineDamage": 8, "EdgeGold": 20},
        ),
        (
            "things_moss_rune_loom.png",
            "THINGS_MOSS_RUNE_LOOM",
            loom_raw,
            (0.5, 0.50),
            ("WEAVE", "GATHER_SAP"),
            {"MaxSteps": 4, "SapHeal": 8},
        ),
    ]

    manifest = []
    for filename, event_id, src_path, centering, option_keys, variables in specs:
        shutil.copy2(src_path, raw_dir / src_path.name)
        src = Image.open(src_path).convert("RGB")
        # Crop 2px margin to strip any JPEG edge ringing, then fit cleanly to 3440x1616
        src = src.crop((2, 2, src.width - 2, src.height - 2))
        fitted = ImageOps.fit(src, TARGET_SIZE, Image.Resampling.LANCZOS, centering=centering).convert("RGBA")
        if filename == "things_bilge_mines.png":
            # Shift focal cluster slightly left (~140px) so the toxin sacs sit completely clear
            # of the right-hand option buttons, extending the uniform nocturnal canal edge on the far right,
            # and gently deepen the right-side atmospheric shadow so prose_mean_L sits cleanly around ~32.
            shift = 140
            arr = np.asarray(fitted, dtype=np.float32)
            shifted = np.empty_like(arr)
            shifted[:, :TARGET_SIZE[0] - shift, :] = arr[:, shift:, :]
            # Seamlessly stretch the rightmost dark canal strip across the off-screen ultrawide margin.
            edge_strip = arr[:, TARGET_SIZE[0] - 48:, :]
            edge_img = Image.fromarray(np.uint8(np.clip(edge_strip, 0, 255))).resize(
                (shift, TARGET_SIZE[1]), Image.Resampling.LANCZOS)
            shifted[:, TARGET_SIZE[0] - shift:, :] = np.asarray(edge_img, dtype=np.float32)
            xs = np.linspace(0.0, 1.0, TARGET_SIZE[0], dtype=np.float32)[None, :, None]
            t = np.clip((xs - 0.42) / 0.26, 0.0, 1.0)
            smooth = t * t * (3.0 - 2.0 * t)
            shade = 1.0 - 0.24 * smooth
            shifted[:, :, :3] = np.clip(shifted[:, :, :3] * shade, 0, 255)
            fitted = Image.fromarray(np.uint8(np.rint(shifted)), mode="RGBA")
        # Enforce Alpha = 255 everywhere
        r, g, b, _ = fitted.split()
        alpha = Image.new("L", TARGET_SIZE, 255)
        portrait = Image.merge("RGBA", (r, g, b, alpha))

        out_path = ROOT / "images" / "events" / filename
        portrait.save(out_path, format="PNG")

        mean_l, p95_l, std_l = prose_luma_stats(portrait)
        render_event(
            ROOT,
            event_id,
            portrait,
            variables,
            option_keys,
            prev_dir / event_id.lower(),
            page="INITIAL",
        )
        manifest.append({
            "event": filename,
            "event_id": event_id,
            "source": src_path.name,
            "size": list(portrait.size),
            "mode": portrait.mode,
            "prose_mean_L": round(mean_l, 1),
            "prose_p95_L": round(p95_l, 1),
            "prose_std_L": round(std_l, 1),
            "sha256": sha256_file(out_path),
        })

    (ARCHIVE / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({"well_sha256_ok": True, "manifest": manifest}, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
