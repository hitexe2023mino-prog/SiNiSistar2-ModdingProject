using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

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
    /// <summary>
    /// The enemy identifier (SPEC003 5.3.1): a <c>GalleryEnemyID</c> name, an <c>EnemyID</c> name, or
    /// a normalised object name behind <see cref="EnemyIds.ObjectPrefix"/>. Never <c>None</c>.
    /// </summary>
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

    /// <summary>
    /// What the game calls this enemy on screen (SPEC003 FR-282). Read from the enemy itself when it
    /// takes hold, so it is present only for enemies that have been met. Absent from the file rather
    /// than written as null, because a row that has never been met has nothing to say here.
    /// </summary>
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    /// <summary>
    /// Not serialised: it is <see cref="Kind"/> parsed, and the file's field list (SPEC003 6.2) has
    /// five fields. Writing it as well produced a sixth that nothing reads and that contradicts
    /// <see cref="Kind"/> the moment the file is edited by hand.
    /// </summary>
    [JsonIgnore]
    public EnemyAttackSetting Setting =>
        Enum.TryParse(Kind, ignoreCase: true, out EnemyAttackSetting parsed)
            ? parsed
            : EnemyAttackSetting.Auto;
}

/// <summary>
/// What counts as an enemy identifier (SPEC003 5.3.1).
///
/// The rule that matters is that <c>None</c> is not one. Both of the game's enumerations carry a
/// <c>None</c> member meaning "not set", and treating it as a name collapses every enemy the game
/// left unset into a single row: one decision made about one of them would then apply to all of
/// them, and no decision about any of them would apply to the one in front of the player.
/// </summary>
public static class EnemyIds
{
    /// <summary>Marks an identifier that came from an object name rather than an enumeration.</summary>
    public const string ObjectPrefix = "obj:";

    /// <summary>The name both of the game's enemy enumerations use for "not set".</summary>
    public const string Unset = "None";

    /// <summary>
    /// Names Unity scenes reuse for structure rather than for identity. Hold colliders and attack
    /// areas hang off objects called <c>Root</c> under every character in this build, so a row keyed
    /// on one would stand for whichever binder happened to sit there.
    /// </summary>
    private static readonly HashSet<string> StructuralNames =
        new(StringComparer.Ordinal) { "Root", "Base" };

    public static bool IsUsable(string? id) =>
        !string.IsNullOrWhiteSpace(id) && !string.Equals(id, Unset, StringComparison.Ordinal);

    /// <summary>Whether a scene object's name is one the game reuses for structure.</summary>
    public static bool IsStructuralName(string? objectName) =>
        objectName is not null && StructuralNames.Contains(objectName.Trim());

    /// <summary>
    /// The last resort of 5.3.1: an identifier built from the object's own name.
    ///
    /// Unity appends <c>(Clone)</c> for every instantiation and <c> (2)</c> for every duplicate in a
    /// scene, so the same enemy arrives under a different name depending on how it got there. Left
    /// alone, one worm would occupy a row per spawn and a decision made about one of them would not
    /// apply to the next. Returns null when nothing is left to name.
    /// </summary>
    public static string? FromObjectName(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        string trimmed = objectName!.Trim();
        bool changed = true;
        while (changed && trimmed.Length > 0)
        {
            changed = false;
            trimmed = trimmed.TrimEnd();

            if (trimmed.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^"(Clone)".Length];
                changed = true;
                continue;
            }

            // A trailing "(12)" is Unity's duplicate counter. Digits outside brackets are part of the
            // name — MeatWorm1 is not MeatWorm — so only the bracketed form is removed.
            int open = trimmed.LastIndexOf('(');
            if (open >= 0 && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                string inside = trimmed[(open + 1)..^1];
                if (inside.Length > 0 && inside.All(char.IsDigit))
                {
                    trimmed = trimmed[..open];
                    changed = true;
                }
            }
        }

        trimmed = trimmed.Trim();
        if (trimmed.Length == 0 || IsStructuralName(trimmed))
        {
            return null;
        }

