"""
Hard-surface modelling, texture baking and export helpers for the SALTLINE weapons.
Executed (exec) by weapons_build.py inside Blender; not an add-on.

Conventions (match the game's GreyboxGun and the Kappa export):
  Blender +X = weapon right, +Y = forward (towards the muzzle), +Z = up, metres.
  Origin = the weapon's reference point (receiver centre); the game positions the gun by it.

Every part is a mesh object built from side profiles, prisms and tubes, cut with boolean
cutters, then bevelled (Bevel + Weighted Normal modifiers) so edges catch light.
Each part has a "surface" (steel, polymer, ...); the bake turns surfaces + baked edges, AO and
noise into one texture set per weapon: albedo, normal (with rounded-edge detail) and a mask
(R metallic, G occlusion, A smoothness) - the same layout as the map's CC0 materials.
"""
import math
import os
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------- surfaces
# name -> (base colour (linear), metallic, roughness, edge wear 0..1, bare colour (linear))
SURFACES = {
    "steel":    ((0.030, 0.031, 0.033), 0.85, 0.55, 0.85, (0.50, 0.50, 0.50)),   # parkerised / nitride steel
    "alloy":    ((0.040, 0.040, 0.042), 0.70, 0.52, 0.95, (0.62, 0.62, 0.63)),   # black anodised aluminium
    "polymer":  ((0.022, 0.022, 0.022), 0.00, 0.62, 0.15, (0.06, 0.06, 0.06)),   # glass-filled nylon
    "rubber":   ((0.015, 0.015, 0.015), 0.00, 0.85, 0.00, (0.02, 0.02, 0.02)),
    "bare":     ((0.42, 0.42, 0.43), 1.00, 0.30, 0.00, (0.55, 0.55, 0.55)),     # polished steel: bolt, barrel crown
    "brass":    ((0.62, 0.42, 0.16), 1.00, 0.28, 0.00, (0.70, 0.50, 0.22)),
    "accent_fde": ((0.21, 0.155, 0.085), 0.10, 0.55, 0.80, (0.45, 0.45, 0.45)),  # flat dark earth cerakote
    "polymer_fde": ((0.20, 0.15, 0.085), 0.00, 0.64, 0.25, (0.31, 0.25, 0.17)),  # tan glass-filled polymer (magazines)
    "accent_od":  ((0.075, 0.085, 0.055), 0.10, 0.55, 0.80, (0.45, 0.45, 0.45)),  # olive drab cerakote
    "accent_grey": ((0.11, 0.115, 0.12), 0.20, 0.50, 0.80, (0.50, 0.50, 0.50)),   # tungsten grey cerakote
    "red":      ((0.40, 0.02, 0.015), 0.00, 0.40, 0.40, (0.45, 0.45, 0.45)),    # safety markings
    "glass":    ((0.02, 0.03, 0.03), 0.00, 0.05, 0.00, (0.02, 0.03, 0.03)),
}

PARTS = []       # every part object, in build order
BUMP = {}        # surface name -> (wrinkle depth m, wrinkle scale): fabric folds baked into the normal map
MATERIAL_HOOKS = {}   # surface name -> fn(node_tree): may replace the "P_base" / "P_params" nodes (e.g. painted masks)
CUTTERS = None   # hidden collection of boolean cutters


# --------------------------------------------------------------------------- scene
def wipe():
    global CUTTERS
    CUTTERS = None   # its collection is removed below; a stale reference would raise on the next cut
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.curves,
                 bpy.data.cameras, bpy.data.lights, bpy.data.node_groups):
        for b in list(coll):
            coll.remove(b)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)
    PARTS.clear()


def collection(name, parent=None, hidden=False):
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if c.name not in (parent or bpy.context.scene.collection).children:
        (parent or bpy.context.scene.collection).children.link(c)
    c.hide_render = hidden
    c.hide_viewport = hidden
    return c


def cutters():
    global CUTTERS
    if CUTTERS is None or CUTTERS.name not in bpy.data.collections:
        CUTTERS = collection("CUTTERS", hidden=True)
    return CUTTERS


# --------------------------------------------------------------------------- mesh primitives
def _mesh_object(name, bm, coll):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def _plane_axes(axis):
    """For a prism extruded along `axis`, the 2D profile axes (u, v) and the extrusion axis."""
    if axis == "x":   # side profile: u = y (forward), v = z (up)
        return Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))
    if axis == "y":   # cross-section: u = x, v = z
        return Vector((1, 0, 0)), Vector((0, 0, 1)), Vector((0, 1, 0))
    return Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))  # top view: u = x, v = y


