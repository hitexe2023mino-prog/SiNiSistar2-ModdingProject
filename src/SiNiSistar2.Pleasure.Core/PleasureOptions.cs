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

    public float CorruptionPerClimax { get; init; }

    public float CorruptionPerSexualHit { get; init; }

    public float CorruptionGainScale { get; init; }

    /// <summary>
    /// The most corruption that can be accumulated.
    ///
    /// Twelve rather than ten so the two things it drives land on whole numbers: the drawn mark has
    /// six parts (every 2) and the crest has four stocks above the halfway threshold (every 2 from
    /// 6). A scale where the boundaries fall between values is a scale that reads as jitter.
    /// </summary>
    public float CorruptionCap { get; init; } = 12f;

    /// <summary>
    /// How many further <c>Breast</c> applications, arriving while it is already at its maximum
    /// level, escalate to <c>BreastSuper</c>. 0 means never, which is the shipped state.
    /// </summary>
    public int BreastSuperAfterApplications { get; init; }

    public float BreastSuperCorruptionThreshold { get; init; }

    /// <summary>Whether <c>Breast</c> is removed as <c>BreastSuper</c> is applied.</summary>
    public bool BreastSuperReplacesBreast { get; init; } = true;

    /// <summary>
    /// Whether the game's own <c>Breast</c> cure also removes <c>BreastSuper</c>. The cure is an
    /// authored list of statuses and the escalated one is not in it, so without this the MOD would
    /// have added a status the game cannot take away.
    /// </summary>
    public bool BreastSuperCuredWithBreast { get; init; } = true;

    /// <summary>Seconds of black over the transition. 0 draws nothing.</summary>
    public float BreastSuperFadeSeconds { get; init; } = 0.8f;

    /// <summary>
    /// Seconds of self-milking before the swelling steps down. 0 switches the key off.
    ///
    /// The duration is the whole mechanism. An instant cure on a key is a menu; one that takes
    /// seconds and is wasted if anything hits you is a decision about whether it is safe enough
    /// right now, which the player can get wrong.
    /// </summary>
    /// <summary>
    /// Milk gained from one sexual hit taken while swollen. 0 means the gauge never fills.
    /// </summary>
    public float MilkPerSexualHit { get; init; } = 0.12f;

    /// <summary>
    /// Milk the body works off per second while the escalation is worn. 0 leaves it with no way out.
    ///
    /// This is the escalation's whole duration, and it is deliberately not a duration. The default
    /// empties a full gauge in about fifty seconds if nothing lands, and every sexual hit taken
    /// meanwhile puts roughly <c>MilkPerSexualHit</c> back — so a player who cannot get clear is
    /// not counting down, they are losing ground. That is the penalty (FR-264).
    /// </summary>
    public float MilkDrainPerSecond { get; init; } = 0.02f;

    /// <summary>
    /// Counts every <c>Breast</c> application rather than only those at the maximum level. A
    /// debugging aid: it makes the escalation reachable by using the item that applies swelling a
    /// few times, without having to reach the ceiling first.
    /// </summary>
    public bool BreastSuperCountBelowMaxLevel { get; init; }

    /// <summary>
    /// Whether to mark <c>BreastSuper</c> curable by Haanja, so the game's existing cure event
    /// covers it. Off until the in-game check in 付録A A-14 has been made.
    /// </summary>
    public bool BreastSuperMakeHaanjaCurable { get; init; }

    public bool LogTransitions { get; init; }

    /// <summary>
    /// Records every status added to anyone, every time, with no de-duplication. The one-shot probe
    /// records each status name once, so a status the save restored at load is never reported again
    /// — which made an item that applies it look as though it did nothing at all.
    /// </summary>
    public bool LogAllStatusChanges { get; init; }

    /// <summary>Enables F11, which applies <c>Breast</c> to the player through the game's own path.</summary>
    public bool EnableDebugKeys { get; init; }

    /// <summary>
    /// Records what the 付録A measurements need, each distinct finding once. On by default because
    /// the MOD ships in a state where measuring is the only thing it can usefully do.
    /// </summary>
    public bool ProbeMeasurements { get; init; } = true;

    /// <summary>Draws the pleasure gauge, corruption and climax count on screen.</summary>
    public bool ShowOverlay { get; init; } = true;

    public float GaugeCentreX { get; init; } = PleasureOverlayLayout.Default.Gauge.CentreX;

    public float GaugeBottomOffset { get; init; } = PleasureOverlayLayout.Default.Gauge.BottomOffset;

    public float GaugeSize { get; init; } = PleasureOverlayLayout.Default.Gauge.Size;

    public float CrossCentreX { get; init; } = PleasureOverlayLayout.Default.Cross.CentreX;

    public float CrossBottomOffset { get; init; } = PleasureOverlayLayout.Default.Cross.BottomOffset;

    public float CrossSize { get; init; } = PleasureOverlayLayout.Default.Cross.Size;

    /// <summary>
    /// The fraction of the cap at which the game's own lust crest is put on the player. 0 never
    /// puts it on.
    ///
    /// The mark the HUD draws is the MOD's own picture of the corruption. This is the point where
    /// the game's status of the same name is actually applied, so the picture stops being a picture
    /// (SPEC003 FR-267).
    /// </summary>
    public float CorruptionCrestAtFraction { get; init; } = 0.5f;

    /// <summary>
    /// What one unit of corruption becomes while the lust crest is worn.
    ///
    /// The crest's own flavour is that the body has been made sensitive, so it is applied to the
    /// rate rather than as a one-off: a marked body learns faster. This is also what makes an enemy
    /// putting the crest on an uncorrupted player a serious event rather than a cosmetic one —
    /// nothing has been lost yet, but everything from here costs more.
    /// </summary>
    public float CorruptionCrestGainScale { get; init; } = 2f;

    /// <summary>
    /// Seconds the camera keeps moving after a climax. 0 leaves the camera alone.
    ///
    /// Shorter than the haze on purpose. The haze says what happened and can linger; the shake is
    /// the moment itself, and a camera that keeps moving after the moment has passed reads as a
    /// fault rather than a feeling.
    /// </summary>
    public float ClimaxShakeSeconds { get; init; } = 0.45f;

    /// <summary>
    /// How far the camera moves at the height of the shake, as a fraction of what is on screen.
    ///
    /// A fraction rather than world units: world units mean nothing without knowing how much of the
    /// world is visible, and the first version was set in them at a value that turned out to be
    /// under a percent of the frame. Still small — this is a game the player has to keep reading
    /// while it happens, and the shake is there to be felt rather than watched.
    /// </summary>
    public float ClimaxShakeStrength { get; init; } = 0.035f;

    /// <summary>Shows the cross that breaks as the climax limit is approached.</summary>
    public bool ShowCross { get; init; } = true;

    public float MilkCentreX { get; init; } = PleasureOverlayLayout.Default.Milk.CentreX;

    public float MilkBottomOffset { get; init; } = PleasureOverlayLayout.Default.Milk.BottomOffset;

    public float MilkSize { get; init; } = PleasureOverlayLayout.Default.Milk.Size;

    public float CrestCentreX { get; init; } = PleasureOverlayLayout.Default.Crest.CentreX;

    public float CrestBottomOffset { get; init; } = PleasureOverlayLayout.Default.Crest.BottomOffset;

    public float CrestSize { get; init; } = PleasureOverlayLayout.Default.Crest.Size;
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
