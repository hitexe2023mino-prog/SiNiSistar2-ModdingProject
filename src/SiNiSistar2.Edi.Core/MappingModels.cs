namespace SiNiSistar2.Edi.Core;

public static class EdiChannels
{
    public const string Main = "main";
    public const string Breast = "breast";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Main, Breast };
}

public sealed class MappingDocument
{
    public int SchemaVersion { get; init; }
    public string MappingVersion { get; init; } = string.Empty;
    public TargetGameBuild TargetGameBuild { get; init; } = new();
    public List<EventMapping> Events { get; init; } = new();
    public List<StatusRule> StatusRules { get; init; } = new();
    public Dictionary<string, string> DefaultFillers { get; init; } = new(StringComparer.Ordinal);
}

public sealed class TargetGameBuild
{
    public string GameAssemblySha256 { get; init; } = string.Empty;
    public string GlobalMetadataSha256 { get; init; } = string.Empty;
}

public sealed class EventMapping
{
    public string Id { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public string AnimationId { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string? Gallery { get; init; }
    public List<string> Channels { get; init; } = new();
    public string? SeekMode { get; init; }
    public string? IgnoreReason { get; init; }

    public EventKey Key => new(Context, ActorId, AnimationId, Phase);
}

public sealed class StatusRule
{
    public string StatusId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string? Channel { get; init; }
    public string? FillerGallery { get; init; }
    public string? IgnoreReason { get; init; }
}

public readonly record struct EventKey(
    string Context,
    string ActorId,
    string AnimationId,
    string Phase);

public sealed record EventObservation(
    EventKey Key,
    double NormalizedTime,
    double ClipLengthSeconds,
    bool IsLooping,
    string SceneName,
    DateTimeOffset ObservedAt);

public enum MappingDisposition
{
    Mapped,
    Ignored,
    Unclassified,
}

public sealed record MappingValidationResult(
    MappingRepository? Repository,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Repository is not null && Errors.Count == 0;
}
