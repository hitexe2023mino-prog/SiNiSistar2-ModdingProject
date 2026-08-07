namespace SiNiSistar2.Pleasure.Core;

/// <summary>Tuning for the pleasure gauge (SPEC003 5.2).</summary>
public sealed record PleasureTuning(bool Enabled, float GainPerHit, float DecayPerSecond, float SensitivityScale)
{
    public static PleasureTuning Disabled { get; } = new(false, 0f, 0f, 0f);

    /// <summary>A gauge that cannot rise is inert, which is the shipped default (FR-233).</summary>
    public bool HasEffect => Enabled && GainPerHit > 0f;
}

/// <summary>Tuning for climaxes, the limit, and the defeat it produces (SPEC003 5.4, 5.5).</summary>
public sealed record ClimaxTuning(
    bool Enabled,
    float OverlaySeconds,
    int LimitBase,
    float LimitPerDurability,
    bool GameOverEnabled,
    bool ResetAtObeliskOnly)
{
    public static ClimaxTuning Disabled { get; } = new(false, 0f, 0, 0f, false, false);
}

/// <summary>Tuning for the one-way sensitivity track (SPEC003 5.7).</summary>
public sealed record SensitivityTuning(bool Enabled, float PerClimax, float PerSexualHit, float Cap)
{
    public static SensitivityTuning Disabled { get; } = new(false, 0f, 0f, 0f);

    public bool HasEffect => Enabled && (PerClimax > 0f || PerSexualHit > 0f);
}

/// <summary>Tuning for opening <c>BreastSuper</c> to ordinary play (SPEC003 5.8).</summary>
public sealed record BreastSuperTuning(bool Enabled, float Chance, float SensitivityThreshold)
{
    public static BreastSuperTuning Disabled { get; } = new(false, 0f, 0f);

    public bool HasEffect => Enabled && Chance > 0f;
}

/// <summary>
/// Where the pleasure ring is drawn, as fractions of the screen so it survives a resolution change
/// (SPEC003 5.4).
///
/// The defaults place it concentric with the game's own HP/MP dial and just outside it. Values are
/// exposed because the dial's position is read off a screenshot rather than from the game, and a
/// different aspect ratio may need a nudge.
/// </summary>
public sealed record PleasureOverlayLayout(
    float CentreX,
    float CentreY,
    float Radius,
    float Thickness,
    float FlashSeconds)
{
    public static PleasureOverlayLayout Default { get; } = new(0.282f, 0.867f, 0.115f, 0.007f, 1.5f);
}

