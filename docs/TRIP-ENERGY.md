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

## 7. Where the antimatter comes from: the plant at Venus

The design says the Workers make antimatter near the Sun. What does that cost in power, and
how big is the plant?

### Sunlight at Venus

| Orbit | Flux | vs Earth | Per km² at 30 % end-to-end |
|---|---|---|---|
| Mercury 0.387 AU | 9 126 W/m² | 6.7× | 2.74 GW |
| **Venus 0.723 AU** | **2 613 W/m²** | **1.92×** | **0.78 GW** |
| Earth 1.000 AU | 1 367 W/m² | 1.00× | 0.41 GW |
| Mars 1.524 AU | 589 W/m² | 0.43× | 0.18 GW |
| Jupiter 5.204 AU | 51 W/m² | 0.04× | 0.02 GW |

A square kilometre at Venus makes **0.78 GW**, or 2.5 × 10¹⁶ J in a year.

### Power required per tonne of antimatter

Antimatter costs `2mc²` = 1.8 × 10¹⁷ J/kg to *make* at 100 % efficiency. Real accelerators
run at about 10⁻⁹, so the real figure is 1.8 × 10²⁶ J/kg.

| Production efficiency | Power for 1 tonne/yr | Collector area at Venus | For scale |
|---|---|---|---|
| 100 % | 5.7 TW | 7 300 km² | 5 % of Greece |
| 10 % | 57 TW | 73 000 km² | half of Greece |
| **1 %** | **570 TW** | **728 000 km²** | **5.5× Greece** |
| 0.1 % | 5 700 TW | 7 300 000 km² | 1.6 % of Venus's surface |
| 10⁻⁴ | 57 000 TW | 73 000 000 km² | 16 % of Venus's surface |
| 10⁻⁹ | 5.7 × 10⁶ TW | 7.3 × 10⁹ km² | 16 000× Venus's surface |

For context, humanity's total primary energy use is about **20 TW**. A one-tonne-a-year
antimatter plant at 1 % production efficiency draws **28× the entire industrial output of
Earth**, and at 10⁻⁹ it is absurd — which is the same conclusion as §4, now with the
collector geometry attached.

### The bottleneck is not the collector

At Venus, a terawatt of collection takes only **1 276 km²** of array. Sunlight is not scarce
there. What decides everything is the **production efficiency**, because it decides how much
of that terawatt becomes antimatter and how much becomes heat.

### The waste heat is as big as the plant

At 1 % conversion, **99 % of the collected power is dumped as heat**. For the one-tonne plant
that is 565 TW, and radiating it is not a detail:

| Radiator temperature | Flux | Area needed |
|---|---|---|
| 800 K | 23 kW/m² | 24 300 km² |
| 1 000 K | 57 kW/m² | 9 960 km² |
| 1 500 K | 287 kW/m² | 1 970 km² |
| 2 000 K | 907 kW/m² | 622 km² |

At 1 000 K the radiator is **comparable to the collector**. An antimatter plant is not a
solar farm with a factory attached — it is a solar farm that is **mostly radiator**, glowing
in the dark, and that is a thing you can see from Earth.

### "Near the Sun" has a floor, and it is thermal

The temptation is to go inward, because flux rises as `1/r²`:

| Orbit | Flux vs Earth | Collector for 1 t/yr at 1 % | Black-panel equilibrium temperature |
|---|---|---|---|
| 0.20 AU | 25.0× | 55 600 km² | **881 K (608 °C)** |
| 0.30 AU | 11.1× | 125 000 km² | — |
| Mercury 0.387 AU | 6.7× | 208 000 km² | 633 K (360 °C) |
| Venus 0.723 AU | 1.9× | 728 000 km² | 463 K (190 °C) |
| Earth 1.000 AU | 1.0× | 1 391 000 km² | 394 K (121 °C) |

At 0.2 AU a passive panel sits at **881 K** before it has done anything. That is why
"near the Sun" is not "as close as you like": the array has to survive its own
illumination, and radiator area *grows* as the plant moves inward because there is more
waste heat to dump.

**Venus orbit is a reasonable compromise** — 1.9× Earth's flux, a survivable 463 K, and a
planet already claimed by the Workers with an atmosphere to hide behind.

### What this does for the setting

1. **The plant is a place, and a legible one.** At 1 % efficiency it is a million square
   kilometres of collector and radiator — big enough to find, blockade and destroy, small
   enough to be a countable asset rather than a background fact.
2. **The Workers' advantage is technology, not sunlight.** Venus gives 1.92× Earth for the
   same steel. That is a 2× saving on structure — useful, not decisive. What actually
   matters is whether their converters are at 10⁻² or 10⁻⁴, and that is a technology number,
   not an orbital-mechanics one. Their real advantage is that **nobody else has a working
   converter at all**.
3. **It glows.** 565 TW of waste heat at any radiator temperature is a beacon. The Workers'
   antimatter production is visible from the outer system, which means the Illuminus always
   know roughly how much they are making — and the Workers know they know.

---

