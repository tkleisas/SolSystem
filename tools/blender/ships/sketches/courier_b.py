"""
Sketch B -- "The Yacht".

A concept-direction render for the Illuminus courier redesign. The blended spindle:
an elliptical, lofted cross-section instead of a barrel, the dome faired into the
body until the hull is one continuous surface -- an SSTO spaceplane's descendant, not
a tanker's. The radiator is two canted wings in a shallow V. Rank through form: no
rings, no seams, nothing to count.

STOWAGE: each wing rotates about its root fairing and folds flat against the flank,
tips aft, into the shallow scallop the flank already carries; closed, the V disappears
into the spindle and the hull is smooth again.

Physics envelope (law, not taste): 55 m nose-to-engine-plane, 9 m gauge kept at the
widest section (4.5 m semi-axis), 12+1 magnetic nozzles at the engine plane, 730-860
m2 of radiator, Cygnus navigation lights with the counts intact.

    blender --background --python tools/blender/ships/sketches/courier_b.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

import bpy  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from pipeline import (  # noqa: E402
    apply_transform, box, collection, cylinder, lathe, link, material, render_views,
    reset, ring_of, shade_smooth_by_angle, sphere,
)

HULL_LENGTH = 55.0
GAUGE = 4.5                  # semi-axis at the widest section: the 9 m gauge stays

# Two wings, chord 24 at the root sweeping to 18 at the tip over a 17.5 m span:
# 735 m2, inside the 730-860 m2 budget.
WING_ROOT = 24.0
WING_TIP = 18.0
WING_SPAN = 17.5
WING_THICK = 0.18
WING_AREA = 2 * (WING_ROOT + WING_TIP) / 2.0 * WING_SPAN
WING_CANT = 25.0             # degrees above horizontal -- the shallow V

NOZZLES = 12
NOZZLE_RADIUS = 0.42
NOZZLE_LENGTH = 3.4
NOZZLE_RING = 2.6            # the spindle's engine plane is smaller than a barrel's

MATERIALS = {}


def build_materials():
    MATERIALS["steel"] = material(
        "SketchHullB", (0.78, 0.80, 0.85), metallic=0.85, roughness=0.16)
    MATERIALS["dark"] = material(
        "SketchTrimB", (0.018, 0.019, 0.022), metallic=0.85, roughness=0.20)
    MATERIALS["canopy"] = material(
        "SketchCanopyB", (0.012, 0.016, 0.024), metallic=0.25, roughness=0.06)
    MATERIALS["radiator"] = material(
        "SketchRadiatorB", (0.22, 0.23, 0.26), metallic=0.30, roughness=0.50)
    MATERIALS["radiator_hot"] = material(
        "SketchRadiatorHotB", (0.34, 0.10, 0.05), metallic=0.2, roughness=0.66,
        emission=(0.40, 0.085, 0.03), emission_strength=0.55)
    MATERIALS["nozzle"] = material(
        "SketchNozzleB", (0.26, 0.27, 0.30), metallic=0.7, roughness=0.32)

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


# --------------------------------------------------------------------------- the loft
# The pipeline has a lathe (surfaces of revolution) but no loft: a spindle is a
# family of ellipses, not circles, so it is built here, in the sketch, as the brief
# instructs.

def ease(t):
    return t * t * (3.0 - 2.0 * t)


def sample_sections(control, count):
    """Smoothly interpolates (z, rx, ry) control points into `count` loft sections."""
    sections = []
    z0, z1 = control[0][0], control[-1][0]
    for i in range(count + 1):
        z = z0 + (z1 - z0) * i / count
        for k in range(len(control) - 1):
            if control[k][0] <= z <= control[k + 1][0]:
                span = control[k + 1][0] - control[k][0]
                t = ease((z - control[k][0]) / span) if span > 1e-9 else 0.0
                rx = control[k][1] + (control[k + 1][1] - control[k][1]) * t
                ry = control[k][2] + (control[k + 1][2] - control[k][2]) * t
                sections.append((z, rx, ry))
                break
    return sections


def loft(name, sections, segments=72):
    """
    A surface through a family of elliptical sections. A zero semi-axis at either
    end collapses that ring to a pole, which is how the nose closes.
    """
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()

    rings = []
    for z, rx, ry in sections:
        if rx <= 1e-9 or ry <= 1e-9:
            rings.append([bm.verts.new((0.0, 0.0, z))])
            continue
        ring = []
        for i in range(segments):
            a = 2.0 * math.pi * i / segments
            ring.append(bm.verts.new((rx * math.cos(a), ry * math.sin(a), z)))
        rings.append(ring)

    for ra, rb in zip(rings, rings[1:]):
        if len(ra) == 1:
            for i in range(segments):
                bm.faces.new((ra[0], rb[i], rb[(i + 1) % segments]))
        elif len(rb) == 1:
            for i in range(segments):
                bm.faces.new((ra[i], ra[(i + 1) % segments], rb[0]))
        else:
            for i in range(segments):
                j = (i + 1) % segments
                bm.faces.new((ra[i], ra[j], rb[j], rb[i]))

    # The engine-plane cap: a flat ngon across the aft ellipse.
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.shade_smooth()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_hull(col):
    """
    The spindle. Widest (and exactly at the 9 m gauge) through the mid-body, the
    dorsal line drawn long and low, the nose a continuous fairing rather than a
    dome set on a barrel.
    """
    control = [
        (0.0, 3.60, 3.10),
        (5.0, GAUGE, 3.75),
        (22.0, GAUGE, 3.75),
        (34.0, 4.35, 3.60),
        (42.0, 3.55, 3.05),
        (48.0, 2.35, 2.05),
        (52.0, 1.15, 1.00),
        (HULL_LENGTH, 0.0, 0.0),
    ]
    hull = loft("Hull", sample_sections(control, 44), segments=88)
    assign(hull, "steel")
    link(hull, col)
    return hull


def ellipsoid(name, location, scale, key, col, rotation=(0, 0, 0)):
    obj = sphere(name, radius=1.0, location=location, segments=32, rings=16)
    obj.scale = scale
    obj.rotation_euler = rotation
    apply_transform(obj, scale=True)
    assign(obj, key)
    link(obj, col)
    return obj


def build_canopy(col):
    """The command deck: a dark blister faired into the dorsal line, forward."""
    ellipsoid("Canopy", (3.05, 0.0, 45.5), (1.1, 1.6, 3.6), "canopy", col,
              rotation=(0.0, -0.35, 0.0))


def panel_mesh(name, root_chord, tip_chord, span, thick, tip_offset,
               location, rotation):
    """
    A trapezoidal panel: `root_chord` fore-to-aft at the root edge (local -y),
    `tip_chord` at the tip edge, the tip chord shifted fore by `tip_offset` so the
    trailing edge sweeps. The pipeline's box cannot taper, and a wing that does not
    taper is a plank.
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


