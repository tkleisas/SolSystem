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

## `blender/`

The model pipeline. See [`blender/README.md`](blender/README.md) for the build conventions and
[`blender/NOTES-blender5-api.md`](blender/NOTES-blender5-api.md) for the API changes in Blender 5
that cost time here.

## `probe/`

The probe scripts. See [`probe/README.md`](probe/README.md).
