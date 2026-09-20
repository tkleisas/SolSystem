# SolSystem — Design Document

A 3D space game set in the Solar System, in two modes that share one simulation:
**Strategy** (a command view over a persistent world) and **Action** (a cockpit in
it). Solo project. This document is the reference we build against.

Status markers used throughout:

- **[DECIDED]** — settled, build against it.
- **[OPEN]** — a real fork; do not build until it is closed.
- **[DEFERRED]** — deliberately out of scope until the vertical slice proves out.

---

## 1. The setting

Earth is neutral and dying. Two factions descended from Earth stock — the
**Illuminus** and the **Workers** — are racing to make a second home before Earth
stops being one. Neither project is optional, and neither can be completed alone.

The war is not about ideology, though both sides will tell you it is. It is about
**who gets to breathe**.

### 1.1 The clock

Earth's habitability is in accelerating decline. This is not an asteroid or a
single catastrophe — it is the slow return on a bill nobody alive incurred and
nobody alive can pay off. Earth is neutral because it is too busy dying to fight,
and its orbital infrastructure is the last functioning thing it owns.

**[DECIDED] Earth's decline is the loss condition.** If neither terraforming
project reaches its survival threshold, everyone dies. There is no "you win
anyway." This is the pressure that makes the strategic layer mean something, and
it is identical for both factions and for the player.

**[OPEN]** The exact decline curve, and whether the player can slow it. A player
who can meaningfully help Earth has a third playstyle; a player who cannot
watches a countdown they are powerless against, which is thematically stronger
but mechanically flatter.

### 1.2 Biological divergence

Both factions are human. A few generations down a gravity well rewrites a body,
and the numbers are not close.

| | Gravity | Who can live there unassisted |
|---|---|---|
| Earth | 1.00 g | Workers, baseline humans |
| **Venus** (surface) | **0.90 g** | **Workers** |
| Mars | 0.38 g | Illuminus |

1. **At 0.38 g the Illuminus body cannot take Earth or Venus.** An occupation in
   the flesh is an exoskeleton programme, a drug regime, a short tour of duty and
   a medical discharge.
2. **The Workers can hold both inner planets in the flesh.** They are the only
   faction that can be everywhere that matters.
3. **The Workers pay for it with the Sun.** Operating inward means radiation
   shielding, shielding means mass, mass means delta-v, and delta-v means
   antimatter. The inner system is their home and their tax.
4. **[DECIDED] A captain out of their home gravity is a fish out of water.** An
   Illuminus on Earth in the flesh is mechanically handicapped, not just
   narratively uncomfortable. Characterisation through systems.
5. **[OPEN] Irreversible adaptation.** An Illuminus who spends years at 1 g
   changes and cannot easily go home. Prosthetics, calcification, cardiovascular
   rebuild. This is a story engine — a captain who has worked the inner system too
   long is a captain with nowhere to retire — but it needs a mechanic that is
   poignant rather than punishing.

### 1.3 Shells — the Illuminus answer to gravity

**[DECIDED]** The Illuminus are researching the transfer of a mind into a
mechanical or cybernetic body. A **shell** carries the mind; the mind survives the
shell — unless something kills the mind too.

**[DECIDED] The Illuminus can die, permanently.** Shells do not make them
immortal, and the ways they die are the faction's texture:

| Cause | What it is |
|---|---|
| **Corruption** | Bit rot, radiation, a solar flare through inadequate shielding. The mind degrades and is not there any more. |
| **Physical destruction** | Someone kills the shell faster than the mind can be saved or transmitted. |
| **The cull** | Their own society votes them out (§1.4). Not an accident. A decision. |

A shell is the mind's **only working copy plus its body**. That single sentence is
what keeps the faction mortal and the stakes real.

This does not remove 1.2. It converts a hard wall into a cost wall, and a cost
wall is more interesting to play: "they cannot occupy Venus" is a rule the player
memorises, while "they can, and it costs them a fortune per body, forever" is a
dial.

**The asymmetry survives by moving.** The question was never who can invade. It is
**who can stay.**

| | Workers on Venus | Illuminus on Venus |
|---|---|---|
| Requirement | Pressure suit, water, sealed habitat | Power, spares, maintenance, **replacement shells from Mars** |
| Once established | Self-sustaining | Never |
| It is | A place | **A siege that never ends** |

The Illuminus answer to the inner system is a **beachhead, not a home**. It can be
starved by cutting a shipping lane, which is exactly the pressure the strategic
layer exists to apply.

