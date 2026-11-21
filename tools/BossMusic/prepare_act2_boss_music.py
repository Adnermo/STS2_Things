#!/usr/bin/env python3
"""Prepare seamless Act 2 / Depths Boss BGM WAVs & Godot .import files.

Tracks:
- Cave God (洞神): source_assets/music/cave_god/cave_god_boss_master.wav
    -> music/cave_god/cave_god_boss_theme.wav
- Bowlbug Progenitor (盛碗虫族母): source_assets/music/bowlbug_progenitor/bowlbug_progenitor_boss_master.wav
    -> music/bowlbug_progenitor/bowlbug_progenitor_boss_theme.wav
- Abyssal Inkmaw (渊墨巨蛸): source_assets/music/abyssal_inkmaw/abyssal_inkmaw_boss_master.wav
    -> music/abyssal_inkmaw/abyssal_inkmaw_boss_theme.wav
"""
from __future__ import annotations

import hashlib
import shutil
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SAMPLE_RATE = 48_000
TARGET_RMS_DB = -20.0
PEAK_LIMIT_DB = -2.0

CAVE_GOD_MASTER = ROOT / "source_assets" / "music" / "cave_god" / "cave_god_boss_master.wav"
CAVE_GOD_OUT = ROOT / "music" / "cave_god" / "cave_god_boss_theme.wav"

BOWLBUG_MASTER = ROOT / "source_assets" / "music" / "bowlbug_progenitor" / "bowlbug_progenitor_boss_master.wav"
BOWLBUG_LEGACY_SRC = ROOT / "source_assets" / "music" / "human_face_tower" / "Subterranean_Siege_master.wav"
BOWLBUG_OUT = ROOT / "music" / "bowlbug_progenitor" / "bowlbug_progenitor_boss_theme.wav"

INKMAW_MASTER = ROOT / "source_assets" / "music" / "abyssal_inkmaw" / "abyssal_inkmaw_boss_master.wav"
INKMAW_OUT = ROOT / "music" / "abyssal_inkmaw" / "abyssal_inkmaw_boss_theme.wav"


def ensure_masters() -> None:
    CAVE_GOD_MASTER.parent.mkdir(parents=True, exist_ok=True)
    if not CAVE_GOD_MASTER.is_file():
        cand = Path.home() / "Downloads" / "09_act3_the_queen.wav"
        if cand.is_file():
            shutil.copy2(cand, CAVE_GOD_MASTER)
    if not CAVE_GOD_MASTER.is_file():
        raise FileNotFoundError(f"Cave God Boss BGM master WAV not found: {CAVE_GOD_MASTER}")

    BOWLBUG_MASTER.parent.mkdir(parents=True, exist_ok=True)
    if not BOWLBUG_MASTER.is_file():
        if BOWLBUG_LEGACY_SRC.is_file():
            shutil.copy2(BOWLBUG_LEGACY_SRC, BOWLBUG_MASTER)
    if not BOWLBUG_MASTER.is_file():
        raise FileNotFoundError(f"Bowlbug Progenitor Boss BGM master WAV not found: {BOWLBUG_MASTER}")

    INKMAW_MASTER.parent.mkdir(parents=True, exist_ok=True)
    if not INKMAW_MASTER.is_file():
        cand = Path.home() / "Downloads" / "05_act1b_waterfall_giant.wav"
        if cand.is_file():
            shutil.copy2(cand, INKMAW_MASTER)
    if not INKMAW_MASTER.is_file():
        raise FileNotFoundError(f"Abyssal Inkmaw Boss BGM master WAV not found: {INKMAW_MASTER}")


def load_pcm(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as w:
        channels = w.getnchannels()
        sampwidth = w.getsampwidth()
        rate = w.getframerate()
        nframes = w.getnframes()
        if channels != 2 or sampwidth != 2 or rate != SAMPLE_RATE:
            raise RuntimeError(
                f"Expected 48kHz stereo PCM16 WAV for {path}, got ch={channels}, sw={sampwidth}, sr={rate}"
            )
        raw = np.frombuffer(w.readframes(nframes), dtype="<i2").reshape(-1, 2)
    return raw.astype(np.float64) / 32768.0


def normalize_master(loop: np.ndarray) -> tuple[np.ndarray, float, float]:
    out = loop - np.mean(loop, axis=0, keepdims=True)
    cur_rms = float(np.sqrt(np.mean(out ** 2)))
    out *= (10.0 ** (TARGET_RMS_DB / 20.0)) / max(cur_rms, 1e-12)
    ceiling = 10.0 ** (PEAK_LIMIT_DB / 20.0)
    peak = float(np.max(np.abs(out)))
    if peak > ceiling:
        thresh = ceiling * 0.85
        mag = np.abs(out)
        over = mag > thresh
        out[over] = np.sign(out[over]) * (
            thresh + (ceiling - thresh) * np.tanh((mag[over] - thresh) / (ceiling - thresh))
        )
    rms_db = 20.0 * np.log10(float(np.sqrt(np.mean(out ** 2))) + 1e-12)
    pk_db = 20.0 * np.log10(float(np.max(np.abs(out))) + 1e-12)
    return out, rms_db, pk_db


def build_cave_god_loop(pcm: np.ndarray) -> tuple[np.ndarray, float, float]:
    # Trim 180ms leading digital pad and fold the 1.40s reverberant cathedral decay
    # [127.80s..129.20s] smoothly into the eerie opening organ drone [0.18s..1.58s].
    s0 = int(round(0.18 * SAMPLE_RATE))
    s1 = int(round(127.80 * SAMPLE_RATE))
    nf = min(int(round(1.40 * SAMPLE_RATE)), len(pcm) - s1)
    loop = pcm[s0:s1].copy()
    r0 = int(round(0.006 * SAMPLE_RATE))
    loop[:r0] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, r0, dtype=np.float64)))[:, None]
    if nf > 0:
        tail = pcm[s1 : s1 + nf].copy()
        t = np.linspace(0.0, 1.0, nf, endpoint=False, dtype=np.float64)[:, None]
        loop[:nf] += tail * (0.5 + 0.5 * np.cos(np.pi * t))
    return normalize_master(loop)


