# Tools

## `plot_csv.py`

Plots a CSV as an SVG. No dependencies — it was written because the obvious library was
unavailable and installing it was blocked, which turned out to be better: it renders anywhere
and the output is a text file that can be committed and diffed.

```sh
python3 tools/plot_csv.py /tmp/approach.csv --x tick \
    --panels "range@symlog,closing,throttle,nose_x" \
    --marks "2:capture envelope,0.5:latch limit" \
    --bands phase --title "docking approach" --out /tmp/approach.svg
```

Panels are stacked and share an x axis. A panel may be `column`, `column@log` or
`column@symlog`; `@symlog` is usually what a range needs, because an approach that starts at two
kilometres and has to be judged in its last two metres is unreadable on a linear scale.

`--bands <column>` shades the background by a categorical column. That is how a phase machine is
best read: the question is never what the number was, but what the law was *doing* when the
number went wrong.

The SVG rasterises with any browser:

```sh
google-chrome --headless --disable-gpu --no-sandbox --window-size=1200,760 \
    --screenshot=plot.png file:///tmp/approach.svg
```

### Why it exists

Seven wrong diagnoses in `Approach.cs` came from reading traces a line at a time. The plot found
the real one in a single look: the range fell to two metres and then climbed to a hundred
kilometres with the throttle pinned at maximum. Every number in the trace had been
self-consistent — only the shape showed the sign was wrong.

Any test can emit a CSV the same way; `ApproachTests.DumpTheApproach` is the worked example.

```sh
APPROACH_CSV=/tmp/approach.csv dotnet test --filter DumpTheApproach
```

## `pack_stars.py` and `draw_sky.py`

```sh
# Fetch once, then pack. HYG v4.1 is 34 MB of CSV; the game ships 188 kB of fixed-point records.
curl -sL -o /tmp/hyg.csv https://raw.githubusercontent.com/astronexus/HYG-Database/main/hyg/CURRENT/hygdata_v41.csv
python3 tools/pack_stars.py --source /tmp/hyg.csv --out art/sky/stars.bin

# Draw a sky. The probe writes the data; this draws it.
dotnet run -c Release --project src/SolSystem.Probe -- --probe tools/probe/sky.probe
python3 tools/draw_sky.py /tmp/sky-greenwich-winter.csv --out /tmp/sky.svg \
    --title "Greenwich, 2000 January 1"
```

`pack_stars.py` keeps everything brighter than magnitude 6.5 plus everything within 25 parsecs
whatever its magnitude — Proxima Centauri is magnitude 11.1 and is the most interesting star in the
sky for parallax, so a magnitude cut alone would lose it.

`draw_sky.py` is an azimuthal equidistant projection through the zenith: the whole visible hemisphere
in one disc, the way you see it lying on your back. Star size is logarithmic in intensity, because
magnitude already is; colour is the same chain the simulation uses, B–V to temperature to a Planck
spectrum to sRGB. Nothing in it is chosen by eye.

The Milky Way samples come from the probe rather than being computed here. The frame arithmetic is
the thing this project keeps getting wrong, so there is one implementation of the galactic pole and
it lives in the simulation.

Preview charts are in `art/previews/sky/`.

## `blender/`

The model pipeline. See [`blender/README.md`](blender/README.md) for the build conventions and
[`blender/NOTES-blender5-api.md`](blender/NOTES-blender5-api.md) for the API changes in Blender 5
that cost time here.

## `probe/`

The probe scripts. See [`probe/README.md`](probe/README.md).
