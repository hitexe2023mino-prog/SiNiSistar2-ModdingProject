namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// Configuration is the only place a user can make the MOD do something, so what a wrong value
/// does is part of the contract: it disables its own mechanism and says so, and never quietly
/// takes the rest of the MOD with it (SPEC002 6.2, 9章).
/// </summary>
public sealed class ProfileValidationTests
{
    /// <summary>AC-126: shipped defaults change nothing until 付録A has been measured (FR-128).</summary>
    [Fact]
    public void ShippedDefaultsHaveNoEffectOnTheGame()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions());

        Assert.Empty(result.Errors);
        Assert.False(result.Profile.Abnormal.HasEffect);
        Assert.False(result.Profile.Pleasure.HasEffect);
        Assert.False(result.Profile.Burden.HasEffect);

        // ForceHardData is on by default, so the tier itself is still doing something; that is the
        // one default that is meant to be visible.
        Assert.True(result.Profile.ForceHardData);
    }

    /// <summary>AC-103: Off is answered before anything else and applies no patches (FR-125).</summary>
    [Fact]
    public void OffProducesAnInactiveProfileWithoutValidatingAnythingElse()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            Tier = DifficultyTier.Off,

            // Deliberately invalid. Off must not report on a mechanism the user already turned off.
            AbnormalRateMultiplier = -5f,
            RecoveryPenaltySeconds = -1f,
        });

        Assert.Empty(result.Errors);
        Assert.False(result.Profile.AnyMechanismActive);
        Assert.False(result.Profile.ForceHardData);
        Assert.Equal(DifficultyTier.Off, result.Profile.Tier);
    }

    /// <summary>AC-125: a negative value disables only its own mechanism (FR-127).</summary>
    [Fact]
    public void ANegativeMultiplierDisablesOnlyTheStatusMechanism()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            AbnormalRateMultiplier = -1f,
            NullificationDurationSeconds = 1f,
            NullificationIntervalSeconds = 4f,
            RecoveryPenaltySeconds = 2f,
        });

        Assert.Contains(result.Errors, x => x.Contains("AbnormalRateMultiplier", StringComparison.Ordinal));
        Assert.False(result.Profile.Abnormal.Enabled);

        // The other two mechanisms were configured to do something and must be untouched.
        Assert.True(result.Profile.Pleasure.HasEffect);
        Assert.True(result.Profile.Burden.HasEffect);
    }

    [Fact]
    public void ANegativeRecoveryValueDisablesOnlyTheRecoveryMechanism()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            AbnormalRateMultiplier = 2f,
            RecoveryPenaltySeconds = -3f,
        });

        Assert.Contains(result.Errors, x => x.Contains("RecoveryPenaltySeconds", StringComparison.Ordinal));
        Assert.False(result.Profile.Burden.Enabled);
        Assert.True(result.Profile.Abnormal.HasEffect);
    }

    /// <summary>AC-124: one bad name is dropped, the rest of the group survives (FR-126).</summary>
    [Fact]
    public void AnUnknownStatusNameIsReportedAndTheRestOfTheGroupStays()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 1f,
            PleasureAbnormalTypes = "Lustfull, NoSuchStatus, MindControl",
        });

        Assert.Contains(result.Notices, x => x.Contains("NoSuchStatus", StringComparison.Ordinal));
        Assert.Empty(result.Errors);
        Assert.True(result.Profile.Pleasure.Types.Contains("Lustfull"));
        Assert.True(result.Profile.Pleasure.Types.Contains("MindControl"));
        Assert.False(result.Profile.Pleasure.Types.Contains("NoSuchStatus"));
    }

    /// <summary>
    /// AC-114: defilement cannot be smuggled into the pleasure group through configuration.
    /// The MOD's whole reason for using a time band is to leave the defilement axis alone
    /// (FR-112, FR-114, DEC-102).
    /// </summary>
    [Fact]
    public void DefilementIsRefusedAsAPleasureStatusButDoesNotVoidTheGroup()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 1f,
            PleasureAbnormalTypes = "Lustfull,Defilement,Milk",
        });

        Assert.Contains(result.Errors, x => x.Contains("Defilement", StringComparison.Ordinal));
        Assert.False(result.Profile.Pleasure.Types.Contains("Defilement"));
        Assert.True(result.Profile.Pleasure.Types.Contains("Lustfull"));
        Assert.True(result.Profile.Pleasure.Types.Contains("Milk"));
        Assert.True(result.Profile.Pleasure.HasEffect);
    }

    /// <summary>Defilement is not in the shipped pleasure defaults either (SPEC002 6.1).</summary>
    [Fact]
    public void DefilementIsAbsentFromBothShippedGroups()
    {
        Assert.DoesNotContain(AbnormalTypeDefaults.Defilement, AbnormalTypeDefaults.Pleasure);
        Assert.DoesNotContain(AbnormalTypeDefaults.Defilement, AbnormalTypeDefaults.Burden);
    }

    /// <summary>AC-117: a near-permanent configuration warns but is still allowed (FR-117).</summary>
    [Fact]
    public void AHighDutyCycleWarnsWithoutDisablingTheMechanism()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 9f,
            NullificationIntervalSeconds = 1f,
            NullificationDutyWarnThreshold = 0.6f,
        });

        Assert.Contains(result.Warnings, x => x.Contains("90", StringComparison.Ordinal));
        Assert.Empty(result.Errors);
        Assert.True(result.Profile.Pleasure.HasEffect);
    }

    [Fact]
    public void ADutyCycleUnderTheThresholdIsSilent()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 1f,
            NullificationIntervalSeconds = 9f,
            NullificationDutyWarnThreshold = 0.6f,
        });

        Assert.Empty(result.Warnings);
        Assert.Equal(0.1d, result.Profile.Pleasure.ExpectedDutyCycle, 6);
    }

    /// <summary>A status may legitimately be both pleasure and burden (SPEC002 6.2).</summary>
    [Fact]
    public void TheSameStatusMayBelongToBothGroups()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 1f,
            RecoveryPenaltySeconds = 1f,
            PleasureAbnormalTypes = "Pregnant",
            BurdenAbnormalTypes = "Pregnant",
        });

        Assert.Empty(result.Errors);
        Assert.True(result.Profile.Pleasure.Types.Contains("Pregnant"));
        Assert.True(result.Profile.Burden.Types.Contains("Pregnant"));
    }

    /// <summary>An empty group can never open a window, and the user is told rather than left guessing.</summary>
    [Fact]
    public void AnEmptyPleasureGroupIsInertAndReported()
    {
        ProfileValidation result = TestSupport.Validate(new DifficultyOptions
        {
            NullificationDurationSeconds = 5f,
            PleasureAbnormalTypes = "   ",
        });

        Assert.False(result.Profile.Pleasure.HasEffect);
        Assert.Contains(result.Notices, x => x.Contains("empty group", StringComparison.Ordinal));
    }

    /// <summary>
    /// When the game's enumerator names cannot be read, configured names are taken as written
    /// rather than every one of them being reported as unknown.
    /// </summary>
    [Fact]
    public void AnEmptyKnownSetAcceptsConfiguredNamesAsWritten()
    {
        AbnormalTypeSetParse parse = AbnormalTypeSet.Parse("Alpha,Beta", Array.Empty<string>());

        Assert.Empty(parse.Unknown);
        Assert.True(parse.Set.Contains("Alpha"));
        Assert.True(parse.Set.Contains("Beta"));
    }
}
