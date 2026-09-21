using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The nav-point reticles: named markers in screen space over the flight view, so that a pilot
/// can find a thing in the sky without already knowing where it is.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the flight view is otherwise unreadable as a navigation instrument: the
/// panel says the port is four hundred metres away, and the sky is a hundred and twenty degrees
/// of stars in every direction. A marker is the join between the two — the number on the panel
/// and the point in the sky are the same fact, and the overlay is what says so.
/// </para>
/// <para>
/// <b>The projection is direction-only, and it is the star projection.</b> A marker's screen
/// position depends only on which way the thing lies and which way the camera looks — two dot
/// products and a divide, exactly what <see cref="SkyRenderer.DrawStars"/> does with the same
/// camera basis and the same field of view. That one recipe serves the port at four hundred
/// metres and Neptune at four billion kilometres, because placement never needs the distance;
/// only the label does. The port is the exception that proves it: its vector is passed
/// un-normalised, so its parallax against the station's own hull is right up close.
/// </para>
/// <para>
/// <b>Velocity markers are station-relative, not heliocentric.</b> The ship and the station are
/// both falling round the Earth at seven and a half kilometres a second, and the Earth round the
/// Sun at thirty; a heliocentric prograde marker near the station would point at the Earth's
/// orbital motion and never at anything the pilot can do something about. The frame that matters
/// for a docking is the difference between the ship's velocity and the station's, and the local
/// frame's axes are the ecliptic's, so that difference drops straight into the projection.
/// </para>
/// <para>
/// Nothing here writes to the simulation. The overlay reads the session, the flight and the
/// camera, and draws.
/// </para>
/// </remarks>
internal sealed class NavOverlay
{
    /// <summary>How far off the screen's edge a clamped marker sits, in pixels.</summary>
    /// <remarks>
    /// Twenty-four: clear of the frame the HUD draws at the very edge, close enough that a
    /// clamped marker still reads as being on the side it is pointing off.
    /// </remarks>
    private const float Margin = 24f;

    /// <summary>Marker outline radius, in pixels. Small and quiet: an instrument, not an arcade.</summary>
    private const float Radius = 11f;

    /// <summary>Segments in a drawn circle. Twenty reads as round at this radius.</summary>
    private const int CircleSegments = 20;

    /// <summary>
    /// Below this station-relative speed, in m/s, no velocity markers are drawn at all.
    /// </summary>
    /// <remarks>
    /// A velocity marker that cannot decide which way it points is noise: at zero relative speed
    /// the direction is a rounding error, and a prograde circle wandering randomly around the
    /// reticle reads as a bug, not as information.
    /// </remarks>
    private const double MinimumMarkerSpeed = 0.05;

    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly SpriteFont _hud;

    internal NavOverlay(SpriteBatch sprites, Texture2D pixel, SpriteFont hud)
    {
        _sprites = sprites;
        _pixel = pixel;
        _hud = hud;
    }

    /// <summary>The outline shapes: one per kind of thing, so shape carries the category.</summary>
    private enum Shape
    {
        /// <summary>Bodies: the Earth, the Sun, the Moon.</summary>
        Diamond,

        /// <summary>The station's docking port — a made thing, not a body.</summary>
        Square,

        /// <summary>The chart destination, and prograde: a circle.</summary>
        Circle,

        /// <summary>Retrograde: a circle with a cross, the usual "against the motion" glyph.</summary>
        CircleCross,
    }

