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
    /// A bad colour costs the tint, not the difficulty change. The window is the mechanism; the
    /// colour only makes it readable.
    /// </summary>
    [Fact]
    public void ABadColourDropsOnlyTheTint()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 2f,
            NullificationIntervalSeconds = 4f,
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
        });

        Assert.Null(result.Profile.Pleasure.GaugeHighlight);
        Assert.True(result.Profile.Pleasure.HasEffect);
    }
}
