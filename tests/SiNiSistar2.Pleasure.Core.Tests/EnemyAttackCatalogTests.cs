namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The catalogue decides whether being held raises pleasure at all, and it is edited by hand from
/// inside the game. Both halves matter: the rules it feeds the classifier, and the file it survives
/// in (SPEC003 5.3, FR-235〜239).
/// </summary>
public sealed class EnemyAttackCatalogTests
{
    private static SexualAttackClassifier Classifier(EnemyAttackCatalog catalog) =>
        new(new[] { "Lustfull", "Semen" }, catalog);

    /// <summary>The shipped state: nothing declared, so the status test still decides (DEC-203).</summary>
    [Fact]
    public void AnEnemyWithNoEntryIsLeftToTheStatusTest()
    {
        var catalog = new EnemyAttackCatalog();
        SexualAttackClassifier classifier = Classifier(catalog);

        Assert.Equal(EnemyAttackSetting.Auto, catalog.SettingFor("GaID_Ghoul"));
        Assert.Equal(AttackKind.Sexual, classifier.Classify("GaID_Ghoul", null, new[] { "Semen" }));
        Assert.Equal(AttackKind.NonSexual, classifier.Classify("GaID_Ghoul", null, new[] { "Poison" }));
    }

    [Fact]
    public void SexualForcesTheAnswerEvenWhenTheAttackInflictsNothing()
    {
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);

        Assert.Equal(AttackKind.Sexual, Classifier(catalog).Classify("GaID_Ghoul", null, Array.Empty<string>()));
    }

    /// <summary>AC-206: the safe answer outranks everything, which is why it exists.</summary>
    [Fact]
    public void NonSexualOutranksTheStatusTest()
    {
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_MeatWormVore", EnemyAttackSetting.NonSexual);

        Assert.Equal(
            AttackKind.NonSexual,
            Classifier(catalog).Classify("GaID_MeatWormVore", null, new[] { "Lustfull" }));
    }

    /// <summary>
    /// FR-236: an edit made while held applies to the next hit. The classifier holds the catalogue
    /// by reference for exactly this reason, and a copy would make the in-game editor pointless.
    /// </summary>
    [Fact]
    public void AnEditAppliesWithoutRebuildingTheClassifier()
    {
        var catalog = new EnemyAttackCatalog();
        SexualAttackClassifier classifier = Classifier(catalog);

        Assert.Equal(AttackKind.NonSexual, classifier.Classify("GaID_Hag", null, new[] { "Poison" }));

        catalog.Set("GaID_Hag", EnemyAttackSetting.Sexual);

        Assert.Equal(AttackKind.Sexual, classifier.Classify("GaID_Hag", null, new[] { "Poison" }));
    }

    [Fact]
    public void CyclingWalksTheThreeSettingsAndReturns()
    {
        var catalog = new EnemyAttackCatalog();

        Assert.Equal(EnemyAttackSetting.Sexual, catalog.Cycle("GaID_Frog_M"));
        Assert.Equal(EnemyAttackSetting.NonSexual, catalog.Cycle("GaID_Frog_M"));
        Assert.Equal(EnemyAttackSetting.Auto, catalog.Cycle("GaID_Frog_M"));
    }

    /// <summary>The first sighting is worth writing; the rest of a long hold is not.</summary>
    [Fact]
    public void OnlyTheFirstSightingReportsAChange()
    {
        var catalog = new EnemyAttackCatalog();

        Assert.True(catalog.MarkSeen("GaID_SlugM"));
        Assert.False(catalog.MarkSeen("GaID_SlugM"));
    }

    /// <summary>
    /// FR-237: every id the game defines is listed, so an enemy can be classified before it is met
    /// rather than only after surviving it. Existing decisions are left alone.
    /// </summary>
    [Fact]
    public void AddingTheKnownIdsLeavesExistingDecisionsAlone()
    {
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);

        int added = catalog.AddMissing(new[] { "GaID_Ghoul", "GaID_Hag", "GaID_Zombie" });

        Assert.Equal(2, added);
        Assert.Equal(3, catalog.Count);
        Assert.Equal(EnemyAttackSetting.Sexual, catalog.SettingFor("GaID_Ghoul"));
        Assert.Equal(EnemyAttackSetting.Auto, catalog.SettingFor("GaID_Hag"));
    }

    /// <summary>FR-235: upgrading must not throw away a configuration that already existed.</summary>
    [Fact]
    public void TheOldConfigListsSeedANewCatalogue()
    {
        var catalog = new EnemyAttackCatalog();
        catalog.SeedFrom(new[] { "GaID_PictureFrameBig", "GaID_Both" }, new[] { "GaID_Both" });

        Assert.Equal(EnemyAttackSetting.Sexual, catalog.SettingFor("GaID_PictureFrameBig"));

        // An id named in both lists resolves the way the rule order does: the safe answer wins.
        Assert.Equal(EnemyAttackSetting.NonSexual, catalog.SettingFor("GaID_Both"));
    }

    /// <summary>Enemies already met come first; 108 rows are otherwise unusable in a menu.</summary>
    [Fact]
    public void MetEnemiesSortAheadOfUnmetOnes()
    {
        var catalog = new EnemyAttackCatalog();
        catalog.AddMissing(new[] { "GaID_Aaa", "GaID_Bbb", "GaID_Ccc" });
        catalog.MarkSeen("GaID_Ccc");

        IReadOnlyList<EnemyAttackRow> rows = catalog.Rows();

        Assert.Equal("GaID_Ccc", rows[0].Id);
        Assert.True(rows[0].Seen);
        Assert.Equal(new[] { "GaID_Aaa", "GaID_Bbb" }, rows.Skip(1).Select(row => row.Id));
    }

    /// <summary>FR-238: cancelling the editor puts every setting back, in the same object.</summary>
    [Fact]
    public void CancellingRestoresTheSnapshotInPlace()
    {
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);
        EnemyAttackDocument snapshot = catalog.ToDocument();
        SexualAttackClassifier classifier = Classifier(catalog);

        catalog.Set("GaID_Ghoul", EnemyAttackSetting.NonSexual);
        Assert.Equal(AttackKind.NonSexual, classifier.Classify("GaID_Ghoul", null, null));

        catalog.RestoreFrom(snapshot);

        Assert.Equal(AttackKind.Sexual, classifier.Classify("GaID_Ghoul", null, null));
        Assert.False(catalog.IsDirty);
    }

    [Fact]
    public void NothingIsDirtyUntilSomethingChanges()
    {
        var catalog = new EnemyAttackCatalog();
        Assert.False(catalog.IsDirty);

        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Auto);
        Assert.False(catalog.IsDirty);

        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);
        Assert.True(catalog.IsDirty);
    }
}

