"""
Kappa Saltworks: detailed props and building dressing, used by kappa_build.py.

kappa_build.py runs this file into its own globals (Geo, G, box, cylinder, EX/EY/EZ, Vector ... are shared),
so everything here produces the same kind of named mesh groups and follows the same naming rules.

Shapes are built in a local Frame (u forward, v left, z up) so one generator serves both team sides and any
orientation. Edges that catch light are chamfered (cbox), because a perfectly sharp box edge is the main thing
that makes blockout geometry read as blockout.
"""
import math

from mathutils import Vector, noise


class Frame:
    """Local axes: f (forward, horizontal), l (left), EZ (up), placed at o."""

    def __init__(self, o, yaw=0.0):
        self.o = Vector((o[0], o[1], o[2] if len(o) > 2 else 0.0))
        self.f = Vector((math.cos(yaw), math.sin(yaw), 0.0))
        self.l = Vector((-math.sin(yaw), math.cos(yaw), 0.0))

    def p(self, u, v, z):
        return self.o + self.f * u + self.l * v + EZ * z

    def d(self, u, v, z=0.0):
        return self.f * u + self.l * v + EZ * z

    @property
    def axes(self):
        return (self.f, self.l, EZ)


WORLD = Frame((0, 0, 0), 0.0)


# =========================================================================== primitives

def _oct(ua, ub, va, vb, c):
    return [(ub, va + c), (ub, vb - c), (ub - c, vb), (ua + c, vb), (ua, vb - c), (ua, va + c), (ua + c, va), (ub - c, va)]


def fbox(g, fr, u0, u1, v0, v1, z0, z1, skip=()):
    """Plain box in a frame. skip: '-z', '+z', '-u', '+u', '-v', '+v'."""
    ua, ub = sorted((u0, u1))
    va, vb = sorted((v0, v1))
    za, zb = sorted((z0, z1))
    if ub - ua < 1e-6 or vb - va < 1e-6 or zb - za < 1e-6:
        return
    P = fr.p
    faces = {
        "+u": ([P(ub, va, za), P(ub, vb, za), P(ub, vb, zb), P(ub, va, zb)], fr.f),
        "-u": ([P(ua, va, za), P(ua, va, zb), P(ua, vb, zb), P(ua, vb, za)], -fr.f),
        "+v": ([P(ua, vb, za), P(ua, vb, zb), P(ub, vb, zb), P(ub, vb, za)], fr.l),
        "-v": ([P(ua, va, za), P(ub, va, za), P(ub, va, zb), P(ua, va, zb)], -fr.l),
        "+z": ([P(ua, va, zb), P(ub, va, zb), P(ub, vb, zb), P(ua, vb, zb)], EZ),
        "-z": ([P(ua, va, za), P(ua, vb, za), P(ub, vb, za), P(ub, va, za)], -EZ),
    }
    for key, (pts, want) in faces.items():
        if key not in skip:
            g.poly(pts, axes=fr.axes, want=want)


def cbox(g, fr, u0, u1, v0, v1, z0, z1, b=0.02, bottom=False, top=True):
    """Box with its vertical and top edges chamfered by b (bottom edges stay sharp: things stand on something)."""
    ua, ub = sorted((u0, u1))
    va, vb = sorted((v0, v1))
    za, zb = sorted((z0, z1))
    du, dv, dz = ub - ua, vb - va, zb - za
    if min(du, dv, dz) < 1e-6:
        return
    b = min(b, 0.3 * min(du, dv), 0.45 * dz)
    if b < 0.003:
        fbox(g, fr, ua, ub, va, vb, za, zb, skip=() if bottom else ("-z",))
        return
    P = fr.p
    cu, cv = (ua + ub) / 2, (va + vb) / 2
    O = _oct(ua, ub, va, vb, b)
    zt = zb - b if top else zb
    for i in range(8):
        (u1_, v1_), (u2_, v2_) = O[i], O[(i + 1) % 8]
        out = fr.d((u1_ + u2_) / 2 - cu, (v1_ + v2_) / 2 - cv)
        g.poly([P(u1_, v1_, za), P(u2_, v2_, za), P(u2_, v2_, zt), P(u1_, v1_, zt)], axes=fr.axes, want=out)
    if top:
        I = _oct(ua + b, ub - b, va + b, vb - b, b * 0.586)
        for i in range(8):
            o1, o2, i1, i2 = O[i], O[(i + 1) % 8], I[i], I[(i + 1) % 8]
            out = fr.d((o1[0] + o2[0]) / 2 - cu, (o1[1] + o2[1]) / 2 - cv)
            g.poly([P(o1[0], o1[1], zt), P(o2[0], o2[1], zt), P(i2[0], i2[1], zb), P(i1[0], i1[1], zb)],
                   axes=fr.axes, want=out.normalized() + EZ)
        g.poly([P(u, v, zb) for u, v in I], axes=fr.axes, want=EZ)
    else:
        g.poly([P(u, v, zb) for u, v in O], axes=fr.axes, want=EZ)
    if bottom:
        g.poly([P(u, v, za) for u, v in O], axes=fr.axes, want=-EZ)


def wbox(g, x0, x1, y0, y1, z0, z1, b=0.02, bottom=False):
    """World-aligned chamfered box."""
    cbox(g, WORLD, x0, x1, y0, y1, z0, z1, b=b, bottom=bottom)


def prism(g, o, A, B, D, pts, d0, d1, caps=(True, True), axes=None):
    """Convex polygon pts [(a, b)] in the plane (A, B) through o, extruded along D from d0 to d1."""
    o = Vector(o)
    area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
    if area < 0:
        pts = list(reversed(pts))
    axes = axes or (A, D, B)

    def W(a, b, d):
        return o + A * a + B * b + D * d

    n = len(pts)
    for i in range(n):
        p, q = pts[i], pts[(i + 1) % n]
        want = A * (q[1] - p[1]) + B * (-(q[0] - p[0]))
        g.poly([W(p[0], p[1], d0), W(q[0], q[1], d0), W(q[0], q[1], d1), W(p[0], p[1], d1)], axes=axes, want=want)
    if caps[0]:
        g.poly([W(a, b, d0) for a, b in pts], axes=axes, want=-D)
    if caps[1]:
        g.poly([W(a, b, d1) for a, b in pts], axes=axes, want=D)


