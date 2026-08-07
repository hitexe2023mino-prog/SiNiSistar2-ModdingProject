namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// The recovery window registers a contribution on the game's own multi-source value, so opening
/// and closing it exactly once is what keeps the player from staying slowed forever
/// (SPEC002 5.4, FR-119, FR-120).
/// </summary>
public sealed class RecoveryPenaltySchedulerTests
{
    /// <summary>AC-118: the window opens on escape and reports that a contribution is needed.</summary>
    [Fact]
    public void EscapingOpensTheWindowOnce()
    {
        var scheduler = new RecoveryPenaltyScheduler(TestSupport.Burden(penalty: 3f));

        Assert.True(scheduler.Begin(0d, 0));
        Assert.True(scheduler.IsActive);
        Assert.Equal(3d, scheduler.EndsAt, 6);
    }

    /// <summary>AC-119: the close edge is reported exactly once, so the release happens once.</summary>
    [Fact]
    public void TheWindowClosesExactlyOnce()
    {
        var scheduler = new RecoveryPenaltyScheduler(TestSupport.Burden(penalty: 3f));
        scheduler.Begin(0d, 0);

        Assert.Equal(RecoveryClose.None, scheduler.Poll(2.99d));
        Assert.Equal(RecoveryClose.Elapsed, scheduler.Poll(3d));

        // Polling again must not ask for a second release of a contribution already gone.
        Assert.Equal(RecoveryClose.None, scheduler.Poll(3.5d));
        Assert.Equal(RecoveryClose.None, scheduler.Poll(100d));
        Assert.False(scheduler.IsActive);
    }

    /// <summary>
    /// AC-120: escaping again while still slowed replaces the window instead of stacking a second
    /// contribution. Begin returns false because one is already registered.
    /// </summary>
    [Fact]
    public void ASecondEscapeReplacesTheWindowWithoutStackingAContribution()
    {
        var scheduler = new RecoveryPenaltyScheduler(TestSupport.Burden(penalty: 3f));

        Assert.True(scheduler.Begin(0d, 0));
        Assert.False(scheduler.Begin(1d, 0));
        Assert.Equal(4d, scheduler.EndsAt, 6);

        // The replaced window governs, so the original end time no longer closes it.
        Assert.Equal(RecoveryClose.None, scheduler.Poll(3d));
        Assert.Equal(RecoveryClose.Elapsed, scheduler.Poll(4d));
    }

    /// <summary>AC-119: being bound again closes the window early, and only if it was open.</summary>
    [Fact]
    public void BeingBoundAgainCancelsTheWindowOnlyWhenItWasOpen()
    {
        var scheduler = new RecoveryPenaltyScheduler(TestSupport.Burden(penalty: 3f));

        Assert.Equal(RecoveryClose.None, scheduler.Cancel());

        scheduler.Begin(0d, 0);
        Assert.Equal(RecoveryClose.Interrupted, scheduler.Cancel());
        Assert.Equal(RecoveryClose.None, scheduler.Cancel());
        Assert.False(scheduler.IsActive);
    }

    /// <summary>SPEC002 5.4: a higher summed burden level lengthens the window.</summary>
    [Fact]
    public void ALargerBurdenLevelSumLengthensTheWindow()
    {
        var scheduler = new RecoveryPenaltyScheduler(
            TestSupport.Burden(penalty: 2f, levelScaling: 0.5f));

        scheduler.Begin(0d, 4);
        Assert.Equal(6d, scheduler.EndsAt, 6);
    }

    /// <summary>A zero-length penalty is inert, which is the shipped default (FR-128).</summary>
    [Fact]
    public void AZeroLengthPenaltyNeverOpens()
    {
        var scheduler = new RecoveryPenaltyScheduler(TestSupport.Burden(penalty: 0f));

        Assert.False(scheduler.Begin(0d, 3));
        Assert.False(scheduler.IsActive);
    }

    /// <summary>FR-118: with no burden status configured the window can never open.</summary>
    [Fact]
    public void ADisabledTuningNeverOpens()
    {
        var scheduler = new RecoveryPenaltyScheduler(BurdenTuning.Disabled);

        Assert.False(scheduler.Begin(0d, 5));
        Assert.Equal(RecoveryClose.None, scheduler.Poll(100d));
    }
}
