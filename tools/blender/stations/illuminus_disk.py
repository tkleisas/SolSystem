"""
The Illuminus disk — a residence, not a wheel.

    blender --background --python tools/blender/stations/illuminus_disk.py

Not a wheel and never was one. A wheel is a hoop you inhabit; a disk is a place
that owns the sky it sits in. Three stacked plates, a crown dome, and a mooring
spire that is taller than the disk is thick, because the silhouette of an
Illuminus station is the same statement as the silhouette of an Illuminus ship:
inherited property, displayed. DESIGN.md §6.5 — smooth, geometric, gleaming,
intimidating. There are no trusses and there is no clutter. The intimidation is
scale and gleam, and nothing else is permitted.

THE SPIN ARITHMETIC, because it is why a disk this size is allowed to exist:

    rim radius   300 m
    one gravity  w = sqrt(9.80665 / 300) = 0.181 rad/s = 1.73 rpm

Twice Meridian's stately 0.95 and still inside the two-to-four-rpm comfort band
every crew study puts on permanent residence. A smaller station must spin faster
for the same gravity, and the crew notice — Coriolis is a fact of dinner — but
1.73 is adaptation, not endurance. The Illuminus pay it, because a residence
does not ask its owners to adapt to half a gravity.

THE THERMAL ARITHMETIC, because the Illuminus do not get to skip it either:

    residence    ~20 000 people plus port load
    reactor      ~1 GW thermal at cruise
    radiation    A = Q / (2 sigma T^4), T = 1 500 K, two-sided -> 1 742 m2 minimum

The blades give 2 400 m2, and the margin over the minimum is the port's: a
station that moors torch ships sells coolant as well as dock space. Ten blades,
flush on the underside — the physics is honoured and the plumbing is still not
displayed.

WHAT THE DIMENSIONS TRACE TO

  disk           600 m across, 132 m to the crown
  spire          96..296 m: mooring deck at 284 m, glow collars at 138 and 280 m
  radiators      10 blades, 2 400 m2, underside, fixed
  port           mooring ring at the deck rim, ships tie along the axis

ORIENTATION. The disk lies in the xy plane and spins about +z. Blender is z-up
and the glTF export converts to y-up, so the client reads the station's axis
along +y. "Dorsal" and "ventral" on a station are the spin-axis POLES: the two
white lamps ride the spire and the single yellow is under the base plate, and
that reading is noted here because on a disk the poles are the only fore and
aft that means anything. All node transforms are baked to identity before
export — the client frames assets from transformed AABB corners, and a
transform left on a node inflates the measure it frames by.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, asset_paths, box, collection, cylinder, export_blend,
    export_glb, join, lathe, link, material, render_views, reset, ring_of,
    shade_smooth_by_angle, sphere, torus,
)

NAME = "illuminus_disk"

# --------------------------------------------------------------------------- numbers

DISK_RADIUS = 300.0            # the rim: 600 m across
SPIRE_TOP = 296.0              # the tip of the mooring mast
DECK_Z = 284.0                 # the mooring deck
DECK_RADIUS = 40.0

GLOW_INNER_Z = 138.0           # the core collar, on the shaft
GLOW_DECK_Z = 280.0            # the deck's under-light

RADIATOR_BLADES = 10
BLADE_LENGTH = 60.0
BLADE_WIDTH = 40.0
RADIATOR_AREA = RADIATOR_BLADES * BLADE_LENGTH * BLADE_WIDTH

MATERIALS = {}


def build_materials():
    # Mirror steel and almost nothing else, with the one indulgence a displayed
    # residence is allowed: the core glows, and it glows so the hero view knows.
    MATERIALS["steel"] = material(
        "IlluminusHull", (0.85, 0.87, 0.90), metallic=0.95, roughness=0.10)
    MATERIALS["steel_worn"] = material(
        "IlluminusHullWorn", (0.62, 0.635, 0.67), metallic=0.6, roughness=0.40)
    MATERIALS["dark"] = material(
        "IlluminusTrim", (0.02, 0.021, 0.024), metallic=0.85, roughness=0.22)
    MATERIALS["radiator"] = material(
        "IlluminusRadiator", (0.20, 0.21, 0.23), metallic=0.25, roughness=0.55)
    # A radiator at 1 500 K glows dull red, and the emission is deliberately
    # weak -- a strong one reads as a lamp bolted to the hull.
    MATERIALS["radiator_hot"] = material(
        "IlluminusRadiatorHot", (0.34, 0.10, 0.05), metallic=0.2, roughness=0.66,
        emission=(0.40, 0.085, 0.03), emission_strength=0.55)
    MATERIALS["glow"] = material(
        "IlluminusCoreGlow", (0.55, 0.40, 0.20), metallic=0.1, roughness=0.4,
        emission=(1.0, 0.72, 0.38), emission_strength=3.2)
    MATERIALS["window"] = material(
        "IlluminusWindow", (0.30, 0.24, 0.12), metallic=0.0, roughness=0.3,
        emission=(1.0, 0.85, 0.55), emission_strength=1.2)

    # The Cygnus convention. The names are the contract, and the client flashes
    # everything named Nav or Strobe by schedule rather than by faction.
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
    """A lens in a dark housing -- a bare emissive point reads as an error."""
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


# --------------------------------------------------------------------------- the disk


def build_disk(col):
    """
    The chess piece made monument: three stacked plates, a crown dome.

    The plates step down in radius as they rise, so the silhouette reads as a
    wedding cake and not a can. Every plate's edge carries one thin dark band --
    the only trim on the station -- and three small clusters of lit windows,
    because a displayed residence shows its lights in arrangements, not arrays.
    The dome's shoulder carries a second tier of clusters: presence is scale
    first, but scale needs somewhere for the eye to measure against, and a
    window is the only ruler a station has.
    """
    parts = []

    profile = [
        (0.0, 0.0), (270.0, 0.0), (DISK_RADIUS, 3.0), (DISK_RADIUS, 15.0),
        (282.0, 20.0),
        (282.0, 46.0), (236.0, 48.0),
        (236.0, 72.0), (186.0, 74.0),
        (186.0, 96.0), (92.0, 98.0),
    ]
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

    for band_i, (r, z) in enumerate(((DISK_RADIUS, 17.5), (282.0, 47.0),
                                     (236.0, 73.0), (186.0, 97.0))):
        band = torus(f"EdgeBand{band_i}", major=r, minor=0.9,
                     location=(0, 0, z), major_segments=128, minor_segments=8)
        assign(band, "dark")
        link(band, col)
        parts.append(band)

    # The windows: three clusters per plate edge, never a full ring, and a
    # second tier on the dome's shoulder.
    placements = [(DISK_RADIUS - 1.0, 9.0, 6), (281.0, 33.0, 6),
                  (235.0, 60.0, 6), (185.0, 85.0, 6), (80.0, 107.0, 5)]
    for plate_i, (r, z, count) in enumerate(placements):
        for cluster in range(3):
            centre = cluster * math.tau / 3.0 + plate_i * 0.45
            for w_i in range(count):
                angle = centre + (w_i - (count - 1) / 2.0) * 0.016
                w = box(f"Window{plate_i}_{cluster}_{w_i}", (6.0, 1.0, 2.4),
                        location=(math.cos(angle) * r, math.sin(angle) * r, z),
                        rotation=(0, 0, angle))
                assign(w, "window")
                link(w, col)
                parts.append(w)

    return parts


# --------------------------------------------------------------------------- the spire


def build_spire(col):
    """
    The mooring spire: taller than the disk is thick, with the glowing core at
    mid-shaft and the mooring deck at the top.

    Ships tie along the axis, the way a ship ties up at a pylon -- the only way
    to dock at a disk and keep the disk clean. The deck is a disc forty metres
    across with the mooring ring on its rim and guide strobes beneath it; the
    core collar sits far enough down the shaft that the whole station reads
    through it, which is the difference between a light and a statement.
    """
    parts = []

    spire = lathe("Spire", [
        (30.0, 96.0),
        (24.0, 118.0),
        (24.0, 258.0),
        (30.0, 270.0),
        (30.0, DECK_Z),
    ], segments=64)
    assign(spire, "steel")
    shade_smooth_by_angle(spire, math.radians(34))
    link(spire, col)
    parts.append(spire)

    # The core: the one light the station calls its own, sized to read from the
    # hero view rather than only from a detail crop.
    core = torus("Core", major=26.0, minor=4.5, location=(0, 0, GLOW_INNER_Z),
                 major_segments=96, minor_segments=20)
    assign(core, "glow")
    link(core, col)
    parts.append(core)

    # The mooring deck and its under-light.
    deck = cylinder("MooringDeck", DECK_RADIUS, 6.0,
                    location=(0, 0, DECK_Z), vertices=64)
    assign(deck, "steel")
    link(deck, col)
    parts.append(deck)

    deck_glow = torus("DeckGlow", major=32.0, minor=2.0,
                      location=(0, 0, GLOW_DECK_Z),
                      major_segments=96, minor_segments=12)
    assign(deck_glow, "glow")
    link(deck_glow, col)
    parts.append(deck_glow)

    ring = torus("MooringRing", major=DECK_RADIUS - 2.0, minor=2.2,
                 location=(0, 0, DECK_Z + 3.0),
                 major_segments=96, minor_segments=12)
    assign(ring, "dark")
    link(ring, col)
    parts.append(ring)

    mast = cylinder("MooringMast", 3.0, SPIRE_TOP - DECK_Z - 3.0,
                    location=(0, 0, (SPIRE_TOP + DECK_Z + 3.0) / 2.0),
                    vertices=16)
    assign(mast, "steel")
    link(mast, col)
    parts.append(mast)

    for i, (x, y, z) in enumerate(ring_of(4, DECK_RADIUS + 3.0,
                                          z=GLOW_DECK_Z - 2.0,
                                          phase=math.pi / 4)):
        lamp(col, parts, f"DeckGuide{i}", "strobe", (x, y, z), radius=0.9)

    # The convention, read at the poles: reds to port, greens to starboard, TWO
    # whites on the spire, ONE yellow under the base, strobes at the tip and
    # under the base. On a station the spin axis is the only fore and aft that
    # means anything, so dorsal is the spire and ventral is the keel.
    lamp(col, parts, "NavPort", "nav_red", (0.0, -(DISK_RADIUS + 1.5), 20.0))
    lamp(col, parts, "NavPortAft", "nav_red", (0.0, -(282.0 + 1.5), 46.0))
    lamp(col, parts, "NavStarboard", "nav_green",
         (0.0, DISK_RADIUS + 1.5, 20.0))
    lamp(col, parts, "NavStarboardAft", "nav_green", (0.0, 282.0 + 1.5, 46.0))
    lamp(col, parts, "NavDorsalFore", "nav_white", (26.0, 0.0, 160.0))
    lamp(col, parts, "NavDorsalAft", "nav_white", (26.0, 0.0, 200.0))
    lamp(col, parts, "NavVentral", "nav_yellow",
         (-(DISK_RADIUS * 0.6), 0.0, -1.5), radius=0.9)
    lamp(col, parts, "StrobeDorsal", "strobe", (0.0, 0.0, SPIRE_TOP + 1.0),
         radius=0.7)
    lamp(col, parts, "StrobeVentral", "strobe", (0.0, 0.0, -2.0), radius=0.7)

    return parts


# --------------------------------------------------------------------------- the blades


def build_radiators(col):
    """
    The thermal answer, faired: ten slim blades on the underside in two rings,
    raked flush against the base plate. Fixed -- a residence does not unfold,
    it simply gleams, and the blades are close enough to the plate to read as
    part of the moulding from any distance a guest approaches from.
    """
    parts = []
    for ring_i, (radius, phase) in enumerate(((200.0, 0.0), (245.0, 0.63))):
        for i, (x, y, _) in enumerate(ring_of(5, radius, z=0.0, phase=phase)):
            angle = phase + math.tau * i / 5.0
            blade = box(f"Vane{ring_i}_{i}", (BLADE_LENGTH, 1.2, BLADE_WIDTH),
                        location=(x, y, -9.0),
                        rotation=(0.08, 0.0, angle))
            assign(blade, "radiator")
            link(blade, col)
            parts.append(blade)

            for face in (-1, 1):
                hot = box(f"VaneHot{ring_i}_{i}_{face:+d}",
                          (BLADE_LENGTH * 0.92, 0.3, BLADE_WIDTH * 0.92),
                          location=(x, y, -9.0 + face * 0.78),
                          rotation=(0.08, 0.0, angle))
                assign(hot, "radiator_hot")
                link(hot, col)
                parts.append(hot)

    return parts


# --------------------------------------------------------------------------- views
SHOTS = [
    ("hero",   35.0, 18.0, 1.75, 55.0),
    ("port",  -90.0,  4.0, 1.75, 55.0),
    ("bow",   -52.0, 26.0, 1.75, 55.0),
    ("stern", 150.0, 12.0, 1.75, 55.0),
    ("side",   90.0,  10.0, 1.75, 55.0),
    ("above",  55.0,  55.0, 1.75, 55.0),
]


def main():
    reset()
    col = collection("IlluminusDisk")
    build_materials()

    disk = build_disk(col)
    spire = build_spire(col)
    vanes = build_radiators(col)

    # The vanes stay their own node: they are the one part a residence might
    # ever swing, and a part that moves should not be welded in.
    body = join("IlluminusDisk_Hull", disk + spire)
    panels = join("IlluminusDisk_Radiators", vanes)

    # Bake every node transform into the vertices, so the exported nodes are
    # all identity. The client frames a hull from the TRANSFORMED CORNERS of
    # each part's axis-aligned box, and a transform left on a node inflates the
    # measure it frames by.
    for obj in (body, panels):
        apply_transform(obj, location=True, rotation=True, scale=True)

    blend, glb, preview = asset_paths("stations", NAME)

    # What the design asks for, beside what was built.
    rate = math.sqrt(9.80665 / DISK_RADIUS)
    rpm = rate * 60.0 / math.tau
    sigma = 5.670374419e-8
    needed = 1.0e9 / (2.0 * sigma * 1500.0 ** 4)

    print(f"  disk      {DISK_RADIUS * 2:.0f} m across, {SPIRE_TOP:.0f} m to the "
          f"mooring mast")
    print(f"  gravity   {rpm:.2f} rpm at {DISK_RADIUS:.0f} m rim = 1.00 g")
    print(f"  radiator  {RADIATOR_AREA:,.0f} m2 built against {needed:,.0f} m2 "
          f"needed at 1 GW thermal")

    render_views(preview, SHOTS, resolution=1100, samples=64)
    export_glb(glb, NAME)
    export_blend(blend)


main()
