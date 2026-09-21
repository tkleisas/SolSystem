#!/usr/bin/env python3
"""
Turns the raw body bakes into textures a fixed-function renderer can draw.

    python3 tools/pack_bodies.py

Run this after `blender --background --python tools/blender/bake_bodies.py`. It is a separate step
because Blender's bundled Python has no imaging library, and a post-processing pass that lived
inside the bake script silently did nothing at all — three times, because the failure mode of a
`try: import PIL except ImportError: return` is to succeed quietly.

Two of the bakes come out correct and not directly drawable:

**The cloud coverage is a greyscale mask with no alpha.** The client has no fragment shader, so it
cannot turn a luminance into an alpha; the texture has to carry one. The mask becomes the alpha of
a white sheet, which is what a cloud deck is.

**The Sun's photosphere bakes at the emission strength the material was authored with** — a little
under one, so that the Blender preview exposed it sensibly. A body whose entire job is to be the
brightest thing in the sky should saturate its own texture, so it is normalised here rather than
being multiplied by a magic number in the renderer.
"""

import os
import sys

from PIL import Image

TEXTURE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "art", "textures")


def make_clouds(path):
    """
    Greyscale coverage to a white sheet whose alpha is the coverage.

    The coverage is pushed through a curve first, and that is not a taste: the bake comes back with
    a fairly even mid-grey everywhere, which draws as a uniform haze over the whole planet rather
    than as weather. A three-quarter-black floor clears the sky between the systems and a gain above
    it gives the cloud tops somewhere to go.
    """
    # The coverage is the LUMINANCE of the bake, which is written without an alpha channel on
    # purpose. This reads it and writes an alpha sheet; running it twice would therefore read its own
    # white sheet and overcast the planet, so it is a step in a pipeline rather than something to
    # run on a whim. The bake is the source of truth and the bake script says when to run this.
    mask = Image.open(path).convert("L")

    floor = 85
    gain = 255.0 / (255.0 - floor)
    mask = mask.point(lambda v: 0 if v <= floor else min(255, int((v - floor) * gain)))

    sheet = Image.new("RGBA", mask.size, (255, 255, 255, 0))
    sheet.putalpha(mask)
    sheet.save(path)

    histogram = mask.histogram()
    total = sum(histogram)
    mean = sum(i * v for i, v in enumerate(histogram)) / max(total, 1)
    return f"coverage mean {mean:.0f} of 255, {sheet.mode}"


def normalise_sun(path):
    """Scale the photosphere so its brightest 0.1% is white."""
    image = Image.open(path).convert("RGB")

    histogram = image.convert("L").histogram()
    total = sum(histogram)
    running = 0
    high = 255

    for value, count in enumerate(histogram):
        running += count
        if running >= total * 0.999:
            high = value
            break

    # The 99.9th percentile rather than the maximum: one hot pixel on a bake seam should not decide
    # the exposure of the whole disc.
    scale = 255.0 / max(high, 1)
    image.point(lambda v: min(255, int(v * scale))).save(path)
    return f"scaled by {scale:.2f} from a 99.9th percentile of {high}"


JOBS = [
    ("earth_clouds.png", make_clouds),
    ("sun_photosphere.png", normalise_sun),
]


def main():
    directory = os.path.normpath(TEXTURE_DIR)

    for name, job in JOBS:
        path = os.path.join(directory, name)

        if not os.path.exists(path):
            print(f"  {name}: not baked yet")
            continue

        note = job(path)
        print(f"  {name}: {note}  ({os.path.getsize(path):,} bytes)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
