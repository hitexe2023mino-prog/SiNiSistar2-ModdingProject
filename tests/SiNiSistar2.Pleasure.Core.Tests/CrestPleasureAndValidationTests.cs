using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The crest's effect on pleasure gain, and the configuration rules that keep the staging honest
/// (SPEC005 5.2.1, 5.5.3, AC-407, AC-415, AC-418).
/// </summary>
public sealed class CrestPleasureAndValidationTests
{
    /// <summary>
    /// AC-415: the worked example from 5.2.1 — 0.10 base, corruption 0.40 at a scale of 1, and the
    /// 1.25 the mark applies.
    /// </summary>
    [Fact]
    public void SublimatedBodyGainsTheWorkedExampleAmount()
    {
        var before = new PleasureMeter(0.10f, 1f, 0f, 1.25f);
        var after = new PleasureMeter(0.10f, 1f, 0f, 1.25f);

        before.AddSexualHit(0.40f, crestSublimated: false);
        after.AddSexualHit(0.40f, crestSublimated: true);

        Assert.Equal(0.140f, before.Value, 4);
        Assert.Equal(0.175f, after.Value, 4);
    }

    /// <summary>
    /// AC-407: the multiplier is the mark's, not the curse's. The curse stages are deliberately
    /// indistinguishable here — their sensitivity is carried by corruption accumulating faster
    /// instead, which is what keeps the same idea out of two places (DEC-408).
    /// </summary>
    [Fact]
    public void TheCurseStagesDoNotTakeTheMultiplier()
    {
        var meter = new PleasureMeter(0.10f, 1f, 0f, 1.25f);

        meter.AddSexualHit(0.40f, crestSublimated: false);

        Assert.Equal(0.140f, meter.Value, 4);
    }

    /// <summary>A scale of 1 is the off switch, and leaves the SPEC003 formula untouched.</summary>
    [Fact]
    public void AScaleOfOneRestoresTheOriginalFormula()
    {
        var meter = new PleasureMeter(0.10f, 1f, 0f, 1f);

        meter.AddSexualHit(0.40f, crestSublimated: true);

        Assert.Equal(0.140f, meter.Value, 4);
    }

    /// <summary>The mark is never a resistance to pleasure, whatever the file says (FR-408).</summary>
    [Fact]
    public void AScaleBelowOneIsRefused()
    {
        var meter = new PleasureMeter(0.10f, 0f, 0f, 0.5f);

        meter.AddSexualHit(0f, crestSublimated: true);

        Assert.Equal(0.10f, meter.Value, 4);
    }

