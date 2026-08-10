namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// What the crest does to the rate corruption accumulates at (SPEC005 5.5).
///
/// SPEC003 FR-267 applied one flat multiplier for as long as the status was worn, which made the
/// curse and the mark numerically identical. They are not the same thing: the curse can still be
/// lifted and the mark cannot, and a rate that does not change at that boundary says the boundary
/// does not matter. The curse stages accelerate slightly and in proportion to how far they have
/// gone; sublimation steps to a different number altogether (DEC-413).
///
/// The discontinuity is the point. A player who is still inside the curse should be able to read
/// the acceleration as a warning worth acting on, and a player who has passed it should feel the
/// floor give way.
/// </summary>
public static class CrestStaging
{
    /// <summary>
    /// The multiplier applied to one gain of corruption.
    /// </summary>
    /// <param name="stockLevel">Curse stocks currently held, 0 for none.</param>
    /// <param name="maxLevel">
    /// The status's own level ceiling, read from the game rather than assumed (FR-421). The last
    /// stock is sublimation, so the reversible stages are 1 to <paramref name="maxLevel"/>-1.
    /// </param>
    /// <param name="sublimated">Whether this run has already reached the last stock.</param>
    /// <param name="curseGainMax">What the last reversible stock adds. 0 leaves the curse inert.</param>
    /// <param name="crestGainScale">What sublimation multiplies by outright.</param>
    public static float Coefficient(
        int stockLevel,
        int maxLevel,
        bool sublimated,
        float curseGainMax,
        float crestGainScale)
    {
        // Sublimation replaces the stock term rather than adding to it. The last stock is still
        // held once the mark is permanent, so adding would put the mark one step above the curse
        // on a continuous line — and a continuous line is exactly what this exists to break.
        if (sublimated)
        {
            return Math.Max(1f, crestGainScale);
        }

        if (stockLevel <= 0 || curseGainMax <= 0f)
        {
            return 1f;
        }

        // A ceiling of one means there are no reversible stages at all: the first stock is already
        // the last one, and reaching it is sublimation. There is nothing here to grade.
        int reversible = maxLevel - 1;
        if (reversible <= 0)
        {
            return 1f;
        }

        // Expressed as a fraction of the way to the cliff rather than as a fixed step per stock, so
        // the curse's ceiling stays where it was put whatever level count the game reports.
        float progress = Math.Clamp(stockLevel / (float)reversible, 0f, 1f);
        return 1f + (curseGainMax * progress);
    }
}
