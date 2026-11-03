"""
Makes a neutral grey copy of the CC0 container scan, so each container can be painted its own colour
by the material tint (red, blue, white, green, orange) instead of every container being the scan's green.

Input:  TexturesCC0/T_Container_{D.jpg, N.jpg, M.png}   (from import_cc0.py, Poly Haven "container_side", CC0)
Output: TexturesCC0/T_ContainerPaint_D.jpg
        The albedo keeps the scan's streaks, dents and dirt as brightness variation around a mean linear
        luminance of 0.5. The materials reuse T_Container_N/_M (KappaSceneFactory.SharedScanMaps).
        sources.json gets an entry.

Run:  python3 derive_container_paint.py <TexturesCC0 dir>
"""
import json
import os
import sys

import numpy as np
from PIL import Image

TARGET = 0.5


def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def main():
    d = sys.argv[1]
    rgb = np.asarray(Image.open(os.path.join(d, "T_Container_D.jpg")).convert("RGB"), dtype=np.float64) / 255.0
    lin = srgb_to_lin(rgb)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    grey = np.clip(lum * (TARGET / lum.mean()), 0.0, 0.92)
    out = lin_to_srgb(np.repeat(grey[..., None], 3, axis=-1))
    Image.fromarray((out * 255.0 + 0.5).astype(np.uint8)).save(os.path.join(d, "T_ContainerPaint_D.jpg"), quality=93, subsampling=0)

    path = os.path.join(d, "sources.json")
    sources = json.load(open(path))
    base = sources["Container"]
    sources["ContainerPaint"] = {
        "polyhaven_id": base["polyhaven_id"], "url": base["url"], "license": "CC0", "authors": base["authors"],
        "size_m": base["size_m"], "treatment": "neutral grey, derived from T_Container_D (derive_container_paint.py)",
        "albedo_lum_linear": round(float(grey.mean()), 3),
    }
    with open(path, "w") as f:
        json.dump(sources, f, indent=2)
    print("ContainerPaint: mean linear luminance", round(float(grey.mean()), 3), "(scan was", round(float(lum.mean()), 3), ")")


if __name__ == "__main__":
    main()
