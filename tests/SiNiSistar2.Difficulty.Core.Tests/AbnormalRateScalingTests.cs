namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// The rate field's real range is unmeasured (SPEC002 付録A A-4), so the scaling has to be safe
/// against being wrong about it: a bad guess may under-deliver the difficulty increase, but it
/// must never turn it into a decrease (SPEC002 5.2).
/// </summary>
public sealed class AbnormalRateScalingTests
{
    [Fact]
    public void AMultiplierOfOneLeavesTheRateExactlyAlone()
    {
        Assert.Equal(37, AbnormalRateScaling.Apply(37, 1f));
        Assert.Equal(0, AbnormalRateScaling.Apply(0, 1f));
    }

    [Fact]
    public void ARateOfZeroStaysZeroBecauseTheAttackAppliesNoStatus()
    {
        Assert.Equal(0, AbnormalRateScaling.Apply(0, 5f));
    }

    [Fact]
    public void ScalingUpRaisesTheRate()
    {
        Assert.Equal(40, AbnormalRateScaling.Apply(20, 2f));
    }

    /// <summary>
    /// The guard that matters: if the field is scaled to something larger than the assumed 100,
    /// capping at 100 would lower a rate the game had set higher. Scaling up must never return
    /// less than the original.
    /// </summary>
    [Fact]
    public void ScalingUpNeverReturnsLessThanTheGameAlreadySet()
    {
        Assert.Equal(300, AbnormalRateScaling.Apply(300, 1.5f));
        Assert.Equal(500, AbnormalRateScaling.Apply(500, 2f));
    }

    [Fact]
    public void ScalingUpIsHeldAtTheAssumedMaximumForOrdinaryRates()
    {
        Assert.Equal(100, AbnormalRateScaling.Apply(60, 5f));
    }

    /// <summary>A deliberate reduction is honoured; the cap is not allowed to undo it.</summary>
    [Fact]
    public void ScalingDownIsHonoured()
    {
        Assert.Equal(10, AbnormalRateScaling.Apply(20, 0.5f));
        Assert.Equal(0, AbnormalRateScaling.Apply(1, 0.1f));
    }
}
