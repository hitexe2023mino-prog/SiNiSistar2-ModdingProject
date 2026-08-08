using System.Text;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class FillerAuthoringTests
{
    private static readonly TargetGameBuild Build = new()
    {
        GameAssemblySha256 = TestMappings.Hash,
        GlobalMetadataSha256 = TestMappings.Hash,
    };

    private const string Definitions =
        "Name,FileName,StartTime,EndTime,Type,Loop,Description\n"
        + "Filler Main,filler-main,0,2000,filler,true,Default A10 piston filler\n"
        + "Filler Breast,filler-breast,0,2000,filler,true,Default synchronized UFO TW filler\n"
        + "Filler Breast Swollen,filler-breast-swollen,0,2000,filler,true,\"Stronger, for 膨乳\"\n";

    private static FunscriptDocument Script(params (int Pos, long At)[] points) =>
        new("1.0", false, 100, points.Select(p => new FunscriptAction(p.Pos, p.At)).ToArray());

    private static (AuthoringStore Store, List<int> Reloads) CreateStore(
        TempDirectory temp,
        Func<Task>? onReload = null)
    {
        string galleryRoot = Path.Combine(temp.Root, "Gallery");
        Directory.CreateDirectory(galleryRoot);
        File.WriteAllText(Path.Combine(galleryRoot, "Definitions.csv"), Definitions, new UTF8Encoding(false));

        var catalog = new TriggerCatalog(temp.File("catalog.json"), "build", Build);
        var reloads = new List<int>();
        var store = new AuthoringStore(
            galleryRoot,
            Path.Combine(temp.Root, "manifests"),
            temp.File("mappings.json"),
            "build",
            TestMappings.Create(),
            catalog,
            async _ =>
            {
                if (onReload is not null)
                {
                    await onReload();
                }

                reloads.Add(reloads.Count + 1);
            });
        return (store, reloads);
    }

    /// <summary>Fillers come from the mapping file, not the catalog, so they must be discoverable.</summary>
    [Fact]
    public void FillersAreListedFromTheMappingFile()
    {
        using var temp = new TempDirectory();
        (AuthoringStore store, _) = CreateStore(temp);

        IReadOnlyList<FillerDescriptor> fillers = store.ListFillers();

        Assert.Equal(3, fillers.Count);

        FillerDescriptor swollen = fillers.Single(f => f.Gallery == "filler-breast-swollen");
        Assert.Equal(
            new[] { TestMappings.BreastLeft, TestMappings.BreastRight }, swollen.Outputs);
        Assert.Equal("status", swollen.Role);
        Assert.Equal("Breast", swollen.StatusId);
        Assert.Equal("膨乳", swollen.StatusDisplayName);

        Assert.Equal("default", fillers.Single(f => f.Gallery == "filler-main").Role);
        Assert.Equal(
            new[] { TestMappings.Main }, fillers.Single(f => f.Gallery == "filler-main").Outputs);
    }

    /// <summary>
    /// EDI takes a filler's playback length from Definitions.csv, so saving a different length has
    /// to update the table or EDI keeps looping the old duration.
    /// </summary>
    [Fact]
    public async Task SavingAFillerRewritesItsDefinitionEndTime()
    {
        using var temp = new TempDirectory();
        (AuthoringStore store, List<int> reloads) = CreateStore(temp);

        FillerSaveResult result = await store.SaveFillerAsync(
            "filler-breast-swollen",
            new Dictionary<string, FunscriptDocument>
            {
                ["ufo-left"] = Script((0, 0), (100, 1500), (0, 3000)),
                ["ufo-right"] = Script((100, 0), (0, 1500), (100, 3000)),
            });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(3000, result.DurationMilliseconds);
        Assert.True(result.DefinitionUpdated);
        Assert.Single(reloads);

        IReadOnlyList<EdiGalleryDefinition> rows = EdiGalleryDefinitions.Read(store.DefinitionsPath);
        Assert.Equal(3000, rows.Single(r => r.FileName == "filler-breast-swollen").EndTime);

        // Untouched rows keep their values, including a quoted description.
        Assert.Equal(2000, rows.Single(r => r.FileName == "filler-main").EndTime);
        Assert.Equal("Stronger, for 膨乳", rows.Single(r => r.FileName == "filler-breast-swollen").Description);
        Assert.True(rows.All(r => r.Loop));
    }

    /// <summary>Both UFO TW sides loop on one gallery, so unequal lengths would drift apart.</summary>
    [Fact]
    public async Task VariantsOfDifferentLengthsAreRejected()
    {
        using var temp = new TempDirectory();
        (AuthoringStore store, List<int> reloads) = CreateStore(temp);

        FillerSaveResult result = await store.SaveFillerAsync(
            "filler-breast",
            new Dictionary<string, FunscriptDocument>
            {
                ["ufo-left"] = Script((0, 0), (100, 2000)),
                ["ufo-right"] = Script((0, 0), (100, 2500)),
            });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("same length", StringComparison.Ordinal));
        Assert.Empty(reloads);
    }

    [Fact]
    public async Task BreastFillerRequiresBothSides()
    {
        using var temp = new TempDirectory();
        (AuthoringStore store, _) = CreateStore(temp);

        FillerSaveResult result = await store.SaveFillerAsync(
            "filler-breast",
            new Dictionary<string, FunscriptDocument> { ["ufo-left"] = Script((0, 0), (100, 2000)) });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("ufo-right", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGalleryThatIsNotAFillerIsRejected()
    {
        using var temp = new TempDirectory();
        (AuthoringStore store, _) = CreateStore(temp);

        FillerSaveResult result = await store.SaveFillerAsync(
            "sinisistar2-deadbeef",
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500)) });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("is not a filler", StringComparison.Ordinal));
    }

    /// <summary>An EDI failure must not leave the table claiming a length nothing was registered for.</summary>
    [Fact]
    public async Task EdiFailureIsReportedAndTheFilesAreStillKept()
    {
        using var temp = new TempDirectory();
        (AuthoringStore store, _) = CreateStore(temp, () => throw new HttpRequestException("offline"));

        FillerSaveResult result = await store.SaveFillerAsync(
            "filler-main",
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 1000)) });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("EDI did not re-read the gallery", StringComparison.Ordinal));
        Assert.NotEmpty(result.WrittenPaths);
        Assert.True(File.Exists(result.WrittenPaths[0]));
    }

    [Fact]
    public async Task DefinitionRowsSurviveARoundTrip()
    {
        using var temp = new TempDirectory();
        string path = temp.File("Definitions.csv");
        await File.WriteAllTextAsync(path, Definitions, new UTF8Encoding(false));

        IReadOnlyList<EdiGalleryDefinition> original = EdiGalleryDefinitions.Read(path);
        await EdiGalleryDefinitions.WriteAsync(path, original);
        IReadOnlyList<EdiGalleryDefinition> reloaded = EdiGalleryDefinitions.Read(path);

        Assert.Equal(original, reloaded);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task UpsertingAnUnknownGalleryAddsARowKeyedByFileName()
    {
        using var temp = new TempDirectory();
        string path = temp.File("Definitions.csv");
        await File.WriteAllTextAsync(path, Definitions, new UTF8Encoding(false));

        Assert.True(await EdiGalleryDefinitions.UpsertAsync(path, "missing", 1234, "filler", true, "added"));
        IReadOnlyList<EdiGalleryDefinition> rows = EdiGalleryDefinitions.Read(path);
        Assert.Equal(4, rows.Count);
        EdiGalleryDefinition added = rows.Single(r => r.FileName == "missing");
        Assert.Equal("missing", added.Name);
        Assert.Equal(1234, added.EndTime);
    }
}
