"""
Procedural textures for SALTLINE's weapon and impact effects. All original, generated from
seeded noise, so the output is reproducible:

    python3 ArtSource/FX/fx_textures.py Assets/_Saltline/Art/FX/Textures

Outputs (RGBA PNG unless noted):
  T_FX_MuzzleFront   2x2 flipbook, front view of a muzzle flash (star of flame petals)
  T_FX_MuzzleSide    2x2 flipbook, side view of the flame jet; u runs from the muzzle (0) to the tip (1)
  T_FX_Smoke         2x2 flipbook, soft smoke puffs with baked top light
  T_FX_Glow          soft round dot (sparks, glints, impact flashes)
  T_FX_Debris        2x2 flipbook, irregular chips (alpha-tested, tinted per surface at runtime)
  T_FX_Hole_D/_N/_M  2x2 bullet-hole atlas: concrete, metal, wood, soft ground
                     colour + alpha, tangent-space normal (OpenGL, +y up), metallic (R) / smoothness (A)
  T_FX_Tracer        soft line, bright core across v
Needs numpy and Pillow.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

TAU = 2.0 * math.pi


# ---------------------------------------------------------------- noise helpers

def vnoise(n, cells, rng):
    """Smooth value noise, n x n, about `cells` features across."""
    g = rng.random((cells + 3, cells + 3)).astype(np.float32)
    big = Image.fromarray(g).resize((n * (cells + 3) // cells, n * (cells + 3) // cells), Image.BICUBIC)
    a = np.asarray(big)
    o = n // cells
    return a[o:o + n, o:o + n]


def fbm(n, cells, octaves, rng, gain=0.5):
    total = np.zeros((n, n), np.float32)
    amp, norm = 1.0, 0.0
    for i in range(octaves):
        total += amp * vnoise(n, cells * (2 ** i), rng)
        norm += amp
        amp *= gain
    return total / norm


def noise1d(count, samples, rng):
    """Periodic 1-D noise with `count` features, sampled `samples` times over one period."""
    k = rng.random(count).astype(np.float32)
    x = np.linspace(0, count, samples, endpoint=False)
    i0 = np.floor(x).astype(int) % count
    i1 = (i0 + 1) % count
    f = x - np.floor(x)
    f = f * f * (3 - 2 * f)
    return k[i0] * (1 - f) + k[i1] * f


def periodic_lookup(table, angle):
    """Sample a periodic 1-D table at angles in radians."""
    n = len(table)
    x = (angle % TAU) / TAU * n
    i0 = np.floor(x).astype(int) % n
    i1 = (i0 + 1) % n
    f = x - np.floor(x)
    return table[i0] * (1 - f) + table[i1] * f


def grid(n):
    c = (np.arange(n, dtype=np.float32) + 0.5) / n * 2 - 1
    x, y = np.meshgrid(c, -c)  # y up
    return x, y


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def ramp(t, stops):
    """Piecewise-linear colour ramp. stops: list of (position, (r, g, b))."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,), np.float32)
    for (p0, c0), (p1, c1) in zip(stops[:-1], stops[1:]):
        m = (t >= p0) & (t <= p1)
        f = ((t - p0) / max(p1 - p0, 1e-6))[m][:, None]
        out[m] = np.array(c0) * (1 - f) + np.array(c1) * f
    return out


FLAME = [(0.0, (0.55, 0.12, 0.02)), (0.25, (0.95, 0.40, 0.08)), (0.55, (1.0, 0.74, 0.30)),
         (0.82, (1.0, 0.93, 0.70)), (1.0, (1.0, 1.0, 0.95))]


def atlas(cells):
    """2x2 atlas from four equal cells (top-left, top-right, bottom-left, bottom-right)."""
    top = np.concatenate([cells[0], cells[1]], axis=1)
    bot = np.concatenate([cells[2], cells[3]], axis=1)
    return np.concatenate([top, bot], axis=0)


def save(path, rgba):
    a = np.clip(rgba * 255.0 + 0.5, 0, 255).astype(np.uint8)
    mode = "RGBA" if a.shape[-1] == 4 else "RGB"
    Image.fromarray(a).save(path, optimize=True)


# ---------------------------------------------------------------- muzzle flash

