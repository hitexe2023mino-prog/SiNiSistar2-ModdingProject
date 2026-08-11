using System.Net;
using System.Text;
using System.Text.Json;

namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// Serves the play-statistics page over loopback only (SPEC006 FR-606, 8章).
///
/// The page has no authentication, so the loopback restriction is the only thing keeping it off the
/// network and a non-loopback address is a configuration error rather than a preference. This is the
/// same trust model and the same validation SPEC001's authoring GUI uses (SPEC001 FR-035); the code
/// is written out again rather than shared, because the two MODs must not reference one another
/// (SPEC006 2.3).
///
/// Read-only by construction: there is no route that accepts a body and none that writes anything.
/// A viewer cannot reach the game's state through it (FR-608).
/// </summary>
public sealed class StatsServer : IAsyncDisposable
{
    private static readonly IReadOnlySet<string> LoopbackHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1", "localhost", "::1", "[::1]",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<StatsSnapshot> _read;
    private readonly Action<string>? _logWarning;
    private Task? _worker;

    private StatsServer(Uri baseUri, Func<StatsSnapshot> read, Action<string>? logWarning)
    {
        BaseUri = baseUri;
        _read = read;
        _logWarning = logWarning;
        _listener.Prefixes.Add(baseUri.AbsoluteUri);
    }

    public Uri BaseUri { get; }

    /// <summary>
    /// Checks the configured address before a socket is opened, so a bad one is a startup message
    /// rather than a page quietly reachable from the network.
    /// </summary>
    public static IReadOnlyList<string> ValidateBaseUrl(string value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return new[] { $"The statistics page URL '{value}' is not an absolute URL." };
        }

        if (parsed.Scheme != Uri.UriSchemeHttp)
        {
            return new[] { $"The statistics page URL '{value}' must use http." };
        }

        if (!LoopbackHosts.Contains(parsed.Host) && !parsed.IsLoopback)
        {
            return new[]
            {
                $"The statistics page URL '{value}' must bind to loopback; the page has no "
                + "authentication and must not be reachable from other hosts.",
            };
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return new[] { $"The statistics page URL '{value}' must not contain user information." };
        }

        uri = parsed.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? parsed
            : new Uri(parsed.AbsoluteUri + "/");
        return Array.Empty<string>();
    }

    /// <summary>
    /// Starts the page, or returns null with the reason it could not start (FR-611).
    /// </summary>
    /// <param name="read">
    /// Hands back the latest reading. Called from a request thread, so it must return a snapshot the
    /// game has already built on its own thread and must never touch a Unity object itself: reading
    /// one from a worker is what takes the game down with the page (SPEC001 4.3).
    /// </param>
    public static StatsServer? TryStart(
        string baseUrl,
        Func<StatsSnapshot> read,
        out IReadOnlyList<string> errors,
        Action<string>? logInfo = null,
        Action<string>? logWarning = null)
    {
        errors = ValidateBaseUrl(baseUrl, out Uri? uri);
        if (errors.Count > 0)
        {
            return null;
        }

        var server = new StatsServer(uri!, read, logWarning);
        try
        {
            server._listener.Start();
        }
        catch (Exception exception) when (
            exception is HttpListenerException or ObjectDisposedException or PlatformNotSupportedException)
        {
            errors = new[]
            {
                $"The statistics page could not listen on '{uri}': {exception.Message}. "
                + "Play continues without it.",
            };
            server._listener.Close();
            return null;
        }

        server._worker = Task.Run(server.AcceptLoopAsync);
        logInfo?.Invoke($"Play statistics available at {uri}");
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
                // Best effort: shutting down must not hold the game up.
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
            _logWarning?.Invoke($"A statistics request failed: {exception.Message}");
            try
            {
                await WriteJsonAsync(context, 500, new { error = exception.Message }).ConfigureAwait(false);
            }
            catch (Exception inner) when (
                inner is HttpListenerException or ObjectDisposedException or IOException)
            {
                // The browser went away.
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception exception) when (
                exception is ObjectDisposedException or InvalidOperationException)
            {
                // Already closed.
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;

        // Defence in depth. The prefix already restricts what can be bound, but a request that did
        // not come from this machine is refused on its own merits as well.
        if (request.RemoteEndPoint is not null && !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        {
            await WriteJsonAsync(context, 403, new { error = "Only loopback clients are served." })
                .ConfigureAwait(false);
            return;
        }

        string path = request.Url?.AbsolutePath ?? "/";

        // Everything here reads. Anything that is not a GET is refused before the path is looked at,
        // so no method can ever be added to a route by accident (FR-608). HEAD is refused with the
        // rest rather than special-cased: nothing needs it, and answering it correctly means
        // suppressing a body this code would otherwise have written.
        if (!string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            context.Response.AddHeader("Allow", "GET");
            await WriteJsonAsync(context, 405, new { error = "The statistics page is read-only." })
                .ConfigureAwait(false);
            return;
        }

        switch (path)
        {
            case "/":
            case "/index.html":
                await WriteHtmlAsync(context, StatsPage.Html).ConfigureAwait(false);
                return;

            case "/api/stats":
                await WriteJsonAsync(context, 200, ReadSafely()).ConfigureAwait(false);
                return;

            default:
                await WriteJsonAsync(context, 404, new { error = "Not found." }).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Reads the latest snapshot, standing in an empty one if the game could not supply it. A poll
    /// arriving before the first frame, or during a teardown, is ordinary rather than an error.
    /// </summary>
    private StatsSnapshot ReadSafely()
    {
        try
        {
            return _read() ?? StatsSnapshot.Empty(DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            _logWarning?.Invoke($"The statistics reading could not be built: {exception.Message}");
            return StatsSnapshot.Empty(DateTimeOffset.UtcNow);
        }
    }

    private static async Task WriteHtmlAsync(HttpListenerContext context, string html)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object payload)
    {
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        // The page polls this every few seconds; a cached reply would show a stale count for as long
        // as the browser felt like keeping it.
        context.Response.AddHeader("Cache-Control", "no-store");
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
    }
}