## 8. If the Workers reach 50 % conversion

The design asks what happens if their converters run at **50 %** — half of the collected
sunlight becoming antimatter rest mass. It transforms the picture, but not into the one you
might expect.

### Everything about the plant gets easier

| | At 1 % | **At 50 %** | Improvement |
|---|---|---|---|
| Power for 1 t/yr | 570 TW | **11.4 TW** | 50× |
| Collector at Venus | 728 000 km² | **14 600 km²** | 50× |
| Waste heat | 565 TW | **5.7 TW** | 50× |
| Radiator at 1 000 K | 9 960 km² | **101 km²** | 50× |

**The plant stops being a megastructure.** 14 600 km² is a large industrial site — a fifth
of Greece — not a planet-sized undertaking. And with 50 % of the power leaving as antimatter
rather than heat, **the radiator stops mattering**: 101 km² is a detail rather than the
dominant structure.

Per square kilometre of collector at Venus, 50 % conversion yields **69 kg of antimatter a
year**.

### But the ships eat it faster than the plant makes it

This is the part that decides the design. A **500 t ship** making a fast Mars transit —
0.1 g, arriving at rest, vₑ = 500 km/s — needs **106 t of antimatter for one trip**.

| Collector field | Power | Antimatter/yr | 500 t ship transits/yr |
|---|---|---|---|
| Greece (132 000 km²) | 103 TW | 9.1 t | **0.09** |
| 10⁶ km² (7.6× Greece) | 784 TW | 69 t | **0.65** |
| 10⁷ km² (76× Greece) | 7 838 TW | 687 t | **6.5** |

**A collector field the size of Greece fuels one fast crossing every eleven years.** Even a
field of ten million square kilometres — nearly 2 % of Venus's surface — supports six or
seven a year. The plant is no longer the impossible part; the *ships* are.

### So antimatter is for couriers, not for cargo

The antimatter cost scales with ship mass, so a **100 t courier** costs 21 t a trip against
the 500 t ship's 106 t. For a fixed antimatter budget the tonnage moved is the same either
way; what changes is how many hulls you can fly and how often.

That lands exactly on the tiering the design already has, now for a hard reason:

| Role | Drive | Why |
|---|---|---|
| Bulk cargo, colony supply, terraforming freight | **Fusion** | Deuterium is mined, not manufactured. Energy is the cheap part; reaction mass is not. |
| Couriers, warships, priority transits | **Antimatter** | The only way to move a small mass *fast*, and fast is what a warship is for |

**50 % conversion makes antimatter a real strategic capability rather than a fantasy fuel.**
It does not make it a general-purpose one.

### What it does to the war

1. **The Illuminus lose their speed advantage.** Until now the asymmetry was Workers =
   numbers, Illuminus = speed and quality. If the Workers can fuel fast ships, the Illuminus
   have better hulls and nothing else. Their doctrine has to become *not being found*.
2. **The Illuminus lose the fuel argument too.** The design gave them deuterium
   self-sufficiency as their counterweight to the Workers' antimatter. If the Workers can
   make antimatter at 50 %, deuterium is the cheap fuel for the *bulk* economy and stops
   being a strategic lever.
3. **Antimatter becomes tradeable.** At 1 % production it is a war asset and nothing else.
   At 50 % the Workers can sell it — to Earth, to the scoundrel, even to the Illuminus. That
   is a diplomatic instrument, and it makes them the indispensable party rather than merely
   the besieged one.
4. **The bottleneck moves to the antimatter in store.** Antimatter needs active magnetic
   containment and vacuum, drawing power continuously, so it cannot be stockpiled cheaply —
   and a containment failure is a bomb. At 50 % the temptation is to hold large stocks, and
   large stocks are the most dangerous objects in the solar system:

   | Stock held | If containment fails |
   |---|---|
   | 1 kg | 3.6 megatons |
   | 8 kg | 29 megatons |
   | 83 kg | 286 megatons |

   **Antimatter logistics are just-in-time, and the depot is the single most valuable and
   most fragile thing anyone owns.** That is a much better strategic engine than a simple
   fuel shortage, because it makes *tempo* the contested resource rather than quantity.

### Is 50 % physically defensible?

It is extraordinary but not forbidden. The theoretical ceiling is 100 % — all the collected
energy becoming antimatter rest mass — so 50 % is half the maximum. What makes it hard is
that pair production needs photons above **1.022 MeV** (gamma rays), and sunlight peaks near
**2 eV**. The energy must be re-concentrated into gammas first, and that step is where the
losses live today.

A plant doing it at 50 % end to end would need solar-pumped gamma-ray lasing and
photoproduction on a high-Z target at near-ideal yield. That is exactly the kind of thing
worth calling **the Workers' defining technology** and worth the Illuminus trying to steal —
which is the thematic gun §1.3 already put on the mantelpiece.

---

## 9. The drive plant is the wall, and the two plants are not the same object

There are two power plants in this setting and **only one of them is a real problem.**