def flash_front(rng, n=256):
    x, y = grid(n)
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    petals = int(rng.integers(4, 7))
    phase = rng.random() * TAU
    reach = np.zeros_like(r)
    for i in range(petals):
        c = phase + i * TAU / petals + rng.normal(0, 0.12)
        d = np.angle(np.exp(1j * (th - c)))
        width = (0.20 + 0.10 * rng.random()) * TAU / petals
        length = 0.50 + 0.42 * rng.random()
        reach = np.maximum(reach, length * np.exp(-(d / width) ** 2))
    jag = periodic_lookup(noise1d(48, 1024, rng), th)
    rlim = 0.20 + reach * (0.80 + 0.35 * jag)
    breakup = fbm(n, 6, 4, rng)
    body = np.clip(1 - r / np.maximum(rlim, 1e-3), 0, 1) ** 1.35
    body *= 0.55 + 0.75 * breakup
    streak = periodic_lookup(noise1d(90, 2048, rng), th)
    body *= np.where(r > 0.25, 0.65 + 0.5 * streak, 1.0)
    core = np.exp(-(r / 0.16) ** 2)
    i = np.clip(body + 0.95 * core, 0, 1)
    i *= smoothstep(1.0, 0.86, r)
    rgb = ramp(i ** 0.8, FLAME)
    return np.dstack([rgb, np.clip(i ** 1.1, 0, 1)])


