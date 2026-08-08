using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiNiSistar2.Pleasure.Core;

/// <summary>What one enemy's attacks are declared to be (SPEC003 5.3).</summary>
public enum EnemyAttackSetting
{
    /// <summary>
    /// Decide from what the attack inflicts. This is the shipped state for every enemy: the status
    /// test already answers most of them, and an entry set by hand is a claim the player is making
    /// that ought to be visible as such (DEC-203).
    /// </summary>
    Auto,

    /// <summary>Every attack from this enemy raises pleasure, whatever it inflicts.</summary>
    Sexual,

    /// <summary>No attack from this enemy raises pleasure. Outranks every other rule.</summary>
    NonSexual,
}

/// <summary>Read access to the per-enemy decisions, so the classifier sees edits as they are made.</summary>
public interface IEnemyAttackOverrides
{
    EnemyAttackSetting SettingFor(string enemyId);
}

/// <summary>One line of the catalogue file.</summary>
public sealed record EnemyAttackEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Stored as text rather than a number so the file stays readable and hand-editable, and so an
    /// unrecognised word degrades to <see cref="EnemyAttackSetting.Auto"/> instead of silently
    /// meaning whatever enum member happens to sit at that index.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = nameof(EnemyAttackSetting.Auto);

    /// <summary>
    /// Whether this enemy has ever held the player. The editor puts those first, because a hold is
    /// the only situation the classification applies to, and 108 rows are otherwise unusable.
    /// </summary>
    [JsonPropertyName("seen")]
    public bool Seen { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    public EnemyAttackSetting Setting =>
        Enum.TryParse(Kind, ignoreCase: true, out EnemyAttackSetting parsed)
            ? parsed
            : EnemyAttackSetting.Auto;
}

/// <summary>
/// The catalogue file: which enemies make sexual attacks while they hold the player (SPEC003 5.3).
///
/// It is a file of its own rather than the comma-separated config lists it replaces. There are 108
/// enemy ids in this build, the decision is per enemy, and it is the kind of thing a player revises
/// as they meet them — none of which a single config line supports. Being a file also means the
/// in-game editor has somewhere to write that survives a restart.
/// </summary>
public sealed record EnemyAttackDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("note")]
    public string Note { get; init; } =
        "kind: Auto decides from the statuses the attack inflicts, Sexual always raises pleasure, "
        + "NonSexual never does. NonSexual outranks everything. Edit in game with F10.";

    [JsonPropertyName("enemies")]
    public IReadOnlyList<EnemyAttackEntry> Enemies { get; init; } = Array.Empty<EnemyAttackEntry>();

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Reads a catalogue. A future schema version yields no document so the caller refuses to
    /// overwrite it, on the same reasoning as the sidecar: an older MOD must not destroy decisions
    /// a newer one understood.
    /// </summary>
    public static EnemyAttackParse Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EnemyAttackParse.Failed("The catalogue file is empty.", unsupportedSchema: false);
        }

        EnemyAttackDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<EnemyAttackDocument>(json!);
        }
        catch (JsonException exception)
        {
            return EnemyAttackParse.Failed($"The catalogue file is not valid JSON: {exception.Message}", false);
        }

        if (document is null)
        {
            return EnemyAttackParse.Failed("The catalogue file held no object.", false);
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            return EnemyAttackParse.Failed(
                $"The catalogue file is schema version {document.SchemaVersion}; this MOD reads "
                + $"{CurrentSchemaVersion}. It will not be read and will not be overwritten.",
                unsupportedSchema: true);
        }

        return EnemyAttackParse.Loaded(document);
    }
}

public sealed record EnemyAttackParse(EnemyAttackDocument? Document, string? Error, bool UnsupportedSchema)
{
    public bool IsLoaded => Document is not null;

    internal static EnemyAttackParse Loaded(EnemyAttackDocument document) => new(document, null, false);

    internal static EnemyAttackParse Failed(string error, bool unsupportedSchema) =>
        new(null, error, unsupportedSchema);
}

/// <summary>One row as the in-game editor shows it.</summary>
public sealed record EnemyAttackRow(string Id, EnemyAttackSetting Setting, bool Seen);

/// <summary>
/// The catalogue in memory. The classifier holds this object rather than a copy of its contents, so
/// a decision made in the editor applies to the very next hit instead of after a restart.
/// </summary>
public sealed class EnemyAttackCatalog : IEnemyAttackOverrides
{
    private readonly Dictionary<string, EnemyAttackEntry> _entries = new(StringComparer.Ordinal);

    public EnemyAttackCatalog()
    {
    }

