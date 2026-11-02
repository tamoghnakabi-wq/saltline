"""
Original sound effects for SALTLINE, synthesised from seeded noise, oscillators and filters.

Nothing is sampled or recorded, so every file is original and can be regenerated:

    python3 ArtSource/Audio/gen_sfx.py [--out DIR]

Writes 48 kHz 16-bit mono WAV files (default: Assets/_Saltline/Audio/Generated) and a
manifest.json listing every sound with its group, variants and length.
"""
import argparse
import json
import os
import wave

import numpy as np
from scipy import signal

SR = 48000
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "_Saltline", "Audio", "Generated"))


# ---------------------------------------------------------------- building blocks

def t_axis(dur):
    return np.arange(int(dur * SR)) / SR


def noise(dur, rng):
    return rng.standard_normal(int(dur * SR))


def env_exp(dur, tau, attack=0.0005):
    t = t_axis(dur)
    a = np.clip(t / max(attack, 1e-6), 0, 1)
    return a * np.exp(-t / tau)


def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, min(hi, SR / 2 - 100)], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp(x, f, order=2):
    sos = signal.butter(order, min(f, SR / 2 - 100), btype="low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def hp(x, f, order=2):
    sos = signal.butter(order, f, btype="high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def pad(x, n):
    return np.pad(x, (0, max(0, n - len(x))))[:n] if len(x) < n else x[:n]


def place(dst, src, at):
    i = int(at * SR)
    j = min(len(dst), i + len(src))
    if j > i:
        dst[i:j] += src[: j - i]
    return dst


def sweep(dur, f0, f1, tau, rng=None, phase=0.0):
    t = t_axis(dur)
    f = f1 + (f0 - f1) * np.exp(-t / max(tau, 1e-4))
    ph = 2 * np.pi * np.cumsum(f) / SR + phase
    return np.sin(ph)


def modes(dur, freqs, decays, amps, rng, jitter=0.01):
    """Sum of damped sinusoids: metal pings, clicks, resonant bodies."""
    t = t_axis(dur)
    out = np.zeros_like(t)
    for f, d, a in zip(freqs, decays, amps):
        f *= 1 + rng.uniform(-jitter, jitter)
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / d)
    return out


def reverb_ir(dur, rt, rng, bright=6000, predelay=0.012):
    """Exponentially decaying filtered noise with a few early reflections."""
    n = int(dur * SR)
    ir = rng.standard_normal(n) * np.exp(-t_axis(dur) / (rt / 6.9))
    ir = lp(ir, bright)
    ir[: int(predelay * SR)] = 0
    for k in range(6):
        d = predelay + rng.uniform(0.004, 0.05)
        i = int(d * SR)
        if i < n:
            ir[i] += rng.uniform(0.2, 0.5) * (1 if k % 2 else -1)
    return ir / np.sqrt(np.sum(ir ** 2) + 1e-12)


def convolve(x, ir):
    return signal.fftconvolve(x, ir)[: len(x) + len(ir) - 1]


def finish(x, peak=0.89, drive=1.0, fade=0.004):
    x = np.asarray(x, dtype=np.float64)
    if drive != 1.0:
        x = np.tanh(x * drive) / np.tanh(drive)
    m = np.max(np.abs(x)) + 1e-12
    x = x / m * peak
    f = int(fade * SR)
    if f > 0 and len(x) > 2 * f:
        x[-f:] *= np.linspace(1, 0, f)
    # trim trailing near-silence
    a = np.abs(x)
    last = np.nonzero(a > 10 ** (-60 / 20))[0]
    if len(last):
        x = x[: min(len(x), last[-1] + int(0.02 * SR))]
    return x


# ---------------------------------------------------------------- sound recipes

def gunshot(rng, crack_band, crack_tau, body_f, body_tau, boom, tail_rt, tail_mix, mech_f, drive, far=False):
    dur = 0.35 + tail_rt
    x = np.zeros(int(dur * SR))
    # muzzle transient
    click = hp(noise(0.003, rng), 2000) * env_exp(0.003, 0.0007)
    place(x, click * 0.9, 0.0)
    # supersonic / muzzle crack
    c = bp(noise(0.08, rng), *crack_band, order=2) * env_exp(0.08, crack_tau, 0.0003)
    place(x, c * 1.4, 0.0005)
    # pressure body: falling sine + low noise
    b = sweep(0.4, body_f * 2.2, body_f, 0.012) * env_exp(0.4, body_tau, 0.001)
    bn = lp(noise(0.4, rng), body_f * 6) * env_exp(0.4, body_tau * 0.7, 0.001)
    place(x, (b * 1.0 + bn * 0.8) * boom, 0.0008)
    # action: small metal clack a little later
    m = modes(0.08, [mech_f, mech_f * 2.37, mech_f * 3.91], [0.012, 0.008, 0.005], [0.5, 0.3, 0.2], rng)
    place(x, m * 0.18, 0.045 + rng.uniform(0, 0.01))
    dry = x.copy()
    ir = reverb_ir(tail_rt, tail_rt, rng, bright=3500 if not far else 1800, predelay=0.02 if not far else 0.06)
    wet = convolve(dry, ir)[: len(x)]
    if far:
        dry = lp(dry, 1400)
        out = dry * 0.35 + wet * tail_mix * 2.2
    else:
        out = dry + wet * tail_mix
    return finish(out, drive=drive)


WEAPONS = {
    # name: crack band, crack tau, body Hz, body tau, boom, tail rt, tail mix, mech Hz, drive
    "ar": dict(crack_band=(900, 9000), crack_tau=0.012, body_f=62, body_tau=0.06, boom=1.0, tail_rt=1.1, tail_mix=0.32, mech_f=2300, drive=2.2),
    "smg": dict(crack_band=(1300, 11000), crack_tau=0.008, body_f=80, body_tau=0.04, boom=0.7, tail_rt=0.8, tail_mix=0.26, mech_f=2800, drive=2.0),
    "shotgun": dict(crack_band=(500, 6000), crack_tau=0.02, body_f=48, body_tau=0.11, boom=1.5, tail_rt=1.6, tail_mix=0.36, mech_f=1700, drive=2.6),
    "sniper": dict(crack_band=(1100, 12000), crack_tau=0.016, body_f=44, body_tau=0.14, boom=1.6, tail_rt=2.2, tail_mix=0.42, mech_f=1500, drive=2.8),
    "pistol": dict(crack_band=(1000, 8000), crack_tau=0.01, body_f=90, body_tau=0.045, boom=0.75, tail_rt=0.9, tail_mix=0.28, mech_f=3100, drive=1.8),
}


def click(rng, f, dur=0.06, level=1.0, n_modes=4, noise_amt=0.6):
    freqs = [f * k for k in (1, 1.71, 2.53, 3.46)[:n_modes]]
    m = modes(dur, freqs, [0.01, 0.007, 0.005, 0.003], [1, 0.6, 0.4, 0.25], rng, jitter=0.03)
    n = hp(noise(dur, rng), 1500) * env_exp(dur, 0.003)
    return (m + n * noise_amt) * level


def mech_sequence(rng, events, dur):
    """events: list of (time, freq, level, noise) clicks; adds a light cloth/handling bed."""
    x = np.zeros(int(dur * SR))
    for (at, f, lv, nz) in events:
        place(x, click(rng, f, level=lv, noise_amt=nz), at)
    bed = bp(noise(dur, rng), 300, 2500) * 0.03 * np.sin(np.pi * t_axis(dur) / dur)
    return finish(x + bed, peak=0.7)


def footstep(rng, kind):
    dur = 0.25
    x = np.zeros(int(dur * SR))
    if kind == "concrete":
        heel = lp(noise(0.05, rng), 1800) * env_exp(0.05, 0.008)
        grit = bp(noise(0.12, rng), 2500, 9000) * env_exp(0.12, 0.02) * 0.25
        place(x, heel, 0); place(x, grit, 0.01)
        place(x, lp(noise(0.04, rng), 1500) * env_exp(0.04, 0.006) * 0.6, 0.045 + rng.uniform(0, 0.015))
    elif kind == "metal":
        heel = lp(noise(0.04, rng), 2500) * env_exp(0.04, 0.006)
        ring = modes(0.25, [310, 742, 1180, 1960, 2890], [0.05, 0.035, 0.025, 0.018, 0.012], [0.5, 0.35, 0.25, 0.2, 0.12], rng, jitter=0.08)
        place(x, heel, 0); place(x, ring * 0.35, 0.002)
        place(x, ring[: int(0.1 * SR)] * 0.2, 0.05)
    elif kind == "gravel":
        for k in range(14):
            g = bp(noise(0.02, rng), 1500, 7000) * env_exp(0.02, 0.003)
            place(x, g * rng.uniform(0.2, 0.6), rng.uniform(0, 0.11))
        place(x, lp(noise(0.06, rng), 900) * env_exp(0.06, 0.012) * 0.7, 0)
    elif kind == "wood":
        knock = modes(0.15, [180, 410, 690], [0.03, 0.02, 0.012], [1, 0.5, 0.3], rng, jitter=0.1)
        place(x, knock + lp(noise(0.15, rng), 1200) * env_exp(0.15, 0.01) * 0.6, 0)
    elif kind == "water":
        for k in range(8):
            d = sweep(0.06, rng.uniform(600, 1400), rng.uniform(1500, 2600), 0.02) * env_exp(0.06, 0.012)
            place(x, d * rng.uniform(0.15, 0.4), rng.uniform(0, 0.08))
        place(x, bp(noise(0.2, rng), 400, 4000) * env_exp(0.2, 0.04) * 0.5, 0)
    return finish(x, peak=0.6)


def impact(rng, kind):
    dur = 0.6
    x = np.zeros(int(dur * SR))
    base = hp(noise(0.01, rng), 1000) * env_exp(0.01, 0.0015)
    place(x, base, 0)
    if kind == "concrete":
        place(x, bp(noise(0.15, rng), 600, 7000) * env_exp(0.15, 0.02), 0)
        for k in range(10):  # debris
            place(x, bp(noise(0.015, rng), 2000, 8000) * env_exp(0.015, 0.002) * rng.uniform(0.05, 0.25), rng.uniform(0.03, 0.35))
    elif kind == "metal":
        ring = modes(0.6, [rng.uniform(900, 1300) * k for k in (1, 2.76, 5.4, 8.93)], [0.25, 0.12, 0.07, 0.04], [0.6, 0.5, 0.3, 0.2], rng, jitter=0.02)
        place(x, ring * 0.7, 0)
        place(x, bp(noise(0.05, rng), 2000, 10000) * env_exp(0.05, 0.006), 0)
    elif kind == "thinmetal":
        rattle = modes(0.5, [rng.uniform(250, 400) * k for k in (1, 1.6, 2.3, 3.1, 4.4)], [0.15, 0.1, 0.08, 0.06, 0.04], [0.5, 0.4, 0.4, 0.3, 0.2], rng, jitter=0.05)
        place(x, rattle * (1 + 0.5 * np.sin(2 * np.pi * 23 * t_axis(0.5))) * 0.6, 0)
        place(x, bp(noise(0.08, rng), 1200, 8000) * env_exp(0.08, 0.01), 0)
    elif kind == "wood":
        place(x, modes(0.2, [220, 530, 940], [0.04, 0.02, 0.01], [1, 0.6, 0.3], rng, jitter=0.1) * 0.8, 0)
        place(x, bp(noise(0.12, rng), 800, 5000) * env_exp(0.12, 0.015), 0)
    elif kind == "dirt":
        place(x, lp(noise(0.2, rng), 2500) * env_exp(0.2, 0.025), 0)
        for k in range(8):
            place(x, bp(noise(0.02, rng), 1000, 5000) * env_exp(0.02, 0.004) * rng.uniform(0.05, 0.2), rng.uniform(0.04, 0.3))
    elif kind == "water":
        place(x, bp(noise(0.35, rng), 300, 5000) * env_exp(0.35, 0.06), 0)
        for k in range(10):
            place(x, sweep(0.05, rng.uniform(700, 1500), rng.uniform(1800, 3000), 0.015) * env_exp(0.05, 0.01) * rng.uniform(0.1, 0.3), rng.uniform(0.02, 0.3))
    elif kind == "glass":
        place(x, modes(0.5, [rng.uniform(2500, 4200) * k for k in (1, 1.43, 2.11, 2.97)], [0.1, 0.08, 0.05, 0.03], [0.5, 0.4, 0.3, 0.2], rng, jitter=0.05), 0)
        for k in range(16):
            place(x, hp(noise(0.01, rng), 4000) * env_exp(0.01, 0.002) * rng.uniform(0.05, 0.3), rng.uniform(0.01, 0.4))
    elif kind == "flesh":
        place(x, lp(noise(0.12, rng), 900) * env_exp(0.12, 0.018) * 1.2, 0)
        place(x, sweep(0.12, 160, 70, 0.02) * env_exp(0.12, 0.03) * 0.8, 0)
    return finish(x, peak=0.75)


def death(rng):
    """Body hit, then gear and body settling on the ground."""
    x = np.zeros(int(1.0 * SR))
    place(x, impact(rng, "flesh"), 0)
    place(x, lp(noise(0.4, rng), 700) * env_exp(0.4, 0.05) * 0.8, 0.32)
    place(x, mech_sequence(rng, [(0.0, 1200, 0.5, 0.8), (0.06, 1900, 0.3, 0.6)], 0.2) * 0.5, 0.36)
    return finish(x, peak=0.7)


def whizz(rng):
    dur = 0.35
    t = t_axis(dur)
    pos = (t - dur * 0.45) / 0.06
    amp = 1 / (1 + pos ** 2)
    f = 3200 - 1400 * np.tanh(pos)  # doppler fall as it passes
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * 0.3
    hiss = bp(noise(dur, rng), 2500, 9000)
    return finish((tone + hiss) * amp, peak=0.6)


def tone(freqs, dur, tau, attack=0.002, shape="sine", rng=None):
    t = t_axis(dur)
    out = np.zeros_like(t)
    for k, f in enumerate(freqs):
        if shape == "sine":
            w = np.sin(2 * np.pi * f * t)
        else:  # soft square
            w = np.tanh(3 * np.sin(2 * np.pi * f * t))
        out += w / (k + 1)
    return out * env_exp(dur, tau, attack)


def chime(notes, step, tau=0.25, dur=None):
    dur = dur or step * len(notes) + tau * 4
    x = np.zeros(int(dur * SR))
    for i, f in enumerate(notes):
        place(x, tone([f, f * 2, f * 3.01], tau * 5, tau), i * step)
    return finish(x, peak=0.6)


def stinger(rng, notes, step, rise=False):
    dur = step * len(notes) + 1.6
    x = np.zeros(int(dur * SR))
    for i, f in enumerate(notes):
        p = tone([f, f * 1.5, f * 2], 1.4, 0.45, attack=0.01, shape="square") * 0.5
        place(x, lp(p, 4000), i * step)
    if rise:
        place(x, bp(noise(step * len(notes), rng), 500, 4000) * np.linspace(0, 0.25, int(step * len(notes) * SR)), 0)
    x = x + convolve(x, reverb_ir(1.2, 1.2, rng, 5000))[: len(x)] * 0.25
    return finish(x, peak=0.65)


def loop_crossfade(x, n, fade):
    """x has n + fade samples; blend its overrun into the start so x[:n] loops without a click."""
    f = int(fade * SR)
    out = x[:n].copy()
    out[:f] = x[:f] * np.linspace(0, 1, f) + x[n:n + f] * np.linspace(1, 0, f)
    return out


def ambience_coast(rng, dur=32.0, fade=2.0):
    n = int(dur * SR)
    m = n + int(fade * SR)
    t = np.arange(m) / SR
    # wind: band-limited noise with slow gusts
    wind = bp(rng.standard_normal(m), 200, 1400)
    gust = 0.55 + 0.45 * np.sin(2 * np.pi * t / 16.0 + 0.4) * np.sin(2 * np.pi * t / 32.0 * 3 + 1.3)
    whistle = bp(rng.standard_normal(m), 900, 1300, order=4) * (0.3 + 0.7 * np.clip(gust - 0.6, 0, 1))
    # waves: low noise swells on an 8 s cycle (4 per loop)
    swell = np.clip(np.sin(2 * np.pi * t / 8.0), 0, 1) ** 2.5
    waves = lp(rng.standard_normal(m), 700) * (0.2 + swell)
    surf = bp(rng.standard_normal(m), 1500, 6000) * swell ** 3 * 0.25
    x = loop_crossfade(wind * gust * 0.6 + whistle * 0.25 + waves * 0.9 + surf, n, fade)
    return x / (np.max(np.abs(x)) + 1e-12) * 0.5


def ambience_hum(rng, dur=12.0, fade=1.0):
    n = int(dur * SR)
    m = n + int(fade * SR)
    t = np.arange(m) / SR
    hum = sum(np.sin(2 * np.pi * 50 * k * t + k) / k ** 1.3 for k in range(1, 7))  # whole cycles per loop
    rumble = lp(rng.standard_normal(m), 180)
    x = loop_crossfade(hum * 0.4 + rumble * 0.6, n, fade)
    return x / (np.max(np.abs(x)) + 1e-12) * 0.4


# ---------------------------------------------------------------- catalogue

def build(out):
    os.makedirs(out, exist_ok=True)
    manifest = []

    def emit(name, group, gen, variants=1, seed=0):
        lengths = []
        for v in range(variants):
            rng = np.random.default_rng(seed * 100 + v + 1)
            x = gen(rng)
            fn = f"{name}_{v}.wav" if variants > 1 else f"{name}.wav"
            write_wav(os.path.join(out, fn), x)
            lengths.append(round(len(x) / SR, 3))
        manifest.append({"name": name, "group": group, "variants": variants, "seconds": lengths})

    s = 1
    for w, p in WEAPONS.items():
        emit(f"shot_{w}", "weapon", lambda r, p=p: gunshot(r, **p), 3, s); s += 1
        emit(f"shot_{w}_far", "weapon", lambda r, p=p: gunshot(r, far=True, **p), 2, s); s += 1

    emit("mag_out", "weapon", lambda r: mech_sequence(r, [(0.0, 1900, 0.8, 0.5), (0.05, 1300, 0.5, 0.8)], 0.3), 1, s); s += 1
    emit("mag_in", "weapon", lambda r: mech_sequence(r, [(0.0, 1400, 0.5, 0.9), (0.09, 2400, 1.0, 0.4)], 0.35), 1, s); s += 1
    emit("bolt_rack", "weapon", lambda r: mech_sequence(r, [(0.0, 2100, 0.8, 0.6), (0.11, 1700, 1.0, 0.5)], 0.35), 1, s); s += 1
    emit("pump", "weapon", lambda r: mech_sequence(r, [(0.0, 900, 0.9, 0.9), (0.16, 1100, 1.0, 0.8)], 0.4), 1, s); s += 1
    emit("shell_in", "weapon", lambda r: mech_sequence(r, [(0.0, 1500, 0.7, 0.7), (0.035, 2600, 0.5, 0.3)], 0.2), 2, s); s += 1
    emit("bolt_cycle", "weapon", lambda r: mech_sequence(r, [(0.0, 1300, 0.6, 0.5), (0.12, 1600, 0.8, 0.5), (0.3, 1500, 0.9, 0.5), (0.42, 2200, 1.0, 0.4)], 0.6), 1, s); s += 1
    emit("slide", "weapon", lambda r: mech_sequence(r, [(0.0, 2600, 0.7, 0.5), (0.07, 3100, 1.0, 0.3)], 0.25), 1, s); s += 1
    emit("dry_fire", "weapon", lambda r: mech_sequence(r, [(0.0, 3400, 1.0, 0.3)], 0.12), 1, s); s += 1
    emit("equip", "weapon", lambda r: mech_sequence(r, [(0.0, 900, 0.3, 1.0), (0.12, 2000, 0.6, 0.6), (0.18, 2700, 0.4, 0.4)], 0.4), 1, s); s += 1

    for kind, n in (("concrete", 4), ("metal", 4), ("gravel", 4), ("wood", 3), ("water", 3)):
        emit(f"step_{kind}", "player", lambda r, k=kind: footstep(r, k), n, s); s += 1
    emit("jump", "player", lambda r: finish(bp(noise(0.25, r), 300, 3000) * env_exp(0.25, 0.06, 0.03), peak=0.4), 1, s); s += 1
    emit("land", "player", lambda r: finish(lp(noise(0.3, r), 900) * env_exp(0.3, 0.03) + sweep(0.3, 120, 55, 0.03) * env_exp(0.3, 0.05) * 0.6, peak=0.7), 2, s); s += 1
    emit("slide_loop", "player", lambda r: finish(bp(noise(0.8, r), 400, 5000) * env_exp(0.8, 0.5, 0.02), peak=0.4), 1, s); s += 1
    emit("hurt", "player", lambda r: impact(r, "flesh"), 2, s); s += 1
    emit("death", "player", death, 1, s); s += 1

    for kind, n in (("concrete", 3), ("metal", 3), ("thinmetal", 2), ("wood", 2), ("dirt", 2), ("water", 2), ("glass", 1), ("flesh", 2)):
        emit(f"impact_{kind}", "impact", lambda r, k=kind: impact(r, k), n, s); s += 1
    emit("whizz", "impact", whizz, 3, s); s += 1

    emit("hitmarker", "feedback", lambda r: finish(click(r, 3800, 0.05, 1.0, 2, 0.2), peak=0.5), 1, s); s += 1
    emit("headshot", "feedback", lambda r: chime([2093, 3136], 0.0, 0.12, 0.5), 1, s); s += 1
    emit("kill", "feedback", lambda r: chime([1318, 1976], 0.07, 0.14), 1, s); s += 1
    emit("ui_hover", "ui", lambda r: finish(tone([2400], 0.04, 0.008), peak=0.25), 1, s); s += 1
    emit("ui_click", "ui", lambda r: finish(click(r, 1800, 0.06, 1.0, 2, 0.3), peak=0.4), 1, s); s += 1
    emit("ui_back", "ui", lambda r: chime([880, 660], 0.05, 0.05), 1, s); s += 1
    emit("ui_ready", "ui", lambda r: chime([660, 990], 0.06, 0.08), 1, s); s += 1
    emit("countdown", "ui", lambda r: finish(tone([1046], 0.25, 0.06), peak=0.45), 1, s); s += 1
    emit("match_start", "ui", lambda r: stinger(r, [220, 277, 330, 440], 0.12, rise=True), 1, s); s += 1
    emit("victory", "ui", lambda r: stinger(r, [262, 330, 392, 523], 0.16), 1, s); s += 1
    emit("defeat", "ui", lambda r: stinger(r, [392, 330, 262, 196], 0.2), 1, s); s += 1

    emit("amb_coast", "ambience", lambda r: ambience_coast(r), 1, s); s += 1
    emit("amb_hum", "ambience", lambda r: ambience_hum(r), 1, s); s += 1

    with open(os.path.join(out, "manifest.json"), "w") as f:
        json.dump({"sampleRate": SR, "sounds": manifest}, f, indent=1)
    total = sum(m["variants"] for m in manifest)
    print(f"wrote {total} files ({len(manifest)} sounds) to {out}")


def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    build(ap.parse_args().out)
