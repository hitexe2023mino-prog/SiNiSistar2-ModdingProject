using System.Collections.Concurrent;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

public interface IEventDiagnostics
{
    bool RecordEvent(EventObservation observation);
    void RegisterStatus(string statusId, string displayName);
    Task WriteCoverageAsync(CancellationToken cancellationToken = default);
}

public sealed class DiagnosticRecorder : IEventDiagnostics
{
    private readonly MappingRepository _mappings;
    private readonly string _outputPath;
    private readonly string _candidatesPath;
    private readonly ConcurrentDictionary<EventKey, ObservedEvent> _events = new();
    private readonly ConcurrentDictionary<string, string> _statuses = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public DiagnosticRecorder(
        MappingRepository mappings,
        string outputPath,
        string? candidatesPath = null)
    {
        _mappings = mappings;
        _outputPath = outputPath;
        _candidatesPath = candidatesPath
            ?? Path.Combine(
                Path.GetDirectoryName(outputPath) ?? string.Empty,
                "mapping-candidates.json");
        LoadPreviousCandidates();
    }

    public bool RecordEvent(EventObservation observation)
    {
        var created = false;
        _events.AddOrUpdate(
            observation.Key,
            _ =>
            {
                created = true;
                return ObservedEvent.From(observation);
            },
            (_, current) => current.Update(observation));
        return created;
    }

    public void RegisterStatus(string statusId, string displayName) =>
        _statuses.TryAdd(statusId, displayName);

    public async Task WriteCoverageAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var eventEntries = _events.Values
                .OrderBy(x => x.Context, StringComparer.Ordinal)
                .ThenBy(x => x.ActorId, StringComparer.Ordinal)
                .ThenBy(x => x.AnimationId, StringComparer.Ordinal)
                .ThenBy(x => x.Phase, StringComparer.Ordinal)
                .ThenBy(x => x.StageId, StringComparer.Ordinal)
                .Select(x => new CoverageEvent(x, _mappings.Classify(x.Key).ToString().ToLowerInvariant()))
                .ToArray();

