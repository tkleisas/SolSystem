"""
Sketch A -- "The Tramp". A Workers freighter direction.

Progress/ATV lineage: a ribbed cylindrical core IS the ship, a cluster of tanks
(two cylinders and two spheres, because a cluster is what a cluster looks like)
forward, the engine block aft on a flared skirt, radiator wings amidships on
outriggers, and cargo pods clamped along a dorsal rail in whatever order they
arrived. Reads as a workboat: one core, everything else bolted to it.

Physics envelope (law): exhaust velocity 1 200 km/s, 8 900 t wet, 4 nozzles, and
the radiator sized by the thermal budget -- the printout is the check. The tanks
are the production script's 6 m gauge. Cygnus navigation lights, Workers names.

RADIATOR: fixed, not stowed. A Worker's panels are bolted to their outriggers and
stay there; stowing gear is mass, and mass is cargo.

    blender --background --python tools/blender/ships/sketches/freighter_a.py
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
WET_MASS_T = 8_900.0
EXHAUST_VELOCITY = 1_200_000.0
CRUISE_MILLIGEE = 0.26
ACCELERATION = CRUISE_MILLIGEE / 1000.0 * 9.80665
RADIATOR_TEMPERATURE = 1500.0
RADIATOR_EFFICIENCY = 0.65
RADIATOR_AREAL_DENSITY = 8.0

# Four panels of 78 x 44 on outriggers: the production area, kept exactly.
RADIATOR_PANELS = 4
PANEL_LENGTH = 78.0
PANEL_WIDTH = 44.0
PANEL_SPLAY = 50.0
PANEL_THICKNESS = 0.22
PANEL_STANDOFF = 5.5

NOZZLES = 4

CORE_RADIUS = 4.6

# The layout, engine plane to nose.
CORE_BOTTOM = 8.0
CORE_TOP = 86.0
SPHERE_Z = (96.0, 107.0)
FORETANK_BOTTOM = 120.0
FORETANK_TOP = 162.0
HABITAT_BOTTOM = 162.0
HABITAT_TOP = 175.0

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
    # Dull red, weakly emissive, for the reason the production script records.
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


def patch(col, name, size, location, rotation=(0, 0, 0), key="hull_new"):
    """A replacement plate: a thin slab in the NEWER finish, the maintenance story."""
    p = box(name, size, location=location, rotation=rotation)
    assign(p, key)
    link(p, col)
    return p


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


# --------------------------------------------------------------------------- sections


def build_engine(col):
    """Flared skirt, block, four nozzles. The aft end of every Worker's hull."""
    skirt = lathe("Skirt", [
        (3.6, 0.0),
        (4.4, 1.2),
        (5.0, 3.2),
        (5.0, 8.0),
    ], segments=64)
    assign(skirt, "hull_new")
    shade_smooth_by_angle(skirt, math.radians(34))
    link(skirt, col)

    for i, (x, y, _) in enumerate(ring_of(NOZZLES, 2.6, z=0.0)):
        nozzle = lathe(f"Nozzle{i}", [
            (0.55, 0.0),
            (0.62, 0.5),
            (1.05, 2.4),
            (1.25, 3.6),
        ], segments=36, location=(x, y, 1.2))
        assign(nozzle, "dark")
        shade_smooth_by_angle(nozzle, math.radians(40))
        link(nozzle, col)


def build_core(col):
    """The ribbed cylinder the whole ship hangs off."""
    core = cylinder("Core", CORE_RADIUS, CORE_TOP - CORE_BOTTOM,
                    location=(0, 0, (CORE_BOTTOM + CORE_TOP) / 2.0), vertices=64)
    assign(core, "hull")
    link(core, col)

    # The ribs, every five metres: a tramp steamer's framing, worn smooth.
    z = CORE_BOTTOM + 2.0
    i = 0
    while z < CORE_TOP - 1.0:
        rib = torus(f"CoreRib{i}", CORE_RADIUS + 0.10, 0.14,
                    location=(0, 0, z), major_segments=64, minor_segments=10)
        assign(rib, "structure")
        link(rib, col)
        z += 5.0
        i += 1

    # Replacement plates, because nothing on this ship is the age of anything else.
    patch(col, "PatchA", (0.14, 3.2, 6.0), (0.0, -CORE_RADIUS - 0.02, 30.0))
    patch(col, "PatchB", (0.14, 2.4, 4.5), (CORE_RADIUS + 0.02, 0.0, 55.0),
          rotation=(0, 0, 0))
    patch(col, "PatchC", (0.14, 2.8, 5.0), (0.0, CORE_RADIUS + 0.02, 68.0),
          key="dark")


