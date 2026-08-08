namespace SiNiSistar2.Pleasure.Core;

/// <summary>What a tick of the milk reservoir produced.</summary>
public enum MilkOutcome
{
    None,

    /// <summary>Milking drained it to nothing; the swelling should step down.</summary>
    Emptied,
}

/// <summary>
/// The milk the swelling produces, and milking removes (SPEC003 5.8, FR-259).
///
/// This replaces the fixed duration milking used to have. A timer said only "wait six seconds"; a
/// reservoir says how much there is to get through, which is a thing the player can watch, plan
/// around, and be caught out by. It also gives the swelling something to do while it is worn: it
/// fills, so leaving it alone makes the eventual milking longer.
/// </summary>
public sealed class MilkReservoir
{
    private readonly float _fillPerSecond;
    private readonly float _drainPerSecond;
    private readonly float _superMultiplier;

    public MilkReservoir(float fillPerSecond, float drainPerSecond, float superMultiplier = 2f)
    {
        _fillPerSecond = Math.Max(0f, fillPerSecond);
        _drainPerSecond = Math.Max(0f, drainPerSecond);
        _superMultiplier = Math.Max(1f, superMultiplier);
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

    /// <summary>
    /// Advances by one frame.
    /// </summary>
    /// <param name="delta">Seconds since the last tick.</param>
    /// <param name="swollen">Whether <c>Breast</c> or <c>BreastSuper</c> is present.</param>
    /// <param name="super">Whether it is the escalated one, which fills faster.</param>
    public MilkOutcome Tick(double delta, bool swollen, bool super)
    {
        if (delta <= 0d)
        {
            return MilkOutcome.None;
        }

        if (IsMilking)
        {
            Fill = (float)Math.Max(0d, Fill - (_drainPerSecond * delta));
            if (Fill > 0f)
            {
                return MilkOutcome.None;
            }

            IsMilking = false;
            return MilkOutcome.Emptied;
        }

        // Only swelling produces milk. Without that the reservoir would refill after a cure and the
        // gauge would report something the player has no way to act on.
        if (!swollen)
        {
            return MilkOutcome.None;
        }

        float rate = super ? _fillPerSecond * _superMultiplier : _fillPerSecond;
        Fill = (float)Math.Min(1d, Fill + (rate * delta));
        return MilkOutcome.None;
    }

    public void LoadFrom(float fill) => Fill = Math.Clamp(fill, 0f, 1f);

    public void Reset()
    {
        Fill = 0f;
        IsMilking = false;
    }
}
