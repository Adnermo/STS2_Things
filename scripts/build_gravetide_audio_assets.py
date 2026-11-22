#!/usr/bin/env python3
"""Author metric-matched Gravetide music and SFX from original synthesis."""

from __future__ import annotations

import math
import shutil
import wave
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
SAMPLE_RATE = 48_000
MUSIC_MASTER = ROOT / "source_assets/music/gravetide_slug/gravetide_slug_boss_master.wav"
MUSIC_START_S = 0.35
MUSIC_END_S = 144.60
MUSIC_TAIL_FOLD_S = 1.60


def rms(samples: np.ndarray) -> float:
    return float(np.sqrt(np.mean(np.square(samples, dtype=np.float64))))


def normalize(samples: np.ndarray, target_rms_dbfs: float, peak_dbfs: float) -> np.ndarray:
    data = np.nan_to_num(np.asarray(samples, dtype=np.float64))
    if data.ndim == 1:
        data = np.column_stack((data, data))
    data -= np.mean(data, axis=0, keepdims=True)
    current = rms(data)
    if current > 1e-12:
        data *= 10.0 ** (target_rms_dbfs / 20.0) / current
    ceiling = 10.0 ** (peak_dbfs / 20.0)
    peak = float(np.max(np.abs(data)))
    if peak > ceiling:
        data *= ceiling / peak
    return data


