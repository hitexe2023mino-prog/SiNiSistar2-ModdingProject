using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

public enum MotionOutputKind
{
    Main,
    BreastLeft,
    BreastRight,
}

public sealed record FunscriptAction(int Pos, long At);

public sealed record FunscriptDocument(
    string Version,
    bool Inverted,
    int Range,
    IReadOnlyList<FunscriptAction> Actions);

public sealed record MotionSourceSelection(
    string TargetPath,
    string ReferencePath,
    string SignalType,
    Vector3Snapshot ProjectionAxis,
    double Range,
    double AxisConcentration,
    double SemanticScore,
    double QualityScore);

public sealed record MotionGenerationResult(
    bool Success,
    string? UnavailableReason,
    FunscriptDocument? Script,
    MotionSourceSelection? Source,
    long DurationMilliseconds,
    int SourceSampleCount);

public sealed class MotionScriptGenerator
{
    private static readonly string[] MainTerms =
    {
        "pelvis", "hip", "waist", "groin", "genital", "vagina", "pussy", "anus", "ass",
        "penis", "cock", "dick", "kosi", "koshi",
    };

    private static readonly string[] BreastTerms = { "breast", "bust", "boob", "chest", "mune", "tit" };
    private static readonly string[] LeftTerms = { "left", "_l", ".l", " l ", "-l" };
    private static readonly string[] RightTerms = { "right", "_r", ".r", " r ", "-r" };

    public MotionGenerationResult Generate(
        IReadOnlyList<AnimationFrameSnapshot> frames,
        MotionOutputKind outputKind,
        bool requireLoopBoundary = false)
    {
        if (frames.Count < 8)
        {
            return Unavailable("fewer-than-8-frames", frames.Count);
        }

        AnimationFrameSnapshot[] ordered = frames.OrderBy(frame => frame.RealtimeMilliseconds).ToArray();
        long start = ordered[0].RealtimeMilliseconds;
        long duration = ordered[^1].RealtimeMilliseconds - start;
        if (duration < 100)
        {
            return Unavailable("duration-below-100ms", ordered.Length);
        }

        if (!HasContinuousTimeline(ordered))
        {
            return Unavailable("sample-gap-exceeds-four-frame-intervals", ordered.Length);
        }

        Candidate? candidate = SelectCandidate(ordered, outputKind);
        if (candidate is null)
        {
            return Unavailable("no-independent-semantic-motion-signal", ordered.Length);
        }

        if (candidate.Range < 0.0001)
        {
            return Unavailable("motion-range-too-small", ordered.Length);
        }

        if (candidate.AxisConcentration < 0.5)
        {
            return Unavailable("motion-has-no-dominant-axis", ordered.Length);
        }

        if (outputKind == MotionOutputKind.Main
            && candidate.SignalType != "pair-distance-inverted")
        {
            return Unavailable("no-measured-anatomical-contact-pair", ordered.Length);
        }

        if (requireLoopBoundary
            && Math.Abs(candidate.Values[0] - candidate.Values[^1]) > candidate.Range * 0.2)
        {
            return Unavailable("loop-boundary-position-discontinuity", ordered.Length);
        }

        double lower = Percentile(candidate.Values, 0.02);
        double upper = Percentile(candidate.Values, 0.98);
        if (upper - lower < 0.0001)
        {
            return Unavailable("robust-motion-range-too-small", ordered.Length);
        }

        var actions = new List<FunscriptAction>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            long at = ordered[index].RealtimeMilliseconds - start;
            int pos = (int)Math.Round(Math.Clamp((candidate.Values[index] - lower) / (upper - lower), 0, 1) * 100);
            if (actions.Count > 0 && actions[^1].At == at)
            {
                actions[^1] = new FunscriptAction(pos, at);
            }
            else
            {
                actions.Add(new FunscriptAction(pos, at));
            }
        }

