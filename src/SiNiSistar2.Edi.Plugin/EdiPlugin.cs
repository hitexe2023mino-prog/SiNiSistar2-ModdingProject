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
    public const string PluginVersion = "1.2.0";

    private readonly CancellationTokenSource _lifetime = new();
    private EdiHttpClient? _ediClient;
    private AsyncEdiCommandSink? _sink;
    private OutputGate? _gateSink;
    private DiagnosticRecorder? _diagnostics;
    private AnimationSessionWriter? _session;
    private TriggerCatalog? _catalog;
    private AuthoringServer? _authoring;
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
            "Main-thread trigger discovery interval. Zero observes every Update.");
        ConfigEntry<float> channelDiscoverySeconds = Config.Bind(
            "EDI",
            "BindingDiscoverySeconds",
            60f,
            "How long after EDI first answers to keep re-verifying an output whose binding does "
            + "not hold yet, so a device connected during startup still opens (SPEC001 DEC-018). "
            + "Outputs still unbound when this elapses stay suppressed until the game is restarted.");
        ConfigEntry<string> authoringUrl = Config.Bind(
            "Authoring",
            "BaseUrl",
            "http://127.0.0.1:5601/",
            "Loopback base URL that serves the funscript authoring GUI. Non-loopback is rejected.");
        ConfigEntry<string> authoringKey = Config.Bind(
            "Authoring",
            "OpenGuiKey",
            "F6",
            "Key that opens the authoring GUI in the default browser while the game runs. A "
            + "UnityEngine.KeyCode name; empty or 'None' disables the key. F7 to F11 are taken by "
            + "the pleasure MOD's screens.");
        ConfigEntry<bool> openAuthoringOnStart = Config.Bind(
            "Authoring",
            "OpenGuiOnStart",
            false,
            "Open the authoring GUI in the default browser as the game starts. Off by default: a "
            + "browser stealing focus during launch is not what someone starting the game asked "
            + "for. The key above is the usual way in.");

        string mappingPath = Path.Combine(Paths.ConfigPath, PluginGuid, "mappings.json");
        MappingValidationResult validation = MappingRepository.Load(mappingPath);
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
                new CaptureCapability("trigger-transitions", true, null),
                new CaptureCapability("gallery-stage-enumeration", true, null),
                new CaptureCapability("unity-ui-text", true, null),
                new CaptureCapability(
                    "hold-stage-state-machine",
                    false,
                    "This build exposes no general hold state machine; HoldStateRp exists only on "
                    + "MeatTentacleCluster. Hold stages fall back to the animator state name "
                    + "(SPEC001 FR-033)."),
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

        _catalog = new TriggerCatalog(
            Path.Combine(diagnosticsRoot, "catalog", gameBuildId, "trigger-catalog.json"),
            gameBuildId,
            mappings.Document.TargetGameBuild,
            message => Log.LogWarning(message));

        _sink = new AsyncEdiCommandSink(
            _ediClient,
            mappings.OutputIds,
            message => Log.LogInfo(message),
            message => Log.LogWarning(message));
        _gateSink = new OutputGate(_sink, mappings.OutputIds);
        var coordinator = new PlaybackCoordinator(
            mappings,
            _gateSink,
            _diagnostics,
            message => Log.LogWarning(message),
            _gateSink);

        var authoringStore = new AuthoringStore(
            Path.Combine(Paths.GameRootPath, "Edi", "Gallery"),
            Path.Combine(diagnosticsRoot, "generated"),
            mappingPath,
            gameBuildId,
            mappings,
            _catalog,
            token => _ediClient!.ReloadAsync(token));

        var live = new LiveTriggerState();
        string authoringAssets = Path.Combine(Paths.PluginPath, PluginGuid, "authoring");
        _authoring = AuthoringServer.TryStart(
            authoringUrl.Value,
            _catalog,
            mappings,
            authoringStore,
            coordinator,
            live,
            authoringAssets,
            out IReadOnlyList<string> authoringErrors,
            message => Log.LogInfo(message),
            message => Log.LogWarning(message),
            _gateSink);
        foreach (string error in authoringErrors)
        {
            Log.LogError(error);
        }

        string? authoringGuiUrl = _authoring?.BaseUri.ToString();
        KeyCode openKey = ParseKey(authoringKey.Value);
        _observer = AddComponent<RuntimeObserver>();
        _observer.Configure(
            coordinator,
            _diagnostics,
            _session,
            _catalog,
            live,
            Math.Max(0f, pollInterval.Value),
            Log,
            authoringGuiUrl,
            openKey);

        if (openAuthoringOnStart.Value)
        {
            AuthoringGuiLauncher.Open(authoringGuiUrl, Log, "OpenGuiOnStart is enabled");
        }

        // Said plainly and near the URL, because a key nobody knows about is the same as no key.
        if (authoringGuiUrl is not null)
        {
            Log.LogInfo(
                openKey == KeyCode.None
                    ? $"Press nothing to open the authoring GUI: Authoring.OpenGuiKey is disabled. "
                      + $"It is served at {authoringGuiUrl}."
                    : $"Press {openKey} in game to open the funscript authoring GUI in your browser "
                      + $"({authoringGuiUrl}). Change the key with Authoring.OpenGuiKey.");
        }

        _readinessTask = StartUpAsync(
            authoringStore.GalleryRoot,
            mappings,
            TimeSpan.FromSeconds(Math.Max(0d, channelDiscoverySeconds.Value)),
            _lifetime.Token);
        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; mapping={mappings.Document.MappingVersion}, "
            + $"mappingPath={Path.GetFullPath(mappingPath)}, gameAssemblySha256={gameAssemblyHash}, "
            + $"globalMetadataSha256={metadataHash}, triggerCapture=always, session={_session.OutputPath}, "
            + $"catalog={_catalog.Path}, authoringGui={_authoring?.BaseUri.ToString() ?? "disabled"}, "
            + $"endpoint={uri}.");
    }

    /// <summary>
    /// Reads the configured hotkey, or <c>None</c> when it is empty or unrecognised.
    ///
    /// An unrecognised name is reported rather than silently dropped: a key that does nothing reads
    /// as a broken feature, and the reason it does nothing is a typo the user can fix.
    /// </summary>
    private KeyCode ParseKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return KeyCode.None;
        }

        if (Enum.TryParse(name!.Trim(), ignoreCase: true, out KeyCode key))
        {
            return key;
        }

        Log.LogWarning(
            $"Authoring.OpenGuiKey '{name}' is not a UnityEngine.KeyCode name, so no key opens the "
            + "authoring GUI. Use a name such as 'F6', or leave it empty to disable it on purpose.");
        return KeyCode.None;
    }

    public override bool Unload()
    {
        _lifetime.Cancel();
        _observer?.Shutdown();

        try
        {
            _authoring?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            _readinessTask?.Wait(TimeSpan.FromSeconds(2));
            _sink?.ShutdownAsync().Wait(TimeSpan.FromSeconds(2));
            if (_diagnostics is not null)
            {
                _diagnostics.WriteCoverageAsync().Wait(TimeSpan.FromSeconds(2));
            }
            _catalog?.SaveAsync().Wait(TimeSpan.FromSeconds(2));
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

    /// <summary>
    /// Reach EDI, confirm it carries the behaviour this MOD depends on, then verify each output's
    /// binding. The order matters: EDI being down is not a capability failure, and enabling an
    /// output whose binding is unverified is what lets a gallery reach the wrong device
    /// (SPEC001 7.4.3, FR-052).
    /// </summary>
    private async Task StartUpAsync(
        string galleryRoot,
        MappingRepository mappings,
        TimeSpan discoveryWindow,
        CancellationToken cancellationToken)
    {
        EdiCapabilities? capabilities = await ReachEdiAsync(cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        CapabilityCheck check = EdiCapabilityCheck.Evaluate(capabilities);
        Log.LogInfo(
            $"EDI reports version={capabilities?.Version ?? "(unknown)"}, "
            + $"strictVariantResolution={capabilities?.StrictVariantResolution.ToString() ?? "n/a"}, "
            + $"stopClearsFiller={capabilities?.StopClearsFiller.ToString() ?? "n/a"}, "
            + $"unassignedDeviceChannel={capabilities?.UnassignedDeviceChannel ?? "(unset)"}.");

        foreach (string warning in check.Warnings)
        {
            Log.LogWarning(warning);
        }

        if (!check.AllowsPlayback)
        {
            foreach (string blocking in check.Blocking)
            {
                Log.LogError(blocking);
            }

            Log.LogError("Playback stays disabled for this session until EDI provides these capabilities.");
            return;
        }

        GalleryRegistrationResult reload = await GalleryRegistration
            .ReloadAsync(galleryRoot, mappings, token => _ediClient!.ReloadAsync(token), cancellationToken)
            .ConfigureAwait(false);

        if (reload.Missing.Count > 0)
        {
            Log.LogWarning(
                "Filler variants named by the mapping are absent from the repository: "
                + $"{string.Join(", ", reload.Missing)}. Those outputs will fail to resolve the filler.");
        }

        if (reload.Stray.Count > 0)
        {
            Log.LogWarning(
                $"Gallery folders {string.Join(", ", reload.Stray)} hold funscripts but no output in the "
                + "roster plays those variants, so their target outputs cannot be derived (FR-057).");
        }

        if (reload.Succeeded)
        {
            Log.LogInfo(
                $"EDI re-read the gallery root; fillers in the mapping: {string.Join(", ", reload.Fillers)}.");
        }
        else
        {
            // EDI may already hold a current scan, so this is not fatal (FR-015).
            Log.LogWarning(
                $"EDI did not re-read the gallery root: {reload.Failure}. Playback continues; a gallery "
                + "EDI does not know will fail to resolve.");
        }

        await VerifyBindingsAsync(mappings, discoveryWindow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until EDI answers, then asks what it supports. Connection failures are retried with
    /// backoff; an HTTP answer, including 404, ends the wait because it is an answer (AC-051).
    /// </summary>
    private async Task<EdiCapabilities?> ReachEdiAsync(CancellationToken cancellationToken)
    {
        bool outageLogged = false;
        TimeSpan backoff = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                return await _ediClient!.GetInfoAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                if (!outageLogged)
                {
                    outageLogged = true;
                    Log.LogWarning(
                        "EDI is not reachable yet; playback stays fail-closed and the MOD keeps "
                        + $"retrying: {exception.Message}");
                }
            }

            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
        }

        return null;
    }

    /// <summary>
    /// Enables each output whose binding holds and suppresses the rest, one by one. A device that
    /// finishes connecting shortly after the game starts is absorbed by the discovery window,
    /// without leaving a permanent poll running in a partial setup (FR-042, DEC-018).
    /// </summary>
    private async Task VerifyBindingsAsync(
        MappingRepository mappings,
        TimeSpan discoveryWindow,
        CancellationToken cancellationToken)
    {
        bool outageLogged = false;
        bool channelMismatchLogged = false;
        DateTime? deadline = null;
        TimeSpan backoff = TimeSpan.FromSeconds(2);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<EdiDevice> devices = await _ediClient!
                    .GetDevicesAsync(cancellationToken)
                    .ConfigureAwait(false);

                // The window runs from EDI's first answer, so EDI starting late does not consume it.
                deadline ??= DateTime.UtcNow + discoveryWindow;
                outageLogged = false;

                if (!channelMismatchLogged)
                {
                    channelMismatchLogged = await WarnOnChannelMismatchAsync(mappings, cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (OutputBindingResult result in BindingVerifier.Verify(mappings.Outputs, devices))
                {
                    if (result.IsBound)
                    {
                        if (_gateSink!.Open(result.Output))
                        {
                            reported.Remove(result.Output);
                            _authoring?.ReportSuppression(result.Output, null);
                            Log.LogInfo($"Output '{result.Output}' is bound; playback enabled for it.");
                        }

                        continue;
                    }

                    string reason = string.Join("; ", result.Failures);
                    if (_gateSink!.Suppress(result.Output))
                    {
                        Log.LogWarning(
                            $"Output '{result.Output}' lost its binding and was stopped: {reason}");
                    }
                    else if (reported.Add(result.Output))
                    {
                        Log.LogWarning($"Output '{result.Output}' is suppressed: {reason}");
                    }

                    _authoring?.ReportSuppression(result.Output, reason);
                }

                if (_gateSink!.Suppressed().Count == 0)
                {
                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Log.LogWarning(
                        $"Output(s) {string.Join(", ", _gateSink!.Suppressed())} were still unbound after "
                        + $"{discoveryWindow.TotalSeconds:F0}s. They stay suppressed for this session; fix "
                        + "the EDI device assignment and restart the game.");
                    return;
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
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
        }
    }

    /// <summary>
    /// The roster and EDI's channel list are configured separately, so updating only one of them
    /// is easy to do and hard to see. Returns true once it has been reported (SPEC001 7.1).
    /// </summary>
    private async Task<bool> WarnOnChannelMismatchAsync(
        MappingRepository mappings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> channels = await _ediClient!
            .GetChannelsAsync(cancellationToken)
            .ConfigureAwait(false);
        string[] absent = mappings.OutputIds.Where(id => !channels.Contains(id)).ToArray();
        if (absent.Length > 0)
        {
            // EDI builds its channel set from EdiConfig.json only while selecting a game. Simply
            // restarting it restores the saved selection without re-reading Channels, so a channel
            // added to the file after the last selection stays absent and no device can be put on
            // it. Editing the file again is not the fix; re-selecting is.
            Log.LogWarning(
                $"EDI does not offer the channel(s) {string.Join(", ", absent)} that the device roster "
                + "declares, so no device can be assigned to them. EdiConfig.json already listing them "
                + "is not enough: EDI only rebuilds its channel set when a game is selected. Re-select "
                + "this repository's gallery folder in EDI once, then restart the game.");
        }

        return true;
    }
}
