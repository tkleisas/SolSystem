"""
Sketch C -- "The Train". A Workers freighter direction.

Space railroad: one long open lattice truss, cargo canisters clipped in a row
along it, a small Soyuz ball-and-cone crew tug at the front, the engine on a frame
at the back, and the radiator wings mounted perpendicular across the truss like
the ISS solar arrays -- wingspan wider than the hull is long, which is what a
fusion railroad actually looks like. Maximum modular: every part is a separate
bolt-on, and the truss is the order the clutter sits on.

RADIATOR: fixed, perpendicular. Four wings of 44 m along the truss by 78 m across,
two per side on two cross-truss stations. ISS practice and the physics agree: the
panels want to be as far from the hull and from each other as the structure allows.

    blender --background --python tools/blender/ships/sketches/freighter_c.py
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

WET_MASS_T = 8_900.0
EXHAUST_VELOCITY = 1_200_000.0
CRUISE_MILLIGEE = 0.26
ACCELERATION = CRUISE_MILLIGEE / 1000.0 * 9.80665
RADIATOR_TEMPERATURE = 1500.0
RADIATOR_EFFICIENCY = 0.65
RADIATOR_AREAL_DENSITY = 8.0

# Four wings, 44 m along the truss by 78 m across: the production area, kept.
RADIATOR_PANELS = 4
PANEL_ALONG = 44.0
PANEL_ACROSS = 78.0
PANEL_THICKNESS = 0.22

NOZZLES = 4

# The layout, engine plane to nose.
FRAME_TOP = 8.0
TRUSS_BOTTOM = 8.0
TRUSS_TOP = 128.0
TRUSS_HALF = 1.8               # the truss is 3.6 m square
TUG_Z = 132.0

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


def build_truss(col, name, z0, z1, half, step=8.0, key="structure"):
    """
    An open lattice box truss: four longerons, posts, and alternating diagonals
    on every face. The one clear order everything else hangs off -- a truss reads
    as engineering rather than mess precisely because it repeats.
    """
    length = z1 - z0
    zc = (z0 + z1) / 2.0

    for x in (-half, half):
        for y in (-half, half):
            rail = box(f"{name}Longeron", (0.30, 0.30, length),
                       location=(x, y, zc))
            assign(rail, key)
            link(rail, col)

    stations = int(length / step)
    for i in range(stations + 1):
        z = z0 + length * i / stations
        for y in (-half, half):
            post = box(f"{name}PostX", (2 * half, 0.24, 0.24),
                       location=(0, y, z))
            assign(post, key)
            link(post, col)
        for x in (-half, half):
            post = box(f"{name}PostY", (0.24, 2 * half, 0.24),
                       location=(x, 0, z))
            assign(post, key)
            link(post, col)

    for i in range(stations):
        za = z0 + length * i / stations
        zb = z0 + length * (i + 1) / stations
        dz = zb - za
        span = 2 * half
        diag_len = math.hypot(span, dz)
        angle = math.atan2(dz, span)

        for y in (-half, half):
            # Diagonals on the x-facing faces, alternating direction.
            d = box(f"{name}DiagX", (diag_len, 0.18, 0.18),
                    location=(0, y, (za + zb) / 2.0),
                    rotation=(0, -angle * (1 if i % 2 == 0 else -1), 0))
            assign(d, key)
            link(d, col)
        for x in (-half, half):
            d = box(f"{name}DiagY", (0.18, diag_len, 0.18),
                    location=(x, 0, (za + zb) / 2.0),
                    rotation=(angle * (1 if i % 2 == 0 else -1), 0, 0))
            assign(d, key)
            link(d, col)


def build_engine(col):
    """The back end of the train: a wider frame, a thrust plate, four nozzles."""
    build_truss(col, "EngineFrame", 0.0, FRAME_TOP, 3.0, step=4.0)

    plate = box("ThrustPlate", (7.0, 7.0, 1.4), location=(0, 0, FRAME_TOP - 0.7))
    assign(plate, "structure")
    link(plate, col)

    for i, (x, y, _) in enumerate(ring_of(NOZZLES, 2.8, z=0.0)):
        nozzle = lathe(f"Nozzle{i}", [
            (0.55, 0.0),
            (0.62, 0.5),
            (1.05, 2.4),
            (1.25, 3.6),
        ], segments=36, location=(x, y, 2.0))
        assign(nozzle, "dark")
        shade_smooth_by_angle(nozzle, math.radians(40))
        link(nozzle, col)


def build_canisters(col):
    """
    The freight: canisters clipped in a row under the truss. Uneven sizes and
    finishes, three of them propellant (warning-striped), the rest whatever the
    contract says they are. Clamps, not cradles -- every canister is a swap.
    """
    manifest = [
        # (radius, length, z centre, key, propellant?)
        (3.2, 16.0, 18.0, "hull", False),
        (4.5, 26.0, 38.0, "hull", True),
        (3.2, 14.0, 56.0, "hull_new", False),
        (4.5, 26.0, 76.0, "hull", True),
        (3.2, 16.0, 94.0, "hull", False),
        (3.6, 18.0, 112.0, "hull_new", False),
    ]
    for i, (radius, length, z, key, propellant) in enumerate(manifest):
        y = -(TRUSS_HALF + radius + 0.6)
        body = cylinder(f"Canister{i}", radius, length,
                        location=(0, y, z), vertices=40)
        assign(body, key)
        link(body, col)

        for zz in (z - length / 2.0 + 1.0, z + length / 2.0 - 1.0):
            flange = torus(f"CanFlange{i}{zz:.0f}", radius + 0.05, 0.12,
                           location=(0, y, zz), major_segments=40, minor_segments=8)
            assign(flange, "structure")
            link(flange, col)

        # The clip: two straps from the truss down round the barrel.
        for dz in (-length * 0.28, length * 0.28):
            clip = box(f"Clip{i}{dz:+.0f}", (0.3, 0.5, 1.2),
                       location=(0, -(TRUSS_HALF + 0.3), z + dz))
            assign(clip, "structure")
            link(clip, col)

        if propellant:
            stripe = cylinder(f"CanStripe{i}", radius + 0.06, 1.6,
                              location=(0, y, z), vertices=40)
            assign(stripe, "warning")
            link(stripe, col)


def build_tug(col):
    """
    The crew tug: a Soyuz ball, a cone, a collar. Small, because the crew are
    passengers on their own railroad and the freight is the ship.
    """
    ball = sphere("TugBall", radius=3.4, location=(0, 0, TUG_Z),
                  segments=40, rings=20)
    assign(ball, "hull")
    link(ball, col)

    cone = lathe("TugCone", [
        (3.0, 0.0),
        (2.6, 2.0),
        (1.6, 5.5),
        (1.4, 7.0),
    ], segments=48, location=(0, 0, TUG_Z + 2.0))
    assign(cone, "hull_new")
    shade_smooth_by_angle(cone, math.radians(35))
    link(cone, col)

    collar = torus("TugCollar", 1.4, 0.3, location=(0, 0, TUG_Z + 9.4),
                   major_segments=40, minor_segments=10)
    assign(collar, "structure")
    link(collar, col)

    for i in range(4):
        angle = 0.5 + i * 0.5
        w = cylinder(f"TugWindow{i}", 0.26, 0.12,
                     location=(3.42 * math.cos(angle), 3.42 * math.sin(angle),
                               TUG_Z + 0.8),
                     rotation=(math.pi / 2, 0, angle), vertices=20)
        assign(w, "window")
        link(w, col)


def build_radiators(col):
    """
    Four wings perpendicular to the truss, two cross-truss stations amidships.
    Each wing runs 78 m out from the truss and 44 m along it: wingspan over a
    hundred and fifty metres, which is the honest silhouette of this much panel.
    """
    for station, z in enumerate((58.0, 104.0)):
        # The cross truss this station's wings hang from.
        bar = box(f"CrossTruss{station}", (26.0, 1.0, 1.0),
                  location=(0, 0, z))
        assign(bar, "structure")
        link(bar, col)

        for side in (-1, 1):
            # A mast out from the cross truss to the wing root, with a tie back.
            mast = box(f"WingMast{station}{side}",
                       (PANEL_ACROSS * 0.22, 0.5, 0.5),
                       location=(side * (13.0 + PANEL_ACROSS * 0.11), 0, z))
            assign(mast, "structure")
            link(mast, col)

            wing = box(f"Wing{station}{side}",
                       (PANEL_ACROSS, PANEL_THICKNESS, PANEL_ALONG),
                       location=(side * (13.0 + PANEL_ACROSS / 2.0), 0, z))
            assign(wing, "radiator")
            link(wing, col)

            # The hot outer half, as a thin skin PROUD of each face rather than a
            # second slab inside the wing's volume: two coplanar surfaces fight,
            # and the wings were rendering hot-or-grey by face lottery.
            for face in (-1, 1):
                hot = box(f"WingHot{station}{side}{face:+d}",
                          (PANEL_ACROSS * 0.55, 0.024, PANEL_ALONG),
                          location=(side * (13.0 + PANEL_ACROSS * 0.72),
                                    face * (PANEL_THICKNESS / 2.0 + 0.012), z))
                assign(hot, "radiator_hot")
                link(hot, col)

            # Blanket divisions: the wing is four blankets, not one slab.
            for j in range(1, 4):
                seam = box(f"WingSeam{station}{side}{j}",
                           (PANEL_ACROSS, PANEL_THICKNESS * 2.5, 0.5),
                           location=(side * (13.0 + PANEL_ACROSS / 2.0), 0,
                                     z - PANEL_ALONG / 2.0 + PANEL_ALONG * j / 4.0))
                assign(seam, "structure")
                link(seam, col)

            # A tie from the wingtip back to the truss, because 78 m of wing
            # does not cantilever.
            tie = box(f"WingTie{station}{side}", (0.25, 0.25, 40.0),
                      location=(side * (13.0 + PANEL_ACROSS * 0.85), 0, z - 16.0),
                      rotation=(0.0, 0.0, 0.0))
            assign(tie, "structure")
            link(tie, col)


def build_plumbing(col):
    """A pair of propellant mains down the truss, flanged at every bay."""
    for x in (-0.9, 0.9):
        pipe = cylinder(f"Main{x:+.0f}", 0.22, TRUSS_TOP - TRUSS_BOTTOM,
                        location=(x, 0, (TRUSS_BOTTOM + TRUSS_TOP) / 2.0),
                        vertices=12)
        assign(pipe, "plumbing")
        link(pipe, col)

        z = TRUSS_BOTTOM + 4.0
        j = 0
        while z < TRUSS_TOP - 2.0:
            flange = torus(f"MainFlange{x:+.0f}{j}", 0.32, 0.09,
                           location=(x, 0, z), major_segments=18, minor_segments=8)
            assign(flange, "structure")
            link(flange, col)
            z += 8.0
            j += 1


def build_navigation_lights(col):
    """Cygnus, counts intact: tug lights fore, engine-frame lights aft."""
    lamp(col, "NavPort", "nav_red", (0.0, -3.8, TUG_Z))
    lamp(col, "NavPortAft", "nav_red", (0.0, -3.4, 4.0))
    lamp(col, "NavStarboard", "nav_green", (0.0, 3.8, TUG_Z))
    lamp(col, "NavStarboardAft", "nav_green", (0.0, 3.4, 4.0))

    # DORSAL: TWO white -- one on the tug's roofline, one on a truss-top mast.
    lamp(col, "NavDorsalFore", "nav_white", (3.8, 0.0, TUG_Z + 1.0))
    mast = box("DorsalMast", (0.3, 0.3, 2.2), location=(TRUSS_HALF + 1.0, 0, 70.0))
    assign(mast, "structure")
    link(mast, col)
    lamp(col, "NavDorsalAft", "nav_white", (TRUSS_HALF + 1.4, 0.0, 71.2))

    # VENTRAL: ONE yellow.
    lamp(col, "NavVentral", "nav_yellow", (-3.8, 0.0, TUG_Z - 0.6), radius=0.42)
    lamp(col, "StrobeDorsal", "strobe", (2.6, 0.0, TUG_Z + 8.0), radius=0.30)
    lamp(col, "StrobeVentral", "strobe", (-2.6, 0.0, TUG_Z + 8.0), radius=0.30)


SHOTS = [
    ("hero", 40.0, 14.0, 2.0, 55.0),
    ("port", -90.0, 5.0, 2.0, 55.0),
    ("stern", 150.0, -16.0, 2.0, 55.0),
]


def main():
    reset()
    col = collection("FreighterC")
    build_materials()

    build_engine(col)
    build_truss(col, "MainTruss", TRUSS_BOTTOM, TRUSS_TOP, TRUSS_HALF)
    build_canisters(col)
    build_tug(col)
    build_radiators(col)
    build_plumbing(col)
    build_navigation_lights(col)

    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = RADIATOR_PANELS * PANEL_ALONG * PANEL_ACROSS

    print(f"  sketch C  {TUG_Z + 10.0:.0f} m overall, {WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE} milligee")

    render_views("/tmp/freighter-sketches/C.png", SHOTS, resolution=900, samples=28)

    # The maintained bit: a canister, its clips, the truss bays, the flanged mains.
    render_preview("/tmp/freighter-sketches/C_detail.png",
                   target=(0.0, -5.0, 56.0), distance=24.0,
                   azimuth=38.0, elevation=8.0, resolution=900, samples=28)


main()
