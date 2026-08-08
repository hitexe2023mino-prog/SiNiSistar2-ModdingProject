using System.Text.Json.Serialization;

namespace SiNiSistar2.Edi.Core;

/// <summary>
/// One controllable device. An output owns exactly one EDI channel (named after
/// <see cref="Id"/>) and exactly one EDI variant, so a gallery sent to it can only ever reach
/// the device the roster names (SPEC001 4.2, DEC-002).
/// </summary>
public sealed class OutputBinding
{
    /// <summary>Output identifier. Doubles as the EDI channel name.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-facing name used by the authoring GUI and the logs.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Device name EDI reports from <c>GET /Devices</c>.</summary>
    public string EdiDeviceName { get; init; } = string.Empty;

    /// <summary>EDI variant this output plays.</summary>
    public string EdiVariant { get; init; } = string.Empty;
}

/// <summary>
/// What one output should play for a trigger or a status rule. A <see langword="null"/>
/// <see cref="Gallery"/> means the output is silenced rather than driven by a still waveform
/// (SPEC001 6.2, FR-047).
/// </summary>
public sealed class OutputAssignment
{
    public string Id { get; init; } = string.Empty;

    public string? Gallery { get; init; }
}

public sealed class MappingDocument
{
    public int SchemaVersion { get; init; }
    public string MappingVersion { get; init; } = string.Empty;
    public TargetGameBuild TargetGameBuild { get; init; } = new();

    /// <summary>The device roster. The only definition of which outputs exist (FR-048).</summary>
    public List<OutputBinding> Outputs { get; init; } = new();

    public List<EventMapping> Events { get; init; } = new();
    public List<StatusRule> StatusRules { get; init; } = new();

    /// <summary>
    /// Output id to default filler gallery. Every output needs a key; a <see langword="null"/>
    /// value means the output idles silent (FR-056).
    /// </summary>
    public Dictionary<string, string?> DefaultFillers { get; init; } = new(StringComparer.Ordinal);
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
    public string StageId { get; init; } = EventKey.DefaultStageId;
    public string Disposition { get; init; } = string.Empty;

    /// <summary>
    /// Which outputs this trigger drives, and with what. An output that is absent keeps whatever
    /// it was doing; an output present with a null gallery is stopped (SPEC001 6.2).
    /// </summary>
    public List<OutputAssignment> Outputs { get; init; } = new();

    public string? SeekMode { get; init; }
    public string? IgnoreReason { get; init; }

    /// <summary>Derived from the fields above, so it is never written back into mappings.json.</summary>
    [JsonIgnore]
    public EventKey Key => new(Context, ActorId, AnimationId, Phase, StageId);
}

public sealed class StatusRule
{
    public string StatusId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;

    /// <summary>Which outputs this status selects a filler for (SPEC001 6.3).</summary>
    public List<OutputAssignment> Outputs { get; init; } = new();

    /// <summary>
    /// Rank among the rules of one output; the largest wins. Statuses stack, so several rules can
    /// match at once and the winner must not depend on where a rule happens to sit in the file
    /// (SPEC001 5.4, FR-043, DEC-019).
    /// </summary>
    public int Priority { get; init; }

    public string? IgnoreReason { get; init; }
}

/// <summary>
/// Identifies one trigger. <paramref name="Phase"/> is the role of the animation and
/// <paramref name="StageId"/> is the step within a multi-stage performance; the two are
/// independent because the number of stages differs per enemy (SPEC001 6.2).
/// </summary>
public readonly record struct EventKey(
    string Context,
    string ActorId,
    string AnimationId,
    string Phase,
    string StageId = EventKey.DefaultStageId)
{
    /// <summary>Stage identifier used by triggers that have exactly one stage.</summary>
    public const string DefaultStageId = "default";

    /// <summary>
    /// Placeholder animation id for a stage that was read from a game-side stage array but has not
    /// been played yet, so its clip is not known. Such an entry is a catalog placeholder only: it
    /// is replaced by the real trigger on first observation and can never be mapped.
    /// </summary>
    public const string UnobservedAnimationId = "*";

    public bool IsUnobservedPlaceholder =>
        string.Equals(AnimationId, UnobservedAnimationId, StringComparison.Ordinal);

    public override string ToString() => $"{Context}/{ActorId}/{AnimationId}/{Phase}/{StageId}";
}

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
