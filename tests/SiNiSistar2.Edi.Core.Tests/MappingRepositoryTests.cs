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
            result.Repository.SelectFiller(EdiChannels.Breast, new HashSet<string> { "Breast" }));
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
        Assert.Contains(result.Errors, x => x.Contains("unknown channel", StringComparison.Ordinal));
    }

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

    [Fact]
    public async Task GeneratedMappingIsPersistedAndLoadedWithoutUserInput()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sinisistar2-mappings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string basePath = Path.Combine(directory, "mappings.json");
            string generatedPath = Path.Combine(directory, "generated-mappings.json");
            MappingRepository original = TestMappings.Create();
            await File.WriteAllTextAsync(basePath, JsonSerializer.Serialize(original.Document, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
            EventMapping generated = TestMappings.Event("auto-event", "automatic", "measured-motion");
            original.RegisterGenerated(generated);
            await original.SaveGeneratedAsync(generatedPath);

            MappingValidationResult loaded = MappingRepository.Load(basePath, generatedPath);

            Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Errors));
            Assert.True(loaded.Repository!.TryResolve(generated.Key, out EventMapping? mapping));
            Assert.Equal("auto-event", mapping.Id);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