def build_bowlbug_loop(pcm: np.ndarray) -> tuple[np.ndarray, float, float]:
    # Preserve the full 133.20s Subterranean Siege arrangement with 6ms cosine micro-declick ramps.
    loop, rms_db, pk_db = normalize_master(pcm.copy())
    fade_n = 288
    ramp = (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, fade_n, dtype=np.float64)))[:, None]
    loop[:fade_n] *= ramp
    loop[-fade_n:] *= ramp[::-1]
    return loop, rms_db, pk_db


def build_inkmaw_loop(pcm: np.ndarray) -> tuple[np.ndarray, float, float]:
    # Trim 550ms leading digital silence and fold the 1.60s echoing abyssal rumble
    # [121.60s..123.20s] smoothly into the rising opening sub-aquatic brass groan [0.55s..2.15s].
    s0 = int(round(0.55 * SAMPLE_RATE))
    s1 = int(round(121.60 * SAMPLE_RATE))
    nf = min(int(round(1.60 * SAMPLE_RATE)), len(pcm) - s1)
    loop = pcm[s0:s1].copy()
    loop -= np.mean(loop, axis=0, keepdims=True)
    r0 = int(round(0.006 * SAMPLE_RATE))
    loop[:r0] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, r0, dtype=np.float64)))[:, None]
    if nf > 0:
        tail = pcm[s1 : s1 + nf].copy()
        tail -= np.mean(tail, axis=0, keepdims=True)
        t = np.linspace(0.0, 1.0, nf, endpoint=False, dtype=np.float64)[:, None]
        loop[:nf] += tail * (0.5 + 0.5 * np.cos(np.pi * t))
    return normalize_master(loop)


def write_godot_wav(out_path: Path, pcm_float: np.ndarray, uid_prefix: str) -> list[int]:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    pcm16 = np.round(np.clip(pcm_float, -1.0, 1.0) * 32767.0).astype("<i2")
    with wave.open(str(out_path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(pcm16.tobytes())

    rel_res = "res://" + out_path.relative_to(ROOT).as_posix()
    md5_hex = hashlib.md5(rel_res.encode("utf-8")).hexdigest()
    uid_seed = hashlib.sha1(rel_res.encode("utf-8")).hexdigest()[:10]
    sample_dest = f"res://.godot/imported/{out_path.name}-{md5_hex}.sample"
    import_text = f"""[remap]

importer="wav"
type="AudioStreamWAV"
uid="uid://{uid_prefix}{uid_seed}"
path="{sample_dest}"

[deps]

source_file="{rel_res}"
dest_files=["{sample_dest}"]

[params]

force/8_bit=false
force/mono=false
force/max_rate=false
force/max_rate_hz=44100
edit/trim=false
edit/normalize=false
edit/loop_mode=2
edit/loop_begin=0
edit/loop_end=-1
compress/mode=2
"""
    out_path.with_name(out_path.name + ".import").write_text(
        import_text, encoding="utf-8", newline="\n"
    )
    return np.abs(pcm16[0].astype(np.int32) - pcm16[-1].astype(np.int32)).tolist()


def main() -> int:
    ensure_masters()

    cg_loop, cg_rms, cg_pk = build_cave_god_loop(load_pcm(CAVE_GOD_MASTER))
    cg_jump = write_godot_wav(CAVE_GOD_OUT, cg_loop, "cgbm")

    bb_loop, bb_rms, bb_pk = build_bowlbug_loop(load_pcm(BOWLBUG_MASTER))
    bb_jump = write_godot_wav(BOWLBUG_OUT, bb_loop, "bbbm")

    im_loop, im_rms, im_pk = build_inkmaw_loop(load_pcm(INKMAW_MASTER))
    im_jump = write_godot_wav(INKMAW_OUT, im_loop, "aibm")

    print(
        f"ACT2_BOSS_MUSIC_READY "
        f"cave_god={len(cg_loop)/SAMPLE_RATE:.2f}s({len(cg_loop)}f,rms={cg_rms:.1f}dB,pk={cg_pk:.1f}dB,jump={cg_jump}) "
        f"bowlbug={len(bb_loop)/SAMPLE_RATE:.2f}s({len(bb_loop)}f,rms={bb_rms:.1f}dB,pk={bb_pk:.1f}dB,jump={bb_jump}) "
        f"inkmaw={len(im_loop)/SAMPLE_RATE:.2f}s({len(im_loop)}f,rms={im_rms:.1f}dB,pk={im_pk:.1f}dB,jump={im_jump})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
