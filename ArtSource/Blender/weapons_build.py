"""
SALTLINE weapons, modelled in Blender from code. Original designs: none copies a real firearm.

Run in Blender (via the Blender MCP or the Python console):
    g = {"SALTLINE_ROOT": "<repo>", "WEAPONS": ["AR"], "WEAPON_BAKE": True, "__name__": "weapons"}
    exec(open(g["SALTLINE_ROOT"] + "/ArtSource/Blender/weapons_build.py").read(), g)
Outputs Assets/_Saltline/Art/Weapons/<TAG>/W_<TAG>.fbx and its baked texture set.

Each weapon is built around the game's conventions (see weapons_lib.py): bore along +Y, origin at
the receiver, and named markers the game reads:
    Muzzle  - flash / tracer origin          Sight  - point the eye lines up with when aiming
    Eject   - shell ejection port            Grip_R / Grip_L - hand positions for the arms
Moving parts are separate groups (Mag, Bolt, Charge, Trigger, Pump, Slide) so the game can
animate them; their origin is their pivot.
"""
import math
import os

import bpy
from mathutils import Vector

ROOT = globals()["SALTLINE_ROOT"]
exec(open(os.path.join(ROOT, "ArtSource", "Blender", "weapons_lib.py")).read(), globals())

OUT = globals().get("WEAPON_OUT") or os.path.join(ROOT, "Assets", "_Saltline", "Art", "Weapons")
DO_BAKE = globals().get("WEAPON_BAKE", True)
RES = globals().get("WEAPON_RES", 2048)


