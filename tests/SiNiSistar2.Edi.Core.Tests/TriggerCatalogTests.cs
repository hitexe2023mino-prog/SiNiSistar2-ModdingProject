using System.Text.Json;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class TriggerCatalogTests
{
    private static readonly TargetGameBuild Build = new()
    {
        GameAssemblySha256 = TestMappings.Hash,
        GlobalMetadataSha256 = TestMappings.Hash,
    };

    private static TriggerCatalogEntry Entry(
        string stageId,
        string source,
        double? clipLength = null,
        bool? isLooping = null,
        string actor = "Enemy") =>
        TriggerCatalogEntry.Create(
            new EventKey("gallery", actor, "Take", "loop", stageId),
            clipLength,
            isLooping,
            source,
            stageId,
            "GalleryScene",
            DateTimeOffset.UtcNow);

    /// <summary>AC-024: a stage array is catalogued before any stage is reached.</summary>
    [Fact]
    public void EnumeratedStagesAreCataloguedBeforeTheyAreReached()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);

        IReadOnlyList<TriggerCatalogEntry> added = catalog.RegisterEnumerated(new[]
        {
            Entry("Take_00", TriggerSources.StaticEnumeration, isLooping: true),
            Entry("Take_01", TriggerSources.StaticEnumeration, isLooping: true),
            Entry("Take_02", TriggerSources.StaticEnumeration, isLooping: false),
        });

        Assert.Equal(3, added.Count);
        Assert.Equal(3, catalog.Count);
        Assert.All(catalog.Snapshot(), entry => Assert.Equal(TriggerSources.StaticEnumeration, entry.Source));
        Assert.Equal(
            new[] { "Take_00", "Take_01", "Take_02" },
            catalog.Snapshot().Select(entry => entry.StageId).ToArray());
    }

    /// <summary>
    /// A stage array cannot know which clips a take will queue, so it registers a placeholder.
    /// The first observation must retire it instead of leaving a permanent ghost row.
    /// </summary>
    [Fact]
    public void ObservingAStageRetiresItsUnplayedPlaceholder()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        var placeholder = new EventKey(
            "gallery", "VillagerRegion", EventKey.UnobservedAnimationId, "reaction", "VillagerRegion_HoldDown");
        catalog.Register(TriggerCatalogEntry.Create(
            placeholder, null, false, TriggerSources.StaticEnumeration, "VillagerRegion_HoldDown", "Ga", DateTimeOffset.UtcNow));
        Assert.Equal(1, catalog.Count);

        // The observed phase differs from the one the stage array implied, and one take queues
        // two clips; both real triggers must survive and the placeholder must not.
        foreach ((string clip, string phase, double length, bool loop) in new[]
                 {
                     ("Hold_Down", "reaction", 0.9, false),
                     ("Hold_Down_Loop", "loop", 1.6, true),
                 })
        {
            catalog.Register(TriggerCatalogEntry.Create(
                new EventKey("gallery", "VillagerRegion", clip, phase, "VillagerRegion_HoldDown"),
                length,
                loop,
                TriggerSources.Observed,
                "VillagerRegion_HoldDown",
                "Ga",
                DateTimeOffset.UtcNow));
        }

        Assert.Equal(2, catalog.Count);
        Assert.DoesNotContain(catalog.Snapshot(), entry => entry.Key.IsUnobservedPlaceholder);
        Assert.Equal(
            new[] { "Hold_Down", "Hold_Down_Loop" },
            catalog.Snapshot().Select(entry => entry.AnimationId).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Only the stage array knows the game's own names, so retiring a placeholder must hand them
    /// to the observed trigger rather than drop them.
    /// </summary>
    [Fact]
    public void RetiredPlaceholderHandsItsDisplayNamesToTheObservedTrigger()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        catalog.Register(TriggerCatalogEntry.Create(
            new EventKey("gallery", "Region", EventKey.UnobservedAnimationId, "reaction", "VillagerRegion_Hold"),
            null,
            null,
            TriggerSources.StaticEnumeration,
            "拘束",
            null,
            DateTimeOffset.UtcNow,
            "構造の落とし子"));

        catalog.Register(TriggerCatalogEntry.Create(
            new EventKey("gallery", "Region", "Hold", "reaction", "VillagerRegion_Hold"),
            0.4,
            false,
            TriggerSources.Observed,
            null,
            "Ga",
            DateTimeOffset.UtcNow));

        TriggerCatalogEntry observed = Assert.Single(catalog.Snapshot());
        Assert.Equal("Hold", observed.AnimationId);
        Assert.Equal("拘束", observed.DisplayName);
        Assert.Equal("構造の落とし子", observed.ActorDisplayName);
        Assert.Equal(0.4, observed.ClipLengthSeconds);
    }

    /// <summary>
    /// A take that queues an intro clip and a loop clip yields two triggers, but only the first
    /// consumes the placeholder. Both must still carry the game's names.
    /// </summary>
    [Fact]
    public void EveryClipOfAMultiClipStageKeepsTheStageAndActorNames()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        catalog.Register(TriggerCatalogEntry.Create(
            new EventKey("gallery", "Region", EventKey.UnobservedAnimationId, "reaction", "VillagerRegion_HoldDown"),
            null, null, TriggerSources.StaticEnumeration, "押し倒し", null, DateTimeOffset.UtcNow, "構造の落とし子"));

        foreach ((string clip, string phase, double length, bool loop) in new[]
                 {
                     ("Hold_Down", "reaction", 0.9, false),
                     ("Hold_Down_Loop", "loop", 1.6, true),
                 })
        {
            catalog.Register(TriggerCatalogEntry.Create(
                new EventKey("gallery", "Region", clip, phase, "VillagerRegion_HoldDown"),
                length, loop, TriggerSources.Observed, null, "Ga", DateTimeOffset.UtcNow));
        }

        Assert.Equal(2, catalog.Count);
        Assert.All(catalog.Snapshot(), entry =>
        {
            Assert.Equal("押し倒し", entry.DisplayName);
            Assert.Equal("構造の落とし子", entry.ActorDisplayName);
        });
    }

    /// <summary>
    /// The in-game viewer selects stages by number, so the number must survive placeholder
    /// retirement and be shared by every clip of the stage; rows sort in that order.
    /// </summary>
    [Fact]
    public void StageNumbersSurviveObservationAndDriveTheOrder()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        foreach ((string stage, int number, int index) in new[]
                 {
                     ("Hold", 2, 1),
                     ("GO", 1, 0),
                     ("MagicSuccessHold", 3, 2),
                 })
        {
            catalog.Register(TriggerCatalogEntry.Create(
                new EventKey("gallery", "GaID_OuterOne", EventKey.UnobservedAnimationId, "reaction", stage),
                null, null, TriggerSources.StaticEnumeration, stage, null, DateTimeOffset.UtcNow,
                "外なる者", number, index));
        }

        foreach ((string clip, string phase) in new[] { ("Hold_A", "reaction"), ("Hold_A_Loop", "loop") })
        {
            catalog.Register(TriggerCatalogEntry.Create(
                new EventKey("gallery", "GaID_OuterOne", clip, phase, "Hold"),
                1.0, phase == "loop", TriggerSources.Observed, null, "Ga", DateTimeOffset.UtcNow));
        }

        IReadOnlyList<TriggerCatalogEntry> rows = catalog.Snapshot();
        Assert.Equal(new int?[] { 1, 2, 2, 3 }, rows.Select(r => r.DisplayNumber).ToArray());
        Assert.All(
            rows.Where(r => r.StageId == "Hold"),
            r =>
            {
                Assert.Equal(2, r.DisplayNumber);
                Assert.Equal("外なる者", r.ActorDisplayName);
            });
    }

    [Fact]
    public void DisplayNumberFallsBackToTheArrayPosition()
    {
        TriggerCatalogEntry withNumber = TriggerCatalogEntry.Create(
            new EventKey("gallery", "A", "c", "loop", "s"),
            null, null, TriggerSources.StaticEnumeration, null, null, DateTimeOffset.UtcNow, null, 7, 0);
        TriggerCatalogEntry indexOnly = withNumber with { StageNumber = null, StageIndex = 3 };
        TriggerCatalogEntry neither = withNumber with { StageNumber = null, StageIndex = null };

        Assert.Equal(7, withNumber.DisplayNumber);
        Assert.Equal(4, indexOnly.DisplayNumber);
        Assert.Null(neither.DisplayNumber);
    }

    /// <summary>A different stage of the same actor keeps the actor name but not the stage name.</summary>
    [Fact]
    public void ActorNameSpreadsAcrossStagesButStageNameDoesNot()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        catalog.Register(TriggerCatalogEntry.Create(
            new EventKey("gallery", "Region", "Hold", "reaction", "Stage_A"),
            0.4, false, TriggerSources.Observed, "拘束", "Ga", DateTimeOffset.UtcNow, "構造の落とし子"));

        catalog.Register(TriggerCatalogEntry.Create(
            new EventKey("gallery", "Region", "Other", "loop", "Stage_B"),
            1.0, true, TriggerSources.Observed, null, "Ga", DateTimeOffset.UtcNow));

        TriggerCatalogEntry second = catalog.Snapshot().Single(e => e.StageId == "Stage_B");
        Assert.Equal("構造の落とし子", second.ActorDisplayName);
        Assert.Null(second.DisplayName);
    }

    [Fact]
    public void PlaceholdersForOtherStagesAreKept()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        foreach (string stage in new[] { "Stage_A", "Stage_B" })
        {
            catalog.Register(TriggerCatalogEntry.Create(
                new EventKey("gallery", "Enemy", EventKey.UnobservedAnimationId, "loop", stage),
                null, true, TriggerSources.StaticEnumeration, stage, "Ga", DateTimeOffset.UtcNow));
        }

        catalog.Register(TriggerCatalogEntry.Create(
            new EventKey("gallery", "Enemy", "Clip_A", "loop", "Stage_A"),
            1.0, true, TriggerSources.Observed, "Stage_A", "Ga", DateTimeOffset.UtcNow));

        Assert.Equal(2, catalog.Count);
        TriggerCatalogEntry remaining = Assert.Single(catalog.Snapshot(), e => e.Key.IsUnobservedPlaceholder);
        Assert.Equal("Stage_B", remaining.StageId);
    }

    /// <summary>AC-026: a re-observation fills gaps without losing the original entry.</summary>
    [Fact]
    public void ObservationCompletesAnEnumeratedEntryWithoutLosingIt()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        catalog.Register(Entry("Take_00", TriggerSources.StaticEnumeration, isLooping: true));
        DateTimeOffset firstSeen = catalog.Snapshot()[0].FirstSeenAt;

        bool added = catalog.Register(Entry("Take_00", TriggerSources.Observed, clipLength: 1.5, isLooping: true));

        Assert.False(added);
        Assert.Equal(1, catalog.Count);
        TriggerCatalogEntry merged = catalog.Snapshot()[0];
        Assert.Equal(1.5, merged.ClipLengthSeconds);
        Assert.Equal(TriggerSources.Observed, merged.Source);
        Assert.Equal(firstSeen, merged.FirstSeenAt);
    }

    /// <summary>AC-026: reloading preserves everything and writes no partial document.</summary>
    [Fact]
    public async Task CatalogSurvivesReloadAndAddsOnlyNewStages()
    {
        using var temp = new TempDirectory();
        string path = temp.File("trigger-catalog.json");

        var first = new TriggerCatalog(path, "build", Build);
        first.Register(Entry("Take_00", TriggerSources.Observed, clipLength: 0.7, isLooping: true));
        await first.SaveAsync();

        var second = new TriggerCatalog(path, "build", Build);
        Assert.Equal(1, second.Count);

        second.Register(Entry("Take_01", TriggerSources.StaticEnumeration, isLooping: false));
        await second.SaveAsync();

        var third = new TriggerCatalog(path, "build", Build);
        Assert.Equal(2, third.Count);
        Assert.True(third.TryGet(new EventKey("gallery", "Enemy", "Take", "loop", "Take_00"), out TriggerCatalogEntry kept));
        Assert.Equal(0.7, kept.ClipLengthSeconds);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task CatalogFromAnotherGameBuildIsNotReused()
    {
        using var temp = new TempDirectory();
        string path = temp.File("trigger-catalog.json");
        var first = new TriggerCatalog(path, "build", Build);
        first.Register(Entry("Take_00", TriggerSources.Observed));
        await first.SaveAsync();

        var otherBuild = new TargetGameBuild
        {
            GameAssemblySha256 = new string('B', 64),
            GlobalMetadataSha256 = new string('B', 64),
        };
        var second = new TriggerCatalog(path, "other", otherBuild);

        Assert.Equal(0, second.Count);
    }

    [Fact]
    public async Task SavedCatalogIsMachineReadable()
    {
        using var temp = new TempDirectory();
        string path = temp.File("trigger-catalog.json");
        var catalog = new TriggerCatalog(path, "build", Build);
        catalog.Register(Entry("Take_00", TriggerSources.Observed, clipLength: 0.733, isLooping: true));
        await catalog.SaveAsync();

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        JsonElement trigger = document.RootElement.GetProperty("triggers")[0];
        Assert.Equal("Take_00", trigger.GetProperty("stageId").GetString());
        Assert.Equal("observed", trigger.GetProperty("source").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), $"sinisistar2-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup must never fail a test run.
        }
    }
}