**The antimatter plant is stationary**, so its mass is free. At 50 % conversion it needs
14 600 km² of collector for a tonne a year and can weigh whatever it likes. Nothing about it
is hard except the conversion efficiency.

**The drive plant has to fly**, so its mass *is* the ship. That is the whole difficulty.

### The relation that settles it

For a ship of total mass `m` accelerating at `a` with exhaust velocity `vₑ`:

```
F  = m·a                 thrust
P  = F·vₑ/2              jet power
Mp = P/SP                plant mass, SP = specific power in W/kg
```

Substituting:

> **`Mp/m = a·vₑ / (2·SP)`**

**The plant's mass fraction does not depend on ship size at all.** Build a bigger ship and
the reactor grows with it. The only ways to a higher acceleration are a lower exhaust
velocity or a better specific power.

| Acceleration | vₑ | SP = 200 kW/kg | SP = 1 000 | SP = 10 000 | SP = 50 000 |
|---|---|---|---|---|---|
| 0.003 g | 500 km/s | 3.7 % | 0.7 % | 0.1 % | 0.0 % |
| 0.010 g | 500 km/s | 12.3 % | 2.5 % | 0.2 % | 0.0 % |
| 0.030 g | 500 km/s | 36.8 % | 7.4 % | 0.7 % | 0.1 % |
| **0.100 g** | 500 km/s | **122.6 %** | **24.5 %** | 2.5 % | 0.5 % |
| 0.300 g | 500 km/s | 367.7 % | 73.5 % | 7.4 % | 1.5 % |
| 1.000 g | 500 km/s | 1 225.8 % | 245.2 % | 24.5 % | 4.9 % |

*(plant as a fraction of the ship; above 100 % means the reactor alone outweighs the ship)*

For a 0.1 g ship at vₑ = 500 km/s the plant must be under a quarter of the ship, which needs
**SP ≥ 981 kW/kg.**

### What that means for the setting

| Assumption | Value | Verdict |
|---|---|---|
| NERVA flew at | ~200 kW/kg | 0.03 g is the ceiling |
| Best fission concept | ~1 000 kW/kg | 0.1 g, and the reactor is a quarter of the ship |
| Fusion, normally assumed | 10 000–100 000 kW/kg | 0.1–1 g comfortable |

So:

1. **`Mp/m` is size-independent, so a bigger ship does not accelerate harder.** The only
   route to speed is a better reactor. There is no "build it large enough" escape.
2. **0.1 g is reachable; 1 g is not.** At 1 g even 10 000 kW/kg leaves the reactor at a
   quarter of the ship, before payload, structure, propellant or tankage. **The design's
   0.1–1 g crewed band is therefore realistic at the bottom and aspirational at the top** —
   and the top of the band should be understood as a capability only the best hulls have.
3. **Fast ships are scarce for a reactor reason, not a fuel reason.** The antimatter is
   manufacturable, the collector field is a civil engineering project, and the propellant is
   a rounding error. The reactor is the bottleneck, and the reactor is a technology, not a
   resource.
4. **That makes the Workers' 50 % converter necessary but not sufficient.** They can make
   fuel nobody else can make; they still have to build a drive nobody else can build. The
   Illuminus' five-times-better hulls are therefore aimed at exactly the right thing.

**[OPEN] Drive specific power for each faction.** It is now the single number that decides
who has fast ships, and it is independent of the antimatter question entirely.

---

## 10. The plant is in the clouds, and it is built from the atmosphere

The antimatter plant is not on Venus's surface. It is in the **cloud deck**, and its
collectors are manufactured from the CO₂ they float in. That changes what the whole
terraforming programme is.

### The cloud deck is the only habitable place on Venus

| Altitude | Pressure | Temperature | |
|---|---|---|---|
| 50 km | 0.75 bar | 348 K (+75 °C) | airship altitude |
| **55 km** | **0.53 bar** | **300 K (+27 °C)** | **breathing mask, not a pressure suit** |
| 60 km | 0.24 bar | 263 K (−10 °C) | airship altitude |
| surface | **92 bar** | **737 K (+464 °C)** | nothing lands there and stays |

At 55 km a person needs a breathing mask and ordinary clothes. **That is where the
Workers live and work**, and it is why the colony is in the clouds rather than on the
ground. It also means the antimatter plant, the processing plants and the settlement are
the same object.

### The atmosphere is not a constraint on building — it is inexhaustible for building

Venus's atmosphere is **4.77 × 10²⁰ kg**, ninety-three times Earth's. The carbon within it
is **1.26 × 10²⁰ kg**.

| Target | Collector mass | Fraction of Venus's carbon |
|---|---|---|
| Antimatter plant, 1 t/yr (14 600 km²) | 7.3 × 10⁹ kg | 6 × 10⁻¹¹ |
| Antimatter plant, 100 t/yr | 7.3 × 10¹¹ kg | 6 × 10⁻⁹ |
| Shade swarm blocking 2 % of the disc | 4.6 × 10¹² kg | 4 × 10⁻⁸ |