def build_fore_cluster(col):
    """
    The tanks, clustered fore: two cylinders abreast and two spheres on the axis
    behind them. A cluster, not a row -- Progress carries its propellant in balls
    because pressure vessels are cheapest round.
    """
    for i, y in enumerate((-6.2, 6.2)):
        tank = lathe(f"ForeTank{i}", tank_profile(TANK_RADIUS, 42.0),
                     segments=64, location=(0, y, FORETANK_BOTTOM))
        assign(tank, "hull")
        shade_smooth_by_angle(tank, math.radians(35))
        link(tank, col)

        for zz in (FORETANK_BOTTOM + 1.4, FORETANK_TOP - 1.4):
            flange = torus(f"ForeFlange{i}{zz:.0f}", TANK_RADIUS + 0.06, 0.16,
                           location=(0, y, zz), major_segments=64, minor_segments=10)
            assign(flange, "structure")
            link(flange, col)

    for i, z in enumerate(SPHERE_Z):
        s = sphere(f"SphereTank{i}", radius=5.0, location=(0, 0, z),
                   segments=32, rings=16)
        assign(s, "hull_new" if i == 0 else "hull")
        link(s, col)


def build_habitat(col):
    """A blunt crew cylinder and a docking collar, right at the front."""
    body = lathe("Habitat", [
        (0.0, 0.0),
        (3.2, 0.9),
        (3.4, 2.4),
        (3.4, 10.0),
        (3.2, 11.0),
        (0.0, 13.0),
    ], segments=48, location=(0, 0, HABITAT_BOTTOM))
    assign(body, "hull")
    shade_smooth_by_angle(body, math.radians(35))
    link(body, col)

    collar = torus("DockingCollar", 1.9, 0.35, location=(0, 0, HABITAT_TOP + 0.6),
                   major_segments=48, minor_segments=12)
    assign(collar, "structure")
    link(collar, col)

    for i in range(6):
        angle = -0.5 + i * 0.22
        w = cylinder(f"Window{i}", 0.30, 0.12,
                     location=(3.42 * math.cos(angle), 3.42 * math.sin(angle),
                               HABITAT_BOTTOM + 6.5),
                     rotation=(math.pi / 2, 0, angle), vertices=20)
        assign(w, "window")
        link(w, col)


def build_cargo(col):
    """
    Cargo pods clamped along a dorsal rail on the core. Uneven sizes, uneven
    finishes, one of them not cargo at all -- added as they were needed.
    """
    rail = box("CargoRail", (0.35, 0.35, CORE_TOP - CORE_BOTTOM - 4.0),
               location=(CORE_RADIUS + 0.5, 0, (CORE_BOTTOM + CORE_TOP) / 2.0))
    assign(rail, "structure")
    link(rail, col)

    sizes = [
        (3.2, 2.8, 12.0, "hull", 16.0),
        (2.4, 2.2, 7.0, "hull_new", 30.0),
        (3.8, 3.2, 15.0, "hull", 47.0),
        (2.2, 2.0, 6.0, "hull_new", 60.0),
        (3.0, 2.6, 10.0, "hull", 72.0),
    ]
    for i, (w, h, length, key, z) in enumerate(sizes):
        x = CORE_RADIUS + 1.0 + w / 2.0
        b = box(f"Pod{i}", (w, h, length), location=(x, 0, z))
        assign(b, key)
        link(b, col)

        # Two clamp bands per pod, because nothing here is welded if it can be clamped.
        for dz in (-length * 0.3, length * 0.3):
            clamp = torus(f"Clamp{i}{dz:+.0f}", 0.5, 0.12,
                          location=(CORE_RADIUS + 0.75, 0, z + dz),
                          rotation=(0, math.pi / 2, 0),
                          major_segments=24, minor_segments=8)
            assign(clamp, "structure")
            link(clamp, col)

        if i % 2 == 1:
            stripe = box(f"PodStripe{i}", (w * 1.02, h * 1.02, length * 0.14),
                         location=(x, 0, z))
            assign(stripe, "warning")
            link(stripe, col)


def build_plumbing(col):
    """Propellant lines down the core to the engine, outside where a wrench reaches."""
    for i in range(4):
        angle = 0.6 + i * 0.5
        x = (CORE_RADIUS + 0.4) * math.cos(angle)
        y = (CORE_RADIUS + 0.4) * math.sin(angle)
        pipe = cylinder(f"Pipe{i}", 0.20, CORE_TOP - CORE_BOTTOM - 8.0,
                        location=(x, y, (CORE_BOTTOM + CORE_TOP) / 2.0),
                        vertices=12)
        assign(pipe, "plumbing")
        link(pipe, col)

    for x in (-1.8, 1.8):
        feed = cylinder("Feed", 0.42, 10.0, location=(x, -CORE_RADIUS * 0.7, 4.0),
                        rotation=(0.35, 0, 0), vertices=16)
        assign(feed, "plumbing")
        link(feed, col)


