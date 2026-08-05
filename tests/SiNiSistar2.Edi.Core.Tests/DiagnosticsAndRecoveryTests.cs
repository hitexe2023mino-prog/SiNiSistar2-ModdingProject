using System.Collections.Concurrent;
using System.Text.Json;
using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class DiagnosticsAndRecoveryTests
{
    [Fact]
    public async Task CoverageReportMachineCountsUnclassifiedEntries()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sinisistar2-edi-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "coverage.json");
        string candidatesPath = Path.Combine(directory, "mapping-candidates.json");
        try
        {
            var recorder = new DiagnosticRecorder(TestMappings.Create(), path);
            recorder.RegisterStatus("Breast", "膨乳");
            recorder.RegisterStatus("FutureStatus", "FutureStatus");
            recorder.RecordEvent(new EventObservation(
                new EventKey("hold", "unknown", "clip", "loop"),
                0,
                1,
                true,
                "Scene",
                DateTimeOffset.UtcNow));

            await recorder.WriteCoverageAsync();

            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(2, document.RootElement.GetProperty("unclassifiedCount").GetInt32());
            Assert.Equal(
                "mapped",
                document.RootElement.GetProperty("statuses")[0].GetProperty("classification").GetString());

            using JsonDocument candidates = JsonDocument.Parse(await File.ReadAllTextAsync(candidatesPath));
            JsonElement candidate = Assert.Single(candidates.RootElement.GetProperty("events").EnumerateArray());
            Assert.Equal("hold", candidate.GetProperty("context").GetString());
            Assert.Equal("unknown", candidate.GetProperty("actorId").GetString());
            Assert.Equal("clip", candidate.GetProperty("animationId").GetString());
            Assert.Equal("unclassified", candidate.GetProperty("classification").GetString());
            Assert.Equal(TestMappings.Hash, candidates.RootElement
                .GetProperty("targetGameBuild")
                .GetProperty("gameAssemblySha256")
                .GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task CapturedCandidatesAccumulateAcrossGameSessionsForSameBuild()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sinisistar2-edi-{Guid.NewGuid():N}");
        string coveragePath = Path.Combine(directory, "coverage.json");
        string candidatesPath = Path.Combine(directory, "mapping-candidates.json");
        try
        {
            var firstSession = new DiagnosticRecorder(TestMappings.Create(), coveragePath, candidatesPath);
            firstSession.RecordEvent(Observation("enemy-a", "clip-a"));
            await firstSession.WriteCoverageAsync();

            var secondSession = new DiagnosticRecorder(TestMappings.Create(), coveragePath, candidatesPath);
            secondSession.RecordEvent(Observation("enemy-b", "clip-b"));
            await secondSession.WriteCoverageAsync();

            using JsonDocument candidates = JsonDocument.Parse(await File.ReadAllTextAsync(candidatesPath));
            JsonElement[] events = candidates.RootElement.GetProperty("events").EnumerateArray().ToArray();
            Assert.Equal(2, events.Length);
            Assert.Contains(events, x => x.GetProperty("actorId").GetString() == "enemy-a");
            Assert.Contains(events, x => x.GetProperty("actorId").GetString() == "enemy-b");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task RecoverySendsLatestDesiredStateInsteadOfHistory()
    {
        var client = new RecoveringClient();
        await using var sink = new AsyncEdiCommandSink(client);
        sink.Publish(PlaybackCommand.Play("old", 0, EdiChannels.Main));
        await client.FirstFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sink.Publish(PlaybackCommand.Play("latest", 0, EdiChannels.Main));
        client.Available = true;

        await WaitUntilAsync(
            () => client.Successes.Any(x => x.Kind == PlaybackCommandKind.Play),
            TimeSpan.FromSeconds(4));

        PlaybackCommand played = Assert.Single(client.Successes, x => x.Kind == PlaybackCommandKind.Play);
        Assert.Equal("latest", played.Gallery);
    }

    [Fact]
    public async Task ShutdownAttemptsStopForEveryKnownChannel()
    {
        var client = new RecoveringClient { Available = true };
        await using var sink = new AsyncEdiCommandSink(client);

        await sink.ShutdownAsync();

        PlaybackCommand[] stops = client.Successes
            .Where(x => x.Kind == PlaybackCommandKind.Stop)
            .OrderBy(x => x.Channel, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, stops.Length);
        Assert.Equal(new[] { EdiChannels.Breast, EdiChannels.Main }, stops.Select(x => x.Channel));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset end = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < end)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "Condition was not satisfied before timeout.");
    }

    private static EventObservation Observation(string actor, string animation) => new(
        new EventKey("hold", actor, animation, "loop"),
        0,
        1,
        true,
        "Scene",
        DateTimeOffset.UtcNow);

    private sealed class RecoveringClient : IEdiClient
    {
        public volatile bool Available;
        public TaskCompletionSource FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<PlaybackCommand> Successes { get; } = new();

        public Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(new[] { EdiChannels.Main, EdiChannels.Breast });

        public Task ExecuteAsync(PlaybackCommand command, CancellationToken cancellationToken)
        {
            if (!Available)
            {
                FirstFailure.TrySetResult();
                throw new HttpRequestException("offline");
            }

            Successes.Enqueue(command);
            return Task.CompletedTask;
        }
    }
}