# =========================================================================== VK-21 Halyard (AR)
def build_ar():
    """
    VK-21 Halyard: 5.56 mm-class carbine. Flat-top monolithic upper with a full-length rail,
    octagonal slotted handguard, three-port compensator, side charging handle, adjustable stock,
    curved polymer magazine and a compact open reflex sight.
    """
    T = "AR"
    W = 0.015  # half width of the upper receiver
    # ---- upper receiver
    up = part("upper", prism_bm(round_profile([(-0.128, -0.012), (0.066, -0.012), (0.066, 0.022), (-0.122, 0.022), (-0.128, 0.014)], 0.002), "x", -W, W), "alloy")
    cut(up, box_bm((0.006, -0.036, -0.005), (0.03, 0.030, 0.0125)), "ar_port")                  # ejection port (right)
    cut(up, box_bm((-0.03, -0.070, 0.0075), (-0.011, 0.052, 0.0145)), "ar_chslot")              # side charging slot (left)
    cut(up, box_bm((-0.03, -0.11, -0.006), (0.03, -0.098, 0.016)), "ar_relief")                 # relief groove
    for sx in (-1, 1):
        cut(up, side_box(sx, W, 0.0008, -0.095, -0.05, 0.001, 0.016), f"ar_flat{sx}")  # shallow side flats
    picatinny("rail_top", -0.122, 0.312, 0.022)
    # ---- barrel and muzzle device
    part("barrel", tube_bm(0.0095, 0.0095, 0.066, 0.33, "y", 32), "steel", bevel=0.0006)
    part("barrel_fwd", tube_bm(0.0085, 0.0085, 0.33, 0.432, "y", 32), "steel", bevel=0.0006)
    part("barrel_nut", tube_bm(0.0175, 0.0175, 0.062, 0.078, "y", 24), "alloy", bevel=0.0008)
    comp = part("comp", tube_bm(0.0118, 0.0118, 0.43, 0.487, "y", 32, r_in=0.0048), "steel", bevel=0.0007)
    for i in range(3):
        y = 0.446 + i * 0.013
        cut(comp, box_bm((-0.004, y - 0.0028, 0.0), (0.004, y + 0.0028, 0.02)), f"ar_comp_top{i}")
        cut(comp, box_bm((-0.02, y - 0.0028, -0.0035), (0.02, y + 0.0028, 0.0035)), f"ar_comp_side{i}")
    part("comp_collar", tube_bm(0.0108, 0.0108, 0.424, 0.432, "y", 24), "steel", bevel=0.0004)
    # ---- handguard: octagon, slots on the sides and diagonals
    hg = part("handguard", tube_bm(0.0245, 0.0245, 0.07, 0.316, "y", 8, centre=(0, 0.0005), r_in=0.0205, ngon=True), "alloy", bevel=0.0010)
    for k in range(5):
        y = 0.098 + k * 0.044
        for ang in (0, 45, 135, 180, 225, 315):
            c = box_bm((0.012, y - 0.016, -0.0036), (0.04, y + 0.016, 0.0036))
            rotate_bm(c, ang, "Y")
            cut(hg, c, f"ar_hg_{k}_{ang}")
    hg_cap = part("hg_cap", tube_bm(0.0248, 0.0248, 0.312, 0.318, "y", 8, centre=(0, 0.0005), r_in=0.0118, ngon=True), "alloy", bevel=0.0006)
    for k in range(4):
        for sx in (-1, 1):
            screw(f"hg_screw_{k}_{sx}", (sx * 0.0228, 0.084 + k * 0.066, -0.012), "x", 0.0021, 0.0016)
    # ---- lower receiver with magwell
    lo = part("lower", prism_bm(round_profile([(-0.118, -0.012), (0.071, -0.012), (0.071, -0.024), (0.068, -0.058), (0.006, -0.058),
                                                (0.004, -0.031), (-0.072, -0.031), (-0.101, -0.026), (-0.118, -0.020)], 0.0025), "x", -0.0135, 0.0135), "alloy")
    cut(lo, box_bm((-0.0116, 0.0115, -0.07), (0.0116, 0.0645, -0.006)), "ar_magwell")
    cut(lo, box_bm((-0.006, -0.045, -0.035), (0.006, -0.002, -0.0115)), "ar_trigslot")
    flare = part("magwell_flare", prism_bm(round_profile([(0.003, -0.050), (0.074, -0.050), (0.077, -0.062), (0.0, -0.062)], 0.0015), "x", -0.0155, 0.0155), "alloy", bevel=0.0008)
    cut(flare, box_bm((-0.0118, 0.0112, -0.07), (0.0118, 0.0648, -0.04)), "ar_flarehole")
    # trigger guard
    tg = part("trig_guard", box_bm((-0.0065, -0.056, -0.054), (0.0065, 0.006, -0.029)), "alloy", bevel=0.0015)
    cut(tg, box_bm((-0.01, -0.049, -0.048), (0.01, -0.0005, -0.02)), "ar_tg_in")
    # trigger (pivot at its pin)
    part("trigger", prism_bm(round_profile([(-0.010, -0.022), (-0.005, -0.022), (-0.008, -0.034), (-0.014, -0.0435), (-0.0175, -0.0425), (-0.0125, -0.033)], 0.001), "x", -0.003, 0.003), "steel", group="Trigger", bevel=0.0005)
    PIVOTS["Trigger"] = (0.0, -0.007, -0.020)
    # pins, selector, mag release, bolt catch
    for sx in (-1, 1):
        part(f"pin_r{sx}", tube_bm(0.0028, 0.0028, sx * 0.0135, sx * 0.0148, "x", 16, (-0.106, -0.019)), "steel", bevel=0.0003)
        part(f"pin_f{sx}", tube_bm(0.0028, 0.0028, sx * 0.0135, sx * 0.0148, "x", 16, (0.060, -0.017)), "steel", bevel=0.0003)
        sel = prism_bm(round_profile([(-0.080, -0.017), (-0.070, -0.017), (-0.060, -0.021), (-0.062, -0.025), (-0.080, -0.023)], 0.0012), "x", sx * 0.0135, sx * 0.0162)
        part(f"selector{sx}", sel, "steel", bevel=0.0004)
        part(f"selector_hub{sx}", tube_bm(0.0045, 0.0045, sx * 0.0135, sx * 0.0156, "x", 20, (-0.076, -0.020)), "steel", bevel=0.0004)
    part("mag_release", tube_bm(0.0042, 0.0042, 0.0135, 0.0168, "x", 20, (0.004, -0.036)), "steel", bevel=0.0005)
    part("bolt_catch", prism_bm(round_profile([(-0.004, -0.016), (0.006, -0.016), (0.010, -0.030), (0.0, -0.032), (-0.004, -0.024)], 0.001), "x", -0.0165, -0.0135), "steel", bevel=0.0004)
    # ---- pistol grip (polymer), raked 17 degrees
    gp = round_profile([(0.0, 0.0), (-0.034, 0.0), (-0.037, -0.030), (-0.038, -0.104), (-0.031, -0.112), (-0.004, -0.112), (0.002, -0.097),
                        (-0.002, -0.082), (0.003, -0.066), (-0.001, -0.051), (0.003, -0.036), (0.001, -0.014)], 0.003)
    grip = prism_bm(gp, "x", -0.0145, 0.0145)
    shape_x(grip, lambda y, z: 1.0 - 0.12 * min(1.0, -z / 0.11) + 0.06 * min(1.0, max(0.0, -y / 0.035)))
    rotate_bm(grip, -17.0, "X", (0, 0, 0))
    move_bm(grip, (0.0, -0.040, -0.027))
    g = part("grip", grip, "polymer", bevel=0.0035, segments=3)
    for sx in (-1, 1):
        panel = side_box(sx, 0.0128, 0.0010, -0.030, -0.004, -0.098, -0.036)
        rotate_bm(panel, -17.0, "X", (0, 0, 0))
        move_bm(panel, (0, -0.040, -0.027))
        cut(g, panel, f"ar_grip_panel{sx}")
    # ---- buffer tube and stock
    part("buffer_tube", tube_bm(0.0145, 0.0145, -0.300, -0.118, "y", 32), "alloy", bevel=0.0008)
    nut = part("castle_nut", tube_bm(0.0172, 0.0172, -0.130, -0.120, "y", 32), "steel", bevel=0.0005)
    for k in range(8):
        c = box_bm((-0.0025, -0.135, 0.013), (0.0025, -0.126, 0.02))
        rotate_bm(c, k * 45.0, "Y")
        cut(nut, c, f"ar_nut{k}")
    part("end_plate", prism_bm(round_profile([(-0.118, 0.012), (-0.112, 0.012), (-0.112, -0.024), (-0.118, -0.024)], 0.001), "x", -0.016, 0.016), "steel", bevel=0.0004)
    stock_bm = prism_bm(round_profile([(-0.205, 0.021), (-0.300, 0.026), (-0.360, 0.031), (-0.373, 0.031), (-0.378, -0.074), (-0.357, -0.084),
                                       (-0.330, -0.062), (-0.292, -0.036), (-0.240, -0.025), (-0.205, -0.019)], 0.006), "x", -0.0175, 0.0175)
    shape_x(stock_bm, lambda y, z: 1.0 - 0.25 * min(1.0, max(0.0, -(z + 0.015) / 0.07)))
    st = part("stock", stock_bm, "polymer", bevel=0.003, segments=3)
    cut(st, tube_bm(0.0149, 0.0149, -0.36, -0.19, "y", 32), "ar_stock_tube")
    cut(st, prism_bm(round_profile([(-0.352, -0.024), (-0.298, -0.024), (-0.318, -0.046), (-0.340, -0.062), (-0.354, -0.062)], 0.004), "x", -0.03, 0.03), "ar_stock_window")
    for sx in (-1, 1):
        cut(st, side_box(sx, 0.0175, 0.0012, -0.335, -0.235, 0.004, 0.018), f"ar_stock_flute{sx}")
    part("buttpad", prism_bm(round_profile([(-0.392, 0.032), (-0.376, 0.032), (-0.382, -0.074), (-0.392, -0.074)], 0.004), "x", -0.0205, 0.0205), "rubber", bevel=0.002)
    part("stock_lever", box_bm((-0.004, -0.262, -0.036), (0.004, -0.226, -0.028)), "steel", bevel=0.0012)
    # ---- magazine (curved, polymer) and floorplate; origin at its top so it drops cleanly
    # The body curves forward as it goes down; its bottom (and the floorplate) stays square to that curve.
    front = [(0.0635, -0.010), (0.066, -0.060), (0.074, -0.110), (0.087, -0.158), (0.101, -0.196)]
    rear = [(0.0125, -0.010), (0.0135, -0.060), (0.020, -0.110), (0.031, -0.162), (0.044, -0.212)]
    mag = part("mag", prism_bm(round_profile(front + list(reversed(rear)), 0.002), "x", -0.0112, 0.0112), "polymer_fde", group="Mag", bevel=0.0012)
    for k in range(4):  # raised grip ribs, square to the body like the floorplate
        z0 = -0.118 - k * 0.016
        y_rear = 0.020 + (0.031 - 0.020) * (-(z0 + 0.110) / 0.052)
        y_front = 0.074 + (0.087 - 0.074) * (-(z0 + 0.110) / 0.048)
        rib = box_bm((-0.0121, y_rear + 0.006, z0 - 0.0016), (0.0121, y_front - 0.006, z0 + 0.0016))
        rotate_bm(rib, 13.0, "X", (0, (y_rear + y_front) / 2, z0))
        part(f"mag_rib{k}", rib, "polymer_fde", group="Mag", bevel=0.0006)
    floor_angle = math.degrees(math.atan2(front[-1][1] - rear[-1][1], front[-1][0] - rear[-1][0]))
    fy, fz = (front[-1][0] + rear[-1][0]) / 2, (front[-1][1] + rear[-1][1]) / 2
    fp_ = box_bm((-0.0130, fy - 0.035, fz - 0.0085), (0.0130, fy + 0.036, fz + 0.003))
    rotate_bm(fp_, floor_angle, "X", (0, fy, fz))
    part("mag_floor", fp_, "polymer", group="Mag", bevel=0.0018)
    part("mag_round", tube_bm(0.0045, 0.0045, 0.020, 0.052, "y", 16, (0.0, -0.008)), "brass", group="Mag", bevel=0.0003)
    PIVOTS["Mag"] = (0.0, 0.038, -0.012)
    # ---- bolt carrier (visible in the port) and charging handle
    # Set back from the port so it reads as a recess; dark nitride with bright worn edges.
    part("bolt", box_bm((0.003, -0.040, -0.0035), (0.0104, 0.034, 0.0112)), "steel", group="Bolt", bevel=0.0006)
    part("bolt_head", tube_bm(0.0055, 0.0055, 0.034, 0.042, "y", 20, (0.0046, 0.0035)), "bare", group="Bolt", bevel=0.0004)
    PIVOTS["Bolt"] = (0.008, 0.0, 0.004)
    ch = prism_bm(round_profile([(0.030, 0.0065), (0.052, 0.0065), (0.052, 0.0175), (0.030, 0.0175)], 0.0015), "x", -0.031, -0.0115)
    chg = part("charge_knob", ch, "steel", group="Charge", bevel=0.0007)
    for k in range(4):
        cut(chg, box_bm((-0.033, 0.033 + k * 0.005, 0.005), (-0.0285, 0.0348 + k * 0.005, 0.019)), f"ar_ch_knurl{k}")
    PIVOTS["Charge"] = (-0.02, 0.041, 0.012)
    # ---- reflex sight (black anodised) on the rail
    part("rds_base", box_bm((-0.0125, -0.078, 0.0315), (0.0125, -0.022, 0.041)), "alloy", bevel=0.0012)
    hood_prof = round_profile([(-0.0165, 0.040), (0.0165, 0.040), (0.0165, 0.088), (0.010, 0.0985), (-0.010, 0.0985), (-0.0165, 0.088)], 0.003)
    hood = part("rds_hood", prism_bm(hood_prof, "y", -0.066, -0.026), "alloy", bevel=0.0012)
    cut(hood, prism_bm(round_profile([(-0.0128, 0.0505), (0.0128, 0.0505), (0.0128, 0.0855), (0.0085, 0.0925), (-0.0085, 0.0925), (-0.0128, 0.0855)], 0.0025), "y", -0.08, -0.01), "ar_rds_window")
    part("rds_knob", tube_bm(0.0055, 0.0055, 0.0165, 0.0215, "x", 24, (-0.050, 0.062)), "steel", bevel=0.0005)
    part("rds_battery", tube_bm(0.0068, 0.0068, 0.0985, 0.1015, "z", 24, (0.006, -0.046)), "steel", bevel=0.0005)
    part("rds_clamp", box_bm((0.0125, -0.065, 0.026), (0.0165, -0.035, 0.039)), "steel", bevel=0.0008)
    screw("rds_clamp_screw", (0.0172, -0.050, 0.033), "x", 0.0024, 0.0016)
    part("rds_lens", box_bm((-0.0132, -0.0285, 0.050), (0.0132, -0.0275, 0.093)), "glass", group="Lens", bevel=0.0)
    part("rds_dot", tube_bm(0.00075, 0.00075, -0.0287, -0.0286, "y", 12, (0.0, 0.0715)), "red", group="Reticle", bevel=0.0)
    # ---- sling hardware
    part("qd_cup", tube_bm(0.0055, 0.0055, -0.020, -0.016, "x", 20, (-0.118, -0.005)), "steel", bevel=0.0005)
    markers = [
        empty("Muzzle", (0.0, 0.487, 0.0)),
        empty("Sight", (0.0, -0.066, 0.0715)),
        empty("Eject", (0.018, -0.003, 0.004)),
        empty("Grip_R", (0.0, -0.062, -0.075)),
        empty("Grip_L", (0.0, 0.205, -0.026)),
        empty("MagWell", (0.0, 0.038, -0.012)),
    ]
    return T, markers