Consequences:

1. **[DECIDED] Casualties invert.** The Illuminus do not take losses, they take
   *equipment losses*. Attrition works on their **budget**, not their will.
2. **[DECIDED] Fearless is not the same as effective.** A mind that cannot die has
   a distorted relationship with risk and will make tactically insane choices.
   Fearless and reckless are one trait seen from two angles — a natural basis for
   Illuminus AI doctrine.
3. **[DECIDED] Uplinks are a target class.** A mind needs a link home to survive
   its shell. Killing a shell is a hardware loss; **severing the link is a death**.
   This gives raiders a mission with real moral weight.
4. **[DECIDED] Shells are property, and property is inherited.** This is why the
   Illuminus are feudal — not merely because they are greedy, but because **they
   are the people who can afford not to die**. The infantry in the field are copies
   of people who could not.
5. **[DECIDED] Illuminus morale breaks by identity, not fear.** If shells are
   scarce, the same mind ships twice. Then both copies come home and one is
   surplus. A mind that has met itself and disagreed stops following orders.
6. **[DECIDED] The outer system supplies bodies as well as fuel.** Shells need
   rare metals and fabrication capacity, which sharpens the Jovian logic in §4.1
   rather than competing with it.
7. **[DECIDED] The Workers' ideology is tested from the other side.** A captured
   shell is neither a machine nor a prisoner in any sense Workers' law was written
   for. Does the person inside have rights? Labour rights? Can they defect — and
   what does a Worker factory do with a defecting Illuminus mind asking for a body?
8. **[OPEN] How steep is the cost wall?** It must be steep enough that anything
   larger than a raid remains impractical, or §1.2's flavour is lost. This is a
   balance lever that will be turned for months.
9. **[OPEN] Earth becomes a live battlefield.** A shell does not care about a
   failing biosphere, so Earth is cheap for the Illuminus by their standards — and
   catastrophic diplomatically. This is a great scenario and a real complication
   for the neutrality in §1.1. Not yet resolvable.
10. **[OPEN] The Workers will try to steal it.** Their identity is advanced
    production technology; the day they succeed they stop being the Workers.
    Keep this as a late-game fork, loaded from the start, rather than a mechanic.

### 1.4 The cull — scarcity applied to people

**[DECIDED]** Illuminus society periodically holds an **evolutionary vote**. Under
resource pressure it decides, collectively and publicly, which of its own minds to
erase — the less efficient, and the merely badly networked or badly publicised.
This is §4's scarcity model applied to people: when a society cannot afford
immortality for everyone, "everyone" becomes a budget line.

**[DECIDED] A cull is not a data wipe. It is a decision.** A shell is the mind's
only working copy plus its body, so the community **physically destroys one of its
own**, ceremonially, on a vote. Visceral and concrete, never abstract.

**[DECIDED] The vote optimises for social fitness, not evolutionary fitness.**
Networking and PR win it, so it concentrates the same people cycle after cycle and
calls the result merit. They believe their own justification — this is not a
cynical elite lying to a gullible public. It is a society that has built a machine
for deciding who deserves to live and cannot see what the machine is actually
selecting for.

**[DECIDED] The uniformity hazard is a real strategic weakness, not a flavour
note.** Removing outliers and dissenters removes the population's **variance**, and
variance is what survives novel threats. A monoculture is efficient right up until
it meets something it was not selected for.

| | Illuminus | Workers |
|---|---|---|
| Individual | Superior | Adequate |
| Collective | **Brittle** | **Resilient** |
| On surprise | Slow — a society that decides by vote and propaganda cannot act on one person's judgement | Fast — decentralised, absorbs losses, adapts |
| Strength now | Yes | No |
| Strength later | No | Yes |

**[DECIDED]** This is why the Workers' egalitarianism is load-bearing rather than
decorative. It is not only their ethics, it is their **survival strategy**. The
game should argue something real rather than assigning one side horns: hierarchy is
strong now and fragile later; equality is weak now and resilient later.

**[DECIDED] Fearless shells commanded by a committee that cannot decide quickly.**
This pairs with §1.3's recklessness into a coherent doctrine — locally terrifying,
strategically sluggish.

Mechanical hooks:

- **Internal politics as a real system.** The cull is a strategic-layer event with
  a cadence. The player can be threatened by it, lobby against it, or exploit the
  distraction it causes.
