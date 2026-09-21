"""
Sketch C -- "The Cathedral".

A concept-direction render for the Illuminus courier redesign. The intimidation
direction: the cylinder is kept because a cylinder is what a hundred-and-fifty-year
hull looks like, and everything added is added to make it read ENORMOUS. A long
dorsal keel fin runs the spine like a nave roof. The radiator is four trapezoidal
petals at the aft third that open like a flower -- a rose window of hot metal. And
the nozzle ring is presented as a crown: a flared collar with twelve merlons between
the twelve throats, because a ship that docks this one should remember looking up
at it. Rank through scale cues.

STOWAGE: the four petals fold forward about their root lines and lie flat against
the aft barrel in overlapping scales, tips aft, inside the collar's flare; the keel
fin is fixed.

Physics envelope (law, not taste): 55 m nose-to-engine-plane, 4.5 m hull radius,
12+1 magnetic nozzles at the engine plane, 730-860 m2 of radiator, Cygnus navigation
lights with the counts intact.

    blender --background --python tools/blender/ships/sketches/courier_c.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, bevel, box, collection, cylinder, lathe, link, material,
    render_views, reset, ring_of, shade_smooth_by_angle, sphere, torus,
)

HULL_RADIUS = 4.5
HULL_LENGTH = 55.0
BARREL_TOP = 44.0

# Four trapezoidal petals, 4.5 m at the root to 18.6 m at the tip over 16 m:
# 739 m2, inside the 730-860 m2 budget. Short and wide, because a flower's petals
# open; they do not droop.
PETALS = 4
PETAL_LENGTH = 16.0
PETAL_ROOT = 4.5
PETAL_TIP = 18.6
PETAL_THICK = 0.16
PETAL_AREA = PETALS * (PETAL_ROOT + PETAL_TIP) / 2.0 * PETAL_LENGTH
PETAL_TILT = 45.0            # degrees off the hull surface, deployed

NOZZLES = 12
NOZZLE_RADIUS = 0.42
NOZZLE_LENGTH = 3.4

MATERIALS = {}


def build_materials():
    MATERIALS["steel"] = material(
        "SketchHullC", (0.66, 0.68, 0.73), metallic=0.75, roughness=0.26)
    MATERIALS["steel_worn"] = material(
        "SketchHullWornC", (0.52, 0.53, 0.57), metallic=0.65, roughness=0.38)
    MATERIALS["dark"] = material(
        "SketchTrimC", (0.02, 0.021, 0.024), metallic=0.85, roughness=0.22)
    MATERIALS["radiator"] = material(
        "SketchRadiatorC", (0.20, 0.21, 0.23), metallic=0.25, roughness=0.55)
    # 1 500 K is a dull red, and the emission stays weak for the reason the
    # production script records: any stronger and hot metal reads as a lamp.
    MATERIALS["radiator_hot"] = material(
        "SketchRadiatorHotC", (0.34, 0.10, 0.05), metallic=0.2, roughness=0.66,
        emission=(0.40, 0.085, 0.03), emission_strength=0.55)
    MATERIALS["nozzle"] = material(
        "SketchNozzleC", (0.26, 0.27, 0.30), metallic=0.7, roughness=0.32)

    # The Cygnus convention. The names are the contract -- see the production script.
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
    MATERIALS["strobe"] = material(
        "IlluminusStrobe", (0.85, 0.85, 0.85), metallic=0.0, roughness=0.30,
        emission=(1.0, 1.0, 1.0), emission_strength=1.0)


def assign(obj, key):
    obj.data.materials.append(MATERIALS[key])
    return obj


def build_hull(col):
    """The production barrel and dome: age is the point, so the ancestor shows."""
    barrel = [(0.0, 0.0), (HULL_RADIUS * 0.98, 0.0)]
    for i in range(19):
        barrel.append((HULL_RADIUS, 2.0 + i * 2.3))
    for i in range(9):
        t = (i + 1) / 9.0
        barrel.append((HULL_RADIUS, BARREL_TOP - 2.0 + t * 2.0))

    nose = []
    for i in range(1, 41):
        t = i / 40.0
        r = HULL_RADIUS * (1.0 - t * t) ** 0.62
        nose.append((r, BARREL_TOP + t * (HULL_LENGTH - BARREL_TOP)))

    hull = lathe("Hull", barrel + nose, segments=96)
    assign(hull, "steel")
    shade_smooth_by_angle(hull, math.radians(38))
    link(hull, col)
    return hull


def build_rings(col):
    """Weld lines and the forward band, kept: on a cathedral the seams are buttresses."""
    for z in (12.0, 24.0, 36.0):
        ring = torus(f"Ring{z:.0f}", HULL_RADIUS + 0.02, 0.075,
                     location=(0, 0, z), major_segments=96, minor_segments=10)
        assign(ring, "dark")
        link(ring, col)

    band = cylinder("ForwardBand", HULL_RADIUS + 0.035, 1.1,
                    location=(0, 0, BARREL_TOP), vertices=96)
    assign(band, "dark")
    link(band, col)


def prism(name, profile, thickness, key, col):
    """
    A flat blade extruded from a side profile. The pipeline has no prism either,
    and a keel fin is exactly a side profile with a thickness.
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
    three metres clear of the barrel. From the beam it doubles the ship's plan;
    from below it is the thing you sail under.
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


