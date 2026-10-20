# SolSystem.Client

A window you can fly in.

```sh
dotnet run                      # fly
dotnet run -- --help            # what else it does
```

**No arguments means fly.** That is the whole point of the program, and it is worth writing down
because the first version of it printed the usage text and exited — a guard left over from when the
client could only render a single frame and had nothing to do with no arguments. Interactive mode
was then added underneath it and nobody ran `dotnet run` with no arguments again.

## Controls

### The ship

| Key | What it does exactly |
|---|---|
| `W` / `S` | Throttle up / down at **0.8 per second**, so 0 → 100 % takes 1.25 s. A tap is a nudge. |
| `Z` / `X` | Throttle straight to 100 % / 0 %. |
| `A` / `D` | Yaw left / right, at the helm's maximum. |
| `R` / `F` | Pitch up / down. |
| `Q` / `E` | Roll left / right. |
| `Up` / `Down` | Time compression: one rung per press up or down a ladder of ×0.1, ×1, ×10, ×100, ×1000. |
| `L` | Open or close the local strategic map. |
| `M` | Open or close the solar-system chart. |
| `Esc` | Quit. |

**The engine fires along the nose.** `W` does not push the ship in the direction it is drifting; it
pushes it in the direction it is *pointing*. So the throttle and the helm are one control, and going
anywhere is a two-part decision — where to point, then how hard to burn.

**Roll does not move the nose.** `E` and `Q` turn the ship about its own long axis, so the nose stays
where it is and the deck goes round. That sounds obvious and it was not: roll used to be a rotation
about the *world* z-axis, which does not roll a ship at all — it swings the nose sideways. So `Q`/`E`
and `A`/`D` did the same thing and there was no roll. And `D` turned *left*, because the yaw axis was
the wrong way round.

**The helm is six degrees a second, and that is a hard limit.** Every turn key commands the maximum
and the ship clamps it, so:

| | |
|---|---|
| 90° | 15 s |
| 180° — a full reversal | **30 s** |

Half a minute of turning, during which the engine is useless because it points the wrong way. That
is the single most important number on this list: **slowing down means turning round first.**

### At a docked port

| Key | What it does exactly |
|---|---|
| `T` | Open or close the market. It only opens when the docking law says the ship is at the port. |
| `1` / `2` | Select the good. |
| `B` / `S` | Buy or sell one tonne. |
| `F` | Load ten tonnes of fuel, limited by the tank. |

The market has the keyboard while it is open; the engine is shut, but the clock still runs.

**Full throttle is four milligee** — 0.0393 m/s², because the drive is limited by what the radiator
can reject rather than by what the engine could produce:

| After | Speed |
|---|---|
| 10 s | 0.39 m/s |
| 60 s | 2.4 m/s |
| 1 hour | 0.14 km/s |

That is why the engine plume exists: at these accelerations the ship is *always* moving and it never
looks like it.

**Propellant**: 4.58 grams a second at full throttle, so the 40 tonnes aboard last **101 days** of
continuous burn. Delta-v and burn time are on the flight display because they are the same fact said
two ways.

### The camera

| Input | What it does exactly |
|---|---|
| `C` | Cycles chase → orbit → cockpit → port. |
| **Drag (left button)** | **Looks around** — 0.315° per pixel. The camera stays put and the view swings past the ship. |
| Drag (right button) | Orbits: travels the camera round the hull. Walking round your own ship, which is a different motion and now a different button. |
| Wheel | Zooms the camera you are in: ×1.18 a notch, clamped to 25 m – 4 km. |
| `Up` / `Down` | *(ship controls — they do not move the camera)* |

**Look and orbit are two different motions and were one button.** The left drag travelled the camera
round the hull at a fixed radius, so the ship stayed pinned in the middle of the frame and the sky
whirled around it — reported, exactly rightly, as *"dragging the mouse instead of rotating the view,
rotates the ship"*. Nothing was rotating but the camera, and the only thing that looked like it was
moving was the ship. Left drag now aims the view; right drag walks round the ship.