- **An Illuminus player can be culled.** Their reputation and patronage, not just
  their combat record, are survival stats. Losing the vote is a death that has
  nothing to do with flying well.
- **Copies vote.** A mind shipped twice can vote on which copy is surplus — or vote
  against its own original. §1.3's identity crisis and §1.4's cull are the same
  machine seen from two ends.
- **Military consequence.** Purges of the officer class cost the Illuminus
  experienced commanders at exactly the moments they can least afford it, which is
  a self-inflicted strategic tempo hit the Workers can learn to time.

**[OPEN]** The cull's cadence and severity, and whether the player can ever be a
voter rather than only a candidate. A player who sits on the panel that decides who
dies has a very different faction fantasy from one who only survives it.

---

## 2. The player

**[DECIDED]** Three ways in, all available:

| Role | Fantasy | Access | Cost |
|---|---|---|---|
| **Worker** | Build the lifeboat | Own industry, cheap hulls, swarm | Tethered to fuel logistics |
| **Illuminus** | Build the ark | Best hulls, best drives, self-sufficient, **shells** | Few ships, each loss hurts; a shell costs a fortune; **the cull can kill you** |
| **Scoundrel** | Play both sides | Both markets, smuggling, brokered peace | No protection, no friends |

The scoundrel is not a mercenary without stakes. They can see the clock and have
concluded the factions are too busy hating each other to save anyone. Playing both
sides is a bet that the fastest path to a living planet runs through people who do
not care who is right.

**[DECIDED]** Strategic and action modes are available from the start for all three
roles. Neither is a mode you unlock; they are two views of one world and the player
chooses how much of each they want.

---

## 3. The energy asymmetry — the core of the game

**[DECIDED] This is the design's centre of gravity.** Everything else is arranged
around it. Antimatter is bottled sunlight; fusion is not.

| | Workers | Illuminus |
|---|---|---|
| Drive | Antimatter | Fusion torch |
| Fuel made | Only near the Sun | Anywhere with ice and a reactor |
| Strategic shape | Fuel **logistics network** | Fuel **self-sufficient**, hardware-starved |
| Failure mode | Cut the lane, the fleet becomes statues | Cut the chain, the fleet is merely smaller |
| Signature | Depends on load | **Radiators glow like a small star** |
| Cost curve | Cheap fuel, expensive hulls | Expensive fuel, expensive hulls, better hulls |

The asymmetry is not a damage bonus. **It is a cost table**, and it determines
where every fight, route and colony ends up.

**[DECIDED] Antimatter is made only near the Sun.** Supply is geographically
fixed. It ships outward in magnetically contained cans that are expensive,
fragile, and worth raiding. The Workers' civilisation is a fuel logistics network
and should feel like one.

**[DECIDED] Fusion fuel is deuterium, not helium-3.** Deuterium is genuinely
abundant in water ice, which makes the outer system a *real* supply rather than a
contrivance. He-3 would have made the fuel chain a plot device.

**[DECIDED] Fusion drives are luminous.** A torch under combat power is visible a
very long way away. Stealth is running cold and letting a body occlude you — not a
cloak.

---

## 4. Scarcity — the actual game

**[DECIDED]** "Limited resources" means every resource has a visible source and a
visible sink. A resource the player cannot trace is noise, not scarcity.

| Resource | Source | Sink | Notes |
|---|---|---|---|
| **Delta-v** | — | everything | The true currency. Everything else is priced in it. |
| **Antimatter** | Solar collectors, inner system only | Propulsion, reactors | Slow to make, dangerous to store, catastrophic to lose |
| **Deuterium / water ice** | Outer system, Jovian moons | Fusion drives, terraforming | The flashpoint |
| **Fissiles, rare metals** | Specific deposits | Reactors, industry | No substitute exists; deposits are permanent flashpoints |
| **Construction capacity** | Built, slowly | Factories, refineries, shade swarms | Impossible to hide, tempting to raid |

**[DECIDED] Nothing regenerates.** A depleted deposit stays depleted. This is what
makes terraforming a *drain the whole war must be fed by*, and what makes raiding
a convoy strategically meaningful rather than merely good sport.

### 4.1 Why the Jovian moons are the war

The terraforming programmes make Jupiter the centre of the conflict rather than a
side theatre:

- **Workers → Venus** need hydrogen. Venus has almost none, and its CO₂ is useless
  without it. Hydrogen means water ice means the outer system.
