using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>The progression haze's strength (SPEC005 5.4, AC-412).</summary>
public sealed class CrestProgressEffectTests
{
    [Fact]
    public void NothingIsDrawnBeforeTheFirstStock()
    {
        Assert.Equal(0f, CrestProgressEffect.Intensity(0, 4, false, 0.2f), 4);
    }

    [Fact]
    public void IntensityRisesWithTheStage()
    {
        float first = CrestProgressEffect.Intensity(1, 4, false, 0.2f);
        float second = CrestProgressEffect.Intensity(2, 4, false, 0.2f);
        float last = CrestProgressEffect.Intensity(3, 4, false, 0.2f);

        Assert.True(first > 0f);
        Assert.True(second > first);
        Assert.True(last > second);
    }

    /// <summary>
    /// AC-412: sublimation is the strongest, and strictly so.
    ///
    /// The regression this locks: multiplying and then clamping saturates. With six stocks and 0.2
    /// a stage, the fifth stock and the sublimation both came out at 1.0 — the step that cannot be
    /// undone looked exactly like the last warning before it.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void SublimationIsAlwaysStrictlyTheStrongest(int maxLevel)
    {
        float lastCurable = CrestProgressEffect.Intensity(maxLevel - 1, maxLevel, false, 0.2f);
        float sublimated = CrestProgressEffect.Intensity(maxLevel, maxLevel, true, 0.2f);

        Assert.True(
            sublimated > lastCurable,
            $"maxLevel {maxLevel}: sublimation {sublimated} should exceed {lastCurable}");
    }

    /// <summary>Sublimation is the top stage even if the status has momentarily gone.</summary>
    [Fact]
    public void SublimationUsesTheTopStageWhateverTheLevelSays()
    {
        Assert.Equal(
            CrestProgressEffect.Intensity(4, 4, true, 0.2f),
            CrestProgressEffect.Intensity(0, 4, true, 0.2f),
            4);
    }

    [Fact]
    public void IntensityNeverExceedsOne()
    {
        Assert.Equal(1f, CrestProgressEffect.Intensity(4, 4, true, 5f), 4);
    }

    [Fact]
    public void ZeroPerStageDrawsNothing()
    {
        Assert.Equal(0f, CrestProgressEffect.Intensity(4, 4, true, 0f), 4);
    }
}
