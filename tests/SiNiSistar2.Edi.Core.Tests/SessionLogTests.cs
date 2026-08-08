using System.Text.Json;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class SessionLogTests
{
    private static readonly IReadOnlyList<CaptureCapability> Capabilities = new[]
    {
        new CaptureCapability("trigger-transitions", true, null),
        new CaptureCapability("hold-stage-state-machine", false, "No general hold state machine in this build."),
    };

    /// <summary>
    /// AC-034: the session log carries transitions and text only, in monotonic order, and never a
    /// per-frame record.
    /// </summary>
    [Fact]
    public async Task SessionLogHoldsTransitionsWithoutPerFrameRecords()
    {
        using var temp = new TempDirectory();
        var writer = new AnimationSessionWriter(temp.Root, "build", "1.0.0", Capabilities);
        var key = new EventKey("gallery", "Enemy", "Take", "loop", "Take_01");

        writer.Enqueue(
            "event-transition",
            new TriggerTransitionSnapshot(
                "instance", null, key, "GalleryScene", 0.733, true, 0.25, 10, 10, 1, new[] { "Breast" }),
            100,
            6);
        writer.Enqueue(
            "catalog-update",
            new { source = TriggerSources.StaticEnumeration, actorId = "Enemy" },
            110,
            7);
        writer.Enqueue(
            "text-change",
            new TextChangeSnapshot("UnityEngine.UI.Text", "Root[0]/Name[1]", "模造の落とし子", true),
            120,
            8);
        await writer.ShutdownAsync();

        string[] lines = await File.ReadAllLinesAsync(writer.OutputPath);
        var records = lines.Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        string[] types = records.Select(r => r.GetProperty("recordType").GetString()!).ToArray();

        Assert.DoesNotContain("animation-sample", types);
        Assert.Equal("session-start", types[0]);
        Assert.Equal("session-end", types[^1]);
        Assert.Contains("event-transition", types);
        Assert.Contains("catalog-update", types);
        Assert.Contains("text-change", types);

        long previousSequence = 0;
        long previousRealtime = -1;
        foreach (JsonElement record in records)
        {
            long sequence = record.GetProperty("sequence").GetInt64();
            Assert.True(sequence > previousSequence, "sequence must increase monotonically");
            previousSequence = sequence;

            if (record.GetProperty("recordType").GetString() is "session-start" or "session-end")
            {
                continue;
            }

            long realtime = record.GetProperty("realtimeMs").GetInt64();
            Assert.True(realtime >= previousRealtime, "realtimeMs must not go backwards");
            previousRealtime = realtime;
        }

        JsonElement start = records[0].GetProperty("payload");
        JsonElement unavailable = start.GetProperty("capabilities")
            .EnumerateArray()
            .Single(item => !item.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(unavailable.GetProperty("unavailableReason").GetString()));
    }

    [Fact]
    public async Task TransitionRecordsBothSidesOfAStageChange()
    {
        using var temp = new TempDirectory();
        var writer = new AnimationSessionWriter(temp.Root, "build", "1.0.0", Capabilities);
        var first = new EventKey("gallery", "Enemy", "Take", "loop", "Take_01");
        var second = first with { StageId = "Take_02" };

        writer.Enqueue(
            "event-transition",
            new TriggerTransitionSnapshot(
                "instance", first, second, "GalleryScene", 1.0, true, 0, 1, 1, 1, Array.Empty<string>()),
            200,
            12);
        await writer.ShutdownAsync();

        JsonElement payload = (await File.ReadAllLinesAsync(writer.OutputPath))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Single(record => record.GetProperty("recordType").GetString() == "event-transition")
            .GetProperty("payload");

        Assert.Equal("Take_01", payload.GetProperty("previous").GetProperty("stageId").GetString());
        Assert.Equal("Take_02", payload.GetProperty("current").GetProperty("stageId").GetString());
    }
}
