using System.Net;
using System.Text;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

/// <summary>
/// Serves the funscript authoring GUI over loopback only. The GUI has no authentication, so the
/// loopback restriction is the only reachability control and a non-loopback host is a
/// configuration error (SPEC001 FR-035, 10.2).
/// </summary>
public sealed class AuthoringServer : IAsyncDisposable
{
    /// <summary>How long a live reading stays valid; a little over the GUI's poll interval.</summary>
    private static readonly TimeSpan LivePollWindow = TimeSpan.FromSeconds(3);

    private static readonly IReadOnlySet<string> LoopbackHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1", "localhost", "::1", "[::1]",
    };

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TriggerCatalog _catalog;
    private readonly MappingRepository _mappings;
    private readonly AuthoringStore _store;
    private readonly PlaybackCoordinator _coordinator;
    private readonly LiveTriggerState _live;
    private readonly string _assetRoot;
    private readonly Action<string>? _logInfo;
    private readonly Action<string>? _logWarning;
    private readonly IOutputAvailability? _availability;
    private readonly Dictionary<string, string> _suppressionReasons = new(StringComparer.Ordinal);
    private Task? _worker;

    private AuthoringServer(
        Uri baseUri,
        TriggerCatalog catalog,
        MappingRepository mappings,
        AuthoringStore store,
        PlaybackCoordinator coordinator,
        LiveTriggerState live,
        string assetRoot,
        Action<string>? logInfo,
        Action<string>? logWarning,
        IOutputAvailability? availability)
    {
        BaseUri = baseUri;
        _availability = availability;
        _catalog = catalog;
        _mappings = mappings;
        _store = store;
        _coordinator = coordinator;
        _live = live;
        _assetRoot = assetRoot;
        _logInfo = logInfo;
        _logWarning = logWarning;
        _listener.Prefixes.Add(baseUri.AbsoluteUri);
    }

    public Uri BaseUri { get; }

    /// <summary>Validates the configured address before any socket is opened.</summary>
    public static IReadOnlyList<string> ValidateBaseUrl(string value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return new[] { $"The authoring GUI URL '{value}' is not an absolute URL." };
        }

        if (parsed.Scheme != Uri.UriSchemeHttp)
        {
            return new[] { $"The authoring GUI URL '{value}' must use http." };
        }

        if (!LoopbackHosts.Contains(parsed.Host) && !parsed.IsLoopback)
        {
            return new[]
            {
                $"The authoring GUI URL '{value}' must bind to loopback; the GUI has no "
                + "authentication and must not be reachable from other hosts.",
            };
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return new[] { $"The authoring GUI URL '{value}' must not contain user information." };
        }

        uri = parsed.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? parsed
            : new Uri(parsed.AbsoluteUri + "/");
        return Array.Empty<string>();
    }

    public static AuthoringServer? TryStart(
        string baseUrl,
        TriggerCatalog catalog,
        MappingRepository mappings,
        AuthoringStore store,
        PlaybackCoordinator coordinator,
        LiveTriggerState live,
        string assetRoot,
        out IReadOnlyList<string> errors,
        Action<string>? logInfo = null,
        Action<string>? logWarning = null,
        IOutputAvailability? availability = null)
    {
        errors = ValidateBaseUrl(baseUrl, out Uri? uri);
        if (errors.Count > 0)
        {
            return null;
        }

        var server = new AuthoringServer(
            uri!, catalog, mappings, store, coordinator, live, assetRoot, logInfo, logWarning, availability);
        try
        {
            server._listener.Start();
        }
        catch (HttpListenerException exception)
        {
            errors = new[]
            {
                $"The authoring GUI could not listen on '{uri}': {exception.Message}. "
                + "Gameplay and device control continue without the GUI.",
            };
            server._listener.Close();
            return null;
        }

        server._worker = Task.Run(server.AcceptLoopAsync);
        logInfo?.Invoke($"Authoring GUI available at {uri}");
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
        }

        if (_worker is not null)
        {
            try
            {
                await _worker.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                // Best effort: shutdown must not block the game.
            }
        }

        _lifetime.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => HandleSafelyAsync(context));
        }
    }

    private async Task HandleSafelyAsync(HttpListenerContext context)
    {
        try
        {
            await HandleAsync(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logWarning?.Invoke($"Authoring request failed: {exception.Message}");
            try
            {
                await WriteJsonAsync(context, 500, new { error = exception.Message }).ConfigureAwait(false);
            }
            catch (Exception inner) when (inner is HttpListenerException or ObjectDisposedException or IOException)
            {
                // The client disconnected.
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
            {
                // Already closed.
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;

        // Defence in depth: the prefix already restricts binding, but reject any request that did
        // not originate from this machine.
        if (request.RemoteEndPoint is not null && !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        {
            await WriteJsonAsync(context, 403, new { error = "Only loopback clients are served." }).ConfigureAwait(false);
            return;
        }

        string path = request.Url?.AbsolutePath ?? "/";
        switch (path)
        {
            case "/api/catalog" when request.HttpMethod == "GET":
                await WriteJsonAsync(context, 200, BuildCatalogResponse()).ConfigureAwait(false);
                return;

            case "/api/current" when request.HttpMethod == "GET":
                await WriteJsonAsync(context, 200, BuildCurrentResponse()).ConfigureAwait(false);
                return;

            case "/api/script" when request.HttpMethod == "GET":
                await HandleLoadScriptAsync(context, request).ConfigureAwait(false);
                return;

            case "/api/fillers" when request.HttpMethod == "GET":
                await WriteJsonAsync(context, 200, BuildFillersResponse()).ConfigureAwait(false);
                return;

            case "/api/save-filler" when request.HttpMethod == "POST":
                await HandleSaveFillerAsync(context, request).ConfigureAwait(false);
                return;

            case "/api/save" when request.HttpMethod == "POST":
                await HandleSaveAsync(context, request).ConfigureAwait(false);
                return;

            case "/api/preview" when request.HttpMethod == "POST":
                await HandlePreviewAsync(context, request).ConfigureAwait(false);
                return;

            case "/api/preview/stop" when request.HttpMethod == "POST":
                _coordinator.EndPreview();
                await WriteJsonAsync(context, 200, new { stopped = true }).ConfigureAwait(false);
                return;

            default:
                await ServeStaticAsync(context, path).ConfigureAwait(false);
                return;
        }
    }

    private object BuildCatalogResponse()
    {
        var triggers = _catalog.Snapshot().Select(entry =>
        {
            EventKey key = entry.Key;
            _mappings.TryGet(key, out EventMapping? mapping);
            IReadOnlyDictionary<string, FunscriptDocument> existing = _store.LoadExisting(key);
            return new
            {
                context = entry.Context,
                actorId = entry.ActorId,
                animationId = entry.AnimationId,
                phase = entry.Phase,
                stageId = entry.StageId,
                gallery = Funscript.CreateGalleryName(key),
                clipLengthSeconds = entry.ClipLengthSeconds,
                isLooping = entry.IsLooping,
                source = entry.Source,
                displayName = entry.DisplayName,
                actorDisplayName = entry.ActorDisplayName,
                displayNumber = entry.DisplayNumber,
                stageNumber = entry.StageNumber,
                stageIndex = entry.StageIndex,
                sceneName = entry.SceneName,
                firstSeenAt = entry.FirstSeenAt,
                lastSeenAt = entry.LastSeenAt,
                disposition = mapping?.Disposition ?? "unclassified",
                outputs = mapping?.Outputs
                    .Select(assignment => new { id = assignment.Id, gallery = assignment.Gallery })
                    .ToArray()
                    ?? Array.Empty<object>(),
                authoredVariants = existing.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            };
        }).ToArray();

        return new
        {
            gameBuild = _mappings.Document.TargetGameBuild,
            mappingVersion = _mappings.Document.MappingVersion,
            loopToleranceMs = Funscript.LoopToleranceMilliseconds,
            outputs = _mappings.Outputs.Select(output => new
            {
                id = output.Id,
                displayName = output.DisplayName,
                variant = output.EdiVariant,
                ediDeviceName = output.EdiDeviceName,
                available = _availability?.IsAvailable(output.Id) ?? true,
                suppressionReason = _suppressionReasons.TryGetValue(output.Id, out string? reason) ? reason : null,
            }).ToArray(),
            triggers,
        };
    }

    /// <summary>
    /// The trigger the game is playing right now. A reading older than the window is dropped so a
    /// stale row is never highlighted after the game pauses, changes scene, or exits.
    /// </summary>
    private object BuildCurrentResponse()
    {
        LiveTrigger? live = _live.Read(LivePollWindow);
        if (live is null)
        {
            return new { playing = false };
        }

        return new
        {
            playing = true,
            context = live.Key.Context,
            actorId = live.Key.ActorId,
            animationId = live.Key.AnimationId,
            phase = live.Key.Phase,
            stageId = live.Key.StageId,
            sceneName = live.SceneName,
            normalizedTime = live.NormalizedTime,
            clipLengthSeconds = live.ClipLengthSeconds,
            isLooping = live.IsLooping,
            observedAt = live.ObservedAt,
        };
    }

    /// <summary>
    /// Fillers with their current waveforms and the length EDI has registered for them, so the
    /// editor can load them directly and show when the two disagree.
    /// </summary>
    private object BuildFillersResponse()
    {
        IReadOnlyList<EdiGalleryDefinition> definitions = EdiGalleryDefinitions.Read(_store.DefinitionsPath);
        var fillers = _store.ListFillers().Select(filler =>
        {
            IReadOnlyDictionary<string, FunscriptDocument> variants = _store.LoadFiller(filler.Gallery);
            EdiGalleryDefinition? definition = definitions
                .FirstOrDefault(row => string.Equals(row.FileName, filler.Gallery, StringComparison.Ordinal));
            return new
            {
                gallery = filler.Gallery,
                outputs = filler.Outputs,
                role = filler.Role,
                statusId = filler.StatusId,
                statusDisplayName = filler.StatusDisplayName,
                requiredVariants = _store.VariantsForOutputs(filler.Outputs),
                authoredVariants = variants.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                durationMilliseconds = variants.Count == 0
                    ? 0
                    : variants.Values.Max(script => script.DurationMilliseconds),
                definitionEndTime = definition?.EndTime,
                definitionLoop = definition?.Loop,
                definitionDescription = definition?.Description,
                variants,
            };
        }).ToArray();

        return new { definitionsPath = _store.DefinitionsPath, fillers };
    }

    private async Task HandleSaveFillerAsync(HttpListenerContext context, HttpListenerRequest request)
    {
        SaveFillerPayload? payload = await ReadJsonAsync<SaveFillerPayload>(request).ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Gallery))
        {
            await WriteJsonAsync(context, 400, new { error = "The filler payload could not be parsed." })
                .ConfigureAwait(false);
            return;
        }

        var variants = new Dictionary<string, FunscriptDocument>(StringComparer.Ordinal);
        foreach ((string variant, List<ActionPayload> actions) in payload.Variants ?? new())
        {
            variants[variant] = new FunscriptDocument(
                "1.0",
                false,
                100,
                actions.Select(action => new FunscriptAction(action.Pos, action.At)).ToArray());
        }

        FillerSaveResult result = await _store
            .SaveFillerAsync(payload.Gallery, variants, _lifetime.Token)
            .ConfigureAwait(false);

        if (result.Success)
        {
            _logInfo?.Invoke(
                $"Filler '{result.Gallery}' saved ({result.DurationMilliseconds} ms"
                + (result.DefinitionUpdated ? ", Definitions.csv updated" : ", no definition row") + ").");
        }
        else
        {
            _logWarning?.Invoke($"Filler save rejected for '{result.Gallery}': {string.Join("; ", result.Errors)}");
        }

        await WriteJsonAsync(context, result.Success ? 200 : 409, result).ConfigureAwait(false);
    }

    private async Task HandleLoadScriptAsync(HttpListenerContext context, HttpListenerRequest request)
    {
        EventKey key = ReadKeyFromQuery(request);
        IReadOnlyDictionary<string, FunscriptDocument> variants = _store.LoadExisting(key);
        await WriteJsonAsync(context, 200, new
        {
            gallery = Funscript.CreateGalleryName(key),
            variants,
        }).ConfigureAwait(false);
    }

    private async Task HandleSaveAsync(HttpListenerContext context, HttpListenerRequest request)
    {
        SavePayload? payload = await ReadJsonAsync<SavePayload>(request).ConfigureAwait(false);
        if (payload is null)
        {
            await WriteJsonAsync(context, 400, new { error = "The save payload could not be parsed." })
                .ConfigureAwait(false);
            return;
        }

        var key = new EventKey(
            payload.Context ?? string.Empty,
            payload.ActorId ?? string.Empty,
            payload.AnimationId ?? string.Empty,
            payload.Phase ?? string.Empty,
            string.IsNullOrWhiteSpace(payload.StageId) ? EventKey.DefaultStageId : payload.StageId);

        var variants = new Dictionary<string, FunscriptDocument>(StringComparer.Ordinal);
        foreach ((string variant, List<ActionPayload> actions) in payload.Variants ?? new())
        {
            variants[variant] = new FunscriptDocument(
                "1.0",
                false,
                100,
                actions.Select(action => new FunscriptAction(action.Pos, action.At)).ToArray());
        }

        AuthoringSaveResult result = await _store
            .SaveAsync(
                new AuthoringSaveRequest(
                    key, variants, payload.ApproveLoopMismatch, payload.Repeat, payload.SilentOutputs),
                _lifetime.Token)
            .ConfigureAwait(false);

        if (result.Success)
        {
            _logInfo?.Invoke($"Authored gallery '{result.Gallery}' saved and mapped for {key}.");
        }
        else
        {
            _logWarning?.Invoke(
                $"Authoring save rejected for {key}: {string.Join("; ", result.Errors.Concat(result.LoopWarnings))}");
        }

        await WriteJsonAsync(context, result.Success ? 200 : 409, result).ConfigureAwait(false);
    }

    private async Task HandlePreviewAsync(HttpListenerContext context, HttpListenerRequest request)
    {
        PreviewPayload? payload = await ReadJsonAsync<PreviewPayload>(request).ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Gallery))
        {
            await WriteJsonAsync(context, 400, new { error = "A preview requires a gallery name." })
                .ConfigureAwait(false);
            return;
        }

        try
        {
            IReadOnlyList<string> targets = payload.Outputs ?? new List<string>();
            string? rejection = DescribePreviewRejection(payload.Gallery, targets);
            if (rejection is not null)
            {
                await WriteJsonAsync(context, 400, new { error = rejection }).ConfigureAwait(false);
                return;
            }

            _coordinator.BeginPreview(payload.Gallery, targets);
        }
        catch (ArgumentException exception)
        {
            await WriteJsonAsync(context, 400, new { error = exception.Message }).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, 200, new { previewing = payload.Gallery }).ConfigureAwait(false);
    }

    /// <summary>
    /// Records why an output is suppressed so the GUI can show it next to the output, instead of
    /// the user discovering it as a preview that quietly does nothing (SPEC001 6.7-10).
    /// </summary>
    public void ReportSuppression(string output, string? reason)
    {
        lock (_suppressionReasons)
        {
            if (reason is null)
            {
                _suppressionReasons.Remove(output);
            }
            else
            {
                _suppressionReasons[output] = reason;
            }
        }
    }

    /// <summary>
    /// Why a preview must not run, or null when it may. A gallery may only be auditioned on the
    /// outputs it actually carries a variant for; sending it anywhere else is the misroute the
    /// whole design exists to prevent (SPEC001 6.7-5, FR-049).
    /// </summary>
    private string? DescribePreviewRejection(string gallery, IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 0)
        {
            return "A preview requires at least one output.";
        }

        foreach (string output in outputs)
        {
            if (!_mappings.TryGetOutput(output, out OutputBinding binding))
            {
                return $"'{output}' is not an output in the device roster.";
            }

            string path = Path.Combine(_store.GalleryRoot, binding.EdiVariant, $"{gallery}.funscript");
            if (!File.Exists(path))
            {
                return
                    $"'{gallery}' has no '{binding.EdiVariant}' variant, so it is not content for "
                    + $"{binding.DisplayName}. Author and save that variant first.";
            }
        }

        return null;
    }

    private async Task ServeStaticAsync(HttpListenerContext context, string path)
    {
        string relative = path is "/" or "" ? "index.html" : path.TrimStart('/');
        string fullPath = Path.GetFullPath(Path.Combine(_assetRoot, relative));
        string rootFull = Path.GetFullPath(_assetRoot);

        // Reject any path that escapes the asset root.
        if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            await WriteJsonAsync(context, 404, new { error = "Not found." }).ConfigureAwait(false);
            return;
        }

        byte[] body = await File.ReadAllBytesAsync(fullPath, _lifetime.Token).ConfigureAwait(false);
        context.Response.StatusCode = 200;
        context.Response.ContentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body, _lifetime.Token).ConfigureAwait(false);
    }

    private static EventKey ReadKeyFromQuery(HttpListenerRequest request)
    {
        System.Collections.Specialized.NameValueCollection query = request.QueryString;
        return new EventKey(
            query["context"] ?? string.Empty,
            query["actorId"] ?? string.Empty,
            query["animationId"] ?? string.Empty,
            query["phase"] ?? string.Empty,
            string.IsNullOrWhiteSpace(query["stageId"]) ? EventKey.DefaultStageId : query["stageId"]!);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        string body = await reader.ReadToEndAsync().ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object payload)
    {
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ActionPayload(int Pos, long At);

    private sealed record SavePayload(
        string? Context,
        string? ActorId,
        string? AnimationId,
        string? Phase,
        string? StageId,
        Dictionary<string, List<ActionPayload>>? Variants,
        bool ApproveLoopMismatch,
        bool? Repeat = null,
        List<string>? SilentOutputs = null);

    private sealed record PreviewPayload(string? Gallery, List<string>? Outputs);

    private sealed record SaveFillerPayload(
        string? Gallery,
        Dictionary<string, List<ActionPayload>>? Variants);
}
