namespace SiNiSistar2.Edi.Core.Tests;

/// <summary>
/// The point of the per-output redesign: what one device plays can never be decided by what
/// another device is playing (SPEC001 1.2, FR-006).
/// </summary>
public sealed class OutputIndependenceTests
{
    /// <summary>
    /// AC-037. The failure this replaces was real: the piston went still because it was fed the
    /// breast filler's flat padding asset while a parasite status was active.
    /// </summary>
    [Fact]
    public void ParasiteAndSwollenBreastApplyTogetherWithoutOverwritingEachOther()
    {
        MappingRepository mappings = TestMappings.Create(
            null,
            TestMappings.DefaultStatusRules().Concat(new[]
            {
                TestMappings.Status("Parasite", "filler-main-parasite", 10, TestMappings.Main),
            }),
            null);
        var sink = new RecordingSink();
        var coordinator = new PlaybackCoordinator(mappings, sink, new NullDiagnostics());

        coordinator.SetGameplayActive(new[] { "Parasite", "Breast" });

        Assert.Equal("filler-main-parasite", Gallery(sink, TestMappings.Main));
        Assert.Equal("filler-breast-swollen", Gallery(sink, TestMappings.BreastLeft));
        Assert.Equal("filler-breast-swollen", Gallery(sink, TestMappings.BreastRight));
    }

    /// <summary>
    /// AC-042: a trigger can silence one device while another plays, and an output the trigger
    /// does not name is left exactly as it was.
    /// </summary>
    [Fact]
    public void ATriggerCanSilenceOneOutputWhileAnotherPlaysAndLeaveTheRestAlone()
    {
        EventMapping mapping = TestMappings.Event(
            "one",
            "enemy",
            "clip",
            new[]
            {
                new OutputAssignment { Id = TestMappings.BreastLeft, Gallery = null },
                new OutputAssignment { Id = TestMappings.BreastRight, Gallery = "event-gallery" },
            });
        MappingRepository mappings = TestMappings.Create(mapping);
        var sink = new RecordingSink();
        var coordinator = new PlaybackCoordinator(mappings, sink, new NullDiagnostics());

        coordinator.SetGameplayActive(Array.Empty<string>());
        Assert.Equal("filler-main", Gallery(sink, TestMappings.Main));
        sink.Commands.Clear();

        coordinator.ObserveEvent(Observe(mapping.Key));

        PlaybackCommand left = Assert.Single(sink.Commands, x => x.Output == TestMappings.BreastLeft);
        Assert.Equal(PlaybackCommandKind.Stop, left.Kind);
        Assert.Equal("event-gallery", Gallery(sink, TestMappings.BreastRight));

        // main was not named by the trigger, so it keeps its filler and is not republished.
        Assert.DoesNotContain(sink.Commands, x => x.Output == TestMappings.Main);
    }

    /// <summary>
    /// A trigger that ends restores only the outputs it held. The rest never moved, so they must
    /// not be re-sent either (FR-012).
    /// </summary>
    [Fact]
    public void EndingATriggerOnlyRestoresTheOutputsItHeld()
    {
        EventMapping mapping = TestMappings.Event("one", "enemy", "clip", output: TestMappings.Main);
        MappingRepository mappings = TestMappings.Create(mapping);
        var sink = new RecordingSink();
        var coordinator = new PlaybackCoordinator(mappings, sink, new NullDiagnostics());

        coordinator.SetGameplayActive(Array.Empty<string>());
        coordinator.ObserveEvent(Observe(mapping.Key));
        sink.Commands.Clear();

        coordinator.EndEvent(mapping.Key);

        PlaybackCommand restored = Assert.Single(sink.Commands);
        Assert.Equal(TestMappings.Main, restored.Output);
        Assert.Equal("filler-main", restored.Gallery);
    }

    /// <summary>
    /// A null filler means the device is meant to be still, and that is sent as a stop rather than
    /// carried by a flat waveform (FR-047, DEC-024).
    /// </summary>
    [Fact]
    public void AnOutputWithNoDefaultFillerIsStoppedRatherThanFedAStillWaveform()
    {
        MappingRepository mappings = TestMappings.Create(
            null,
            null,
            new Dictionary<string, string?>
            {
                [TestMappings.Main] = "filler-main",
                [TestMappings.BreastLeft] = null,
                [TestMappings.BreastRight] = null,
            });
        var sink = new RecordingSink();
        var coordinator = new PlaybackCoordinator(mappings, sink, new NullDiagnostics());

        coordinator.SetGameplayActive(Array.Empty<string>());

        Assert.Equal("filler-main", Gallery(sink, TestMappings.Main));
        Assert.Equal(
            PlaybackCommandKind.Stop,
            Assert.Single(sink.Commands, x => x.Output == TestMappings.BreastLeft).Kind);
    }

    /// <summary>
    /// AC-052: an output that loses its binding while playing is stopped, not merely abandoned.
    /// Holding commands alone would leave the device looping the last gallery forever (FR-055).
    /// </summary>
    [Fact]
    public void SuppressingAnOpenOutputStopsTheDeviceFirst()
    {
        var inner = new RecordingSink();
        var gate = new OutputGate(inner, TestMappings.Roster().Select(x => x.Id).ToArray());
        gate.Open(TestMappings.Main);
        gate.Publish(new[] { PlaybackCommand.Play("filler-main", 0, TestMappings.Main) });
        inner.Commands.Clear();

        Assert.True(gate.Suppress(TestMappings.Main));

        PlaybackCommand stop = Assert.Single(inner.Commands);
        Assert.Equal(PlaybackCommandKind.Stop, stop.Kind);
        Assert.Equal(TestMappings.Main, stop.Output);

        // Suppressing an output that was never open has nothing to stop.
        Assert.False(gate.Suppress(TestMappings.BreastLeft));
        Assert.Single(inner.Commands);
    }

    private static string? Gallery(RecordingSink sink, string output) =>
        sink.Commands.Last(command => command.Output == output).Gallery;

    private static EventObservation Observe(EventKey key) =>
        new(key, 0, 1, true, "TestScene", DateTimeOffset.UtcNow);

    private sealed class RecordingSink : IPlaybackCommandSink
    {
        public List<PlaybackCommand> Commands { get; } = new();
        public void Publish(IReadOnlyList<PlaybackCommand> commands) => Commands.AddRange(commands);
        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullDiagnostics : IEventDiagnostics
    {
        public bool RecordEvent(EventObservation observation) => true;
        public void RegisterStatus(string statusId, string displayName) { }
        public Task WriteCoverageAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
