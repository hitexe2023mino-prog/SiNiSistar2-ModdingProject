namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// A runtime patch that leaves something registered produces behaviour with no visible cause in
/// the next scene or the next launch, so "the ledger is empty" has to be a reliable statement
/// (SPEC002 FR-124, DEC-110).
/// </summary>
public sealed class InterventionLedgerTests
{
    /// <summary>AC-123: unloading undoes everything and empties the ledger.</summary>
    [Fact]
    public void ReleaseAllUndoesEveryOpenInterventionAndEmptiesTheLedger()
    {
        var undone = new List<string>();
        var ledger = new InterventionLedger();
        ledger.Register("move-slow", () => undone.Add("move-slow"));
        ledger.Register("invincible", () => undone.Add("invincible"));

        Assert.False(ledger.IsEmpty);
        IReadOnlyList<InterventionFailure> failures = ledger.ReleaseAll();

        Assert.Empty(failures);
        Assert.True(ledger.IsEmpty);
        Assert.Equal(2, undone.Count);
    }

    /// <summary>
    /// AC-123: a release that throws is recorded with its target and reason rather than taking
    /// down the cleanup pass that is trying to back the MOD out.
    /// </summary>
    [Fact]
    public void AFailedReleaseIsRecordedAndTheRestStillRun()
    {
        var undone = new List<string>();
        var ledger = new InterventionLedger();
        ledger.Register("broken", () => throw new InvalidOperationException("destroyed"));
        ledger.Register("healthy", () => undone.Add("healthy"));

        IReadOnlyList<InterventionFailure> failures = ledger.ReleaseAll();

        InterventionFailure failure = Assert.Single(failures);
        Assert.Equal("broken", failure.Key);
        Assert.Contains("destroyed", failure.Reason, StringComparison.Ordinal);

        // The stuck one must not strand the others, and the ledger is empty either way.
        Assert.Contains("healthy", undone);
        Assert.True(ledger.IsEmpty);
    }

    /// <summary>
    /// FR-120: registering the same key twice cannot leave two undo actions behind. The earlier
    /// one is released as part of being superseded.
    /// </summary>
    [Fact]
    public void RegisteringTheSameKeyTwiceReleasesTheEarlierOne()
    {
        var undone = new List<string>();
        var ledger = new InterventionLedger();
        ledger.Register("move-slow", () => undone.Add("first"));
        ledger.Register("move-slow", () => undone.Add("second"));

        Assert.Equal(new[] { "first" }, undone);
        Assert.Single(ledger.OpenKeys);

        ledger.ReleaseAll();
        Assert.Equal(new[] { "first", "second" }, undone);
    }

    [Fact]
    public void ReleasingAKeyThatWasNeverOpenIsNotAnError()
    {
        var ledger = new InterventionLedger();

        Assert.Null(ledger.Release("never-registered"));
        Assert.True(ledger.IsEmpty);
    }

    [Fact]
    public void ReleaseReportsTheFailureWithoutThrowing()
    {
        var ledger = new InterventionLedger();
        ledger.Register("broken", () => throw new InvalidOperationException("gone"));

        InterventionFailure? failure = ledger.Release("broken");

        Assert.NotNull(failure);
        Assert.Equal("broken", failure!.Key);

        // Even a failed release removes the entry: retrying it every frame is worse than losing it.
        Assert.True(ledger.IsEmpty);
    }
}
