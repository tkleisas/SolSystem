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
        double payout)
    {
        Name = name;
        Cargo = cargo;
        Destination = destination;
        DeadlineJulianDate = deadlineJulianDate;
        Payout = payout;
    }

    internal string Name { get; }

    internal string Cargo { get; }

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
