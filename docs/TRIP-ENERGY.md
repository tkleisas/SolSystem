# Trip energy and propellant budgets

What a trip costs in delta-v, propellant, fuel mass, and the energy to make that fuel —
and what the answers force the design to say.

Every figure here is derived from the rocket equation and published constants, with the
arithmetic shown so a number can be argued with rather than trusted. **Accuracy: order of
magnitude.** These are idealised Hohmann transfers with a flat margin, not
porkchop-optimised trajectories, and the fusion and antimatter numbers depend on
assumptions the design has not yet fixed.

---

## 1. Delta-v budget

Ideal Hohmann transfer between circular coplanar orbits, plus a plane change, plus 15 % for
midcourse corrections, finite burn losses and boil-off.

| Route | Ideal | Plane change | Subtotal | +15 % margin | **Budget** | Hohmann time | Windows |
|---|---|---|---|---|---|---|---|
| Earth → Mars | 5.59 | 0.78 | 6.37 | 0.96 | **7.33 km/s** | 259 d | 780 d |
| Earth → Venus | 5.20 | 2.07 | 7.27 | 1.09 | **8.36 km/s** | 146 d | 584 d |
| Earth → Jupiter | 14.44 | 0.30 | 14.74 | 2.21 | **16.95 km/s** | 998 d | 399 d |
| Earth → Saturn | 15.74 | 0.35 | 16.09 | 2.41 | **18.50 km/s** | 2 223 d | 378 d |

Two things worth noticing.

**Venus is more expensive than Mars** despite being closer, because its orbit is inclined
3.39° against Mars's 1.85°, and a plane change costs velocity in proportion to how fast you
are already going. The Workers' target is the harder one to reach. That is a useful
asymmetry rather than an accident.

**These are impulsive figures.** A crewed ship limited to 1 g cannot execute an
instantaneous burn: at 1 g a 3 km/s departure burn takes five minutes and the gravity
losses are real. The 15 % margin covers it, but a low-thrust ship — a fusion torch at
0.1 g — would need meaningfully more and would spend days spiralling out.

---

## 2. Propellant mass

Mass ratio is `exp(Δv / vₑ)`. For a **100 t dry ship**, one way:

| Drive | vₑ | Mars (7.33) | Venus (8.36) | Jupiter (16.95) | Saturn (18.50) |
|---|---|---|---|---|---|
| Chemical (LH₂/LOX) | 4.4 km/s | 429 t | 569 t | 4 607 t | 6 598 t |
| Fusion torch, low | 30 km/s | 27.7 t | 32.2 t | 75.9 t | 85.3 t |
| Fusion torch, mid | 100 km/s | 7.6 t | 8.7 t | 18.5 t | 20.3 t |
| Fusion torch, high | 300 km/s | 2.5 t | 2.8 t | 5.8 t | 6.4 t |
| Antimatter | 1 000 km/s | 0.7 t | 0.8 t | 1.7 t | 1.9 t |

This is the whole argument for fusion in one table. **Chemical propulsion cannot leave the
inner system** — a Saturn run needs sixty-six times the dry mass in propellant, which is a
fuel depot that happens to have a ship attached. Fusion makes the outer system reachable
and antimatter makes it cheap, which is exactly the asymmetry the design wants.

---

## 3. Energy content, and what the fuel costs to *make*

This is where the two fuels stop being comparable.

| | Per kg | Note |
|---|---|---|
| **Deuterium fusion** (D-D) | 3.4 × 10¹⁴ J | NASA figure for D-D; D-T is about 4× but needs bred tritium |
| **Antimatter** (annihilating with an equal mass of matter) | 1.8 × 10¹⁷ J | `2mc²`, so 500× deuterium by mass |

By energy density alone antimatter wins by 500×. **By production cost it loses by about
10¹⁸**, because antimatter is not mined, it is manufactured — and a particle accelerator
makes it at roughly **10⁻⁹ efficiency**. One kilogram of antimatter costs about
1.8 × 10²⁶ J to produce and returns 1.8 × 10¹⁷ J when burned.

Deuterium, by contrast, is *mined*. It is 1 part in 6 700 of Earth's seawater, and water ice
is abundant throughout the outer system. A kilogram of deuterium costs whatever it costs to
separate it isotopically — an industrial process, not an energy transformation.

### What that does to a single trip

Earth → Mars, 100 t dry, antimatter drive at vₑ = 300 km/s:

| | |
|---|---|
| Propellant | 2.47 t, of which **1 237 kg is antimatter** |
| Exhaust kinetic energy delivered | 1.1 × 10¹⁴ J |
| Energy to produce that antimatter, at 10⁻⁹ | 2.2 × 10²⁹ J |
| Energy to produce it, at 10⁻² | 2.2 × 10²² J |

The same trip on a fusion torch at vₑ = 100 km/s needs 7.6 t of propellant, which is
plasma the reactor must heat. Delivering 1.1 × 10¹⁴ J of exhaust energy at 30 % efficiency
means the reactor produces 3.7 × 10¹⁴ J, which is **1.1 kg of deuterium**.

