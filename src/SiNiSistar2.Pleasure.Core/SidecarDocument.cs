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
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Which game build wrote this. A mismatch is reported, never a reason to discard.</summary>
    [JsonPropertyName("gameBuildId")]
    public string GameBuildId { get; init; } = string.Empty;

    [JsonPropertyName("sensitivity")]
    public float Sensitivity { get; init; }

    [JsonPropertyName("climaxCount")]
    public int ClimaxCount { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
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

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            return SidecarParse.Failed(
                $"The sidecar file is schema version {document.SchemaVersion}; this MOD reads "
                + $"{CurrentSchemaVersion}. It will not be read and will not be overwritten.",
                unsupportedSchema: true);
        }

        // Negative values can only come from a hand-edited or damaged file; clamping keeps them
        // from turning into a limit that can never be reached.
        return SidecarParse.Loaded(document with
        {
            Sensitivity = Math.Max(0f, document.Sensitivity),
            ClimaxCount = Math.Max(0, document.ClimaxCount),
        });
    }
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
    public static string? Compose(int selectId, string? loadedFileName)
    {
        if (selectId < 0 && string.IsNullOrWhiteSpace(loadedFileName))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(loadedFileName ?? string.Empty).Trim();
        var sanitised = new string(name.Select(c => Invalid.Contains(c) ? '_' : c).ToArray());
        return sanitised.Length == 0 ? $"slot{selectId}" : $"slot{selectId}-{sanitised}";
    }
}
