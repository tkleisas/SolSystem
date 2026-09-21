#!/usr/bin/env python3
"""
Builds Earth's albedo map from the packed data map.

    pack_earth.py --out art/textures/earth_albedo.jpg

`art/textures/earth_map.png` is a *data* map: red is a land mask, green a coastline outline, blue
the cosine of latitude. It has no colour in it at all, because the Blender preview shader computed
the colour from those three channels. A game renderer wants an ordinary albedo — one texture fetch
and no shader — so this does the same computation offline, once, and ships the result.

The colouring is the same one the Blender shader uses, so a planet rendered in the game matches the
planet rendered in the preview:

    ocean      a dark blue, because the sea from orbit is dark
    land       a five-stop ramp in latitude: ice, taiga, temperate, desert, tropical
    coast      darker and greener where the coastline channel says so

**The latitude ramp is a stand-in for climate and it is honest about it.** Real land colour depends
on rainfall, altitude, ocean currents and the season; what a planet looks like from orbit is mostly
latitude plus the two great deserts, and this is latitude. The Sahara comes out desert because it is
at the right latitude, not because it is a desert — which is the kind of thing worth writing down
rather than discovering later.
"""

import argparse
import os

from PIL import Image


def ramp(latitude_cos, stops):
    """Linear interpolation through a list of (position, (r, g, b)) stops."""
    if latitude_cos <= stops[0][0]:
        return stops[0][1]

    if latitude_cos >= stops[-1][0]:
        return stops[-1][1]

    for i in range(len(stops) - 1):
        a_pos, a_colour = stops[i]
        b_pos, b_colour = stops[i + 1]

        if a_pos <= latitude_cos <= b_pos:
            span = b_pos - a_pos
            t = 0.0 if span == 0 else (latitude_cos - a_pos) / span
            return tuple(a + ((b - a) * t) for a, b in zip(a_colour, b_colour))

    return stops[-1][1]


# The five biomes, by the cosine of latitude: 0 is the pole and 1 the equator.
BIOMES = [
    (0.10, (0.72, 0.76, 0.80)),   # ice
    (0.36, (0.10, 0.19, 0.09)),   # taiga
    (0.62, (0.16, 0.28, 0.10)),   # temperate
    (0.82, (0.44, 0.34, 0.14)),   # desert
    (0.95, (0.20, 0.30, 0.09)),   # tropical
]

OCEAN = (0.012, 0.045, 0.130)


def build(source, destination, coastal_darkening):
    image = Image.open(source).convert("RGB")
    width, height = image.size
    pixels = image.load()

    out = Image.new("RGB", (width, height))
    target = out.load()

    for y in range(height):
        for x in range(width):
            land_raw, coast_raw, latitude_raw = pixels[x, y]

            land = land_raw / 255.0
            coast = coast_raw / 255.0
            latitude = latitude_raw / 255.0

            # The mask arrives as a hard step; a small ramp gives the coastline an edge rather than
            # a staircase, which at this resolution is one pixel wide and still visible from orbit.
            blend = min(1.0, max(0.0, (land - 0.42) / 0.16))

            biome = ramp(latitude, BIOMES)

            # Coastal margins are darker, from the outline channel.
            land_colour = tuple(c * (1.0 - (coastal_darkening * coast)) for c in biome)

            colour = tuple(
                (ocean * (1.0 - blend)) + (land_colour_component * blend)
                for ocean, land_colour_component in zip(OCEAN, land_colour))

            # To 8-bit display values. The frame buffer this ends up in is not gamma-corrected, so
            # the value stored is the value seen — around the outside of a square root, which is
            # roughly what a display does with a linear colour.
            target[x, y] = tuple(
                max(0, min(255, int(round((c ** (1.0 / 2.2)) * 255.0)))) for c in colour)

    out.save(destination, quality=90, optimize=True)
    print(f"{destination}: {width}x{height}, {os.path.getsize(destination):,} bytes")


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", default="art/textures/earth_map.png")
    parser.add_argument("--out", default="art/textures/earth_albedo.jpg")
    parser.add_argument("--coastal-darkening", type=float, default=0.35)
    args = parser.parse_args()

    build(args.source, args.out, args.coastal_darkening)


if __name__ == "__main__":
    main()
