"""
SALTLINE operator: an original tactical character, modelled and rigged in Blender from code.

Run in Blender (via the Blender MCP or the Python console):
    g = {"SALTLINE_ROOT": "<repo>", "__name__": "operator"}
    exec(open(g["SALTLINE_ROOT"] + "/ArtSource/Blender/operator_build.py").read(), g)

Conventions (shared with the weapons): metres, +Z up, the character faces +Y with its left side at -X,
so it imports into Unity facing +Z. Proportions follow the game's hitboxes (Runtime/Combat/Hitboxes.cs):
1.80 m tall, eyes at 1.62 m, shoulders at 1.42 m, hips at 0.92 m. T-pose, so Unity's Humanoid avatar
maps it directly and Mixamo clips retarget onto it.

Stages:
    joints()          bone head/tail positions (Mixamo-style names, no prefix)
    body()            base body from a skin-modifier graph, subdivided: clothed torso, limbs, gloves, boots
    armature()        the skeleton
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

ROOT = globals().get("SALTLINE_ROOT", "")
# Shared modelling + baking toolkit (prisms, tubes, bevelled parts, joins, the texture bake).
exec(open(os.path.join(ROOT, "ArtSource", "Blender", "weapons_lib.py")).read(), globals())

# Character surfaces: (base colour linear, metallic, roughness, edge wear, bare colour)
SURFACES.update({
    "shirt":     ((0.150, 0.128, 0.092), 0.0, 0.88, 0.0, (0.15, 0.13, 0.09)),   # khaki combat shirt
    "pants":     ((0.072, 0.078, 0.056), 0.0, 0.86, 0.0, (0.07, 0.08, 0.06)),   # ranger-green trousers
    "balaclava": ((0.062, 0.061, 0.054), 0.0, 0.92, 0.0, (0.06, 0.06, 0.054)),  # charcoal-olive knit
    "skin":      ((0.270, 0.160, 0.105), 0.0, 0.50, 0.0, (0.27, 0.16, 0.105)),  # painted into the balaclava's eye opening
    "glove":     ((0.050, 0.047, 0.040), 0.0, 0.70, 0.10, (0.09, 0.08, 0.07)),
    "nylon":     ((0.140, 0.100, 0.060), 0.0, 0.82, 0.05, (0.20, 0.16, 0.11)),  # coyote 500D
    "webbing":   ((0.060, 0.050, 0.035), 0.0, 0.80, 0.0, (0.06, 0.05, 0.035)),
    "helmet":    ((0.170, 0.140, 0.095), 0.0, 0.62, 0.35, (0.30, 0.28, 0.25)),  # painted ballistic shell
    "leather":   ((0.150, 0.100, 0.055), 0.0, 0.66, 0.20, (0.22, 0.16, 0.10)),  # boot uppers
    "sole":      ((0.022, 0.020, 0.018), 0.0, 0.90, 0.0, (0.03, 0.03, 0.03)),
    "lens":      ((0.010, 0.010, 0.012), 0.0, 0.06, 0.0, (0.01, 0.01, 0.01)),
    "team":      ((0.700, 0.700, 0.700), 0.0, 0.75, 0.0, (0.70, 0.70, 0.70)),   # tinted per team in the game
})

# --------------------------------------------------------------------------- scene
def wipe():
    global CUTTERS
    CUTTERS = None
    PARTS.clear()
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images, bpy.data.cameras,
                 bpy.data.lights, bpy.data.node_groups, bpy.data.actions):
        for b in list(coll):
            coll.remove(b)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


# --------------------------------------------------------------------------- skeleton
# name: (head, parent). Tails point at the first child (or along the parent direction for ends).
SPINE = [
    ("Hips", (0.0, 0.0, 0.97), None),
    ("Spine", (0.0, 0.004, 1.07), "Hips"),
    ("Spine1", (0.0, 0.0, 1.19), "Spine"),
    ("Spine2", (0.0, -0.008, 1.31), "Spine1"),
    ("Neck", (0.0, -0.006, 1.50), "Spine2"),
    ("Head", (0.0, 0.010, 1.585), "Neck"),
    ("HeadTop_End", (0.0, 0.010, 1.80), "Head"),
]


def _side(sign, prefix):
    """One side's arm, hand and leg joints. sign = -1 left (-X), +1 right."""
    s = sign
    j = [
        (f"{prefix}Shoulder", (s * 0.035, -0.010, 1.45), "Spine2"),
        (f"{prefix}Arm", (s * 0.180, -0.020, 1.425), f"{prefix}Shoulder"),
        (f"{prefix}ForeArm", (s * 0.450, -0.020, 1.425), f"{prefix}Arm"),
        (f"{prefix}Hand", (s * 0.705, -0.020, 1.425), f"{prefix}ForeArm"),
        (f"{prefix}UpLeg", (s * 0.095, 0.000, 0.920), "Hips"),
        (f"{prefix}Leg", (s * 0.100, 0.015, 0.505), f"{prefix}UpLeg"),
        (f"{prefix}Foot", (s * 0.105, -0.020, 0.085), f"{prefix}Leg"),
        (f"{prefix}ToeBase", (s * 0.110, 0.115, 0.024), f"{prefix}Foot"),
        (f"{prefix}Toe_End", (s * 0.112, 0.195, 0.024), f"{prefix}ToeBase"),
    ]
    # fingers: palm down, fingers along the arm, thumb forward (+Y)
    fingers = {
        "Thumb": [(0.735, 0.030, 1.412), (0.768, 0.058, 1.402), (0.792, 0.077, 1.398), (0.812, 0.092, 1.396)],
        "Index": [(0.800, 0.026, 1.428), (0.842, 0.027, 1.428), (0.866, 0.028, 1.427), (0.887, 0.029, 1.426)],
        "Middle": [(0.804, 0.001, 1.429), (0.850, 0.001, 1.429), (0.877, 0.001, 1.428), (0.899, 0.001, 1.427)],
        "Ring": [(0.800, -0.022, 1.428), (0.842, -0.023, 1.428), (0.867, -0.023, 1.427), (0.887, -0.024, 1.426)],
        "Pinky": [(0.790, -0.042, 1.426), (0.823, -0.044, 1.425), (0.842, -0.045, 1.424), (0.859, -0.046, 1.423)],
    }
    for f, pts in fingers.items():
        parent = f"{prefix}Hand"
        for k, p in enumerate(pts):
            name = f"{prefix}Hand{f}{k + 1}"
            j.append((name, (s * p[0], p[1], p[2]), parent))
            parent = name
    return j


def joints():
    return SPINE + _side(-1, "Left") + _side(1, "Right")