    public EnemyAttackCatalog(EnemyAttackDocument document)
    {
        foreach (EnemyAttackEntry entry in document.Enemies)
        {
            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                _entries[entry.Id] = entry;
            }
        }
    }

    /// <summary>Whether something has changed since the last write.</summary>
    public bool IsDirty { get; private set; }

    public int Count => _entries.Count;

    public EnemyAttackSetting SettingFor(string enemyId) =>
        _entries.TryGetValue(enemyId, out EnemyAttackEntry? entry) ? entry.Setting : EnemyAttackSetting.Auto;

    public void Set(string enemyId, EnemyAttackSetting setting, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
        {
            return;
        }

        EnemyAttackEntry existing = _entries.TryGetValue(enemyId, out EnemyAttackEntry? found)
            ? found
            : new EnemyAttackEntry { Id = enemyId };

        if (existing.Setting == setting && note is null)
        {
            return;
        }

        _entries[enemyId] = existing with { Kind = setting.ToString(), Note = note ?? existing.Note };
        IsDirty = true;
    }

    /// <summary>
    /// Steps one enemy through the three settings. The editor is a list of 108 rows worked through
    /// with one hand, so a cycle beats three separate controls per row.
    /// </summary>
    public EnemyAttackSetting Cycle(string enemyId)
    {
        EnemyAttackSetting next = SettingFor(enemyId) switch
        {
            EnemyAttackSetting.Auto => EnemyAttackSetting.Sexual,
            EnemyAttackSetting.Sexual => EnemyAttackSetting.NonSexual,
            _ => EnemyAttackSetting.Auto,
        };

        Set(enemyId, next);
        return next;
    }

    /// <summary>
    /// Records that this enemy has held the player. Returns true the first time, which is when the
    /// file is worth rewriting; after that it is a no-op so a long hold does not write once a frame.
    /// </summary>
    public bool MarkSeen(string enemyId)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
        {
            return false;
        }

        if (_entries.TryGetValue(enemyId, out EnemyAttackEntry? entry))
        {
            if (entry.Seen)
            {
                return false;
            }

            _entries[enemyId] = entry with { Seen = true };
        }
        else
        {
            _entries[enemyId] = new EnemyAttackEntry { Id = enemyId, Seen = true };
        }

        IsDirty = true;
        return true;
    }

    /// <summary>
    /// Adds every id the game defines that the file does not yet mention, as <c>Auto</c>. The editor
    /// can then list enemies the player has not met, which is the only way to decide about one
    /// before it kills them.
    /// </summary>
    public int AddMissing(IEnumerable<string> enemyIds)
    {
        var added = 0;
        foreach (string id in enemyIds)
        {
            if (string.IsNullOrWhiteSpace(id) || _entries.ContainsKey(id))
            {
                continue;
            }

            _entries[id] = new EnemyAttackEntry { Id = id };
            added++;
        }

        if (added > 0)
        {
            IsDirty = true;
        }

        return added;
    }

    /// <summary>
    /// Carries the old comma-separated config lists into a catalogue being created for the first
    /// time. Without this, upgrading would silently discard whatever the player had already tuned.
    /// </summary>
    public void SeedFrom(IEnumerable<string> sexualIds, IEnumerable<string> nonSexualIds)
    {
        foreach (string id in sexualIds)
        {
            Set(id, EnemyAttackSetting.Sexual, "carried over from Pleasure.SexualEnemyIds");
        }

        // Applied second so it wins if an id somehow appeared in both, matching the rule order.
        foreach (string id in nonSexualIds)
        {
            Set(id, EnemyAttackSetting.NonSexual, "carried over from Pleasure.NonSexualEnemyIds");
        }
    }

    /// <summary>
    /// The rows the editor draws: enemies that have held the player first, alphabetical within each
    /// group. Those are the ones a decision can actually be made about, and 108 rows is too many to
    /// hunt through when the handful that matter are known.
    /// </summary>
    public IReadOnlyList<EnemyAttackRow> Rows() =>
        _entries.Values
            .Select(entry => new EnemyAttackRow(entry.Id, entry.Setting, entry.Seen))
            .OrderByDescending(row => row.Seen)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToArray();

    public EnemyAttackDocument ToDocument() => new()
    {
        Enemies = _entries.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray(),
    };

    public void MarkClean() => IsDirty = false;

    /// <summary>
    /// Puts the contents back to a snapshot, in place. The classifier holds this object, so a
    /// cancelled edit has to be undone inside it rather than by handing out a different catalogue.
    /// </summary>
    public void RestoreFrom(EnemyAttackDocument snapshot)
    {
        _entries.Clear();
        foreach (EnemyAttackEntry entry in snapshot.Enemies)
        {
            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                _entries[entry.Id] = entry;
            }
        }

        IsDirty = false;
    }

    /// <summary>How many enemies carry a decision rather than being left to the status test.</summary>
    public string Summary()
    {
        var sexual = 0;
        var nonSexual = 0;
        foreach (EnemyAttackEntry entry in _entries.Values)
        {
            switch (entry.Setting)
            {
                case EnemyAttackSetting.Sexual:
                    sexual++;
                    break;
                case EnemyAttackSetting.NonSexual:
                    nonSexual++;
                    break;
            }
        }

        return $"{_entries.Count} enemies, {sexual} forced sexual, {nonSexual} forced non-sexual";
    }
}
