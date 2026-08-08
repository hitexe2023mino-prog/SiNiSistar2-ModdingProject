namespace SiNiSistar2.Pleasure.Core;

/// <summary>Tuning for the pleasure gauge (SPEC003 5.2).</summary>
public sealed record PleasureTuning(bool Enabled, float GainPerHit, float DecayPerSecond, float CorruptionScale)
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

/// <summary>Tuning for the one-way corruption track (SPEC003 5.7).</summary>
public sealed record CorruptionTuning(bool Enabled, float PerClimax, float PerSexualHit, float Cap)
{
    public static CorruptionTuning Disabled { get; } = new(false, 0f, 0f, 0f);

    public bool HasEffect => Enabled && (PerClimax > 0f || PerSexualHit > 0f);
}

/// <summary>Tuning for opening <c>BreastSuper</c> to ordinary play (SPEC003 5.8).</summary>
public sealed record BreastSuperTuning(
    bool Enabled,
    int ApplicationsAtMaxLevel,
    float CorruptionThreshold,
    bool ReplaceBreast,
    bool MakeHaanjaCurable,
    bool CountBelowMaxLevel,
    bool CuredWithBreast,
    float FadeSeconds,
    float MilkPerSexualHit,
    float MilkDrainPerSecond)
{
    public static BreastSuperTuning Disabled { get; } =
        new(false, 0, 0f, false, false, false, false, 0f, 0f, 0f);

    /// <summary>Whether the escalation has any way out at all.</summary>
    public bool CanMilk => MilkDrainPerSecond > 0f;

    /// <summary>A count of zero means the escalation can never be reached, which is the shipped state.</summary>
    public bool HasEffect => Enabled && ApplicationsAtMaxLevel > 0;
}

/// <summary>
/// Where one overlay element sits, as fractions of the screen so it survives a resolution change.
///
/// The vertical position is measured up from the bottom edge because the game's HUD is anchored
/// there; measuring down from the top moved every element as soon as the window was not the height
/// the values were chosen on.
/// </summary>
public sealed record OverlayPlacement(float CentreX, float BottomOffset, float Size);

/// <summary>
/// Where the overlay's elements sit (SPEC003 5.4).
///
/// Each element carries its own placement. They mark different things — one is the state of the
/// current hold, the other is how much of the run is left — and a HUD that suits one screen rarely
/// wants both in the same relationship on another.
/// </summary>
public sealed record PleasureOverlayLayout(
    OverlayPlacement Gauge,
    OverlayPlacement Cross,
    OverlayPlacement Milk,
    float FlashSeconds,
    bool ShowCross)
{
    public static PleasureOverlayLayout Default { get; } = new(
        new OverlayPlacement(0.283f, 0.115f, 0.086f),
        new OverlayPlacement(0.283f, 0.275f, 0.129f),
        new OverlayPlacement(0.375f, 0.135f, 0.070f),
        1.5f,
        true);
}

