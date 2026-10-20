using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// The market, as a panel over the dock: what the station sells, what it buys, and what is aboard.
/// </summary>
/// <remarks>
/// A screen rather than a HUD line because a market is a decision with three numbers in it — price,
/// hold and credits — and the player has to be able to read all three at once. The panel is opened
/// only when the docking law says the ship is at the port; being near a station is not being at one.
/// </remarks>
internal sealed class MarketScreen
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly SpriteFont _font;

    private static readonly Color Panel = new(8, 12, 18, 235);
    private static readonly Color Edge = new(90, 150, 120, 220);
    private static readonly Color Ink = new(170, 225, 190);
    private static readonly Color Dim = new(100, 140, 125);
    private static readonly Color Warm = new(230, 180, 100);
    private static readonly Color Selected = new(30, 60, 48, 220);

    internal MarketScreen(GraphicsDevice device, SpriteBatch sprites, Texture2D pixel, SpriteFont font)
    {
        _device = device;
        _sprites = sprites;
        _pixel = pixel;
        _font = font;
    }

    internal void Draw(Market market, CargoHold hold, Ledger ledger, Flight flight, int selected)
    {
        _sprites.Begin();

        // Dim the flight behind the panel so the market is the thing being read.
        _sprites.Draw(_pixel,
            new Rectangle(0, 0, _device.Viewport.Width, _device.Viewport.Height),
            new Color(0, 0, 0, 150));

        int width = 760;
        int height = 470;
        int x = (_device.Viewport.Width - width) / 2;
        int y = (_device.Viewport.Height - height) / 2;

        _sprites.Draw(_pixel, new Rectangle(x, y, width, height), Panel);
        _sprites.Draw(_pixel, new Rectangle(x, y, width, 1), Edge);
        _sprites.Draw(_pixel, new Rectangle(x, y + height - 1, width, 1), Edge);
        _sprites.Draw(_pixel, new Rectangle(x, y, 1, height), Edge);
        _sprites.Draw(_pixel, new Rectangle(x + width - 1, y, 1, height), Edge);

        var at = new Vector2(x + 28, y + 22);
        _sprites.DrawString(_font, $"MARKET  \u00b7  {market.Station.ToUpperInvariant()}", at, Ink);
        at.Y += FlightUi.Line * 1.5f;

        _sprites.DrawString(_font, "GOOD                 BUY     SELL    ABOARD", at, Dim);
        at.Y += FlightUi.Line;

        for (int i = 0; i < market.Goods.Count; i++)
        {
            MarketGood good = market.Goods[i];
            bool isSelected = i == selected;

            if (isSelected)
            {
                _sprites.Draw(_pixel, new Rectangle(x + 14, (int)at.Y - 3, width - 28, 23),
                    Selected);
            }

            string marker = isSelected ? ">" : " ";
            string name = $"{marker} {good.Name.ToUpperInvariant(),-18}";
            string line = $"{name}{market.BuyPrice(i),8:N0}{market.SellPrice(i),9:N0}"
                + $"{hold.TonnesOf(good.Name),9:F1} t";
            _sprites.DrawString(_font, line, at, isSelected ? Ink : Dim);
            at.Y += FlightUi.Line;
        }

        // The contract cargo is not for sale. It is in the hold because the patron loaded it, and the
        // only way out is delivery or a failed run.
        at.Y += FlightUi.Line * 0.4f;
        _sprites.DrawString(_font,
            $"CONTRACT CARGO  MEDICAL SUPPLIES                {hold.TonnesOf("Medical supplies"),9:F1} t",
            at, Warm);
        at.Y += FlightUi.Line * 1.2f;

        double propellant = flight.Ship.Propellant.ToDouble();
        _sprites.DrawString(_font,
            $"FUEL  {market.FuelPrice,6:N0} /t     TANK {propellant,5:F1} t     MASS {flight.Ship.Mass.ToDouble(),6:F1} t",
            at, Ink);
        at.Y += FlightUi.Line;

        _sprites.DrawString(_font,
            $"CREDITS {ledger.Credits,10:N0}     HOLD {hold.MassTonnes,5:F1} / {CargoHold.CapacityTonnes:F0} t",
            at, Ink);
        at.Y += FlightUi.Line * 1.4f;

        _sprites.DrawString(_font,
            "1 / 2 select    B buy 1 t    S sell 1 t    F load 10 t fuel    T close",
            at, Ink);
        at.Y += FlightUi.Line * 1.2f;

        if (ledger.Entries.Count > 0)
        {
            _sprites.DrawString(_font, ledger.Entries[^1], at, Dim);
        }

        _sprites.End();
    }
}
