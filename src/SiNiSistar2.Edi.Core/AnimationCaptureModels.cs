namespace SiNiSistar2.Edi.Core;

public readonly record struct Vector3Snapshot(double X, double Y, double Z)
{
    public static Vector3Snapshot operator -(Vector3Snapshot left, Vector3Snapshot right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
}

public readonly record struct QuaternionSnapshot(double X, double Y, double Z, double W);

public sealed record TransformSnapshot(
    string Path,
    string Name,
    bool Active,
    Vector3Snapshot LocalPosition,
    QuaternionSnapshot LocalRotation,
    Vector3Snapshot LocalScale,
    Vector3Snapshot WorldPosition,
    QuaternionSnapshot WorldRotation,
    Vector3Snapshot LossyScale,
    string? HumanoidBone);

public sealed record AnimationClipSnapshot(
    string Name,
    double LengthSeconds,
    double FrameRate,
    bool IsLooping,
    double Weight,
    bool HasMotionCurves,
    bool HasRootCurves,
    bool HasRootMotion,
    bool IsHumanMotion);

public sealed record AnimatorStateSnapshot(
    int FullPathHash,
    int ShortNameHash,
    int TagHash,
    double NormalizedTime,
    double LengthSeconds,
    double Speed,
    double SpeedMultiplier,
    bool IsLooping);

public sealed record AnimatorLayerSnapshot(
    int Index,
    string Name,
    double Weight,
    bool IsInTransition,
    AnimatorStateSnapshot CurrentState,
    AnimatorStateSnapshot? NextState,
    AnimatorTransitionSnapshot? Transition,
    IReadOnlyList<AnimationClipSnapshot> CurrentClips,
    IReadOnlyList<AnimationClipSnapshot> NextClips);

public sealed record AnimatorTransitionSnapshot(
    int FullPathHash,
    int NameHash,
    int UserNameHash,
    double Duration,
    double NormalizedTime,
    bool HasFixedDuration,
    bool IsAnyState,
    bool IsEntry,
    bool IsExit);

public sealed record AnimatorParameterSnapshot(
    string? Name,
    int? NameHash,
    string? Type,
    object? Value,
    bool Available,
    string? UnavailableReason);

public sealed record AnimatorRuntimeSnapshot(
    string Name,
    string Path,
    bool ActiveAndEnabled,
    double Speed,
    string UpdateMode,
    bool ApplyRootMotion,
    bool HasRootMotion,
    bool IsHuman,
    Vector3Snapshot DeltaPosition,
    QuaternionSnapshot DeltaRotation,
    Vector3Snapshot RootPosition,
    QuaternionSnapshot RootRotation,
    Vector3Snapshot BodyPosition,
    QuaternionSnapshot BodyRotation,
    Vector3Snapshot Velocity,
    Vector3Snapshot AngularVelocity,
    Vector3Snapshot PivotPosition,
    IReadOnlyList<AnimatorLayerSnapshot> Layers,
    IReadOnlyList<AnimatorParameterSnapshot> Parameters,
    IReadOnlyList<TransformSnapshot> Transforms);

public sealed record AnimationFrameSnapshot(
    string EventInstanceId,
    EventKey EventKey,
    string SceneName,
    long RealtimeMilliseconds,
    int FrameCount,
    double UnityTimeSeconds,
    double UnscaledTimeSeconds,
    double DeltaTimeSeconds,
    double UnscaledDeltaTimeSeconds,
    double TimeScale,
    double NormalizedTime,
    IReadOnlyList<string> ActiveStatuses,
    AnimatorRuntimeSnapshot Animator,
    IReadOnlyList<AnimatorRuntimeSnapshot>? RelatedAnimators = null);

public sealed record TextChangeSnapshot(
    string ComponentType,
    string Path,
    string Text,
    bool Active);

public sealed record CaptureCapability(
    string Category,
    bool Available,
    string? UnavailableReason);