**So the same trip costs 1.1 kg of mined deuterium or 1 237 kg of manufactured antimatter.**
Those are not two flavours of the same thing.

---

## 4. The problem: antimatter production is a megastructure

The design says the Workers make antimatter near the Sun. The Sun is the only place with
enough energy to make it at all, so that part is right. But the arithmetic is brutal.

Collector output, at 30 % end-to-end conversion:

| Distance | Flux | Per km² | Per km² per year |
|---|---|---|---|
| 0.1 AU | 137 kW/m² | 41.0 GW | 1.3 × 10¹⁸ J |
| 0.2 AU | 34.2 kW/m² | 10.3 GW | 3.2 × 10¹⁷ J |
| 0.39 AU (Mercury) | 9.0 kW/m² | 2.7 GW | 8.5 × 10¹⁶ J |
| 0.72 AU (Venus) | 2.6 kW/m² | 0.79 GW | 2.5 × 10¹⁶ J |
| 1.0 AU (Earth) | 1.4 kW/m² | 0.39 GW | 1.3 × 10¹⁶ J |

At the **real** accelerator efficiency of 10⁻⁹, a square kilometre at 0.2 AU yields about
**two nanograms of antimatter a year**. Twenty thousand years to fuel one Mars trip.

At a century-ahead efficiency of **10⁻²**, the numbers become a design:

| Production efficiency | kg/yr per 100 km² at 0.2 AU | Collector area for 1 Mars trip/yr |
|---|---|---|
| 10⁻⁹ | 1.8 × 10⁻⁷ | 6.9 × 10¹¹ km² |
| 10⁻⁶ | 1.8 × 10⁻⁴ | 6.9 × 10⁸ km² |
| 10⁻⁴ | 0.018 | 6.9 × 10⁶ km² |
| 10⁻³ | 0.18 | 6.9 × 10⁵ km² |
| **10⁻²** | **1.8** | **6.9 × 10⁴ km²** |
| 10⁻¹ | 18 | 6.9 × 10³ km² |

Ten thousand square kilometres at 0.1 efficiency, or seventy thousand at 0.01. For scale,
Greece is 132 000 km². So a working antimatter economy needs a collector field **the size of
a country**, held in a solar orbit, feeding a chain of production stations.

**That is either the best thing in the setting or the worst.** It is a megastructure, which
means it is a place, which means it can be found, blockaded and destroyed — and the war is
over exactly that. But at a million square kilometres it is also implausible that anyone
could hide it, lose it, or rebuild it quickly, and the whole strategic layer collapses into
"who owns the collectors".

---

## 5. The design decision this forces

The energy density argument in the setting is sound and should stay: antimatter is 500×
deuterium by mass, it is made only where sunlight is intense, and that is why the Workers
sit inward and the Illuminus sit outward. The magnitudes are right.

**But "antimatter for everything" does not survive the arithmetic.** The design needs to
choose, and my recommendation is the middle path:

1. **Antimatter is for the military and the flagship.** A few tonnes a year, reserved for
   warships and the handful of high-value transits. At vₑ = 1 000 km/s a Jupiter run needs
   under 2 t of propellant, and a navy can be fuelled by a collector field of a few thousand
   square kilometres — a megastructure, but a *countable* one that makes a legible target.

2. **The civilian economy runs on fusion.** Bulk cargo, colony supply, terraforming
   freighters: all fusion, because the fuel is mined rather than manufactured and the
   infrastructure is distributed instead of concentrated in one naked place.

3. **Antimatter is the thing the war is actually fought over**, not a general fuel. This
   sharpens rather than changes the Jovian logic: the Illuminus hold the deuterium and the
   Workers hold the antimatter, so a decisive strike on either is decisive for the other.

That gives three resource tiers that each mean something: **deuterium** paces the economy,
**antimatter** paces the war, and **collector area** paces antimatter.

### The number still to fix

**Antimatter production efficiency is the single most load-bearing figure in the setting.**
At 10⁻⁹ there is no game. At 10⁻² there is a hard but workable one. It should be written
into the design as an explicit decision, with the collector-area consequence attached, so
that it cannot drift.

---

## 6. Correction: Hohmann is the wrong model for a torch ship

Sections 1–2 assume a **minimum-energy Hohmann transfer**, which is optimal for a
low-thrust ship and badly wrong for a fusion torch or an antimatter drive. A ship that can
burn continuously does not coast for months; it accelerates to the midpoint, turns over,
and decelerates. That is a **brachistochrone**, and it is days to weeks rather than months.

### The times

`t = 2·sqrt(d/a)` and `Δv = 2·sqrt(a·d)` for a flip-and-burn between rest and rest, at the
closest-approach distance:

