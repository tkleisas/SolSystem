"""
Sketch B -- "The Tanker". A Workers freighter direction.

The tanks ARE the hull: three fat propellant cylinders abreast, no truss, the
engine on a stern frame, a blunt castle at the bow, and the pipe runs along the
whole length with a flange every few metres, outside where a wrench reaches. Sea
-tanker bluntness: the ship is a deck of cylinders and everything else is fittings.

A note on proportions, because it is a consequence of the physics rather than a
styling decision: the production script's tanks are 6 m by 42 m, and three of them
abreast are 42 m long. The ship is therefore SHORT and the radiator wings are
proportionally enormous -- 13 440 m2 of panel on a 74 m hull is what a fusion
tanker actually looks like. The length class moves to the beam.

RADIATOR: fixed. Two wings, each two panels on a shared outrigger frame, splayed
30 degrees so every panel sees sky. Nothing on this ship folds.

    blender --background --python tools/blender/ships/sketches/freighter_b.py
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

TANK_RADIUS = 6.0              # the production gauge
TANK_LENGTH = 42.0
TANK_Y = (-12.4, 0.0, 12.4)    # three abreast, 0.4 m between barrels

WET_MASS_T = 8_900.0
EXHAUST_VELOCITY = 1_200_000.0
CRUISE_MILLIGEE = 0.26
ACCELERATION = CRUISE_MILLIGEE / 1000.0 * 9.80665
RADIATOR_TEMPERATURE = 1500.0
RADIATOR_EFFICIENCY = 0.65
RADIATOR_AREAL_DENSITY = 8.0

# Four panels of 40 x 42 in two wings of two: 13 440 m2 against 12 770 needed.
RADIATOR_PANELS = 4
PANEL_LENGTH = 40.0
PANEL_WIDTH = 42.0
PANEL_SPLAY = 30.0
PANEL_THICKNESS = 0.22
PANEL_STANDOFF = 4.5

NOZZLES = 4

# The layout, engine plane to bow.
FRAME_BOTTOM = 4.0
TANKS_BOTTOM = 14.0
TANKS_TOP = TANKS_BOTTOM + TANK_LENGTH
CASTLE_BOTTOM = TANKS_TOP
CASTLE_TOP = CASTLE_BOTTOM + 14.0
DOCK_TOP = CASTLE_TOP + 4.0

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
    obj.data.materials.append(MATERIALS[key])
    return obj


def lamp(col, name, key, location, radius=0.36):
    housing = sphere(f"{name}_Housing", radius=radius * 1.6, location=location,
                     segments=16, rings=8)
    assign(housing, "dark")
    link(housing, col)
    lens = sphere(name, radius=radius, location=location, segments=16, rings=8)
    assign(lens, key)
    link(lens, col)


def tank_profile(radius, length, dome=2.2):
    profile = [(0.0, 0.0)]
    for j in range(9):
        t = j / 8.0
        profile.append((radius * math.sin(t * math.pi / 2.0), t * dome))
    profile.append((radius, dome))
    profile.append((radius, length - dome))
    for j in range(9):
        t = j / 8.0
        profile.append((radius * math.cos(t * math.pi / 2.0),
                        length - dome + t * dome))
    return profile


def build_tanks(col):
    """Three barrels abreast, flanged at both ends. The whole hull."""
    for i, y in enumerate(TANK_Y):
        tank = lathe(f"Tank{i}", tank_profile(TANK_RADIUS, TANK_LENGTH),
                     segments=64, location=(0, y, TANKS_BOTTOM))
        assign(tank, "hull")
        shade_smooth_by_angle(tank, math.radians(35))
        link(tank, col)

        for zz in (TANKS_BOTTOM + 1.4, TANKS_TOP - 1.4):
            flange = torus(f"Flange{i}{zz:.0f}", TANK_RADIUS + 0.06, 0.16,
                           location=(0, y, zz), major_segments=64, minor_segments=10)
            assign(flange, "structure")
            link(flange, col)

    # Saddle bands tying the three barrels into one structure, three stations.
    for z in (TANKS_BOTTOM + 8.0, (TANKS_BOTTOM + TANKS_TOP) / 2.0, TANKS_TOP - 8.0):
        band = box("Saddle", (1.2, 2 * (TANK_Y[2] + TANK_RADIUS) + 1.0, 2.2),
                   location=(0, 0, z))
        assign(band, "structure")
        link(band, col)


def build_engine_frame(col):
    """The stern frame: a thrust plate, four posts, four nozzles. No skirt, no fairing."""
    plate = box("ThrustPlate", (2 * (TANK_Y[2] + TANK_RADIUS) + 2.0, 24.0, 1.6),
                location=(0, 0, FRAME_BOTTOM + 8.0))
    assign(plate, "structure")
    link(plate, col)

    for x in (-8.0, 8.0):
        for y in (-8.0, 8.0):
            post = box("FramePost", (0.5, 0.5, 10.0),
                       location=(x, y, FRAME_BOTTOM + 3.0))
            assign(post, "structure")
            link(post, col)

    for i, (x, y, _) in enumerate(ring_of(NOZZLES, 3.4, z=0.0, phase=math.pi / 4)):
        nozzle = lathe(f"Nozzle{i}", [
            (0.55, 0.0),
            (0.62, 0.5),
            (1.05, 2.4),
            (1.25, 3.6),
        ], segments=36, location=(x, y, FRAME_BOTTOM + 4.0))
        assign(nozzle, "dark")
        shade_smooth_by_angle(nozzle, math.radians(40))
        link(nozzle, col)


def build_castle(col):
    """The bow superstructure: a blunt block with a bridge band and the dock on top."""
    body = box("Castle", (16.0, 22.0, CASTLE_TOP - CASTLE_BOTTOM),
               location=(0, 0, (CASTLE_BOTTOM + CASTLE_TOP) / 2.0))
    assign(body, "hull_new")
    link(body, col)

    # A step back at the front face, so the bow reads as built, not extruded.
    step = box("CastleStep", (12.0, 16.0, 5.0), location=(2.0, 0, CASTLE_TOP + 2.5))
    assign(step, "hull")
    link(step, col)

    # The bridge: one band of windows across the front face.
    for i in range(7):
        w = box(f"Bridge{i}", (0.10, 1.4, 0.9),
                location=(8.05, -6.0 + i * 2.0, CASTLE_TOP - 3.0))
        assign(w, "window")
        link(w, col)

    collar = torus("DockingCollar", 1.9, 0.35,
                   location=(0, 0, CASTLE_TOP + 5.4),
                   major_segments=48, minor_segments=12)
    assign(collar, "structure")
    link(collar, col)

    hatch = cylinder("Hatch", 1.5, 0.5, location=(0, 0, CASTLE_TOP + 5.8),
                     vertices=32)
    assign(hatch, "dark")
    link(hatch, col)


def build_plumbing(col):
    """
    Pipe runs the full length of the barrels, with a flange every eight metres.
    The Tanker's texture: nothing inside that can go outside.
    """
    runs = [
        (TANK_RADIUS + 0.5, -6.2),
        (TANK_RADIUS + 0.5, 0.0),
        (TANK_RADIUS + 0.5, 6.2),
        (-(TANK_RADIUS + 0.5), -3.1),
        (-(TANK_RADIUS + 0.5), 3.1),
    ]
    length = TANKS_TOP - TANKS_BOTTOM - 6.0
    for i, (x, y) in enumerate(runs):
        pipe = cylinder(f"Run{i}", 0.28, length,
                        location=(x, y, (TANKS_BOTTOM + TANKS_TOP) / 2.0),
                        vertices=12)
        assign(pipe, "plumbing")
        link(pipe, col)

        z = TANKS_BOTTOM + 5.0
        j = 0
        while z < TANKS_TOP - 4.0:
            flange = torus(f"RunFlange{i}{j}", 0.40, 0.10,
                           location=(x, y, z), major_segments=20, minor_segments=8)
            assign(flange, "structure")
            link(flange, col)
            z += 8.0
            j += 1

    # Crossovers between the barrels at the mid saddle, with valve wheels as boxes.
    for y in (-6.2, 6.2):
        cross = cylinder("Crossover", 0.24, 13.0, location=(0, y, 35.0),
                         rotation=(math.pi / 2, 0, 0), vertices=12)
        assign(cross, "plumbing")
        link(cross, col)
        valve = box(f"Valve{y:+.0f}", (0.8, 0.8, 0.8), location=(0, y, 35.9))
        assign(valve, "warning")
        link(valve, col)


def build_radiators(col):
    """
    Two wings, one per side of the flat bundle, each two panels of 40 x 42 on a
    shared outrigger frame, splayed 30 degrees. Fixed -- nothing on this ship folds.

    Mounted on the bundle's x faces, width along x, for a reason that took a wrong
    first version to find: with the wings on the y sides, the beam view looked
    straight INTO forty metres of panel and the hull behind it vanished. On the x
    faces the wings flank the silhouette and the deck of barrels still reads
    between them -- the same lesson the production layout already teaches.
    """
    splay = math.radians(PANEL_SPLAY)
    placements = [TANKS_BOTTOM + 3.0 + PANEL_LENGTH / 2.0,
                  TANKS_BOTTOM + 3.0 + PANEL_LENGTH * 1.5 + 2.0]

    for side in (-1, 1):
        hinge_x = (TANK_RADIUS + PANEL_STANDOFF) * side

        # The outrigger frame: three spars per station, out from the barrels.
        for z in (placements[0] - PANEL_LENGTH / 2.0 + 3.0,
                  (placements[0] + placements[1]) / 2.0,
                  placements[1] + PANEL_LENGTH / 2.0 - 3.0):
            for y in (-6.2, 0.0, 6.2):
                spar = box(f"Outrigger{side}{z:.0f}{y:+.0f}",
                           (PANEL_STANDOFF, 0.7, 0.7),
                           location=((TANK_RADIUS + PANEL_STANDOFF / 2.0) * side,
                                     y, z))
                assign(spar, "structure")
                link(spar, col)

        for i, z_centre in enumerate(placements):
            panel = box(f"Panel{side}{i}",
                        (PANEL_WIDTH, PANEL_THICKNESS, PANEL_LENGTH),
                        location=(hinge_x + side * PANEL_WIDTH / 2.0 * math.cos(splay),
                                  0.0, z_centre),
                        rotation=(splay * side, 0.0, 0.0))
            assign(panel, "radiator")
            link(panel, col)

            # The hot half of the sheet, as a thin skin PROUD of each face. The
            # first version made the hot half a second box of the same thickness
            # inside the panel volume, which is two coplanar surfaces fighting:
            # the grey face won some, the hot face won others, and the wings read
            # as undecided. A skin offset a hair off each face has no such ambiguity.
            for face in (-1, 1):
                hot = box(f"PanelHot{side}{i}{face:+d}",
                          (PANEL_WIDTH * 0.5, 0.024, PANEL_LENGTH),
                          location=(hinge_x + side * PANEL_WIDTH * 0.75 * math.cos(splay),
                                    face * (PANEL_THICKNESS / 2.0 + 0.012),
                                    z_centre),
                          rotation=(splay * side, 0.0, 0.0))
                assign(hot, "radiator_hot")
                link(hot, col)

            for j in range(4):
                z = z_centre - PANEL_LENGTH / 2.0 + PANEL_LENGTH * (j + 0.5) / 4.0
                rib = box(f"PanelRib{side}{i}{j}",
                          (PANEL_WIDTH, PANEL_THICKNESS * 3.0, 0.6),
                          location=(hinge_x + side * PANEL_WIDTH / 2.0 * math.cos(splay),
                                    0.0, z),
                          rotation=(splay * side, 0.0, 0.0))
                assign(rib, "structure")
                link(rib, col)


def build_navigation_lights(col):
    """Cygnus, counts intact."""
    lamp(col, "NavPort", "nav_red", (0.0, TANK_Y[0] - TANK_RADIUS - 0.4, 35.0))
    lamp(col, "NavPortAft", "nav_red", (0.0, TANK_Y[0] - TANK_RADIUS - 0.4, 12.0))
    lamp(col, "NavStarboard", "nav_green",
         (0.0, TANK_Y[2] + TANK_RADIUS + 0.4, 35.0))
    lamp(col, "NavStarboardAft", "nav_green",
         (0.0, TANK_Y[2] + TANK_RADIUS + 0.4, 12.0))

    # DORSAL: TWO white, one on the castle, one on the mid-tank roofline.
    lamp(col, "NavDorsalFore", "nav_white", (8.4, 0.0, CASTLE_BOTTOM + 7.0))
    lamp(col, "NavDorsalAft", "nav_white", (TANK_RADIUS + 0.4, 0.0, 35.0))

    # VENTRAL: ONE yellow.
    lamp(col, "NavVentral", "nav_yellow", (-(TANK_RADIUS + 0.4), 0.0, 35.0),
         radius=0.42)
    lamp(col, "StrobeDorsal", "strobe", (8.4, 0.0, CASTLE_TOP + 5.0), radius=0.30)
    lamp(col, "StrobeVentral", "strobe", (-(TANK_RADIUS + 0.6), 0.0, 35.0),
         radius=0.30)


SHOTS = [
    ("hero", 40.0, 16.0, 1.9, 55.0),
    ("port", -90.0, 5.0, 1.9, 55.0),
    ("stern", 150.0, -18.0, 1.9, 55.0),
]


def main():
    reset()
    col = collection("FreighterB")
    build_materials()

    build_tanks(col)
    build_engine_frame(col)
    build_castle(col)
    build_plumbing(col)
    build_radiators(col)
    build_navigation_lights(col)

    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = RADIATOR_PANELS * PANEL_LENGTH * PANEL_WIDTH

    print(f"  sketch B  {DOCK_TOP:.0f} m overall, {WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE} milligee")

    render_views("/tmp/freighter-sketches/B.png", SHOTS, resolution=900, samples=28)

    # The maintained bit: the flanged pipe runs between the barrels.
    render_preview("/tmp/freighter-sketches/B_detail.png",
                   target=(0.0, 0.0, 9.0), distance=40.0,
                   azimuth=55.0, elevation=-28.0, resolution=900, samples=28)


main()