/// <summary>The validated configuration the plugin acts on (SPEC003 6.2).</summary>
public sealed record PleasureProfile(
    bool Enabled,
    bool SuppressHp0WhileBound,
    PleasureTuning Pleasure,
    ClimaxTuning Climax,
    CorruptionTuning Corruption,
    BreastSuperTuning BreastSuper,
    SexualAttackClassifier Classifier,
    bool RaiseDuringDefeatPerformance,
    bool LogTransitions,
    bool LogAllStatusChanges,
    bool EnableDebugKeys,
    bool ProbeMeasurements,
    bool ShowOverlay,
    PleasureOverlayLayout Overlay)
{
    public static PleasureProfile Inactive { get; } = new(
        false,
        false,
        PleasureTuning.Disabled,
        ClimaxTuning.Disabled,
        CorruptionTuning.Disabled,
        BreastSuperTuning.Disabled,
        new SexualAttackClassifier(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
        false,
        false,
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
        && (SuppressHp0WhileBound || Pleasure.HasEffect || Corruption.HasEffect
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
        IReadOnlyCollection<string> knownAbnormalNames,
        IEnemyAttackOverrides? enemies = null)
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
        CorruptionTuning corruption = BuildCorruption(options, errors);
        BreastSuperTuning breastSuper = BuildBreastSuper(options, errors, warnings, notices);
        SexualAttackClassifier classifier = BuildClassifier(options, knownAbnormalNames, enemies, notices);
        var profile = new PleasureProfile(
            true,
            options.SuppressHp0WhileBound,
            pleasure,
            climax,
            corruption,
            breastSuper,
            classifier,
            options.RaiseDuringDefeatPerformance,
            options.LogTransitions,
            options.LogAllStatusChanges,
            options.EnableDebugKeys,
            options.ProbeMeasurements,
            options.ShowOverlay,
            new PleasureOverlayLayout(
                new OverlayPlacement(
                    options.GaugeCentreX,
                    options.GaugeBottomOffset,
                    Math.Max(0.01f, options.GaugeSize)),
                new OverlayPlacement(
                    options.CrossCentreX,
                    options.CrossBottomOffset,
                    Math.Max(0.01f, options.CrossSize)),
                new OverlayPlacement(
                    options.MilkCentreX,
                    options.MilkBottomOffset,
                    Math.Max(0.01f, options.MilkSize)),
                Math.Max(0.01f, options.ClimaxOverlaySeconds),
                options.ShowCross));

        if (!pleasure.HasEffect)
        {
            notices.Add(
                "The pleasure gauge cannot rise: set Pleasure.PleasureGainPerHit above 0. Until the "
                + "SPEC003 付録A measurements are taken this is the intended shipped state, and only "
                + "the HP0 suppression and the probe log are active (FR-233).");
        }

        if (!breastSuper.HasEffect)
        {
            notices.Add(
                "BreastSuper will never be reached: set BreastSuper.BreastSuperAfterApplications "
                + "above 0. Until then Breast behaves exactly as the game intends it.");
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
                     ("CorruptionGainScale", options.CorruptionGainScale),
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
                options.CorruptionGainScale);
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

    private static CorruptionTuning BuildCorruption(PleasureOptions options, List<string> errors)
    {
        var failed = false;
        foreach ((string key, float value) in new[]
                 {
                     ("CorruptionPerClimax", options.CorruptionPerClimax),
                     ("CorruptionPerSexualHit", options.CorruptionPerSexualHit),
                     ("CorruptionCap", options.CorruptionCap),
                 })
        {
            if (value < 0f)
            {
                errors.Add($"Corruption.{key} is {value}; negative values would let corruption fall, which it must never do. The corruption track is disabled.");
                failed = true;
            }
        }

        return failed
            ? CorruptionTuning.Disabled
            : new CorruptionTuning(
                true,
                options.CorruptionPerClimax,
                options.CorruptionPerSexualHit,
                options.CorruptionCap);
    }

    private static BreastSuperTuning BuildBreastSuper(
        PleasureOptions options,
        List<string> errors,
        List<string> warnings,
        List<string> notices)
    {
        if (options.BreastSuperAfterApplications < 0)
        {
            errors.Add(
                $"BreastSuper.BreastSuperAfterApplications is {options.BreastSuperAfterApplications}; "
                + "a negative count has no meaning. BreastSuper is disabled.");
            return BreastSuperTuning.Disabled;
        }

        if (options.BreastSuperCorruptionThreshold < 0f)
        {
            errors.Add($"BreastSuper.BreastSuperCorruptionThreshold is {options.BreastSuperCorruptionThreshold}; BreastSuper is disabled.");
            return BreastSuperTuning.Disabled;
        }

        if (options.BreastSuperAfterApplications > 0)
        {
            warnings.Add(
                "BreastSuper can now occur in ordinary play. It is authored for an event, so check "
                + "in game that it can be cured and that its dialogue and portrait behave "
                + "(SPEC003 付録A A-10).");
        }

        if (options.BreastSuperMakeHaanjaCurable)
        {
            warnings.Add(
                "BreastSuper.MakeHaanjaCurable is on: the MOD will mark BreastSuper as curable by "
                + "Haanja. This changes a value on the game's own AbnormalData for the session and "
                + "is undone on unload. Confirm in game that the cure actually completes "
                + "(SPEC003 付録A A-14).");
        }

        if (options.BreastSuperCountBelowMaxLevel)
        {
            notices.Add(
                "BreastSuper.CountBelowMaxLevel is on: every Breast application counts, including "
                + "the ones that only raise the level. This is a debugging aid and departs from "
                + "SPEC003 5.8.");
        }

        return new BreastSuperTuning(
            true,
            options.BreastSuperAfterApplications,
            options.BreastSuperCorruptionThreshold,
            options.BreastSuperReplacesBreast,
            options.BreastSuperMakeHaanjaCurable,
            options.BreastSuperCountBelowMaxLevel,
            options.BreastSuperCuredWithBreast,
            Math.Max(0f, options.BreastSuperFadeSeconds),
            Math.Max(0f, options.MilkPerSexualHit),
            Math.Max(0f, options.MilkDrainPerSecond));
    }

    private static SexualAttackClassifier BuildClassifier(
        PleasureOptions options,
        IReadOnlyCollection<string> known,
        IEnemyAttackOverrides? enemies,
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

        // The catalogue file is the authority when there is one. The config lists survive only as
        // the seed for a catalogue being created for the first time, so an existing configuration
        // is not lost on upgrade (FR-235).
        IEnemyAttackOverrides overrides = enemies
            ?? new FixedEnemyAttackOverrides(
                Split(options.SexualEnemyIds),
                Split(options.NonSexualEnemyIds));

        return new SexualAttackClassifier(
            accepted,
            overrides,
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
