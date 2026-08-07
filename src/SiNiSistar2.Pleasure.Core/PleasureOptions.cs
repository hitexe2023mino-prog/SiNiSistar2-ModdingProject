namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// Raw configuration as read from BepInEx, before validation (SPEC003 6章).
///
/// Every tuning value ships at a no-change setting. The one exception is
/// <see cref="SuppressHp0WhileBound"/>: removing the HP0 defeat is the requirement itself and has
/// no value to tune, so it is on from the start (SPEC003 FR-233).
/// </summary>
public sealed record PleasureOptions
{
    public bool Enabled { get; init; } = true;

    public bool SuppressHp0WhileBound { get; init; } = true;

    public float PleasureGainPerHit { get; init; }

    public float PleasureDecayPerSecond { get; init; }

    public string SexualAbnormalTypes { get; init; } = string.Join(",", SexualAbnormalDefaults.Types);

    public string SexualEnemyIds { get; init; } = string.Join(",", SexualAbnormalDefaults.SexualEnemyIds);

    public string NonSexualEnemyIds { get; init; } = string.Empty;

    /// <summary>
    /// Damage-sender names always treated as sexual, matched as case-insensitive substrings. This
    /// is how an attacker that never binds the player is reached at all (SPEC003 5.3).
    /// </summary>
    public string SexualSenderNames { get; init; } = string.Join(",", SexualAbnormalDefaults.SenderNames);

    public string NonSexualSenderNames { get; init; } = string.Empty;

    /// <summary>
    /// Whether pleasure keeps rising during a defeat performance. Sexual attacks otherwise only
    /// happen while bound, but some defeat performances go on delivering them (SPEC003 5.2).
    /// </summary>
    public bool RaiseDuringDefeatPerformance { get; init; } = true;

    public float ClimaxOverlaySeconds { get; init; } = 1.5f;

    public int ClimaxLimitBase { get; init; }

    public float ClimaxLimitPerDurability { get; init; }

    public bool EnableClimaxGameOver { get; init; }

    public bool ResetAtObeliskOnly { get; init; }

    public float SensitivityPerClimax { get; init; }

    public float SensitivityPerSexualHit { get; init; }

    public float SensitivityGainScale { get; init; }

    public float SensitivityCap { get; init; } = 10f;

    public float BreastSuperChance { get; init; }

    public float BreastSuperSensitivityThreshold { get; init; }

    public bool LogTransitions { get; init; }

    /// <summary>
    /// Records what the 付録A measurements need, each distinct finding once. On by default because
    /// the MOD ships in a state where measuring is the only thing it can usefully do.
    /// </summary>
    public bool ProbeMeasurements { get; init; } = true;

    /// <summary>Draws the pleasure gauge, sensitivity and climax count on screen.</summary>
    public bool ShowOverlay { get; init; } = true;

    /// <summary>Ring centre as a fraction of screen width. Defaults to the game's HP/MP dial.</summary>
    public float OverlayCentreX { get; init; } = PleasureOverlayLayout.Default.CentreX;

    /// <summary>Ring centre as a fraction of screen height.</summary>
    public float OverlayCentreY { get; init; } = PleasureOverlayLayout.Default.CentreY;

    /// <summary>Ring radius as a fraction of screen height, just outside the dial.</summary>
    public float OverlayRadius { get; init; } = PleasureOverlayLayout.Default.Radius;

    /// <summary>Ring thickness as a fraction of screen height.</summary>
    public float OverlayThickness { get; init; } = PleasureOverlayLayout.Default.Thickness;

    /// <summary>Shows the cross that breaks as the climax limit is approached.</summary>
    public bool ShowCross { get; init; } = true;
}

/// <summary>
/// Statuses treated as evidence of a sexual attack (SPEC003 6.1).
///
/// Deliberately not the same set as SPEC002's pleasure group: that one means "the will to resist
/// is eroded", this one means "a sexual act happened". <c>Defilement</c> is excluded there to keep
/// off the game's own escape axis, and included here because it is the most direct indicator there
/// is (SPEC003 DEC-210).
/// </summary>
public static class SexualAbnormalDefaults
{
    public static readonly IReadOnlyList<string> Types = new[]
    {
        "Lustfull",
        "Lustfull_Forever",
        "LustMarkCurse",
        "Defilement",
        "Semen",
        "Semen_mucus",
        "Pregnant",
        "Pregnant_Demi",
        "Breast",
        "BreastSuper",
        "Milk",
        "WetNurse",
        "MindControl",
        "MindIntegration",
        "Infertility",
        "InfertilityBlessing",
    };

    /// <summary>
    /// Attackers named as sexual regardless of what they inflict. The art gallery picture frame is
    /// here because it never binds the player, so no captor-based rule can ever see it.
    /// </summary>
    public static readonly IReadOnlyList<string> SenderNames = new[]
    {
        "PictureFrame",
    };

    /// <summary>
    /// Captors whose every attack counts as sexual, whatever it inflicts. Measured on the target
    /// build: the picture frame lands both a Defilement-bearing attack and one carrying no statuses
    /// at all, so the status test alone classifies the same enemy inconsistently.
    /// </summary>
    public static readonly IReadOnlyList<string> SexualEnemyIds = new[]
    {
        "GaID_PictureFrameBig",
    };
}
