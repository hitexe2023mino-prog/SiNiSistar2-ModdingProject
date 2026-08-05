using System.Security.Cryptography;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

public sealed record GeneratedUploadAsset(string FileName, string Path);

public sealed record GeneratedAssetResult(
    bool Success,
    string Gallery,
    EventMapping? Mapping,
    IReadOnlyList<GeneratedUploadAsset> UploadAssets,
    string ManifestPath,
    IReadOnlyDictionary<string, MotionGenerationResult> Outputs,
    string? UnavailableReason);

public sealed class GeneratedMotionAssetStore
{
    private readonly string _galleryRoot;
    private readonly string _manifestRoot;
    private readonly string _generatedMappingsPath;
    private readonly string _gameBuildId;
    private readonly MappingRepository _mappings;
    private readonly MotionScriptGenerator _generator = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public GeneratedMotionAssetStore(
        string galleryRoot,
        string manifestRoot,
        string generatedMappingsPath,
        string gameBuildId,
        MappingRepository mappings)
    {
        _galleryRoot = galleryRoot;
        _manifestRoot = manifestRoot;
        _generatedMappingsPath = generatedMappingsPath;
        _gameBuildId = gameBuildId;
        _mappings = mappings;
    }

    public async Task<GeneratedAssetResult> GenerateAsync(
        string sourceSessionId,
        EventKey key,
        IReadOnlyList<AnimationFrameSnapshot> frames,
        bool isLoop,
        CancellationToken cancellationToken = default)
    {
        string gallery = MotionScriptGenerator.CreateGalleryName(key);
        MotionGenerationResult main = _generator.Generate(frames, MotionOutputKind.Main, isLoop);
        MotionGenerationResult left = _generator.Generate(frames, MotionOutputKind.BreastLeft, isLoop);
        MotionGenerationResult right = _generator.Generate(frames, MotionOutputKind.BreastRight, isLoop);
        var outputs = new Dictionary<string, MotionGenerationResult>(StringComparer.Ordinal)
        {
            ["a10-main"] = main,
            ["ufo-left"] = left,
            ["ufo-right"] = right,
        };

        var channels = new List<string>(2);
        if (main.Success)
        {
            channels.Add(EdiChannels.Main);
        }

        if (left.Success && right.Success)
        {
            channels.Add(EdiChannels.Breast);
        }

        string manifestDirectory = Path.Combine(_manifestRoot, _gameBuildId);
        string manifestPath = Path.Combine(manifestDirectory, $"{gallery}.manifest.json");
        if (channels.Count == 0)
        {
            await WriteManifestAsync(
                manifestPath,
                sourceSessionId,
                key,
                gallery,
                frames,
                isLoop,
                outputs,
                Array.Empty<GeneratedUploadAsset>(),
                "no-output-passed-motion-quality",
                cancellationToken).ConfigureAwait(false);
            return new GeneratedAssetResult(
                false,
                gallery,
                null,
                Array.Empty<GeneratedUploadAsset>(),
                manifestPath,
                outputs,
                "no-output-passed-motion-quality");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var assets = new List<GeneratedUploadAsset>(3);
            await WriteIfSuccessful("a10-main", main, gallery, assets, cancellationToken).ConfigureAwait(false);
            if (left.Success && right.Success)
            {
                await WriteIfSuccessful("ufo-left", left, gallery, assets, cancellationToken).ConfigureAwait(false);
                await WriteIfSuccessful("ufo-right", right, gallery, assets, cancellationToken).ConfigureAwait(false);
            }

            var mapping = new EventMapping
            {
                Id = $"auto-{gallery}",
                Context = key.Context,
                ActorId = key.ActorId,
                AnimationId = key.AnimationId,
                Phase = key.Phase,
                Disposition = "mapped",
                Gallery = gallery,
                Channels = channels,
                SeekMode = "animation-time",
            };
            await WriteManifestAsync(
                manifestPath,
                sourceSessionId,
                key,
                gallery,
                frames,
                isLoop,
                outputs,
                assets,
                null,
                cancellationToken).ConfigureAwait(false);
            return new GeneratedAssetResult(
                true,
                gallery,
                mapping,
                assets,
                manifestPath,
                outputs,
                null);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task CommitMappingAsync(
        EventMapping mapping,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _mappings.RegisterGenerated(mapping);
            await _mappings.SaveGeneratedAsync(_generatedMappingsPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteIfSuccessful(
        string variant,
        MotionGenerationResult result,
        string gallery,
        ICollection<GeneratedUploadAsset> assets,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return;
        }

        string path = Path.Combine(_galleryRoot, variant, $"{gallery}.funscript");
        await MotionScriptGenerator.WriteFunscriptAtomicAsync(path, result.Script!, cancellationToken)
            .ConfigureAwait(false);
        assets.Add(new GeneratedUploadAsset($"{gallery}.{variant}.funscript", path));
    }

    private static async Task WriteManifestAsync(
        string path,
        string sourceSessionId,
        EventKey key,
        string gallery,
        IReadOnlyList<AnimationFrameSnapshot> frames,
        bool isLoop,
        IReadOnlyDictionary<string, MotionGenerationResult> outputs,
        IReadOnlyList<GeneratedUploadAsset> assets,
        string? unavailableReason,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GeneratedUploadAsset asset in assets)
        {
            await using FileStream stream = File.OpenRead(asset.Path);
            using var sha256 = SHA256.Create();
            hashes[asset.FileName] = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }

        var manifest = new
        {
            schemaVersion = 1,
            generatorVersion = 1,
            sourceSessionId,
            eventKey = key,
            gallery,
            isLoop,
            sourceSampleCount = frames.Count,
            sourceStartRealtimeMs = frames.Count == 0 ? 0 : frames[0].RealtimeMilliseconds,
            sourceEndRealtimeMs = frames.Count == 0 ? 0 : frames[^1].RealtimeMilliseconds,
            outputs,
            fileSha256 = hashes,
            unavailableReason,
            generatedAt = DateTimeOffset.UtcNow,
        };
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        await File.WriteAllTextAsync(
            temp,
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