- **Illuminus → Mars** need volatiles by the megatonne, plus deuterium for their
  drives. The same ice, the same moons.

Whoever holds the Jovian system rations the other faction's terraforming programme
*and* its fuel. That is a war with a reason to be fought that is not ideology.

**[DECIDED]** The Illuminus deuterium-cracking plants are on **Mars**. It gives
Mars a reason to exist beyond being the colony they picked, and it means a strike
on Mars is a strategic strike.

---

## 5. Terraforming

**[DECIDED] Terraforming is a slow, visible, economic drain** — a staged progress
system fed by the resource ledger, not a cutscene and not a win button.

**[DECIDED]** Both projects are visible on the strategic map as they advance. Venus
goes from featureless yellow to mottled to a visible atmosphere front. Mars goes
from rust to a thickening sky.

**[DECIDED]** Realistic timescales are centuries, which is unplayable, so time
compression is the honest answer: the simulation runs fast, the *stages* are the
content. Thematically consistent too — an expanding industrial base accelerates
its own terraforming.

**[OPEN]** The stage list per body, and the survival threshold that counts as
"won." Candidate for Venus: shade swarm → atmosphere processing → surface cooling
→ liquid water. Candidate for Mars: volatile import → atmospheric thickening →
magnetic shield at L1 → liquid water. Neither list is fixed.

**[DEFERRED]** Any modelling of the terraforming physics itself. It is a state
machine with inputs and a progress bar until the slice proves the loop is fun.

---

## 6. Architecture

**[DECIDED] One simulation, two views.** No loading screen between modes, no
second world. The strategic layer is a command view over the same simulation the
cockpit sits inside.

### 6.1 The rule that carries over from MiVic

MiVic's architecture already is this split, arrived at from the other direction:

| MiVic | SolSystem |
|---|---|
| `MiVic.Core` — no graphics, no floating point | `SolSystem.Sim` — the world, headless |
| `MiVic.Game` — client owns no state | `SolSystem.Client` — all three views |
| `MiVic.Map` — headless renderer, no GPU | `SolSystem.Map` — strategic map + SVG/PNG reports |
| missions/maps/cutscenes as validated files | hulls/drives/sites/terraforming as validated files |

**The client owns no game state. Rendering can never change the outcome of a
tick.** Keep that rule exactly as it is. It is what makes a two-mode game possible
at all, and it is what makes the whole thing testable by one person.

### 6.2 Numerics — [DECIDED]

> **Decided: Option A. Closed before the first line of flight code was written.**

**Option A — fixed point, in two widths and two frames.**
**[DECIDED — verified; results in `docs/SPIKE-NUMERICS.md`, §6.2.1 below]**

| Frame | Type | Unit | Reach | Grid | For |
|---|---|---|---|---|---|
| **Solar** | `Fix128`, Q64.64 | kilometre | ±9.2 × 10¹⁸ km ≈ 6 × 10¹⁰ AU | 5.4 × 10⁻²⁰ km | Bodies, transfers, the strategic map |
| **Local** | `Fix64`, Q32.32 | megametre | ±2.1 × 10⁹ Mm ≈ 14.4 AU | 233 nm | Ships, stations, docking, combat |

**Two widths, because a Q32.32 `long` cannot propagate a planetary orbit at all.**
Gravity needs `r²`. At 1 AU that is 2.24 × 10¹⁶ km², and a Q32.32 value tops out at
2.1 × 10⁹ — it overflows by a factor of **10⁷**. The largest `r` a Q32.32 can square is
about 46 000 km, one seventh of an Earth radius. The spike measured this; it is not a
derivation.

**And no unit rescues it.** The solar frame needs a heliocentric coordinate out to
billions of kilometres (31 bits of integer) *and* a thrust acceleration down to
nanometres per second squared (~60 bits of fraction) at the same time. That is more than
64 bits of dynamic range by construction, so the type has to be *wider* rather than
differently scaled. Earlier drafts of this section tried three different unit-based
rescues and were wrong each time.

**The local frame stays narrow on purpose.** `Fix64` is roughly a hundred times cheaper
per operation, and the action layer ticks hundreds of entities at 120 Hz. Its 233 nm grid
is far finer than a ship needs; the solar frame's 5.4 × 10⁻²⁰ km grid is what an orbit
needs. Two frames, two widths, each matched to its job.