# =========================================================================== shared pieces
def grip_bm(rake, at, height=0.112, half_width=0.0145):
    """Polymer pistol grip with finger grooves, raked back by `rake` degrees, its top-front at `at` (y, z)."""
    k = height / 0.112
    gp = round_profile([(0.0, 0.0), (-0.034, 0.0), (-0.037, -0.030 * k), (-0.038, -0.104 * k), (-0.031, -0.112 * k), (-0.004, -0.112 * k),
                        (0.002, -0.097 * k), (-0.002, -0.082 * k), (0.003, -0.066 * k), (-0.001, -0.051 * k), (0.003, -0.036 * k), (0.001, -0.014 * k)], 0.003)
    bm = prism_bm(gp, "x", -half_width, half_width)
    shape_x(bm, lambda y, z: 1.0 - 0.12 * min(1.0, -z / height) + 0.06 * min(1.0, max(0.0, -y / 0.035)))
    rotate_bm(bm, -rake, "X", (0, 0, 0))
    move_bm(bm, (0.0, at[0], at[1]))
    return bm


def grip(name, rake, at, height=0.112, half_width=0.0145, surface="polymer"):
    g = part(name, grip_bm(rake, at, height, half_width), surface, bevel=0.0035, segments=3)
    for sx in (-1, 1):  # stippled side panels
        panel = side_box(sx, half_width - 0.0017, 0.0010, -0.030, -0.004, -0.098 * height / 0.112, -0.036)
        rotate_bm(panel, -rake, "X", (0, 0, 0))
        move_bm(panel, (0, at[0], at[1]))
        cut(g, panel, f"{name}_panel{sx}")
    return g


def trigger(name, at, scale=1.0, pivot_offset=(0.003, 0.002)):
    """Curved trigger blade whose top sits at `at` (y, z); its pivot is the pin just above."""
    y, z = at
    pts = [(y + a * scale, z + b * scale) for a, b in [(0.0, 0.0), (0.005, 0.0), (0.002, -0.012), (-0.004, -0.0215), (-0.0075, -0.0205), (-0.0025, -0.011)]]
    part(name, prism_bm(round_profile(pts, 0.001), "x", -0.003, 0.003), "steel", group="Trigger", bevel=0.0005)
    PIVOTS["Trigger"] = (0.0, y + pivot_offset[0], z + pivot_offset[1])


