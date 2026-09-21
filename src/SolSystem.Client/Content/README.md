# Content

Two things that cannot be drawn without a shader, and the pieces that build them.

    Content.mgcb     the MonoGame content pipeline
    Sun.fx           the Sun's disc and corona
    Hud.spritefont   the flight display's font
    Fonts/           the face it is built from

`dotnet build` runs the pipeline: `MonoGame.Content.Builder.Task` invokes the `dotnet-mgcb` tool
pinned in `/.config/dotnet-tools.json`, which compiles `Sun.fx` and `Hud.spritefont` to `.xnb` files
beside the executable. A fresh clone therefore needs

    dotnet tool restore

once, or the build fails with *"dotnet-mgcb does not exist"* — which is the message to look for if
the shader mysteriously stops being found.

**OpenGL takes shader model 3.0 and nothing higher.** A `.fx` compiled for `vs_4_0` is rejected with
*"Vertex shader must be SM 3.0 or lower"*. DesktopGL is the only backend here, so everything is
compiled `vs_3_0`/`ps_3_0`.

## Why a font is in the repository

DejaVu Sans Mono, copied out of the system font package. It is monospaced because the numbers on a
flight display are read as columns, and a proportional face makes a value that changes length jitter
sideways every frame. The licence permits redistribution. The alternative — a hand-authored bitmap
font — would be several hundred lines of glyph data to avoid one file.
