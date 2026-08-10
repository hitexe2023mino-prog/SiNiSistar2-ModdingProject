using SiNiSistar2.Spawn.Core;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

public class StagnationDetectorTests
{
    private static StagnationDetector NewDetector() =>
        new(stagnationSeconds: 10f, windowSeconds: 4f, moveEpsilon: 3f, penaltyInterval: 5f);

    private static bool Feed(StagnationDetector detector, double from, double to, float x, bool paused = false)
    {
        var due = false;
        for (double t = from; t <= to; t += 0.5)
        {
            due |= detector.Sample(t, x, 0f, paused);
        }

        return due;
    }

    [Fact]
    public void NoPenaltyBeforeDwellTime()
    {
        StagnationDetector detector = NewDetector();
        Assert.False(Feed(detector, 0, 9.5, x: 0f));
    }

    [Fact]
    public void PenaltyFiresAfterDwellWithoutMovement()
    {
        StagnationDetector detector = NewDetector();
        Assert.True(Feed(detector, 0, 12, x: 0f));
    }

    [Fact]
    public void MovementResetsDwellAndStopsPenalties()
    {
        StagnationDetector detector = NewDetector();
        Assert.True(Feed(detector, 0, 12, x: 0f));

        // A burst of movement larger than the epsilon ends stagnation.
        var moved = false;
        for (var i = 0; i < 8; i++)
        {
            moved |= detector.Sample(12.5 + (i * 0.5), i * 1.0f, 0f, paused: false);
        }

        Assert.False(moved);
        Assert.False(detector.IsStagnant);

        // Standing still again needs the full dwell time before the next penalty.
        Assert.False(Feed(detector, 17, 24, x: 8f));
        Assert.True(Feed(detector, 24, 32, x: 8f));
    }

    [Fact]
    public void PenaltiesRepeatOnInterval()
    {
        StagnationDetector detector = NewDetector();
        Assert.True(Feed(detector, 0, 12, x: 0f)); // first penalty at clock 10
        Assert.False(Feed(detector, 12.5, 14.5, x: 0f)); // interval of 5 not yet elapsed
        Assert.True(Feed(detector, 15, 18, x: 0f)); // due again from clock 15
    }

    [Fact]
    public void PausedFramesFreezeTheClock()
    {
        StagnationDetector detector = NewDetector();
        Assert.False(Feed(detector, 0, 8, x: 0f));

        // 100 paused seconds must not advance the stagnation clock.
        Assert.False(Feed(detector, 8, 108, x: 0f, paused: true));
        Assert.False(Feed(detector, 108, 109, x: 0f));
        Assert.True(Feed(detector, 109, 112, x: 0f));
    }

    [Fact]
    public void ResetStartsOver()
    {
        StagnationDetector detector = NewDetector();
        Assert.True(Feed(detector, 0, 12, x: 0f));
        detector.Reset();
        Assert.False(detector.IsStagnant);
        Assert.False(Feed(detector, 20, 29, x: 0f));
    }
}
