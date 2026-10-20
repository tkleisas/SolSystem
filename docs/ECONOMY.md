# The economy: resources, sites, prices, and the standard

The model behind the market, the contract board and the station's food and waste loops.
Every number here is derived from the setting's physics and physiology, with the arithmetic
shown so it can be argued with rather than trusted. **Accuracy: order of magnitude.** The
site data is not fixed yet; the point of this page is to make the disagreements visible.

Status markers as in `DESIGN.md`: **[DECIDED]**, **[OPEN]**, **[DEFERRED]**.

---

## 0. The premise this document tests

The intuition that started this page was:

> Food is bulky and wet; lifting it is expensive; therefore stations should grow it in situ,
> and close the waste loop.

The first half is true for chemical rockets and false for this setting's fusion drive. At
1 200 km/s of exhaust velocity, lifting a tonne to low Earth orbit costs about 0.8 % of its
mass in propellant. Food is expensive to **grow**, not to **lift**. The station's farm is a
power plant with a crop attached, and at the energy prices this economy implies the LED farm
is roughly two orders of magnitude more expensive per tonne than imported food. That does
not make the farm pointless — resilience, freshness, morale and a closed loop are real
goods — but it means the **in-situ food story is a strategic and biological one, not a
transport-cost one**. The waste loop is the part that pays back in mass and water.

The document is written so that this conclusion can be checked line by line.

---

## 1. The monetary standard

**[DECIDED] Two tiers, one unit of account.** A resource-constrained economy does not need
fiat money, but it does need a fungible unit. The design uses a **commodity-backed credit**
with two anchors:

| Tier | Anchor | Used for |
|---|---|---|
| **Reserve** | 1 credit = **1 kWh at a named station's bus** | Food, water, recycling, parts, labour, the cost of living |
| **Transport** | 1 credit = **1 m/s of delta-v for a reference 100 t ship** | The contract's delivery cost, propellant, the two drives |
| **Basket** | A station's annual food/water/O₂ consumption | The station's consumer prices and the cost of living index |

A credit is a **claim**, not a token: it names the station whose bus backs it and it is
redeemable in energy. Because energy cannot move between sites for free, a Meridian credit
and a Ceres credit are different claims, and the exchange rate between them is the transport
cost. That is the game.

**[DECIDED] The delta-v is the transport price, not the reserve.** `DESIGN.md` §6 says
delta-v is the true currency; the honest reading is that delta-v is what **moving mass**
costs, while energy is what **making things** costs. The two are related through the drive's
exhaust velocity: a tonne of propellant carries `½ vₑ²` of jet energy. The client's two
drives make the exchange rate visible — the Workers drive buys twice the delta-v per tonne,
the Illuminus drive buys 1.6× the acceleration — and `--drive` is the choice between them.

**[OPEN] The faction claims.** The natural political layer is three claims with three
backings:

| Issuer | Claim | Backing |
|---|---|---|
| **Illuminus** | Energy credit | The ark's power; the strongest claim, the hardest to redeem outside Illuminus stations |
| **Workers** | Water/deuterium credit | The volatiles the fuel network actually controls |
| **Earth** | Old fiat | Habit and the remains of the pre-Exchange financial system; losing its anchor as the biosphere fails |

The Exchange of 2090 is the arbitrage between them, and the player is a courier who profits
from the spread. The faction currencies are a later layer; the first slice uses a single
station-credit with the two anchors above.

**[DECIDED] Every credit is a claim on a measured stock.** The station cannot print claims
against a farm it has not built. The model's stocks are the collateral; the tests check the
balances, not the prices.

---

## 2. The resource model

Six material stocks — energy, water, carbon, nutrients, food and waste — plus labour and
parts. Everything is conserved except the losses named explicitly.

| Stock | Unit | Notes |
|---|---|---|
| **Energy** | kWh (3.6 MJ) | The reserve asset; the station bus; the farm's LED; the recycler; the radiator's heat is a constraint, not a stock |
| **Water** | t | The station's largest mass flow; recycled, with a loss |
| **Carbon** | t | CO₂, biomass, food; the crop's carbon comes from the air |
| **Nutrients** | t (N, P, K) | The limiting nutrients; the waste loop's real prize |
| **Food** | t wet, kcal, protein | The calorie and protein targets are separate constraints |
| **Labour** | person-hours | The crew; the 40 000; the wages |
| **Parts / volatiles** | t | The imports; the ice; the ore; the equipment |
| **Waste** | t | Feces, urine, biomass; the same atoms, dirty |