Changing camera mode with `C` recentres the view, so you are never left staring at empty sky with no
idea which way the ship was pointing when you looked away.

**The wheel used to work in one mode out of four.** It moved the orbit distance and nothing else, and
the chase camera ignored it — so in the default view it did nothing at all. A zoom control that works
in a quarter of the views and says so nowhere is worse than no zoom control. Both cameras now keep
their own distance and both respond.

### If the camera does not respond

The display carries two lines that answer it:

```
VIEW    CHASE 130 m  yaw 0  pitch 16
MOUSE   drag 0 px   wheel 0   window active
```

Drag the mouse and watch `drag`. Scroll and watch `wheel`. Both count cumulative pixels and notches,
so if they stay at zero the input is not arriving and the problem is the window, not the camera — and
if `window active` reads **WINDOW NOT FOCUSED** in orange, that is the answer: the game only receives
mouse input while its window has focus, and clicking the title bar is not the same as clicking into
the view.

The two angles are a readout rather than decoration. *"The drag does nothing"* cannot be told apart
from *"the drag works and is not obvious"* without seeing what the input did to the state, and two
numbers settle it.

| Mode | Where it is | What it is for |
|---|---|---|
| **Chase** | 130 m behind and 42 m above the hull, aimed 40 m ahead of the nose | flying — the ship is in frame and the direction of travel is in the middle |
| **Orbit** | 260 m out, aimed *at* the hull | looking at your own ship, and zooming to inspect it |
| **Cockpit** | Just past the nose — 1.06 hull lengths from the origin — looking wherever the helm points | the only view where the reticle means anything, and the one to fly a docking approach from |
| **Port** | 230 m off the far side of the hull, looking back along the docking corridor | judging an approach |

### Time compression

`Up` and `Down` step one rung per press:

| | ×0.1 | ×1 | ×10 | ×100 | ×1000 |
|---|---|---|---|---|---|
| one second of real time is | 0.1 s | 1 s | 10 s | 1 m 40 s | 16 m 40 s |

The rate is on the flight display, because **a clock running at a thousand times real time and a
clock running at one look exactly the same** until you have watched one of them for a minute. The
first version multiplied the rate by sixty for as long as the key was held, which has two faults: no
way to ask for twice, and no way to know what you got.

**The top two rungs are the strategic clock.** At ×100 and ×1000 the stations are rotated by their
exact circular orbit, the ship is held in the home station's frame, and the clock advances exactly;
the panel says `STRATEGIC`. The engine is shut and the helm is disengaged: this is the waiting
clock, not a way to fly for free. Below them the ship flies at 120 Hz. The station-keeping and sky
probes use the same rotation for their `days` spans, which is why a ten-day probe no longer takes
ten minutes.

### The local map

`L` opens the local strategic map: the Hill frame of Meridian, +x along the orbit, +y radially
outward, the Earth 6 778 km below. The home is a square at the origin, the waystation a diamond
6.8 km ahead, the ship a triangle, and the line between ship and target is the range and the
closing. The contract, the clock, the credits, the cargo and the mass are on the left; the grid is
one kilometre and the scale bar says so.

The map **runs**: the clock, the stations and the contract keep moving while it is open, and the
ship's own controls stay live. `M` is the solar-system chart, `T` the market, `--local` starts here.

### One thing the controls do not do

- **No gentle turn.** The keys are on or off, so every rotation is at the six-degree limit. A finer
  helm is a thing a docking pilot would want and it is not there yet.

## Why the camera matters more than it looks

The first version of this client had one fixed chase view, and the first thing anybody said about it
was *"I can see the earth but nothing happens"*. The physics was right the whole time — the ship
accelerates at four milligee, which over ten seconds is four tenths of a metre a second, and from a
camera a hundred and thirty metres back that is indistinguishable from sitting still. The simulation
worked and the game was unplayable, because nothing on the screen said a key had done anything.

