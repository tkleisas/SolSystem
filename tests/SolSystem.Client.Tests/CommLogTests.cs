using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The terminal's typing pace, which has to match the voice.
/// </summary>
public class CommLogTests
{
    [Fact]
    public void TypingRateMatchesTheSpokenDuration()
    {
        Assert.Equal(10.0, CommLog.RateFor(new string('x', 100), 10.0), 3);
    }

    [Fact]
    public void ALongLineOverAShortDurationIsClamped()
    {
        // A hundred characters in one second is a hundred a second; the terminal stops at ninety.
        Assert.Equal(90.0, CommLog.RateFor(new string('x', 100), 1.0), 3);
    }

    [Fact]
    public void ASlowLineIsClamped()
    {
        // A hundred characters over a hundred seconds is one a second; the terminal holds four.
        Assert.Equal(4.0, CommLog.RateFor(new string('x', 100), 100.0), 3);
    }

    [Fact]
    public void ANoteWithoutAVoiceUsesTheTerminalSpeed()
    {
        Assert.Equal(30.0, CommLog.RateFor("hello", 0.0), 3);
    }
}
