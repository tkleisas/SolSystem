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
| `sundistance` | How far the last body named is from the Sun |
| `launch <station> [standoff m] [closing m/s]` | Puts the ship down a station's corridor |
| `ship` | Position, velocity, mass, nose, delta-v |
| `station <name>` | Offset and orbital altitude |
| `body <name>` | Heliocentric position and speed, for the eight planets and the Moon |
| `range` | Distance to the ship's home port |
| `closing` | Closing speed toward the port, positive when approaching |
| `lateral` | Distance from the corridor centreline |
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
| `docking.probe` | Does the ship arrive, and does it arrive the same way twice? | The transcript is byte-identical. The script ends mid-creep — at 253 600 ticks the ship is in the terminal phase, 148 m out and closing at the fixed 0.15 m/s — so arrival itself is pinned by the test suite: `ApproachTests.AShipFlownFromTwoKilometres_Docks` docks in 131 556 ticks |
| `station-keeping.probe` | Does a station hold its orbit, and does the Moon keep its own? | Passing |
| `scale.probe` | Are the frames and the ephemeris telling the same story? | Passing |

## The docking pilot, and the eight bugs it took

The law now lives in `SolSystem.Core/Local/Approach.cs` with its own tests. It flew the
approach four times as a private method in this harness first, which is the wrong place to
develop a controller — every fix had to be re-derived without tests — and every attempt failed
differently. The table is worth keeping because the failures are more instructive than the
success:

| What went wrong | What it actually was |
|---|---|
| Range grew at 7 668 m/s | The station was copied at launch and never moved; the live one does |
| Ship fell four kilometres while turning | Launched crosswise, so a thirty-second reversal preceded any thrust |
| Throttle firewalled at 43 m/s | The pilot never turned at all; the gain scale kept it below the gate |
| Gravity 220× the drive | The pull was measured against the station instead of the Earth's centre |
| Ship drifted outward in a held frame | It was given the station's *absolute* orbital velocity |
| Range grew quadratically with throttle shut | A proportional law asks for zero acceleration at target speed |
| Locked at 0.899 alignment forever | The lateral blend swung the nose past the throttle gate |
| Ran away at 44 m/s | Past the port, "close faster" and "back away" swap meanings along a fixed axis |
| Parked 2.3 m outside a 2 m envelope | A fixed creep speed approaches the port asymptotically |
| Hovered 8 mm from the port, never captured | A switching law cannot regulate a five-centimetre-a-second target |
| Helm reversed every 8 ms | A P-D helm with a rate-limited actuator oscillates at the tick rate |
| **Tumbled on the spot forever** | **Three sign errors and a fold that scaled instead of flipping the axis** |

The endgame was the piece that stayed open longest, and the diagnosis was specific rather than a
shrug: the target closing speed falls with the range, and a law that switches between full thrust
and full brake cannot regulate a quantity that small — it nudges across the axis, the commanded
direction flips, and it nudges back. One version reached 3.5 cm and hovered there forever. The fix
is the two decisions now documented in `Approach`: the handover from braking to creeping happens
on the *rate* (0.15 m/s), not on the profile, and the creep is thrust-only — nose forward, the
ship can only accelerate, so it regulates the last metres by coasting and can never overshoot into
another reversal. The ship docks. In the test's fixed-port world that takes 1 096 s from two
kilometres; the probe's live world is slower and the script ends mid-creep, and the difference
between the two clocks has not been chased down.

The item marked as an attitude failure was three separate faults conspiring:

* `Attitude.Step` folded a rotation vector past π by **scaling** it back to π. Scaling keeps the
  axis and changes the rotation — the correct fold is θ > π about an axis becoming 2π − θ
  about the *opposite* axis. The scaled version pins a reversing ship at the limit forever,
  because the command advances it past π, the fold hauls it back, and the command is still lit.
* `Docking.Evaluate`'s closing speed was negated relative to its own documentation, so every
  approach read as "moving away".
* `Fix128` unary minus toggles a sign flag and leaves the magnitude alone, so a wrongly-negated
  rate is the right number with the wrong sign — which no trace of magnitudes will reveal, and
  which is why three of these took as long as they did.

The probe was carrying three of these faults at once and reporting perfectly consistent numbers.
That is the argument for the test suite rather than the harness: a probe says what the world
did, and only a test says what it should have done.

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
