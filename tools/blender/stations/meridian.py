"""
Meridian — the station the courier flies to.

    blender --background --python tools/blender/stations/meridian.py

A station is not a ship and must not look like one. A ship is a thing that goes somewhere, so it is
long, pointed and built around a drive; a station is a place, so it is broad, blunt and built around
the two things that cannot be moved: the docking interface and the radiator.

Meridian is a *rotating* station, because the setting needs one and because it decides the shape.
The ring is 120 m across and turns at 2.7 revolutions a minute, which is half a gravity at the rim —
not a full one, and that is a deliberate figure rather than a compromise. Half a gravity is enough
for a person to live in indefinitely, it halves the structural mass, and it is what the trading
stations in the Belt actually offer. The people who can afford a full gravity live on Earth.

WHAT THE DIMENSIONS TRACE TO

  ring radius    60 m      half a gravity at 2.7 rpm: a = w^2 r, w = 0.286 rad/s
  spine          180 m     the docking port at one end, the reactor and radiators at the other
  radiators      2 x 2,400 m2   the station's whole thermal problem, and the reason for the length
  port           12 m bore, 6.4 m of standoff structure, on the spine's axis

The port is on the axis at x = 0 and the station extends along +x, so a ship on the approach corridor
comes in along -x and docks nose-first. That is the convention `Station.Port` uses, and getting the
axis backwards puts the target behind the ship.

ORIENTATION. The spine runs along +x and the ring turns about it, so the ring lies in the yz plane.
Blender is z-up and the glTF export converts to y-up, which is what the client reads.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, asset_paths, box, collection, cylinder, export_blend,
    export_glb, join, material, render_views, reset,
    shade_smooth_by_angle, torus,
)

NAME = "meridian"

# --------------------------------------------------------------------------- numbers
#
# A WHEEL, and the wheel is the whole design. The reference is the station in 2001 --
# a fat torus turning about a central spindle, with ships docking on the axis -- and
# the reason to build one is that it is the only shape in which the answer to "where
# is down" is a direction a person can see. On a station with a spine, down is a
# corridor floor and nobody can tell which way it points; on a wheel, down is outward
# and the whole structure says so.
#
# THE TUBE IS FAT. This is what separates a wheel from a bicycle tyre, and it is the
# thing most attempts get wrong: a ring of 150 m radius with a 7 m tube is a hoop, and
# a hoop does not read as a place to live. At 30 m across the tube is eight decks deep
# and the ring reads as a *building* bent into a circle, which is what it is.

RING_RADIUS = 1000.0           # m to the habitat tube's centreline: a 2 km wheel
RING_TUBE = 70.0               # m radius: a 140 m tube, twenty decks
RING_DECKS = 20
RING_WINDOWS = 240             # round each rim: one every twenty-six metres
RINGS = 2                      # the double wheel. Two, because one is a hoop
RING_SEPARATION = 150.0        # m between the two rings' centrelines

SPOKES = 8                     # per ring, from the hub out to the habitat

# The hub, and the hub is now a harbour rather than a spindle.
#
# A 2 km wheel's interior is a void 1.86 km across and open to space, and the obvious
# thing to do with a void that size is to PARK IN IT. So the hub is a cylinder 300 m
# across and 560 m long with an open mouth at the forward end, and a ship flies in
# through the mouth and berths against the inside wall. That is what "internal port"
# means here, and it is why the hub is thirty times the volume it needs to be for
# bearings and tanks: the volume IS the facility.
HUB_RADIUS = 150.0
HUB_START = 60.0               # m, behind the small-craft port at the nose
HUB_END = 620.0

BAY_RADIUS = 128.0             # m, the clear bore a ship flies into
BAY_START = 24.0
BAY_END = 470.0
BAY_BERTHS = 12                # round the inside wall

PORT_BORE = 12.0               # m, the small-craft port at the very nose
PORT_LENGTH = 26.0

BOOM_END = 1180.0              # m, where the reactor housing sits
RADIATOR_STATION = 980.0       # m along the boom, where the radiator masts are
RADIATOR_PANELS = 4
RADIATOR_LENGTH = 320.0
RADIATOR_WIDTH = 150.0

# Colours, linear. Workers-built: painted, patched, honest about being machinery. No
# polish, high contrast where a person has to see an edge, and a great deal of yellow.
HULL_WHITE = (0.62, 0.63, 0.62)
HULL_SHADOW = (0.19, 0.20, 0.21)
STRUCTURE = (0.30, 0.31, 0.32)
TRUSS = (0.24, 0.25, 0.26)
RADIATOR = (0.78, 0.79, 0.80)
RADIATOR_HOT = (0.30, 0.05, 0.02)
WARNING = (0.85, 0.55, 0.03)
WINDOW = (0.95, 0.86, 0.62)
BEACON = (0.9, 0.08, 0.05)


def build_materials():
    material("MeridianHull", HULL_WHITE, metallic=0.1, roughness=0.55)
    material("MeridianShadow", HULL_SHADOW, metallic=0.2, roughness=0.6)
    material("MeridianStructure", STRUCTURE, metallic=0.7, roughness=0.4)
    material("MeridianTruss", TRUSS, metallic=0.8, roughness=0.45)
    material("MeridianRadiator", RADIATOR, metallic=0.15, roughness=0.25)

    # The hot face of a radiator, emissive. The client draws this with the emissive
    # term, so a station reads as running rather than as a model of a station.
    material("MeridianRadiatorHot", RADIATOR_HOT, metallic=0.0, roughness=0.7,
             emission=(1.0, 0.22, 0.06), emission_strength=6.0)

    material("MeridianWarning", WARNING, metallic=0.0, roughness=0.5)
    material("MeridianWindow", WINDOW, metallic=0.0, roughness=0.2,
             emission=(1.0, 0.88, 0.62), emission_strength=3.0)
    material("MeridianBeacon", BEACON, metallic=0.0, roughness=0.4,
             emission=(1.0, 0.1, 0.05), emission_strength=8.0)


def assign(obj, key):
    """Give an object one material, replacing whatever it has."""
    obj.data.materials.clear()
    obj.data.materials.append(bpy.data.materials[key])
    return obj


# --------------------------------------------------------------------------- the port


def build_port(col):
    """
    The docking interface, on the axis, where a ship arrives.

    On the axis and NOT on the rim, which is the opposite of what a wheel suggests.
    A ship that docked at the rim would have to match a point moving at 38 m/s and
    then be lifted 150 m up a spoke; a ship that docks on the spindle steps off into
    the hub and takes the lift out. Every real proposal does it this way and so does
    this one.

    The funnel is sized by the ship: a 55 m courier needs a bore it can put its nose
    into and a cone that forgives a pilot who is a metre out.
    """
    parts = []

    funnel = bpy.ops.mesh.primitive_cone_add(
        vertices=64, radius1=26.0, radius2=PORT_BORE, depth=22.0,
        location=(0, 0, 0), rotation=(0, math.pi / 2, 0))
    obj = bpy.context.object
    obj.name = "MeridianFunnel"
    parts.append(assign(obj, "MeridianWarning"))

    # The collar, which is what the latches take hold of.
    collar = cylinder("MeridianCollar", radius=PORT_BORE + 4.0, depth=10.0,
                      location=(0, 0, -22.0), rotation=(0, math.pi / 2, 0))
    parts.append(assign(collar, "MeridianStructure"))

    bore = cylinder("MeridianBore", radius=PORT_BORE - 2.0, depth=2.0,
                    location=(0, 0, -21.0), rotation=(0, math.pi / 2, 0))
    parts.append(assign(bore, "MeridianWindow"))

    # Approach lights at the four cardinals. Four is enough to read a roll and few
    # enough to count at a glance, which is the whole job of an approach light.
    for i in range(4):
        angle = (i * math.tau / 4.0) + (math.pi / 4.0)
        bpy.ops.mesh.primitive_uv_sphere_add(
            segments=20, ring_count=10, radius=1.6,
            location=(0,
                      math.sin(angle) * (PORT_BORE + 8.0),
                      -22.0 + math.cos(angle) * (PORT_BORE + 8.0)))
        light = bpy.context.object
        light.name = f"MeridianApproach{i}"
        parts.append(assign(light, "MeridianBeacon"))

    return parts


# --------------------------------------------------------------------------- the hub


def build_hub(col):
    """
    The harbour: an open cylinder 300 m across with a mouth at the forward end.

    THIS IS THE POINT OF THE STATION. A 2 km wheel encloses a void 1.86 km wide, and the
    only reason to build a wheel that size rather than a smaller one is that the void is
    useful — a ship flies in through the mouth, berths against the inside wall, and is
    under cover for cargo transfer without ever being pressurised into the habitat. The
    alternative, docking on the outside of the hub, means every tonne of cargo crosses
    vacuum twice.

    The bay is a CAGE, not a pressure vessel: it is open to space at the forward end and
    through the ring's plane. Twelve berths round the inside wall, each with a cradle,
    a power trunk and approach lights, and the whole thing lit so that a pilot coming in
    sees the far wall rather than a hole.

    The non-rotating core the wheel turns around is inside all of this, and the bearing
    is the single hardest engineering problem on the station: 300 m across, turning at
    0.95 rpm, carrying the weight of a city.
    """
    parts = []

    length = HUB_END - HUB_START
    middle = -(HUB_START + (length / 2.0))

    # The outer shell, in bands, with the gaps where the bearing runs. The gaps are not
    # decoration: the ring turns and the hub does not, and a skin across the joint would
    # be a skin that sheared.
    for band in range(9):
        along = HUB_START + 30.0 + (band * ((length - 60.0) / 9.0))
        ring = torus(f"MeridianHubBand{band}", major=HUB_RADIUS, minor=6.0,
                     location=(0, 0, -along), rotation=(0, math.pi / 2, 0),
                     major_segments=128, minor_segments=12)
        parts.append(assign(ring, "MeridianStructure"))

    # The longitudinal framing, which is what makes it a structure rather than a stack
    # of hoops. Sixteen ribs, on the outside so the inside stays clear for the ships.
    for rib in range(16):
        angle = rib * math.tau / 16.0
        rib_length = HUB_END - HUB_START
        spine = box(f"MeridianHubRib{rib}", size=(6.0, 6.0, rib_length),
                    location=(math.cos(angle) * HUB_RADIUS, math.sin(angle) * HUB_RADIUS,
                              -middle))
        parts.append(assign(spine, "MeridianTruss"))

    # The bearing housing, between the two rings, where the wheel meets the core.
    bearing = cylinder("MeridianBearing", radius=HUB_RADIUS + 30.0, depth=90.0,
                       location=(0, 0, -(HUB_START + (length / 2.0))),
                       rotation=(0, math.pi / 2, 0))
    parts.append(assign(bearing, "MeridianStructure"))

    # ---------------------------------------------------------------- the open bay
    #
    # The mouth: a rim, so that the forward edge of a 256 m hole reads as an edge rather
    # than as an absence. A hole in space has no silhouette and a pilot cannot judge its
    # size; a lit rim can be judged from ten kilometres out.
    lip = torus("MeridianBayLip", major=BAY_RADIUS + 8.0, minor=8.0,
                location=(0, 0, -BAY_START), rotation=(0, math.pi / 2, 0),
                major_segments=128, minor_segments=12)
    parts.append(assign(lip, "MeridianWarning"))

    # Approach lights round the mouth, twenty-four of them. This is the one light on the
    # station that is trying to be seen from far away.
    for i in range(24):
        angle = i * math.tau / 24.0
        bpy.ops.mesh.primitive_uv_sphere_add(
            segments=16, ring_count=8, radius=3.0,
            location=(math.cos(angle) * (BAY_RADIUS + 16.0),
                      math.sin(angle) * (BAY_RADIUS + 16.0), -BAY_START))
        light = bpy.context.object
        light.name = f"MeridianMouthLight{i}"
        parts.append(assign(light, "MeridianBeacon"))

    # The berths. Twelve cradles round the wall, each a platform with a power trunk and
    # a pair of guide rails, spaced so that a 180 m freighter fits between any two.
    for berth in range(BAY_BERTHS):
        angle = berth * math.tau / BAY_BERTHS
        along = -(BAY_START + (BAY_END - BAY_START) / 2.0)

        cradle = box(f"MeridianBerth{berth}", size=(30.0, 60.0, 150.0),
                     location=(math.cos(angle) * (BAY_RADIUS - 18.0),
                               math.sin(angle) * (BAY_RADIUS - 18.0), along),
                     rotation=(0, 0, angle))
        parts.append(assign(cradle, "MeridianShadow"))

        trunk = box(f"MeridianBerthTrunk{berth}", size=(10.0, 10.0, 140.0),
                    location=(math.cos(angle) * (BAY_RADIUS + 4.0),
                              math.sin(angle) * (BAY_RADIUS + 4.0), along),
                    rotation=(0, 0, angle))
        parts.append(assign(trunk, "MeridianTruss"))

        for rail in (-1, 1):
            offset = rail * 46.0
            guide = box(f"MeridianBerthRail{berth}_{rail}", size=(4.0, 4.0, 150.0),
                        location=(math.cos(angle) * (BAY_RADIUS - 8.0) - (math.sin(angle) * offset),
                                  math.sin(angle) * (BAY_RADIUS - 8.0) + (math.cos(angle) * offset),
                                  along),
                        rotation=(0, 0, angle))
            parts.append(assign(guide, "MeridianWarning"))

    # ---------------------------------------------------------------- the nose
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=64, ring_count=32, radius=HUB_RADIUS * 0.62,
        location=(0, 0, -HUB_START))
    dome = bpy.context.object
    dome.name = "MeridianDome"
    parts.append(assign(dome, "MeridianHull"))

    # The traffic control band, on the dome, which is the only place on the station with
    # a view of the whole approach.
    for i in range(28):
        angle = i * math.tau / 28.0
        radius = HUB_RADIUS * 0.62 - 4.0
        window = box(f"MeridianDomeWindow{i}", size=(8.0, 4.0, 1.5),
                     location=(math.cos(angle) * radius, math.sin(angle) * radius,
                               -(HUB_START + 26.0)),
                     rotation=(0, 0, angle))
        parts.append(assign(window, "MeridianWindow"))

    return parts


def build_wheel(col):
    """
    The habitat: two fat tori, their spokes, and the rims people live on.

    ONE GRAVITY, which is worth the rotation rate. At 150 m a full g needs 2.44
    revolutions a minute, and the comfort limit for a person who has to adapt is
    usually put at two to four; a station that wanted 1 g and could only manage half
    would be one nobody chose to live on. So Meridian turns briskly and the Belt
    stations, which are smaller, do not.

        a = w^2 r    w = sqrt(9.80665 / 150) = 0.2557 rad/s = 2.44 rpm

    The tube is 30 m across and eight decks deep, and the outermost deck is the one at
    a full g -- which means the outermost deck is also the one with the greatest
    Coriolis force on a running child, and the reason the gymnasium is on deck two.
    """
    parts = []

    for wheel in range(RINGS):
        station = HUB_START + (HUB_END - HUB_START) / 2.0
        station += (wheel - ((RINGS - 1) / 2.0)) * RING_SEPARATION

        ring = torus(f"MeridianRing{wheel}", major=RING_RADIUS, minor=RING_TUBE,
                     location=(0, 0, -station), rotation=(0, math.pi / 2, 0),
                     major_segments=192, minor_segments=32)
        parts.append(assign(ring, "MeridianHull"))

        # The window band, on the OUTWARD face: down is outward, so the outward face is
        # where the view is. Ninety-six of them at this radius is a window every four
        # metres, which at a glance reads as a continuous strip of light -- and a strip
        # of light round the rim of a wheel is the thing that tells a pilot, from ten
        # kilometres out, that there is a place here and it is inhabited.
        for i in range(RING_WINDOWS):
            angle = i * math.tau / RING_WINDOWS
            radius = RING_RADIUS + RING_TUBE - 0.5
            window = box(f"MeridianRimWindow{wheel}_{i}", size=(22.0, 9.0, 2.0),
                         location=(math.cos(angle) * radius,
                                   math.sin(angle) * radius,
                                   -station),
                         rotation=(0, 0, angle))
            parts.append(assign(window, "MeridianWindow"))

        # DECK BANDS, and the geometry of them is the whole of this loop.
        #
        # A deck is a floor parallel to the ring's plane, so it cuts the tube along a
        # circle that lies ON the tube's surface. For a tube of radius r whose centre is
        # R from the axis, a deck at height h above the ring's plane has radius
        # sqrt(r^2 - h^2) about its own centre -- NOT the tube's radius -- and sits at
        # height h. Getting that wrong draws flat rings floating inside the habitat,
        # which is what the first version did: eight circles all the same size, none of
        # them touching the wall they were supposed to be the floors of.
        for deck in range(RING_DECKS + 1):
            height = -RING_TUBE + (deck * (2.0 * RING_TUBE / RING_DECKS))
            half = math.sqrt(max((RING_TUBE * RING_TUBE) - (height * height), 1e-6))

            # Each deck is a pair of rings where it meets the tube's two walls.
            for side in (1, -1):
                ring_radius = RING_RADIUS + (side * half)
                band = torus(f"MeridianDeck{wheel}_{deck}_{side}",
                             major=ring_radius, minor=2.0,
                             location=(0, 0, -(station + height)),
                             rotation=(0, math.pi / 2, 0),
                             major_segments=160, minor_segments=8)
                parts.append(assign(band, "MeridianStructure"))

        # The spokes. Six, in compression, carrying the ring's weight to the hub --
        # which is the cheap direction for a structure and the reason a wheel is
        # cheaper than it looks.
        for spoke in range(SPOKES):
            angle = spoke * math.tau / SPOKES
            inner = HUB_RADIUS + 8.0
            outer = RING_RADIUS - RING_TUBE + 2.0
            length = outer - inner

            arm = box(f"MeridianSpoke{wheel}_{spoke}", size=(length, 26.0, 22.0),
                      location=(math.cos(angle) * (inner + (length / 2.0)),
                                math.sin(angle) * (inner + (length / 2.0)),
                                -station),
                      rotation=(0, 0, angle))
            parts.append(assign(arm, "MeridianStructure"))

            # A lift car on each spoke: the only thing on the ring that moves relative
            # to it, and therefore the only thing that needs a marking on it.
            car = box(f"MeridianCar{wheel}_{spoke}", size=(30.0, 34.0, 26.0),
                      location=(math.cos(angle) * (outer - 12.0),
                                math.sin(angle) * (outer - 12.0),
                                -station),
                      rotation=(0, 0, angle))
            parts.append(assign(car, "MeridianWarning"))

    return parts


def build_boom(col):
    """
    The boom: what holds the reactor and the radiators clear of the wheel.

    A radiator shadows whatever is behind it and a reactor irradiates whatever is near
    it, so both want distance -- and distance in space costs mass and nothing else. The
    boom is a box truss 114 m long, and it is the only part of Meridian that is pure
    structure rather than a place.
    """
    parts = []

    span = BOOM_END - HUB_END
    bays = 20

    for bay in range(bays):
        along = HUB_END + 20.0 + (bay * (span / bays))

        for corner in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            strut = box(f"MeridianLongeron{bay}_{corner[0]}_{corner[1]}",
                        size=(6.0, 6.0, (span / bays) + 1.0),
                        location=(corner[1] * 26.0, corner[0] * 26.0, -along))
            parts.append(assign(strut, "MeridianTruss"))

        brace = box(f"MeridianBrace{bay}", size=(4.0, 4.0, 74.0),
                    location=(0, 0, -along), rotation=(0, math.radians(41), 0))
        parts.append(assign(brace, "MeridianTruss"))

    return parts


def build_reactor(col):
    """
    The reactor housing, at the far end, and the radiators behind it.

    The reactor is a long way from the people, which is the oldest rule in spacecraft
    design and the reason the spine is 180 m rather than 40. The shadow shield is a
    disc between the two, and it is the single most massive thing on the station.
    """
    parts = []

    housing = cylinder("MeridianReactor", radius=34.0, depth=110.0,
                       location=(0, 0, -(BOOM_END + 55.0)), rotation=(0, math.pi / 2, 0))
    parts.append(assign(housing, "MeridianShadow"))

    # The shadow shield: a disc wider than the reactor, because a shadow is cast from a
    # point source and a shield the same width as the source casts a shadow that ends.
    shield = cylinder("MeridianShield", radius=66.0, depth=9.0,
                      location=(0, 0, -(BOOM_END - 24.0)), rotation=(0, math.pi / 2, 0))
    parts.append(assign(shield, "MeridianStructure"))

    for vane in range(6):
        angle = vane * math.tau / 6.0
        fin = box(f"MeridianReactorFin{vane}", size=(7.0, 30.0, 100.0),
                  location=(math.cos(angle) * 56.0, math.sin(angle) * 56.0,
                            -(BOOM_END + 55.0)),
                  rotation=(0, 0, angle))
        parts.append(assign(fin, "MeridianRadiator"))

    return parts


def build_radiators(col):
    """
    The main radiators: two panels, each 120 m by 40 m.

    A station's radiator is sized by what it has to reject and it cannot be stowed, so
    it is simply there — and at 4,800 square metres between them the panels are most of
    Meridian's silhouette. They are also why the station's designers put the habitat on
    a ring: the ring can turn to face the Sun and the radiators cannot.
    """
    parts = []

    for panel in range(RADIATOR_PANELS):
        side = 1 if panel == 0 else -1

        # The mast out from the spine to the panel's inner edge.
        mast = box(f"MeridianMast{panel}", size=(16.0, 130.0, 16.0),
                   location=(0, side * 78.0, -RADIATOR_STATION))
        parts.append(assign(mast, "MeridianTruss"))

        # The panel itself, edge-on to the spine so it sees sky in both directions.
        face = box(f"MeridianPanel{panel}",
                   size=(RADIATOR_WIDTH, RADIATOR_LENGTH, 2.0),
                   location=(0, side * (150.0 + (RADIATOR_LENGTH / 2.0)), -RADIATOR_STATION))
        parts.append(assign(face, "MeridianRadiator"))

        # The hot face: the side that faces the hull. Emissive, and the only part of
        # the station that is genuinely hot to look at.
        hot = box(f"MeridianPanelHot{panel}",
                  size=(RADIATOR_WIDTH - 14.0, RADIATOR_LENGTH - 14.0, 1.0),
                  location=(0, side * (150.0 + (RADIATOR_LENGTH / 2.0)),
                            -RADIATOR_STATION + 1.8))
        parts.append(assign(hot, "MeridianRadiatorHot"))

        # Ribs across the panel, for scale again: 120 m of flat nothing is unreadable.
        for rib in range(15):
            along = 150.0 + 14.0 + (rib * 21.0)
            spar = box(f"MeridianPanelRib{panel}_{rib}", size=(RADIATOR_WIDTH, 3.0, 5.0),
                       location=(0, side * along, -RADIATOR_STATION))
            parts.append(assign(spar, "MeridianStructure"))

    return parts


# --------------------------------------------------------------------------- corners


def cone_part(name, r1, r2, depth, location, rotation, vertices=48):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2,
                                    depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return obj


def sphere_part(name, radius, location, segments=24, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                         radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    return obj


# --------------------------------------------------------------------------- assembly

# (name, azimuth, elevation, distance as a multiple of the model's longest axis, lens)
#
# Six angles rather than four, because Meridian is the first asset here that is longer
# than it is wide *and* wider than it is long, depending on which end you are looking
# at: the spine is 206 m and the ring is 120 m across it, so no single view contains
# the shape. The port shot is close and on the axis because that is what a pilot sees
# for the last kilometre, and it is the view the model most has to work in.
SHOTS = [
    ("hero",   38.0,  16.0, 1.9, 52.0),
    ("port",  180.0,   4.0, 0.5, 40.0),
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
    hub = build_hub(col)
    wheel = build_wheel(col)
    boom = build_boom(col)
    reactor = build_reactor(col)
    radiators = build_radiators(col)

    # THE HUB DOES NOT TURN AND THE WHEEL DOES. They are two objects because they are
    # two objects: a client that wants to show the station running has to be able to
    # turn one against the other, and a part that moves should not be welded in.
    body = join("Meridian_Hub", port + hub + boom + reactor + radiators)
    habitat = join("Meridian_Wheel", wheel)

    for obj in (body, habitat):
        apply_transform(obj, scale=True)
        shade_smooth_by_angle(obj, math.radians(35))

    # THE MODEL'S LONG AXIS IS +Z IN BLENDER, AND THEREFORE +Y IN THE EXPORT.
    #
    # The station is built along +x because that is the natural way to lay out a spindle,
    # and the ships are built nose-up about +z because that is the natural way to lay out
    # a hull. The glTF exporter turns Blender's z-up into y-up, so the ships arrive long
    # along y and the station arrived long along x — and the client, which has one
    # convention for "which way does this model point", drew the station edge-on.
    #
    # So this rotates the finished station a quarter turn to match. It is done here, once,
    # rather than in the loader, because a convention the loader has to correct for is a
    # convention that every future asset will get wrong in a new way.
    for obj in (body, habitat):
        obj.rotation_euler = (0.0, math.radians(90.0), 0.0)
        apply_transform(obj, rotation=True)

    blend, glb, preview = asset_paths("stations", NAME)

    # What the design asks for, beside what was built. A model is a claim about a
    # place, and this is the line where the claim gets checked.
    rate = math.sqrt(9.80665 / RING_RADIUS)
    rpm = rate * 60.0 / math.tau
    speed = rate * RING_RADIUS

    print(f"  wheel     {RINGS} rings, {2 * (RING_RADIUS + RING_TUBE):.0f} m across, "
          f"{2 * RING_TUBE:.0f} m tube, {RING_DECKS} decks")
    print(f"  gravity   {rpm:.2f} rpm at {RING_RADIUS:.0f} m = {speed:.1f} m/s rim speed"
          f" = 1.00 g")
    print(f"  hub       {HUB_END - HUB_START:.0f} m spindle, {HUB_RADIUS * 2:.0f} m across")
    print(f"  boom      {BOOM_END - HUB_END:.0f} m, reactor and "
          f"{RADIATOR_PANELS * RADIATOR_LENGTH * RADIATOR_WIDTH:,.0f} m2 of radiator")

    render_views(preview, SHOTS, resolution=1100, samples=64)
    export_glb(glb, NAME)
    export_blend(blend)


main()
