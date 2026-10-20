"""
The surface detail maps for the hulls.

    python3 tools/blender/hull_textures.py

The hulls are modelled as shapes, and shape alone cannot say what a surface is
made of. These are the grayscale maps that turn a smooth white torus into painted
plate and a steel barrel into a barrel that has been to space: panel seams with a
bevel highlight, rivets, brushed streaks, paint chips and grime. They are tiled
in the model, so every map must be seamless in both directions.

WHY GRAYSCALE. The map modulates a material's own colour through a MULTIPLY node
in Blender and through the glTF base-colour factor in the client. That keeps one
texture serving every painted material on a hull — the courier's white steel and
its dark trim take the same seams — and it means a colour change is still a change
to the material, not to a bitmap. The maps are near white on purpose: the detail
is the dark in the seams, not a tint over the whole surface.

The three languages are different on purpose, and the design says why:

  illuminus  smooth, few seams, brushed metal. A ship that has never been rained on
  workers    painted plate, patch panels, chipped paint, rust streaks, grime
  meridian   clean white panels, seams, the grime that collects in a seam anyway

These files are inputs to the Blender scripts, not the client: the client reads
the maps out of the exported GLB, where they are embedded.
"""

import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(PROJECT, "art", "textures", "hulls")

SIZE = 1024