/// <summary>The validated configuration the plugin acts on (SPEC003 6.2).</summary>
public sealed record PleasureProfile(
    bool Enabled,
    bool SuppressHp0WhileBound,
    PleasureTuning Pleasure,
    ClimaxTuning Climax,
    SensitivityTuning Sensitivity,
    BreastSuperTuning BreastSuper,
    SexualAttackClassifier Classifier,
    bool RaiseDuringDefeatPerformance,
    bool LogTransitions,
    bool ProbeMeasurements,
    bool ShowOverlay,
    PleasureOverlayLayout Overlay)
{
    public static PleasureProfile Inactive { get; } = new(
        false,
        false,
        PleasureTuning.Disabled,
        ClimaxTuning.Disabled,
        SensitivityTuning.Disabled,
        BreastSuperTuning.Disabled,
        new SexualAttackClassifier(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
        false,
        false,
        false,
        false,
        PleasureOverlayLayout.Default);

    /// <summary>
    /// True when the MOD would do anything observable. HP0 suppression alone counts: it is the
    /// requirement that ships enabled.
    /// </summary>
    public bool AnyMechanismActive =>
        Enabled
        && (SuppressHp0WhileBound || Pleasure.HasEffect || Sensitivity.HasEffect
            || BreastSuper.HasEffect || ProbeMeasurements);
}

public sealed record PleasureValidation(
    PleasureProfile Profile,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Notices);

public static class PleasureProfileFactory
{
    /// <summary>
    /// Validates raw options. An out-of-range value switches off the mechanism it belongs to and
    /// leaves the rest running, so one bad number cannot silently disable the MOD (SPEC003 9章).
    /// </summary>
    public static PleasureValidation Create(
        PleasureOptions options,
        IReadOnlyCollection<string> knownAbnormalNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var notices = new List<string>();

        if (!options.Enabled)
        {
            notices.Add("Enabled=false: no patches will be applied and no sidecar will be written.");
            return new PleasureValidation(PleasureProfile.Inactive, errors, warnings, notices);
        }

        PleasureTuning pleasure = BuildPleasure(options, errors);
        ClimaxTuning climax = BuildClimax(options, errors, warnings);
        SensitivityTuning sensitivity = BuildSensitivity(options, errors);
        BreastSuperTuning breastSuper = BuildBreastSuper(options, errors, warnings);
        SexualAttackClassifier classifier = BuildClassifier(options, knownAbnormalNames, notices);

        var profile = new PleasureProfile(
            true,
            options.SuppressHp0WhileBound,
            pleasure,
            climax,
            sensitivity,
            breastSuper,
            classifier,
            options.RaiseDuringDefeatPerformance,
            options.LogTransitions,
            options.ProbeMeasurements,
            options.ShowOverlay,
            new PleasureOverlayLayout(
                options.OverlayCentreX,
                options.OverlayCentreY,
                Math.Max(0.01f, options.OverlayRadius),
                Math.Max(0.001f, options.OverlayThickness),
                Math.Max(0.01f, options.ClimaxOverlaySeconds)));

        if (!pleasure.HasEffect)
        {
            notices.Add(
                "The pleasure gauge cannot rise: set Pleasure.PleasureGainPerHit above 0. Until the "
                + "SPEC003 付録A measurements are taken this is the intended shipped state, and only "
                + "the HP0 suppression and the probe log are active (FR-233).");
        }

        if (climax.GameOverEnabled && !pleasure.HasEffect)
        {
            warnings.Add(
                "Climax.EnableClimaxGameOver is on but the pleasure gauge cannot rise, so no climax "
                + "can ever occur and the limit can never be reached.");
        }

        return new PleasureValidation(profile, errors, warnings, notices);
    }

    private static PleasureTuning BuildPleasure(PleasureOptions options, List<string> errors)
    {
        var failed = false;
        foreach ((string key, float value) in new[]
                 {
                     ("PleasureGainPerHit", options.PleasureGainPerHit),
                     ("PleasureDecayPerSecond", options.PleasureDecayPerSecond),
                     ("SensitivityGainScale", options.SensitivityGainScale),
                 })
        {
            if (value < 0f)
            {
                errors.Add($"Pleasure.{key} is {value}; negative values are not defined. The pleasure gauge is disabled.");
                failed = true;
            }
        }

        return failed
            ? PleasureTuning.Disabled
            : new PleasureTuning(
                true,
                options.PleasureGainPerHit,
                options.PleasureDecayPerSecond,
                options.SensitivityGainScale);
    }

    private static ClimaxTuning BuildClimax(
        PleasureOptions options,
        List<string> errors,
        List<string> warnings)
    {
        var failed = false;
        if (options.ClimaxOverlaySeconds < 0f)
        {
            errors.Add($"Climax.ClimaxOverlaySeconds is {options.ClimaxOverlaySeconds}; the climax mechanism is disabled.");
            failed = true;
        }

        if (options.ClimaxLimitPerDurability < 0f)
        {
            errors.Add($"Climax.ClimaxLimitPerDurability is {options.ClimaxLimitPerDurability}; the climax mechanism is disabled.");
            failed = true;
        }

        if (options.ClimaxLimitBase < 0)
        {
            errors.Add($"Climax.ClimaxLimitBase is {options.ClimaxLimitBase}; the climax mechanism is disabled.");
            failed = true;
        }

        if (failed)
        {
            return ClimaxTuning.Disabled;
        }

        // A limit of zero would make the very first hold fatal before a climax could happen, which
        // is never what a base of zero is meant to express (SPEC003 5.5).
        if (options.EnableClimaxGameOver
            && ClimaxLimit.Compute(options.ClimaxLimitBase, options.ClimaxLimitPerDurability, 0f) <= 0
            && options.ClimaxLimitPerDurability <= 0f)
        {
            warnings.Add(
                "Climax.EnableClimaxGameOver is on but the limit computes to 0 and does not grow "
                + "with durability, so a hold would be fatal immediately. Set Climax.ClimaxLimitBase.");
        }

        return new ClimaxTuning(
            true,
            options.ClimaxOverlaySeconds,
            options.ClimaxLimitBase,
            options.ClimaxLimitPerDurability,
            options.EnableClimaxGameOver,
            options.ResetAtObeliskOnly);
    }

    private static SensitivityTuning BuildSensitivity(PleasureOptions options, List<string> errors)
    {
        var failed = false;
        foreach ((string key, float value) in new[]
                 {
                     ("SensitivityPerClimax", options.SensitivityPerClimax),
                     ("SensitivityPerSexualHit", options.SensitivityPerSexualHit),
                     ("SensitivityCap", options.SensitivityCap),
                 })
        {
            if (value < 0f)
            {
                errors.Add($"Sensitivity.{key} is {value}; negative values would let sensitivity fall, which it must never do. The sensitivity track is disabled.");
                failed = true;
            }
        }

        return failed
            ? SensitivityTuning.Disabled
            : new SensitivityTuning(
                true,
                options.SensitivityPerClimax,
                options.SensitivityPerSexualHit,
                options.SensitivityCap);
    }

    private static BreastSuperTuning BuildBreastSuper(
        PleasureOptions options,
        List<string> errors,
        List<string> warnings)
    {
        if (options.BreastSuperChance < 0f || options.BreastSuperChance > 1f)
        {
            errors.Add($"BreastSuper.BreastSuperChance is {options.BreastSuperChance}; it must be between 0 and 1. BreastSuper is disabled.");
            return BreastSuperTuning.Disabled;
        }

        if (options.BreastSuperSensitivityThreshold < 0f)
        {
            errors.Add($"BreastSuper.BreastSuperSensitivityThreshold is {options.BreastSuperSensitivityThreshold}; BreastSuper is disabled.");
            return BreastSuperTuning.Disabled;
        }

        if (options.BreastSuperChance > 0f)
        {
            warnings.Add(
                "BreastSuper can now occur in ordinary play. It is authored for an event, so check "
                + "in game that it can be cured and that its dialogue and portrait behave "
                + "(SPEC003 付録A A-10).");
        }

        return new BreastSuperTuning(true, options.BreastSuperChance, options.BreastSuperSensitivityThreshold);
    }

    private static SexualAttackClassifier BuildClassifier(
        PleasureOptions options,
        IReadOnlyCollection<string> known,
        List<string> notices)
    {
        var knownSet = new HashSet<string>(known, StringComparer.Ordinal);
        var accepted = new List<string>();
        var unknown = new List<string>();

        foreach (string name in Split(options.SexualAbnormalTypes))
        {
            if (knownSet.Count > 0 && !knownSet.Contains(name))
            {
                unknown.Add(name);
                continue;
            }

            accepted.Add(name);
        }

        if (unknown.Count > 0)
        {
            notices.Add(
                $"Pleasure.SexualAbnormalTypes: ignored unknown status name(s) {string.Join(", ", unknown)}. "
                + "The remaining names stay in effect.");
        }

        return new SexualAttackClassifier(
            accepted,
            Split(options.SexualEnemyIds),
            Split(options.NonSexualEnemyIds),
            Split(options.SexualSenderNames),
            Split(options.NonSexualSenderNames));
    }

    private static string[] Split(string? raw) =>
        (raw ?? string.Empty)
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
}