def build_wing(col, name, side):
    """
    One radiator wing, deployed at its cant.

    The root fairing is a canoe on the flank; the wing rotates out of it to a
    shallow V, chord sweeping back as it tapers. Folding it back down against the
    flank is the stowage -- see the module docstring.
    """
    cant = math.radians(WING_CANT)
    # Span direction: out along the flank axis, tipped toward the dorsal side by
    # the cant. side is +1 starboard, -1 port.
    span = Vector((math.sin(cant), side * math.cos(cant), 0.0))
    normal = Vector((math.cos(cant), -side * math.sin(cant), 0.0))

    root_z = 24.0
    root_y = side * 3.45

    fairing = ellipsoid(f"{name}Root", (0.6, root_y, root_z), (1.6, 1.05, 10.5),
                        "steel", col)

    centre = Vector((0.6, root_y, root_z)) + span * (WING_SPAN / 2.0)
    rotation = Matrix((normal, span, Vector((0, 0, 1)))).transposed().to_euler()

    # The tip chord shifts fore by the chord difference, so the leading edge stays
    # straight and the sweep is all in the trailing edge.
    sweep = (WING_ROOT - WING_TIP) / 2.0

    wing = panel_mesh(name, WING_ROOT, WING_TIP, WING_SPAN, WING_THICK, sweep,
                      centre, rotation)
    assign(wing, "radiator")
    link(wing, col)

    # The whole dorsal face is the hot face: 1 500 K, dulled, offset a hair so the
    # two faces do not fight.
    hot = panel_mesh(f"{name}Hot", WING_ROOT, WING_TIP, WING_SPAN, 0.02, sweep,
                     centre + normal * (WING_THICK / 2 + 0.012), rotation)
    assign(hot, "radiator_hot")
    link(hot, col)

    # One spar line on the shaded face, at the mid span.
    spar_centre = centre - normal * (WING_THICK / 2 + 0.06)
    spar = box(f"{name}Spar", (0.09, WING_SPAN * 0.8, 0.5),
               location=spar_centre, rotation=rotation)
    assign(spar, "dark")
    link(spar, col)

    return fairing