    /// <summary>
    /// AC-418: a configuration with no cliff is refused. The staging exists to put a discontinuity
    /// where the curse stops being reversible; without one, passing the point of no return costs
    /// nothing (FR-420).
    /// </summary>
    [Fact]
    public void ConfigurationWithoutACliffIsRejectedAndCorruptionKeepsRunning()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions
            {
                CorruptionPerSexualHit = 1f,
                CorruptionCurseGainMax = 0.30f,
                CorruptionCrestGainScale = 1.20f,
            },
            Array.Empty<string>());

        Assert.Contains(result.Errors, x => x.Contains("CorruptionCrestGainScale", StringComparison.Ordinal));

        // Switched off rather than left half-applied, and the track itself is untouched: a bad
        // multiplier must not be able to stop corruption accumulating (FR-420).
        Assert.True(result.Profile.Corruption.Enabled);
        Assert.Equal(1f, result.Profile.Corruption.ScaleFor(3, 4, false), 3);
        Assert.Equal(1f, result.Profile.Corruption.ScaleFor(4, 4, true), 3);
    }

    /// <summary>The shipped defaults keep the cliff: no curse acceleration, sublimation at 2.0.</summary>
    [Fact]
    public void ShippedDefaultsKeepTheCliff()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions { CorruptionPerSexualHit = 1f },
            Array.Empty<string>());

        Assert.DoesNotContain(
            result.Errors,
            x => x.Contains("CorruptionCrestGainScale", StringComparison.Ordinal));
        Assert.Equal(1f, result.Profile.Corruption.ScaleFor(2, 4, false), 3);
        Assert.Equal(2f, result.Profile.Corruption.ScaleFor(4, 4, true), 3);
    }

    /// <summary>A curse that slowed corruption down would invert the whole axis.</summary>
    [Fact]
    public void NegativeCurseAccelerationIsRefused()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions { CorruptionPerSexualHit = 1f, CorruptionCurseGainMax = -0.5f },
            Array.Empty<string>());

        Assert.Contains(result.Errors, x => x.Contains("CorruptionCurseGainMax", StringComparison.Ordinal));
        Assert.Equal(0f, result.Profile.Corruption.CurseGainMax, 3);
    }

    /// <summary>
    /// The MP0 penalty ships off, and unknown input names are dropped one at a time rather than
    /// taking the whole set down (SPEC005 6章 設定の検証).
    /// </summary>
    [Fact]
    public void MpPenaltyShipsDisabled()
    {
        PleasureValidation result = PleasureProfileFactory.Create(new PleasureOptions(), Array.Empty<string>());

        Assert.False(result.Profile.MpPenalty.Enabled);
        Assert.False(result.Profile.MpPenalty.HasEffect);
    }

    /// <summary>
    /// The MP0 penalty asks for the whole corruption track, not half of it
    /// (利用者決定 2026-08-10, DEC-405, CHG-514).
    ///
    /// Locked because the earlier value — the fraction at which the crest first appears — put the
    /// penalty at the same point as the warning, which flattens the whole second half of the fall.
    /// </summary>
    [Fact]
    public void TheMpPenaltyRequiresTheCorruptionToBeAtItsCap()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions { MpPenaltyEnabled = true, StunChance = 1f },
            Array.Empty<string>());

        Assert.Equal(1f, result.Profile.MpPenalty.CorruptionFraction, 3);
    }

    /// <summary>
    /// The MP gate is a fifth of the bar, not exactly nothing (利用者決定 2026-08-10, CHG-517).
    ///
    /// Zero is a state the player passes through rather than sits in, so a penalty keyed to it is
    /// met by accident and never planned around.
    /// </summary>
    [Fact]
    public void TheMpPenaltyFiresBelowAFractionOfTheBarRatherThanAtZero()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions { MpPenaltyEnabled = true, StunChance = 1f },
            Array.Empty<string>());

        Assert.Equal(0.2f, result.Profile.MpPenalty.MpFraction, 3);
    }

    /// <summary>A share above the whole bar is meaningless; it is clamped rather than obeyed.</summary>
    [Fact]
    public void TheMpGateIsClampedToTheBar()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions
            {
                MpPenaltyEnabled = true,
                StunChance = 1f,
                MpPenaltyMpFraction = 4f,
            },
            Array.Empty<string>());

        Assert.Equal(1f, result.Profile.MpPenalty.MpFraction, 3);
    }

    [Fact]
    public void UnknownStunInputsAreIgnoredWithoutLosingTheKnownOnes()
    {
        PleasureValidation result = PleasureProfileFactory.Create(
            new PleasureOptions
            {
                MpPenaltyEnabled = true,
                StunChance = 0.2f,
                StunTriggerInputs = "attack, Sneeze ,Jump",
            },
            Array.Empty<string>());

        Assert.Contains(result.Notices, x => x.Contains("Sneeze", StringComparison.Ordinal));
        Assert.Equal(
            new[] { StunInputs.Attack, StunInputs.Jump },
            result.Profile.MpPenalty.TriggerInputs);
    }

    /// <summary>
    /// AC-413, updated (利用者決定 2026-08-10, CHG-516): a value the user has settled is not
    /// "waiting on a measurement" any more, and FR-415 only asks unmeasured values to be inert.
    /// The regen buff went unfelt in the first verification pass precisely because
    /// RegenDurationPerClimax shipped at 0 — correct per the old rule, indistinguishable from
    /// broken in play. It is a settled value now, the same way CrestPleasureGainScale already was.
    ///
    /// The MP0 penalty and the curse-stage acceleration are the two that remain genuinely
    /// unmeasured (付録A A-406) and stay inert by default.
    /// </summary>
    [Fact]
    public void ShippedDefaultsLeaveOnlyTheUnmeasuredMechanismsInert()
    {
        PleasureValidation result = PleasureProfileFactory.Create(new PleasureOptions(), Array.Empty<string>());

        Assert.True(result.Profile.Regen.HasEffect);
        Assert.Equal(15f, result.Profile.Regen.DurationPerClimax, 3);
        Assert.Equal(2f, result.Profile.Regen.HpPerSecond, 3);
        Assert.Equal(2f, result.Profile.Regen.MpPerSecond, 3);

        // MpPenalty.Enabled itself still ships off — A-401 answers how the stagger plays, not
        // whether the penalty should be on by default. Disabled collapses every field to its own
        // zeroes regardless of what the options held, so the settled threshold and chance are
        // covered separately, with Enabled=true, in AZeroChancePenaltyCountsThePressAndSaysItIsOff
        // and TheMpPenaltyRequiresTheCorruptionToBeAtItsCap.
        Assert.False(result.Profile.MpPenalty.HasEffect);
        Assert.Equal(MpPenaltyTuning.Disabled, result.Profile.MpPenalty);

        Assert.Equal(0f, result.Profile.Corruption.CurseGainMax, 3);
        Assert.Equal(1.25f, result.Profile.Pleasure.CrestScale, 3);
    }
}
