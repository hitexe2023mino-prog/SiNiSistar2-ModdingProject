namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// The nullification window is the mechanism that makes escape harder without touching any of the
/// numbers defilement already escalates, so when it opens and closes is the behaviour that has to
/// hold (SPEC002 5.3, FR-111, DEC-102).
/// </summary>
public sealed class NullificationSchedulerTests
{
    /// <summary>
    /// AC-111: a hold starts responsive. The player has to see the gauge answer input before it
    /// stops answering, otherwise a window is indistinguishable from a broken control.
    /// </summary>
    [Fact]
    public void AHoldBeginsWithInputStillReachingTheGauge()
    {
        var scheduler = new NullificationScheduler(TestSupport.Pleasure(), new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 0);

        Assert.False(scheduler.IsNullifying);
        Assert.False(scheduler.Update(0d, 0));
        Assert.False(scheduler.Update(1.9d, 0));
    }

    /// <summary>AC-111: the window opens after the gap and closes after its duration.</summary>
    [Fact]
    public void TheWindowOpensAfterTheGapAndClosesAfterItsDuration()
    {
        var scheduler = new NullificationScheduler(
            TestSupport.Pleasure(interval: 2f, duration: 1f),
            new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 0);

        Assert.False(scheduler.Update(1.99d, 0));
        Assert.True(scheduler.Update(2d, 0));
        Assert.True(scheduler.Update(2.99d, 0));
        Assert.False(scheduler.Update(3d, 0));

        // And it comes back: the bands repeat for as long as the hold lasts.
        Assert.False(scheduler.Update(4.99d, 0));
        Assert.True(scheduler.Update(5d, 0));
    }

    /// <summary>
    /// A frame that lands far past a boundary must not leave the schedule a band behind for the
    /// rest of the hold. Two full cycles elapse here, so the state has to be the same as if every
    /// frame had been observed.
    /// </summary>
    [Fact]
    public void ALongFrameCatchesUpInsteadOfDriftingByOneBand()
    {
        var scheduler = new NullificationScheduler(
            TestSupport.Pleasure(interval: 2f, duration: 1f),
            new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 0);

        // 0-2 gap, 2-3 open, 3-5 gap, 5-6 open. t=5.5 is inside the second window.
        Assert.True(scheduler.Update(5.5d, 0));
        Assert.Equal(6d, scheduler.ChangeAt, 6);
    }

    /// <summary>SPEC002 5.3: the schedule is discarded with the hold, not resumed mid-window.</summary>
    [Fact]
    public void EndingTheHoldDiscardsTheSchedule()
    {
        var scheduler = new NullificationScheduler(
            TestSupport.Pleasure(interval: 2f, duration: 1f),
            new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 0);
        Assert.True(scheduler.Update(2d, 0));

        scheduler.EndHold();
        Assert.False(scheduler.IsNullifying);
        Assert.False(scheduler.Update(2.5d, 0));

        // The next hold starts from a fresh gap rather than resuming the open window.
        scheduler.BeginHold(10d, 0);
        Assert.False(scheduler.Update(10.5d, 0));
        Assert.True(scheduler.Update(12d, 0));
    }

    /// <summary>FR-111: with no pleasure status configured the window can never open.</summary>
    [Fact]
    public void ADisabledTuningNeverNullifies()
    {
        var scheduler = new NullificationScheduler(PleasureTuning.Disabled, new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 0);

        Assert.False(scheduler.Update(1000d, 5));
        Assert.False(scheduler.IsNullifying);
    }

    /// <summary>A zero-length window is inert, which is the shipped default (FR-128).</summary>
    [Fact]
    public void AZeroLengthWindowIsInert()
    {
        var scheduler = new NullificationScheduler(
            TestSupport.Pleasure(interval: 2f, duration: 0f),
            new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 0);

        Assert.False(scheduler.Update(100d, 0));
    }

    /// <summary>
    /// SPEC002 5.3: a higher summed pleasure level shortens the gap and lengthens the window.
    /// Scaling 0.5 at level sum 2 halves a 2s gap and doubles a 1s window.
    /// </summary>
    [Fact]
    public void ALargerPleasureLevelSumShortensTheGapAndLengthensTheWindow()
    {
        var scheduler = new NullificationScheduler(
            TestSupport.Pleasure(interval: 2f, duration: 1f, levelScaling: 0.5f),
            new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 2);

        Assert.True(scheduler.Update(1d, 2));
        Assert.Equal(3d, scheduler.ChangeAt, 6);
        Assert.True(scheduler.Update(2.99d, 2));
        Assert.False(scheduler.Update(3d, 2));
    }

    /// <summary>Scaling of zero is the default and must leave timings exactly alone.</summary>
    [Fact]
    public void ZeroLevelScalingIgnoresTheLevelSum()
    {
        var scheduler = new NullificationScheduler(
            TestSupport.Pleasure(interval: 2f, duration: 1f),
            new FakeRandom(0.5d));
        scheduler.BeginHold(0d, 9);

        Assert.False(scheduler.Update(1.99d, 9));
        Assert.True(scheduler.Update(2d, 9));
    }

    /// <summary>
    /// Jitter has to actually move the boundary, otherwise the windows are perfectly predictable
    /// and can be waited out mechanically.
    /// </summary>
    [Fact]
    public void JitterMovesTheBoundary()
    {
        var tuning = new PleasureTuning(
            true,
            AbnormalTypeSet.Parse("Lustfull", TestSupport.KnownNames).Set,
            IntervalSeconds: 2f,
            IntervalJitter: 0.5f,
            DurationSeconds: 1f,
            DurationJitter: 0f,
            LevelScaling: 0f);

        // 0.0 gives the low end of the jitter range: 2 * (1 - 0.5) = 1.
        var low = new NullificationScheduler(tuning, new FakeRandom(0d));
        low.BeginHold(0d, 0);
        Assert.Equal(1d, low.ChangeAt, 6);

        // 1.0 gives the high end: 2 * (1 + 0.5) = 3.
        var high = new NullificationScheduler(tuning, new FakeRandom(1d));
        high.BeginHold(0d, 0);
        Assert.Equal(3d, high.ChangeAt, 6);
    }
}
