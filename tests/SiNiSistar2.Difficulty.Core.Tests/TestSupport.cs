namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// A random source with a scripted sequence, so window timings are exact in tests. A value of
/// 0.5 produces a jitter factor of 1.0 for any jitter setting, which is the neutral case.
/// </summary>
public sealed class FakeRandom : IRandomSource
{
    private readonly double[] _values;
    private int _index;

    public FakeRandom(params double[] values) =>
        _values = values.Length == 0 ? new[] { 0.5d } : values;

    public double NextUnit()
    {
        double value = _values[_index % _values.Length];
        _index++;
        return value;
    }
}

public static class TestSupport
{
    /// <summary>Stands in for the game's own AbnormalType enumerator names.</summary>
    public static IReadOnlyCollection<string> KnownNames { get; } =
        AbnormalTypeDefaults.Pleasure
            .Concat(AbnormalTypeDefaults.Burden)
            .Append(AbnormalTypeDefaults.Defilement)
            .Append("Poison")
            .ToArray();

    public static ProfileValidation Validate(DifficultyOptions options) =>
        DifficultyProfileFactory.Create(options, KnownNames);

    /// <summary>A pleasure tuning with no jitter, so band boundaries land on exact times.</summary>
    public static PleasureTuning Pleasure(
        float interval = 2f,
        float duration = 1f,
        float levelScaling = 0f,
        params string[] types) =>
        new(
            true,
            AbnormalTypeSet.Parse(
                string.Join(",", types.Length == 0 ? new[] { "Lustfull" } : types),
                KnownNames).Set,
            interval,
            0f,
            duration,
            0f,
            levelScaling);

    public static BurdenTuning Burden(
        float penalty = 3f,
        float levelScaling = 0f,
        params string[] types) =>
        new(
            true,
            AbnormalTypeSet.Parse(
                string.Join(",", types.Length == 0 ? new[] { "Pregnant" } : types),
                KnownNames).Set,
            penalty,
            0.5f,
            1f,
            levelScaling);
}
