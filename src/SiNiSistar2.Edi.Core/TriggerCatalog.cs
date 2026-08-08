using System.Collections.Concurrent;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

/// <summary>How a catalog entry was discovered.</summary>
public static class TriggerSources
{
    /// <summary>Read from a game-side stage array before the stage was reached.</summary>
    public const string StaticEnumeration = "static-enumeration";

    /// <summary>Recorded when the transition actually happened.</summary>
    public const string Observed = "observed";
}

public sealed record TriggerCatalogEntry(
    string Context,
    string ActorId,
    string AnimationId,
    string Phase,
    string StageId,
    double? ClipLengthSeconds,
    bool? IsLooping,
    string Source,
    string? DisplayName,
    string? SceneName,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? ActorDisplayName = null,
    int? StageNumber = null,
    int? StageIndex = null)
{
    public EventKey Key => new(Context, ActorId, AnimationId, Phase, StageId);

    /// <summary>
    /// Ordering number for the stage. This build leaves <see cref="StageNumber"/> at 0 for every
    /// take, so a non-positive value is treated as unset and the position in the stage array is
    /// used instead. It orders the list; it is not a confirmed match for the gallery's own tabs.
    /// </summary>
    public int? DisplayNumber => StageNumber is int number && number > 0
        ? number
        : StageIndex is int index ? index + 1 : null;

    public static TriggerCatalogEntry Create(
        EventKey key,
        double? clipLengthSeconds,
        bool? isLooping,
        string source,
        string? displayName,
        string? sceneName,
        DateTimeOffset at,
        string? actorDisplayName = null,
        int? stageNumber = null,
        int? stageIndex = null) =>
        new(
            key.Context,
            key.ActorId,
            key.AnimationId,
            key.Phase,
            key.StageId,
            clipLengthSeconds,
            isLooping,
            source,
            displayName,
            sceneName,
            at,
            at,
            actorDisplayName,
            stageNumber,
            stageIndex);

    /// <summary>
    /// Merges a re-observation into an existing entry. Only missing values are filled in and the
    /// first-seen time is preserved, so a re-observation can never lose information (FR-034).
    /// </summary>
    public TriggerCatalogEntry MergeWith(TriggerCatalogEntry other) => this with
    {
        ClipLengthSeconds = ClipLengthSeconds ?? other.ClipLengthSeconds,
        IsLooping = IsLooping ?? other.IsLooping,
        // An observation supersedes a static enumeration because it carries measured values.
        Source = other.Source == TriggerSources.Observed || Source == TriggerSources.Observed
            ? TriggerSources.Observed
            : Source,
        DisplayName = other.DisplayName ?? DisplayName,
        ActorDisplayName = other.ActorDisplayName ?? ActorDisplayName,
        StageNumber = other.StageNumber ?? StageNumber,
        StageIndex = other.StageIndex ?? StageIndex,
        SceneName = other.SceneName ?? SceneName,
        FirstSeenAt = FirstSeenAt <= other.FirstSeenAt ? FirstSeenAt : other.FirstSeenAt,
        LastSeenAt = LastSeenAt >= other.LastSeenAt ? LastSeenAt : other.LastSeenAt,
    };
}

/// <summary>
/// Accumulates every trigger stage discovered for one game build and writes it atomically.
/// The catalog is the input to authoring; it never authorises playback (SPEC001 6.5, FR-041).
/// </summary>
public sealed class TriggerCatalog
{
    private const int SupportedSchemaVersion = 1;

    private readonly string _path;
    private readonly string _gameBuildId;
    private readonly TargetGameBuild _targetGameBuild;
    private readonly ConcurrentDictionary<EventKey, TriggerCatalogEntry> _entries = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Action<string>? _logWarning;
    private long _revision;
    private long _persistedRevision = -1;

