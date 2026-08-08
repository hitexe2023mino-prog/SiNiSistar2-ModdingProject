namespace SiNiSistar2.Edi.Core;

/// <summary>
/// One trigger transition: a start, a stage change, or an end. This replaces the withdrawn
/// per-frame `animation-sample` record; the target build has no bones to measure, so the
/// session log stores transitions only (SPEC001 6.4, 付録C).
/// </summary>
public sealed record TriggerTransitionSnapshot(
    string EventInstanceId,
    EventKey? Previous,
    EventKey? Current,
    string SceneName,
    double? ClipLengthSeconds,
    bool? IsLooping,
    double? NormalizedTime,
    double UnityTimeSeconds,
    double UnscaledTimeSeconds,
    double TimeScale,
    IReadOnlyList<string> ActiveStatuses);

public sealed record TextChangeSnapshot(
    string ComponentType,
    string Path,
    string Text,
    bool Active);

public sealed record CaptureCapability(
    string Category,
    bool Available,
    string? UnavailableReason);
