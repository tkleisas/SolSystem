using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The first contract: a deadline, a payout, and no way to be paid twice.
/// </summary>
public class ContractTests
{
    private const double Deadline = 2451623.80 + (2.0 / 24.0);

    private static Contract Make() =>
        new("Waystation run", "medical supplies", "Waystation", Deadline, 12_000.0);

    [Fact]
    public void DeliveringBeforeTheDeadlinePaysThePayout()
    {
        Contract contract = Make();

        Assert.True(contract.TryComplete(Deadline - 0.001));
        Assert.True(contract.Completed);
        Assert.False(contract.Failed);
        Assert.Equal(12_000.0, contract.Credits, 3);
    }

    [Fact]
    public void DeliveringAfterTheDeadlineFailsAndPaysNothing()
    {
        Contract contract = Make();

        Assert.False(contract.TryComplete(Deadline + 0.001));
        Assert.False(contract.Completed);
        Assert.True(contract.Failed);
        Assert.Equal(0.0, contract.Credits, 3);
    }

    [Fact]
    public void TheDeadlineExpiresOnce()
    {
        Contract contract = Make();

        Assert.True(contract.CheckDeadline(Deadline + 0.001));
        Assert.False(contract.CheckDeadline(Deadline + 0.002));
        Assert.True(contract.Failed);
    }

    [Fact]
    public void CreditsAreNotPaidTwice()
    {
        Contract contract = Make();

        Assert.True(contract.TryComplete(Deadline - 0.001));
        Assert.False(contract.TryComplete(Deadline - 0.001));
        Assert.Equal(12_000.0, contract.Credits, 3);
    }
}
