namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// Raw configuration values as bound from BepInEx config, before validation. The validated form
/// is <see cref="SpawnProfile"/>; nothing reads these values directly at runtime (SPEC004 6章).
/// </summary>
public sealed class SpawnOptions
{
    public bool Enabled { get; init; } = true;

    public int Seed { get; init; }

    public bool DiagnosticsEnabled { get; init; }

    public float SpawnCountMultiplierMin { get; init; } = 1.0f;

    public float SpawnCountMultiplierMax { get; init; } = 1.5f;

    public float SpawnIntervalMultiplierMin { get; init; } = 0.7f;

    public float SpawnIntervalMultiplierMax { get; init; } = 1.0f;

    public float CoolTimeMultiplierMin { get; init; } = 0.7f;

    public float CoolTimeMultiplierMax { get; init; } = 1.0f;

    public float MaxSpawnMultiplierMin { get; init; } = 1.0f;

    public float MaxSpawnMultiplierMax { get; init; } = 1.5f;

    public int AdditionalSpawnCapPerVisit { get; init; } = 5;

    public int AdditionalAliveCap { get; init; } = 3;

    public float OffscreenMargin { get; init; } = 0.1f;

    public float AmbushChance { get; init; } = 0.25f;

    public float StagnationSeconds { get; init; } = 90f;

    public float StagnationWindowSeconds { get; init; } = 20f;

    public float StagnationMoveEpsilon { get; init; } = 3.0f;

    public float StagnationPenaltyInterval { get; init; } = 30f;

    public string AllowedGimmickTypes { get; init; } = string.Empty;

    public int GimmickClonesPerVisit { get; init; } = 2;

    public float GimmickCloneOffsetRange { get; init; } = 2.0f;

    public bool MimicBoxEnabled { get; init; }

    public int MimicBoxesPerVisit { get; init; } = 2;

    public float MimicChance { get; init; } = 0.5f;

    public string RewardTable { get; init; } = "PortionHP:1:1,PortionMP:1:1";

    public int RewardLootValue { get; init; } = 2;

    public bool LogInterventions { get; init; } = true;

    /// <summary>Initial HUD stage (SPEC004 5.8). Off by default: normal play draws nothing.</summary>
    public HudMode HudMode { get; init; } = HudMode.Off;

    /// <summary>
    /// Whether the debug panel's commands act. Off by default so a stray keypress during normal
    /// play cannot spawn anything; the panel stays readable either way (FR-331).
    /// </summary>
    public bool DebugCommandsEnabled { get; init; }
}