    public TriggerCatalog(
        string path,
        string gameBuildId,
        TargetGameBuild targetGameBuild,
        Action<string>? logWarning = null)
    {
        _path = path;
        _gameBuildId = gameBuildId;
        _targetGameBuild = targetGameBuild;
        _logWarning = logWarning;
        LoadExisting();
    }

    public string Path => _path;
    public int Count => _entries.Count;

    /// <summary>Adds or merges an entry. Returns true when the catalog gained a new stage.</summary>
    public bool Register(TriggerCatalogEntry entry)
    {
        // A stage array can only name the stage, not the clips it will play, so it is registered
        // as a placeholder. The first real observation of that stage retires the placeholder;
        // otherwise the catalog would keep a permanent ghost row alongside the real trigger.
        if (!entry.Key.IsUnobservedPlaceholder)
        {
            // The placeholder carries the game's own display names, which an observation cannot
            // recover, so they are inherited rather than dropped with it.
            TriggerCatalogEntry? retired = RetirePlaceholderFor(entry);
            if (retired is not null)
            {
                entry = entry with
                {
                    DisplayName = entry.DisplayName ?? retired.DisplayName,
                    ActorDisplayName = entry.ActorDisplayName ?? retired.ActorDisplayName,
                    StageNumber = entry.StageNumber ?? retired.StageNumber,
                    StageIndex = entry.StageIndex ?? retired.StageIndex,
                };
            }

            entry = InheritNamesFromSiblings(entry);
        }

        var added = false;
        _entries.AddOrUpdate(
            entry.Key,
            _ =>
            {
                added = true;
                return entry;
            },
            (_, existing) => existing.MergeWith(entry));
        Interlocked.Increment(ref _revision);
        return added;
    }

    /// <summary>
    /// A take can queue several clips, so one stage may produce several triggers while only the
    /// first consumes the placeholder. The rest take their names from an already-known sibling:
    /// the stage label from the same stage, the actor label from anywhere under the same actor.
    /// </summary>
    private TriggerCatalogEntry InheritNamesFromSiblings(TriggerCatalogEntry entry)
    {
        if (entry.DisplayName is not null
            && entry.ActorDisplayName is not null
            && entry.StageNumber is not null
            && entry.StageIndex is not null)
        {
            return entry;
        }

        string? stageName = entry.DisplayName;
        string? actorName = entry.ActorDisplayName;
        int? stageNumber = entry.StageNumber;
        int? stageIndex = entry.StageIndex;
        foreach (TriggerCatalogEntry other in _entries.Values)
        {
            if (!string.Equals(other.Context, entry.Context, StringComparison.Ordinal)
                || !string.Equals(other.ActorId, entry.ActorId, StringComparison.Ordinal))
            {
                continue;
            }

            actorName ??= other.ActorDisplayName;
            if (string.Equals(other.StageId, entry.StageId, StringComparison.Ordinal))
            {
                stageName ??= other.DisplayName;
                stageNumber ??= other.StageNumber;
                stageIndex ??= other.StageIndex;
            }

            if (stageName is not null && actorName is not null
                && stageNumber is not null && stageIndex is not null)
            {
                break;
            }
        }

        return entry with
        {
            DisplayName = stageName,
            ActorDisplayName = actorName,
            StageNumber = stageNumber,
            StageIndex = stageIndex,
        };
    }

    /// <summary>Removes the placeholder for this stage, if any, and returns what it held.</summary>
    private TriggerCatalogEntry? RetirePlaceholderFor(TriggerCatalogEntry observed)
    {
        var placeholder = new EventKey(
            observed.Context,
            observed.ActorId,
            EventKey.UnobservedAnimationId,
            observed.Phase,
            observed.StageId);
        if (_entries.TryRemove(placeholder, out TriggerCatalogEntry? exact))
        {
            Interlocked.Increment(ref _revision);
            return exact;
        }

        // The phase is derived from the clip once observed, so it may differ from the phase the
        // stage array implied. Retire any placeholder that matches the stage regardless of phase.
        TriggerCatalogEntry? removed = null;
        foreach (EventKey key in _entries.Keys)
        {
            if (key.IsUnobservedPlaceholder
                && string.Equals(key.Context, observed.Context, StringComparison.Ordinal)
                && string.Equals(key.ActorId, observed.ActorId, StringComparison.Ordinal)
                && string.Equals(key.StageId, observed.StageId, StringComparison.Ordinal)
                && _entries.TryRemove(key, out TriggerCatalogEntry? candidate))
            {
                removed ??= candidate;
                Interlocked.Increment(ref _revision);
            }
        }

        return removed;
    }

