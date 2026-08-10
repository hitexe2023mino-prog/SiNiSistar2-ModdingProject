namespace SiNiSistar2.Edi.Core.Tests;

/// <summary>
/// Several stages of one performance play what is all but the same motion; which of them the game
/// selects depends on what the player was doing when it started. A link states that once, so a
/// correction to the waveform reaches every stage that plays it (SPEC001 6.7-14, FR-062).
/// </summary>
public sealed class LinkedTriggerTests
{
    private static readonly TargetGameBuild Build = new()
    {
        GameAssemblySha256 = TestMappings.Hash,
        GlobalMetadataSha256 = TestMappings.Hash,
    };

    private static readonly EventKey Source = new("hold", "EnmID_MeatWorm1", "Idle_Broken", "loop", "Idle_Broken");
    private static readonly EventKey Target = new("hold", "EnmID_MeatWorm1", "Walk2", "loop", "Walk2");

    private static FunscriptDocument Script(params (int Pos, long At)[] points) =>
        new("1.0", false, 100, points.Select(p => new FunscriptAction(p.Pos, p.At)).ToArray());

    private static FunscriptDocument OneSecond() => Script((0, 0), (100, 500), (0, 1000));

    private sealed record Fixture(
        AuthoringStore Store,
        MappingRepository Mappings,
        TriggerCatalog Catalog,
        string GalleryRoot);

    private static Fixture CreateFixture(TempDirectory temp, params (EventKey Key, double Clip, bool Loop)[] stages)
    {
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        foreach ((EventKey key, double clip, bool loop) in stages)
        {
            catalog.Register(TriggerCatalogEntry.Create(
                key, clip, loop, TriggerSources.Observed, key.StageId, "Dungeon", DateTimeOffset.UtcNow));
        }

        MappingRepository mappings = TestMappings.Create();
        var store = new AuthoringStore(
            Path.Combine(temp.Root, "Gallery"),
            Path.Combine(temp.Root, "manifests"),
            temp.File("mappings.json"),
            "build",
            mappings,
            catalog,
            _ => Task.CompletedTask);

        return new Fixture(store, mappings, catalog, Path.Combine(temp.Root, "Gallery"));
    }