def build_radiators(col):
    """
    Four panels of 78 x 44 on outriggers, splayed 35 degrees -- the production
    geometry, because it is the physics' geometry: every panel has to see sky.
    """
    splay = math.radians(PANEL_SPLAY)
    placements = [
        (CORE_BOTTOM + PANEL_LENGTH / 2.0),
        (CORE_TOP + PANEL_LENGTH / 2.0),
    ]

    for side in (-1, 1):
        for i, z_centre in enumerate(placements):
            hinge = CORE_RADIUS + PANEL_STANDOFF
            x = hinge * side
            rotation = (splay * side, 0.0, 0.0)

            panel = box(f"Panel{side}{i}",
                        (PANEL_WIDTH, PANEL_THICKNESS, PANEL_LENGTH),
                        location=(x + PANEL_WIDTH / 2.0 * side * math.cos(splay),
                                  0.0, z_centre),
                        rotation=rotation)
            assign(panel, "radiator")
            link(panel, col)

            hot = box(f"PanelHot{side}{i}",
                      (PANEL_WIDTH * 0.5, PANEL_THICKNESS, PANEL_LENGTH),
                      location=(x + PANEL_WIDTH * 0.75 * side * math.cos(splay),
                                0.0, z_centre),
                      rotation=rotation)
            assign(hot, "radiator_hot")
            link(hot, col)

            for j in range(5):
                z = z_centre - PANEL_LENGTH / 2.0 + PANEL_LENGTH * (j + 0.5) / 5.0
                rib = box(f"PanelRib{side}{i}{j}",
                          (PANEL_WIDTH, PANEL_THICKNESS * 3.0, 0.6),
                          location=(x + PANEL_WIDTH / 2.0 * side * math.cos(splay),
                                    0.0, z),
                          rotation=rotation)
                assign(rib, "structure")
                link(rib, col)

            for z in (z_centre - PANEL_LENGTH / 2.0 + 4.0,
                      z_centre + PANEL_LENGTH / 2.0 - 4.0):
                spar = box(f"Spar{side}{i}{z:.0f}",
                           (PANEL_STANDOFF, 0.7, 0.7),
                           location=((CORE_RADIUS + PANEL_STANDOFF / 2.0) * side,
                                     0.0, z))
                assign(spar, "structure")
                link(spar, col)


def build_navigation_lights(col):
    """Cygnus, counts intact: pairs fore and aft so the length reads."""
    lamp(col, "NavPort", "nav_red", (0.0, -(CORE_RADIUS + 0.4), 40.0))
    lamp(col, "NavPortAft", "nav_red", (0.0, -(TANK_RADIUS + 6.6), 141.0))
    lamp(col, "NavStarboard", "nav_green", (0.0, CORE_RADIUS + 0.4, 40.0))
    lamp(col, "NavStarboardAft", "nav_green", (0.0, TANK_RADIUS + 6.6, 141.0))

    # DORSAL: TWO white, on the core's roofline.
    lamp(col, "NavDorsalFore", "nav_white", (CORE_RADIUS + 0.4, 0.0, 30.0))
    lamp(col, "NavDorsalAft", "nav_white", (CORE_RADIUS + 0.4, 0.0, 70.0))

    # VENTRAL: ONE yellow.
    lamp(col, "NavVentral", "nav_yellow", (-(CORE_RADIUS + 0.4), 0.0, 50.0),
         radius=0.42)
    lamp(col, "StrobeDorsal", "strobe", (CORE_RADIUS + 0.6, 0.0, 84.0), radius=0.30)
    lamp(col, "StrobeVentral", "strobe", (-(CORE_RADIUS + 0.6), 0.0, 84.0),
         radius=0.30)


SHOTS = [
    ("hero", 62.0, 15.0, 1.9, 55.0),
    ("port", -90.0, 5.0, 1.9, 55.0),
    ("stern", 150.0, -18.0, 1.9, 55.0),
]


def main():
    reset()
    col = collection("FreighterA")
    build_materials()

    build_engine(col)
    build_core(col)
    build_fore_cluster(col)
    build_habitat(col)
    build_cargo(col)
    build_plumbing(col)
    build_radiators(col)
    build_navigation_lights(col)

    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = RADIATOR_PANELS * PANEL_LENGTH * PANEL_WIDTH

    print(f"  sketch A  {HABITAT_TOP:.0f} m overall, {WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE} milligee")

    render_views("/tmp/freighter-sketches/A.png", SHOTS, resolution=900, samples=28)

    # The maintained bit: the cargo pods, their clamps, and the pipe runs.
    render_preview("/tmp/freighter-sketches/A_detail.png",
                   target=(6.0, 0.0, 50.0), distance=42.0,
                   azimuth=15.0, elevation=22.0, resolution=900, samples=28)


main()
