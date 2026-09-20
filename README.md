# SolSystem

A 3D space game set across the Solar System, in two modes that share one simulation: a
**strategy** layer that commands a persistent world, and an **action** layer that puts the
player in a cockpit inside it.

Earth is neutral and dying. Two factions descended from Earth stock — the **Illuminus** and
the **Workers** — are racing to make a second home before Earth stops being one. The war is
not about ideology, though both sides will tell you it is. It is about who gets to breathe.

**Status: Phase 0, numeric foundation.** There is no game yet. The simulation core — the
numeric types, the trigonometry, and the Keplerian propagator — is built, tested and
measured; the client does not exist.

## The design

`DESIGN.md` is the reference: setting, factions, the energy economy, the architecture, and
the roadmap. Decisions in it are marked `[DECIDED]`, `[OPEN]` or `[DEFERRED]`, so it is
always clear which parts are settled and which are still forks.

The short version of the world:

- **The Workers** make antimatter near the Sun and colonise Venus, whose 0.90 g surface
  their bodies can take.
- **The Illuminus** run fusion torches on deuterium and colonise Mars, at 0.38 g. Their
  bodies cannot take Earth or Venus, so they invade in **shells** — mind transfer into
  mechanical bodies that cost a fortune each.
- **The Jovian moons are the war**, because both terraforming programmes need the same
  water ice, and the Illuminus need the same ice for fuel.

The history and biology — three living worlds, the dead Martian civilisation, the Venusian
cloud hives — are in **`docs/SETTING.md`**.

The propellant and energy budgets behind that — what a trip costs in delta-v, fuel mass,
and the energy to *make* the fuel — are worked through in `docs/TRIP-ENERGY.md`. The
headline: antimatter beats deuterium 500× on energy density and loses by 10¹⁸ on production
cost, so antimatter is a strategic fuel rather than a general one, and the collector fields
that make it are the size of a country.

## The numeric foundation

This is what exists today, and it is the part with the least room for error.

| | |
|---|---|
| **Solar frame** | Q64.64, unit = kilometre. Reach ±6 × 10¹⁰ AU, grid 5.4 × 10⁻²⁰ km |
| **Local frame** | Q64.64, unit = metre. Reach ±9.2 × 10¹⁸ m, grid 5.4 × 10⁻²⁰ m |

**One numeric type, two units.** `Fix128` is Q64.64 throughout; what changes between the
frames is only what a unit means.

The design originally gave the local frame a narrower Q32.32 type, and measurement killed
that idea twice over. In metres a Q32.32 square cannot exceed 2.147 × 10⁹, so a position
past about 46 km overflows `|r|²` — and a low Earth orbit is 150 times beyond that, failing
*silently*. In megametres the squares fit but Earth's surface gravity becomes 8.13 × 10⁻⁶,
leaving 16 bits of significand, and the position increment over a 120 Hz tick comes to
**two raw units**: an orbit integrated that way is almost entirely rounding, and the
measured energy and angular momentum drifted by 0.8 % in a single revolution.

Q64.64 in metres has neither problem. The cost is about 700 ns per gravity evaluation, or
roughly 17 ms of CPU per second for two hundred ships at 120 Hz.

Bodies travel on **Keplerian rails**: given the elements and a time, the position is solved
for directly rather than integrated, so the cost is the same at any distance and no error
accumulates over centuries. A one-year Earth orbit closes on itself to 0.000000 km, and
the analytic invariants — radius, specific energy, angular momentum, orbital plane — hold
to one part in 10⁷.

The measurement behind the numeric decision, and the nine bugs the work surfaced, are in
**`docs/SPIKE-NUMERICS.md`**. The headline: Q64.64 holds a one-year Earth orbit to 1.4 × 10⁻⁶
relative error with energy conserved to one part in 10¹².

Everything is exact integer arithmetic. Nothing in `SolSystem.Core` uses `float` or
`double` except at the boundary where a measured constant is read in, and in tests.

## Build and test

Requires the .NET 10 SDK.

```sh
dotnet build -c Release
dotnet test  -c Release        # 178 tests
dotnet run   -c Release --project src/SolSystem.Spike
```

`NuGet.config` redirects the NuGet HTTP cache and packages folder into `artifacts/`, so a
restore never needs to write outside the repository.

## Layout

| Project | Purpose |
|---|---|
| `src/SolSystem.Core` | Fixed-point maths, integer trigonometry, Keplerian orbits, attitude and docking. No graphics, no floating point. |
| `src/SolSystem.Spike` | The Phase 0 numerics experiment. Not shipped; it is the evidence. |
| `tests/SolSystem.Core.Tests` | Checked against independent references, not hand-computed values. |
| `docs/` | The spike report, the transit and energy analysis, and the setting. |
| `tools/` | Reserved for the probe harness and the model pipeline. |

## Why the tests look the way they do

The fixed-point types accumulated six bugs during construction — a multiply with its
halves swapped, a divide whose shift truncated silently, a square root that converged to a
wrong fixed point, a start bit one place too high, and two gravity formulas wrong by a
factor of `r` and by underflow. **Every one returned plausible numbers rather than raising
an exception, and two were in the `double` reference rather than the fixed-point code.**

So the numeric tests check against an independent `BigInteger` reference rather than
against expectations typed by hand. That discipline is the point, not the ceremony.
