"""
The Workers freighter — a working ship, and the shape the physics actually asks for.

Design language (§6.5 of DESIGN.md): utilitarian, function over form. Radiators where
the heat is, tanks where the mass is, handrails where a person has to go. Asymmetric
because the parts are different sizes and hiding that would cost mass. Maintained
rather than styled: patches, replacement panels, visible plumbing.

The ancestry is Soviet and Chinese heavy engineering, which in practice means:

  * Cross-axis symmetry only where the load is symmetric. The spine runs along the
    top of the hull because that is where the truss is stiffest against thrust, not
    because it is centred.
  * Every module is a separate object with a flange on each end. The ship looks
    assembled because it IS assembled, and a yard can swap a tank without a dry dock.
  * Nothing is faired in. The plumbing is on the outside because putting it inside
    costs hull volume and makes it unrepairable.

The radiator is the dominant feature, and that is not a stylistic choice. At a
milligee the panels are 4.4 % of the ship; at four they are 17.7 %. A freighter that
will accept months instead of days spends that mass on cargo, so what is modelled here
is the slow configuration — and the panels are still the largest thing on the hull,
which is the honest picture of a fusion ship.

Everything is in metres.

    blender --background --python tools/blender/ships/workers_freighter.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, asset_paths, box, collection, cylinder, export_blend, export_glb,
    join, lathe, link, material, render_orthographic, render_preview, render_views, reset, ring_of,
    shade_smooth_by_angle, sphere, torus,
)

NAME = "workers_freighter"

# --------------------------------------------------------------------------- numbers

# The layout is stated once, as a stack of z ranges, so that every part's position
# comes from the same table and a module cannot drift off the end of the hull. A
# freighter with a habitat floating two metres clear of its tanks is a modelling bug
# that is invisible in a three-quarter view and obvious in an elevation.
TANK_RADIUS = 6.0              # 12 m across
TANK_LENGTH = 42.0
TANK_COUNT = 3
TANK_GAP = 1.2

ENGINE_TOP = 6.0               # the engine block runs from 0 to here
TANKS_BOTTOM = ENGINE_TOP + 0.8
TANKS_TOP = TANKS_BOTTOM + TANK_COUNT * TANK_LENGTH + (TANK_COUNT - 1) * TANK_GAP
HABITAT_BOTTOM = TANKS_TOP + 0.8
HABITAT_LENGTH = 13.0
HABITAT_TOP = HABITAT_BOTTOM + HABITAT_LENGTH
DOCK_TOP = HABITAT_TOP + 1.4
SPINE_LENGTH = HABITAT_TOP - ENGINE_TOP
DRY_MASS_T = 6_500.0
PROPELLANT_T = 2_400.0
WET_MASS_T = DRY_MASS_T + PROPELLANT_T

EXHAUST_VELOCITY = 1_200_000.0

# A freighter is slow because being fast costs it cargo. The radiator is a RATIO of
# the ship -- A/m = (a v_e / 2)(1-eta)/eta / (2 sigma T^4) * areal -- so halving the
# acceleration halves the fraction of the hull given over to radiators:
#
#     a = 1.00 milligee -> 4.41 % of the ship, Jupiter in 185 days
#     a = 0.26 milligee -> 1.15 %,             Jupiter in 363 days
#
# At 8 900 t, 0.26 milligee asks for 12 770 m2 and the eight panels below supply
# 13 440. The panels are still the largest single thing on the hull, which is the
# honest picture of a fusion ship at any acceleration worth having.
CRUISE_MILLIGEE = 0.26
ACCELERATION = CRUISE_MILLIGEE / 1000.0 * 9.80665

RADIATOR_TEMPERATURE = 1500.0
RADIATOR_EFFICIENCY = 0.65
RADIATOR_AREAL_DENSITY = 8.0

# Four panels large enough to matter, each on an outrigger so it stands clear of the
# hull. Eight panels lying flat on the tanks would meet the same area and hide the
# ship: a radiator is only useful if it can see the sky, and a model is only readable
# if the hull it belongs to is visible.
RADIATOR_PANELS = 4
PANEL_LENGTH = 78.0
PANEL_WIDTH = 44.0
PANEL_SPLAY_DEGREES = 35.0
PANEL_THICKNESS = 0.22
PANEL_STANDOFF = 5.5   # metres from the tank surface to the panel

NOZZLES = 4

MATERIALS = {}


def build_materials():
    """
    Painted and galvanised steel, scuffed structures, and one conspicuously newer
    panel. The mixed finishes are the point: a maintained ship is not a uniform one.
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
    # Dull red, weakly emissive. A strongly emissive panel reads as a lamp rather than
    # as hot metal, which is how the courier's radiator was misread first time round.
    MATERIALS["radiator_hot"] = material(
        "WorkersRadiatorHot", (0.36, 0.11, 0.055), metallic=0.2, roughness=0.64,
        emission=(0.44, 0.10, 0.04), emission_strength=0.6)
    MATERIALS["plumbing"] = material(
        "WorkersPlumbing", (0.26, 0.265, 0.28), metallic=0.9, roughness=0.45)
    MATERIALS["warning"] = material(
        "WorkersWarning", (0.55, 0.32, 0.03), metallic=0.3, roughness=0.6)
    MATERIALS["window"] = material(
        "WorkersWindow", (0.02, 0.025, 0.035), metallic=0.4, roughness=0.15)
    MATERIALS["glow"] = material(
        "WorkersPlume", (0.42, 0.68, 1.0), metallic=0.0, roughness=0.4,
        emission=(0.42, 0.68, 1.0), emission_strength=4.0)


