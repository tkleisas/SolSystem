# Blender 5.2 API notes

Things that changed in Blender 5 and cost time here, recorded so they cost it once.
Every one of them fails with an error that reads like a version problem and is
actually a rename or a move.

## The compositor is a node group

`scene.node_tree` **does not exist**. It is `scene.compositing_node_group`, and it is a
node group datablock that has to be created and assigned:

```python
tree = bpy.data.node_groups.new("Glare", 'CompositorNodeTree')
scene.compositing_node_group = tree
```

Consequences:

* There is no `CompositorNodeComposite`. The terminal node is `NodeGroupOutput`.
* That node carries **one unnamed input socket**, and the name cannot be set on the
  node — a group's sockets belong to the tree's `interface`, not to the node. Address
  it by index: `tree.links.new(glare.outputs['Image'], composite.inputs[0])`. Asking
  for `inputs['Image']` is a `KeyError` even though the socket is plainly there.
* `scene.use_nodes` is deprecated and slated for removal in 6.0.

## Node settings moved into input sockets

The glare node has **no RNA properties** for its settings any more. `node.glare_type`,
`node.quality`, `node.threshold`, `node.size` and `node.mix` all raise
`AttributeError`; they are input sockets now, and the names are capitalised —
`inputs['Type']`, `inputs['Quality']`, `inputs['Threshold']`, `inputs['Size']`,
`inputs['Strength']`.

## MENU sockets cannot be introspected

A socket of type `MENU` has **no `enum_items`**: the usual introspection raises or
returns an empty list, so the valid values cannot be read off it. Worse, it is set by
the *displayed* name and rejects the identifier form the C source uses:

```python
glare.inputs['Type'].default_value = 'FOG_GLOW'   # TypeError
glare.inputs['Type'].default_value = 'Fog Glow'   # works
```

`pipeline.set_menu` tries both forms and verifies the assignment, so a wrong name
fails loudly rather than silently doing nothing.

## Colour management and emissive surfaces

The default view transform is **AgX**, which is built to roll bright values smoothly
toward white. That is right for a photograph of a lit scene and wrong for a body that
emits its own light: under AgX the Sun's photosphere desaturates to a grey disc no
matter what colour the shader outputs. Anything emissive wants `Standard`.

Related, and the reason two attempts at the solar shader came out white: **anything
driven past 1.0 clips and takes its colour with it**. An emissive surface has to be
authored to sit *inside* the display range, which means the emission strength is a
display decision as much as a physical one.

## Texture coordinates

A **Generated** or **Object** space coordinate runs **-1 to 1**, not 0 to 1. A noise
field sampled on one spends half its range on negative values, so a colour ramp whose
stops sit at 0.5 gives a hard black-and-white split with nothing in between. Remap
with a `MULTIPLY_ADD` of 0.5 then 0.5 before any ramp.

## Images and colour space

Blender decodes an image as sRGB before the shader sees it. On a **data map** — a
mask, a distance field, a latitude — that is wrong: a channel meaning "half way"
arrives as 0.21, which quietly moves every threshold. Set
`image.colorspace_settings.name = 'Non-Color'`.

## Removed or renamed nodes

* `ShaderNodeTexMusgrave` is gone. Use `ShaderNodeTexNoise`, which gained Musgrave's
  `Lacunarity`, `Offset` and `Gain` inputs.
* `CompositorNodeMixRGB` is gone; the replacement is `ShaderNodeMix`.
* `ShaderNodeMixRGB` still exists in shader trees but is legacy — it reports as
  `Mix (Legacy)`.

## Finding the truth

The shipped UI scripts are the most reliable reference for what a node offers, because
they are what the user interface itself calls:

```sh
grep -rn "CompositorNodeGlare" /path/to/blender/5.2/scripts/startup/bl_ui/
```

Failing that, enumerate the sockets rather than guessing at attributes:
`[s.name for s in node.inputs]`.

## The glTF exporter drops a multiplied base colour

A Principled **Base Color** fed by `Image Texture → MixRGB (MULTIPLY) → Base Color` is
exported as the texture alone: **`baseColorFactor` is not written**. The exporter
recognises an image linked to the socket, not the colour beside it, so a hull whose
preview is white steel and dark trim exports as white steel and white trim. The
workaround here is deliberate and lives in one place: the Blender preview keeps the
multiply, and `pipeline._restore_base_colors` writes the factor back into the exported
GLB. A direct `Image Texture → Base Color` does export, and that is the version that
loses all colour.

Relatedly, glTF clamps `emissiveFactor` to 0..1. Emissive strengths above one are
written as the `KHR_materials_emissive_strength` extension, so a reader that ignores
the extension draws a beacon at 1/8 of its authored brightness.

## `uv.cube_project` in background mode

The operator needs a mesh in **Edit** mode with an active object and a selected mesh,
and an existing UV layer; it does not create one itself. The shape that works here:

```python
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
if not obj.data.uv_layers:
    obj.data.uv_layers.new(name="UVMap")
bpy.ops.uv.cube_project(cube_size=6.0, correct_aspect=True, scale_to_bounds=False)
bpy.ops.object.mode_set(mode='OBJECT')
```

`cube_size` is in metres and is the physical size of one texture repeat, which is what
makes a tiled plate map keep its scale from a 55 m courier to a 2 km station.
