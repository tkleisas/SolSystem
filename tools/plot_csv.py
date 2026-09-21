#!/usr/bin/env python3
"""
Plots a CSV of probe or test output as an SVG, with no dependencies.

Written because the obvious tool was not available and installing it was blocked, which turned
out to be a good thing: this renders anywhere, needs nothing, and the output is a text file that
can be committed and diffed.

    plot_csv.py data.csv --x tick --panels "range,closing,throttle" --out plot.svg

Panels are stacked and share the x axis. Each may name a column, or use `column@log` or
`column@symlog` for a logarithmic axis, which is usually what a range wants: an approach that
starts at two kilometres and has to be judged in its last two metres is unreadable on a linear
scale.

    plot_csv.py data.csv --x tick --panels "range@symlog,closing,throttle" \\
        --marks "2:capture envelope" --bands phase --out approach.svg

`--bands` colours the background by a categorical column, which is how a phase machine is best
read: the question is never "what was the number" but "what was it doing when the number went
wrong".
"""

import argparse
import csv
import math
import sys

# A small palette, chosen to stay legible against the band colours below.
SERIES = "#1f3f8f"
BAND_ALPHA = 0.16
BANDS = ["#4c78a8", "#f58518", "#54a24b", "#e45756", "#b279a2", "#9d755d", "#bab0ac"]


def read_csv(path):
    with open(path, newline="") as handle:
        return list(csv.DictReader(handle))


def to_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def nice_bounds(low, high):
    """Round a range out to something a person would choose."""
    if low == high:
        return low - 1.0, high + 1.0

    span = high - low
    step = 10.0 ** math.floor(math.log10(span))
    for multiple in (1, 2, 2.5, 5, 10):
        if span / (step * multiple) <= 6:
            step *= multiple
            break

    return math.floor(low / step) * step, math.ceil(high / step) * step


def ticks(low, high, count=5):
    if low == high:
        return [low]
    return [low + (high - low) * i / (count - 1) for i in range(count)]


class Panel:
    """One stacked subplot: a y column against the shared x."""

    def __init__(self, spec, rows, xkey):
        self.raw = spec
        self.scale = "linear"
        name = spec
        if "@" in spec:
            name, self.scale = spec.rsplit("@", 1)
        self.name = name

        self.xs, self.ys = [], []
        for row in rows:
            x = to_float(row.get(xkey))
            y = to_float(row.get(name))
            if x is None or y is None:
                continue
            if self.scale in ("log", "symlog") and y <= 0:
                if self.scale == "log":
                    continue
                y = None  # a symlog panel simply omits non-positive points
            if y is not None:
                self.xs.append(x)
                self.ys.append(y)

    def transform(self, y):
        if self.scale == "log":
            return math.log10(y) if y > 0 else None
        if self.scale == "symlog":
            limit = getattr(self, "linthresh", 1.0)
            if abs(y) <= limit:
                return y / limit
            return math.copysign(1 + math.log10(abs(y) / limit), y)
        return y

    def inverse(self, v):
        if self.scale == "log":
            return 10.0 ** v
        if self.scale == "symlog":
            limit = getattr(self, "linthresh", 1.0)
            if abs(v) <= 1:
                return v * limit
            return math.copysign(limit * 10.0 ** (abs(v) - 1), v)
        return v


