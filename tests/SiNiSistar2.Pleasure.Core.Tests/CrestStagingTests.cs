using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The staged corruption coefficient (SPEC005 5.5, AC-416, AC-417, AC-418).
///
/// The point under test is not the arithmetic but the shape: flat below the curse, rising through
/// it, and discontinuous at the step that cannot be undone.
/// </summary>
public sealed class CrestStagingTests
{
    private const float CurseMax = 0.30f;
    private const float CrestScale = 2.0f;

    /// <summary>AC-417: the worked example from 5.5.2, with the game reporting four stocks.</summary>
    [Theory]
    [InlineData(0, false, 1.00f)]
    [InlineData(1, false, 1.10f)]
    [InlineData(2, false, 1.20f)]
    [InlineData(3, false, 1.30f)]
    [InlineData(4, true, 2.00f)]
    public void StagesFollowTheWorkedExample(int stock, bool sublimated, float expected)
    {
        float actual = CrestStaging.Coefficient(stock, 4, sublimated, CurseMax, CrestScale);

        Assert.Equal(expected, actual, 3);
    }

    /// <summary>
    /// AC-416: the ordering is the requirement. Whatever the numbers are tuned to, more of the
    /// curse must never be cheaper than less of it, and the mark must cost more than all of it.
    /// </summary>
    [Fact]
    public void CoefficientRisesWithTheStageAndJumpsAtSublimation()
    {
        float none = CrestStaging.Coefficient(0, 4, false, CurseMax, CrestScale);
        float first = CrestStaging.Coefficient(1, 4, false, CurseMax, CrestScale);
        float last = CrestStaging.Coefficient(3, 4, false, CurseMax, CrestScale);
        float mark = CrestStaging.Coefficient(4, 4, true, CurseMax, CrestScale);

        Assert.Equal(1f, none, 3);
        Assert.True(first > none);
        Assert.True(last > first);
        Assert.True(mark > last);
    }

    /// <summary>
    /// Sublimation replaces the stock term rather than adding to it. Adding would leave the mark
    /// one continuous step above the last curse stock, which is the cliff the staging exists to
    /// create being flattened away (5.5.1).
    /// </summary>
    [Fact]
    public void SublimationReplacesTheStockTermRatherThanAddingToIt()
    {
        float mark = CrestStaging.Coefficient(4, 4, true, CurseMax, CrestScale);

        Assert.Equal(CrestScale, mark, 3);
        Assert.NotEqual(CrestScale + CurseMax, mark, 3);
    }

    /// <summary>
    /// Sublimation holds even if the status has momentarily gone. It is a fact about the run, not
    /// about what is on the body this frame — the observer puts the status back (SPEC003 FR-273).
    /// </summary>
    [Fact]
    public void SublimationHoldsWhileTheStatusIsMissing()
    {
        float actual = CrestStaging.Coefficient(0, 4, true, CurseMax, CrestScale);

        Assert.Equal(CrestScale, actual, 3);
    }

    /// <summary>
    /// The curse's ceiling is expressed as a fraction of the way to the cliff, so a build that
    /// reports a different level count does not quietly move it (FR-421).
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void TheLastReversibleStockAlwaysReachesTheSameCeiling(int maxLevel)
    {
        float last = CrestStaging.Coefficient(maxLevel - 1, maxLevel, false, CurseMax, CrestScale);

        Assert.Equal(1f + CurseMax, last, 3);
    }

    /// <summary>A ceiling of one leaves no reversible stage to grade.</summary>
    [Fact]
    public void NoReversibleStageWhenTheStatusHasOnlyOneLevel()
    {
        Assert.Equal(1f, CrestStaging.Coefficient(1, 1, false, CurseMax, CrestScale), 3);
        Assert.Equal(CrestScale, CrestStaging.Coefficient(1, 1, true, CurseMax, CrestScale), 3);
    }

    /// <summary>
    /// The shipped default: no curse acceleration at all, and sublimation keeping the multiplier
    /// SPEC003 already applied. The curse stages become cheaper than they were, which is the
    /// intended direction — a warning the player can still act on (FR-415).
    /// </summary>
    [Fact]
    public void ShippedDefaultLeavesTheCurseStagesInert()
    {
        Assert.Equal(1f, CrestStaging.Coefficient(2, 4, false, 0f, CrestScale), 3);
        Assert.Equal(CrestScale, CrestStaging.Coefficient(4, 4, true, 0f, CrestScale), 3);
    }

    /// <summary>A scale below 1 would make the mark a blessing; it is never allowed to be one.</summary>
    [Fact]
    public void SublimationIsNeverAReduction()
    {
        Assert.Equal(1f, CrestStaging.Coefficient(4, 4, true, 0f, 0.5f), 3);
    }
}
