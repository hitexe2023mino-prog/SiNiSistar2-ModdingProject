using System.Text.Json;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class MotionScriptGeneratorTests
{
    [Fact]
    public void MainScriptUsesMeasuredPairDistanceAtMeasuredTimes()
    {
        AnimationFrameSnapshot[] frames = Enumerable.Range(0, 21)
            .Select(index => CreateFrame(
                index * 16,
                index / 20.0,
                (index <= 10 ? index : 20 - index) / 10.0,
                index / 20.0,
                (20 - index) / 20.0))
            .ToArray();

        var result = new MotionScriptGenerator().Generate(frames, MotionOutputKind.Main);

        Assert.True(result.Success, result.UnavailableReason);
        Assert.Equal("pair-distance-inverted", result.Source!.SignalType);
        Assert.Contains("ActorA/Pelvis", result.Source.TargetPath + result.Source.ReferencePath);
        Assert.Contains("ActorB/Pelvis", result.Source.TargetPath + result.Source.ReferencePath);
        Assert.Equal(frames.Select(frame => frame.RealtimeMilliseconds - frames[0].RealtimeMilliseconds),
            result.Script!.Actions.Select(action => action.At));
        Assert.Equal(100, result.Script.Actions[10].Pos);
        Assert.InRange(result.Script.Actions[0].Pos, 0, 5);
        Assert.InRange(result.Script.Actions[^1].Pos, 0, 5);
    }

    [Fact]
    public void BreastSidesUseIndependentMeasuredTransforms()
    {
        AnimationFrameSnapshot[] frames = Enumerable.Range(0, 21)
            .Select(index => CreateFrame(
                index * 20,
                index / 20.0,
                index / 20.0,
                index / 20.0,
                (20 - index) / 20.0))
            .ToArray();

        var generator = new MotionScriptGenerator();
        MotionGenerationResult left = generator.Generate(frames, MotionOutputKind.BreastLeft);
        MotionGenerationResult right = generator.Generate(frames, MotionOutputKind.BreastRight);

        Assert.True(left.Success, left.UnavailableReason);
        Assert.True(right.Success, right.UnavailableReason);
        Assert.Contains("Breast_L", left.Source!.TargetPath);
        Assert.Contains("Breast_R", right.Source!.TargetPath);
        Assert.NotEqual(left.Script!.Actions[0].Pos, right.Script!.Actions[0].Pos);
        Assert.NotEqual(left.Source.TargetPath, right.Source.TargetPath);
    }

    [Fact]
    public void StaticMotionIsRejectedWithoutSyntheticWaveform()
    {
        AnimationFrameSnapshot[] frames = Enumerable.Range(0, 12)
            .Select(index => CreateFrame(index * 20, index / 12.0, 0, 0, 0))
            .ToArray();

        MotionGenerationResult result = new MotionScriptGenerator().Generate(frames, MotionOutputKind.Main);

        Assert.False(result.Success);
        Assert.Contains("range", result.UnavailableReason!);
        Assert.Null(result.Script);
    }

    [Fact]
    public void MainMotionWithoutMeasuredContactPairIsRejected()
    {
        AnimationFrameSnapshot[] frames = Enumerable.Range(0, 21)
            .Select(index => CreateFrame(index * 20, index / 20.0, index / 20.0, 0, 0))
            .Select(frame => frame with
            {
                Animator = frame.Animator with
                {
                    Transforms = frame.Animator.Transforms
                        .Where(transform => !transform.Path.Contains("ActorA/Pelvis", StringComparison.Ordinal))
                        .ToArray(),
                },
            })
            .ToArray();

        MotionGenerationResult result = new MotionScriptGenerator().Generate(frames, MotionOutputKind.Main);

        Assert.False(result.Success);
        Assert.Equal("no-measured-anatomical-contact-pair", result.UnavailableReason);
        Assert.Null(result.Script);
    }

    [Fact]
    public void CompleteLoopIsBoundedByTwoObservedWraps()
    {
        double[] times = { 0.7, 0.9, 1.05, 1.3, 1.8, 2.05, 2.4 };
        AnimationFrameSnapshot[] frames = times.Select((time, index) =>
            CreateFrame(index * 20, time, index / 10.0, 0, 0)).ToArray();

        bool found = MotionScriptGenerator.TryExtractFirstCompleteLoop(frames, out var loop);

        Assert.True(found);
        Assert.Equal(2, loop[0].FrameCount);
        Assert.Equal(5, loop[^1].FrameCount);
    }

    [Fact]
    public async Task SessionWriterCreatesAppendOnlyJsonLinesWithMonotonicSequence()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sinisistar2-capture-{Guid.NewGuid():N}");
        try
        {
            await using var writer = new AnimationSessionWriter(
                directory,
                "build-id",
                "1.0.0",
                new[] { new CaptureCapability("animator", true, null) },
                capacity: 32);
            Assert.True(writer.Enqueue("animation-sample", new { value = 1 }, 10, 1));
            Assert.True(writer.Enqueue("text-change", new { text = "sample" }, 20, 2));
            await writer.ShutdownAsync();

            string[] lines = await File.ReadAllLinesAsync(writer.OutputPath);
            using JsonDocument first = JsonDocument.Parse(lines[0]);
            using JsonDocument last = JsonDocument.Parse(lines[^1]);
            Assert.Equal("session-start", first.RootElement.GetProperty("recordType").GetString());
            Assert.Equal("session-end", last.RootElement.GetProperty("recordType").GetString());
            long[] sequences = lines.Select(line =>
            {
                using JsonDocument document = JsonDocument.Parse(line);
                return document.RootElement.GetProperty("sequence").GetInt64();
            }).ToArray();
            Assert.Equal(sequences.OrderBy(value => value), sequences);
            Assert.Equal(sequences.Length, sequences.Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static AnimationFrameSnapshot CreateFrame(
        long realtimeMs,
        double normalizedTime,
        double stroke,
        double leftBreast,
        double rightBreast)
    {
        TransformSnapshot[] transforms =
        {
            Transform("Root", 0, 0, 0),
            Transform("Root/ActorA/Pelvis", 0, 0, 0),
            Transform("Root/ActorB/Pelvis", 1 - (stroke * 0.5), 0, 0),
            Transform("Root/Body/Breast_L", leftBreast, 0, 0),
            Transform("Root/Body/Breast_R", 0, rightBreast, 0),
        };
        var animator = new AnimatorRuntimeSnapshot(
            "animator",
            "Root",
            true,
            1,
            "Normal",
            false,
            false,
            true,
            default,
            new QuaternionSnapshot(0, 0, 0, 1),
            default,
            new QuaternionSnapshot(0, 0, 0, 1),
            default,
            new QuaternionSnapshot(0, 0, 0, 1),
            default,
            default,
            default,
            Array.Empty<AnimatorLayerSnapshot>(),
            Array.Empty<AnimatorParameterSnapshot>(),
            transforms);
        return new AnimationFrameSnapshot(
            "event-1",
            new EventKey("gallery", "actor", "clip", "loop"),
            "scene",
            realtimeMs,
            (int)(realtimeMs / 20),
            realtimeMs / 1000.0,
            realtimeMs / 1000.0,
            0.02,
            0.02,
            1,
            normalizedTime,
            Array.Empty<string>(),
            animator);
    }

    private static TransformSnapshot Transform(string path, double x, double y, double z) => new(
        path,
        path.Split('/')[^1],
        true,
        new Vector3Snapshot(x, y, z),
        new QuaternionSnapshot(0, 0, 0, 1),
        new Vector3Snapshot(1, 1, 1),
        new Vector3Snapshot(x, y, z),
        new QuaternionSnapshot(0, 0, 0, 1),
        new Vector3Snapshot(1, 1, 1),
        null);
}
