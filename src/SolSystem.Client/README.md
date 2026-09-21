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

| | |
|---|---|
| `W` / `S` | throttle up and down |
| `A` / `D` | yaw |
| `R` / `F` | pitch |
| `Q` / `E` | roll |
| `Z` / `X` | full throttle / cut |
| `Up` / `Down` | time rate, up to an hour a second |
| `C` | change camera: chase, orbit, cockpit, port |
| drag (left button) | look around |
| wheel | zoom the orbit camera |
| `Esc` | quit |

The helm is rate-limited to six degrees a second because that is what a crewed hull can do, and the
engine fires along the nose because it is bolted to the back of it. Slowing down therefore means
turning round first, which takes thirty seconds and a kilometre of corridor. The controls do not
hide that, because that is the game.

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
