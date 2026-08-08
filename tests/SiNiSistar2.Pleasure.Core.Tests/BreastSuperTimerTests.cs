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
        var timer = new BreastSuperTimer(0d);
        timer.Start();

        Assert.False(timer.HasDuration);
        Assert.False(timer.Tick(1000d));
    }

    [Fact]
    public void ItExpiresOnceTheDurationHasPassed()
    {
        var timer = new BreastSuperTimer(3d);
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
        var timer = new BreastSuperTimer(1d);
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
        var timer = new BreastSuperTimer(2d);
        timer.Start();
        timer.Tick(1.5d);

        timer.Start();

        Assert.True(timer.Tick(0.5d));
    }

    [Fact]
    public void StoppingClearsTheProgress()
    {
        var timer = new BreastSuperTimer(2d);
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
        var timer = new BreastSuperTimer(1d);

        Assert.False(timer.Tick(5d));
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void RemainingCountsDown()
    {
        var timer = new BreastSuperTimer(10d);
        timer.Start();
        timer.Tick(4d);

        Assert.Equal(6d, timer.Remaining, 5);
    }
}
