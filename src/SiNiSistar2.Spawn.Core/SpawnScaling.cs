namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// Componentwise scaling rules for the spawner fields (SPEC004 5.2 の表). Counts only go up and
/// delays only go down relative to the game's resolved base value, so a multiplier can never make
/// an area easier than vanilla by accident.
/// </summary>
public static class SpawnScaling
{
    /// <summary>Spawn counts and pool sizes: round, but never below the original.</summary>
    public static int ScaleCount(int original, float multiplier)
    {
        if (original <= 0)
        {
            return original;
        }

        var scaled = (int)MathF.Round(original * multiplier);
        return Math.Max(original, scaled);
    }

    /// <summary>Intervals and cool times: scale down, but never below zero and never above the original.</summary>
    public static float ScaleDelay(float original, float multiplier)
    {
        if (original <= 0f || !float.IsFinite(original))
        {
            return original;
        }

        return Math.Clamp(original * multiplier, 0f, original);
    }
}