def build_crown(col):
    """
    The engine bay as a crown: a flared collar round the engine plane, and twelve
    merlons standing between the twelve nozzles, so the drive presents as a circlet
    of points rather than as plumbing.
    """
    collar = lathe("CrownCollar", [
        (HULL_RADIUS * 0.99, 1.8),
        (HULL_RADIUS * 1.18, 0.5),
        (HULL_RADIUS * 1.27, -0.4),
        (HULL_RADIUS * 1.28, -0.8),
    ], segments=96)
    assign(collar, "steel_worn")
    shade_smooth_by_angle(collar, math.radians(34))
    link(collar, col)

    for i, (x, y, z) in enumerate(ring_of(NOZZLES, 5.35, z=-1.2, phase=math.pi / 12)):
        angle = math.pi / 12 + 2.0 * math.pi * i / NOZZLES
        merlon = box(f"Merlon{i}", (0.32, 0.75, 3.2), location=(x, y, z),
                     rotation=(0, 0, angle))
        assign(merlon, "dark")
        link(merlon, col)


def build_nozzles(col):
    """Twelve magnetic nozzles in a ring and one on the axis -- long throats, no bells."""
    nozzles = []
    for i, (x, y, z) in enumerate(ring_of(NOZZLES, 3.3, z=-NOZZLE_LENGTH / 2 + 0.2)):
        n = lathe(f"Nozzle{i}", [
            (NOZZLE_RADIUS * 0.55, 0.0),
            (NOZZLE_RADIUS * 0.62, 0.4),
            (NOZZLE_RADIUS, NOZZLE_LENGTH * 0.55),
            (NOZZLE_RADIUS * 1.18, NOZZLE_LENGTH),
        ], segments=40, location=(x, y, z))
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


