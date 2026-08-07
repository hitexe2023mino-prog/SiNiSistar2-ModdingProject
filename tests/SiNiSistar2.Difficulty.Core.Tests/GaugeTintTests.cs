namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// DEC-103 keeps the struggle gauge on screen so the player can see input not registering, but a
/// gauge that merely stops moving reads as the game having locked up. The tint is what makes the
/// stall legible as deliberate, so a wrong colour setting must be reported rather than substituted.
/// </summary>
public sealed class GaugeTintTests
{
    [Theory]
    [InlineData("FF3E9D")]
    [InlineData("#FF3E9D")]
    [InlineData("  ff3e9d  ")]
    public void SixDigitHexIsAcceptedAndFullyOpaque(string text)
    {
        Assert.True(HexColor.TryParse(text, out Rgba color));
        Assert.Equal(1f, color.R, 3);
        Assert.Equal(0.243f, color.G, 3);
        Assert.Equal(0.616f, color.B, 3);
        Assert.Equal(1f, color.A, 3);
    }

    [Fact]
    public void EightDigitHexCarriesAlpha()
    {
        Assert.True(HexColor.TryParse("FF3E9D80", out Rgba color));
        Assert.Equal(0.502f, color.A, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("pink")]
    [InlineData("FF3E9")]
    [InlineData("FF3E9DFF00")]
    [InlineData("GG3E9D")]
    public void AnythingElseIsRefusedRatherThanSubstituted(string? text)
    {
        Assert.False(HexColor.TryParse(text, out _));
    }

    /// <summary>
    /// With no penalty the colour is only feedback, so a bad value costs the tint and nothing
    /// else. The window is the mechanism; the colour merely makes it readable. (With a penalty the
    /// cue is mandatory and a bad value falls back instead — see the test below.)
    /// </summary>
    [Fact]
    public void ABadColourDropsOnlyTheTintWhenResistingIsNotPunished()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationIntervalSeconds = 4f,
            NullificationResistPenalty = 0f,
            NullificationGaugeColor = "not-a-colour",
        });

        Assert.Contains(result.Errors, x => x.Contains("NullificationGaugeColor", StringComparison.Ordinal));
        Assert.Null(result.Profile.Pleasure.GaugeHighlight);
        Assert.True(result.Profile.Pleasure.HasEffect);
    }

    [Fact]
    public void TheTintIsOnByDefaultWhenTheWindowIsConfigured()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationIntervalSeconds = 4f,
        });

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Profile.Pleasure.GaugeHighlight);
    }

    [Fact]
    public void TurningTheTintOffLeavesTheWindowIntact()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationIntervalSeconds = 4f,
            HighlightGauge = false,
            NullificationResistPenalty = 0f,
        });

        Assert.Null(result.Profile.Pleasure.GaugeHighlight);
        Assert.True(result.Profile.Pleasure.HasEffect);
    }

    /// <summary>
    /// AC-139: once resisting costs progress the colour stops being optional. A punished window
    /// with no cue would be unexplained loss, so the setting no longer switches it off
    /// (FR-137, DEC-115).
    /// </summary>
    [Fact]
    public void ThePenaltyForcesTheTintOnEvenWhenTheSettingTurnsItOff()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationIntervalSeconds = 4f,
            HighlightGauge = false,
            NullificationResistPenalty = 1f,
        });

        Assert.NotNull(result.Profile.Pleasure.GaugeHighlight);
    }

    /// <summary>
    /// A punished window still has to be signalled, so an unusable colour falls back to the
    /// shipped one instead of the cue disappearing with the setting.
    /// </summary>
    [Fact]
    public void ABadColourFallsBackToTheDefaultWhileThePenaltyIsOn()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationIntervalSeconds = 4f,
            NullificationResistPenalty = 1f,
            NullificationGaugeColor = "nonsense",
        });

        Assert.Contains(result.Errors, x => x.Contains("Falling back", StringComparison.Ordinal));
        Assert.NotNull(result.Profile.Pleasure.GaugeHighlight);
    }

    /// <summary>A negative penalty would reward resisting, so it disables the mechanism (FR-127).</summary>
    [Fact]
    public void ANegativePenaltyDisablesTheMechanism()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationResistPenalty = -1f,
        });

        Assert.Contains(result.Errors, x => x.Contains("NullificationResistPenalty", StringComparison.Ordinal));
        Assert.False(result.Profile.Pleasure.Enabled);
    }
}
