namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// Scales the status-ailment application rate the game carries on its damage data
/// (<c>DamageParameter.m_AbnormalRate</c>, an <c>Int32</c>) for player-received damage
/// (SPEC002 5.2).
/// </summary>
public static class AbnormalRateScaling
{
    /// <summary>
    /// The value the rate is read as a percentage against. The field's real range is 付録A A-4 and
    /// has not been measured, so <see cref="Apply"/> never uses this cap to lower a rate below
    /// what the game already set.
    /// </summary>
    public const int AssumedMaximum = 100;

    /// <summary>
    /// Returns the scaled rate. The cap is only allowed to hold a value down to what the game
    /// itself configured: if the field turns out to be scaled to something other than 100, a
    /// wrong cap must not silently turn a difficulty increase into a decrease.
    /// </summary>
    public static int Apply(int original, float multiplier)
    {
        if (original <= 0 || Math.Abs(multiplier - 1f) < 1e-6f)
        {
            return original;
        }

        double scaled = original * (double)multiplier;
        var rounded = (int)Math.Round(Math.Min(scaled, int.MaxValue), MidpointRounding.AwayFromZero);

        if (multiplier < 1f)
        {
            // The user asked for a lower rate, so the cap has no business raising it back.
            return Math.Max(0, rounded);
        }

        int ceiling = Math.Max(AssumedMaximum, original);
        return Math.Clamp(rounded, original, ceiling);
    }
}