def build_engine_bay(col):
    """The engine plate: a dark ellipse the nozzles stand on."""
    plate = cylinder("EnginePlate", 1.0, 0.3, location=(0, 0, 0.15), vertices=72)
    plate.scale = (3.55, 3.0, 1.0)
    apply_transform(plate, scale=True)
    assign(plate, "dark")
    link(plate, col)


def build_nozzles(col):
    """Twelve magnetic nozzles in a ring and one on the axis -- long throats, no bells."""
    nozzles = []
    for i, (x, y, z) in enumerate(ring_of(NOZZLES, NOZZLE_RING,
                                          z=-NOZZLE_LENGTH / 2 + 0.2)):
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


def build_navigation_lights(col):
    """Cygnus, counts intact. The pairs ride the wingtips so the span reads at night."""
    def lamp(name, key, location, radius=0.36):
        housing = sphere(f"{name}_Housing", radius=radius * 1.6, location=location,
                         segments=16, rings=8)
        assign(housing, "dark")
        link(housing, col)
        lens = sphere(name, radius=radius, location=location, segments=16, rings=8)
        assign(lens, key)
        link(lens, col)

    cant = math.radians(WING_CANT)
    tip_y = 3.45 + math.cos(cant) * WING_SPAN
    tip_x = 0.6 + math.sin(cant) * WING_SPAN

    # PORT: red pair, fore and aft on the port wingtip.
    lamp("NavPort", "nav_red", (tip_x, -tip_y, 30.0))
    lamp("NavPortAft", "nav_red", (tip_x, -tip_y, 18.0))
    # STARBOARD: green pair, the same stations.
    lamp("NavStarboard", "nav_green", (tip_x, tip_y, 30.0))
    lamp("NavStarboardAft", "nav_green", (tip_x, tip_y, 18.0))

    # DORSAL: TWO white, on the spine.
    lamp("NavDorsalFore", "nav_white", (4.45, 0.0, 30.0))
    lamp("NavDorsalAft", "nav_white", (4.55, 0.0, 12.0))

    # VENTRAL: ONE yellow.
    lamp("NavVentral", "nav_yellow", (-3.85, 0.0, 22.0), radius=0.42)
    lamp("StrobeDorsal", "strobe", (4.3, 0.0, 40.0), radius=0.30)
    lamp("StrobeVentral", "strobe", (-3.7, 0.0, 40.0), radius=0.30)


SHOTS = [
    ("hero", 40.0, 18.0, 2.0, 55.0),
    ("port", -90.0, 5.0, 2.0, 55.0),
    ("stern", 150.0, -15.0, 2.0, 55.0),
]


def main():
    reset()
    col = collection("SketchB")
    build_materials()

    build_hull(col)
    build_canopy(col)
    build_engine_bay(col)
    build_nozzles(col)
    build_wing(col, "WingPort", -1.0)
    build_wing(col, "WingStarboard", 1.0)
    build_navigation_lights(col)

    print(f"  sketch B  hull {HULL_LENGTH:.0f} m x {GAUGE * 2:.1f} m gauge, "
          f"radiator {WING_AREA:,.0f} m2 in 2 wings "
          f"({WING_AREA * 8.0 / 1000.0:,.1f} t at 8 kg/m2)")

    render_views("/tmp/courier-sketches/B.png", SHOTS, resolution=900, samples=28)


main()