    /// <summary>
    /// Draws every marker for one frame, through the camera the frame was rendered with.
    /// </summary>
    /// <param name="device">For the viewport's size.</param>
    /// <param name="session">Where everything is.</param>
    /// <param name="flight">The ship: its velocity relative to the station drives three markers.</param>
    /// <param name="cameraForward">The direction the CAMERA is looking — not the ship's nose.</param>
    /// <param name="cameraUp">The camera's up.</param>
    /// <param name="fieldOfViewDegrees">The same field of view the passes were rendered with.</param>
    /// <param name="portOffset">The port's position relative to the ship, in metres, un-normalised.</param>
    /// <param name="destination">The chart's selected body, if one is selected.</param>
    internal void Draw(
        GraphicsDevice device,
        FlightSession session,
        Flight flight,
        Fix128Vec cameraForward,
        Fix128Vec cameraUp,
        float fieldOfViewDegrees,
        Vector3 portOffset,
        Ephemeris.Body? destination)
    {
        // The HUD's own palette, so the overlay reads as part of the same instrument panel.
        var ink = new Color(150, 220, 175);
        var dim = new Color(110, 150, 135);
        var warn = new Color(230, 170, 90);

        var system = new SolarSystem();
        system.SetTime((session.JulianDate - Ephemeris.J2000JulianDate) * 86400.0);

        Vector3 f = Unit(cameraForward);
        Vector3 u = Unit(cameraUp);
        Vector3 r = Vector3.Normalize(Vector3.Cross(f, u));

        float width = device.Viewport.Width;
        float height = device.Viewport.Height;
        float tanY = MathF.Tan(MathHelper.ToRadians(fieldOfViewDegrees) * 0.5f);
        float tanX = tanY * (width / height);
        var centre = new Vector2(width * 0.5f, height * 0.5f);

        _sprites.Begin();

        // The label blocks already on screen, so that two markers which project to the same
        // place — the port dead ahead and the Earth's centre directly behind it, which is where
        // the corridor points — stack their labels instead of printing one over the other.
        var placed = new List<Rectangle>();

        // The bodies, as diamonds. DirectionTo and DistanceTo answer from the observer's
        // heliocentric position, which already includes the station's offset from the Earth —
        // so the Earth marker points at the Earth's centre and not at the anti-Sun, the mistake
        // the session's own aim made first.
        Body(session.DirectionTo(session.Earth().Position),
            "EARTH", session.DistanceTo(session.Earth().Position));

        // The Sun is the heliocentric origin, so the direction to it is the reverse of where
        // the observer is.
        Body(session.DirectionTo(Fix128Vec.Zero), "SUN", session.DistanceTo(Fix128Vec.Zero));

        Fix128Vec moon = system.MoonHeliocentric().Position;
        Body(session.DirectionTo(moon), "MOON", session.DistanceTo(moon));

        void Body(Fix128Vec direction, string name, double rangeKm)
        {
            Vector2 at = Place(Unit(direction), f, r, u, tanX, tanY, width, height,
                out bool clamped);

            Color colour = clamped ? Faded(ink) : ink;
            Marker(at, Shape.Diamond, colour, clamped, centre);
            Label(at, centre, clamped, width, height, placed, name,
                [$"{rangeKm.ToString("N0", CultureInfo.InvariantCulture)} km"],
                colour, Faded(ink));
        }

        // The port, as a square, with the two numbers an approach is flown by. The range is the
        // length of the same ship-relative vector the camera is positioned with, and the closing
        // rate is the ship's station-relative velocity along it, negated so that closing reads
        // negative — the convention a docking display has always used, where "minus five" means
        // "coming in at five".
        float range = portOffset.Length();
        Vector3 relative = Unit(flight.Ship.Velocity - session.Station.Velocity);
        float closing = range > 0.5f
            ? -Vector3.Dot(relative, portOffset / range)
            : 0f;

        Vector2 portAt = Place(portOffset, f, r, u, tanX, tanY, width, height,
            out bool portClamped);

        Color portColour = portClamped ? Faded(ink) : ink;
        Marker(portAt, Shape.Square, portColour, portClamped, centre);
        Label(portAt, centre, portClamped, width, height, placed, "MERIDIAN PORT",
            [
                $"{range:F0} m",
                $"CLOSING {closing:+0.0;-0.0;0.0} m/s",
            ],
            portColour, Faded(ink));

        // The chart's destination, as a circle, in the amber the chart itself selects with — the
        // same colour in both views, so the body picked on the chart is recognisably the same
        // body in the sky.
        if (destination is Ephemeris.Body chosen)
        {
            Fix128Vec there = system.Heliocentric(chosen).Position;
            Vector2 at = Place(Unit(session.DirectionTo(there)), f, r, u, tanX, tanY,
                width, height, out bool clamped);

            Color colour = clamped ? Faded(warn) : warn;
            Marker(at, Shape.Circle, colour, clamped, centre);
            Label(at, centre, clamped, width, height, placed,
                SolarSystem.BodyOf(chosen).Name.ToUpperInvariant(),
                [$"{session.DistanceTo(there).ToString("N0", CultureInfo.InvariantCulture)} km"],
                colour, Faded(warn));
        }

        // Prograde and retrograde, station-relative — see the type's remarks for why the
        // heliocentric figure would be worse than useless here. No range: a direction has none.
        double speed = relative.Length();
        if (speed > MinimumMarkerSpeed)
        {
            Vector3 prograde = relative / (float)speed;

            Vector2 proAt = Place(prograde, f, r, u, tanX, tanY, width, height,
                out bool proClamped);
            Marker(proAt, Shape.Circle, proClamped ? Faded(dim) : dim, proClamped, centre);
            Label(proAt, centre, proClamped, width, height, placed, "PROGRADE", [],
                proClamped ? Faded(dim) : dim, Faded(dim));

            Vector2 retroAt = Place(-prograde, f, r, u, tanX, tanY, width, height,
                out bool retroClamped);
            Marker(retroAt, Shape.CircleCross, retroClamped ? Faded(dim) : dim,
                retroClamped, centre);
            Label(retroAt, centre, retroClamped, width, height, placed, "RETROGRADE", [],
                retroClamped ? Faded(dim) : dim, Faded(dim));
        }

        _sprites.End();
    }

