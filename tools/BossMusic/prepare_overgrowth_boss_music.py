#!/usr/bin/env python3
"""Prepare seamless beat-locked Act 1 Overgrowth Boss BGM WAVs & Godot .import files.

Tracks:
- Origin Fogmog (始源雾菇): source_assets/music/origin_fogmog/origin_fogmog_boss_master.wav
    -> music/origin_fogmog/origin_fogmog_boss_theme.wav
- Scale Beetle (放缩巨甲虫): source_assets/music/scale_beetle/scale_beetle_boss_master.wav
    -> music/scale_beetle/scale_beetle_boss_theme.wav
"""
from __future__ import annotations

import hashlib
import shutil
import wave
from dataclasses import dataclass
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SAMPLE_RATE = 48_000
TARGET_RMS_DB = -20.0
PEAK_LIMIT_DB = -2.0


@dataclass(frozen=True)
class BossTrackSpec:
    slug: str
    download_name: str
    master_rel: str
    out_rel: str
    uid_prefix: str
    start_s: float
    end_s: float
    fade_in_ms: float
    fade_out_ms: float


TRACKS: tuple[BossTrackSpec, ...] = (
    BossTrackSpec(
        slug="origin_fogmog",
        download_name="02_act1_the_kin.wav",
        master_rel="source_assets/music/origin_fogmog/origin_fogmog_boss_master.wav",
        out_rel="music/origin_fogmog/origin_fogmog_boss_theme.wav",
        uid_prefix="ofbm",
        # Trim 135ms leading encoder silence and cut right at 112.370s where the eerie
        # celesta chimes complete their cadence so loop wrap resolves cleanly onto the
        # opening timpani downbeat with zero silence gap or double-hit.
        start_s=0.135,
        end_s=112.370,
        fade_in_ms=4.0,
        fade_out_ms=12.0,
    ),
    BossTrackSpec(
        slug="scale_beetle",
        download_name="02_act1_the_kin(1).wav",
        master_rel="source_assets/music/scale_beetle/scale_beetle_boss_master.wav",
        out_rel="music/scale_beetle/scale_beetle_boss_theme.wav",
        uid_prefix="sbbm",
        # Trim 115ms leading encoder silence and cut right at 94.505s where the ascending
        # chromatic string run slams straight into the opening downbeat with exact beat lock.
        start_s=0.115,
        end_s=94.505,
        fade_in_ms=4.0,
        fade_out_ms=10.0,
    ),
)


def ensure_master(spec: BossTrackSpec) -> Path:
    master_path = ROOT / spec.master_rel
    master_path.parent.mkdir(parents=True, exist_ok=True)
    if not master_path.is_file():
        candidate = Path.home() / "Downloads" / spec.download_name
        if candidate.is_file():
            shutil.copy2(candidate, master_path)
    if not master_path.is_file():
        raise FileNotFoundError(f"Boss BGM master WAV not found: {master_path}")
    return master_path


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


def build_seamless_boss_loop(pcm: np.ndarray, spec: BossTrackSpec) -> tuple[np.ndarray, float, float]:
    s0 = int(round(spec.start_s * SAMPLE_RATE))
    s1 = int(round(spec.end_s * SAMPLE_RATE))
    if s0 < 0 or s1 > len(pcm) or s1 <= s0 + SAMPLE_RATE:
        raise ValueError(f"Invalid trim window [{s0}, {s1}] for {spec.slug} (len={len(pcm)})")

    loop = pcm[s0:s1].copy()
    # Remove any sub-audible DC bias before mastering and micro-ramping.
    loop -= np.mean(loop, axis=0, keepdims=True)

    cur_rms = float(np.sqrt(np.mean(loop ** 2)))
    gain = (10.0 ** (TARGET_RMS_DB / 20.0)) / max(cur_rms, 1e-12)
    loop *= gain

    ceiling = 10.0 ** (PEAK_LIMIT_DB / 20.0)
    peak = float(np.max(np.abs(loop)))
    if peak > ceiling:
        thresh = ceiling * 0.85
        mag = np.abs(loop)
        over = mag > thresh
        loop[over] = np.sign(loop[over]) * (
            thresh + (ceiling - thresh) * np.tanh((mag[over] - thresh) / (ceiling - thresh))
        )

    # Micro-cosine declick ramps at the exact beat-locked loop boundary so wrap jump is [0, 0].
    n_in = max(1, int(round(spec.fade_in_ms * 0.001 * SAMPLE_RATE)))
    n_out = max(1, int(round(spec.fade_out_ms * 0.001 * SAMPLE_RATE)))
    ramp_in = (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, n_in, dtype=np.float64)))[:, None]
    ramp_out = (0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, n_out, dtype=np.float64)))[:, None]
    loop[:n_in] *= ramp_in
    loop[-n_out:] *= ramp_out

    final_rms_db = 20.0 * np.log10(float(np.sqrt(np.mean(loop ** 2))) + 1e-12)
    final_peak_db = 20.0 * np.log10(float(np.max(np.abs(loop))) + 1e-12)
    return loop, final_rms_db, final_peak_db


def write_godot_wav(out_path: Path, pcm_float: np.ndarray, uid_prefix: str) -> None:
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


def main() -> int:
    summaries: list[str] = []
    for spec in TRACKS:
        master_path = ensure_master(spec)
        pcm = load_pcm(master_path)
        loop, rms_db, peak_db = build_seamless_boss_loop(pcm, spec)
        out_path = ROOT / spec.out_rel
        write_godot_wav(out_path, loop, spec.uid_prefix)
        summaries.append(
            f"{spec.slug}={len(loop) / SAMPLE_RATE:.2f}s({len(loop)}f,rms={rms_db:.1f}dB,pk={peak_db:.1f}dB)"
        )
    print("OVERGROWTH_BOSS_MUSIC_READY " + " ".join(summaries))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
