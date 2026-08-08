namespace SiNiSistar2.Pleasure.Core;

/// <summary>What a tick of the milk reservoir produced.</summary>
public enum MilkOutcome
{
    None,

    /// <summary>It drained to nothing; the swelling should step down.</summary>
    Emptied,
}

/// <summary>
/// The milk that sexual attacks on a swollen body accumulate, and that the body works off by itself
/// (SPEC003 5.8, FR-259, FR-264).
///
/// It does not fill with time. Time would make the escalation something that happens to a player
/// who put the controller down, which is the opposite of a penalty for what they are doing. Filling
/// from hits means the gauge is a record of what the run has actually been like, and a full one is
/// earned rather than waited out.
///
/// Below full, the gauge is a one-way countdown to the escalation. At full it turns around: the
/// escalation is worn, the gauge drains on its own, and the swelling steps down when it reaches
/// nothing. There is no key for this and no action to take.
///
/// That is what makes it a penalty rather than a chore. The gauge is the only way out, and the same
/// attacks that filled it keep filling it, so an escalated player who cannot get clear of a fight
/// is not counting down — they are losing ground. How long it lasts is decided by how well the next
/// half minute goes, which is a better question than any number the config could hold.
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

    /// <summary>Whether the gauge can ever go down. 0 leaves the escalation with no way out.</summary>
    public bool CanDrain => _drainPerSecond > 0f;

    /// <summary>Whether it is full, which is what escalates the swelling.</summary>
    public bool IsFull => Fill >= 1f;

    /// <summary>
    /// Records one sexual hit on a swollen body. Returns true on the hit that fills it, once, so the
    /// caller can escalate without having to remember that it already did.
    ///
    /// It fills while the escalation is already worn as well, and that is the point: the way out is
    /// the gauge, so a hit taken while escalated puts the way out further away. Nothing here knows
    /// which swelling is worn — the caller does, and it is the caller that decides what a full gauge
    /// means.
    /// </summary>
    public bool AddFromHit()
    {
        if (_perHit <= 0f || IsFull)
        {
            return false;
        }

        Fill = Math.Min(1f, Fill + _perHit);
        return IsFull;
    }

    /// <summary>
    /// Works the gauge down. Called only while the escalation is worn and the player is free to
    /// recover; below the escalation the gauge does not move on its own (FR-259).
    /// </summary>
    public MilkOutcome Tick(double delta)
    {
        if (delta <= 0d || !CanDrain || Fill <= 0f)
        {
            return MilkOutcome.None;
        }

        Fill = (float)Math.Max(0d, Fill - (_drainPerSecond * delta));
        return Fill > 0f ? MilkOutcome.None : MilkOutcome.Emptied;
    }

    public void LoadFrom(float fill) => Fill = Math.Clamp(fill, 0f, 1f);

    public void Reset() => Fill = 0f;
}
