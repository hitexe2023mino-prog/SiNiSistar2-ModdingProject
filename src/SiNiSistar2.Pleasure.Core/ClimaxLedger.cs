namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// Counts climaxes and answers whether the limit has been reached (SPEC003 5.4, 5.5).
///
/// The defeat condition is the count rather than the gauge itself. Making the gauge the condition
/// would settle a hold in one fill and leave no room for "worn down over many" to mean anything
/// (SPEC003 DEC-202).
/// </summary>
public sealed class ClimaxLedger
{
    public int Count { get; private set; }

    /// <summary>Records one climax.</summary>
    public void Record() => Count++;

    /// <summary>Clears the count. Corruption is untouched — it is a separate, one-way track.</summary>
    public void ResetCount() => Count = 0;

    /// <summary>Sets the count read from a sidecar file.</summary>
    public void LoadFrom(int saved) => Count = Math.Max(0, saved);

    public bool IsAtLimit(int limit) => limit > 0 && Count >= limit;
}

/// <summary>How many climaxes the player can take before a hold becomes fatal.</summary>
public static class ClimaxLimit
{
    /// <summary>
    /// Base plus a share of maximum durability, floored. Durability is the stat that already means
    /// "how much can be taken", so tying the ceiling to it lets ordinary progression raise it
    /// without a separate currency.
    /// </summary>
    public static int Compute(int baseLimit, float perDurability, float maxDurability)
    {
        if (perDurability <= 0f || maxDurability <= 0f)
        {
            return Math.Max(0, baseLimit);
        }

        double scaled = Math.Floor(maxDurability * (double)perDurability);
        double total = baseLimit + scaled;
        return (int)Math.Clamp(total, 0d, int.MaxValue);
    }
}
