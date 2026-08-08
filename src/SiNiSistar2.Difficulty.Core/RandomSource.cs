namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// Source of the jitter applied to window timings. Injected so that scheduling can be tested
/// deterministically without launching the game (SPEC002 FR-134).
/// </summary>
public interface IRandomSource
{
    /// <summary>A value in [0, 1).</summary>
    double NextUnit();
}

/// <summary>Default source. Not thread safe; the schedulers run on the game's main thread.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    public SystemRandomSource(int seed) => _random = new Random(seed);

    public SystemRandomSource() => _random = new Random();

    public double NextUnit() => _random.NextDouble();
}

/// <summary>Shared timing arithmetic for both schedulers.</summary>
internal static class WindowMath
{
    /// <summary>
    /// Applies a symmetric jitter around <paramref name="value"/>. A jitter of 0 returns the value
    /// unchanged, which is what keeps a fixed-interval configuration exactly fixed.
    /// </summary>
    internal static double Jitter(double value, double jitter, IRandomSource random)
    {
        if (jitter <= 0d || value <= 0d)
        {
            return Math.Max(0d, value);
        }

        double factor = 1d + (jitter * ((2d * random.NextUnit()) - 1d));
        return Math.Max(0d, value * factor);
    }

    /// <summary>
    /// Scales a duration up with the summed level of the statuses driving it. Scaling of 0 leaves
    /// the duration alone, which is the shipped default (SPEC002 6章).
    /// </summary>
    internal static double Lengthen(double value, double scaling, int levelSum) =>
        scaling <= 0d || levelSum <= 0 ? value : value * (1d + (scaling * levelSum));

    /// <summary>Inverse of <see cref="Lengthen"/>: the same level sum shortens a gap.</summary>
    internal static double Shorten(double value, double scaling, int levelSum) =>
        scaling <= 0d || levelSum <= 0 ? value : value / (1d + (scaling * levelSum));
}
