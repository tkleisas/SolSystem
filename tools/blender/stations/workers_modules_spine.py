"""
The Workers modules, spine pattern — a waystation, grown one can at a time.

    blender --background --python tools/blender/stations/workers_modules_spine.py

Mir and Tiangong multiplied: a line of docked cans with a node in the middle,
a ferry parked at the fore port, and a short truss with radiator wings at the
back. Grown, not designed-once: the cans are different sizes and different
ages, the handrails are where a person has to go, and the patches are where the
years went. It shares the Train freighter's clipped-module DNA because it is
the same civilisation: every can is a bolt-on with a collar at each end, so the
station reads assembled because it is.

GRAVITY, OR THE HONEST LACK OF IT. This station does not spin, and that is a
deliberate answer rather than an omission. A waystation of nine hundred tonnes
cannot carry a spin ring, a despin bearing and the reaction mass to hold
attitude, and the physiology costs less than all of it: the crew rotate through
tours of months, exercise by roster, and adapt to 0.90 g because that is the
figure Workers' crews adapt to. It is the Russian answer. Mir never spun
either, and it worked for fifteen years.

THE CONVENTION, at a waystation's scale. "Dorsal" and "ventral" are the axis
poles of the spine: the two whites ride the top of the cans and the single
yellow rides the bottom, and the strobes sit above and below the node ball. The
pairs are fore and aft so that the station's length reads at night, which is
the whole reason a 90 m waystation carries eleven lamps.

ORIENTATION. The spine runs along +z in Blender, fore at the top. Blender is
z-up and the glTF export converts to y-up, so the client reads it long along
+y. All node transforms are baked to identity before export — the client frames
assets from transformed AABB corners, and a transform left on a node inflates
the measure it frames by.
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

NAME = "workers_modules_spine"

# --------------------------------------------------------------------------- numbers

TRUSS_HALF = 1.6
WING_SPAN = 22.0
WING_CHORD = 10.0
WING_AREA = 2 * 2 * WING_SPAN * WING_CHORD   # two stations, two sides

MATERIALS = {}


def build_materials():
    """
    Painted and galvanised steel, scuffed structures, and one conspicuously newer
    panel. The mixed finishes are the point: a maintained station is not a
    uniform one.
    """
    MATERIALS["hull"] = material(
        "WorkersHull", (0.30, 0.315, 0.335), metallic=0.75, roughness=0.55)
    MATERIALS["hull_new"] = material(
        "WorkersHullNew", (0.44, 0.45, 0.47), metallic=0.8, roughness=0.42)
    MATERIALS["structure"] = material(
        "WorkersStructure", (0.19, 0.20, 0.215), metallic=0.7, roughness=0.68)
    MATERIALS["dark"] = material(
        "WorkersDark", (0.045, 0.048, 0.055), metallic=0.5, roughness=0.72)
    MATERIALS["radiator"] = material(
        "WorkersRadiator", (0.115, 0.12, 0.135), metallic=0.25, roughness=0.6)
    # Dull red, weakly emissive, for the reason the freighter's script records.
    MATERIALS["radiator_hot"] = material(
        "WorkersRadiatorHot", (0.36, 0.11, 0.055), metallic=0.2, roughness=0.64,
        emission=(0.44, 0.10, 0.04), emission_strength=0.6)
    MATERIALS["plumbing"] = material(
        "WorkersPlumbing", (0.26, 0.265, 0.28), metallic=0.9, roughness=0.45)
    MATERIALS["warning"] = material(
        "WorkersWarning", (0.55, 0.32, 0.03), metallic=0.3, roughness=0.6)
    MATERIALS["window"] = material(
        "WorkersWindow", (0.02, 0.025, 0.035), metallic=0.4, roughness=0.15)

    # The Cygnus convention, Workers register. Counts are the contract.
    MATERIALS["nav_red"] = material(
        "WorkersNavRed", (0.55, 0.02, 0.02), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.04, 0.03), emission_strength=1.0)
    MATERIALS["nav_green"] = material(
        "WorkersNavGreen", (0.02, 0.50, 0.06), metallic=0.0, roughness=0.35,
        emission=(0.05, 1.0, 0.12), emission_strength=1.0)
    MATERIALS["nav_white"] = material(
        "WorkersNavWhite", (0.70, 0.70, 0.68), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.98, 0.92), emission_strength=1.0)
    MATERIALS["nav_yellow"] = material(
        "WorkersNavYellow", (0.62, 0.50, 0.02), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.78, 0.05), emission_strength=1.0)
    MATERIALS["strobe"] = material(
        "WorkersStrobe", (0.85, 0.85, 0.85), metallic=0.0, roughness=0.30,
        emission=(1.0, 1.0, 1.0), emission_strength=1.0)


def assign(obj, key):
    obj.data.materials.clear()
    obj.data.materials.append(MATERIALS[key])
    return obj


def lamp(col, parts, name, key, location, radius=0.30):
    """A lens in a dark housing, because a bare emissive point reads as an error."""
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


def can(col, parts, name, radius, z0, z1, key, windows=6):
    """One bolt-on module: a can, two end collars, a window band, a patch."""
    body = cylinder(name, radius, z1 - z0, location=(0, 0, (z0 + z1) / 2.0),
                    vertices=40)
    assign(body, key)
    link(body, col)
    parts.append(body)

    for z in (z0 + 0.5, z1 - 0.5):
        collar = torus(f"{name}Collar{z:.0f}", radius + 0.06, 0.16,
                       location=(0, 0, z), major_segments=40, minor_segments=8)
        assign(collar, "structure")
        link(collar, col)
        parts.append(collar)

    for i in range(windows):
        angle = 0.4 + i * (math.tau / windows)
        w = cylinder(f"{name}Window{i}", 0.22, 0.10,
                     location=(radius * math.cos(angle), radius * math.sin(angle),
                               (z0 + z1) / 2.0 + 1.0),
                     rotation=(math.pi / 2, 0, angle), vertices=16)
        assign(w, "window")
        link(w, col)
        parts.append(w)

    patch = box(f"{name}Patch", (0.10, radius * 1.2, (z1 - z0) * 0.35),
                location=(radius * 0.72, 0, (z0 + z1) / 2.0 - 1.0))
    assign(patch, "hull_new" if key == "hull" else "hull")
    link(patch, col)
    parts.append(patch)

    return body


def handrail(col, parts, name, radius, z0, z1):
    """A handrail along a can, on standoff posts, where a person has to go."""
    x = radius + 0.14
    rail = cylinder(f"{name}Rail", 0.05, z1 - z0 - 1.0,
                    location=(x, 0, (z0 + z1) / 2.0), vertices=8)
    assign(rail, "plumbing")
    link(rail, col)
    parts.append(rail)

    z = z0 + 1.0
    while z < z1 - 0.5:
        post = box(f"{name}Post{z:.0f}", (0.16, 0.05, 0.05),
                   location=(radius + 0.07, 0, z))
        assign(post, "plumbing")
        link(post, col)
        parts.append(post)
        z += 2.5


def build_truss(col, parts, name, z0, z1, half, step=6.0):
    """A short box truss, the Train's DNA at waystation scale."""
    length = z1 - z0
    for x in (-half, half):
        for y in (-half, half):
            rail = box(f"{name}Longeron", (0.24, 0.24, length),
                       location=(x, y, (z0 + z1) / 2.0))
            assign(rail, "structure")
            link(rail, col)
            parts.append(rail)

    stations = int(length / step)
    for i in range(stations + 1):
        z = z0 + length * i / stations
        for y in (-half, half):
            post = box(f"{name}PostX", (2 * half, 0.2, 0.2), location=(0, y, z))
            assign(post, "structure")
            link(post, col)
            parts.append(post)
        for x in (-half, half):
            post = box(f"{name}PostY", (0.2, 2 * half, 0.2), location=(x, 0, z))
            assign(post, "structure")
            link(post, col)
            parts.append(post)

    for i in range(stations):
        za = z0 + length * i / stations
        zb = z0 + length * (i + 1) / stations
        dz = zb - za
        diag = math.hypot(2 * half, dz)
        angle = math.atan2(dz, 2 * half)
        for y in (-half, half):
            d = box(f"{name}DiagX", (diag, 0.16, 0.16),
                    location=(0, y, (za + zb) / 2.0),
                    rotation=(0, -angle * (1 if i % 2 == 0 else -1), 0))
            assign(d, "structure")
            link(d, col)
            parts.append(d)
        for x in (-half, half):
            d = box(f"{name}DiagY", (0.16, diag, 0.16),
                    location=(x, 0, (za + zb) / 2.0),
                    rotation=(angle * (1 if i % 2 == 0 else -1), 0, 0))
            assign(d, "structure")
            link(d, col)
            parts.append(d)


