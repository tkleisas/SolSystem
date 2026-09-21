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
| `Up` / `Down` | Time compression: ×60 or ×0.1, from the base rate. Not cumulative. |
| `Esc` | Quit. |

**The engine fires along the nose.** `W` does not push the ship in the direction it is drifting; it
pushes it in the direction it is *pointing*. So the throttle and the helm are one control, and going
anywhere is a two-part decision — where to point, then how hard to burn.

**The helm is six degrees a second, and that is a hard limit.** Every turn key commands the maximum
and the ship clamps it, so:

| | |
|---|---|
| 90° | 15 s |
| 180° — a full reversal | **30 s** |

Half a minute of turning, during which the engine is useless because it points the wrong way. That
is the single most important number on this list: **slowing down means turning round first.**

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
| Drag (left button) | Looks around: 0.315° per pixel. In cockpit it turns your head; in chase and orbit it swings the camera round the ship. |
| Wheel | Zooms the **orbit** camera only: ×1.18 a notch, clamped to 25 m – 4 km. |
| `Up` / `Down` | *(ship controls — they do not move the camera)* |

| Mode | Where it is | What it is for |
|---|---|---|
| **Chase** | 130 m behind and 42 m above the hull, aimed 40 m ahead of the nose | flying — the ship is in frame and the direction of travel is in the middle |
| **Orbit** | 260 m out, aimed *at* the hull | looking at your own ship, and zooming to inspect it |
| **Cockpit** | 34 m forward of the origin, on the nose, nothing of the ship in view | the only view where the reticle means anything |
| **Port** | 230 m off the far side of the hull, looking back along the docking corridor | judging an approach |

### Two things the controls do not do

- **No gentle turn.** The keys are on or off, so every rotation is at the six-degree limit. A finer
  helm is a thing a docking pilot would want and it is not there yet.
- **Time compression saturates.** The ship steps at a fixed 120 Hz and at most 240 ticks a frame, so
  above two seconds of simulated time per frame the clock advances faster than the ship flies. At
  ×60 that needs 30 fps; below it, the sky runs ahead of the hull.

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

## Rendering a frame without flying

```sh
dotnet run -- --shot out.png --milkyway
dotnet run -- --shot out.png --sunward --at 2451545.0
dotnet run -- --frames 90 --shot out.png     # run the interactive loop, then save and exit
dotnet run -- --shot out.png --camera port   # chase, orbit, cockpit or port
dotnet run -- --shot out.png --throttle 1    # with the engine lit, for the plume
dotnet run -- --shot out.png --lineup        # every asset at true size, side by side
```

A 3D scene is hard to test and easy to believe, and the only honest check on "does the sky look
right" is to render it and look. Every claim this project makes about the Milky Way being in the
right place, or the Sun being where the ephemeris says on a given date, is checkable against a frame
from this mode.

`--frames` runs the loop a player gets — update, keyboard, fixed timestep — rather than the
one-frame path, which is the only way to check that the thing a person actually types starts.

## Two scales, and why

The far pass has a unit of a thousand kilometres, because Neptune is four and a half million of them
away. A fifty-metre hull is five hundred-millionths of one of those, and the near plane that keeps the
Earth sharp is four hundred metres — which would put the ship behind the camera's own clipping plane.
So the hull is drawn in a second pass whose unit is one metre, with its own projection, over a
background that is already painted.
