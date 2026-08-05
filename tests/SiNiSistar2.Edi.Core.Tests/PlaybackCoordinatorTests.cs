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
        Assert.Equal(EdiChannels.Main, command.Channel);
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
            EdiChannels.Breast,
            "breast-event");
        var sink = new RecordingSink();
        var coordinator = Create(TestMappings.Create(mapping), sink);
        coordinator.SetGameplayActive(Array.Empty<string>());
        sink.Commands.Clear();

        coordinator.ObserveEvent(Observe(mapping.Key));
        coordinator.UpdateStatuses(new[] { "Breast" });

        Assert.DoesNotContain(
            sink.Commands,
            x => x.Channel == EdiChannels.Breast && x.Gallery == "filler-breast-swollen");

        coordinator.EndEvent(mapping.Key);

        Assert.Equal("filler-breast-swollen", sink.Commands[^1].Gallery);
        Assert.Equal(EdiChannels.Breast, sink.Commands[^1].Channel);
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

        Assert.Contains(sink.Commands, x => x.Kind == PlaybackCommandKind.Pause && x.Channel == EdiChannels.Main);
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

    private static PlaybackCoordinator Create(MappingRepository mappings, RecordingSink sink) =>
        new(mappings, sink, new RecordingDiagnostics());

    private static EventObservation Observe(EventKey key, double normalized = 0, double length = 1) =>
        new(key, normalized, length, true, "TestScene", DateTimeOffset.UtcNow);

    private sealed class RecordingSink : IPlaybackCommandSink
    {
        public List<PlaybackCommand> Commands { get; } = new();
        public void Publish(PlaybackCommand command) => Commands.Add(command);
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