def prism_bm(pts, axis, a0, a1):
    u, v, w = _plane_axes(axis)
    bm = bmesh.new()
    ring0 = [bm.verts.new(u * p[0] + v * p[1] + w * a0) for p in pts]
    ring1 = [bm.verts.new(u * p[0] + v * p[1] + w * a1) for p in pts]
    n = len(pts)
    bm.faces.new(ring0)
    bm.faces.new(list(reversed(ring1)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def tube_bm(r0, r1, a0, a1, axis="y", seg=32, centre=(0, 0), r_in=None, ngon=False):
    """Cylinder/cone along an axis, optionally hollow (r_in) - barrels, bells, tubes."""
    u, v, w = _plane_axes(axis)
    bm = bmesh.new()
    c = u * centre[0] + v * centre[1]

    def ring(r, a, phase=0.0):
        return [bm.verts.new(c + w * a + (u * math.cos(2 * math.pi * k / seg + phase) + v * math.sin(2 * math.pi * k / seg + phase)) * r)
                for k in range(seg)]
    phase = math.pi / seg if ngon else 0.0
    o0, o1 = ring(r0, a0, phase), ring(r1, a1, phase)
    for k in range(seg):
        j = (k + 1) % seg
        bm.faces.new((o0[k], o0[j], o1[j], o1[k]))
    if r_in:
        i0, i1 = ring(r_in, a0, phase), ring(r_in, a1, phase)
        for k in range(seg):
            j = (k + 1) % seg
            bm.faces.new((i1[k], i1[j], i0[j], i0[k]))
            bm.faces.new((o0[j], o0[k], i0[k], i0[j]))
            bm.faces.new((o1[k], o1[j], i1[j], i1[k]))
    else:
        bm.faces.new(list(reversed(o0)))
        bm.faces.new(o1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def box_bm(mn, mx):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    size = Vector(mx) - Vector(mn)
    centre = (Vector(mx) + Vector(mn)) * 0.5
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=centre, verts=bm.verts)
    return bm


def round_profile(pts, radius, steps=3):
    """Round the corners of a 2D polygon (fillet) so receivers and grips are not razor-edged."""
    if radius <= 0:
        return pts
    out = []
    n = len(pts)
    for i in range(n):
        p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
        d0, d1 = (p0 - p1), (p2 - p1)
        l0, l1 = d0.length, d1.length
        if l0 < 1e-6 or l1 < 1e-6:
            continue
        r = min(radius, l0 * 0.45, l1 * 0.45)
        a, b = p1 + d0.normalized() * r, p1 + d1.normalized() * r
        for s in range(steps + 1):
            t = s / steps
            q = (1 - t) ** 2 * a + 2 * (1 - t) * t * p1 + t ** 2 * b   # quadratic Bezier corner
            out.append((q.x, q.y))
    return out


# --------------------------------------------------------------------------- parts
class Part:
    def __init__(self, ob, surface, group):
        self.ob, self.surface, self.group = ob, surface, group


def part(name, bm, surface, group="Body", coll=None, bevel=0.0012, segments=2, angle=35.0, sharp=True):
    """Make a part object with bevel + weighted normals. `group` names the exported object it joins."""
    ob = _mesh_object(name, bm, coll or bpy.context.scene.collection)
    ob["surface"] = surface
    ob["group"] = group
    for p in ob.data.polygons:
        p.use_smooth = True
    if bevel > 0:
        bv = ob.modifiers.new("Bevel", "BEVEL")
        bv.width = bevel
        bv.segments = segments
        bv.limit_method = "ANGLE"
        bv.angle_limit = math.radians(angle)
        bv.use_clamp_overlap = True
        bv.harden_normals = True
        bv.miter_outer = "MITER_ARC"
    wn = ob.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    wn.keep_sharp = sharp
    wn.weight = 50
    PARTS.append(Part(ob, surface, group))
    return ob


def cut(target, bm, name=None, solver="EXACT"):
    """Boolean-subtract a cutter mesh from a part (inserted before the bevel, so cuts get bevelled too)."""
    c = _mesh_object(name or (target.name + "_cut"), bm, cutters())
    c.display_type = "WIRE"
    mod = target.modifiers.new("Cut_" + c.name, "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = solver
    mod.object = c
    # keep every boolean ahead of the bevel / weighted normal
    first_other = next((i for i, m in enumerate(target.modifiers) if m.type != "BOOLEAN"), None)
    if first_other is not None:
        target.modifiers.move(len(target.modifiers) - 1, first_other)
    return c


def array_bm(make, count, step):
    """Union of `count` copies of a cutter shape, offset by `step` - slots, serrations, flutes."""
    out = bmesh.new()
    for i in range(count):
        bm = make(i)
        bmesh.ops.translate(bm, vec=Vector(step) * i, verts=bm.verts)
        tmp = bpy.data.meshes.new("tmp")
        bm.to_mesh(tmp)
        bm.free()
        out.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    return out


def rotate_bm(bm, angle_deg, axis, pivot=(0, 0, 0)):
    m = Matrix.Translation(Vector(pivot)) @ Matrix.Rotation(math.radians(angle_deg), 4, axis) @ Matrix.Translation(-Vector(pivot))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return bm


def move_bm(bm, offset):
    bmesh.ops.translate(bm, vec=Vector(offset), verts=bm.verts)
    return bm


def empty(name, loc, parent_name=None, size=0.01):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = size
    e.location = Vector(loc)
    bpy.context.scene.collection.objects.link(e)
    e["marker"] = True
    return e


# --------------------------------------------------------------------------- common details
def shape_x(bm, scale_of):
    """Scale vertex x by scale_of(y, z): tapers prisms (grips narrower at the toe, stocks at the bottom)."""
    for v in bm.verts:
        v.co.x *= scale_of(v.co.y, v.co.z)
    return bm


def side_box(sx, surface_x, depth, y0, y1, z0, z1, beyond=0.004):
    """Cutter that removes `depth` from the side face at x = sx * surface_x (sx = -1 left, +1 right)."""
    inner, outer = surface_x - depth, surface_x + beyond
    if sx > 0:
        return box_bm((inner, y0, z0), (outer, y1, z1))
    return box_bm((-outer, y0, z0), (-inner, y1, z1))


def picatinny(name, y0, y1, z_base, surface="alloy", group="Body", width=0.0212, height=0.0095, slot_pitch=0.01, x=0.0):
    """MIL-STD-1913-style rail: dovetail cross-section, cross slots every 10 mm."""
    hw = width / 2
    prof = [(-hw * 0.70, 0.0), (hw * 0.70, 0.0), (hw * 0.70, height * 0.25), (hw, height * 0.55),
            (hw * 0.80, height), (-hw * 0.80, height), (-hw, height * 0.55), (-hw * 0.70, height * 0.25)]
    prof = [(p[0] + x, p[1] + z_base) for p in prof]
    ob = part(name, prism_bm(prof, "y", y0, y1), surface, group, bevel=0.0005)
    n = max(1, int((y1 - y0 - 0.006) / slot_pitch))
    start = y0 + (y1 - y0 - (n - 1) * slot_pitch) / 2
    cut(ob, array_bm(lambda i: box_bm((x - width, start - 0.0026, z_base + height * 0.42), (x + width, start + 0.0026, z_base + height * 1.2)), n, (0, slot_pitch, 0)), name + "_slots")
    return ob


def screw(name, centre, axis="x", r=0.0022, depth=0.0015, surface="steel", group="Body"):
    a = {"x": 0, "y": 1, "z": 2}[axis]
    c = list(centre)
    lo, hi = c[a] - depth / 2, c[a] + depth / 2
    if axis == "x":
        bm = tube_bm(r, r, lo, hi, "x", 12, (c[1], c[2]))
    elif axis == "y":
        bm = tube_bm(r, r, lo, hi, "y", 12, (c[0], c[2]))
    else:
        bm = tube_bm(r, r, lo, hi, "z", 12, (c[0], c[1]))
    ob = part(name, bm, surface, group, bevel=0.0004, segments=1)
    # hex socket
    s = 0.45 * r
    if axis == "x":
        hb = tube_bm(s, s, hi - depth * 0.6 if c[0] > 0 else lo - 0.001, hi + 0.001 if c[0] > 0 else lo + depth * 0.6, "x", 6, (c[1], c[2]))
        cut(ob, hb, name + "_hex")
    return ob


# --------------------------------------------------------------------------- finalise / bake / export
def evaluated_copy(ob, name):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    me.name = name
    out = bpy.data.objects.new(name, me)
    out.matrix_world = ob.matrix_world
    return out


def join_meshes(objs, name, coll, triangulate=True):
    """
    Join evaluated copies of `objs` (modifiers applied) into one new object. Each face keeps a
    "surface_id" (index into SURFACES): from the part's surface, or from its own attribute
    when the source is already a joined group.
    """
    names = list(SURFACES.keys())
    bm = bmesh.new()
    for o in objs:
        tmp = evaluated_copy(o, "tmp_" + o.name)
        tmp.data.transform(o.matrix_world)
        attr = tmp.data.attributes.get("surface_id")
        ids = [a.value for a in attr.data] if attr is not None and attr.domain == "FACE" else [names.index(o["surface"])] * len(tmp.data.polygons)
        start = len(bm.faces)
        bm.from_mesh(tmp.data)
        layer = bm.faces.layers.int.get("surface_id") or bm.faces.layers.int.new("surface_id")
        bm.faces.ensure_lookup_table()
        for f, v in zip(bm.faces[start:], ids):
            f[layer] = v
        me = tmp.data
        bpy.data.objects.remove(tmp)
        bpy.data.meshes.remove(me)
    if triangulate:
        bmesh.ops.triangulate(bm, faces=bm.faces)
    return _mesh_object(name, bm, coll)


def ensure_uv(ob, name="UVMap"):
    if not ob.data.uv_layers:
        ob.data.uv_layers.new(name=name)
    return ob.data.uv_layers[0]


def unwrap_together(objs, margin=0.003):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        ensure_uv(o)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=margin, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")


def _bake_material(name, sid):
    """Material whose emission can be switched between the bake passes."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bevel = nt.nodes.new("ShaderNodeBevel")
    bevel.samples = 16
    bevel.inputs["Radius"].default_value = 0.0012
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    tex = nt.nodes.new("ShaderNodeTexCoord")
    img = nt.nodes.new("ShaderNodeTexImage")
    img.name = "BAKE_TARGET"
    nt.nodes.active = img
    nt.links.new(bevel.outputs["Normal"], bsdf.inputs["Normal"])
    sname = list(SURFACES.keys())[sid]
    if sname in BUMP:   # fabric: soft folds plus a fine nap, fed through the bevel so edges still round off
        depth, scale = BUMP[sname]
        tc = nt.nodes.new("ShaderNodeTexCoord")
        folds = nt.nodes.new("ShaderNodeTexNoise"); folds.inputs["Scale"].default_value = scale
        folds.inputs["Detail"].default_value = 3.0; folds.inputs["Distortion"].default_value = 0.8
        nap = nt.nodes.new("ShaderNodeTexNoise"); nap.inputs["Scale"].default_value = scale * 30.0; nap.inputs["Detail"].default_value = 2.0
        nt.links.new(tc.outputs["Object"], folds.inputs["Vector"]); nt.links.new(tc.outputs["Object"], nap.inputs["Vector"])
        mix = nt.nodes.new("ShaderNodeMath"); mix.operation = "MULTIPLY_ADD"
        nt.links.new(nap.outputs["Fac"], mix.inputs[0]); mix.inputs[1].default_value = 0.08
        nt.links.new(folds.outputs["Fac"], mix.inputs[2])
        bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Distance"].default_value = depth
        nt.links.new(mix.outputs[0], bump.inputs["Height"])
        nt.links.new(bump.outputs["Normal"], bevel.inputs["Normal"])
    base, metal, rough, wear, bare = SURFACES[sname]
    # pass values
    rgb_base = nt.nodes.new("ShaderNodeRGB"); rgb_base.name = "P_base"; rgb_base.outputs[0].default_value = (*base, 1)
    rgb_par = nt.nodes.new("ShaderNodeRGB"); rgb_par.name = "P_params"; rgb_par.outputs[0].default_value = (metal, rough, wear, 1)
    # edge: 1 - dot(bevelled normal, true normal), amplified
    dot = nt.nodes.new("ShaderNodeVectorMath"); dot.operation = "DOT_PRODUCT"
    nt.links.new(bevel.outputs["Normal"], dot.inputs[0]); nt.links.new(geo.outputs["Normal"], dot.inputs[1])
    sub = nt.nodes.new("ShaderNodeMath"); sub.operation = "SUBTRACT"; sub.inputs[0].default_value = 1.0
    nt.links.new(dot.outputs["Value"], sub.inputs[1])
    amp = nt.nodes.new("ShaderNodeMath"); amp.operation = "MULTIPLY"; amp.inputs[1].default_value = 6.0; amp.use_clamp = True
    amp.name = "P_edge"
    nt.links.new(sub.outputs[0], amp.inputs[0])
    # noise: fine, grime, scratches (object space, so no UV seams)
    def noise(scale, detail, name, stretch=None):
        n = nt.nodes.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale; n.inputs["Detail"].default_value = detail
        n.name = name
        if stretch:
            mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = stretch
            nt.links.new(tex.outputs["Object"], mp.inputs["Vector"]); nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
        else:
            nt.links.new(tex.outputs["Object"], n.inputs["Vector"])
        return n
    n1 = noise(900.0, 4.0, "N_fine")
    n2 = noise(30.0, 6.0, "N_grime")
    n3 = noise(1400.0, 2.0, "N_scratch", stretch=(1.0, 0.07, 1.0))   # ~0.7 mm wide, ~1 cm long, along the bore
    comb = nt.nodes.new("ShaderNodeCombineColor"); comb.name = "P_noise"
    nt.links.new(n1.outputs["Fac"], comb.inputs[0]); nt.links.new(n2.outputs["Fac"], comb.inputs[1]); nt.links.new(n3.outputs["Fac"], comb.inputs[2])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    hook = MATERIAL_HOOKS.get(sname)
    if hook:
        hook(nt)
    m["sid"] = sid
    return m


def _set_pass(mat, which):
    nt = mat.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    emit = next(n for n in nt.nodes if n.type == "EMISSION")
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    for l in list(emit.inputs["Color"].links):
        nt.links.remove(l)
    for l in list(out.inputs["Surface"].links):
        nt.links.remove(l)
    if which == "normal":
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        return
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    src = {"base": nt.nodes["P_base"].outputs[0], "params": nt.nodes["P_params"].outputs[0],
           "edge": nt.nodes["P_edge"].outputs[0], "noise": nt.nodes["P_noise"].outputs[0]}[which]
    nt.links.new(src, emit.inputs["Color"])


def bake_weapon(ob, res, out_dir, tag, samples=16):
    """Bake one joined weapon mesh (with surface_id face attribute) into albedo / normal / mask PNGs.
    Every other object is hidden from the bake: the part objects the bake mesh was joined from sit in
    the same place, and AO rays hitting them would darken every surface."""
    scene = bpy.context.scene
    others = [o for o in scene.objects if o is not ob and not o.hide_render]
    for o in others:
        o.hide_render = True
    try:
        return _bake_weapon(ob, res, out_dir, tag, samples)
    finally:
        for o in others:
            o.hide_render = False


def _bake_weapon(ob, res, out_dir, tag, samples):
    scene = bpy.context.scene
    # AO looks this far for occluders. Weapon-scale: crevices and recesses darken, open faces stay white.
    if scene.world is None:
        scene.world = bpy.data.worlds.new("BakeWorld")
    scene.world.light_settings.distance = AO_DISTANCE
    scene.render.engine = "CYCLES"
    scene.cycles.device = "GPU"
    scene.cycles.samples = samples
    scene.render.bake.margin = 16
    scene.render.bake.use_clear = True

    # one bake material per surface present
    names = list(SURFACES.keys())
    attr = ob.data.attributes["surface_id"].data
    present = sorted({a.value for a in attr})
    ob.data.materials.clear()
    mats = []
    for sid in present:
        m = _bake_material(f"BAKE_{tag}_{names[sid]}", sid)
        ob.data.materials.append(m)
        mats.append(m)
    index_of = {sid: i for i, sid in enumerate(present)}
    for poly, a in zip(ob.data.polygons, attr):
        poly.material_index = index_of[a.value]

    def image(name):
        im = bpy.data.images.get(name) or bpy.data.images.new(name, res, res, alpha=True, float_buffer=True)
        im.colorspace_settings.name = "Non-Color"
        return im

    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    results = {}
    timings = {}
    for which, btype in (("base", "EMIT"), ("params", "EMIT"), ("edge", "EMIT"), ("noise", "EMIT"), ("ao", "AO"), ("normal", "NORMAL")):
        im = image(f"BAKE_{tag}_{which}")
        for m in mats:
            m.node_tree.nodes["BAKE_TARGET"].image = im
            m.node_tree.nodes.active = m.node_tree.nodes["BAKE_TARGET"]
            _set_pass(m, "normal" if which in ("ao", "normal") else which)
        scene.cycles.samples = 64 if which == "ao" else (16 if which in ("edge", "normal") else 1)
        t0 = time.time()
        if btype == "NORMAL":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y", normal_b="POS_Z", margin=16)
        elif btype == "AO":
            bpy.ops.object.bake(type="AO", margin=16)
        else:
            bpy.ops.object.bake(type="EMIT", margin=16)
        px = np.empty(res * res * 4, dtype=np.float32)
        im.pixels.foreach_get(px)
        results[which] = px.reshape(res, res, 4)
        timings[which] = round(time.time() - t0, 1)

    base = results["base"][..., :3]
    metal, rough, wear = results["params"][..., 0], results["params"][..., 1], results["params"][..., 2]
    edge = results["edge"][..., 0]
    fine, grime, scratch = results["noise"][..., 0], results["noise"][..., 1], results["noise"][..., 2]
    ao = np.clip(results["ao"][..., 0], 0, 1)

    # bare colour per texel: the surface whose (base colour, metallic, roughness) is nearest
    bare = np.zeros_like(base)
    best = np.full(base.shape[:2], np.inf, dtype=np.float32)
    for n in names:
        b0, m0, r0, _, bc = SURFACES[n]
        d = ((base - np.array(b0, dtype=np.float32)) ** 2).sum(-1) + 0.25 * (metal - m0) ** 2 + 0.25 * (rough - r0) ** 2
        closer = d < best
        best[closer] = d[closer]
        bare[closer] = bc

    convex = np.clip((ao - 0.55) * 2.5, 0, 1)                               # only exposed edges wear
    worn = np.clip((edge * 2.2 - 0.2) * (0.5 + 1.0 * fine), 0, 1) * wear * convex
    scratches = np.clip((scratch - 0.70) * 8.0, 0, 1) * wear * 0.35 * convex
    worn = np.clip(worn + scratches * (0.4 + fine), 0, 1)
    grime_amt = np.clip((1 - ao) * 0.9 + (grime - 0.5) * 0.35, 0, 1)

    albedo = base * (0.88 + 0.24 * fine[..., None]) * (0.92 + 0.16 * grime[..., None])
    albedo = albedo * (1 - 0.45 * grime_amt[..., None])
    albedo = albedo * (1 - worn[..., None]) + bare * worn[..., None]
    metallic = metal * (1 - worn) + 1.0 * worn
    roughness = np.clip(rough + (fine - 0.5) * 0.14 + (grime - 0.5) * 0.12 + grime_amt * 0.12 - worn * 0.18, 0.05, 1.0)

    def srgb(c):
        c = np.clip(c, 0, 1)
        return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)

    def save(name, rgba):
        im = bpy.data.images.new(name, res, res, alpha=True, float_buffer=False)
        im.colorspace_settings.name = "Non-Color"
        im.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
        im.filepath_raw = os.path.join(out_dir, name + ".png")
        im.file_format = "PNG"
        im.save()
        bpy.data.images.remove(im)

    os.makedirs(out_dir, exist_ok=True)
    one = np.ones_like(ao)
    save(f"T_{tag}_D", np.dstack([srgb(albedo), one]))
    save(f"T_{tag}_N", np.dstack([results["normal"][..., :3], one]))
    save(f"T_{tag}_M", np.dstack([metallic, ao, np.zeros_like(ao), 1 - roughness]))
    for which in results:
        bpy.data.images.remove(bpy.data.images[f"BAKE_{tag}_{which}"])
    used = results["base"][..., :3].sum(-1) > 1e-4   # baked texels (the clear colour is black)
    pct = lambda x: [round(float(v), 3) for v in np.percentile(x[used], [10, 50, 90, 99])] if used.any() else []
    return {"uv_coverage": round(float(used.mean()), 3), "worn_fraction": round(float((worn[used] > 0.5).mean()), 4),
            "ao_p10_50_90_99": pct(ao), "edge_p10_50_90_99": pct(edge), "grime_amt_p50": pct(grime_amt)[1:2],
            "bake_seconds": timings}


def _placeholder_material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    return m


UNBAKED_GROUPS = ("Lens", "Reticle")
AO_DISTANCE = 0.035  # metres


# Largest sane extent of a weapon's first-person meshes (metres): x = width, y = length, z = height.
# A boolean or bevel that blows up throws geometry far outside this; fail instead of exporting it.
ENVELOPE = (0.16, 1.40, 0.36)


def _evaluated_bounds(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    try:
        if not len(me.vertices):
            return None
        co = np.empty(len(me.vertices) * 3, dtype=np.float64)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        return co.min(0), co.max(0)
    finally:
        ev.to_mesh_clear()


def guard_bevels(slack=0.0005):
    """
    Booleans and bevels only remove material, so a part's finished mesh must fit inside its base
    mesh. The bevel's arc miters occasionally explode on boolean-cut thin walls (vertices at 1e35),
    which ruins the export and the bake. Such parts fall back to sharp miters, then to no bevel.
    Returns {part name: fix applied}.
    """
    fixes = {}
    for prt in PARTS:
        ob = prt.ob
        bv = ob.modifiers.get("Bevel")
        if not ob.modifiers or not len(ob.data.vertices):
            continue
        co = np.empty(len(ob.data.vertices) * 3, dtype=np.float64)
        ob.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        lo, hi = co.min(0) - slack, co.max(0) + slack

        def ok():
            b = _evaluated_bounds(ob)
            return b is not None and bool((b[0] >= lo).all() and (b[1] <= hi).all())

        if ok():
            continue
        if bv is None:
            fixes[ob.name] = "STILL BROKEN"
            continue
        bv.miter_outer = "MITER_SHARP"
        if ok():
            fixes[ob.name] = "sharp miters"
            continue
        bv.show_viewport = bv.show_render = False
        fixes[ob.name] = "bevel removed" if ok() else "STILL BROKEN"
    return fixes


def check_envelope(tag, fp):
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    worst = {}
    for g, o in fp.items():
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            for i in range(3):
                lo[i], hi[i] = min(lo[i], w[i]), max(hi[i], w[i])
        if len(o.data.vertices):
            ext = [max(v.co[i] for v in o.data.vertices) - min(v.co[i] for v in o.data.vertices) for i in range(3)]
            worst[g] = [round(e, 3) for e in ext]
    size = hi - lo
    bad = [("xyz"[i], round(size[i], 3), ENVELOPE[i]) for i in range(3) if size[i] > ENVELOPE[i]]
    if bad:
        raise RuntimeError(f"{tag}: first-person meshes exceed the weapon envelope {bad}; per group extents {worst}")
    return [round(v, 3) for v in size]


def finalize_weapon(tag, out_dir, res=2048, tp_ratio=0.3, do_bake=True, markers=()):
    """
    Turn the parts built for one weapon into export objects, all top-level:
      <tag>_<Group>   first-person meshes, one per animated group (Body, Mag, Bolt, ...)
      <tag>_TP_Body, <tag>_TP_Mag   decimated third-person meshes (same textures)
      markers (Muzzle, Sight, Eject, Grip_R, Grip_L, ...)
    Bakes the texture set, exports W_<tag>.fbx and returns stats.
    """
    bevel_fixes = guard_bevels()
    broken = [n for n, fix in bevel_fixes.items() if fix == "STILL BROKEN"]
    if broken:
        raise RuntimeError(f"{tag}: parts whose modifiers grow them past their base mesh even without a bevel "
                           f"(a boolean failed): {broken}")
    coll = collection("EXPORT_" + tag)
    groups = {}
    for p in PARTS:
        groups.setdefault(p.group, []).append(p.ob)
    fp = {}
    for g, objs in groups.items():
        fp[g] = join_meshes(objs, f"{tag}_{g}", coll)
    check_envelope(tag, fp)
    baked = [o for g, o in fp.items() if g not in UNBAKED_GROUPS]
    unwrap_together(baked)
    stats = {"tris_fp": sum(len(o.data.polygons) for o in fp.values()), "groups": sorted(fp.keys()),
             "bevel_fixes": bevel_fixes}
    if do_bake:
        bake_src = join_meshes(baked, f"{tag}_BAKE", coll, triangulate=False)
        stats.update(bake_weapon(bake_src, res, out_dir, tag))
        if stats["uv_coverage"] < 0.15:
            raise RuntimeError(f"{tag}: bake covered only {stats['uv_coverage']:.1%} of the texture; textures would be blank")
        me = bake_src.data
        bpy.data.objects.remove(bake_src)
        bpy.data.meshes.remove(me)

    mat = _placeholder_material(f"M_W_{tag}")
    glass = _placeholder_material("M_W_Glass")
    reticle = _placeholder_material("M_W_Reticle")
    for g, o in fp.items():
        o.data.materials.clear()
        o.data.materials.append(glass if g == "Lens" else reticle if g == "Reticle" else mat)

    # third person: everything but the magazine in one decimated body, plus the magazine
    tp = {}
    body_src = [o for g, o in fp.items() if g not in ("Mag",) + UNBAKED_GROUPS]
    tp["Body"] = join_meshes(body_src, f"{tag}_TP_Body", coll)
    if "Mag" in fp:
        tp["Mag"] = join_meshes([fp["Mag"]], f"{tag}_TP_Mag", coll)
    for o in tp.values():
        dec = o.modifiers.new("Decimate", "DECIMATE")
        dec.decimate_type = "COLLAPSE"
        dec.ratio = tp_ratio
        dec.use_collapse_triangulate = True
        o.data.materials.clear()
        o.data.materials.append(mat)
    # apply the decimation so the export and the stats agree
    for k, o in list(tp.items()):
        flat = join_meshes([o], o.name + "_d", coll)
        flat.data.materials.append(mat)
        old = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(old)
        flat.name = f"{tag}_TP_{k}"
        flat.data.name = flat.name
        tp[k] = flat
    stats["tris_tp"] = sum(len(o.data.polygons) for o in tp.values())

    # Flat layout: every mesh and marker is a top-level object. The FBX exporter's bake_space_transform
    # is only correct for top-level objects (nested ones get a stray 90 degree rotation), and without it
    # the axis conversion lands on the root node instead of the data. Unity wraps the top-level nodes in a
    # root named after the file (W_<tag>) and tells first- and third-person meshes apart by "_TP_".
    for m in markers:
        for c in m.users_collection:
            c.objects.unlink(m)
        coll.objects.link(m)
        m.parent = None

    # group pivots: moving parts rotate/slide about their own origin (set by the design via ["pivot"])
    for g, o in fp.items():
        piv = PIVOTS.get(g)
        if piv is not None:
            set_origin(o, piv)

    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"W_{tag}.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for o in coll.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(fp.values()))
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        # bake_space_transform puts the axis conversion into the data, so every node imports unrotated
        # (requires the flat layout above).
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, mesh_smooth_type="OFF",
        use_custom_props=False, add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
        use_triangles=True)
    stats["fbx"] = path
    return stats


PIVOTS = {}   # group -> pivot point (world), set by designs for parts that rotate (bolt handle, trigger)


def set_origin(ob, point):
    """Move an object's origin to `point` without moving its geometry (parents are at the origin)."""
    p = Vector(point)
    ob.data.transform(Matrix.Translation(-(p - ob.location)))
    ob.location = p


# --------------------------------------------------------------------------- preview
PREVIEW_COLOURS = {
    "steel": (0.05, 0.05, 0.055), "alloy": (0.07, 0.07, 0.075), "polymer": (0.035, 0.035, 0.035), "rubber": (0.02, 0.02, 0.02),
    "bare": (0.5, 0.5, 0.5), "brass": (0.7, 0.48, 0.18), "accent_fde": (0.36, 0.27, 0.16), "accent_od": (0.12, 0.13, 0.08),
    "accent_grey": (0.16, 0.16, 0.17), "red": (0.8, 0.05, 0.03), "glass": (0.1, 0.2, 0.2),
}


def preview(path_prefix, views=(("side", (0.0, -1, 0)),), res=(1600, 900), ortho_scale=1.0, centre=(0, 0.06, -0.04)):
    """Workbench renders of the current parts (geometry review before baking)."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL"
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.show_shadows = False
    sh.show_specular_highlight = True
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.film_transparent = False
    scene.world = scene.world or bpy.data.worlds.new("World")
    for p in PARTS:
        name = "PV_" + p.surface
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.diffuse_color = (*PREVIEW_COLOURS.get(p.surface, (0.5, 0.5, 0.5)), 1)
        m.roughness = 0.4
        m.metallic = 0.6 if p.surface in ("steel", "alloy", "bare") else 0.0
        p.ob.data.materials.clear()
        p.ob.data.materials.append(m)
    cam_data = bpy.data.cameras.get("PV_cam") or bpy.data.cameras.new("PV_cam")
    cam = bpy.data.objects.get("PV_cam") or bpy.data.objects.new("PV_cam", cam_data)
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    scene.camera = cam
    out = []
    c = Vector(centre)
    for name, d in views:
        d = Vector(d).normalized()
        if name.startswith("persp"):
            cam_data.type = "PERSP"
            cam_data.lens = 50
            cam.location = c - d * ortho_scale * 1.9
        else:
            cam_data.type = "ORTHO"
            cam_data.ortho_scale = ortho_scale
            cam.location = c - d * 2.0
        cam_data.clip_start = 0.01
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{path_prefix}_{name}.png"
        bpy.ops.render.render(write_still=True)
        out.append(scene.render.filepath)
    return out
