using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Speech;

namespace SolSystem.Client;

/// <summary>
/// What the narrator says, and when.
/// </summary>
/// <remarks>
/// <para>
/// The lines are authored; the triggers are read from the simulation. The opening is the
/// setting in one breath, spoken over the first frame of flight; the arrival is said once,
/// when the port first becomes close enough to judge; and the corridor callouts are
/// computed — distance and closing rate from the flight itself, spelled by
/// <see cref="NumberWords"/> — spoken once as the ship crosses into each band of the
/// approach ladder, the way a docking controller calls a shuttle in.
/// </para>
/// </remarks>
internal static class Narration
{
    internal const int SampleRate = 48000;
    internal const int Seed = 1234;

    /// <summary>The narrator's own recording, cloned by default when present.</summary>
    internal static readonly string DefaultPromptPath = Path.Combine(
        FlightSession.RepositoryRoot(), "art", "audio", "narration", "prompt.wav");

    /// <summary>
    /// Buffers of audio seated before the speaker starts, each one generated frame
    /// (3 840 samples = 0.08 s). Two seconds of lead, so the decode's own pace is never
    /// what the ear hears.
    /// </summary>
    internal const int LeadBuffers = 24;

    internal const string Intro =
        "Earth is dying. Two inheritors argue over what she left behind. "
        + "This is not about who is right. It is about who gets to breathe.";

    internal const string Arrival =
        "Meridian. Two kilometres of wheel, turning once a minute at the rim. "
        + "Two hundred and thirty thousand berths, forty thousand souls. "
        + "The gap between those numbers is the whole story of this place.";

    /// <summary>The corridor bands, from the far end in: first crossing downward is said once.</summary>
    private static readonly double[] Bands = [2000.0, 1000.0, 500.0, 200.0, 100.0, 50.0, 20.0, 10.0];

    internal static List<string?> Update(NarrationState state, Flight flight, FlightSession session)
    {
        var lines = new List<string?>();

        // The arrival, once: the port plane under two kilometres and the ship inbound.
        double range = (flight.Ship.Position - session.Station.Port.Position).Length.ToDouble();
        if (!state.ArrivalSaid && range < 2_000.0 && range > 1.0)
        {
            state.ArrivalSaid = true;
            lines.Add(Arrival);
        }

        // The corridor callouts are said on CROSSING: the range was outside a band and is
        // inside it. A ship that spawns inside a band gets no retroactive callout.
        double closing = Fix128Vec.Dot(
            flight.Ship.Velocity,
            (session.Station.Port.Position - flight.Ship.Position).Normalized()).ToDouble();

        if (state.PreviousRange is double previous)
        {
            foreach (double band in Bands)
            {
                if (previous >= band && range < band)
                {
                    lines.Add(
                        $"final approach. {NumberWords.Metres(band)} out, "
                        + $"closing {NumberWords.Rate(closing)} metres a second.");
                }
            }
        }

        state.PreviousRange = range;
        return lines;
    }
}

/// <summary>The flags the narration latches as it is said.</summary>
internal sealed class NarrationState
{
    internal bool ArrivalSaid;
    internal double? PreviousRange;
}
