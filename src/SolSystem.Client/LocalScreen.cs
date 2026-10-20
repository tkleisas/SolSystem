using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The local strategic map: the Hill frame around the home station, with the target and the ship.
/// </summary>
/// <remarks>
/// <para>
/// The solar chart answers "where is Mars"; this answers "where is the contract". The frame is the
/// home station's own: +x along the orbit, +y radially outward, the Earth below at the origin of
/// the radial. The target is a diamond 6.8 km ahead; the ship is a triangle wherever it is; the
/// line between them is the range and the closing.
/// </para>
/// <para>
/// The map runs rather than pauses: the clock, the contract and the stations keep moving while it
/// is open, because the point of a strategic view is to watch the deadline approach. The ship's
/// own controls remain live, so the pilot can fly from here; the map is a different view of the
/// same world, not a menu over it.
/// </para>
/// </remarks>
internal sealed class LocalScreen
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly SpriteFont _font;

    private static readonly Color Ink = new(150, 220, 175);
    private static readonly Color Dim = new(90, 125, 112);
    private static readonly Color Warm = new(230, 180, 100);
    private static readonly Color Grid = new(22, 36, 44);
    private static readonly Color Home = new(130, 210, 255);
    private static readonly Color Target = new(240, 190, 90);

    internal LocalScreen(GraphicsDevice device, SpriteBatch sprites, Texture2D pixel, SpriteFont font)
    {
        _device = device;
        _sprites = sprites;
        _pixel = pixel;
        _font = font;
    }

    internal void Draw(FlightSession session, Flight flight, Ledger ledger, CargoHold cargo,
        Contract contract, double timeRate)
    {
        _sprites.Begin();

        int width = _device.Viewport.Width;
        int height = _device.Viewport.Height;
        _sprites.Draw(_pixel, new Rectangle(0, 0, width, height), new Color(3, 6, 9));

        Station home = session.Station;
        Station target = session.Target;
        Ship ship = flight.Ship;

        // The home station's orbital frame: tangent along the velocity, radial outward from Earth.
        Fix128Vec radial = home.Offset.Normalized();
        Fix128Vec tangent = home.Velocity.Normalized();

        double targetX = Dot(target.Offset - home.Offset, tangent);
        double targetY = Dot(target.Offset - home.Offset, radial);
        double shipX = Dot(ship.Position - home.Offset, tangent);
        double shipY = Dot(ship.Position - home.Offset, radial);

        double extent = Math.Max(12_000.0,
            Math.Max(Math.Abs(targetX), Math.Max(Math.Abs(targetY),
                Math.Max(Math.Abs(shipX), Math.Abs(shipY)))));
        extent *= 1.18;

        float centreX = width * 0.54f;
        float centreY = height * 0.52f;
        float half = MathF.Min(width, height) * 0.40f;
        float scale = (float)(half / extent);

        float Px(double x) => centreX + ((float)x * scale);
        float Py(double y) => centreY - ((float)y * scale);

        // A one-kilometre grid, because at this scale the eye needs a ruler that is not the target.
        for (int km = -10; km <= 10; km++)
        {
            float gx = Px(km * 1000.0);
            if (gx >= 0 && gx <= width)
            {
                _sprites.Draw(_pixel, new Rectangle((int)gx, 0, 1, height), Grid);
            }

            float gy = Py(km * 1000.0);
            if (gy >= 0 && gy <= height)
            {
                _sprites.Draw(_pixel, new Rectangle(0, (int)gy, width, 1), Grid);
            }
        }

        // The orbit runs along +x through the home; the Earth is 6 778 km down the radial.
        _sprites.Draw(_pixel, new Rectangle((int)Px(-extent), (int)centreY, (int)(2 * extent * scale), 1),
            new Color(40, 70, 65));

        int limb = (int)Py(-6_778_100.0);
        if (limb > 0 && limb < height)
        {
            _sprites.Draw(_pixel, new Rectangle(0, limb, width, height - limb), new Color(8, 18, 30));
            _sprites.Draw(_pixel, new Rectangle(0, limb, width, 1), new Color(50, 100, 150));
            _sprites.DrawString(_font, "EARTH", new Vector2(28, limb + 8), new Color(70, 120, 160));
        }
        else
        {
            _sprites.DrawString(_font, "EARTH 6 778 km BELOW", new Vector2(28, height - 30), Dim);
        }

        // The home, the target and the ship.
        Square(Px(0), Py(0), 7, Home);
        _sprites.DrawString(_font, "MERIDIAN", new Vector2(Px(0) + 12, Py(0) - 8), Home);

        Diamond(Px(targetX), Py(targetY), 8, Target);
        _sprites.DrawString(_font, "WAYSTATION",
            new Vector2(Px(targetX) + 14, Py(targetY) - 20), Target);

        Triangle(Px(shipX), Py(shipY), 9, Ink);
        _sprites.DrawString(_font, "SHIP",
            new Vector2(Px(shipX) + 12, Py(shipY) + 4), Ink);

        // The line between ship and target: the thing the contract is about.
        Line(Px(shipX), Py(shipY), Px(targetX), Py(targetY), new Color(80, 130, 110));

        double range = (target.Port.Position - ship.Position).Length.ToDouble();
        double closing = Closing(ship, target);

        var at = new Vector2(28, 26);
        _sprites.DrawString(_font, "LOCAL ORBIT  \u00b7  HILL FRAME OF MERIDIAN", at, Ink);
        at.Y += FlightUi.Line * 1.5f;

        _sprites.DrawString(_font, $"CONTRACT   {contract.Name}", at, Ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"DUE        {contract.Status(session.JulianDate)}", at,
            contract.Failed ? Warm : Ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"TARGET     {range,9:N0} m", at, Ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"CLOSING    {closing,9:+0.0;-0.0;0.0} m/s", at, Ink);
        at.Y += FlightUi.Line * 1.4f;

        _sprites.DrawString(_font, $"CREDITS    {ledger.Credits,9:N0}", at, Ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font,
            $"CARGO      {cargo.MassTonnes,5:F1} / {CargoHold.CapacityTonnes:F0} t", at, Ink);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"MASS       {ship.Mass.ToDouble(),9:F1} t", at, Ink);
        at.Y += FlightUi.Line * 1.4f;

        _sprites.DrawString(_font, $"EPOCH JD   {session.JulianDate,10:F4}", at, Dim);
        at.Y += FlightUi.Line;
        _sprites.DrawString(_font, $"TIME       {FlightPanel.DescribeRate(timeRate),9}", at, Dim);
        at.Y += FlightUi.Line * 1.4f;

        _sprites.DrawString(_font, "L close   M solar chart   T market   Up/Down time", at,
            Dim);
        at.Y += FlightUi.Line;

        if (contract.Completed)
        {
            _sprites.DrawString(_font, $"DELIVERED  \u00b7  +{contract.Payout:N0}", at, Ink);
        }
        else if (contract.Failed)
        {
            _sprites.DrawString(_font, "CONTRACT EXPIRED", at, Warm);
        }

        // The scale bar, in kilometres, at the bottom left.
        float bar = scale * 1000.0f;
        int bx = 28;
        int by = height - 32;
        _sprites.Draw(_pixel, new Rectangle(bx, by, (int)bar, 2), Dim);
        _sprites.DrawString(_font, "1 km", new Vector2(bx, by - 22), Dim);

        _sprites.End();
    }

    private static double Closing(Ship ship, Station target)
    {
        Fix128Vec relative = ship.Velocity - target.Velocity;
        Fix128Vec toPort = target.Port.Position - ship.Position;
        double distance = toPort.Length.ToDouble();

        return distance <= 0.5 ? 0.0 : -Fix128Vec.Dot(relative, toPort.Normalized()).ToDouble();
    }

    private static double Dot(Fix128Vec a, Fix128Vec b) => Fix128Vec.Dot(a, b).ToDouble();

    private void Square(float x, float y, int half, Color colour)
    {
        _sprites.Draw(_pixel,
            new Rectangle((int)(x - half), (int)(y - half), half * 2, half * 2), colour);
    }

    private void Diamond(float x, float y, int half, Color colour)
    {
        for (int row = -half; row <= half; row++)
        {
            int run = half - Math.Abs(row);
            _sprites.Draw(_pixel,
                new Rectangle((int)(x - run), (int)(y + row), (run * 2) + 1, 1), colour);
        }
    }

    private void Triangle(float x, float y, int size, Color colour)
    {
        for (int row = 0; row < size; row++)
        {
            _sprites.Draw(_pixel,
                new Rectangle((int)(x - row), (int)(y + row), (row * 2) + 1, 1), colour);
        }
    }

    private void Line(float x0, float y0, float x1, float y1, Color colour)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        int steps = (int)MathF.Max(MathF.Abs(dx), MathF.Abs(dy));

        if (steps <= 0)
        {
            return;
        }

        for (int i = 0; i <= steps; i += 2)
        {
            float t = i / (float)steps;
            _sprites.Draw(_pixel,
                new Rectangle((int)(x0 + (dx * t)), (int)(y0 + (dy * t)), 1, 1), colour);
        }
    }
}