**The collectors are a rounding error against the atmosphere they are made from.** At
0.5 kg/m² a 14 600 km² array is 1.5 × 10⁻¹¹ of the atmospheric mass — the surface pressure
would drop from 92 bar by 0.0000000014 bar.

So the swarm is **self-supplying**: the atmosphere is the feedstock, and the product is the
machine that dismantles the atmosphere. There is no material shortage at any point, and the
question "is there enough carbon to build the collectors" has an overwhelming yes.

### But the collectors cannot be the carbon sink

The obvious next thought is that the swarm *is* the terraforming — build enough panels and
the atmosphere goes away. It does not work, and the number is decisive:

To reach Earth-like pressure, virtually all 4.73 × 10²⁰ kg of CO₂ must go. Made into
0.5 kg/m² panels that is **2.5 × 10¹⁴ km²** — **546 000 times Venus's entire surface area.**
That is not a swarm, it is a shell hundreds of thousands of layers deep.

**The collectors are a bootstrap, not a solution.** They supply the power and shade; they
cannot absorb the carbon.

### What actually removes the carbon

Not splitting it. Breaking CO₂ into carbon and oxygen means breaking two C=O bonds at
1 598 kJ/mol, which is **1.3 × 10⁸ J per kg of carbon**:

| | |
|---|---|
| Energy to split the atmosphere | 1.72 × 10²⁸ J |
| Sunlight falling on Venus | 1.20 × 10¹⁸ W |
| Time using *all* of it | **452 years** |
| At a realistic 10 % of it | **4 500 years** |

The cheap route is **mineral carbonation** — reacting the CO₂ with calcium silicates in
Venus's basaltic crust to make calcite and quartz:

```
CO2 + CaSiO3 -> CaCO3 + SiO2
```

which is **exothermic**. It releases heat rather than needing it, so the constraint stops
being energy entirely and becomes **rock throughput**:

| Target timescale | Silicate rock per day | Volume per day |
|---|---|---|
| 100 years | 2.3 × 10¹⁶ kg | 7 800 km³/day |
| 300 years | 7.5 × 10¹⁵ kg | 2 600 km³/day |
| 1 000 years | 2.3 × 10¹⁵ kg | 780 km³/day |
| 5 000 years | 4.5 × 10¹⁴ kg | 156 km³/day |

**A three-century terraforming of Venus means moving a mountain range every single day.**
That is the honest industrial scale of the project, and it is why the Workers are defined by
production technology rather than by weapons.

### What this does to the setting

1. **The plant, the settlement and the first terraforming stage are one thing.** The cloud
   cities are the antimatter works, and the panels they make are the first step of the
   terraforming. That is a much stronger design than three separate systems.
2. **The terraforming clock is an industrial throughput problem.** It is paced by mining and
   manufacturing, which means it is a thing the player can *watch advance* and a thing worth
   attacking. The Illuminus cannot out-build it; they can only slow it.
3. **Two useful products from one process.** The panels shade Venus toward habitability at
   the same time as they power the antimatter works, so the cooling and the industry are the
   same project.
4. **Venus's carbon has a market: Mars.** The Illuminus need volatiles and a thicker
   atmosphere; Venus has a surplus of exactly the carbon Mars lacks. That makes the two
   terraformings *complementary* rather than merely parallel — which is a far more
   interesting war, because the rational move is trade and the war is what happens instead.
5. **Sequestering is the long pole, not building.** Construction can expand exponentially
   from a small seed because the feedstock is everywhere. Rock processing cannot: it is
   linear in machines, and machines cost material and time. **The bottleneck is the crusher
   fleet, not the solar array.**

---

## 11. Can you actually make enough antimatter? Yes — at a price

The short answer: **the antimatter was never the hard part, and a nation-scale collector
field makes plenty.** Whether it is "enough" depends entirely on what you are trying to fly.

At 50 % conversion, making one kilogram of antimatter costs **3.6 × 10¹⁷ J** of collected
energy — 1.8 × 10¹⁷ J becomes the antimatter and the other half is lost.

### What different plant sizes buy

| Plant | Power | Collector at Venus | vs Greece | What it fuels |
|---|---|---|---|---|
| 1 t/yr | 11.4 TW | 14 600 km² | 0.11× | one 500 t transit every 106 years |
| 10 t/yr | 114 TW | 146 000 km² | 1.1× | a courier service |
| **100 t/yr** | **1 141 TW** | **1 455 000 km²** | **11×** | **a strategic capability** |
| 1 000 t/yr | 11 407 TW | 14 553 000 km² | 110× | a war at unmatched tempo |

Greece is 132 000 km²; Venus's surface is 4.60 × 10⁸ km².

### The design point that works: couriers, not capital ships

Antimatter cost scales with ship mass, so **small and fast is the efficient use**:

| Ship | Antimatter per one-way Mars crossing | Crossings per 100 t/yr |
|---|---|---|
| 50 t | 10.6 t | 9.4 |
| **100 t** | **21.3 t** | **4.7** |
| 500 t | 106.4 t | 0.9 |