**Crossing frames is exact.** A body sits at a position in the solar frame; a ship sits at
a local offset from it. Converting the offset to kilometres scales by 10³ and lands on the
solar frame's grid, which is finer than the local one — so the crossing loses nothing.
A local offset cannot exceed 2.1 × 10⁹ Mm, well inside the solar frame's range.

**Rendering is camera-relative, and that is not optional.** The GPU transform is `float32`
with a 24-bit mantissa, so at 1 AU its quantum is ±16 m whatever the simulation uses. The
client subtracts the camera position in fixed point and converts the small result to
float. This is required under *every* numeric option and is not an argument for or against
fixed point — an earlier draft presented it as one.

**Option B — rejected: fixed point for the strategic layer, `double` with a floating
origin in action.**

- *For:* easier physics, GPU and `System.Math` directly, no 128-bit arithmetic.
- *Against:* two numeric disciplines; the crossover must be defined and tested; and no
  byte-exact replay of a dogfight, which is the capability the whole workflow rests on.
  A fight would resolve to a logged result and could never be replayed.
- *Why it lost:* the deciding argument is not precision, which both options have in
  abundance. It is that determinism in the cockpit lets the probe harness reproduce a
  docking approach exactly — twice, on any machine — which turns flight tuning from
  judgement into measurement.

**Four unit errors were made and corrected while deciding this.** They are recorded rather
than quietly fixed, because they share one failure mode — a factor of a thousand that stays
invisible until something tries to reach Saturn — and it is the most likely thing to recur
in this codebase:

1. The reach was quoted as "±14 300 AU" against a unit that was kilometres in one sentence
   and megametres in another. Both readings are real; they differ by 10³.
2. The Q32.32 grid was quoted as 233 nanometres. It is **233 micrometres** in kilometres
   and 0.233 mm in megametres — from silently comparing the nanometre count of the
   *fractional* unit against metres.
3. The first correction of (1) concluded the reach was 14.355 AU with Uranus out of range,
   which holds only in the megametre reading.
4. The spike then found that none of it mattered, because `r²` overflows Q32.32 at 1 AU.

#### 6.2.1 What the spike measured

`src/SolSystem.Spike` integrates one Earth orbit for a year at a one-hour step, twice —
once in Q64.64, once in `double` — with the same algorithm, step and initial conditions.
The full report is **`docs/SPIKE-NUMERICS.md`**; the transcript is
`artifacts/spike-numerics.txt`.

| Measurement | Result |
|---|---|
| Relative position error after a year | **1.4 × 10⁻⁶** |
| Energy drift, Q64.64 | **1.5 × 10⁻¹²** |
| Final radius, Q64.64 | 149 597 870.170 km — 170 m from 1 AU |
| Cost, Q64.64 | ~16 µs/step, irrelevant for a few hundred bodies |
| Cost, `Fix64` | ~100× cheaper, which is why the action layer keeps it |

The fixed/double difference grows **linearly**, not as a random walk: the fixed-point Sun
is marginally weaker because GM is not exactly representable, and the orbit answers with a
constant bias of ~10⁻¹⁶ in acceleration. Over a year that is 1.3 × 10⁶ m — six orders of
magnitude below anything the simulation can act on. Energy is conserved to one part in
10¹², which is the property that matters: it says the integrator neither leaks nor gains
orbital energy over long runs.

**The remaining risk in Option A is not precision. It is implementation correctness**, and
that risk is real. The types accumulated **six bugs** during construction, every one of
which returned plausible numbers rather than raising: a multiply whose halves were
assembled wrongly, a divide whose shift truncated silently, a square root that converged to
a wrong fixed point (`sqrt(100)` → 8), a start bit one place too high, and two gravity
formulas that were wrong by a factor of `r` and by underflow respectively. **Two of the six
were in the `double` reference rather than the fixed-point code** — the path that was
supposed to be the trustworthy one.

`tests/SolSystem.Core.Tests/Fix128Tests.cs` therefore checks the type against an
independent `BigInteger` reference rather than against hand-computed expectations, and the
same discipline should apply to every numeric addition from here.

### 6.3 The crossover

**[DECIDED]** Regardless of 6.2, the cross-domain surface is deliberately tiny:

- **Position and velocity** of the player's ship, converted at the boundary.
- **Resource state** — fuel, cargo, damage — carried as the same numbers the
  strategic ledger uses.
- **Battle result** — the outcome crosses to the strategic layer as an event with
  a timestamp, not a tick-by-tick transcript.