The **loss** terms are the only things that leave the loop: the water lost to the airlock and
the recycler's brine, the nitrogen lost as N₂, the phosphorus lost in the sludge, the carbon
lost as CO₂ vented. The **make-up** is the imported mass that closes the loss.

**[DECIDED] Prices are marginal costs, not tables.** For a site with recipes `a` and shadow
prices `λ`, the price of good `i` is:

```
p_i = Σ_k λ_k · a_ki  +  transport_i  +  capital_i
```

where `λ_k` is the shadow price of the binding constraint `k` — power, water, area, labour,
heat rejection — and `a_ki` is the amount of `k` the recipe uses per unit of `i`. The market's
spread, drift and trade pressure sit on top as the market layer; they do not create the
price.

---

## 3. The recipes

Five recipes carry the first slice. Coefficients are per unit of output; the numbers are the
worked example in §5.

### 3.1 Grow

```
water + nutrients + CO₂ + light/energy + area + labour
    → food + O₂ + biomass waste
```

The **crop mix** is a parameter, not a constant. Wheat and soy give calories and protein;
potatoes and leafy greens give calories and morale; the mix is chosen to hit the calorie and
protein targets. The **area** and the **light** are the two binding constraints; the CO₂
comes from the crew and the recycler.

### 3.2 Recycle

```
feces + urine + biomass + energy
    → water + nutrients + CO₂ + loss
```

The recycler is the station's most important machine. It returns the water and the nutrients;
the loss is the make-up mass the station must import. The **closure fraction** is the figure
of merit: the fraction of each element that comes back. The first slice models water,
nitrogen and phosphorus separately.

### 3.3 Life support

```
food + water + O₂ → labour + waste + CO₂
```

The crew is both the consumer and the labour pool. The food and water targets are per person
per day; the waste is the recycler's input.

### 3.4 Fuel

```
ice/volatiles + energy → propellant
```

The propellant's cost is the energy to refine it plus the volatile's own cost. The two drives
set the exchange rate between propellant and delta-v.

### 3.5 Fabricate

```
ore + energy → parts
```

Parts are the station's capital goods and the player's cargo. The ore's cost is the mining
and the transport; the energy is the smelting and the machining.

---

## 4. The sites

The first slice has three sites, chosen because they bracket the model: a large closed-loop
station, a small station, and the biosphere.

| Site | Population | Power | Water | Food | Notes |
|---|---|---|---|---|---|
| **Meridian** | 40 000 | Solar at 1 AU, 1.36 kW/m² | Recycled, 98 % | Import + farm | The 2 km wheel; 6 778 km orbit; the closed loop |
| **Waystation** | Small crew | Solar/fuel cell | Imported/recycled | Imported | The 91 m Workers spine; the contract's destination |
| **Earth** | The biosphere | The Sun | The biosphere | The biosphere | The food exporter; the fiat issuer; the failing world |

**[OPEN] The later sites.** Mercury (power-rich, volatile-poor), Venus (aerostat, CO₂-rich,
solar-rich), Mars (CO₂ and water, low solar), Ceres (water-rich, power-poor), the Jovian
stations (power-rich, radiation-poor). They are the Phase 2 theatre, not the first slice.

**[DECIDED] The site data is authored, the prices are derived.** The site table gives the
capacities and the recipes; the model computes the shadow prices and the market prices. A
site whose farm is not built has a shadow price for food equal to the import price, not the
farm's cost.

---

## 5. The worked example: Meridian

The station's own numbers are the argument. The population and the wheel are from the
setting and the narration: 40 000 souls, a 2 km wheel, 230 000 berths.

### 5.1 The crew's flows

| Flow | Per person per day | 40 000 people |
|---|---|---|
| Food, wet | 1.5 kg | **60 t/day** |
| Food, dry | ~0.375 kg | **15 t/day** |
| Calories | 2 500 kcal | **100 Gcal/day** (4.84 MW) |
| Water | 100 L | **4 000 t/day** |
| Urine | 1.5 L | **60 t/day** |
| Feces | 0.15 kg | **6 t/day** |
| Oxygen | 0.84 kg | **34 t/day** |

The **food energy** is 4.84 MW; the **waste water** is 61.5 t/day; the **dry waste** is
4.5 t/day. The recycler's job is to return the water and the nutrients and lose as little as
possible.

### 5.2 The farm

