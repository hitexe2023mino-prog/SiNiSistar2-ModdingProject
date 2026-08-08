namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// Milking is a cure that takes time and can be lost. The duration is the mechanism: an instant
/// cure on a key is a menu, one that can be interrupted is a decision about whether it is safe
/// enough right now (SPEC003 5.8, FR-257).
/// </summary>
public sealed class MilkingChannelTests
{
    [Fact]
    public void ADurationOfZeroCannotBeStarted()
    {
        var channel = new MilkingChannel(0d);

        Assert.False(channel.IsEnabled);
        Assert.False(channel.TryStart());
        Assert.False(channel.IsRunning);
    }

    [Fact]
    public void ItCompletesOnceTheDurationHasPassed()
    {
        var channel = new MilkingChannel(4d);
        Assert.True(channel.TryStart());

        Assert.Equal(MilkingOutcome.Running, channel.Tick(2d));
        Assert.Equal(MilkingOutcome.Running, channel.Tick(1d));
        Assert.Equal(MilkingOutcome.Completed, channel.Tick(1d));
    }

    /// <summary>Being hit wastes the attempt outright; it does not pause and resume.</summary>
    [Fact]
    public void AnInterruptionThrowsAwayTheProgress()
    {
        var channel = new MilkingChannel(4d);
        channel.TryStart();
        channel.Tick(3.9d);

        Assert.True(channel.Interrupt());
        Assert.False(channel.IsRunning);

        channel.TryStart();
        Assert.Equal(MilkingOutcome.Running, channel.Tick(3.9d));
    }

    /// <summary>
    /// Interrupting nothing reports nothing. The caller tells the player their attempt was wasted,
    /// and saying so when they were not milking would be a lie in a message.
    /// </summary>
    [Fact]
    public void InterruptingWhenIdleReportsThatNothingWasRunning()
    {
        Assert.False(new MilkingChannel(4d).Interrupt());
    }

    [Fact]
    public void StartingTwiceDoesNotRestartIt()
    {
        var channel = new MilkingChannel(2d);
        channel.TryStart();
        channel.Tick(1.5d);

        Assert.False(channel.TryStart());
        Assert.Equal(MilkingOutcome.Completed, channel.Tick(0.5d));
    }

    [Fact]
    public void ProgressReportsHowFarAlongItIs()
    {
        var channel = new MilkingChannel(4d);
        channel.TryStart();
        channel.Tick(1d);

        Assert.Equal(0.25f, channel.Progress, 3);
    }

    [Fact]
    public void NothingAdvancesBeforeItIsStarted()
    {
        var channel = new MilkingChannel(1d);

        Assert.Equal(MilkingOutcome.Running, channel.Tick(5d));
        Assert.False(channel.IsRunning);
    }
}