def assign(obj, key):
    obj.data.materials.append(MATERIALS[key])
    return obj


# --------------------------------------------------------------------------- tanks


def build_tanks(col):
    """
    Three pressure tanks on the axis, each with a flange at both ends.

    A tank is a pressure vessel first and a container second, so it is a barrel with
    domed ends rather than a cylinder with flat caps — and the domes are where the
    mass is saved, which is why a real one looks like three sausages in a row.
    """
    tanks = []
    z = TANKS_BOTTOM
    for i in range(TANK_COUNT):
        profile = [(0.0, 0.0)]
        for j in range(9):
            t = j / 8.0
            profile.append((TANK_RADIUS * math.sin(t * math.pi / 2.0), t * 2.2))
        profile.append((TANK_RADIUS, 2.2))
        profile.append((TANK_RADIUS, TANK_LENGTH - 2.2))
        for j in range(9):
            t = j / 8.0
            profile.append((TANK_RADIUS * math.cos(t * math.pi / 2.0), TANK_LENGTH - 2.2 + t * 2.2))

        tank = lathe(f"Tank{i}", profile, segments=64, location=(0, 0, z))
        assign(tank, "hull")
        shade_smooth_by_angle(tank, math.radians(35))
        link(tank, col)
        tanks.append(tank)

        # Flanges, so the assembly reads as bolted rather than grown.
        for zz in (z + 1.4, z + TANK_LENGTH - 1.4):
            flange = torus(f"Flange{i}_{zz:.0f}", TANK_RADIUS + 0.06, 0.16,
                           location=(0, 0, zz), major_segments=64, minor_segments=10)
            assign(flange, "structure")
            link(flange, col)
            tanks.append(flange)

        z += TANK_LENGTH + TANK_GAP

    return tanks


def build_spine(col):
    """
    A truss along the top of the tanks, and the one asymmetry the ship is built around.

    It runs at the top rather than through the middle because that is where a truss
    can be continuous past the tanks without a joint, and because a freighter's cargo
    handling happens underneath where the cranes are.
    """
    parts = []
    length = TANKS_TOP - TANKS_BOTTOM
    y = TANK_RADIUS + 1.4
    base = TANKS_BOTTOM

    # Two longerons and a run of vertical posts with diagonal bracing.
    for x, key in ((-1.5, "structure"), (1.5, "structure")):
        rail = box("Longeron", (0.35, 0.35, length),
                   location=(x, y, base + length / 2.0))
        assign(rail, key)
        link(rail, col)
        parts.append(rail)

    posts = 16
    for i in range(posts + 1):
        z = base + length * i / posts
        post = box("Post", (3.0, 0.3, 0.3), location=(0, y, z))
        assign(post, "structure")
        link(post, col)
        parts.append(post)

    # Diagonals between posts, the thing that makes a truss read as a truss.
    for i in range(posts):
        z0 = base + length * i / posts
        z1 = base + length * (i + 1) / posts
        dz = z1 - z0
        brace = box("Brace", (0.22, 0.22, math.hypot(3.0, dz)),
                    location=(0, y, (z0 + z1) / 2.0),
                    rotation=(0, math.atan2(3.0, dz) * (1 if i % 2 == 0 else -1), 0))
        assign(brace, "structure")
        link(brace, col)
        parts.append(brace)

    return parts


