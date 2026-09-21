"""
The Workers freighter — a working ship, in the Train pattern.

Design language (§6.5 of DESIGN.md): utilitarian, function over form. Radiators where
the heat is, tanks where the mass is, handrails where a person has to go. Asymmetric
because the parts are different sizes and hiding that would cost mass. Maintained
rather than styled: patches, replacement panels, visible plumbing.

The ancestry is Soviet and Chinese heavy engineering, which in practice means:

  * ONE clear structure with the clutter subordinate to it. The failure mode of a
    modular ship is "scaffolding" — all mess and no order — and Mir and the ISS work
    visually because the truss is the order and everything else is clipped to it.
    This hull is built the same way: one open lattice truss, and a manifest of
    canisters, wings, a tug and an engine bolted to it in a row.
  * Every module is a separate object with a flange on each end. The ship looks
    assembled because it IS assembled, and a yard can swap a canister without a dry
    dock.
  * Nothing is faired in. The plumbing is on the outside because putting it inside
    costs hull volume and makes it unrepairable.

The composition, engine to nose: an engine frame with four nozzles, a hundred and
thirty metres of open truss, a row of clipped canisters — three warning-striped
propellant, the rest whatever the contract says — a Soyuz ball-and-cone crew tug at
the front, and four radiator wings mounted perpendicular across the truss like the
station arrays the design descends from. The wingspan is wider than the hull is
long, which is what thirteen and a half thousand square metres of panel asks for.

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
    join, lathe, link, material, render_views, reset, ring_of,
    shade_smooth_by_angle, sphere, torus,
)

NAME = "workers_freighter"

# --------------------------------------------------------------------------- numbers

# The layout is stated once, as a stack of z ranges, so that every part's position
# comes from the same table and a module cannot drift off the end of the hull. A
# freighter with a habitat floating two metres clear of its tanks is a modelling bug
# that is invisible in a three-quarter view and obvious in an elevation.
FRAME_TOP = 8.0                # the engine frame runs from 0 to here
TRUSS_BOTTOM = FRAME_TOP
TRUSS_TOP = 140.0
TRUSS_HALF = 1.8               # the truss is 3.6 m square
TUG_Z = 146.0                  # the crew ball's centre
TUG_TOP = TUG_Z + 9.7          # collar face
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
# At 8 900 t, 0.26 milligee asks for 12 770 m2 and the four wings below supply
# 13 728. The panels are still the largest single thing on the hull, which is the
# honest picture of a fusion ship at any acceleration worth having.
CRUISE_MILLIGEE = 0.26
ACCELERATION = CRUISE_MILLIGEE / 1000.0 * 9.80665

RADIATOR_TEMPERATURE = 1500.0
RADIATOR_EFFICIENCY = 0.65
RADIATOR_AREAL_DENSITY = 8.0

# Four wings mounted PERPENDICULAR to the truss, two cross-truss stations amidships.
# Each wing runs 78 m out from the truss and 44 m along it, split into four blankets:
# station practice and the physics agree, because a panel wants to be as far from the
# hull and from its neighbours as the structure allows.
RADIATOR_PANELS = 4
PANEL_ALONG = 44.0
PANEL_ACROSS = 78.0
PANEL_THICKNESS = 0.22
WING_STATIONS = (62.0, 110.0)

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

    # ---------------------------------------------------------------- navigation lights
    #
    # THE CYGNUS CONVENTION, new for this hull. The freighter carried no navigation
    # lights at all until now; the convention is written up in full in DESIGN.md
    # section 6.6, and the short version is that the COUNT is the message:
    #
    #     PORT        flashing RED, in a pair so the hull's length reads
    #     STARBOARD   flashing GREEN, the same pair
    #     DORSAL      exactly TWO flashing WHITE
    #     VENTRAL     exactly ONE flashing YELLOW
    #
    # Two white above and one yellow below answers "which way is that thing's roof
    # pointing" even when the colours wash out. The client flashes everything named
    # with Nav or Strobe, by schedule rather than by faction -- a steady light is not
    # the convention.
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

    # The anti-collision strobe, which Dragon carries alongside its red and green. Much
    # brighter than the rest and meant to be seen before anything else.
    MATERIALS["strobe"] = material(
        "WorkersStrobe", (0.85, 0.85, 0.85), metallic=0.0, roughness=0.30,
        emission=(1.0, 1.0, 1.0), emission_strength=1.0)

    MATERIALS["glow"] = material(
        "WorkersPlume", (0.42, 0.68, 1.0), metallic=0.0, roughness=0.4,
        emission=(0.42, 0.68, 1.0), emission_strength=4.0)


def assign(obj, key):
    obj.data.materials.append(MATERIALS[key])
    return obj


# --------------------------------------------------------------------------- truss


def build_truss(col, name, z0, z1, half, step=8.0):
    """
    An open lattice box truss: four longerons, posts, and alternating diagonals on
    every face. The one clear order everything else hangs off — a truss reads as
    engineering rather than mess precisely because it repeats.
    """
    parts = []
    length = z1 - z0
    zc = (z0 + z1) / 2.0

    for x in (-half, half):
        for y in (-half, half):
            rail = box(f"{name}Longeron", (0.30, 0.30, length),
                       location=(x, y, zc))
            assign(rail, "structure")
            link(rail, col)
            parts.append(rail)

    stations = int(length / step)
    for i in range(stations + 1):
        z = z0 + length * i / stations
        for y in (-half, half):
            post = box(f"{name}PostX", (2 * half, 0.24, 0.24),
                       location=(0, y, z))
            assign(post, "structure")
            link(post, col)
            parts.append(post)
        for x in (-half, half):
            post = box(f"{name}PostY", (0.24, 2 * half, 0.24),
                       location=(x, 0, z))
            assign(post, "structure")
            link(post, col)
            parts.append(post)

    for i in range(stations):
        za = z0 + length * i / stations
        zb = z0 + length * (i + 1) / stations
        dz = zb - za
        span = 2 * half
        diag_len = math.hypot(span, dz)
        angle = math.atan2(dz, span)

        for y in (-half, half):
            d = box(f"{name}DiagX", (diag_len, 0.18, 0.18),
                    location=(0, y, (za + zb) / 2.0),
                    rotation=(0, -angle * (1 if i % 2 == 0 else -1), 0))
            assign(d, "structure")
            link(d, col)
            parts.append(d)
        for x in (-half, half):
            d = box(f"{name}DiagY", (0.18, diag_len, 0.18),
                    location=(x, 0, (za + zb) / 2.0),
                    rotation=(angle * (1 if i % 2 == 0 else -1), 0, 0))
            assign(d, "structure")
            link(d, col)
            parts.append(d)

    return parts


def build_handrails(col):
    """
    Handrails along two longerons, because a person has to get from the tug to the
    engine with something to hold. A rail and its standoff posts, nothing more.
    """
    parts = []
    for x, y in ((TRUSS_HALF + 0.18, 0.0), (0.0, TRUSS_HALF + 0.18)):
        length = TRUSS_TOP - TRUSS_BOTTOM - 8.0
        rail = box("Handrail", (0.08 if x else 0.5, 0.5 if x else 0.08, length),
                   location=(x, y, (TRUSS_BOTTOM + TRUSS_TOP) / 2.0))
        assign(rail, "plumbing")
        link(rail, col)
        parts.append(rail)

        z = TRUSS_BOTTOM + 6.0
        while z < TRUSS_TOP - 4.0:
            post = box("HandrailPost", (0.5 if x else 0.06, 0.06 if x else 0.5, 0.06),
                       location=(x - 0.15 if x else 0.0, y - 0.15 if y else 0.0, z))
            assign(post, "plumbing")
            link(post, col)
            parts.append(post)
            z += 6.0

    return parts


# --------------------------------------------------------------------------- drive


def build_engine(col):
    """The back end of the train: a wider frame, a thrust plate, four nozzles."""
    parts = build_truss(col, "EngineFrame", 0.0, FRAME_TOP, 3.0, step=4.0)

    plate = box("ThrustPlate", (7.0, 7.0, 1.4), location=(0, 0, FRAME_TOP - 0.7))
    assign(plate, "structure")
    link(plate, col)
    parts.append(plate)

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
        parts.append(nozzle)

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
    ], segments=36, location=(0, 0, 0.0))
    assign(plume, "glow")
    link(plume, col)
    return plume


# --------------------------------------------------------------------------- manifest


def build_canisters(col):
    """
    The freight: canisters clipped in a row under the truss. Uneven sizes and
    finishes, three of them propellant (warning-striped), the rest whatever the
    contract says they are. Clips, not cradles — every canister is a swap, and two
    of them carry replacement plates because nothing on this ship is one age.
    """
    manifest = [
        # (radius, length, z centre, key, propellant?)
        (3.2, 16.0, 18.0, "hull", False),
        (4.5, 26.0, 40.0, "hull", True),
        (3.2, 14.0, 60.0, "hull_new", False),
        (4.5, 26.0, 78.0, "hull", True),
        (3.6, 18.0, 98.0, "hull_new", False),
        (4.5, 22.0, 118.0, "hull", True),
        (3.2, 14.0, 131.0, "hull", False),
    ]
    parts = []
    for i, (radius, length, z, key, propellant) in enumerate(manifest):
        y = -(TRUSS_HALF + radius + 0.6)
        body = cylinder(f"Canister{i}", radius, length,
                        location=(0, y, z), vertices=40)
        assign(body, key)
        link(body, col)
        parts.append(body)

        for zz in (z - length / 2.0 + 1.0, z + length / 2.0 - 1.0):
            flange = torus(f"CanFlange{i}{zz:.0f}", radius + 0.05, 0.12,
                           location=(0, y, zz), major_segments=40, minor_segments=8)
            assign(flange, "structure")
            link(flange, col)
            parts.append(flange)

        # The clips: two straps per canister from the truss down round the barrel.
        for dz in (-length * 0.28, length * 0.28):
            clip = box(f"Clip{i}{dz:+.0f}", (0.3, 1.0, 1.2),
                       location=(0, -(TRUSS_HALF + 0.5), z + dz))
            assign(clip, "structure")
            link(clip, col)
            parts.append(clip)

        if propellant:
            stripe = cylinder(f"CanStripe{i}", radius + 0.06, 1.6,
                              location=(0, y, z), vertices=40)
            assign(stripe, "warning")
            link(stripe, col)
            parts.append(stripe)
        elif i % 2 == 1:
            # A replacement plate on every second cargo canister.
            plate = box(f"CanPlate{i}", (0.08, radius * 1.1, length * 0.4),
                        location=(radius * 0.7, y, z))
            assign(plate, "hull_new")
            link(plate, col)
            parts.append(plate)

    return parts


def build_tug(col):
    """
    The crew tug: a Soyuz ball, a cone, a collar. Small, because the crew are
    passengers on their own railroad and the freight is the ship.
    """
    parts = []

    ball = sphere("TugBall", radius=3.4, location=(0, 0, TUG_Z),
                  segments=40, rings=20)
    assign(ball, "hull")
    link(ball, col)
    parts.append(ball)

    cone = lathe("TugCone", [
        (3.0, 0.0),
        (2.6, 2.0),
        (1.6, 5.5),
        (1.4, 7.0),
    ], segments=48, location=(0, 0, TUG_Z + 2.0))
    assign(cone, "hull_new")
    shade_smooth_by_angle(cone, math.radians(35))
    link(cone, col)
    parts.append(cone)

    collar = torus("TugCollar", 1.4, 0.3, location=(0, 0, TUG_Z + 9.4),
                   major_segments=40, minor_segments=10)
    assign(collar, "structure")
    link(collar, col)
    parts.append(collar)

    for i in range(4):
        angle = 0.5 + i * 0.5
        w = cylinder(f"TugWindow{i}", 0.26, 0.12,
                     location=(3.42 * math.cos(angle), 3.42 * math.sin(angle),
                               TUG_Z + 0.8),
                     rotation=(math.pi / 2, 0, angle), vertices=20)
        assign(w, "window")
        link(w, col)
        parts.append(w)

    return parts


# --------------------------------------------------------------------------- radiator


def build_wing(col, station, z, side):
    """
    One radiator wing, deployed and fixed: a mast out from the cross truss, a sheet
    78 m across and 44 m along, split into four blankets by seams, with a tie from
    the wingtip back to the truss because 78 m of wing does not cantilever. Workers
    do not stow: folding gear is mass, and mass is cargo.
    """
    parts = []

    mast = box(f"WingMast{station}{side}",
               (PANEL_ACROSS * 0.22, 0.5, 0.5),
               location=(side * (13.0 + PANEL_ACROSS * 0.11), 0, z))
    assign(mast, "structure")
    link(mast, col)
    parts.append(mast)

    wing = box(f"Wing{station}{side}",
               (PANEL_ACROSS, PANEL_THICKNESS, PANEL_ALONG),
               location=(side * (13.0 + PANEL_ACROSS / 2.0), 0, z))
    assign(wing, "radiator")
    link(wing, col)
    parts.append(wing)

    # The hot outer half, as a thin skin PROUD of each face rather than a second
    # slab inside the wing's volume: two coplanar surfaces fight, and the first
    # version of this rendered hot-or-grey by face lottery.
    for face in (-1, 1):
        hot = box(f"WingHot{station}{side}{face:+d}",
                  (PANEL_ACROSS * 0.55, 0.024, PANEL_ALONG),
                  location=(side * (13.0 + PANEL_ACROSS * 0.72),
                            face * (PANEL_THICKNESS / 2.0 + 0.012), z))
        assign(hot, "radiator_hot")
        link(hot, col)
        parts.append(hot)

    # Blanket divisions: the wing is four blankets, not one slab.
    for j in range(1, 4):
        seam = box(f"WingSeam{station}{side}{j}",
                   (PANEL_ACROSS, PANEL_THICKNESS * 2.5, 0.5),
                   location=(side * (13.0 + PANEL_ACROSS / 2.0), 0,
                             z - PANEL_ALONG / 2.0 + PANEL_ALONG * j / 4.0))
        assign(seam, "structure")
        link(seam, col)
        parts.append(seam)

    # The wingtip tie: a diagonal from the tip back down to the truss.
    tip_x = side * (13.0 + PANEL_ACROSS * 0.85)
    run = abs(tip_x) - TRUSS_HALF
    drop = 36.0
    tie_len = math.hypot(run, drop)
    tie = box(f"WingTie{station}{side}", (0.25, 0.25, tie_len),
              location=((tip_x + side * TRUSS_HALF) / 2.0, 0, z - drop / 2.0),
              rotation=(0.0, side * math.atan2(run, drop), 0.0))
    assign(tie, "structure")
    link(tie, col)
    parts.append(tie)

    return parts


def build_radiators(col):
    """
    Four wings on two cross-truss stations amidships, perpendicular to the truss.
    The wingspan is the widest thing about the ship, and that is the honest
    silhouette of this much panel.
    """
    parts = []
    for station, z in enumerate(WING_STATIONS):
        bar = box(f"CrossTruss{station}", (26.0, 1.0, 1.0),
                  location=(0, 0, z))
        assign(bar, "structure")
        link(bar, col)
        parts.append(bar)

        for side in (-1, 1):
            parts.extend(build_wing(col, station, z, side))

    return parts


def build_plumbing(col):
    """A pair of propellant mains down the truss, flanged at every bay."""
    parts = []
    for x in (-0.9, 0.9):
        pipe = cylinder(f"Main{x:+.0f}", 0.22, TRUSS_TOP - TRUSS_BOTTOM,
                        location=(x, 0, (TRUSS_BOTTOM + TRUSS_TOP) / 2.0),
                        vertices=12)
        assign(pipe, "plumbing")
        link(pipe, col)
        parts.append(pipe)

        z = TRUSS_BOTTOM + 4.0
        j = 0
        while z < TRUSS_TOP - 2.0:
            flange = torus(f"MainFlange{x:+.0f}{j}", 0.32, 0.09,
                           location=(x, 0, z), major_segments=18, minor_segments=8)
            assign(flange, "structure")
            link(flange, col)
            parts.append(flange)
            z += 8.0
            j += 1

    return parts


def build_navigation_lights(col):
    """
    The navigation lights, to the Cygnus convention — new for this hull, which
    carried none until now. See the note over the materials. Each lamp is a dark
    housing with a lens in it, because an emissive patch with nothing around it
    reads as a texture error and a lens in a fitting reads as a lamp.
    """
    lights = []

    def lamp(name, key, location, radius=0.36):
        housing = sphere(f"{name}_Housing", radius=radius * 1.6, location=location,
                         segments=16, rings=8)
        assign(housing, "dark")
        link(housing, col)

        lens = sphere(name, radius=radius, location=location, segments=16, rings=8)
        assign(lens, key)
        link(lens, col)

        lights.append(housing)
        lights.append(lens)
        return lens

    # PORT: red, fore and aft, so the hull's length reads as well as its heading.
    lamp("NavPort", "nav_red", (0.0, -3.8, TUG_Z))
    lamp("NavPortAft", "nav_red", (0.0, -3.4, 4.0))

    # STARBOARD: green, the same two stations.
    lamp("NavStarboard", "nav_green", (0.0, 3.8, TUG_Z))
    lamp("NavStarboardAft", "nav_green", (0.0, 3.4, 4.0))

    # DORSAL: TWO white — one on the tug's roofline, one on a mast over the truss.
    # The count is the message, so there are exactly two.
    lamp("NavDorsalFore", "nav_white", (3.8, 0.0, TUG_Z + 1.0))
    mast = box("DorsalMast", (0.3, 0.3, 2.2),
               location=(TRUSS_HALF + 1.0, 0, 76.0))
    assign(mast, "structure")
    link(mast, col)
    lights.append(mast)
    lamp("NavDorsalAft", "nav_white", (TRUSS_HALF + 1.4, 0.0, 77.2))

    # VENTRAL: ONE yellow. Not two. That asymmetry with the roof is the mechanism.
    lamp("NavVentral", "nav_yellow", (-3.8, 0.0, TUG_Z - 0.6), radius=0.42)

    # The anti-collision strobes, dorsal and ventral, on the tug's nose.
    lamp("StrobeDorsal", "strobe", (2.6, 0.0, TUG_Z + 8.0), radius=0.30)
    lamp("StrobeVentral", "strobe", (-2.6, 0.0, TUG_Z + 8.0), radius=0.30)

    return lights


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

    engine = build_engine(col)
    truss = build_truss(col, "MainTruss", TRUSS_BOTTOM, TRUSS_TOP, TRUSS_HALF)
    handrails = build_handrails(col)
    canisters = build_canisters(col)
    tug = build_tug(col)
    radiators = build_radiators(col)
    plumbing = build_plumbing(col)
    lights = build_navigation_lights(col)

    # Fixed parts into one mesh; the drive and the wings stay separate because they
    # are the parts that move.
    hull_parts = join(
        "WorkersFreighter_Hull",
        truss + handrails + canisters + tug + plumbing + lights)
    drive = join("WorkersFreighter_Drive", engine)
    panels = join("WorkersFreighter_Radiators", radiators)

    # Bake every node transform into the vertices, so the exported nodes are all
    # identity. The client frames a hull from the TRANSFORMED CORNERS of each
    # part's axis-aligned box, and a box rotated about a node inflates by root two
    # -- with the wing frame left on the Radiators node the client measured the
    # courier 79 m long instead of 58, and the cockpit camera keys its standoff
    # off that length. The pivots an animation would want are geometry positions
    # (the two cross-truss stations, the spar roots), not node transforms, so
    # nothing is lost by baking them.
    for obj in (hull_parts, drive, panels):
        apply_transform(obj, location=True, rotation=True, scale=True)

    blend, glb, preview = asset_paths("ships", NAME)

    # What the thermal budget asks for, against what was built.
    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = RADIATOR_PANELS * PANEL_ALONG * PANEL_ACROSS

    print(f"  hull      {TUG_TOP:.0f} m overall, {WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE} milligee")
    print(f"            {built * RADIATOR_AREAL_DENSITY / 1000.0:,.0f} t "
          f"= {built * RADIATOR_AREAL_DENSITY / (WET_MASS_T * 1000.0) * 100:.1f} % of the ship")
    print(f"            {RADIATOR_PANELS} perpendicular wings at z = "
          f"{', '.join(f'{z:.0f}' for z in WING_STATIONS)}")
    print(f"  drive     {NOZZLES} nozzles at v_e = {EXHAUST_VELOCITY / 1000:,.0f} km/s")

    # Framed to the whole ship plus its wings: 156 m of hull and a 180 m span
    # needs about 370 m of standoff at this lens to sit inside the frame.
    render_views(preview, SHOTS, resolution=1200, samples=80)
    export_glb(glb, NAME)
    export_blend(blend)


main()
