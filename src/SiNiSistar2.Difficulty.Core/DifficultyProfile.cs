namespace SiNiSistar2.Difficulty.Core;

/// <summary>Tuning for the status-ailment mechanism (SPEC002 5.2).</summary>
public sealed record AbnormalTuning(bool Enabled, float RateMultiplier, int LevelBonus)
{
    public static AbnormalTuning Disabled { get; } = new(false, 1f, 0);

    /// <summary>True when the mechanism is on and at least one knob actually changes something.</summary>
    public bool HasEffect => Enabled && (Math.Abs(RateMultiplier - 1f) > 1e-6f || LevelBonus > 0);
}

/// <summary>Tuning for the resistance-nullification mechanism (SPEC002 5.3).</summary>
public sealed record PleasureTuning(
    bool Enabled,
    AbnormalTypeSet Types,
    float IntervalSeconds,
    float IntervalJitter,
    float DurationSeconds,
    float DurationJitter,
    float LevelScaling,
    Rgba? GaugeHighlight = null)
{
    public static PleasureTuning Disabled { get; } =
        new(false, AbnormalTypeSet.Empty, 0f, 0f, 0f, 0f, 0f);

    /// <summary>A zero-length window can never nullify anything, so the patch is not worth applying.</summary>
    public bool HasEffect => Enabled && DurationSeconds > 0f && Types.Count > 0;

    /// <summary>
    /// Fraction of the time a window is expected to be open, at level sum zero. Used to warn about
    /// a configuration that amounts to permanent nullification (SPEC002 FR-117).
    /// </summary>
    public double ExpectedDutyCycle =>
        DurationSeconds <= 0f ? 0d : DurationSeconds / (double)(DurationSeconds + Math.Max(0f, IntervalSeconds));
}

/// <summary>Tuning for the post-escape recovery mechanism (SPEC002 5.4).</summary>
public sealed record BurdenTuning(
    bool Enabled,
    AbnormalTypeSet Types,
    float PenaltySeconds,
    float MoveSlowRate,
    float InvincibleScale,
    float LevelScaling)
{
    public static BurdenTuning Disabled { get; } =
        new(false, AbnormalTypeSet.Empty, 0f, 0f, 1f, 0f);

    public bool HasEffect => Enabled && PenaltySeconds > 0f && Types.Count > 0;
}

/// <summary>
/// The validated configuration the plugin acts on. Anything that failed validation arrives here
/// already switched off, so the patch layer never has to re-check a value (SPEC002 6.2).
/// </summary>
public sealed record DifficultyProfile(
    DifficultyTier Tier,
    bool ForceHardData,
    AbnormalTuning Abnormal,
    PleasureTuning Pleasure,
    BurdenTuning Burden,
    bool LogInterventions)
{
    /// <summary>Everything off. <c>Tier = Off</c> must apply no patches at all (SPEC002 FR-125).</summary>
    public static DifficultyProfile Inactive { get; } = new(
        DifficultyTier.Off,
        false,
        AbnormalTuning.Disabled,
        PleasureTuning.Disabled,
        BurdenTuning.Disabled,
        false);

    /// <summary>True when at least one patch would do something observable.</summary>
    public bool AnyMechanismActive =>
        Tier != DifficultyTier.Off
        && (ForceHardData || Abnormal.HasEffect || Pleasure.HasEffect || Burden.HasEffect);
}

/// <summary>
/// The result of validating raw options. Errors do not stop the MOD; each one switches off the
/// mechanism it belongs to and leaves the rest running (SPEC002 9章).
/// </summary>
public sealed record ProfileValidation(
    DifficultyProfile Profile,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Notices);

public static class DifficultyProfileFactory
{
    /// <summary>
    /// Validates raw options into a profile. <paramref name="knownAbnormalNames"/> is the game's
    /// own <c>AbnormalType</c> enumerator names; pass an empty collection when they cannot be
    /// enumerated and configured names will be accepted as written.
    /// </summary>
    public static ProfileValidation Create(
        DifficultyOptions options,
        IReadOnlyCollection<string> knownAbnormalNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var notices = new List<string>();

        // Off is answered before anything else is looked at, so a broken value in a mechanism the
        // user has already turned off cannot produce noise (SPEC002 6.2).
        if (options.Tier == DifficultyTier.Off)
        {
            notices.Add("Tier=Off: no patches will be applied.");
            return new ProfileValidation(DifficultyProfile.Inactive, errors, warnings, notices);
        }

        AbnormalTuning abnormal = BuildAbnormal(options, errors);
        PleasureTuning pleasure = BuildPleasure(options, knownAbnormalNames, errors, warnings, notices);
        BurdenTuning burden = BuildBurden(options, knownAbnormalNames, errors, notices);

        var profile = new DifficultyProfile(
            options.Tier,
            options.ForceHardData,
            abnormal,
            pleasure,
            burden,
            options.LogInterventions);

        if (!profile.AnyMechanismActive)
        {
            notices.Add(
                "No mechanism has an effect with the current values. This is the shipped default: "
                + "the tuning values stay at no-change until SPEC002 付録A has been measured (FR-128).");
        }

        return new ProfileValidation(profile, errors, warnings, notices);
    }

    private static AbnormalTuning BuildAbnormal(DifficultyOptions options, List<string> errors)
    {
        if (!options.AbnormalEnabled)
        {
            return AbnormalTuning.Disabled;
        }

        var failed = false;
        if (options.AbnormalRateMultiplier < 0f)
        {
            errors.Add(
                $"Abnormal.AbnormalRateMultiplier is {options.AbnormalRateMultiplier}; a negative "
                + "multiplier would invert the roll. The status-ailment mechanism is disabled (FR-127).");
            failed = true;
        }

        if (options.LevelBonus < 0)
        {
            errors.Add(
                $"Abnormal.LevelBonus is {options.LevelBonus}; a negative bonus would cure the "
                + "player. The status-ailment mechanism is disabled (FR-127).");
            failed = true;
        }

        return failed
            ? AbnormalTuning.Disabled
            : new AbnormalTuning(true, options.AbnormalRateMultiplier, options.LevelBonus);
    }

