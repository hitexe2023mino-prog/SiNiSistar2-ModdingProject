using System.Net;
using System.Text;
using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class EdiHttpClientTests
{
    [Fact]
    public void RemoteEndpointIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new EdiHttpClient(new Uri("http://example.com:5000")));
    }

    [Fact]
    public async Task PlayUsesPostAndEncodesGalleryChannelAndSeek()
    {
        var handler = new RecordingHandler("[]");
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        await client.ExecuteAsync(
            new PlaybackRequest(
                PlaybackCommandKind.Play, new[] { "main channel" }, "enemy event/loop", 1234),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "http://127.0.0.1:5000/Edi/Play/enemy%20event%2Floop?seek=1234&channels=main%20channel",
            request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ChannelDiscoveryUsesExpectedEndpoint()
    {
        var handler = new RecordingHandler("[\"main\",\"breast\"]");
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://localhost:5000/"), httpClient);

        IReadOnlyList<string> channels = await client.GetChannelsAsync(CancellationToken.None);

        Assert.Equal(new[] { "main", "breast" }, channels);
        Assert.Equal("http://localhost:5000/Edi/Channels", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// A re-scan makes EDI read its whole gallery folder, so it must not share the tight bound
    /// that keeps playback commands off the game thread.
    /// </summary>
    [Fact]
    public void ReloadIsAllowedMoreTimeThanAPlaybackCommand()
    {
        Assert.True(
            EdiHttpClient.ReloadTimeout > EdiHttpClient.CommandTimeout,
            $"reload {EdiHttpClient.ReloadTimeout} must exceed command {EdiHttpClient.CommandTimeout}");
        Assert.Equal(TimeSpan.FromSeconds(2), EdiHttpClient.CommandTimeout);
    }

    /// <summary>A slow EDI must not stall a playback command past its bound.</summary>
    [Fact]
    public async Task PlaybackCommandGivesUpAtTheCommandTimeout()
    {
        using var httpClient = new HttpClient(new StallingHandler())
        {
            Timeout = EdiHttpClient.ReloadTimeout,
        };
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        var started = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ExecuteAsync(
                new PlaybackRequest(PlaybackCommandKind.Stop, new[] { TestMappings.Main }),
                CancellationToken.None));
        started.Stop();

        Assert.True(
            started.Elapsed < EdiHttpClient.ReloadTimeout,
            $"gave up after {started.Elapsed}, which should be near {EdiHttpClient.CommandTimeout}");
    }

    private sealed class StallingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    /// <summary>
    /// The MOD writes gallery files itself, so all EDI needs is a re-scan. Uploading would move
    /// EDI's gallery root to its upload folder and lose the definition table (SPEC001 7.4 E3).
    /// </summary>
    [Fact]
    public async Task ReloadPostsToTheRescanEndpointWithoutTransferringFiles()
    {
        var handler = new RecordingHandler("[]");
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        await client.ReloadAsync(CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://127.0.0.1:5000/Edi/Reload", request.RequestUri!.AbsoluteUri);
        Assert.Equal(string.Empty, handler.RequestBody);
    }

    /// <summary>Grouped outputs travel as one comma-separated channels value (FR-046).</summary>
    [Fact]
    public async Task GroupedOutputsAreSentAsOneCommaSeparatedRequest()
    {
        var handler = new RecordingHandler("[]");
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        await client.ExecuteAsync(
            new PlaybackRequest(
                PlaybackCommandKind.Play, new[] { "breast-left", "breast-right" }, "filler-breast"),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(
            "http://127.0.0.1:5000/Edi/Play/filler-breast?seek=0&channels=breast-left%2Cbreast-right",
            request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task DeviceQueryReadsNameChannelVariantAndReadiness()
    {
        var handler = new RecordingHandler(
            "[{\"name\":\"Vorze Piston\",\"channel\":\"main\",\"selectedVariant\":\"a10-main\",\"isReady\":true}]");
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        IReadOnlyList<EdiDevice> devices = await client.GetDevicesAsync(CancellationToken.None);

        EdiDevice device = Assert.Single(devices);
        Assert.Equal("Vorze Piston", device.Name);
        Assert.Equal("main", device.Channel);
        Assert.Equal("a10-main", device.SelectedVariant);
        Assert.True(device.IsReady);
        Assert.Equal("http://127.0.0.1:5000/Devices", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    /// <summary>AC-049: an EDI without the endpoint answers 404, which is a verdict, not an outage.</summary>
    [Fact]
    public async Task MissingInfoEndpointIsReportedAsNoCapabilitiesRatherThanThrowing()
    {
        var handler = new RecordingHandler("not found", HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        Assert.Null(await client.GetInfoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InfoReportsTheEffectiveCapabilities()
    {
        var handler = new RecordingHandler(
            "{\"version\":\"1.0.2\",\"strictVariantResolution\":true,\"stopClearsFiller\":true,"
            + "\"unassignedDeviceChannel\":\"unassigned\"}");
        using var httpClient = new HttpClient(handler);
        using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

        EdiCapabilities? capabilities = await client.GetInfoAsync(CancellationToken.None);

        Assert.NotNull(capabilities);
        Assert.True(capabilities!.StrictVariantResolution);
        Assert.True(capabilities.StopClearsFiller);
        Assert.Equal("unassigned", capabilities.UnassignedDeviceChannel);
        Assert.Equal("http://127.0.0.1:5000/Edi/Info", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _status;

        public RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        public List<HttpRequestMessage> Requests { get; } = new();
        public string RequestBody { get; private set; } = string.Empty;
        public string ContentType { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