**A 100 t/yr plant flies a 100 t courier to Mars about five times a year.** A 1 000 t/yr
plant flies it forty-seven times a year — that is a fleet, and a fleet nobody else can match.

### So the answer is yes, and it changes what the war is about

**A collector field about twice the area of Greece makes enough antimatter for roughly fifty
courier crossings a year.** That is a hard industrial project, but it is a *nation*-scale
project, not a solar-system-scale one. Venus's surface could hold ten thousand of them.

Which means:

1. **The binding constraint is the collector field and the drive reactors, never the fuel.**
   This is the third time the same conclusion has come out from a different direction, and it
   should now be treated as settled.
2. **Antimatter abundance is a choice about tempo, not a scarcity.** How much speed the
   Workers buy is a budget decision — how much of their civilisation to spend on crossing
   time rather than on the terraforming. Those two compete for the same collector area, the
   same carbon, and the same yards.
3. **[OPEN] That competition is the strategic heart of the Workers' campaign.** Every
   square kilometre of collector turned over to antimatter is a square kilometre not
   shading Venus or grinding rock. **The fleet and the terraforming are the same budget**,
   and the player decides the split. No other mechanic in the design captures the faction's
   dilemma so cleanly.

---

## 12. Orbital panels: the area is bigger than I said, and distance is the lever

The earlier sections quoted a *generator* area and stopped there. A real orbital plant has
three surfaces — generator, radiator and structure — and the total is what gets built. This
section corrects the figures and works out where such a plant should actually go.

### The thermal limit comes first

A panel absorbs sunlight and converts part of it; **the rest becomes heat**, and the only way
to shed it in vacuum is to radiate. For a thin film with two radiating faces:

```
P_rad  = 2·σ·T⁴·A
T      = ( flux·(1-η) / 2σ )^(1/4)
```

Note what that says: **a lower-efficiency film runs *cooler*, not hotter**, because it absorbs
less net energy. That is why cheap thin films survive sunward where good cells bake.

| Orbit | Flux | Panel temp at η = 30 % | Panel temp at η = 50 % |
|---|---|---|---|
| 0.20 AU | 34 171 W/m² | **678 K** | 623 K |
| 0.30 AU | 15 187 W/m² | **553 K** | 509 K |
| Mercury 0.387 AU | 9 126 W/m² | 487 K | 448 K |
| Venus 0.723 AU | 2 613 W/m² | 356 K | 328 K |
| Earth 1.000 AU | 1 367 W/m² | 303 K | 279 K |

Conventional silicon dies near 400 K. Refractory thin films and concentrator cells reach
500–600 K. **That ceiling is what decides where the plant can be.**

### The corrected areas, at Venus orbit

For one tonne of antimatter a year at 50 % conversion — 11.41 TW collected:

| Panel η | Panel temp | Generator | Radiator | Structure | **Total** | vs Greece |
|---|---|---|---|---|---|---|
| 40 % | 343 K | 10 915 km² | 10 915 km² | 2 183 km² | **24 013 km²** | 0.18× |
| 30 % | 356 K | 14 553 km² | 14 553 km² | 2 911 km² | **32 017 km²** | 0.24× |
| 20 % | 368 K | 21 830 km² | 21 830 km² | 4 366 km² | **48 026 km²** | 0.36× |
| **10 %** | **379 K** | **43 660 km²** | **43 660 km²** | **8 732 km²** | **96 051 km²** | **0.73×** |
| 5 % | 385 K | 87 319 km² | 87 319 km² | 17 464 km² | **192 102 km²** | 1.46× |

**The radiator is as large as the generator.** Half the collected power is waste heat at
50 % conversion, and shedding it at a few hundred kelvin needs as much area as collecting it
did. Any figure that quotes only the collector understates the plant by about half.

At a realistic 10 % for a long-lived thin film, **one tonne a year is about 96 000 km²** —
most of Greece, not a fifth of it.

### Distance is the strongest lever there is

Flux falls as `1/r²`, so moving sunward shrinks everything:

| Orbit | Flux | Panel temp (η = 10 %) | Generator | **Total** | vs Greece |
|---|---|---|---|---|---|
| **0.20 AU** | 34 171 W/m² | 722 K | 3 338 km² | **4 006 km²** | 0.03× |
| **0.30 AU** | 15 187 W/m² | 589 K | 7 511 km² | **9 013 km²** | 0.07× |
| Mercury 0.387 AU | 9 126 W/m² | 519 K | 12 499 km² | **14 998 km²** | 0.11× |
| Venus 0.723 AU | 2 613 W/m² | 379 K | 43 660 km² | **52 392 km²** | 0.40× |
| Earth 1.000 AU | 1 367 W/m² | 323 K | 83 453 km² | **100 144 km²** | 0.76× |

**A plant at 0.3 AU is five times smaller than the same plant at Venus orbit**, and its
panels run at 589 K — hot, but inside what a refractory film can take. At 0.2 AU it is
thirteen times smaller, and 722 K is beyond anything plausible.

