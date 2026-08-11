namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// Raw configuration as read from BepInEx, before validation (SPEC003 6章).
///
/// Every tuning value ships at a no-change setting, with no exception (SPEC003 FR-233).
/// <see cref="SuppressSexualHpDamage"/> is on from the start but does nothing on its own: the
/// suppression only takes effect once the gauge can rise, so a fresh install still changes nothing
/// (FR-278).
/// </summary>
public sealed record PleasureOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether a sexual hit taken while bound leaves HP untouched (SPEC003 5.1).
    ///
    /// v1.1 replaced v1.0's <c>SuppressHp0WhileBound</c>, which clamped HP at 1 and let damage keep
    /// arriving. Clamping left HP as the instrument the player read the danger from, and left the
    /// moment of defeat decided by how much of it was left (DEC-256).
    /// </summary>
    public bool SuppressSexualHpDamage { get; init; } = true;

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
    /// What one unit of corruption becomes once the mark is permanent (SPEC005 5.5).
    ///
    /// The crest's own flavour is that the body has been made sensitive, so it is applied to the
    /// rate rather than as a one-off: a marked body learns faster. This is also what makes an enemy
    /// putting the crest on an uncorrupted player a serious event rather than a cosmetic one —
    /// nothing has been lost yet, but everything from here costs more.
    ///
    /// This is the far side of the cliff. The curse stages below it use
    /// <see cref="CorruptionCurseGainMax"/> instead, and must stay under this (FR-420).
    /// </summary>
    public float CorruptionCrestGainScale { get; init; } = 2f;

    /// <summary>
    /// What the last reversible curse stock adds to the rate (SPEC005 5.5.1).
    ///
    /// Ships at 0, which leaves the curse stages exactly as they were: no acceleration at all until
    /// the 付録A A-406 measurement says how much of one the player can still outrun. The whole
    /// value of the curse being a warning rests on this staying small — it is the gain of a loop
    /// that feeds itself, because more corruption means more stocks and more stocks mean more
    /// corruption (SPEC005 14.3).
    /// </summary>
    public float CorruptionCurseGainMax { get; init; }

    /// <summary>
    /// What pleasure gain is multiplied by once the mark is permanent (SPEC005 5.2, FR-408).
    ///
    /// Fixed rather than staged, and deliberately so. How far the body has gone is already carried
    /// by <see cref="CorruptionGainScale"/>; a second term that also grew with the stage would be
    /// the same idea managed in two places (DEC-408).
    ///
    /// The one tuning value that is not inert on a fresh install. It is a settled figure rather
    /// than one waiting on a measurement, so it applies from the moment the MOD is added — set it
    /// to 1 to turn it off.
    /// </summary>
    public float CrestPleasureGainScale { get; init; } = 1.25f;

    /// <summary>Whether the succubus regeneration buff is active at all (SPEC005 5.1).</summary>
    public bool RegenEnabled { get; init; } = true;

    /// <summary>
    /// Seconds of regeneration one qualifying climax adds. 0 never grants the buff.
    ///
    /// 15 seconds is a settled figure rather than one waiting on a measurement (利用者決定
    /// 2026-08-10): the shipped-inert default (0) was the reason the buff went unnoticed in the
    /// first verification pass — "0 never grants it" is correct behaviour, but it reads
    /// indistinguishably from a mechanism that is broken. Fifteen seconds against a limit around
    /// five climaxes (base 3 + durability) is long enough inside one hold to matter, short enough
    /// that it does not carry past the next one uninvited.
    /// </summary>
    public float RegenDurationPerClimax { get; init; } = 15f;

    /// <summary>
    /// A ceiling on the banked duration. 0 means no ceiling was asked for, which is the shipped
    /// reading: climaxing repeatedly banks time rather than merely refreshing it (DEC-403).
    /// </summary>
    public float RegenDurationCap { get; init; }

    /// <summary>
    /// HP restored per second while the buff runs.
    ///
    /// 2/s against a maximum around 100 empties the deficit in roughly a minute rather than at
    /// once — felt, not decisive (利用者決定 2026-08-10).
    /// </summary>
    public float HpRegenPerSecond { get; init; } = 2f;

    /// <summary>MP restored per second while the buff runs. See <see cref="HpRegenPerSecond"/>.</summary>
    public float MpRegenPerSecond { get; init; } = 2f;

    /// <summary>
    /// Whether an empty MP bar makes acting unreliable (SPEC005 5.3).
    ///
    /// Off until 付録A A-401 settles how the game's own no-MP stagger is played from outside. The
    /// rule is implemented and tested; what is missing is the animation path, and a penalty that
    /// decides to fire and then cannot show anything is worse than one that never fires.
    /// </summary>
    public bool MpPenaltyEnabled { get; init; }

    /// <summary>
    /// The share of the cap the corruption must reach before the penalty applies (SPEC005 5.3).
    ///
    /// Held together with the crest by an AND. An enemy can put the crest on a barely-corrupted
    /// player, and punishing that player for a state they were handed rather than earned is not
    /// what the penalty is for (DEC-405).
    ///
    /// The whole cap, not half of it (利用者決定 2026-08-10). Losing the reliability of your own
    /// hands is the deepest thing the corruption does, and it belongs at the bottom of the track
    /// rather than at the point the mark first appears — by which the player still has half the
    /// track left to fall.
    /// </summary>
    public float MpPenaltyCorruptionFraction { get; init; } = 1f;

    /// <summary>
    /// Chance, per press of a trigger input, that the press staggers (SPEC005 5.3).
    ///
    /// Roughly one press in five (利用者決定 2026-08-10). Acting still works; acting in front of
    /// something is a gamble. The verification pass ran at 1.0 and read as constant rather than
    /// unpredictable, which is the wrong feeling for a penalty whose whole description is
    /// "予測不能なタイミングで硬直が発生する". The cooldown bounds it from the other side: whatever
    /// this is set to, staggers cannot chain.
    /// </summary>
    public float StunChance { get; init; } = 0.2f;

    /// <summary>
    /// The share of the MP bar below which acting becomes unreliable (SPEC005 5.3 適用条件3).
    ///
    /// A fifth of the bar rather than exactly nothing (利用者決定 2026-08-10). Empty is a state
    /// the player passes through rather than sits in, so a penalty keyed to it is met by accident
    /// and never planned around; a threshold gives the approach to empty a meaning of its own.
    /// 0 restores the original "only at zero" reading.
    /// </summary>
    public float MpPenaltyMpFraction { get; init; } = 0.2f;

    public float StunCooldownSeconds { get; init; } = 3f;

    public string StunTriggerInputs { get; init; } = string.Join(",", StunInputs.Defaults);

    /// <summary>Whether the haze is drawn as the curse advances (SPEC005 5.4).</summary>
    public bool CrestFxEnabled { get; init; } = true;

    public float CrestFxDurationSeconds { get; init; } = 1.2f;

    public float CrestFxIntensityPerStage { get; init; } = 0.2f;

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
