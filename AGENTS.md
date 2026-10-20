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

Most bodies are **baked to textures** by `bake_bodies.py` because most of the client
is fixed-function. The Earth is the exception on both counts: its day map and cloud
alpha are real NASA imagery shared by the preview and the client, and it is drawn by
`Content/Planet.fx` rather than the fixed-function sphere. That shader owns the
terminator, the Black Marble city lights (`art/textures/earth_night.jpg`), the cloud
deck (sampled in the same shader so it shares the terminator) and the 2 % atmosphere
shell. The other bodies still use the baked spheres.

## Probes

```sh
dotnet run -c Release --no-build --project src/SolSystem.Probe -- \
    --probe tools/probe/docking.probe --probe-out artifacts/probe/docking.txt
```

Exit code 0 is clean, 1 a command failed, 2 a check failed, 3 the script could not be
read. The transcript is written even on failure.

**The `days` command chooses its step by what it is advancing.** With no ship, the
stations are circular and a day is one exact rotation (`Station.AdvanceCoarse`); the
station-keeping and sky probes are seconds. With a ship, the approach is an action span
and `days` steps at 120 Hz — a station costs about 2.7 µs a tick there, and the ship
path still clamps at `int.MaxValue` ticks (about 207 days), so do not ask it for years.

| Probe | Span | Cost |
|---|---|---|
| `docking.probe` | ~16 hours | seconds |
| `station-keeping.probe` | 10 days | under a second |
| `sky.probe` | 182.6 days | about a second |

The sky probe is cheap now, but its summer half still has no sun-altitude check: the
winter site's noon is measured, the summer one is a screenshot.

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
* **The local map is a view, not a pause.** `L` opens the Hill frame of the home station:
  +x along the orbit, +y radially outward, the Earth below. The map keeps running and the ship's
  controls stay live; `M` is the solar chart and `T` the market. Do not make it a menu.
* **The market moves, and the drives are a table.** Prices are a function of the session age and
  the pilot's own trades (`Market.Update(days)`, `TradePressure` per tonne, decay over 0.75 day);
  never a wall clock or a random number generator. `--drive workers|illuminus` chooses the engine:
  1,200 km/s at 5.5 kN or 600 km/s at 9.0 kN. The default is Illuminus; the tests and probes that
  call `Hulls.Courier` without an engine get the reference Workers torch.
* **The hold is part of the ship's mass.** A tonne bought at the market is a tonne of dry mass:
  `FlightGame.SyncShipMass` sets `Mass = hull + hold + propellant` after every trade, refuel and
  delivery. The contract cargo is loaded at the start and removed only by `Contract.SettleDelivery`;
  a failed run leaves it aboard. The market opens only when the docking law says the ship is at
  the port, and the ship's controls are suppressed while it is open.
* **The second site is co-orbital, not another world.** The target is a
  `Station.CoOrbital` of Meridian, a milliradian ahead — 6.8 km, 7.7 m/s of relative drift.
  The home station stays the observer anchor and the ship's start; the target owns the
  approach, nav, panel, gates and mission. The contract's deadline is the session clock; do
  not add a second timer.
* **The narrator schedules lines on the audio clock.** A line is synthesized in full, its
  duration is used for the comm log's typing rate, and `TryTakeStartedLine` releases it when
  the audio starts. Do not write narrator text from the voice thread or before playback.