### So where does it go?

There is a genuine tension, and it is a good one:

- **At Venus orbit** the plant is cool, convenient, sits in the cloud deck you already
  inhabit, and feeds the works directly — but it is five times the area.
- **Sunward at ~0.3 AU** it is a fifth the area and a fifth the mass — but it is a
  separate location, the panels are near their thermal limit, and the energy has to get
  back to Venus somehow.

**[DECIDED] The answer is both, and it is the interesting one.** A **power swarm at
0.25–0.35 AU** — small, hot, refractory, cheap in material — feeding a **processing works
in the Venus cloud deck** that is where people live. The two are joined by the only thing
that makes sense over that distance: **the antimatter itself.** The swarm makes it; ships
carry it in; the works uses it. No beamed power, no exotic transmission — the product is
already the energy carrier, which is the neatest thing about the whole arrangement.

### The corrected headline numbers

| Plant output | At 10 % panels | At 50 % panels (§13) |
|---|---|---|
| 1 t/yr | 16 500 km² | **3 300 km²** |
| 10 t/yr | 165 000 km² | **33 000 km²** |
| **100 t/yr** | 1 652 000 km² | **330 000 km²** |
| 1 000 t/yr | 16 520 000 km² | 3 305 000 km² |

*(Generator, radiator and structure; 0.30 AU.)* The 10 % column is what the figures in
§12's prose were based on. **§13 supersedes it**: the panels run at 50 %, which is a 5×
reduction.

Compare with the Venus-orbit figures, which were 5× larger. **A hundred tonnes of antimatter
a year — enough for roughly five fast courier crossings — needs a power swarm about twelve
times the area of Greece, sitting closer to the Sun than Mercury.** That is the real scale of the
Workers' strategic capability: large, but a nation's project rather than a world's.

It also gives the swarm a **vulnerability with a shape**: it is not at Venus, it is not
defended by the cloud deck, and it is the single point on which the entire antimatter economy
depends. Small, hot, far from home, and indispensable.

---

## 13. The locked design point: 100 t/yr on quantum panels

**[DECIDED] The Workers' antimatter plant produces 100 tonnes a year, and its panels run at
50 % efficiency.** The efficiency is "quantum magic" in the sense that it is far beyond
anything conventional photovoltaics reaches, and it is the same order of engineering as
their 50 % antimatter converter — the two are the same technology family, which is why
nobody else has either.

### The design

