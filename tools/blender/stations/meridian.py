"""
Meridian — the station the courier flies to.

    blender --background --python tools/blender/stations/meridian.py

A station is not a ship and must not look like one. A ship is a thing that goes
somewhere; a station is a place. Meridian is THE place: a two-kilometre wheel
turning at 0.95 rpm for one gravity at the rim, with a harbour inside the hub.
Capacity two hundred and thirty thousand, population forty — mostly empty, and
the emptiness is the setting. Old, neutral, Earth-built: the last great thing
Earth made. The reference is 2001 — elegant, monumental, a little dated.

THE LAYOUT, nose to reactor

  port funnel      z = 0          the small-craft dock the player flies to
  nose drum        20..70 m       antenna farm, traffic control
  harbour drum     70..640 m      an open bay round the core: 240 m across,
                                  twelve berths, mouth lights — the hub is a
                                  PLACE, and the mouth is how you tell
  wheel           710..980 m      two rings, 2 100 m across, 100 m tube,
                                  ten lit districts per ring and the rest dark
  boom            640..2 050 m    box truss through the wheel's centre
  radiators       1 000..1 400 m  four panels on two masts
  reactor         2 050..2 350 m  housing, shadow shield, end mast

WHAT THE DIMENSIONS TRACE TO

  ring radius    1 000 m    one gravity at 0.95 rpm: a = w^2 r, w = 0.099 rad/s
  overall        ~2 352 m   the figure the client frames this asset by
  radiators      192 000 m2 the station's whole thermal problem
  port           12 m bore, on the axis at the origin

The port is at the ORIGIN and the station extends along -z in Blender space.
Blender is z-up and the glTF exporter converts to y-up, so the client reads the
station long along -y with the port at the origin — which is exactly how the
client's StationTransform places it: the corridor axis is +x, a ship on the
approach comes in along -x, and the station's body lies on the far side of the
port. All node transforms are baked to identity before export, because the
client frames assets from transformed AABB corners and any transform left on a
node inflates the measure it frames by (the courier measured 79 m instead of 58
until the same bake was applied there).

ORIENTATION HISTORY, for the next person: an earlier version of this script
built the tori rotated and then applied a second rotation to the joined meshes
about an off-origin pivot. The composition happened to land close to the same
silhouette and the accident was never untangled; the measured 2 352 m the client
reports descends from it. This version builds every part directly in its final
frame — no post-rotation anywhere — and reproduces that measured figure
honestly: the wheel stays a 2 km wheel at 0.95 rpm (the rim-rate physics is not
negotiable) and the spindle carries the rest of the length, which is the
Discovery silhouette and is the right look for the last great thing Earth made.
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

NAME = "meridian"

# --------------------------------------------------------------------------- numbers

RING_RADIUS = 1000.0           # m to the habitat tube's centreline: a 2 km wheel
RING_TUBE = 50.0               # m radius: a 100 m tube, twelve decks at the rim
RINGS = 2                      # the double wheel. Two, because one is a hoop
RING_STATIONS = (-760.0, -930.0)  # z of each ring's plane
SPOKES = 6                     # per ring, from the hub wall out to the habitat
DISTRICTS = 10                 # lit window districts per ring; the rest is dark

HUB_RADIUS = 240.0             # the harbour drum, 480 m across
HUB_FACE = -70.0               # z of the harbour mouth
HUB_END = -640.0               # z of the aft bearing
CORE_RADIUS = 60.0             # the core the port sits on, through the bay
BAY_BERTHS = 12                # round the inside wall

PORT_BORE = 12.0               # m, the small-craft port at the very nose

BOOM_END = -2050.0             # z, where the reactor housing starts
RADIATOR_STATIONS = (-1050.0, -1250.0)
RADIATOR_PANELS = 4
RADIATOR_LENGTH = 320.0
RADIATOR_WIDTH = 150.0

END_MAST = -2338.0             # z, the comm mast that caps the spindle

# Colours, linear. Earth-built and old: painted white gone grey, structural dark,
# warning yellow where a pilot has to see an edge, and the city's windows warm.
HULL_WHITE = (0.62, 0.63, 0.62)
HULL_SHADOW = (0.19, 0.20, 0.21)
STRUCTURE = (0.30, 0.31, 0.32)
TRUSS = (0.24, 0.25, 0.26)
RADIATOR = (0.78, 0.79, 0.80)
RADIATOR_HOT = (0.30, 0.05, 0.02)
WARNING = (0.85, 0.55, 0.03)
WINDOW = (0.95, 0.86, 0.62)
BEACON = (0.9, 0.08, 0.05)

MATERIALS = {}


def build_materials():
    MATERIALS["hull"] = material(
        "MeridianHull", HULL_WHITE, metallic=0.1, roughness=0.55)
    MATERIALS["shadow"] = material(
        "MeridianShadow", HULL_SHADOW, metallic=0.2, roughness=0.6)
    MATERIALS["structure"] = material(
        "MeridianStructure", STRUCTURE, metallic=0.7, roughness=0.4)
    MATERIALS["truss"] = material(
        "MeridianTruss", TRUSS, metallic=0.8, roughness=0.45)
    MATERIALS["radiator"] = material(
        "MeridianRadiator", RADIATOR, metallic=0.15, roughness=0.25)
    # The hot face of a radiator, emissive. The client draws this with the emissive
    # term, so a station reads as running rather than as a model of a station.
    MATERIALS["radiator_hot"] = material(
        "MeridianRadiatorHot", RADIATOR_HOT, metallic=0.0, roughness=0.7,
        emission=(1.0, 0.22, 0.06), emission_strength=6.0)
    MATERIALS["warning"] = material(
        "MeridianWarning", WARNING, metallic=0.0, roughness=0.5)
    # The city's windows: warm, dim, and steady. A city at night does not flash.
    MATERIALS["window"] = material(
        "MeridianWindow", WINDOW, metallic=0.0, roughness=0.2,
        emission=(1.0, 0.88, 0.62), emission_strength=2.2)
    MATERIALS["beacon"] = material(
        "MeridianBeacon", BEACON, metallic=0.0, roughness=0.4,
        emission=(1.0, 0.1, 0.05), emission_strength=8.0)

    # The Cygnus convention, station register. The client flashes anything named
    # Nav or Strobe by schedule: reds and greens together at a hertz, whites and
    # the yellow on the same clock, strobes much faster and shorter. A station is
    # a dock target, and the convention is how the approach reads at night.
    MATERIALS["nav_red"] = material(
        "MeridianNavRed", (0.55, 0.02, 0.02), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.04, 0.03), emission_strength=1.0)
    MATERIALS["nav_green"] = material(
        "MeridianNavGreen", (0.02, 0.50, 0.06), metallic=0.0, roughness=0.35,
        emission=(0.05, 1.0, 0.12), emission_strength=1.0)
    MATERIALS["nav_white"] = material(
        "MeridianNavWhite", (0.70, 0.70, 0.68), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.98, 0.92), emission_strength=1.0)
    MATERIALS["nav_yellow"] = material(
        "MeridianNavYellow", (0.62, 0.50, 0.02), metallic=0.0, roughness=0.35,
        emission=(1.0, 0.78, 0.05), emission_strength=1.0)
    MATERIALS["strobe"] = material(
        "MeridianStrobe", (0.85, 0.85, 0.85), metallic=0.0, roughness=0.30,
        emission=(1.0, 1.0, 1.0), emission_strength=1.0)


def assign(obj, key):
    """Give an object one material, replacing whatever it has."""
    obj.data.materials.clear()
    obj.data.materials.append(MATERIALS[key])
    return obj


def lamp(col, parts, name, key, location, radius=0.8):
    """A lens in a dark housing, because a bare emissive point reads as an error.

    Appended to `parts` by the caller, and that is not decoration: the first
    version of this station created its hundred and fifty lamps WITHOUT joining
    them, the exporter wrote each as its own mesh, and the client's part count
    went from thirteen to a hundred and seventy — a draw call per light bulb.
    """
    housing = sphere(f"{name}_Housing", radius=radius * 1.6, location=location,
                     segments=16, rings=8)
    assign(housing, "structure")
    link(housing, col)
    lens = sphere(name, radius=radius, location=location, segments=16, rings=8)
    assign(lens, key)
    link(lens, col)
    parts.append(housing)
    parts.append(lens)
    return lens


# --------------------------------------------------------------------------- the port


def build_port(col):
    """
    The docking interface, on the axis at the origin, where a ship arrives.

    On the axis and NOT on the rim, which is the opposite of what a wheel suggests.
    A ship that docked at the rim would have to match a point moving at a hundred
    metres a second and then be lifted a kilometre up a spoke; a ship that docks on
    the spindle steps off into the hub and takes the lift out. Every real proposal
    does it this way and so does this one.
    """
    parts = []

    funnel = lathe("MeridianFunnel", [
        (PORT_BORE * 2.2, -8.0),
        (PORT_BORE * 1.6, -2.0),
        (PORT_BORE, 6.0),
        (PORT_BORE, 10.0),
    ], segments=64)
    parts.append(assign(funnel, "warning"))
    link(funnel, col)

    # The collar, which is what the latches take hold of.
    collar = cylinder("MeridianCollar", radius=PORT_BORE + 4.0, depth=12.0,
                      location=(0, 0, -12.0))
    parts.append(assign(collar, "structure"))
    link(collar, col)

    bore = cylinder("MeridianBore", radius=PORT_BORE - 2.0, depth=2.0,
                    location=(0, 0, -19.0))
    parts.append(assign(bore, "window"))
    link(bore, col)

    # Approach lights at the four cardinals, close round the bore. Four is enough
    # to read a roll and few enough to count at a glance.
    for i in range(4):
        angle = (i * math.tau / 4.0) + (math.pi / 4.0)
        lamp(col, parts, f"MeridianApproach{i}", "beacon",
             (math.cos(angle) * (PORT_BORE + 7.0),
              math.sin(angle) * (PORT_BORE + 7.0), -8.0), radius=1.4)

    return parts


# --------------------------------------------------------------------------- the nose


def build_nose(col):
    """
    The nose drum and its fittings: the core the port mounts on, the antenna farm,
    and the navigation lights a pilot reads on final.
    """
    parts = []

    drum = cylinder("MeridianNose", radius=CORE_RADIUS, depth=55.0,
                    location=(0, 0, -47.0), vertices=64)
    parts.append(assign(drum, "hull"))
    link(drum, col)

    cap = sphere("MeridianNoseCap", radius=CORE_RADIUS, location=(0, 0, -72.0),
                 segments=48, rings=24)
    parts.append(assign(cap, "hull"))
    link(cap, col)

    # The antenna farm: three masts and a radar dome, the only bits of the station
    # that are allowed to be untidy.
    for i, (x, y, h) in enumerate(((40.0, 25.0, 60.0), (-38.0, 30.0, 45.0),
                                   (10.0, -42.0, 52.0))):
        mast = cylinder(f"MeridianAntenna{i}", 1.2, h,
                        location=(x, y, -30.0 - h / 2.0), vertices=12)
        parts.append(assign(mast, "truss"))
        link(mast, col)
    dome = sphere("MeridianRadar", radius=9.0, location=(25.0, -20.0, -55.0),
                  segments=24, rings=12)
    parts.append(assign(dome, "structure"))
    link(dome, col)

    # The convention, on the nose where an approach sees it: reds to port, greens
    # to starboard, TWO whites on the roofline, ONE yellow underneath, strobes
    # above and below.
    lamp(col, parts, "NavPort", "nav_red", (0.0, -(CORE_RADIUS + 1.5), -30.0))
    lamp(col, parts, "NavPortAft", "nav_red", (0.0, -(CORE_RADIUS + 1.5), -60.0))
    lamp(col, parts, "NavStarboard", "nav_green", (0.0, CORE_RADIUS + 1.5, -30.0))
    lamp(col, parts, "NavStarboardAft", "nav_green", (0.0, CORE_RADIUS + 1.5, -60.0))
    lamp(col, parts, "NavDorsalFore", "nav_white", (CORE_RADIUS + 1.5, 0.0, -30.0))
    lamp(col, parts, "NavDorsalAft", "nav_white", (CORE_RADIUS + 1.5, 0.0, -60.0))
    lamp(col, parts, "NavVentral", "nav_yellow", (-(CORE_RADIUS + 1.5), 0.0, -45.0),
         radius=0.9)
    lamp(col, parts, "StrobeDorsal", "strobe", (CORE_RADIUS + 2.0, 0.0, -45.0),
         radius=0.6)
    lamp(col, parts, "StrobeVentral", "strobe", (-(CORE_RADIUS + 2.0), 0.0, -45.0),
         radius=0.6)

    return parts


# --------------------------------------------------------------------------- the harbour


def build_harbour(col):
    """
    The harbour: an open drum 480 m across with the bay between it and the core.

    THIS IS THE POINT OF THE STATION. A 2 km wheel encloses a void two kilometres
    wide, and the obvious thing to do with a void that size is to PARK IN IT. The
    drum's forward face is open from the core out to the wall, a ship flies in
    through the mouth and berths against the inside of the drum, and the cargo
    never crosses open vacuum. The alternative — docking on the outside of the
    hub — means every tonne crosses vacuum twice.

    The mouth has to READ as a mouth from five kilometres out, because a hole in
    space has no silhouette. So the lip is warning yellow, the rim carries sixteen
    strobes, and two rings of cabin lights run down the bay's length so the depth
    of the place is visible from the corridor.
    """
    parts = []

    # The drum shell: wall bands and longitudinal ribs, on the outside so the
    # inside stays clear for ships.
    for band in range(9):
        z = HUB_FACE - 20.0 - (band * ((HUB_FACE - HUB_END - 40.0) / 8.0))
        ring = torus(f"MeridianHubBand{band}", major=HUB_RADIUS, minor=5.0,
                     location=(0, 0, z), major_segments=128, minor_segments=10)
        parts.append(assign(ring, "structure"))
        link(ring, col)

    for rib in range(16):
        angle = rib * math.tau / 16.0
        spine = box(f"MeridianHubRib{rib}",
                    (5.0, 5.0, HUB_FACE - HUB_END - 20.0),
                    location=(math.cos(angle) * HUB_RADIUS,
                              math.sin(angle) * HUB_RADIUS,
                              (HUB_FACE + HUB_END) / 2.0 - 10.0))
        parts.append(assign(spine, "truss"))
        link(spine, col)

    # The drum's deck skin between the bands, so the wall reads as a wall.
    skin = cylinder("MeridianHubSkin", HUB_RADIUS - 2.0,
                    HUB_FACE - HUB_END - 30.0,
                    location=(0, 0, (HUB_FACE + HUB_END) / 2.0 - 15.0),
                    vertices=128, cap='NOTHING')
    parts.append(assign(skin, "hull"))
    link(skin, col)

    # The aft bulkhead, closing the bay behind the berths.
    bulkhead = cylinder("MeridianBulkhead", HUB_RADIUS - 2.0, 12.0,
                        location=(0, 0, HUB_END + 20.0), vertices=128)
    parts.append(assign(bulkhead, "shadow"))
    link(bulkhead, col)

    # The mouth lip: the edge a pilot judges the hole by.
    lip = torus("MeridianBayLip", major=HUB_RADIUS, minor=6.0,
                location=(0, 0, HUB_FACE), major_segments=128, minor_segments=12)
    parts.append(assign(lip, "warning"))
    link(lip, col)

    # Sixteen strobes round the mouth — the one light on the station that is
    # trying to be seen from far away.
    for i in range(16):
        angle = i * math.tau / 16.0
        lamp(col, parts, f"MeridianMouthStrobe{i}", "strobe",
             (math.cos(angle) * (HUB_RADIUS + 9.0),
              math.sin(angle) * (HUB_RADIUS + 9.0), HUB_FACE),
             radius=2.0)

    # The berths. Twelve cradles round the wall, each with a power trunk and a
    # pair of guide rails, spaced so a freighter fits between any two.
    bay_mid = (HUB_FACE + HUB_END) / 2.0 - 10.0
    for berth in range(BAY_BERTHS):
        angle = berth * math.tau / BAY_BERTHS
        cx = math.cos(angle)
        sy = math.sin(angle)

        cradle = box(f"MeridianBerth{berth}", (24.0, 50.0, 130.0),
                     location=(cx * (HUB_RADIUS - 30.0),
                               sy * (HUB_RADIUS - 30.0), bay_mid),
                     rotation=(0, 0, angle))
        parts.append(assign(cradle, "shadow"))
        link(cradle, col)

        trunk = box(f"MeridianBerthTrunk{berth}", (8.0, 8.0, 120.0),
                    location=(cx * (HUB_RADIUS - 8.0),
                              sy * (HUB_RADIUS - 8.0), bay_mid),
                    rotation=(0, 0, angle))
        parts.append(assign(trunk, "truss"))
        link(trunk, col)

        for rail in (-1, 1):
            offset = rail * 40.0
            guide = box(f"MeridianBerthRail{berth}_{rail}", (3.0, 3.0, 120.0),
                        location=(cx * (HUB_RADIUS - 12.0) - (sy * offset),
                                  sy * (HUB_RADIUS - 12.0) + (cx * offset),
                                  bay_mid),
                        rotation=(0, 0, angle))
            parts.append(assign(guide, "warning"))
            link(guide, col)

        # Berth lights: two lamps per cradle, warm and steady.
        for dz in (-40.0, 40.0):
            lamp(col, parts, f"MeridianBerthLight{berth}_{dz:+.0f}", "window",
                 (cx * (HUB_RADIUS - 14.0), sy * (HUB_RADIUS - 14.0),
                  bay_mid + dz), radius=1.6)

    # Two rings of cabin lights down the bay, so the depth of it reads from the
    # corridor: the mouth, a middle, and the far end.
    for ring_i, z in enumerate((-220.0, -440.0)):
        for i in range(12):
            angle = i * math.tau / 12.0 + ring_i * 0.26
            lamp(col, parts, f"MeridianBayLight{ring_i}_{i}", "window",
                 (math.cos(angle) * (HUB_RADIUS - 10.0),
                  math.sin(angle) * (HUB_RADIUS - 10.0), z), radius=2.2)

    # The core, running from the nose back through the bay to the aft bearing.
    core = cylinder("MeridianCore", CORE_RADIUS, -(HUB_END - 20.0) - 20.0,
                    location=(0, 0, (HUB_END + 20.0 - 20.0) / 2.0), vertices=64)
    parts.append(assign(core, "hull"))
    link(core, col)

    # The aft bearing housing, where the wheel meets the core: the single hardest
    # engineering problem on the station, and it is allowed to look like it.
    bearing = cylinder("MeridianBearing", CORE_RADIUS + 40.0, 80.0,
                       location=(0, 0, HUB_END - 30.0), vertices=96)
    parts.append(assign(bearing, "structure"))
    link(bearing, col)

    return parts


# --------------------------------------------------------------------------- the wheel


def build_spoke(col, wheel, spoke, angle):
    """One spoke: a box-truss arm from the hub wall to the ring, with a lift car."""
    parts = []
    inner = HUB_RADIUS + 10.0
    outer = RING_RADIUS - RING_TUBE - 4.0
    length = outer - inner
    z = RING_STATIONS[wheel]
    cx = math.cos(angle)
    sy = math.sin(angle)

    for offset in (-6.0, 6.0):
        rail = box(f"MeridianSpokeRail{wheel}_{spoke}_{offset:+.0f}",
                   (length, 2.0, 2.0),
                   location=(cx * (inner + length / 2.0) - (sy * offset),
                             sy * (inner + length / 2.0) + (cx * offset), z),
                   rotation=(0, 0, angle))
        parts.append(assign(rail, "truss"))
        link(rail, col)

    for i in range(9):
        along = inner + length * (i + 0.5) / 9.0
        brace = box(f"MeridianSpokeBrace{wheel}_{spoke}_{i}",
                    (2.0, 12.0, 2.0),
                    location=(cx * along, sy * along, z),
                    rotation=(0, 0, angle))
        parts.append(assign(brace, "truss"))
        link(brace, col)

    # A lift car on each spoke: the only thing on the ring that moves relative to
    # it, and therefore the only thing that needs a marking on it.
    car = box(f"MeridianCar{wheel}_{spoke}", (26.0, 30.0, 22.0),
              location=(cx * (inner + length * 0.6),
                        sy * (inner + length * 0.6), z),
              rotation=(0, 0, angle))
    parts.append(assign(car, "warning"))
    link(car, col)

    return parts


def build_districts(col, wheel):
    """
    The city at night: ten lit districts on each ring's outer face, and the rest
    of the rim dark.

    The station was built for two hundred and thirty thousand and forty came, so
    most of the ring is unlit. A district is a small grid of warm windows; between
    them the rim is hull. From ten kilometres out that reads as a place that is
    inhabited in patches — the alternative, a continuous ring of light, is the
    dotted bracelet the earlier model drew, which read as a broken array rather
    than as a city.
    """
    parts = []
    z = RING_STATIONS[wheel]
    face = RING_RADIUS + RING_TUBE - 0.5

    for district in range(DISTRICTS):
        centre = district * math.tau / DISTRICTS + (wheel * 0.31)
        for row in (-1, 0, 1):
            for col_i in range(5):
                angle = centre + (col_i - 2) * 0.010
                w = box(f"MeridianWindow{wheel}_{district}_{row}_{col_i}",
                        (7.0, 3.0, 1.2),
                        location=(math.cos(angle) * face,
                                  math.sin(angle) * face,
                                  z + (row * 7.0)),
                        rotation=(0, 0, angle))
                parts.append(assign(w, "window"))
                link(w, col)

    # Scattered singles, so the dark quarters are not uniformly dead.
    for i in range(24):
        angle = (i * math.tau / 24.0) + 0.13 + (wheel * 0.31)
        w = box(f"MeridianWindowS{wheel}_{i}", (5.0, 2.5, 1.2),
                location=(math.cos(angle) * face,
                          math.sin(angle) * face, z + (i % 3 - 1) * 6.0),
                rotation=(0, 0, angle))
        parts.append(assign(w, "window"))
        link(w, col)

    return parts


def build_wheel(col):
    """
    The habitat: two rings, their spokes, the lit districts, and the rim bays.

    ONE GRAVITY, which is worth the rotation rate. At a kilometre the full g needs
    0.95 revolutions a minute, the rim moves at a hundred metres a second, and a
    person standing on the outer deck weighs exactly what they weighed on Earth.
    That is the figure the whole station exists to make, and it is printed below.
    """
    parts = []

    for wheel in range(RINGS):
        z = RING_STATIONS[wheel]

        ring = torus(f"MeridianRing{wheel}", major=RING_RADIUS, minor=RING_TUBE,
                     location=(0, 0, z), major_segments=192, minor_segments=28)
        parts.append(assign(ring, "hull"))
        link(ring, col)

        # Structural bands round the drum, so the tube reads as built and not grown.
        # A band round the tube is a SMALL torus of the tube's own radius, centred
        # on the drum's centreline with its axis along the drum's tangent — the
        # first version of this gave the bands the ring's own major radius, which
        # is how the wheel became a polygonal cage in every preview.
        for band in range(12):
            angle = band * math.tau / 12.0
            rib = torus(f"MeridianRimBand{wheel}_{band}",
                        major=RING_TUBE + 0.8, minor=2.2,
                        location=(math.cos(angle) * RING_RADIUS,
                                  math.sin(angle) * RING_RADIUS, z),
                        major_segments=48, minor_segments=8)
            rib.rotation_euler = (math.pi / 2, 0.0, angle + math.pi / 2)
            parts.append(assign(rib, "structure"))
            link(rib, col)

        parts.extend(build_districts(col, wheel))

        # Two docking bays on each ring's flanks: freight berths that do not
        # deserve the harbour. A recess box, a warning rim, four guide lamps.
        for bay_i, angle in enumerate((0.6, 3.6)):
            cx = math.cos(angle)
            sy = math.sin(angle)
            for face in (-1, 1):
                bay = box(f"MeridianRimBay{wheel}_{bay_i}_{face:+d}",
                          (36.0, 24.0, 18.0),
                          location=(cx * (RING_RADIUS - RING_TUBE + 10.0),
                                    sy * (RING_RADIUS - RING_TUBE + 10.0),
                                    z + face * (RING_TUBE + 8.0)),
                          rotation=(0, 0, angle))
                parts.append(assign(bay, "shadow"))
                link(bay, col)

                rim = box(f"MeridianRimBayRim{wheel}_{bay_i}_{face:+d}",
                          (40.0, 3.0, 22.0),
                          location=(cx * (RING_RADIUS - RING_TUBE + 10.0),
                                    sy * (RING_RADIUS - RING_TUBE + 10.0),
                                    z + face * (RING_TUBE + 17.0)),
                          rotation=(0, 0, angle))
                parts.append(assign(rim, "warning"))
                link(rim, col)

        for spoke in range(SPOKES):
            parts.extend(build_spoke(col, wheel, spoke,
                                     spoke * math.tau / SPOKES
                                     + wheel * (math.tau / 12.0)))

    return parts


# --------------------------------------------------------------------------- the boom


def build_boom(col):
    """
    The boom: what holds the reactor and the radiators clear of the wheel.

    A radiator shadows whatever is behind it and a reactor irradiates whatever is
    near it, so both want distance. The boom is a box truss running from the aft
    bearing through the wheel's centre to the reactor, and it is the only part of
    Meridian that is pure structure rather than a place.
    """
    parts = []
    z0, z1 = HUB_END - 40.0, BOOM_END
    length = z0 - z1
    half = 14.0
    step = 90.0

    for x in (-half, half):
        for y in (-half, half):
            rail = box("MeridianLongeron", (4.0, 4.0, length),
                       location=(x, y, (z0 + z1) / 2.0))
            parts.append(assign(rail, "truss"))
            link(rail, col)

    bays = int(length / step)
    for i in range(bays + 1):
        z = z0 - length * i / bays
        for y in (-half, half):
            post = box("MeridianBoomPostX", (2 * half, 2.0, 2.0),
                       location=(0, y, z))
            parts.append(assign(post, "truss"))
            link(post, col)
        for x in (-half, half):
            post = box("MeridianBoomPostY", (2.0, 2 * half, 2.0),
                       location=(x, 0, z))
            parts.append(assign(post, "truss"))
            link(post, col)

    for i in range(bays):
        za = z0 - length * i / bays
        zb = z0 - length * (i + 1) / bays
        dz = za - zb
        span = 2 * half
        diag = math.hypot(span, dz)
        angle = math.atan2(dz, span)
        for y in (-half, half):
            d = box("MeridianBoomDiagX", (diag, 1.6, 1.6),
                    location=(0, y, (za + zb) / 2.0),
                    rotation=(0, -angle * (1 if i % 2 == 0 else -1), 0))
            parts.append(assign(d, "truss"))
            link(d, col)
        for x in (-half, half):
            d = box("MeridianBoomDiagY", (1.6, diag, 1.6),
                    location=(x, 0, (za + zb) / 2.0),
                    rotation=(angle * (1 if i % 2 == 0 else -1), 0, 0))
            parts.append(assign(d, "truss"))
            link(d, col)

    return parts


def build_radiators(col):
    """
    The main radiators: four panels on two masts amid the boom.

    A station's radiator is sized by what it has to reject and it cannot be
    stowed, so it is simply there. The panels stand edge-on to the spindle so
    each sees sky in both directions, and the hot face is a thin skin proud of
    each side rather than a second slab inside the panel — two coplanar surfaces
    fight, and the ship renderers already lost that lottery once.
    """
    parts = []

    for mast_i, z in enumerate(RADIATOR_STATIONS):
        for side in (-1, 1):
            mast = box(f"MeridianMast{mast_i}_{side:+d}",
                       (14.0, 130.0, 14.0),
                       location=(0, side * 78.0, z))
            parts.append(assign(mast, "truss"))
            link(mast, col)

            face = box(f"MeridianPanel{mast_i}_{side:+d}",
                       (RADIATOR_WIDTH, RADIATOR_LENGTH, 2.0),
                       location=(0, side * (150.0 + RADIATOR_LENGTH / 2.0), z))
            parts.append(assign(face, "radiator"))
            link(face, col)

            for f in (-1, 1):
                hot = box(f"MeridianPanelHot{mast_i}_{side:+d}_{f:+d}",
                          (RADIATOR_WIDTH - 14.0, RADIATOR_LENGTH - 14.0, 0.6),
                          location=(0, side * (150.0 + RADIATOR_LENGTH / 2.0),
                                    z + f * 1.4))
                parts.append(assign(hot, "radiator_hot"))
                link(hot, col)

            for rib in range(12):
                along = 150.0 + 14.0 + (rib * 25.0)
                spar = box(f"MeridianPanelRib{mast_i}_{side:+d}_{rib}",
                           (RADIATOR_WIDTH, 3.0, 5.0),
                           location=(0, side * along, z))
                parts.append(assign(spar, "structure"))
                link(spar, col)

    return parts


def build_reactor(col):
    """
    The reactor housing, at the far end, and the end mast that caps the spindle.

    The reactor is a long way from the people, which is the oldest rule in
    spacecraft design. The shadow shield is a disc between it and the wheel, and
    it is the single most massive thing on the station.
    """
    parts = []

    housing = cylinder("MeridianReactor", radius=34.0, depth=140.0,
                       location=(0, 0, BOOM_END - 70.0), vertices=64)
    parts.append(assign(housing, "shadow"))
    link(housing, col)

    shield = cylinder("MeridianShield", radius=66.0, depth=9.0,
                      location=(0, 0, BOOM_END + 24.0), vertices=64)
    parts.append(assign(shield, "structure"))
    link(shield, col)

    for vane in range(6):
        angle = vane * math.tau / 6.0
        fin = box(f"MeridianReactorFin{vane}", (7.0, 30.0, 110.0),
                  location=(math.cos(angle) * 56.0, math.sin(angle) * 56.0,
                          BOOM_END - 70.0),
                  rotation=(0, 0, angle))
        parts.append(assign(fin, "radiator"))
        link(fin, col)

    # The end mast: the spindle's full stop, and the thing that brings the
    # measured overall to the figure the client frames this asset by.
    mast = cylinder("MeridianEndMast", 3.0, -(END_MAST - (BOOM_END - 140.0)),
                    location=(0, 0, (END_MAST + BOOM_END - 140.0) / 2.0),
                    vertices=12)
    parts.append(assign(mast, "truss"))
    link(mast, col)

    lamp(col, parts, "StrobeAft", "strobe", (0.0, 0.0, END_MAST - 4.0), radius=1.4)

    return parts


# --------------------------------------------------------------------------- views
# Six angles, because no single view contains the shape: the wheel is 2.1 km
# across and the spindle is 2.35 km long. The port shot is close and on the axis
# because that is what a pilot sees for the last kilometre, and it is the view
# the model most has to work in.
SHOTS = [
    ("hero",   38.0,  16.0, 1.9, 52.0),
    ("port",  180.0,   6.0, 2.2, 55.0),
    ("ring",   90.0,  28.0, 1.3, 45.0),
    ("aft",      0.0,  20.0, 1.6, 48.0),
    ("side",   90.0,   2.0, 2.0, 52.0),
    ("above",  55.0,  62.0, 1.9, 52.0),
]


def main():
    reset()
    col = collection("Meridian")
    build_materials()

    port = build_port(col)
    nose = build_nose(col)
    harbour = build_harbour(col)
    wheel = build_wheel(col)
    boom = build_boom(col)
    radiators = build_radiators(col)
    reactor = build_reactor(col)

    # THE HUB DOES NOT TURN AND THE WHEEL DOES. They are two objects because they
    # are two objects: a client that wants to show the station running has to be
    # able to turn one against the other, and a part that moves should not be
    # welded in.
    body = join("Meridian_Hub",
                port + nose + harbour + boom + radiators + reactor)
    habitat = join("Meridian_Wheel", wheel)

    # Bake every node transform into the vertices, so the exported nodes are all
    # identity. The client frames a hull from the TRANSFORMED CORNERS of each
    # part's axis-aligned box, and a transform left on a node inflates the measure
    # it frames by — and on a station, the framing IS the contract.
    for obj in (body, habitat):
        apply_transform(obj, location=True, rotation=True, scale=True)
        shade_smooth_by_angle(obj, math.radians(35))

    blend, glb, preview = asset_paths("stations", NAME)

    # What the design asks for, beside what was built. A model is a claim about a
    # place, and this is the line where the claim gets checked.
    rate = math.sqrt(9.80665 / RING_RADIUS)
    rpm = rate * 60.0 / math.tau
    speed = rate * RING_RADIUS

    print(f"  wheel     {RINGS} rings, {2 * (RING_RADIUS + RING_TUBE):.0f} m across, "
          f"{2 * RING_TUBE:.0f} m tube")
    print(f"  gravity   {rpm:.2f} rpm at {RING_RADIUS:.0f} m = {speed:.1f} m/s rim speed"
          f" = 1.00 g")
    print(f"  harbour   {HUB_RADIUS * 2:.0f} m drum, {BAY_BERTHS} berths, "
          f"mouth at z = {HUB_FACE:.0f} m")
    print(f"  spindle   {-(END_MAST - 14.0):.0f} m nose to end mast, "
          f"{RADIATOR_PANELS * RADIATOR_LENGTH * RADIATOR_WIDTH:,.0f} m2 of radiator")

    render_views(preview, SHOTS, resolution=1100, samples=64)

    # The harbour mouth, from the corridor, at the range a pilot judges it at.
    # render_views always frames the whole station, and the mouth is the one
    # detail that has to work on its own.
    from pipeline import render_preview
    render_preview(preview.replace(".png", "_mouth.png"),
                   target=(0.0, 0.0, HUB_FACE - 110.0), distance=800.0,
                   azimuth=15.0, elevation=30.0, resolution=1100, samples=64)

    export_glb(glb, NAME)
    export_blend(blend)


main()
