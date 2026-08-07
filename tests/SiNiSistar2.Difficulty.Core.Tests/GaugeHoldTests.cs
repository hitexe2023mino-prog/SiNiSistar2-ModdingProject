namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// The hold has to stop the gauge rising while still letting the game's decay lower it. If it
/// pinned the value outright the window would read as a pause, and DEC-103 depends on the player
/// watching the gauge fall while they mash.
/// </summary>
public sealed class GaugeHoldTests
{
    [Fact]
    public void NothingIsWrittenWhileTheWindowIsClosed()
    {
        var hold = new GaugeHold();

        Assert.False(hold.TryHold(0.9f, out float held));
        Assert.Equal(0.9f, held, 5);
        Assert.False(hold.IsHolding);
    }

    /// <summary>AC-111: input during the window does not move the gauge.</summary>
    [Fact]
    public void ARiseIsPushedBackToTheCeiling()
    {
        var hold = new GaugeHold();
        hold.Begin(0.4f);

        Assert.True(hold.TryHold(0.55f, out float held));
        Assert.Equal(0.4f, held, 5);
    }

    /// <summary>AC-115: decay keeps working, so the gauge visibly falls while the player mashes.</summary>
    [Fact]
    public void AFallIsAllowedAndBecomesTheNewCeiling()
    {
        var hold = new GaugeHold();
        hold.Begin(0.4f);

        Assert.False(hold.TryHold(0.3f, out float held));
        Assert.Equal(0.3f, held, 5);
        Assert.Equal(0.3f, hold.Ceiling, 5);

        // Ground lost to decay cannot be won back while the window is still open.
        Assert.True(hold.TryHold(0.38f, out held));
        Assert.Equal(0.3f, held, 5);
    }

    [Fact]
    public void TheCeilingNeverRatchetsUp()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f);

        hold.TryHold(0.9f, out _);
        hold.TryHold(0.9f, out _);
        Assert.Equal(0.5f, hold.Ceiling, 5);

        hold.TryHold(0.2f, out _);
        hold.TryHold(0.9f, out float held);
        Assert.Equal(0.2f, held, 5);
    }

    /// <summary>
    /// Ending the window hands the gauge straight back: the next rise has to register, or the
    /// window would effectively never close.
    /// </summary>
    [Fact]
    public void EndingTheWindowReleasesTheGaugeImmediately()
    {
        var hold = new GaugeHold();
        hold.Begin(0.4f);
        hold.TryHold(0.7f, out _);

        hold.End();

        Assert.False(hold.IsHolding);
        Assert.False(hold.TryHold(0.7f, out float held));
        Assert.Equal(0.7f, held, 5);
    }

    /// <summary>A fresh window starts from wherever the gauge actually is, not the old ceiling.</summary>
    [Fact]
    public void EachWindowStartsFromTheCurrentValue()
    {
        var hold = new GaugeHold();
        hold.Begin(0.4f);
        hold.TryHold(0.1f, out _);
        hold.End();

        hold.Begin(0.8f);
        Assert.Equal(0.8f, hold.Ceiling, 5);
        Assert.False(hold.TryHold(0.75f, out _));
    }
}