def write_wav(
    path: Path,
    samples: np.ndarray,
    target_rms_dbfs: float,
    peak_dbfs: float = -3.0,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    data = normalize(samples, target_rms_dbfs, peak_dbfs)
    pcm = np.round(np.clip(data, -1.0, 1.0) * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(pcm.tobytes())


def envelope(length: int, attack: float, release: float) -> np.ndarray:
    result = np.ones(length, dtype=np.float32)
    attack_samples = max(1, round(length * attack))
    release_samples = max(1, round(length * release))
    result[:attack_samples] = np.sin(
        np.linspace(0.0, math.pi / 2.0, attack_samples, dtype=np.float32)
    ) ** 2
    result[-release_samples:] *= np.sin(
        np.linspace(math.pi / 2.0, 0.0, release_samples, dtype=np.float32)
    ) ** 2
    return result


def swept_sine(duration: float, start_hz: float, end_hz: float) -> np.ndarray:
    length = round(duration * SAMPLE_RATE)
    frequencies = np.linspace(start_hz, end_hz, length, dtype=np.float64)
    phase = 2.0 * math.pi * np.cumsum(frequencies) / SAMPLE_RATE
    return np.sin(phase).astype(np.float32)


def band_noise(
    length: int,
    center_hz: float,
    octave_width: float,
    seed: int,
) -> np.ndarray:
    rng = np.random.default_rng(seed)
    source = rng.normal(0.0, 1.0, length).astype(np.float32)
    spectrum = np.fft.rfft(source)
    frequencies = np.fft.rfftfreq(length, 1.0 / SAMPLE_RATE)
    ratio = np.maximum(frequencies, 1.0) / center_hz
    weights = np.exp(-0.5 * (np.log2(ratio) / octave_width) ** 2)
    weights[0] = 0.0
    shaped = np.fft.irfft(spectrum * weights, n=length).astype(np.float32)
    level = rms(shaped)
    return shaped if level <= 1e-12 else shaped / level


def stereo_from_mono(
    mono: np.ndarray,
    side: np.ndarray,
    side_amount: float,
    delay_seconds: float,
) -> np.ndarray:
    delay = round(delay_seconds * SAMPLE_RATE)
    delayed_side = np.roll(side, delay)
    if delay > 0:
        delayed_side[:delay] = 0.0
    left = mono + side * side_amount
    right = mono + delayed_side * side_amount * 0.85
    return np.column_stack((left, right))


def wet_impact(
    duration: float,
    seed: int,
    body_hz: float,
    brightness: float,
) -> np.ndarray:
    length = round(duration * SAMPLE_RATE)
    t = np.arange(length, dtype=np.float32) / SAMPLE_RATE
    body = swept_sine(duration, body_hz * 1.45, body_hz * 0.58)
    body *= np.exp(-t * 3.1)
    slap = band_noise(length, 760.0 * brightness, 0.92, seed)
    slap *= np.exp(-t * 2.25) * (0.42 + 0.58 * envelope(length, 0.008, 0.72))
    grit = band_noise(length, 2_250.0, 0.65, seed + 10_000)
    grit *= np.exp(-t * 6.8) * 0.24
    formant = np.sin(
        2.0 * math.pi * (410.0 + 75.0 * np.sin(2.0 * math.pi * 4.2 * t)) * t
    ).astype(np.float32)
    formant *= np.exp(-t * 2.5) * 0.22
    mono = (body * 0.38 + slap * 0.24 + grit + formant) * envelope(
        length, 0.004, 0.35
    )
    side = band_noise(length, 1_150.0, 1.05, seed + 20_000)
    side *= np.exp(-t * 2.7)
    return stereo_from_mono(mono, side, 0.22, 0.0045)


def suction(duration: float, seed: int, release: bool = False) -> np.ndarray:
    length = round(duration * SAMPLE_RATE)
    t = np.arange(length, dtype=np.float32) / SAMPLE_RATE
    arch = np.sin(math.pi * np.clip(t / duration, 0.0, 1.0)) ** 2.5
    start_hz, end_hz = ((780.0, 340.0) if release else (280.0, 860.0))
    throat = swept_sine(duration, start_hz, end_hz)
    throat += 0.42 * swept_sine(duration, start_hz * 1.73, end_hz * 1.38)
    wet = band_noise(length, 690.0 if release else 820.0, 1.08, seed)
    hiss = band_noise(length, 2_600.0, 0.72, seed + 1_000)
    bubble = np.sin(
        2.0 * math.pi * (230.0 + 46.0 * np.sin(2.0 * math.pi * 5.4 * t)) * t
    ).astype(np.float32)
    mono = (
        throat * 0.22
        + wet * 0.24
        + hiss * 0.075
        + bubble * 0.14
    ) * arch
    if release:
        mono *= np.exp(-t * 0.7)
    side = band_noise(length, 1_000.0, 1.1, seed + 2_000) * arch
    return stereo_from_mono(mono, side, 0.18, 0.008)


def death_sound(duration: float, seed: int) -> np.ndarray:
    length = round(duration * SAMPLE_RATE)
    t = np.arange(length, dtype=np.float32) / SAMPLE_RATE
    collapse = wet_impact(duration, seed, 118.0, 0.95)
    groan = swept_sine(duration, 380.0, 105.0) * np.exp(-t * 0.8)
    crackle = band_noise(length, 1_450.0, 1.05, seed + 3_000)
    crackle *= np.exp(-t * 1.25) * 0.16
    mono_tail = (groan * 0.25 + crackle) * envelope(length, 0.01, 0.5)
    tail = stereo_from_mono(
        mono_tail,
        band_noise(length, 1_850.0, 0.9, seed + 4_000),
        0.12,
        0.012,
    )
    return collapse * 0.72 + tail


def add_wrapped(
    mix: np.ndarray,
    start_sample: int,
    signal: np.ndarray,
    pan: float,
    gain: float,
) -> None:
    start = start_sample % len(mix)
    mono = signal if signal.ndim == 1 else signal.mean(axis=1)
    left_gain = math.sqrt((1.0 - pan) * 0.5) * gain
    right_gain = math.sqrt((1.0 + pan) * 0.5) * gain
    first = min(len(mono), len(mix) - start)
    mix[start : start + first, 0] += mono[:first] * left_gain
    mix[start : start + first, 1] += mono[:first] * right_gain
    remaining = len(mono) - first
    if remaining > 0:
        mix[:remaining, 0] += mono[first:] * left_gain
        mix[:remaining, 1] += mono[first:] * right_gain


def pad_note(frequency: float, duration: float, phase: float) -> np.ndarray:
    length = round(duration * SAMPLE_RATE)
    t = np.arange(length, dtype=np.float64) / SAMPLE_RATE
    tone = np.zeros(length, dtype=np.float64)
    for partial, level in ((1.0, 1.0), (2.0, 0.31), (3.0, 0.14), (4.0, 0.06)):
        tone += np.sin(2.0 * math.pi * frequency * partial * t + phase * partial) * level
    return (tone * envelope(length, 0.14, 0.22)).astype(np.float32)


def bone_bell(frequency: float, duration: float, phase: float) -> np.ndarray:
    length = round(duration * SAMPLE_RATE)
    t = np.arange(length, dtype=np.float64) / SAMPLE_RATE
    attack = 1.0 - np.exp(-t * 90.0)
    decay = np.exp(-t * 1.15)
    tone = (
        np.sin(2.0 * math.pi * frequency * t + phase)
        + 0.46 * np.sin(2.0 * math.pi * frequency * 2.71 * t + phase * 0.7)
        + 0.18 * np.sin(2.0 * math.pi * frequency * 4.13 * t + phase * 1.3)
    )
    return (tone * attack * decay).astype(np.float32)


def build_mastered_boss_music() -> np.ndarray:
    MUSIC_MASTER.parent.mkdir(parents=True, exist_ok=True)
    if not MUSIC_MASTER.is_file():
        candidate = Path.home() / "Downloads" / "Dockyard Rot.wav"
        if candidate.is_file():
            shutil.copy2(candidate, MUSIC_MASTER)
    if not MUSIC_MASTER.is_file():
        raise FileNotFoundError(f"Gravetide Boss BGM master WAV not found: {MUSIC_MASTER}")

    with wave.open(str(MUSIC_MASTER), "rb") as source:
        channels = source.getnchannels()
        sampwidth = source.getsampwidth()
        rate = source.getframerate()
        nframes = source.getnframes()
        if channels != 2 or sampwidth != 2 or rate != SAMPLE_RATE:
            raise RuntimeError(
                f"Expected 48kHz stereo PCM16 WAV for {MUSIC_MASTER}, got ch={channels}, sw={sampwidth}, sr={rate}"
            )
        pcm = np.frombuffer(source.readframes(nframes), dtype="<i2").reshape(-1, 2).astype(np.float64) / 32768.0

    s0 = int(round(MUSIC_START_S * SAMPLE_RATE))
    s1 = int(round(MUSIC_END_S * SAMPLE_RATE))
    nf = min(int(round(MUSIC_TAIL_FOLD_S * SAMPLE_RATE)), len(pcm) - s1)
    if s0 < 0 or s1 > len(pcm) or s1 <= s0 + SAMPLE_RATE:
        raise ValueError(f"Invalid trim window [{s0}, {s1}] for {MUSIC_MASTER} (len={len(pcm)})")

    # Trim 350ms leading digital pad and fold the 1.60s echoing dark dockyard tail smoothly
    # into the opening subterranean low-brass swell for a continuous zero-gap loop.
    loop = pcm[s0:s1].copy()
    loop -= np.mean(loop, axis=0, keepdims=True)
    r0 = int(round(0.006 * SAMPLE_RATE))
    loop[:r0] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, math.pi, r0, dtype=np.float64)))[:, np.newaxis]
    if nf > 0:
        tail = pcm[s1 : s1 + nf].copy()
        tail -= np.mean(tail, axis=0, keepdims=True)
        t = np.linspace(0.0, 1.0, nf, endpoint=False, dtype=np.float64)[:, np.newaxis]
        loop[:nf] += tail * (0.5 + 0.5 * np.cos(math.pi * t))
    return loop