At a continuous mixed-crop yield of **15 g dry/m²/day**, 15 t dry/day needs **1 km²** of
growing area. That is a large new deck on a 2 km wheel, but it is not impossible: the ring's
usable area is a few square kilometres.

The LED load is the problem. At **800 µmol/m²/s** of photosynthetically active radiation,
the PAR flux is about **0.17 kW/m²**. At 40 % LED efficiency that is **0.43 kW/m²** of
electricity, so 1 km² needs **430 MW**. The crop's photosynthesis captures only a few percent
of that as biomass; the rest is heat that the radiator must reject.

| Farm item | Value |
|---|---|
| Area | 1 km² |
| LED electric power | ~430 MW |
| Heat rejected | ~260 MW |
| Daily energy | ~10 300 MWh/day |
| Energy per tonne of food | ~170 kWh/kg |

At **1 credit = 1 kWh**, the farm's energy alone is **~10.3 million credits/day** for
60 t/day of food, or **~170 000 credits/t**. That is the in-situ food price before labour,
capital or nutrients.

### 5.3 The import alternative

Take the Earth food price at the current market's scale — **2 000 credits/t**, the same order
as the client's machine parts; the exact food price is an [OPEN] content number. The station
needs 60 t/day, so **120 000 credits/day**. The transport is the delta-v: 9.4 km/s from Earth's surface to
low orbit. With the **Workers drive** (1 200 km/s exhaust) the mass ratio is
`exp(9.4/1200) = 1.0079`; a 60 t cargo needs about **0.5 t of propellant**, or
**125 credits/day** at 250 credits/t. With the **Illuminus drive** (600 km/s) it is about
**1.0 t and 250 credits/day**.

| Option | Daily cost | Per tonne | Binding constraint |
|---|---|---|---|
| **Import** | ~120 000 credits | ~2 000 credits/t | Earth's food supply; the lift |
| **Farm** | ~10 300 000 credits | ~170 000 credits/t | Power, area, radiator |

The farm is roughly **85×** the import cost at these assumptions. The symmetry that the
intuition expected does not exist in this setting, because the fusion drive makes the lift
cheap and the LED makes the growing expensive.

### 5.4 What the farm is actually for

The farm is not a food-cost optimisation. It is:

- **Resilience.** A station that can feed itself for a season is a station that cannot be
  starved by a blocked supply line — the strategic pressure the design's factions want.
- **Freshness and morale.** Fresh produce is a different good from shipped rations; the
  station's 40 000 people are a market for it.
- **A closed loop.** The farm consumes the recycler's CO₂ and nutrients and returns O₂ and
  biomass; it is part of the life-support system, not a separate factory.
- **A smaller make-up mass.** The recycler plus the farm reduces the water and nutrient
  imports, which is the mass that actually costs delta-v.

**[DECIDED] The first slice models the farm as a resilience and loop asset, not as the cheap
food source.** The contract board can offer both: deliver food (the cheap option) or deliver
the farm equipment (the strategic option). The player's choice is which risk to buy down.

### 5.5 The waste loop

The recycler is where the economics actually close. At 98 % water closure the station loses
**80 t/day** of water; at 99.5 % it loses **20 t/day**. The make-up is lifted from Earth or
mined from ice. The nutrients are the harder loss: nitrogen and phosphorus are a few tonnes
per day at most, but they are the difference between a farm that works and one that does not.

| Closure | Water loss | Make-up cost (import) | Energy cost (recycler) |
|---|---|---|---|
| 95 % | 200 t/day | ~400 000 credits/day | ~2 000 kWh/day |
| 98 % | 80 t/day | ~160 000 credits/day | ~4 000 kWh/day |
| 99.5 % | 20 t/day | ~40 000 credits/day | ~10 000 kWh/day |

The recycler's energy is cheap relative to the make-up mass it saves; the closure is worth
paying for. That is the first real in-situ investment the player should be able to make.

---

## 6. The transport cost

The transport price is the delta-v, and the two drives set the exchange rate. The rocket
equation gives the propellant mass for a cargo:

```
m_prop = m_cargo · (exp(Δv / vₑ) − 1)
```

| Route | Δv | Workers (1 200 km/s) | Illuminus (600 km/s) |
|---|---|---|---|
| Earth surface → LEO | 9.4 km/s | 0.79 % of cargo | 1.58 % |
| LEO → GEO | 3.9 km/s | 0.33 % | 0.65 % |
| LEO → lunar transfer | 3.2 km/s | 0.27 % | 0.53 % |
| Earth → Mars | 7.3 km/s | 0.61 % | 1.22 % |