            var statusEntries = _statuses
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new CoverageStatus(
                    x.Key,
                    x.Value,
                    _mappings.ClassifyStatus(x.Key).ToString().ToLowerInvariant()))
                .ToArray();

            var report = new CoverageReport(
                _mappings.Document.MappingVersion,
                DateTimeOffset.UtcNow,
                eventEntries,
                statusEntries,
                eventEntries.Count(x => x.Classification == "unclassified")
                    + statusEntries.Count(x => x.Classification == "unclassified"));

            var candidates = new MappingCandidateReport(
                1,
                _mappings.Document.MappingVersion,
                _mappings.Document.TargetGameBuild,
                DateTimeOffset.UtcNow,
                _events.Values
                    .OrderBy(x => x.Context, StringComparer.Ordinal)
                    .ThenBy(x => x.ActorId, StringComparer.Ordinal)
                    .ThenBy(x => x.AnimationId, StringComparer.Ordinal)
                    .ThenBy(x => x.Phase, StringComparer.Ordinal)
                    .ThenBy(x => x.StageId, StringComparer.Ordinal)
                    .Select(ToCandidate)
                    .ToArray(),
                statusEntries);

            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await WriteAtomicAsync(_outputPath, report, cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(_candidatesPath, candidates, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private MappingCandidate ToCandidate(ObservedEvent observation)
    {
        _mappings.TryGet(observation.Key, out EventMapping? existing);
        return new MappingCandidate(
            CreateCandidateId(observation.Key),
            observation.Context,
            observation.ActorId,
            observation.AnimationId,
            observation.Phase,
            observation.StageId,
            _mappings.Classify(observation.Key).ToString().ToLowerInvariant(),
            existing is null
                ? null
                : string.Join(
                    ", ",
                    existing.Outputs.Select(x => $"{x.Id}={x.Gallery ?? "silent"}")),
            existing is null
                ? Array.Empty<string>()
                : existing.Outputs.Select(x => x.Id).ToArray(),
            existing?.SeekMode,
            existing?.IgnoreReason,
            observation.SceneName,
            observation.FirstObservedAt,
            observation.LastObservedAt,
            observation.ClipLengthSeconds,
            observation.IsLooping);
    }

    private void LoadPreviousCandidates()
    {
        if (!File.Exists(_candidatesPath))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_candidatesPath));
            JsonElement root = document.RootElement;
            JsonElement build = root.GetProperty("targetGameBuild");
            if (!string.Equals(
                    build.GetProperty("gameAssemblySha256").GetString(),
                    _mappings.Document.TargetGameBuild.GameAssemblySha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    build.GetProperty("globalMetadataSha256").GetString(),
                    _mappings.Document.TargetGameBuild.GlobalMetadataSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (JsonElement item in root.GetProperty("events").EnumerateArray())
            {
                var key = new EventKey(
                    item.GetProperty("context").GetString() ?? string.Empty,
                    item.GetProperty("actorId").GetString() ?? string.Empty,
                    item.GetProperty("animationId").GetString() ?? string.Empty,
                    item.GetProperty("phase").GetString() ?? string.Empty,
                    item.TryGetProperty("stageId", out JsonElement stage)
                        ? stage.GetString() ?? EventKey.DefaultStageId
                        : EventKey.DefaultStageId);
                _events[key] = new ObservedEvent(
                    key,
                    key.Context,
                    key.ActorId,
                    key.AnimationId,
                    key.Phase,
                    key.StageId,
                    item.GetProperty("sceneName").GetString() ?? string.Empty,
                    item.GetProperty("firstObservedAt").GetDateTimeOffset(),
                    item.GetProperty("lastObservedAt").GetDateTimeOffset(),
                    item.GetProperty("clipLengthSeconds").GetDouble(),
                    item.GetProperty("isLooping").GetBoolean());
            }

            foreach (JsonElement item in root.GetProperty("statuses").EnumerateArray())
            {
                string id = item.GetProperty("statusId").GetString() ?? string.Empty;
                string displayName = item.GetProperty("displayName").GetString() ?? id;
                if (!string.IsNullOrEmpty(id))
                {
                    _statuses[id] = displayName;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A partial or obsolete generated file must never prevent game startup.
        }
    }

    private static string CreateCandidateId(EventKey key)
    {
        string value = $"{key.Context}\n{key.ActorId}\n{key.AnimationId}\n{key.Phase}\n{key.StageId}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        string hash = Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value)));
        return $"captured-{hash[..16].ToLowerInvariant()}";
    }

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record ObservedEvent(
        EventKey Key,
        string Context,
        string ActorId,
        string AnimationId,
        string Phase,
        string StageId,
        string SceneName,
        DateTimeOffset FirstObservedAt,
        DateTimeOffset LastObservedAt,
        double ClipLengthSeconds,
        bool IsLooping)
    {
        public static ObservedEvent From(EventObservation observation) => new(
            observation.Key,
            observation.Key.Context,
            observation.Key.ActorId,
            observation.Key.AnimationId,
            observation.Key.Phase,
            observation.Key.StageId,
            observation.SceneName,
            observation.ObservedAt,
            observation.ObservedAt,
            observation.ClipLengthSeconds,
            observation.IsLooping);

        public ObservedEvent Update(EventObservation observation) => this with
        {
            LastObservedAt = observation.ObservedAt,
            ClipLengthSeconds = observation.ClipLengthSeconds,
            IsLooping = observation.IsLooping,
            SceneName = observation.SceneName,
        };
    }

    private sealed record CoverageReport(
        string MappingVersion,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<CoverageEvent> Events,
        IReadOnlyList<CoverageStatus> Statuses,
        int UnclassifiedCount);

    private sealed record CoverageEvent(
        ObservedEvent Observation,
        string Classification);

    private sealed record CoverageStatus(
        string StatusId,
        string DisplayName,
        string Classification);

    private sealed record MappingCandidateReport(
        int SchemaVersion,
        string SourceMappingVersion,
        TargetGameBuild TargetGameBuild,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<MappingCandidate> Events,
        IReadOnlyList<CoverageStatus> Statuses);

    private sealed record MappingCandidate(
        string Id,
        string Context,
        string ActorId,
        string AnimationId,
        string Phase,
        string StageId,
        string Classification,
        string? Gallery,
        IReadOnlyList<string> Outputs,
        string? SeekMode,
        string? IgnoreReason,
        string SceneName,
        DateTimeOffset FirstObservedAt,
        DateTimeOffset LastObservedAt,
        double ClipLengthSeconds,
        bool IsLooping);
}
