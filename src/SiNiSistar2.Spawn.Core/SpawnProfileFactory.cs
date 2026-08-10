namespace SiNiSistar2.Spawn.Core;

/// <summary>The validated profile plus everything the user should be told about their config.</summary>
public sealed record ProfileValidation
{
    public SpawnProfile Profile { get; init; } = new();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Turns raw options into a validated <see cref="SpawnProfile"/>. Every invalid value is replaced
/// by its default and reported, never fatal (SPEC004 FR-317). Direction rules follow 5.2: count
/// and pool multipliers may only raise (≥1), interval and cool-time multipliers may only shorten (≤1).
/// </summary>
public static class SpawnProfileFactory
{
    public static ProfileValidation Create(
        SpawnOptions options,
        string areasJson,
        IReadOnlyCollection<string> knownSceneNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var defaults = new SpawnOptions();

        MultiplierRange spawnCount = RaiseOnlyRange(
            "SpawnCountMultiplier",
            options.SpawnCountMultiplierMin,
            options.SpawnCountMultiplierMax,
            new MultiplierRange(defaults.SpawnCountMultiplierMin, defaults.SpawnCountMultiplierMax),
            errors);
        MultiplierRange spawnInterval = ShortenOnlyRange(
            "SpawnIntervalMultiplier",
            options.SpawnIntervalMultiplierMin,
            options.SpawnIntervalMultiplierMax,
            new MultiplierRange(defaults.SpawnIntervalMultiplierMin, defaults.SpawnIntervalMultiplierMax),
            errors);
        MultiplierRange coolTime = ShortenOnlyRange(
            "CoolTimeMultiplier",
            options.CoolTimeMultiplierMin,
            options.CoolTimeMultiplierMax,
            new MultiplierRange(defaults.CoolTimeMultiplierMin, defaults.CoolTimeMultiplierMax),
            errors);
        MultiplierRange maxSpawn = RaiseOnlyRange(
            "MaxSpawnMultiplier",
            options.MaxSpawnMultiplierMin,
            options.MaxSpawnMultiplierMax,
            new MultiplierRange(defaults.MaxSpawnMultiplierMin, defaults.MaxSpawnMultiplierMax),
            errors);

        int spawnCap = NonNegative("AdditionalSpawnCapPerVisit", options.AdditionalSpawnCapPerVisit, defaults.AdditionalSpawnCapPerVisit, errors);
        int aliveCap = NonNegative("AdditionalAliveCap", options.AdditionalAliveCap, defaults.AdditionalAliveCap, errors);
        float margin = NonNegative("OffscreenMargin", options.OffscreenMargin, defaults.OffscreenMargin, errors);
        float ambush = Probability("AmbushChance", options.AmbushChance, defaults.AmbushChance, errors);
        float stagnation = Positive("StagnationSeconds", options.StagnationSeconds, defaults.StagnationSeconds, errors);
        float window = Positive("StagnationWindowSeconds", options.StagnationWindowSeconds, defaults.StagnationWindowSeconds, errors);
        float epsilon = Positive("StagnationMoveEpsilon", options.StagnationMoveEpsilon, defaults.StagnationMoveEpsilon, errors);
        float interval = Positive("StagnationPenaltyInterval", options.StagnationPenaltyInterval, defaults.StagnationPenaltyInterval, errors);
        int gimmickCap = NonNegative("GimmickClonesPerVisit", options.GimmickClonesPerVisit, defaults.GimmickClonesPerVisit, errors);
        float gimmickOffset = NonNegative("GimmickCloneOffsetRange", options.GimmickCloneOffsetRange, defaults.GimmickCloneOffsetRange, errors);
        int mimicCap = NonNegative("MimicBoxesPerVisit", options.MimicBoxesPerVisit, defaults.MimicBoxesPerVisit, errors);
        float mimicChance = Probability("MimicChance", options.MimicChance, defaults.MimicChance, errors);
        int lootValue = NonNegative("RewardLootValue", options.RewardLootValue, defaults.RewardLootValue, errors);

        string[] gimmickTypes = options.AllowedGimmickTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        RewardTable rewards = RewardTable.Parse(options.RewardTable, out List<string> rewardErrors);
        errors.AddRange(rewardErrors);
        if (options.MimicBoxEnabled && rewards.IsEmpty)
        {
            warnings.Add(
                "MimicBox is enabled but the RewardTable has no usable entry; a miss will only "
                + "scatter loot (RewardLootValue) and grant nothing.");
        }

        Dictionary<string, AreaOverride> overrides = AreaOverrideDocument.Parse(areasJson, out List<string> areaErrors);
        errors.AddRange(areaErrors);
        if (knownSceneNames.Count > 0)
        {
            foreach (string name in overrides.Keys.Where(k => !knownSceneNames.Contains(k)).ToArray())
            {
                errors.Add($"areas.json entry '{name}' is not a SceneID of this build and is ignored.");
                overrides.Remove(name);
            }
        }

        var profile = new SpawnProfile
        {
            Enabled = options.Enabled,
            Seed = options.Seed,
            DiagnosticsEnabled = options.DiagnosticsEnabled,
            SpawnCount = spawnCount,
            SpawnInterval = spawnInterval,
            CoolTime = coolTime,
            MaxSpawn = maxSpawn,
            AdditionalSpawnCapPerVisit = spawnCap,
            AdditionalAliveCap = aliveCap,
            OffscreenMargin = margin,
            AmbushChance = ambush,
            StagnationSeconds = stagnation,
            StagnationWindowSeconds = window,
            StagnationMoveEpsilon = epsilon,
            StagnationPenaltyInterval = interval,
            AllowedGimmickTypes = gimmickTypes,
            GimmickClonesPerVisit = gimmickCap,
            GimmickCloneOffsetRange = gimmickOffset,
            MimicBoxEnabled = options.MimicBoxEnabled,
            MimicBoxesPerVisit = mimicCap,
            MimicChance = mimicChance,
            RewardTable = rewards,
            RewardLootValue = lootValue,
            LogInterventions = options.LogInterventions,
            HudMode = options.HudMode,
            DebugCommandsEnabled = options.DebugCommandsEnabled,
            AreaOverrides = overrides,
        };

        return new ProfileValidation { Profile = profile, Errors = errors, Warnings = warnings };
    }

