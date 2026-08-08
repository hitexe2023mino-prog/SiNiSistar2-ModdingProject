namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// <c>BreastSuper</c> is a penalty, not a permanent state. Without a way back, reaching it in a
/// fight turns one bad moment into a detour to find the cure (SPEC003 5.8, FR-254).
/// </summary>
public sealed class BreastSuperTimerTests
{
    /// <summary>FR-233: no duration means it never subsides, which is the shipped state.</summary>
    [Fact]
    public void ADurationOfZeroNeverExpires()
    {
        var timer = new BreastSuperTimer(0d, 0d);
        timer.Start();

        Assert.False(timer.HasDuration);
        Assert.False(timer.Tick(1000d));
    }

    [Fact]
    public void ItExpiresOnceTheDurationHasPassed()
    {
        var timer = new BreastSuperTimer(3d, 3d);
        timer.Start();

        Assert.False(timer.Tick(1d));
        Assert.False(timer.Tick(1d));
        Assert.True(timer.Tick(1d));
    }

    /// <summary>
    /// Exactly once. The caller acts on the return value, and a timer that kept saying "expired"
    /// would take the status away again every frame.
    /// </summary>
    [Fact]
    public void ItExpiresOnlyOnce()
    {
        var timer = new BreastSuperTimer(1d, 1d);
        timer.Start();

        Assert.True(timer.Tick(2d));
        Assert.False(timer.Tick(2d));
    }

    /// <summary>
    /// Starting again while it runs must not reset it. The observer calls Start on every frame the
    /// status is present, so a restart there would mean it never expired at all.
    /// </summary>
    [Fact]
    public void StartingWhileRunningDoesNotRestartIt()
    {
        var timer = new BreastSuperTimer(2d, 2d);
        timer.Start();
        timer.Tick(1.5d);

        timer.Start();

        Assert.True(timer.Tick(0.5d));
    }

    [Fact]
    public void StoppingClearsTheProgress()
    {
        var timer = new BreastSuperTimer(2d, 2d);
        timer.Start();
        timer.Tick(1.9d);

        timer.Stop();
        timer.Start();

        Assert.False(timer.Tick(1.9d));
        Assert.True(timer.Tick(0.2d));
    }

    [Fact]
    public void NothingAdvancesBeforeItIsStarted()
    {
        var timer = new BreastSuperTimer(1d, 1d);

        Assert.False(timer.Tick(5d));
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void RemainingCountsDown()
    {
        var timer = new BreastSuperTimer(10d, 10d);
        timer.Start();
        timer.Tick(4d);

        Assert.Equal(6d, timer.Remaining, 5);
    }

    /// <summary>
    /// FR-259: the span is drawn from the range, not fixed. A player who can count the wait off is
    /// not enduring it, they are waiting out a cooldown.
    /// </summary>
    [Fact]
    public void TheSpanIsDrawnFromTheRange()
    {
        var timer = new BreastSuperTimer(30d, 60d, () => 0.5d);
        timer.Start();

        Assert.Equal(45d, timer.Target, 5);
        Assert.False(timer.Tick(44d));
        Assert.True(timer.Tick(1d));
    }

    /// <summary>Each escalation draws its own span, so the previous one cannot be relied on.</summary>
    [Fact]
    public void EachRunDrawsAgain()
    {
        var samples = new Queue<double>(new[] { 0d, 1d });
        var timer = new BreastSuperTimer(30d, 60d, samples.Dequeue);

        timer.Start();
        Assert.Equal(30d, timer.Target, 5);
        Assert.True(timer.Tick(30d));

        timer.Start();
        Assert.Equal(60d, timer.Target, 5);
        Assert.False(timer.Tick(59d));
        Assert.True(timer.Tick(1d));
    }

    /// <summary>
    /// A sample outside 0..1 is clamped rather than trusted. The function is supplied from outside,
    /// and a value of 2 would silently double the longest wait the config allows.
    /// </summary>
    [Fact]
    public void ASampleOutsideTheRangeIsClamped()
    {
        var timer = new BreastSuperTimer(30d, 60d, () => 4d);
        timer.Start();

        Assert.Equal(60d, timer.Target, 5);
    }

    /// <summary>A maximum below the minimum is raised to it rather than inverting the range.</summary>
    [Fact]
    public void AMaximumBelowTheMinimumIsRaisedToIt()
    {
        var timer = new BreastSuperTimer(45d, 10d, () => 1d);
        timer.Start();

        Assert.Equal(45d, timer.Target, 5);
    }
}
