"""
Sketch -- "The Disk". An Illuminus station archetype.

Not a wheel: a solid disk, six hundred metres across, a hundred and fifty thick at
the crown. The spin arithmetic, because it is the whole reason a disk this size is
allowed to exist:

    rim radius   300 m
    one gravity  w = sqrt(9.81 / 300) = 0.181 rad/s = 1.73 rpm

Twice Meridian's stately 0.95, and still inside the two-to-four-rpm comfort band
every study puts on crews who have to live with it. A smaller wheel must spin
faster for the same gravity, and the crew notice -- Coriolis is a fact of dinner
-- but 1.7 rpm is adaptation, not endurance. The Illuminus pay it, because their
stations are residences and statements, not waystations, and a residence does not
ask its owners to adapt to half a gravity.

Design language (§6.5): smooth, geometric, gleaming, intimidating, displayed
rather than used. The disk is a chess piece: three stacked plates, a crown dome,
a mooring spire with a glowing core at its foot. Nothing protrudes that is not
the spire. There are no trusses, no booms, no scaffolding. The radiators are
flush blades on the underside, because even the thermal problem gets faired.

    blender --background --python tools/blender/stations/sketches/disk_a.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

import bpy  # noqa: E402
from pipeline import (  # noqa: E402
    box, collection, cylinder, lathe, link, material, render_preview, render_views,
    reset, ring_of, shade_smooth_by_angle, sphere, torus,
)

DISK_RADIUS = 300.0            # the rim: 600 m across
SPIRE_TOP = 210.0

MATERIALS = {}


def build_materials():
    # Mirror steel and almost nothing else, with the one indulgence: the core
    # glows. An Illuminus station is a residence on display, and a displayed
    # thing is allowed exactly one light of its own.
    MATERIALS["steel"] = material(
        "IlluminusHull", (0.85, 0.87, 0.90), metallic=0.95, roughness=0.10)
    MATERIALS["steel_worn"] = material(
        "IlluminusHullWorn", (0.62, 0.635, 0.67), metallic=0.6, roughness=0.40)
    MATERIALS["dark"] = material(
        "IlluminusTrim", (0.02, 0.021, 0.024), metallic=0.85, roughness=0.22)
    MATERIALS["radiator"] = material(
        "IlluminusRadiator", (0.20, 0.21, 0.23), metallic=0.25, roughness=0.55)
    MATERIALS["radiator_hot"] = material(
        "IlluminusRadiatorHot", (0.34, 0.10, 0.05), metallic=0.2, roughness=0.66,
        emission=(0.40, 0.085, 0.03), emission_strength=0.55)
    MATERIALS["glow"] = material(
        "IlluminusCoreGlow", (0.55, 0.40, 0.20), metallic=0.1, roughness=0.4,
        emission=(1.0, 0.72, 0.38), emission_strength=2.4)
    MATERIALS["window"] = material(
        "IlluminusWindow", (0.30, 0.24, 0.12), metallic=0.0, roughness=0.3,
        emission=(1.0, 0.85, 0.55), emission_strength=1.2)

    # The Cygnus convention. The names are the contract.
    MATERIALS["nav_red"] = material(
        "IlluminusNavRed", (0.55, 0.02, 0.02), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.04, 0.03), emission_strength=1.0)
    MATERIALS["nav_green"] = material(
        "IlluminusNavGreen", (0.02, 0.50, 0.06), metallic=0.0, roughness=0.35,
        emission=(0.05, 1.0, 0.12), emission_strength=1.0)
    MATERIALS["nav_white"] = material(
        "IlluminusNavWhite", (0.70, 0.70, 0.68), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.98, 0.92), emission_strength=1.0)
    MATERIALS["nav_yellow"] = material(
        "IlluminusNavYellow", (0.62, 0.50, 0.02), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.78, 0.05), emission_strength=1.0)
    MATERIALS["strobe"] = material(
        "IlluminusStrobe", (0.85, 0.85, 0.85), metallic=0.0, roughness=0.30,
        emission=(1.0, 1.0, 1.0), emission_strength=1.0)


def assign(obj, key):
    obj.data.materials.clear()
    obj.data.materials.append(MATERIALS[key])
    return obj


def lamp(col, parts, name, key, location, radius=0.8):
    housing = sphere(f"{name}_Housing", radius=radius * 1.6, location=location,
                     segments=16, rings=8)
    assign(housing, "dark")
    link(housing, col)
    lens = sphere(name, radius=radius, location=location, segments=16, rings=8)
    assign(lens, key)
    link(lens, col)
    parts.append(housing)
    parts.append(lens)
    return lens


def build_disk(col):
    """
    The chess piece: three stacked plates, a crown dome, a mooring spire.

    The plates step down in radius as they rise, so the silhouette reads as a
    wedding cake and not a can. Every plate's edge carries one thin dark band —
    the only trim on the station — and three small clusters of lit windows,
    because a displayed residence shows its lights in arrangements, not in arrays.
    """
    parts = []

    profile = [
        (0.0, 0.0), (270.0, 0.0), (DISK_RADIUS, 3.0), (DISK_RADIUS, 15.0),
        (282.0, 20.0),
        (282.0, 46.0), (236.0, 48.0),
        (236.0, 72.0), (186.0, 74.0),
        (186.0, 96.0), (92.0, 98.0),
    ]
    # The crown dome, faired into the top plate.
    for i in range(1, 21):
        t = i / 20.0
        r = 92.0 * math.cos(t * math.pi / 2.0) ** 0.8
        profile.append((max(r, 0.0), 98.0 + t * 34.0))
    profile.append((0.0, 132.0))

    disk = lathe("Disk", profile, segments=128)
    assign(disk, "steel")
    shade_smooth_by_angle(disk, math.radians(32))
    link(disk, col)
    parts.append(disk)

    # The plate-edge bands: one thin dark line per plate, the only trim.
    for band_i, (r, z) in enumerate(((DISK_RADIUS, 17.5), (282.0, 47.0),
                                     (236.0, 73.0), (186.0, 97.0))):
        band = torus(f"EdgeBand{band_i}", major=r, minor=0.9,
                     location=(0, 0, z), major_segments=128, minor_segments=8)
        assign(band, "dark")
        link(band, col)
        parts.append(band)

    # The windows: three clusters per plate edge, never a full ring.
    for plate_i, (r, z) in enumerate(((DISK_RADIUS - 1.0, 9.0),
                                      (281.0, 33.0), (235.0, 60.0),
                                      (185.0, 85.0))):
        for cluster in range(3):
            centre = cluster * math.tau / 3.0 + plate_i * 0.45
            for w_i in range(6):
                angle = centre + (w_i - 2.5) * 0.012
                w = box(f"Window{plate_i}_{cluster}_{w_i}", (6.0, 1.0, 2.4),
                        location=(math.cos(angle) * r, math.sin(angle) * r, z),
                        rotation=(0, 0, angle))
                assign(w, "window")
                link(w, col)
                parts.append(w)

    return parts


def build_spire(col):
    """
    The mooring spire: a slender tower from the crown, the glowing core at its
    foot, and the dock at its tip. Ships moor along the axis, the way a ship
    ties up at a pylon — the only way to dock at a disk and keep the disk clean.
    """
    parts = []

    spire = lathe("Spire", [
        (26.0, 96.0),
        (22.0, 112.0),
        (22.0, 186.0),
        (26.0, 196.0),
        (26.0, SPIRE_TOP),
    ], segments=64)
    assign(spire, "steel")
    shade_smooth_by_angle(spire, math.radians(34))
    link(spire, col)
    parts.append(spire)

    # The core: the one light the station is allowed to call its own.
    core = torus("Core", major=24.0, minor=3.2, location=(0, 0, 138.0),
                 major_segments=96, minor_segments=16)
    assign(core, "glow")
    link(core, col)
    parts.append(core)

    collar = torus("MooringCollar", 9.0, 1.6, location=(0, 0, SPIRE_TOP - 4.0),
                   major_segments=48, minor_segments=10)
    assign(collar, "dark")
    link(collar, col)
    parts.append(collar)

    for i, (x, y, z) in enumerate(ring_of(4, 11.0, z=SPIRE_TOP - 8.0,
                                          phase=math.pi / 4)):
        lamp(col, parts, f"MooringGuide{i}", "strobe", (x, y, z), radius=0.7)

    # The convention, on the spire and the rim: reds to port, greens to
    # starboard, TWO whites on the spire, ONE yellow underneath, strobes at
    # the tip and under the base.
    lamp(col, parts, "NavPort", "nav_red", (0.0, -(DISK_RADIUS + 1.5), 20.0))
    lamp(col, parts, "NavPortAft", "nav_red", (0.0, -(282.0 + 1.5), 46.0))
    lamp(col, parts, "NavStarboard", "nav_green", (0.0, DISK_RADIUS + 1.5, 20.0))
    lamp(col, parts, "NavStarboardAft", "nav_green", (0.0, 282.0 + 1.5, 46.0))
    lamp(col, parts, "NavDorsalFore", "nav_white", (24.0, 0.0, 140.0))
    lamp(col, parts, "NavDorsalAft", "nav_white", (24.0, 0.0, 168.0))
    lamp(col, parts, "NavVentral", "nav_yellow", (-(DISK_RADIUS * 0.6), 0.0, -1.5),
         radius=0.9)
    lamp(col, parts, "StrobeDorsal", "strobe", (0.0, 0.0, SPIRE_TOP + 2.0),
         radius=0.6)
    lamp(col, parts, "StrobeVentral", "strobe", (0.0, 0.0, -2.0), radius=0.6)

    return parts


def build_radiators(col):
    """
    The thermal answer, faired: five slim blades on the underside, raked flush
    against the base plate. The Illuminus do not display their plumbing, but the
    physics is not negotiable, so the blades are there and they are hot.
    """
    parts = []
    for i, (x, y, _) in enumerate(ring_of(5, 232.0, z=0.0)):
        angle = math.tau * i / 5.0
        blade = box(f"Vane{i}", (64.0, 1.2, 15.0),
                    location=(x * 1.02, y * 1.02, -8.0),
                    rotation=(0.10, 0.0, angle))
        assign(blade, "radiator")
        link(blade, col)
        parts.append(blade)

        hot = box(f"VaneHot{i}", (64.0, 0.3, 15.0),
                  location=(x * 1.02, y * 1.02, -8.7),
                  rotation=(0.10, 0.0, angle))
        assign(hot, "radiator_hot")
        link(hot, col)
        parts.append(hot)

    return parts


SHOTS = [
    ("hero", 35.0, 18.0, 1.75, 55.0),
    ("port", -90.0, 4.0, 1.75, 55.0),
]


def main():
    reset()
    col = collection("Disk")
    build_materials()

    parts = []
    parts.extend(build_disk(col))
    parts.extend(build_spire(col))
    parts.extend(build_radiators(col))

    rate = math.sqrt(9.80665 / DISK_RADIUS)
    print(f"  disk      {DISK_RADIUS * 2:.0f} m across, {SPIRE_TOP:.0f} m to the "
          f"mooring tip")
    print(f"  gravity   {rate * 60.0 / math.tau:.2f} rpm at {DISK_RADIUS:.0f} m rim"
          f" = {rate * rate * DISK_RADIUS:.2f} g")

    render_views("/tmp/station-sketches/disk.png", SHOTS, resolution=900,
                 samples=28)

    # The sell is the surface: the core glow, the plate edges, the mooring.
    render_preview("/tmp/station-sketches/disk_detail.png",
                   target=(0.0, 0.0, 138.0), distance=240.0,
                   azimuth=30.0, elevation=14.0, resolution=900, samples=28)


main()
