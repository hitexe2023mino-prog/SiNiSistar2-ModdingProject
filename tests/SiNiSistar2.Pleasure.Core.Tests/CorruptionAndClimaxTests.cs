namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// Corruption is required to be one-way, and the climax count is the thing that actually decides
/// defeat. Both are persisted, so a mistake here follows the player across sessions
/// (SPEC003 5.7, 5.4, 5.5).
/// </summary>
public sealed class CorruptionAndClimaxTests
{
    /// <summary>AC-215: nothing the player can do lowers corruption.</summary>
    [Fact]
    public void CorruptionNeverFalls()
    {
        var track = new CorruptionTrack(cap: 10f);
        track.Add(3f);

        track.Add(-5f);
        track.Add(0f);

        Assert.Equal(3f, track.Value, 5);
    }

    /// <summary>AC-216: the cap stops growth without ever taking value away.</summary>
    [Fact]
    public void TheCapStopsGrowthWithoutReducing()
    {
        var track = new CorruptionTrack(cap: 5f);
        track.Add(4f);
        track.Add(4f);

        Assert.Equal(5f, track.Value, 5);
        Assert.True(track.IsAtCap);

        track.Add(4f);
        Assert.Equal(5f, track.Value, 5);
    }

    /// <summary>
    /// Loading an earlier save is not a decrease: the one-way rule governs progress inside one
    /// timeline, not which timeline is being played (SPEC003 4.4).
    /// </summary>
    [Fact]
    public void LoadingASaveSetsTheValueEvenIfItIsLower()
    {
        var track = new CorruptionTrack(cap: 10f);
        track.Add(8f);

        track.LoadFrom(2f);

        Assert.Equal(2f, track.Value, 5);
    }

    [Fact]
    public void LoadingClampsOutOfRangeValuesFromADamagedFile()
    {
        var track = new CorruptionTrack(cap: 10f);

        track.LoadFrom(-4f);
        Assert.Equal(0f, track.Value, 5);

        track.LoadFrom(999f);
        Assert.Equal(10f, track.Value, 5);
    }

    /// <summary>AC-214: resetting the count leaves corruption alone. They are separate tracks.</summary>
    [Fact]
    public void ResettingTheCountDoesNotTouchCorruption()
    {
        var ledger = new ClimaxLedger();
        var track = new CorruptionTrack(cap: 10f);
        ledger.Record();
        ledger.Record();
        track.Add(2f);

        ledger.ResetCount();

        Assert.Equal(0, ledger.Count);
        Assert.Equal(2f, track.Value, 5);
    }

    /// <summary>AC-212: the ceiling grows with durability, so ordinary progression raises it.</summary>
    [Theory]
    [InlineData(3, 0.1f, 0f, 3)]
    [InlineData(3, 0.1f, 50f, 8)]
    [InlineData(3, 0.1f, 100f, 13)]
    public void TheLimitGrowsWithDurability(int baseLimit, float per, float durability, int expected)
    {
        Assert.Equal(expected, ClimaxLimit.Compute(baseLimit, per, durability));
    }

    /// <summary>
    /// FR-214: when durability cannot be read the base still applies, rather than the limit
    /// collapsing to zero and making the first hold fatal.
    /// </summary>
    [Fact]
    public void AnUnreadableDurabilityLeavesTheBaseIntact()
    {
        Assert.Equal(5, ClimaxLimit.Compute(5, 0.2f, 0f));
        Assert.Equal(5, ClimaxLimit.Compute(5, 0f, 100f));
    }

    [Fact]
    public void TheLimitIsReachedOnlyAtOrAboveTheCount()
    {
        var ledger = new ClimaxLedger();
        ledger.Record();
        ledger.Record();

        Assert.False(ledger.IsAtLimit(3));
        ledger.Record();
        Assert.True(ledger.IsAtLimit(3));

        // A limit of zero is not "immediately fatal"; it means no limit has been configured.
        Assert.False(ledger.IsAtLimit(0));
    }
}
