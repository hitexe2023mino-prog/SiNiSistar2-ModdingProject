namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The MOD ships in a state where only the HP0 removal and the probe log are live, so that the
/// 付録A measurements can be taken before any tuning value is guessed at (SPEC003 FR-233).
/// </summary>
public sealed class PleasureProfileTests
{
    private static readonly string[] Known = SexualAbnormalDefaults.Types
        .Concat(new[] { "Poison", "Blinded" })
        .ToArray();

    private static PleasureValidation Validate(PleasureOptions options) =>
        PleasureProfileFactory.Create(options, Known);

    /// <summary>AC-227: defaults remove the HP0 defeat and nothing else.</summary>
    [Fact]
    public void ShippedDefaultsOnlyRemoveTheHp0Defeat()
    {
        PleasureValidation result = Validate(new PleasureOptions());

        Assert.Empty(result.Errors);
        Assert.True(result.Profile.SuppressHp0WhileBound);
        Assert.False(result.Profile.Pleasure.HasEffect);
        Assert.False(result.Profile.Sensitivity.HasEffect);
        Assert.False(result.Profile.BreastSuper.HasEffect);
        Assert.False(result.Profile.Climax.GameOverEnabled);
        Assert.Contains(result.Notices, x => x.Contains("PleasureGainPerHit", StringComparison.Ordinal));
    }

    /// <summary>AC-228: disabling the MOD leaves nothing active.</summary>
    [Fact]
    public void DisablingTheModProducesAnInactiveProfile()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            Enabled = false,
            PleasureGainPerHit = 5f,
            SuppressHp0WhileBound = true,
        });

        Assert.False(result.Profile.AnyMechanismActive);
        Assert.False(result.Profile.SuppressHp0WhileBound);
    }

    /// <summary>A negative value disables only its own mechanism.</summary>
    [Fact]
    public void ANegativeGainDisablesOnlyTheGauge()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            PleasureGainPerHit = -1f,
            SensitivityPerClimax = 0.5f,
        });

        Assert.Contains(result.Errors, x => x.Contains("PleasureGainPerHit", StringComparison.Ordinal));
        Assert.False(result.Profile.Pleasure.Enabled);
        Assert.True(result.Profile.Sensitivity.HasEffect);
        Assert.True(result.Profile.SuppressHp0WhileBound);
    }

    /// <summary>
    /// Sensitivity must never fall, so a negative increment is a configuration error rather than
    /// something to silently clamp.
    /// </summary>
    [Fact]
    public void ANegativeSensitivityIncrementIsRefused()
    {
        PleasureValidation result = Validate(new PleasureOptions { SensitivityPerClimax = -1f });

        Assert.Contains(result.Errors, x => x.Contains("never do", StringComparison.Ordinal));
        Assert.False(result.Profile.Sensitivity.Enabled);
    }

    /// <summary>A game over that can never trigger is a configuration mistake worth saying aloud.</summary>
    [Fact]
    public void GameOverWithoutARisingGaugeWarns()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            EnableClimaxGameOver = true,
            ClimaxLimitBase = 3,
        });

        Assert.Contains(result.Warnings, x => x.Contains("no climax can ever occur", StringComparison.Ordinal));
    }

    /// <summary>A limit that computes to zero would make the first hold fatal.</summary>
    [Fact]
    public void GameOverWithAZeroLimitWarns()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            PleasureGainPerHit = 0.5f,
            EnableClimaxGameOver = true,
            ClimaxLimitBase = 0,
            ClimaxLimitPerDurability = 0f,
        });

        Assert.Contains(result.Warnings, x => x.Contains("fatal immediately", StringComparison.Ordinal));
    }

    /// <summary>Enabling BreastSuper in ordinary play is flagged: it is authored for an event.</summary>
    [Fact]
    public void EnablingBreastSuperWarns()
    {
        PleasureValidation result = Validate(new PleasureOptions { BreastSuperAfterApplications = 3 });

        Assert.Contains(result.Warnings, x => x.Contains("BreastSuper", StringComparison.Ordinal));
        Assert.True(result.Profile.BreastSuper.HasEffect);
    }

    /// <summary>FR-233: the shipped count of 0 means the escalation can never happen.</summary>
    [Fact]
    public void TheShippedCountNeverEscalates()
    {
        PleasureValidation result = Validate(new PleasureOptions());

        Assert.False(result.Profile.BreastSuper.HasEffect);
        Assert.DoesNotContain(result.Warnings, x => x.Contains("BreastSuper", StringComparison.Ordinal));
    }

    [Fact]
    public void ANegativeCountIsRefused()
    {
        PleasureValidation result = Validate(new PleasureOptions { BreastSuperAfterApplications = -1 });

        Assert.Contains(result.Errors, x => x.Contains("BreastSuperAfterApplications", StringComparison.Ordinal));
        Assert.False(result.Profile.BreastSuper.HasEffect);
    }

    /// <summary>Changing a value on the game's own AbnormalData is announced, not done quietly.</summary>
    [Fact]
    public void TheHaanjaCurableOverrideWarns()
    {
        PleasureValidation result = Validate(new PleasureOptions { BreastSuperMakeHaanjaCurable = true });

        Assert.Contains(result.Warnings, x => x.Contains("Haanja", StringComparison.Ordinal));
        Assert.True(result.Profile.BreastSuper.MakeHaanjaCurable);
    }

    /// <summary>An unknown status name is dropped and named, not fatal to the whole group.</summary>
    [Fact]
    public void AnUnknownStatusNameIsReportedAndTheRestSurvive()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            PleasureGainPerHit = 0.5f,
            SexualAbnormalTypes = "Lustfull,NoSuchStatus,Semen",
        });

        Assert.Contains(result.Notices, x => x.Contains("NoSuchStatus", StringComparison.Ordinal));
        Assert.Equal(AttackKind.Sexual, result.Profile.Classifier.Classify(null, null, new[] { "Semen" }));
    }

    /// <summary>DEC-210: the sexual set is not SPEC002's pleasure set; Defilement belongs here.</summary>
    [Fact]
    public void DefilementCountsAsEvidenceOfASexualAttack()
    {
        Assert.Contains("Defilement", SexualAbnormalDefaults.Types);

        PleasureValidation result = Validate(new PleasureOptions { PleasureGainPerHit = 0.5f });
        Assert.Equal(AttackKind.Sexual, result.Profile.Classifier.Classify(null, null, new[] { "Defilement" }));
    }
}
