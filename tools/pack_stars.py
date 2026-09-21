#!/usr/bin/env python3
"""
Packs the HYG star catalogue into the binary the game loads.

    pack_stars.py --source /tmp/hyg.csv --out art/textures/stars.bin

The source is the HYG database v4.1 (https://github.com/astronexus/HYG-Database), which merges
Hipparcos, Yale BSC and Gliese and carries about 119 000 stars with position, magnitude, colour
index and distance. It is 34 MB of CSV, which is not something to ship or to parse at startup, so
this reduces it to the stars a person can actually see plus the ones close enough that their
parallax is real, and writes them as fixed-size records.

**The subset.** Everything brighter than magnitude 6.5 — the naked-eye limit, about 8 900 stars —
plus everything within 25 parsecs whatever its magnitude, which adds the faint nearby red dwarfs.
Proxima Centauri is magnitude 11.1 and is the single most interesting star in the sky for
parallax; the brightest star within 25 pc that is *not* naked eye would be missing from a
magnitude cut alone. 11 558 stars, 185 kB.

**The packing.** Sixteen bytes a star, all fixed point, because the simulation is fixed point and
a star chart that drifts against the planets would be worse than no star chart:

    RA        uint32   fraction of a turn, times 2^32      (0.084 arcsec)
    Dec       int32    fraction of a turn, times 2^32      (0.084 arcsec)
    magnitude int16    thousandths of a magnitude
    colour    int16    thousandths of a B-V colour index
    distance  uint16   hundredths of a parsec, 0 = at infinity
    name      uint16   0 = unnamed, else an index into the name table

Epoch J2000, which is what HYG carries and what `Ephemeris` uses for the planets — so stars and
planets are in the same frame and precession is applied to both, or to neither, but never to one.
"""

import argparse
import csv
import struct
import sys

MAGIC = b"SOLSTARS"
VERSION = 1
RECORD = 16

# Naked-eye limit. 6.5 is about as faint as a good sky allows, and the catalogue's own limit of
# completeness is not much beyond it.
NAKED_EYE = 6.5

# Everything inside this is kept whatever its magnitude.
NEARBY_PARSECS = 25.0

# Beyond this the distance field cannot hold it and the parallax is under a milliarcsecond, so the
# star is written as being at infinity. 655.35 pc is what a uint16 of centiparsecs reaches.
MAX_DISTANCE_PC = 655.35

TURN = 1 << 32


def parse(source):
    """Reads the HYG CSV and returns the subset worth shipping."""
    kept = []
    with open(source, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            # The Sun is a row in this catalogue, at the origin, with a magnitude of -26.7. It is
            # already the centre of the simulation and would be a very confusing star.
            if (row.get("proper") or "") == "Sol":
                continue

            try:
                ra_hours = float(row["ra"])
                dec_degrees = float(row["dec"])
                magnitude = float(row["mag"])
            except (TypeError, ValueError):
                continue

            distance = None
            if row["dist"]:
                try:
                    distance = float(row["dist"])
                except ValueError:
                    distance = None

            if magnitude > NAKED_EYE and not (distance and distance < NEARBY_PARSECS):
                continue

            colour = None
            if row["ci"]:
                try:
                    colour = float(row["ci"])
                except ValueError:
                    colour = None

            kept.append((ra_hours, dec_degrees, magnitude, colour, distance,
                         (row.get("proper") or "").strip()))

    # Brightest first, so a renderer that can only afford part of the catalogue takes the part
    # worth having.
    kept.sort(key=lambda s: s[2])
    return kept


def pack(stars):
    """Packs to bytes, returning the blob and the number of named stars."""
    names = []
    index = {}

    body = bytearray()
    for ra_hours, dec_degrees, magnitude, colour, distance, name in stars:
        if name and name not in index:
            index[name] = len(names) + 1        # 0 means unnamed
            names.append(name)

        # RA in hours to a fraction of a turn; Dec in degrees likewise. A turn is the natural unit
        # for the fixed-point trigonometry the simulation uses, so storing it this way means no
        # conversion at load time.
        ra = round((ra_hours / 24.0) * TURN) % TURN
        dec = round((dec_degrees / 360.0) * TURN)
        dec = max(-TURN // 4, min(TURN // 4, dec))

        mag = max(-32768, min(32767, round(magnitude * 1000)))
        ci = 0 if colour is None else max(-32768, min(32767, round(colour * 1000)))
        dist = 0
        if distance and 0.0 < distance <= MAX_DISTANCE_PC:
            dist = max(1, round(distance * 100))

        body += struct.pack("<IihhHH", ra, dec, mag, ci, dist, index.get(name, 0))

    blob = bytearray()
    blob += MAGIC + struct.pack("<II", VERSION, len(stars))
    blob += body
    blob += struct.pack("<H", len(names))
    for name in names:
        encoded = name.encode("utf-8")
        blob += struct.pack("<B", len(encoded)) + encoded

    return bytes(blob), len(names)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", required=True, help="the HYG CSV")
    parser.add_argument("--out", required=True, help="where to write the packed catalogue")
    args = parser.parse_args()

    stars = parse(args.source)
    if not stars:
        sys.exit("no stars parsed — is that the right file?")

    blob, named = pack(stars)
    with open(args.out, "wb") as handle:
        handle.write(blob)

    print(f"{len(stars):,} stars, {named} named, {len(blob):,} bytes -> {args.out}")
    print(f"  brightest {stars[0][2]:.2f}, faintest {stars[-1][2]:.2f}")
    nearest = min((s for s in stars if s[4]), key=lambda s: s[4])
    print(f"  nearest {nearest[5] or 'unnamed'} at {nearest[4]:.3f} pc "
          f"(parallax {1000.0 / nearest[4]:.1f} mas)")


if __name__ == "__main__":
    main()