def build_ferry(col):
    """The parked ferry: a Soyuz ball and cone at the fore port. Its own node,
    because a ferry comes and goes and a part that moves should not be welded in."""
    parts = []

    ball = sphere("FerryBall", radius=2.8, location=(0, 0, 52.0),
                  segments=32, rings=16)
    assign(ball, "hull_new")
    link(ball, col)
    parts.append(ball)

    cone = lathe("FerryCone", [
        (2.5, 0.0),
        (2.2, 1.6),
        (1.3, 4.5),
        (1.1, 5.6),
    ], segments=40, location=(0, 0, 53.6))
    assign(cone, "hull")
    shade_smooth_by_angle(cone, math.radians(35))
    link(cone, col)
    parts.append(cone)

    for i in range(3):
        angle = 0.6 + i * 0.6
        w = cylinder(f"FerryWindow{i}", 0.20, 0.10,
                     location=(2.82 * math.cos(angle), 2.82 * math.sin(angle),
                               52.6),
                     rotation=(math.pi / 2, 0, angle), vertices=16)
        assign(w, "window")
        link(w, col)
        parts.append(w)

    return parts


def build_station(col):
    """The waystation itself, fore to aft: ferry, cans, node, cans, truss."""
    parts = []

    adapter = cylinder("ForeAdapter", 2.4, 4.0, location=(0, 0, 46.5), vertices=32)
    assign(adapter, "structure")
    link(adapter, col)
    parts.append(adapter)

    can(col, parts, "Habitat", 4.0, 30.0, 44.0, "hull", windows=8)
    can(col, parts, "Lab", 3.4, 14.0, 26.0, "hull_new", windows=5)

    # The node: an oblate collar hub, not a ball. The first sketch carried a
    # sphere and it read as a balloon caught in a drainpipe; the Mir silhouette
    # is a hub the cans pass through, not a bulge they hang off.
    node = lathe("Node", [
        (0.0, -4.0),
        (3.6, -3.4),
        (4.4, -2.0),
        (4.4, 2.0),
        (3.6, 3.4),
        (0.0, 4.0),
    ], segments=48, location=(0, 0, 8.0))
    assign(node, "hull")
    shade_smooth_by_angle(node, math.radians(34))
    link(node, col)
    parts.append(node)

    for z in (12.0, 4.0):
        collar = torus(f"NodeCollar{z:.0f}", 2.0, 0.3, location=(0, 0, z),
                       major_segments=32, minor_segments=10)
        assign(collar, "structure")
        link(collar, col)
        parts.append(collar)
    for angle in (0.0, math.pi):
        collar = torus(f"NodeRadial{angle:.0f}", 2.0, 0.3,
                       location=(4.4 * math.cos(angle), 4.4 * math.sin(angle), 8.0),
                       rotation=(0, math.pi / 2, 0),
                       major_segments=32, minor_segments=10)
        assign(collar, "structure")
        link(collar, col)
        parts.append(collar)

    can(col, parts, "Stores", 4.4, -12.0, 3.0, "hull", windows=4)

    handrail(col, parts, "HabitatRail", 4.0, 31.0, 43.0)
    handrail(col, parts, "LabRail", 3.4, 15.0, 25.0)
    handrail(col, parts, "StoresRail", 4.4, -11.0, 2.0)

    # Antennas on the node, because a waystation talks for a living.
    for i, (x, y, h) in enumerate(((3.5, 1.5, 9.0), (-3.0, 2.5, 7.0))):
        mast = cylinder(f"Antenna{i}", 0.12, h, location=(x, y, 8.0 + h / 2.0),
                        vertices=8)
        assign(mast, "plumbing")
        link(mast, col)
        parts.append(mast)

    # The aft truss and its two radiator wings, perpendicular, outrigger-mounted.
    build_truss(col, parts, "AftTruss", -30.0, -14.0, TRUSS_HALF)

    # A reaction-control block at the very back, because even a waystation
    # has to hold attitude against its own dockings.
    plate = box("RcsPlate", (4.0, 4.0, 0.8), location=(0, 0, -30.4))
    assign(plate, "structure")
    link(plate, col)
    parts.append(plate)
    for i, (x, y, _) in enumerate(ring_of(4, 1.6, z=-31.4, phase=math.pi / 4)):
        nozzle = lathe(f"Rcs{i}", [(0.3, 0.0), (0.5, 0.8)], segments=16,
                       location=(x, y, -31.4))
        assign(nozzle, "dark")
        link(nozzle, col)
        parts.append(nozzle)

    # The convention: pairs fore and aft so the length reads, TWO whites on the
    # roofline, ONE yellow, strobes above and below the node.
    lamp(col, parts, "NavPort", "nav_red", (0.0, -4.4, 37.0))
    lamp(col, parts, "NavPortAft", "nav_red", (0.0, -4.8, -4.0))
    lamp(col, parts, "NavStarboard", "nav_green", (0.0, 4.4, 37.0))
    lamp(col, parts, "NavStarboardAft", "nav_green", (0.0, 4.8, -4.0))
    lamp(col, parts, "NavDorsalFore", "nav_white", (4.4, 0.0, 37.0))
    lamp(col, parts, "NavDorsalAft", "nav_white", (4.8, 0.0, 8.0))
    lamp(col, parts, "NavVentral", "nav_yellow", (-4.4, 0.0, 20.0), radius=0.36)
    lamp(col, parts, "StrobeDorsal", "strobe", (4.8, 0.0, 8.0), radius=0.26)
    lamp(col, parts, "StrobeVentral", "strobe", (-4.8, 0.0, 8.0), radius=0.26)

    return parts


