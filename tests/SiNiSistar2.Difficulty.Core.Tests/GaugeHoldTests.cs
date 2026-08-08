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
        hold.Begin(0.4f, 0f);

        Assert.True(hold.TryHold(0.55f, out float held));
        Assert.Equal(0.4f, held, 5);
    }

    /// <summary>AC-115: decay keeps working, so the gauge visibly falls while the player mashes.</summary>
    [Fact]
    public void AFallIsAllowedAndBecomesTheNewCeiling()
    {
        var hold = new GaugeHold();
        hold.Begin(0.4f, 0f);

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
        hold.Begin(0.5f, 0f);

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
        hold.Begin(0.4f, 0f);
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
        hold.Begin(0.4f, 0f);
        hold.TryHold(0.1f, out _);
        hold.End();

        hold.Begin(0.8f, 0f);
        Assert.Equal(0.8f, hold.Ceiling, 5);
        Assert.False(hold.TryHold(0.75f, out _));
    }

    /// <summary>
    /// AC-135: at penalty 1.0 the player loses exactly what the input would have gained, so
    /// resisting inside the window costs ground rather than merely achieving nothing (FR-136).
    /// </summary>
    [Fact]
    public void ResistingCostsWhatItWouldHaveGained()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f, 1f);

        // A rise of 0.1 is detected, so 0.1 is taken off instead of being granted.
        Assert.True(hold.TryHold(0.6f, out float held));
        Assert.Equal(0.4f, held, 5);
        Assert.Equal(0.4f, hold.Ceiling, 5);
    }

    /// <summary>The penalty scales with how hard the player mashed, not with the frame rate.</summary>
    [Fact]
    public void APenaltyScalesWithTheSizeOfTheAttemptedRise()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f, 2f);

        Assert.True(hold.TryHold(0.55f, out float held));
        Assert.Equal(0.4f, held, 5);
    }

    /// <summary>
    /// AC-135: the penalty only fires on an attempted rise. Decay alone must not be amplified, or
    /// the window would be a faster-decay setting rather than a punishment for resisting.
    /// </summary>
    [Fact]
    public void DecayAloneIsNotPenalised()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f, 1f);

        Assert.False(hold.TryHold(0.45f, out float held));
        Assert.Equal(0.45f, held, 5);
        Assert.Equal(0.45f, hold.Ceiling, 5);
    }

    /// <summary>AC-136: the gauge cannot be driven below zero however hard the player resists.</summary>
    [Fact]
    public void ThePenaltyNeverDrivesTheGaugeBelowZero()
    {
        var hold = new GaugeHold();
        hold.Begin(0.05f, 5f);

        Assert.True(hold.TryHold(0.5f, out float held));
        Assert.Equal(0f, held, 5);
        Assert.Equal(0f, hold.Ceiling, 5);

        Assert.True(hold.TryHold(0.4f, out held));
        Assert.Equal(0f, held, 5);
    }

    /// <summary>AC-137: ground lost to the penalty cannot be won back inside the same window.</summary>
    [Fact]
    public void GroundLostToThePenaltyStaysLostForTheRestOfTheWindow()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f, 1f);

        hold.TryHold(0.6f, out _);
        Assert.True(hold.TryHold(0.5f, out float held));
        Assert.Equal(0.3f, held, 5);
    }

    /// <summary>AC-138: a penalty of zero is the previous behaviour, so the change is reversible.</summary>
    [Fact]
    public void APenaltyOfZeroOnlyStopsTheRise()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f, 0f);

        Assert.True(hold.TryHold(0.9f, out float held));
        Assert.Equal(0.5f, held, 5);
    }

    /// <summary>A negative penalty would reward resisting, so it is clamped away at the boundary.</summary>
    [Fact]
    public void ANegativePenaltyCannotRewardResisting()
    {
        var hold = new GaugeHold();
        hold.Begin(0.5f, -3f);

        Assert.True(hold.TryHold(0.9f, out float held));
        Assert.Equal(0.5f, held, 5);
    }
}