def build_radiators(col):
    """
    Four panels on outriggers, splayed outward like wings.

    Sized to the physics first and modelled second: at 0.26 milligee the thermal budget
    asks for 12 770 m2 and four panels of 78 by 44 m give 13 728.

    The splay is the design, and it is a radiator decision rather than a styling one.
    A panel lying flat against the hull radiates into the hull; a panel standing off
    parallel to it radiates into the next panel along, because a 78 m sheet beside a
    78 m sheet at five metres' separation is mostly looking at its neighbour. Splaying
    them outward at thirty-five degrees means every one of them can see open sky, which
    is the only thing a radiator is for -- and it has the side effect of letting the
    tanks be seen, which is what the ship is.
    """
    parts = []
    per_side = RADIATOR_PANELS // 2
    span = TANKS_TOP - TANKS_BOTTOM
    splay = math.radians(35.0)

    for side in (-1, 1):
        for i in range(per_side):
            z_centre = TANKS_BOTTOM + span * (i + 0.5) / per_side

            # The panel's own frame: rotated about z so its width runs outward, then
            # tilted about the ship's long axis so it lifts away from the hull.
            rotation = (splay * side, 0.0, 0.0)
            # A point on the panel's inner edge, out on the outrigger.
            hinge = TANK_RADIUS + PANEL_STANDOFF
            x = hinge * side

            panel = box(f"Panel{side}{i}",
                        (PANEL_WIDTH, PANEL_THICKNESS, PANEL_LENGTH),
                        location=(x + PANEL_WIDTH / 2.0 * side * math.cos(splay),
                                  0.0,
                                  z_centre),
                        rotation=rotation)
            assign(panel, "radiator")
            link(panel, col)
            parts.append(panel)

            # The outward half of the sheet, the hot end of the gradient.
            hot = box(f"PanelHot{side}{i}",
                      (PANEL_WIDTH * 0.5, PANEL_THICKNESS, PANEL_LENGTH),
                      location=(x + PANEL_WIDTH * 0.75 * side * math.cos(splay),
                                0.0,
                                z_centre),
                      rotation=rotation)
            assign(hot, "radiator_hot")
            link(hot, col)
            parts.append(hot)

            # Stiffeners, on the hull-facing edge where they cannot shade the sheet.
            for j in range(5):
                z = z_centre - PANEL_LENGTH / 2.0 + PANEL_LENGTH * (j + 0.5) / 5.0
                rib = box(f"PanelRib{side}{i}{j}",
                          (PANEL_WIDTH, PANEL_THICKNESS * 3.0, 0.6),
                          location=(x + PANEL_WIDTH / 2.0 * side * math.cos(splay),
                                    0.0, z),
                          rotation=rotation)
                assign(rib, "structure")
                link(rib, col)
                parts.append(rib)

            # The outrigger: a spar from the tank out to the hinge, and a tie back to
            # the hull, which is what keeps a 78 m wing from folding.
            for z in (z_centre - PANEL_LENGTH / 2.0 + 4.0,
                      z_centre + PANEL_LENGTH / 2.0 - 4.0):
                spar = box(f"Spar{side}{i}{z:.0f}",
                           (PANEL_STANDOFF, 0.7, 0.7),
                           location=((TANK_RADIUS + PANEL_STANDOFF / 2.0) * side, 0.0, z))
                assign(spar, "structure")
                link(spar, col)
                parts.append(spar)

                tie = box(f"Tie{side}{i}{z:.0f}",
                          (PANEL_STANDOFF * 1.6, 0.4, 0.4),
                          location=((TANK_RADIUS + PANEL_STANDOFF * 0.6) * side,
                                    0.0,
                                    z + 3.4),
                          rotation=(0.0, 0.0, 0.0))
                assign(tie, "structure")
                link(tie, col)
                parts.append(tie)

    return parts