        return ObjectPrefix + trimmed;
    }

    /// <summary>
    /// An identifier built from the binder's own type, used when its object name does not identify
    /// it. A binder sitting on an object called <c>Root</c> still has a class of its own —
    /// <c>ParasiteTentacle</c>, <c>StoneEye</c> — and that is what the player is being held by.
    /// </summary>
    public static string? FromTypeName(string? typeName) =>
        string.IsNullOrWhiteSpace(typeName) ? null : ObjectPrefix + typeName!.Trim();
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

    /// <summary>
    /// Indented, and with the whole of Unicode left unescaped. The file exists to be read and diffed
    /// by hand (SPEC003 6.2), and the default encoder turns a display name such as 大口のワーム into
    /// a run of 大-style escapes — technically the same string, unreadable as a row in a list.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

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
public sealed record EnemyAttackRow(string Id, EnemyAttackSetting Setting, bool Seen, string? DisplayName = null);

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
        Absorb(document);
    }

    /// <summary>Whether something has changed since the last write.</summary>
    public bool IsDirty { get; private set; }

    public int Count => _entries.Count;

    /// <summary>
    /// The declaration carried by a discarded <c>None</c> row, when it was not <c>Auto</c>.
    ///
    /// Such a row is a leftover from before 5.3.1, when every unidentified captor shared one line.
    /// It cannot be carried anywhere, because there is no way to tell which enemy it was meant for.
    /// Dropping it silently would take a decision away without saying so, so it is reported once.
    /// </summary>
    public EnemyAttackSetting? DiscardedUnsetDeclaration { get; private set; }

    public EnemyAttackSetting SettingFor(string enemyId) =>
        EnemyIds.IsUsable(enemyId) && _entries.TryGetValue(enemyId, out EnemyAttackEntry? entry)
            ? entry.Setting
            : EnemyAttackSetting.Auto;

    public void Set(string enemyId, EnemyAttackSetting setting, string? note = null)
    {
        if (!EnemyIds.IsUsable(enemyId))
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
    /// Records that this enemy has held the player, and what the game calls it (SPEC003 FR-282).
    ///
    /// Returns true when something changed, which is when the file is worth rewriting; a hold that
    /// tells it nothing new is a no-op, so a long hold does not write once a frame. The display name
    /// is compared as well as the sighting because switching the game's language changes it, and a
    /// list showing yesterday's language is worse than one showing none.
    /// </summary>
    public bool MarkSeen(string enemyId, string? displayName = null)
    {
        if (!EnemyIds.IsUsable(enemyId))
        {
            return false;
        }

        string? name = string.IsNullOrWhiteSpace(displayName) ? null : displayName!.Trim();

        if (_entries.TryGetValue(enemyId, out EnemyAttackEntry? entry))
        {
            bool nameIsNew = name is not null && !string.Equals(entry.DisplayName, name, StringComparison.Ordinal);
            if (entry.Seen && !nameIsNew)
            {
                return false;
            }

            _entries[enemyId] = entry with { Seen = true, DisplayName = name ?? entry.DisplayName };
        }
        else
        {
            _entries[enemyId] = new EnemyAttackEntry { Id = enemyId, Seen = true, DisplayName = name };
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
            if (!EnemyIds.IsUsable(id) || _entries.ContainsKey(id))
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
            .Select(entry => new EnemyAttackRow(entry.Id, entry.Setting, entry.Seen, entry.DisplayName))
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
        Absorb(snapshot);
        IsDirty = false;
    }

    /// <summary>
    /// Takes a document's rows in, dropping the ones that name no enemy (SPEC003 FR-281). The drop
    /// happens here rather than at the file boundary so a snapshot restored by a cancelled edit
    /// cannot put a <c>None</c> row back.
    /// </summary>
    private void Absorb(EnemyAttackDocument document)
    {
        foreach (EnemyAttackEntry entry in document.Enemies)
        {
            if (EnemyIds.IsUsable(entry.Id))
            {
                _entries[entry.Id] = entry;
                continue;
            }

            if (string.Equals(entry.Id, EnemyIds.Unset, StringComparison.Ordinal)
                && entry.Setting != EnemyAttackSetting.Auto)
            {
                DiscardedUnsetDeclaration = entry.Setting;
            }

            // The row was dropped, so what is in memory no longer matches the file. Without this the
            // leftover row would survive every launch where nothing else changed.
            IsDirty = true;
        }
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