def trigger_guard(name, y0, y1, z_top, z_bottom, surface="polymer", half_width=0.0062, thick=0.0055):
    tg = part(name, box_bm((-half_width, y0, z_bottom), (half_width, y1, z_top)), surface, bevel=0.0015)
    cut(tg, box_bm((-0.012, y0 + thick, z_bottom + thick), (0.012, y1 - thick, z_top + 0.01)), name + "_in")
    return tg


def lens(name, y, r, z, group="Lens"):
    part(name, tube_bm(r, r, y - 0.0006, y + 0.0006, "y", 32, (0.0, z)), "glass", group=group, bevel=0.0)


# =========================================================================== SMR-9 Wasp (SMG)
def build_smg():
    """
    SMR-9 Wasp: compact 9 mm-class submachine gun. Boxy anodised upper with a top rail and left-side
    cocking tube, ventilated barrel shroud, polymer lower with a straight stick magazine ahead of
    the trigger guard, collapsing two-rod stock and a window-style holographic sight.
    """
    T = "SMG"
    W = 0.0175
    up = part("upper", prism_bm(round_profile([(-0.105, -0.016), (0.150, -0.016), (0.150, 0.026), (0.138, 0.032), (-0.095, 0.032), (-0.105, 0.024)], 0.003), "x", -W, W), "alloy")
    cut(up, box_bm((0.008, -0.012, -0.0045), (0.03, 0.040, 0.0135)), "smg_port")
    cut(up, box_bm((-0.03, -0.040, 0.0125), (-0.0135, 0.128, 0.0205)), "smg_cockslot")
    for sx in (-1, 1):
        cut(up, side_box(sx, W, 0.0009, 0.050, 0.140, -0.010, 0.006), f"smg_flat{sx}")
        cut(up, side_box(sx, W, 0.0009, -0.098, -0.050, -0.010, 0.022), f"smg_flatr{sx}")
    picatinny("rail_top", -0.095, 0.138, 0.032)
    # barrel, shroud with vents, thread protector
    part("barrel", tube_bm(0.0080, 0.0080, 0.150, 0.270, "y", 28), "steel", bevel=0.0005)
    sh = part("shroud", tube_bm(0.0200, 0.0200, 0.150, 0.245, "y", 32, r_in=0.0170), "alloy", bevel=0.0009)
    for k in range(4):
        for ang in range(0, 360, 60):
            c = tube_bm(0.0042, 0.0042, 0.010, 0.030, "z", 16, (0.0, 0.166 + k * 0.020))
            rotate_bm(c, ang, "Y")
            cut(sh, c, f"smg_vent_{k}_{ang}")
    part("shroud_cap", tube_bm(0.0205, 0.0195, 0.243, 0.251, "y", 32, r_in=0.0090), "alloy", bevel=0.0006)
    part("thread_cap", tube_bm(0.0098, 0.0098, 0.266, 0.286, "y", 24, r_in=0.0048), "steel", bevel=0.0006)
    # lower: grip frame, magwell, guard
    lo = part("lower", prism_bm(round_profile([(-0.088, -0.016), (0.112, -0.016), (0.112, -0.028), (0.104, -0.034), (-0.020, -0.034), (-0.060, -0.030), (-0.088, -0.026)], 0.003), "x", -0.0165, 0.0165), "polymer", bevel=0.002)
    mw = part("magwell", prism_bm(round_profile([(0.052, -0.016), (0.108, -0.016), (0.110, -0.078), (0.050, -0.078)], 0.004), "x", -0.0158, 0.0158), "polymer", bevel=0.002)
    cut(mw, box_bm((-0.0114, 0.0628, -0.09), (0.0114, 0.0972, -0.010)), "smg_magwell")
    for sx in (-1, 1):
        cut(mw, side_box(sx, 0.0158, 0.0012, 0.058, 0.102, -0.072, -0.040), f"smg_mw_grip{sx}")
    trigger_guard("trig_guard", -0.012, 0.054, -0.030, -0.058)
    trigger("trigger", (0.022, -0.026))
    grip("grip", 15.0, (-0.006, -0.030))
    part("mag_release", tube_bm(0.0042, 0.0042, 0.0158, 0.0185, "x", 20, (0.050, -0.030)), "steel", bevel=0.0005)
    part("selector", prism_bm(round_profile([(-0.040, -0.019), (-0.026, -0.019), (-0.018, -0.024), (-0.022, -0.028), (-0.040, -0.026)], 0.0012), "x", -0.0193, -0.0165), "steel", bevel=0.0004)
    part("selector_hub", tube_bm(0.0048, 0.0048, -0.0187, -0.0165, "x", 20, (-0.034, -0.023)), "steel", bevel=0.0004)
    for y in (-0.075, 0.095):
        for sx in (-1, 1):
            part(f"pin_{y}_{sx}", tube_bm(0.0026, 0.0026, sx * 0.0165, sx * 0.0178, "x", 16, (y, -0.024)), "steel", bevel=0.0003)
    # magazine: straight double stack, steel body, polymer base
    mag = part("mag", prism_bm(round_profile([(0.0635, -0.012), (0.0965, -0.012), (0.0965, -0.206), (0.0635, -0.206)], 0.002), "x", -0.0105, 0.0105), "steel", group="Mag", bevel=0.001)
    for k in range(5):
        cut(mag, box_bm((-0.012, 0.0905, -0.100 - k * 0.018), (0.012, 0.0935, -0.093 - k * 0.018)), f"smg_witness{k}")
    part("mag_base", box_bm((-0.0122, 0.0605, -0.215), (0.0122, 0.1000, -0.203)), "polymer", group="Mag", bevel=0.002)
    part("mag_round", tube_bm(0.0045, 0.0045, 0.068, 0.088, "y", 16, (0.0, -0.008)), "brass", group="Mag", bevel=0.0003)
    PIVOTS["Mag"] = (0.0, 0.080, -0.016)
    # bolt (visible in the port) and the left cocking handle
    part("bolt", box_bm((0.002, -0.012, -0.0035), (0.0150, 0.040, 0.0125)), "steel", group="Bolt", bevel=0.0006)
    PIVOTS["Bolt"] = (0.008, 0.014, 0.004)
    part("charge_knob", tube_bm(0.0052, 0.0060, -0.042, -0.0175, "x", 20, (0.118, 0.0165)), "steel", group="Charge", bevel=0.0008)
    for k in range(4):  # raised grip bands (hollow-ring boolean grooves break the exact solver here)
        x0 = -0.0395 + k * 0.005
        r = 0.0052 + 0.0008 * (x0 + 0.042) / 0.0245 + 0.0006
        part(f"charge_band{k}", tube_bm(r, r, x0, x0 + 0.0016, "x", 20, (0.118, 0.0165)), "steel", group="Charge", bevel=0.0003, segments=1)
    PIVOTS["Charge"] = (-0.03, 0.118, 0.0165)
    # stock: two rods, block on the receiver, butt plate with rubber pad
    part("stock_block", box_bm((-0.022, -0.118, -0.024), (0.022, -0.088, 0.018)), "polymer", bevel=0.003)
    for sx in (-1, 1):
        part(f"stock_rod{sx}", tube_bm(0.0042, 0.0042, -0.318, -0.110, "y", 20, (sx * 0.0165, -0.004)), "steel", bevel=0.0005)
    part("stock_rod_low", tube_bm(0.0042, 0.0042, -0.318, -0.110, "y", 20, (0.0, -0.030)), "steel", bevel=0.0005)
    bp = part("butt", prism_bm(round_profile([(-0.322, 0.026), (-0.304, 0.026), (-0.304, -0.088), (-0.322, -0.088)], 0.004), "x", -0.022, 0.022), "polymer", bevel=0.003)
    cut(bp, box_bm((-0.012, -0.33, -0.070), (0.012, -0.30, -0.040)), "smg_butt_window")
    part("butt_pad", prism_bm(round_profile([(-0.334, 0.028), (-0.321, 0.028), (-0.321, -0.090), (-0.334, -0.090)], 0.004), "x", -0.0235, 0.0235), "rubber", bevel=0.002)
    # holographic-style sight: window hood on a base, lens and dot
    part("holo_base", box_bm((-0.0145, -0.078, 0.032), (0.0145, -0.004, 0.0405)), "alloy", bevel=0.0012)
    hood = part("holo_hood", prism_bm(round_profile([(-0.0195, 0.040), (0.0195, 0.040), (0.0195, 0.081), (-0.0195, 0.081)], 0.004), "y", -0.056, -0.010), "alloy", bevel=0.0012)
    cut(hood, box_bm((-0.0160, -0.08, 0.0445), (0.0160, 0.0, 0.0765)), "smg_holo_window")
    part("holo_battery", tube_bm(0.0062, 0.0062, 0.0145, 0.0225, "x", 24, (-0.024, 0.036)), "steel", bevel=0.0005)
    for k in range(3):
        part(f"holo_button{k}", box_bm((-0.009 + k * 0.0065, -0.080, 0.0335), (-0.005 + k * 0.0065, -0.077, 0.0385)), "rubber", bevel=0.0006)
    part("holo_lens", box_bm((-0.0162, -0.0306, 0.0445), (0.0162, -0.0296, 0.0765)), "glass", group="Lens", bevel=0.0)
    part("holo_dot", tube_bm(0.0009, 0.0009, -0.0308, -0.0307, "y", 12, (0.0, 0.0605)), "red", group="Reticle", bevel=0.0)
    part("sling_loop", tube_bm(0.0050, 0.0050, -0.0205, -0.0165, "x", 16, (0.135, -0.005), r_in=0.0028), "steel", bevel=0.0004)
    markers = [
        empty("Muzzle", (0.0, 0.286, 0.0)),
        empty("Sight", (0.0, -0.050, 0.0605)),
        empty("Eject", (0.018, 0.014, 0.004)),
        empty("Grip_R", (0.0, -0.028, -0.072)),
        empty("Grip_L", (0.0, 0.080, -0.060)),
        empty("MagWell", (0.0, 0.080, -0.016)),
    ]
    return T, markers