def main() -> int:
    sfx = ROOT / "sfx/gravetide_slug"
    music = ROOT / "music/gravetide_slug"

    for index in range(1, 4):
        write_wav(
            sfx / f"gravetide_slug_attack_light-{index:02d}.wav",
            wet_impact(2.10, 100 + index, 150.0 + index * 4.0, 1.0),
            target_rms_dbfs=-31.0,
            peak_dbfs=-9.0,
        )
    for index in range(1, 3):
        write_wav(
            sfx / f"gravetide_slug_attack-{index:02d}.wav",
            wet_impact(1.82, 200 + index, 132.0 + index * 5.0, 1.18),
            target_rms_dbfs=-29.0,
            peak_dbfs=-7.0,
        )
    for index in range(1, 5):
        write_wav(
            sfx / f"gravetide_slug_hurt-{index:02d}.wav",
            wet_impact(0.78, 300 + index, 175.0 + index * 3.0, 1.24),
            target_rms_dbfs=-31.0,
            peak_dbfs=-9.0,
        )
    for index in range(1, 3):
        write_wav(
            sfx / f"gravetide_slug_die-{index:02d}.wav",
            death_sound(2.28, 400 + index),
            target_rms_dbfs=-30.0,
            peak_dbfs=-8.0,
        )
        write_wav(
            sfx / f"gravetide_slug_devour-{index:02d}.wav",
            suction(1.23, 500 + index),
            target_rms_dbfs=-34.0,
            peak_dbfs=-11.0,
        )
        write_wav(
            sfx / f"gravetide_slug_devour_end-{index:02d}.wav",
            suction(2.05, 600 + index, release=True),
            target_rms_dbfs=-38.0,
            peak_dbfs=-14.0,
        )

    write_wav(
        music / "gravetide_slug_boss_theme.wav",
        build_mastered_boss_music(),
        target_rms_dbfs=-20.0,
        peak_dbfs=-2.0,
    )
    print("GRAVETIDE_AUDIO_ASSET_BUILD_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
