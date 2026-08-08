namespace SiNiSistar2.Edi.Core.Tests;

/// <summary>
/// Startup has to prove two things before any device moves: that EDI behaves the way this MOD
/// depends on, and that each output is backed by the device the roster names (SPEC001 7.1, 7.4.3).
/// </summary>
public sealed class BindingAndCapabilityTests
{
    private static readonly IReadOnlyList<OutputBinding> Roster = TestMappings.Roster();

    private static EdiDevice Device(
        string name,
        string? channel,
        string? variant,
        bool isReady = true) => new(name, channel, variant, isReady);

    private static IReadOnlyList<EdiDevice> AllBound() => new[]
    {
        Device("Vorze Piston", "main", "a10-main"),
        Device("Vorze UFO TW Rotate: 1", "breast-left", "ufo-left"),
        Device("Vorze UFO TW Rotate: 2", "breast-right", "ufo-right"),
    };

    [Fact]
    public void AFullyWiredSetupBindsEveryOutput()
    {
        IReadOnlyList<OutputBindingResult> results = BindingVerifier.Verify(Roster, AllBound());

        Assert.All(results, result => Assert.True(result.IsBound, string.Join("; ", result.Failures)));
    }

    /// <summary>
    /// AC-041: the piston sitting on a breast channel is the configuration that let a breast
    /// gallery reach it. Both outputs must fail: the one whose device is elsewhere, and the one
    /// whose channel now holds a device it does not name.
    /// </summary>
    [Fact]
    public void APistonOnTheWrongChannelSuppressesBothOutputsAndNamesTheMismatch()
    {
        var devices = new[]
        {
            Device("Vorze Piston", "breast-left", "a10-main"),
            Device("Vorze UFO TW Rotate: 1", "breast-left", "ufo-left"),
            Device("Vorze UFO TW Rotate: 2", "breast-right", "ufo-right"),
        };

        IReadOnlyList<OutputBindingResult> results = BindingVerifier.Verify(Roster, devices);

        OutputBindingResult main = results.Single(x => x.Output == "main");
        Assert.False(main.IsBound);
        Assert.Contains(main.Failures, f => f.Contains("breast-left", StringComparison.Ordinal));

        // The right side is untouched, so a partial setup keeps working (FR-042).
        Assert.True(results.Single(x => x.Output == "breast-right").IsBound);
    }

