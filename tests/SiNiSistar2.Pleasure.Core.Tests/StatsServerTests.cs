using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The statistics page (SPEC006 4.5, FR-606〜613).
///
/// It has no authentication, so what it refuses matters more than what it serves: a non-loopback
/// address, a write of any kind, and any route nobody asked for.
/// </summary>
public sealed class StatsServerTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static StatsSnapshot Sample()
    {
        var actors = new ActorClimaxLedger();
        actors.Record("Worm");
        actors.Record("Worm");
        actors.Record(null);

        var debuffs = new DebuffCounters();
        debuffs.Record("Breast");

        return StatsSnapshot.Build(
            42.5f,
            100f,
            3,
            12,
            actors,
            debuffs,
            id => id == "Worm" ? "大口のワーム" : null,
            type => type == "Breast" ? "膨乳" : null,
            At);
    }

    private static async Task WithServer(Func<HttpClient, Uri, Task> body, Func<StatsSnapshot>? read = null)
    {
        string url = $"http://127.0.0.1:{FreePort()}/";
        StatsServer? server = StatsServer.TryStart(
            url, read ?? Sample, out IReadOnlyList<string> errors);

        Assert.Empty(errors);
        Assert.NotNull(server);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            await body(client, server!.BaseUri);
        }
        finally
        {
            await server!.DisposeAsync();
        }
    }

    /// <summary>AC-606: an address other hosts could reach is refused before a socket is opened.</summary>
    [Theory]
    [InlineData("http://0.0.0.0:5602/")]
    [InlineData("http://192.168.1.10:5602/")]
    [InlineData("http://example.com:5602/")]
    [InlineData("http://+:5602/")]
    public void ANonLoopbackAddressIsRefused(string url)
    {
        IReadOnlyList<string> errors = StatsServer.ValidateBaseUrl(url, out Uri? uri);

        Assert.NotEmpty(errors);
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5602/")]
    [InlineData("http://localhost:5602/")]
    public void ALoopbackAddressIsAccepted(string url)
    {
        Assert.Empty(StatsServer.ValidateBaseUrl(url, out Uri? uri));
        Assert.NotNull(uri);
    }

    /// <summary>https and credentials are both refused: the page is plain local http and nothing else.</summary>
    [Theory]
    [InlineData("https://127.0.0.1:5602/")]
    [InlineData("http://user:pass@127.0.0.1:5602/")]
    [InlineData("not-a-url")]
    public void OnlyPlainLoopbackHttpIsAccepted(string url)
    {
        Assert.NotEmpty(StatsServer.ValidateBaseUrl(url, out _));
    }

    /// <summary>A missing trailing slash is a prefix HttpListener will not take, so it is added.</summary>
    [Fact]
    public void AMissingTrailingSlashIsSuppliedRatherThanRejected()
    {
        Assert.Empty(StatsServer.ValidateBaseUrl("http://127.0.0.1:5602", out Uri? uri));
        Assert.Equal("http://127.0.0.1:5602/", uri!.AbsoluteUri);
    }

    /// <summary>AC-606, AC-611: the invalid address never becomes a running server.</summary>
    [Fact]
    public void AnInvalidAddressStartsNothing()
    {
        StatsServer? server = StatsServer.TryStart(
            "http://192.168.1.10:5602/", Sample, out IReadOnlyList<string> errors);

        Assert.Null(server);
        Assert.NotEmpty(errors);
    }

    /// <summary>AC-607: the reading carries every figure the page shows.</summary>
    [Fact]
    public async Task TheApiServesTheWholeReading()
    {
        await WithServer(async (client, baseUri) =>
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(baseUri, "api/stats"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;

            Assert.Equal(42.5, root.GetProperty("corruption").GetProperty("value").GetDouble(), 3);
            Assert.Equal(100, root.GetProperty("corruption").GetProperty("cap").GetDouble(), 3);
            Assert.Equal(3, root.GetProperty("climax").GetProperty("count").GetInt32());
            Assert.Equal(12, root.GetProperty("climax").GetProperty("limit").GetInt32());

            JsonElement top = root.GetProperty("topActor");
            Assert.Equal("Worm", top.GetProperty("actorId").GetString());
            Assert.Equal("大口のワーム", top.GetProperty("displayName").GetString());
            Assert.Equal(2, top.GetProperty("count").GetInt32());

            JsonElement debuffs = root.GetProperty("debuffCounts");
            Assert.Equal("Breast", debuffs[0].GetProperty("abnormalType").GetString());
            Assert.Equal("膨乳", debuffs[0].GetProperty("displayName").GetString());

            // The unknown bucket is served so the totals add up, with no name attached to it.
            JsonElement actors = root.GetProperty("actorClimaxCounts");
            Assert.Equal(2, actors.GetArrayLength());
            Assert.Equal(
                ActorClimaxLedger.UnknownActorId,
                actors[1].GetProperty("actorId").GetString());
            Assert.Equal(JsonValueKind.Null, actors[1].GetProperty("displayName").ValueKind);

            Assert.Equal("2026-08-10T12:34:56Z", root.GetProperty("generatedAt").GetString());
        });
    }

    /// <summary>A poll must not be answered from a browser cache, or the diary would freeze.</summary>
    [Fact]
    public async Task TheApiIsNotCacheable()
    {
        await WithServer(async (client, baseUri) =>
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(baseUri, "api/stats"));
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        });
    }

    /// <summary>AC-607: the page itself is served, in Japanese, with its headings in place.</summary>
    [Fact]
    public async Task ThePageIsServedWithItsHeadings()
    {
        await WithServer(async (client, baseUri) =>
        {
            using HttpResponseMessage response = await client.GetAsync(baseUri);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

            string html = await response.Content.ReadAsStringAsync();
            Assert.Contains("<html lang=\"ja\">", html);
            Assert.Contains("堕落", html);
            Assert.Contains("絶頂回数", html);
            Assert.Contains("最も辱めた者", html);
            Assert.Contains("受けた呪いの記録", html);
            Assert.Contains("まだ絶頂させた敵がいません", html);
        });
    }

    /// <summary>
    /// AC-611: nothing on the page is fetched from anywhere. The MOD runs beside an offline game,
    /// and a stylesheet or font from a CDN would leave the diary blank exactly when it was wanted.
    /// </summary>
    [Fact]
    public async Task ThePageReachesNoExternalHost()
    {
        await WithServer(async (client, baseUri) =>
        {
            string html = await client.GetStringAsync(baseUri);

            Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", string.Empty));
            Assert.DoesNotContain("https://", html);
            Assert.DoesNotContain("//fonts.", html);
            Assert.DoesNotContain("<link", html);
            Assert.DoesNotContain("src=", html);
            Assert.DoesNotContain("@import", html);
        });
    }

    /// <summary>AC-609: the page polls on its own rather than waiting to be reloaded.</summary>
    [Fact]
    public async Task ThePagePollsForItself()
    {
        await WithServer(async (client, baseUri) =>
        {
            string html = await client.GetStringAsync(baseUri);

            Assert.Contains("setInterval(poll", html);
            Assert.Contains("fetch('/api/stats'", html);
        });
    }

    /// <summary>
    /// AC-608: the page is read-only. Every method that could change something is refused before
    /// the path is even looked at, so no route can grow a write by accident.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task WritesAreRefused(string method)
    {
        await WithServer(async (client, baseUri) =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(baseUri, "api/stats"));
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        });
    }

    /// <summary>Nothing but the two documented routes exists.</summary>
    [Theory]
    [InlineData("api/save")]
    [InlineData("api/reset")]
    [InlineData("../secret.txt")]
    public async Task UnknownRoutesAreNotFound(string path)
    {
        await WithServer(async (client, baseUri) =>
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(baseUri, path));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        });
    }

    /// <summary>
    /// A poll arriving before the game has built a reading is ordinary, not an error: the page shows
    /// an empty diary rather than a failure (SPEC006 7章).
    /// </summary>
    [Fact]
    public async Task AReadingThatIsNotReadyYetServesAnEmptyDiary()
    {
        await WithServer(
            async (client, baseUri) =>
            {
                using HttpResponseMessage response = await client.GetAsync(new Uri(baseUri, "api/stats"));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(
                    JsonValueKind.Null,
                    document.RootElement.GetProperty("topActor").ValueKind);
                Assert.Equal(0, document.RootElement.GetProperty("climax").GetProperty("count").GetInt32());
            },
            read: () => throw new InvalidOperationException("the game is not ready"));
    }
}
