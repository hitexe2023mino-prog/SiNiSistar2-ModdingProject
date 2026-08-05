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
            PlaybackCommand.Play("enemy event/loop", 1234, "main channel"),
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

    [Fact]
    public async Task GeneratedVariantIsUploadedThroughAssetsApi()
    {
        string path = Path.Combine(Path.GetTempPath(), $"edi-upload-{Guid.NewGuid():N}.funscript");
        await File.WriteAllTextAsync(path, "{\"actions\":[{\"pos\":50,\"at\":0}]}");
        try
        {
            var handler = new RecordingHandler("[]");
            using var httpClient = new HttpClient(handler);
            using var client = new EdiHttpClient(new Uri("http://127.0.0.1:5000"), httpClient);

            await client.UploadAssetsAsync(
                new[] { new GeneratedUploadAsset("measured.a10-main.funscript", path) },
                CancellationToken.None);

            HttpRequestMessage request = Assert.Single(handler.Requests);
            Assert.Equal("http://127.0.0.1:5000/Edi/Assets", request.RequestUri!.AbsoluteUri);
            Assert.Contains("multipart/form-data", handler.ContentType);
            Assert.Contains("measured.a10-main.funscript", handler.RequestBody);
            Assert.Contains("\"pos\":50", handler.RequestBody);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public RecordingHandler(string response) => _response = response;
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
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
