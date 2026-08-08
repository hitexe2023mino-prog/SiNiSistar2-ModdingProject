namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The reservoir is the road to the escalation and the way back out of it. It fills one way from
/// sexual hits taken while swollen, and only milking the escalated swelling takes anything out
/// (SPEC003 5.8, FR-259, FR-262).
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
    public void MilkingDrainsItAndReportsWhenItEmpties()
    {
        MilkReservoir milk = Reservoir(drain: 0.5f);
        milk.LoadFrom(1f);
        Assert.True(milk.TryStartMilking());

        Assert.Equal(MilkOutcome.None, milk.Tick(1d));
        Assert.Equal(0.5f, milk.Fill, 4);
        Assert.Equal(MilkOutcome.Emptied, milk.Tick(1d));
        Assert.Equal(0f, milk.Fill);
        Assert.False(milk.IsMilking);
    }

    [Fact]
    public void AnEmptyReservoirCannotBeMilked()
    {
        Assert.False(Reservoir().TryStartMilking());
    }

    [Fact]
    public void ADrainOfZeroCannotMilk()
    {
        MilkReservoir milk = Reservoir(drain: 0f);
        milk.LoadFrom(1f);

        Assert.False(milk.CanMilk);
        Assert.False(milk.TryStartMilking());
    }

    /// <summary>
    /// An interruption keeps what is left, and the gauge fills again from there. Being hit costs
    /// the attempt, not the reservoir; the redo is the cost.
    /// </summary>
    [Fact]
    public void StoppingKeepsWhatIsLeftAndLetsItFillAgain()
    {
        MilkReservoir milk = Reservoir(perHit: 0.25f, drain: 0.5f);
        milk.LoadFrom(1f);
        milk.TryStartMilking();
        milk.Tick(1d);

        Assert.True(milk.StopMilking());
        Assert.Equal(0.5f, milk.Fill, 4);

        milk.AddFromHit();
        Assert.Equal(0.75f, milk.Fill, 4);
    }

    /// <summary>Hits do not accumulate while it is being drained, or the two would fight.</summary>
    [Fact]
    public void HitsDoNotFillItWhileMilking()
    {
        MilkReservoir milk = Reservoir(perHit: 0.5f, drain: 0.1f);
        milk.LoadFrom(0.5f);
        milk.TryStartMilking();

        Assert.False(milk.AddFromHit());
        Assert.Equal(0.5f, milk.Fill, 4);
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