# --------------------------------------------------------------------------- base body
# Skin-modifier graph: (name, position, (radius across, radius front-back)), and edges.
# The radii include clothing: a combat shirt and trousers over a 1.80 m build.
def _body_graph():
    n = {}
    e = []

    def node(name, p, r):
        n[name] = (Vector(p), r)

    def chain(*names):
        for a, b in zip(names, names[1:]):
            e.append((a, b))

    node("pelvis", (0.0, 0.0, 0.900), (0.205, 0.135))
    node("hips", (0.0, 0.0, 0.975), (0.222, 0.140))
    node("waist", (0.0, 0.006, 1.080), (0.175, 0.120))
    node("belly", (0.0, 0.002, 1.190), (0.178, 0.128))
    node("chest", (0.0, -0.004, 1.305), (0.215, 0.175))
    node("upchest", (0.0, -0.012, 1.405), (0.200, 0.150))
    node("neck0", (0.0, -0.010, 1.480), (0.075, 0.075))
    node("neck1", (0.0, 0.000, 1.535), (0.070, 0.072))
    node("jaw", (0.0, 0.016, 1.585), (0.072, 0.088))
    node("head", (0.0, 0.010, 1.670), (0.093, 0.116))
    node("crown", (0.0, 0.000, 1.755), (0.080, 0.095))
    node("top", (0.0, -0.004, 1.795), (0.030, 0.034))
    chain("pelvis", "hips", "waist", "belly", "chest", "upchest", "neck0", "neck1", "jaw", "head", "crown", "top")

    for s, side in ((-1, "L"), (1, "R")):
        def N(name, p, r):
            node(name + side, (s * p[0], p[1], p[2]), r)
        N("shoulder", (0.190, -0.016, 1.420), (0.070, 0.072))
        N("biceps", (0.315, -0.020, 1.424), (0.066, 0.063))
        N("elbow", (0.450, -0.020, 1.425), (0.052, 0.052))
        N("forearm", (0.565, -0.020, 1.426), (0.060, 0.052))
        N("wrist", (0.700, -0.020, 1.425), (0.043, 0.032))
        N("palm", (0.765, -0.008, 1.426), (0.046, 0.020))
        N("knuckle", (0.800, -0.008, 1.427), (0.046, 0.016))
        for f, pts, r in (
            ("thumb", [(0.735, 0.030, 1.412), (0.768, 0.058, 1.402), (0.792, 0.077, 1.398), (0.812, 0.092, 1.396)], 0.0115),
            ("index", [(0.800, 0.026, 1.428), (0.842, 0.027, 1.428), (0.866, 0.028, 1.427), (0.890, 0.029, 1.426)], 0.0102),
            ("middle", [(0.804, 0.001, 1.429), (0.850, 0.001, 1.429), (0.877, 0.001, 1.428), (0.902, 0.001, 1.427)], 0.0105),
            ("ring", [(0.800, -0.022, 1.428), (0.842, -0.023, 1.428), (0.867, -0.023, 1.427), (0.890, -0.024, 1.426)], 0.0098),
            ("pinky", [(0.790, -0.042, 1.426), (0.823, -0.044, 1.425), (0.842, -0.045, 1.424), (0.862, -0.046, 1.423)], 0.0088),
        ):
            names = []
            for k, p in enumerate(pts):
                N(f"{f}{k}", p, (r * (1.0 - 0.08 * k), r * (0.9 - 0.08 * k)))
                names.append(f"{f}{k}{side}")
            chain(("palm" if f == "thumb" else "knuckle") + side, *names)
        N("hipj", (0.098, 0.000, 0.880), (0.105, 0.110))
        N("thigh", (0.100, 0.006, 0.720), (0.100, 0.108))
        N("knee", (0.100, 0.015, 0.505), (0.066, 0.070))
        N("calf", (0.103, -0.006, 0.330), (0.069, 0.088))
        N("ankle", (0.105, -0.010, 0.120), (0.050, 0.055))   # boot shaft
        N("heel", (0.106, -0.030, 0.045), (0.046, 0.050))
        N("ball", (0.110, 0.110, 0.034), (0.048, 0.030))
        N("toe", (0.112, 0.180, 0.034), (0.040, 0.028))
        chain("upchest", "shoulder" + side, "biceps" + side, "elbow" + side, "forearm" + side, "wrist" + side, "palm" + side, "knuckle" + side)
        chain("pelvis", "hipj" + side, "thigh" + side, "knee" + side, "calf" + side, "ankle" + side, "heel" + side, "ball" + side, "toe" + side)
    return n, e


def body(subdiv=2):
    nodes, edges = _body_graph()
    me = bpy.data.meshes.new("OP_Body")
    bm = bmesh.new()
    idx = {}
    for k, (name, (p, r)) in enumerate(nodes.items()):
        idx[name] = bm.verts.new(p)
    for a, b in edges:
        bm.edges.new((idx[a], idx[b]))
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("OP_Body", me)
    bpy.context.scene.collection.objects.link(ob)
    skin = ob.modifiers.new("Skin", "SKIN")
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.6
    data = me.skin_vertices[0].data
    for k, (name, (p, r)) in enumerate(nodes.items()):
        data[k].radius = r
        data[k].use_root = name == "pelvis"
    sub = ob.modifiers.new("Subdiv", "SUBSURF")
    sub.levels = sub.render_levels = subdiv
    dg = bpy.context.evaluated_depsgraph_get()
    flat = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data
    ob.modifiers.clear()
    ob.data = flat
    bpy.data.meshes.remove(old)
    flat.name = "OP_Body"
    for p in flat.polygons:
        p.use_smooth = True
    return ob


