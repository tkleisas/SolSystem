#!/usr/bin/env python3
"""
Draws an all-sky chart from a probe's sky dump.

    dotnet run -c Release --project src/SolSystem.Probe -- --probe sky.probe
    python3 tools/draw_sky.py /tmp/sky.csv --out /tmp/sky.svg --title "Greenwich, 2000 January 1"

The input is the CSV that the probe's `sky` command writes: every star above the horizon in
altitude and azimuth, with magnitude and colour, plus the Sun and the planets.

**The projection is azimuthal equidistant through the zenith** — the whole visible hemisphere in one
disc, the way you see it lying on your back. Altitude 90 is the centre, the horizon is the rim, and
azimuth runs round it, which is the projection a planisphere uses and the only one that puts the
whole sky in one picture without a seam.

**Star size is logarithmic in intensity**, which is not a stylistic choice. Magnitude is already a
logarithm — five magnitudes is a factor of a hundred in brightness — so a size proportional to
magnitude directly would make Sirius a hundred times the radius of a sixth-magnitude star. The
convention planetarium software uses is a radius that grows roughly as the square root of the
intensity, which compresses the range to something the eye can take in.

**Colour is the same chain the simulation uses**: B-V to temperature, temperature to a Planck
spectrum, spectrum to linear sRGB, then the sRGB transfer function. Nothing here is chosen by eye.
"""

import argparse
import csv
import math
import os

# ---------------------------------------------------------------------------- colour

# The CIE 1931 colour-matching functions at 5 nm, and Planck's law, are duplicated here in miniature
# rather than reimplemented: this is a preview tool, the simulation has the real version, and two
# implementations of a colour chain that disagree would be worse than one that is approximate. The
# approximation here is Tanner Helland's fit, which is within a few per cent over the stellar range.
def blackbody_rgb(kelvin):
    t = max(1000.0, min(40000.0, kelvin)) / 100.0

    if t <= 66:
        red = 255.0
        green = 99.4708025861 * math.log(t) - 161.1195681661
    else:
        red = 329.698727446 * ((t - 60) ** -0.1332047592)
        green = 288.1221695283 * ((t - 60) ** -0.0755148492)

    if t >= 66:
        blue = 255.0
    elif t <= 19:
        blue = 0.0
    else:
        blue = 138.5177312231 * math.log(t - 10) - 305.0447927307

    return (max(0.0, min(255.0, red)) / 255.0,
            max(0.0, min(255.0, green)) / 255.0,
            max(0.0, min(255.0, blue)) / 255.0)


def temperature_from_bv(bv):
    return 4600.0 * ((1.0 / ((0.92 * bv) + 1.7)) + (1.0 / ((0.92 * bv) + 0.62)))


def star_colour(bv_text):
    if not bv_text:
        return (1.0, 1.0, 1.0)
    try:
        bv = float(bv_text)
    except ValueError:
        return (1.0, 1.0, 1.0)
    return blackbody_rgb(temperature_from_bv(max(-0.4, min(2.5, bv))))


# ---------------------------------------------------------------------------- projection

def project(altitude, azimuth, radius):
    """Azimuthal equidistant through the zenith: centre is up, rim is the horizon."""
    r = radius * (90.0 - altitude) / 90.0
    # Azimuth runs north through east, and the chart is drawn as seen lying on your back looking
    # up, so east goes anticlockwise from north — the mirror of a map, and the reason a planisphere
    # has its directions printed the way it does.
    theta = math.radians(azimuth)
    return (r * math.sin(theta), -r * math.cos(theta))


def star_radius(magnitude):
    """
    Radius in pixels for a star of this magnitude.

    Magnitude is already logarithmic — five magnitudes is a factor of a hundred in brightness — so
    the intensity has to be compressed hard to fit on a screen. The exponent is the whole question:
    a square root leaves Sirius, at magnitude -1.44, thirty pixels across and the chart becomes a
    field of blobs, which is what the first version did. An exponent near 0.28 puts the brightest
    star in the sky at about five pixels and a naked-eye limit star at one, which is roughly the
    range a printed planisphere uses.
    """
    if magnitude > 6.6:
        return 0.0
    intensity = 10.0 ** (-0.4 * (magnitude - 6.5))
    return 0.62 * (intensity ** 0.28)


