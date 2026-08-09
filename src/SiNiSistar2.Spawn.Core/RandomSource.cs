namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// The MOD's own random stream (SPEC004 5.6, FR-315). Nothing here touches the game's RNG, so
/// the game's own rolls are left byte-for-byte what they would have been without the MOD.
/// </summary>
public interface IRandomSource
{
    /// <summary>Uniform value in [0, 1).</summary>
    float NextFloat();

    /// <summary>Uniform integer in [0, exclusiveMax). Returns 0 when exclusiveMax is 0 or less.</summary>
    int NextInt(int exclusiveMax);
}

/// <summary>
/// Seeded stream for one area visit. `Seed ^ sceneId ^ visitCount` per SPEC004 5.1-3, so the same
/// seed, area and visit number reproduce the same draws for tests and balance work.
/// </summary>
public sealed class SeededRandomSource : IRandomSource
{
    private readonly Random _random;

    public SeededRandomSource(int seed) => _random = new Random(seed);

    public static SeededRandomSource ForVisit(int baseSeed, int sceneId, int visitCount) =>
        new(baseSeed ^ sceneId ^ visitCount);

    public float NextFloat() => (float)_random.NextDouble();

    public int NextInt(int exclusiveMax) => exclusiveMax <= 0 ? 0 : _random.Next(exclusiveMax);
}

/// <summary>Non-deterministic stream, used when Seed is 0 (SPEC004 5.1-3).</summary>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random = new();

    public float NextFloat() => (float)_random.NextDouble();

    public int NextInt(int exclusiveMax) => exclusiveMax <= 0 ? 0 : _random.Next(exclusiveMax);
}