# --------------------------------------------------------------------------- armature
def armature():
    arm = bpy.data.armatures.new("OP_Armature")
    ob = bpy.data.objects.new("OP_Armature", arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    js = joints()
    pos = {n: Vector(p) for n, p, _ in js}
    children = {}
    for n, p, par in js:
        children.setdefault(par, []).append(n)
    bones = {}
    for n, p, par in js:
        b = arm.edit_bones.new(n)
        b.head = pos[n]
        kids = children.get(n, [])
        # the main child decides the tail: the spine continues up, a limb continues along itself
        main = next((k for k in kids if not k.startswith(("LeftShoulder", "RightShoulder", "LeftUpLeg", "RightUpLeg"))
                     and "Thumb" not in k and "Index" not in k and "Ring" not in k and "Pinky" not in k), None)
        if n.endswith("Hand"):
            main = n + "Middle1"
        if main is not None:
            b.tail = pos[main]
        else:
            d = (pos[n] - pos[par]) if par else Vector((0, 0, 0.1))
            b.tail = pos[n] + d.normalized() * max(0.02, min(0.1, d.length * 0.6))
        bones[n] = b
    for n, p, par in js:
        if par:
            bones[n].parent = bones[par]
            # connect chains where the parent's tail is this joint
            bones[n].use_connect = (bones[par].tail - bones[n].head).length < 1e-5
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


# --------------------------------------------------------------------------- preview
def preview(path_prefix, views=("front", "side", "back"), size=(900, 1200)):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = size
    cam = bpy.data.objects.get("OP_PreviewCam") or bpy.data.objects.new("OP_PreviewCam", bpy.data.cameras.new("OP_PreviewCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.05
    sc.camera = cam
    target = Vector((0, 0, 0.92))
    spots = {"front": Vector((0, 5, 0.92)), "side": Vector((5, 0, 0.92)), "back": Vector((0, -5, 0.92)),
             "quarter": Vector((3.2, 3.6, 1.6))}
    files = []
    for v in views:
        cam.location = spots[v]
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = f"{path_prefix}_{v}.png"
        bpy.ops.render.render(write_still=True)
        files.append(sc.render.filepath)
    return files


# --------------------------------------------------------------------------- checks
# Cross-section targets (m) for the clothed body: (point, axis, target). Width = x, depth = y, vertical = z.
TARGETS = {
    "chest width": ((0, 0, 1.305), 0, 0.36), "chest depth": ((0, 0, 1.305), 1, 0.25),
    "waist width": ((0, 0, 1.08), 0, 0.31), "waist depth": ((0, 0, 1.08), 1, 0.22),
    "hips width": ((0, 0, 0.975), 0, 0.34), "neck width": ((0, -0.005, 1.51), 0, 0.13),
    "head width": ((0, 0.01, 1.67), 0, 0.16), "head depth": ((0, 0.01, 1.67), 1, 0.20),
    "thigh depth": ((-0.10, 0.006, 0.72), 1, 0.18), "calf depth": ((-0.103, -0.006, 0.33), 1, 0.13),
    "biceps height": ((-0.315, -0.02, 1.424), 2, 0.115), "forearm height": ((-0.565, -0.02, 1.426), 2, 0.095),
}


def measure(ob):
    """Surface-to-surface span through each target point; returns {name: (measured, target)}."""
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
    out = {}
    for name, (c, axis, target) in TARGETS.items():
        c = Vector(c)
        d = Vector((0, 0, 0))
        d[axis] = 1.0
        a = bvh.ray_cast(c + d * 0.6, -d, 1.2)[0]
        b = bvh.ray_cast(c - d * 0.6, d, 1.2)[0]
        out[name] = (round((a - b).length, 3) if a and b else None, target)
    return out


# --------------------------------------------------------------------------- gear
class Surface:
    """Ray-cast probe of the body surface, so gear sits on the body rather than on guessed numbers."""

    def __init__(self, ob):
        from mathutils.bvhtree import BVHTree
        self.bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())

    def hit(self, origin, direction, dist=1.0):
        h = self.bvh.ray_cast(Vector(origin), Vector(direction).normalized(), dist)[0]
        return h

    def front(self, x, z):   # largest y of the body at (x, z)
        h = self.hit((x, 0.6, z), (0, -1, 0))
        return h.y if h else 0.0

    def back(self, x, z):
        h = self.hit((x, -0.6, z), (0, 1, 0))
        return h.y if h else 0.0

    def side(self, y, z, sign):  # |x| of the torso side at (y, z), sign = -1 left / +1 right
        h = self.hit((sign * 0.6, y, z), (-sign, 0, 0))
        return abs(h.x) if h else 0.0


def bend(bm, radius, sign=1.0, axis_x=0.0):
    """Curve a flat piece around a vertical axis: y -= sign * (x - axis_x)^2 / (2 R). Wraps plates and pads round the body."""
    for v in bm.verts:
        dx = v.co.x - axis_x
        v.co.y -= sign * dx * dx / (2.0 * radius)
    return bm


def ellipse_ring(a, b, z0, z1, thick, seg=48, centre=(0.0, 0.0), axis="z"):
    """Elliptical band (belt, cummerbund, armband): outer semi-axes a, b, wall `thick`."""
    bm = tube_bm(1.0, 1.0, z0, z1, axis, seg, (0.0, 0.0), r_in=1.0 - thick / max(a, b))
    u, v, w = _plane_axes(axis)
    for vert in bm.verts:
        pu, pv = vert.co.dot(u), vert.co.dot(v)
        vert.co = vert.co - u * pu - v * pv + u * (pu * a + centre[0]) + v * (pv * b + centre[1])
    return bm


def solid_shell(bm, thickness):
    """Give an open surface a thickness (inward)."""
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=thickness)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def loft_bm(stations, centre_x=0.0, seg=24, n_top=2.4, n_bottom=7.0):
    """Closed loft along +Y through rounded-box sections (y, half width, bottom z, top z): boot toe boxes.
    The lower half is near-flat (n_bottom) so it sits on a sole; the upper half is rounded (n_top)."""
    bm = bmesh.new()
    rings = []
    for y, w, z0, z1 in stations:
        zc, hz = (z0 + z1) * 0.5, (z1 - z0) * 0.5
        ring = []
        for k in range(seg):
            t = 2.0 * math.pi * k / seg
            c, s_ = math.cos(t), math.sin(t)
            n = n_top if s_ > 0 else n_bottom
            x = w * math.copysign(abs(c) ** (2.0 / n), c)
            z = zc + hz * math.copysign(abs(s_) ** (2.0 / n), s_)
            ring.append(bm.verts.new((centre_x + x, y, z)))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(seg):
            j = (k + 1) % seg
            bm.faces.new((r0[k], r0[j], r1[j], r1[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


HELMET_N = 2.5   # superellipsoid exponent: >2 flattens the crown so the helmet is not egg-shaped


def head_fit(body_ob, pad=0.024, shell=0.010, top_pad=0.012):
    """Measure the head and size the helmet ellipsoid around it (pad gap + shell thickness)."""
    vs = [v.co for v in body_ob.data.vertices if v.co.z > 1.60]
    band = [v for v in vs if 1.655 < v.z < 1.705]
    top = max(v.z for v in vs)
    side = max(abs(v.x) for v in band)
    y0, y1 = min(v.y for v in band), max(v.y for v in band)
    centre = Vector((0.0, (y0 + y1) * 0.5 - 0.004, 1.664))
    g = pad + shell
    return {"centre": centre, "a": side + g, "b": (y1 - y0) * 0.5 + g, "c": top + top_pad + shell - centre.z, "shell": shell, "side": side,
            "front": y1, "back": y0, "top": top}


def helmet_rim_z(az):
    """Rim height by azimuth (0 = front): brow over the eyes, high cut over the ears, low at the nape."""
    a = math.atan2(math.sin(az), math.cos(az))
    z = 1.668 + 0.034 * math.cos(a) + 0.007 * math.cos(2 * a)
    return z + 0.012 * math.exp(-((abs(a) - 1.50) / 0.32) ** 2)


def _rho(H, z):   # horizontal scale of the superellipsoid at height z
    return max(0.0, 1.0 - abs((z - H["centre"].z) / H["c"]) ** HELMET_N) ** (1.0 / HELMET_N)


def ell_x(H, y, z):
    q = _rho(H, z) ** 2 - ((y - H["centre"].y) / H["b"]) ** 2
    return H["a"] * math.sqrt(max(q, 0.0))


def ell_y(H, x, z, back=False):
    q = _rho(H, z) ** 2 - (x / H["a"]) ** 2
    return H["centre"].y + (-1.0 if back else 1.0) * H["b"] * math.sqrt(max(q, 0.0))


def helmet_bm(H, rim_band=None, outset=0.0, na=72, ne=22):
    """Ellipsoid shell cut cleanly at helmet_rim_z (no stair-stepping): rings run from the rim to the crown.
    With rim_band, only a band that high above the rim is built (the rubber edge trim), pushed out by outset."""
    c, a, b, cz = H["centre"], H["a"] + outset, H["b"] + outset, H["c"] + outset
    bm = bmesh.new()
    cols = []
    for i in range(na):
        az = 2.0 * math.pi * i / na
        sa, ca = math.sin(az), math.cos(az)
        def el_at(z):   # inverse of the superellipsoid height map
            u = max(-1.0, min(1.0, (z - c.z) / cz))
            return math.asin(math.copysign(abs(u) ** (HELMET_N / 2.0), u))
        el0 = el_at(helmet_rim_z(az))
        el1 = el_at(helmet_rim_z(az) + rim_band) if rim_band else math.radians(88)
        col = []
        for j in range(ne + 1 if not rim_band else 3):
            t = j / (ne if not rim_band else 2)
            el = el0 + (el1 - el0) * (t if rim_band else (1 - (1 - t) ** 1.6))
            ce, se = math.cos(el), math.sin(el)
            r = abs(ce) ** (2.0 / HELMET_N)
            col.append(bm.verts.new((c.x + a * r * sa, c.y + b * r * ca, c.z + cz * math.copysign(abs(se) ** (2.0 / HELMET_N), se))))
        cols.append(col)
    n = len(cols[0])
    for i in range(na):
        c0, c1 = cols[i], cols[(i + 1) % na]
        for j in range(n - 1):
            bm.faces.new((c0[j], c1[j], c1[j + 1], c0[j + 1]))
    if not rim_band:
        pole = bm.verts.new((c.x, c.y, c.z + cz))
        for i in range(na):
            bm.faces.new((cols[i][-1], cols[(i + 1) % na][-1], pole))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:   # outward
        if f.normal.dot(f.calc_center_median() - c) < 0:
            f.normal_flip()
    solid_shell(bm, H["shell"] if not rim_band else 0.006)
    return bm


def sculpt_head(ob):
    """Give the covered head a face under the balaclava: brow, eye sockets, nose, cheekbones, chin and jaw,
    and slim the neck. Gaussian pushes along fixed directions, measured from the head's own front."""
    me = ob.data
    pts = [v.co for v in me.vertices]
    def front_at(z, x=0.0):
        near = [p.y for p in pts if abs(p.z - z) < 0.006 and abs(p.x - x) < 0.012]
        return max(near) if near else 0.08
    yf = front_at(1.63)
    fields = [  # centre, sigma (x, y, z), amount, direction
        ((0.0, yf, 1.612), (0.010, 0.030, 0.016), 0.016, (0, 1, 0)),     # nose
        ((0.0, yf, 1.596), (0.012, 0.030, 0.008), 0.006, (0, 1, 0)),     # nose tip / base
        ((0.0, yf, 1.655), (0.040, 0.030, 0.010), 0.006, (0, 1, 0)),     # brow ridge
        ((-0.033, yf, 1.634), (0.013, 0.030, 0.010), -0.010, (0, 1, 0)), # eye sockets
        ((0.033, yf, 1.634), (0.013, 0.030, 0.010), -0.010, (0, 1, 0)),
        ((0.0, yf, 1.548), (0.020, 0.040, 0.014), 0.014, (0, 1, 0)),     # chin
        ((-0.050, yf - 0.03, 1.600), (0.016, 0.030, 0.014), 0.006, (-1, 0.5, 0)),  # cheekbones
        ((0.050, yf - 0.03, 1.600), (0.016, 0.030, 0.014), 0.006, (1, 0.5, 0)),
        ((-0.058, 0.000, 1.560), (0.020, 0.040, 0.020), 0.008, (-1, 0, 0)),  # jaw angle
        ((0.058, 0.000, 1.560), (0.020, 0.040, 0.020), 0.008, (1, 0, 0)),
    ]
    for v in me.vertices:
        p = v.co
        if p.z < 1.44 or p.z > 1.82:
            continue
        d = Vector((0, 0, 0))
        for cen, sg, amt, dr in fields:
            q = ((p.x - cen[0]) / sg[0]) ** 2 + ((p.y - cen[1]) / sg[1]) ** 2 + ((p.z - cen[2]) / sg[2]) ** 2
            if q < 12:
                d += Vector(dr).normalized() * amt * math.exp(-0.5 * q)
        # neck: slim it between the collar and the jaw (front of the neck more than the back)
        k = math.exp(-((p.z - 1.505) / 0.030) ** 2)
        d.x -= p.x * 0.10 * k
        d.y -= (p.y - 0.0) * (0.12 if p.y > 0 else 0.05) * k
        v.co = p + d
    me.update()


RIGID = {}  # part name -> bone that carries it rigidly (helmet, glasses ...); others take the body's weights


def gear(body_ob):
    S = Surface(body_ob)
    # ---- plate carrier: curved front / back plates sized to the chest
    zs = [1.10 + 0.02 * k for k in range(17)]
    front = max(S.front(x, z) for x in (-0.1, 0.0, 0.1) for z in zs)
    back = min(S.back(x, z) for x in (-0.1, 0.0, 0.1) for z in zs)
    plate = [(-0.135, 1.095), (0.135, 1.095), (0.135, 1.33), (0.085, 1.425), (-0.085, 1.425), (-0.135, 1.33)]
    fp = part("plate_front", bend(prism_bm(round_profile(plate, 0.012), "y", front + 0.006, front + 0.042), 0.30), "nylon", bevel=0.006, segments=3)
    bp = part("plate_back", bend(prism_bm(round_profile(plate, 0.012), "y", back - 0.042, back - 0.006), 0.30, sign=-1.0), "nylon", bevel=0.006, segments=3)
    # molle rows on the front: thin raised webbing bands
    for k, z in enumerate((1.140, 1.175, 1.210, 1.245)):
        part(f"molle{k}", bend(box_bm((-0.125, front + 0.040, z), (0.125, front + 0.046, z + 0.022)), 0.30), "webbing", bevel=0.0015)
    # cummerbund: band round the ribs between the plates
    zc = 1.18
    half_w = max(S.side(0.0, zc, -1), S.side(0.0, zc, 1)) + 0.014
    half_d = max(abs(front), abs(back)) + 0.010
    part("cummerbund", ellipse_ring(half_w, half_d, 1.105, 1.265, 0.012), "nylon", bevel=0.003)
    # shoulder straps: follow the shoulder surface from the front plate's top edge to the back plate's
    for sx in (-1, 1):
        x = sx * 0.100
        c = Vector((x, -0.008, 1.36))
        outer, inner = [], []
        for k in range(15):
            t = math.radians(28 + (152 - 28) * k / 14)
            d = Vector((0.0, math.cos(t), math.sin(t)))
            h = S.hit(c + d * 0.5, -d, 0.6)
            if h is None:
                continue
            outer.append(((h + d * 0.017).y, (h + d * 0.017).z))
            inner.append(((h + d * 0.004).y, (h + d * 0.004).z))
        prof = [(front + 0.030, 1.405), (front + 0.042, 1.425)] + outer + [(back - 0.042, 1.425), (back - 0.030, 1.405)] + list(reversed(inner))
        part(f"strap{sx}", prism_bm(prof, "x", x - 0.026, x + 0.026), "nylon", bevel=0.003)
    # mag pouches (three) with flaps, admin pouch above
    for k, x in enumerate((-0.080, 0.0, 0.080)):
        ang = math.degrees(math.atan2(x, 0.30))
        y0 = front + 0.046 - x * x / (2 * 0.30)
        pb = box_bm((-0.034, 0.0, 1.100), (0.034, 0.046, 1.205))
        pf = box_bm((-0.037, 0.0, 1.185), (0.037, 0.050, 1.222))
        for bm_, nm, surf, bv in ((pb, f"pouch{k}", "nylon", 0.006), (pf, f"flap{k}", "nylon", 0.004)):
            rotate_bm(bm_, -ang, "Z")
            move_bm(bm_, (x, y0, 0.0))
            part(nm, bm_, surf, bevel=bv, segments=3)
    adm = bend(box_bm((-0.090, front + 0.042, 1.262), (0.090, front + 0.066, 1.345)), 0.30)
    part("admin", adm, "nylon", bevel=0.006, segments=3)
    # radio pouch + antenna on the left side, dump pouch on the right
    rx = -(half_w + 0.028)
    part("radio_pouch", box_bm((rx - 0.026, -0.045, 1.115), (rx + 0.026, 0.025, 1.255)), "nylon", bevel=0.006, segments=3)
    part("radio", box_bm((rx - 0.018, -0.030, 1.255), (rx + 0.018, 0.012, 1.285)), "sole", bevel=0.003)
    ant = tube_bm(0.0035, 0.0025, 0.0, 0.30, "z", 10, (0.0, 0.0))
    rotate_bm(ant, -12.0, "X"); rotate_bm(ant, 8.0, "Y")
    move_bm(ant, (rx, -0.015, 1.285))
    part("antenna", ant, "sole", bevel=0.0)
    part("dump_pouch", box_bm((half_w + 0.004, -0.030, 1.12), (half_w + 0.050, 0.050, 1.23)), "nylon", bevel=0.010, segments=3)
    # assault pack on the back
    pk = box_bm((-0.130, back - 0.165, 1.060), (0.130, back - 0.040, 1.420))
    part("pack", pk, "nylon", bevel=0.022, segments=4)
    for sx in (-1, 1):
        part(f"pack_strap{sx}", box_bm((sx * 0.090 - 0.012, back - 0.170, 1.08), (sx * 0.090 + 0.012, back - 0.160, 1.40)), "webbing", bevel=0.002)
    part("pack_pocket", box_bm((-0.100, back - 0.195, 1.10), (0.100, back - 0.160, 1.26)), "nylon", bevel=0.014, segments=3)

    # ---- battle belt, holster on the right thigh, knee pads
    zb = 0.955
    bw = max(S.side(0.0, zb, -1), S.side(0.0, zb, 1)) + 0.016
    bd = max(abs(S.front(0, zb)), abs(S.back(0, zb))) + 0.016
    part("belt", ellipse_ring(bw, bd, 0.925, 0.985, 0.014), "nylon", bevel=0.004)
    part("buckle", box_bm((-0.030, bd - 0.004, 0.930), (0.030, bd + 0.012, 0.980)), "sole", bevel=0.003)
    part("ifak", box_bm((-0.070, -bd - 0.060, 0.905), (0.070, -bd + 0.004, 0.995)), "nylon", bevel=0.012, segments=3)
    part("mag_belt", box_bm((-bw - 0.004 - 0.050, -0.020, 0.905), (-bw - 0.004, 0.050, 1.010)), "nylon", bevel=0.008, segments=3)
    hx = 0.100 + 0.092
    part("holster", box_bm((hx, -0.030, 0.640), (hx + 0.045, 0.080, 0.840)), "sole", bevel=0.010, segments=3)
    grip = box_bm((hx + 0.004, -0.020, 0.835), (hx + 0.040, 0.020, 0.915))
    rotate_bm(grip, 14.0, "X", (0, 0.0, 0.84))
    part("holster_grip", grip, "sole", bevel=0.006, segments=2)
    part("drop_strap", box_bm((hx - 0.010, -0.010, 0.880), (hx + 0.005, 0.030, 0.985)), "webbing", bevel=0.002)
    for z in (0.700, 0.790):
        tw = 0.100 + 0.004
        part(f"thigh_strap{z}", ellipse_ring(0.098, 0.096, z, z + 0.028, 0.006, 32, (0.100, 0.006)), "webbing", bevel=0.0015)
    for sx in (-1, 1):
        kx = sx * 0.100
        ky = S.front(kx, 0.505)
        pad = bend(box_bm((kx - 0.050, ky - 0.004, 0.450), (kx + 0.050, ky + 0.022, 0.575)), 0.07, axis_x=kx)
        part(f"kneepad{sx}", pad, "sole", bevel=0.010, segments=3)
        part(f"knee_strap{sx}", ellipse_ring(0.072, 0.074, 0.470, 0.488, 0.005, 32, (kx, 0.012)), "webbing", bevel=0.0012)

    # ---- boots: lofted foot (flat sole, rounded toe box), open shaft, laces, lugged sole
    for sx in (-1, 1):
        bx = sx * 0.107
        st = [(-0.094, 0.010, 0.080), (-0.091, 0.026, 0.098), (-0.085, 0.039, 0.112), (-0.060, 0.044, 0.128),
              (-0.020, 0.046, 0.140), (0.030, 0.049, 0.128), (0.070, 0.052, 0.100), (0.110, 0.052, 0.084),
              (0.150, 0.048, 0.076), (0.180, 0.042, 0.070), (0.198, 0.033, 0.064), (0.207, 0.020, 0.056), (0.211, 0.008, 0.048)]
        part(f"boot_foot{sx}", loft_bm([(y, w, 0.028, h) for y, w, h in st], centre_x=bx), "leather", bevel=0.0)
        part(f"boot_shaft{sx}", ellipse_ring(0.051, 0.057, 0.090, 0.240, 0.006, 32, (bx, -0.022)), "leather", bevel=0.002)
        part(f"boot_collar{sx}", ellipse_ring(0.055, 0.061, 0.222, 0.246, 0.005, 32, (bx, -0.022)), "webbing", bevel=0.002)
        part(f"boot_tongue{sx}", bend(box_bm((bx - 0.022, 0.026, 0.100), (bx + 0.022, 0.040, 0.236)), 0.05, axis_x=bx), "leather", bevel=0.003)
        for k in range(6):   # laces across the tongue
            z = 0.112 + 0.022 * k
            part(f"lace{sx}_{k}", bend(box_bm((bx - 0.027, 0.038, z), (bx + 0.027, 0.044, z + 0.005)), 0.05, axis_x=bx), "webbing", bevel=0.0012)
        sole = [(y, w + 0.004) for y, w, _ in st]
        outline = [(bx - w, y) for y, w in sole] + [(bx + w, y) for y, w in reversed(sole)]
        part(f"sole{sx}", prism_bm(outline, "z", 0.0, 0.030), "sole", bevel=0.004, segments=2)
        part(f"heel{sx}", box_bm((bx - 0.040, -0.090, -0.004), (bx + 0.040, -0.020, 0.002)), "sole", bevel=0.003)
        for k in range(5):   # lugs under the forefoot
            y = 0.030 + 0.034 * k
            part(f"lug{sx}_{k}", box_bm((bx - 0.042, y, -0.004), (bx + 0.042, y + 0.016, 0.002)), "sole", bevel=0.002)

    # ---- helmet, ear protection, NVG mount, glasses (rigid on the head), fitted to the head with a pad gap
    H = head_fit(body_ob)
    hc, ha, hb, hcz = H["centre"], H["a"], H["b"], H["c"]
    part("helmet", helmet_bm(H), "helmet", bevel=0.0)
    RIGID["helmet"] = "Head"
    part("helmet_rim", helmet_bm(H, rim_band=0.010, outset=0.003), "sole", bevel=0.0)
    RIGID["helmet_rim"] = "Head"

    def on_shell(bm, sx, gap=0.0):   # push a side piece (built at x = 0 .. thickness) onto the shell surface
        for v in bm.verts:
            v.co.x = sx * (ell_x(H, v.co.y, v.co.z) + gap + v.co.x)
        return bm
    for sx in (-1, 1):
        part(f"rail{sx}", on_shell(box_bm((-0.002, hc.y - 0.070, 1.676), (0.008, hc.y + 0.060, 1.690)), sx), "sole", bevel=0.002)
        RIGID[f"rail{sx}"] = "Head"
        ex = H["side"] + 0.004
        cup = tube_bm(0.041, 0.037, sx * ex, sx * (ex + 0.036), "x", 28, (hc.y - 0.010, 1.612))
        part(f"earcup{sx}", cup, "sole", bevel=0.006, segments=3); RIGID[f"earcup{sx}"] = "Head"
        part(f"earcup_pad{sx}", tube_bm(0.043, 0.043, sx * (ex - 0.006), sx * (ex + 0.004), "x", 28, (hc.y - 0.010, 1.612)), "balaclava", bevel=0.003)
        RIGID[f"earcup_pad{sx}"] = "Head"
        ax = ex + 0.030
        part(f"earcup_arm{sx}", box_bm((min(sx * ax, sx * (ax + 0.010)), hc.y - 0.018, 1.650), (max(sx * ax, sx * (ax + 0.010)), hc.y - 0.002, 1.684)), "sole", bevel=0.002)
        RIGID[f"earcup_arm{sx}"] = "Head"
    yf = ell_y(H, 0.0, 1.735)
    part("nvg_mount", box_bm((-0.022, yf - 0.006, 1.712), (0.022, yf + 0.016, 1.756)), "sole", bevel=0.004, segments=2)
    part("nvg_arm", box_bm((-0.010, yf + 0.010, 1.700), (0.010, yf + 0.030, 1.722)), "sole", bevel=0.003)
    RIGID["nvg_mount"] = RIGID["nvg_arm"] = "Head"
    yb = ell_y(H, 0.0, 1.700, back=True)
    part("helmet_patch", box_bm((-0.032, yb - 0.010, 1.684), (0.032, yb + 0.006, 1.722)), "team", bevel=0.002)
    part("battery_pack", box_bm((-0.040, yb - 0.026, 1.640), (0.040, yb + 0.004, 1.676)), "sole", bevel=0.005, segments=2)
    RIGID["helmet_patch"] = RIGID["battery_pack"] = "Head"
    nose = max(S.front(x, z) for x in (-0.006, 0.0, 0.006) for z in (1.610, 1.618, 1.626, 1.634))
    lens_y = nose + 0.004
    yo = min(S.front(x, 1.632) for x in (-0.060, 0.060))     # face front towards the outer eye corners
    R = min(0.10, max(0.045, 0.060 ** 2 / (2.0 * max(0.005, lens_y - (yo + 0.010)))))  # wrap so the lens clears the face by ~10 mm
    lens = [(-0.074, 1.614), (-0.016, 1.612), (0.0, 1.604), (0.016, 1.612), (0.074, 1.614), (0.078, 1.642), (0.060, 1.656), (-0.060, 1.656), (-0.078, 1.642)]
    part("glasses", bend(prism_bm(round_profile(lens, 0.008), "y", lens_y, lens_y + 0.004), R), "lens", bevel=0.0012)
    RIGID["glasses"] = "Head"
    for sx in (-1, 1):
        gx = S.side(hc.y + 0.02, 1.645, sx) + 0.004
        part(f"glasses_arm{sx}", box_bm((min(sx * gx, sx * (gx + 0.005)), hc.y - 0.020, 1.640), (max(sx * gx, sx * (gx + 0.005)), lens_y - 0.074 ** 2 / (2 * R) - 0.002, 1.648)), "lens", bevel=0.001)
        RIGID[f"glasses_arm{sx}"] = "Head"

    # shirt collar round the base of the neck
    nz = 1.478
    part("collar", ellipse_ring(S.side(-0.005, nz, 1) + 0.010, max(S.front(0, nz), -S.back(0, nz)) + 0.010, 1.452, 1.500, 0.008, 32, (0.0, -0.006)), "shirt", bevel=0.003)

    # ---- arms: team armbands, glove cuffs
    for sx in (-1, 1):
        part(f"armband{sx}", ellipse_ring(0.066, 0.064, sx * 0.265, sx * 0.315, 0.005, 32, (-0.020, 1.424), axis="x") if sx < 0
             else ellipse_ring(0.066, 0.064, sx * 0.315, sx * 0.265, 0.005, 32, (-0.020, 1.424), axis="x"), "team", bevel=0.0012)
        part(f"glove_cuff{sx}", ellipse_ring(0.034, 0.044, sx * 0.670, sx * 0.700, 0.006, 28, (-0.020, 1.425), axis="x") if sx < 0
             else ellipse_ring(0.034, 0.044, sx * 0.700, sx * 0.670, 0.006, 28, (-0.020, 1.425), axis="x"), "glove", bevel=0.002)
    return PARTS


def body_zones(ob):
    """Per-face surface on the base body: balaclava, shirt, trousers, gloves. Feet are cut away (boots replace them)."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0.20), plane_no=(0, 0, 1), clear_inner=True)
    names = list(SURFACES.keys())
    layer = bm.faces.layers.int.new("surface_id")
    for f in bm.faces:
        c = f.calc_center_median()
        if abs(c.x) > 0.690:
            s = "glove"
        elif c.z > 1.470 and abs(c.x) < 0.14:
            s = "balaclava"
        elif c.z >= 0.985 or abs(c.x) > 0.21:
            s = "shirt"
        else:
            s = "pants"
        f[layer] = names.index(s)
    bm.to_mesh(me)
    bm.free()


def preview_materials(body_ob):
    """Flat viewport colours per surface (Workbench previews only; the game uses the baked set)."""
    names = list(SURFACES.keys())
    mats = {}
    for n in names:
        m = bpy.data.materials.get("PV_" + n) or bpy.data.materials.new("PV_" + n)
        c = SURFACES[n][0]
        m.diffuse_color = (*[min(1.0, (v ** (1 / 2.2)) * 1.1) for v in c], 1.0)
        m.roughness = SURFACES[n][2]
        mats[n] = m
    for prt in PARTS:
        prt.ob.data.materials.clear()
        prt.ob.data.materials.append(mats[prt.surface])
    me = body_ob.data
    me.materials.clear()
    for n in names:
        me.materials.append(mats[n])
    attr = me.attributes["surface_id"].data
    for poly, a in zip(me.polygons, attr):
        poly.material_index = a.value


# --------------------------------------------------------------------------- rig + skin
def build():
    """Body, face, clothing zones and gear. Returns the body object."""
    wipe()
    RIGID.clear()
    b = body()
    sculpt_head(b)
    body_zones(b)
    gear(b)
    return b


def _side_of(x):
    return "Left" if x < 0 else "Right"


def _blend(t, a, b):
    t = max(0.0, min(1.0, t))
    return {a: 1.0 - t, b: t} if a != b else {a: 1.0}


def _spine(z):
    if z <= 1.12:
        return {"Spine": 1.0}
    if z <= 1.22:
        return _blend((z - 1.12) / 0.10, "Spine", "Spine1")
    if z <= 1.32:
        return _blend((z - 1.22) / 0.10, "Spine1", "Spine2")
    return {"Spine2": 1.0}


TORSO = ("plate_front", "plate_back", "molle", "cummerbund", "strap", "pouch", "flap", "admin", "radio_pouch", "radio",
         "antenna", "dump_pouch")
PACK = ("pack",)
BELT = ("belt", "buckle", "ifak", "mag_belt")
BOOT_FOOT = ("boot_foot", "sole", "heel", "lug")
BOOT_SHAFT = ("boot_shaft", "boot_collar", "boot_tongue", "lace")


def gear_rule(name, co):
    """Bone weights for a gear vertex by part name; None = take the body's weights under it."""
    if name in RIGID:
        return {RIGID[name]: 1.0}
    if name.startswith(PACK):
        return {"Spine2": 0.7, "Spine1": 0.3}
    if name.startswith("cummerbund"):
        return _spine(co.z)
    if name.startswith("strap"):
        return {"Spine2": 1.0}
    if name.startswith(TORSO):   # the plate carrier is stiff: one blended transform, so twisting does not shear it
        return {"Spine2": 0.6, "Spine1": 0.4}
    if name.startswith(BELT):
        return {"Hips": 1.0}
    s = _side_of(co.x)
    if name.startswith(("holster", "thigh_strap")):
        return _blend((co.z - 0.80) / 0.10, "RightUpLeg", "Hips") if co.z > 0.80 else {"RightUpLeg": 1.0}
    if name.startswith("drop_strap"):
        return _blend((co.z - 0.88) / 0.10, "RightUpLeg", "Hips")
    if name.startswith(("kneepad", "knee_strap")):
        return {s + "Leg": 1.0}
    if name.startswith(BOOT_FOOT):
        return _blend((co.y - 0.06) / 0.07, s + "Foot", s + "ToeBase")
    if name.startswith(BOOT_SHAFT):
        return _blend((co.z - 0.10) / 0.06, s + "Foot", s + "Leg")
    if name.startswith("armband"):
        return {s + "Arm": 1.0}
    return None


def _limit(weights, n=4):
    top = sorted(weights.items(), key=lambda kv: -kv[1])[:n]
    tot = sum(w for _, w in top)
    return {k: w / tot for k, w in top if w / tot > 1e-3} if tot > 0 else {}


def skin(b, arm):
    """Automatic (bone heat) weights for the body; gear copies them or follows gear_rule. Joins everything into
    one skinned mesh (the body object, renamed OP_Operator). Returns stats."""
    for bn in arm.data.bones:
        bn.use_deform = not (bn.name.endswith("_End") or bn.name[-1] == "4")
    bpy.ops.object.select_all(action="DESELECT")
    b.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    names = [g.name for g in b.vertex_groups]
    bw = []
    for v in b.data.vertices:
        bw.append({names[g.group]: g.weight for g in v.groups if g.weight > 1e-4})
    # vertices bone heat left empty take their nearest weighted neighbour's weights
    from mathutils.kdtree import KDTree
    good = [i for i, w in enumerate(bw) if w]
    kd = KDTree(len(good))
    for i in good:
        kd.insert(b.data.vertices[i].co, i)
    kd.balance()
    unweighted = 0
    for i, w in enumerate(bw):
        if not w:
            unweighted += 1
            bw[i] = dict(bw[kd.find(b.data.vertices[i].co)[1]])
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromObject(b, bpy.context.evaluated_depsgraph_get(), deform=False)
    polys = b.data.polygons
    verts = b.data.vertices

    def under(co):
        loc, _, idx, _ = bvh.find_nearest(co)
        acc = {}
        ws = []
        for vi in polys[idx].vertices:
            d = (verts[vi].co - loc).length
            ws.append((vi, 1.0 / max(d, 1e-5)))
        tot = sum(w for _, w in ws)
        for vi, w in ws:
            for k, x in bw[vi].items():
                acc[k] = acc.get(k, 0.0) + x * w / tot
        return acc

    # body: rewrite its groups limited to 4 influences
    for vg in list(b.vertex_groups):
        b.vertex_groups.remove(vg)
    groups = {}

    def group(obj, n):
        key = (obj.name, n)
        if key not in groups:
            groups[key] = obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n)
        return groups[key]
    for i, w in enumerate(bw):
        for k, x in _limit(w).items():
            group(b, k).add([i], x, "REPLACE")

    sid = list(SURFACES.keys())
    copies = []
    rule_count, transfer_count = 0, 0
    for prt in PARTS:
        c = evaluated_copy(prt.ob, "J_" + prt.ob.name)
        c.data.transform(prt.ob.matrix_world)
        c.matrix_world = Matrix.Identity(4)
        bpy.context.scene.collection.objects.link(c)
        a = c.data.attributes.get("surface_id") or c.data.attributes.new("surface_id", "INT", "FACE")
        a.data.foreach_set("value", [sid.index(prt.surface)] * len(c.data.polygons))
        for v in c.data.vertices:
            w = gear_rule(prt.ob.name, v.co)
            if w is None:
                w = under(v.co)
                transfer_count += 1
            else:
                rule_count += 1
            for k, x in _limit(w).items():
                group(c, k).add([v.index], x, "REPLACE")
        copies.append(c)
    # UVs before the join: the organic body gets large islands (wide angle), hard gear the usual split
    _smart_project([b], 72.0)
    _smart_project(copies, 50.0)
    # join copies into the body
    bpy.ops.object.select_all(action="DESELECT")
    for c in copies:
        c.select_set(True)
    b.select_set(True)
    bpy.context.view_layer.objects.active = b
    bpy.ops.object.join()
    b.name = b.data.name = "OP_Operator"
    for prt in PARTS:   # the originals are no longer needed
        me = prt.ob.data
        bpy.data.objects.remove(prt.ob)
        if me.users == 0:
            bpy.data.meshes.remove(me)
    PARTS.clear()
    return {"body_unweighted_fixed": unweighted, "gear_verts_rule": rule_count, "gear_verts_transfer": transfer_count,
            "verts": len(b.data.vertices), "tris": sum(len(p.vertices) - 2 for p in b.data.polygons),
            "groups": len(b.vertex_groups)}


def _smart_project(objs, angle):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        ensure_uv(o)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=0.002, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def pack_uvs(ob, margin=0.002):
    """One texel density for everything, then pack."""
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")


def pose(arm, rotations):
    """Pose bones by world-space rotations {bone: (axis 'X'|'Y'|'Z', degrees)}; empty dict = rest."""
    from mathutils import Matrix as M
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
    for n, (axis, deg) in rotations.items():
        pb = arm.pose.bones[n]
        rest = pb.bone.matrix_local.to_quaternion()
        rw = M.Rotation(math.radians(deg), 4, axis).to_quaternion()
        pb.rotation_quaternion = rest.inverted() @ rw @ rest
    bpy.context.view_layer.update()


POSE_TESTS = {
    "arms_down": {"LeftArm": ("Y", -70), "RightArm": ("Y", 70), "LeftForeArm": ("Z", -50), "RightForeArm": ("Z", 50)},
    "crouch": {"LeftUpLeg": ("X", 75), "RightUpLeg": ("X", 40), "LeftLeg": ("X", -110), "RightLeg": ("X", -80),
               "Spine1": ("X", -15), "Spine2": ("X", -15), "LeftArm": ("Y", -55), "RightArm": ("Y", 55)},
    "twist": {"Spine1": ("Z", 25), "Spine2": ("Z", 20), "Head": ("Z", -40), "LeftArm": ("Z", -60), "RightArm": ("Z", 60),
              "LeftForeArm": ("Z", -70), "RightForeArm": ("Z", 70), "LeftHand": ("X", 30)},
}


# The balaclava's eye slot: skin painted into the knit with a soft oval edge (the face mesh's own polygons
# would give a stair-stepped edge). Centre and half-size in metres (x across, z up), front-facing only.
EYE_SLOT = (1.634, 0.072, 0.027)


def _eye_opening(nt):
    tc = next(n for n in nt.nodes if n.type == "TEX_COORD")
    geo = next(n for n in nt.nodes if n.type == "NEW_GEOMETRY")

    def math(op, a, b):
        n = nt.nodes.new("ShaderNodeMath"); n.operation = op
        for i, v in enumerate((a, b)):
            if isinstance(v, (int, float)): n.inputs[i].default_value = v
            else: nt.links.new(v, n.inputs[i])
        return n.outputs[0]

    def remap(v, a, b, c, d):
        n = nt.nodes.new("ShaderNodeMapRange"); n.clamp = True
        nt.links.new(v, n.inputs["Value"])
        n.inputs["From Min"].default_value, n.inputs["From Max"].default_value = a, b
        n.inputs["To Min"].default_value, n.inputs["To Max"].default_value = c, d
        return n.outputs["Result"]

    def mix(fac, a, b, name):
        m = nt.nodes.new("ShaderNodeMix"); m.data_type = "RGBA"
        nt.links.new(fac, m.inputs[0]); nt.links.new(a, m.inputs[6]); nt.links.new(b, m.inputs[7])
        g = nt.nodes.new("ShaderNodeGamma"); g.inputs["Gamma"].default_value = 1.0   # colour passthrough: the bake
        nt.links.new(m.outputs[2], g.inputs["Color"])                                 # passes read outputs[0]
        g.name = name
        return g

    cz, ax, az = EYE_SLOT
    p = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(tc.outputs["Object"], p.inputs[0])
    nrm = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["Normal"], nrm.inputs[0])
    r2 = math("ADD", math("POWER", math("DIVIDE", p.outputs["X"], ax), 2.0),
              math("POWER", math("DIVIDE", math("SUBTRACT", p.outputs["Z"], cz), az), 2.0))
    mask = math("MULTIPLY", remap(r2, 0.62, 1.0, 1.0, 0.0), remap(nrm.outputs["Y"], 0.12, 0.40, 0.0, 1.0))

    base, params = nt.nodes["P_base"], nt.nodes["P_params"]
    base.name, params.name = "P_base_knit", "P_params_knit"
    sb, sm, sr, sw, _ = SURFACES["skin"]
    skin = nt.nodes.new("ShaderNodeRGB"); skin.outputs[0].default_value = (*sb, 1)
    skin_p = nt.nodes.new("ShaderNodeRGB"); skin_p.outputs[0].default_value = (sm, sr, sw, 1)
    mix(mask, base.outputs[0], skin.outputs[0], "P_base")
    mix(mask, params.outputs[0], skin_p.outputs[0], "P_params")


# fabric folds / nap baked into the normal map: (depth m, noise scale)
CHAR_BUMP = {"shirt": (0.009, 16.0), "pants": (0.009, 12.0), "balaclava": (0.004, 30.0), "skin": (0.0006, 140.0), "glove": (0.003, 45.0),
             "nylon": (0.0015, 90.0), "webbing": (0.001, 200.0)}


def finalize_operator(out_dir, res=2048, lod_ratios=(0.5, 0.22)):
    """Build, rig, skin, unwrap, bake (T_OP_D/N/M) and export SK_Operator.fbx with three LODs. Returns stats."""
    global AO_DISTANCE
    t0 = time.time()
    b = build()
    arm = armature()
    st = skin(b, arm)
    st["skin_seconds"] = round(time.time() - t0, 1)
    pack_uvs(b)
    AO_DISTANCE = 0.08      # character scale: folds, under the plates and pouches
    BUMP.clear()
    BUMP.update(CHAR_BUMP)
    MATERIAL_HOOKS["balaclava"] = _eye_opening
    try:
        st.update(bake_weapon(b, res, out_dir, "OP"))
    finally:
        BUMP.clear()
        MATERIAL_HOOKS.clear()
        AO_DISTANCE = 0.035
    if st["uv_coverage"] < 0.15:
        raise RuntimeError(f"operator bake covered only {st['uv_coverage']:.1%} of the texture")
    # two material slots: everything, and the team-coloured marks (same textures, tinted in the game)
    names = list(SURFACES.keys())
    team = names.index("team")
    me = b.data
    me.materials.clear()
    me.materials.append(_placeholder_material("M_OP_Main"))
    me.materials.append(_placeholder_material("M_OP_Team"))
    for poly, a in zip(me.polygons, me.attributes["surface_id"].data):
        poly.material_index = 1 if a.value == team else 0
    b.name = "OP_Operator_LOD0"
    lods = [b]
    dg = bpy.context.evaluated_depsgraph_get
    for k, r in enumerate(lod_ratios, 1):
        c = b.copy()
        c.data = b.data.copy()
        c.name = f"OP_Operator_LOD{k}"
        bpy.context.scene.collection.objects.link(c)
        c.modifiers["Armature"].show_viewport = False
        dec = c.modifiers.new("Decimate", "DECIMATE")
        dec.decimate_type = "COLLAPSE"
        dec.ratio = r
        dec.use_collapse_triangulate = True
        flat = bpy.data.meshes.new_from_object(c.evaluated_get(dg()))
        old = c.data
        c.modifiers.remove(dec)
        c.data = flat
        flat.name = c.name
        bpy.data.meshes.remove(old)
        c.modifiers["Armature"].show_viewport = True
        lods.append(c)
    st["tris_lod"] = [sum(len(p.vertices) - 2 for p in o.data.polygons) for o in lods]
    st["lod_groups_ok"] = [len(o.vertex_groups) == len(b.vertex_groups) and any(v.groups for v in o.data.vertices) for o in lods]
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "SK_Operator.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for o in lods + [arm]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, mesh_smooth_type="OFF",
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False,
        armature_nodetype="NULL", bake_anim=False, path_mode="STRIP", embed_textures=False, use_triangles=True,
        use_custom_props=False)
    st["fbx"] = path
    st["total_seconds"] = round(time.time() - t0, 1)
    return st
