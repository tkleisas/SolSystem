"""
Bakes the procedural body materials to equirectangular textures.

    blender --background --python tools/blender/bake_bodies.py

The Blender previews and the game client have to agree about what a body looks like, and the way to
guarantee that is to have one implementation of the material, not two. Three of the bodies — the
Sun's photosphere, Ceres' cratered surface, and Earth's cloud deck — are procedural shaders that
exist only inside their .blend files, so the client had nothing to load and drew flat colours.

This bakes exactly those materials, with Blender's own shader, to textures the client can load. The
pattern is evaluated once here instead of every frame there, and neither pipeline has its own copy
of the noise.

**What is not baked, and why.** Mars, the Moon, Mercury and Earth's surface are already *mapped* —
their materials sample an image, so the image is the material and there is nothing to bake. Baking
them would re-encode a texture as a slightly worse texture.

**The UV convention is the client's**, not Blender's default, and it has to be exact or every map is
flipped or rotated half a turn:

    u = atan2(y, x) / 2pi + 0.5          the map's prime meridian at u = 0.5
    v = 1 - polar_angle / pi             v = 0 at +z, because the client reads row zero first

The second line is the one that catches people. Blender's image v runs bottom to top and a loaded
image's v runs top to bottom, so a bake that uses Blender's natural convention arrives upside down.
"""

import math
import os
import sys
import time

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND_DIR = os.path.join(PROJECT, "art", "blend", "bodies")
TEXTURE_DIR = os.path.join(PROJECT, "art", "textures")

# What to bake, and how.
#
#   emit      the shader's own colour, for something that makes its own light
#   diffuse   the base colour only, with the lighting passes off, for a surface
#   coverage  a greyscale mask, for a cloud deck whose alpha is the whole point
#
BAKES = [
    # The Sun makes its own light, so its emission is the whole of it.
    ("sun", "sun_photosphere.png", "emit", 2048, 1024),

    # The surfaces. Mars, the Moon and Mercury are baked even though they are mapped, because their
    # materials do more than sample the map: they tint it, they darken it along the coast, and Mars
    # adds its polar caps by latitude. Baking the material carries all of that in one texture and
    # leaves the client with no second implementation to disagree with.
    ("mars", "mars_surface.png", "diffuse", 2048, 1024),
    ("moon", "moon_surface.png", "diffuse", 2048, 1024),
    ("mercury", "mercury_surface.png", "diffuse", 2048, 1024),
    ("venus", "venus_surface.png", "diffuse", 2048, 1024),
    ("ceres", "ceres_surface.png", "diffuse", 2048, 1024),

    # Earth's cloud deck is a coverage mask, not a colour: what matters is where the clouds are.
    ("earth", "earth_clouds.png", "coverage", 2048, 1024),
]

# The material to bake, and the object it belongs to.
TARGETS = {
    "sun": ("Sun", "SunPhotosphere"),
    "mars": ("Mars", "MarsSurface"),
    "moon": ("Moon", "MoonSurface"),
    "mercury": ("Mercury", "MercurySurface"),
    "venus": ("Venus", "VenusClouds"),
    "ceres": ("Ceres", "CeresRegolith"),
    "earth": ("EarthClouds", "EarthClouds"),
}