def render(rows, xkey, panels, out_path, title, bands, marks, width, height):
    if not rows:
        sys.exit("no rows to plot")

    xs = [to_float(r.get(xkey)) for r in rows]
    xs = [x for x in xs if x is not None]
    x_low, x_high = min(xs), max(xs)
    if x_low == x_high:
        x_high = x_low + 1.0

    margin_left, margin_right, margin_top, margin_bottom = 78, 150, 46, 46
    gap = 22
    paper_height = margin_top + margin_bottom + len(panels) * height + (len(panels) - 1) * gap
    plot_width = width - margin_left - margin_right

    def sx(x):
        return margin_left + (x - x_low) / (x_high - x_low) * plot_width

    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{paper_height}" '
        f'viewBox="0 0 {width} {paper_height}" font-family="Helvetica,Arial,sans-serif" '
        f'font-size="11">',
        f'<rect width="{width}" height="{paper_height}" fill="white"/>',
        f'<text x="{margin_left}" y="24" font-size="14" font-weight="bold">{title}</text>',
    ]

    # Phase bands, drawn first so everything else sits on top.
    if bands and bands in rows[0]:
        seen = []
        start = 0
        for i in range(1, len(rows) + 1):
            changed = i == len(rows) or rows[i][bands] != rows[start][bands]
            if changed:
                value = rows[start][bands]
                if value not in seen:
                    seen.append(value)
                colour = BANDS[seen.index(value) % len(BANDS)]
                x0 = sx(to_float(rows[start][xkey]) or x_low)
                x1 = sx(to_float(rows[min(i, len(rows) - 1)][xkey]) or x_high)
                for p_index in range(len(panels)):
                    y0 = margin_top + p_index * (height + gap)
                    parts.append(
                        f'<rect x="{x0:.1f}" y="{y0}" width="{max(x1 - x0, 0.5):.1f}" '
                        f'height="{height}" fill="{colour}" fill-opacity="{BAND_ALPHA}"/>')
                start = i

        # A legend for the bands, down the right-hand side of the first panel.
        lx = margin_left + plot_width + 10
        ly = margin_top + 14
        parts.append(f'<text x="{lx}" y="{ly - 4}" font-size="10" fill="#444">{bands}</text>')
        for i, value in enumerate(seen):
            parts.append(f'<rect x="{lx}" y="{ly + i * 15}" width="10" height="10" '
                         f'fill="{BANDS[i % len(BANDS)]}" fill-opacity="0.5"/>')
            parts.append(f'<text x="{lx + 15}" y="{ly + 9 + i * 15}" font-size="10">{value}</text>')

    for index, panel in enumerate(panels):
        top = margin_top + index * (height + gap)
        bottom = top + height

        if panel.scale == "symlog" and panel.ys:
            limit = 10.0 ** math.floor(math.log10(
                max(1e-9, min(abs(y) for y in panel.ys if y != 0) or 1.0)))
            panel.linthresh = max(limit, 1e-6)

        transformed = [panel.transform(y) for y in panel.ys]
        t_low, t_high = min(transformed), max(transformed)
        if t_low == t_high:
            t_low, t_high = t_low - 0.5, t_high + 0.5

        parts.append(f'<rect x="{margin_left}" y="{top}" width="{plot_width}" height="{height}" '
                     f'fill="none" stroke="#bbb"/>')
        parts.append(f'<text x="12" y="{top + 14}" font-size="11" font-weight="bold">{panel.raw}</text>')

        for value in ticks(t_low, t_high, 5):
            y = bottom - (value - t_low) / (t_high - t_low) * height
            label = panel.inverse(value)
            parts.append(f'<line x1="{margin_left}" y1="{y:.1f}" x2="{margin_left + plot_width}" '
                         f'y2="{y:.1f}" stroke="#eee"/>')
            parts.append(f'<text x="{margin_left - 6}" y="{y + 4:.1f}" text-anchor="end" '
                         f'font-size="10" fill="#555">{label:.4g}</text>')

        points = []
        for x, y in zip(panel.xs, panel.ys):
            ty = panel.transform(y)
            py = bottom - (ty - t_low) / (t_high - t_low) * height
            points.append(f"{sx(x):.1f},{py:.1f}")

        if points:
            parts.append(f'<polyline points="{" ".join(points)}" fill="none" stroke="{SERIES}" '
                         f'stroke-width="1.1"/>')

        for mark in marks:
            value, label = mark
            ty = panel.transform(value)
            if t_low <= ty <= t_high:
                y = bottom - (ty - t_low) / (t_high - t_low) * height
                parts.append(f'<line x1="{margin_left}" y1="{y:.1f}" x2="{margin_left + plot_width}" '
                             f'y2="{y:.1f}" stroke="#2a7" stroke-dasharray="4 3" stroke-width="1"/>')
                parts.append(f'<text x="{margin_left + plot_width - 4}" y="{y - 3:.1f}" '
                             f'text-anchor="end" font-size="9" fill="#2a7">{label}</text>')

    for value in ticks(x_low, x_high, 9):
        x = sx(value)
        parts.append(f'<line x1="{x:.1f}" y1="{margin_top}" x2="{x:.1f}" '
                     f'y2="{paper_height - margin_bottom + 6}" stroke="#f2f2f2"/>')
        parts.append(f'<text x="{x:.1f}" y="{paper_height - margin_bottom + 20}" text-anchor="middle" '
                     f'font-size="10" fill="#555">{value:.0f}</text>')

    parts.append(f'<text x="{margin_left + plot_width / 2:.0f}" y="{paper_height - 10}" '
                 f'text-anchor="middle" font-size="11">{xkey}</text>')
    parts.append("</svg>")

    with open(out_path, "w") as handle:
        handle.write("\n".join(parts))
    return out_path


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv")
    parser.add_argument("--x", default="tick")
    parser.add_argument("--panels", required=True,
                        help="comma-separated columns, each optionally suffixed @log or @symlog")
    parser.add_argument("--out", default="plot.svg")
    parser.add_argument("--title", default="")
    parser.add_argument("--bands", default=None, help="categorical column to shade by")
    parser.add_argument("--marks", default="",
                        help="comma-separated value:label pairs to draw as horizontal lines")
    parser.add_argument("--width", type=int, default=1200)
    parser.add_argument("--height", type=int, default=170)
    args = parser.parse_args()

    rows = read_csv(args.csv)
    specs = [s.strip() for s in args.panels.split(",") if s.strip()]
    panels = [Panel(s, rows, args.x) for s in specs]

    marks = []
    for item in args.marks.split(","):
        if item.strip():
            value, _, label = item.partition(":")
            marks.append((float(value), label or value))

    path = render(rows, args.x, panels, args.out,
                  args.title or args.csv, args.bands, marks, args.width, args.height)
    print(f"wrote {path}  ({len(rows)} rows, {len(panels)} panels)")


if __name__ == "__main__":
    main()