Three things came out of that sentence:

  - **The camera is an instrument, not a decoration.** Four modes, because a pilot needs all four at
    different moments: chase shows you the ship, cockpit shows you where you are going, orbit shows
    you the ship from outside, and port looks back down the docking corridor at an approach.
  - **The engine plume is a readout.** A fusion torch at four milligee has no exhaust you could see
    from outside; what is drawn is the radiator glow at the throat, scaled by throttle. It is a
    deliberate lie about brightness in service of a truth about state.
  - **Earthshine is a real light and was missing.** A hull four hundred metres up is lit by the Sun
    *and* by the 30.6 % of sunlight the planet bounces back — which is why the night side of a
    spacecraft in low orbit is a deep blue-grey in every photograph ever taken from one, and not
    black. One directional light made half of every hull a silhouette.

**The Earth is the one body drawn by a shader.** Its day map and cloud alpha are still the NASA
images, but `Planet.fx` owns the terminator, the Black Marble city lights, the cloud deck — sampled
in the same shader so a cloud and the ground under it agree on where night is — and a 2 % Fresnel
atmosphere shell. The other bodies stay baked sphere textures; only the Earth has a night side worth
lighting.

## Rendering a frame without flying

```sh
dotnet run -- --shot out.png --milkyway
dotnet run -- --shot out.png --sunward --at 2451545.0
dotnet run -- --frames 90 --shot out.png     # run the interactive loop, then save and exit
dotnet run -- --shot out.png --camera port   # chase, orbit, cockpit or port
dotnet run -- --shot out.png --throttle 1    # with the engine lit, for the plume
dotnet run -- --shot out.png --lineup        # every asset at true size, side by side
dotnet run -- --shot out.png --local         # the local strategic map
dotnet run -- --shot out.png --market        # docked, with the market open
```

**The opening frame is lit by construction.** The default date is the March 2000 equinox,
JD 2451623.80, not J2000. At that instant Earth is on the ecliptic's −x axis, so the Sun
lies along +x — the direction Meridian's offset and docking corridor already point. The
station is therefore between Earth and Sun, a ship on the corridor is on the sunward side
of both, and the opening chase view looks back at a fully lit Earth with the Sun behind
its camera. `--at` overrides the date; the simulation's own origin is still J2000. The chase
camera opens with a 20° look offset so the lit Earth and the station share the frame; `C`
recentres, and `--camera-yaw` replaces the offset (the cockpit always starts along the nose).

**The reticles are projections, not screen furniture.** A marker and a star use the same
direction-only projection and the camera's orthonormal screen basis; a pitched camera's deck is
not that basis, which is why the Sun's diamond used to sit away from the Sun. The Sun's disc is
the real 0.53° — about six pixels at 720p and a 60° field — and its corona is a screen-space quad,
so the glow stays circular when the Sun is off-axis instead of projecting as a trapezoid.

A 3D scene is hard to test and easy to believe, and the only honest check on "does the sky look
right" is to render it and look. Every claim this project makes about the Milky Way being in the
right place, or the Sun being where the ephemeris says on a given date, is checkable against a frame
from this mode.

`--frames` runs the loop a player gets — update, keyboard, fixed timestep — rather than the
one-frame path, which is the only way to check that the thing a person actually types starts.

## The first contract

The second site is a Workers waystation a milliradian ahead of Meridian on the same orbit:
6.8 km away, 7.7 m/s of relative drift, a 91 m spine with its port facing outward. The home
station is still the observer's anchor and the ship's start; the target is what the range
panel, the nav marker, the corridor gates, the approach and the mission now measure against.

The contract pays 12,000 credits for medical supplies delivered within two hours. The deadline
is the session clock, the credits are on the panel, and the docking law at the target is the
same one the probe flies at the home.

## The first economy

The pilot starts with **3,000 credits**. The contract's five tonnes of medical supplies are in
the hold from the first frame, and the hold is part of the ship's mass: five tonnes of cargo is
five tonnes less acceleration for the whole run. Delivery pays 12,000 credits and charges the
fuel burned at the home port's price — 250 credits a tonne — and the settlement line says both.

The market opens at a **docked** port with `T`; the start is four hundred metres down the
corridor, so the first thing a trader does is dock. Meridian sells machine parts at 2,000 and
buys water at 100; the waystation sells water at 300 and buys machine parts at 3,200. Carry
parts out and water back and the run pays before the contract does. The hold is 20 t, every
purchase and sale moves the ship's mass, and `F` loads ten tonnes of fuel at the local price
(limited by the tank).

