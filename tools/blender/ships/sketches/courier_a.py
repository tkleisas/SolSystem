"""
Sketch A -- "The Heirloom".

A concept-direction render for the Illuminus courier redesign. The purest Starship
evolution of the three: the barrel-and-dome silhouette is kept exactly, the forward
and aft flaps survive only as flush chine fairings (ancestry as affectation -- a hull
this old money does not need aerodynamics and wears them like a crest), and the
radiator deploys as four petals from flush recesses on the dorsal face. Mirror steel,
near-zero trim.

STOWAGE: the four petals counter-rotate about their hinge rails and lie over the
dorsal barrel as chords, wide ends overlapping at the centreline seam like scales,
in shallow faired recesses; the hinge rails stay as the only visible fitting.

Physics envelope (law, not taste): 55 m nose-to-engine-plane, 4.5 m hull radius,
12+1 magnetic nozzles at the engine plane, 730-860 m2 of radiator, Cygnus navigation
lights with the counts intact.

    blender --background --python tools/blender/ships/sketches/courier_a.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, box, collection, cylinder, lathe, link, material, render_views,
    reset, ring_of, shade_smooth_by_angle, sphere, torus,
)

HULL_RADIUS = 4.5
HULL_LENGTH = 55.0
BARREL_TOP = 44.0

# Four trapezoidal petals: chord 17 m at the hinge rail to 20 m at the free edge
# over a 10 m span, flaring toward the outer z end -- 740 m2, inside the budget.
PETALS = 4
PETAL_SPAN = 10.0
PETAL_ROOT = 17.0
PETAL_TIP = 20.0
PETAL_THICK = 0.16
PETAL_AREA = PETALS * (PETAL_ROOT + PETAL_TIP) / 2.0 * PETAL_SPAN

NOZZLES = 12
NOZZLE_RADIUS = 0.42
NOZZLE_LENGTH = 3.4

MATERIALS = {}


def build_materials():
    # Mirror steel and almost nothing else: an heirloom does not wear trim.
    MATERIALS["steel"] = material(
        "SketchHullA", (0.85, 0.87, 0.90), metallic=0.95, roughness=0.10)
    MATERIALS["dark"] = material(
        "SketchTrimA", (0.02, 0.021, 0.024), metallic=0.85, roughness=0.22)
    MATERIALS["radiator"] = material(
        "SketchRadiatorA", (0.22, 0.23, 0.26), metallic=0.30, roughness=0.50)
    # 1 500 K is a dull red, and the emission stays weak for the reason the production
    # script records: any stronger and hot metal reads as a lamp.
    MATERIALS["radiator_hot"] = material(
        "SketchRadiatorHotA", (0.34, 0.10, 0.05), metallic=0.2, roughness=0.66,
        emission=(0.40, 0.085, 0.03), emission_strength=0.55)
    MATERIALS["nozzle"] = material(
        "SketchNozzleA", (0.26, 0.27, 0.30), metallic=0.7, roughness=0.32)

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
    """The production silhouette, kept entire: one barrel, one dome, one drawn nose."""
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


def ellipsoid(name, location, scale, key, col, rotation=(0, 0, 0)):
    """A squashed sphere: the fairing shape that reads as grown, not bolted on."""
    obj = sphere(name, radius=1.0, location=location, segments=32, rings=16)
    obj.scale = scale
    obj.rotation_euler = rotation
    apply_transform(obj, scale=True)
    assign(obj, key)
    link(obj, col)
    return obj


def build_chines(col):
    """
    The vestigial flaps. Two short fairings on the forward dome where a Starship
    carries its canards, two long ones on the aft barrel where it carries its aft
    flaps -- both flush, both useless, both non-negotiable to the family.
    """
    ellipsoid("ChineFwdPort", (0, -(HULL_RADIUS - 0.25), 46.0), (0.55, 1.5, 5.2),
              "steel", col, rotation=(0.0, -0.28, 0.0))
    ellipsoid("ChineFwdStarboard", (0, HULL_RADIUS - 0.25, 46.0), (0.55, 1.5, 5.2),
              "steel", col, rotation=(0.0, -0.28, 0.0))
    ellipsoid("ChineAftPort", (0, -(HULL_RADIUS - 0.15), 11.0), (0.5, 1.15, 9.5),
              "steel", col)
    ellipsoid("ChineAftStarboard", (0, HULL_RADIUS - 0.15, 11.0), (0.5, 1.15, 9.5),
              "steel", col)


def panel_mesh(name, root_chord, tip_chord, span, thick, tip_offset,
               location, rotation):
    """
    A trapezoidal panel: `root_chord` along the hull at the hinge edge (local -y),
    `tip_chord` at the free edge (local +y), with the tip chord shifted along the
    hull by `tip_offset` so the panel sweeps. The pipeline's box cannot taper, and
    a petal that does not taper is a slab.
    """
    x = thick / 2.0
    S = span / 2.0
    rc, tc = root_chord / 2.0, tip_chord / 2.0

    verts = [
        (-x, -S, -rc), (x, -S, -rc), (-x, -S, rc), (x, -S, rc),
        (-x, S, tip_offset - tc), (x, S, tip_offset - tc),
        (-x, S, tip_offset + tc), (x, S, tip_offset + tc),
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


def build_petal(col, index, hinge_deg, deploy_deg, z_centre, tip_offset):
    """
    One radiator petal, deployed.

    Hinged on a rail near the equator of the dorsal face, and the deployment is a
    true rotation about that rail -- about z, the rail's own axis, with the sign
    chosen per row so both rows swing up and out symmetrically. The first version
    composed the rotation by hand and gave the port row the wrong handedness: it
    deployed DOWN the flank and read as a broken wing in the broadside view.
    Stowed, the petals lie over the dorsal face as chords, the recesses faired to
    take them, overlapping at the centreline seam like scales.
    """
    phi = math.radians(hinge_deg)
    theta = math.radians(deploy_deg) * (1.0 if hinge_deg > 0.0 else -1.0)
    n = Vector((math.cos(phi), math.sin(phi), 0.0))        # radial at the hinge
    t = Vector((-math.sin(phi), math.cos(phi), 0.0))       # tangent, +phi direction
    s0 = -t if hinge_deg > 0.0 else t                      # stowed span: over the top

    hinge = n * (HULL_RADIUS + 0.10)
    hinge.z = z_centre

    # The stowed panel's outward normal: the radial at its stowed centre.
    stowed = hinge + s0 * (PETAL_SPAN / 2.0)
    n_mid = Vector((stowed.x, stowed.y, 0.0)).normalized()

    basis = Matrix((n_mid, s0, Vector((0, 0, 1)))).transposed()
    swing = Matrix.Rotation(theta, 3, 'Z')
    rotation = (swing @ basis).to_euler()

    span = swing @ s0
    normal = swing @ n_mid
    centre = hinge + span * (PETAL_SPAN / 2.0)

    panel = panel_mesh(f"Petal{index}", PETAL_ROOT, PETAL_TIP, PETAL_SPAN,
                       PETAL_THICK, tip_offset, centre, rotation)
    assign(panel, "radiator")
    link(panel, col)

    # The whole outward face is the hot face: 1 500 K, dulled, offset a hair so the
    # two faces do not fight.
    hot = panel_mesh(f"PetalHot{index}", PETAL_ROOT, PETAL_TIP, PETAL_SPAN,
                     0.02, tip_offset,
                     centre + normal * (PETAL_THICK / 2 + 0.012), rotation)
    assign(hot, "radiator_hot")
    link(hot, col)

    # Two ribs on the shaded face, at the third and two-thirds span.
    for j, frac in enumerate((0.35, 0.68)):
        rib_centre = (centre - normal * (PETAL_THICK / 2 + 0.05)
                      + swing @ (s0 * ((frac - 0.5) * PETAL_SPAN)))
        rib = box(f"PetalRib{index}_{j}", (0.08, 0.4, PETAL_ROOT * 0.85),
                  location=rib_centre, rotation=rotation)
        assign(rib, "dark")
        link(rib, col)


def build_radiator(col):
    """
    Four petals in two rows, fore and aft, deployed gull-wing from the dorsal rails.
    """
    for row, hinge_deg in enumerate((80.0, -80.0)):
        # The hinge rail itself: the one fitting that stays visible when stowed.
        phi = math.radians(hinge_deg)
        rail = box(f"HingeRail{row}", (0.30, 0.5, 42.0),
                   location=((HULL_RADIUS + 0.08) * math.cos(phi),
                             (HULL_RADIUS + 0.08) * math.sin(phi), 24.0),
                   rotation=(0, 0, phi))
        assign(rail, "dark")
        link(rail, col)

        # Fore petal sweeps fore, aft petal sweeps aft: the wide ends point away
        # from the mid gap, like leaves off a stem.
        build_petal(col, row * 2, hinge_deg, 60.0, 32.5, +(PETAL_TIP - PETAL_ROOT) / 2.0)
        build_petal(col, row * 2 + 1, hinge_deg, 60.0, 11.5, -(PETAL_TIP - PETAL_ROOT) / 2.0)


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


def build_engine_bay(col):
    skirt = lathe("EngineSkirt", [
        (HULL_RADIUS * 0.86, -0.9),
        (HULL_RADIUS * 0.97, -0.35),
        (HULL_RADIUS * 1.005, 0.35),
        (HULL_RADIUS, 1.2),
    ], segments=96)
    assign(skirt, "dark")
    shade_smooth_by_angle(skirt, math.radians(36))
    link(skirt, col)
    return skirt


def build_navigation_lights(col):
    """Cygnus, counts intact: 2+2 red/green in pairs, TWO white dorsal, ONE yellow ventral."""
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

    # The dorsal pair move outboard of the petals' arc, onto the forward dome where
    # deployed petals cannot cover them. Still exactly TWO, still on the roof.
    lamp("NavDorsalFore", "nav_white", (HULL_RADIUS * 0.72, 0.0, 46.5))
    lamp("NavDorsalAft", "nav_white", (HULL_RADIUS + 0.4, 0.0, 42.0))

    lamp("NavVentral", "nav_yellow", (-(HULL_RADIUS + 0.4), 0.0, 23.0), radius=0.42)
    lamp("StrobeDorsal", "strobe", (HULL_RADIUS + 0.6, 0.0, 45.0), radius=0.30)
    lamp("StrobeVentral", "strobe", (-(HULL_RADIUS + 0.6), 0.0, 45.0), radius=0.30)


def build_windows(col):
    """One ribbon of viewports at the forward section -- the only concession to crew."""
    for i in range(12):
        angle = 2.0 * math.pi * i / 12
        x = (HULL_RADIUS + 0.01) * math.cos(angle)
        y = (HULL_RADIUS + 0.01) * math.sin(angle)
        w = box(f"Viewport{i}", (0.9, 0.06, 0.42), location=(x, y, 40.0),
                rotation=(0, 0, angle + math.pi / 2))
        assign(w, "dark")
        link(w, col)


SHOTS = [
    ("hero", 40.0, 18.0, 1.55, 58.0),
    ("port", -90.0, 5.0, 1.55, 58.0),
    ("stern", 150.0, -25.0, 1.55, 58.0),
]


def main():
    reset()
    col = collection("SketchA")
    build_materials()

    build_hull(col)
    build_chines(col)
    build_windows(col)
    build_engine_bay(col)
    build_nozzles(col)
    build_radiator(col)
    build_navigation_lights(col)

    print(f"  sketch A  hull {HULL_LENGTH:.0f} m x {HULL_RADIUS * 2:.1f} m, "
          f"radiator {PETAL_AREA:,.0f} m2 in {PETALS} petals "
          f"({PETAL_AREA * 8.0 / 1000.0:,.1f} t at 8 kg/m2)")

    render_views("/tmp/courier-sketches/A.png", SHOTS, resolution=900, samples=28)


main()
