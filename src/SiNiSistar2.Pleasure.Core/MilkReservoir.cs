namespace SiNiSistar2.Pleasure.Core;

/// <summary>What a tick of the milk reservoir produced.</summary>
public enum MilkOutcome
{
    None,

    /// <summary>Milking drained it to nothing; the swelling should step down.</summary>
    Emptied,
}

/// <summary>
/// The milk that sexual attacks on a swollen body accumulate, and that milking removes
/// (SPEC003 5.8, FR-259).
///
/// It does not fill with time. Time would make the escalation something that happens to a player
/// who put the controller down, which is the opposite of a penalty for what they are doing. Filling
/// from hits means the gauge is a record of what the run has actually been like, and a full one is
/// earned rather than waited out.
///
/// Accumulation is one-way. The only thing that takes milk out is milking, and only the escalated
/// swelling can be milked — so an ordinary swelling walks one way towards the escalation and the
/// gauge is a countdown to it.
/// </summary>
public sealed class MilkReservoir
{
    private readonly float _perHit;
    private readonly float _drainPerSecond;

    public MilkReservoir(float perHit, float drainPerSecond)
    {
        _perHit = Math.Max(0f, perHit);
        _drainPerSecond = Math.Max(0f, drainPerSecond);
    }

    /// <summary>0 to 1.</summary>
    public float Fill { get; private set; }

    public bool IsMilking { get; private set; }

    /// <summary>Whether milking can do anything at all.</summary>
    public bool CanMilk => _drainPerSecond > 0f && Fill > 0f;

    public bool TryStartMilking()
    {
        if (IsMilking || !CanMilk)
        {
            return false;
        }

        IsMilking = true;
        return true;
    }

    /// <summary>Stops milking. Returns whether anything was actually running.</summary>
    public bool StopMilking()
    {
        if (!IsMilking)
        {
            return false;
        }

        IsMilking = false;
        return true;
    }

    /// <summary>Whether it is full, which is what escalates the swelling.</summary>
    public bool IsFull => Fill >= 1f;

    /// <summary>
    /// Records one sexual hit on a swollen body. Returns true on the hit that fills it, once, so the
    /// caller can escalate without having to remember that it already did.
    /// </summary>
    public bool AddFromHit()
    {
        if (_perHit <= 0f || IsFull || IsMilking)
        {
            return false;
        }

        Fill = Math.Min(1f, Fill + _perHit);
        return IsFull;
    }

    /// <summary>Drains while milking. Nothing else moves the gauge (FR-259).</summary>
    public MilkOutcome Tick(double delta)
    {
        if (delta <= 0d || !IsMilking)
        {
            return MilkOutcome.None;
        }

        Fill = (float)Math.Max(0d, Fill - (_drainPerSecond * delta));
        if (Fill > 0f)
        {
            return MilkOutcome.None;
        }

        IsMilking = false;
        return MilkOutcome.Emptied;
    }

    public void LoadFrom(float fill) => Fill = Math.Clamp(fill, 0f, 1f);

    public void Reset()
    {
        Fill = 0f;
        IsMilking = false;
    }
}