    /// <summary>Registers a whole stage array read from the game before the stages are reached.</summary>
    public IReadOnlyList<TriggerCatalogEntry> RegisterEnumerated(IEnumerable<TriggerCatalogEntry> entries)
    {
        var added = new List<TriggerCatalogEntry>();
        foreach (TriggerCatalogEntry entry in entries)
        {
            if (Register(entry))
            {
                added.Add(entry);
            }
        }

        return added;
    }

    /// <summary>
    /// Ordered the way the gallery presents its stages, so a row can be found by the number shown
    /// on screen. Entries without a number sort after the numbered ones.
    /// </summary>
    public IReadOnlyList<TriggerCatalogEntry> Snapshot() => _entries.Values
        .OrderBy(x => x.Context, StringComparer.Ordinal)
        .ThenBy(x => x.ActorDisplayName ?? x.ActorId, StringComparer.Ordinal)
        .ThenBy(x => x.ActorId, StringComparer.Ordinal)
        .ThenBy(x => x.DisplayNumber ?? int.MaxValue)
        .ThenBy(x => x.StageId, StringComparer.Ordinal)
        .ThenBy(x => x.Phase, StringComparer.Ordinal)
        .ThenBy(x => x.AnimationId, StringComparer.Ordinal)
        .ToArray();

    public bool TryGet(EventKey key, out TriggerCatalogEntry entry) =>
        _entries.TryGetValue(key, out entry!);

    /// <summary>
    /// Writes the catalog atomically. A failed write leaves the previous file untouched and never
    /// leaves a partial document behind (SPEC001 9章).
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long revision = Interlocked.Read(ref _revision);
            if (revision == Interlocked.Read(ref _persistedRevision))
            {
                return;
            }

            var document = new TriggerCatalogDocument(
                SupportedSchemaVersion,
                _gameBuildId,
                _targetGameBuild,
                DateTimeOffset.UtcNow,
                Snapshot());

            string? directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temp, _path, true);
            Interlocked.Exchange(ref _persistedRevision, revision);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logWarning?.Invoke($"Could not write the trigger catalog '{_path}': {exception.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LoadExisting()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            TriggerCatalogDocument? document = JsonSerializer.Deserialize<TriggerCatalogDocument>(
                File.ReadAllText(_path),
                JsonOptions);
            if (document is null || document.SchemaVersion != SupportedSchemaVersion)
            {
                return;
            }

            // Catalogs are build-specific and must never be carried across builds (SPEC001 10.3).
            if (!string.Equals(
                    document.TargetGameBuild.GameAssemblySha256,
                    _targetGameBuild.GameAssemblySha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    document.TargetGameBuild.GlobalMetadataSha256,
                    _targetGameBuild.GlobalMetadataSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (TriggerCatalogEntry entry in document.Triggers)
            {
                _entries[entry.Key] = entry;
            }

            Interlocked.Exchange(ref _persistedRevision, Interlocked.Read(ref _revision));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A partial or obsolete catalog must never prevent game startup.
            _logWarning?.Invoke($"Ignoring an unreadable trigger catalog '{_path}': {exception.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record TriggerCatalogDocument(
        int SchemaVersion,
        string GameBuildId,
        TargetGameBuild TargetGameBuild,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<TriggerCatalogEntry> Triggers);
}