| | |
|---|---|
| Antimatter output | **100 t/yr** |
| Location | Power swarm at **0.30 AU** |
| Panel efficiency | **50 %** |
| Panel temperature | **509 K** |
| Collected power | 1 141 TW |
| Generator | 150 216 km² |
| Radiator | 150 216 km² |
| Structure | 30 043 km² |
| **Total** | **330 475 km²** |
| | **2.5 × the area of Greece** (0.07 % of Venus's surface) |
| Panel mass at 0.1 kg/m² | 3.3 × 10¹⁰ kg — 33 million tonnes |

**The efficiency is worth exactly 5× over the 10 % case**, and it is worth that much for two
reasons rather than one:

1. **Half the area collects the same power**, which is the obvious gain.
2. **The panels run cooler, not hotter.** A better cell converts more of what it absorbs, so
   less becomes heat: 509 K at 50 % against 589 K at 10 %. The thermal ceiling moves
   *outward* with efficiency, so the gain is not partly eaten by cooling.

That second point is worth dwelling on, because it is counter-intuitive: **making the panels
better makes them cooler.** The radiator is still half the plant, though — at 50 %
conversion half the collected power still leaves as heat, and that is a floor set by the
converter, not the panels.

### Where else it could go

| Orbit | Panel temp (50 %) | Total area | vs Greece |
|---|---|---|---|
| 0.25 AU | 557 K | **229 496 km²** | 1.7× |
| **0.30 AU** | **509 K** | **330 475 km²** | **2.5×** |
| 0.35 AU | 471 K | 449 813 km² | 3.4× |
| Mercury 0.387 AU | 448 K | 549 943 km² | 4.2× |
| Venus 0.723 AU | 328 K | 1 921 022 km² | 14.6× |

At 50 % the swarm can sit **as close as 0.25 AU** and still keep its panels at 557 K, which
is inside what a refractory film takes. Every step sunward is area and mass saved.

**[OPEN] 0.30 AU or 0.25 AU.** 0.30 AU is comfortable at 509 K and needs 2.5× Greece.
0.25 AU saves a third of the structure but runs the panels at 557 K, near the limit.
The choice is how much thermal margin the Workers buy with steel.

### Building it is cheap, and the numbers are blunt about it

At 0.1 kg/m² the swarm is 3.3 × 10¹⁰ kg. Over a ten-year build that is 9 × 10⁶ kg/day,
against the terraforming's **7.5 × 10¹⁵ kg of rock per day** (§10). The swarm is **nine
orders of magnitude less material** than the work going on below it.

It is large in area and trivial in mass. The energy to make the carbon is an ordinary
industrial process at perhaps 1 × 10⁷ J/kg — over ten years, **0.02 % of what the plant
collects.**

### What 100 t/yr buys

One-way Mars crossing at 0.1 g is 3.3 days and 277 km/s:

| Ship | Antimatter per crossing | Crossings per year |
|---|---|---|
| 50 t | 10.6 t | 9.4 |
| **100 t** | **21.3 t** | **4.7** |
| 200 t | 42.6 t | 2.3 |
| 500 t | 106.4 t | 0.9 |

**About five fast crossings a year for a 100 t courier — one every eleven weeks.** Or one a
year for a 500 t capital ship. A courier service and a strategic strike capability; not a
fleet. The Workers cannot fight a high-tempo war with this, but they can **decide things
quickly** with it.

### And what it does not buy

100 t/yr is 1.8 × 10²² J — **5 million megatons**, about 28 years of humanity's entire
primary energy use.

But splitting Venus's atmosphere would need **1.72 × 10²⁸ J**, so the entire annual output is
**0.01 % of that bill.** Splitting the CO₂ by antimatter is not a plan; it would take
**950 000 years** of the whole plant.

**So the antimatter is militarily decisive and terraformingly irrelevant**, and that is a
clean split: the antimatter buys *speed and violence*, and the terraforming is bought with
sunlight, rock and time. The two do not compete for the same resource — they compete for the
same **yards and workers**, which is a sharper constraint than a shared fuel, because it is
about factories and people rather than joules.

---

## 14. Assumptions, for anyone who wants to argue

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
  exponential in `Δv/vₑ`. Section 15's haul rates inherit that: at vₑ = 500 km/s a
  milligee hauler's mass ratio is 1.37, and at 100 km/s it would be much better — see
  [OPEN] §11.17.
- Section 15 charges drive energy as `Δv²/2 / efficiency` per kilogram delivered, which
  treats each transit as paying for its own kinetic energy from rest. A permanent fleet
  flying a circuit recovers none of it either, so the figure is right for the purpose, but
  it is a *jet* energy and says nothing about how much of the drive's output ends up in the
  exhaust versus the ship.
- Section 15 assumes the haul rate is set by the reactors rather than by the ice, the
  shipyards or the crews. Nothing here shows those are not the binding constraint at
  10¹⁵ kg/yr; it shows that the reactors would be.
- Fusion energy is the D-D figure per kilogram of deuterium *consumed in the plasma*.
  Reactor mass, shielding and the energy cost of refining the fuel are not counted; at these
  magnitudes they are not the constraint, but reactor **power** is — a 100 kN engine at
  vₑ = 100 km/s is a 5 GW jet.
- Collector conversion is 30 % end-to-end, from sunlight to stored antimatter, excluding the
  production efficiency which is shown separately.

## 15. Hydrogen is the clock, and the ice is not the constraint

This section closes an [OPEN] from §4 and settles where a terraforming programme's
hydrogen comes from, because §5's schemes all need it and none of them can make it.

### The solar wind is not a source, and the margin is not close

The Sun sheds about 2.5 × 10⁻¹⁴ solar masses a year in the wind. Spread over a sphere at
Venus' orbit that is **0.34 kg of hydrogen per km² per year** — and the *entire* wind
intercepted by Venus' disc comes to **39 tonnes a year**.

| Goal | Hydrogen needed | Years of the whole wind |
|---|---|---|
| Reduce all atmospheric CO₂ to graphite and water | 1.2 × 10¹⁹ kg | 3 × 10¹⁴ |
| A 100 m global ocean | 5.2 × 10¹⁸ kg | 1.3 × 10¹⁴ |
| A 2.7 km ocean, Earth-equivalent | 1.4 × 10²⁰ kg | 3.6 × 10¹⁵ |

To collect even the smallest of those from the wind would need a scoop about **10⁹ ×
Venus' surface area**. Ionisation does not help: a magsail collects an ionised wind
perfectly well, and the dilution still kills it. Moving to Mercury orbit buys a factor of
seven in flux and nothing else.

> **DECIDED. The solar wind is never a bulk hydrogen source. Terraforming hydrogen comes
> from ice, and ice comes from the outer system.**

What the wind *is* good for is texture, and it is worth keeping for that: magsails for
station-keeping and cheap low-thrust logistics, trace-gas scooping as a niche economy, and
radiation pressure as one of the forces the §13 swarm has to trim against. Never a line
item.

### The ice is genuinely not the constraint either

Europa holds an estimated two to three Earth oceans, roughly 3.5 × 10²¹ kg of water;
Ganymede and Callisto are comparable. Venus' entire 2.7 km appetite is **2.9 % of Europa
alone**, which is what makes the Jovian theatre the place the war is actually about — and
the deuterium rides along in the same ice, so §4.1's double role for those moons is
confirmed rather than assumed.

That was the expected answer. The surprise is one line down:

> **Ceres holds about 2.3 × 10²⁰ kg of water ice — roughly twice Venus' entire 2.7 km
> ocean, and about 20 % of it would cover Venus to a depth of 100 m — at a small fraction
> of the Jovian Δv.**

So the inner system does not need the outer system for water at all. Section 4.1's convoy
economy survives, but its justification has to change: the Jovian moons are the *cheap*
source, not the only one. That is a better strategic situation, not a worse one — it means
the Illuminus can deny a route without denying the resource, and it puts Ceres on the map
as the forward depot, bargaining chip and obvious place to fight.

### The real constraint is the transport energy, and it is enormous

A Jupiter–Venus crossing at vₑ = 500 km/s, over 4.2 AU (the orbit-to-orbit average, so
shorter than the 5.2 AU figure between the two orbits' far sides):

| Acceleration | Trip time | Δv | Mass ratio at vₑ = 500 km/s | Payload fraction |
|---|---|---|---|---|
| 0.1 g (warship sprint) | 18.5 d | 1 570 km/s | 23.1 | 4.3 % |
| 0.01 g | 58.6 d | 496 km/s | 2.7 | 37 % |
| 0.001 g (bulk hauler) | 185 d | 157 km/s | 1.37 | 73 % |

This is the design's best structural result so far, and it falls out of the physics rather
than being asserted:

> **Acceleration is the dial. Bulk ice moves slow, low and predictable; warships sprint.
> One propulsion physics, two logistics tiers.**

A milligee hauler still flies a powered brachistochrone — no coasting, so §3's "no long
slow crossing mode" survives — and delivers most of its wet mass as payload. Slow, visible,
schedule-able convoys are exactly the gameplay §4.1 wanted, and they are now a consequence
of the rocket equation rather than a convention. The hauler's reaction mass can be water,
so the cargo partially fuels its own run and a fraction of it is cracked for deuterium on
arrival.

**The energy is the wall.** At 50 % drive efficiency the ideal figure of 1.23 × 10¹⁰ J/kg
becomes 2.5 × 10¹⁰ J/kg delivered, and that makes the required jet power:

| Haul rate | Jet power | Reactor mass at SP = 100 kW/kg |
|---|---|---|
| 10¹² kg/yr | 781 TW | 7.8 × 10⁹ kg |
| 10¹⁵ kg/yr | 781 000 TW | 7.8 × 10¹² kg |
| 3.3 × 10¹⁷ kg/yr (the whole 2.7 km ocean in 300 yr) | 2.6 × 10²⁰ W | 2.6 × 10¹⁵ kg |

The last line is 258 000 TW and two and a half *billion* tonnes of reactor. **A 2.7 km
ocean delivered in three centuries is not a shipping problem, it is a Kardashev-scale
engineering project.** The Venus antimatter plant's entire 3.3 × 10¹⁰ kg collector field is
five orders of magnitude short of the drive plant that would need to move it.

### What this does to the design

1. **[DECIDED]-ready: hydrogen throughput sets the terraforming clock.** Dedicating
   shipping capacity to ice haulage is how a faction accelerates its survival project, and
   raiding the enemy's ice convoys directly slows their clock. The war and the countdown
   are now mechanically coupled: an attack on terraforming is a physical act with a
   measurable delay attached, not an abstract one.

2. **The scarcity row is volatile **transport capacity**, not volatiles.** The ice is
   abundant; the drives, the reactors and the power to run them are not. That is a much
   better thing for the game to be scarce, because it is buildable, targetable and
   visible.

3. **Scale the ambition to the power available, and let that be the story.** A 10 m ocean
   needs 5.2 × 10¹⁷ kg of hydrogen; 100 m needs 5.2 × 10¹⁸ kg. Both are within reach of a
   faction that has already built the §13 antimatter plant and a fusion drive fleet. The
   2.7 km Earth-equivalent is a *thousand-year* project and should be treated as the
   horizon the factions are arguing about, not the plan.

4. **A Venus-scale carbon sink is a 121-metre graphite layer.** All 1.26 × 10²⁰ kg of
   atmospheric carbon, reduced and left on the surface, is 5.6 × 10¹⁶ m³ of graphite —
   about 121 m deep spread evenly. It is a real geological unit, and black. Worth knowing
   before §5 decides what the finished planet looks like.

### Hypersleep, and where it actually belongs

Bulk cargo with a hibernating crew is the intuitive answer to a 185-day crossing, and the
numbers say the opposite for the bulk tier: at 73 % payload fraction the hauler is nearly
all cargo, so crew and consumables are pure overhead on a run that is already
schedule-able and predictable. **Automate the bulk tier and keep crews on the fast tier.**
That inverts the usual science-fiction arrangement, and it falls straight out of the mass
ratios above — hulls that carry people are the ones fast enough to need them.

Where hypersleep does earn its place is the *military* tier and the long-duration station:
a 0.1 g sprint is 18 days, which nobody sleeps through, but a picket stationed at a Jovian
moon for years is a different problem, and a warship that can transit at 0.01 g while its
crew sleeps is a warship that arrives without having aged. That is a capability, and §8 can
price it.