def panel_mesh(name, root_w, tip_w, length, thick, location, rotation):
    """
    A trapezoidal panel: `root_w` across at the local -z end, `tip_w` at +z. The
    pipeline's box cannot taper, and a petal that does not taper is a slab.
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


def build_petal(col, index, phi_deg):
    """
    One radiator petal of the aft flower, deployed.

    Hinged at its narrow root on the aft-third barrel and tilted out, so the four
    of them open like a flower round the corridor the docking ship flies down.
    Trapezoidal because petals that meet at the root must be narrow there, and the
    area budget wants them wide at the tip.
    """
    phi = math.radians(phi_deg)
    tilt = math.radians(PETAL_TILT)
    n = Vector((math.cos(phi), math.sin(phi), 0.0))
    t = Vector((-math.sin(phi), math.cos(phi), 0.0))

    # The span runs aft and outward from the hinge; the face normal follows.
    d = (n * math.sin(tilt) + Vector((0, 0, -math.cos(tilt)))).normalized()
    m = (n * math.cos(tilt) + Vector((0, 0, math.sin(tilt)))).normalized()

    hinge = n * (HULL_RADIUS + 0.10)
    hinge.z = 20.0
    centre = hinge + d * (PETAL_LENGTH / 2.0)
    rotation = Matrix((m, t, d)).transposed().to_euler()

    panel = panel_mesh(f"Petal{index}", PETAL_ROOT, PETAL_TIP, PETAL_LENGTH,
                       PETAL_THICK, centre, rotation)
    assign(panel, "radiator")
    link(panel, col)

    # Only the tip half glows: the root half of a radiator runs coolest, and a
    # fully hot petal reads as a traffic cone, not as engineered metal. The hot
    # skin covers the outer fifty-five per cent of the face.
    hot_frac = 0.55
    hot_root = PETAL_ROOT + (PETAL_TIP - PETAL_ROOT) * (1.0 - hot_frac)
    hot_length = PETAL_LENGTH * hot_frac
    hot_centre = (hinge + d * (PETAL_LENGTH * (1.0 - hot_frac + hot_frac / 2.0))
                  + m * (PETAL_THICK / 2 + 0.012))
    hot = panel_mesh(f"PetalHot{index}", hot_root, PETAL_TIP, hot_length,
                     0.02, hot_centre, rotation)
    assign(hot, "radiator_hot")
    link(hot, col)

    # Two stiffeners on the shaded face, root to tip.
    for j, frac in enumerate((0.38, 0.66)):
        rib_centre = (centre - m * (PETAL_THICK / 2 + 0.05)
                      + d * (frac - 0.5) * PETAL_LENGTH)
        rib = box(f"PetalRib{index}_{j}", (0.08, 0.4, PETAL_LENGTH * 0.5),
                  location=rib_centre, rotation=rotation)
        assign(rib, "dark")
        link(rib, col)


def build_flower(col):
    """Four petals at the intercardinal angles, so the flower reads from any quarter."""
    for i, phi in enumerate((45.0, 135.0, 225.0, 315.0)):
        build_petal(col, i, phi)


def build_windows(col):
    """The viewport ribbon: one row, high on the barrel, the honest scale cue."""
    for i in range(12):
        angle = 2.0 * math.pi * i / 12
        x = (HULL_RADIUS + 0.01) * math.cos(angle)
        y = (HULL_RADIUS + 0.01) * math.sin(angle)
        w = box(f"Viewport{i}", (0.9, 0.06, 0.42), location=(x, y, 40.0),
                rotation=(0, 0, angle + math.pi / 2))
        assign(w, "dark")
        link(w, col)


def build_navigation_lights(col):
    """Cygnus, counts intact: the whites ride the keel, the pairs the barrel."""
    def lamp(name, key, location, radius=0.36):
        housing = sphere(f"{name}_Housing", radius=radius * 1.6, location=location,
                         segments=16, rings=8)
        assign(housing, "dark")
        link(housing, col)
        lens = sphere(name, radius=radius, location=location, segments=16, rings=8)
        assign(lens, key)
        link(lens, col)

    lamp("NavPort", "nav_red", (0.0, -(HULL_RADIUS + 0.4), 33.0))
    lamp("NavPortAft", "nav_red", (0.0, -(HULL_RADIUS + 0.4), 9.0))
    lamp("NavStarboard", "nav_green", (0.0, HULL_RADIUS + 0.4, 33.0))
    lamp("NavStarboardAft", "nav_green", (0.0, HULL_RADIUS + 0.4, 9.0))

    # DORSAL: TWO white, both proud of the keel fin's outboard edge.
    lamp("NavDorsalFore", "nav_white", (8.80, 0.0, 30.5))
    lamp("NavDorsalAft", "nav_white", (6.75, 0.0, 40.0))

    # VENTRAL: ONE yellow.
    lamp("NavVentral", "nav_yellow", (-(HULL_RADIUS + 0.4), 0.0, 23.0), radius=0.42)

    # Strobes: one at the keel's peak, one under the dome.
    lamp("StrobeDorsal", "strobe", (8.90, 0.0, 34.2), radius=0.30)
    lamp("StrobeVentral", "strobe", (-(HULL_RADIUS + 0.6), 0.0, 45.0), radius=0.30)


SHOTS = [
    ("hero", 40.0, 18.0, 1.55, 58.0),
    ("port", -90.0, 5.0, 1.55, 58.0),
    ("stern", 150.0, -22.0, 1.85, 58.0),
]


def main():
    reset()
    col = collection("SketchC")
    build_materials()

    build_hull(col)
    build_rings(col)
    build_keel(col)
    build_windows(col)
    build_crown(col)
    build_nozzles(col)
    build_flower(col)
    build_navigation_lights(col)

    print(f"  sketch C  hull {HULL_LENGTH:.0f} m x {HULL_RADIUS * 2:.1f} m, "
          f"radiator {PETAL_AREA:,.0f} m2 in {PETALS} petals "
          f"({PETAL_AREA * 8.0 / 1000.0:,.1f} t at 8 kg/m2)")

    render_views("/tmp/courier-sketches/C.png", SHOTS, resolution=900, samples=28)


main()