    /// <summary>
    /// Projects a world vector onto the screen, clamping off-screen and behind-camera points
    /// to the screen's edge in the object's own direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In front of the camera this is the star projection verbatim: divide by the forward
    /// component, scale by the tangent of the half-angle.
    /// </para>
    /// <para>
    /// Behind the camera the perspective divide flips the point to the wrong side of the frame —
    /// an object astern and to port projects as though it were ahead and to starboard. The
    /// standard fix is to negate the PROJECTED coordinates, and since the divisor is negative
    /// that puts the point back on the side the raw lateral components already named: for a
    /// point astern the clamp direction is simply <c>(x, y)</c>, no flip at all. The first
    /// version of this negated the raw components instead of the projected ones, which is one
    /// flip too few, and every astern marker sat on the opposite edge from its object — caught
    /// by turning ninety degrees off the Sun and finding its marker pointing away from it.
    /// </para>
    /// </remarks>
    private static Vector2 Place(Vector3 v, Vector3 f, Vector3 r, Vector3 u,
        float tanX, float tanY, float width, float height, out bool clamped)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        float z = Vector3.Dot(v, f);
        float x = Vector3.Dot(v, r);
        float y = Vector3.Dot(v, u);

        float px;
        float py;
        if (z <= 0.0f)
        {
            // Astern: direction only, and un-negated — see above. The magnitude is arbitrary
            // because the clamp below rescales it; what matters is which side it falls on.
            px = halfWidth + (x / tanX * halfWidth);
            py = halfHeight - (y / tanY * halfHeight);
        }
        else
        {
            px = halfWidth + (x / z / tanX * halfWidth);
            py = halfHeight - (y / z / tanY * halfHeight);
        }

        clamped = z <= 0.0f
            || px < Margin || px > width - Margin
            || py < Margin || py > height - Margin;
        if (!clamped)
        {
            return new Vector2(px, py);
        }