The transport is **cheap in propellant** and **expensive in time**: at 4 milligee a 9.4 km/s
burn takes about 2.8 days, and the Illuminus drive brings that to about 1.8 days while
roughly doubling the propellant. The contract's deadline is the real transport cost, not the
fuel.

**[DECIDED] The transport cost is charged in delta-v, not in tonnes of propellant.** The
contract's price includes the delta-v the delivery consumes at the reference ship's mass; the
player's actual propellant bill depends on the drive and the cargo mass. That keeps the
contract comparable across drives while the two drives stay a real choice.

---

## 7. The prices

The market layer is already in the client: a base mid, a spread, a three-day drift, a trade
pressure that decays over 0.75 day, and a moving fuel price. The model replaces the base mid
with the marginal cost above; the rest of the market layer stays.

**[DECIDED] The market is per site, not global.** Food is cheap where it grows and expensive
where it does not; the price difference is the transport cost plus the local scarcity. The
player's arbitrage is the point.

**[DECIDED] The price is a function of the session clock and the trades.** No wall clock, no
random walk. Two runs that make the same decisions see the same prices. The tests assert the
determinism, the spread, the pressure and the conservation.

**[OPEN] The consumer price basket.** The station's cost of living — food, water, O₂, the
habitat's share of the power — is the natural index for the credit's purchasing power. The
first slice uses a fixed basket; the moving basket is a later layer.

---

## 8. The game loop

The player is a courier with a 100 t hull, 40 t of propellant and 20 t of hold. The loop:

1. **The contract board** offers deliveries between the sites: food to Meridian, parts to the
   waystation, equipment to a farm, waste-processing equipment to a recycler.
2. **The market** prices the cargo and the fuel; the player can buy low and sell high, or
   carry the contract's cargo for a fixed fee.
3. **The in-situ decision**: deliver food (cheap, recurring) or deliver the farm/recycler
   equipment (expensive, strategic). The station's closure and its resilience are the return.
4. **The waste loop**: the station's recycler produces water and nutrients; the player can
   carry the make-up mass, the equipment, or the waste products.
5. **The two drives**: the Workers drive is the efficient hauler; the Illuminus drive is the
   deadline-maker. The contract's delta-v price is the same; the player's cost is not.
6. **The clock**: the strategic rates rotate the stations exactly; the contract's deadline is
   the session clock; the market drifts while the clock runs.

The first slice is deliberately small: **Meridian, the waystation and Earth**, with **food,
water, waste, energy and parts**. The player can make the food run, the parts run, or the
equipment run; the station's farm and recycler are the strategic investments; the waste loop
is the mass that closes.

---

## 9. The first implementation slice

**[DECIDED] The model lives behind the existing `Market` interface.** The client already has
`Market`, `Ledger`, `CargoHold`, `Contract` and the market screen. The first slice adds a
`SiteEconomy` that computes the base mid from the site's recipes and shadow prices; the
`Market` keeps the spread, drift and pressure; the `Contract` keeps the deadline and the
payout. No second market system.

The slice:

| Piece | What it does |
|---|---|
| **Site data** | Meridian, Waystation, Earth: power, water, population, recipes, capacities |
| **Stocks** | Energy, water, nutrients, food, waste, parts, labour |
| **Recipes** | Grow, recycle, life support, fuel, fabricate |
| **Marginal cost** | `p_i = Σ λ_k a_ki + transport + capital`; the shadow prices from the binding constraints |
| **Market** | The existing spread/drift/pressure; the base mid from the model |
| **Contract** | The delta-v price; the payout; the in-situ alternative |
| **UI** | The market screen's cost breakdown; the contract board; the closure and the food |
| **Tests** | Mass balance, energy balance, closure, marginal cost, transport, determinism |

**[OPEN] The data format.** The site and recipe tables should be authored data, not
hard-coded constants; the design's `sites/*.json` and `recipes/*.json` are the intended
shape. The first slice can start as a C# table with the same fields, moved to data when the
loader exists.

---

## 10. The open questions

1. **The food farm's real purpose.** Is the farm a resilience asset (this document's
   reading) or should the design change the energy price so it is also the cheap food? The
   first is more honest to the physics; the second is more satisfying to the intuition.
