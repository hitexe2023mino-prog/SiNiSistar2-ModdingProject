namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// How long <c>BreastSuper</c> lasts before it subsides back to <c>Breast</c> (SPEC003 5.8).
///
/// The escalation is a penalty, not a permanent state. Without a way back, a player who reaches it
/// in a fight carries it until they find the cure, which turns one bad moment into a detour. Giving
/// it a duration makes enduring the aftermath a real option, and it is what lets the status be
/// applied mid-fight at all.
///
/// A duration of zero means it never subsides, which is the shipped state: the requirement that
/// opened <c>BreastSuper</c> to ordinary play said nothing about it wearing off, and adding an exit
/// nobody asked for would be a behaviour change hiding in a default.
/// </summary>
public sealed class BreastSuperTimer
{
    private readonly double _seconds;

    public BreastSuperTimer(double seconds) => _seconds = Math.Max(0d, seconds);

    public bool IsRunning { get; private set; }

    public double Elapsed { get; private set; }

    public double Remaining => _seconds <= 0d ? 0d : Math.Max(0d, _seconds - Elapsed);

    /// <summary>Whether a duration was configured at all.</summary>
    public bool HasDuration => _seconds > 0d;

    /// <summary>
    /// Starts the countdown, or leaves it alone if it is already running. Restarting on every frame
    /// the status is present would mean it never expired.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Elapsed = 0d;
    }

    public void Stop()
    {
        IsRunning = false;
        Elapsed = 0d;
    }

    /// <summary>
    /// Advances the countdown. Returns true exactly once, on the tick that reaches the duration, so
    /// the caller can act on it without needing to remember that it already has.
    /// </summary>
    public bool Tick(double delta)
    {
        if (!IsRunning || !HasDuration || delta <= 0d)
        {
            return false;
        }

        Elapsed += delta;
        if (Elapsed < _seconds)
        {
            return false;
        }

        IsRunning = false;
        Elapsed = 0d;
        return true;
    }
}
