using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// What the MOD keeps alongside one of the game's save slots (SPEC003 5.9).
///
/// It is a separate file rather than a game variable because the game's flag/variable system has
/// fixed categories and fixed-length arrays with no way to add a name. Using it would mean
/// squatting on an index nobody can prove is free, and a game update that started using it would
/// silently corrupt quest or boss progress (SPEC003 DEC-206).
/// </summary>
public sealed record SidecarDocument
{
    public const int CurrentSchemaVersion = 6;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Which game build wrote this. A mismatch is reported, never a reason to discard.</summary>
    [JsonPropertyName("gameBuildId")]
    public string GameBuildId { get; init; } = string.Empty;

    /// <summary>
    /// How far the corruption has gone (SPEC003 5.7). Written under this name from schema 4.
    /// </summary>
    [JsonPropertyName("corruption")]
    public float Corruption { get; init; }

    /// <summary>
    /// What schema 3 and earlier called the same number.
    ///
    /// Read, never written. Renaming the axis must not cost a player the progress they had under
    /// the old name — it is the same accumulation, and it is the one thing in the file that can
    /// never be earned back, because nothing lowers it (DEC-208).
    /// </summary>
    [JsonPropertyName("sensitivity")]
    public float? LegacySensitivity { get; init; }

    [JsonPropertyName("climaxCount")]
    public int ClimaxCount { get; init; }

    /// <summary>
    /// How many <c>Breast</c> applications have landed while it was already at its maximum level
    /// (SPEC003 5.8). Added in schema 2; a schema 1 file simply has none and starts from zero.
    /// </summary>
    [JsonPropertyName("breastAtMaxCount")]
    public int BreastAtMaxCount { get; init; }

    /// <summary>How full the milk reservoir is (SPEC003 5.8). Added in schema 3.</summary>
    [JsonPropertyName("milk")]
    public float Milk { get; init; }

    /// <summary>
    /// Whether the lust crest has ever been received in this run (SPEC003 FR-272). Added in
    /// schema 5.
    ///
    /// Kept separately from the corruption that earned it, because it does not come off: a cure
    /// that took the status away must not also take away the fact that it was once carried, or the
    /// mark would be curable by being cured of something else.
    /// </summary>
    [JsonPropertyName("lustCrest")]
    public bool LustCrest { get; init; }

    /// <summary>
    /// How many climaxes each enemy has to its name, for the life of this save (SPEC006 FR-602).
    /// Added in schema 6; a file written before it simply has none and starts from empty.
    /// </summary>
    [JsonPropertyName("actorClimaxCounts")]
    public IReadOnlyList<ActorClimaxCount> ActorClimaxCounts { get; init; } =
        Array.Empty<ActorClimaxCount>();

