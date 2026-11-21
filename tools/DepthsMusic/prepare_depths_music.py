#!/usr/bin/env python3
"""Prepare seamless multi-stage Depths Act BGM suites (Explore + Combat) from lossless masters."""
from __future__ import annotations

import hashlib
import shutil
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC_ARCHIVE_DIR = ROOT / "source_assets" / "music" / "depths"
OUT_DIR = ROOT / "music" / "depths"

MASTER_BGM1 = SRC_ARCHIVE_DIR / "depths_bgm1_master.wav"
MASTER_BGM2 = SRC_ARCHIVE_DIR / "depths_bgm2_master.wav"
SAMPLE_RATE = 48_000


def ensure_masters() -> None:
    SRC_ARCHIVE_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    downloads = Path.home() / "Downloads"
    for dl_name, dst in (
        ("BGM1.wav", MASTER_BGM1),
        ("BGM2.wav", MASTER_BGM2),
    ):
        cand = downloads / dl_name
        if cand.is_file() and not dst.is_file():
            shutil.copy2(cand, dst)
        if not dst.is_file():
            raise FileNotFoundError(f"Depths BGM master WAV not found: {dst}")


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


def xfade_correlated(seg_out: np.ndarray, seg_in: np.ndarray) -> np.ndarray:
    length = len(seg_out)
    t = np.linspace(0.0, 1.0, length, endpoint=False, dtype=np.float64)[:, None]
    alpha = 0.5 - 0.5 * np.cos(np.pi * t)
    norm = float(np.linalg.norm(seg_out) * np.linalg.norm(seg_in)) + 1e-12
    corr = max(0.0, min(1.0, float(np.sum(seg_out * seg_in) / norm)))
    denom = np.sqrt((1.0 - alpha) ** 2 + alpha ** 2 + 2.0 * corr * (1.0 - alpha) * alpha)
    return ((1.0 - alpha) / denom) * seg_out + (alpha / denom) * seg_in


def refine_phase_seam(
    pcm: np.ndarray,
    t0_est: float,
    t1_est: float,
    fade_s: float,
    search_s: float = 0.35,
) -> tuple[int, int, int]:
    mono = pcm.mean(axis=1)
    fade_n = int(round(fade_s * SAMPLE_RATE))
    s0_base = int(round(t0_est * SAMPLE_RATE))
    s1_base = int(round(t1_est * SAMPLE_RATE))
    rad = int(round(search_s * SAMPLE_RATE))

    ref = mono[s0_base : s0_base + fade_n]
    ref_z = ref - np.mean(ref)
    ref_norm = float(np.linalg.norm(ref_z)) + 1e-12

    best_corr = -2.0
    best_s1 = s1_base
    lo = max(s0_base + fade_n * 2, s1_base - rad)
    hi = min(len(mono) - fade_n - 1, s1_base + rad)
    for s1 in range(lo, hi, 96):
        cand = mono[s1 : s1 + fade_n]
        cand_z = cand - np.mean(cand)
        corr = float(np.dot(ref_z, cand_z) / (ref_norm * (float(np.linalg.norm(cand_z)) + 1e-12)))
        if corr > best_corr:
            best_corr = corr
            best_s1 = s1

    for s1 in range(max(lo, best_s1 - 200), min(hi, best_s1 + 200)):
        cand = mono[s1 : s1 + fade_n]
        cand_z = cand - np.mean(cand)
        corr = float(np.dot(ref_z, cand_z) / (ref_norm * (float(np.linalg.norm(cand_z)) + 1e-12)))
        if corr > best_corr:
            best_corr = corr
            best_s1 = s1

    best_amp = 1e9
    best_delta = 0
    for delta in range(-1200, 1200):
        if s0_base + delta < 0 or best_s1 + delta + fade_n >= len(mono):
            continue
        idx = best_s1 + delta
        amp = (
            abs(pcm[idx, 0])
            + abs(pcm[idx, 1])
            + 0.5 * (abs(pcm[idx - 1, 0]) + abs(pcm[idx - 1, 1]))
        )
        if amp < best_amp:
            best_amp = amp
            best_delta = delta

    return s0_base + best_delta, best_s1 + best_delta, fade_n


