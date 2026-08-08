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

/// <summary>
/// Whether the climax that just happened ends the run (SPEC003 5.5.2, FR-215, FR-216, FR-279).
///
/// Kept here rather than in the observer so the rule can be tested without a game (FR-232). The
/// observer supplies the facts it can only learn from the game — the count, the durability, whether
/// the player is already dead — and applies whatever comes back.
/// </summary>
public static class ClimaxLethality
{
    /// <summary>
    /// Called at the moment a climax completes, never on a sweep.
    ///
    /// A per-frame "is the count at the limit" test would kill the player the instant a save stored
    /// at or above the limit was loaded, before they were given control. Making the climax itself
    /// the trigger means such a save is survivable until the next one lands (DEC-258).
    /// </summary>
    /// <param name="tuning">Climax configuration.</param>
    /// <param name="count">The climax count including the one that just happened.</param>
    /// <param name="maxDurability">The player's maximum durability, or 0 when it cannot be read.</param>
    /// <param name="alreadyDead">Whether the game already has the player in a defeat state.</param>
    /// <param name="alreadyFired">Whether this run has already been made lethal.</param>
    public static bool ShouldBeLethal(
        ClimaxTuning tuning,
        int count,
        float maxDurability,
        bool alreadyDead,
        bool alreadyFired)
    {
        if (!tuning.Enabled || !tuning.GameOverEnabled || alreadyDead || alreadyFired)
        {
            return false;
        }

        int limit = ClimaxLimit.Compute(tuning.LimitBase, tuning.LimitPerDurability, maxDurability);

        // A limit of zero is a configuration error rather than "fatal at once" (5.5.1). Treating it
        // as a ceiling would make the first climax of a fresh install lethal.
        return limit > 0 && count >= limit;
    }
}
