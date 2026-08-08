using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class AuthoringServerTests
{
    private static readonly TargetGameBuild Build = new()
    {
        GameAssemblySha256 = TestMappings.Hash,
        GlobalMetadataSha256 = TestMappings.Hash,
    };

    private static readonly EventKey Key = new("gallery", "Enemy", "Take", "loop", "Take_01");

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record Harness(
        AuthoringServer Server,
        HttpClient Client,
        List<PlaybackCommand> Commands,
        MappingRepository Mappings,
        LiveTriggerState Live);

    private static Harness Start(TempDirectory temp, out string assetRoot)
    {
        assetRoot = Path.Combine(temp.Root, "authoring");
        Directory.CreateDirectory(assetRoot);
        File.WriteAllText(Path.Combine(assetRoot, "index.html"), "<h1>editor</h1>");
        File.WriteAllText(Path.Combine(temp.Root, "secret.txt"), "must-not-be-served");

        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        catalog.Register(TriggerCatalogEntry.Create(
            Key, 0.733, true, TriggerSources.Observed, "Take_01", "GalleryScene", DateTimeOffset.UtcNow));
        catalog.Register(TriggerCatalogEntry.Create(
            Key with { StageId = "Take_02" },
            null,
            true,
            TriggerSources.StaticEnumeration,
            "Take_02",
            "GalleryScene",
            DateTimeOffset.UtcNow));

        MappingRepository mappings = TestMappings.Create();
        var store = new AuthoringStore(
            Path.Combine(temp.Root, "Gallery"),
            Path.Combine(temp.Root, "manifests"),
            temp.File("mappings.json"),
            "build",
            mappings,
            catalog,
            _ => Task.CompletedTask);

        var commands = new List<PlaybackCommand>();
        var coordinator = new PlaybackCoordinator(mappings, new CapturingSink(commands), new NullDiagnostics());
        coordinator.SetGameplayActive(Array.Empty<string>());
        commands.Clear();

        var live = new LiveTriggerState();
        string url = $"http://127.0.0.1:{FreePort()}/";
        AuthoringServer? server = AuthoringServer.TryStart(
            url, catalog, mappings, store, coordinator, live, assetRoot, out IReadOnlyList<string> errors);
        Assert.Empty(errors);
        Assert.NotNull(server);

        return new Harness(
            server!,
            new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(10) },
            commands,
            mappings,
            live);
    }

    /// <summary>AC-024/AC-027: the GUI is served over loopback and lists every catalogued stage.</summary>
    [Fact]
    public async Task ServerServesTheGuiAndTheStageCatalog()
    {
        using var temp = new TempDirectory();
        Harness harness = Start(temp, out _);
        try
        {
            HttpResponseMessage index = await harness.Client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Contains("editor", await index.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using JsonDocument catalog = JsonDocument.Parse(await harness.Client.GetStringAsync("/api/catalog"));
            JsonElement triggers = catalog.RootElement.GetProperty("triggers");
            Assert.Equal(2, triggers.GetArrayLength());

            JsonElement observed = triggers[0];
            Assert.Equal("Take_01", observed.GetProperty("stageId").GetString());
            Assert.Equal("unclassified", observed.GetProperty("disposition").GetString());
            Assert.StartsWith("sinisistar2-", observed.GetProperty("gallery").GetString(), StringComparison.Ordinal);

            // The unreached stage is offered for authoring too (FR-032).
            Assert.Equal("static-enumeration", triggers[1].GetProperty("source").GetString());
        }
        finally
        {
            harness.Client.Dispose();
            await harness.Server.DisposeAsync();
        }
    }

    /// <summary>
    /// The GUI identifies a stage by asking the game what it is playing, because this build
    /// exposes no stage number that matches the gallery's own tabs.
    /// </summary>
    [Fact]
    public async Task CurrentEndpointReportsWhatTheGameIsPlaying()
    {
        using var temp = new TempDirectory();
        Harness harness = Start(temp, out _);
        try
        {
            using (JsonDocument idle = JsonDocument.Parse(await harness.Client.GetStringAsync("/api/current")))
            {
                Assert.False(idle.RootElement.GetProperty("playing").GetBoolean());
            }

            harness.Live.Set(new LiveTrigger(Key, "GalleryScene", 0.25, 0.733, true, DateTimeOffset.UtcNow));

            using JsonDocument playing = JsonDocument.Parse(await harness.Client.GetStringAsync("/api/current"));
            Assert.True(playing.RootElement.GetProperty("playing").GetBoolean());
            Assert.Equal("Take_01", playing.RootElement.GetProperty("stageId").GetString());
            Assert.Equal("Enemy", playing.RootElement.GetProperty("actorId").GetString());
            Assert.Equal(0.733, playing.RootElement.GetProperty("clipLengthSeconds").GetDouble(), 3);
        }
        finally
        {
            harness.Client.Dispose();
            await harness.Server.DisposeAsync();
        }
    }

    /// <summary>A stale reading must not keep a row highlighted after the game stops reporting.</summary>
    [Fact]
    public async Task StaleLiveReadingIsNotReportedAsPlaying()
    {
        using var temp = new TempDirectory();
        Harness harness = Start(temp, out _);
        try
        {
            harness.Live.Set(new LiveTrigger(
                Key, "GalleryScene", 0, 1, true, DateTimeOffset.UtcNow.AddMinutes(-1)));

            using JsonDocument body = JsonDocument.Parse(await harness.Client.GetStringAsync("/api/current"));
            Assert.False(body.RootElement.GetProperty("playing").GetBoolean());
        }
        finally
        {
            harness.Client.Dispose();
            await harness.Server.DisposeAsync();
        }
    }

    /// <summary>AC-029: a preview goes through the coordinator with the channel named.</summary>
    [Fact]
    public async Task PreviewGoesThroughTheCoordinatorAndCanBeStopped()
    {
        using var temp = new TempDirectory();
        Harness harness = Start(temp, out _);
        try
        {
            // FR-049: a gallery is only auditionable on the outputs it carries a variant for.
            string variantPath = Path.Combine(
                temp.Root, "Gallery", "a10-main", "preview-gallery.funscript");
            Directory.CreateDirectory(Path.GetDirectoryName(variantPath)!);
            await File.WriteAllTextAsync(variantPath, "{\"actions\":[{\"pos\":0,\"at\":0}]}");

            HttpResponseMessage started = await harness.Client.PostAsync(
                "/api/preview",
                new StringContent(
                    JsonSerializer.Serialize(new { gallery = "preview-gallery", outputs = new[] { "main" } }),
                    Encoding.UTF8,
                    "application/json"));

            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            PlaybackCommand play = Assert.Single(harness.Commands);
            Assert.Equal(PlaybackCommandKind.Play, play.Kind);
            Assert.Equal("preview-gallery", play.Gallery);
            Assert.Equal(TestMappings.Main, play.Output);

            harness.Commands.Clear();
            HttpResponseMessage stopped = await harness.Client.PostAsync("/api/preview/stop", null);
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
            Assert.Contains(harness.Commands, command => command.Gallery == "filler-main");
        }
        finally
        {
            harness.Client.Dispose();
            await harness.Server.DisposeAsync();
        }
    }

    /// <summary>AC-028: saving through the API maps the trigger.</summary>
    [Fact]
    public async Task SaveThroughTheApiMapsTheTrigger()
    {
        using var temp = new TempDirectory();
        Harness harness = Start(temp, out _);
        try
        {
            var payload = new
            {
                context = Key.Context,
                actorId = Key.ActorId,
                animationId = Key.AnimationId,
                phase = Key.Phase,
                stageId = Key.StageId,
                variants = new Dictionary<string, object[]>
                {
                    ["a10-main"] = new object[]
                    {
                        new { pos = 0, at = 0 },
                        new { pos = 100, at = 366 },
                        new { pos = 0, at = 733 },
                    },
                },
                approveLoopMismatch = false,
            };

            HttpResponseMessage response = await harness.Client.PostAsync(
                "/api/save",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
            Assert.True(harness.Mappings.TryResolve(Key, out EventMapping? mapping));
            Assert.Equal("mapped", mapping.Disposition);

            using JsonDocument catalog = JsonDocument.Parse(await harness.Client.GetStringAsync("/api/catalog"));
            JsonElement trigger = catalog.RootElement.GetProperty("triggers")[0];
            Assert.Equal("mapped", trigger.GetProperty("disposition").GetString());
            Assert.Equal("a10-main", trigger.GetProperty("authoredVariants")[0].GetString());
        }
        finally
        {
            harness.Client.Dispose();
            await harness.Server.DisposeAsync();
        }
    }

    /// <summary>
    /// Files outside the asset root are never served. http.sys rejects most traversal forms with
    /// 403 before the handler runs; the handler's own root check covers the rest.
    /// </summary>
    [Theory]
    [InlineData("/..%2Fsecret.txt")]
    [InlineData("/../secret.txt")]
    [InlineData("/nested/../../secret.txt")]
    [InlineData("/secret.txt")]
    public async Task StaticServingCannotEscapeTheAssetRoot(string path)
    {
        using var temp = new TempDirectory();
        Harness harness = Start(temp, out _);
        try
        {
            HttpResponseMessage response = await harness.Client.GetAsync(path);

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
                    or HttpStatusCode.BadRequest,
                $"'{path}' returned {(int)response.StatusCode}; it must be refused.");
            Assert.DoesNotContain(
                "must-not-be-served",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
        finally
        {
            harness.Client.Dispose();
            await harness.Server.DisposeAsync();
        }
    }

    private sealed class CapturingSink : IPlaybackCommandSink
    {
        private readonly List<PlaybackCommand> _commands;
        public CapturingSink(List<PlaybackCommand> commands) => _commands = commands;
        public void Publish(IReadOnlyList<PlaybackCommand> commands) => _commands.AddRange(commands);
        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullDiagnostics : IEventDiagnostics
    {
        public bool RecordEvent(EventObservation observation) => false;
        public void RegisterStatus(string statusId, string displayName) { }
        public Task WriteCoverageAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
