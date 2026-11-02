"""
Converts downloaded CC0 Poly Haven texture sets into the layout the Kappa materials use.

Input:  <src>/<polyhaven_id>/{Diffuse.jpg, nor_gl.jpg, arm.jpg}   (2K, from api.polyhaven.com)
Output: <out>/T_<Name>_D.jpg   albedo (sRGB)
        <out>/T_<Name>_N.jpg   normal map, OpenGL convention (same as Unity)
        <out>/T_<Name>_M.png   mask: R = metallic, G = ambient occlusion, B = 0, A = smoothness
                               (URP Lit reads R/A as its metallic map and G as its occlusion map)

Run:  python3 import_cc0.py <src> <out>
Every source is CC0; authors are listed in ATTRIBUTIONS.md and in sources.json (written here).
"""
import json
import os
import sys

import numpy as np
from PIL import Image

# name used by the game -> (Poly Haven id, albedo treatment, target mean linear albedo or None)
# Scanned albedo is often darker than the real surface (exposure and cross-polarisation choices
# vary per scan). Dark albedo kills bounce light, so each set is scaled to a measured-world mean:
# concrete 0.2-0.35, weathered wood 0.15-0.3, rusted steel 0.08-0.15, asphalt 0.1-0.15.
SETS = {
    "Asphalt": ("asphalt_02", None, 0.12),
    "Concrete": ("concrete_wall_006", None, 0.24),
    "ConcreteSmooth": ("concrete_wall_008", None, None),
    "ConcreteFloor": ("concrete_floor_worn_001", None, 0.18),
    "Facade": ("painted_plaster_wall", None, None),
    "Salt": ("sand_02", "salt", None),
    "Metal": ("metal_plate_02", None, 0.14),
    "Grate": ("metal_grate_rusty", None, 0.12),
    "TankPaint": ("rusty_metal_04", None, 0.12),
    "Rust": ("rust_coarse_01", None, 0.10),
    "Corrugated": ("corrugated_iron_02", None, 0.16),
    "CorrugatedRust": ("rusty_corrugated_iron", None, 0.10),
    "Container": ("container_side", None, None),
    "Wood": ("weathered_planks", None, 0.16),
    "Rock": ("rock_face", None, 0.14),
    "Ground": ("dry_ground_rocks", "muted", None),
    "Gravel": ("gravel_floor", None, 0.22),
    "Shutter": ("painted_metal_shutter", None, None),
}


def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def salt(rgb):
    """Sand -> evaporated salt crust: keep the lumps and grain, lose the colour, lift to off-white."""
    lin = srgb_to_lin(rgb)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    z = (lum - lum.mean()) / (lum.std() + 1e-6)
    base = np.array([0.78, 0.76, 0.73])            # linear, slightly warm white
    out = base[None, None, :] * (1.0 + 0.10 * np.clip(z, -3, 3))[..., None]
    return lin_to_srgb(out)


def calibrate(rgb, target, saturation=1.0):
    """Scale linear albedo so its mean luminance is `target` (never pushing the brightest 0.5% past 0.9)."""
    lin = srgb_to_lin(rgb)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    scale = 1.0
    if target is not None:
        scale = min(target / max(float(lum.mean()), 1e-4), 0.9 / max(float(np.percentile(lum, 99.5)), 1e-4))
    lin = lin * scale
    if saturation != 1.0:
        l2 = (lum * scale)[..., None]
        lin = l2 + (lin - l2) * saturation
    return lin_to_srgb(lin), scale


def convert(src, out, name, pid, treatment, target):
    d = os.path.join(src, pid)
    alb = Image.open(os.path.join(d, "Diffuse.jpg")).convert("RGB")
    nor = Image.open(os.path.join(d, "nor_gl.jpg")).convert("RGB")
    arm = np.asarray(Image.open(os.path.join(d, "arm.jpg")).convert("RGB"), dtype=np.float32) / 255.0
    scale = 1.0
    if treatment == "salt":
        a = salt(np.asarray(alb, dtype=np.float32) / 255.0)
        alb = Image.fromarray((a * 255.0 + 0.5).astype(np.uint8))
    elif target is not None or treatment == "muted":
        # "muted": the dry hillside reads orange under a warm sun; take 30% of the colour out.
        a, scale = calibrate(np.asarray(alb, dtype=np.float32) / 255.0, target, 0.7 if treatment == "muted" else 1.0)
        alb = Image.fromarray((a * 255.0 + 0.5).astype(np.uint8))
    alb.save(os.path.join(out, f"T_{name}_D.jpg"), quality=93, subsampling=0)
    nor.save(os.path.join(out, f"T_{name}_N.jpg"), quality=95, subsampling=0)
    ao, rough, metal = arm[..., 0], arm[..., 1], arm[..., 2]
    mask = np.stack([metal, ao, np.zeros_like(ao), 1.0 - rough], axis=-1)
    Image.fromarray((mask * 255.0 + 0.5).astype(np.uint8)).save(os.path.join(out, f"T_{name}_M.png"), optimize=True)
    lin = srgb_to_lin(np.asarray(alb, dtype=np.float32)[::4, ::4] / 255.0)
    return {"albedo_scale": round(scale, 3),
            "albedo_lum_linear": round(float((0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]).mean()), 3),
            "albedo_mean": [round(float(v), 3) for v in np.asarray(alb, dtype=np.float32).reshape(-1, 3).mean(0) / 255.0],
            "metal_mean": round(float(metal.mean()), 3), "rough_mean": round(float(rough.mean()), 3), "ao_mean": round(float(ao.mean()), 3)}


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    sources = {}
    for name, (pid, treatment, target) in SETS.items():
        stats = convert(src, out, name, pid, treatment, target)
        info_path = os.path.join(src, "info", pid + ".json")
        info = json.load(open(info_path)) if os.path.exists(info_path) else {}
        dims = info.get("dimensions") or [0, 0]
        sources[name] = {
            "polyhaven_id": pid,
            "url": f"https://polyhaven.com/a/{pid}",
            "license": "CC0",
            "authors": sorted((info.get("authors") or {}).keys()),
            "size_m": round(float(dims[0]) / 1000.0, 2),
            "treatment": treatment,
            **stats,
        }
        print(name, pid, stats)
    with open(os.path.join(out, "sources.json"), "w") as f:
        json.dump(sources, f, indent=2)


if __name__ == "__main__":
    main()
