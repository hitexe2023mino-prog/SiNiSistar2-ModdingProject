using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class EventCaptureTrackerTests
{
    [Fact]
    public void CapturesStartPrimaryAndEndForEveryRuntimeTransition()
    {
        var diagnostics = new UniqueDiagnostics();
        var tracker = new EventCaptureTracker(diagnostics);
        EventObservation first = Observation("enemy-a", "clip-a");
        EventObservation second = Observation("enemy-b", "clip-b");

        Assert.True(tracker.Observe(first));
        Assert.False(tracker.Observe(first));
        Assert.True(tracker.Observe(second));
        Assert.True(tracker.Observe(null));

        Assert.Equal(6, diagnostics.Events.Count);
        Assert.Contains(new EventKey("hold", "enemy-a", "clip-a", "start"), diagnostics.Events);
        Assert.Contains(new EventKey("hold", "enemy-a", "clip-a", "loop"), diagnostics.Events);
        Assert.Contains(new EventKey("hold", "enemy-a", "clip-a", "end"), diagnostics.Events);
        Assert.Contains(new EventKey("hold", "enemy-b", "clip-b", "start"), diagnostics.Events);
        Assert.Contains(new EventKey("hold", "enemy-b", "clip-b", "loop"), diagnostics.Events);
        Assert.Contains(new EventKey("hold", "enemy-b", "clip-b", "end"), diagnostics.Events);
    }

    private static EventObservation Observation(string actor, string animation) => new(
        new EventKey("hold", actor, animation, "loop"),
        0.25,
        2,
        true,
        "TestScene",
        DateTimeOffset.UtcNow);

    private sealed class UniqueDiagnostics : IEventDiagnostics
    {
        public HashSet<EventKey> Events { get; } = new();
        public bool RecordEvent(EventObservation observation) => Events.Add(observation.Key);
        public void RegisterStatus(string statusId, string displayName) { }
        public Task WriteCoverageAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
