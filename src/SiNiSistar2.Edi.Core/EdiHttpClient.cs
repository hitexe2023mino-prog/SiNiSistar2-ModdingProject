using System.Net.Http.Json;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

public interface IEdiClient
{
    Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(PlaybackCommand command, CancellationToken cancellationToken);
}

public sealed class EdiHttpClient : IEdiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public EdiHttpClient(Uri baseUri, HttpClient? httpClient = null)
    {
        ValidateBaseUri(baseUri);
        BaseUri = EnsureTrailingSlash(baseUri);
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2),
        };
        _ownsClient = httpClient is null;
    }

    public Uri BaseUri { get; }

    public async Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(new Uri(BaseUri, "Edi/Channels"), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<string[]>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? Array.Empty<string>();
    }

    public async Task ExecuteAsync(PlaybackCommand command, CancellationToken cancellationToken)
    {
        var relative = command.Kind switch
        {
            PlaybackCommandKind.Play =>
                $"Edi/Play/{Uri.EscapeDataString(command.Gallery!)}?seek={command.SeekMilliseconds}"
                + $"&channels={Uri.EscapeDataString(command.Channel)}",
            PlaybackCommandKind.Stop =>
                $"Edi/Stop?channels={Uri.EscapeDataString(command.Channel)}",
            PlaybackCommandKind.Pause =>
                $"Edi/Pause?untilResume=true&channels={Uri.EscapeDataString(command.Channel)}",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, relative));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadAssetsAsync(
        IReadOnlyList<GeneratedUploadAsset> assets,
        CancellationToken cancellationToken)
    {
        if (assets.Count == 0)
        {
            return;
        }

        using var content = new MultipartFormDataContent();
        var streams = new List<FileStream>(assets.Count);
        try
        {
            foreach (GeneratedUploadAsset asset in assets)
            {
                var stream = new FileStream(asset.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                streams.Add(stream);
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                content.Add(fileContent, "files", asset.FileName);
            }

            using var response = await _httpClient
                .PostAsync(new Uri(BaseUri, "Edi/Assets"), content, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            foreach (FileStream stream in streams)
            {
                stream.Dispose();
            }
        }
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