    private static MultiplierRange RaiseOnlyRange(
        string key, float min, float max, MultiplierRange fallback, List<string> errors)
    {
        var range = new MultiplierRange(min, max);
        if (!range.IsValid || min < 1f)
        {
            errors.Add($"{key}Min/Max must satisfy 1.0 <= Min <= Max; using {fallback}.");
            return fallback;
        }

        return range;
    }

    private static MultiplierRange ShortenOnlyRange(
        string key, float min, float max, MultiplierRange fallback, List<string> errors)
    {
        var range = new MultiplierRange(min, max);
        if (!range.IsValid || max > 1f)
        {
            errors.Add($"{key}Min/Max must satisfy 0 < Min <= Max <= 1.0; using {fallback}.");
            return fallback;
        }

        return range;
    }

    private static int NonNegative(string key, int value, int fallback, List<string> errors)
    {
        if (value < 0)
        {
            errors.Add($"{key} must be 0 or more; using {fallback}.");
            return fallback;
        }

        return value;
    }

    private static float NonNegative(string key, float value, float fallback, List<string> errors)
    {
        if (value < 0f || !float.IsFinite(value))
        {
            errors.Add($"{key} must be 0 or more; using {fallback}.");
            return fallback;
        }

        return value;
    }

    private static float Positive(string key, float value, float fallback, List<string> errors)
    {
        if (value <= 0f || !float.IsFinite(value))
        {
            errors.Add($"{key} must be above 0; using {fallback}.");
            return fallback;
        }

        return value;
    }

    private static float Probability(string key, float value, float fallback, List<string> errors)
    {
        if (value < 0f || value > 1f || !float.IsFinite(value))
        {
            errors.Add($"{key} must be between 0 and 1; using {fallback}.");
            return fallback;
        }

        return value;
    }
}
