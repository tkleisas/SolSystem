using System;
using System.Collections.Generic;

namespace SolSystem.Client;

/// <summary>
/// The pilot's account, and the short history that makes it legible.
/// </summary>
/// <remarks>
/// The contract pays a number; without costs the number is not a decision. The ledger holds the
/// credits, the fuel bill and the market trades in one place, and keeps the last few lines so the
/// panel can say why the balance moved rather than only what it is. Fractional credits are rounded
/// for display, never for arithmetic.
/// </remarks>
internal sealed class Ledger
{
    /// <summary>What the pilot starts with. Enough to matter, not enough to buy a full hold.</summary>
    internal const double StartingCredits = 3_000.0;

    private readonly List<string> _entries = new();

    internal Ledger(double credits = StartingCredits)
    {
        Credits = credits;
    }

    internal double Credits { get; private set; }

    /// <summary>The last few movements, newest last. Bounded so a long run cannot grow it forever.</summary>
    internal IReadOnlyList<string> Entries => _entries;

    /// <summary>Deducts a cost. Returns false and changes nothing if the balance cannot cover it.</summary>
    internal bool TrySpend(double amount, string reason)
    {
        if (amount <= 0.0 || Credits + 1e-9 < amount)
        {
            return false;
        }

        Credits -= amount;
        Record($"-{amount,8:N0}  {reason}");
        return true;
    }

    /// <summary>Banks a payment. Non-positive amounts are ignored rather than recorded as noise.</summary>
    internal void Credit(double amount, string reason)
    {
        if (amount <= 0.0)
        {
            return;
        }

        Credits += amount;
        Record($"+{amount,8:N0}  {reason}");
    }

    private void Record(string entry)
    {
        _entries.Add(entry);
        if (_entries.Count > 8)
        {
            _entries.RemoveAt(0);
        }
    }
}