def wrap_noise(size, cells, seed):
    """A smooth value-noise field that wraps at the edges."""
    rng = np.random.default_rng(seed)
    low = rng.random((cells, cells)).astype(np.float32)
    padded = np.pad(low, 1, mode="wrap")
    image = Image.fromarray((padded * 255).astype(np.uint8), "L")
    image = image.resize((size + 2 * size // cells, size + 2 * size // cells),
                         Image.BICUBIC)
    a = size // cells
    return np.asarray(image)[a:a + size, a:a + size].astype(np.float32) / 255.0


def grid(size, cells, seam=0.62, width=2, highlight=0.14):
    """A panel grid with a dark seam and a one-pixel bevel highlight."""
    a = np.ones((size, size), np.float32)
    step = size // cells
    for i in range(cells):
        x = i * step
        a[:, x:x + width] *= seam
        a[:, x + width:x + width + 1] = np.minimum(1.0, a[:, x + width:x + width + 1] + highlight)
        a[x:x + width, :] *= seam
        a[x + width:x + width + 1, :] = np.minimum(1.0, a[x + width:x + width + 1, :] + highlight)
    return a


def rivets(size, cells, value=0.72):
    """A rivet at each panel corner, drawn with wrap copies."""
    a = np.ones((size, size), np.float32)
    step = size // cells
    yy, xx = np.mgrid[0:size, 0:size]
    for i in range(cells):
        for j in range(cells):
            cx, cy = i * step, j * step
            d = (xx - cx) ** 2 + (yy - cy) ** 2
            a[d <= 3.0] = np.minimum(a[d <= 3.0], value)
    return a


def brushed(size, seed, strength=0.03):
    """Fine horizontal streaks, wrapped."""
    rng = np.random.default_rng(seed)
    lines = rng.normal(0.0, strength, size).astype(np.float32)
    field = np.tile(lines, (size, 1))
    return 1.0 + field


def scratches(image, count, seed, dark=0.55):
    """A few long scratches; each is drawn twice so the tile stays seamless."""
    rng = random.Random(seed)
    draw = ImageDraw.Draw(image)
    for _ in range(count):
        x = rng.randrange(SIZE)
        y = rng.randrange(SIZE)
        length = rng.randrange(40, 220)
        angle = rng.uniform(-0.35, 0.35) + (math.pi / 2 if rng.random() < 0.5 else 0.0)
        x2 = x + math.cos(angle) * length
        y2 = y + math.sin(angle) * length
        value = int(255 * rng.uniform(dark, dark + 0.18))
        width = rng.choice((1, 1, 2))
        for dx in (-SIZE, 0, SIZE):
            for dy in (-SIZE, 0, SIZE):
                draw.line((x + dx, y + dy, x2 + dx, y2 + dy), fill=(value, value, value), width=width)
    return image


def patches(image, cells, seed, count):
    """Replacement panels: a few cells tinted a little lighter or darker."""
    rng = random.Random(seed)
    draw = ImageDraw.Draw(image)
    step = SIZE // cells
    for _ in range(count):
        i, j = rng.randrange(cells), rng.randrange(cells)
        value = int(255 * rng.uniform(0.72, 1.0))
        draw.rectangle((i * step + 4, j * step + 4,
                        (i + 1) * step - 4, (j + 1) * step - 4),
                       fill=(value, value, value))
    return image


def to_image(field):
    field = np.clip(field, 0.0, 1.0)
    rgb = np.repeat((field * 255).astype(np.uint8)[:, :, None], 3, axis=2)
    return Image.fromarray(rgb, "RGB")


def illuminus():
    base = np.ones((SIZE, SIZE), np.float32)
    base *= grid(SIZE, 2, seam=0.72, width=2, highlight=0.10)
    base *= rivets(SIZE, 2, value=0.80)
    base *= 1.0 + (wrap_noise(SIZE, 8, 11) - 0.5) * 0.035
    base *= brushed(SIZE, 12, 0.018)
    image = to_image(base)
    image = scratches(image, 9, 13, dark=0.66)
    return image.filter(ImageFilter.SMOOTH)


def workers():
    base = np.ones((SIZE, SIZE), np.float32)
    base *= grid(SIZE, 4, seam=0.56, width=3, highlight=0.16)
    base *= rivets(SIZE, 4, value=0.70)
    base *= 0.90 + wrap_noise(SIZE, 6, 21) * 0.16
    base *= 0.96 + wrap_noise(SIZE, 24, 22) * 0.08
    image = to_image(base)
    image = patches(image, 4, 23, 5)
    image = scratches(image, 26, 24, dark=0.48)
    # Rust: warm, irregular, and concentrated in a few streaks, because rust on a
    # maintained ship runs from a fitting rather than covering the plate.
    px = image.load()
    rng = random.Random(25)
    for _ in range(7):
        x = rng.randrange(SIZE)
        y = rng.randrange(SIZE)
        for step in range(rng.randrange(60, 200)):
            xx = (x + int(math.sin(step * 0.09) * 5)) % SIZE
            yy = (y + step) % SIZE
            r, g, b = px[xx, yy]
            px[xx, yy] = (min(255, r + 20), max(0, g - 8), max(0, b - 18))
    return image.filter(ImageFilter.SMOOTH)


def meridian():
    base = np.ones((SIZE, SIZE), np.float32)
    base *= grid(SIZE, 3, seam=0.68, width=2, highlight=0.12)
    base *= rivets(SIZE, 3, value=0.78)
    base *= 0.94 + wrap_noise(SIZE, 5, 31) * 0.10
    base *= 1.0 + (wrap_noise(SIZE, 32, 32) - 0.5) * 0.03
    image = to_image(base)
    # Grime in the seams: a soft dark wash, strongest where the panel lines are.
    grime = 1.0 - (1.0 - grid(SIZE, 3, seam=0.55, width=6, highlight=0.0)) * 0.35
    grime = np.clip(grime, 0.0, 1.0)
    image = Image.composite(to_image(grime), image, Image.fromarray(
        (np.clip(1.0 - grime, 0.0, 1.0) * 255).astype(np.uint8), "L"))
    image = scratches(image, 12, 33, dark=0.60)
    return image.filter(ImageFilter.SMOOTH)


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, build in (("illuminus_detail.png", illuminus),
                        ("workers_detail.png", workers),
                        ("meridian_detail.png", meridian)):
        image = build()
        path = os.path.join(OUT, name)
        image.save(path, optimize=True)
        print(f"  {name}  {image.size[0]}x{image.size[1]}")


if __name__ == "__main__":
    main()