    private static PleasureTuning BuildPleasure(
        DifficultyOptions options,
        IReadOnlyCollection<string> known,
        List<string> errors,
        List<string> warnings,
        List<string> notices)
    {
        if (!options.PleasureEnabled)
        {
            return PleasureTuning.Disabled;
        }

        var failed = false;
        foreach ((string key, float value) in new[]
                 {
                     ("NullificationIntervalSeconds", options.NullificationIntervalSeconds),
                     ("NullificationIntervalJitter", options.NullificationIntervalJitter),
                     ("NullificationDurationSeconds", options.NullificationDurationSeconds),
                     ("NullificationDurationJitter", options.NullificationDurationJitter),
                     ("PleasureLevelScaling", options.PleasureLevelScaling),
                 })
        {
            if (value < 0f)
            {
                errors.Add(
                    $"Pleasure.{key} is {value}; negative values are not defined. The resistance "
                    + "nullification mechanism is disabled (FR-127).");
                failed = true;
            }
        }

        AbnormalTypeSetParse parse = AbnormalTypeSet.Parse(
            options.PleasureAbnormalTypes,
            known,
            new[] { AbnormalTypeDefaults.Defilement });

        if (parse.Unknown.Count > 0)
        {
            notices.Add(
                $"Pleasure.PleasureAbnormalTypes: ignored unknown status name(s) {string.Join(", ", parse.Unknown)}. "
                + "The remaining names stay in effect (FR-126).");
        }

        if (parse.Rejected.Count > 0)
        {
            errors.Add(
                $"Pleasure.PleasureAbnormalTypes names {string.Join(", ", parse.Rejected)}, which the MOD "
                + "refuses to treat as a pleasure status. Escalating escape difficulty with defilement is "
                + "the game's own axis and the MOD must not act on the same quantity; the name is ignored "
                + "and the rest of the group stays in effect (FR-112, FR-114).");
        }

        if (failed)
        {
            return PleasureTuning.Disabled;
        }

        Rgba? highlight = null;
        if (options.HighlightGauge)
        {
            if (HexColor.TryParse(options.NullificationGaugeColor, out Rgba parsed))
            {
                highlight = parsed;
            }
            else
            {
                // Only the tint is dropped. The window itself is the difficulty change; losing the
                // colour makes it harder to read, not absent.
                errors.Add(
                    $"Pleasure.NullificationGaugeColor '{options.NullificationGaugeColor}' is not "
                    + "RRGGBB or RRGGBBAA. The gauge will not be tinted; the nullification window "
                    + "still applies.");
            }
        }

        var tuning = new PleasureTuning(
            true,
            parse.Set,
            options.NullificationIntervalSeconds,
            options.NullificationIntervalJitter,
            options.NullificationDurationSeconds,
            options.NullificationDurationJitter,
            options.PleasureLevelScaling,
            highlight);

        if (tuning.HasEffect && tuning.ExpectedDutyCycle > options.NullificationDutyWarnThreshold)
        {
            // Not an error: the user has accepted that escape can become impossible. It is a
            // warning because that outcome must not arrive as a surprise (SPEC002 5.3, FR-117).
            warnings.Add(
                $"Nullification windows are expected to be open {tuning.ExpectedDutyCycle:P0} of the time, "
                + $"above the {options.NullificationDutyWarnThreshold:P0} warning threshold. Resistance "
                + "input will be ignored for most of a hold, which approaches permanent nullification.");
        }

        if (tuning.Enabled && tuning.Types.Count == 0)
        {
            notices.Add(
                "Pleasure.PleasureAbnormalTypes resolved to an empty group, so no nullification window "
                + "can ever open.");
        }

        return tuning;
    }

    private static BurdenTuning BuildBurden(
        DifficultyOptions options,
        IReadOnlyCollection<string> known,
        List<string> errors,
        List<string> notices)
    {
        if (!options.BurdenEnabled)
        {
            return BurdenTuning.Disabled;
        }

        var failed = false;
        foreach ((string key, float value) in new[]
                 {
                     ("RecoveryPenaltySeconds", options.RecoveryPenaltySeconds),
                     ("RecoveryMoveSlowRate", options.RecoveryMoveSlowRate),
                     ("RecoveryInvincibleScale", options.RecoveryInvincibleScale),
                     ("BurdenLevelScaling", options.BurdenLevelScaling),
                 })
        {
            if (value < 0f)
            {
                errors.Add(
                    $"Burden.{key} is {value}; negative values are not defined. The recovery delay "
                    + "mechanism is disabled (FR-127).");
                failed = true;
            }
        }

        AbnormalTypeSetParse parse = AbnormalTypeSet.Parse(options.BurdenAbnormalTypes, known);
        if (parse.Unknown.Count > 0)
        {
            notices.Add(
                $"Burden.BurdenAbnormalTypes: ignored unknown status name(s) {string.Join(", ", parse.Unknown)}. "
                + "The remaining names stay in effect (FR-126).");
        }

        return failed
            ? BurdenTuning.Disabled
            : new BurdenTuning(
                true,
                parse.Set,
                options.RecoveryPenaltySeconds,
                options.RecoveryMoveSlowRate,
                options.RecoveryInvincibleScale,
                options.BurdenLevelScaling);
    }
}
