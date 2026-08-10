using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>The succubus regeneration buff (SPEC005 5.1, AC-401, AC-404, AC-406).</summary>
public sealed class RegenBuffTrackTests
{
    private static RegenBuffTrack Track(float perClimax = 10f, float cap = 0f, float hp = 2f, float mp = 1f) =>
        new(new RegenTuning(true, perClimax, cap, hp, mp));

    /// <summary>AC-401: a qualifying climax starts the buff and it restores while it runs.</summary>
    [Fact]
    public void QualifyingClimaxStartsTheBuffAndItRestores()
    {
        RegenBuffTrack track = Track();
        Assert.False(track.IsActive);

        track.OnQualifyingClimax();

        Assert.True(track.IsActive);
        Assert.Equal(10d, track.Remaining, 3);

        RegenTick tick = track.Advance(1d);

        Assert.Equal(2, tick.Hp);
        Assert.Equal(1, tick.Mp);
        Assert.Equal(9d, track.Remaining, 3);
    }

    /// <summary>AC-404: repeated climaxes bank the duration rather than refreshing it (DEC-403).</summary>
    [Fact]
    public void RepeatedClimaxesAddRatherThanReset()
    {
        RegenBuffTrack track = Track();
        track.OnQualifyingClimax();
        track.Advance(4d);

        track.OnQualifyingClimax();

        Assert.Equal(16d, track.Remaining, 3);
    }

    /// <summary>A cap of 0 is not a cap of nothing: it means no ceiling was asked for.</summary>
    [Fact]
    public void ZeroCapMeansNoCeiling()
    {
        RegenBuffTrack track = Track(perClimax: 10f, cap: 0f);
        for (var i = 0; i < 5; i++)
        {
            track.OnQualifyingClimax();
        }

        Assert.Equal(50d, track.Remaining, 3);
    }

    [Fact]
    public void PositiveCapHoldsTheBankedTimeDown()
    {
        RegenBuffTrack track = Track(perClimax: 10f, cap: 25f);
        for (var i = 0; i < 5; i++)
        {
            track.OnQualifyingClimax();
        }

        Assert.Equal(25d, track.Remaining, 3);
    }

    /// <summary>
    /// A rate below one point per second still restores. Flooring every tick independently would
    /// silently return nothing at all, which is the quiet way a regen mechanic does nothing.
    /// </summary>
    [Fact]
    public void FractionsAccumulateRatherThanBeingLost()
    {
        RegenBuffTrack track = Track(perClimax: 10f, hp: 0.4f, mp: 0f);
        track.OnQualifyingClimax();

        Assert.Equal(0, track.Advance(1d).Hp);
        Assert.Equal(0, track.Advance(1d).Hp);
        Assert.Equal(1, track.Advance(1d).Hp);
    }

    /// <summary>Time not advanced is time not spent: a paused game does not drain the buff (FR-406).</summary>
    [Fact]
    public void NotAdvancingDoesNotSpendTheBuff()
    {
        RegenBuffTrack track = Track();
        track.OnQualifyingClimax();

        track.Advance(0d);
        track.Advance(-5d);

        Assert.Equal(10d, track.Remaining, 3);
    }

    [Fact]
    public void TheBuffStopsExactlyWhenItRunsOut()
    {
        RegenBuffTrack track = Track(perClimax: 2f, hp: 1f, mp: 0f);
        track.OnQualifyingClimax();

        RegenTick tick = track.Advance(10d);

        Assert.False(track.IsActive);

        // Only the two seconds it actually had are paid out, not the ten that were offered.
        Assert.Equal(2, tick.Hp);
        Assert.True(track.Advance(10d).IsEmpty);
    }

    /// <summary>AC-406: a save point, a load or a game over ends it, fractions included (FR-407).</summary>
    [Fact]
    public void DiscardEndsTheBuffAndDropsTheCarriedFractions()
    {
        RegenBuffTrack track = Track(perClimax: 10f, hp: 0.9f, mp: 0f);
        track.OnQualifyingClimax();
        track.Advance(1d);

        track.Discard();

        Assert.False(track.IsActive);
        Assert.Equal(0d, track.Remaining, 3);

        track.OnQualifyingClimax();

        // The 0.9 carried before the discard would otherwise round the very next tick up.
        Assert.Equal(0, track.Advance(1d).Hp);
    }

    /// <summary>
    /// The shipped state grants nothing. A duration with nothing to restore, or restoration with no
    /// duration, is inert (FR-415).
    /// </summary>
    [Theory]
    [InlineData(0f, 5f, 5f)]
    [InlineData(10f, 0f, 0f)]
    public void InertTuningNeverGrantsTheBuff(float perClimax, float hp, float mp)
    {
        var track = new RegenBuffTrack(new RegenTuning(true, perClimax, 0f, hp, mp));

        track.OnQualifyingClimax();

        Assert.False(track.IsActive);
    }

    [Fact]
    public void DisabledTuningNeverGrantsTheBuff()
    {
        var track = new RegenBuffTrack(RegenTuning.Disabled);

        track.OnQualifyingClimax();

        Assert.False(track.IsActive);
    }
}
