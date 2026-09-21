# The probe harness

A probe is a question asked of a running simulation, in a text file, answered with a
transcript. The pattern is ported from MiVic, where it was the difference between a test
that cost a process launch, a screenshot and a guess, and a test that costs one launch and
prints forty numbers.

```sh
dotnet run -c Release --project src/SolSystem.Probe -- \
    --probe tools/probe/docking.probe --probe-out artifacts/probe/docking.txt
```

Exit code: `0` clean, `1` a command failed, `2` a check failed, `3` the script could not be
read. The transcript is written even when the run fails, because a failed run is the one
worth reading.

## Commands

| Command | What it does |
|---|---|
| `advance <ticks>` | Advances the world one 120 Hz tick at a time |
| `days <n>` | Advances by a span of days. Use this for anything longer than minutes |
| `launch <station> [standoff m] [closing m/s]` | Puts the ship down a station's corridor |
| `ship` | Position, velocity, mass, nose, delta-v |
| `station <name>` | Offset and orbital altitude |
| `body <name>` | Heliocentric position and speed, for the eight planets and the Moon |
| `range` | Distance to the ship's home port |
| `closing` | Closing speed against the port |
| `phase` | Which phase of the approach the ship is in |
| `hash [label]` | SHA-256 of the world's raw state |
| `emit <text>` | A line in the transcript |
| `expect "<what>" <quantity> <value> <relation>` | A check |

Relations are `near`, `exactly`, `less`, `more`, `atleast`, `atmost`.

## Keeping the transcript diffable

Two properties are load-bearing and easy to lose.

**Commands never abort the script.** A failed one writes an `error:` line and the next
command runs, so a typo on line four of a fifty-line probe costs one line rather than the
other forty-six.

**Numbers are printed invariantly, and fixed-point values at full precision.** A transcript
that depends on a culture or a rounding cannot be diffed, and diffing two transcripts is
the cheapest reproducibility test there is. The hash is taken over the *raw fixed-point
words* rather than the printed decimals, because formatting rounds and rounding hides a
one-bit drift — which is exactly what a determinism check is for.

## What the probes currently check

| Script | Question | State |
|---|---|---|
| `docking.probe` | Does the ship arrive, and does it arrive the same way twice? | **The transcript is byte-identical between runs. The pilot does not yet arrive** |
| `station-keeping.probe` | Does a station hold its orbit, and does the Moon keep its own? | Passing |
| `scale.probe` | Are the frames and the ephemeris telling the same story? | Passing |

## The docking pilot is not finished, and why

The `docking.probe` pilot accelerates, judges when to brake, and comes about — and it has been
the source of eight separate bugs, six of them in the *pilot* and two in the engine it
exposed. It is recorded here because the failures are more instructive than the successes:

| What went wrong | What it actually was |
|---|---|
| Range grew at 7 668 m/s | The station was copied at launch and never moved; the live one does |
| Ship fell four kilometres while turning | Launched crosswise, so a thirty-second reversal preceded any thrust |
| Throttle firewalled at 43 m/s | The pilot never turned at all; the gain scale kept it below the gate |
| Gravity 220× the drive | The pull was measured against the station instead of the Earth's centre |
| Ship drifted outward in a held frame | It was given the station's *absolute* orbital velocity |
| Range grew quadratically with throttle shut | A proportional law asks for zero acceleration at target speed |
| Locked at 0.899 alignment forever | The lateral blend swung the nose past the throttle gate |
| *Tumbling on the spot forever* | **`Attitude.Step` folded only past pi, making pi a fixed point** |

The last one is a genuine engine bug and is now a test: a ship commanded to reverse used to
reach exactly half a turn, be rewritten to half a turn by the fold, and never move again. The
others are the shape of the problem rather than mistakes — docking under a 0.039 m/s² drive
with a thirty-second reversal is a controller worth building properly, not something to
improvise in a probe.

**What is proven and what is not.** The frame arithmetic, the attitude dynamics, the mass
accounting, the ephemeris, the capture envelope and the determinism of the transcript are all
tested and passing. What is not is the guidance law that flies the approach end to end.

## What a probe costs

Measured on this machine, one navigation tick:

| Work | Cost |
|---|---|
| A station's step, gravity twice plus velocity Verlet | 2.7 µs |
| A ship's step, coasting | 2.6 µs |
| One body through the ephemeris | 0.2 µs |
| All eight planets | 3.4 µs |

Which makes a simulated day 27 seconds of wall clock per object integrated at 120 Hz.
That is affordable for an action frame and **not affordable for a strategic timescale**: a
year of world time would be two and a half hours of CPU for a single station.

The conclusion is not that the integrator is slow. Gravity is called twice per step
because velocity Verlet needs the acceleration at the new position, and that is precisely
what makes it symplectic — a station that holds its altitude to the metre over ten days is
the result. The conclusion is that **the strategic layer needs a coarser step than the
navigation tick**, which the design already implies and which the probe now puts a number
on: eight bodies through the ephemeris cost less than one station's Verlet step, so the
strategic layer should use the analytic ephemeris and not integrate anything it can
compute.