# =========================================================================== TS-12 Breaker (SG)
def build_sg():
    """
    TS-12 Breaker: 12-gauge pump action. Steel receiver with a bottom loading port, vent-ribbed
    barrel over a six-round tube magazine, grooved polymer pump on twin action bars, ghost-ring
    rear aperture with protective ears and a fibre-optic front post, pistol-grip fixed stock.
    """
    T = "SG"
    W = 0.0165
    rc = part("receiver", prism_bm(round_profile([(-0.088, -0.042), (0.092, -0.042), (0.092, 0.018), (0.082, 0.024), (-0.074, 0.024), (-0.088, 0.016)], 0.004), "x", -W, W), "alloy", bevel=0.0015)
    cut(rc, box_bm((0.010, -0.006, -0.017), (0.03, 0.060, 0.009)), "sg_port")
    cut(rc, box_bm((-0.0112, 0.004, -0.05), (0.0112, 0.078, -0.034)), "sg_loadport")
    for sx in (-1, 1):
        cut(rc, side_box(sx, W, 0.0009, -0.078, 0.000, -0.032, 0.012), f"sg_flat{sx}")
    part("barrel", tube_bm(0.0118, 0.0112, 0.090, 0.500, "y", 32, r_in=0.0092), "steel", bevel=0.0006)
    rib = part("vent_rib", box_bm((-0.0036, 0.105, 0.0108), (0.0036, 0.492, 0.0158)), "steel", bevel=0.0004)
    cut(rib, array_bm(lambda i: box_bm((-0.005, 0.112, 0.0108), (0.005, 0.1185, 0.0138)), 28, (0, 0.0135, 0)), "sg_rib_slots")
    part("mag_tube", tube_bm(0.0118, 0.0118, 0.090, 0.456, "y", 28, (0.0, -0.0275)), "steel", bevel=0.0005)
    cap = part("mag_cap", tube_bm(0.0132, 0.0126, 0.454, 0.478, "y", 28, (0.0, -0.0275)), "steel", bevel=0.0008)
    for k in range(10):
        c = box_bm((-0.0015, 0.458, -0.044), (0.0015, 0.474, -0.039))
        rotate_bm(c, k * 36.0, "Y", (0, 0, -0.0275))
        cut(cap, c, f"sg_cap_knurl{k}")
    part("barrel_clamp", prism_bm(round_profile([(-0.0145, 0.004), (0.0145, 0.004), (0.0152, -0.040), (-0.0152, -0.040)], 0.004), "y", 0.430, 0.446), "steel", bevel=0.0008)
    # pump (moves) and its action bars
    pump_prof = round_profile([(-0.0225, -0.049), (0.0225, -0.049), (0.0245, -0.020), (0.017, -0.009), (-0.017, -0.009), (-0.0245, -0.020)], 0.004)
    pmp = part("pump", prism_bm(pump_prof, "y", 0.170, 0.330), "polymer", group="Pump", bevel=0.0025, segments=3)
    for sx in (-1, 1):
        cut(pmp, array_bm(lambda i: side_box(sx, 0.0245, 0.0022, 0.188, 0.193, -0.046, -0.013), 11, (0, 0.0122, 0)), f"sg_pump_grooves{sx}")
    for sx in (-1, 1):
        part(f"action_bar{sx}", box_bm((sx * 0.0100 - 0.0012, 0.060, -0.0415), (sx * 0.0100 + 0.0012, 0.175, -0.0365)), "steel", group="Pump", bevel=0.0003)
    PIVOTS["Pump"] = (0.0, 0.250, -0.0275)
    part("bolt", box_bm((0.002, -0.006, -0.0165), (0.0152, 0.058, 0.0085)), "steel", group="Bolt", bevel=0.0006)
    PIVOTS["Bolt"] = (0.008, 0.026, -0.004)
    # trigger group, grip, stock
    tgh = part("trig_housing", box_bm((-0.0145, -0.072, -0.052), (0.0145, 0.060, -0.040)), "polymer", bevel=0.0018)
    trigger_guard("trig_guard", -0.036, 0.030, -0.050, -0.076)
    trigger("trigger", (-0.002, -0.050))
    part("safety", tube_bm(0.0035, 0.0035, -0.0175, 0.0175, "x", 16, (0.036, -0.046)), "steel", bevel=0.0005)
    grip("grip", 20.0, (-0.060, -0.046), height=0.105)
    st_prof = round_profile([(-0.086, 0.018), (-0.385, 0.010), (-0.392, -0.108), (-0.330, -0.098), (-0.215, -0.068), (-0.125, -0.056), (-0.086, -0.042)], 0.008)
    st = prism_bm(st_prof, "x", -0.019, 0.019)
    shape_x(st, lambda y, z: 1.0 - 0.18 * min(1.0, max(0.0, -(z + 0.02) / 0.08)))
    stk = part("stock", st, "polymer", bevel=0.003, segments=3)
    for sx in (-1, 1):
        cut(stk, side_box(sx, 0.019 * 0.95, 0.0015, -0.330, -0.150, -0.030, 0.002), f"sg_stock_panel{sx}")
    part("buttpad", prism_bm(round_profile([(-0.408, 0.014), (-0.386, 0.014), (-0.393, -0.112), (-0.408, -0.112)], 0.004), "x", -0.0215, 0.0215), "rubber", bevel=0.002)
    part("sling_qd", tube_bm(0.0055, 0.0055, -0.0215, -0.0180, "x", 20, (-0.330, -0.020)), "steel", bevel=0.0005)
    # sights: ghost ring with ears on the receiver, fibre front post on the rib
    part("rear_base", box_bm((-0.0125, -0.072, 0.0235), (0.0125, -0.038, 0.0305)), "steel", bevel=0.0008)
    for sx in (-1, 1):
        part(f"rear_ear{sx}", box_bm((sx * 0.0072 - (0.0045 if sx > 0 else 0), -0.064, 0.030), (sx * 0.0072 + (0.0045 if sx < 0 else 0), -0.050, 0.0475)), "steel", bevel=0.0008)
    part("ghost_ring", tube_bm(0.0062, 0.0062, -0.059, -0.0545, "y", 28, (0.0, 0.0400), r_in=0.0034), "steel", bevel=0.0004)
    part("ghost_post", box_bm((-0.0018, -0.059, 0.0300), (0.0018, -0.0545, 0.0345)), "steel", bevel=0.0003)
    part("front_base", box_bm((-0.0045, 0.462, 0.0155), (0.0045, 0.478, 0.0205)), "steel", bevel=0.0006)
    part("front_post", box_bm((-0.0015, 0.466, 0.0200), (0.0015, 0.474, 0.0380)), "steel", bevel=0.0004)
    part("front_fibre", tube_bm(0.00125, 0.00125, 0.4655, 0.4745, "y", 12, (0.0, 0.0390)), "red", bevel=0.0002)
    markers = [
        empty("Muzzle", (0.0, 0.500, 0.0)),
        empty("Sight", (0.0, -0.057, 0.0400)),
        empty("Eject", (0.0175, 0.026, -0.004)),
        empty("Grip_R", (0.0, -0.088, -0.090)),
        empty("Grip_L", (0.0, 0.250, -0.045)),
        empty("MagWell", (0.0, 0.040, -0.040)),
    ]
    return T, markers