/// <summary>
/// The catalogue file holds decisions a player made one enemy at a time. Losing it or overwriting a
/// newer one is the same class of harm as losing a save, so the failure paths are tested with it
/// (SPEC003 FR-239).
/// </summary>
public sealed class EnemyAttackCatalogStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sinisistar2-pleasure-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public void AMissingFileIsAFirstRunRatherThanAnError()
    {
        EnemyAttackCatalogLoad load = new EnemyAttackCatalogStore(_root).Load();

        Assert.Equal(0, load.Catalog.Count);
        Assert.Null(load.Notice);
        Assert.False(load.Locked);
    }

    [Fact]
    public void DecisionsSurviveASaveAndLoad()
    {
        var store = new EnemyAttackCatalogStore(_root);
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);
        catalog.Set("GaID_MeatWormVore", EnemyAttackSetting.NonSexual);
        catalog.MarkSeen("GaID_Ghoul");

        Assert.Null(store.Save(catalog));
        Assert.False(catalog.IsDirty);

        EnemyAttackCatalog reloaded = new EnemyAttackCatalogStore(_root).Load().Catalog;

        Assert.Equal(EnemyAttackSetting.Sexual, reloaded.SettingFor("GaID_Ghoul"));
        Assert.Equal(EnemyAttackSetting.NonSexual, reloaded.SettingFor("GaID_MeatWormVore"));
        Assert.True(reloaded.Rows().Single(row => row.Id == "GaID_Ghoul").Seen);
    }

    [Fact]
    public void SavingLeavesNoTemporaryFile()
    {
        var store = new EnemyAttackCatalogStore(_root);
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);
        store.Save(catalog);

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    /// <summary>A file from a newer MOD is neither read nor replaced, as with the sidecar.</summary>
    [Fact]
    public void ANewerSchemaLocksTheFileAgainstWriting()
    {
        Directory.CreateDirectory(_root);
        var store = new EnemyAttackCatalogStore(_root);
        string future = new EnemyAttackDocument { SchemaVersion = 99 }.Serialize();
        File.WriteAllText(store.FilePath, future);

        EnemyAttackCatalogLoad load = store.Load();
        Assert.True(load.Locked);
        Assert.Equal(0, load.Catalog.Count);

        var replacement = new EnemyAttackCatalog();
        replacement.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);

        Assert.NotNull(store.Save(replacement));
        Assert.Equal(future, File.ReadAllText(store.FilePath));
    }

    /// <summary>A damaged file is set aside, and the catalogue is usable again straight away.</summary>
    [Fact]
    public void ADamagedFileIsQuarantinedAndTheCatalogueStartsFresh()
    {
        Directory.CreateDirectory(_root);
        var store = new EnemyAttackCatalogStore(_root);
        File.WriteAllText(store.FilePath, "{ not json at all");

        EnemyAttackCatalogLoad load = store.Load();

        Assert.False(load.Locked);
        Assert.Contains("corrupt", load.Notice!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(_root, "*.corrupt*"));

        var fresh = new EnemyAttackCatalog();
        fresh.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);
        Assert.Null(store.Save(fresh));
    }

    /// <summary>FR-239: an unwritable location is reported, not thrown into the frame that called.</summary>
    [Fact]
    public void AnUnwritableRootIsReportedRatherThanThrown()
    {
        var store = new EnemyAttackCatalogStore(Path.Combine(_root, "\0invalid"));
        var catalog = new EnemyAttackCatalog();
        catalog.Set("GaID_Ghoul", EnemyAttackSetting.Sexual);

        Assert.NotNull(store.Save(catalog));
    }
}
