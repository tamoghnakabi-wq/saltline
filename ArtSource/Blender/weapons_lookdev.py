"""
Look-development renders of a finished weapon: the first-person meshes with the baked texture set,
lit by an HDRI, rendered with Cycles. Used to review a weapon before it goes into Unity.

Run inside Blender after finalize_weapon() (the EXPORT_<tag> collection must exist):
    exec(open(".../weapons_lookdev.py").read(), g); g["lookdev"]("AR", tex_dir, out_prefix, hdr_path)
Writes <out_prefix>_<view>.png for each view.
"""
import bpy
import mathutils

VIEWS = {
    # name: (camera location, look-at point, focal length mm); weapon frame: +y forward, +z up, +x right
    "right": ((1.45, 0.06, 0.06), (0.0, 0.06, -0.02), 50),
    "left": ((-1.45, 0.06, 0.06), (0.0, 0.06, -0.02), 50),
    "quarter": ((0.55, -0.55, 0.25), (0.0, 0.05, 0.0), 40),
    "receiver": ((0.32, -0.05, 0.08), (0.0, 0.0, 0.0), 50),
}


def _material(tag, tex_dir):
    name = f"LD_{tag}"
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")

    def img(kind, cs):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(f"{tex_dir}/T_{tag}_{kind}.png", check_existing=False)
        n.image.colorspace_settings.name = cs
        return n

    d, nm, mk = img("D", "sRGB"), img("N", "Non-Color"), img("M", "Non-Color")
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(d.outputs[0], bsdf.inputs["Base Color"])
    nt.links.new(nm.outputs[0], nmap.inputs["Color"])
    nt.links.new(nmap.outputs[0], bsdf.inputs["Normal"])
    nt.links.new(mk.outputs[0], sep.inputs[0])
    nt.links.new(sep.outputs[0], bsdf.inputs["Metallic"])
    nt.links.new(mk.outputs[1], inv.inputs[1])   # alpha = smoothness
    nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    return m


def _world(hdr_path):
    w = bpy.data.worlds.get("LD_World") or bpy.data.worlds.new("LD_World")
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    env = nt.nodes.new("ShaderNodeTexEnvironment")
    env.image = bpy.data.images.load(hdr_path, check_existing=True)
    bg = nt.nodes.new("ShaderNodeBackground")
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(env.outputs[0], bg.inputs[0])
    nt.links.new(bg.outputs[0], out.inputs[0])
    return w


def lookdev(tag, tex_dir, out_prefix, hdr_path, views=("right", "quarter"), samples=96, size=(1600, 900)):
    # the first-person meshes of the export (third-person ones carry "_TP_")
    fp = [o for o in bpy.data.collections[f"EXPORT_{tag}"].objects if o.type == "MESH" and "_TP_" not in o.name]
    mat = _material(tag, tex_dir)
    for o in fp:
        if not o.name.endswith(("_Lens", "_Reticle")):
            o.data.materials.clear()
            o.data.materials.append(mat)
    hidden = {}
    for o in bpy.data.objects:
        hidden[o.name] = o.hide_render
        o.hide_render = o not in fp
    sc = bpy.context.scene
    old_world, old_cam = sc.world, sc.camera
    sc.world = _world(hdr_path)
    sc.world.light_settings.distance = 10.0
    sc.render.engine = "CYCLES"
    sc.cycles.device = "GPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.view_settings.view_transform = "AgX"
    cam = bpy.data.objects.get("LD_Cam")
    if cam is None:
        cam = bpy.data.objects.new("LD_Cam", bpy.data.cameras.new("LD_Cam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    sc.camera = cam
    files = []
    for v in views:
        loc, target, lens = VIEWS[v]
        cam.data.lens = lens
        cam.location = loc
        cam.rotation_euler = (mathutils.Vector(target) - mathutils.Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = f"{out_prefix}_{v}.png"
        bpy.ops.render.render(write_still=True)
        files.append(sc.render.filepath)
    for o in bpy.data.objects:
        if o.name in hidden:
            o.hide_render = hidden[o.name]
    sc.world, sc.camera = old_world, old_cam
    bpy.data.objects.remove(cam)
    return files
