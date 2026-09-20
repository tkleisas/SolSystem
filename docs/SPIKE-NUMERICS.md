# Phase 0 numerics spike — results

**Run:** `dotnet run -c Release --project src/SolSystem.Spike`
**Transcript:** `artifacts/spike-numerics.txt`
**Verdict:** PASS. Option A is sound, with one correction to the original design.

---

## What was tested

One Earth orbit about the Sun, propagated for a year at a one-hour step, twice: once in
Q64.64 fixed point and once in `double`. Same algorithm, same step, same operation order,
same initial conditions. The only difference is the arithmetic type, so any divergence is
attributable to fixed-point rounding and not to integrator truncation.

## What the spike found

**1. A Q32.32 `long` cannot do the solar frame at all.**

Gravity needs `r²`. At 1 AU that is 2.24 × 10¹⁶ km², and a Q32.32 value tops out at
2.1 × 10⁹. It overflows by a factor of **1.04 × 10⁷**. The reachable `r` in Q32.32 is
about 46 000 km — one seventh of an Earth radius — so the type cannot propagate a
planetary orbit regardless of what unit is chosen.

**2. No unit rescues it, because the frame needs both ends of the range at once.**

A heliocentric coordinate out to billions of kilometres needs 31 bits of integer, and a
thrust acceleration down to nano-metres per second squared needs around 60 bits of
fraction. That is more than 64 bits of dynamic range by construction. The type has to be
wider. `Fix128`, Q64.64 in kilometres, covers the whole system with 5.4 × 10⁻²⁰ km
resolution.

**3. The design's original worry was wrong, and the real worry was different.**

§6.2 flagged *acceleration* as the term that loses bits first. Fixed point keeps constant
**absolute** precision, so a small value keeps *more* relative precision — the reverse of
the floating-point intuition. Solar acceleration at 1 AU has 35 bits of headroom. The term
that actually breaks is `r²`.

**4. Option A holds an orbit.**

| Measurement | Result |
|---|---|
| Relative position error after one year | **1.37 × 10⁻⁶** |
| Energy drift, Q64.64 | **1.5 × 10⁻¹²** |
| Energy drift, `double` | 6.3 × 10⁻¹⁵ |
| Final radius, Q64.64 | 149 597 870.170 km (170 m from 1 AU) |
| Final radius, `double` | 149 597 869.809 km |
| Cost, Q64.64 | ~16 µs/step |
| Cost, `double` | ~130 ns/step |

The fixed/double difference grows **linearly** at about 150 m per step, which is a constant
rounding bias rather than a random walk or a divergence. Its source is the representation
of GM: `1.32712440018 × 10¹¹` is not exactly representable in Q64.64, so the fixed-point
Sun is very slightly weaker than the double one, and the orbit responds with a bias of
about 1.4 × 10⁻¹⁶ in acceleration. Over a year that integrates to ~1.3 × 10⁶ m — **1.4 × 10⁻⁶
of the orbit**, six orders of magnitude below anything the simulation can act on.

Energy is conserved to one part in 10¹², which is the property that matters: it means the
integrator is not leaking or gaining orbital energy over long runs.

## Cost

16 µs per step for the solar frame is far more than `double`'s 130 ns, and it does not
matter. The strategic layer propagates a few hundred bodies at a one-hour step: 200 bodies
is 3.2 ms of CPU per simulated hour, or about 0.09 % of one core at 1× time. Even at
10 000× compression it is under a core.

**The local frame is the one to watch.** `Fix64` is roughly 100× cheaper, which is why the
design keeps it for ships and combat, where hundreds of entities tick at 120 Hz.

## Bugs the work surfaced

Recorded because every one produced plausible numbers rather than an exception:

| Bug | Symptom |
|---|---|
| `Fix128` multiply assembled from selected halves | returned the low word as the whole result |
| `Fix128` divide shifted the numerator in `UInt128` | silently truncated; integer division for all inputs |
| `SqrtScaled` guessed the root's bit one place high | converged to a wrong fixed point, `sqrt(100)` → 8 |
| `SqrtScaled` steps and shifts confused | under-estimated by ~2 × 10⁹, invisible for perfect squares |
| Gravity scale multiplied the position vector by `GM/r²` | acceleration too large by a factor of `r` |
| Gravity divided by `r²` twice | underflowed to exactly zero |

The gravity mistakes were in the **reference** implementation as well as the fixed-point
one — the double path, which was supposed to be the trustworthy one, was the one that sent
the orbit out of the solar system.

## What this changes in the design

§6.2 is rewritten: the numeric decision now names **two types and two frames** rather than
one type at two scales, and the reason for the split is `r²` and the width it forces, not
precision at the render boundary.
