namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The reservoir is what makes milking a decision rather than a keypress: it says how long the next
/// attempt will take, and it grows while the swelling is left alone (SPEC003 5.8, FR-259).
/// </summary>
public sealed class MilkReservoirTests
{
    private static MilkReservoir Reservoir(float fill = 0.1f, float drain = 0.5f, float super = 2f) =>
        new(fill, drain, super);

    [Fact]
    public void OnlySwellingFillsIt()
    {
        MilkReservoir milk = Reservoir();

        milk.Tick(1d, swollen: false, super: false);
        Assert.Equal(0f, milk.Fill);

        milk.Tick(1d, swollen: true, super: false);
        Assert.Equal(0.1f, milk.Fill, 4);
    }

    /// <summary>The escalated swelling fills faster, so leaving it on costs more to undo.</summary>
    [Fact]
    public void TheEscalatedSwellingFillsFaster()
    {
        MilkReservoir ordinary = Reservoir();
        MilkReservoir escalated = Reservoir();

        ordinary.Tick(1d, swollen: true, super: false);
        escalated.Tick(1d, swollen: true, super: true);

        Assert.True(escalated.Fill > ordinary.Fill);
        Assert.Equal(ordinary.Fill * 2f, escalated.Fill, 4);
    }

    [Fact]
    public void ItStopsAtFull()
    {
        MilkReservoir milk = Reservoir(fill: 1f);

        milk.Tick(10d, swollen: true, super: true);

        Assert.Equal(1f, milk.Fill);
    }

    [Fact]
    public void MilkingDrainsItAndReportsWhenItEmpties()
    {
        MilkReservoir milk = Reservoir(drain: 0.5f);
        milk.LoadFrom(1f);
        Assert.True(milk.TryStartMilking());

        Assert.Equal(MilkOutcome.None, milk.Tick(1d, true, false));
        Assert.Equal(0.5f, milk.Fill, 4);
        Assert.Equal(MilkOutcome.Emptied, milk.Tick(1d, true, false));
        Assert.Equal(0f, milk.Fill);
        Assert.False(milk.IsMilking);
    }

    /// <summary>Milking an empty reservoir is refused rather than completing instantly.</summary>
    [Fact]
    public void AnEmptyReservoirCannotBeMilked()
    {
        MilkReservoir milk = Reservoir();

        Assert.False(milk.CanMilk);
        Assert.False(milk.TryStartMilking());
    }

    /// <summary>A drain of zero switches milking off entirely.</summary>
    [Fact]
    public void ADrainOfZeroCannotMilk()
    {
        MilkReservoir milk = Reservoir(drain: 0f);
        milk.LoadFrom(1f);

        Assert.False(milk.CanMilk);
        Assert.False(milk.TryStartMilking());
    }

    /// <summary>
    /// An interruption keeps what is left. Throwing the whole reservoir away would make one hit
    /// undo minutes of filling, which punishes far harder than the risk is worth.
    /// </summary>
    [Fact]
    public void StoppingKeepsWhatIsLeft()
    {
        MilkReservoir milk = Reservoir(drain: 0.5f);
        milk.LoadFrom(1f);
        milk.TryStartMilking();
        milk.Tick(1d, true, false);

        Assert.True(milk.StopMilking());
        Assert.Equal(0.5f, milk.Fill, 4);
        Assert.False(milk.StopMilking());
    }

    /// <summary>It does not refill while being drained, or the two would fight each other.</summary>
    [Fact]
    public void ItDoesNotFillWhileMilking()
    {
        MilkReservoir milk = Reservoir(fill: 10f, drain: 0.5f);
        milk.LoadFrom(1f);
        milk.TryStartMilking();

        milk.Tick(1d, swollen: true, super: true);

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
