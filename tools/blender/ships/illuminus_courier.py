"""
The Illuminus courier — a Starship descendant.

Design language (§6.5 of DESIGN.md): futuristic, gleaming, stylised, intimidating.
Smooth hulls, long unbroken curves, few visible seams, no obvious machinery. The
people who own these are showing off, because a shell is inherited property and a
statement of rank. Hard edges and high contrast: the aesthetic of something that has
never been rained on.

The silhouette is a Starship's and the ancestry is meant to be legible — one long
stainless cylinder, a domed forward section, a nose that is a curve rather than a
cone, and an engine bay crowded with bells. What has changed in a century and a half
is everything the physics forced:

  * The bells are magnetic nozzles, not combustion chambers. The drive is
    antimatter-catalysed D-D fusion at v_e = 1200 km/s, so there is no throat and no
    expansion ratio to read; what the nozzles do instead is a long, barely-tapered
    throat to give the plasma somewhere to finish expanding.
  * The radiator is enormous and the ship is built around it. Three panels stowed
    flat against the barrel, covering the aft two thirds of the hull and swung clear
    in flight. They are 17.6 % of the ship's mass, which is not a design choice so
    much as the thermal statement of the problem: the panels are the ship, and the
    hull is the thing that holds them apart.

Everything is in metres, and every dimension traces to a number in
docs/TRIP-ENERGY.md §16.

    blender --background --python tools/blender/ships/illuminus_courier.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, asset_paths, bevel, box, cylinder, export_blend, export_glb,
    join, lathe, link, material, collection, render_views, reset,
    ring_of, shade_smooth_by_angle, torus,
)

NAME = "illuminus_courier"

# --------------------------------------------------------------------------- numbers
# The ship, from the drive outward. Every one of these is load-bearing.

HULL_RADIUS = 4.5              # 9 m across, a Starship's gauge
HULL_LENGTH = 55.0             # nose tip to engine plane
BARREL_TOP = 44.0              # where the barrel ends and the forward dome begins
DRY_MASS_T = 1_100.0
PROPELLANT_T = 220.0
WET_MASS_T = DRY_MASS_T + PROPELLANT_T
EXHAUST_VELOCITY = 1_200_000.0  # m/s, the Workers' antimatter-catalysed fusion torch

# A courier is not a warship, and this is the number that makes it a courier rather
# than a torch. The thermal budget is a RATIO -- radiator per kilogram of ship is
# (a v_e / 2)(1-eta)/eta / (2 sigma T^4) * areal -- so a courier that will accept
# months instead of weeks across the system buys back nearly all of its own mass:
#
#     a = 4.0  milligee -> 17.7 % of the ship is radiator, Jupiter in 93 days
#     a = 1.0  milligee ->  4.4 %, Jupiter in 185 days
#     a = 0.1  milligee ->  0.4 %, Jupiter in 586 days
#
# At a tenth of a milligee the radiator is 0.4 % of the ship and the panels barely
# break the silhouette, which is the whole point. This is the one hull in the fleet
# that is not in a hurry, and the design language wants it smooth: a courier that
# arrives gleaming has been coasting, and one that arrives with its wings out has
# been running for its life. The Illuminus build the second kind too, and this is
# not it.
ACCELERATION = 0.000981        # m/s² = 0.1 milligee
CRUISE_MILLIGEE = 0.1

# The radiator, sized by the physics rather than by taste.
#
#   jet power   P  = m a v_e / 2          =   589 W per kg of ship
#   waste heat  Q  = P (1 - eta) / eta    with eta = 0.65
#   area        A  = Q / (2 sigma T^4)    with T = 1500 K, two-sided
#   mass        M  = A * 8 kg/m2
#
# For 1 320 t at 0.1 milligee that is 729 m2 and 6 t. Two panels of 21 x 18 m come to
# 756, which is the stowage plan for it: two facts, one geometry.
RADIATOR_PANELS = 2
PANEL_LENGTH = 21.0
PANEL_WIDTH = 18.0
PANEL_THICKNESS = 0.16
RADIATOR_AREAL_DENSITY = 8.0   # kg/m2
RADIATOR_TEMPERATURE = 1500.0  # K
RADIATOR_EFFICIENCY = 0.65

NOZZLES = 12                   # a ring, plus one in the middle
NOZZLE_RADIUS = 0.42
NOZZLE_LENGTH = 3.4

MATERIALS = {}


def build_materials():
    """Polished stainless steel, and the few other surfaces that break it up."""
    MATERIALS["steel"] = material(
        "IlluminusHull", (0.80, 0.82, 0.86), metallic=0.55, roughness=0.28)
    MATERIALS["steel_worn"] = material(
        "IlluminusHullWorn", (0.62, 0.635, 0.67), metallic=0.6, roughness=0.40)
    MATERIALS["dark"] = material(
        "IlluminusTrim", (0.02, 0.021, 0.024), metallic=0.85, roughness=0.22)
    MATERIALS["radiator"] = material(
        "IlluminusRadiator", (0.20, 0.21, 0.23), metallic=0.25, roughness=0.55)
    # A radiator at 1 500 K glows dull red, and the emission is deliberately weak. A
    # strong one stops reading as hot metal and starts reading as a lamp bolted to the
    # ship -- which is exactly how it was misread in the first plan view.
    MATERIALS["radiator_hot"] = material(
        "IlluminusRadiatorHot", (0.34, 0.10, 0.05), metallic=0.2, roughness=0.66,
        emission=(0.40, 0.085, 0.03), emission_strength=0.55)
    MATERIALS["nozzle"] = material(
        "IlluminusNozzle", (0.26, 0.27, 0.30), metallic=0.7, roughness=0.32)
    MATERIALS["glow"] = material(
        "IlluminusPlume", (0.35, 0.62, 0.95), metallic=0.0, roughness=0.4,
        emission=(0.35, 0.62, 0.95), emission_strength=4.0)


def assign(obj, key):
    obj.data.materials.append(MATERIALS[key])
    return obj


# --------------------------------------------------------------------------- hull


def build_hull(col):
    """
    The pressure hull: one barrel, one dome, one nose.

    The nose is a von Karman-ish curve rather than a cone because that is what the
    silhouette is for — a Starship's nose reads as a drawn line, and a cone does not.
    """
    # (radius, z) from the engine plane forward.
    barrel = [
        (0.0, 0.0), (HULL_RADIUS * 0.98, 0.0),
    ]
    for i in range(19):
        barrel.append((HULL_RADIUS, 2.0 + i * 2.3))
    for i in range(9):
        t = (i + 1) / 9.0
        barrel.append((HULL_RADIUS, BARREL_TOP - 2.0 + t * 2.0))

    # Forward dome and nose: r falls from the barrel to the tip over 11 m.
    nose_length = HULL_LENGTH - BARREL_TOP
    nose = []
    for i in range(1, 41):
        t = i / 40.0
        # A flattened power curve: blunt well past the half-way mark, then a fast
        # taper, which is the shape that reads as a nose rather than as a spike.
        r = HULL_RADIUS * (1.0 - t * t) ** 0.62
        nose.append((r, BARREL_TOP + t * nose_length))

    hull = lathe("Hull", barrier_safe(barrel) + nose, segments=96)
    assign(hull, "steel")
    shade_smooth_by_angle(hull, math.radians(38))
    link(hull, col)
    return hull


def barrier_safe(profile):
    """Drops consecutive duplicate rings, which would otherwise make zero-area faces."""
    out = []
    for radius, z in profile:
        if out and abs(out[-1][1] - z) < 1e-9 and abs(out[-1][0] - radius) < 1e-9:
            continue
        out.append((radius, z))
    return out


def build_rings(col):
    """
    Weld lines and structural rings.

    The brief says few visible seams, so these are deliberately sparse: three
    structural rings that read as joints between barrel sections, plus a raised
    band at the forward dome. Enough to give the eye a scale, not enough to make it
    look assembled.
    """
    rings = []
    for z in (12.0, 24.0, 36.0):
        ring = torus(f"Ring{z:.0f}", HULL_RADIUS + 0.02, 0.075,
                     location=(0, 0, z), major_segments=96, minor_segments=10)
        assign(ring, "dark")
        link(ring, col)
        rings.append(ring)

    # The forward band, where the barrel meets the dome.
    band = cylinder("ForwardBand", HULL_RADIUS + 0.035, 1.1, location=(0, 0, BARREL_TOP),
                    vertices=96)
    assign(band, "dark")
    link(band, col)
    rings.append(band)
    return rings


def build_windows(col):
    """
    A single row of viewports at the forward section.

    A crewed hull needs somewhere to look out of, and one continuous ribbon is
    cheaper and colder-looking than a scatter of portholes. It also gives the eye
    the only curved detail on an otherwise featureless cylinder.
    """
    windows = []
    for i in range(12):
        angle = 2.0 * math.pi * i / 12
        x = (HULL_RADIUS + 0.01) * math.cos(angle)
        y = (HULL_RADIUS + 0.01) * math.sin(angle)
        w = box(f"Viewport{i}", (0.9, 0.06, 0.42), location=(x, y, 40.0),
                rotation=(0, 0, angle + math.pi / 2))
        assign(w, "dark")
        link(w, col)
        windows.append(w)
    return windows


# --------------------------------------------------------------------------- drive


def build_nozzles(col):
    """
    Magnetic nozzles: a ring of twelve and one on the axis.

    No throat and no bell. The plasma is already at 1200 km/s when it leaves the
    reaction zone, so the nozzle's job is to stop it touching anything, which reads
    as a long straight throat with a slight flare rather than as a rocket engine.
    """
    nozzles = []
    for i, (x, y, z) in enumerate(ring_of(NOZZLES, 3.3, z=-NOZZLE_LENGTH / 2 + 0.2)):
        n = lathe(f"Nozzle{i}", [
            (NOZZLE_RADIUS * 0.55, 0.0),
            (NOZZLE_RADIUS * 0.62, 0.4),
            (NOZZLE_RADIUS, NOZZLE_LENGTH * 0.55),
            (NOZZLE_RADIUS * 1.18, NOZZLE_LENGTH),
        ] + [(NOZZLE_RADIUS * 1.18, NOZZLE_LENGTH + 0.02)], segments=40,
            location=(x, y, z))
        assign(n, "nozzle")
        shade_smooth_by_angle(n, math.radians(40))
        link(n, col)
        nozzles.append(n)

    centre = lathe("NozzleCentre", [
        (NOZZLE_RADIUS * 0.7, 0.0),
        (NOZZLE_RADIUS * 0.8, 0.5),
        (NOZZLE_RADIUS * 1.3, NOZZLE_LENGTH * 0.6),
        (NOZZLE_RADIUS * 1.55, NOZZLE_LENGTH),
    ], segments=40, location=(0, 0, -NOZZLE_LENGTH / 2 + 0.2))
    assign(centre, "nozzle")
    shade_smooth_by_angle(centre, math.radians(40))
    link(centre, col)
    nozzles.append(centre)
    return nozzles


def build_plume(col):
    """
    Engine exhaust, deliberately NOT part of the exported model.

    A cone of emissive material is a placeholder for an effect the client should draw
    -- it has no mass, no collider and no silhouette, and as a solid glowing object
    sitting off the stern it reads, in a plan view, as a separate lit thing flying in
    formation. Kept here so the shape is not lost, left out of the assembly.
    """
    """A short emissive cone, so a rendered ship reads as under power."""
    plume = lathe("Plume", [
        (NOZZLE_RADIUS * 1.1, 0.0),
        (NOZZLE_RADIUS * 1.0, -2.0),
        (NOZZLE_RADIUS * 0.75, -5.0),
        (0.0, -9.0),
    ], segments=40, location=(0, 0, -NOZZLE_LENGTH / 2 - 0.02))
    assign(plume, "glow")
    link(plume, col)
    return plume


def build_engine_bay(col):
    """The skirt: a shallow taper from the barrel down to the engine plane."""
    skirt = lathe("EngineSkirt", [
        (HULL_RADIUS * 0.86, -0.9),
        (HULL_RADIUS * 0.97, -0.35),
        (HULL_RADIUS * 1.005, 0.35),
        (HULL_RADIUS, 1.2),
    ], segments=96)
    assign(skirt, "steel_worn")
    shade_smooth_by_angle(skirt, math.radians(36))
    link(skirt, col)
    return skirt


# --------------------------------------------------------------------------- radiator


PANEL_BOOM = 0.55    # how far the panel stands off the barrel
PANEL_BOTTOM = 5.0   # where the panel starts, measured from the engine plane
PANEL_TOP_PAD = 15.0  # how far short of the forward band it stops


def build_radiator(col, index, angle):
    """
    One radiator panel, stowed flat against the barrel and swung out on a boom.

    The panel hangs vertically alongside the hull rather than sticking out sideways
    from it, and that is a geometric necessity rather than a preference: a 23.5 m
    panel bolted to the side of a 4.5 m barrel intersects it along its whole length
    unless it is set just clear of the surface, and setting it clear is what a real
    stowed array does.

    The outer 18 % runs hotter, which is where a real panel's temperature gradient
    puts the peak, and the stiffeners are there because a 46 m sheet of anything is
    not stiff enough to hold its own shape.
    """
    parts = []

    panel_length = BARREL_TOP - PANEL_BOTTOM - PANEL_TOP_PAD
    panel_bottom = PANEL_BOTTOM
    panel_centre_z = panel_bottom + panel_length / 2.0
    radius = HULL_RADIUS + PANEL_BOOM + PANEL_THICKNESS / 2.0

    # The panel: length up the hull, width out from it, thickness radial.
    panel = box(
        f"Panel{index}",
        (PANEL_THICKNESS, PANEL_WIDTH, panel_length),
        location=(radius, 0, panel_centre_z),
        rotation=(0, 0, angle))
    assign(panel, "radiator")
    link(panel, col)
    parts.append(panel)

    # The outward-facing half of the sheet, which is the hot end of the gradient.
    hot = box(
        f"PanelHot{index}",
        (PANEL_THICKNESS, PANEL_WIDTH * 0.5, panel_length),
        location=(radius, -PANEL_WIDTH * 0.25, panel_centre_z),
        rotation=(0, 0, angle))
    assign(hot, "radiator_hot")
    link(hot, col)
    parts.append(hot)

    # Three stiffeners across the panel, on the hull-facing face where a stiffener
    # actually goes: putting them on the radiating face would shade it.
    for j, z in enumerate((panel_bottom + panel_length * 0.2,
                           panel_centre_z,
                           panel_bottom + panel_length * 0.8)):
        rib = box(
            f"PanelRib{index}_{j}",
            (PANEL_THICKNESS * 3.0, PANEL_WIDTH, 0.5),
            location=(radius - PANEL_THICKNESS, 0, z),
            rotation=(0, 0, angle))
        assign(rib, "dark")
        link(rib, col)
        parts.append(rib)

    # Two short booms holding it off the barrel, at the forward and aft stiffeners.
    for j, z in enumerate((panel_bottom + panel_length * 0.2,
                           panel_bottom + panel_length * 0.8)):
        boom = box(
            f"Boom{index}_{j}",
            (PANEL_BOOM + PANEL_THICKNESS, 0.45, 0.45),
            location=(HULL_RADIUS + (PANEL_BOOM + PANEL_THICKNESS) / 2.0, 0, z),
            rotation=(0, 0, angle))
        assign(boom, "dark")
        link(boom, col)
        parts.append(boom)

    return parts


def build_radiators(col):
    """
    Two panels, opposed, covering the aft third of the barrel.

    Two and not four because at a tenth of a milligee there is very little area to
    stow and the panels are better hidden than displayed. They sit at 180 degrees so
    that one is always edge-on to the Sun and one always face-on, which is what a
    pair of opposed radiators is for, and they stop short of the forward section so
    the profile from the side is a clean barrel with a line down each flank.
    """
    parts = []
    for i in range(RADIATOR_PANELS):
        angle = math.pi * i
        parts.extend(build_radiator(col, i, angle))
    return parts


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
    col = collection("IlluminusCourier")
    build_materials()

    hull = build_hull(col)
    rings = build_rings(col)
    windows = build_windows(col)
    skirt = build_engine_bay(col)
    nozzles = build_nozzles(col)
    radiators = build_radiators(col)

    # Fixed parts into one mesh; the radiators stay separate because they move.
    hull_parts = join("IlluminusCourier_Hull", [hull] + rings + windows + [skirt])
    drive = join("IlluminusCourier_Drive", nozzles)
    panels = join("IlluminusCourier_Radiators", radiators)

    for obj in (hull_parts, drive, panels):
        apply_transform(obj, scale=True)

    # Everything is modelled nose-up about z; the engine plane becomes the origin.
    for obj in (hull_parts, drive, panels):
        obj.location.z -= 0.0

    blend, glb, preview = asset_paths("ships", NAME)

    # What the thermal budget asks for, beside what was actually built. A model is a
    # claim about a ship, and this is the line where the claim is checked.
    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = RADIATOR_PANELS * (BARREL_TOP - PANEL_BOTTOM - PANEL_TOP_PAD) * PANEL_WIDTH

    print(f"  hull      {HULL_LENGTH:.0f} m x {HULL_RADIUS * 2:.1f} m, {WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE} milligee")
    print(f"            {built * RADIATOR_AREAL_DENSITY / 1000.0:,.0f} t of {WET_MASS_T:,.0f} t "
          f"= {built * RADIATOR_AREAL_DENSITY / (WET_MASS_T * 1000.0) * 100:.1f} % of the ship")
    print(f"  drive     {NOZZLES + 1} magnetic nozzles at v_e = "
          f"{EXHAUST_VELOCITY / 1000:,.0f} km/s")

    # Framed to the whole ship plus its stowed radiators: 55 m of hull and a 25 m
    # beam needs about 150 m of standoff at this lens to sit inside the frame.
    render_views(preview, SHOTS, resolution=1100, samples=72)
    export_glb(glb, NAME)
    export_blend(blend)


main()