This is MiVic's command-log pattern pointed at combat. The strategic replay logs
the result; nobody has to reproduce a 90-second furball.

### 6.4 Project layout

```
src/SolSystem.Core      Fix64 + Fix128, integer trig, orbits — no graphics deps
src/SolSystem.Sim       the world: sites, fleets, economy, terraforming, replay
src/SolSystem.Client    MonoGame client: cockpit, tactical, strategic views
src/SolSystem.Map       headless strategic-map renderer (SVG/PNG), no GPU
tests/SolSystem.Core.Tests
tests/SolSystem.Sim.Tests
tests/SolSystem.Map.Tests
tools/probe/            probe scripts
tools/blender/          procedural hull generation
```

---

## 7. Time

**[DECIDED] One clock, one tick rate, variable compression.** The simulation ticks
at a fixed rate; compression changes how many ticks advance per real second. There
is no separate "strategy time" and "action time."

**[DECIDED] Entering the cockpit drops compression to 1:1 and the world keeps
running in lockstep.** Not paused. A five-minute fight costs the world five
minutes of terraforming progress.

This keeps the strategic replay fully deterministic — it is still just ticks — and
it produces the thing that makes dual-mode games sing: *you were in the wrong
place*. Your carrier group is engaging at Europa while the convoy you were
escorting gets jumped at L4. No mission generator fakes that.

**[OPEN]** Whether a player may *choose* to pause during an action engagement
(difficulty/accessibility) without breaking the lockstep guarantee. Pausing is a
client-side halt of tick advancement, so determinism survives; the question is
whether it costs the design more than it buys.

---

## 8. Combat

**[DECIDED]** Newtonian, no artificial drag, no speed cap. **Delta-v is the
currency.** Armour, heat and radiators are the wounds.

**[DECIDED]** A fusion torch under combat power is visible at enormous range.
Stealth is running cold and using a body as occlusion.

**[DECIDED]** An Illuminus engagement is not attrition of people but of hardware
(§1.3) — with the standing exception that the mind can still die. Their morale
breaks by identity, not fear, and their logistics include replacement bodies. A
fight that costs the Workers lives costs the Illuminus a line item, and neither
side should find that symmetrical.

**[DECIDED]** Illuminus command is locally aggressive and strategically slow,
because a society that decides by vote and propaganda cannot act on one person's
judgement (§1.4). Their doctrine should feel like a machine that commits hard and
then takes a long time to change its mind.

**[DECIDED] An antimatter ship's weakness is range, not combat performance.** It
has better thrust-to-mass and worse endurance, and it is tethered to depots.
Raiders should therefore attack the **depot**, not the ship.

**[DECIDED]** From Privateer: you are one hull against a solar system, patrons give
missions, upgrades are grubby and incremental. From Elite: the feel of flying and
the sense that the map has texture.

**[DECIDED]** No friction-in-space flight model. It is lovely and it does not match
a world where the drive is a fusion torch.

**[DEFERRED]** Weapons, damage model, boarding, fleet combat resolution. The slice
needs one gun and one target.

---

## 9. Content as data

**[DECIDED]** Follow MiVic's pattern exactly: versioned files, validated on load,
with a loader that **refuses** invalid content and says why.

| File | Governs |
|---|---|
| `factions/*.json` | Doctrine, modifiers, tech ceiling, gravity tolerance |
| `hulls/*.json` | Mass, armour, radiators, hardpoints, crew, gravity rating |
| `drives/*.json` | Thrust, exhaust velocity, fuel type, signature |
| `sites/*.json` | Position, owner, deposits, industry, docking |
| `bodies/*.json` | Orbit, mass, radius, terraforming stage list |
| `contracts/*.json` | Patron, objective, deadline, payment, consequence |

**[DECIDED]** Content authored as files means content is authored, not programmed.
For one person this is the difference between a game with ten sites and a game
with three.

---

## 10. Roadmap

Four weeks, to be judged honestly at the end. **[DECIDED]** Do not commit to the
year until week four is in.

### Phase 0 — the walking skeleton (weeks 1–4)

**Gate: if flying this ship is not fun with nothing else attached, nothing else
rescues it.**

