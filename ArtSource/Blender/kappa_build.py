"""
Kappa Saltworks — procedural map generator for Blender (5.x).

Builds the whole playable map as real meshes from code, so the layout is reviewable,
repeatable and original. Coordinates: X = east, Y = north, Z = up, metres.
Red spawns in the west (X < 0), Blue in the east; the map is mirror-symmetric in X.

Object naming tells the Unity importer what to do:
    <name>          visible + mesh collider, surface type from the material
    <name>__NC      visible, no collider (decor)
    <name>__FAR     visible, no collider, no shadow casting (backdrop)
    <name>__PC      invisible player clip (blocks movement, not bullets)
    SPAWN_RED_nn / SPAWN_BLUE_nn / LIGHT_nn   empties used as markers

Run inside Blender with SALTLINE_ROOT set (globals or environment), e.g.
    SALTLINE_ROOT = "/path/to/Saltline"; exec(open(SALTLINE_ROOT + "/ArtSource/Blender/kappa_build.py").read())
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector, noise

ROOT = globals().get("SALTLINE_ROOT") or os.environ.get("SALTLINE_ROOT")
assert ROOT, "SALTLINE_ROOT is not set"
TEX_DIR = os.path.join(ROOT, "Assets", "_Saltline", "Art", "Maps", "Kappa", "Textures")
FBX_PATH = os.path.join(ROOT, "Assets", "_Saltline", "Art", "Maps", "Kappa", "Kappa.fbx")
BLEND_PATH = os.path.join(ROOT, "ArtSource", "Blender", "kappa.blend")
DO_EXPORT = globals().get("KAPPA_EXPORT", True)

# material -> texture, UV metres per tile (u, v), tint
MATS = {
    "M_Asphalt": ("Asphalt", (6, 6), (1, 1, 1)),
    "M_Concrete": ("Concrete", (4, 4), (1, 1, 1)),
    "M_ConcreteFloor": ("ConcreteFloor", (4, 4), (1, 1, 1)),
    "M_Facade": ("Facade", (4, 3.6), (1, 1, 1)),
    "M_Salt": ("Salt", (5, 5), (1, 1, 1)),
    "M_Sacks": ("Sacks", (1.2, 1.2), (1, 1, 1)),
    "M_Metal": ("Metal", (2, 2), (1, 1, 1)),
    "M_Plate": ("Metal", (2, 2), (1.5, 1.4, 1.25)),
    "M_Grate": ("Grate", (1, 1), (1, 1, 1)),
    "M_TankPaint": ("TankPaint", (4, 4), (1, 1, 1)),
    "M_Rust": ("Rust", (2, 2), (1, 1, 1)),
    "M_Corrugated": ("Corrugated", (2, 2), (1, 1, 1)),
    "M_Container": ("Container", (2.24, 2.24), (1, 1, 1)),
    "M_Wood": ("Wood", (1.5, 1.5), (1, 1, 1)),
    "M_Rock": ("Rock", (6, 6), (1, 1, 1)),
    "M_Hazard": ("Hazard", (1, 1), (1, 1, 1)),
    "M_PaintYellow": ("Paint", (2, 2), (0.86, 0.62, 0.1)),
    "M_PaintRed": ("Paint", (2, 2), (0.72, 0.13, 0.09)),
    "M_PaintBlue": ("Paint", (2, 2), (0.1, 0.32, 0.72)),
    "M_Dark": ("Paint", (1, 1), (0.07, 0.07, 0.07)),
    "M_Brine": ("Water", (4, 4), (0.86, 0.64, 0.68)),
    "M_Sea": ("Water", (24, 24), (0.06, 0.17, 0.22)),
    "M_Lamp": (None, (1, 1), (1.0, 0.86, 0.62)),
    "M_Clip": (None, (1, 1), (1.0, 0.0, 1.0)),
}

KIND_SUFFIX = {"geo": "", "nc": "__NC", "far": "__FAR", "clip": "__PC"}
KIND_COLL = {"geo": "KAPPA_GEO", "nc": "KAPPA_DECOR", "far": "KAPPA_FAR", "clip": "KAPPA_CLIP"}


# =========================================================================== geometry core

def newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i, p in enumerate(pts):
        q = pts[(i + 1) % len(pts)]
        n.x += (p.y - q.y) * (p.z + q.z)
        n.y += (p.z - q.z) * (p.x + q.x)
        n.z += (p.x - q.x) * (p.y + q.y)
    return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))


EX, EY, EZ = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


class Geo:
    def __init__(self, name, mat, kind):
        self.name, self.mat, self.kind = name, mat, kind
        self.verts, self.faces, self.uvs = [], [], []
        self.uvmul = 1.0

    @property
    def uvscale(self):
        su, sv = MATS[self.mat][1]
        return su * self.uvmul, sv * self.uvmul

    def poly(self, pts, axes=(EX, EY, EZ), uvs=None, want=None):
        pts = [Vector(p) for p in pts]
        n = newell(pts)
        if want is not None and n.dot(Vector(want)) < 0:
            pts.reverse()
            if uvs is not None:
                uvs = list(reversed(uvs))
            n = -n
        if uvs is None:
            su, sv = self.uvscale
            ex, ey, ez = axes
            ax, ay, az = abs(n.dot(ex)), abs(n.dot(ey)), abs(n.dot(ez))
            if az >= ax and az >= ay:
                uvs = [(p.dot(ex) / su, p.dot(ey) / sv) for p in pts]
            elif ax >= ay:
                uvs = [(p.dot(ey) / su, p.dot(ez) / sv) for p in pts]
            else:
                uvs = [(p.dot(ex) / su, p.dot(ez) / sv) for p in pts]
        base = len(self.verts)
        self.verts.extend(pts)
        self.faces.append(tuple(range(base, base + len(pts))))
        self.uvs.append(uvs)


REG = {}


def G(name, mat, kind="geo", uvmul=None):
    g = REG.get(name)
    if g is None:
        g = REG[name] = Geo(name, mat, kind)
    if uvmul is not None:
        g.uvmul = uvmul
    assert g.mat == mat and g.kind == kind, f"{name}: one material/kind per object"
    return g


SIGNS = {
    "+x": [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)],
    "-x": [(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)],
    "+y": [(-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)],
    "-y": [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)],
    "+z": [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)],
    "-z": [(-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1)],
}


def box(g, c, size, rz=0.0, skip=()):
    c = Vector(c)
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    cs, sn = math.cos(rz), math.sin(rz)
    ex, ey = Vector((cs, sn, 0)), Vector((-sn, cs, 0))
    for key, corners in SIGNS.items():
        if key in skip:
            continue
        g.poly([c + ex * (a * hx) + ey * (b * hy) + EZ * (d * hz) for a, b, d in corners], axes=(ex, ey, EZ))


def boxr(g, x0, x1, y0, y1, z0, z1, skip=()):
    xa, xb = sorted((x0, x1))
    ya, yb = sorted((y0, y1))
    za, zb = sorted((z0, z1))
    if xb - xa < 1e-6 or yb - ya < 1e-6 or zb - za < 1e-6:
        return
    box(g, ((xa + xb) / 2, (ya + yb) / 2, (za + zb) / 2), (xb - xa, yb - ya, zb - za), skip=skip)


def wall(g, axis, s0, s1, t0, t1, z0, z1, openings=()):
    """Wall running along `axis` ('x' or 'y') over s0..s1, thickness t0..t1, with
    rectangular openings (s_a, s_b, z_a, z_b)."""
    s0, s1 = sorted((s0, s1))
    ops = [(min(a, b), max(a, b), za, zb) for a, b, za, zb in openings]
    cuts = {s0, s1}
    for a, b, _, _ in ops:
        cuts.add(max(s0, min(s1, a)))
        cuts.add(max(s0, min(s1, b)))
    cuts = sorted(cuts)
    for i in range(len(cuts) - 1):
        a, b = cuts[i], cuts[i + 1]
        if b - a < 1e-6:
            continue
        m = (a + b) / 2
        spans = [(z0, z1)]
        for o in ops:
            if not (o[0] <= m <= o[1]):
                continue
            nxt = []
            for p, q in spans:
                if o[3] <= p or o[2] >= q:
                    nxt.append((p, q))
                    continue
                if o[2] > p:
                    nxt.append((p, o[2]))
                if o[3] < q:
                    nxt.append((o[3], q))
            spans = nxt
        for p, q in spans:
            if axis == "x":
                boxr(g, a, b, t0, t1, p, q)
            else:
                boxr(g, t0, t1, a, b, p, q)


def steps(g, axis, start, d, l0, l1, zfloor, top0, dz, n, run=0.35):
    """n solid steps along axis from `start` in direction d (+1/-1). Step i top = top0 + dz*i."""
    for i in range(n):
        a, b = start + d * i * run, start + d * (i + 1) * run
        top = top0 + dz * i
        if axis == "x":
            boxr(g, a, b, l0, l1, zfloor, top, skip=("-z",))
        else:
            boxr(g, l0, l1, a, b, zfloor, top, skip=("-z",))


def wedge(g, x0, x1, y0, y1, z0, z1, up):
    """Solid ramp over the rectangle; the top rises from z0 to z1 towards `up` ('+x','-x','+y','-y')."""
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))

    def h(x, y):
        t = {"+x": (x - x0) / (x1 - x0), "-x": (x1 - x) / (x1 - x0),
             "+y": (y - y0) / (y1 - y0), "-y": (y1 - y) / (y1 - y0)}[up]
        return z0 + (z1 - z0) * t

    c = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    top = [Vector((x, y, h(x, y))) for x, y in c]
    g.poly(top, want=(0, 0, 1))
    for i in range(4):
        (xa, ya), (xb, yb) = c[i], c[(i + 1) % 4]
        ha, hb = h(xa, ya), h(xb, yb)
        out = Vector((yb - ya, -(xb - xa), 0))  # outward for counter-clockwise c
        if ha - z0 < 1e-6 and hb - z0 < 1e-6:
            continue
        pts = [Vector((xa, ya, z0)), Vector((xb, yb, z0))]
        if hb - z0 > 1e-6:
            pts.append(Vector((xb, yb, hb)))
        if ha - z0 > 1e-6:
            pts.append(Vector((xa, ya, ha)))
        g.poly(pts, want=out)


def cylinder(g, p0, p1, r0, r1=None, seg=24, caps=(True, True)):
    """Cylinder/cone along p0 -> p1 with cylindrical UVs (seamless around the circumference)."""
    p0, p1 = Vector(p0), Vector(p1)
    r1 = r0 if r1 is None else r1
    t = (p1 - p0)
    length = t.length
    t.normalize()
    n1 = EX if abs(t.dot(EX)) < 0.9 else EY
    n1 = (n1 - t * n1.dot(t)).normalized()
    n2 = t.cross(n1)
    su, sv = g.uvscale
    tiles = max(1, round(2 * math.pi * max(r0, r1) / su))
    v0 = p0.dot(t) / sv
    v1 = v0 + length / sv

    def ring(p, r, a):
        return p + n1 * (r * math.cos(a)) + n2 * (r * math.sin(a))

    for i in range(seg):
        a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
        u0, u1 = tiles * i / seg, tiles * (i + 1) / seg
        if r1 > 1e-6:
            g.poly([ring(p0, r0, a0), ring(p0, r0, a1), ring(p1, r1, a1), ring(p1, r1, a0)],
                   uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        else:
            g.poly([ring(p0, r0, a0), ring(p0, r0, a1), p1], uvs=[(u0, v0), (u1, v0), ((u0 + u1) / 2, v1)])
    axes = (n1, n2, t)
    if caps[1] and r1 > 1e-6:
        g.poly([ring(p1, r1, 2 * math.pi * i / seg) for i in range(seg)], axes=axes)
    if caps[0] and r0 > 1e-6:
        g.poly([ring(p0, r0, 2 * math.pi * i / seg) for i in reversed(range(seg))], axes=axes)


def vcyl(g, x, y, z0, z1, r, r_top=None, seg=24, caps=(False, True)):
    cylinder(g, (x, y, z0), (x, y, z1), r, r_top, seg, caps)


def salt_pile(g, cx, cy, r, h, seed, seg=28, rings=10):
    """Lumpy cone. Kept steeper than the 46 degree slope limit so it is cover, not a hill."""
    pts = []
    for k in range(rings + 1):
        t = k / rings
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            d = Vector((math.cos(a), math.sin(a), 0))
            lump = 1 + 0.12 * noise.noise(Vector((d.x * 1.7 + seed, d.y * 1.7, t * 2.0)))
            rr = r * (1 - t) ** 0.92 * lump
            z = h * t
            row.append(Vector((cx, cy, 0)) + d * rr + Vector((0, 0, z)))
        pts.append(row)
    apex = Vector((cx, cy, h))
    su, sv = g.uvscale
    for k in range(rings):
        for i in range(seg):
            j = (i + 1) % seg
            if k == rings - 1:
                g.poly([pts[k][i], pts[k][j], apex], want=(pts[k][i] + pts[k][j]) / 2 - Vector((cx, cy, 0)))
            else:
                face = [pts[k][i], pts[k][j], pts[k + 1][j], pts[k + 1][i]]
                c = sum(face, Vector()) / 4
                g.poly(face, want=c - Vector((cx, cy, c.z - 1.0)))


def rock(g, center, radii, seed, subdiv=2, amp=0.28):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    c = Vector(center)
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in bm.verts:
        d = v.co.normalized()
        disp = 1 + amp * noise.noise(d * 1.6 + off) + amp * 0.45 * noise.noise(d * 3.7 + off)
        v.co = c + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * disp
    for f in bm.faces:
        g.poly([l.vert.co.copy() for l in f.loops])
    bm.free()


def grid_surface(g, nx, ny, fn, want):
    """Height-field style surface: fn(i, j) -> Vector for i in 0..nx, j in 0..ny."""
    P = [[fn(i, j) for j in range(ny + 1)] for i in range(nx + 1)]
    for i in range(nx):
        for j in range(ny):
            g.poly([P[i][j], P[i + 1][j], P[i + 1][j + 1], P[i][j + 1]], want=want)


# =========================================================================== props

def crate(g, x, y, z, s=1.2, rz=0.0):
    box(g, (x, y, z + s / 2), (s, s, s), rz=rz, skip=("-z",))


def sacks(g, x0, x1, y0, y1, z0, h):
    boxr(g, x0, x1, y0, y1, z0, z0 + h, skip=("-z",))


def jersey(g, x0, x1, y0, y1):
    """1.1 m concrete barrier (tapered profile approximated with two boxes)."""
    along_x = (abs(x1 - x0) > abs(y1 - y0))
    boxr(g, x0, x1, y0, y1, 0, 0.35, skip=("-z",))
    if along_x:
        ym = (y0 + y1) / 2
        boxr(g, x0, x1, ym - 0.15, ym + 0.15, 0.35, 1.1, skip=("-z",))
    else:
        xm = (x0 + x1) / 2
        boxr(g, xm - 0.15, xm + 0.15, y0, y1, 0.35, 1.1, skip=("-z",))


def railing(name, p0, p1, z, height=1.05, clip_h=1.45):
    """Visual railing (no collision: bullets pass) plus an invisible player clip."""
    p0, p1 = Vector((p0[0], p0[1], z)), Vector((p1[0], p1[1], z))
    d = p1 - p0
    length = d.length
    n = max(1, math.ceil(length / 1.5))
    g = G(name + "_Rail", "M_PaintYellow", "nc")
    rz = math.atan2(d.y, d.x)
    for i in range(n + 1):
        p = p0 + d * (i / n)
        box(g, (p.x, p.y, z + height / 2), (0.06, 0.06, height), rz=rz)
    mid = (p0 + p1) / 2
    box(g, (mid.x, mid.y, z + height - 0.025), (length, 0.05, 0.05), rz=rz)
    box(g, (mid.x, mid.y, z + 0.5), (length, 0.035, 0.035), rz=rz)
    c = G(name + "_Clip", "M_Clip", "clip")
    box(c, (mid.x, mid.y, z + clip_h / 2), (length, 0.12, clip_h), rz=rz)


def tank(name, x, y, r, h, plinth=True):
    if plinth:
        vcyl(G(name + "_Plinth", "M_Concrete"), x, y, 0, 0.3, r + 0.3, caps=(False, True))
    vcyl(G(name + "_Shell", "M_TankPaint"), x, y, 0.3, h, r, caps=(False, False), seg=32)
    vcyl(G(name + "_Roof", "M_TankPaint"), x, y, h, h + r * 0.28, r, r_top=0.35, seg=32, caps=(False, True))
    bands = G(name + "_Bands", "M_Metal", "nc")
    for zb in (h * 0.33, h * 0.66):
        vcyl(bands, x, y, zb, zb + 0.14, r + 0.035, seg=32, caps=(False, False))
    vcyl(G(name + "_Bands", "M_Metal", "nc"), x, y, h - 0.1, h + 0.02, r + 0.05, seg=32, caps=(False, True))


def lamp(i, x, y, z):
    boxr(G("Lamps", "M_Lamp", "nc"), x - 0.35, x + 0.35, y - 0.12, y + 0.12, z, z + 0.08)
    marker(f"LIGHT_{i:02d}", (x, y, z - 0.15))


MARKERS = []


def marker(name, pos):
    MARKERS.append((name, Vector(pos)))


# =========================================================================== layout

def build_center():
    # ---- channel through the salt pans (sunken 1.2 m)
    boxr(G("Channel_Floor", "M_ConcreteFloor"), -1.5, 1.5, 13.0, 36.0, -1.7, -1.2, skip=("-z",))
    boxr(G("Channel_Brine", "M_Brine", "nc"), -1.5, 1.5, 14.05, 34.6, -1.2, -1.16, skip=("-z",))
    steps(G("Channel_Stairs", "M_Concrete"), "y", 13.0, +1, -1.5, 1.5, -1.7, -0.3, -0.3, 3)
    steps(G("Channel_Stairs", "M_Concrete"), "y", 36.0, -1, -1.5, 1.5, -1.7, 0.0, -0.3, 4)
    # footbridge: crouch clearance (1.25 m) for players walking in the channel
    boxr(G("Channel_Bridge", "M_Grate"), -2.2, 2.2, 23.2, 24.8, 0.05, 0.2)
    # sluice gate: pillars on the banks, beam and a raised gate over the channel
    boxr(G("Sluice", "M_Concrete"), -2.9, 2.9, 19.6, 20.4, 2.7, 3.2)
    boxr(G("Sluice_Gate", "M_Rust"), -1.5, 1.5, 19.85, 20.15, 1.4, 2.7)

    # ---- pump house: interior floor, pump, annex, south alcove divider
    boxr(G("Pump_Floor", "M_ConcreteFloor"), -7.6, 7.6, -6.6, 6.6, 0.0, 0.1, skip=("-z",))
    boxr(G("Pump_Machine", "M_Metal"), -2.2, 2.2, -2.8, 2.8, 0.1, 2.5, skip=("-z",))
    boxr(G("Pump_Housing", "M_Metal"), -1.5, 1.5, -1.5, 1.5, 3.7, 4.8, skip=("-z",))
    pipes = G("Pump_Pipes", "M_Rust")
    for yy in (-2.2, 2.2):
        vcyl(pipes, 0.0, yy, 2.5, 7.1, 0.3, caps=(False, False))
    for xx in (-1.6, 1.6):
        cylinder(pipes, (xx, 2.8, 1.0), (xx, 6.6, 1.0), 0.25, caps=(False, False))
    flywheel = G("Pump_Flywheel", "M_Dark", "nc")
    cylinder(flywheel, (-2.35, 0, 1.4), (-2.2, 0, 1.4), 0.9, seg=28)
    cylinder(flywheel, (2.2, 0, 1.4), (2.35, 0, 1.4), 0.9, seg=28)
    boxr(G("Pump_Crates", "M_Wood"), -0.7, 0.7, 3.6, 5.0, 0.1, 2.5, skip=("-z",))
    boxr(G("Pump_Annex", "M_Concrete"), -3.0, 3.0, 7.0, 9.0, 0.0, 3.0, skip=("-z",))
    boxr(G("Pump_AnnexDoor", "M_Metal", "nc"), -0.7, 0.7, 9.0, 9.04, 0.0, 2.2)
    boxr(G("Alcove_Divider", "M_Concrete"), -0.25, 0.25, -11.6, -7.0, 0.0, 3.3, skip=("-z",))

    # ---- north boundary: retaining wall over the full width
    boxr(G("Boundary_North", "M_Concrete"), -58.0, 58.0, 36.0, 37.0, -1.7, 3.0)
    boxr(G("Boundary_North_Clip", "M_Clip", "clip"), -58.0, 58.0, 35.75, 36.0, 0.0, 14.0)
    boxr(G("Boundary_South_Clip", "M_Clip", "clip"), -58.0, 58.0, -36.0, -35.75, 0.0, 14.0)

    # ---- catwalk hut over the channel
    boxr(G("Catwalk_Deck", "M_Grate"), -2.5, 2.5, 30.5, 34.5, 2.85, 3.0)
    hut = G("Hut_Walls", "M_Corrugated")
    wall(hut, "x", -2.5, 2.5, 30.5, 30.6, 3.0, 5.6, [(-1.6, 1.6, 4.1, 5.0)])
    wall(hut, "x", -2.5, 2.5, 34.4, 34.5, 3.0, 5.6, [(-1.0, 1.0, 4.1, 5.0)])
    for s in (-1, 1):
        wall(hut, "y", 30.6, 34.4, s * 2.4, s * 2.5, 3.0, 5.6, [(31.6, 33.4, 3.0, 5.3)])
    boxr(G("Hut_Roof", "M_Metal"), -2.7, 2.7, 30.3, 34.7, 5.6, 5.75)
    boxr(G("Hut_Desk", "M_Metal"), -1.2, 1.2, 30.6, 31.2, 3.0, 4.0, skip=("-z",))
    lamp(0, 0.0, 32.5, 5.5)

    # ---- dock: raised platform, crane, centre control room
    boxr(G("Dock_Platform", "M_ConcreteFloor"), -14.0, 14.0, -36.0, -27.0, -0.5, 1.2, skip=("-z",))
    boxr(G("Dock_Edge", "M_Hazard", "nc"), -14.0, 14.0, -27.3, -27.0, 1.2, 1.206)
    boxr(G("Crane_Base", "M_PaintYellow"), -1.5, 1.5, -34.5, -31.5, 1.2, 4.2, skip=("-z",))
    crane = G("Crane_Boom", "M_PaintYellow", "nc")
    vcyl(crane, 0.0, -33.0, 4.2, 5.4, 1.1, seg=20, caps=(False, True))
    box(crane, (-2.0, -38.5, 7.0), (0.9, 12.5, 0.9), rz=math.radians(-20))
    box(crane, (0.0, -33.0, 6.2), (1.6, 2.6, 1.6))
    boxr(G("Control_Room", "M_Facade"), -4.5, 4.5, -16.0, -11.6, 0.0, 3.4, skip=("-z", "+z"))
    boxr(G("Control_Roof", "M_Concrete"), -4.7, 4.7, -16.2, -11.6, 3.4, 3.6)
    boxr(G("Seawall", "M_Concrete"), -14.0, 14.0, -36.5, -36.0, -1.0, 2.3)

    # ---- backdrop: cliff face, sea, hillside, sea stacks
    def cliff(i, j):
        x = -160 + i * 4.0
        z = 0.2 - j * 2.0
        y = -36.6 - (-z) * 0.32 - 2.2 * (noise.noise(Vector((x * 0.08, z * 0.11, 1.3))) + 0.6)
        if j == 0:
            y = -36.6
        return Vector((x, y, z))
    grid_surface(G("Cliff", "M_Rock", "far", uvmul=2.0), 80, 14, cliff, want=(0, -1, 0))

    def hill_z(x, y):
        """Hillside height: a rising slope broken up by two octaves of noise and a few gullies."""
        ramp = min(1.0, (y - 37.0) / 6.0)
        base = 3.0 + min(26.0, (y - 37.0) * 0.42)
        relief = (6.0 * noise.noise(Vector((x * 0.022, y * 0.03, 7.7)))
                  + 2.2 * noise.noise(Vector((x * 0.09, y * 0.11, 3.1)))
                  - 3.5 * max(0.0, 1.0 - abs(noise.noise(Vector((x * 0.018, 0.4, 9.2)))) * 6.0))
        return base + relief * ramp

    def hill(i, j):
        x = -160 + i * 4.0
        y = 37.0 + j * 4.0
        return Vector((x, y, 3.0 if j == 0 else hill_z(x, y)))
    grid_surface(G("Hillside", "M_Rock", "far", uvmul=3.0), 80, 22, hill, want=(0, 0, 1))
    boulders = G("HillRocks", "M_Rock", "far", uvmul=1.5)
    for k in range(34):
        bxp = -150 + k * 9.1 + 4.0 * noise.noise(Vector((k * 0.9, 1.1, 0.2)))
        byp = 46.0 + (k * 37 % 41) + 3.0 * noise.noise(Vector((k * 0.5, 3.3, 0.7)))
        r = 1.6 + 1.4 * (0.5 + 0.5 * noise.noise(Vector((k * 1.3, 0.2, 5.0))))
        rock(boulders, (bxp, byp, hill_z(bxp, byp) + r * 0.25), (r * 1.3, r, r * 0.8), seed=k + 60, subdiv=2)
    boxr(G("Sea", "M_Sea", "far"), -1500, 1500, -1500, -36.0, -26.5, -26.0, skip=("-z",))
    stacks = G("SeaStacks", "M_Rock", "far")
    for k, (x, y, r, h) in enumerate([(-70, -75, 9, 22), (-38, -95, 6, 15), (25, -82, 8, 19), (85, -70, 10, 26),
                                      (140, -120, 14, 30), (-130, -110, 12, 24), (0, -150, 9, 14)]):
        rock(stacks, (x, y, -26 + h * 0.45), (r, r * 0.8, h * 0.6), seed=k + 1, subdiv=3, amp=0.22)
    beach = G("CliffRocks", "M_Rock", "far")
    for k in range(26):
        x = -150 + k * 12 + 5 * noise.noise(Vector((k * 0.7, 0.3, 0.1)))
        rock(beach, (x, -48 - 3 * noise.noise(Vector((k * 0.3, 2.0, 0))), -26.0), (6, 4, 5), seed=k + 20, subdiv=2)


def build_side(sx):
    """Everything that exists once per team side. sx = -1 builds the west (Red), +1 the east (Blue)."""
    S = "W" if sx < 0 else "E"
    team = "RED" if sx < 0 else "BLUE"

    def bx(g, a0, a1, y0, y1, z0, z1, skip=()):
        boxr(g, sx * a0, sx * a1, y0, y1, z0, z1, skip=skip)

    def wx(g, a0, a1, y0, y1, z0, z1, ops=()):
        wall(g, "x", sx * a0, sx * a1, y0, y1, z0, z1, [(sx * p, sx * q, zp, zq) for p, q, zp, zq in ops])

    def wy(g, y0, y1, a0, a1, z0, z1, ops=()):
        wall(g, "y", y0, y1, sx * a0, sx * a1, z0, z1, ops)

    def X(a):
        return sx * a

    # ------------------------------------------------------------------ ground
    gnd = G("Ground_Asphalt_" + S, "M_Asphalt")
    bx(gnd, 0, 58, -27, 13, -0.5, 0, skip=("-z",))
    bx(gnd, 24, 58, 13, 36, -0.5, 0, skip=("-z",))
    bx(gnd, 14, 58, -36, -27, -0.5, 0, skip=("-z",))
    bx(G("Ground_Pans_" + S, "M_Salt"), 1.9, 24, 13, 36, -0.5, 0, skip=("-z",))
    bx(G("Channel_Wall_" + S, "M_Concrete"), 1.5, 1.9, 12.9, 36, -1.7, 0.05)

    # ------------------------------------------------------------------ boundary
    bx(G("Warehouse_Wall_" + S, "M_Corrugated"), 58, 58.4, -36.5, 37, 0, 13)
    bx(G("Warehouse_Clip_" + S, "M_Clip", "clip"), 57.75, 58, -36, 36, 0, 16)
    wh = G("Warehouse_Shell_" + S, "M_Corrugated", "far")
    bx(wh, 58.4, 84, -36.5, -36.1, 0, 13)
    bx(wh, 58.4, 84, 36.6, 37, 0, 13)
    roof = G("Warehouse_Roof_" + S, "M_Metal", "far")
    for y0, y1 in ((-36.5, 37.0),):
        ym = (y0 + y1) / 2
        hh = 3.5
        p = [Vector((X(58.0), y0, 13)), Vector((X(84.0), y0, 13)), Vector((X(84.0), ym, 13 + hh)), Vector((X(58.0), ym, 13 + hh))]
        roof.poly(p, want=(0, -0.6, 1))
        q = [Vector((X(58.0), ym, 13 + hh)), Vector((X(84.0), ym, 13 + hh)), Vector((X(84.0), y1, 13)), Vector((X(58.0), y1, 13))]
        roof.poly(q, want=(0, 0.6, 1))
        roof.poly([Vector((X(58.0), y0, 13)), Vector((X(58.0), ym, 13 + hh)), Vector((X(58.0), y1, 13))], want=(-sx, 0, 0))
    bx(G("Seawall_" + S, "M_Concrete"), 14, 58, -36.5, -36.0, -1.0, 1.1)
    posts = G("LampPosts_" + S, "M_Metal")
    for a in (20, 32, 44, 54):
        vcyl(posts, X(a), -35.8, 0.0, 7.0, 0.09, seg=10)
        box(posts, (X(a), -35.2, 7.0), (0.25, 1.4, 0.15))

    # ------------------------------------------------------------------ spawn yard (a 44..58, |y| < 12)
    yard = G("Yard_Walls_" + S, "M_Concrete")
    wx(yard, 38, 58, 12.0, 12.6, 0, 4.0, [(47.0, 50.5, 0, 3.0)])
    wx(yard, 38, 58, -12.6, -12.0, 0, 4.0, [(47.0, 50.5, 0, 3.0)])
    # Baffles continue the office line into the yard so no exit or door can see the spawns.
    bx(yard, 44.0, 52.5, 8.6, 9.0, 0, 2.8, skip=("-z",))
    bx(yard, 44.0, 52.5, -9.0, -8.6, 0, 2.8, skip=("-z",))
    paint = G("Yard_Paint_" + S, "M_Paint" + ("Red" if sx < 0 else "Blue"), "nc")
    bx(paint, 57.95, 58.0, -12.0, 12.0, 0.0, 1.4)
    bx(paint, 44.0, 44.05, -9.0, 9.0, 0.0, 1.4)
    bx(paint, 44.05, 52.5, 8.55, 8.6, 0.0, 1.0)
    bx(paint, 44.05, 52.5, -8.6, -8.55, 0.0, 1.0)
    bx(paint, 46.0, 56.0, -0.15, 0.15, 0.0, 0.006)
    canopy = G("Yard_Canopy_" + S, "M_Corrugated")
    bx(canopy, 52.0, 58.0, -10.0, 10.0, 4.2, 4.35)
    cols = G("Yard_Columns_" + S, "M_Metal")
    for yy in (-9.8, -3.3, 3.3, 9.8):
        bx(cols, 52.05, 52.35, yy - 0.15, yy + 0.15, 0, 4.2, skip=("-z",))
    lamp(1 if sx < 0 else 2, X(55.0), 0.0, 4.1)
    k = 0
    for a in (50.0, 54.0):
        for yy in (-4.5, -1.5, 1.5, 4.5):
            marker(f"SPAWN_{team}_{k:02d}", (X(a), yy, 0.05))
            k += 1
    # office block between the yard and the staging area
    bx(G("Office_" + S, "M_Facade"), 38, 44, -9, 9, 0, 7.2, skip=("-z", "+z"))
    bx(G("Office_Roof_" + S, "M_Concrete"), 37.8, 44.2, -9.2, 9.2, 7.2, 7.6)
    ac = G("Office_AC_" + S, "M_Metal", "nc")
    bx(ac, 40, 41.6, -4, -2.4, 7.6, 8.6)
    bx(ac, 40, 41.6, 2.0, 3.6, 7.6, 8.6)

    # ------------------------------------------------------------------ staging yard (a 24..44)
    truck = "Truck_" + S
    bx(G(truck + "_Chassis", "M_Dark"), 30.0, 32.0, 5.5, 13.0, 0.55, 1.2)
    wheels = G(truck + "_Wheels", "M_Dark")
    for yy in (6.4, 8.0, 12.0):
        for a in (29.6, 32.02):
            cylinder(wheels, (X(a), yy, 0.55), (X(a + 0.38), yy, 0.55), 0.55, seg=16)
    bx(G(truck + "_Hopper", "M_TankPaint"), 29.9, 32.1, 5.5, 10.6, 1.2, 3.4, skip=("-z",))
    bx(G(truck + "_Cab", "M_PaintYellow"), 29.9, 32.1, 11.0, 13.0, 1.2, 3.0, skip=("-z",))
    cont = G("Containers_" + S, "M_Container")
    bx(cont, 29.5, 31.94, -13.5, -7.4, 0, 2.6, skip=("-z",))
    bx(cont, 16.5, 18.94, -1.5, 4.6, 0, 2.6, skip=("-z",))
    bx(cont, 33.0, 39.1, -35.6, -33.16, 0, 2.6, skip=("-z",))
    jer = G("Barriers_" + S, "M_Concrete")
    jersey(jer, X(26), X(29), -2.3, -1.7)
    jersey(jer, X(10.5), X(13.5), 5.7, 6.3)
    jersey(jer, X(11.7), X(12.3), -6.5, -3.5)
    jersey(jer, X(42), X(45), -28.5, -27.9)
    bx(jer, 27.0, 29.4, -30.6, -29.4, 0, 1.2, skip=("-z",))
    sk = G("Sacks_" + S, "M_Sacks")
    sacks(sk, X(34.0), X(35.2), -3.0, -1.0, 0, 1.2)
    sacks(sk, X(21.0), X(22.2), -8.0, -6.0, 0, 1.2)
    sacks(sk, X(20.0), X(21.2), 7.5, 9.5, 0, 1.2)
    sacks(sk, X(32.0), X(33.2), -20.0, -18.0, 0, 1.2)
    sacks(sk, X(20.0), X(21.2), -31.0, -29.0, 0, 1.2)
    cr = G("Crates_" + S, "M_Wood")
    for (a, y, z) in [(27.1, 3.1, 0), (27.1, 3.1, 1.2), (12.6, 9.6, 0), (12.6, 9.6, 1.2), (13.8, 10.4, 0), (13.8, 10.4, 1.2),
                      (38.6, -16.4, 0), (38.6, -16.4, 1.2)]:
        crate(cr, X(a), y, z)
    # overhead pipe rack across the mid courtyard
    bx(G("PipeRack_Cols_" + S, "M_Metal"), 22.3, 22.7, -10.8, -10.4, 0, 5.0, skip=("-z",))
    bx(G("PipeRack_Cols_" + S, "M_Metal"), 22.3, 22.7, 10.4, 10.8, 0, 5.0, skip=("-z",))
    rack = G("PipeRack_" + S, "M_Rust", "nc")
    bx(rack, 22.2, 22.8, -10.8, 10.8, 4.6, 5.0)
    for k, a in enumerate((22.15, 22.85)):
        cylinder(rack, (X(a), -10.8, 5.25), (X(a), 10.8, 5.25), 0.22, seg=14, caps=(False, False))
    # weighbridge office (south-east of the yard)
    bx(G("Weighbridge_" + S, "M_Facade"), 47, 53, -22, -17, 0, 3.2, skip=("-z", "+z"))
    bx(G("Weighbridge_Roof_" + S, "M_Concrete"), 46.8, 53.2, -22.2, -16.8, 3.2, 3.4)
    # brine pump shed in the north staging area (breaks the open ground between tank C and the pans)
    bx(G("PumpShed_" + S, "M_Corrugated"), 28.5, 33.5, 28.0, 33.5, 0, 3.6, skip=("-z", "+z"))
    bx(G("PumpShed_Roof_" + S, "M_Metal"), 28.3, 33.7, 27.8, 33.7, 3.6, 3.75)
    crate(G("Crates_" + S, "M_Wood"), X(26.6), 19.6, 0)
    # dump truck on the cliff road
    dt = "DumpTruck_" + S
    bx(G(dt + "_Chassis", "M_Dark"), 46.0, 52.0, -30.3, -28.5, 0.6, 1.3)
    dwh = G(dt + "_Wheels", "M_Dark")
    for a in (47.0, 48.4, 51.0):
        for yy, w in ((-30.7, 0.38), (-28.1, -0.38)):
            cylinder(dwh, (X(a), yy, 0.6), (X(a), yy + w, 0.6), 0.6, seg=16)
    bx(G(dt + "_Bed", "M_PaintYellow"), 46.0, 50.0, -30.4, -28.4, 1.3, 3.2, skip=("-z",))
    bx(G(dt + "_Cab", "M_Metal"), 50.2, 52.0, -30.3, -28.5, 1.3, 3.1, skip=("-z",))
    barrels = G("Barrels_" + S, "M_Metal")
    for (a, y) in [(45.0, 16.0), (45.7, 16.2), (45.3, 16.8), (36.5, 20.5), (37.1, 20.9), (25.4, -5.2)]:
        vcyl(barrels, X(a), y, 0, 0.9, 0.3, seg=14, caps=(False, True))

    # ------------------------------------------------------------------ pump house half
    ph = G("Pump_Walls_" + S, "M_Concrete")
    wy(ph, -7.0, 7.0, 7.6, 8.0, 0, 8.0,
       [(-1.2, 1.2, 0.0, 2.8), (3.5, 5.5, 1.1, 2.3), (-4.8, -3.0, 1.1, 2.3), (-3.0, 3.0, 4.8, 6.4)])
    wx(ph, 0, 7.6, 6.6, 7.0, 0, 8.0, [(3.5, 5.5, 0.0, 2.6), (2.0, 5.0, 4.8, 6.0)])
    wx(ph, 0, 7.6, -7.0, -6.6, 0, 8.0, [(0.6, 2.6, 0.0, 2.6), (0.8, 2.8, 4.8, 6.0)])
    slab = G("Pump_Slab_" + S, "M_ConcreteFloor")
    bx(slab, 0, 7.6, -5.0, 6.6, 3.4, 3.7)
    bx(slab, 0, 3.2, -6.6, -5.0, 3.4, 3.7)
    bx(G("Pump_Roof_" + S, "M_Concrete"), 0, 7.6, -6.6, 6.6, 7.1, 7.4)
    # Stairs climb from the middle (next to the south door) towards the outer wall; at the top you
    # step north onto the floor through the gap left in the stairwell railing.
    steps(G("Pump_Stairs_" + S, "M_Concrete"), "x", X(3.2), sx, -6.6, -5.0, 0.1, 0.4, 0.3, 12)
    railing("Pump_StairRail_" + S, (X(3.2), -5.0), (X(6.3), -5.0), 3.7)
    pc = G("Pump_Cover_" + S, "M_Wood")
    crate(pc, X(5.1), 4.0, 0.1)
    crate(pc, X(5.6), 3.8, 3.7)
    sacks(G("Pump_Sacks_" + S, "M_Sacks"), X(6.4), X(7.4), -2.6, -1.4, 3.7, 1.1)
    lamp(3 if sx < 0 else 4, X(5.0), 0.0, 3.25)
    lamp(5 if sx < 0 else 6, X(4.0), 2.0, 7.0)
    trim = G("Pump_Trim_" + S, "M_Hazard", "nc")
    bx(trim, 8.0, 8.04, -1.45, -1.2, 0.0, 2.8)
    bx(trim, 8.0, 8.04, 1.2, 1.45, 0.0, 2.8)

    # ------------------------------------------------------------------ lane dividers
    div = G("Divider_" + S, "M_Concrete")
    wx(div, 3.0, 24.0, 11.0, 11.6, 0, 3.2, [(8.5, 11.5, 0, 3.2)])
    wx(div, 0.25, 14.0, -11.6, -11.0, 0, 3.2, [(8.0, 11.0, 0, 3.2)])
    dp = G("Divider_Pipes_" + S, "M_Rust", "nc")
    for z in (3.45, 3.85):
        cylinder(dp, (X(3.0), 11.3, z), (X(8.5), 11.3, z), 0.17, seg=12, caps=(False, False))
        cylinder(dp, (X(11.5), 11.3, z), (X(24.0), 11.3, z), 0.17, seg=12, caps=(False, False))

    # ------------------------------------------------------------------ north lane: pans, tanks, catwalk
    berm = G("Pans_Berms_" + S, "M_Concrete")
    for a in (7.7, 15.7):
        bx(berm, a, a + 0.6, 13.3, 35.7, 0, 0.25, skip=("-z",))
    for a0, a1 in ((1.9, 7.7), (8.3, 15.7), (16.3, 24.0)):
        bx(berm, a0, a1, 23.7, 24.3, 0, 0.25, skip=("-z",))
    brine = G("Pans_Brine_" + S, "M_Brine", "nc")
    bx(brine, 1.95, 7.65, 24.35, 35.65, 0, 0.02)
    bx(brine, 16.35, 23.95, 13.35, 23.65, 0, 0.02)
    tank("TankA_" + S, X(13.0), 28.5, 3.0, 6.5)
    tank("TankB_" + S, X(21.0), 17.5, 2.4, 5.0)
    tank("TankC_" + S, X(40.0), 26.0, 4.0, 7.5)
    tank("TankD_" + S, X(50.5), 30.5, 3.0, 6.0)
    ht = G("HTank_" + S, "M_TankPaint")
    cylinder(ht, (X(3.5), 18.5, 1.65), (X(8.5), 18.5, 1.65), 1.3, seg=28)
    sad = G("HTank_Saddles_" + S, "M_Metal")
    for a in (4.5, 7.5):
        bx(sad, a - 0.3, a + 0.3, 17.4, 19.6, 0, 0.6, skip=("-z",))
    bx(G("Sluice_Pillars_" + S, "M_Concrete"), 1.9, 2.9, 19.5, 20.5, 0, 3.2, skip=("-z",))
    cylinder(G("Pans_Pipe_" + S, "M_Rust"), (X(11.0), 26.5, 0.5), (X(2.0), 26.5, 0.5), 0.22, seg=14)
    hv = "Harvester_" + S
    bx(G(hv + "_Body", "M_PaintYellow"), 10.0, 13.5, 13.4, 15.8, 0.8, 2.8, skip=("-z",))
    bx(G(hv + "_Tracks", "M_Dark"), 10.0, 13.5, 13.4, 14.0, 0, 0.8, skip=("-z",))
    bx(G(hv + "_Tracks", "M_Dark"), 10.0, 13.5, 15.2, 15.8, 0, 0.8, skip=("-z",))
    bx(G(hv + "_Scoop", "M_Rust", "nc"), 13.5, 14.6, 13.6, 15.6, 0.2, 1.0)
    ps = G("Pans_Sacks_" + S, "M_Sacks")
    sacks(ps, X(16.5), X(18.5), 21.5, 23.5, 0, 2.4)
    sacks(ps, X(4.0), X(5.2), 21.0, 22.2, 0, 1.2)
    sacks(ps, X(21.5), X(23.5), 27.5, 29.5, 0, 2.4)
    # catwalk along the north edge
    bx(G("Catwalk_Deck", "M_Grate"), 2.5, 20.0, 31.5, 33.5, 2.85, 3.0)
    legs = G("Catwalk_Legs_" + S, "M_Metal")
    for a in (2.4, 7.0, 10.0, 15.0, 19.8):
        for yy in (31.6, 33.4):
            bx(legs, a - 0.12, a + 0.12, yy - 0.12, yy + 0.12, 0, 2.85, skip=("-z",))
    for yy in (30.6, 34.4):
        bx(legs, 2.28, 2.52, yy - 0.12, yy + 0.12, 0, 2.85, skip=("-z",))
    steps(G("Catwalk_Stairs_" + S, "M_Metal"), "x", X(20.0), sx, 31.5, 33.5, 0.0, 2.7, -0.3, 9)
    steps(G("Catwalk_Stairs_" + S, "M_Metal"), "y", 31.5, -1, X(4.5), X(6.0), 0.0, 2.7, -0.3, 9)
    railing("Catwalk_RailS1_" + S, (X(2.5), 31.5), (X(4.5), 31.5), 3.0)
    railing("Catwalk_RailS2_" + S, (X(6.0), 31.5), (X(20.0), 31.5), 3.0)
    railing("Catwalk_RailN_" + S, (X(2.5), 33.5), (X(20.0), 33.5), 3.0)
    plates = G("Catwalk_Plates_" + S, "M_Plate")
    for a0, a1 in ((7.5, 9.5), (13.0, 15.0), (17.0, 19.0)):
        bx(plates, a0, a1, 31.42, 31.5, 3.0, 4.1)

    # ------------------------------------------------------------------ south lane: shed, dock yard, cliff road
    shed = G("Shed_Walls_" + S, "M_Corrugated")
    wx(shed, 14.0, 26.0, -11.15, -11.0, 0, 5.5)
    wx(shed, 14.0, 26.0, -25.0, -24.85, 0, 5.5, [(18.5, 21.5, 0, 3.0)])
    wy(shed, -24.85, -11.15, 14.0, 14.15, 0, 5.5, [(-20.0, -16.0, 0, 3.4)])
    wy(shed, -24.85, -11.15, 25.85, 26.0, 0, 5.5, [(-21.0, -15.0, 0, 4.2)])
    roofg = G("Shed_Roof_" + S, "M_Corrugated")
    for y0, y1 in ((-25.0, -21.5), (-20.5, -15.5), (-14.5, -11.0)):
        bx(roofg, 14.0, 26.0, y0, y1, 5.5, 5.62)
    bx(G("Shed_Beams_" + S, "M_Metal", "nc"), 14.0, 26.0, -21.6, -21.4, 5.2, 5.5)
    bx(G("Shed_Beams_" + S, "M_Metal", "nc"), 14.0, 26.0, -15.6, -15.4, 5.2, 5.5)
    bx(G("Shed_Floor_" + S, "M_ConcreteFloor"), 14.15, 25.85, -24.85, -11.15, 0, 0.06, skip=("-z",))
    conv = G("Shed_Conveyor_" + S, "M_Metal")
    bx(conv, 16.5, 23.5, -18.6, -17.6, 0.06, 1.0, skip=("-z",))
    rollers = G("Shed_Rollers_" + S, "M_Dark", "nc")
    for i in range(18):
        a = 16.7 + i * 0.38
        cylinder(rollers, (X(a), -18.55, 1.04), (X(a), -17.65, 1.04), 0.05, seg=8, caps=(False, False))
    ss = G("Shed_Sacks_" + S, "M_Sacks")
    sacks(ss, X(15.0), X(16.2), -24.3, -22.3, 0.06, 2.4)
    sacks(ss, X(23.2), X(25.2), -12.9, -11.7, 0.06, 1.2)
    crate(G("Shed_Crates_" + S, "M_Wood"), X(19.9), -13.4, 0.06)
    bx(G("Shed_Rack_" + S, "M_Metal"), 22.5, 25.5, -24.6, -23.6, 0.06, 3.0, skip=("-z",))
    lamp(7 if sx < 0 else 8, X(17.0), -18.0, 5.05)
    lamp(9 if sx < 0 else 10, X(23.0), -18.0, 5.05)
    # dock yard
    salt_pile(G("SaltPile_" + S, "M_Salt"), X(8.8), -18.8, 3.4, 3.8, seed=3.0 if sx < 0 else 7.0)
    sacks(G("Dock_Sacks_" + S, "M_Sacks"), X(0.6), X(1.8), -23.5, -22.3, 0, 1.2)
    crate(G("Dock_Crates_" + S, "M_Wood"), X(12.1), -24.9, 0)
    crate(G("Dock_Crates_" + S, "M_Wood"), X(12.1), -24.9, 1.2)
    rampg = G("Dock_Ramps_" + S, "M_Concrete")
    wedge(rampg, X(3.0), X(6.0), -27.0, -22.5, 0.0, 1.2, "-y")
    steps(G("Dock_Stairs_" + S, "M_Concrete"), "x", X(14.0), sx, -34.0, -30.5, 0.0, 0.9, -0.3, 3)
    dc = G("Dock_Cover_" + S, "M_Wood")
    for (a, y, z) in [(7.1, -30.6, 1.2), (8.3, -30.6, 1.2), (7.1, -29.4, 1.2), (7.7, -30.0, 2.4)]:
        crate(dc, X(a), y, z)
    crate(dc, X(11.1), -34.4, 1.2)
    bol = G("Bollards_" + S, "M_Dark")
    for a in (4.0, 9.0, 13.0):
        vcyl(bol, X(a), -35.6, 1.2, 1.8, 0.22, seg=12)

    # ------------------------------------------------------------------ road markings (decals)
    mark = G("RoadMarks_" + S, "M_PaintYellow", "nc")
    bx(mark, 26.0, 44.0, -30.15, -30.0, 0.0, 0.006)
    for a in range(27, 44, 3):
        bx(mark, a, a + 1.5, -26.2, -26.05, 0.0, 0.006)


def build():
    REG.clear()
    MARKERS.clear()
    build_center()
    build_side(-1)
    build_side(+1)


# =========================================================================== blender output

def reset_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def make_material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    tex, _, tint = MATS[name]
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = 0.6
    if name == "M_Lamp":
        bsdf.inputs["Emission Color"].default_value = (*tint, 1)
        bsdf.inputs["Emission Strength"].default_value = 6.0
    if tex:
        path = os.path.join(TEX_DIR, f"T_{tex}_D.png")
        img = bpy.data.images.get(os.path.basename(path)) or bpy.data.images.load(path, check_existing=True)
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        nt.links.new(node.outputs["Color"], mul.inputs[6])
        mul.inputs[7].default_value = (*[min(1.0, c) for c in tint], 1)
        nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*tint, 1)
    return m


def finalize():
    colls = {}
    root = bpy.data.collections.new("KAPPA")
    bpy.context.scene.collection.children.link(root)
    for name in list(KIND_COLL.values()) + ["KAPPA_MARKERS"]:
        c = bpy.data.collections.new(name)
        root.children.link(c)
        colls[name] = c
    mats = {}
    tris = 0
    for g in REG.values():
        if not g.faces:
            continue
        me = bpy.data.meshes.new(g.name)
        me.from_pydata([tuple(v) for v in g.verts], [], g.faces)
        uv = me.uv_layers.new(name="UVMap")
        flat = [c for face in g.uvs for p in face for c in p]
        uv.data.foreach_set("uv", flat)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
        bm.to_mesh(me)
        bm.free()
        me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
        me.set_sharp_from_angle(angle=math.radians(35))
        me.validate()
        me.update()
        if g.mat not in mats:
            mats[g.mat] = make_material(g.mat)
        me.materials.append(mats[g.mat])
        obj = bpy.data.objects.new(g.name + KIND_SUFFIX[g.kind], me)
        colls[KIND_COLL[g.kind]].objects.link(obj)
        if g.kind == "clip":
            obj.display_type = "WIRE"
            obj.hide_render = True
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
    for name, pos in MARKERS:
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.6
        e.location = pos
        colls["KAPPA_MARKERS"].objects.link(e)
    return tris


def export():
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in bpy.data.collections["KAPPA"].all_objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, mesh_smooth_type="OFF",
        use_custom_props=False, add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, copy=True)


def run():
    reset_scene()
    build()
    tris = finalize()
    if DO_EXPORT:
        export()
    kinds = {}
    for g in REG.values():
        kinds[g.kind] = kinds.get(g.kind, 0) + 1
    return {"objects": len(REG), "kinds": kinds, "triangles": tris, "markers": len(MARKERS),
            "fbx": FBX_PATH if DO_EXPORT else None}


RESULT = run()
