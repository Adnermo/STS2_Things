"""Switch the active shipping Spine 4.2 package for The Legacy (腐化之遗) between variants.

Usage:
    python tools/TheLegacyRig/switch_variant.py v0_original [--import]
    python tools/TheLegacyRig/switch_variant.py v1_optimized [--import]
    python tools/TheLegacyRig/switch_variant.py v2_rebuilt [--import]
    python tools/TheLegacyRig/switch_variant.py status
"""
from __future__ import annotations
import argparse
import hashlib
import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT_ROOT = HERE.parents[1]
SHIP_DIR = PROJECT_ROOT / "STS2_Things" / "animations" / "monsters" / "the_legacy"
VARIANTS_DIR = HERE / "variants"
ALIASES = {
    "v0": "v0_original",
    "original": "v0_original",
    "v0_original": "v0_original",
    "v1": "v1_optimized",
    "optimized": "v1_optimized",
    "a": "v1_optimized",
    "v1_optimized": "v1_optimized",
    "v2": "v2_rebuilt",
    "rebuilt": "v2_rebuilt",
    "b": "v2_rebuilt",
    "v2_rebuilt": "v2_rebuilt",
}
FILES = (
    "the_legacy.png",
    "the_legacy.atlas",
    "the_legacy.spatlas",
    "the_legacy.spjson",
    "the_legacy_skel_data.tres",
)


def fingerprint(folder: Path) -> str:
    h = hashlib.sha256()
    for fn in FILES:
        p = folder / fn
        if not p.is_file():
            return "incomplete"
        h.update(fn.encode("utf-8"))
        h.update(p.read_bytes())
    return h.hexdigest()[:16]


def detect_godot() -> str:
    env = os.environ.get("GODOT_4_5_1_MONO")
    if env and Path(env).is_file():
        return env
    for cand in (
        r"D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe",
        r"D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe",
    ):
        if Path(cand).is_file():
            return cand
    raise FileNotFoundError("Godot 4.5.1 Mono console executable not found.")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("target", help="Variant name (v0_original | v1_optimized | v2_rebuilt | status)")
    ap.add_argument("--import", dest="do_import", action="store_true", help="Run Godot headless --import after copying")
    args = ap.parse_args()

    ship_fp = fingerprint(SHIP_DIR)
    if args.target.lower() == "status":
        print(f"Active shipping dir: {SHIP_DIR} (sha256={ship_fp})")
        for v in ("v0_original", "v1_optimized", "v2_rebuilt"):
            pkg = VARIANTS_DIR / v / "pkg"
            vfp = fingerprint(pkg)
            mark = "  <== ACTIVE" if vfp == ship_fp and vfp != "incomplete" else ""
            print(f"  - {v:14s}: {vfp}{mark}")
        return 0

    key = ALIASES.get(args.target.lower())
    if not key:
        print(f"Unknown variant: {args.target!r}. Choose from: {sorted(set(ALIASES.values()))}", file=sys.stderr)
        return 2
    pkg = VARIANTS_DIR / key / "pkg"
    for fn in FILES:
        if not (pkg / fn).is_file():
            print(f"Variant {key} is missing {pkg / fn}", file=sys.stderr)
            return 3

    SHIP_DIR.mkdir(parents=True, exist_ok=True)
    for fn in FILES:
        shutil.copy2(pkg / fn, SHIP_DIR / fn)
    print(f"Switched active The Legacy rig -> {key} (sha256={fingerprint(SHIP_DIR)})")

    if args.do_import:
        godot = detect_godot()
        log_dir = PROJECT_ROOT / "build" / "the_legacy_review" / "logs"
        log_dir.mkdir(parents=True, exist_ok=True)
        log_file = log_dir / f"import_{key}.log"
        cmd = [godot, "--headless", "--editor", "--path", str(PROJECT_ROOT), "--import", "--quit", "--log-file", str(log_file)]
        res = subprocess.run(cmd, check=False)
        if res.returncode != 0:
            print(f"Godot import exited with {res.returncode}; check {log_file}", file=sys.stderr)
            return res.returncode
        print(f"Godot resource import succeeded ({log_file})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