def flash_side(rng, n=256):
    """u (columns) runs from the muzzle (0) to the flame tip (1); v (rows) across the jet."""
    c = (np.arange(n, dtype=np.float32) + 0.5) / n
    u, v = np.meshgrid(c, (c * 2 - 1)[::-1])
    width = 1.15 * (u ** 0.5) * ((1 - u) ** 0.9) + 0.05
    wob = vnoise(n, 7, rng)
    width *= 0.75 + 0.5 * wob
    across = np.abs(v) / np.maximum(width, 1e-3)
    # streaks run along the jet: noise squashed across u, then stretched back
    tongues = fbm(n, 6, 4, rng)
    tongues = np.asarray(Image.fromarray(tongues).resize((n // 5, n), Image.BICUBIC).resize((n, n), Image.BICUBIC))
    body = np.clip(1 - across, 0, 1) ** 1.4
    body *= np.clip(0.30 + 1.1 * tongues - 0.50 * u, 0, 1.4)
    diamond_at = 0.22 + 0.08 * rng.random()
    diamond = np.exp(-((u - diamond_at) / 0.06) ** 2 - (v / 0.12) ** 2)
    root = np.exp(-((u - 0.05) / 0.05) ** 2 - (v / 0.14) ** 2)
    i = np.clip(body * (1.0 - 0.35 * u) + 0.85 * diamond + 0.6 * root, 0, 1)
    i *= smoothstep(1.0, 0.82, u) * smoothstep(0.0, 0.05, u)
    rgb = ramp(i ** 0.85, FLAME)
    return np.dstack([rgb, np.clip(i, 0, 1)])


# ---------------------------------------------------------------- smoke, glow, debris

def smoke_puff(rng, n=256):
    x, y = grid(n)
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    edge = periodic_lookup(noise1d(10, 1024, rng), th)
    rad = 0.62 + 0.30 * edge
    mask = np.clip(1 - (r / rad) ** 2, 0, 1)
    dens = fbm(n, 3, 5, rng)
    # a dense middle with billowy, broken edges (no hollow rings)
    d = np.clip((0.55 * mask ** 0.6 + 0.65 * dens - 0.42) * 2.0, 0, 1) * mask ** 0.5
    d = np.asarray(Image.fromarray((d * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2.0)), np.float32) / 255.0
    gy, gx = np.gradient(d)
    light = np.clip(0.80 + 6.0 * (gx * -0.5 + gy * 0.8), 0.45, 1.05)  # rows grow downward, so +gy faces up
    shade = light * (0.88 + 0.12 * y)
    rgb = np.dstack([shade, shade, shade * 0.98])
    return np.dstack([np.clip(rgb, 0, 1), np.clip(d ** 1.1, 0, 1)])


def glow(n=128):
    x, y = grid(n)
    r = np.hypot(x, y)
    i = np.exp(-(r / 0.22) ** 2) + 0.35 * np.exp(-(r / 0.55) ** 2)
    i = np.clip(i / i.max(), 0, 1) * smoothstep(1.0, 0.8, r)
    return np.dstack([np.ones_like(i), np.ones_like(i), np.ones_like(i), i])


def chip(rng, n=128):
    ss = 4
    img = Image.new("L", (n * ss, n * ss), 0)
    k = int(rng.integers(5, 9))
    angles = np.sort(rng.random(k) * TAU)
    pts = []
    for a in angles:
        rr = (0.45 + 0.45 * rng.random()) * n * ss * 0.5
        pts.append((n * ss / 2 + rr * math.cos(a), n * ss / 2 + rr * math.sin(a)))
    ImageDraw.Draw(img).polygon(pts, fill=255)
    mask = np.asarray(img.resize((n, n), Image.LANCZOS), np.float32) / 255.0
    tone = 0.55 + 0.35 * fbm(n, 4, 3, rng)
    edge = np.asarray(Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(5)), np.float32) / 255.0
    tone = tone * (0.70 + 0.30 * edge)
    return np.dstack([tone, tone, tone, mask])


# ---------------------------------------------------------------- bullet holes

def normal_from_height(h, strength):
    gy, gx = np.gradient(h)
    nx = -gx * strength
    ny = gy * strength  # rows grow downward; +v is up
    nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.dstack([nx / l * 0.5 + 0.5, ny / l * 0.5 + 0.5, nz / l * 0.5 + 0.5])


def hole_concrete(rng, n=256):
    x, y = grid(n)
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    crater = 0.40 * (1 + 0.30 * (periodic_lookup(noise1d(12, 1024, rng), th) - 0.5)
                     + 0.12 * (periodic_lookup(noise1d(40, 2048, rng), th) - 0.5))
    hole = 0.10 * (1 + 0.25 * (periodic_lookup(noise1d(8, 512, rng), th) - 0.5))
    rough = fbm(n, 10, 4, rng)
    t = np.clip(r / crater, 0, 1.5)
    h = np.where(r < crater, -0.55 * np.clip(1 - t ** 2, 0, 1) ** 0.6 + 0.18 * (rough - 0.5), 0.0)
    h = np.where(r < hole, -1.0, h)
    fresh = np.dstack([0.50 + 0.14 * rough, 0.485 + 0.13 * rough, 0.46 + 0.12 * rough]) * (0.80 + 0.20 * t[..., None])
    rgb = np.where((r < crater)[..., None], fresh, 0.22)
    rgb = np.where((r < hole)[..., None], np.array([0.025, 0.024, 0.022]), rgb)
    rim = smoothstep(crater - 0.05, crater, r) * (r < crater)
    rgb = rgb * (1 - 0.35 * rim[..., None])
    a = smoothstep(crater + 0.012, crater - 0.012, r)
    # powder burn and dust around the crater
    dust = smoothstep(crater * 1.75, crater, r) * (0.30 + 0.25 * rough)
    rgb = np.where((r >= crater)[..., None], np.array([0.20, 0.19, 0.18]), rgb)
    a = np.maximum(a, dust * (r >= crater))
    # hairline cracks
    for _ in range(int(rng.integers(3, 6))):
        c = rng.random() * TAU
        d = np.abs(np.angle(np.exp(1j * (th - c - 0.05 * (vnoise(n, 12, rng) - 0.5)))))
        length = crater * (1.5 + 0.6 * rng.random())
        line = (d * r < 0.010 * (1.2 - r / length)) & (r > crater * 0.9) & (r < length)
        rgb = np.where(line[..., None], np.array([0.10, 0.10, 0.10]), rgb)
        a = np.where(line, np.maximum(a, 0.75), a)
        h = np.where(line, h - 0.15, h)
    met = np.zeros_like(r)
    smo = np.where(r < hole, 0.05, 0.12)
    return rgb, a, h, met, smo, 9.0


def hole_metal(rng, n=256):
    x, y = grid(n)
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    hole = 0.095 * (1 + 0.12 * (periodic_lookup(noise1d(9, 512, rng), th) - 0.5))
    lip = hole + 0.055
    bare = 0.24 * (1 + 0.35 * (periodic_lookup(noise1d(14, 1024, rng), th) - 0.5))
    scratch = fbm(n, 24, 3, rng)
    dent = -0.45 * np.exp(-(r / 0.30) ** 2)
    petals = 0.35 * np.clip(1 - np.abs(r - hole - 0.02) / 0.05, 0, 1) * (0.7 + 0.3 * np.cos(th * 7 + rng.random() * TAU))
    h = dent + petals
    h = np.where(r < hole, -1.0, h)
    steel = np.dstack([0.70 + 0.12 * scratch, 0.69 + 0.12 * scratch, 0.67 + 0.11 * scratch])
    soot = np.array([0.07, 0.065, 0.06])
    rgb = np.where((r < bare)[..., None], steel, soot)
    rgb = np.where((r < hole)[..., None], np.array([0.02, 0.02, 0.02]), rgb)
    a = np.where(r < bare, 1.0, smoothstep(bare * 2.1, bare, r) * (0.55 + 0.35 * fbm(n, 6, 3, rng)))
    # paint lifts at the edge of the bare ring
    edge = smoothstep(bare - 0.02, bare, r) * (r < bare + 0.01)
    rgb = rgb * (1 - 0.5 * edge[..., None])
    met = np.where((r < bare) & (r >= hole), 1.0, 0.0)
    smo = np.where((r < bare) & (r >= hole), 0.62 + 0.25 * scratch, 0.25)
    return rgb, np.clip(a, 0, 1), h, met, smo, 7.0


def hole_wood(rng, n=256):
    x, y = grid(n)
    grain = fbm(n, 6, 4, rng)
    grain = np.asarray(Image.fromarray(grain).resize((n // 6, n), Image.BICUBIC).resize((n, n), Image.BICUBIC))
    jag = vnoise(n, 16, rng)
    jag = np.asarray(Image.fromarray(jag).resize((n // 8, n), Image.BICUBIC).resize((n, n), Image.BICUBIC))
    e = np.hypot(x / (0.48 + 0.55 * (jag - 0.5)), y / 0.19)
    hole = np.hypot(x / 0.12, y / 0.085)
    fresh = np.dstack([0.60 + 0.16 * grain, 0.47 + 0.13 * grain, 0.33 + 0.09 * grain])
    rgb = np.where((e < 1)[..., None], fresh, np.array([0.15, 0.115, 0.08]))
    rgb = np.where((hole < 1)[..., None], np.array([0.03, 0.022, 0.015]), rgb)
    h = np.where(e < 1, -0.35 * (1 - e) + 0.35 * (grain - 0.5), 0.0)
    h = np.where(hole < 1, -1.0, h)
    a = smoothstep(1.03, 0.97, e)
    a = np.maximum(a, smoothstep(1.8, 1.0, e) * 0.35 * (np.abs(y) < 0.30))
    # long splinters along the grain
    for _ in range(int(rng.integers(4, 7))):
        yy = rng.normal(0, 0.09)
        reach = 0.55 + 0.35 * rng.random()
        side = 1 if rng.random() < 0.5 else -1
        line = (np.abs(y - yy) < 0.012 * (1 - np.abs(x) / reach)) & (x * side > 0.3) & (np.abs(x) < reach)
        rgb = np.where(line[..., None], np.array([0.68, 0.55, 0.39]), rgb)
        a = np.where(line, 1.0, a)
        h = np.where(line, h + 0.25, h)
    met = np.zeros_like(x)
    smo = np.full_like(x, 0.18)
    return rgb, np.clip(a, 0, 1), h, met, smo, 8.0


def hole_soft(rng, n=256):
    x, y = grid(n)
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    rad = 0.34 * (1 + 0.35 * (periodic_lookup(noise1d(10, 1024, rng), th) - 0.5))
    rough = fbm(n, 12, 3, rng)
    t = np.clip(r / rad, 0, 1.6)
    h = np.where(t < 1, -0.6 * (1 - t ** 2), 0.12 * np.exp(-((t - 1.15) / 0.2) ** 2)) + 0.1 * (rough - 0.5)
    tone = 0.10 + 0.20 * np.clip(t, 0, 1) + 0.06 * rough
    rgb = np.dstack([tone, tone * 0.93, tone * 0.85])
    a = smoothstep(1.5, 0.9, t) * (0.75 + 0.25 * rough)
    met = np.zeros_like(r)
    smo = np.full_like(r, 0.06)
    return rgb, np.clip(a, 0, 1), h, met, smo, 6.0


# ---------------------------------------------------------------- tracer

def tracer(w=256, h=32):
    v = (np.arange(h, dtype=np.float32) + 0.5) / h * 2 - 1
    i = np.exp(-(v / 0.16) ** 2) + 0.35 * np.exp(-(v / 0.55) ** 2)
    i = np.clip(i / i.max(), 0, 1)
    i = np.tile(i[:, None], (1, w))
    white = np.ones_like(i)
    core = np.clip(i * 1.4, 0, 1)
    return np.dstack([white, 0.80 + 0.20 * core, 0.55 + 0.45 * core, i])


# ---------------------------------------------------------------- main

def main(out):
    os.makedirs(out, exist_ok=True)
    rng = np.random.default_rng(20261103)
    save(os.path.join(out, "T_FX_MuzzleFront.png"), atlas([flash_front(rng) for _ in range(4)]))
    save(os.path.join(out, "T_FX_MuzzleSide.png"), atlas([flash_side(rng) for _ in range(4)]))
    save(os.path.join(out, "T_FX_Smoke.png"), atlas([smoke_puff(rng) for _ in range(4)]))
    save(os.path.join(out, "T_FX_Glow.png"), glow())
    save(os.path.join(out, "T_FX_Debris.png"), atlas([chip(rng) for _ in range(4)]))
    save(os.path.join(out, "T_FX_Tracer.png"), tracer())

    d, nrm, m = [], [], []
    for fn in (hole_concrete, hole_metal, hole_wood, hole_soft):
        rgb, a, h, met, smo, strength = fn(rng)
        d.append(np.dstack([rgb, a]))
        nrm.append(normal_from_height(h, strength))
        m.append(np.dstack([met, np.ones_like(met), np.zeros_like(met), smo]))
    save(os.path.join(out, "T_FX_Hole_D.png"), atlas(d))
    save(os.path.join(out, "T_FX_Hole_N.png"), atlas(nrm))
    save(os.path.join(out, "T_FX_Hole_M.png"), atlas(m))
    print("wrote FX textures to", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Assets/_Saltline/Art/FX/Textures")
