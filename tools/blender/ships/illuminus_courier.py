"""
The Illuminus courier — a Starship descendant, in the Cathedral pattern.

Design language (§6.5 of DESIGN.md): futuristic, gleaming, stylised, intimidating.
Smooth hulls, long unbroken curves, few visible seams, no obvious machinery. The
people who own these are showing off, because a shell is inherited property and a
statement of rank. Hard edges and high contrast: the aesthetic of something that has
never been rained on.

The silhouette is a Starship's and the ancestry is meant to be legible — one long
stainless cylinder, a domed forward section, a nose that is a curve rather than a
cone, and an engine bay crowded with nozzles. The Cathedral pattern keeps all of
that and adds its rank through scale cues, because a ship this old is entitled to
buttresses:

  * The weld rings stay. On a barrel this old the seams are not hidden, they are
    pointed at — three structural rings and the forward band, read as buttresses.
  * A long dorsal keel fin runs the spine like a nave roof. From the beam it
    doubles the ship's plan; from below it is the thing you sail under.
  * The engine bay is presented as a crown: a flared collar with twelve merlons
    between the twelve magnetic nozzles, so the drive reads as a circlet of points
    rather than as plumbing — which is what a docking ship should remember looking
    up at.
  * The radiator is four articulated blanket wings at the aft third, opened like a
    flower. An earlier draft made them four monolithic trapezoids and they read as
    missile fins, which is the one thing a ship that has never been rained on is
    not. Missile fins are solid; engineered arrays come in blankets — so each wing
    is four segments on a spar on a deployment boom, with the hot faces out.

What has changed in a century and a half is everything the physics forced: the
bells are magnetic nozzles, not combustion chambers (long, barely-tapered throats
to give the plasma somewhere to finish expanding), and the radiator is sized by
the thermal budget, not by taste — see the numbers below, every one of which
traces to docs/TRIP-ENERGY.md §16.

STOWAGE: each blanket folds about its spar root and lies flat against the aft
barrel, segments stacked, inside the crown collar's flare. The Radiators node is
exported DEPLOYED — 55 degrees off the barrel — because the ship in the client is
under power in flight. A deployment animation rotates each wing about its spar
line, from 0 degrees (flat) to the 55 shown here.

    blender --background --python tools/blender/ships/illuminus_courier.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, asset_paths, bevel, box, cylinder, export_blend, export_glb,
    join, lathe, link, material, collection, render_views, reset,
    ring_of, shade_smooth_by_angle, sphere, torus,
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
# For 1 320 t at 0.1 milligee that is 729 m2 and 6 t. The array built below is four
# blanket wings of four 6.5 x 8.0 m segments each: 832 m2, which is the stowage
# plan for it -- two facts, one geometry.
RADIATOR_LINES = 4             # blanket wings, at the intercardinal angles
RADIATOR_SEGMENTS = 4          # panels per wing -- the articulation that kills the fin
SEGMENT_LENGTH = 6.5
SEGMENT_WIDTH = 8.0
PANEL_THICKNESS = 0.16
RADIATOR_TILT = 55.0           # degrees off the barrel, deployed
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
    # ---------------------------------------------------------------- navigation lights
    #
    # THE CYGNUS CONVENTION. Written up in full in DESIGN.md section 6.6 — read that
    # before changing anything here, and carry all five lights onto any new hull.
    #
    # A boat carries red to port, green to starboard and white fore and aft, and that is
    # enough at sea because the sea is a plane and gravity keeps every hull in it the right
    # way up. Neither is true in space. A spacecraft can be inverted or rolled, and
    # red-and-green alone leaves the most important question unanswered: which way is that
    # thing's roof pointing.
    #
    # The convention that answers it was developed by ORBITEC for the Cygnus cargo vehicle
    # in 2011 and is the first LED navigation system flown on a spacecraft. Cygnus carries:
    #
    #     PORT        one flashing RED
    #     STARBOARD   one flashing GREEN
    #     DORSAL      TWO flashing WHITE
    #     VENTRAL     ONE flashing YELLOW
    #
    # Two white above and one yellow below, and the COUNT is the point. A pilot who can see
    # two lights knows they are on top of the ship and one means underneath, and that works
    # even if the colours are washed out or the observer is colour-blind -- which matters,
    # because about one man in twelve is. SpaceX's Dragon carries the same red and green
    # plus a white strobe.
    #
    # This is the scheme used here, unaltered. A hull 55 m long carries its reds and greens
    # in pairs so that its LENGTH reads as well as its heading; the two whites and the single
    # yellow are left exactly as the convention specifies, because the count is the message.
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

    # The anti-collision strobe, which Dragon carries alongside its red and green. Much
    # brighter than the rest and meant to be seen before anything else.
    MATERIALS["strobe"] = material(
        "IlluminusStrobe", (0.85, 0.85, 0.85), metallic=0.0, roughness=0.30,
        emission=(1.0, 1.0, 1.0), emission_strength=1.0)

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

    hull = lathe("Hull", barrel + nose, segments=96)
    assign(hull, "steel")
    shade_smooth_by_angle(hull, math.radians(38))
    link(hull, col)
    return hull


def build_rings(col):
    """
    Weld lines and structural rings.

    The brief says few visible seams, and the Cathedral answer is that the seams
    stay and are pointed at: on a barrel this old they are buttresses, and hiding
    them would be pretending the ship is younger than it is. Three structural rings
    plus a raised band at the forward dome.
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