def lathe(g, centre, axis, prof, seg=16):
    """Surface of revolution. prof [(radius, offset along axis)] runs over the outside of the solid; the
    normal is the profile tangent turned towards the outside."""
    c, t = Vector(centre), Vector(axis).normalized()
    n1 = EX if abs(t.dot(EX)) < 0.9 else EY
    n1 = (n1 - t * n1.dot(t)).normalized()
    n2 = t.cross(n1)
    su, sv = g.uvscale
    rmax = max(r for r, _ in prof)
    tiles = max(1, round(2 * math.pi * rmax / su))
    acc = [0.0]
    for i in range(1, len(prof)):
        acc.append(acc[-1] + math.hypot(prof[i][0] - prof[i - 1][0], prof[i][1] - prof[i - 1][1]))

    def at(r, s, a):
        return c + t * s + (n1 * math.cos(a) + n2 * math.sin(a)) * r

    for k in range(len(prof) - 1):
        (ra, sa), (rb, sb) = prof[k], prof[k + 1]
        if ra < 1e-6 and rb < 1e-6:
            continue
        dt, dr = sb - sa, rb - ra
        for i in range(seg):
            a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
            am = (a0 + a1) / 2
            radial = n1 * math.cos(am) + n2 * math.sin(am)
            want = t * (-dr) + radial * dt
            u0, u1 = tiles * i / seg, tiles * (i + 1) / seg
            v0, v1 = acc[k] / sv, acc[k + 1] / sv
            if ra < 1e-6:
                g.poly([at(0, sa, am), at(rb, sb, a1), at(rb, sb, a0)], uvs=[((u0 + u1) / 2, v0), (u1, v1), (u0, v1)], want=want)
            elif rb < 1e-6:
                g.poly([at(ra, sa, a0), at(ra, sa, a1), at(0, sb, am)], uvs=[(u0, v0), (u1, v0), ((u0 + u1) / 2, v1)], want=want)
            else:
                g.poly([at(ra, sa, a0), at(ra, sa, a1), at(rb, sb, a1), at(rb, sb, a0)],
                       uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)], want=want)


def tube(g, pts, r, seg=8):
    """Bent pipe along a polyline (open ends)."""
    for i in range(len(pts) - 1):
        cylinder(g, pts[i], pts[i + 1], r, seg=seg, caps=(False, False))


def pillow(g, fr, cu, cv, z0, hu, hv, hz, n=5, p=5.0):
    """Filled bag: a rounded box (superellipsoid) with a flat bottom standing on z0. No bottom faces."""
    flat = -0.86
    span = 1.0 - flat

    def shape(q):
        s = (abs(q.x) ** p + abs(q.y) ** p + abs(q.z) ** p) ** (1.0 / p)
        q = q / s
        z = max(q.z, flat)
        return fr.p(cu + q.x * hu, cv + q.y * hv, z0 + (z - flat) / span * 2 * hz)

    centre = fr.p(cu, cv, z0 + hz)
    for k in range(3):
        for s in (-1, 1):
            if k == 2 and s < 0:
                continue
            a, b = [j for j in range(3) if j != k]
            grid = []
            for i in range(n + 1):
                row = []
                for j in range(n + 1):
                    q = [0.0, 0.0, 0.0]
                    q[k] = s
                    q[a] = -1 + 2 * i / n
                    q[b] = -1 + 2 * j / n
                    row.append(shape(Vector(q)))
                grid.append(row)
            for i in range(n):
                for j in range(n):
                    face = [grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]
                    if (face[0] - face[2]).length < 1e-5:
                        continue
                    mid = sum(face, Vector()) / 4
                    g.poly(face, axes=fr.axes, want=mid - centre)


def catenary(p0, p1, sag, n=10):
    p0, p1 = Vector(p0), Vector(p1)
    return [p0.lerp(p1, i / n) - EZ * (4 * sag * (i / n) * (1 - i / n)) for i in range(n + 1)]


