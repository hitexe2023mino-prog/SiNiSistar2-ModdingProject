namespace SiNiSistar2.Edi.Core.Tests;

public sealed class AuthoringTests
{
    private static readonly TargetGameBuild Build = new()
    {
        GameAssemblySha256 = TestMappings.Hash,
        GlobalMetadataSha256 = TestMappings.Hash,
    };

    private static readonly EventKey Key = new("gallery", "Enemy", "Take", "loop", "Take_01");

    private static FunscriptDocument Script(params (int Pos, long At)[] points) =>
        new("1.0", false, 100, points.Select(p => new FunscriptAction(p.Pos, p.At)).ToArray());

    private static (AuthoringStore Store, MappingRepository Mappings, string MappingPath, List<int> Reloads)
        CreateStore(TempDirectory temp, TriggerCatalog catalog, Func<Task>? onReload = null)
    {
        MappingRepository mappings = TestMappings.Create();
        string mappingPath = temp.File("mappings.json");
        var reloads = new List<int>();
        var store = new AuthoringStore(
            Path.Combine(temp.Root, "Gallery"),
            Path.Combine(temp.Root, "manifests"),
            mappingPath,
            "build",
            mappings,
            catalog,
            async _ =>
            {
                if (onReload is not null)
                {
                    await onReload();
                }

                reloads.Add(reloads.Count + 1);
            });
        return (store, mappings, mappingPath, reloads);
    }

    private static TriggerCatalog CatalogWithStage(TempDirectory temp, double clipLength, bool isLoop)
    {
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        catalog.Register(TriggerCatalogEntry.Create(
            Key, clipLength, isLoop, TriggerSources.Observed, "Take_01", "GalleryScene", DateTimeOffset.UtcNow));
        return catalog;
    }