# ---------------------------------------------------------------------------- drawing

def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv")
    parser.add_argument("--out", default="sky.svg")
    parser.add_argument("--title", default="")
    parser.add_argument("--size", type=int, default=1100)
    parser.add_argument("--limit", type=float, default=5.8,
                        help="faintest magnitude to draw")
    parser.add_argument("--labels", type=float, default=1.6,
                        help="label stars brighter than this")
    parser.add_argument("--band-step", type=float, default=3.0,
                        help="grid spacing of the Milky Way samples, in degrees")
    args = parser.parse_args()

    stars = []
    bodies = []
    band = []
    with open(args.csv, newline="") as handle:
        for row in csv.DictReader(handle):
            kind = row["kind"]
            try:
                altitude = float(row["altitude"])
                azimuth = float(row["azimuth"])
            except (TypeError, ValueError):
                continue

            if kind == "band":
                band.append((altitude, azimuth, float(row["colour"])))
                continue

            try:
                magnitude = float(row["magnitude"])
            except (TypeError, ValueError):
                continue

            entry = (altitude, azimuth, magnitude, row.get("colour", ""), row.get("name", ""))
            (bodies if kind == "body" else stars).append(entry)

    size = args.size
    centre = size / 2.0
    radius = centre - 74.0

    out = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size + 34}" '
        f'viewBox="0 0 {size} {size + 34}" font-family="Helvetica,Arial,sans-serif">',
        # The sky is not black. A dark blue-grey reads as night without looking like a hole in the
        # page, and it is roughly what a genuinely dark site looks like away from the Milky Way.
        '<defs>',
        # A blur on the band, because it is drawn as a grid of samples and a grid of samples looks
        # like a grid. One filter is cheaper than interpolating and it is what the eye does anyway.
        '<filter id="band" x="-20%" y="-20%" width="140%" height="140%">',
        '<feGaussianBlur stdDeviation="7"/>',
        '</filter>',
        '</defs>',
        f'<rect width="{size}" height="{size + 34}" fill="#05070d"/>',
        f'<circle cx="{centre}" cy="{centre}" r="{radius + 26}" fill="#080c16"/>',
        f'<text x="{centre}" y="{size + 22}" text-anchor="middle" font-size="13" fill="#9aa4b8">'
        f'{args.title}</text>',
    ]

    # Altitude rings every 30 degrees, and the horizon.
    for altitude in (0, 30, 60):
        r = radius * (90.0 - altitude) / 90.0
        width = 1.2 if altitude == 0 else 0.5
        colour = "#3d4a63" if altitude == 0 else "#1b2333"
        out.append(f'<circle cx="{centre}" cy="{centre}" r="{r:.1f}" fill="none" '
                   f'stroke="{colour}" stroke-width="{width}"/>')

    for azimuth in range(0, 360, 45):
        x, y = project(0.0, azimuth, radius)
        out.append(f'<line x1="{centre}" y1="{centre}" x2="{centre + x:.1f}" y2="{centre + y:.1f}" '
                   f'stroke="#161d2b" stroke-width="0.5"/>')

    for label, azimuth in (("N", 0), ("NE", 45), ("E", 90), ("SE", 135),
                           ("S", 180), ("SW", 225), ("W", 270), ("NW", 315)):
        x, y = project(0.0, azimuth, radius + 15)
        out.append(f'<text x="{centre + x:.1f}" y="{centre + y + 4:.1f}" text-anchor="middle" '
                   f'font-size="12" fill="#6b7890"/>')

    # The Milky Way, before the stars so they sit on top of it. Each sample is a small square, and
    # they overlap, which is what makes a grid of squares read as a continuous band.
    if band:
        out.append('<g filter="url(#band)">')
        cell = radius * (args.band_step / 90.0) * 2.6
        for altitude, azimuth, brightness in band:
            x, y = project(altitude, azimuth, radius)
            # Warm white, because the band is starlight and unresolved stars are not blue. The
            # opacity is the profile the simulation computed, scaled to something a screen shows.
            alpha = min(0.5, 0.42 * brightness)
            out.append(f'<rect x="{centre + x - cell / 2:.1f}" y="{centre + y - cell / 2:.1f}" '
                       f'width="{cell:.1f}" height="{cell:.1f}" fill="#cfc6b4" '
                       f'opacity="{alpha:.3f}"/>')
        out.append('</g>')

    # Stars, faintest first so the bright ones sit on top.
    visible = [s for s in stars if s[2] <= args.limit]
    visible.sort(key=lambda s: -s[2])

    for altitude, azimuth, magnitude, colour, _ in visible:
        r = star_radius(magnitude)
        if r <= 0.0:
            continue
        x, y = project(altitude, azimuth, radius)
        red, green, blue = star_colour(colour)
        fill = "#%02x%02x%02x" % (int(red * 255), int(green * 255), int(blue * 255))

        # A halo, but only on the genuinely bright stars and only just: a large faint disc around
        # everything reads as fog rather than as light.
        if r > 2.2:
            out.append(f'<circle cx="{centre + x:.1f}" cy="{centre + y:.1f}" r="{r * 1.9:.1f}" '
                       f'fill="{fill}" opacity="0.10"/>')
        out.append(f'<circle cx="{centre + x:.1f}" cy="{centre + y:.1f}" r="{r:.2f}" fill="{fill}"/>')

    # Labels, only for the bright ones so the chart stays readable.
    for altitude, azimuth, magnitude, _, name in visible:
        if magnitude > args.labels or not name:
            continue
        x, y = project(altitude, azimuth, radius)
        r = star_radius(magnitude)
        out.append(f'<text x="{centre + x + r + 4:.1f}" y="{centre + y + 3.5:.1f}" '
                   f'font-size="10" fill="#c8d0e0" opacity="0.85">{name}</text>')

    # The Sun and the planets, which is the whole point: they move against this sky.
    body_style = {
        "Sun": ("#fff3c4", 9.0), "Moon": ("#e8e8ee", 7.0), "Mercury": ("#b8b0a4", 3.2),
        "Venus": ("#fff0d0", 4.6), "Mars": ("#e09070", 4.0), "Jupiter": ("#e8d8b8", 5.2),
        "Saturn": ("#e0d0a0", 4.6), "Uranus": ("#bcd8e0", 3.4), "Neptune": ("#a0b8e8", 3.4),
    }

    for altitude, azimuth, magnitude, _, name in bodies:
        colour, dot = body_style.get(name, ("#ffffff", 3.0))
        if altitude < 0.0:
            # Below the horizon: show it at the rim, dimly, because knowing that Jupiter is just
            # down is useful and hiding it entirely is not.
            x, y = project(0.0, azimuth, radius)
            out.append(f'<circle cx="{centre + x:.1f}" cy="{centre + y:.1f}" r="{dot * 0.5:.1f}" '
                       f'fill="none" stroke="{colour}" stroke-width="0.8" opacity="0.28"/>')
            continue

        x, y = project(altitude, azimuth, radius)
        out.append(f'<circle cx="{centre + x:.1f}" cy="{centre + y:.1f}" r="{dot * 2.6:.1f}" '
                   f'fill="{colour}" opacity="0.10"/>')
        out.append(f'<circle cx="{centre + x:.1f}" cy="{centre + y:.1f}" r="{dot:.1f}" '
                   f'fill="{colour}"/>')
        out.append(f'<text x="{centre + x + dot + 5:.1f}" y="{centre + y + 4:.1f}" font-size="12" '
                   f'font-weight="bold" fill="{colour}">{name}</text>')

    out.append("</svg>")

    with open(args.out, "w") as handle:
        handle.write("\n".join(out))

    brightest = min((s[2] for s in visible), default=0.0)
    print(f"wrote {args.out}: {len(visible)} stars, {len(bodies)} bodies, "
          f"brightest magnitude {brightest:.2f}")


if __name__ == "__main__":
    main()
