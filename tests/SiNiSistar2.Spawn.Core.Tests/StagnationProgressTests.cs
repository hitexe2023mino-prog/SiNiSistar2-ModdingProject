using SiNiSistar2.Spawn.Core;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

/// <summary>
/// The HUD's stagnation panel reads these three members, so what it shows has to agree with when
/// the detector actually fires (SPEC004 AC-318).
/// </summary>
public class StagnationProgressTests
{
    private static StagnationDetector NewDetector() =>
        new(stagnationSeconds: 10f, windowSeconds: 4f, moveEpsilon: 3f, penaltyInterval: 5f);

    private static bool Feed(StagnationDetector detector, double from, double to, float x)
    {
        var due = false;
        for (double t = from; t <= to; t += 0.5)
        {
            due |= detector.Sample(t, x, 0f, paused: false);
        }

        return due;
    }

    /// <summary>
    /// AC-318 asks that the HUD's countdown agree with when the spawn actually happens. The
    /// countdown is read before the frame's sample advances the clock, so the last value a player
    /// can see is at most one frame's worth above zero — it never fires early, and never lingers
    /// at zero without firing. Both halves are asserted here.
    /// </summary>
    [Fact]
    public void ThePenaltyFiresOnTheFrameTheDisplayedCountdownRunsOut()
    {
        const double step = 0.5;
        StagnationDetector detector = NewDetector();
        double? beforeFiring = null;

        for (double t = 0; t <= 12; t += step)
        {
            double? shown = detector.SecondsUntilNextPenalty;
            if (detector.Sample(t, 0f, 0f, paused: false))
            {
                beforeFiring = shown;
                break;
            }

            // It must not still be counting down after the fire should have happened.
            Assert.True(shown > 0, $"The countdown read {shown} without a penalty firing.");
        }

        Assert.NotNull(beforeFiring);
        Assert.InRange(beforeFiring!.Value, 0, step);
    }

    [Fact]
    public void FiringRearmsTheCountdownToTheInterval()
    {
        StagnationDetector detector = NewDetector();
        Assert.True(Feed(detector, 0, 10, x: 0f));

        // Read on the same frame as the fire: a full interval is now pending.
        Assert.Equal(5, detector.SecondsUntilNextPenalty!.Value, precision: 3);
    }

    [Fact]
    public void DwellAndTravelTrackTheSamplesTheHudShows()
    {
        StagnationDetector detector = NewDetector();
        Feed(detector, 0, 6, x: 0f);

        Assert.Equal(6, detector.Dwell, precision: 3);
        Assert.Equal(0f, detector.WindowTravel, precision: 3);

        detector.Sample(6.5, 2f, 0f, paused: false);
        Assert.True(detector.WindowTravel > 0f);
    }

    [Fact]
    public void MovingResetsTheCountdownBackToTheFullDwell()
    {
        StagnationDetector detector = NewDetector();
        Assert.True(Feed(detector, 0, 12, x: 0f));
        Assert.True(detector.IsStagnant);

        for (var i = 0; i < 8; i++)
        {
            detector.Sample(12.5 + (i * 0.5), i * 1.0f, 0f, paused: false);
        }

        Assert.False(detector.IsStagnant);
        Assert.Equal(10, detector.SecondsUntilNextPenalty!.Value, precision: 1);
    }

    /// <summary>
    /// SPEC004 5.9: the fast-forward makes the wait short, not the rule loose. The very next
    /// standing-still frame fires, and a frame with movement still refuses to.
    /// </summary>
    [Fact]
    public void FastForwardMakesTheNextStillFrameFire()
    {
        StagnationDetector detector = NewDetector();
        Feed(detector, 0, 2, x: 0f);
        Assert.False(detector.IsStagnant);

        detector.FastForwardToStagnation();

        Assert.True(detector.Sample(2.5, 0f, 0f, paused: false));
        Assert.True(detector.IsStagnant);
    }

    [Fact]
    public void FastForwardDoesNotFireWhileThePlayerIsMoving()
    {
        StagnationDetector detector = NewDetector();
        Feed(detector, 0, 2, x: 0f);
        detector.FastForwardToStagnation();

        // A jump larger than the epsilon is movement, and movement outranks the fabricated dwell.
        Assert.False(detector.Sample(2.5, 10f, 0f, paused: false));
        Assert.False(detector.IsStagnant);
    }

    [Fact]
    public void FastForwardDoesNotFireOnAPausedFrame()
    {
        StagnationDetector detector = NewDetector();
        Feed(detector, 0, 2, x: 0f);
        detector.FastForwardToStagnation();

        Assert.False(detector.Sample(2.5, 0f, 0f, paused: true));
    }

    [Fact]
    public void PausedSamplesDoNotAdvanceTheDwellTheHudReports()
    {
        StagnationDetector detector = NewDetector();
        Feed(detector, 0, 5, x: 0f);
        double before = detector.Dwell;

        for (var i = 0; i < 20; i++)
        {
            detector.Sample(5.5 + i, 0f, 0f, paused: true);
        }

        Assert.Equal(before, detector.Dwell, precision: 3);
    }
}