def build_engine_block(col):
    """
    Four nozzles in a block, with the thrust structure that carries them into the spine.

    Clustered rather than spread over the base: a freighter is not trying to be
    graceful, and four bells close together need one thrust frame instead of six.
    """
    parts = []

    block = lathe("EngineBlock", [
        (TANK_RADIUS * 0.82, -3.6),
        (TANK_RADIUS * 0.95, -1.8),
        (TANK_RADIUS, 0.0),
        (TANK_RADIUS, 2.4),
    ], segments=64, location=(0, 0, ENGINE_TOP))
    assign(block, "hull_new")
    shade_smooth_by_angle(block, math.radians(34))
    link(block, col)
    parts.append(block)

    for i, (x, y, _) in enumerate(ring_of(NOZZLES, 3.4, z=0.0)):
        nozzle = lathe(f"Nozzle{i}", [
            (0.55, 0.0),
            (0.62, 0.5),
            (1.05, 2.4),
            (1.25, 3.6),
        ], segments=36, location=(x, y, ENGINE_TOP - 4.6))
        assign(nozzle, "dark")
        shade_smooth_by_angle(nozzle, math.radians(40))
        link(nozzle, col)
        parts.append(nozzle)

    # Thrust frame: the struts that take 4 MN and hand it to the tanks.
    for i in range(8):
        angle = 2.0 * math.pi * i / 8
        x = TANK_RADIUS * 0.9 * math.cos(angle)
        y = TANK_RADIUS * 0.9 * math.sin(angle)
        strut = box(f"ThrustStrut{i}", (0.4, 0.4, 5.0),
                    location=(x, y, ENGINE_TOP + 2.0),
                    rotation=(math.atan2(y, x) * 0.0, 0.22, 0))
        assign(strut, "structure")
        link(strut, col)
        parts.append(strut)

    return parts


# --------------------------------------------------------------------------- modules


def build_habitat(col):
    """
    The crew module, forward of the tanks and as far from the drive as the hull allows.

    A freighter's crew live next to their cargo, so the habitat is small, has windows
    because people need them, and is mounted on the spine rather than faired into it.
    """
    parts = []

    body = lathe("Habitat", [
        (0.0, 0.0),
        (3.2, 0.9),
        (3.4, 2.4),
        (3.4, HABITAT_LENGTH - 2.4),
        (3.2, HABITAT_LENGTH - 0.9),
        (0.0, HABITAT_LENGTH),
    ], segments=48, location=(0, 0, HABITAT_BOTTOM))
    assign(body, "hull")
    shade_smooth_by_angle(body, math.radians(35))
    link(body, col)
    parts.append(body)

    # A docking collar on the nose, which is how the freighter meets a station.
    collar = torus("DockingCollar", 1.9, 0.35, location=(0, 0, DOCK_TOP - 0.7),
                   major_segments=48, minor_segments=12)
    assign(collar, "structure")
    link(collar, col)
    parts.append(collar)

    hatch = cylinder("Hatch", 1.5, 0.5, location=(0, 0, DOCK_TOP - 0.35), vertices=32)
    assign(hatch, "dark")
    link(hatch, col)
    parts.append(hatch)

    # Windows. Eight, on one side only, because that is where the crew sit.
    for i in range(8):
        angle = -0.6 + i * 0.22
        x = 3.42 * math.cos(angle)
        y = 3.42 * math.sin(angle)
        w = cylinder(f"Window{i}", 0.34, 0.12,
                     location=(x, y, HABITAT_BOTTOM + HABITAT_LENGTH * 0.52),
                     rotation=(math.pi / 2, 0, angle), vertices=20)
        assign(w, "window")
        link(w, col)
        parts.append(w)

    return parts


def build_cargo(col):
    """
    Cargo and equipment boxes clamped to the underside of the spine.

    Deliberately uneven: the boxes are different sizes and not sympathetically
    arranged, because on a real ship they were added as they were needed.
    """
    parts = []
    y = -(TANK_RADIUS + 1.9)
    span = TANKS_TOP - TANKS_BOTTOM
    sizes = [
        (5.6, 3.4, 14.0, "hull", TANKS_BOTTOM + span * 0.10),
        (4.2, 3.0, 9.0, "hull_new", TANKS_BOTTOM + span * 0.28),
        (6.4, 3.8, 16.0, "hull", TANKS_BOTTOM + span * 0.48),
        (3.6, 2.6, 7.0, "hull_new", TANKS_BOTTOM + span * 0.68),
        (5.0, 3.2, 11.0, "hull", TANKS_BOTTOM + span * 0.86),
    ]
    for i, (w, h, length, key, z) in enumerate(sizes):
        b = box(f"Cargo{i}", (w, h, length), location=(0, y, z))
        assign(b, key)
        link(b, col)
        parts.append(b)

        # A warning stripe on one box, because some of them are not cargo.
        if i % 2 == 1:
            stripe = box(f"Stripe{i}", (w * 1.02, 0.06, length * 0.16),
                         location=(0, y - h / 2.0, z))
            assign(stripe, "warning")
            link(stripe, col)
            parts.append(stripe)

    return parts