def master_loop(loop: np.ndarray, target_rms_db: float, peak_limit_db: float) -> np.ndarray:
    out = loop - np.mean(loop, axis=0, keepdims=True)
    cur_rms = float(np.sqrt(np.mean(out ** 2)))
    out *= (10.0 ** (target_rms_db / 20.0)) / max(cur_rms, 1e-12)
    ceiling = 10.0 ** (peak_limit_db / 20.0)
    peak = float(np.max(np.abs(out)))
    if peak > ceiling:
        thresh = ceiling * 0.85
        mag = np.abs(out)
        over = mag > thresh
        out[over] = np.sign(out[over]) * (
            thresh + (ceiling - thresh) * np.tanh((mag[over] - thresh) / (ceiling - thresh))
        )
    return out


def write_godot_wav(out_path: Path, pcm_float: np.ndarray, uid_prefix: str) -> None:
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


def main() -> int:
    ensure_masters()
    pcm1 = load_pcm(MASTER_BGM1)
    pcm2 = load_pcm(MASTER_BGM2)

    # 1. Suite A1 Combat (106.95s seamless symphonic march from BGM1)
    s0_c1, s1_c1, f_c1 = refine_phase_seam(pcm1, 43.85, 150.50, fade_s=2.2)
    loop_c1 = np.vstack([
        xfade_correlated(pcm1[s1_c1 : s1_c1 + f_c1], pcm1[s0_c1 : s0_c1 + f_c1]),
        pcm1[s0_c1 + f_c1 : s1_c1],
    ])
    loop_c1 = master_loop(loop_c1, target_rms_db=-23.0, peak_limit_db=-2.0)
    write_godot_wav(OUT_DIR / "depths_a1_combat.wav", loop_c1, "dpac")

    # 2. Suite A1 Explore (59.00s two-part Prelude + Deep Cavern Coda from BGM1)
    f_e1 = int(round(2.6 * SAMPLE_RATE))
    a0, a1 = int(round(3.0 * SAMPLE_RATE)), int(round(40.2 * SAMPLE_RATE))
    c0, c1 = int(round(152.2 * SAMPLE_RATE)), int(round(174.0 * SAMPLE_RATE))
    loop_e1 = np.vstack([
        xfade_correlated(pcm1[c1 : c1 + f_e1], pcm1[a0 : a0 + f_e1]),
        pcm1[a0 + f_e1 : a1],
        xfade_correlated(pcm1[a1 : a1 + f_e1], pcm1[c0 : c0 + f_e1]),
        pcm1[c0 + f_e1 : c1],
    ])
    loop_e1 = master_loop(loop_e1, target_rms_db=-29.5, peak_limit_db=-4.0)
    write_godot_wav(OUT_DIR / "depths_a1_explore.wav", loop_e1, "dpae")

    # 3. Suite A2 Combat (91.55s seamless symphonic march from BGM2)
    s0_c2, s1_c2, f_c2 = refine_phase_seam(pcm2, 46.20, 137.80, fade_s=2.2)
    loop_c2 = np.vstack([
        xfade_correlated(pcm2[s1_c2 : s1_c2 + f_c2], pcm2[s0_c2 : s0_c2 + f_c2]),
        pcm2[s0_c2 + f_c2 : s1_c2],
    ])
    loop_c2 = master_loop(loop_c2, target_rms_db=-23.0, peak_limit_db=-2.0)
    write_godot_wav(OUT_DIR / "depths_a2_combat.wav", loop_c2, "dpbc")

    # 4. Suite A2 Explore (28.58s brooding oboe & low strings prelude from BGM2)
    s0_e2, s1_e2, f_e2 = refine_phase_seam(pcm2, 5.25, 35.35, fade_s=2.6, search_s=2.2)
    loop_e2 = np.vstack([
        xfade_correlated(pcm2[s1_e2 : s1_e2 + f_e2], pcm2[s0_e2 : s0_e2 + f_e2]),
        pcm2[s0_e2 + f_e2 : s1_e2],
    ])
    loop_e2 = master_loop(loop_e2, target_rms_db=-29.5, peak_limit_db=-4.0)
    write_godot_wav(OUT_DIR / "depths_a2_explore.wav", loop_e2, "dpbe")

    print(
        f"DEPTHS_MUSIC_READY a1_combat={len(loop_c1)/SAMPLE_RATE:.2f}s "
        f"a1_explore={len(loop_e1)/SAMPLE_RATE:.2f}s "
        f"a2_combat={len(loop_c2)/SAMPLE_RATE:.2f}s "
        f"a2_explore={len(loop_e2)/SAMPLE_RATE:.2f}s"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
