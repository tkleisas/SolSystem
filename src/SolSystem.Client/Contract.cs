namespace SolSystem.Client;

/// <summary>
/// One cargo run: what is carried, where to, by when, and what it pays.
/// </summary>
/// <remarks>
/// The first game loop. A destination is only a place until there is a reason to reach it, and a
/// deadline is what makes the clock on the panel mean something. The contract is deliberately
/// small: no market, no reputation, no counter-offer — just a target, a date, and a number. The
/// economy in Phase 1 grows out of this, and keeping the first one readable is how the growth stays
/// checkable.
/// </remarks>
internal sealed class Contract
{
    internal Contract(string name, string cargo, string destination, double deadlineJulianDate,
        double payout, double cargoTonnes = 5.0)
    {
        Name = name;
        Cargo = cargo;
        Destination = destination;
        DeadlineJulianDate = deadlineJulianDate;
        Payout = payout;
        CargoTonnes = cargoTonnes;
    }

    internal string Name { get; }

    internal string Cargo { get; }

    /// <summary>Tonnes of <see cref="Cargo"/> loaded at the start and handed over on delivery.</summary>
    internal double CargoTonnes { get; }

    internal string Destination { get; }

    /// <summary>When the delivery stops counting, as a Julian date.</summary>
    internal double DeadlineJulianDate { get; }

    /// <summary>Credits paid on delivery.</summary>
    internal double Payout { get; }

    internal bool Completed { get; private set; }

    internal bool Failed { get; private set; }

    /// <summary>Credits banked. Starts at zero; a failed contract pays nothing.</summary>
    internal double Credits { get; private set; }

    /// <summary>Seconds until the deadline; negative once it has passed.</summary>
    internal double SecondsRemaining(double now) => (DeadlineJulianDate - now) * 86400.0;

    /// <summary>
    /// Records a delivery. Returns true only the first time, and only before the deadline.
    /// </summary>
    internal bool TryComplete(double now)
    {
        if (Completed || Failed)
        {
            return false;
        }

        if (now > DeadlineJulianDate)
        {
            Failed = true;
            return false;
        }

        Completed = true;
        Credits += Payout;
        return true;
    }

    /// <summary>
    /// Marks the contract failed if the deadline has passed. Returns true on the transition.
    /// </summary>
    internal bool CheckDeadline(double now)
    {
        if (Completed || Failed || now <= DeadlineJulianDate)
        {
            return false;
        }

        Failed = true;
        return true;
    }

    private bool _settled;

    /// <summary>
    /// Settles a completed delivery: hands over the cargo, banks the payout and charges the fuel.
    /// </summary>
    /// <remarks>
    /// The fuel is charged at the price the pilot filled up at, which is the home station's; the
    /// contract itself does not know prices, so the caller passes the rate. Returns false if the
    /// contract is not completed or has already been settled, and changes nothing in that case.
    /// </remarks>
    internal bool SettleDelivery(Ledger ledger, CargoHold hold, double fuelUsedTonnes,
        double fuelPricePerTonne)
    {
        if (!Completed || _settled)
        {
            return false;
        }

        _settled = true;
        hold.TryRemove(Cargo, CargoTonnes);

        double fuelCost = Math.Max(0.0, fuelUsedTonnes) * fuelPricePerTonne;
        ledger.Credit(Payout, $"{Name} delivered");
        ledger.TrySpend(fuelCost, $"fuel used {fuelUsedTonnes:F2} t");
        return true;
    }

    /// <summary>Settles an expired contract: charges the fuel it burned and leaves the cargo aboard.</summary>
    internal bool SettleFailure(Ledger ledger, double fuelUsedTonnes, double fuelPricePerTonne)
    {
        if (!Failed || _settled)
        {
            return false;
        }

        _settled = true;
        double fuelCost = Math.Max(0.0, fuelUsedTonnes) * fuelPricePerTonne;
        ledger.TrySpend(fuelCost, $"fuel used {fuelUsedTonnes:F2} t");
        return true;
    }

    /// <summary>The state the panel prints.</summary>
    internal string Status(double now)
    {
        if (Completed)
        {
            return "DELIVERED";
        }

        if (Failed)
        {
            return "EXPIRED";
        }

        double seconds = Math.Max(0.0, SecondsRemaining(now));
        int hours = (int)(seconds / 3600.0);
        int minutes = (int)((seconds - (hours * 3600.0)) / 60.0);
        return $"{hours}h {minutes:00}m";
    }
}
