using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using SiNiSistar2.Edi.Core;
using UnityEngine;

namespace SiNiSistar2.Edi.Plugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class EdiPlugin : BasePlugin
{
    public const string PluginGuid = "community.sinisistar2.edi";
    public const string PluginName = "SiNiSistar2 EDI Integration";
    public const string PluginVersion = "1.0.0";

    private readonly CancellationTokenSource _lifetime = new();
    private EdiHttpClient? _ediClient;
    private AsyncEdiCommandSink? _sink;
    private ReadinessGatedSink? _gateSink;
    private DiagnosticRecorder? _diagnostics;
    private AnimationSessionWriter? _session;
    private GeneratedMotionAssetStore? _generatedAssets;
    private RuntimeObserver? _observer;
    private Task? _readinessTask;

    public override void Load()
    {
        ConfigEntry<string> baseUrl = Config.Bind(
            "EDI",
            "BaseUrl",
            "http://127.0.0.1:5000/",
            "Loopback base URL of the EDI REST service.");
        ConfigEntry<float> pollInterval = Config.Bind(
            "Runtime",
            "PollIntervalSeconds",
            0f,
            "Main-thread event discovery interval. Zero observes every Update and is required for complete capture.");

        string mappingPath = Path.Combine(Paths.ConfigPath, PluginGuid, "mappings.json");
        string generatedMappingsPath = Path.Combine(Paths.ConfigPath, PluginGuid, "generated-mappings.json");
        MappingValidationResult validation = MappingRepository.Load(mappingPath, generatedMappingsPath);
        if (!validation.IsValid)
        {
            foreach (string error in validation.Errors)
            {
                Log.LogError(error);
            }

            Log.LogError($"Playback disabled: mapping validation failed at '{mappingPath}'.");
            return;
        }

        MappingRepository mappings = validation.Repository!;
        string gameAssemblyPath = Path.Combine(Paths.GameRootPath, "GameAssembly.dll");
        string metadataPath = Path.Combine(
            Paths.GameRootPath,
            "SiNiSistar2_Data",
            "il2cpp_data",
            "Metadata",
            "global-metadata.dat");

        string gameAssemblyHash;
        string metadataHash;
        try
        {
            gameAssemblyHash = BuildFingerprint.ComputeSha256(gameAssemblyPath);
            metadataHash = BuildFingerprint.ComputeSha256(metadataPath);
            if (!string.Equals(
                    gameAssemblyHash,
                    mappings.Document.TargetGameBuild.GameAssemblySha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    metadataHash,
                    mappings.Document.TargetGameBuild.GlobalMetadataSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                Log.LogError("Playback disabled: the installed game build does not match mappings.json.");
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError($"Playback disabled: could not fingerprint the game build: {exception.Message}");
            return;
        }

        Uri uri;
        try
        {
            uri = new Uri(baseUrl.Value, UriKind.Absolute);
            _ediClient = new EdiHttpClient(uri);
        }
        catch (Exception exception) when (exception is UriFormatException or ArgumentException)
        {
            Log.LogError($"Playback disabled: invalid EDI BaseUrl: {exception.Message}");
            return;
        }

        string coveragePath = Path.Combine(Paths.BepInExRootPath, "diagnostics", PluginGuid, "coverage.json");
        _diagnostics = new DiagnosticRecorder(mappings, coveragePath);
        string gameBuildId = $"{gameAssemblyHash[..16].ToLowerInvariant()}-{metadataHash[..16].ToLowerInvariant()}";
        string diagnosticsRoot = Path.Combine(Paths.BepInExRootPath, "diagnostics", PluginGuid);
        _session = new AnimationSessionWriter(
            Path.Combine(diagnosticsRoot, "sessions"),
            gameBuildId,
            PluginVersion,
            new[]
            {
                new CaptureCapability("animator-layers-clips-transforms", true, null),
                new CaptureCapability("unity-ui-text", true, null),
                new CaptureCapability(
                    "animator-parameters",
                    false,
                    "AnimatorControllerParameter getters are unstripped in the generated interop for this game build."),
                new CaptureCapability(
                    "textmeshpro",
                    false,
                    "Unity.TextMeshPro interop assembly is not present in the generated game interop."),
            },
            logWarning: message => Log.LogWarning(message));
        _generatedAssets = new GeneratedMotionAssetStore(
            Path.Combine(Paths.GameRootPath, "Edi", "Gallery"),
            Path.Combine(diagnosticsRoot, "generated"),
            generatedMappingsPath,
            gameBuildId,
            mappings);
        _sink = new AsyncEdiCommandSink(_ediClient, message => Log.LogWarning(message));
        _gateSink = new ReadinessGatedSink(_sink);
        var coordinator = new PlaybackCoordinator(
            mappings,
            _gateSink,
            _diagnostics,
            message => Log.LogWarning(message));

        _observer = AddComponent<RuntimeObserver>();
        _observer.Configure(
            coordinator,
            _diagnostics,
            _session,
            GenerateRegisterAndUploadAsync,
            Math.Max(0f, pollInterval.Value),
            Log);

        _readinessTask = VerifyChannelsUntilReadyAsync(_lifetime.Token);
        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; mapping={mappings.Document.MappingVersion}, "
            + $"mappingPath={Path.GetFullPath(mappingPath)}, gameAssemblySha256={gameAssemblyHash}, "
            + $"globalMetadataSha256={metadataHash}, eventCapture=always, session={_session.OutputPath}, endpoint={uri}.");
    }

    public override bool Unload()
    {
        _lifetime.Cancel();
        _observer?.Shutdown();

        try
        {
            _readinessTask?.Wait(TimeSpan.FromSeconds(2));
            _observer?.WaitForGenerationAsync().Wait(TimeSpan.FromSeconds(5));
            _sink?.ShutdownAsync().Wait(TimeSpan.FromSeconds(2));
            if (_diagnostics is not null)
            {
                _diagnostics.WriteCoverageAsync().Wait(TimeSpan.FromSeconds(2));
            }
            _session?.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Best-effort shutdown did not finish cleanly: {exception.Message}");
        }

        _sink?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
        _ediClient?.Dispose();
        _lifetime.Dispose();
        return true;
    }

    private async Task<GeneratedAssetResult> GenerateRegisterAndUploadAsync(
        EventKey key,
        IReadOnlyList<AnimationFrameSnapshot> frames,
        bool isLoop)
    {
        GeneratedAssetResult result = await _generatedAssets!
            .GenerateAsync(_session!.SessionId, key, frames, isLoop, _lifetime.Token)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return result;
        }

        await _ediClient!.UploadAssetsAsync(result.UploadAssets, _lifetime.Token).ConfigureAwait(false);
        await _generatedAssets.CommitMappingAsync(result.Mapping!, _lifetime.Token).ConfigureAwait(false);
        return result;
    }

    private async Task VerifyChannelsUntilReadyAsync(CancellationToken cancellationToken)
    {
        bool outageLogged = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<string> channels = await _ediClient!
                    .GetChannelsAsync(cancellationToken)
                    .ConfigureAwait(false);
                var available = channels.ToHashSet(StringComparer.Ordinal);
                string[] missing = EdiChannels.All.Where(x => !available.Contains(x)).ToArray();
                if (missing.Length == 0)
                {
                    _gateSink!.SetReady();
                    Log.LogInfo("EDI channels verified: main, breast. Playback enabled.");
                    return;
                }

                if (!outageLogged)
                {
                    outageLogged = true;
                    Log.LogWarning($"Playback waiting for required EDI channels: {string.Join(", ", missing)}.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                if (!outageLogged)
                {
                    outageLogged = true;
                    Log.LogWarning($"EDI is unavailable; playback remains fail-closed and will retry: {exception.Message}");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
