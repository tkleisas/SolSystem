"""
The Workers modules, radial pattern — a waystation grown around a well.

    blender --background --python tools/blender/stations/workers_modules_radial.py

The Mir answer to the same brief: one fat node with six ports, and the station
grows around it like a town around a well. Four radial cans on the equator, a
fore can with the ferry parked at its port, an aft can with the radiators, and
the clutter — handrails, antennas, patch plates, a warning stripe — sitting on
top of that order and never in front of it. Same civilisation as the Train and
the spine: every can is a bolt-on with a collar at each end, so the station
reads assembled because it is.

GRAVITY, OR THE HONEST LACK OF IT. The station does not spin, and the reason is
mass, not preference: a spin ring, a despin bearing and the reaction mass to
hold attitude all cost more than the physiology does. The crew rotate through
tours of months, exercise by roster, and adapt to 0.90 g because that is the
figure Workers' crews adapt to. It is the Russian answer, and Mir is the proof
it works — fifteen years, no spin ring, no regrets the logs record.

THE CONVENTION. On a cross-shaped station there is still a long axis, and it is
the one the ferry docks on: "dorsal" is the node's +x shoulder and the fore
can's roofline, "ventral" is the −x underside, and the pairs ride the fore and
aft cans so the length reads at night. Interpretation recorded here because a
radial station is where "fore and aft" starts to mean "whichever way the ferry
came in".

ORIENTATION. The long axis runs along +z in Blender, fore at the top. Blender
is z-up and the glTF export converts to y-up, so the client reads it long along
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

NAME = "workers_modules_radial"

WING_SPAN = 20.0
WING_CHORD = 9.0
WING_AREA = 2 * WING_SPAN * WING_CHORD

MATERIALS = {}


def build_materials():
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
    MATERIALS["radiator_hot"] = material(
        "WorkersRadiatorHot", (0.36, 0.11, 0.055), metallic=0.2, roughness=0.64,
        emission=(0.44, 0.10, 0.04), emission_strength=0.6)
    MATERIALS["plumbing"] = material(
        "WorkersPlumbing", (0.26, 0.265, 0.28), metallic=0.9, roughness=0.45)
    MATERIALS["warning"] = material(
        "WorkersWarning", (0.55, 0.32, 0.03), metallic=0.3, roughness=0.6)
    MATERIALS["window"] = material(
        "WorkersWindow", (0.02, 0.025, 0.035), metallic=0.4, roughness=0.15)

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


def can(col, parts, name, radius, location, rotation, length, key, windows=5):
    """One bolt-on module, on whatever axis the port faces."""
    body = cylinder(name, radius, length, location=location, rotation=rotation,
                    vertices=40)
    assign(body, key)
    link(body, col)
    parts.append(body)

    for end in (-1, 1):
        if rotation[1] != 0.0:
            collar_loc = (location[0] + end * (length / 2.0 - 0.5),
                          location[1], location[2])
            collar_rot = (0, math.pi / 2, 0)
        elif rotation[0] != 0.0:
            collar_loc = (location[0], location[1] + end * (length / 2.0 - 0.5),
                          location[2])
            collar_rot = (math.pi / 2, 0, 0)
        else:
            collar_loc = (location[0], location[1],
                          location[2] + end * (length / 2.0 - 0.5))
            collar_rot = (0, 0, 0)
        collar = torus(f"{name}Collar{end:+d}", radius + 0.06, 0.15,
                       location=collar_loc, rotation=collar_rot,
                       major_segments=40, minor_segments=8)
        assign(collar, "structure")
        link(collar, col)
        parts.append(collar)

    for i in range(windows):
        angle = 0.5 + i * (math.tau / windows)
        w = cylinder(f"{name}Window{i}", 0.20, 0.10,
                     location=(location[0] + radius * math.cos(angle),
                               location[1] + radius * math.sin(angle),
                               location[2] + 1.0),
                     rotation=(math.pi / 2, 0, angle), vertices=16)
        assign(w, "window")
        link(w, col)
        parts.append(w)

    patch = box(f"{name}Patch", (0.10, radius * 1.1, length * 0.35),
                location=(location[0] + radius * 0.72, location[1],
                          location[2] - 1.0))
    assign(patch, "hull_new" if key == "hull" else "hull")
    link(patch, col)
    parts.append(patch)


def build_ferry(col):
    """The parked ferry: a Soyuz ball and cone on the fore port. Its own node,
    because a ferry comes and goes and a part that moves should not be welded in."""
    parts = []

    ball = sphere("FerryBall", radius=2.8, location=(0, 0, 22.5),
                  segments=32, rings=16)
    assign(ball, "hull_new")
    link(ball, col)
    parts.append(ball)

    cone = lathe("FerryCone", [
        (2.5, 0.0),
        (2.2, 1.6),
        (1.3, 4.5),
        (1.1, 5.6),
    ], segments=40, location=(0, 0, 24.1))
    assign(cone, "hull")
    shade_smooth_by_angle(cone, math.radians(35))
    link(cone, col)
    parts.append(cone)

    return parts


def build_node(col, parts):
    """The well the town grows around: a fat node with six ports and a rail."""
    node = lathe("Node", [
        (0.0, -6.0),
        (4.2, -5.0),
        (6.0, -3.0),
        (6.0, 3.0),
        (4.2, 5.0),
        (0.0, 6.0),
    ], segments=64, location=(0, 0, 1.0))
    assign(node, "hull")
    shade_smooth_by_angle(node, math.radians(34))
    link(node, col)
    parts.append(node)

    for z in (7.0, -5.0):
        collar = torus(f"NodeCollar{z:.0f}", 2.2, 0.3, location=(0, 0, z),
                       major_segments=32, minor_segments=10)
        assign(collar, "structure")
        link(collar, col)
        parts.append(collar)

    for i in range(4):
        angle = i * math.tau / 4.0 + math.pi / 4.0
        collar = torus(f"NodeRadial{i}", 2.2, 0.3,
                       location=(6.0 * math.cos(angle), 6.0 * math.sin(angle), 1.0),
                       rotation=(0, math.pi / 2, angle),
                       major_segments=32, minor_segments=10)
        assign(collar, "structure")
        link(collar, col)
        parts.append(collar)

    # A handrail ring round the node's waist, because everything starts here.
    rail = torus("NodeRail", 6.5, 0.06, location=(0, 0, 1.0),
                 major_segments=64, minor_segments=6)
    assign(rail, "plumbing")
    link(rail, col)
    parts.append(rail)
    for i in range(12):
        angle = i * math.tau / 12.0
        post = box(f"NodeRailPost{i}", (0.05, 0.05, 0.5),
                   location=(6.28 * math.cos(angle), 6.28 * math.sin(angle), 1.0))
        assign(post, "plumbing")
        link(post, col)
        parts.append(post)

    # The antenna farm: a waystation talks for a living.
    for i, (x, y, h) in enumerate(((3.0, 2.0, 9.0), (-3.4, 1.0, 7.5),
                                   (0.5, -3.6, 8.0))):
        mast = cylinder(f"Antenna{i}", 0.12, h,
                        location=(x, y, 4.0 + h / 2.0), vertices=8)
        assign(mast, "plumbing")
        link(mast, col)
        parts.append(mast)


def build_wings(col):
    """Two radiator wings on outriggers off the aft can. Their own node, because
    they are the one part a waystation might ever have to feather."""
    parts = []

    for side in (-1, 1):
        spar = box(f"Outrigger{side:+d}", (0.5, 4.0, 0.5),
                   location=(0, side * 3.8, -16.0))
        assign(spar, "structure")
        link(spar, col)
        parts.append(spar)

        wing = box(f"Wing{side:+d}", (0.18, WING_SPAN, WING_CHORD),
                   location=(0, side * (5.8 + WING_SPAN / 2.0), -16.0))
        assign(wing, "radiator")
        link(wing, col)
        parts.append(wing)

        for face in (-1, 1):
            hot = box(f"WingHot{side:+d}{face:+d}",
                      (0.05, WING_SPAN * 0.55, WING_CHORD),
                      location=(face * 0.12, side * (5.8 + WING_SPAN * 0.72),
                                -16.0))
            assign(hot, "radiator_hot")
            link(hot, col)
            parts.append(hot)

        for j in range(1, 3):
            seam = box(f"WingSeam{side:+d}{j}", (0.3, WING_SPAN, 0.3),
                       location=(0, side * (5.8 + WING_SPAN / 2.0),
                                 -16.0 - WING_CHORD / 2.0 + j * WING_CHORD / 3.0))
            assign(seam, "structure")
            link(seam, col)
            parts.append(seam)

    return parts


def build_station(col):
    parts = []

    build_node(col, parts)

    can(col, parts, "ForeCan", 3.4, (0, 0, 14.0), (0, 0, 0), 12.0, "hull_new",
        windows=5)
    can(col, parts, "AftCan", 4.0, (0, 0, -12.0), (0, 0, 0), 12.0, "hull",
        windows=4)

    # The radial four, at the intercardinals: different sizes, different ages.
    can(col, parts, "RadialA", 3.2, (11.0, 0, 1.0), (0, math.pi / 2, 0), 10.0,
        "hull", windows=4)
    can(col, parts, "RadialB", 3.6, (-11.5, 0, 1.0), (0, math.pi / 2, 0), 11.0,
        "hull_new", windows=5)
    can(col, parts, "RadialC", 3.2, (0, 11.0, 1.0), (math.pi / 2, 0, 0), 10.0,
        "hull_new", windows=4)
    can(col, parts, "RadialD", 3.4, (0, -11.0, 1.0), (math.pi / 2, 0, 0), 10.0,
        "hull", windows=3)

    # A warning stripe on the stores can, because some cans are not larders.
    stripe = cylinder("RadialAStripe", 3.28, 1.2,
                      location=(11.0, 0, 1.0), rotation=(0, math.pi / 2, 0),
                      vertices=40)
    assign(stripe, "warning")
    link(stripe, col)
    parts.append(stripe)

    # The convention: pairs so the length reads, TWO whites on the roofline,
    # ONE yellow, strobes on the node top and bottom.
    lamp(col, parts, "NavPort", "nav_red", (0.0, -4.2, 14.0))
    lamp(col, parts, "NavPortAft", "nav_red", (0.0, -4.4, -12.0))
    lamp(col, parts, "NavStarboard", "nav_green", (0.0, 4.2, 14.0))
    lamp(col, parts, "NavStarboardAft", "nav_green", (0.0, 4.4, -12.0))
    lamp(col, parts, "NavDorsalFore", "nav_white", (3.8, 0.0, 14.0))
    lamp(col, parts, "NavDorsalAft", "nav_white", (6.4, 0.0, 1.0))
    lamp(col, parts, "NavVentral", "nav_yellow", (-4.4, 0.0, -6.0), radius=0.36)
    lamp(col, parts, "StrobeDorsal", "strobe", (0.0, 0.0, 7.6), radius=0.26)
    lamp(col, parts, "StrobeVentral", "strobe", (0.0, 0.0, -5.6), radius=0.26)

    return parts


# --------------------------------------------------------------------------- views
SHOTS = [
    ("hero",   40.0, 16.0, 2.4, 55.0),
    ("port",  -90.0,  6.0, 2.4, 55.0),
    ("bow",   -52.0,  22.0, 2.4, 55.0),
    ("stern", 150.0, -12.0, 2.4, 55.0),
    ("side",   90.0,   2.0, 2.4, 55.0),
    ("above",  55.0,  50.0, 2.4, 55.0),
]


def main():
    reset()
    col = collection("WorkersModulesRadial")
    build_materials()

    station = build_station(col)
    wings = build_wings(col)
    ferry = build_ferry(col)

    hull = join("WorkersModulesRadial_Hull", station)
    panels = join("WorkersModulesRadial_Radiators", wings)
    ship = join("WorkersModulesRadial_Ferry", ferry)

    # Bake every node transform into the vertices, so the exported nodes are
    # all identity -- the client frames assets from transformed AABB corners.
    for obj in (hull, panels, ship):
        apply_transform(obj, location=True, rotation=True, scale=True)

    blend, glb, preview = asset_paths("stations", NAME)

    print(f"  radial    46 m cluster, 6-port node + 6 cans + ferry, no spin")
    print(f"  radiator  {WING_AREA:,.0f} m2 in 2 wings: house load plus ferry "
          f"charging, with margin")

    render_views(preview, SHOTS, resolution=1100, samples=64)
    export_glb(glb, NAME)
    export_blend(blend)


main()