        var centre = new Vector2(halfWidth, halfHeight);
        var d = new Vector2(px, py) - centre;

        // Directly astern the lateral components are both zero and there is no side to prefer;
        // any edge answers "turn round", and the bottom one is the one this picks.
        if (d.LengthSquared() < 1.0e-6f)
        {
            d = new Vector2(0f, 1f);
        }

        // The smallest scale that puts the point on an edge of the margin rectangle: the edge the
        // line from the centre meets first, which is the side the object is off.
        float t = float.MaxValue;
        if (d.X > 0f)
        {
            t = MathF.Min(t, (width - Margin - halfWidth) / d.X);
        }
        else if (d.X < 0f)
        {
            t = MathF.Min(t, (Margin - halfWidth) / d.X);
        }

        if (d.Y > 0f)
        {
            t = MathF.Min(t, (height - Margin - halfHeight) / d.Y);
        }
        else if (d.Y < 0f)
        {
            t = MathF.Min(t, (Margin - halfHeight) / d.Y);
        }

        if (t == float.MaxValue)
        {
            t = 0f;
        }

        return centre + (d * t);
    }

    /// <summary>
    /// One marker outline, with a gap at its centre.
    /// </summary>
    /// <remarks>
    /// The gap is the point, and the HUD's centre reticle established it: a filled marker covers
    /// the very thing it marks, and the first version of that reticle hid the Sun behind its own
    /// cross. Every shape here is an outline with nothing in the middle. A clamped marker also
    /// gets a short tick pointing off the screen, so "the edge of the frame" reads as "thataway".
    /// </remarks>
    private void Marker(Vector2 at, Shape shape, Color colour, bool clamped, Vector2 centre)
    {
        switch (shape)
        {
            case Shape.Diamond:
            {
                var top = new Vector2(at.X, at.Y - Radius);
                var right = new Vector2(at.X + Radius, at.Y);
                var bottom = new Vector2(at.X, at.Y + Radius);
                var left = new Vector2(at.X - Radius, at.Y);

                Line(top, right, colour);
                Line(right, bottom, colour);
                Line(bottom, left, colour);
                Line(left, top, colour);
                break;
            }

            case Shape.Square:
            {
                float s = Radius * 0.8f;

                Line(new Vector2(at.X - s, at.Y - s), new Vector2(at.X + s, at.Y - s), colour);
                Line(new Vector2(at.X + s, at.Y - s), new Vector2(at.X + s, at.Y + s), colour);
                Line(new Vector2(at.X + s, at.Y + s), new Vector2(at.X - s, at.Y + s), colour);
                Line(new Vector2(at.X - s, at.Y + s), new Vector2(at.X - s, at.Y - s), colour);
                break;
            }

            case Shape.Circle:
            case Shape.CircleCross:
            {
                for (int i = 0; i < CircleSegments; i++)
                {
                    float a0 = MathF.Tau * i / CircleSegments;
                    float a1 = MathF.Tau * (i + 1) / CircleSegments;

                    Line(
                        at + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * Radius,
                        at + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * Radius,
                        colour);
                }

                if (shape == Shape.CircleCross)
                {
                    float q = Radius * 0.55f;
                    Line(new Vector2(at.X - q, at.Y - q), new Vector2(at.X + q, at.Y + q), colour);
                    Line(new Vector2(at.X - q, at.Y + q), new Vector2(at.X + q, at.Y - q), colour);
                }

                break;
            }
        }

        if (clamped)
        {
            Vector2 outwards = at - centre;
            if (outwards.LengthSquared() > 1.0e-6f)
            {
                outwards = Vector2.Normalize(outwards);
                Line(at + (outwards * (Radius + 2f)), at + (outwards * (Radius + 9f)), colour);
            }
        }
    }

    /// <summary>
    /// The name under a marker, with any figures under the name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Below the marker ordinarily, because the sky above a target is the bit a pilot is looking
    /// through. A clamped marker has no below — it is sitting on the edge — so its label steps
    /// back toward the centre of the screen instead of falling off the frame.
    /// </para>
    /// <para>
    /// Two markers can project to the same spot — the port dead ahead has the Earth's centre
    /// directly behind it, because the corridor points at the planet — and two labels printed
    /// there read as one garbled word. So a label that overlaps one already drawn slides along
    /// its own placement direction until it is clear, and only gives up after a dozen line-heights.
    /// </para>
    /// </remarks>
    private void Label(Vector2 at, Vector2 centre, bool clamped, float width, float height,
        List<Rectangle> placed, string name, string[] figures,
        Color nameColour, Color figureColour)
    {
        float spacing = _hud.LineSpacing;
        float blockHeight = (1 + figures.Length) * spacing;

        float blockWidth = _hud.MeasureString(name).X;
        foreach (string figure in figures)
        {
            blockWidth = MathF.Max(blockWidth, _hud.MeasureString(figure).X);
        }

        // The direction the block grows in: inward for a clamped marker, straight down for an
        // on-screen one — or straight up when down would fall off the bottom of the frame.
        Vector2 dir;
        Vector2 anchor;
        if (clamped)
        {
            dir = Vector2.Normalize(centre - at);
            anchor = at + (dir * (Radius + 8f));
        }
        else
        {
            bool below = at.Y + Radius + 8f + blockHeight <= height - 4f;
            dir = new Vector2(0f, below ? 1f : -1f);
            anchor = new Vector2(at.X, at.Y + (dir.Y * (Radius + 8f)));
        }

        Vector2 blockCentre = anchor + (dir * (blockHeight * 0.5f));

        // A marker near the left or right edge has a perfectly good place to be and no room for
        // its name: the block slides back inside the frame rather than printing half off it.
        blockCentre.X = Math.Clamp(blockCentre.X,
            (blockWidth * 0.5f) + 4f, width - (blockWidth * 0.5f) - 4f);

        for (int tries = 0; ; tries++)
        {
            var rect = new Rectangle(
                (int)(blockCentre.X - (blockWidth * 0.5f)) - 2,
                (int)(blockCentre.Y - (blockHeight * 0.5f)) - 2,
                (int)blockWidth + 4,
                (int)blockHeight + 4);

            bool clear = true;
            foreach (Rectangle other in placed)
            {
                if (rect.Intersects(other))
                {
                    clear = false;
                    break;
                }
            }

            if (clear || tries == 12)
            {
                placed.Add(rect);
                break;
            }

            blockCentre += dir * spacing;
        }

        float y = blockCentre.Y - (blockHeight * 0.5f);

        Vector2 size = _hud.MeasureString(name);
        _sprites.DrawString(_hud, name,
            new Vector2(blockCentre.X - (size.X * 0.5f), y), nameColour);
        y += spacing;

        foreach (string figure in figures)
        {
            size = _hud.MeasureString(figure);
            _sprites.DrawString(_hud, figure,
                new Vector2(blockCentre.X - (size.X * 0.5f), y), figureColour);
            y += spacing;
        }
    }

    /// <summary>A hairline, drawn from the one white pixel stretched and turned.</summary>
    private void Line(Vector2 from, Vector2 to, Color colour)
    {
        Vector2 d = to - from;
        float length = d.Length();
        if (length < 0.5f)
        {
            return;
        }

        _sprites.Draw(_pixel, from, null, colour, MathF.Atan2(d.Y, d.X),
            new Vector2(0f, 0.5f), new Vector2(length, 1.5f), SpriteEffects.None, 0f);
    }

    /// <summary>The clamped variant of a colour: the same hue, quieter.</summary>
    private static Color Faded(Color colour) => new(colour.R, colour.G, colour.B, (byte)110);

    private static Vector3 Unit(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());
}