def build_wings(col):
    """The radiator wings, their own node: they are the one part a waystation
    might ever have to feather, and a part that moves should not be welded in."""
    parts = []

    for station, z in enumerate((-16.5, -25.5)):
        bar = box(f"CrossTruss{station}", (10.0, 0.5, 0.5), location=(0, 0, z))
        assign(bar, "structure")
        link(bar, col)
        parts.append(bar)

        for side in (-1, 1):
            wing = box(f"Wing{station}{side:+d}", (WING_SPAN, 0.18, WING_CHORD),
                       location=(side * (5.0 + WING_SPAN / 2.0), 0, z))
            assign(wing, "radiator")
            link(wing, col)
            parts.append(wing)

            for face in (-1, 1):
                hot = box(f"WingHot{station}{side:+d}{face:+d}",
                          (WING_SPAN * 0.55, 0.05, WING_CHORD),
                          location=(side * (5.0 + WING_SPAN * 0.72),
                                    face * 0.12, z))
                assign(hot, "radiator_hot")
                link(hot, col)
                parts.append(hot)

            for j in range(1, 3):
                seam = box(f"WingSeam{station}{side:+d}{j}",
                           (WING_SPAN, 0.3, 0.3),
                           location=(side * (5.0 + WING_SPAN / 2.0), 0,
                                     z - WING_CHORD / 2.0 + j * WING_CHORD / 3.0))
                assign(seam, "structure")
                link(seam, col)
                parts.append(seam)

    return parts


