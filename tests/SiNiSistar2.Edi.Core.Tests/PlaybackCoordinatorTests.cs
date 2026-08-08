using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class PlaybackCoordinatorTests
{
    [Fact]
    public void MappedEventUsesSeekAndDuplicateObservationDoesNotReplay()
    {
        EventMapping mapping = TestMappings.Event("one", "enemy", "clip");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(mapping), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        sink.Commands.Clear();
        EventObservation observation = Observe(mapping.Key, 1.25, 2);

        Assert.True(coordinator.ObserveEvent(observation));
        Assert.True(coordinator.ObserveEvent(observation));

        PlaybackCommand command = Assert.Single(sink.Commands);
        Assert.Equal(PlaybackCommandKind.Play, command.Kind);
        Assert.Equal("event-gallery", command.Gallery);
        Assert.Equal(500, command.SeekMilliseconds);
        Assert.Equal(TestMappings.Main, command.Output);
    }

    [Fact]
    public void StaleEndCannotReplaceNewerEvent()
    {
        EventMapping first = TestMappings.Event("one", "enemy", "clip-a", gallery: "gallery-a");
        EventMapping second = TestMappings.Event("two", "enemy", "clip-b", gallery: "gallery-b");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(first, second), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        sink.Commands.Clear();

        coordinator.ObserveEvent(Observe(first.Key));
        coordinator.ObserveEvent(Observe(second.Key));
        coordinator.EndEvent(first.Key);

        Assert.Equal(2, sink.Commands.Count);
        Assert.Equal("gallery-b", sink.Commands[^1].Gallery);
    }

    [Fact]
    public void StatusChangeWaitsForEventEndThenSelectsSwollenFiller()
    {
        EventMapping mapping = TestMappings.Event(
            "breast-event",
            "enemy",
            "clip",
            TestMappings.BreastLeft,
            "breast-event");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(mapping), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        sink.Commands.Clear();

        coordinator.ObserveEvent(Observe(mapping.Key));
        coordinator.UpdateStatuses(new[] { "Breast" });

        Assert.DoesNotContain(
            sink.Commands,
            x => x.Output == TestMappings.BreastLeft && x.Gallery == "filler-breast-swollen");

        coordinator.EndEvent(mapping.Key);

        Assert.Equal("filler-breast-swollen", sink.Commands[^1].Gallery);
        Assert.Equal(TestMappings.BreastLeft, sink.Commands[^1].Output);
    }

    [Fact]
    public void PauseAndResumeReconstructCurrentEventFromCurrentPosition()
    {
        EventMapping mapping = TestMappings.Event("one", "enemy", "clip");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(mapping), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        coordinator.ObserveEvent(Observe(mapping.Key, 0.1, 2));
        sink.Commands.Clear();

        coordinator.Pause();
        coordinator.Resume(Observe(mapping.Key, 0.75, 2));

        Assert.Contains(sink.Commands, x => x.Kind == PlaybackCommandKind.Pause && x.Output == TestMappings.Main);
        PlaybackCommand resumed = Assert.Single(
            sink.Commands,
            x => x.Kind == PlaybackCommandKind.Play && x.Gallery == "event-gallery");
        Assert.Equal(1500, resumed.SeekMilliseconds);
    }

    [Fact]
    public void UnknownEventIsDiagnosedAndNeverPlayed()
    {
        var sink = new RecordingSink();
        var diagnostics = new RecordingDiagnostics();
        string? warning = null;
        var coordinator = new PlaybackCoordinator(
            TestMappings.Create(),
            sink,
            diagnostics,
            value => warning = value);
        var unknown = new EventKey("hold", "new-enemy", "new-clip", "loop");

        Assert.False(coordinator.ObserveEvent(Observe(unknown)));

        Assert.Empty(sink.Commands);
        Assert.Equal(unknown, Assert.Single(diagnostics.Events).Key);
        Assert.Contains("new-enemy", warning, StringComparison.Ordinal);
    }

    /// <summary>AC-029: a preview outranks filler and restores the game state when it ends.</summary>
    [Fact]
    public void PreviewOutranksFillerAndRestoresGameStateOnEnd()
    {
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        sink.Commands.Clear();

        coordinator.BeginPreview("preview-gallery", new[] { TestMappings.Main });

        PlaybackCommand started = Assert.Single(sink.Commands);
        Assert.Equal("preview-gallery", started.Gallery);
        Assert.Equal(TestMappings.Main, started.Output);

        // Game-driven updates must not interrupt the audition.
        sink.Commands.Clear();
        coordinator.UpdateStatuses(new[] { "Breast" });
        Assert.Empty(sink.Commands);

        coordinator.EndPreview();

        Assert.Contains(sink.Commands, command =>
            command.Output == TestMappings.Main && command.Gallery == "filler-main");
        Assert.Contains(sink.Commands, command =>
            command.Output == TestMappings.BreastLeft && command.Gallery == "filler-breast-swollen");
    }

    /// <summary>AC-029: an event that started during a preview is resumed after it ends.</summary>
    [Fact]
    public void PreviewEndRestoresTheActiveEvent()
    {
        EventMapping mapping = TestMappings.Event("one", "enemy", "clip");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(mapping), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        coordinator.ObserveEvent(Observe(mapping.Key));

        coordinator.BeginPreview("preview-gallery", new[] { TestMappings.Main });
        sink.Commands.Clear();

        coordinator.EndPreview();

        Assert.Contains(sink.Commands, command =>
            command.Output == TestMappings.Main && command.Gallery == "event-gallery");
    }

    [Fact]
    public void LeavingGameplayStopsAnActivePreview()
    {
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        coordinator.BeginPreview("preview-gallery", new[] { TestMappings.Main });
        sink.Commands.Clear();

        coordinator.SetInactive();

        Assert.All(sink.Commands, command => Assert.Equal(PlaybackCommandKind.Stop, command.Kind));
        Assert.Equal(
            TestMappings.Roster().Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal),
            sink.Commands.Select(x => x.Output).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void PreviewRejectsUnknownChannels()
    {
        var coordinator = Create(TestMappings.Create(), new RecordingSink());

        Assert.Throws<ArgumentException>(() => coordinator.BeginPreview("g", new[] { "unknown" }));
        Assert.Throws<ArgumentException>(() => coordinator.BeginPreview(" ", new[] { TestMappings.Main }));
    }

    /// <summary>AC-033: a catalogued but unmapped stage is never played.</summary>
    [Fact]
    public void CataloguedButUnmappedStageIsNeverPlayed()
    {
        EventMapping mapped = TestMappings.Event("one", "enemy", "clip", stageId: "Stage_01");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(mapped), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        sink.Commands.Clear();

        Assert.False(coordinator.ObserveEvent(Observe(mapped.Key with { StageId = "Stage_02" })));
        Assert.Empty(sink.Commands);
    }

    /// <summary>
    /// AC-035: with only the piston connected, EDI offers `main` alone. The piston must still play
    /// and only `breast` may be held back — previously a missing `breast` disabled both channels
    /// and the connected device never moved (CHG-019).
    /// </summary>
    [Fact]
    public void OnlyBoundOutputsReceiveCommands()
    {
        EventMapping mapping = TestMappings.Event("one", "enemy", "clip");
        var sink = new RecordingSink();
        var gate = new OutputGate(sink, TestMappings.Roster().Select(x => x.Id).ToArray());
        var coordinator = new PlaybackCoordinator(
            TestMappings.Create(mapping), gate, new RecordingDiagnostics(), availability: gate);

        gate.Open(TestMappings.Main);
        coordinator.SetGameplayActive(Array.Empty<string>());
        coordinator.ObserveEvent(Observe(mapping.Key));

        Assert.Equal(
            new[] { TestMappings.BreastLeft, TestMappings.BreastRight }, gate.Suppressed());
        Assert.All(sink.Commands, command => Assert.Equal(TestMappings.Main, command.Output));
        Assert.Contains(sink.Commands, command => command.Gallery == "event-gallery");
    }

    /// <summary>A channel that connects later receives the state it should already be in.</summary>
    [Fact]
    public void OpeningAnOutputLaterSendsItsLatestDesiredStateOnly()
    {
        var sink = new RecordingSink();
        var gate = new OutputGate(sink, TestMappings.Roster().Select(x => x.Id).ToArray());

        gate.Publish(PlaybackCommand.Play("first", 0, TestMappings.BreastLeft));
        gate.Publish(PlaybackCommand.Play("second", 0, TestMappings.BreastLeft));
        Assert.Empty(sink.Commands);

        Assert.True(gate.Open(TestMappings.BreastLeft));
        Assert.False(gate.Open(TestMappings.BreastLeft));

        PlaybackCommand command = Assert.Single(sink.Commands);
        Assert.Equal("second", command.Gallery);
    }

    /// <summary>
    /// AC-036: auditioning on a channel EDI does not offer must say so rather than silently doing
    /// nothing, which is how the disabled-output state went unnoticed.
    /// </summary>
    [Fact]
    public void PreviewingASuppressedOutputIsRejectedWithAReason()
    {
        var sink = new RecordingSink();
        var gate = new OutputGate(sink, TestMappings.Roster().Select(x => x.Id).ToArray());
        var coordinator = new PlaybackCoordinator(
            TestMappings.Create(), gate, new RecordingDiagnostics(), availability: gate);
        gate.Open(TestMappings.Main);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => coordinator.BeginPreview("authored", new[] { TestMappings.Main, TestMappings.BreastLeft }));

        Assert.Contains(TestMappings.BreastLeft, error.Message, StringComparison.Ordinal);
        Assert.Empty(sink.Commands);

        // The available channel on its own is still auditionable.
        coordinator.BeginPreview("authored", new[] { TestMappings.Main });
        Assert.Equal(TestMappings.Main, Assert.Single(sink.Commands).Output);
    }

    private static PlaybackCoordinator Create(MappingRepository mappings, RecordingSink sink) =>
        new(mappings, sink, new RecordingDiagnostics());

    private static EventObservation Observe(EventKey key, double normalized = 0, double length = 1) =>
        new(key, normalized, length, true, "TestScene", DateTimeOffset.UtcNow);

    private sealed class RecordingSink : IPlaybackCommandSink
    {
        public List<PlaybackCommand> Commands { get; } = new();
        public void Publish(IReadOnlyList<PlaybackCommand> commands) => Commands.AddRange(commands);
        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDiagnostics : IEventDiagnostics
    {
        public List<EventObservation> Events { get; } = new();
        public bool RecordEvent(EventObservation observation)
        {
            Events.Add(observation);
            return true;
        }

        public void RegisterStatus(string statusId, string displayName) { }
        public Task WriteCoverageAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