`--docked` starts at the port and `--market` starts there with the market open, which is how the
market preview is rendered.

## The narrator

The session speaks. The opening plays over the first frame; the arrival at a body is said once
when its position comes within reach; and the main engine's burn, the tanks, and the dock approach
each have a line, read from the same state the renderer draws and the session advances. The voice
is [MOSS-TTS-Nano](https://github.com/OpenMOSS/MOSS-TTS-Nano) (Apache-2.0) — 0.1B parameters,
evaluated on CPU, in-process: no service, no sidecar, no network. The weights are not committed;
`tools/tts/fetch_models.sh` fetches them once (~770 MB, gitignored under `artifacts/moss-tts/`).

A session's voice is one channel: chosen at startup (`--narrator-voice <name>` for the built-in
presets, `--narrator-prompt <file>` to clone one from a 16-bit PCM recording) and held. The
numbers in the course lines are generated, not authored — a fixed set of audio could not say what
the simulation says.

**The terminal and the voice are one performance.** A line is synthesized before it is played, so
its duration is known; the comm log starts typing when the audio starts and types the line over
that duration. Mission notes with no voice behind them fall back to the terminal's fixed speed.

## The controller

`--controller <port>` opens HTTP on `127.0.0.1:<port>` and puts the client in **external drive**:
the world advances only when a request moves it. The window keeps drawing, so a driven session can
be watched; the camera still follows the mouse; the keyboard flies nothing, because the driver is
the pilot.

The requests, all JSON:

| Request | What it does |
|---|---|
| `GET /state` | Clock, ticks, station, ship — metres, the simulation's own frame |
| `GET /hash` | SHA-256 over the world's raw fixed-point words |
| `POST /advance` | `{"ticks":n}` or `{"seconds":n}` or `{"days":n}` — steps the world, answers with state |
| `POST /throttle` | `{"fraction":0..1}` — sets the lever |
| `POST /engage` | `{"body":"Mars"}` (or `{"option":"torch-1"}`) — the chart's course, computed |
| `POST /render` | `{"frames":n,"dir":path}` — saves numbered PNGs through the ordinary loop |
| `POST /quit` | `{}` — closes the client |

POSTs take a body (send `{}` when there is nothing to say — the listener refuses a bodyless POST).
At most one request runs per update, at a tick boundary, in order: a driver drives the same loop a
player flies and can prove it is the same world by hashing it. Two runs given the same request
sequence produce the same hashes — and the same PNGs, byte for byte. A render run is the ordinary
frame loop with the ordinary save; `ffmpeg` turns a directory of its frames into a movie.

## The mission

`--mission <file>` loads a Lua script that watches the flight and speaks to the pilot over the
narrator's channel (and prints what it logs when there is no voice to say it). The file's first
non-empty line must be the format stamp `-- solsystem-mission:1`; other stamps are refused with the
mismatch named, before the interpreter runs. It reads the
simulation through the probe's vocabulary — `sim.range()`, `sim.lateral()`, `sim.closing()`,
`sim.docked()`, `sim.in_contact()`, the lever and the tanks — registers outcome verdicts with
`mission.outcome(name, predicate)`, and talks with `mission.say(text)` / `mission.note(text)`.
The world moves on the world's schedule; the script is a passenger with opinions, and a script
that throws says nothing more. The interpreter is MoonSharp's soft sandbox: no filesystem, no
operating system, no dynamic `load`, no `math.random`. The first mission is the docking tutorial
(`tools/missions/docking-tutorial.lua`).

## Two scales, and why

The far pass has a unit of a thousand kilometres, because Neptune is four and a half million of them
away. A fifty-metre hull is five hundred-millionths of one of those, and the near plane that keeps the
Earth sharp is four hundred metres — which would put the ship behind the camera's own clipping plane.
So the hull is drawn in a second pass whose unit is one metre, with its own projection, over a
background that is already painted.
