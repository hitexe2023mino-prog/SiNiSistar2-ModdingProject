namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// A `[Min, Max]` range a per-visit multiplier is drawn from (SPEC004 5.2). The direction each
/// range is allowed to point (only up for counts, only down for delays) is enforced by the
/// profile factory, not here, so the factory can report the violation with its config key.
/// </summary>
public readonly record struct MultiplierRange(float Min, float Max)
{
    public bool IsValid => Min <= Max && Min > 0f && float.IsFinite(Min) && float.IsFinite(Max);

    /// <summary>Whether every draw from this range is exactly 1.0, i.e. it changes nothing.</summary>
    public bool IsIdentity => Min == 1f && Max == 1f;

    public float Sample(IRandomSource random) =>
        Min == Max ? Min : Min + ((Max - Min) * random.NextFloat());

    public static MultiplierRange Identity => new(1f, 1f);

    public override string ToString() => Min == Max ? $"x{Min:0.###}" : $"x[{Min:0.###},{Max:0.###}]";
}
