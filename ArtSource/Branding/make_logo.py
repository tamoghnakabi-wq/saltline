"""
The SALTLINE logo: an S drawn as the map's three lanes, joined like the salt works' pipes, with the sea
horizon (the "line") running through the middle lane. Original artwork made for this project.

One geometry, written out at every size the game and the website use:

    Assets/_Saltline/Art/Branding/SaltlineIcon_<size>.png   app icon (macOS and Windows), 16-1024 px
    Assets/_Saltline/Art/Branding/SaltlineMark.png          the S alone, transparent, for the main menu
    Web/public/icon.svg                                     site favicon
    Web/public/logo-mark.svg                                the S alone, for the site header

    python3 ArtSource/Branding/make_logo.py [--preview out.png]
"""
import argparse
import functools
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
ASSETS = os.path.join(ROOT, "Assets", "_Saltline", "Art", "Branding")
WEB = os.path.join(ROOT, "Web", "public")

# ---- palette (the game's UI tokens, Assets/_Saltline/UI/Saltline.uss)
ACCENT_TOP = (255, 184, 90)
ACCENT_BOTTOM = (234, 136, 34)
BG_TOP = (30, 36, 45)
BG_BOTTOM = (9, 11, 15)
SALT = (233, 237, 241)

# ---- the S, in its own units: W x H, lanes T thick, turns of centre radius R, both ends cut at a slant
#      (the top end's lower edge and the bottom end's upper edge are SLANT shorter), so the S leans forward
W, H, T, R = 500.0, 600.0, 124.0, 78.0
SLANT = 0.36 * T
Y1, Y2, Y3 = T / 2, H / 2, H - T / 2
X0, X1 = T / 2, W - T / 2

# ---- the icon, on Apple's 1024 grid: an 824 px body with 185 px corners, centred
ICON, BODY, CORNER = 1024, 824, 185.4
S_HEIGHT = 0.56 * BODY
SS = 4  # supersampling


def centreline(n=64):
    """(point, unit tangent) along the S, from the top-right end to the bottom-left end."""
    out = []

    def straight(a, b):
        d = (b[0] - a[0], b[1] - a[1])
        L = math.hypot(*d)
        out.append((a, (d[0] / L, d[1] / L)))

    def arc(cx, cy, a0, a1):
        sgn = 1 if a1 > a0 else -1
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            p = (cx + R * math.cos(a), cy + R * math.sin(a))
            out.append((p, (-math.sin(a) * sgn, math.cos(a) * sgn)))

    straight((W, Y1), (X0 + R, Y1))
    arc(X0 + R, Y1 + R, 270, 180)
    straight((X0, Y1 + R), (X0, Y2 - R))
    arc(X0 + R, Y2 - R, 180, 90)
    straight((X0 + R, Y2), (X1 - R, Y2))
    arc(X1 - R, Y2 + R, -90, 0)
    straight((X1, Y2 + R), (X1, Y3 - R))
    arc(X1 - R, Y3 - R, 0, 90)
    out.append(((0.0, Y3), (-1.0, 0.0)))
    return out


def s_polygon(scale, ox, oy, n=64):
    """The outline of the S stroke: its left edge forward, then its right edge back."""
    left, right = [], []
    for (px, py), (tx, ty) in centreline(n):
        nx, ny = ty, -tx  # left of the direction of travel (y down)
        left.append((ox + (px + nx * T / 2) * scale, oy + (py + ny * T / 2) * scale))
        right.append((ox + (px - nx * T / 2) * scale, oy + (py - ny * T / 2) * scale))
    left[0] = (left[0][0] - SLANT * scale, left[0][1])  # the top end's lower corner
    right[-1] = (right[-1][0] + SLANT * scale, right[-1][1])  # the bottom end's upper corner
    return left + right[::-1]


def svg_path():
    """The S outline as a filled SVG path, in S units."""
    pts = s_polygon(1.0, 0.0, 0.0, n=16)
    return "M" + "L".join(f"{x:.1f} {y:.1f}" for x, y in pts) + "Z"


def vgradient(w, h, top, bottom):
    t = np.linspace(0, 1, h)[:, None, None]
    rgb = np.array(top)[None, None, :] * (1 - t) + np.array(bottom)[None, None, :] * t
    return np.repeat(rgb, w, axis=1)


def layer(rgb, alpha):
    a = np.asarray(alpha, dtype=np.float64)[..., None] / 255.0
    return Image.fromarray(np.concatenate([rgb, a * 255], axis=2).clip(0, 255).astype(np.uint8))


def mask(size, draw_fn):
    m = Image.new("L", (size, size), 0)
    draw_fn(ImageDraw.Draw(m))
    return m


def render_icon(px):
    """The app icon at px x px. Small sizes drop the horizon line, which would only blur."""
    return icon_master(px >= 128).resize((px, px), Image.LANCZOS)


