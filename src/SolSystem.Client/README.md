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
| `Esc` | quit |

The helm is rate-limited to six degrees a second because that is what a crewed hull can do, and the
engine fires along the nose because it is bolted to the back of it. Slowing down therefore means
turning round first, which takes thirty seconds and a kilometre of corridor. The controls do not
hide that, because that is the game.

## Rendering a frame without flying

```sh
dotnet run -- --shot out.png --milkyway
dotnet run -- --shot out.png --sunward --at 2451545.0
dotnet run -- --frames 90 --shot out.png     # run the interactive loop, then save and exit
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
