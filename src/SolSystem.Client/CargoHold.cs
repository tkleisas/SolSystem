using System;
using System.Collections.Generic;
using System.Linq;

namespace SolSystem.Client;

/// <summary>
/// What the ship is carrying, as lots of tonnes.
/// </summary>
/// <remarks>
/// The contract cargo and the market cargo share one hold, because they share one mass: a tonne of
/// medical supplies costs exactly as much acceleration as a tonne of machine parts, and the ship
/// should not be able to cheat by pretending otherwise. The hold has a capacity; the contract's
/// cargo is loaded at the start and removed on delivery.
/// </remarks>
internal sealed class CargoHold
{
    internal const double CapacityTonnes = 20.0;

    private readonly Dictionary<string, double> _lots = new(StringComparer.OrdinalIgnoreCase);

    internal double MassTonnes => _lots.Values.Sum();

    internal double FreeTonnes => CapacityTonnes - MassTonnes;

    internal IReadOnlyDictionary<string, double> Lots => _lots;

    internal double TonnesOf(string good) =>
        _lots.TryGetValue(good, out double tonnes) ? tonnes : 0.0;

    /// <summary>Loads a lot if it fits. Returns false and changes nothing if it does not.</summary>
    internal bool TryAdd(string good, double tonnes)
    {
        if (tonnes <= 0.0 || MassTonnes + tonnes > CapacityTonnes + 1e-9)
        {
            return false;
        }

        _lots[good] = TonnesOf(good) + tonnes;
        return true;
    }

    /// <summary>Removes a lot if the hold has it. Returns false and changes nothing if not.</summary>
    internal bool TryRemove(string good, double tonnes)
    {
        if (tonnes <= 0.0 || TonnesOf(good) + 1e-9 < tonnes)
        {
            return false;
        }

        double left = TonnesOf(good) - tonnes;
        if (left <= 1e-9)
        {
            _lots.Remove(good);
        }
        else
        {
            _lots[good] = left;
        }

        return true;
    }
}
