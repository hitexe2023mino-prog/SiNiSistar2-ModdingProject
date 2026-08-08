using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiNiSistar2.Edi.Core;

/// <summary>One position sample. <see cref="Pos"/> is 0-100, <see cref="At"/> is milliseconds.</summary>
public sealed record FunscriptAction(int Pos, long At);

public sealed record FunscriptDocument(
    string Version,
    bool Inverted,
    int Range,
    IReadOnlyList<FunscriptAction> Actions)
{
    /// <summary>Derived, so it stays out of the `.funscript` file that EDI and players read.</summary>
    [JsonIgnore]
    public long DurationMilliseconds => Actions.Count == 0 ? 0 : Actions[^1].At;
}

/// <summary>Outcome of validating an authored funscript before it is written (FR-040).</summary>
public sealed record FunscriptValidation(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> LoopWarnings)
{
    /// <summary>
    /// Advisory notes about how the waveform will feel on the device. Unlike
    /// <see cref="LoopWarnings"/> these never block a save: the waveform is legal, it just asks
    /// more of the hardware than it can deliver smoothly.
    /// </summary>
    public IReadOnlyList<string> MotionWarnings { get; init; } = Array.Empty<string>();

    public bool IsValid => Errors.Count == 0;
    public bool RequiresLoopApproval => LoopWarnings.Count > 0;
}

/// <summary>
/// Reads, validates, and writes `.funscript` assets. The MOD never synthesises, interpolates,
/// or mirrors a waveform; every position originates from the authoring GUI (SPEC001 FR-039).
/// </summary>
public static class Funscript
{
    /// <summary>A loop end may differ from the clip length by at most one 60 fps frame.</summary>
    public const long LoopToleranceMilliseconds = 17;

    /// <summary>
    /// Fastest travel the A10 piston is asked to follow, in position units per second. 500 is a
    /// full 0-100 traverse in 200 ms. Past this the carriage cannot reach the commanded position
    /// before the next one arrives, so the stroke is truncated and the motion reads as stuttering
    /// rather than as a faster stroke. It is a comfort threshold, not a protocol limit, so it only
    /// ever produces an advisory note.
    /// </summary>
    public const double MaxPistonUnitsPerSecond = 500d;

    /// <summary>A repeat seam wider than this many position units is felt as a jolt.</summary>
    public const int SeamToleranceUnits = 5;

    /// <summary>
    /// Shortest interval between two piston commands that the device can actually act on.
    /// A funscript action is not a sample of a waveform: it is a target the device travels to on
    /// its own. Each one becomes a single Bluetooth "move to position P by time T" write, so a
    /// second action arriving before the first move has run preempts it and the carriage never
    /// covers the distance. Sampling a curve finely therefore produces less motion than drawing
    /// only its turning points, which is the opposite of what the canvas suggests.
    /// </summary>
    public const long MinPistonSegmentMilliseconds = 100;

    /// <summary>
    /// Variant whose device is a linear piston. Used only to phrase the advisory motion notes
    /// below, which are about what that hardware can physically follow. The set of outputs comes
    /// from the mapping roster, never from here (FR-048).
    /// </summary>
    public const string PistonVariant = "a10-main";

    /// <summary>Stable gallery name derived from the trigger key, never from raw game strings.</summary>
    public static string CreateGalleryName(EventKey key)
    {
        string input = $"{key.Context}\n{key.ActorId}\n{key.AnimationId}\n{key.Phase}\n{key.StageId}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"sinisistar2-{hash[..20]}";
    }

