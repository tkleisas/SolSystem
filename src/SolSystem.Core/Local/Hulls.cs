using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// The hulls that exist, until content is files.
/// </summary>
/// <remarks>
/// The design's intent is hulls as validated data files; until that loader exists, hull
/// definitions live here — named, documented and testable — rather than scattered through the
/// client. A hull is a spec sheet: masses, engine, and what a fresh hull starts with. Control
/// feel — throttle rates, turn commands — is not a hull property and does not live here; it is
/// the client's, because it is about the pilot's hands and not about the ship.
/// </remarks>
internal static class Hulls
{
    /// <summary>
    /// The crewed courier the client flies, sized to the hull that was modelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hundred tonnes dry with forty in the tanks, on the crewed engine. The tanks are
    /// <b>not</b> full: a hull that starts full has no reason to think about propellant until
    /// the moment it runs out, and the whole point of the resource is that it is visible from
    /// the first frame.
    /// </para>
    /// <para>
    /// The thrust is chosen so that the acceleration is the crewed steady figure of four
    /// milligee, which is what the radiator can reject rather than what the engine could
    /// produce. A hundred and forty tonnes at four milligee is 5.5 kilonewtons, and the engine
    /// is quoted at the thrust that delivers it.
    /// </para>
    /// </remarks>
    internal const double DryMassTonnes = 100.0;
    internal const double FreshPropellantTonnes = 40.0;

    internal static Ship Courier(Fix128Vec position, Fix128Vec velocity, Attitude attitude)
    {
        var engine = Engine.Crewed(
            Fix128.FromDouble(5.5),
            Engine.CrewedSpecificImpulse);

        return new Ship(
            position,
            velocity,
            Fix128.FromDouble(DryMassTonnes),
            Fix128.FromDouble(FreshPropellantTonnes),
            engine,
            attitude);
    }
}