2. **The closure target.** 98 % or 99.5 %? The answer sets the make-up mass and the recycler's
   energy, and therefore the station's import bill.
3. **The crop mix.** Calories and protein are separate constraints; the mix is a content
   decision.
4. **The credit's convertibility.** Is a Meridian credit redeemable at the waystation at the
   transport cost, or only at Meridian? The first is a currency; the second is a voucher.
5. **The faction currencies.** Do the Illuminus and the Workers issue competing claims, and
   does the player have to choose which to be paid in?
6. **The labour market.** The 40 000 are a labour pool and a consumer market; the wages and
   the cost of living need a first number.
7. **The waste's value.** Is the recycler's output a market good, or only an internal flow?
   The first makes the loop tradeable; the second keeps it a station decision.
8. **The transport's time cost.** The 4 milligee burn times are weeks; the contract deadline
   is the real cost. How much of the contract price should be time?

---

## Appendix A — the numbers and their sources

| Quantity | Value | Source / note |
|---|---|---|
| Station population | 40 000 | The setting and the narration (230 000 berths) |
| Station orbit | 6 778.1 km | `Station.Meridian`; 400 km altitude |
| Station artificial gravity | ~1 g at the 1 km rim | The 2 km wheel at 0.95 rpm; the centripetal acceleration |
| Drive acceleration at the station | 4 milligee, 0.45 % of local gravity | `DESIGN.md`; 0.0392 m/s² against 8.68 m/s² |
| Food per person | 1.5 kg wet/day | A generous ISS/FAO figure; the dry mass ~25 % |
| Calories | 2 500 kcal/day | Adult mean; the crop mix must hit it |
| Water per person | 100 L/day | A station figure with recycling; the ISS uses less, a habitat with laundry and hygiene uses more |
| Urine | 1.5 L/day | The human mean |
| Feces | 0.15 kg/day | The human mean |
| Oxygen | 0.84 kg/day | The human mean |
| Crop yield | 15 g dry/m²/day | A continuous wheat/soy mix; the range is 10–20 |
| PAR | 800 µmol/m²/s | A high-yield LED crop |
| LED efficiency | 40 % | Current high-power LEDs |
| Solar constant | 1.36 kW/m² | At 1 AU |
| Earth → LEO | 9.4 km/s | The design's delta-v budget; the lift is the dominant term |
| Workers drive | 1 200 km/s, 5.5 kN | `Drives`; the current `Hulls.Courier` reference |
| Illuminus drive | 600 km/s, 9.0 kN | `Drives`; the client's default |
| Credit | 1 kWh at the station bus | This document's reserve anchor |

## Appendix B — the balance equations

For each site and each stock `s`:

```
stock_s(t+1) = stock_s(t) + production_s − consumption_s − export_s + import_s − loss_s
```

For each element in the waste loop (water, N, P, K):

```
closure_e = recycled_e / (recycled_e + lost_e)
make_up_e = consumption_e · (1 − closure_e)
```

For the energy:

```
energy_in = Σ recipes energy + transport energy + habitat energy
energy_out = waste heat (the radiator's constraint)
```

For the price:

```
p_i = Σ_k λ_k a_ki + transport_i + capital_i
λ_k ≥ 0, and λ_k = 0 for a constraint that is not binding
```

## Appendix C — the tests the first slice must pass

1. **Mass balance.** The station's water, carbon, nitrogen and phosphorus stocks conserve to
   the loss terms over a simulated day.
2. **Energy balance.** The farm's LED, the recycler and the habitat sum to the station's
   power; the waste heat is within the radiator's capacity.
3. **Closure.** The water closure is the modelled fraction; the make-up mass is the loss.
4. **Marginal cost.** The price of food falls as the farm's shadow price falls; the price of
   an imported good includes the transport.
5. **Transport.** The propellant for a cargo is the rocket equation's answer for the drive;
   the contract's delta-v price is drive-independent.
6. **Market.** The spread, the drift, the trade pressure and the decay behave as the current
   client's tests assert, with the model's base mid.
7. **Determinism.** Two runs with the same site state, clock and trades produce the same
   prices.
8. **Contract.** The delivery pays the payout and charges the delta-v/fuel; the in-situ
   alternative is offered and priced.

---

*This page is the model; the client's `Market`, `Ledger`, `CargoHold` and `Contract` are the
interface. The next code slice is the `SiteEconomy` behind `Market`, with Meridian, the
waystation and Earth as the first three sites.*
