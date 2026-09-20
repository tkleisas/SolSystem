"""
Shared building blocks for the SolSystem model pipeline.

Everything here is deliberately boring: primitives, a few lathe and sweep helpers
that turn a profile into a surface of revolution, principled materials, and an
export step. The interesting decisions are in the per-asset scripts, which are
meant to read like a specification rather than like a modelling session.

Units are metres. Blender's default unit is one metre, so nothing is scaled.

Run headless:

    blender --background --python tools/blender/ships/illuminus_courier.py
"""

import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector, Matrix

# --------------------------------------------------------------------------- scene


def reset():
    """An empty scene. Always called first, so a run never inherits anything."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    return scene


def collection(name):
    """A named collection, created if absent, so a .blend can hold several assets."""
    if name in bpy.data.collections:
        return bpy.data.collections[name]
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    return col


def link(obj, col):
    """Moves an object into a collection, out of whatever it landed in."""
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)
    return obj


# --------------------------------------------------------------------------- materials


def material(name, base_color, metallic=0.0, roughness=0.5, emission=None,
             emission_strength=0.0, alpha=1.0):
    """
    A Principled BSDF material.

    `base_color` and `emission` are linear RGB triples, not sRGB. Blender's default
    colour management will convert on the way out, and picking values by eye in
    display space here is how a model ends up looking washed out in the engine.
    """
    if name in bpy.data.materials:
        return bpy.data.materials[name]

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")

    def setv(key, value):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = value

    setv("Base Color", (*base_color, 1.0))
    setv("Metallic", metallic)
    setv("Roughness", roughness)
    setv("Alpha", alpha)

    if emission is not None:
        setv("Emission Color", (*emission, 1.0))
        setv("Emission Strength", emission_strength)

    if alpha < 1.0:
        mat.blend_method = 'BLEND'

    return mat


# --------------------------------------------------------------------------- primitives


def cylinder(name, radius, depth, location=(0, 0, 0), rotation=(0, 0, 0),
             vertices=48, cap='NGON'):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=location, rotation=rotation, end_fill_type=cap)
    obj = bpy.context.object
    obj.name = name
    return obj


def cone(name, r1, r2, depth, location=(0, 0, 0), rotation=(0, 0, 0), vertices=48):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=r1, radius2=r2, depth=depth,
        location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return obj


def sphere(name, radius, location=(0, 0, 0), segments=48, rings=24):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    return obj


def box(name, size, location=(0, 0, 0), rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = (size[0], size[1], size[2])
    apply_transform(obj, scale=True)
    return obj


def torus(name, major, minor, location=(0, 0, 0), rotation=(0, 0, 0),
          major_segments=64, minor_segments=16):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major, minor_radius=minor,
        major_segments=major_segments, minor_segments=minor_segments,
        location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return obj


# --------------------------------------------------------------------------- helpers


def apply_transform(obj, location=False, rotation=False, scale=True):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=location, rotation=rotation, scale=scale)
    return obj


def lathe(name, profile, segments=64, location=(0, 0, 0)):
    """
    A surface of revolution from a 2D profile.

    `profile` is a list of (radius, height) pairs from one end to the other. This is
    how most of a hull gets built: a rocket is a profile, and a profile is a table of
    numbers that a designer can actually argue about. Radii of zero are allowed at
    either end, which produce poles.

    Built by spinning a bmesh directly rather than by the spin operator, so the
    profile stays the source of truth and the mesh cannot drift from it.
    """
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()

    rings = []
    seen = {}
    for radius, height in profile:
        if radius <= 1e-9:
            key = (0.0, 0.0, round(height, 9))
            if key not in seen:
                seen[key] = bm.verts.new(key)
            rings.append([seen[key]])
            continue

        ring = []
        for i in range(segments):
            angle = 2.0 * math.pi * i / segments
            key = (round(radius * math.cos(angle), 9),
                   round(radius * math.sin(angle), 9),
                   round(height, 9))
            # A profile may double back on itself -- a tank's straight section is two
            # entries at the same radius and different heights, and a flange is two at
            # the same height. Reusing the vertex whenever the position repeats is
            # what keeps those from becoming degenerate faces.
            if key not in seen:
                seen[key] = bm.verts.new(key)
            ring.append(seen[key])
        rings.append(ring)

    # Consecutive identical rings carry no surface; skipping them is cheaper than
    # building zero-area faces and letting a merge modifier clean up after.
    deduped = []
    for ring in rings:
        if deduped and len(ring) == len(deduped[-1]):
            if all(a is b for a, b in zip(ring, deduped[-1])):
                continue
        deduped.append(ring)
    rings = deduped

    for a, b in zip(rings, rings[1:]):
        if len(a) == 1:
            for i in range(segments):
                bm.faces.new((a[0], b[i], b[(i + 1) % segments]))
        elif len(b) == 1:
            for i in range(segments):
                bm.faces.new((a[i], a[(i + 1) % segments], b[0]))
        else:
            n = min(len(a), len(b))
            for i in range(n):
                j = (i + 1) % n
                bm.faces.new((a[i], a[j], b[j], b[i]))

    # Caps for a profile that does not close itself.
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.shade_smooth()

    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)
    return obj


def ring_of(count, radius, z=0.0, phase=0.0):
    """Positions evenly spaced around the z axis, for engine bells and the like."""
    return [
        (radius * math.cos(phase + 2.0 * math.pi * i / count),
         radius * math.sin(phase + 2.0 * math.pi * i / count),
         z)
        for i in range(count)
    ]


def bevel(obj, width, segments=2, angle_limit=math.radians(40)):
    """Softens an edge so it catches light. Most of what reads as 'machined'."""
    mod = obj.modifiers.new("Bevel", 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    mod.angle_limit = angle_limit
    return obj


def mirror_x(obj):
    """A mirror modifier about x, for anything that comes in pairs."""
    mod = obj.modifiers.new("Mirror", 'MIRROR')
    mod.use_axis = (True, False, False)
    return obj


def join(name, objects):
    """Merges objects into one mesh. Used to keep an asset to a few draw calls."""
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    joined = bpy.context.object
    joined.name = name
    return joined


def shade_smooth_by_angle(obj, angle=math.radians(30)):
    """Smooth shading with a hard edge above `angle`, the usual machined look."""
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=angle)
    except Exception:
        bpy.ops.object.shade_smooth()


# --------------------------------------------------------------------------- output


def ensure_dir(path):
    os.makedirs(path, exist_ok=True)
    return path


def export_glb(path, name):
    """Writes a binary glTF. This is what the client will actually load."""
    ensure_dir(os.path.dirname(path))
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format='GLB',
        use_selection=False,
        export_apply=True,
        export_yup=True,
    )
    print(f"  glb     -> {path}")


def export_blend(path):
    ensure_dir(os.path.dirname(path))
    bpy.ops.wm.save_as_mainfile(filepath=path)
    print(f"  blend   -> {path}")


def model_bounds():
    """Bounding box of every mesh in the scene, in world space."""
    lo = Vector((1e18, 1e18, 1e18))
    hi = Vector((-1e18, -1e18, -1e18))
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def _stage(centre, size, key_energy, fill_energy, rim_energy, ambient):
    """World and three-point rig, sized to the subject. Removed by _strike."""
    scene = bpy.context.scene
    world = bpy.data.worlds.new("Stage") if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (ambient, ambient * 1.05, ambient * 1.25, 1.0)
        bg.inputs[1].default_value = 1.0

    rig = []
    lights = (
        ("Key", Vector((1.0, -0.85, 0.75)), key_energy, 0.30),
        ("Fill", Vector((-0.9, -0.55, -0.15)), fill_energy, 0.70),
        ("Rim", Vector((-0.35, 0.95, 0.55)), rim_energy, 0.45),
        ("Under", Vector((0.15, -0.2, -1.0)), fill_energy * 0.5, 0.9),
    )
    for name, direction, energy, size_scale in lights:
        bpy.ops.object.light_add(type='AREA', location=centre + direction * size * 2.2)
        light = bpy.context.object
        light.name = f"Stage{name}"
        # Area lights fall off with distance, so the energy is scaled to keep the
        # exposure the same whatever the subject's size.
        light.data.energy = energy * (size * 2.2) ** 2
        light.data.size = size * size_scale
        d = (centre - light.location).normalized()
        light.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        rig.append(light)

    return rig


def _strike(rig, camera):
    for obj in list(rig) + ([camera] if camera else []):
        bpy.data.objects.remove(obj, do_unlink=True)


def _render_to(path, resolution, samples):
    scene = bpy.context.scene
    ensure_dir(os.path.dirname(path))
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(f"  view    -> {os.path.basename(path)}")


def render_views(path, shots, resolution=1100, samples=64, ambient=0.05):
    """
    Renders a model from several angles into one contact sheet.

    `shots` is a list of (name, azimuth, elevation, distance_factor, lens) and the
    file is written as `<name>_<shot>.png` plus a contact sheet at `path`.

    A single three-quarter view flatters a model and hides its problems: a floating
    module reads as attached, a panel buried in a tank reads as beside it, and a
    silhouette that only works from one side looks fine. Four angles and a plan is
    the minimum that lets a person judge a shape, so this is the default rather than
    a special case.
    """
    lo, hi = model_bounds()
    centre = (lo + hi) / 2.0
    extent = max((hi - lo)[i] for i in range(3))
    # Generous, and deliberately so. A dark grey metal lit for drama is a model
    # nobody can review: the whole point of a turnround is to see the shape, and
    # contrast against a dark background hides exactly the surfaces being judged.
    rig = _stage(centre, extent, key_energy=9.0, fill_energy=3.2, rim_energy=5.0,
                 ambient=ambient)

    written = []
    for name, azimuth, elevation, distance_factor, lens in shots:
        el = math.radians(elevation)
        az = math.radians(azimuth)
        distance = extent * distance_factor
        eye = centre + Vector((
            distance * math.cos(el) * math.cos(az),
            distance * math.cos(el) * math.sin(az),
            distance * math.sin(el)))

        bpy.ops.object.camera_add(location=eye)
        cam = bpy.context.object
        cam.name = f"Cam_{name}"
        cam.rotation_euler = (centre - eye).normalized().to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        bpy.context.scene.camera = cam

        target = path.replace(".png", f"_{name}.png")
        _render_to(target, resolution, samples)
        written.append((name, target))
        bpy.data.objects.remove(cam, do_unlink=True)

    _strike(rig, None)
    make_contact_sheet(path, written)
    return written


def make_contact_sheet(path, images):
    """
    Lays the rendered views into one grid image, with no dependency on PIL.

    Blender can composite and it can also just be told to write pixels, so the sheet
    is assembled from the rendered files by loading them as images and writing a new
    one. Doing it here keeps the pipeline to a single tool.
    """
    loaded = []
    for name, filepath in images:
        if not os.path.exists(filepath):
            continue
        img = bpy.data.images.load(filepath)
        loaded.append((name, img))

    if not loaded:
        return

    columns = 3 if len(loaded) > 4 else 2
    rows = math.ceil(len(loaded) / columns)
    cell_w, cell_h = loaded[0][1].size
    width, height = cell_w * columns, cell_h * rows

    sheet = bpy.data.images.new("ContactSheet", width=width, height=height, alpha=False)
    sheet_pixels = [0.02] * (width * height * 4)

    for index, (name, img) in enumerate(loaded):
        col = index % columns
        row = index // columns
        src = list(img.pixels)
        sw, sh = img.size
        # Images are bottom-up in Blender's buffer; the grid is laid out top-down.
        for y in range(sh):
            dst_y = height - (row + 1) * cell_h + y
            if dst_y < 0 or dst_y >= height:
                continue
            src_row = y * sw * 4
            dst_row = (dst_y * width + col * cell_w) * 4
            sheet_pixels[dst_row:dst_row + sw * 4] = src[src_row:src_row + sw * 4]

    sheet.pixels = sheet_pixels
    sheet.filepath_raw = path
    sheet.file_format = 'PNG'
    sheet.save()
    print(f"  sheet   -> {os.path.basename(path)}")

    for _, img in loaded:
        bpy.data.images.remove(img)
    bpy.data.images.remove(sheet)


def render_preview(path, target=None, distance=None, elevation=20.0,
                   azimuth=35.0, resolution=900, samples=48, transparent=False,
                   fill=1.35):
    """
    A three-quarter view of whatever is in the scene, rendered with Cycles on CPU.

    Cycles rather than EEVEE because it works in a headless build with no GPU and
    this is the picture a human is going to judge the model by.

    The camera distance defaults to the model's own extent times `fill`, so a 55 m
    courier and a 160 m freighter are both framed without a number per asset -- and
    so a model that grows a radiator does not silently leave the frame.
    """
    ensure_dir(os.path.dirname(path))
    scene = bpy.context.scene

    world = bpy.data.worlds.new("Preview") if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.015, 0.017, 0.022, 1.0)
        bg.inputs[1].default_value = 1.0

    if target is None or distance is None:
        lo = Vector((1e18, 1e18, 1e18))
        hi = Vector((-1e18, -1e18, -1e18))
        for obj in bpy.data.objects:
            if obj.type != 'MESH':
                continue
            for corner in obj.bound_box:
                w = obj.matrix_world @ Vector(corner)
                lo = Vector((min(lo[i], w[i]) for i in range(3)))
                hi = Vector((max(hi[i], w[i]) for i in range(3)))
        if target is None:
            target = (lo + hi) / 2.0
        extent = max((hi - lo)[i] for i in range(3))
        if distance is None:
            # Framed for a 65 mm lens: the half-angle is about 15 degrees, so the
            # standoff is roughly twice the extent.
            distance = extent * fill

    target = Vector(target)
    el = math.radians(elevation)
    az = math.radians(azimuth)
    eye = target + Vector((
        distance * math.cos(el) * math.cos(az),
        distance * math.cos(el) * math.sin(az),
        distance * math.sin(el)))

    bpy.ops.object.camera_add(location=eye)
    cam = bpy.context.object
    cam.name = "PreviewCamera"
    direction = (target - eye).normalized()
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 65.0
    scene.camera = cam

    # A key light, a dim fill from the other side, and a rim to separate from the
    # background. Three lights is enough to read a silhouette and a surface.
    for name, loc, energy, size in (
        ("Key", (distance, -distance * 0.7, distance * 0.8), 5.0, 0.35),
        ("Fill", (-distance * 0.9, -distance * 0.4, -distance * 0.2), 1.2, 0.6),
        ("Rim", (-distance * 0.3, distance, distance * 0.5), 3.0, 0.4),
    ):
        bpy.ops.object.light_add(type='AREA', location=target + Vector(loc))
        light = bpy.context.object
        light.name = name
        light.data.energy = energy * distance * distance * 4.0
        light.data.size = size * distance
        d = (target - light.location).normalized()
        light.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.film_transparent = transparent
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(f"  preview -> {path}")

    # The preview rig is not part of the asset.
    for obj in [cam] + [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(obj, do_unlink=True)


def render_orthographic(path, axis='x', resolution=1000, samples=48, margin=1.12,
                        look_at=(0, 0, 0)):
    """
    A flat elevation with no perspective, for judging a profile.

    A three-quarter view flatters a model and hides where its parts are. An
    orthographic side view shows exactly where every part sits relative to the hull,
    which is how a floating habitat or a panel buried in a tank gets caught.

    `axis` is which direction the camera looks along: 'x' gives a side elevation,
    'y' a front one, 'z' a plan.
    """
    ensure_dir(os.path.dirname(path))
    scene = bpy.context.scene

    world = bpy.data.worlds.new("Ortho") if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.02, 0.022, 0.028, 1.0)
        bg.inputs[1].default_value = 1.0

    # Extent of everything visible, so the frame is set by the model rather than by
    # a distance someone guessed.
    lo = Vector((1e18, 1e18, 1e18))
    hi = Vector((-1e18, -1e18, -1e18))
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))

    centre = (lo + hi) / 2.0
    size = max((hi - lo)[i] for i in range(3)) * margin

    direction = {'x': Vector((1, 0, 0)), 'y': Vector((0, 1, 0)), 'z': Vector((0, 0, 1))}[axis]
    bpy.ops.object.camera_add(location=centre + direction * size * 2.0)
    cam = bpy.context.object
    cam.name = "OrthoCamera"
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = size
    d = (centre - cam.location).normalized()
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    # Flat, even lighting: an elevation is a drawing, and shadows confuse it.
    for name, offset in (("A", Vector((1, -1, 1))), ("B", Vector((-1, -0.6, -0.4)))):
        bpy.ops.object.light_add(type='SUN', location=centre + offset * size)
        light = bpy.context.object
        light.name = f"Ortho{name}"
        light.data.energy = 4.0 if name == "A" else 1.6
        d = (centre - light.location).normalized()
        light.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(f"  ortho   -> {path}")

    for obj in [cam] + [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(obj, do_unlink=True)


def textures_dir():
    """Where the shipped image maps live, alongside the models rather than the tools."""
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return os.path.join(root, "art", "textures")


def asset_paths(category, name):
    """The three files every asset produces, in one place so they stay together."""
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return (
        os.path.join(root, "art", "blend", category, f"{name}.blend"),
        os.path.join(root, "art", "models", category, f"{name}.glb"),
        os.path.join(root, "art", "previews", category, f"{name}.png"),
    )