    /// <summary>
    /// How many times each status ailment has actually been applied (SPEC006 FR-601). Added in
    /// schema 6.
    /// </summary>
    [JsonPropertyName("debuffCounts")]
    public IReadOnlyList<DebuffCount> DebuffCounts { get; init; } = Array.Empty<DebuffCount>();

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads a sidecar. An unreadable or future-versioned file yields no document: the caller
    /// starts from defaults and must not overwrite it, so a version mismatch cannot destroy a save
    /// written by a newer MOD (SPEC003 FR-225).
    /// </summary>
    public static SidecarParse Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SidecarParse.Failed("The sidecar file is empty.", unsupportedSchema: false);
        }

        SidecarDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SidecarDocument>(json!);
        }
        catch (JsonException exception)
        {
            return SidecarParse.Failed($"The sidecar file is not valid JSON: {exception.Message}", false);
        }

        if (document is null)
        {
            return SidecarParse.Failed("The sidecar file held no object.", false);
        }

        // Only a newer file is refused. An older one is read: every field this version added is
        // absent rather than wrong, so it defaults, and refusing it would have thrown away the
        // player's accumulated corruption on the very upgrade that introduced a new field.
        if (document.SchemaVersion > CurrentSchemaVersion)
        {
            return SidecarParse.Failed(
                $"The sidecar file is schema version {document.SchemaVersion}; this MOD reads "
                + $"{CurrentSchemaVersion}. It will not be read and will not be overwritten.",
                unsupportedSchema: true);
        }

        if (document.SchemaVersion < 1)
        {
            return SidecarParse.Failed(
                $"The sidecar file declares schema version {document.SchemaVersion}, which is not a "
                + "version this MOD ever wrote.",
                unsupportedSchema: false);
        }

        // Negative values can only come from a hand-edited or damaged file; clamping keeps them
        // from turning into a limit that can never be reached.
        // The old name wins only when the new one is absent. A file written by this version has
        // both if it was upgraded in place, and the new one is the value that has been maintained.
        float corruption = document.Corruption > 0f
            ? document.Corruption
            : document.LegacySensitivity ?? 0f;

        return SidecarParse.Loaded(document with
        {
            SchemaVersion = CurrentSchemaVersion,
            LegacySensitivity = null,
            Corruption = Math.Max(0f, corruption),
            ClimaxCount = Math.Max(0, document.ClimaxCount),
            BreastAtMaxCount = Math.Max(0, document.BreastAtMaxCount),
            Milk = Math.Clamp(document.Milk, 0f, 1f),
            ActorClimaxCounts = Clean(
                document.ActorClimaxCounts,
                static entry => entry.ActorId,
                static entry => entry.Count),
            DebuffCounts = Clean(
                document.DebuffCounts,
                static entry => entry.AbnormalType,
                static entry => entry.Count),
        });
    }

    /// <summary>
    /// Drops the rows a tally can make no use of: a nameless key, and a count that cannot have been
    /// reached by counting. Both can only come from a hand-edited or damaged file, and dropping
    /// them here keeps every reader from having to know that.
    /// </summary>
    private static IReadOnlyList<T> Clean<T>(
        IReadOnlyList<T>? entries,
        Func<T, string> key,
        Func<T, int> count)
    {
        if (entries is null || entries.Count == 0)
        {
            return Array.Empty<T>();
        }

        var kept = new List<T>(entries.Count);
        foreach (T entry in entries)
        {
            if (entry is not null && !string.IsNullOrWhiteSpace(key(entry)) && count(entry) > 0)
            {
                kept.Add(entry);
            }
        }

        return kept;
    }
}

/// <summary>One enemy's lifetime climax total as the sidecar stores it (SPEC006 4.3).</summary>
public sealed record ActorClimaxCount
{
    [JsonPropertyName("actorId")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>One status ailment's lifetime total as the sidecar stores it (SPEC006 4.3).</summary>
public sealed record DebuffCount
{
    [JsonPropertyName("abnormalType")]
    public string AbnormalType { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>
    /// The game's own name for this status, captured when it was applied (SPEC006 FR-613).
    ///
    /// Stored rather than resolved on demand, because it can only be read while the status is
    /// attached to somebody. Without it, every status in the diary would go back to showing its raw
    /// enumerator name after a reload and stay that way until it was suffered again.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }
}

public sealed record SidecarParse(SidecarDocument? Document, string? Error, bool UnsupportedSchema)
{
    public bool IsLoaded => Document is not null;

    internal static SidecarParse Loaded(SidecarDocument document) => new(document, null, false);

    internal static SidecarParse Failed(string error, bool unsupportedSchema) =>
        new(null, error, unsupportedSchema);
}

/// <summary>Ties a sidecar file to one of the game's save slots (SPEC003 5.9).</summary>
public static class SlotKey
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Builds the file-name stem for a slot. Both the slot number and the loaded file name are
    /// used: the number alone is reused across playthroughs, and the name alone is not always
    /// available.
    /// </summary>
    /// <summary>
    /// What the game calls the loaded file when no save has been loaded at all.
    ///
    /// Every fresh game reports this, so it is not a slot — it is the absence of one. Treating it
    /// as a slot gave every playthrough the same key, and the second new game inherited the first
    /// one's corruption (SPEC003 付録A A-44).
    /// </summary>
    private const string NoFileSentinel = "FileEmpty";

    public static string? Compose(int selectId, string? loadedFileName)
    {
        if (selectId < 0 && string.IsNullOrWhiteSpace(loadedFileName))
        {
            return null;
        }

        if (string.Equals(
                Path.GetFileNameWithoutExtension(loadedFileName ?? string.Empty).Trim(),
                NoFileSentinel,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(loadedFileName ?? string.Empty).Trim();
        var sanitised = new string(name.Select(c => Invalid.Contains(c) ? '_' : c).ToArray());
        return sanitised.Length == 0 ? $"slot{selectId}" : $"slot{selectId}-{sanitised}";
    }
}