def blob(g, cx, cy, z, r, seed, n=18):
    """Irregular flat patch (puddles)."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        k = 1 + 0.32 * noise.noise(Vector((math.cos(a) * 1.3 + seed, math.sin(a) * 1.3, seed * 0.7)))
        pts.append(Vector((cx + math.cos(a) * r * k, cy + math.sin(a) * r * k * 0.75, z)))
    g.poly(pts, want=(0, 0, 1))


def yaw_of(dx, dy):
    return math.atan2(dy, dx)


# =========================================================================== small props

def pallet(g, fr, cu, cv, z0, L=1.2, W=1.0):
    """Wooden pallet (0.144 m): bottom boards, nine blocks, stringer boards and a slatted top deck."""
    hu, hv = L / 2, W / 2
    for vv in (-hv + 0.05, 0.0, hv - 0.05):
        fbox(g, fr, cu - hu, cu + hu, cv + vv - 0.05, cv + vv + 0.05, z0, z0 + 0.022)
        for uu in (-hu + 0.07, 0.0, hu - 0.07):
            fbox(g, fr, cu + uu - 0.07, cu + uu + 0.07, cv + vv - 0.05, cv + vv + 0.05, z0 + 0.022, z0 + 0.1, skip=("-z",))
    for uu in (-hu + 0.07, 0.0, hu - 0.07):
        fbox(g, fr, cu + uu - 0.07, cu + uu + 0.07, cv - hv, cv + hv, z0 + 0.1, z0 + 0.122, skip=("-z",))
    for k in range(5):
        vv = -hv + 0.07 + k * (W - 0.14) / 4
        fbox(g, fr, cu - hu, cu + hu, cv + vv - 0.065, cv + vv + 0.065, z0 + 0.122, z0 + 0.144)


def bulk_bags(prefix, x0, x1, y0, y1, z0, h, rz=0.0):
    """Woven bulk bags on pallets filling the footprint (replaces the old textured sack boxes; same size)."""
    xa, xb = sorted((x0, x1))
    ya, yb = sorted((y0, y1))
    fr = Frame(((xa + xb) / 2, (ya + yb) / 2, 0.0), rz)
    w, d = xb - xa, yb - ya
    nu, nv = max(1, round(w / 1.05)), max(1, round(d / 1.05))
    layers = max(1, round(h / 1.2))
    lh = h / layers
    cw, cd = w / nu, d / nv
    bags = G(prefix + "_Bags", "M_Bag")
    pal = G(prefix + "_Pallets", "M_Wood")
    loops = G(prefix + "_BagLoops", "M_Bag", "nc")
    for layer in range(layers):
        zl = z0 + layer * lh
        for i in range(nu):
            for j in range(nv):
                cu = -w / 2 + (i + 0.5) * cw
                cv = -d / 2 + (j + 0.5) * cd
                pallet(pal, fr, cu, cv, zl, L=cw - 0.03, W=cd - 0.03)
                bh = (lh - 0.144) / 2
                hu, hv = cw / 2 - 0.035, cd / 2 - 0.035
                pillow(bags, fr, cu, cv, zl + 0.144, hu, hv, bh)
                top = zl + 0.144 + 2 * bh - 0.03
                for su in (-1, 1):
                    for sv in (-1, 1):
                        a = fr.p(cu + su * (hu - 0.12), cv + sv * (hv - 0.1), top - 0.04)
                        b = a + EZ * 0.13
                        c2 = fr.p(cu + su * (hu - 0.12), cv + sv * (hv - 0.24), top + 0.09)
                        d2 = c2 - EZ * 0.13
                        tube(loops, [a, b, c2, d2], 0.012, seg=5)


def framed_crate(g, x, y, z, s=1.2, rz=0.0):
    """Timber crate: a panelled core inside a frame of chamfered edge boards."""
    fr = Frame((x, y, z), rz)
    h, e, i = s / 2, 0.09, 0.022
    fbox(g, fr, -h + i, h - i, -h + i, h - i, 0, s - i, skip=("-z",))
    for su in (-1, 1):
        for sv in (-1, 1):
            cbox(g, fr, su * h, su * (h - e), sv * h, sv * (h - e), 0, s, b=0.008)
    for z0, z1 in ((0, e), (s - e, s)):
        for sv in (-1, 1):
            cbox(g, fr, -h + e, h - e, sv * h, sv * (h - e), z0, z1, b=0.008)
        for su in (-1, 1):
            cbox(g, fr, su * h, su * (h - e), -h + e, h - e, z0, z1, b=0.008)
    for sv in (-1, 1):
        cbox(g, fr, -h + e, h - e, sv * h, sv * (h - i - 0.012), s / 2 - 0.05, s / 2 + 0.05, b=0.006)
    for su in (-1, 1):
        cbox(g, fr, su * h, su * (h - i - 0.012), -h + e, h - e, s / 2 - 0.05, s / 2 + 0.05, b=0.006)


def drum(body, rings, x, y, z=0.0):
    vcyl(body, x, y, z, z + 0.88, 0.29, seg=16, caps=(False, True))
    for zr in (0.29, 0.59):
        vcyl(rings, x, y, z + zr, z + zr + 0.035, 0.302, seg=16, caps=(False, True))
    vcyl(rings, x, y, z + 0.86, z + 0.9, 0.296, seg=16, caps=(False, False))
    vcyl(rings, x + 0.12, y + 0.08, z + 0.88, z + 0.9, 0.03, seg=6)


def ibc(prefix, x, y, rz=0.0):
    """Intermediate bulk container: plastic tank in a steel cage on a steel pallet (1.2 x 1.0 x 1.15 m)."""
    fr = Frame((x, y, 0.0), rz)
    fbox(G(prefix + "_IBCBase", "M_PaintGrey"), fr, -0.6, 0.6, -0.5, 0.5, 0.0, 0.14, skip=("-z",))
    cbox(G(prefix + "_IBCTank", "M_Plastic"), fr, -0.57, 0.57, -0.47, 0.47, 0.14, 1.1, b=0.07)
    lathe(G(prefix + "_IBCCage", "M_Plate", "nc"), fr.p(0, 0, 1.1), EZ, [(0.16, 0.0), (0.16, 0.06), (0.0, 0.06)], seg=12)
    cage = G(prefix + "_IBCCage", "M_Plate", "nc")
    corners = [fr.p(su * 0.59, sv * 0.49, 0.0) for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    for zc in (0.16, 0.45, 0.8, 1.13):
        ring = [c + EZ * zc for c in corners]
        tube(cage, ring + [ring[0]], 0.011, seg=5)
    for k in range(4):
        a, b = corners[k], corners[(k + 1) % 4]
        for t in (0.0, 0.25, 0.5, 0.75):
            p = a.lerp(b, t)
            cylinder(cage, p + EZ * 0.14, p + EZ * 1.13, 0.011, seg=5, caps=(False, False))


def cable_drum(g, x, y, rz=0.0, r=0.6, w=0.7):
    fr = Frame((x, y, 0.0), rz)
    for s in (-1, 1):
        lathe(g, fr.p(0, s * (w / 2 - 0.03), r), fr.l * s, [(0.0, -0.03), (r, -0.03), (r, 0.03), (0.0, 0.03)], seg=20)
    cylinder(g, fr.p(0, -w / 2 + 0.03, r), fr.p(0, w / 2 - 0.03, r), r * 0.62, seg=16, caps=(False, False))


def cone(body, band, x, y):
    fbox(body, WORLD, x - 0.19, x + 0.19, y - 0.19, y + 0.19, 0.0, 0.03, skip=("-z",))
    lathe(body, (x, y, 0.03), EZ, [(0.15, 0.0), (0.035, 0.68), (0.0, 0.68)], seg=12)
    lathe(band, (x, y, 0.33), EZ, [(0.103, 0.0), (0.081, 0.13)], seg=12)


def pallet_stack(g, x, y, n, rz=0.0):
    fr = Frame((x, y, 0.0), rz)
    for k in range(n):
        pallet(g, fr, 0.0, 0.0, k * 0.145, L=1.2, W=1.0)


# =========================================================================== containers

def container(prefix, paint, cx, cy, yaw, L=6.06, W=2.44, H=2.59, door=1):
    """ISO container: corner posts, side and end rails, a recessed corrugated shell (the scan's ribs), twin
    doors with locking bars, hinges, forklift pockets and corner castings."""
    fr = Frame((cx, cy, 0.0), yaw)
    body = G(prefix, paint)
    hw_ = G(prefix + "_Hardware", "M_Metal", "nc")
    dark = G(prefix + "_Dark", "M_Dark", "nc")
    hl, hv = L / 2, W / 2
    post = 0.16
    for su in (-1, 1):
        for sv in (-1, 1):
            cbox(body, fr, su * hl, su * (hl - post), sv * hv, sv * (hv - post), 0.0, H, b=0.012)
    for sv in (-1, 1):
        cbox(body, fr, -hl + post, hl - post, sv * hv, sv * (hv - 0.1), 0.0, 0.2, b=0.01)
        cbox(body, fr, -hl + post, hl - post, sv * hv, sv * (hv - 0.1), H - 0.13, H, b=0.01)
    for su in (-1, 1):
        cbox(body, fr, su * hl, su * (hl - 0.12), -hv + post, hv - post, 0.0, 0.2, b=0.01)
        cbox(body, fr, su * hl, su * (hl - 0.12), -hv + post, hv - post, H - 0.21, H, b=0.01)
    fbox(body, fr, -hl + 0.07, hl - 0.07, -hv + 0.04, hv - 0.04, 0.2, H - 0.05, skip=("-z",))
    # doors on the +u end (door = -1 puts them on the other end)
    s = door
    du0, du1 = s * (hl - 0.1), s * (hl - 0.045)
    for v0, v1 in ((-hv + post + 0.005, -0.006), (0.006, hv - post - 0.005)):
        cbox(body, fr, du0, du1, v0, v1, 0.2, H - 0.21, b=0.006)
    fbox(dark, fr, s * (hl - 0.075), s * (hl - 0.068), -0.008, 0.008, 0.2, H - 0.21)
    for vb in (-hv + post + 0.32, -0.3, 0.3, hv - post - 0.32):
        uu = s * (hl - 0.02)
        cylinder(hw_, fr.p(uu, vb, 0.16), fr.p(uu, vb, H - 0.16), 0.022, seg=8)
        for zk in (0.26, H - 0.3):
            fbox(hw_, fr, s * (hl - 0.05), s * (hl - 0.005), vb - 0.045, vb + 0.045, zk, zk + 0.08)
        side = 1 if vb > 0 else -1
        side = -side if abs(vb) < 0.5 else side
        fbox(hw_, fr, s * (hl - 0.045), s * (hl - 0.008), vb, vb + side * 0.2, 1.05, 1.1)
    for sv in (-1, 1):
        for zh in (0.45, 1.05, 1.65, 2.1):
            fbox(dark, fr, s * (hl - 0.06), s * (hl - 0.025), sv * (hv - post - 0.03), sv * (hv - post + 0.04), zh, zh + 0.12)
    # forklift pockets and casting holes
    for sv in (-1, 1):
        vv = sv * (hv + 0.003)
        for uc in (-1.0, 1.0):
            pts = [fr.p(uc - 0.18, vv, 0.04), fr.p(uc + 0.18, vv, 0.04), fr.p(uc + 0.18, vv, 0.15), fr.p(uc - 0.18, vv, 0.15)]
            dark.poly(pts, axes=fr.axes, want=fr.l * sv)
        for su in (-1, 1):
            for zc in (0.05, H - 0.09):
                uc = su * (hl - 0.09)
                pts = [fr.p(uc - 0.045, vv, zc), fr.p(uc + 0.045, vv, zc), fr.p(uc + 0.045, vv, zc + 0.04), fr.p(uc - 0.045, vv, zc + 0.04)]
                dark.poly(pts, axes=fr.axes, want=fr.l * sv)


# =========================================================================== vehicles

def wheel(tyre, rim, hub, centre, axis, r, tw=0.38):
    """Tyre with rounded shoulders, a recessed rim and a hub. axis points outwards from the vehicle."""
    a = Vector(axis).normalized()
    rr = r * 0.6
    lathe(tyre, centre, a, [(rr, -tw / 2), (r - 0.07, -tw / 2), (r - 0.012, -tw / 2 + 0.03), (r, -tw / 2 + 0.07),
                            (r, tw / 2 - 0.07), (r - 0.012, tw / 2 - 0.03), (r - 0.07, tw / 2), (rr, tw / 2)], seg=20)
    lathe(rim, centre, a, [(rr, tw / 2), (rr - 0.02, tw / 2 - 0.05), (0.0, tw / 2 - 0.05)], seg=20)
    lathe(rim, centre, -a, [(rr, tw / 2), (rr - 0.02, tw / 2 - 0.05), (0.0, tw / 2 - 0.05)], seg=20)
    lathe(hub, Vector(centre) + a * (tw / 2 - 0.05), a, [(0.17, 0.0), (0.15, 0.06), (0.08, 0.1), (0.0, 0.1)], seg=12)


def truck(base, side, ox, oy, yaw, L, W, r, axles, cab_len, cab_mat, body, body_mat, z_body):
    """Cab-over truck. Frame u runs from the back (0) to the bumper (L); body is 'hopper' or 'dump'.
    Groups are named <base>_<part>_<side> (Truck_Cab_W): the map tests match those names."""

    def N(part):
        return f"{base}_{part}_{side}"

    fr = Frame((ox, oy, 0.0), yaw)
    hw = W / 2
    c0 = L - cab_len
    dark = G(N("Chassis"), "M_Dark")
    tyre = G(N("Tyres"), "M_Rubber")
    rim = G(N("Wheels"), "M_PaintGrey", "nc")
    hub = G(N("Hubs"), "M_Dark", "nc")
    glass = G(N("Glass"), "M_Glass", "nc")
    trim = G(N("Trim"), "M_Dark", "nc")
    metal = G(N("Metal"), "M_Metal", "nc")
    cab = G(N("Cab"), cab_mat)

    # chassis rails, subframe, wheels and mudguards
    for sv in (-1, 1):
        cbox(dark, fr, 0.2, L - 0.25, sv * 0.32, sv * 0.46, z_body - 0.34, z_body - 0.1, b=0.01, bottom=True)
    cbox(dark, fr, 0.05, c0 - 0.05, -hw + 0.12, hw - 0.12, z_body - 0.1, z_body, b=0.01, bottom=True)
    for ua in axles:
        for sv in (-1, 1):
            wheel(tyre, rim, hub, fr.p(ua, sv * (hw + 0.1), r), fr.l * sv, r)
    rear = [ua for ua in axles if ua < c0 - 0.3]
    if rear:
        u0, u1 = min(rear) - r - 0.12, max(rear) + r + 0.12
        for sv in (-1, 1):
            cbox(dark, fr, u0, u1, sv * (hw - 0.12), sv * (hw + 0.32), 2 * r + 0.1, 2 * r + 0.13, b=0.004, bottom=True)
            for ue in (u0, u1 - 0.03):
                fbox(dark, fr, ue, ue + 0.03, sv * (hw - 0.12), sv * (hw + 0.32), 2 * r - 0.2, 2 * r + 0.1)

    # fuel tank, battery box, exhaust
    fu1 = min([ua - r - 0.08 for ua in axles if ua > c0 - 0.3] + [c0 - 0.3])
    fu0 = max([ua + r + 0.08 for ua in rear] + [0.3])
    if fu1 - fu0 > 0.4:
        cylinder(G(N("Tank"), "M_Metal"), fr.p(fu0, hw - 0.32, z_body - 0.48), fr.p(fu1, hw - 0.32, z_body - 0.48), 0.26, seg=14)
        for uu in (fu0 + 0.12, fu1 - 0.12):
            cylinder(trim, fr.p(uu, hw - 0.32, z_body - 0.48), fr.p(uu + 0.04, hw - 0.32, z_body - 0.48), 0.27, seg=14, caps=(False, False))
        cbox(dark, fr, fu0, min(fu1, fu0 + 0.7), -hw + 0.06, -hw + 0.55, z_body - 0.8, z_body - 0.25, b=0.01, bottom=True)
    cylinder(metal, fr.p(c0 - 0.14, -hw + 0.25, z_body - 0.2), fr.p(c0 - 0.14, -hw + 0.25, 3.45), 0.065, seg=10)

    # cab
    zc = r + 0.28
    prof = [(c0, zc), (L - 0.03, zc), (L, zc + 0.15), (L, 1.95), (L - 0.22, 2.95), (c0 + 0.04, 3.0), (c0, 2.96)]
    prism(cab, fr.p(0, 0, 0), fr.f, EZ, fr.l, prof, -hw, hw, axes=fr.axes)
    n = (fr.f * 1.0 + EZ * 0.22).normalized()
    for t0, t1 in ((0.07, 0.93),):
        a, b = fr.p(L, 0, 1.95), fr.p(L - 0.22, 0, 2.95)
        pa, pb = a.lerp(b, t0), a.lerp(b, t1)
        glass.poly([pa + fr.l * (-hw + 0.13) + n * 0.008, pa + fr.l * (hw - 0.13) + n * 0.008,
                    pb + fr.l * (hw - 0.13) + n * 0.008, pb + fr.l * (-hw + 0.13) + n * 0.008], axes=fr.axes, want=n)
    for sv in (-1, 1):
        vv = sv * (hw + 0.006)
        za, zb = 1.95, 2.78
        glass.poly([fr.p(c0 + 0.14, vv, za), fr.p(L - 0.12, vv, za), fr.p(L - 0.12 - 0.22 * (zb - za), vv, zb), fr.p(c0 + 0.14, vv, zb)],
                   axes=fr.axes, want=fr.l * sv)
        for uu in (c0 + 0.07, L - 0.07):
            trim.poly([fr.p(uu - 0.007, vv, zc + 0.12), fr.p(uu + 0.007, vv, zc + 0.12), fr.p(uu + 0.007, vv, 2.88), fr.p(uu - 0.007, vv, 2.88)],
                      axes=fr.axes, want=fr.l * sv)
        fbox(trim, fr, L - 0.75, L - 0.6, sv * hw, sv * (hw + 0.025), 1.78, 1.82)
        # mirror arm and head
        cylinder(trim, fr.p(L - 0.12, sv * (hw - 0.02), 2.6), fr.p(L - 0.06, sv * (hw + 0.27), 2.62), 0.018, seg=6)
        cbox(trim, fr, L - 0.1, L - 0.01, sv * (hw + 0.22), sv * (hw + 0.34), 2.22, 2.78, b=0.015, bottom=True)
        # steps
        for zs in (zc - 0.42, zc - 0.05):
            fbox(metal, fr, c0 + 0.3, c0 + 0.75, sv * (hw - 0.14), sv * (hw + 0.02), zs, zs + 0.04)
        # headlights and indicators
        cbox(glass, fr, L - 0.02, L + 0.02, sv * (hw - 0.4), sv * (hw - 0.1), zc + 0.24, zc + 0.4, b=0.01, bottom=True)
        fbox(G(N("Lamps"), "M_PaintOrange", "nc"), fr, L - 0.02, L + 0.015, sv * (hw - 0.09), sv * (hw - 0.01), zc + 0.24, zc + 0.36)
    trim.poly([fr.p(L + 0.006, -0.62, zc + 0.45), fr.p(L + 0.006, 0.62, zc + 0.45), fr.p(L + 0.006, 0.62, zc + 1.05), fr.p(L + 0.006, -0.62, zc + 1.05)],
              axes=fr.axes, want=fr.f)
    for k in range(4):
        zz = zc + 0.52 + k * 0.15
        fbox(metal, fr, L, L + 0.03, -0.6, 0.6, zz, zz + 0.04)
    cbox(G(N("Bumper"), "M_PaintGrey"), fr, L - 0.03, L + 0.15, -hw - 0.02, hw + 0.02, zc - 0.15, zc + 0.18, b=0.02, bottom=True)
    lathe(G(N("Lamps"), "M_PaintOrange", "nc"), fr.p(c0 + 0.45, 0, 3.0), EZ, [(0.09, 0.0), (0.08, 0.12), (0.0, 0.14)], seg=10)

    # body
    if body == "hopper":
        bm = G(N("Hopper"), body_mat)
        top = 3.4
        sec = [(-0.75, z_body), (0.75, z_body), (hw, top), (-hw, top)]
        prism(bm, fr.p(0, 0, 0), fr.l, EZ, fr.f, sec, 0.0, c0 - 0.15, axes=fr.axes)
        for sv in (-1, 1):
            cbox(bm, fr, 0.0, c0 - 0.15, sv * (hw - 0.07), sv * (hw + 0.03), top - 0.05, top + 0.08, b=0.012, bottom=True)
            nv = Vector((sv * (top - z_body), -(hw - 0.75))).normalized()
            p0, p1 = (sv * 0.75, z_body + 0.05), (sv * hw, top - 0.05)
            rib = [p0, p1, (p1[0] + nv.x * 0.06, p1[1] + nv.y * 0.06), (p0[0] + nv.x * 0.06, p0[1] + nv.y * 0.06)]
            for uu in (0.55, 1.75, 2.95, 4.15):
                if uu + 0.12 < c0 - 0.15:
                    prism(bm, fr.p(0, 0, 0), fr.l, EZ, fr.f, rib, uu, uu + 0.12, axes=fr.axes)
        lad = G(N("Ladder"), "M_Metal", "nc")
        for sv in (-1, 1):
            cylinder(lad, fr.p(-0.05, sv * 0.22, z_body - 0.6), fr.p(-0.05, sv * 0.22, top + 0.4), 0.02, seg=6)
        for k in range(8):
            zz = z_body - 0.45 + k * 0.33
            cylinder(lad, fr.p(-0.05, -0.22, zz), fr.p(-0.05, 0.22, zz), 0.016, seg=6, caps=(False, False))
    else:
        bm = G(N("Bed"), body_mat)
        top = 3.2
        sec = [(-hw + 0.1, z_body), (hw - 0.1, z_body), (hw, top), (-hw, top)]
        prism(bm, fr.p(0, 0, 0), fr.l, EZ, fr.f, sec, 0.05, c0 - 0.1, axes=fr.axes)
        cbox(bm, fr, c0 - 0.12, c0 + 0.55, -hw, hw, top - 0.05, top + 0.06, b=0.012, bottom=True)
        for sv in (-1, 1):
            cbox(bm, fr, 0.05, c0 - 0.1, sv * (hw - 0.06), sv * (hw + 0.04), top - 0.05, top + 0.08, b=0.012, bottom=True)
            for uu in (0.4, 1.3, 2.2, 3.1):
                if uu + 0.1 < c0 - 0.1:
                    cbox(bm, fr, uu, uu + 0.1, sv * (hw - 0.02), sv * (hw + 0.05), z_body + 0.05, top - 0.05, b=0.01)
        cbox(bm, fr, -0.06, 0.08, -hw - 0.03, hw + 0.03, z_body - 0.05, top - 0.15, b=0.015, bottom=True)


def harvester(side, ox, oy, yaw, paint):
    """Tracked salt harvester: two tracks, body, engine deck, glazed cab and a front blade. The body top is
    1.5 m above the tracks, more than a jump, so the machine cannot be climbed (as before)."""

    def N(part):
        return f"Harvester_{part}_{side}"

    fr = Frame((ox, oy, 0.0), yaw)
    track = G(N("Tracks"), "M_Rubber")
    rr = 0.4
    pts = []
    for k in range(7):
        a = math.pi / 2 + math.pi * k / 6
        pts.append((rr + rr * math.cos(a), rr + rr * math.sin(a)))
    for k in range(7):
        a = -math.pi / 2 + math.pi * k / 6
        pts.append((3.5 - rr + rr * math.cos(a), rr + rr * math.sin(a)))
    for v0, v1 in ((-1.2, -0.6), (0.6, 1.2)):
        prism(track, fr.p(0, 0, 0), fr.f, EZ, fr.l, pts, v0, v1, axes=fr.axes)
    hubs = G(N("Hubs"), "M_Dark", "nc")
    for sv in (-1, 1):
        for uu in (0.4, 1.3, 2.2, 3.1):
            lathe(hubs, fr.p(uu, sv * 1.2, 0.4), fr.l * sv, [(0.22, 0.0), (0.2, 0.03), (0.0, 0.03)], seg=12)
    body = G(N("Body"), paint)
    cbox(body, fr, 0.1, 3.4, -1.15, 1.15, 0.8, 2.3, b=0.05, bottom=True)
    cbox(body, fr, 0.15, 1.45, -0.85, 0.85, 2.3, 2.6, b=0.04)
    for su in (1.6, 3.2):
        for sv in (-1, 1):
            cbox(body, fr, su - 0.04, su + 0.04, sv * 0.85, sv * 0.77, 2.3, 3.0, b=0.01)
    cbox(body, fr, 1.5, 3.3, -0.95, 0.95, 3.0, 3.1, b=0.02, bottom=True)
    glass = G(N("Glass"), "M_Glass")
    fbox(glass, fr, 3.17, 3.19, -0.77, 0.77, 2.33, 2.97)
    fbox(glass, fr, 1.62, 1.64, -0.77, 0.77, 2.33, 2.97)
    for sv in (-1, 1):
        fbox(glass, fr, 1.64, 3.16, sv * 0.82, sv * 0.8, 2.33, 2.97)
    cylinder(G(N("Exhaust"), "M_Metal", "nc"), fr.p(0.6, 0.5, 2.6), fr.p(0.6, 0.5, 3.3), 0.06, seg=8)
    blade = [(3.45, 0.25), (4.55, 0.2), (4.6, 0.32), (3.6, 1.05), (3.45, 1.05)]
    prism(G(N("Scoop"), "M_Rust", "nc"), fr.p(0, 0, 0), fr.f, EZ, fr.l, blade, -1.0, 1.0, axes=fr.axes)
    for sv in (-1, 1):
        cbox(G(N("Arms"), "M_Dark", "nc"), fr, 3.0, 3.6, sv * 0.9, sv * 1.0, 0.7, 0.85, b=0.01, bottom=True)


# =========================================================================== tanks, lamps, poles, fences

def tank_details(name, x, y, r, h):
    """Caged ladder facing the map's middle, roof handrail, manway and a base nozzle."""
    th = math.atan2(-y, -x) if abs(x) + abs(y) > 1e-3 else 0.0
    n = Vector((math.cos(th), math.sin(th), 0.0))
    t = Vector((-n.y, n.x, 0.0))
    c = Vector((x, y, 0.0))
    lad = G(name + "_Ladder", "M_Plate", "nc")
    base = c + n * (r + 0.22)
    top = h + 1.0
    for s in (-1, 1):
        cylinder(lad, base + t * (0.22 * s) + EZ * 0.3, base + t * (0.22 * s) + EZ * top, 0.022, seg=6)
    zz = 0.55
    while zz < top - 0.1:
        cylinder(lad, base - t * 0.22 + EZ * zz, base + t * 0.22 + EZ * zz, 0.016, seg=6, caps=(False, False))
        zz += 0.3
    zb = 1.2
    while zb < h:
        for s in (-1, 1):
            cylinder(lad, base + t * (0.22 * s) + EZ * zb, c + n * (r + 0.01) + t * (0.22 * s) + EZ * zb, 0.015, seg=5, caps=(False, False))
        zb += 1.5
    hoop_c = base + n * 0.12
    arc = [hoop_c + t * (0.36 * math.cos(a)) + n * (0.36 * math.sin(a)) for a in (math.pi * k / 8 for k in range(9))]
    zh = 2.4
    while zh < top:
        tube(lad, [p + EZ * zh for p in arc], 0.012, seg=5)
        zh += 0.75
    for k in (1, 3, 4, 5, 7):
        p = arc[k]
        cylinder(lad, p + EZ * 2.4, p + EZ * min(top, zh - 0.75), 0.01, seg=5, caps=(False, False))
    rail = G(name + "_RoofRail", "M_PaintYellow", "nc")
    rr = r - 0.12
    seg = max(12, int(2 * math.pi * rr / 1.0))
    ring = [c + Vector((math.cos(2 * math.pi * k / seg), math.sin(2 * math.pi * k / seg), 0)) * rr for k in range(seg + 1)]
    zr = h + 0.28 * r * (0.12 / r) + 0.02
    for k in range(seg):
        cylinder(rail, ring[k] + EZ * zr, ring[k] + EZ * (zr + 1.05), 0.024, seg=6, caps=(False, True))
    tube(rail, [p + EZ * (zr + 1.05) for p in ring], 0.025, seg=6)
    tube(rail, [p + EZ * (zr + 0.55) for p in ring], 0.018, seg=6)
    am = th + math.radians(70)
    m = Vector((math.cos(am), math.sin(am), 0.0))
    cylinder(G(name + "_Manway", "M_TankPaint"), c + m * (r - 0.05) + EZ * 1.0, c + m * (r + 0.28) + EZ * 1.0, 0.32, seg=18)
    cylinder(G(name + "_Flange", "M_Metal", "nc"), c + m * (r + 0.28) + EZ * 1.0, c + m * (r + 0.33) + EZ * 1.0, 0.39, seg=18)
    an = th - math.radians(60)
    q = Vector((math.cos(an), math.sin(an), 0.0))
    noz = G(name + "_Nozzle", "M_Rust", "nc")
    tube(noz, [c + q * (r - 0.05) + EZ * 0.75, c + q * (r + 0.55) + EZ * 0.75, c + q * (r + 0.55) - EZ * 0.05], 0.12, seg=10)
    vcyl(G(name + "_Vent", "M_Metal", "nc"), x, y, h + 0.28 * r - 0.05, h + 0.28 * r + 0.35, 0.14, seg=10)


def street_lamp(post, head, lens, x, y, dx, dy, h=7.0):
    lathe(post, (x, y, 0.0), EZ, [(0.13, 0.0), (0.11, 0.25), (0.085, h - 0.4), (0.07, h), (0.0, h)], seg=10)
    fbox(post, WORLD, x - 0.2, x + 0.2, y - 0.2, y + 0.2, 0.0, 0.03, skip=("-z",))
    d = Vector((dx, dy, 0.0)).normalized()
    tip = Vector((x, y, h - 0.25)) + d * 1.25 + EZ * 0.3
    cylinder(post, (x, y, h - 0.35), tip, 0.045, seg=8)
    fr = Frame((tip.x, tip.y, tip.z), math.atan2(d.y, d.x))
    cbox(head, fr, -0.1, 0.62, -0.16, 0.16, -0.14, 0.02, b=0.03, bottom=True)
    lens.poly([fr.p(0.0, -0.12, -0.145), fr.p(0.55, -0.12, -0.145), fr.p(0.55, 0.12, -0.145), fr.p(0.0, 0.12, -0.145)], want=-EZ)


def power_line(poles, height=10.0, sag=1.1):
    """Wooden poles with a crossarm and three insulated conductors hanging between them (backdrop)."""
    pg = G("PowerPoles", "M_Wood", "far")
    hw = G("PowerHardware", "M_Dark", "far")
    wires = G("PowerWires", "M_Dark", "far")
    tops = []
    for i, (x, y, z) in enumerate(poles):
        lathe(pg, (x, y, z - 0.5), EZ, [(0.17, 0.0), (0.12, height + 0.5), (0.0, height + 0.55)], seg=8)
        if i + 1 < len(poles):
            nx, ny = poles[i + 1][0] - x, poles[i + 1][1] - y
        else:
            nx, ny = x - poles[i - 1][0], y - poles[i - 1][1]
        fr = Frame((x, y, z + height - 0.4), math.atan2(ny, nx))
        cbox(hw, fr, -0.06, 0.06, -1.3, 1.3, 0.0, 0.12, b=0.01, bottom=True)
        pts = []
        for v in (-1.15, 0.0, 1.15):
            base = fr.p(0, v, 0.12) if v else fr.p(0, v, 0.55)
            if not v:
                cylinder(hw, fr.p(0, 0, 0.12), base, 0.05, seg=6)
            lathe(hw, base, EZ, [(0.06, 0.0), (0.07, 0.06), (0.05, 0.12), (0.07, 0.18), (0.0, 0.2)], seg=8)
            pts.append(base + EZ * 0.2)
        tops.append(pts)
    for i in range(len(tops) - 1):
        for k in range(3):
            tube(wires, catenary(tops[i][k], tops[i + 1][k], sag, n=12), 0.014, seg=4)


def palisade(g, x0, x1, y, z, h=1.9, pitch=0.22):
    """Steel palisade fence: triple-pointed pales on two angle rails, posts every 2.75 m."""
    n = int((x1 - x0) / pitch)
    for i in range(n + 1):
        x = x0 + i * pitch
        pts = [(x - 0.034, z), (x + 0.034, z), (x + 0.034, z + h - 0.12), (x + 0.017, z + h - 0.06), (x, z + h),
               (x - 0.017, z + h - 0.06), (x - 0.034, z + h - 0.12)]
        prism(g, (0, 0, 0), EX, EZ, EY, pts, y - 0.006, y + 0.006, axes=(EX, EY, EZ))
    for zr in (z + 0.3, z + h - 0.35):
        fbox(g, WORLD, x0, x1, y + 0.006, y + 0.05, zr, zr + 0.045)
    xp = x0
    while xp <= x1 + 1e-6:
        fbox(g, WORLD, xp - 0.05, xp + 0.05, y + 0.05, y + 0.15, z, z + h - 0.1)
        xp += 2.75


# =========================================================================== buildings

def coping(g, axis, s0, s1, t0, t1, z, gaps=(), over=0.035, h=0.1):
    """Precast cap on a wall top (axis 'x' or 'y' along the wall; t0..t1 is its thickness), split at gaps."""
    s0, s1 = sorted((s0, s1))
    cuts = [(s0, s1)]
    for a, b in gaps:
        a, b = sorted((a, b))
        nxt = []
        for p, q in cuts:
            if b <= p or a >= q:
                nxt.append((p, q))
                continue
            if a > p:
                nxt.append((p, a))
            if b < q:
                nxt.append((b, q))
        cuts = nxt
    ta, tb = sorted((t0, t1))
    for p, q in cuts:
        if q - p < 0.05:
            continue
        if axis == "x":
            wbox(g, p, q, ta - over, tb + over, z, z + h, b=0.018, bottom=True)
        else:
            wbox(g, ta - over, tb + over, p, q, z, z + h, b=0.018, bottom=True)


def window(prefix, axis, plane, out, s0, s1, z0, z1, mullion=True):
    """Window on a flat facade: glass just proud of the wall, a frame, a mullion and a concrete sill.
    axis: 'x' if the facade runs along x (plane is a y value), 'y' if it runs along y (plane is an x value).
    out: +1/-1, the outward side of the plane."""
    glass = G(prefix + "_Glass", "M_Glass", "nc")
    frame = G(prefix + "_Frames", "M_PaintGrey", "nc")
    sill = G(prefix + "_Sills", "M_ConcreteCast", "nc")

    def B(g, a0, a1, d0, d1, za, zb, b=0.0):
        p0, p1 = plane + out * d0, plane + out * d1
        if axis == "x":
            if b:
                wbox(g, a0, a1, p0, p1, za, zb, b=b, bottom=True)
            else:
                fbox(g, WORLD, a0, a1, p0, p1, za, zb)
        else:
            if b:
                wbox(g, p0, p1, a0, a1, za, zb, b=b, bottom=True)
            else:
                fbox(g, WORLD, p0, p1, a0, a1, za, zb)

    pz = plane + out * 0.004
    if axis == "x":
        pts = [Vector((s0, pz, z0)), Vector((s1, pz, z0)), Vector((s1, pz, z1)), Vector((s0, pz, z1))]
        glass.poly(pts, want=(0, out, 0))
    else:
        pts = [Vector((pz, s0, z0)), Vector((pz, s1, z0)), Vector((pz, s1, z1)), Vector((pz, s0, z1))]
        glass.poly(pts, want=(out, 0, 0))
    f = 0.06
    B(frame, s0 - f, s1 + f, 0.0, 0.05, z1, z1 + f)
    B(frame, s0 - f, s1 + f, 0.0, 0.05, z0 - f, z0)
    B(frame, s0 - f, s0, 0.0, 0.05, z0, z1)
    B(frame, s1, s1 + f, 0.0, 0.05, z0, z1)
    if mullion and s1 - s0 > 1.1:
        m = (s0 + s1) / 2
        B(frame, m - 0.03, m + 0.03, 0.0, 0.04, z0, z1)
    B(frame, s0, s1, 0.0, 0.035, z0 + (z1 - z0) * 0.62, z0 + (z1 - z0) * 0.62 + 0.04)
    B(sill, s0 - 0.12, s1 + 0.12, 0.0, 0.11, z0 - f - 0.07, z0 - f, b=0.012)


def door(prefix, axis, plane, out, s0, s1, h=2.1):
    """Steel door with frame, kick plate and handle, flush with a facade."""
    leaf = G(prefix + "_Doors", "M_PaintGrey", "nc")
    frame = G(prefix + "_Frames", "M_PaintGrey", "nc")
    dark = G(prefix + "_DoorTrim", "M_Dark", "nc")

    def B(g, a0, a1, d0, d1, za, zb):
        p0, p1 = plane + out * d0, plane + out * d1
        if axis == "x":
            fbox(g, WORLD, a0, a1, p0, p1, za, zb)
        else:
            fbox(g, WORLD, p0, p1, a0, a1, za, zb)

    B(leaf, s0, s1, 0.0, 0.025, 0.0, h)
    B(frame, s0 - 0.07, s0, 0.0, 0.05, 0.0, h + 0.07)
    B(frame, s1, s1 + 0.07, 0.0, 0.05, 0.0, h + 0.07)
    B(frame, s0 - 0.07, s1 + 0.07, 0.0, 0.05, h, h + 0.07)
    B(dark, s0 + 0.05, s1 - 0.05, 0.025, 0.03, 0.05, 0.3)
    B(dark, s1 - 0.24, s1 - 0.1, 0.025, 0.07, 1.0, 1.04)
    B(G(prefix + "_Canopy", "M_PaintGrey", "nc"), s0 - 0.35, s1 + 0.35, 0.0, 0.7, h + 0.25, h + 0.32)


def downpipe(g, x, y, z1, dx=0.0, dy=0.0):
    """Rain pipe: from the gutter (offset dx, dy out from the wall) in to the wall, down, and out at a shoe."""
    pts = [Vector((x + dx, y + dy, z1)), Vector((x + dx, y + dy, z1 - 0.25)), Vector((x, y, z1 - 0.55)), Vector((x, y, 0.25)),
           Vector((x + dx * 1.2, y + dy * 1.2, 0.05))]
    tube(g, pts, 0.055, seg=8)


def facade_plinth(g, x0, x1, y0, y1, h=0.35, d=0.03):
    """Base course round a building footprint (decor only: 3 cm)."""
    wbox(g, x0 - d, x1 + d, y0 - d, y0, 0.0, h, b=0.01)
    wbox(g, x0 - d, x1 + d, y1, y1 + d, 0.0, h, b=0.01)
    wbox(g, x0 - d, x0, y0, y1, 0.0, h, b=0.01)
    wbox(g, x1, x1 + d, y0, y1, 0.0, h, b=0.01)


def drain(g_grate, g_frame, x, y, s=0.6):
    fbox(g_frame, WORLD, x - s / 2 - 0.06, x + s / 2 + 0.06, y - s / 2 - 0.06, y + s / 2 + 0.06, 0.0, 0.004, skip=("-z",))
    fbox(g_grate, WORLD, x - s / 2, x + s / 2, y - s / 2, y + s / 2, 0.0, 0.007, skip=("-z",))


def manhole(g_cover, g_ring, x, y):
    lathe(g_ring, (x, y, 0.0), EZ, [(0.42, 0.0), (0.42, 0.004), (0.0, 0.004)], seg=20)
    lathe(g_cover, (x, y, 0.0), EZ, [(0.34, 0.0), (0.34, 0.008), (0.0, 0.008)], seg=20)
