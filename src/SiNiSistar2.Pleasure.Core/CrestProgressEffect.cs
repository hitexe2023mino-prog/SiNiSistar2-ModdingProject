namespace SiNiSistar2.Pleasure.Core;

/// <summary>Tuning for the progression effect (SPEC005 5.4).</summary>
public sealed record CrestFxTuning(bool Enabled, float DurationSeconds, float IntensityPerStage)
{
    public static CrestFxTuning Disabled { get; } = new(false, 0f, 0f);

    public bool HasEffect => Enabled && DurationSeconds > 0f && IntensityPerStage > 0f;
}

/// <summary>
/// The pink haze that marks the curse advancing (SPEC005 5.4, FR-413).
///
/// The same visual language as a climax, because it is the same kind of event: something happened
/// to the body that the player did not choose. The corruption HUD already draws the mark filling
/// in, but a HUD element is something you look at when you think to — this is something that
/// happens to the screen while you are busy, which is what makes a stock arriving feel like an
/// event rather than a number moving.
///
/// The intensity climbs with the stage so the last one before the cliff, and the cliff itself, do
/// not read the same as the first warning.
/// </summary>
public static class CrestProgressEffect
{
    /// <summary>
    /// How strongly to draw the haze, in 0..1. Zero draws nothing.
    /// </summary>
    /// <param name="stockLevel">Stocks held once the change has landed.</param>
    /// <param name="maxLevel">The status's level ceiling, read from the game.</param>
    /// <param name="sublimated">Whether this was the step that made the mark permanent.</param>
    /// <param name="perStage">Intensity added per stage.</param>
    public static float Intensity(int stockLevel, int maxLevel, bool sublimated, float perStage)
    {
        if (perStage <= 0f)
        {
            return 0f;
        }

        int top = Math.Max(1, maxLevel);

        // Sublimation is the top stage whatever the level count says, so the strongest haze always
        // lands on the step that cannot be undone.
        int stage = sublimated ? top : Math.Clamp(stockLevel, 0, top);
        if (stage <= 0)
        {
            return 0f;
        }

        // Scaled against the ceiling rather than clamped after multiplying. A raw product saturates:
        // with six stocks and 0.2 a stage, the fifth stock and the sublimation both come out at 1.0,
        // and the step that cannot be undone would look exactly like the last warning before it
        // (AC-412). Dividing by the ceiling keeps the top stage strictly the strongest at any level
        // count the game reports.
        float ceiling = Math.Clamp(perStage * top, 0f, 1f);
        return ceiling * (stage / (float)top);
    }
}