    /// <summary>
    /// A device the roster does not name, sharing an output's channel, would receive that
    /// output's waveform. EDI parks unknown devices on the first configured channel unless it is
    /// told otherwise, so this is not hypothetical (SPEC001 7.1 condition 5).
    /// </summary>
    [Fact]
    public void AnUnknownDeviceOnAnOutputChannelSuppressesThatOutput()
    {
        IReadOnlyList<EdiDevice> devices = AllBound()
            .Append(Device("Some Other Toy", "main", "default"))
            .ToArray();

        IReadOnlyList<OutputBindingResult> results = BindingVerifier.Verify(Roster, devices);

        OutputBindingResult main = results.Single(x => x.Output == "main");
        Assert.False(main.IsBound);
        Assert.Contains(main.Failures, f => f.Contains("Some Other Toy", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-053: with a holding channel configured, an unrelated device never lands on an output's
    /// channel, so every output stays bound.
    /// </summary>
    [Fact]
    public void AnUnknownDeviceOnTheHoldingChannelLeavesEveryOutputBound()
    {
        IReadOnlyList<EdiDevice> devices = AllBound()
            .Append(Device("Some Other Toy", "unassigned", "default"))
            .ToArray();

        Assert.All(
            BindingVerifier.Verify(Roster, devices),
            result => Assert.True(result.IsBound, string.Join("; ", result.Failures)));
    }

    [Fact]
    public void AWrongVariantOrAnUnreadyDeviceIsReportedWithBothValues()
    {
        var devices = new[]
        {
            Device("Vorze Piston", "main", "ufo-left"),
            Device("Vorze UFO TW Rotate: 1", "breast-left", "ufo-left", isReady: false),
        };

        IReadOnlyList<OutputBindingResult> results = BindingVerifier.Verify(Roster, devices);

        Assert.Contains(
            results.Single(x => x.Output == "main").Failures,
            f => f.Contains("ufo-left", StringComparison.Ordinal)
                 && f.Contains("a10-main", StringComparison.Ordinal));
        Assert.Contains(
            results.Single(x => x.Output == "breast-left").Failures,
            f => f.Contains("IsReady=false", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-050: EDI renames a device that is re-added before the old one is released. The symptom
    /// is "device missing", which sends the user looking in the wrong place (FR-054).
    /// </summary>
    [Fact]
    public void AUniquifiedDeviceNameIsCalledOutInsteadOfReadingAsMissing()
    {
        var devices = new[]
        {
            Device("Vorze Piston (1)", "main", "a10-main"),
        };

        OutputBindingResult main = BindingVerifier.Verify(Roster, devices).Single(x => x.Output == "main");

        Assert.False(main.IsBound);
        Assert.Contains(main.Failures, f => f.Contains("Vorze Piston (1)", StringComparison.Ordinal));
        Assert.Contains(main.Failures, f => f.Contains("re-added", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAbsentDeviceListsWhatEdiDidReport()
    {
        OutputBindingResult main = BindingVerifier
            .Verify(Roster, Array.Empty<EdiDevice>())
            .Single(x => x.Output == "main");

        Assert.False(main.IsBound);
        Assert.Contains(main.Failures, f => f.Contains("no devices at all", StringComparison.Ordinal));
    }

    /// <summary>AC-049: a missing endpoint means 7.4 was never applied, so nothing may play.</summary>
    [Fact]
    public void MissingCapabilitiesBlockPlaybackEntirely()
    {
        CapabilityCheck check = EdiCapabilityCheck.Evaluate(null);

        Assert.False(check.AllowsPlayback);
        Assert.False(check.AllowsReload);
        Assert.Contains(check.Blocking, x => x.Contains("GET /Edi/Info", StringComparison.Ordinal));
    }

    /// <summary>AC-048: each missing capability names the requirement it breaks.</summary>
    [Theory]
    [InlineData(false, true, "FR-050")]
    [InlineData(true, false, "FR-045")]
    public void ADisabledCapabilityBlocksPlaybackAndNamesTheRequirement(
        bool strictVariants,
        bool stopClearsFiller,
        string requirement)
    {
        CapabilityCheck check = EdiCapabilityCheck.Evaluate(
            new EdiCapabilities("1.0.2", strictVariants, stopClearsFiller, "unassigned"));

        Assert.False(check.AllowsPlayback);
        Assert.Contains(check.Blocking, x => x.Contains(requirement, StringComparison.Ordinal));
    }

    /// <summary>
    /// A missing holding channel is a warning, not a gate: binding verification still stops the
    /// damage, it just costs the user a suppressed output (SPEC001 7.4.3).
    /// </summary>
    [Fact]
    public void AMissingHoldingChannelWarnsButStillAllowsPlayback()
    {
        CapabilityCheck check = EdiCapabilityCheck.Evaluate(
            new EdiCapabilities("1.0.2", true, true, null));

        Assert.True(check.AllowsPlayback);
        Assert.Contains(check.Warnings, x => x.Contains("UnassignedDeviceChannel", StringComparison.Ordinal));
    }

    [Fact]
    public void AFullySupportedEdiPassesWithNothingToReport()
    {
        CapabilityCheck check = EdiCapabilityCheck.Evaluate(
            new EdiCapabilities("1.0.2", true, true, "unassigned"));

        Assert.True(check.AllowsPlayback);
        Assert.True(check.IsFullySupported);
    }
}
