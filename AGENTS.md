# Working notes

SolSystem is a .NET 10 simulation with three faces: a fixed-point core, a MonoGame
client, and a headless probe harness. `README.md` is the front door, `DESIGN.md` is the
reference, and `docs/` holds the measurements the design cites.

## Build and test

```sh
dotnet build -c Release
dotnet test  -c Release --no-build
```

`NuGet.config` redirects the package cache into `artifacts/`, so a restore never writes
outside the repository.

## Blender and the models

Blender 5.2.2 is installed at:

```sh
/home/tkleisas/blender/blender-5.2.2-linux-x64/blender
```

Every hull, station and body is a script in `tools/blender/`; nothing is modelled by
hand. Run the script, not the saved `.blend`:

```sh
B=/home/tkleisas/blender/blender-5.2.2-linux-x64/blender

# The tileable detail maps the hulls wear. Rerun after changing a pattern.
python3 tools/blender/hull_textures.py

$B --background --python tools/blender/ships/illuminus_courier.py
$B --background --python tools/blender/ships/workers_freighter.py
$B --background --python tools/blender/stations/meridian.py
$B --background --python tools/blender/stations/illuminus_disk.py
$B --background --python tools/blender/stations/workers_modules_spine.py
$B --background --python tools/blender/stations/workers_modules_radial.py
$B --background --python tools/blender/bodies/build_bodies.py
```

Each run writes three things and prints the numbers it was built from:

| Output | What it is |
|---|---|
| `art/blend/<category>/<name>.blend` | The scene the script built. A cache, not the truth |
| `art/models/<category>/<name>.glb` | What the client loads |
| `art/previews/<category>/<name>_<view>.png` | Six views, plus a contact sheet |

`tools/blender/pipeline.py` has the shared primitives, materials, lighting rigs and
renderer. `tools/blender/NOTES-blender5-api.md` records the Blender 5 API traps that
cost time once already — read it before writing a shader or a compositor node.

Every ship and station now carries a tileable grayscale detail map — plate seams,
rivets, wear — multiplied into its non-emissive materials and box-projected at a
fixed world scale (`pipeline.apply_detail_texture`, `pipeline.uv_cube`). Blender's
glTF exporter drops the colour through a Mix node, so `pipeline._restore_base_colors`
patches `baseColorFactor` back into the GLB after export; **do not remove that step**
or every texture becomes monochrome. The client reads
`KHR_materials_emissive_strength`, which is how the station's windows and floods keep
the strengths they were authored with.

The bodies are **baked to textures** by `bake_bodies.py` because the client is
fixed-function. The Earth is the exception to the old all-procedural rule: its day
map and cloud alpha are real NASA imagery, shared by the preview and the client.

## Probes

```sh
dotnet run -c Release --no-build --project src/SolSystem.Probe -- \
    --probe tools/probe/docking.probe --probe-out artifacts/probe/docking.txt
```

Exit code 0 is clean, 1 a command failed, 2 a check failed, 3 the script could not be
read. The transcript is written even on failure.

**The `days` command steps the world at the 120 Hz navigation tick.** A station costs
about 2.7 µs a tick, so one simulated day is roughly 27 s of CPU. Some costs measured on
this machine:

| Probe | Span | Cost |
|---|---|---|
| `docking.probe` | ~16 hours | seconds |
| `station-keeping.probe` | 10 days | about ten minutes |
| `sky.probe` | 182.6 days | **hours**, not minutes |

Do not reach for `sky.probe` as a quick check. Its summer half is also unasserted: the
site triad's hour angle against the ephemeris is real sky work that is not done, which
the script itself records.

## Ground rules that are load-bearing

* **Core is fixed point.** No `float` or `double` in `SolSystem.Core` except the
  ephemeris and the boundaries documented in `README.md`.
* **Tests check against independent references**, not hand-typed expectations.
* **`Repository.Root` is the one repository-root walk** (`src/SolSystem.Core/Repository.cs`).
  Do not add another.
* **Art is generated.** Change the script and re-run it; never hand-edit a `.glb`, a
  `.blend` or a preview.
* **The unlit side of a hull is dark on purpose.** One sun and earthshine, no fill light;
  a hull on the night side is nearly black, and the radiators and windows are the
  exceptions because they emit. The opening frame is deliberately on the day side — see
  the next line — but that does not make the dark side a bug.
* **The client opens on the 2000 March equinox, JD 2451623.80**, not J2000. At that
  instant Earth is on the ecliptic's −x axis, so the Sun is along +x, which is where
  Meridian's offset and corridor already point: the station is between Earth and Sun and
  the opening view looks back at a fully lit Earth. The chase camera adds a 20° look
  offset so the Earth is in frame; the cockpit starts along the nose. `--at` overrides it.
* **BasicEffect's light direction is the direction the light travels**, not the direction
  towards the light; the shader computes `dot(-lightDirection, normal)`. The renderers
  take a "towards the Sun" vector and negate it at the one place the convention changes.
* **Overlays and stars project through the camera's orthonormal screen basis.** The deck is
  not the screen up when the camera is pitched; `Camera.Build` returns the orthogonalised
  up, and `NavOverlay`/`SkyRenderer` must use it. The Sun is a screen-space quad for the
  same reason — a world-space billboard projects as a trapezoid off-axis.
* **The narrator schedules lines on the audio clock.** A line is synthesized in full, its
  duration is used for the comm log's typing rate, and `TryTakeStartedLine` releases it when
  the audio starts. Do not write narrator text from the voice thread or before playback.
