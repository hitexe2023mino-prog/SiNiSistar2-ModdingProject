namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The gauge decides when a climax happens, and a climax is what eventually ends the run. Counting
/// one fill twice would halve the effective limit (SPEC003 5.2, 5.4).
/// </summary>
public sealed class PleasureMeterTests
{
    private static PleasureMeter Meter(float gain = 0.25f, float scale = 1f, float decay = 0.1f) =>
        new(gain, scale, decay);

    [Fact]
    public void AShippedZeroGainGaugeNeverRises()
    {
        var meter = new PleasureMeter(gainPerHit: 0f, corruptionScale: 1f, decayPerSecond: 0.1f);

        Assert.False(meter.AddSexualHit(5f));
        Assert.Equal(0f, meter.Value, 5);
    }

    [Fact]
    public void HitsAccumulateUntilTheGaugeFills()
    {
        PleasureMeter meter = Meter(gain: 0.25f, scale: 0f);

        Assert.False(meter.AddSexualHit(0f));
        Assert.False(meter.AddSexualHit(0f));
        Assert.False(meter.AddSexualHit(0f));
        Assert.True(meter.AddSexualHit(0f));
        Assert.Equal(1f, meter.Value, 5);
    }

    /// <summary>AC-207: higher corruption means the same attack gives more.</summary>
    [Fact]
    public void CorruptionIncreasesTheGainPerHit()
    {
        PleasureMeter dull = Meter(gain: 0.1f, scale: 1f);
        PleasureMeter keen = Meter(gain: 0.1f, scale: 1f);

        dull.AddSexualHit(0f);
        keen.AddSexualHit(3f);

        Assert.Equal(0.1f, dull.Value, 5);
        Assert.Equal(0.4f, keen.Value, 5);
    }

    /// <summary>AC-209: one fill yields one climax, however many times it is polled.</summary>
    [Fact]
    public void AFullGaugeYieldsExactlyOneClimax()
    {
        PleasureMeter meter = Meter(gain: 1f, scale: 0f);

        Assert.True(meter.AddSexualHit(0f));
        Assert.False(meter.AddSexualHit(0f));
        Assert.False(meter.AddSexualHit(0f));

        meter.ConsumeClimax();
        Assert.Equal(0f, meter.Value, 5);
        Assert.True(meter.AddSexualHit(0f));
    }

    /// <summary>AC-208: the gauge decays when free and stops at zero.</summary>
    [Fact]
    public void DecayLowersTheGaugeAndStopsAtZero()
    {
        PleasureMeter meter = Meter(gain: 0.5f, scale: 0f, decay: 0.1f);
        meter.AddSexualHit(0f);

        meter.Decay(2d);
        Assert.Equal(0.3f, meter.Value, 5);

        meter.Decay(100d);
        Assert.Equal(0f, meter.Value, 5);
    }

    [Fact]
    public void AZeroDecayGaugeHoldsItsValue()
    {
        PleasureMeter meter = Meter(gain: 0.5f, scale: 0f, decay: 0f);
        meter.AddSexualHit(0f);

        meter.Decay(100d);

        Assert.Equal(0.5f, meter.Value, 5);
    }

    [Fact]
    public void TheGaugeNeverExceedsOne()
    {
        PleasureMeter meter = Meter(gain: 5f, scale: 0f);

        meter.AddSexualHit(0f);

        Assert.Equal(1f, meter.Value, 5);
    }
}
