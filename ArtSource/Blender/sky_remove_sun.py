"""
Run inside Blender. Makes the Kappa skybox from a CC0 Poly Haven HDRI by taking the sun out of it.

The game lights the map with a real directional light (realtime shadows, baked bounce), placed where
the HDRI's sun is. Left in the sky texture, the sun would be counted a second time by the ambient and
lightmap bake: lit shadows and noisy hot spots. So every pixel brighter than CLAMP is scaled down to
CLAMP, keeping its hue. The glow round the sun stays; the disc's energy goes.

Globals: SKY_SRC (input .hdr), SKY_DST (output .hdr), SKY_CLAMP (default 12).
Sets RESULT with the sun position (u, v from the bottom) and how much energy was removed.
"""
import bpy
import numpy as np

SRC = globals()["SKY_SRC"]
DST = globals()["SKY_DST"]
CLAMP = float(globals().get("SKY_CLAMP", 12.0))

img = bpy.data.images.load(SRC, check_existing=False)
W, H = img.size
a = np.empty(W * H * 4, dtype=np.float32)
img.pixels.foreach_get(a)
a = a.reshape(H, W, 4)
lum = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]

r, c = np.unravel_index(int(np.argmax(lum)), lum.shape)
rows = np.arange(H, dtype=np.float32)
solid = np.sin(np.pi * (rows + 0.5) / H)[:, None]          # pixel solid angle ~ sin(polar angle)
energy = float((lum * solid).sum())

scale = np.minimum(1.0, CLAMP / np.maximum(lum, 1e-6))
a[..., :3] *= scale[..., None]
lum2 = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
energy2 = float((lum2 * solid).sum())

out = bpy.data.images.new("kappa_sky_nosun", W, H, alpha=False, float_buffer=True)
out.pixels.foreach_set(a.ravel())
out.filepath_raw = DST
out.file_format = "HDR"
out.save()
bpy.data.images.remove(out)
bpy.data.images.remove(img)

RESULT = {
    "size": [W, H],
    "sun_u": (c + 0.5) / W,
    "sun_v_from_bottom": (r + 0.5) / H,
    "sun_elevation_deg": 90.0 - (1.0 - (r + 0.5) / H) * 180.0,
    "clamped_pixels": int((scale < 1.0).sum()),
    "energy_removed_fraction": 1.0 - energy2 / energy,
    "dst": DST,
}