def prism(name, profile, thickness, key, col):
    """
    A flat blade extruded from a side profile. The pipeline has a lathe but no
    prism, and a keel fin is exactly a side profile with a thickness.
    """
    n = len(profile)
    verts = ([(x, -thickness / 2.0, z) for x, z in profile]
             + [(x, thickness / 2.0, z) for x, z in profile])
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    assign(obj, key)
    link(obj, col)
    bevel(obj, 0.12, segments=3)
    return obj


def build_keel(col):
    """
    The dorsal keel fin: a blade from the aft third to the forward dome, rising
    four metres clear of the barrel. From the beam it doubles the ship's plan;
    from below it is the thing you sail under. It is also the roofline, and the
    dorsal navigation lights ride it — see build_navigation_lights.
    """
    profile = [
        (4.15, 6.0),
        (5.00, 13.0),
        (8.40, 26.0),
        (8.60, 34.0),
        (5.40, 46.0),
        (4.35, 50.0),
    ]
    return prism("KeelFin", profile, 0.55, "steel", col)


def build_navigation_lights(col):
    """
    The navigation lights, to the Cygnus convention. See the note over the materials.

    Each is a dark housing with a lens in it, because an emissive patch with nothing around
    it reads as a texture error and a lens in a fitting reads as a lamp. They are all named,
    because the client FLASHES them — every light in the convention flashes, and a steady
    one is not the convention.
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

    # PORT: red. FORWARD and AFT on the beam, so the hull's length reads as well as its
    # heading — one light on a 55 m hull says which side you are on and nothing about how
    # much ship there is.
    lamp("NavPort", "nav_red", (0.0, -(HULL_RADIUS + 0.4), 33.0))
    lamp("NavPortAft", "nav_red", (0.0, -(HULL_RADIUS + 0.4), 9.0))

    # STARBOARD: green, the same two stations.
    lamp("NavStarboard", "nav_green", (0.0, HULL_RADIUS + 0.4, 33.0))
    lamp("NavStarboardAft", "nav_green", (0.0, HULL_RADIUS + 0.4, 9.0))

    # DORSAL: TWO white. The count is the message, so there are exactly two — and on
    # this hull they ride the keel's outboard edge, the highest line on the ship.
    lamp("NavDorsalFore", "nav_white", (8.80, 0.0, 30.5))
    lamp("NavDorsalAft", "nav_white", (6.75, 0.0, 40.0))

    # VENTRAL: ONE yellow. Not two. That asymmetry with the roof is the whole mechanism.
    lamp("NavVentral", "nav_yellow", (-(HULL_RADIUS + 0.4), 0.0, 23.0), radius=0.42)

    # The anti-collision strobes, one at the keel's peak and one under the dome.
    lamp("StrobeDorsal", "strobe", (8.90, 0.0, 34.2), radius=0.30)
    lamp("StrobeVentral", "strobe", (-(HULL_RADIUS + 0.6), 0.0, 45.0), radius=0.30)

    return lights


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


def build_crown(col):
    """
    The engine bay as a crown: a flared collar round the engine plane, and twelve
    merlons standing between the twelve nozzles, so the drive presents as a circlet
    of points rather than as plumbing.
    """
    parts = []

    collar = lathe("CrownCollar", [
        (HULL_RADIUS * 0.99, 1.8),
        (HULL_RADIUS * 1.18, 0.5),
        (HULL_RADIUS * 1.27, -0.4),
        (HULL_RADIUS * 1.28, -0.8),
    ], segments=96)
    assign(collar, "steel_worn")
    shade_smooth_by_angle(collar, math.radians(34))
    link(collar, col)
    parts.append(collar)

    for i, (x, y, z) in enumerate(ring_of(NOZZLES, 5.35, z=-1.2, phase=math.pi / 12)):
        angle = math.pi / 12 + 2.0 * math.pi * i / NOZZLES
        merlon = box(f"Merlon{i}", (0.32, 0.75, 3.2), location=(x, y, z),
                     rotation=(0, 0, angle))
        assign(merlon, "dark")
        link(merlon, col)
        parts.append(merlon)

    return parts


# --------------------------------------------------------------------------- radiator


def panel_mesh(name, root_w, tip_w, length, thick, location, rotation):
    """
    A trapezoidal panel: `root_w` across at the local -z end, `tip_w` at +z. The
    pipeline's box cannot taper, and the outer segment of a blanket wants a shaped
    tip rather than a square one.
    """
    x = thick / 2.0
    rw, tw, L = root_w / 2.0, tip_w / 2.0, length / 2.0
    verts = [
        (-x, -rw, -L), (x, -rw, -L), (-x, rw, -L), (x, rw, -L),
        (-x, -tw, L), (x, -tw, L), (-x, tw, L), (x, tw, L),
    ]
    faces = [
        (0, 4, 6, 2),
        (1, 3, 7, 5),
        (0, 1, 5, 4),
        (2, 6, 7, 3),
        (0, 2, 3, 1),
        (4, 5, 7, 6),
    ]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    obj.rotation_euler = rotation
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_blanket(col, index, phi_deg):
    """
    One radiator blanket wing, deployed.

    A boom lifts a spar clear of the barrel at one of the intercardinal angles, and
    four segments hang off the spar in a row with gaps between them, tilted out to
    RADIATOR_TILT. The articulation is the whole of the anti-fin: missile fins are
    monolithic and a radiator that reads as a fin reads as a weapon, so the wing
    comes in panels on a spar, the way engineered arrays have always come.
    """
    parts = []

    phi = math.radians(phi_deg)
    tilt = math.radians(RADIATOR_TILT)
    n = Vector((math.cos(phi), math.sin(phi), 0.0))
    t = Vector((-math.sin(phi), math.cos(phi), 0.0))

    # The span runs aft and outward from the root; the hot face normal follows it,
    # so the hot faces point out and forward, to space — never at the hull or at
    # each other.
    d = (n * math.sin(tilt) + Vector((0, 0, -math.cos(tilt)))).normalized()
    m = (n * math.cos(tilt) + Vector((0, 0, math.sin(tilt)))).normalized()
    rotation = Matrix((m, t, d)).transposed().to_euler()

    # The deployment boom: the fitting the wing folds about, visible in every view.
    boom_centre = n * (HULL_RADIUS + 0.75)
    boom_centre.z = 24.0
    boom = box(f"Boom{index}", (1.7, 0.45, 0.45), location=boom_centre,
               rotation=(0, 0, phi))
    assign(boom, "dark")
    link(boom, col)
    parts.append(boom)

    root = n * (HULL_RADIUS + 1.6)
    root.z = 24.0

    span = RADIATOR_SEGMENTS * SEGMENT_LENGTH
    spar = box(f"Spar{index}", (0.35, 0.35, span),
               location=root + d * (span / 2.0), rotation=rotation)
    assign(spar, "dark")
    link(spar, col)
    parts.append(spar)

    gap = 0.15
    for k in range(RADIATOR_SEGMENTS):
        seg_centre = root + d * ((k + 0.5) * SEGMENT_LENGTH + k * gap)

        # The outermost segment carries a shaped tip; the rest are square.
        if k == RADIATOR_SEGMENTS - 1:
            panel = panel_mesh(f"Blanket{index}_{k}", SEGMENT_WIDTH,
                               SEGMENT_WIDTH * 0.9, SEGMENT_LENGTH - gap,
                               PANEL_THICKNESS, seg_centre, rotation)
            hot = panel_mesh(f"BlanketHot{index}_{k}", SEGMENT_WIDTH * 0.94,
                             SEGMENT_WIDTH * 0.9 * 0.94, SEGMENT_LENGTH - gap,
                             0.02, seg_centre + m * (PANEL_THICKNESS / 2 + 0.012),
                             rotation)
        else:
            panel = box(f"Blanket{index}_{k}",
                        (PANEL_THICKNESS, SEGMENT_WIDTH, SEGMENT_LENGTH - gap),
                        location=seg_centre, rotation=rotation)
            hot = box(f"BlanketHot{index}_{k}",
                      (0.02, SEGMENT_WIDTH * 0.94, SEGMENT_LENGTH - gap),
                      location=seg_centre + m * (PANEL_THICKNESS / 2 + 0.012),
                      rotation=rotation)

        assign(panel, "radiator")
        link(panel, col)
        parts.append(panel)
        assign(hot, "radiator_hot")
        link(hot, col)
        parts.append(hot)

        # One stiffener per segment, on the shaded face where a stiffener goes.
        rib = box(f"BlanketRib{index}_{k}", (0.08, SEGMENT_WIDTH, 0.4),
                  location=(seg_centre - m * (PANEL_THICKNESS / 2 + 0.05)
                            - d * (SEGMENT_LENGTH * 0.35)),
                  rotation=rotation)
        assign(rib, "dark")
        link(rib, col)
        parts.append(rib)

    return parts


def build_radiators(col):
    """
    Four blanket wings at the intercardinal angles, opened like a flower round the
    corridor a docking ship flies down. Intercardinal, so that from dead astern the
    array is a rose window and from the beam it is two wings, never a cruciform tail.
    """
    parts = []
    for i, phi in enumerate((45.0, 135.0, 225.0, 315.0)):
        parts.extend(build_blanket(col, i, phi))
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
    keel = build_keel(col)
    windows = build_windows(col)
    crown = build_crown(col)
    nozzles = build_nozzles(col)
    radiators = build_radiators(col)
    lights = build_navigation_lights(col)

    # Fixed parts into one mesh; the radiators stay separate because they move.
    hull_parts = join("IlluminusCourier_Hull",
                      [hull] + rings + [keel] + windows + crown + lights)
    drive = join("IlluminusCourier_Drive", nozzles)
    panels = join("IlluminusCourier_Radiators", radiators)

    # Bake every node transform into the vertices, so the exported nodes are all
    # identity. Two reasons, one of each kind:
    #
    # The deployment note: the joined Radiators object otherwise inherits the first
    # boom's hinge frame as its node transform -- and that frame is the WRONG pivot
    # for animation anyway, because four blanket wings fold about four different
    # spar lines, not one node's origin. The pivots a deployment animation wants
    # are the four spar roots, which are geometry positions, not node transforms.
    #
    # The measured-size note: the client frames a hull from the TRANSFORMED CORNERS
    # of each part's axis-aligned box, and a box rotated 45 degrees inflates by
    # root two. With the hinge frame left on the node the client measured this
    # ship 79 m long instead of 58 -- and the cockpit camera keys its standoff off
    # that length, so it parked itself twenty metres further up the nose than the
    # nose is.
    for obj in (hull_parts, drive, panels):
        apply_transform(obj, location=True, rotation=True, scale=True)

    blend, glb, preview = asset_paths("ships", NAME)

    # What the thermal budget asks for, beside what was actually built. A model is a
    # claim about a ship, and this is the line where the claim is checked.
    sigma = 5.670374419e-8
    jet_per_kg = ACCELERATION * EXHAUST_VELOCITY / 2.0
    area_per_kg = (jet_per_kg * (1.0 - RADIATOR_EFFICIENCY) / RADIATOR_EFFICIENCY
                   / (2.0 * sigma * RADIATOR_TEMPERATURE ** 4))
    needed = area_per_kg * WET_MASS_T * 1000.0
    built = (RADIATOR_LINES * RADIATOR_SEGMENTS
             * (SEGMENT_LENGTH - 0.15) * SEGMENT_WIDTH)

    print(f"  hull      {HULL_LENGTH:.0f} m x {HULL_RADIUS * 2:.1f} m, {WET_MASS_T:,.0f} t wet")
    print(f"  radiator  {built:,.0f} m2 built against {needed:,.0f} m2 needed at "
          f"{CRUISE_MILLIGEE} milligee")
    print(f"            {built * RADIATOR_AREAL_DENSITY / 1000.0:,.0f} t of {WET_MASS_T:,.0f} t "
          f"= {built * RADIATOR_AREAL_DENSITY / (WET_MASS_T * 1000.0) * 100:.1f} % of the ship")
    print(f"            {RADIATOR_LINES} blanket wings of {RADIATOR_SEGMENTS} segments, "
          f"deployed at {RADIATOR_TILT:.0f} degrees")
    print(f"  drive     {NOZZLES + 1} magnetic nozzles at v_e = "
          f"{EXHAUST_VELOCITY / 1000:,.0f} km/s")

    # Framed to the whole ship plus its deployed array: 55 m of hull and a 55 m
    # flower needs about 160 m of standoff at this lens to sit inside the frame.
    render_views(preview, SHOTS, resolution=1100, samples=72)
    export_glb(glb, NAME)
    export_blend(blend)


main()
