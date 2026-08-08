namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The reservoir is the road to the escalation and the way back out of it. It fills from sexual
/// hits taken while swollen, and while the escalation is worn the body works it off by itself
/// (SPEC003 5.8, FR-259, FR-264, FR-262).
/// </summary>
public sealed class MilkReservoirTests
{
    private static MilkReservoir Reservoir(float perHit = 0.25f, float drain = 0.5f) =>
        new(perHit, drain);

    /// <summary>
    /// It does not fill with time. Time would make the escalation something that happens to a
    /// player who put the controller down, which is the opposite of a penalty for what they did.
    /// </summary>
    [Fact]
    public void TimeAloneDoesNotFillIt()
    {
        MilkReservoir milk = Reservoir();

        milk.Tick(1000d);

        Assert.Equal(0f, milk.Fill);
    }

    [Fact]
    public void HitsFillItAndTheLastOneReportsItIsFull()
    {
        MilkReservoir milk = Reservoir(perHit: 0.5f);

        Assert.False(milk.AddFromHit());
        Assert.Equal(0.5f, milk.Fill, 4);
        Assert.True(milk.AddFromHit());
        Assert.True(milk.IsFull);
    }

    /// <summary>Once, so the caller escalates once rather than on every later hit.</summary>
    [Fact]
    public void AFullReservoirReportsItOnlyOnTheHitThatFilledIt()
    {
        MilkReservoir milk = Reservoir(perHit: 1f);

        Assert.True(milk.AddFromHit());
        Assert.False(milk.AddFromHit());
        Assert.Equal(1f, milk.Fill);
    }

    [Fact]
    public void APerHitOfZeroNeverFills()
    {
        MilkReservoir milk = Reservoir(perHit: 0f);

        Assert.False(milk.AddFromHit());
        Assert.Equal(0f, milk.Fill);
    }

    [Fact]
    public void ItDrainsAndReportsWhenItEmpties()
    {
        MilkReservoir milk = Reservoir(drain: 0.5f);
        milk.LoadFrom(1f);

        Assert.Equal(MilkOutcome.None, milk.Tick(1d));
        Assert.Equal(0.5f, milk.Fill, 4);
        Assert.Equal(MilkOutcome.Emptied, milk.Tick(1d));
        Assert.Equal(0f, milk.Fill);
    }

    /// <summary>Once. The caller steps the swelling down on it, and twice would step it down twice.</summary>
    [Fact]
    public void ItReportsEmptyOnlyOnTheTickThatEmptiedIt()
    {
        MilkReservoir milk = Reservoir(drain: 1f);
        milk.LoadFrom(0.5f);

        Assert.Equal(MilkOutcome.Emptied, milk.Tick(1d));
        Assert.Equal(MilkOutcome.None, milk.Tick(1d));
    }

    [Fact]
    public void ADrainOfZeroLeavesNoWayOut()
    {
        MilkReservoir milk = Reservoir(drain: 0f);
        milk.LoadFrom(1f);

        Assert.False(milk.CanDrain);
        Assert.Equal(MilkOutcome.None, milk.Tick(1000d));
        Assert.Equal(1f, milk.Fill);
    }

    /// <summary>
    /// FR-264: hits fill it while the escalation is worn too. That is the penalty — the way out is
    /// the gauge, so being hit while escalated puts the way out further away.
    /// </summary>
    [Fact]
    public void HitsFillItWhileItIsDraining()
    {
        MilkReservoir milk = Reservoir(perHit: 0.25f, drain: 0.5f);
        milk.LoadFrom(1f);
        milk.Tick(1d);
        Assert.Equal(0.5f, milk.Fill, 4);

        milk.AddFromHit();
        Assert.Equal(0.75f, milk.Fill, 4);
    }

    /// <summary>A hit landing on a full gauge cannot push it past full, or the escalation would fire twice.</summary>
    [Fact]
    public void AFullGaugeDoesNotOverfill()
    {
        MilkReservoir milk = Reservoir(perHit: 0.5f);
        milk.LoadFrom(1f);

        Assert.False(milk.AddFromHit());
        Assert.Equal(1f, milk.Fill);
    }

    [Fact]
    public void AStoredFillIsClampedRatherThanTrusted()
    {
        MilkReservoir milk = Reservoir();

        milk.LoadFrom(5f);
        Assert.Equal(1f, milk.Fill);

        milk.LoadFrom(-1f);
        Assert.Equal(0f, milk.Fill);
    }
}