# --------------------------------------------------------------------------- views
SHOTS = [
    ("hero",   40.0, 14.0, 2.2, 55.0),
    ("port",  -90.0,  5.0, 2.2, 55.0),
    ("bow",   -52.0,  20.0, 2.2, 55.0),
    ("stern", 150.0, -12.0, 2.2, 55.0),
    ("side",   90.0,   2.0, 2.2, 55.0),
    ("above",  55.0,  50.0, 2.2, 55.0),
]


def main():
    reset()
    col = collection("WorkersModulesSpine")
    build_materials()

    station = build_station(col)
    wings = build_wings(col)
    ferry = build_ferry(col)

    hull = join("WorkersModulesSpine_Hull", station)
    panels = join("WorkersModulesSpine_Radiators", wings)
    ship = join("WorkersModulesSpine_Ferry", ferry)

    # Bake every node transform into the vertices, so the exported nodes are
    # all identity -- the client frames assets from transformed AABB corners.
    for obj in (hull, panels, ship):
        apply_transform(obj, location=True, rotation=True, scale=True)

    blend, glb, preview = asset_paths("stations", NAME)

    print(f"  spine     90 m overall, 6 modules + ferry, no spin -- tours rotate")
    print(f"  radiator  {WING_AREA:,.0f} m2 in 4 panels: house load plus ferry "
          f"charging, with margin")

    render_views(preview, SHOTS, resolution=1100, samples=64)
    export_glb(glb, NAME)
    export_blend(blend)


main()
