using System.Text.Json;
using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class MappingRepositoryTests
{
    [Fact]
    public void ShippedMappingIsValidAndClassifiesEveryDeclaredStatus()
    {
        string path = Path.Combine(
            TestMappings.FindRepositoryRoot(),
            "BepInEx",
            "config",
            "community.sinisistar2.edi",
            "mappings.json");

        MappingValidationResult result = MappingRepository.Load(path);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.NotEmpty(result.Repository!.Document.StatusRules);
        Assert.All(
            result.Repository.Document.StatusRules,
            rule => Assert.NotEqual(MappingDisposition.Unclassified, result.Repository.ClassifyStatus(rule.StatusId)));
        Assert.Equal(
            "filler-breast-swollen",
            result.Repository.SelectFiller(TestMappings.BreastLeft, new HashSet<string> { "Breast" }));
    }

    /// <summary>
    /// Parasite and egg statuses drive the piston filler, and they do so independently of the
    /// breast channel so both devices can respond to different statuses at once.
    /// </summary>
    [Theory]
    [InlineData("Parasite")]
    [InlineData("ParasiteLv13")]
    [InlineData("LivestockParasite")]
    [InlineData("ParasitePennis")]
    [InlineData("FrogEgg")]
    [InlineData("FrogLEgg")]
    [InlineData("LeechEgg")]
    [InlineData("LeechEgg_Boss")]
    [InlineData("SpiderEggSac")]
    [InlineData("TentacleEgg")]
    [InlineData("TentacleEgg_GO")]
    [InlineData("EvilWoodSeed")]
    [InlineData("Assimilation_Seed")]
    public void ParasiteStatusesSelectThePistonParasiteFiller(string statusId)
    {
        MappingRepository repository = LoadShippedMapping();

        Assert.Equal(
            "filler-main-parasite",
            repository.SelectFiller(TestMappings.Main, new HashSet<string> { statusId }));

        // The breast channel is unaffected by a parasite status.
        Assert.Equal(
            "filler-breast",
            repository.SelectFiller(TestMappings.BreastLeft, new HashSet<string> { statusId }));
    }

    [Fact]
    public void ParasiteAndSwollenApplyToTheirOwnChannelsSimultaneously()
    {
        MappingRepository repository = LoadShippedMapping();
        var statuses = new HashSet<string> { "Parasite", "Breast" };

        Assert.Equal("filler-main-parasite", repository.SelectFiller(TestMappings.Main, statuses));
        Assert.Equal("filler-breast-swollen", repository.SelectFiller(TestMappings.BreastLeft, statuses));
    }

    [Fact]
    public void UnrelatedStatusesStillUseTheDefaultFillers()
    {
        MappingRepository repository = LoadShippedMapping();
        var statuses = new HashSet<string> { "Blessing_Lost", "Milk", "LustMarkCurse" };

        Assert.Equal("filler-main", repository.SelectFiller(TestMappings.Main, statuses));
        Assert.Equal("filler-breast", repository.SelectFiller(TestMappings.BreastLeft, statuses));
    }

    private static MappingRepository LoadShippedMapping()
    {
        MappingValidationResult result = MappingRepository.Load(Path.Combine(
            TestMappings.FindRepositoryRoot(),
            "BepInEx",
            "config",
            "community.sinisistar2.edi",
            "mappings.json"));
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.Repository!;
    }

    [Fact]
    public void DuplicateEventKeyAndUnknownChannelFailClosed()
    {
        EventMapping first = TestMappings.Event("one", "enemy", "clip");
        EventMapping duplicate = TestMappings.Event("two", "enemy", "clip", "unknown");
        MappingRepository valid = TestMappings.Create(first);
        MappingDocument document = valid.Document;
        document.Events.Add(duplicate);
        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        MappingValidationResult result = MappingRepository.Parse(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("Duplicate event key", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("unknown output", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-037: the shipped rules must stay unambiguous. Every mapped rule on one channel currently
    /// selects the same filler, so priority never decides anything — this guards the moment that
    /// stops being true, because statuses stack and the loser would otherwise be picked by nothing
    /// more than line order.
    /// </summary>
    [Fact]
    public void HighestPriorityRuleWinsWhenSeveralStatusesAreActiveAtOnce()
    {
        MappingDocument document = TestMappings.Create().Document;
        document.StatusRules.Add(new StatusRule
        {
            StatusId = "Milk",
            DisplayName = "Milk",
            Disposition = "mapped",
            Priority = 5,
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.BreastLeft, Gallery = "filler-breast" },
            },
        });
        document.StatusRules.Add(new StatusRule
        {
            StatusId = "Overwhelming",
            DisplayName = "Overwhelming",
            Disposition = "mapped",
            Priority = 9,
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.BreastLeft, Gallery = "filler-breast-swollen" },
            },
        });

        MappingValidationResult result = MappingRepository.Parse(Serialize(document));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));

        // Both are active; the higher priority decides regardless of which was declared first.
        Assert.Equal(
            "filler-breast-swollen",
            result.Repository!.SelectFiller(
                TestMappings.BreastLeft,
                new HashSet<string> { "Milk", "Overwhelming" }));
        Assert.Equal(
            "filler-breast",
            result.Repository.SelectFiller(TestMappings.BreastLeft, new HashSet<string> { "Milk" }));
    }

    /// <summary>AC-038: a tie that names different fillers has no declared winner, so it fails closed.</summary>
    [Fact]
    public void EqualPriorityRulesSelectingDifferentFillersAreAConfigurationError()
    {
        MappingDocument document = TestMappings.Create().Document;
        document.StatusRules.Add(new StatusRule
        {
            StatusId = "Milk",
            DisplayName = "Milk",
            Disposition = "mapped",
            Priority = 10,
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.BreastLeft, Gallery = "filler-breast" },
            },
        });

        MappingValidationResult result = MappingRepository.Parse(Serialize(document));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("higher priority", StringComparison.Ordinal));
    }

    /// <summary>The same filler at the same priority is unambiguous, so it stays legal.</summary>
    [Fact]
    public void EqualPriorityRulesSharingOneFillerRemainValid()
    {
        MappingRepository repository = LoadShippedMapping();

        // The shipped file has thirteen parasite-family rules on `main`, all at the default
        // priority and all selecting the same filler.
        Assert.True(
            repository.Document.StatusRules
                .Count(x => x.Disposition == "mapped"
                            && x.Outputs.Any(o => o.Id == TestMappings.Main)) > 1);
        Assert.Equal(
            "filler-main-parasite",
            repository.SelectFiller(TestMappings.Main, new HashSet<string> { "FrogEgg", "Parasite" }));
    }

    private static string Serialize(MappingDocument document) =>
        JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    [Fact]
    public void MissingBreastRuleFailsClosed()
    {
        MappingDocument document = TestMappings.Create().Document;
        document.StatusRules.Clear();
        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        MappingValidationResult result = MappingRepository.Parse(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("must map 膨乳", StringComparison.Ordinal));
    }

    /// <summary>AC-028: the authored mapping is the only writer and survives a reload.</summary>
    [Fact]
    public async Task AuthoredMappingIsPersistedIntoTheSingleSourceOfTruth()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sinisistar2-mappings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "mappings.json");
            MappingRepository original = TestMappings.Create();
            EventMapping authored = TestMappings.Event("authored-event", "enemy", "Hold_Loop", stageId: "Hold_Attack02");

            await original.UpsertAsync(authored, path);

            MappingValidationResult loaded = MappingRepository.Load(path);

            Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Errors));
            Assert.True(loaded.Repository!.TryResolve(authored.Key, out EventMapping? mapping));
            Assert.Equal("authored-event", mapping.Id);
            Assert.Equal("Hold_Attack02", mapping.StageId);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// AC-024: two stages of the same animation are distinct triggers, so one mapping must never
    /// resolve for another stage.
    /// </summary>
    [Fact]
    public void StagesOfTheSameAnimationAreIndependentTriggers()
    {
        EventMapping stageOne = TestMappings.Event("s1", "enemy", "Hold_Loop", stageId: "Hold_Attack01");
        EventMapping stageTwo = TestMappings.Event("s2", "enemy", "Hold_Loop", stageId: "Hold_Attack02");
        MappingRepository repository = TestMappings.Create(stageOne, stageTwo);

        Assert.NotEqual(stageOne.Key, stageTwo.Key);
        Assert.True(repository.TryResolve(stageOne.Key, out EventMapping? first));
        Assert.True(repository.TryResolve(stageTwo.Key, out EventMapping? second));
        Assert.Equal("s1", first.Id);
        Assert.Equal("s2", second.Id);
        Assert.Equal(
            MappingDisposition.Unclassified,
            repository.Classify(stageOne.Key with { StageId = "Hold_Attack03" }));
    }

    [Fact]
    public void MissingStageIdFailsClosed()
    {
        MappingRepository valid = TestMappings.Create(TestMappings.Event("one", "enemy", "clip"));
        MappingDocument document = valid.Document;
        document.Events[0] = new EventMapping
        {
            Id = "one",
            Context = "hold",
            ActorId = "enemy",
            AnimationId = "clip",
            Phase = "loop",
            StageId = "   ",
            Disposition = "mapped",
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.Main, Gallery = "g" },
            },
            SeekMode = "zero",
        };
        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        MappingValidationResult result = MappingRepository.Parse(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("stageId is required", StringComparison.Ordinal));
    }
}
