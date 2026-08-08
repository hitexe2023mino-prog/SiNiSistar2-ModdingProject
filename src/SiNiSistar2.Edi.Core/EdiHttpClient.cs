using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

/// <summary>One EDI request. Several outputs share it when they want the same payload (FR-046).</summary>
public sealed record PlaybackRequest(
    PlaybackCommandKind Kind,
    IReadOnlyList<string> Outputs,
    string? Gallery = null,
    long SeekMilliseconds = 0,
    bool UntilResume = false)
{
    public static PlaybackRequest From(PlaybackCommand command, IReadOnlyList<string> outputs) =>
        new(command.Kind, outputs, command.Gallery, command.SeekMilliseconds, command.UntilResume);
}

/// <summary>A device as EDI reports it from <c>GET /Devices</c> (SPEC001 7.1).</summary>
public sealed record EdiDevice(
    string Name,
    string? Channel,
    string? SelectedVariant,
    bool IsReady);

/// <summary>
/// What <c>GET /Edi/Info</c> reports. The MOD depends on behaviour that is switchable in EDI, so
/// it verifies the effective values instead of guessing from a version number (SPEC001 7.4.3).
/// </summary>
public sealed record EdiCapabilities(
    string? Version,
    bool StrictVariantResolution,
    bool StopClearsFiller,
    string? UnassignedDeviceChannel);

public interface IEdiClient
{
    Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EdiDevice>> GetDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Returns null when EDI has no <c>/Edi/Info</c>, which means 7.4 was not applied.</summary>
    Task<EdiCapabilities?> GetInfoAsync(CancellationToken cancellationToken);

    /// <summary>Asks EDI to re-scan its configured gallery root. No files are transferred.</summary>
    Task ReloadAsync(CancellationToken cancellationToken);

    Task ExecuteAsync(PlaybackRequest request, CancellationToken cancellationToken);
}

public sealed class EdiHttpClient : IEdiClient, IDisposable
{
    /// <summary>
    /// Playback and channel queries are bounded tightly: they sit in front of the desired-state
    /// worker and must never hold it up (SPEC001 10.1).
    /// </summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Re-scanning makes EDI read its whole gallery folder, which routinely takes longer than a
    /// playback command. Sharing the 2 second bound failed every re-scan with a timeout and left
    /// newly authored galleries unknown to EDI. This runs from the authoring server and from
    /// startup, not from the game hook, so a longer wait costs nothing on the game side.
    /// </summary>
    public static readonly TimeSpan ReloadTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public EdiHttpClient(Uri baseUri, HttpClient? httpClient = null)
    {
        ValidateBaseUri(baseUri);
        BaseUri = EnsureTrailingSlash(baseUri);

        // The client's own timeout covers the longest operation; short operations get their bound
        // from a linked token instead, because HttpClient.Timeout cannot vary per request.
        _httpClient = httpClient ?? new HttpClient { Timeout = ReloadTimeout };
        _ownsClient = httpClient is null;
    }

    public Uri BaseUri { get; }

    public async Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource bounded = Bound(cancellationToken, CommandTimeout);
        using var response = await _httpClient
            .GetAsync(new Uri(BaseUri, "Edi/Channels"), bounded.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<string[]>(JsonOptions, bounded.Token)
            .ConfigureAwait(false)
            ?? Array.Empty<string>();
    }

    public async Task<IReadOnlyList<EdiDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource bounded = Bound(cancellationToken, CommandTimeout);
        using var response = await _httpClient
            .GetAsync(new Uri(BaseUri, "Devices"), bounded.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<EdiDevice[]>(JsonOptions, bounded.Token)
            .ConfigureAwait(false)
            ?? Array.Empty<EdiDevice>();
    }

    public async Task<EdiCapabilities?> GetInfoAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource bounded = Bound(cancellationToken, CommandTimeout);
        using var response = await _httpClient
            .GetAsync(new Uri(BaseUri, "Edi/Info"), bounded.Token)
            .ConfigureAwait(false);

        // A 404 is an answer, not an outage: this EDI predates SPEC001 7.4 (AC-049).
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<EdiCapabilities>(JsonOptions, bounded.Token)
            .ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource bounded = Bound(cancellationToken, ReloadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "Edi/Reload"));
        using var response = await _httpClient.SendAsync(request, bounded.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ExecuteAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        if (request.Outputs.Count == 0)
        {
            return;
        }

        // EDI splits `channels` on commas and applies the command to those channels in parallel,
        // so one request is what makes several devices start together (SPEC001 4.2).
        string outputs = Uri.EscapeDataString(string.Join(",", request.Outputs));
        var relative = request.Kind switch
        {
            PlaybackCommandKind.Play =>
                $"Edi/Play/{Uri.EscapeDataString(request.Gallery!)}?seek={request.SeekMilliseconds}"
                + $"&channels={outputs}",
            PlaybackCommandKind.Stop =>
                $"Edi/Stop?channels={outputs}",
            PlaybackCommandKind.Pause =>
                $"Edi/Pause?untilResume=true&channels={outputs}",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        using CancellationTokenSource bounded = Bound(cancellationToken, CommandTimeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, relative));
        using var response = await _httpClient.SendAsync(message, bounded.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static CancellationTokenSource Bound(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static void ValidateBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri
            || baseUri.Scheme != Uri.UriSchemeHttp
            || !baseUri.IsLoopback
            || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new ArgumentException(
                "EDI Base URL must be an absolute loopback HTTP URL without user information.",
                nameof(baseUri));
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
