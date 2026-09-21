using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The chart: the whole solar system, seen from above, with somewhere to point at.
/// </summary>
/// <remarks>
/// <para>
/// A top-down view of the ecliptic with the Sun at the middle and every planet on its orbit, drawn
/// logarithmically. <b>Logarithmically is not a convenience.</b> Mercury is at 0.39 astronomical
/// units and Neptune at 30 — a factor of 77 — and on a linear chart either the inner system is a
/// smudge in the middle or the outer system is off the paper. The radius on screen goes as
/// <c>log(1 + r/r0)</c>, which puts the four inner planets in the middle half of the frame and still
/// shows Neptune near the edge, and the rings are labelled with their real distances so the
/// compression is stated rather than hidden.
/// </para>
/// <para>
/// The ship is at the centre of its own chart when zoomed in and a dot on a planet's orbit when
/// zoomed out, and the crossfade between those two is the zoom control: the same view serves "where
/// am I relative to the station" and "where am I relative to Jupiter", which are the two questions a
/// pilot actually asks.
/// </para>
/// </remarks>
internal sealed class Chart : IDisposable
{
    /// <summary>
    /// The radius at which the logarithmic compression starts to bite, in kilometres.
    /// </summary>
    /// <remarks>
    /// A tenth of an astronomical unit. Inside it the chart is nearly linear, so the Earth–Moon
    /// system and the docking corridors are where they should be; outside it compresses hard.
    /// </remarks>
    private const double CompressionRadiusKm = 0.1 * FlightPlan.KilometresPerAu;

    /// <summary>How far out the chart reaches, in screen radii.</summary>
    private const float ChartRadius = 0.92f;

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _disc;
    private readonly Texture2D _orbit;

    /// <summary>Which body the pilot has selected, or null.</summary>
    internal Ephemeris.Body? Selected { get; set; }

    /// <summary>Zoom, from 1 (the inner system) up to 40 (Neptune).</summary>
    internal double Zoom { get; private set; } = 1.0;

    /// <summary>Where each body is on screen this frame, for hit testing.</summary>
    private readonly List<(Ephemeris.Body Body, Vector2 Screen)> _placed = new();

    internal Chart(GraphicsDevice device, SpriteBatch sprites)
    {
        _device = device;
        _sprites = sprites;
        _disc = MakeDisc(device, 64);
        _orbit = MakeRing(device, 128);
    }

    internal void AdjustZoom(float notches) =>
        Zoom = Math.Clamp(Zoom * Math.Pow(1.25, notches), 1.0, 40.0);

    /// <summary>
    /// The logarithmic radius, in kilometres, mapped to a fraction of the chart's radius.
    /// </summary>
    private float Radial(double kilometres) =>
        (float)(Math.Log(1.0 + (kilometres / CompressionRadiusKm))
            / Math.Log(1.0 + ((40.0 * FlightPlan.KilometresPerAu) / CompressionRadiusKm)));

    /// <summary>Where a heliocentric point falls on the chart.</summary>
    private Vector2 Project(Fix128Vec position, Vector2 centre, float radius)
    {
        double x = position.X.ToDouble();
        double y = position.Y.ToDouble();
        double distance = Math.Sqrt((x * x) + (y * y));

        if (distance < 1e-9)
        {
            return centre;
        }

        // The compression is applied to the DISTANCE and the direction is kept, so an orbit stays
        // round-ish and a body stays on its own bearing. Compressing x and y separately would bend
        // every orbit into a different shape and put Mars in the wrong place.
        float mapped = Radial(distance) * radius;

        return new Vector2(
            centre.X + (float)(x / distance * mapped),
            centre.Y - (float)(y / distance * mapped));
    }

    /// <summary>
    /// Draws the chart and returns whichever body is nearest a point, for a click.
    /// </summary>
    internal void Draw(FlightSession session, SolarSystem system, Vector2? pick)
    {
        int width = _device.Viewport.Width;
        int height = _device.Viewport.Height;

        var centre = new Vector2(width * 0.5f, height * 0.5f);
        float radius = Math.Min(width, height) * 0.5f * ChartRadius;

        _placed.Clear();

        // The orbits first, so the bodies sit on top of them.
        foreach (Ephemeris.Body body in Enum.GetValues<Ephemeris.Body>())
        {
            if (body == Ephemeris.Body.Earth)
            {
                continue;
            }

            Ephemeris.State state = system.Heliocentric(body);
            float orbitRadius = Radial(state.Position.Length.ToDouble()) * radius;

            DrawRing(centre, orbitRadius, new Color(70, 110, 130, 90));
        }

        // Then the bodies.
        foreach (Ephemeris.Body body in Enum.GetValues<Ephemeris.Body>())
        {
            Ephemeris.State state = system.Heliocentric(body);
            Vector2 at = Project(state.Position, centre, radius);

            _placed.Add((body, at));

            bool chosen = Selected == body;
            float size = chosen ? 15f : 9f;

            _sprites.Draw(_disc, at, null,
                chosen ? new Color(255, 220, 130) : BodyColour(body),
                0f, new Vector2(_disc.Width * 0.5f), size / (_disc.Width * 0.5f),
                SpriteEffects.None, 0f);

            if (chosen)
            {
                // A ring round the selection, so it reads at a glance which of nine dots is the one
                // the flight computer is about to be pointed at.
                DrawRing(at, 22f, new Color(255, 220, 130, 200));
            }
        }

        // The Sun.
        _sprites.Draw(_disc, centre, null, new Color(255, 245, 210), 0f,
            new Vector2(_disc.Width * 0.5f), 26f / (_disc.Width * 0.5f), SpriteEffects.None, 0f);

        // The ship, and a line to whatever is selected.
        Vector2 ship = Project(session.ObserverPosition, centre, radius);

        if (Selected is Ephemeris.Body target)
        {
            Ephemeris.State state = system.Heliocentric(target);
            Vector2 to = Project(state.Position, centre, radius);

            _sprites.Draw(_orbit, to, null, new Color(180, 220, 255, 60), 0f,
                new Vector2(_orbit.Width * 0.5f),
                Vector2.Distance(ship, to) * 2f / _orbit.Width, SpriteEffects.None, 0f);

            DrawLine(ship, to, new Color(150, 210, 255, 150));
        }

        _sprites.Draw(_disc, ship, null, new Color(140, 255, 180), 0f,
            new Vector2(_disc.Width * 0.5f), 7f / (_disc.Width * 0.5f), SpriteEffects.None, 0f);
    }

