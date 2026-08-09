namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The MOD ships changing nothing at all, so the 付録A measurements can be taken before any tuning
/// value is guessed at (SPEC003 FR-233, FR-278).
/// </summary>
public sealed class PleasureProfileTests
{
    private static readonly string[] Known = SexualAbnormalDefaults.Types
        .Concat(new[] { "Poison", "Blinded" })
        .ToArray();

    private static PleasureValidation Validate(PleasureOptions options) =>
        PleasureProfileFactory.Create(options, Known);

    /// <summary>AC-227: the defaults change nothing, not even the HP suppression.</summary>
    [Fact]
    public void ShippedDefaultsChangeNothing()
    {
        PleasureValidation result = Validate(new PleasureOptions());

        Assert.Empty(result.Errors);
        Assert.True(result.Profile.SuppressSexualHpDamage);
        Assert.False(result.Profile.BlocksSexualHpDamage);
        Assert.False(result.Profile.Pleasure.HasEffect);
        Assert.False(result.Profile.Corruption.HasEffect);
        Assert.False(result.Profile.BreastSuper.HasEffect);
        Assert.False(result.Profile.Climax.GameOverEnabled);
        Assert.Contains(result.Notices, x => x.Contains("PleasureGainPerHit", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-242 / FR-278: the suppression waits for the gauge. Stopping the damage while nothing
    /// takes its place would make a sexual hold cost nothing at all.
    /// </summary>
    [Fact]
    public void TheSuppressionWaitsForAGaugeThatCanRise()
    {
        PleasureValidation result = Validate(new PleasureOptions { SuppressSexualHpDamage = true });

        Assert.False(result.Profile.BlocksSexualHpDamage);
        Assert.Contains(result.Notices, x => x.Contains("FR-278", StringComparison.Ordinal));
    }

    /// <summary>AC-202: with a gauge that can rise, sexual hits stop costing HP.</summary>
    [Fact]
    public void AGaugeThatCanRiseTurnsTheSuppressionOn()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            SuppressSexualHpDamage = true,
            PleasureGainPerHit = 0.2f,
        });

        Assert.True(result.Profile.BlocksSexualHpDamage);
        Assert.DoesNotContain(result.Notices, x => x.Contains("FR-278", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-241 / FR-276: turning the suppression off leaves the rest of the mechanism running. HP is
    /// spent as the game intends and the climax limit still ends the run.
    /// </summary>
    [Fact]
    public void TurningTheSuppressionOffLeavesTheGaugeRunning()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            SuppressSexualHpDamage = false,
            PleasureGainPerHit = 0.2f,
            CorruptionPerClimax = 0.5f,
            EnableClimaxGameOver = true,
            ClimaxLimitBase = 3,
        });

        Assert.False(result.Profile.BlocksSexualHpDamage);
        Assert.True(result.Profile.Pleasure.HasEffect);
        Assert.True(result.Profile.Corruption.HasEffect);
        Assert.True(result.Profile.Climax.GameOverEnabled);
    }

    /// <summary>AC-228: disabling the MOD leaves nothing active.</summary>
    [Fact]
    public void DisablingTheModProducesAnInactiveProfile()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            Enabled = false,
            PleasureGainPerHit = 5f,
            SuppressSexualHpDamage = true,
        });

        Assert.False(result.Profile.AnyMechanismActive);
        Assert.False(result.Profile.SuppressSexualHpDamage);
        Assert.False(result.Profile.BlocksSexualHpDamage);
    }

    /// <summary>A negative value disables only its own mechanism.</summary>
    [Fact]
    public void ANegativeGainDisablesOnlyTheGauge()
    {
        PleasureValidation result = Validate(new PleasureOptions
        {
            PleasureGainPerHit = -1f,
            CorruptionPerClimax = 0.5f,
        });

        Assert.Contains(result.Errors, x => x.Contains("PleasureGainPerHit", StringComparison.Ordinal));
        Assert.False(result.Profile.Pleasure.Enabled);
        Assert.True(result.Profile.Corruption.HasEffect);

        // The gauge is what the suppression depends on, so a gauge that failed validation takes the
        // suppression with it rather than leaving a hold that costs nothing (FR-278).
        Assert.False(result.Profile.BlocksSexualHpDamage);
    }

    /// <summary>
    /// Corruption must never fall, so a negative increment is a configuration error rather than
    /// something to silently clamp.
    /// </summary>
    [Fact]
    public void ANegativeCorruptionIncrementIsRefused()
    {
        PleasureValidation result = Validate(new PleasureOptions { CorruptionPerClimax = -1f });

        Assert.Contains(result.Errors, x => x.Contains("never do", StringComparison.Ordinal));
        Assert.False(result.Profile.Corruption.Enabled);
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