# =========================================================================== LR-338 Longreach (SR)
def build_sr():
    """
    LR-338 Longreach: bolt-action precision rifle. Round steel action with an integral rail, fluted
    heavy barrel and two-chamber brake, olive aluminium chassis with a slotted forend, skeleton
    stock with a cheek riser, five-round box magazine, folded bipod, and a 30 mm long-range scope.
    """
    T = "SR"
    act = part("action", tube_bm(0.0170, 0.0170, -0.100, 0.100, "y", 40), "steel", bevel=0.0010)
    cut(act, box_bm((0.004, -0.030, -0.006), (0.03, 0.050, 0.0175)), "sr_port")
    cut(act, box_bm((0.006, -0.090, -0.006), (0.03, -0.062, 0.006)), "sr_handle_notch")
    picatinny("rail_top", -0.096, 0.098, 0.0165)
    bar = part("barrel", tube_bm(0.0140, 0.0108, 0.100, 0.660, "y", 40), "steel", bevel=0.0006)
    for ang in range(0, 360, 60):
        c = box_bm((-0.0022, 0.190, 0.0115), (0.0022, 0.500, 0.020))
        rotate_bm(c, ang, "Y")
        cut(bar, c, f"sr_flute{ang}")
    brk = part("brake", tube_bm(0.0150, 0.0150, 0.656, 0.722, "y", 32, r_in=0.0062), "steel", bevel=0.0008)
    for i in range(2):
        y = 0.674 + i * 0.026
        cut(brk, box_bm((-0.03, y - 0.0065, -0.0085), (0.03, y + 0.0065, 0.0085)), f"sr_brake_port{i}")
    # bolt: body, shroud, handle with a round knob; pivots about the bore
    part("bolt_body", tube_bm(0.0105, 0.0105, -0.128, 0.060, "y", 28), "bare", group="Bolt", bevel=0.0006)
    part("bolt_shroud", tube_bm(0.0128, 0.0118, -0.150, -0.124, "y", 28), "steel", group="Bolt", bevel=0.0008)
    part("bolt_arm", tube_bm(0.0036, 0.0036, 0.008, 0.052, "x", 16, (-0.076, 0.0)), "steel", group="Bolt", bevel=0.0005)
    part("bolt_knob", tube_bm(0.0085, 0.0095, 0.048, 0.068, "x", 24, (-0.076, 0.0)), "steel", group="Bolt", bevel=0.0035, segments=3)
    PIVOTS["Bolt"] = (0.0, -0.076, 0.0)
    # chassis: lower, forend tube, stock
    ch = part("chassis", prism_bm(round_profile([(-0.130, -0.016), (0.104, -0.016), (0.104, -0.036), (0.084, -0.046), (-0.030, -0.046), (-0.095, -0.040), (-0.130, -0.030)], 0.004), "x", -0.0215, 0.0215), "accent_od", bevel=0.002)
    cut(ch, box_bm((-0.0150, 0.0080, -0.07), (0.0150, 0.0770, -0.010)), "sr_magwell")
    fe = part("forend", tube_bm(0.0300, 0.0300, 0.100, 0.440, "y", 8, centre=(0, -0.004), r_in=0.0262, ngon=True), "accent_od", bevel=0.0012)
    for k in range(6):
        y = 0.130 + k * 0.050
        for ang in (0, 45, 135, 180, 225, 315):
            c = box_bm((0.015, y - 0.018, -0.0040), (0.05, y + 0.018, 0.0040))
            rotate_bm(c, ang, "Y", (0, 0, -0.004))
            cut(fe, c, f"sr_fe_{k}_{ang}")
    part("forend_cap", tube_bm(0.0303, 0.0303, 0.437, 0.444, "y", 8, centre=(0, -0.004), r_in=0.0150, ngon=True), "accent_od", bevel=0.0006)
    trigger_guard("trig_guard", -0.052, 0.004, -0.044, -0.072, surface="accent_od")
    trigger("trigger", (-0.020, -0.044))
    grip("grip", 12.0, (-0.062, -0.044))
    stp = round_profile([(-0.130, 0.014), (-0.452, 0.010), (-0.462, -0.128), (-0.432, -0.128), (-0.300, -0.074), (-0.185, -0.060), (-0.130, -0.044)], 0.006)
    stk = part("stock", prism_bm(stp, "x", -0.0175, 0.0175), "accent_od", bevel=0.002)
    cut(stk, prism_bm(round_profile([(-0.420, -0.006), (-0.205, -0.004), (-0.250, -0.048), (-0.420, -0.100)], 0.008), "x", -0.03, 0.03), "sr_stock_window")
    part("cheek", prism_bm(round_profile([(-0.360, 0.034), (-0.215, 0.034), (-0.205, 0.012), (-0.370, 0.012)], 0.006), "x", -0.016, 0.016), "polymer", bevel=0.003, segments=3)
    for y in (-0.330, -0.245):
        part(f"cheek_post{y}", tube_bm(0.0035, 0.0035, 0.004, 0.014, "z", 16, (0.0, y)), "steel", bevel=0.0004)
    part("buttpad", prism_bm(round_profile([(-0.474, 0.014), (-0.452, 0.014), (-0.462, -0.132), (-0.474, -0.132)], 0.004), "x", -0.020, 0.020), "rubber", bevel=0.002)
    part("monopod", tube_bm(0.0055, 0.0055, -0.128, -0.100, "z", 20, (0.0, -0.420)), "steel", bevel=0.0006)
    # magazine
    part("mag", prism_bm(round_profile([(0.0095, -0.030), (0.0755, -0.030), (0.0755, -0.096), (0.0095, -0.096)], 0.002), "x", -0.0142, 0.0142), "steel", group="Mag", bevel=0.001)
    part("mag_base", box_bm((-0.0158, 0.0070, -0.104), (0.0158, 0.0785, -0.094)), "polymer", group="Mag", bevel=0.002)
    PIVOTS["Mag"] = (0.0, 0.042, -0.030)
    part("mag_release", box_bm((-0.006, 0.0, -0.058), (0.006, 0.008, -0.046)), "steel", bevel=0.0006)
    # bipod, folded under the forend
    part("bipod_mount", box_bm((-0.014, 0.400, -0.046), (0.014, 0.430, -0.034)), "steel", bevel=0.0012)
    for sx in (-1, 1):
        part(f"bipod_leg{sx}", tube_bm(0.0058, 0.0050, 0.245, 0.418, "y", 18, (sx * 0.0115, -0.044)), "alloy", bevel=0.0006)
        part(f"bipod_foot{sx}", tube_bm(0.0075, 0.0075, 0.236, 0.247, "y", 18, (sx * 0.0115, -0.044)), "rubber", bevel=0.0015)
    # scope: 30 mm tube on two rings, objective and ocular bells, turrets
    Z = 0.062
    part("scope_tube", tube_bm(0.0150, 0.0150, -0.105, 0.160, "y", 40, (0.0, Z)), "alloy", bevel=0.0006)
    part("scope_obj_taper", tube_bm(0.0150, 0.0275, 0.158, 0.205, "y", 40, (0.0, Z)), "alloy", bevel=0.0006)
    part("scope_obj", tube_bm(0.0275, 0.0275, 0.203, 0.262, "y", 40, (0.0, Z), r_in=0.0238), "alloy", bevel=0.0008)
    part("scope_eye_taper", tube_bm(0.0205, 0.0150, -0.140, -0.103, "y", 40, (0.0, Z)), "alloy", bevel=0.0006)
    ey = part("scope_eye", tube_bm(0.0210, 0.0210, -0.198, -0.138, "y", 40, (0.0, Z), r_in=0.0172), "alloy", bevel=0.0008)
    cut(ey, box_bm((-0.0011, -0.146, Z + 0.0195), (0.0011, -0.140, Z + 0.03)), "sr_eye_mark")  # dioptre index
    part("scope_power", tube_bm(0.0215, 0.0215, -0.132, -0.112, "y", 40, (0.0, Z)), "rubber", bevel=0.0008)
    lens("lens_obj", 0.245, 0.0238, Z)
    lens("lens_eye", -0.192, 0.0172, Z)
    tur = part("turret_elev", tube_bm(0.0118, 0.0118, Z + 0.014, Z + 0.036, "z", 32, (0.0, 0.025)), "alloy", bevel=0.0008)
    for k in range(16):
        c = box_bm((0.0105, 0.0235, Z + 0.024), (0.014, 0.0265, Z + 0.0375))
        rotate_bm(c, k * 22.5, "Z", (0, 0.025, 0))
        cut(tur, c, f"sr_elev_knurl{k}")
    part("turret_elev_base", tube_bm(0.0128, 0.0128, Z + 0.012, Z + 0.016, "z", 32, (0.0, 0.025)), "alloy", bevel=0.0005)
    part("turret_wind", tube_bm(0.0108, 0.0108, 0.014, 0.034, "x", 32, (0.025, Z)), "alloy", bevel=0.0008)
    part("turret_parallax", tube_bm(0.0120, 0.0120, -0.036, -0.014, "x", 32, (0.025, Z)), "alloy", bevel=0.0008)
    part("turret_saddle", tube_bm(0.0185, 0.0185, -0.004, 0.054, "y", 32, (0.0, Z)), "alloy", bevel=0.0010)
    for y in (-0.060, 0.098):
        part(f"ring{y}", tube_bm(0.0185, 0.0185, y - 0.0075, y + 0.0075, "y", 32, (0.0, Z), r_in=0.0151), "steel", bevel=0.0008)
        part(f"ring_base{y}", box_bm((-0.0110, y - 0.0075, 0.0255), (0.0110, y + 0.0075, Z - 0.014)), "steel", bevel=0.0008)
        screw(f"ring_screw{y}", (0.0115, y, 0.034), "x", 0.0024, 0.0016)
    markers = [
        empty("Muzzle", (0.0, 0.722, 0.0)),
        empty("Sight", (0.0, -0.198, Z)),
        empty("Eject", (0.018, 0.010, 0.006)),
        empty("Grip_R", (0.0, -0.075, -0.090)),
        empty("Grip_L", (0.0, 0.260, -0.040)),
        empty("MagWell", (0.0, 0.042, -0.030)),
    ]
    return T, markers