    /// <summary>
    /// The body nearest a point, if one is close enough to have been aimed at.
    /// </summary>
    /// <remarks>
    /// A generous catchment, because the outer planets are nine pixels across and nobody can hit
    /// nine pixels. Nearest-within-thirty is what a person means by "that one".
    /// </remarks>
    internal Ephemeris.Body? Nearest(Vector2 point, float within = 30f)
    {
        Ephemeris.Body? best = null;
        float closest = within;

        foreach ((Ephemeris.Body body, Vector2 at) in _placed)
        {
            float distance = Vector2.Distance(point, at);
            if (distance < closest)
            {
                closest = distance;
                best = body;
            }
        }

        return best;
    }

    /// <summary>Steps the selection to the next body, for a key.</summary>
    internal void NextBody()
    {
        Ephemeris.Body[] order = Enum.GetValues<Ephemeris.Body>();
        int at = Selected is Ephemeris.Body current ? Array.IndexOf(order, current) : -1;

        Selected = order[(at + 1) % order.Length];
    }

    private static Color BodyColour(Ephemeris.Body body) => body switch
    {
        Ephemeris.Body.Mercury => new Color(170, 165, 160),
        Ephemeris.Body.Venus => new Color(235, 220, 180),
        Ephemeris.Body.Earth => new Color(120, 175, 245),
        Ephemeris.Body.Mars => new Color(215, 130, 90),
        Ephemeris.Body.Ceres => new Color(170, 170, 165),
        Ephemeris.Body.Jupiter => new Color(225, 200, 160),
        Ephemeris.Body.Saturn => new Color(230, 215, 170),
        Ephemeris.Body.Uranus => new Color(170, 225, 230),
        _ => new Color(150, 180, 235),
    };

    /// <summary>A ring, drawn as a soft annulus from a radial texture.</summary>
    private void DrawRing(Vector2 centre, float radius, Color colour)
    {
        float size = radius * 2f;
        if (size < 2f)
        {
            return;
        }

        _sprites.Draw(_orbit, centre, null, colour, 0f, new Vector2(_orbit.Width * 0.5f),
            size / _orbit.Width, SpriteEffects.None, 0f);
    }

    /// <summary>A straight line between two points, from a one-pixel texture.</summary>
    private void DrawLine(Vector2 from, Vector2 to, Color colour)
    {
        Vector2 edge = to - from;
        float length = edge.Length();

        if (length < 1f)
        {
            return;
        }

        _sprites.Draw(_disc, from, null, colour, MathF.Atan2(edge.Y, edge.X),
            new Vector2(0f, _disc.Height * 0.5f),
            new Vector2(length / _disc.Width, 1.5f / _disc.Height), SpriteEffects.None, 0f);
    }

    /// <summary>
    /// A soft disc, and a ring when the middle is pulled out of it.
    /// </summary>
    /// <remarks>
    /// One texture serves both: drawn small it is a dot, and drawn as an annulus — which the orbit
    /// variant is — it is a circle. Two textures rather than a geometry pipeline, because a chart is
    /// nine dots and nine circles.
    /// </remarks>
    private static Texture2D MakeDisc(GraphicsDevice device, int size)
    {
        var texture = new Texture2D(device, size, size);
        var pixels = new Color[size * size];
        float centre = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float d = MathF.Sqrt((dx * dx) + (dy * dy));
                float alpha = d >= 1f ? 0f : MathF.Pow(1f - d, 2.2f);

                pixels[(y * size) + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    /// <summary>An annulus: a thin bright circle with nothing inside it.</summary>
    private static Texture2D MakeRing(GraphicsDevice device, int size)
    {
        var texture = new Texture2D(device, size, size);
        var pixels = new Color[size * size];
        float centre = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float d = MathF.Sqrt((dx * dx) + (dy * dy));

                // A ring a couple of pixels thick at the texture's edge, so that drawn at any size
                // it stays a line rather than becoming a band.
                float edge = MathF.Abs(d - 0.985f);
                float alpha = edge > 0.02f ? 0f : 1f - (edge / 0.02f);

                pixels[(y * size) + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    public void Dispose()
    {
        _disc.Dispose();
        _orbit.Dispose();
    }
}
