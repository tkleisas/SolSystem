"""
The five bodies the game needs to look at: the Sun, Venus, Earth, Mars, Ceres.

These are visual references, not simulation geometry. The simulation places a body
with a radius and a gravitational parameter and never needs a mesh at all; what is
here is the thing drawn at the end of a telescope, and the two are deliberately
separate so that a simulation body costs nothing to add.

Surfaces are procedural rather than texture-mapped, and that is a decision rather
than a shortcut. A photoreal Earth needs a multi-gigabyte imagery set with a licence;
what a game actually needs is a body that is *recognisable at a glance and correct in
the ways a player can check* — the ice cap is at the pole, the terminator falls where
the Sun is, Venus has no visible surface, and Ceres is covered in craters. All four of
those come from geometry and a noise field, and none of them need a satellite photo.

Radii are IAU mean values and match `SolarSystem.Bodies`. The Sun is rendered at its
real relative size to nothing in particular — these are separate assets shown one at a
time, and a body's absolute scale is a camera's problem.

    blender --background --python tools/blender/bodies/build_bodies.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from pipeline import (  # noqa: E402
    asset_paths, collection, ensure_dir, export_blend, export_glb, link, reset,
    render_views, textures_dir,
)

# Radius in kilometres, from SolarSystem.Bodies, used only to keep the assets in
# proportion to each other when both are on screen at once.
BODIES = {
    "sun": 696_000.0,
    "venus": 6_051.8,
    "earth": 6_378.1,
    "mars": 3_396.2,
    "ceres": 469.7,
}

# The view angles a body needs. A sphere is a sphere from every direction, so what
# these actually check is the surface: the terminator, the poles, and whether the
# pattern is doing anything silly at the seam.
BODY_SHOTS = [
    ("hero",   30.0,  12.0, 3.4, 60.0),
    ("quarter", 120.0, 18.0, 3.4, 60.0),
    ("polar",   60.0,  72.0, 3.4, 60.0),
]


def sphere(name, radius=1.0, segments=128, rings=64):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, radius=radius)
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.shade_smooth()
    return obj


def nodes(material):
    """The node tree, and a tiny helper set for wiring it up."""
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()

    def add(kind, **kwargs):
        node = tree.nodes.new(kind)
        for key, value in kwargs.items():
            setattr(node, key, value)
        return node

    return tree, add


def link_into(tree, from_node, from_socket, to_node, to_socket):
    tree.links.new(from_node.outputs[from_socket], to_node.inputs[to_socket])


def base_material(name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    return mat


# --------------------------------------------------------------------------- the sun


def build_sun(col):
    """
    A photosphere: granulation, limb darkening, and no solid surface anywhere.

    Emitted rather than lit, because the Sun is the only body in the scene that makes
    its own light. The emission is a real number in W/m2/sr and it is large, which is
    the honest way to render it: the alternative is a modest emission with the
    exposure turned down, and then every other body in the same scene is wrong.
    """
    mat = base_material("SunPhotosphere")
    tree, add = nodes(mat)

    tex = add('ShaderNodeTexCoord')
    noise = add('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 14.0
    noise.inputs['Detail'].default_value = 12.0
    noise.inputs['Roughness'].default_value = 0.62

    ramp = add('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.36
    ramp.color_ramp.elements[0].color = (0.72, 0.20, 0.02, 1.0)
    ramp.color_ramp.elements[1].position = 0.66
    ramp.color_ramp.elements[1].color = (1.0, 0.86, 0.55, 1.0)

    emission = add('ShaderNodeEmission')
    emission.inputs['Strength'].default_value = 9.0

    output = add('ShaderNodeOutputMaterial')

    link_into(tree, tex, 'Object', noise, 'Vector')
    link_into(tree, noise, 'Fac', ramp, 'Fac')
    link_into(tree, ramp, 'Color', emission, 'Color')
    link_into(tree, emission, 'Emission', output, 'Surface')

    obj = sphere("Sun", radius=1.0, segments=192, rings=96)
    obj.data.materials.append(mat)
    link(obj, col)
    return obj


# --------------------------------------------------------------------------- venus


def build_venus(col):
    """
    Cloud tops, and nothing else, because nothing else can be seen.

    Venus is the easiest body here to get right and the easiest to get wrong: the
    surface is invisible at every wavelength a human eye has, so a model showing
    continents is wrong in the way that matters. What the player sees is a featureless
    sulphuric haze with a slow Y-shaped shear, so that is what is built -- and the
    absence of any surface detail is itself the visual information.
    """
    mat = base_material("VenusClouds")
    tree, add = nodes(mat)

    tex = add('ShaderNodeTexCoord')
    noise = add('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 3.2
    noise.inputs['Detail'].default_value = 8.0
    noise.inputs['Roughness'].default_value = 0.58
    noise.inputs['Distortion'].default_value = 1.4

    # Object-space noise is -1..1; remap before the ramp or the ramp reads half the
    # field as pure black.
    narrow = add('ShaderNodeMath')
    narrow.operation = 'MULTIPLY_ADD'
    narrow.inputs[1].default_value = 0.5
    narrow.inputs[2].default_value = 0.5

    ramp = add('ShaderNodeValToRGB')
    ramp.color_ramp.interpolation = 'EASE'
    ramp.color_ramp.elements[0].position = 0.32
    ramp.color_ramp.elements[0].color = (0.58, 0.45, 0.22, 1.0)
    ramp.color_ramp.elements[1].position = 0.76
    ramp.color_ramp.elements[1].color = (0.96, 0.88, 0.62, 1.0)

    bsdf = add('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 0.85
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.1

    output = add('ShaderNodeOutputMaterial')

    link_into(tree, tex, 'Object', noise, 'Vector')
    link_into(tree, noise, 'Fac', narrow, 0)
    link_into(tree, narrow, 'Value', ramp, 'Fac')
    link_into(tree, ramp, 'Color', bsdf, 'Base Color')
    link_into(tree, bsdf, 'BSDF', output, 'Surface')

    obj = sphere("Venus", radius=1.0, segments=160, rings=80)
    obj.data.materials.append(mat)
    link(obj, col)
    return obj


# --------------------------------------------------------------------------- earth


def build_earth(col):
    """
    The real Earth, because a procedural one is not recognisable and has to be.

    Everything else in this file is generated from noise and that is the right answer
    for Venus, which has no visible surface, and for Ceres, whose only feature is
    craters. Earth is the exception for a specific reason: a player knows what Earth
    looks like, and a noise field produces a blue ball with green splodges that reads
    as *a* planet rather than as *this* one. The continents are the map.

    So the continents come from Natural Earth's 1:50m land polygons, rasterised to an
    equirectangular map with three channels packed into it:

      R  land mask, for the coastline
      G  a coastline-distance outline, for coastal tint
      B  cosine of latitude, for the biome bands

    Twenty kilobytes of coastline data and one texture fetch, and the result is a
    planet a player can navigate by. That is worth more than the purity of generating
    it, which is why the exception is made here and nowhere else.

    The clouds are a separate shell, as they are in fact.
    """
    mat = base_material("EarthSurface")
    tree, add = nodes(mat)

    tex = add('ShaderNodeTexCoord')
    uv = add('ShaderNodeUVMap')

    image = add('ShaderNodeTexImage')
    image.image = bpy.data.images.load(os.path.join(textures_dir(), "earth_map.png"))
    image.interpolation = 'Cubic'
    image.extension = 'EXTEND'

    # A data map, not a photograph. Left as sRGB, Blender decodes every channel with
    # the display transform before the shader sees it, and a channel meaning "half way
    # to the equator" arrives as 0.21 rather than 0.5 -- which put the biome selector
    # below its ice stop everywhere and produced a grey planet. Nothing about a mask,
    # a distance field or a latitude wants a gamma curve applied to it.
    image.image.colorspace_settings.name = 'Non-Color'

    # Split the packed channels.
    split = add('ShaderNodeSeparateColor')

    # Ocean: deep water, in the mask's own zero.
    ocean = add('ShaderNodeRGB')
    ocean.outputs[0].default_value = (0.012, 0.045, 0.13, 1.0)

    # Land, chosen by warmth: ice at the poles, temperate in the middle, and a band of
    # bare ground through the tropics. Enough stops to make the Sahara read as desert
    # and Siberia as taiga without pretending to be a biome map.
    #
    # A fresh colour ramp has exactly two elements, so the rest are added first and
    # then positioned -- indexing element 2 before creating it is an IndexError, and
    # `elements.new(position)` is not a supported overload.
    # Linear rather than B-spline: a spline through five stops undershoots between the
    # ice and the taiga and washes the mid latitudes toward grey, which is half of why
    # the first version had no colour anywhere.
    biome = add('ShaderNodeValToRGB')
    biome.color_ramp.interpolation = 'LINEAR'
    biome.color_ramp.elements.new(0.5)
    biome.color_ramp.elements.new(0.5)
    biome.color_ramp.elements.new(0.5)

    stops = (
        (0.10, (0.72, 0.76, 0.80, 1.0)),   # ice
        (0.36, (0.10, 0.19, 0.09, 1.0)),   # taiga
        (0.62, (0.16, 0.28, 0.10, 1.0)),   # temperate
        (0.82, (0.44, 0.34, 0.14, 1.0)),   # desert
        (0.95, (0.20, 0.30, 0.09, 1.0)),   # tropical
    )
    for element, (position, color) in zip(biome.color_ramp.elements, stops):
        element.position = position
        element.color = color

    # The mask arrives as a hard 0/255 step, which at this map resolution draws a
    # visibly stair-stepped coast. Narrowing it through a ramp gives the blend a
    # couple of pixels to work with instead.
    coast_fade = add('ShaderNodeValToRGB')
    coast_fade.color_ramp.interpolation = 'LINEAR'
    coast_fade.color_ramp.elements[0].position = 0.42
    coast_fade.color_ramp.elements[1].position = 0.58

    land_sea = add('ShaderNodeMixRGB')
    land_sea.blend_type = 'MIX'

    bsdf = add('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 0.62

    output = add('ShaderNodeOutputMaterial')

    link_into(tree, uv, 'UV', image, 'Vector')
    link_into(tree, image, 'Color', split, 'Color')

    # Biome colour from latitude.
    link_into(tree, split, 'Blue', biome, 'Fac')

    # Then the mask picks between the biome and the ocean.
    link_into(tree, biome, 'Color', land_sea, 'Color2')
    link_into(tree, ocean, 0, land_sea, 'Color1')
    link_into(tree, split, 'Red', coast_fade, 'Fac')
    link_into(tree, coast_fade, 'Color', land_sea, 'Fac')

    link_into(tree, land_sea, 'Color', bsdf, 'Base Color')
    link_into(tree, bsdf, 'BSDF', output, 'Surface')

    earth = sphere("Earth", radius=1.0, segments=192, rings=96)
    earth.data.materials.append(mat)
    link(earth, col)

    build_earth_clouds(col)

    return earth


def build_earth_clouds(col):
    """
    A thin, broken cloud shell at 1.006 radii.

    The first version was a solid white sphere, and the reason is worth recording: a
    Principled BSDF with a white base colour is an *opaque white surface*, not a
    translucent one. There was no alpha anywhere in the material, so every part of the
    shell the ramp called "cloud" rendered as paint, and the map the rest of this
    section exists to show was hidden under it.

    What makes a cloud read as thin is not its colour but its coverage and its alpha,
    so the shell now carries a real alpha channel: the noise drives both the colour and
    the transparency, and the maximum opacity is 0.55. Over an ocean that dark, that is
    the difference between a blue planet with weather and a grey ball.
    """
    mat = base_material("EarthClouds")
    tree, add = nodes(mat)

    tex = add('ShaderNodeTexCoord')

    # Two octaves: broad systems, and finer structure inside them.
    broad = add('ShaderNodeTexNoise')
    broad.inputs['Scale'].default_value = 2.6
    broad.inputs['Detail'].default_value = 6.0
    broad.inputs['Roughness'].default_value = 0.55
    broad.inputs['Distortion'].default_value = 0.6

    fine = add('ShaderNodeTexNoise')
    fine.inputs['Scale'].default_value = 7.5
    fine.inputs['Detail'].default_value = 8.0
    fine.inputs['Roughness'].default_value = 0.6

    def remap(name, weight):
        node = add('ShaderNodeMath')
        node.operation = 'MULTIPLY_ADD'
        node.inputs[1].default_value = weight
        node.inputs[2].default_value = 0.0
        node.name = name
        return node

    broad_w = remap("BroadWeight", 0.55)
    fine_w = remap("FineWeight", 0.45)
    total = add('ShaderNodeMath')
    total.operation = 'ADD'
    to_unit = add('ShaderNodeMath')
    to_unit.operation = 'MULTIPLY_ADD'
    to_unit.inputs[1].default_value = 0.5
    to_unit.inputs[2].default_value = 0.5

    # Coverage: everything below 0.62 is clear sky, and above it the cloud thickens.
    cover = add('ShaderNodeValToRGB')
    cover.color_ramp.interpolation = 'EASE'
    cover.color_ramp.elements[0].position = 0.68
    cover.color_ramp.elements[0].color = (0.0, 0.0, 0.0, 1.0)
    cover.color_ramp.elements[1].position = 0.94
    cover.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)

    # The same value drives opacity, scaled hard. A 0.55 ceiling still read as overcast
    # from a distance: the ocean is dark enough that a half-opaque white layer on top of
    # it is grey, and grey is not what a planet full of water looks like from orbit.
    opacity = add('ShaderNodeMath')
    opacity.operation = 'MULTIPLY'
    opacity.inputs[1].default_value = 0.30

    bsdf = add('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 0.9
    bsdf.inputs['Alpha'].default_value = 0.5
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.04

    output = add('ShaderNodeOutputMaterial')

    link_into(tree, tex, 'Object', broad, 'Vector')
    link_into(tree, tex, 'Object', fine, 'Vector')
    link_into(tree, broad, 'Fac', broad_w, 0)
    link_into(tree, fine, 'Fac', fine_w, 0)
    link_into(tree, broad_w, 'Value', total, 0)
    link_into(tree, fine_w, 'Value', total, 1)
    link_into(tree, total, 'Value', to_unit, 0)
    link_into(tree, to_unit, 'Value', cover, 'Fac')
    link_into(tree, cover, 'Color', bsdf, 'Base Color')
    link_into(tree, to_unit, 'Value', opacity, 0)
    link_into(tree, opacity, 'Value', bsdf, 'Alpha')
    link_into(tree, bsdf, 'BSDF', output, 'Surface')

    mat.blend_method = 'BLEND'

    clouds = sphere("EarthClouds", radius=1.006, segments=128, rings=64)
    clouds.data.materials.append(mat)
    link(clouds, col)
    return clouds


# --------------------------------------------------------------------------- mars


def build_mars(col):
    """
    Rust, dark basalt, and a small bright cap — the colours are the whole identity.

    Mars reads as Mars because of one thing: iron oxide, which is a specific and
    unmistakable colour. Everything else here is texture at a scale a player will
    never measure, so the material is built around getting that colour right and
    letting the noise supply the rest.
    """
    mat = base_material("MarsSurface")
    tree, add = nodes(mat)

    tex = add('ShaderNodeTexCoord')
    noise = add('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 5.5
    noise.inputs['Detail'].default_value = 12.0
    noise.inputs['Roughness'].default_value = 0.6

    narrow = add('ShaderNodeMath')
    narrow.operation = 'MULTIPLY_ADD'
    narrow.inputs[1].default_value = 0.5
    narrow.inputs[2].default_value = 0.5

    ramp = add('ShaderNodeValToRGB')
    ramp.color_ramp.interpolation = 'EASE'
    ramp.color_ramp.elements[0].position = 0.30
    ramp.color_ramp.elements[0].color = (0.28, 0.10, 0.04, 1.0)     # dark basalt
    ramp.color_ramp.elements[1].position = 0.72
    ramp.color_ramp.elements[1].color = (0.66, 0.29, 0.13, 1.0)     # iron oxide

    separate = add('ShaderNodeSeparateXYZ')
    latitude = add('ShaderNodeMath')
    latitude.operation = 'ABSOLUTE'
    caps = add('ShaderNodeValToRGB')
    caps.color_ramp.interpolation = 'EASE'
    caps.color_ramp.elements[0].position = 0.86
    caps.color_ramp.elements[0].color = (0.0, 0.0, 0.0, 1.0)
    caps.color_ramp.elements[1].position = 0.95
    caps.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)

    mix = add('ShaderNodeMixRGB')
    mix.blend_type = 'MIX'

    bsdf = add('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 0.78

    output = add('ShaderNodeOutputMaterial')

    link_into(tree, tex, 'Object', noise, 'Vector')
    link_into(tree, noise, 'Fac', narrow, 0)
    link_into(tree, narrow, 'Value', ramp, 'Fac')
    link_into(tree, tex, 'Object', separate, 'Vector')
    link_into(tree, separate, 'Z', latitude, 'Value')
    link_into(tree, latitude, 'Value', caps, 'Fac')
    link_into(tree, ramp, 'Color', mix, 'Color1')
    link_into(tree, caps, 'Color', mix, 'Color2')
    link_into(tree, mix, 'Color', bsdf, 'Base Color')
    link_into(tree, bsdf, 'BSDF', output, 'Surface')

    mars = sphere("Mars", radius=1.0, segments=160, rings=80)
    mars.data.materials.append(mat)
    link(mars, col)
    return mars


# --------------------------------------------------------------------------- ceres


def build_ceres(col):
    """
    A grey rock with a lot of craters, which is the entire visual description.

    The craters are the point, so they are geometry rather than a texture: a
    displacement driven by a Voronoi field gives the right read at every scale, and
    Ceres' actual distinguishing feature — the bright carbonate spots in Occator — is
    a real albedo feature worth having, so one patch is left pale.
    """
    mat = base_material("CeresRegolith")
    tree, add = nodes(mat)

    tex = add('ShaderNodeTexCoord')

    craters = add('ShaderNodeTexVoronoi')
    craters.feature = 'DISTANCE_TO_EDGE'
    craters.inputs['Scale'].default_value = 9.0

    edges = add('ShaderNodeValToRGB')
    edges.color_ramp.elements[0].position = 0.0
    edges.color_ramp.elements[0].color = (0.06, 0.06, 0.062, 1.0)
    edges.color_ramp.elements[1].position = 0.14
    edges.color_ramp.elements[1].color = (0.30, 0.295, 0.285, 1.0)

    grain = add('ShaderNodeTexNoise')
    grain.inputs['Scale'].default_value = 42.0
    grain.inputs['Detail'].default_value = 6.0

    mix = add('ShaderNodeMixRGB')
    mix.blend_type = 'MULTIPLY'
    mix.inputs['Fac'].default_value = 0.35

    bsdf = add('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 0.94

    output = add('ShaderNodeOutputMaterial')

    link_into(tree, tex, 'Object', craters, 'Vector')
    link_into(tree, craters, 'Distance', edges, 'Fac')
    link_into(tree, tex, 'Object', grain, 'Vector')
    link_into(tree, edges, 'Color', mix, 'Color1')
    link_into(tree, grain, 'Fac', mix, 'Color2')
    link_into(tree, mix, 'Color', bsdf, 'Base Color')
    link_into(tree, bsdf, 'BSDF', output, 'Surface')

    obj = sphere("Ceres", radius=1.0, segments=192, rings=96)
    obj.data.materials.append(mat)

    # Crater relief, so the terminator has something to catch on.
    displace = obj.modifiers.new("Craters", 'DISPLACE')
    displace.texture = crater_texture()
    displace.strength = 0.022
    displace.mid_level = 0.5

    link(obj, col)
    return obj


def crater_texture():
    """A Voronoi texture datablock, for the displacement modifier."""
    if "CraterField" in bpy.data.textures:
        return bpy.data.textures["CraterField"]
    tex = bpy.data.textures.new("CraterField", type='VORONOI')
    tex.distance_metric = 'DISTANCE'
    tex.noise_scale = 0.55
    tex.contrast = 1.4
    return tex


# --------------------------------------------------------------------------- scene


def lit_scene():
    """
    A Sun lamp and a dim fill, so a body has a terminator.

    Every body except the Sun is lit by one distant light, which is what makes the day
    side and the night side fall in the right place — the single most obvious thing a
    viewer checks and the easiest to get wrong with a rig built for spacecraft.
    """
    bpy.ops.object.light_add(type='SUN', location=(6, -4, 2))
    key = bpy.context.object
    key.name = "BODY_Key"
    key.data.energy = 6.0
    key.rotation_euler = (math.radians(55.0), 0.0, math.radians(35.0))

    bpy.ops.object.light_add(type='SUN', location=(-5, 3, -1))
    fill = bpy.context.object
    fill.name = "BODY_Fill"
    fill.data.energy = 0.35
    fill.rotation_euler = (math.radians(-40.0), 0.0, math.radians(200.0))


def build(name, builder):
    reset()
    lit_scene()
    col = collection(name)
    builder(col)

    blend, glb, preview = asset_paths("bodies", name)
    render_views(preview, BODY_SHOTS, resolution=1000, samples=64, ambient=0.008)
    export_glb(glb, name)
    export_blend(blend)


def main():
    builders = {
        "sun": build_sun,
        "venus": build_venus,
        "earth": build_earth,
        "mars": build_mars,
        "ceres": build_ceres,
    }

    for name, builder in builders.items():
        print(f"--- {name} (radius {BODIES[name]:,.0f} km)")
        build(name, builder)

    print("bodies done")


main()