| Acceleration | Earth → Mars | Earth → Venus | Earth → Jupiter |
|---|---|---|---|
| 0.001 g | 65 d / 55 km/s | 48 d / 40 km/s | 185 d / 157 km/s |
| 0.01 g | 21 d / 175 km/s | 15 d / 127 km/s | 59 d / 497 km/s |
| **0.1 g** | **6.5 d / 554 km/s** | **4.8 d / 403 km/s** | **18.5 d / 1 571 km/s** |
| 1.0 g | 2.1 d / 1 753 km/s | 1.5 d / 1 274 km/s | 5.9 d / 4 967 km/s |

*(time / delta-v)*

So the setting's fast transits are real. But note the two scaling laws, which are the
reason this is a design decision rather than a free improvement:

- **`t ∝ 1/√a`** — halving the acceleration costs only 41 % more time.
- **`Δv ∝ √a`** — halving the acceleration saves only 29 % of the delta-v.

**Trip time is cheap; delta-v is not.** A 0.1 g Mars run needs **76× the Hohmann delta-v**.
Only high exhaust velocity makes that affordable, and high exhaust velocity has its own
price.

### The real constraint is the power plant, not the propellant

Thrust, mass flow, jet power and plant mass are one coupled system:

```
F = m·a          mdot = F/vₑ          P = F·vₑ/2          M_plant = P / SP
```

where `SP` is the plant's **specific power** in W/kg. Eliminating thrust gives the
governing relation:

> **`a = 2·SP / vₑ`**

Acceleration — and therefore trip time — depends only on the plant's specific power and the
exhaust velocity. **Not on ship size.** That single equation is the design space.

It also shows the trap: raising `vₑ` to cut propellant raises the power needed *linearly*,
which loads plant mass, which is dry mass, which raises the mass ratio again. The two
effects fight, and there is an optimum.

### A worked case: Mars in about five days

100 t payload, structure 10 % of dry mass, arrival at rest:

| vₑ | Minimum plant specific power | Plant mass | Propellant | Launch mass | Payload fraction |
|---|---|---|---|---|---|
| 100 km/s | 31 300 kW/kg | 80 t | 50 700 t | 51 100 t | 0.2 % |
| 200 km/s | 3 900 kW/kg | 80 t | 3 100 t | 3 200 t | 3.1 % |
| 300 km/s | 2 300 kW/kg | 80 t | 1 190 t | 1 270 t | 7.9 % |
| **600 km/s** | **1 850 kW/kg** | **80 t** | **325 t** | **500 t** | **20 %** |
| 1 000 km/s | 2 100 kW/kg | 80 t | 170 t | 350 t | 29 % |

**The sweet spot is around vₑ = 600 km/s**, and the number to look at is the second
column: **1 850 kW/kg**. For scale, the best fission concepts reach about 1 000 kW/kg and
fusion plants are usually assumed at 10 000–30 000 kW/kg. So a five-day Mars transit needs
a power plant roughly **twice as good as the best fission ever proposed, and five to fifteen
times *worse* than a fusion plant is normally assumed to be.**

That is the whole answer: **a fusion torch doing days-to-weeks transits is not
propellant-limited, it is a specific-power problem** — and the required specific power is
well inside what fusion should manage. Where this bites is not the crossing but the ends:
departing from low orbit and arriving into one still costs the same 9–10 km/s, and that is
where a torch's mass ratio is actually spent.

### Waste heat, and why a torch cannot hide

A 100 GW plant at 90 % efficiency still radiates 10 GW. At 1 500 K that needs about
**0.035 km²** of radiator — a 190 m square, on a 500 t ship — and it glows.

That is a *good* property for this design. The combat section says a fusion torch under
power is visible at enormous range and that stealth is running cold behind a body. Days-long
brachistochrone transits mean ships are under power for most of a crossing, so the sky is
full of bright moving things and hiding is something you do, not something you have.

---

## 7. Assumptions, for anyone who wants to argue

- **Both models are idealised.** Section 1–2 are Hohmann transfers between circular
  coplanar orbits; section 6 is a brachistochrone between rest and rest, which is the
  cheapest *shape* for a given acceleration but ignores the departure and arrival costs
  from low orbit. A real transit is neither: it is a spiral out, a fast leg, and a spiral
  in. No Oberth effect, no gravity assists, no aerobraking.
- Plant mass is charged against dry mass, but the *drive* — magnets, shielding, structure
  to carry the thrust — is not costed separately.
- The plant's specific power is the free parameter in section 6. Nothing here says a
  1 850 kW/kg plant is buildable; it says what one would have to be.
- Plane change applied at the destination and not optimised; a split plane change across
  both burns would be cheaper.
- 15 % flat margin on the ideal figure.
- Exhaust velocities are placeholders chosen to bracket the plausible range. The design has
  not yet fixed them, and they matter more than anything else here: propellant mass is
  exponential in `Δv/vₑ`.
- Fusion energy is the D-D figure per kilogram of deuterium *consumed in the plasma*.
  Reactor mass, shielding and the energy cost of refining the fuel are not counted; at these
  magnitudes they are not the constraint, but reactor **power** is — a 100 kN engine at
  vₑ = 100 km/s is a 5 GW jet.
- Collector conversion is 30 % end-to-end, from sunlight to stored antimatter, excluding the
  production efficiency which is shown separately.