@functools.lru_cache(maxsize=None)
def icon_master(with_line):
    n = ICON * SS
    k = n / ICON
    b0, b1 = (ICON - BODY) / 2 * k, (ICON + BODY) / 2 * k
    body = mask(n, lambda d: d.rounded_rectangle((b0, b0, b1, b1), radius=CORNER * k, fill=255))
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))

    # body shadow, inside the grid's margin
    shadow = body.filter(ImageFilter.GaussianBlur(22 * k)).point(lambda v: int(v * 0.38))
    img.alpha_composite(layer(np.zeros((n, n, 3)), shadow), (0, int(12 * k)))

    # body: graphite gradient, a warm glow behind the S, a faint rim
    bg = vgradient(n, n, BG_TOP, BG_BOTTOM)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) / n
    glow = np.exp(-(((xx - 0.5) ** 2) + ((yy - 0.46) ** 2)) / (2 * 0.2 ** 2))[..., None]
    bg = bg * (1 - 0.16 * glow) + np.array(ACCENT_BOTTOM)[None, None, :] * 0.16 * glow
    img.alpha_composite(layer(bg, body))
    rim = mask(n, lambda d: d.rounded_rectangle((b0, b0, b1, b1), radius=CORNER * k, outline=255, width=int(3 * k)))
    img.alpha_composite(layer(np.full((n, n, 3), 255.0), rim.point(lambda v: int(v * 0.07))))

    scale = S_HEIGHT * k / H
    ox, oy = n / 2 - W * scale / 2, n / 2 - H * scale / 2

    if with_line:  # the horizon, fading towards the body's edges
        hy = oy + Y2 * scale
        hh = 0.13 * T * scale
        x0, x1 = b0 + 64 * k, b1 - 64 * k
        line = mask(n, lambda d: d.rectangle((x0, hy - hh / 2, x1, hy + hh / 2), fill=255))
        ramp = np.clip(np.minimum(xx * n - x0, x1 - xx * n) / (150 * k), 0, 1) ** 1.5
        a = np.asarray(line, dtype=np.float64) * ramp * 0.9
        img.alpha_composite(layer(np.full((n, n, 3), SALT, dtype=np.float64), a))

    s = mask(n, lambda d: d.polygon(s_polygon(scale, ox, oy), fill=255))
    sh = s.filter(ImageFilter.GaussianBlur(16 * k)).point(lambda v: int(v * 0.5))
    img.alpha_composite(layer(np.zeros((n, n, 3)), sh), (0, int(10 * k)))
    top, bottom = oy, oy + H * scale
    grad = np.zeros((n, n, 3))
    t = np.clip((np.arange(n) - top) / (bottom - top), 0, 1)[:, None, None]
    grad[:] = np.array(ACCENT_TOP) * (1 - t) + np.array(ACCENT_BOTTOM) * t
    img.alpha_composite(layer(grad, s))
    return img


def render_mark(height):
    """The S alone on transparent, with a little padding so filtering never clips it."""
    pad = 0.04
    h = int(height * SS)
    scale = h * (1 - 2 * pad) / H
    w = int(W * scale + 2 * pad * h)
    o = pad * h
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).polygon(s_polygon(scale, o, o), fill=255)
    t = np.clip((np.arange(h) - o) / (H * scale), 0, 1)[:, None, None]
    grad = np.repeat(np.array(ACCENT_TOP) * (1 - t) + np.array(ACCENT_BOTTOM) * t, w, axis=1)
    img = layer(grad, m)
    return img.resize((round(w / SS), height), Image.LANCZOS)


def favicon_svg():
    # 32-unit square, the S 18 units high, flat colours (a gradient is lost at 16 px)
    s = 18 / H
    tx, ty = 16 - W * s / 2, 16 - H * s / 2
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">'
            '<rect width="32" height="32" rx="7" fill="#12161c"/>'
            f'<path transform="translate({tx:.3f} {ty:.3f}) scale({s:.5f})" d="{svg_path()}" fill="#f29a2e"/>'
            '</svg>\n')


def mark_svg():
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W:g} {H:g}">'
            '<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1">'
            '<stop offset="0" stop-color="#ffb85a"/><stop offset="1" stop-color="#ea8822"/></linearGradient></defs>'
            f'<path d="{svg_path()}" fill="url(#g)"/></svg>\n')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", help="also write a contact sheet of every size to this PNG")
    a = ap.parse_args()

    os.makedirs(ASSETS, exist_ok=True)
    sizes = [1024, 512, 256, 128, 64, 48, 32, 16]
    icons = {s: render_icon(s) for s in sizes}
    for s, im in icons.items():
        im.save(os.path.join(ASSETS, f"SaltlineIcon_{s}.png"), optimize=True)
    mark = render_mark(512)
    mark.save(os.path.join(ASSETS, "SaltlineMark.png"), optimize=True)
    with open(os.path.join(WEB, "icon.svg"), "w") as f:
        f.write(favicon_svg())
    with open(os.path.join(WEB, "logo-mark.svg"), "w") as f:
        f.write(mark_svg())
    print("wrote", len(sizes), "icon sizes, the menu mark and two SVGs")

    if a.preview:
        sheet = Image.new("RGBA", (1700, 1160), (0, 0, 0, 0))
        for i, bg in enumerate([(236, 236, 238, 255), (30, 30, 32, 255)]):
            panel = Image.new("RGBA", (1700, 580), bg)
            panel.alpha_composite(icons[1024].resize((512, 512), Image.LANCZOS), (20, 34))
            x = 560
            for s in [256, 128, 64, 48, 32, 16]:
                panel.alpha_composite(icons[s], (x, 290 - s // 2))
                x += s + 40
            panel.alpha_composite(mark.resize((mark.width * 200 // 512, 200), Image.LANCZOS), (1450, 190))
            sheet.alpha_composite(panel, (0, i * 580))
        sheet.save(a.preview)
        print("preview:", a.preview)


if __name__ == "__main__":
    main()