        var source = new MotionSourceSelection(
            candidate.TargetPath,
            candidate.ReferencePath,
            candidate.SignalType,
            candidate.Axis,
            candidate.Range,
            candidate.AxisConcentration,
            candidate.SemanticScore,
            candidate.QualityScore);
        return new MotionGenerationResult(
            true,
            null,
            new FunscriptDocument("1.0", false, 100, actions),
            source,
            duration,
            ordered.Length);
    }

    public static bool TryExtractFirstCompleteLoop(
        IReadOnlyList<AnimationFrameSnapshot> frames,
        out IReadOnlyList<AnimationFrameSnapshot> loop)
    {
        var wrapIndices = new List<int>(2);
        for (var index = 1; index < frames.Count; index++)
        {
            double previous = Fraction(frames[index - 1].NormalizedTime);
            double current = Fraction(frames[index].NormalizedTime);
            if (previous >= 0.75 && current < 0.25)
            {
                wrapIndices.Add(index);
                if (wrapIndices.Count == 2)
                {
                    loop = frames.Skip(wrapIndices[0]).Take(wrapIndices[1] - wrapIndices[0] + 1).ToArray();
                    return true;
                }
            }
        }

        loop = Array.Empty<AnimationFrameSnapshot>();
        return false;
    }

    public static async Task WriteFunscriptAtomicAsync(
        string path,
        FunscriptDocument script,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        string json = JsonSerializer.Serialize(script, JsonOptions);
        await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    public static string CreateGalleryName(EventKey key)
    {
        string input = $"{key.Context}\n{key.ActorId}\n{key.AnimationId}\n{key.Phase}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"sinisistar2-{hash[..20]}";
    }

    private static Candidate? SelectCandidate(
        IReadOnlyList<AnimationFrameSnapshot> frames,
        MotionOutputKind outputKind)
    {
        var commonPaths = frames
            .Select(frame => AllTransforms(frame).Select(transform => transform.Path).ToHashSet(StringComparer.Ordinal))
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);
                return left;
            });

        var firstByPath = AllTransforms(frames[0])
            .Where(transform => commonPaths.Contains(transform.Path))
            .ToDictionary(transform => transform.Path, StringComparer.Ordinal);
        string rootPath = frames[0].Animator.Path;
        if (!firstByPath.ContainsKey(rootPath))
        {
            rootPath = firstByPath.Keys.OrderBy(path => path.Count(character => character == '/')).FirstOrDefault() ?? string.Empty;
        }

        IEnumerable<TransformSnapshot> semantic = firstByPath.Values.Where(transform =>
            IsSemanticMatch(transform.Path, outputKind));
        TransformSnapshot[] targets = semantic.ToArray();
        if (targets.Length == 0 || string.IsNullOrEmpty(rootPath))
        {
            return null;
        }

        Candidate? best = null;
        foreach (TransformSnapshot target in targets)
        {
            var vectors = frames.Select(frame =>
            {
                TransformSnapshot targetFrame = Find(frame, target.Path);
                TransformSnapshot rootFrame = Find(frame, rootPath);
                return targetFrame.WorldPosition - rootFrame.WorldPosition;
            }).ToArray();
            Candidate translation = CreateVectorCandidate(
                target.Path,
                rootPath,
                "relative-world-translation",
                vectors,
                SemanticScore(target.Path, outputKind));
            best = Better(best, translation);

            var rotations = frames.Select(frame => QuaternionLog(Find(frame, target.Path).LocalRotation)).ToArray();
            Candidate rotation = CreateVectorCandidate(
                target.Path,
                ParentPath(target.Path),
                "local-rotation-log",
                rotations,
                SemanticScore(target.Path, outputKind));
            best = Better(best, rotation);
        }

        if (outputKind == MotionOutputKind.Main)
        {
            TransformSnapshot[] pairTargets = targets.Take(24).ToArray();
            for (var left = 0; left < pairTargets.Length; left++)
            {
                for (var right = left + 1; right < pairTargets.Length; right++)
                {
                    if (ShareImmediateParent(pairTargets[left].Path, pairTargets[right].Path))
                    {
                        continue;
                    }

                    double[] distances = frames.Select(frame =>
                        (Find(frame, pairTargets[left].Path).WorldPosition
                            - Find(frame, pairTargets[right].Path).WorldPosition).Length).ToArray();
                    double range = distances.Max() - distances.Min();
                    double semanticScore = SemanticScore(pairTargets[left].Path, outputKind)
                        + SemanticScore(pairTargets[right].Path, outputKind);
                    var distance = new Candidate(
                        pairTargets[left].Path,
                        pairTargets[right].Path,
                        "pair-distance-inverted",
                        new Vector3Snapshot(1, 0, 0),
                        distances.Select(value => -value).ToArray(),
                        range,
                        1,
                        semanticScore,
                        range * semanticScore * 3);
                    best = Better(best, distance);
                }
            }
        }

        return best;
    }

    private static Candidate CreateVectorCandidate(
        string target,
        string reference,
        string signalType,
        IReadOnlyList<Vector3Snapshot> vectors,
        double semanticScore)
    {
        Vector3Snapshot mean = new(vectors.Average(value => value.X), vectors.Average(value => value.Y), vectors.Average(value => value.Z));
        double[,] covariance = new double[3, 3];
        foreach (Vector3Snapshot value in vectors)
        {
            double[] component = { value.X - mean.X, value.Y - mean.Y, value.Z - mean.Z };
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    covariance[row, column] += component[row] * component[column];
                }
            }
        }

        Vector3Snapshot axis = PrincipalAxis(covariance);
        double[] projected = vectors.Select(value => Dot(value - mean, axis)).ToArray();
        double range = projected.Max() - projected.Min();
        double totalVariance = covariance[0, 0] + covariance[1, 1] + covariance[2, 2];
        double principalVariance = Dot(Multiply(covariance, axis), axis);
        double concentration = totalVariance <= 1e-12 ? 0 : principalVariance / totalVariance;
        return new Candidate(
            target,
            reference,
            signalType,
            axis,
            projected,
            range,
            concentration,
            semanticScore,
            range * concentration * semanticScore);
    }

    private static Candidate? Better(Candidate? current, Candidate next) =>
        current is null || next.QualityScore > current.QualityScore ? next : current;

    private static TransformSnapshot Find(AnimationFrameSnapshot frame, string path) =>
        AllTransforms(frame).First(transform => transform.Path == path);

    private static IEnumerable<TransformSnapshot> AllTransforms(AnimationFrameSnapshot frame) =>
        frame.Animator.Transforms.Concat(
            frame.RelatedAnimators?.SelectMany(animator => animator.Transforms)
            ?? Enumerable.Empty<TransformSnapshot>());

    private static bool IsSemanticMatch(string path, MotionOutputKind kind)
    {
        string value = $" {path.ToLowerInvariant()} ";
        if (kind == MotionOutputKind.Main)
        {
            return MainTerms.Any(value.Contains);
        }

        if (!BreastTerms.Any(value.Contains))
        {
            return false;
        }

        string[] side = kind == MotionOutputKind.BreastLeft ? LeftTerms : RightTerms;
        return side.Any(value.Contains);
    }

    private static double SemanticScore(string path, MotionOutputKind kind)
    {
        string value = $" {path.ToLowerInvariant()} ";
        string[] anatomy = kind == MotionOutputKind.Main ? MainTerms : BreastTerms;
        double score = 1 + anatomy.Count(value.Contains);
        if (kind != MotionOutputKind.Main)
        {
            string[] side = kind == MotionOutputKind.BreastLeft ? LeftTerms : RightTerms;
            score += side.Count(value.Contains) * 0.5;
        }

        return score;
    }

    private static bool HasContinuousTimeline(IReadOnlyList<AnimationFrameSnapshot> frames)
    {
        long[] intervals = frames.Zip(frames.Skip(1), (left, right) => right.RealtimeMilliseconds - left.RealtimeMilliseconds)
            .Where(interval => interval > 0)
            .OrderBy(interval => interval)
            .ToArray();
        if (intervals.Length == 0)
        {
            return false;
        }

        double median = intervals[intervals.Length / 2];
        return intervals.Max() <= Math.Max(100, median * 4);
    }

    private static Vector3Snapshot PrincipalAxis(double[,] covariance)
    {
        var axis = new Vector3Snapshot(1, 1, 1);
        for (var iteration = 0; iteration < 16; iteration++)
        {
            Vector3Snapshot next = Multiply(covariance, axis);
            double length = next.Length;
            if (length <= 1e-12)
            {
                return new Vector3Snapshot(1, 0, 0);
            }

            axis = new Vector3Snapshot(next.X / length, next.Y / length, next.Z / length);
        }

        return axis;
    }

    private static Vector3Snapshot Multiply(double[,] matrix, Vector3Snapshot vector) => new(
        (matrix[0, 0] * vector.X) + (matrix[0, 1] * vector.Y) + (matrix[0, 2] * vector.Z),
        (matrix[1, 0] * vector.X) + (matrix[1, 1] * vector.Y) + (matrix[1, 2] * vector.Z),
        (matrix[2, 0] * vector.X) + (matrix[2, 1] * vector.Y) + (matrix[2, 2] * vector.Z));

    private static double Dot(Vector3Snapshot left, Vector3Snapshot right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static Vector3Snapshot QuaternionLog(QuaternionSnapshot quaternion)
    {
        double norm = Math.Sqrt((quaternion.X * quaternion.X) + (quaternion.Y * quaternion.Y)
            + (quaternion.Z * quaternion.Z) + (quaternion.W * quaternion.W));
        if (norm <= 1e-12)
        {
            return default;
        }

        double sign = quaternion.W < 0 ? -1 : 1;
        double x = quaternion.X * sign / norm;
        double y = quaternion.Y * sign / norm;
        double z = quaternion.Z * sign / norm;
        double w = Math.Clamp(quaternion.W * sign / norm, -1, 1);
        double sinHalf = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (sinHalf <= 1e-12)
        {
            return default;
        }

        double scale = 2 * Math.Atan2(sinHalf, w) / sinHalf;
        return new Vector3Snapshot(x * scale, y * scale, z * scale);
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        double index = (ordered.Length - 1) * percentile;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        double fraction = index - lower;
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * fraction);
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private static string ParentPath(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[..separator];
    }

    private static bool ShareImmediateParent(string left, string right) =>
        string.Equals(ParentPath(left), ParentPath(right), StringComparison.Ordinal);

    private static MotionGenerationResult Unavailable(string reason, int sampleCount) =>
        new(false, reason, null, null, 0, sampleCount);

    private sealed record Candidate(
        string TargetPath,
        string ReferencePath,
        string SignalType,
        Vector3Snapshot Axis,
        double[] Values,
        double Range,
        double AxisConcentration,
        double SemanticScore,
        double QualityScore);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