def build_plumbing(col):
    """
    Propellant lines and cable runs, on the outside where they can be reached.

    This is the detail that separates the two factions' ships at a glance: the
    Workers run their plumbing where a person can get at it with a wrench, and the
    Illuminus do not have any visible.
    """
    parts = []
    for i in range(6):
        angle = 0.5 + i * 0.42
        x = (TANK_RADIUS + 0.5) * math.cos(angle)
        y = (TANK_RADIUS + 0.5) * math.sin(angle)

        # A run along the tanks, as a series of straight segments between the barrels.
        for j in range(TANK_COUNT):
            z0 = TANKS_BOTTOM + j * (TANK_LENGTH + TANK_GAP) + 3.0
            pipe = cylinder(f"Pipe{i}{j}", 0.22, TANK_LENGTH - 6.0,
                            location=(x, y, z0 + (TANK_LENGTH - 6.0) / 2.0),
                            vertices=12)
            assign(pipe, "plumbing")
            link(pipe, col)
            parts.append(pipe)

    # Two big feed lines from the tanks down to the engine block.
    for x in (-2.2, 2.2):
        feed = cylinder("Feed", 0.45, 12.0, location=(x, -TANK_RADIUS * 0.7, ENGINE_TOP),
                        rotation=(0.35, 0, 0), vertices=16)
        assign(feed, "plumbing")
        link(feed, col)
        parts.append(feed)

    return parts


def build_plume(col):
    """
    Engine exhaust, deliberately NOT part of the exported model.

    A cone of emissive material is a placeholder for an effect the client should draw
    -- it has no mass, no collider and no silhouette, and as a solid glowing object
    sitting off the stern it reads, in a plan view, as a separate lit thing flying in
    formation. Kept here so the shape is not lost, left out of the assembly.
    """
    plume = lathe("Plume", [
        (1.5, 0.0),
        (1.3, -3.0),
        (0.9, -8.0),
        (0.0, -14.0),
    ], segments=36, location=(0, 0, ENGINE_TOP - 5.0))
    assign(plume, "glow")
    link(plume, col)
    return plume


# --------------------------------------------------------------------------- views
# The angles a ship has to survive. Four three-quarter views at compass points, an
# elevation with no perspective to check where the parts actually are, and a plan.
# A model that only works from one of these is a model that does not work.
SHOTS = [
    ("hero",   38.0,  16.0, 2.05, 52.0),
    ("port",  218.0,  14.0, 2.05, 52.0),
    ("bow",   -52.0,  24.0, 2.05, 52.0),
    ("stern", 128.0,  20.0, 2.05, 52.0),
    ("side",  180.0,   0.0, 1.85, 52.0),
    ("above",  55.0,  50.0, 2.05, 52.0),
]


# --------------------------------------------------------------------------- assembly


def main():
    reset()
    col = collection("WorkersFreighter")
    build_materials()

    tanks = build_tanks(col)
    spine = build_spine(col)
    radiators = build_radiators(col)
    engine = build_engine_block(col)
    habitat = build_habitat(col)
    cargo = build_cargo(col)
    plumbing = build_plumbing(col)

    hull_parts = join(
        "WorkersFreighter_Hull",
        tanks + spine + engine + habitat + cargo + plumbing)
    panels = join("WorkersFreighter_Radiators", radiators)

    for obj in (hull_parts, panels):
        apply_transform(obj, scale=True)

    blend, glb, preview = asset_paths("ships", NAME)

    # What the thermal budget asks for, against what was built.
    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = RADIATOR_PANELS * PANEL_LENGTH * PANEL_WIDTH

    print(f"  hull      {SPINE_LENGTH:.0f} m overall, {TANK_RADIUS * 2:.0f} m tanks, "
          f"{WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE:.1f} milligee")
    print(f"            {built * RADIATOR_AREAL_DENSITY / 1000.0:,.0f} t "
          f"= {built * RADIATOR_AREAL_DENSITY / (WET_MASS_T * 1000.0) * 100:.1f} % of the ship")
    print(f"  drive     {NOZZLES} nozzles at v_e = {EXHAUST_VELOCITY / 1000:,.0f} km/s")

    render_views(preview, SHOTS, resolution=1200, samples=80)
    export_glb(glb, NAME)
    export_blend(blend)


main()