# =========================================================================== P-45 Brace (PT)
def build_pt():
    """
    P-45 Brace: striker-fired service pistol. Steel slide with front and rear serrations and a
    top ejection port, three-dot sights, polymer frame with an accessory rail and a raked
    stippled grip, flush steel magazine with a polymer base pad.
    """
    T = "PT"
    sl = part("slide", prism_bm(round_profile([(-0.096, -0.010), (0.094, -0.010), (0.094, 0.0155), (0.088, 0.0205), (-0.088, 0.0205), (-0.096, 0.0145)], 0.0022), "x", -0.0125, 0.0125), "steel", group="Slide", bevel=0.0012)
    cut(sl, box_bm((0.0, -0.004, 0.006), (0.02, 0.040, 0.03)), "pt_port")
    for sx in (-1, 1):
        cut(sl, array_bm(lambda i: side_box(sx, 0.0125, 0.0009, -0.088, -0.0855, -0.007, 0.016), 9, (0, 0.0042, 0)), f"pt_rear_serr{sx}")
        cut(sl, array_bm(lambda i: side_box(sx, 0.0125, 0.0009, 0.050, 0.0525, -0.007, 0.016), 7, (0, 0.0042, 0)), f"pt_front_serr{sx}")
    cut(sl, tube_bm(0.0052, 0.0052, 0.090, 0.10, "y", 24), "pt_muzzle_hole")
    # rear (notch) and front sights with white dots, mounted on the slide
    rs = part("rear_sight", box_bm((-0.0105, -0.090, 0.0200), (0.0105, -0.079, 0.0305)), "steel", group="Slide", bevel=0.0008)
    cut(rs, box_bm((-0.0019, -0.095, 0.0270), (0.0019, -0.074, 0.035)), "pt_notch")
    part("front_sight", box_bm((-0.0017, 0.078, 0.0200), (0.0017, 0.087, 0.0270)), "steel", group="Slide", bevel=0.0005)
    for x, y, z in ((-0.0062, -0.0789, 0.0262), (0.0062, -0.0789, 0.0262), (0.0, 0.0871, 0.0238)):
        part(f"dot{x}{y}", tube_bm(0.0011, 0.0011, y, y + 0.0004, "y", 12, (x, z)), "polymer_fde", group="Slide", bevel=0.0)
    PIVOTS["Slide"] = (0.0, 0.0, 0.004)
    # barrel: hood shows in the port, crown at the muzzle
    part("barrel", tube_bm(0.0068, 0.0068, -0.010, 0.0935, "y", 28, r_in=0.0045), "bare", bevel=0.0004)
    part("barrel_hood", box_bm((-0.0074, -0.003, 0.002), (0.0074, 0.038, 0.0105)), "bare", bevel=0.0006)
    # frame with dust cover rail, trigger guard, grip
    fr = part("frame", prism_bm(round_profile([(-0.086, -0.010), (0.090, -0.010), (0.090, -0.024), (0.034, -0.026), (0.028, -0.031), (-0.030, -0.031), (-0.048, -0.022), (-0.086, -0.018)], 0.002), "x", -0.0115, 0.0115), "polymer", bevel=0.0015)
    cut(fr, array_bm(lambda i: box_bm((-0.02, 0.046, -0.028), (0.02, 0.050, -0.022)), 3, (0, 0.012, 0)), "pt_rail_slots")
    trigger_guard("trig_guard", -0.030, 0.034, -0.026, -0.050, half_width=0.0060, thick=0.0050)
    trigger("trigger", (0.002, -0.022), scale=0.95)
    grip("grip", 18.0, (-0.026, -0.022), height=0.098, half_width=0.0145)
    part("slide_stop", prism_bm(round_profile([(-0.014, -0.012), (0.012, -0.012), (0.014, -0.016), (-0.014, -0.017)], 0.0008), "x", -0.0135, -0.0115), "steel", bevel=0.0004)
    part("takedown", tube_bm(0.0028, 0.0028, -0.0130, -0.0115, "x", 16, (0.020, -0.017)), "steel", bevel=0.0003)
    # magazine: inside the grip, along its rake; the base pad shows
    mg = prism_bm(round_profile([(-0.031, -0.024), (-0.006, -0.024), (-0.006, -0.112), (-0.031, -0.112)], 0.002), "x", -0.0092, 0.0092)
    rotate_bm(mg, -18.0, "X", (0, 0, 0)); move_bm(mg, (0, -0.026, -0.022))
    part("mag", mg, "steel", group="Mag", bevel=0.0008)
    bp = box_bm((-0.0125, -0.0365, -0.118), (0.0125, -0.0005, -0.106))
    rotate_bm(bp, -18.0, "X", (0, 0, 0)); move_bm(bp, (0, -0.026, -0.022))
    part("mag_base", bp, "polymer", group="Mag", bevel=0.002)
    PIVOTS["Mag"] = (0.0, -0.040, -0.044)
    markers = [
        empty("Muzzle", (0.0, 0.094, 0.0)),
        empty("Sight", (0.0, -0.084, 0.0270)),
        empty("Eject", (0.012, 0.016, 0.016)),
        empty("Grip_R", (0.0, -0.050, -0.062)),
        empty("Grip_L", (0.0, -0.046, -0.078)),
        empty("MagWell", (0.0, -0.040, -0.044)),
    ]
    return T, markers


BUILDERS = {"AR": build_ar, "SMG": build_smg, "SG": build_sg, "SR": build_sr, "PT": build_pt}


def run(only=None):
    results = {}
    for tag, fn in BUILDERS.items():
        if only and tag not in only:
            continue
        wipe()
        PIVOTS.clear()
        tag, markers = fn()
        results[tag] = finalize_weapon(tag, os.path.join(OUT, tag), RES, do_bake=DO_BAKE, markers=markers)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "ArtSource", "Blender", f"weapon_{tag}.blend"), copy=True)
    return results


if globals().get("__name__") == "weapons":
    RESULT = run(globals().get("WEAPONS"))
