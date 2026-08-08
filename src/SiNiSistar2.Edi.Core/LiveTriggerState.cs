namespace SiNiSistar2.Edi.Core;

/// <summary>What the game is playing right now, as last seen by the observer.</summary>
public sealed record LiveTrigger(
    EventKey Key,
    string SceneName,
    double NormalizedTime,
    double ClipLengthSeconds,
    bool IsLooping,
    DateTimeOffset ObservedAt);

/// <summary>
/// Hands the currently observed trigger from the Unity main thread to the authoring server.
/// Inferring which catalog row matches the gallery screen proved unreliable — this build reports
/// no usable stage number — so the GUI highlights the row the game is actually playing instead.
/// </summary>
public sealed class LiveTriggerState
{
    private LiveTrigger? _current;

    /// <summary>Called from the main thread on every observation.</summary>
    public void Set(LiveTrigger? current) => Volatile.Write(ref _current, current);

    /// <summary>
    /// Read from the authoring server's worker threads. Returns null once the reading is older
    /// than <paramref name="maxAge"/>, so a stale row is never highlighted after the game stops
    /// reporting (paused, quit, or the observer failed closed).
    /// </summary>
    public LiveTrigger? Read(TimeSpan maxAge)
    {
        LiveTrigger? current = Volatile.Read(ref _current);
        if (current is null)
        {
            return null;
        }

        return DateTimeOffset.UtcNow - current.ObservedAt <= maxAge ? current : null;
    }
}