    public static FunscriptValidation Validate(
        FunscriptDocument script,
        double clipLengthSeconds,
        bool isLoop,
        string? variant = null,
        bool repeats = false)
    {
        var errors = new List<string>();
        var loopWarnings = new List<string>();

        if (script.Actions.Count < 2)
        {
            errors.Add("A funscript must contain at least two actions.");
        }

        long previousAt = -1;
        foreach (FunscriptAction action in script.Actions)
        {
            if (action.At < 0)
            {
                errors.Add($"Action time {action.At} must not be negative.");
            }
            else if (action.At <= previousAt)
            {
                errors.Add($"Action times must strictly increase; {action.At} follows {previousAt}.");
            }

            if (action.Pos is < 0 or > 100)
            {
                errors.Add($"Action position {action.Pos} must be between 0 and 100.");
            }

            previousAt = Math.Max(previousAt, action.At);
        }

        if (isLoop && clipLengthSeconds > 0 && script.Actions.Count > 0)
        {
            long clipMilliseconds = (long)Math.Round(clipLengthSeconds * 1000d);
            long difference = Math.Abs(script.DurationMilliseconds - clipMilliseconds);
            if (difference > LoopToleranceMilliseconds)
            {
                loopWarnings.Add(
                    $"The loop ends at {script.DurationMilliseconds} ms but the game clip is "
                    + $"{clipMilliseconds} ms; the difference of {difference} ms exceeds the "
                    + $"{LoopToleranceMilliseconds} ms tolerance.");
            }
        }

        return new FunscriptValidation(errors, loopWarnings)
        {
            MotionWarnings = errors.Count > 0
                ? Array.Empty<string>()
                : DescribeMotion(script, variant, isLoop || repeats),
        };
    }

    /// <summary>
    /// Notes the things that make a legal waveform play roughly: strokes the piston cannot finish
    /// in the time allowed, a script that starts after zero, and a repeat seam that jumps. None of
    /// these are corrected here — the MOD never reshapes an authored waveform (FR-039) — they are
    /// reported so the author can redraw them.
    /// </summary>
    private static IReadOnlyList<string> DescribeMotion(
        FunscriptDocument script,
        string? variant,
        bool repeats)
    {
        var notes = new List<string>();
        if (script.Actions.Count < 2)
        {
            return notes;
        }

        if (string.Equals(variant, PistonVariant, StringComparison.Ordinal))
        {
            var tooShort = 0;
            for (var index = 1; index < script.Actions.Count; index++)
            {
                if (script.Actions[index].At - script.Actions[index - 1].At < MinPistonSegmentMilliseconds)
                {
                    tooShort++;
                }
            }

            if (tooShort > 0)
            {
                notes.Add(
                    $"{tooShort} 区間が {MinPistonSegmentMilliseconds}ms 未満です。点は波形の標本では"
                    + "なく「そこまで動け」という指示で、次の点が届くと前の移動は打ち切られます。"
                    + "細かく打つほどストロークは小さくなるので、曲線を標本化せず折り返し点だけを"
                    + "置いてください（間の動きはデバイスが作ります）。");
            }

            for (var index = 1; index < script.Actions.Count; index++)
            {
                FunscriptAction previous = script.Actions[index - 1];
                FunscriptAction current = script.Actions[index];
                long span = current.At - previous.At;
                if (span <= 0)
                {
                    continue;
                }

                double rate = Math.Abs(current.Pos - previous.Pos) * 1000d / span;
                if (rate > MaxPistonUnitsPerSecond)
                {
                    notes.Add(
                        $"{previous.At}ms→{current.At}ms は {Math.Abs(current.Pos - previous.Pos)} "
                        + $"を {span}ms で移動する指示（{rate:F0} units/s）で、ピストンが追従できる "
                        + $"{MaxPistonUnitsPerSecond:F0} units/s を超えています。区間を長くするか"
                        + "振幅を小さくしてください。");
                }
            }
        }

        if (!repeats)
        {
            return notes;
        }

        long start = script.Actions[0].At;
        if (start > 0)
        {
            notes.Add(
                $"最初の点が {start}ms にあるため、繰り返しのたびに {start}ms の停止が入ります。"
                + "最初の点を 0ms に置いてください。");
        }

        int seam = Math.Abs(script.Actions[^1].Pos - script.Actions[0].Pos);
        if (seam > SeamToleranceUnits)
        {
            notes.Add(
                $"終端 {script.Actions[^1].Pos} と始端 {script.Actions[0].Pos} が {seam} 離れており、"
                + "繰り返しの継ぎ目で瞬間的に跳びます。両端を揃えてください。");
        }

        return notes;
    }

    public static async Task WriteAtomicAsync(
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

    public static FunscriptDocument? TryRead(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<FunscriptDocument>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