    private static Task<AuthoringSaveResult> SaveSourceAsync(Fixture fixture) =>
        fixture.Store.SaveAsync(new AuthoringSaveRequest(
            Source,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = OneSecond() },
            ApproveLoopMismatch: false));

    /// <summary>
    /// The linked stage is mapped to the source's gallery. No second asset is written: one waveform
    /// is played in two places, which is what makes a later edit reach both.
    /// </summary>
    [Fact]
    public async Task LinkingPointsTheTargetAtTheSourceGalleryWithoutCopyingTheAsset()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (Target, 1.0, true));
        AuthoringSaveResult saved = await SaveSourceAsync(fixture);
        Assert.True(saved.Success, string.Join("; ", saved.Errors));

        AuthoringLinkResult result = await fixture.Store.LinkAsync(
            new AuthoringLinkRequest(Source, new[] { Target }));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(saved.Gallery, result.Gallery);
        Assert.True(fixture.Mappings.TryResolve(Target, out EventMapping? mapping));
        Assert.Equal(new[] { saved.Gallery }, mapping.Outputs.Select(x => x.Gallery));
        Assert.Equal(saved.Gallery, fixture.Store.ResolveGallery(Target));

        // The target's own gallery name never becomes a file, so there is only one waveform.
        string own = Funscript.CreateGalleryName(Target);
        Assert.False(File.Exists(Path.Combine(fixture.GalleryRoot, "a10-main", $"{own}.funscript")));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.GalleryRoot, "a10-main")));

        // Both stages report each other, which is what the GUI warns with before an edit.
        Assert.Equal(2, fixture.Store.KeysSharing(result.Gallery).Count);
    }

    /// <summary>Editing from the linked stage edits the shared waveform, not a copy of it.</summary>
    [Fact]
    public async Task EditingALinkedStageWritesTheSharedGallery()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (Target, 1.0, true));
        AuthoringSaveResult saved = await SaveSourceAsync(fixture);
        await fixture.Store.LinkAsync(new AuthoringLinkRequest(Source, new[] { Target }));

        AuthoringSaveResult edited = await fixture.Store.SaveAsync(new AuthoringSaveRequest(
            Target,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((10, 0), (90, 500), (10, 1000)) },
            ApproveLoopMismatch: false));

        Assert.True(edited.Success, string.Join("; ", edited.Errors));
        Assert.Equal(saved.Gallery, edited.Gallery);

        // The source now plays the edited waveform, because there is only one.
        FunscriptDocument fromSource = fixture.Store.LoadExisting(Source)["a10-main"];
        Assert.Equal(10, fromSource.Actions[0].Pos);
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.GalleryRoot, "a10-main")));
    }

    /// <summary>
    /// A stage whose clip does not line up with the shared waveform drifts on every repeat, which is
    /// the harm FR-040 guards against on save. Linking reports it the same way and needs approval.
    /// </summary>
    [Fact]
    public async Task ALoopingTargetWithADifferentClipLengthNeedsApproval()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (Target, 2.5, true));
        await SaveSourceAsync(fixture);

        AuthoringLinkResult refused = await fixture.Store.LinkAsync(
            new AuthoringLinkRequest(Source, new[] { Target }));

        Assert.False(refused.Success);
        AuthoringLinkOutcome outcome = Assert.Single(refused.Targets);
        Assert.Empty(outcome.Errors);
        Assert.Contains(outcome.Warnings, warning => warning.Contains("2500 ms", StringComparison.Ordinal));
        Assert.False(fixture.Mappings.TryResolve(Target, out _));

        AuthoringLinkResult approved = await fixture.Store.LinkAsync(
            new AuthoringLinkRequest(Source, new[] { Target }, ApproveMismatch: true));

        Assert.True(approved.Success, string.Join("; ", approved.Targets.SelectMany(x => x.Warnings)));
        Assert.True(fixture.Mappings.TryResolve(Target, out _));
    }

    /// <summary>
    /// A stage that cannot be mapped cannot be linked either: an unidentified binder stands for
    /// every binder that could not be named, so a waveform there would play for all of them
    /// (FR-060).
    /// </summary>
    [Fact]
    public async Task StagesThatCannotBeMappedCannotBeLinked()
    {
        var unidentified = new EventKey("hold", ActorIds.UnidentifiedBinder, "Idle", "loop", "Idle");
        var unplayed = new EventKey("gallery", "Enemy", EventKey.UnobservedAnimationId, "loop", "Take_02");

        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (unidentified, 1.0, true), (unplayed, 1.0, true));
        await SaveSourceAsync(fixture);

        AuthoringLinkResult result = await fixture.Store.LinkAsync(
            new AuthoringLinkRequest(Source, new[] { unidentified, unplayed, Source }, ApproveMismatch: true));

        Assert.False(result.Success);
        Assert.Equal(3, result.Targets.Count);
        Assert.All(result.Targets, outcome => Assert.False(outcome.Success));
        Assert.Contains(result.Targets, x => x.Errors.Any(e => e.Contains("unidentified binder", StringComparison.Ordinal)));
        Assert.Contains(result.Targets, x => x.Errors.Any(e => e.Contains("has not been played", StringComparison.Ordinal)));
        Assert.Contains(result.Targets, x => x.Errors.Any(e => e.Contains("linked to itself", StringComparison.Ordinal)));
        Assert.False(fixture.Mappings.TryResolve(unidentified, out _));
        Assert.False(fixture.Mappings.TryResolve(unplayed, out _));
    }

    /// <summary>Linking without a saved source has nothing to share, so it is refused.</summary>
    [Fact]
    public async Task LinkingRequiresTheSourceToBeSavedFirst()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (Target, 1.0, true));

        AuthoringLinkResult result = await fixture.Store.LinkAsync(
            new AuthoringLinkRequest(Source, new[] { Target }));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("no saved waveform", StringComparison.Ordinal));
        Assert.Empty(result.Targets);
    }

    /// <summary>
    /// Unlinking gives the stage its own copy of the waveform it was playing; the stages it left
    /// keep the shared one.
    /// </summary>
    [Fact]
    public async Task UnlinkingGivesTheStageItsOwnCopy()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (Target, 1.0, true));
        AuthoringSaveResult saved = await SaveSourceAsync(fixture);
        await fixture.Store.LinkAsync(new AuthoringLinkRequest(Source, new[] { Target }));

        AuthoringSaveResult detached = await fixture.Store.UnlinkAsync(Target);

        Assert.True(detached.Success, string.Join("; ", detached.Errors));
        Assert.Equal(Funscript.CreateGalleryName(Target), detached.Gallery);
        Assert.NotEqual(saved.Gallery, detached.Gallery);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(fixture.GalleryRoot, "a10-main")).Length);
        Assert.Equal(new[] { Source }, fixture.Store.KeysSharing(saved.Gallery));

        // Editing the source no longer reaches the stage that left.
        await fixture.Store.SaveAsync(new AuthoringSaveRequest(
            Source,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = Script((40, 0), (60, 500), (40, 1000)) },
            ApproveLoopMismatch: false));
        Assert.Equal(0, fixture.Store.LoadExisting(Target)["a10-main"].Actions[0].Pos);
    }

    /// <summary>A stage that is not linked has nothing to detach from.</summary>
    [Fact]
    public async Task UnlinkingAnUnlinkedStageIsRefused()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true));
        await SaveSourceAsync(fixture);

        AuthoringSaveResult result = await fixture.Store.UnlinkAsync(Source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("not linked", StringComparison.Ordinal));
    }

    /// <summary>
    /// Entries are named after their trigger, not after the gallery they play. Two entries carrying
    /// one id would make the mapping file a configuration error at the next start, which would
    /// disable device output entirely (FR-016).
    /// </summary>
    [Fact]
    public async Task SavingThroughALinkKeepsTheMappingFileLoadable()
    {
        using var temp = new TempDirectory();
        Fixture fixture = CreateFixture(temp, (Source, 1.0, true), (Target, 1.0, true));
        await SaveSourceAsync(fixture);
        await fixture.Store.LinkAsync(new AuthoringLinkRequest(Source, new[] { Target }));

        AuthoringSaveResult edited = await fixture.Store.SaveAsync(new AuthoringSaveRequest(
            Target,
            new Dictionary<string, FunscriptDocument> { ["a10-main"] = OneSecond() },
            ApproveLoopMismatch: false));
        Assert.True(edited.Success, string.Join("; ", edited.Errors));

        string[] ids = fixture.Mappings.Document.Events.Select(x => x.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        MappingValidationResult reloaded = MappingRepository.Load(temp.File("mappings.json"));
        Assert.True(reloaded.IsValid, string.Join("; ", reloaded.Errors));
        Assert.Equal(2, reloaded.Repository!.Document.Events.Count);
    }

    /// <summary>
    /// A hand-written entry that names two galleries for one trigger is not a link. Editing it must
    /// not silently pick one of them, so the stage falls back to its own gallery.
    /// </summary>
    [Fact]
    public void AnEntryNamingTwoGalleriesResolvesToTheStagesOwnGallery()
    {
        using var temp = new TempDirectory();
        var catalog = new TriggerCatalog(temp.File("trigger-catalog.json"), "build", Build);
        MappingRepository mappings = TestMappings.Create(new EventMapping
        {
            Id = "hand-written",
            Context = Target.Context,
            ActorId = Target.ActorId,
            AnimationId = Target.AnimationId,
            Phase = Target.Phase,
            StageId = Target.StageId,
            Disposition = "mapped",
            SeekMode = "animation-time",
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.Main, Gallery = "one" },
                new() { Id = TestMappings.BreastLeft, Gallery = "another" },
            },
        });

        var store = new AuthoringStore(
            Path.Combine(temp.Root, "Gallery"),
            Path.Combine(temp.Root, "manifests"),
            temp.File("mappings.json"),
            "build",
            mappings,
            catalog,
            _ => Task.CompletedTask);

        Assert.Equal(Funscript.CreateGalleryName(Target), store.ResolveGallery(Target));
    }
}
