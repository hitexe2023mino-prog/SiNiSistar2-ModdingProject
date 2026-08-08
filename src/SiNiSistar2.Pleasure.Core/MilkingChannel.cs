namespace SiNiSistar2.Pleasure.Core;

/// <summary>Why a milking attempt ended.</summary>
public enum MilkingOutcome
{
    /// <summary>Still going.</summary>
    Running,

    /// <summary>Finished; the status should be taken down a step.</summary>
    Completed,

    /// <summary>Broken off before it finished.</summary>
    Interrupted,
}

/// <summary>
/// Self-milking as something that takes time and leaves the player open (SPEC003 5.8, FR-257).
///
/// The point is not the cure, which could have been instant. The point is that it cannot be done
/// while anything is threatening: it takes seconds, and being hit during them wastes it. A safe
/// place is therefore not an area the game marks as safe — it is any moment nothing is attacking,
/// which is a judgement the player makes and can get wrong. Doing it in front of something is a
/// real option with a real cost, which is what makes it a decision rather than a menu.
/// </summary>
public sealed class MilkingChannel
{
    private readonly double _seconds;

    public MilkingChannel(double seconds) => _seconds = Math.Max(0d, seconds);

    public bool IsRunning { get; private set; }

    public double Elapsed { get; private set; }

    /// <summary>0 to 1, for drawing.</summary>
    public float Progress =>
        _seconds <= 0d ? 1f : (float)Math.Clamp(Elapsed / _seconds, 0d, 1d);

    /// <summary>Whether milking is configured to take any time at all.</summary>
    public bool IsEnabled => _seconds > 0d;

    /// <summary>Starts, or reports false if it was already going or is switched off.</summary>
    public bool TryStart()
    {
        if (IsRunning || !IsEnabled)
        {
            return false;
        }

        IsRunning = true;
        Elapsed = 0d;
        return true;
    }

    public MilkingOutcome Tick(double delta)
    {
        if (!IsRunning)
        {
            return MilkingOutcome.Running;
        }

        if (delta > 0d)
        {
            Elapsed += delta;
        }

        if (Elapsed < _seconds)
        {
            return MilkingOutcome.Running;
        }

        IsRunning = false;
        Elapsed = 0d;
        return MilkingOutcome.Completed;
    }

    /// <summary>
    /// Breaks it off. Returns whether anything was actually running, so a caller can tell the player
    /// their attempt was wasted rather than reporting an interruption that never happened.
    /// </summary>
    public bool Interrupt()
    {
        if (!IsRunning)
        {
            return false;
        }

        IsRunning = false;
        Elapsed = 0d;
        return true;
    }
}
