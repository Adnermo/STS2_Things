#!/usr/bin/env python3
"""Prepare the dedicated loop-seamless Boss BGM WAV & Godot .import for Human Face Tower (人面塔)."""
from __future__ import annotations

import hashlib
import shutil
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC_ARCHIVE_DIR = ROOT / "source_assets" / "music" / "human_face_tower"
OUT_DIR = ROOT / "music" / "human_face_tower"
MASTER_COPY = SRC_ARCHIVE_DIR / "human_face_tower_boss_master.wav"
OUT_WAV = OUT_DIR / "human_face_tower_boss_theme.wav"

SAMPLE_RATE = 48_000
TARGET_RMS_DB = -20.0
PEAK_LIMIT_DB = -2.0

# Trim 480ms leading silence before the opening cathedral swell (0.48s -> 0.80s downbeat),
# and fold the 1.60s reverberant tail [123.10s..124.70s] smoothly across the opening so
# long fights loop with continuous dark stone hall decay and zero silence gap.
START_S = 0.48
END_S = 123.10
TAIL_FOLD_S = 1.60


def build_tail_fold_loop(pcm: np.ndarray) -> tuple[np.ndarray, float, float]:
    s0 = int(round(START_S * SAMPLE_RATE))
    s1 = int(round(END_S * SAMPLE_RATE))
    nf = min(int(round(TAIL_FOLD_S * SAMPLE_RATE)), len(pcm) - s1)
    if s0 < 0 or s1 > len(pcm) or s1 <= s0 + SAMPLE_RATE:
        raise ValueError(f"Invalid trim window [{s0}, {s1}] for Human Face Tower (len={len(pcm)})")

    loop = pcm[s0:s1].copy()
    loop -= np.mean(loop, axis=0, keepdims=True)

    # 6ms cosine fade-in on loop body before folding the continuous tail so frame 0 transitions
    # seamlessly from loop[-1] (pcm[s1 - 1]) into tail[0] (pcm[s1]) with negligible step.
    r0 = int(round(0.006 * SAMPLE_RATE))
    loop[:r0] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, r0, dtype=np.float64)))[:, None]
    if nf > 0:
        tail = pcm[s1 : s1 + nf].copy()
        tail -= np.mean(tail, axis=0, keepdims=True)
        t = np.linspace(0.0, 1.0, nf, endpoint=False, dtype=np.float64)[:, None]
        fade_out = 0.5 + 0.5 * np.cos(np.pi * t)
        loop[:nf] += tail * fade_out

    cur_rms = float(np.sqrt(np.mean(loop ** 2)))
    loop *= (10.0 ** (TARGET_RMS_DB / 20.0)) / max(cur_rms, 1e-12)

    ceiling = 10.0 ** (PEAK_LIMIT_DB / 20.0)
    peak = float(np.max(np.abs(loop)))
    if peak > ceiling:
        thresh = ceiling * 0.85
        mag = np.abs(loop)
        over = mag > thresh
        loop[over] = np.sign(loop[over]) * (
            thresh + (ceiling - thresh) * np.tanh((mag[over] - thresh) / (ceiling - thresh))
        )

    final_rms = 20.0 * np.log10(float(np.sqrt(np.mean(loop ** 2))) + 1e-12)
    final_pk = 20.0 * np.log10(float(np.max(np.abs(loop))) + 1e-12)
    return loop, final_rms, final_pk


def main() -> int:
    SRC_ARCHIVE_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    downloads_wav = Path.home() / "Downloads" / "09_act3_the_queen(2).wav"
    if downloads_wav.is_file() and not MASTER_COPY.is_file():
        shutil.copy2(downloads_wav, MASTER_COPY)
    if not MASTER_COPY.is_file():
        raise FileNotFoundError(f"Master WAV not found: {MASTER_COPY}")

    with wave.open(str(MASTER_COPY), "rb") as w:
        channels = w.getnchannels()
        sampwidth = w.getsampwidth()
        rate = w.getframerate()
        nframes = w.getnframes()
        if sampwidth != 2 or channels != 2 or rate != SAMPLE_RATE:
            raise RuntimeError(
                f"Expected 48kHz stereo PCM16 WAV, got ch={channels}, sw={sampwidth}, rate={rate}"
            )
        pcm = np.frombuffer(w.readframes(nframes), dtype="<i2").reshape(-1, channels).astype(np.float64) / 32768.0

    loop, rms_db, pk_db = build_tail_fold_loop(pcm)
    pcm16 = np.round(np.clip(loop, -1.0, 1.0) * 32767.0).astype("<i2")

    with wave.open(str(OUT_WAV), "wb") as out_w:
        out_w.setnchannels(channels)
        out_w.setsampwidth(sampwidth)
        out_w.setframerate(rate)
        out_w.writeframes(pcm16.tobytes())

    rel_res = "res://music/human_face_tower/human_face_tower_boss_theme.wav"
    md5_hex = hashlib.md5(rel_res.encode("utf-8")).hexdigest()
    uid_seed = hashlib.sha1(rel_res.encode("utf-8")).hexdigest()[:12]
    sample_dest = f"res://.godot/imported/{OUT_WAV.name}-{md5_hex}.sample"
    import_text = f"""[remap]

importer="wav"
type="AudioStreamWAV"
uid="uid://hftm{uid_seed}"
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
    OUT_WAV.with_name(OUT_WAV.name + ".import").write_text(import_text, encoding="utf-8", newline="\n")
    wrap_jump = np.abs(pcm16[0].astype(np.int32) - pcm16[-1].astype(np.int32)).tolist()
    print(
        f"HUMAN_FACE_TOWER_MUSIC_READY frames={len(pcm16)} duration={len(pcm16) / rate:.2f}s "
        f"rms={rms_db:.1f}dB pk={pk_db:.1f}dB wrap_jump={wrap_jump}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
