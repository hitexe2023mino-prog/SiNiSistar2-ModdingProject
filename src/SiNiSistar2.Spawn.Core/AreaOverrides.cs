using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// One entry of areas.json: per-area overrides of the [SpawnerTuning] / [AdditionalSpawn] values
/// plus the exclusion flag (SPEC004 6章 areas.json). Absent members inherit the global value.
/// </summary>
public sealed class AreaOverride
{
    [JsonPropertyName("excluded")]
    public bool? Excluded { get; init; }

    [JsonPropertyName("spawnCountMultiplierMin")]
    public float? SpawnCountMultiplierMin { get; init; }

    [JsonPropertyName("spawnCountMultiplierMax")]
    public float? SpawnCountMultiplierMax { get; init; }

    [JsonPropertyName("spawnIntervalMultiplierMin")]
    public float? SpawnIntervalMultiplierMin { get; init; }

    [JsonPropertyName("spawnIntervalMultiplierMax")]
    public float? SpawnIntervalMultiplierMax { get; init; }

    [JsonPropertyName("coolTimeMultiplierMin")]
    public float? CoolTimeMultiplierMin { get; init; }

    [JsonPropertyName("coolTimeMultiplierMax")]
    public float? CoolTimeMultiplierMax { get; init; }

    [JsonPropertyName("maxSpawnMultiplierMin")]
    public float? MaxSpawnMultiplierMin { get; init; }

    [JsonPropertyName("maxSpawnMultiplierMax")]
    public float? MaxSpawnMultiplierMax { get; init; }

    [JsonPropertyName("additionalSpawnCapPerVisit")]
    public int? AdditionalSpawnCapPerVisit { get; init; }

    [JsonPropertyName("additionalAliveCap")]
    public int? AdditionalAliveCap { get; init; }

    [JsonPropertyName("ambushChance")]
    public float? AmbushChance { get; init; }

    [JsonPropertyName("stagnationSeconds")]
    public float? StagnationSeconds { get; init; }

    [JsonPropertyName("stagnationPenaltyInterval")]
    public float? StagnationPenaltyInterval { get; init; }
}

/// <summary>Loader for areas.json. A missing file is a valid, empty document (SPEC004 6章).</summary>
public static class AreaOverrideDocument
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Dictionary<string, AreaOverride> Parse(string json, out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, AreaOverride>(StringComparer.Ordinal);
        }

        try
        {
            Dictionary<string, AreaOverride>? parsed =
                JsonSerializer.Deserialize<Dictionary<string, AreaOverride>>(json, Options);
            return parsed ?? new Dictionary<string, AreaOverride>(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            errors.Add($"areas.json could not be parsed and is ignored: {exception.Message}");
            return new Dictionary<string, AreaOverride>(StringComparer.Ordinal);
        }
    }
}
