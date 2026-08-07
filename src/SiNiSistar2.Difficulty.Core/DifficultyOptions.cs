namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// The MOD's own difficulty step. The game's <c>GameDifficulty</c> is an IL2CPP enum and cannot
/// gain a fourth value at runtime, so <c>Nightmare</c> lives here and the game is only ever told
/// that it is running <c>Hard</c> (SPEC002 DEC-101).
/// </summary>
public enum DifficultyTier
{
    /// <summary>No patches are applied at all (SPEC002 FR-125).</summary>
    Off,

    /// <summary>Hard data is reported as active and the three mechanisms apply.</summary>
    Nightmare,
}

/// <summary>
/// The raw configuration as read from BepInEx, before validation. Values are deliberately not
/// clamped here: 6.2 requires that an out-of-range value is reported and disables its mechanism,
/// which cannot be distinguished from a legitimate value once it has been silently corrected.
/// </summary>
public sealed record DifficultyOptions
{
    public DifficultyTier Tier { get; init; } = DifficultyTier.Nightmare;

    public bool ForceHardData { get; init; } = true;

    public bool AbnormalEnabled { get; init; } = true;

    /// <summary>
    /// Multiplier applied to the status-ailment application rate for player-received damage.
    /// Defaults to no change until A-4 is measured (SPEC002 FR-128).
    /// </summary>
    public float AbnormalRateMultiplier { get; init; } = 1f;

    /// <summary>Extra levels advanced right after a status lands on the player. 0 is no change.</summary>
    public int LevelBonus { get; init; }

    public bool PleasureEnabled { get; init; } = true;

    public string PleasureAbnormalTypes { get; init; } = string.Join(",", AbnormalTypeDefaults.Pleasure);

    /// <summary>Gap between the end of one nullification window and the start of the next.</summary>
    public float NullificationIntervalSeconds { get; init; }

    public float NullificationIntervalJitter { get; init; } = 0.5f;

    /// <summary>Length of one nullification window. 0 disables the window entirely (FR-128).</summary>
    public float NullificationDurationSeconds { get; init; }

    public float NullificationDurationJitter { get; init; } = 0.3f;

    public float PleasureLevelScaling { get; init; }

    public float NullificationDutyWarnThreshold { get; init; } = 0.6f;

    public bool BurdenEnabled { get; init; } = true;

    public string BurdenAbnormalTypes { get; init; } = string.Join(",", AbnormalTypeDefaults.Burden);

    /// <summary>Length of the post-escape recovery window. 0 disables it (FR-128).</summary>
    public float RecoveryPenaltySeconds { get; init; }

    public float RecoveryMoveSlowRate { get; init; }

    public float RecoveryInvincibleScale { get; init; } = 1f;

    public float BurdenLevelScaling { get; init; }

    public bool LogInterventions { get; init; }
}