def build_equirect_sphere(name, segments=192, rings=96):
    """
    A unit sphere whose UVs are the equirectangular convention the client reads.

    Built by hand rather than with the primitive, because the primitive's UVs put the seam in a
    different place and run v the other way — and a bake with the wrong UVs is a texture that looks
    almost right, which is the worst kind.
    """
    vertices = []
    faces = []
    uvs = []

    for ring in range(rings + 1):
        polar = math.pi * ring / rings
        sin_polar = math.sin(polar)
        cos_polar = math.cos(polar)

        for segment in range(segments + 1):
            # The half-turn lives in the GEOMETRY, not in the UVs. Rotating the sphere so segment
            # zero faces -x puts the map's u = 0 at 180 degrees west, which is where an
            # equirectangular map keeps it, and leaves u running cleanly from 0 to 1. Adding the half
            # turn to u instead sends half the sphere off the edge of the image, and the bake writes
            # a texture that is black down one side and looks like a lighting bug.
            longitude = (2.0 * math.pi * segment / segments) + math.pi

            x = sin_polar * math.cos(longitude)
            y = sin_polar * math.sin(longitude)
            z = cos_polar

            vertices.append((x, y, z))
            # v = 1 at the north pole, so it lands in the top row of the saved PNG — which is row
            # zero when the client reads it back. Getting that backwards is a Mars with its caps at
            # the equator.
            uvs.append((segment / segments, 1.0 - (ring / rings)))

    for ring in range(rings):
        for segment in range(segments):
            a = (ring * (segments + 1)) + segment
            b = a + 1
            c = a + segments + 1
            d = c + 1
            faces.append((a, c, b))
            faces.append((b, c, d))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()

    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        uv_layer.data[loop.index].uv = uvs[loop.vertex_index]

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def bake_one(body, texture, mode, width, height):
    blend = os.path.join(BLEND_DIR, f"{body}.blend")
    if not os.path.exists(blend):
        print(f"  {body}: no {blend}")
        return False

    bpy.ops.wm.open_mainfile(filepath=blend)

    object_name, material_name = TARGETS[body]

    material = bpy.data.materials.get(material_name)
    source = bpy.data.objects.get(object_name)

    if material is None:
        print(f"  {body}: no material called {material_name}")
        return False

    # The bake target is a fresh sphere with known UVs, wearing the body's own material.
    for existing in list(bpy.data.objects):
        if existing.name.startswith("BakeTarget"):
            bpy.data.objects.remove(existing, do_unlink=True)

    target = build_equirect_sphere(f"BakeTarget_{body}")

    # Clear the material slots and put the body's material on, so nothing else contributes.
    target.data.materials.clear()
    target.data.materials.append(material)

    # A bake needs an image node, selected and active, on the material.
    tree = material.node_tree
    # No alpha channel. The bake's alpha is always opaque and means nothing, and a file that
    # carries one makes the packer's "is this already packed?" question unanswerable — it read the
    # meaningless 255 and produced a planet under solid overcast.
    image = bpy.data.images.new(f"bake_{body}", width=width, height=height, alpha=False)
    node = tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    node.select = True
    tree.nodes.active = node

    # The coverage bake needs the shader's alpha, and a shader's alpha is not a pass Cycles bakes.
    # The trick is to route it to an emission colour first and bake the emission: what comes back is
    # a greyscale coverage map, which is what a cloud shell wants to read anyway.
    if mode == "coverage":
        alpha = find_alpha_source(tree)
        if alpha is None:
            print(f"  {body}: no alpha to bake")
            return False

        # Relink the material's EXISTING output at its Surface input rather than adding a second
        # output node and trying to make it active. The bake reads whichever output the material
        # calls active, and setting that from a script is easy to get wrong in a way that fails
        # silently: it bakes the original surface, whose emission strength is zero, and writes a
        # perfectly black texture that looks like a bake that worked.
        output = next(
            (n for n in tree.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output),
            None)

        if output is None:
            print(f"  {body}: no active output to bake through")
            return False

        emission = tree.nodes.new("ShaderNodeEmission")
        tree.links.new(alpha, emission.inputs["Color"])
        tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])

    bpy.context.view_layer.objects.active = target
    target.select_set(True)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 32
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    scene.render.bake.margin = 8

    bake_type = "EMIT" if mode in ("emit", "coverage") else "DIFFUSE"

    print(f"  {body}: baking {bake_type} to {width}x{height}...")
    bpy.ops.object.bake(type=bake_type)

    # Blender's save is not finished when save() returns, so the post-process below would open the
    # PREVIOUS run's file and quietly convert that instead. It did: the cloud mask came out fully
    # opaque because the file being read still held last time's alpha channel. Writing to a scratch
    # name and renaming afterwards removes the question.
    # Blender's save is not finished when save() returns. Post-processing the file immediately
    # therefore reads whatever was there BEFORE — which on a second run is the previous output,
    # and the cloud mask came out fully opaque twice because the file being read still held last
    # time's alpha channel. The bake writes here; the conversion happens at the end of main, once
    # every file has been flushed.
    destination = os.path.join(TEXTURE_DIR, texture)
    image.filepath_raw = destination
    image.file_format = "PNG"
    image.save()

    print(f"  {body}: baked {destination}")
    return True


def find_alpha_source(tree):
    """
    Finds whatever drives a material's transparency.

    A Principled BSDF keeps it in its Alpha input; anything else is assumed to route through a Mix
    Shader's factor. Returning the socket rather than a node keeps the caller from caring which.
    """
    for node in tree.nodes:
        if node.type == "BSDF_PRINCIPLED" and "Alpha" in node.inputs:
            alpha = node.inputs["Alpha"]
            if alpha.is_linked:
                return alpha.links[0].from_socket

    for node in tree.nodes:
        if node.type == "MIX_SHADER" and node.inputs[0].is_linked:
            return node.inputs[0].links[0].from_socket

    return None


def main():
    os.makedirs(TEXTURE_DIR, exist_ok=True)
    print(f"baking to {TEXTURE_DIR}")

    baked = 0
    for body, texture, mode, width, height in BAKES:
        if bake_one(body, texture, mode, width, height):
            baked += 1

    print(f"baked {baked} of {len(BAKES)}")


    # The bakes are raw. Turning them into what a fixed-function renderer can draw is
    # tools/pack_bodies.py, run afterwards under the system Python — because Blender's bundled
    # Python has no imaging library, and a post-processing pass inside this script silently did
    # nothing at all for three runs.
    print("now run: python3 tools/pack_bodies.py")


if __name__ == "__main__":
    main()
