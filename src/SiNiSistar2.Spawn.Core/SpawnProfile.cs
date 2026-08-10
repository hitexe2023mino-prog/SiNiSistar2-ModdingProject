namespace SiNiSistar2.Spawn.Core;

/// <summary>The effective, validated settings for one area visit after override resolution (SPEC004 5.1).</summary>
public sealed record AreaSettings
{
    public bool Excluded { get; init; }

    public MultiplierRange SpawnCount { get; init; }

    public MultiplierRange SpawnInterval { get; init; }

    public MultiplierRange CoolTime { get; init; }

    public MultiplierRange MaxSpawn { get; init; }

    public int AdditionalSpawnCapPerVisit { get; init; }

    public int AdditionalAliveCap { get; init; }

    public float AmbushChance { get; init; }

    public float StagnationSeconds { get; init; }

    public float StagnationPenaltyInterval { get; init; }

    public bool TuningHasEffect =>
        !SpawnCount.IsIdentity || !SpawnInterval.IsIdentity || !CoolTime.IsIdentity || !MaxSpawn.IsIdentity;
}

/// <summary>
/// The validated configuration (SPEC004 6章). Invalid values have already been replaced by their
/// defaults and reported, so runtime code never re-validates (FR-317).
/// </summary>
public sealed record SpawnProfile
{
    public bool Enabled { get; init; }

    public int Seed { get; init; }

    public bool DiagnosticsEnabled { get; init; }

    public MultiplierRange SpawnCount { get; init; }

    public MultiplierRange SpawnInterval { get; init; }

    public MultiplierRange CoolTime { get; init; }

    public MultiplierRange MaxSpawn { get; init; }

    public int AdditionalSpawnCapPerVisit { get; init; }

    public int AdditionalAliveCap { get; init; }

    public float OffscreenMargin { get; init; }

    public float AmbushChance { get; init; }

    public float StagnationSeconds { get; init; }

    public float StagnationWindowSeconds { get; init; }

    public float StagnationMoveEpsilon { get; init; }

    public float StagnationPenaltyInterval { get; init; }

    public IReadOnlyList<string> AllowedGimmickTypes { get; init; } = Array.Empty<string>();

    public int GimmickClonesPerVisit { get; init; }

    public float GimmickCloneOffsetRange { get; init; }

    public bool MimicBoxEnabled { get; init; }

    public int MimicBoxesPerVisit { get; init; }

    public float MimicChance { get; init; }

    public RewardTable RewardTable { get; init; } = RewardTable.Empty;

    public int RewardLootValue { get; init; }

    public bool LogInterventions { get; init; }

    public HudMode HudMode { get; init; } = HudMode.Off;

    public bool DebugCommandsEnabled { get; init; }

    public IReadOnlyDictionary<string, AreaOverride> AreaOverrides { get; init; } =
        new Dictionary<string, AreaOverride>();

    public bool GimmickCloningEnabled => AllowedGimmickTypes.Count > 0 && GimmickClonesPerVisit > 0;

    /// <summary>
    /// Resolves the settings for one area. <paramref name="excludedByDefault"/> is the plugin's
    /// built-in exclusion verdict for the scene; an explicit `excluded: false` in areas.json is
    /// the only thing that can lift it (SPEC004 6章, DEC-310).
    /// </summary>
    public AreaSettings Resolve(string sceneName, bool excludedByDefault)
    {
        AreaOverride? o = AreaOverrides.TryGetValue(sceneName, out AreaOverride? found) ? found : null;

        bool excluded = o?.Excluded ?? excludedByDefault;

        return new AreaSettings
        {
            Excluded = excluded,
            SpawnCount = RangeFrom(o?.SpawnCountMultiplierMin, o?.SpawnCountMultiplierMax, SpawnCount),
            SpawnInterval = RangeFrom(o?.SpawnIntervalMultiplierMin, o?.SpawnIntervalMultiplierMax, SpawnInterval),
            CoolTime = RangeFrom(o?.CoolTimeMultiplierMin, o?.CoolTimeMultiplierMax, CoolTime),
            MaxSpawn = RangeFrom(o?.MaxSpawnMultiplierMin, o?.MaxSpawnMultiplierMax, MaxSpawn),
            AdditionalSpawnCapPerVisit = o?.AdditionalSpawnCapPerVisit ?? AdditionalSpawnCapPerVisit,
            AdditionalAliveCap = o?.AdditionalAliveCap ?? AdditionalAliveCap,
            AmbushChance = o?.AmbushChance ?? AmbushChance,
            StagnationSeconds = o?.StagnationSeconds ?? StagnationSeconds,
            StagnationPenaltyInterval = o?.StagnationPenaltyInterval ?? StagnationPenaltyInterval,
        };
    }

    private static MultiplierRange RangeFrom(float? min, float? max, MultiplierRange fallback)
    {
        var candidate = new MultiplierRange(min ?? fallback.Min, max ?? fallback.Max);
        return candidate.IsValid ? candidate : fallback;
    }
}