- [x] Numeric decision (6.2) made and verified by spike
- [x] Fixed-point cores: `Fix64` Q32.32 and `Fix128` Q64.64, with tests
- [x] Integer trig — `sin`, `cos`, `atan2` on turn-based angles
- [ ] One body, two stations, real Keplerian orbits
- [ ] One flyable ship, fixed 120 Hz tick, Newtonian thrust
- [ ] Fuel as delta-v; a burn you can afford and a burn you cannot
- [ ] Docking that is a skill and not a button
- [ ] Probe harness + screenshot pipeline ported from MiVic
- [ ] A probe that reproduces a docking approach byte-exactly, twice

### Phase 1 — the economy behind it (weeks 5–10)

- [ ] Resource ledger with visible sources and sinks
- [ ] Buy, sell, refuel; a market that moves
- [ ] Strategic view with time compression and the same world
- [ ] Second site; a contract with a deadline
- [ ] The two drives, and the cost table that makes them different

### Phase 2 — one moon system (weeks 11–20)

- [ ] The Jovian theatre: three or four locations
- [ ] Convoys, raiding, and a depot worth attacking
- [ ] Faction reputation; the scoundrel's both-markets access
- [ ] One gun, one fight, resolved in action and returned as an event

### Phase 3 — the divergence (weeks 21–34)

- [ ] Both factions, tech trees, doctrine
- [ ] Gravity rating and its consequences for crew and hulls
- [ ] Terraforming as a staged drain, visible on the map
- [ ] Earth's clock, and the loss condition

### Phase 4 — campaign (weeks 35+)

- [ ] Full-system map and interplanetary transit
- [ ] Strategic AI
- [ ] Campaign, endings, the scoundrel's brokered path

### Explicitly not in year one

Full nine-body solar system at launch. Civilian traffic simulation. Diplomacy
beyond reputation. Multiplayer. Voice. Any terraforming model more detailed than a
staged state machine.

---

## 11. Open questions

| # | Question | Gates |
|---|---|---|
| 1 | ~~Numerics: all-int64, or split with doubles in action?~~ (§6.2) | **Closed: all int64 Q32.32, two scales** |
| 2 | Can the player slow Earth's decline? (§1.1) | Whether a third playstyle exists |
| 3 | Is adaptation irreversible, and how is that felt rather than punished? (§1.2) | Character systems |
| 4 | Terraforming stage lists and survival threshold (§5) | Win condition |
| 5 | May the player pause during an engagement? (§7) | Determinism guarantees |
| 6 | What does the scoundrel's endgame look like if both factions survive? | Campaign design |
| 7 | How steep is the shells cost wall? (§1.3) | Whether §1.2's blockade flavour survives |
| 8 | Does shell transfer exist for the player, or only for the NPC elite? (§1.3) | Whether an Illuminus player can die — **closed: they can** |
| 9 | Does Earth's neutrality survive a cheap Illuminus raid? (§1.3) | Diplomatic layer |
| 10 | The cull's cadence and severity; can the player vote, or only be a candidate? (§1.4) | Illuminus internal politics |
| 11 | How does a cull read in the cockpit — witnessed, broadcast, or discovered after? (§1.4) | Tone |

---

## Appendix A — settled decisions, at a glance

1. Earth is neutral because it is dying; its decline is the shared loss condition.
2. Workers are 0.90 g Venus-adapted; Illuminus are 0.38 g Mars-adapted. The
   Illuminus body cannot take Earth or Venus, so they invade in **shells** — mind
   transfer into mechanical bodies that cost a fortune each. The wall is a cost
   wall, not a hard one: they can raid anywhere and hold nothing, because only the
   Workers can *stay*.
3. Workers: antimatter, made near the Sun, logistics-tethered, cheap hulls.
   Illuminus: fusion, deuterium, self-sufficient, few excellent hulls.
4. Deuterium is the fusion fuel; the Jovian moons are therefore the war.
5. Antimatter ships are beaten by taking their depots, not by fighting them.
6. Terraforming is a staged economic drain, visible on the map, and it is the
   thing the war is fought over.
7. One simulation, two views; the client owns no state; content is validated data.
8. Entering the cockpit sets compression to 1:1 and the world keeps running.
9. MonoGame, in the MiVic project shape, with `SolSystem.Core` graphics-free.
10. Four weeks to a flyable ship before any commitment to a year.
11. Illuminus minds live in **shells** and can die — by corruption, by violence, or
    by the community's own **cull**. A shell is the mind's only working copy plus
    its body.
12. The cull selects for social fitness rather than evolutionary fitness, so the
    Illuminus are individually superior and collectively brittle. The Workers'
    egalitarianism is their survival strategy, not just their ethics.
