# The model pipeline

Procedural Blender scripts. Nothing here is sculpted by hand: every asset is a
script that reads like a specification, which is what keeps a fleet of ships
consistent, keeps them re-derivable when a number changes, and means the radiator is
drawn at the size the physics asks for rather than at the size that looks good.

That last point is not a figure of speech. The radiator is a *ratio* of the ship —
`A/m = (a·vₑ/2)(1-η)/η / (2σT⁴) · areal` — so choosing a cruise acceleration fixes
what fraction of the hull is radiator, and the model has to live with the answer.
It is why the workers' freighter has four wings and the Illuminus courier has two
small panels: the courier is not in a hurry.

## Running it

```sh
B=/home/tkleisas/blender/blender-5.2.2-linux-x64/blender
$B --background --python tools/blender/ships/illuminus_courier.py
$B --background --python tools/blender/ships/workers_freighter.py
$B --background --python tools/blender/bodies/build_bodies.py
```

Each script writes three things and prints the numbers it was built from, including
the thermal budget beside the geometry — a model is a claim about a ship, and that
line is where the claim is checked.

| Output | What it is |
|---|---|
| `art/blend/<category>/<name>.blend` | The source. Re-runnable, so it is a cache rather than the truth |
| `art/models/<category>/<name>.glb` | Binary glTF, which is what the client loads |
| `art/previews/<category>/<name>_<view>.png` | Six views: four three-quarter, an elevation, and one from above |
| `art/previews/<category>/<name>.png` | A contact sheet of the six, for judging a shape at a glance |

## Why six views

One three-quarter view flatters a model and hides its problems: a floating module
reads as attached, a panel buried in a tank reads as beside it, and a silhouette that
only works from one direction looks finished. The set is deliberately redundant — the
elevation is there because it has no perspective and therefore cannot lie about where
a part sits.

A true plan view was removed after it caused a misreading. An upright ship seen from
directly above is a circle with its nose pointing at the camera, so anything beside it
appears to be a separate object flying in formation; the courier's radiator was
reported, reasonably, as an illuminated thing outside the ship. The replacement looks
down at 50 degrees, which shows the same surfaces without the ambiguity.

## Conventions

* **Metres, and no scaling.** Blender's unit is a metre, so a hull dimension in the
  script is the hull dimension.
* **Nose along +z, engine plane at the origin.** A ship's origin is where its thrust
  is, and `export_yup=True` converts to the glTF convention on the way out.
* **Layout as data.** Every z position on the workers' freighter comes from one stack
  of named ranges, because a module that floats two metres clear of the hull is
  invisible in a three-quarter view and obvious in an elevation.
* **No emissive geometry in an export.** Engine exhaust belongs in the client as an
  effect. A cone of emissive material has no mass and no silhouette, and as a solid
  glowing object it reads as a separate lit thing rather than as thrust.
* **Materials are authored in linear RGB.** Blender colour-manages on output; picking
  values by eye in display space is how a model arrives in the engine washed out.

## Files

| File | Purpose |
|---|---|
| `pipeline.py` | Primitives, lathe, materials, lighting rigs, the six-view renderer, glTF export |
| `ships/illuminus_courier.py` | 55 m courier, Starship ancestry, sleek by way of a low cruise acceleration |
| `ships/workers_freighter.py` | 143 m freighter, Soviet and Chinese heavy engineering, wings because radiators need sky |
