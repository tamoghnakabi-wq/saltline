"""
Procedural, seamless textures for the Kappa Saltworks map.

Everything here is generated from seeded noise, so the output is original and repeatable.
Writes albedo (_D, RGBA: alpha = smoothness mask) and normal maps (_N, OpenGL / Unity
convention: green = +V) into Assets/_Saltline/Art/Maps/Kappa/Textures.

    python3 ArtSource/Textures/gen_kappa_textures.py
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy.spatial import cKDTree

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "_Saltline", "Art", "Maps", "Kappa", "Textures"))


# ---------------------------------------------------------------- noise helpers (all periodic)

def fnoise(n, beta, seed, aniso=(1.0, 1.0), band=None):
    """Periodic fractal noise in [0,1] made by shaping white noise in the frequency domain."""
    rng = np.random.default_rng(seed)
    w = rng.standard_normal((n, n))
    fy = np.fft.fftfreq(n)[:, None] * aniso[1]
    fx = np.fft.fftfreq(n)[None, :] * aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = 1.0 / f ** beta
    if band is not None:
        lo, hi = band
        amp *= (f >= lo / n) & (f <= hi / n)
    amp[0, 0] = 0.0
    out = np.real(np.fft.ifft2(np.fft.fft2(w) * amp))
    out -= out.min()
    out /= max(out.max(), 1e-9)
    return out


def worley(n, count, seed):
    """Periodic cellular noise. Returns (F1, F2, cell id) in pixels."""
    rng = np.random.default_rng(seed)
    pts = rng.random((count, 2)) * n
    tree = cKDTree(pts, boxsize=n)
    yy, xx = np.mgrid[0:n, 0:n]
    q = np.stack([xx.ravel() + 0.5, yy.ravel() + 0.5], axis=1) % n
    d, idx = tree.query(q, k=2)
    return d[:, 0].reshape(n, n), d[:, 1].reshape(n, n), idx[:, 0].reshape(n, n)


def smooth(x, e0, e1):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def blur(img, r):
    """Periodic box blur (separable, repeated 3x ~ gaussian)."""
    out = img.astype(np.float64)
    for _ in range(3):
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=0)
        out = acc / (2 * r + 1)
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=1)
        out = acc / (2 * r + 1)
    return out


def coords(n):
    """u (left->right) and v (bottom->top) in [0,1). Row 0 of an image is the top (v ~ 1)."""
    u = (np.arange(n) + 0.5) / n
    v = 1.0 - (np.arange(n) + 0.5) / n
    return np.meshgrid(u, v)


def mix(a, b, t):
    t = t[..., None] if np.ndim(t) == 2 else t
    return a * (1 - t) + b * t


def col(rgb):
    return np.array(rgb, dtype=np.float64)[None, None, :]


def normal_from_height(h, strength):
    """Height (rows top->bottom) to an OpenGL-convention normal map."""
    dhdu = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * strength
    dhdrow = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * strength
    dhdv = -dhdrow
    nrm = np.stack([-dhdu, -dhdv, np.ones_like(h)], axis=-1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    return nrm * 0.5 + 0.5


def save(name, albedo, height, strength, smooth_mask=None):
    os.makedirs(OUT, exist_ok=True)
    a = np.clip(albedo, 0, 1)
    if smooth_mask is None:
        smooth_mask = np.ones(a.shape[:2])
    rgba = np.concatenate([a, np.clip(smooth_mask, 0, 1)[..., None]], axis=-1)
    Image.fromarray((rgba * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, f"T_{name}_D.png"))
    if height is not None:
        n = normal_from_height(height, strength)
        Image.fromarray((n * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, f"T_{name}_N.png"))
    print("wrote", name)


# ---------------------------------------------------------------- materials

def asphalt(n=1024):
    # Tile = 4 m (256 px/m): fine aggregate in dark binder, a few worn light chips,
    # and a sparse network of dark hairline cracks. No light joints, so it reads as
    # asphalt rather than paving.
    grain = fnoise(n, 0.35, 11)
    blot = fnoise(n, 2.4, 12)
    f1, f2, cid = worley(n, 24000, 13)
    pebble = smooth(f2 - f1, 0.0, 1.6)
    tone = np.random.default_rng(14).random(24000)[cid]
    base = 0.15 + 0.05 * grain + 0.05 * (blot - 0.5)
    alb = col((1.0, 1.0, 1.02)) * base[..., None]
    chips = (tone > 0.88) * pebble * smooth(fnoise(n, 1.8, 16), 0.4, 0.8)
    alb = mix(alb, col((0.3, 0.295, 0.285)) * (0.8 + 0.4 * tone[..., None]), chips * 0.55)
    c1, c2, _ = worley(n, 30, 17)
    crack = (1 - smooth(c2 - c1, 0.0, 2.2)) * smooth(fnoise(n, 1.6, 18), 0.5, 0.68)
    alb = mix(alb, col((0.045, 0.045, 0.045)), crack * 0.85)
    oil = smooth(fnoise(n, 2.8, 15), 0.72, 0.9)
    alb = mix(alb, col((0.11, 0.11, 0.115)), oil * 0.35)
    h = 0.3 * pebble + 0.4 * grain - 0.9 * crack
    save("Asphalt", alb, h, 3.0, smooth_mask=0.25 + 0.5 * oil)


def concrete_wall(n=1024):
    # Tile = 4 m: cast panels 2 m wide, tie holes on a 0.5 m grid.
    u, v = coords(n)
    base = fnoise(n, 1.6, 21)
    fine = fnoise(n, 0.6, 22)
    streak = fnoise(n, 1.8, 23, aniso=(0.12, 1.0))
    alb = col((0.6, 0.59, 0.56)) * (0.82 + 0.25 * base[..., None] + 0.06 * fine[..., None])
    alb = mix(alb, col((0.36, 0.35, 0.33)), smooth(streak, 0.55, 0.85) * 0.55)
    alb = mix(alb, col((0.42, 0.44, 0.38)), smooth(fnoise(n, 2.2, 24), 0.75, 0.95) * 0.35)
    ju = np.minimum((u * 2) % 1, 1 - (u * 2) % 1) * 2.0  # distance to panel joint, metres
    jv = np.minimum((v * 2) % 1, 1 - (v * 2) % 1) * 2.0
    joint = np.maximum(1 - smooth(ju, 0.0, 0.012), 1 - smooth(jv, 0.0, 0.012))
    hu = np.minimum((u * 8) % 1, 1 - (u * 8) % 1) * 0.5
    hv = np.minimum(((v * 8) + 0.5) % 1, 1 - ((v * 8) + 0.5) % 1) * 0.5
    holes = 1 - smooth(np.sqrt(hu * hu + hv * hv), 0.008, 0.016)
    alb = mix(alb, col((0.3, 0.3, 0.29)), np.maximum(joint * 0.7, holes * 0.8))
    h = 0.5 * base + 0.25 * fine - 0.8 * joint - 0.6 * holes
    save("Concrete", alb, h, 3.0, smooth_mask=0.35 + 0.3 * smooth(streak, 0.55, 0.85))


def concrete_floor(n=1024):
    # Tile = 4 m with a saw-cut joint at the tile border.
    u, v = coords(n)
    base = fnoise(n, 1.9, 31)
    fine = fnoise(n, 0.5, 32)
    alb = col((0.52, 0.51, 0.49)) * (0.85 + 0.22 * base[..., None] + 0.05 * fine[..., None])
    scuff = smooth(fnoise(n, 1.2, 33, aniso=(1.0, 0.2)), 0.7, 0.9)
    alb = mix(alb, col((0.34, 0.33, 0.32)), scuff * 0.4)
    d = np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v)) * 4.0
    joint = 1 - smooth(d, 0.0, 0.01)
    f1, f2, _ = worley(n, 40, 34)
    crack = (1 - smooth(f2 - f1, 0.0, 1.6)) * smooth(fnoise(n, 1.5, 35), 0.6, 0.75)
    alb = mix(alb, col((0.25, 0.25, 0.24)), np.maximum(joint * 0.8, crack * 0.7))
    h = 0.3 * base + 0.2 * fine - joint - 0.5 * crack
    save("ConcreteFloor", alb, h, 3.0, smooth_mask=0.45 + 0.2 * base)


def facade(n=1024):
    # Tile = 4 m x 3.6 m (one storey, one window). Sun-bleached plaster.
    u, v = coords(n)
    base = fnoise(n, 1.7, 41)
    fine = fnoise(n, 0.7, 42)
    alb = col((0.8, 0.74, 0.62)) * (0.86 + 0.18 * base[..., None] + 0.05 * fine[..., None])
    wx = (u > 0.3) & (u < 0.7)
    wy = (v > 0.3) & (v < 0.8)
    win = (wx & wy).astype(float)
    frame = (((u > 0.29) & (u < 0.71) & (v > 0.29) & (v < 0.81)).astype(float) - win)
    mull = win * ((np.abs(u - 0.5) < 0.006) | (np.abs(v - 0.62) < 0.006))
    sill = ((u > 0.27) & (u < 0.73) & (v > 0.26) & (v < 0.29)).astype(float)
    glass = col((0.12, 0.16, 0.19)) + col((0.12, 0.14, 0.15)) * (v[..., None] - 0.3)
    alb = mix(alb, glass, win)
    alb = mix(alb, col((0.32, 0.34, 0.34)), np.maximum(frame, mull))
    alb = mix(alb, col((0.66, 0.64, 0.6)), sill)
    under = ((u > 0.3) & (u < 0.7) & (v < 0.26) & (v > 0.0)).astype(float)
    drip = fnoise(n, 1.4, 43, aniso=(0.1, 1.0))
    alb = mix(alb, col((0.5, 0.47, 0.4)), under * smooth(drip, 0.45, 0.8) * (v[..., None][..., 0] / 0.26) * 0.6)
    belt = (np.abs(v - 0.02) < 0.02).astype(float)
    alb = mix(alb, col((0.62, 0.58, 0.5)), belt)
    h = 0.3 * base + 0.15 * fine - 1.2 * win + 0.4 * frame + 0.6 * sill + 0.3 * belt
    save("Facade", alb, h, 2.5, smooth_mask=0.25 + 0.7 * win)


def salt(n=1024):
    f1, f2, cid = worley(n, 160, 51)
    edge = 1 - smooth(f2 - f1, 0.0, 5.0)
    g1, g2, _ = worley(n, 2400, 52)
    crystal = smooth(g2 - g1, 0.0, 2.5)
    tint = fnoise(n, 2.2, 53)
    grain = fnoise(n, 0.4, 54)
    alb = col((0.93, 0.92, 0.9)) * (0.92 + 0.08 * crystal[..., None] + 0.04 * grain[..., None])
    alb = mix(alb, col((0.92, 0.78, 0.78)), smooth(tint, 0.62, 0.85) * 0.55)
    alb = mix(alb, col((0.62, 0.6, 0.58)), edge * 0.55)
    plate = np.random.default_rng(55).random(160)[cid]
    alb *= (0.96 + 0.06 * plate)[..., None]
    h = 0.5 * crystal + 0.3 * grain - 1.0 * edge + 0.2 * plate
    save("Salt", alb, h, 3.5, smooth_mask=0.3 + 0.3 * crystal)


def sacks(n=512):
    # Tile = 1.2 m: 2 columns x 3 rows of woven sacks.
    u, v = coords(n)
    cu = (u * 2) % 1
    cv = (v * 3) % 1
    du = np.abs(cu - 0.5) * 2
    dv = np.abs(cv - 0.5) * 2
    pillow = np.clip(1 - du ** 6, 0, 1) * np.clip(1 - dv ** 4, 0, 1)
    gap = 1 - smooth(pillow, 0.0, 0.25)
    weave = 0.5 + 0.25 * np.sin(u * n * np.pi * 0.9) * np.sin(v * n * np.pi * 0.9)
    tone = fnoise(n, 1.8, 61)
    alb = col((0.74, 0.66, 0.52)) * (0.84 + 0.16 * tone[..., None] + 0.08 * weave[..., None])
    band = ((dv < 0.12) & (du < 0.7)).astype(float) * (np.abs(cu - 0.5) < 0.35)
    alb = mix(alb, col((0.42, 0.25, 0.18)), band * 0.75)
    alb = mix(alb, col((0.16, 0.14, 0.11)), gap * 0.85)
    h = 1.4 * pillow + 0.08 * weave
    save("Sacks", alb, h, 6.0, smooth_mask=np.full((n, n), 0.15))


def painted_metal(n=1024):
    # Tile = 2 m: panels 1 m with rivet lines.
    u, v = coords(n)
    base = fnoise(n, 1.8, 71)
    fine = fnoise(n, 0.7, 72)
    paint = col((0.25, 0.34, 0.37)) * (0.85 + 0.2 * base[..., None])
    rust_m = smooth(fnoise(n, 2.0, 73) * 0.7 + fnoise(n, 0.8, 74) * 0.3, 0.68, 0.82)
    rust = col((0.36, 0.2, 0.11)) * (0.75 + 0.5 * fine[..., None])
    rng = np.random.default_rng(75)
    scratch = np.zeros((n, n))
    for _ in range(140):
        x0, y0 = rng.random(2) * n
        ang = rng.normal(0.0, 0.5) + (np.pi / 2 if rng.random() < 0.3 else 0)
        ln = rng.uniform(10, 70)
        t = np.linspace(0, 1, int(ln * 2))
        xs = ((x0 + np.cos(ang) * ln * t) % n).astype(int)
        ys = ((y0 + np.sin(ang) * ln * t) % n).astype(int)
        scratch[ys, xs] = 1.0
    scratch = np.clip(blur(scratch, 1) * 3, 0, 1)
    ju = np.minimum(u * 2 % 1, 1 - u * 2 % 1) * 1.0
    jv = np.minimum(v * 2 % 1, 1 - v * 2 % 1) * 1.0
    seam = np.maximum(1 - smooth(ju, 0, 0.006), 1 - smooth(jv, 0, 0.006))
    rv = np.minimum(v * 20 % 1, 1 - v * 20 % 1) * 0.1
    rivet = (1 - smooth(np.sqrt((ju - 0.025) ** 2 + rv ** 2), 0.004, 0.008)) * (ju < 0.05)
    alb = mix(paint, rust, rust_m)
    alb = mix(alb, col((0.55, 0.55, 0.53)), scratch * (1 - rust_m) * 0.8)
    alb = mix(alb, col((0.12, 0.13, 0.13)), seam * 0.8)
    h = 0.15 * base + 0.6 * rust_m * fine - 0.8 * seam + 0.8 * rivet - 0.3 * scratch
    save("Metal", alb, h, 3.0, smooth_mask=0.55 * (1 - rust_m) + 0.15 + 0.2 * scratch)


def grate(n=512):
    # Tile = 1 m, 30 mm bars on a 33 mm pitch (30 x 30 cells).
    u, v = coords(n)
    cu = np.abs((u * 30) % 1 - 0.5) * 2
    cv = np.abs((v * 30) % 1 - 0.5) * 2
    bar = np.maximum(smooth(cu, 0.62, 0.78), smooth(cv, 0.62, 0.78))
    rust = smooth(fnoise(n, 1.6, 81), 0.6, 0.85)
    alb = mix(col((0.04, 0.04, 0.04)), col((0.34, 0.35, 0.34)), bar)
    alb = mix(alb, col((0.32, 0.19, 0.1)), rust * bar * 0.8)
    save("Grate", alb, bar, 8.0, smooth_mask=0.6 * bar * (1 - rust))


def tank_paint(n=1024):
    # Tile = 4 m: welded rings every 1.33 m, vertical seams every 2 m.
    u, v = coords(n)
    base = fnoise(n, 1.8, 91)
    streak = fnoise(n, 1.5, 92, aniso=(0.08, 1.0))
    alb = col((0.84, 0.83, 0.79)) * (0.9 + 0.1 * base[..., None])
    rs = smooth(streak, 0.6, 0.9)
    alb = mix(alb, col((0.5, 0.33, 0.2)), rs * 0.6)
    alb = mix(alb, col((0.62, 0.6, 0.55)), smooth(fnoise(n, 2.4, 93), 0.7, 0.9) * 0.4)
    wv = np.minimum(v * 3 % 1, 1 - v * 3 % 1) * 1.333
    wu = np.minimum(u * 2 % 1, 1 - u * 2 % 1) * 2.0
    weld = np.maximum(1 - smooth(wv, 0.0, 0.01), 1 - smooth(wu, 0.0, 0.01))
    alb = mix(alb, col((0.55, 0.5, 0.44)), weld * 0.6)
    h = 0.15 * base + 0.6 * weld + 0.2 * rs
    save("TankPaint", alb, h, 2.5, smooth_mask=0.5 * (1 - rs) + 0.15)


def rust(n=512):
    a = fnoise(n, 1.7, 101)
    b = fnoise(n, 0.7, 102)
    flake = smooth(fnoise(n, 1.2, 103), 0.55, 0.65)
    alb = mix(col((0.4, 0.2, 0.1)), col((0.55, 0.32, 0.16)), a)
    alb = mix(alb, col((0.22, 0.13, 0.08)), flake * 0.6)
    alb *= (0.85 + 0.25 * b)[..., None]
    h = 0.6 * a + 0.4 * b + 0.5 * flake
    save("Rust", alb, h, 4.0, smooth_mask=0.15 + 0.15 * b)


def corrugated(n=1024):
    # Tile = 2 m with 14 ribs. Faded light-blue paint, rust bleeding from fixings.
    u, v = coords(n)
    rib = 0.5 + 0.5 * np.cos(u * 14 * 2 * np.pi)
    base = fnoise(n, 1.8, 111)
    streak = fnoise(n, 1.6, 112, aniso=(0.06, 1.0))
    alb = col((0.56, 0.63, 0.66)) * (0.86 + 0.15 * base[..., None] + 0.05 * rib[..., None])
    rs = smooth(streak, 0.62, 0.9)
    alb = mix(alb, col((0.45, 0.27, 0.15)), rs * 0.65)
    fix_v = np.minimum(v * 2 % 1, 1 - v * 2 % 1)
    fix = (fix_v < 0.012) * (rib > 0.85)
    alb = mix(alb, col((0.2, 0.18, 0.16)), fix.astype(float))
    h = 1.0 * rib + 0.05 * base
    save("Corrugated", alb, h, 10.0, smooth_mask=0.45 * (1 - rs) + 0.1)


def wood(n=512):
    # Tile = 1.5 m: 6 boards.
    u, v = coords(n)
    board = np.floor(v * 6).astype(int)
    rng = np.random.default_rng(121)
    tone = rng.uniform(0.8, 1.15, 6)[board]
    offs = rng.uniform(0, 1, 6)[board]
    grain = fnoise(n, 1.4, 122, aniso=(1.0, 0.05))
    grain2 = np.sin((grain * 18 + offs * 6) * np.pi) * 0.5 + 0.5
    edge_d = np.minimum(v * 6 % 1, 1 - v * 6 % 1)
    edge = 1 - smooth(edge_d, 0.0, 0.05)
    alb = col((0.6, 0.45, 0.3)) * (tone[..., None] * (0.85 + 0.2 * grain2[..., None]))
    alb = mix(alb, col((0.38, 0.38, 0.36)), smooth(fnoise(n, 2.0, 123), 0.55, 0.8) * 0.5)  # weathering
    alb = mix(alb, col((0.15, 0.11, 0.08)), edge * 0.9)
    nail_u = np.minimum(u * 3 % 1, 1 - u * 3 % 1) / 3
    nail = (1 - smooth(np.sqrt((nail_u - 0.03) ** 2 + ((edge_d - 0.5) / 6) ** 2), 0.003, 0.006))
    alb = mix(alb, col((0.1, 0.1, 0.1)), nail)
    h = 0.4 * grain2 - 1.0 * edge
    save("Wood", alb, h, 4.0, smooth_mask=np.full((n, n), 0.2))


def rock(n=1024):
    u, v = coords(n)
    a = fnoise(n, 2.0, 131)
    b = fnoise(n, 1.0, 132)
    strata = 0.5 + 0.5 * np.sin((v * 9 + a * 1.5) * 2 * np.pi)
    f1, f2, _ = worley(n, 90, 133)
    crack = 1 - smooth(f2 - f1, 0.0, 4.0)
    alb = col((0.42, 0.37, 0.32)) * (0.75 + 0.3 * a[..., None] + 0.1 * strata[..., None])
    alb = mix(alb, col((0.55, 0.53, 0.42)), smooth(fnoise(n, 1.3, 134), 0.72, 0.85) * 0.5)
    alb = mix(alb, col((0.16, 0.14, 0.12)), crack * 0.7)
    h = 0.8 * a + 0.3 * b + 0.25 * strata - 0.8 * crack
    save("Rock", alb, h, 5.0, smooth_mask=0.2 + 0.1 * b)


def hazard(n=512):
    u, v = coords(n)
    s = ((u + v) * 4) % 1
    stripe = smooth(s, 0.48, 0.52) * (1 - smooth(s, 0.98, 1.0))
    wear = smooth(fnoise(n, 1.2, 141), 0.62, 0.8)
    alb = mix(col((0.85, 0.66, 0.08)), col((0.07, 0.07, 0.07)), stripe)
    alb = mix(alb, col((0.45, 0.44, 0.42)), wear * 0.7)
    save("Hazard", alb, fnoise(n, 0.8, 142) * 0.3 + wear * 0.2, 2.0, smooth_mask=0.4 * (1 - wear) + 0.1)


def container(n=1024):
    # Tile = 2.24 m: 8 trapezoid ribs. Weathered red-oxide paint.
    u, v = coords(n)
    p = (u * 8) % 1
    rib = smooth(p, 0.1, 0.2) * (1 - smooth(p, 0.6, 0.7))
    base = fnoise(n, 1.8, 151)
    streak = fnoise(n, 1.6, 152, aniso=(0.07, 1.0))
    alb = col((0.52, 0.2, 0.13)) * (0.82 + 0.22 * base[..., None] + 0.05 * rib[..., None])
    alb = mix(alb, col((0.3, 0.17, 0.1)), smooth(streak, 0.6, 0.88) * 0.6)
    alb = mix(alb, col((0.62, 0.42, 0.34)), smooth(fnoise(n, 2.2, 153), 0.72, 0.9) * 0.4)
    save("Container", alb, rib + 0.05 * base, 6.0, smooth_mask=0.35 + 0.2 * base)


def ripples(n=512):
    a = fnoise(n, 1.6, 161, band=(2, 60))
    b = fnoise(n, 1.2, 162, band=(8, 120))
    h = 0.7 * a + 0.3 * b
    flat = np.ones((n, n, 3))
    save("Water", flat, h, 6.0)


def paint(n=256):
    # Neutral worn-paint mask: multiplied by the team colour in the material.
    w = fnoise(n, 1.4, 171)
    chip = smooth(fnoise(n, 0.9, 172), 0.7, 0.8)
    alb = np.repeat((0.92 + 0.08 * w)[..., None], 3, axis=-1)
    alb = mix(alb, col((0.55, 0.55, 0.55)), chip)
    save("Paint", alb, w * 0.2 - chip * 0.3, 2.0, smooth_mask=0.5 * (1 - chip))


ALL = [asphalt, concrete_wall, concrete_floor, facade, salt, sacks, painted_metal, grate, tank_paint,
       rust, corrugated, wood, rock, hazard, container, ripples, paint]

if __name__ == "__main__":
    only = set(sys.argv[1:])
    for fn in ALL:
        if not only or fn.__name__ in only:
            fn()
