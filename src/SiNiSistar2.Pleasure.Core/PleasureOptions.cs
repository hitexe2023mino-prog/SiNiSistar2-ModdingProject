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

    /// <summary>
    /// How many further <c>Breast</c> applications, arriving while it is already at its maximum
    /// level, escalate to <c>BreastSuper</c>. 0 means never, which is the shipped state.
    /// </summary>
    public int BreastSuperAfterApplications { get; init; }

    public float BreastSuperSensitivityThreshold { get; init; }

    /// <summary>Whether <c>Breast</c> is removed as <c>BreastSuper</c> is applied.</summary>
    public bool BreastSuperReplacesBreast { get; init; } = true;

    /// <summary>
    /// Seconds <c>BreastSuper</c> lasts before subsiding back to <c>Breast</c>. 0 never subsides.
    /// </summary>
    public float BreastSuperSeconds { get; init; }

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

    /// <summary>Milk removed per second while milking. 0 switches milking off.</summary>
    public float MilkDrainPerSecond { get; init; } = 0.25f;

    /// <summary>
    /// The key that milks, as a <c>KeyCode</c> name.
    ///
    /// Not C, which the game already uses to cast. Immediate-mode GUI cannot stop the game reading
    /// the keyboard for itself — it only consumes the event within its own layer — so a key shared
    /// with an action would fire both, and "only while swollen" cannot be arranged from here.
    /// </summary>
    public string MilkingKey { get; init; } = "F8";

    /// <summary>
    /// The animation clip played while milking. Empty leaves the animator alone.
    ///
    /// It is the game's own milking clip, played in the situation the game plays it in, so what any
    /// observer sees is true: milking really is happening. Naming it in the config rather than
    /// hard-coding it means a build that calls it something else is a config edit, not a rebuild.
    ///
    /// Empty until the clip is known. <c>ResumeBreast</c> is not a clip at all: the gallery
    /// records it with a length of zero, which is the reading for a take played by an EventPlayer
    /// — a scripted performance rather than an animator state (付録A A-25). Nothing may be put here
    /// on a guess. A neighbouring clip that happens to be loaded nearby is exactly the substitute
    /// DEC-224 forbids, and it would make an observer read the wrong event.
    /// </summary>
    public string MilkingAnimationState { get; init; } = "";

    /// <summary>
    /// The animator state whose clip is replaced by the milking clip while milking.
    ///
    /// The milking take is not in the player's field controller — measured, not assumed (付録A
    /// A-23). What the field controller does have is a set of states, and an override controller
    /// can put a different clip in any of them. So the state is a vehicle: the clip that plays is
    /// the game's own milking clip, and an observer reading the animator reads that clip's name.
    /// The slot has to be a state the player is not otherwise in, or leaving milking would put
    /// them back into a pose they never chose.
    /// </summary>
    public string MilkingAnimationSlot { get; init; } = "Sit";

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

    /// <summary>Draws the pleasure gauge, sensitivity and climax count on screen.</summary>
    public bool ShowOverlay { get; init; } = true;

    public float GaugeCentreX { get; init; } = PleasureOverlayLayout.Default.Gauge.CentreX;

    public float GaugeBottomOffset { get; init; } = PleasureOverlayLayout.Default.Gauge.BottomOffset;

    public float GaugeSize { get; init; } = PleasureOverlayLayout.Default.Gauge.Size;

    public float CrossCentreX { get; init; } = PleasureOverlayLayout.Default.Cross.CentreX;

    public float CrossBottomOffset { get; init; } = PleasureOverlayLayout.Default.Cross.BottomOffset;

    public float CrossSize { get; init; } = PleasureOverlayLayout.Default.Cross.Size;

    /// <summary>Shows the cross that breaks as the climax limit is approached.</summary>
    public bool ShowCross { get; init; } = true;

    public float MilkCentreX { get; init; } = PleasureOverlayLayout.Default.Milk.CentreX;

    public float MilkBottomOffset { get; init; } = PleasureOverlayLayout.Default.Milk.BottomOffset;

    public float MilkSize { get; init; } = PleasureOverlayLayout.Default.Milk.Size;
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
