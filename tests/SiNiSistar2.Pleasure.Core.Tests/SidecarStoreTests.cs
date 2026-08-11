namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The sidecar carries the only state that outlives a session, and it sits next to the player's
/// saves. Losing it or overwriting a newer one would cost real progress, so the failure paths
/// matter as much as the happy one (SPEC003 5.9, FR-222〜226).
/// </summary>
public sealed class SidecarStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sinisistar2-pleasure-tests",
        Guid.NewGuid().ToString("N"));

    private SidecarStore Store(string build = "b869-a562") => new(_root, build);

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

    /// <summary>AC-218: what was saved comes back.</summary>
    [Fact]
    public void ValuesSurviveASaveAndLoad()
    {
        SidecarStore store = Store();

        Assert.Null(store.Save("slot0-Save01", 3.5f, 4));
        SidecarLoad load = store.Load("slot0-Save01");

        Assert.True(load.IsLoaded);
        Assert.Equal(3.5f, load.Document!.Corruption, 5);
        Assert.Equal(4, load.Document.ClimaxCount);
        Assert.Null(load.Notice);
    }

    /// <summary>AC-224: a slot never played with the MOD starts from defaults, silently.</summary>
    [Fact]
    public void AMissingSlotIsNotAnError()
    {
        SidecarLoad load = Store().Load("slot9-Never");

        Assert.False(load.IsLoaded);
        Assert.Null(load.Notice);
        Assert.False(load.Locked);
    }

    [Fact]
    public void SavingTwiceKeepsTheLatestValues()
    {
        SidecarStore store = Store();
        store.Save("slot1", 1f, 1);
        store.Save("slot1", 2f, 7);

        SidecarLoad load = store.Load("slot1");

        Assert.Equal(2f, load.Document!.Corruption, 5);
        Assert.Equal(7, load.Document.ClimaxCount);
    }

    /// <summary>FR-223: no temporary file is left behind for the next load to trip over.</summary>
    [Fact]
    public void SavingLeavesNoTemporaryFile()
    {
        SidecarStore store = Store();
        store.Save("slot2", 1f, 1);

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    /// <summary>
    /// AC-220: a file from a newer MOD is neither read nor overwritten. Writing over it would
    /// destroy progress the newer version understood and this one does not.
    /// </summary>
    [Fact]
    public void ANewerSchemaLocksTheSlotAgainstWriting()
    {
        Directory.CreateDirectory(_root);
        SidecarStore store = Store();
        string path = store.PathFor("slot3");
        string future = new SidecarDocument { SchemaVersion = 99, Corruption = 9f, ClimaxCount = 9 }.Serialize();
        File.WriteAllText(path, future);

        SidecarLoad load = store.Load("slot3");
        Assert.False(load.IsLoaded);
        Assert.True(load.Locked);

        string? failure = store.Save("slot3", 1f, 1);
        Assert.NotNull(failure);
        Assert.Equal(future, File.ReadAllText(path));
    }

    /// <summary>AC-219: a damaged file is set aside rather than read or silently replaced.</summary>
    [Fact]
    public void ADamagedFileIsQuarantinedAndTheSlotStartsFresh()
    {
        Directory.CreateDirectory(_root);
        SidecarStore store = Store();
        File.WriteAllText(store.PathFor("slot4"), "{ this is not json");

        SidecarLoad load = store.Load("slot4");

        Assert.False(load.IsLoaded);
        Assert.False(load.Locked);
        Assert.Contains("corrupt", load.Notice!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(_root, "*.corrupt*"));

        // The slot is usable again, because a damaged file must not brick a save.
        Assert.Null(store.Save("slot4", 1f, 1));
        Assert.True(store.Load("slot4").IsLoaded);
    }

    /// <summary>A build mismatch is reported but the values are still used; discarding them would lose progress.</summary>
    [Fact]
    public void ADifferentGameBuildIsReportedButStillRead()
    {
        Store("older-build").Save("slot5", 2f, 3);

        SidecarLoad load = Store("newer-build").Load("slot5");

        Assert.True(load.IsLoaded);
        Assert.Equal(2f, load.Document!.Corruption, 5);
        Assert.Contains("older-build", load.Notice!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file written by an earlier schema is still read. Refusing it would have thrown away the
    /// player's accumulated corruption on the very upgrade that added a field, which is the
    /// opposite of what a version check is for.
    /// </summary>
    [Fact]
    public void AnOlderSchemaIsReadWithDefaultsForWhatItLacks()
    {
        Directory.CreateDirectory(_root);
        SidecarStore store = Store();
        File.WriteAllText(
            store.PathFor("slot7"),
            "{\"schemaVersion\": 1, \"gameBuildId\": \"b869-a562\", \"corruption\": 4.5, \"climaxCount\": 2}");

        SidecarLoad load = store.Load("slot7");

        Assert.True(load.IsLoaded);
        Assert.Equal(4.5f, load.Document!.Corruption, 5);
        Assert.Equal(2, load.Document.ClimaxCount);
        Assert.Equal(0, load.Document.BreastAtMaxCount);
        Assert.False(load.Locked);
    }

    /// <summary>The breast escalation count rides along with the rest (SPEC003 5.8).</summary>
    [Fact]
    public void TheBreastCountSurvivesASaveAndLoad()
    {
        SidecarStore store = Store();
        Assert.Null(store.Save("slot8", 1f, 1, breastAtMaxCount: 4));

        Assert.Equal(4, store.Load("slot8").Document!.BreastAtMaxCount);
    }

    /// <summary>FR-226: an unwritable location is reported, not thrown.</summary>
    [Fact]
    public void AnUnwritableRootIsReportedRatherThanThrown()
    {
        var store = new SidecarStore(Path.Combine(_root, "\0invalid"), "build");

        string? failure = store.Save("slot6", 1f, 1);

        Assert.NotNull(failure);
    }

    /// <summary>
    /// The axis was renamed from sensitivity to corruption. Nothing else about it changed, and it
    /// is the one number in the file that can never be earned back — nothing lowers it (DEC-208).
    /// A file written before the rename has to keep its progress.
    /// </summary>
    [Fact]
    public void AFileWrittenUnderTheOldNameKeepsItsProgress()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """{"schemaVersion":3,"gameBuildId":"x","sensitivity":0.42,"climaxCount":3}""");

        Assert.True(parse.IsLoaded);
        Assert.Equal(0.42f, parse.Document!.Corruption, 4);
        Assert.Equal(3, parse.Document.ClimaxCount);
    }

    /// <summary>The new name wins where both are present, and the old one is not written back.</summary>
    [Fact]
    public void TheNewNameWinsAndTheOldOneIsNotWrittenBack()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """{"schemaVersion":4,"gameBuildId":"x","sensitivity":0.10,"corruption":0.80}""");

        Assert.True(parse.IsLoaded);
        Assert.Equal(0.80f, parse.Document!.Corruption, 4);
        Assert.DoesNotContain("sensitivity", parse.Document.Serialize());
    }

    /// <summary>
    /// FR-265, 付録A A-44: the game reports "FileEmpty" whenever no save is loaded, which every
    /// fresh playthrough passes through. Treating that as a slot gave them all one key, and the
    /// second new game inherited the first one's corruption.
    /// </summary>
    [Theory]
    [InlineData("FileEmpty")]
    [InlineData("fileempty")]
    [InlineData("FileEmpty.sav")]
    public void TheNoSaveSentinelIsNotASlot(string fileName)
    {
        Assert.Null(SlotKey.Compose(0, fileName));
    }

    [Fact]
    public void ARealFileIsStillASlot()
    {
        Assert.Equal("slot2-Save03", SlotKey.Compose(2, "Save03.sav"));
    }

    /// <summary>
    /// FR-272: the crest does not come off for the rest of the run, so the fact that it was
    /// received has to survive a reload — a cure written for something else must not lift it.
    /// </summary>
    [Fact]
    public void TheCrestSurvivesAReload()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """{"schemaVersion":5,"gameBuildId":"x","corruption":6,"lustCrest":true}""");

        Assert.True(parse.IsLoaded);
        Assert.True(parse.Document!.LustCrest);
    }

    /// <summary>A file written before the crest existed simply has not received it.</summary>
    [Fact]
    public void AnOlderFileHasNoCrest()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """{"schemaVersion":4,"gameBuildId":"x","corruption":6}""");

        Assert.True(parse.IsLoaded);
        Assert.False(parse.Document!.LustCrest);
    }

    /// <summary>
    /// SPEC006 AC-605: a file from before the diary existed starts it empty rather than refusing to
    /// load. The one thing an upgrade must never do is cost the player the corruption they had.
    /// </summary>
    [Fact]
    public void AFileFromBeforeTheDiaryStartsItEmpty()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """{"schemaVersion":5,"gameBuildId":"x","corruption":6,"climaxCount":2}""");

        Assert.True(parse.IsLoaded);
        Assert.Empty(parse.Document!.ActorClimaxCounts);
        Assert.Empty(parse.Document.DebuffCounts);
        Assert.Equal(6f, parse.Document.Corruption);

        var actors = new ActorClimaxLedger();
        var debuffs = new DebuffCounters();
        SidecarStatistics.Restore(parse.Document, actors, debuffs);

        Assert.Equal(0, actors.Total);
        Assert.Equal(0, debuffs.Total);
    }

    /// <summary>
    /// SPEC006 FR-605: what the diary held comes back, enemy by enemy and status by status.
    /// </summary>
    [Fact]
    public void TheDiarySurvivesASaveAndLoad()
    {
        SidecarStore store = Store();

        var actors = new ActorClimaxLedger();
        actors.Record("Worm");
        actors.Record("Worm");
        actors.Record(null);

        var debuffs = new DebuffCounters();
        debuffs.Record("Breast");
        debuffs.Record("Parasite");
        debuffs.Record("Breast");

        Assert.Null(store.Save("slot0-Save01", 6f, 2, actors: actors, debuffs: debuffs));

        SidecarLoad load = store.Load("slot0-Save01");
        Assert.True(load.IsLoaded);

        var restoredActors = new ActorClimaxLedger();
        var restoredDebuffs = new DebuffCounters();
        SidecarStatistics.Restore(load.Document!, restoredActors, restoredDebuffs);

        Assert.Equal(2, restoredActors.CountFor("Worm"));
        Assert.Equal(1, restoredActors.CountFor(ActorClimaxLedger.UnknownActorId));
        Assert.Equal(3, restoredActors.Total);
        Assert.Equal("Worm", restoredActors.TopActor()!.Value.Key);

        Assert.Equal(2, restoredDebuffs.CountFor("Breast"));
        Assert.Equal(1, restoredDebuffs.CountFor("Parasite"));
    }

    /// <summary>
    /// SPEC006 FR-613: the names come back with the counts.
    ///
    /// They cannot be resolved again after the fact — the game only answers while the status is
    /// attached to somebody — so a diary that did not store them would go back to raw enumerator
    /// names on every reload and stay that way until each status was suffered again (付録A A-603).
    /// </summary>
    [Fact]
    public void TheNamesTheGameGaveSurviveAReload()
    {
        SidecarStore store = Store();

        var debuffs = new DebuffCounters();
        debuffs.Record("Breast", "膨乳");
        debuffs.Record("Parasite", "寄生Lv1");
        debuffs.Record("MindControl");

        Assert.Null(store.Save("slot0-Save01", 1f, 1, debuffs: debuffs));

        var restored = new DebuffCounters();
        SidecarStatistics.Restore(store.Load("slot0-Save01").Document!, null, restored);

        Assert.Equal("膨乳", restored.DisplayNameFor("Breast"));
        Assert.Equal("寄生Lv1", restored.DisplayNameFor("Parasite"));
        Assert.Null(restored.DisplayNameFor("MindControl"));
        Assert.Equal(1, restored.CountFor("MindControl"));
    }

    /// <summary>
    /// A schema 6 file written before names were stored still loads; those statuses simply have no
    /// name until they are next applied.
    /// </summary>
    [Fact]
    public void ASchemaSixFileWithoutNamesStillLoads()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """
            {"schemaVersion":6,"gameBuildId":"x",
             "debuffCounts":[{"abnormalType":"Breast","count":4}]}
            """);

        Assert.True(parse.IsLoaded);

        var restored = new DebuffCounters();
        SidecarStatistics.Restore(parse.Document!, null, restored);

        Assert.Equal(4, restored.CountFor("Breast"));
        Assert.Null(restored.DisplayNameFor("Breast"));
    }

    /// <summary>
    /// A restore replaces the tally rather than adding to it. Loading the same slot twice — which a
    /// defeat does — must not double every count in the diary.
    /// </summary>
    [Fact]
    public void RestoringTwiceDoesNotDoubleTheDiary()
    {
        SidecarStore store = Store();

        var actors = new ActorClimaxLedger();
        actors.Record("Worm");
        Assert.Null(store.Save("slot0-Save01", 1f, 1, actors: actors));

        SidecarDocument stored = store.Load("slot0-Save01").Document!;
        var restored = new ActorClimaxLedger();
        SidecarStatistics.Restore(stored, restored, null);
        SidecarStatistics.Restore(stored, restored, null);

        Assert.Equal(1, restored.CountFor("Worm"));
    }

    /// <summary>A hand-edited row with no name or an impossible count is dropped on read.</summary>
    [Fact]
    public void DamagedDiaryRowsAreDropped()
    {
        SidecarParse parse = SidecarDocument.Parse(
            """
            {"schemaVersion":6,"gameBuildId":"x",
             "actorClimaxCounts":[{"actorId":"Worm","count":2},{"actorId":"","count":5},
                                  {"actorId":"Slime","count":-3}],
             "debuffCounts":[{"abnormalType":"Breast","count":1}]}
            """);

        Assert.True(parse.IsLoaded);
        Assert.Single(parse.Document!.ActorClimaxCounts);
        Assert.Equal("Worm", parse.Document.ActorClimaxCounts[0].ActorId);
    }
}