    /// <summary>AC-028: saving writes the asset, registers it with EDI, and maps the trigger.</summary>
    [Fact]
    public async Task SavingWritesTheAssetRegistersItAndMapsTheTrigger()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: true);
        (AuthoringStore store, MappingRepository mappings, string mappingPath, List<int> reloads) =
            CreateStore(temp, catalog);

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500), (0, 1000)) },
            ApproveLoopMismatch: false));

        Assert.True(result.Success, string.Join("; ", result.Errors.Concat(result.LoopWarnings)));
        Assert.True(result.MappingUpdated);
        Assert.Single(reloads);
        Assert.True(File.Exists(Path.Combine(temp.Root, "Gallery", "a10-main", $"{result.Gallery}.funscript")));
        Assert.True(File.Exists(result.ManifestPath!));
        Assert.True(mappings.TryResolve(Key, out EventMapping? mapping));
        Assert.Equal(new[] { TestMappings.Main }, mapping.Outputs.Select(x => x.Id));
        Assert.True(File.Exists(mappingPath));
    }

    /// <summary>AC-030: an EDI failure leaves the trigger unmapped so it is never played.</summary>
    [Fact]
    public async Task EdiRegistrationFailureLeavesTheTriggerUnmapped()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: true);
        (AuthoringStore store, MappingRepository mappings, string mappingPath, _) =
            CreateStore(temp, catalog, () => throw new HttpRequestException("EDI is offline."));

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500), (0, 1000)) },
            ApproveLoopMismatch: false));

        Assert.False(result.Success);
        Assert.False(result.MappingUpdated);
        Assert.Contains(
            result.Errors,
            error => error.Contains("EDI did not re-read the gallery", StringComparison.Ordinal));
        Assert.False(mappings.TryResolve(Key, out _));
        Assert.False(File.Exists(mappingPath));

        // The authored file is still kept so the user does not lose the work.
        Assert.NotEmpty(result.WrittenPaths);
    }

    /// <summary>AC-031: a loop that does not match the clip length needs explicit approval.</summary>
    [Fact]
    public async Task LoopLengthMismatchRequiresApprovalAndIsRecorded()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 0.733, isLoop: true);
        (AuthoringStore store, _, _, _) = CreateStore(temp, catalog);
        var variants = new Dictionary<string, FunscriptDocument>
        {
            ["a10-main"] = Script((0, 0), (100, 500), (0, 1000)),
        };

        AuthoringSaveResult rejected = await store.SaveAsync(
            new AuthoringSaveRequest(Key, variants, ApproveLoopMismatch: false));

        Assert.False(rejected.Success);
        Assert.NotEmpty(rejected.LoopWarnings);
        Assert.Empty(rejected.WrittenPaths);

        AuthoringSaveResult approved = await store.SaveAsync(
            new AuthoringSaveRequest(Key, variants, ApproveLoopMismatch: true));

        Assert.True(approved.Success, string.Join("; ", approved.Errors));
        string manifest = await File.ReadAllTextAsync(approved.ManifestPath!);
        Assert.Contains("\"loopMismatchApproved\": true", manifest, StringComparison.Ordinal);
    }

    /// <summary>
    /// One side alone is a supported trigger now that each device is its own output: the other
    /// side simply is not part of the trigger and keeps its filler. The MOD still never invents
    /// the missing waveform (FR-039), it just does not require one.
    /// </summary>
    [Fact]
    public async Task OneSidedBreastAuthoringMapsOnlyThatOutput()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: false);
        (AuthoringStore store, MappingRepository mappings, _, List<int> reloads) =
            CreateStore(temp, catalog);

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["ufo-left"] = Script((0, 0), (100, 400)) },
            ApproveLoopMismatch: false));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(reloads);
        Assert.True(mappings.TryResolve(Key, out EventMapping? mapping));
        Assert.Equal(new[] { TestMappings.BreastLeft }, mapping.Outputs.Select(x => x.Id));
        Assert.False(File.Exists(Path.Combine(temp.Root, "Gallery", "ufo-right", $"{result.Gallery}.funscript")));
    }

    [Fact]
    public async Task BothBreastSidesBecomeTwoOutputs()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: false);
        (AuthoringStore store, MappingRepository mappings, _, _) = CreateStore(temp, catalog);

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument>
            {
                ["ufo-left"] = Script((0, 0), (100, 400)),
                ["ufo-right"] = Script((100, 0), (10, 400)),
            },
            ApproveLoopMismatch: false));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(mappings.TryResolve(Key, out EventMapping? mapping));
        Assert.Equal(
            new[] { TestMappings.BreastLeft, TestMappings.BreastRight },
            mapping.Outputs.Select(x => x.Id));

        IReadOnlyDictionary<string, FunscriptDocument> reloaded = store.LoadExisting(Key);
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(100, reloaded["ufo-right"].Actions[0].Pos);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5601/", true)]
    [InlineData("http://localhost:5601/", true)]
    [InlineData("http://0.0.0.0:5601/", false)]
    [InlineData("http://192.168.1.10:5601/", false)]
    [InlineData("https://127.0.0.1:5601/", false)]
    [InlineData("not-a-url", false)]
    public void AuthoringServerOnlyAcceptsLoopbackAddresses(string url, bool expectedValid)
    {
        IReadOnlyList<string> errors = AuthoringServer.ValidateBaseUrl(url, out Uri? uri);

        Assert.Equal(expectedValid, errors.Count == 0);
        Assert.Equal(expectedValid, uri is not null);
    }

    [Fact]
    public void FunscriptValidationRejectsNonIncreasingTimesAndOutOfRangePositions()
    {
        FunscriptValidation validation = Funscript.Validate(
            new FunscriptDocument("1.0", false, 100, new[]
            {
                new FunscriptAction(0, 0),
                new FunscriptAction(140, 100),
                new FunscriptAction(50, 100),
            }),
            clipLengthSeconds: 0,
            isLoop: false);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("strictly increase", StringComparison.Ordinal));
        Assert.Contains(validation.Errors, error => error.Contains("between 0 and 100", StringComparison.Ordinal));
    }

    /// <summary>
    /// The written `.funscript` must contain only the funscript contract. Derived helpers such as
    /// the duration must not leak into a file other tools parse.
    /// </summary>
    [Fact]
    public async Task WrittenFunscriptContainsOnlyTheFunscriptContract()
    {
        using var temp = new TempDirectory();
        string path = temp.File("out.funscript");
        await Funscript.WriteAtomicAsync(path, Script((0, 0), (100, 500)));

        string json = await File.ReadAllTextAsync(path);

        Assert.Contains("\"actions\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("durationMilliseconds", json, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(Funscript.TryRead(path));
    }

    /// <summary>The mapping file must not gain a redundant derived key alongside its fields.</summary>
    [Fact]
    public async Task SavedMappingFileDoesNotRepeatTheDerivedKey()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: false);
        (AuthoringStore store, _, string mappingPath, _) = CreateStore(temp, catalog);

        await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500)) },
            ApproveLoopMismatch: false));

        string json = await File.ReadAllTextAsync(mappingPath);

        Assert.Contains("\"stageId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", json, StringComparison.Ordinal);
        Assert.True(MappingRepository.Load(mappingPath).IsValid);
    }

    /// <summary>
    /// EDI keys galleries by the Name column and only auto-generates definitions when the file is
    /// absent, so an authored gallery with no row cannot be played at all.
    /// </summary>
    [Fact]
    public async Task SavingATriggerAddsItsGalleryToTheDefinitionTable()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "Gallery"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Root, "Gallery", "Definitions.csv"),
            "Name,FileName,StartTime,EndTime,Type,Loop,Description\n"
            + "filler-main,filler-main,0,2000,filler,true,Default A10 piston filler\n");

        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: true);
        (AuthoringStore store, _, _, _) = CreateStore(temp, catalog);

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500), (0, 1000)) },
            ApproveLoopMismatch: false));

        Assert.True(result.Success, string.Join("; ", result.Errors));

        IReadOnlyList<EdiGalleryDefinition> rows = EdiGalleryDefinitions.Read(store.DefinitionsPath);
        EdiGalleryDefinition row = rows.Single(r => r.FileName == result.Gallery);

        // Name must equal what the MOD plays, which is the gallery id.
        Assert.Equal(result.Gallery, row.Name);
        Assert.Equal(1000, row.EndTime);
        Assert.Equal("gallery", row.Type);
        Assert.True(row.Loop);

        // The pre-existing filler row is untouched.
        Assert.Equal(2000, rows.Single(r => r.FileName == "filler-main").EndTime);
    }

    /// <summary>Re-saving updates the existing row rather than adding a duplicate Name, which EDI rejects.</summary>
    [Fact]
    public async Task ResavingATriggerDoesNotDuplicateItsDefinitionRow()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "Gallery"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Root, "Gallery", "Definitions.csv"),
            "Name,FileName,StartTime,EndTime,Type,Loop,Description\n");

        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: true);
        (AuthoringStore store, _, _, _) = CreateStore(temp, catalog);

        await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 1000)) },
            ApproveLoopMismatch: false));
        await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 400), (0, 1000)) },
            ApproveLoopMismatch: false));

        IReadOnlyList<EdiGalleryDefinition> rows = EdiGalleryDefinitions.Read(store.DefinitionsPath);
        Assert.Single(rows);
        Assert.Equal(rows.Select(r => r.Name).Distinct().Count(), rows.Count);
    }

    /// <summary>
    /// Re-authoring a trigger for a different channel must remove the variant it no longer uses.
    /// A left-behind file is reloaded on the next edit and silently re-added, which is how a
    /// breast-only trigger kept driving the piston.
    /// </summary>
    [Fact]
    public async Task ReauthoringForAnotherOutputRemovesTheUnusedVariant()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 1.0, isLoop: false);
        (AuthoringStore store, MappingRepository mappings, _, _) = CreateStore(temp, catalog);

        AuthoringSaveResult first = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500)) },
            ApproveLoopMismatch: false));
        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.Equal(new[] { TestMappings.Main }, first.Outputs);

        string mainPath = Path.Combine(temp.Root, "Gallery", "a10-main", $"{first.Gallery}.funscript");
        Assert.True(File.Exists(mainPath));

        AuthoringSaveResult second = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument>
            {
                ["ufo-left"] = Script((0, 0), (100, 500)),
                ["ufo-right"] = Script((100, 0), (0, 500)),
            },
            ApproveLoopMismatch: false));

        Assert.True(second.Success, string.Join("; ", second.Errors));
        Assert.Equal(
            new[] { TestMappings.Main, TestMappings.BreastLeft, TestMappings.BreastRight },
            second.Outputs);
        Assert.Contains(mainPath, second.RemovedPaths!);
        Assert.False(File.Exists(mainPath), "the unused a10-main variant must be gone");

        // The merge is additive, so main survives as an assignment even though its asset is gone.
        // The asset check (FR-049) is what stops it being played, and AC-015 is what reports it.
        Assert.True(mappings.TryResolve(Key, out EventMapping? mapping));
        Assert.Equal(
            new[] { TestMappings.Main, TestMappings.BreastLeft, TestMappings.BreastRight },
            mapping.Outputs.Select(x => x.Id));
        Assert.Equal(new[] { "ufo-left", "ufo-right" }, store.LoadExisting(Key).Keys.OrderBy(x => x).ToArray());
    }

    /// <summary>An unplayed stage has no known clip or length, so it must never become a mapping.</summary>
    [Fact]
    public async Task UnplayedStagePlaceholderCannotBeAuthored()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        (AuthoringStore store, MappingRepository mappings, _, List<int> reloads) =
            CreateStore(temp, catalog);
        var placeholder = new EventKey(
            "gallery", "VillagerRegion", EventKey.UnobservedAnimationId, "loop", "VillagerRegion_Hold");

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            placeholder,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500)) },
            ApproveLoopMismatch: false));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("has not been played yet", StringComparison.Ordinal));
        Assert.Empty(reloads);
        Assert.False(mappings.TryResolve(placeholder, out _));
    }

    /// <summary>
    /// A take with no animator reports no loop even when the stage repeats on screen. Without the
    /// author's own answer the definition row said Loop=false, EDI played the script once, and the
    /// piston then stood still for the rest of the stage.
    /// </summary>
    [Fact]
    public async Task AnAuthorCanMarkAStageAsRepeatingWhenTheGameReportsNoLoop()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 0, isLoop: false);
        (AuthoringStore store, _, _, _) = CreateStore(temp, catalog);

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500), (0, 1000)) },
            ApproveLoopMismatch: false,
            Repeat: true));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(EdiGalleryDefinitions.Read(store.DefinitionsPath).Single(r => r.FileName == result.Gallery).Loop);
    }

    /// <summary>Without an explicit answer the game's own loop flag still decides.</summary>
    [Fact]
    public async Task RepeatDefaultsToWhetherTheGameClipLoops()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 0, isLoop: false);
        (AuthoringStore store, _, _, _) = CreateStore(temp, catalog);

        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((0, 0), (100, 500), (0, 1000)) },
            ApproveLoopMismatch: false));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(EdiGalleryDefinitions.Read(store.DefinitionsPath).Single(r => r.FileName == result.Gallery).Loop);
    }

    /// <summary>
    /// Strokes past the piston's follow rate, a repeating script that starts late, and a repeat
    /// seam that jumps are all reported — and none of them block the save, because the waveform is
    /// legal and only the author may change it (FR-039).
    /// </summary>
    [Fact]
    public async Task StrokesThePistonCannotFollowAreReportedWithoutBlockingTheSave()
    {
        using var temp = new TempDirectory();
        TriggerCatalog catalog = CatalogWithStage(temp, 0, isLoop: false);
        (AuthoringStore store, _, _, _) = CreateStore(temp, catalog);

        // The shape the GUI produced for GaID_PriestRod_H/Continue: 70 units in 147 ms, a start at
        // 37 ms, and ends that do not meet.
        AuthoringSaveResult result = await store.SaveAsync(new AuthoringSaveRequest(
            Key,
            new Dictionary<string, FunscriptDocument>
            {
                ["a10-main"] = Script((25, 37), (95, 184), (86, 454), (17, 547), (85, 786), (14, 890)),
            },
            ApproveLoopMismatch: false,
            Repeat: true));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.MotionWarnings, w => w.Contains("units/s", StringComparison.Ordinal));
        Assert.Contains(result.MotionWarnings, w => w.Contains("0ms に置いて", StringComparison.Ordinal));
        Assert.Contains(result.MotionWarnings, w => w.Contains("継ぎ目", StringComparison.Ordinal));
    }

    /// <summary>A waveform the piston can follow is reported clean.</summary>
    [Fact]
    public void AFollowableRepeatingWaveformProducesNoMotionWarnings()
    {
        FunscriptValidation validation = Funscript.Validate(
            Script((20, 0), (65, 500), (30, 1000), (75, 1500), (20, 2000)),
            clipLengthSeconds: 2,
            isLoop: true,
            variant: "a10-main",
            repeats: true);

        Assert.Empty(validation.Errors);
        Assert.Empty(validation.MotionWarnings);
    }

    /// <summary>The rate note is for the piston only; the UFO variants are rotational.</summary>
    [Fact]
    public void TheStrokeRateNoteDoesNotApplyToTheBreastVariants()
    {
        FunscriptValidation validation = Funscript.Validate(
            Script((0, 0), (100, 50), (0, 100)),
            clipLengthSeconds: 0,
            isLoop: false,
            variant: "ufo-left");

        Assert.Empty(validation.MotionWarnings);
    }

    [Fact]
    public void GalleryNameIsStableAndStageSpecific()
    {
        string first = Funscript.CreateGalleryName(Key);
        string second = Funscript.CreateGalleryName(Key);
        string otherStage = Funscript.CreateGalleryName(Key with { StageId = "Take_02" });

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherStage);
        Assert.StartsWith("sinisistar2-", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Enemy", first, StringComparison.Ordinal);
    }
}
