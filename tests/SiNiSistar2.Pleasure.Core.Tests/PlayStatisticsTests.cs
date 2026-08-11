namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The diary's two tallies (SPEC006 5章).
///
/// They are lifetime totals sitting beside a climax count that a save point clears, which is the
/// one thing about them easiest to get wrong: the numbers look alike and are reset by different
/// events (DEC-602).
/// </summary>
public sealed class PlayStatisticsTests
{
    /// <summary>AC-601: a status that landed is counted under its own name.</summary>
    [Fact]
    public void DebuffsAreCountedByType()
    {
        var debuffs = new DebuffCounters();

        debuffs.Record("Breast");
        debuffs.Record("Breast");
        debuffs.Record("Parasite");

        Assert.Equal(2, debuffs.CountFor("Breast"));
        Assert.Equal(1, debuffs.CountFor("Parasite"));
        Assert.Equal(3, debuffs.Total);
        Assert.Equal(0, debuffs.CountFor("Milk"));
    }

    /// <summary>AC-602: the enemy with the most climaxes is the one named.</summary>
    [Fact]
    public void TheTopActorIsTheMostCounted()
    {
        var actors = new ActorClimaxLedger();

        actors.Record("Worm");
        actors.Record("Worm");
        actors.Record("Slime");

        KeyValuePair<string, int>? top = actors.TopActor();

        Assert.NotNull(top);
        Assert.Equal("Worm", top!.Value.Key);
        Assert.Equal(2, top.Value.Value);
    }

    /// <summary>
    /// AC-603: an unidentified captor still counts, but is never named as the worst.
    ///
    /// Both halves matter. Dropping it would make the total disagree with the climaxes that
    /// actually happened, and naming it would tell the player they lost most often to "unknown".
    /// </summary>
    [Fact]
    public void AnUnidentifiedCaptorIsCountedButNeverTop()
    {
        var actors = new ActorClimaxLedger();

        actors.Record(null);
        actors.Record(null);
        actors.Record("   ");
        actors.Record("Worm");

        Assert.Equal(3, actors.CountFor(ActorClimaxLedger.UnknownActorId));
        Assert.Equal(4, actors.Total);

        KeyValuePair<string, int>? top = actors.TopActor();
        Assert.NotNull(top);
        Assert.Equal("Worm", top!.Value.Key);
    }

    /// <summary>There is no enemy to name when only unidentified ones have been met.</summary>
    [Fact]
    public void NothingIsTopWhenNoCaptorWasEverIdentified()
    {
        var actors = new ActorClimaxLedger();
        actors.Record(null);

        Assert.Null(actors.TopActor());
        Assert.Equal(1, actors.Total);
    }

    /// <summary>
    /// DEC-604: a tie is broken by identifier so the page does not swap names between polls.
    /// </summary>
    [Fact]
    public void ATieIsBrokenByIdentifier()
    {
        var actors = new ActorClimaxLedger();
        actors.Record("Worm");
        actors.Record("Ant");

        Assert.Equal("Ant", actors.TopActor()!.Value.Key);
        Assert.Equal(new[] { "Ant", "Worm" }, actors.Ordered().Select(entry => entry.Key));
    }

    /// <summary>
    /// FR-613: the game's name for a status is kept with its count.
    ///
    /// It cannot be derived from the type. The game's keys are spelled three different ways
    /// (<c>ID_Ab_ParasiteLv1</c>, <c>ID_Ab_LustMarkCurse_Lv1</c>, <c>ID_Ab_Spore1</c>) and 34 of the
    /// 71 types have no key at all, so the only reliable source is what the game said at the moment
    /// the status landed (付録A A-603).
    /// </summary>
    [Fact]
    public void TheNameTheGameGaveAStatusIsKeptWithItsCount()
    {
        var debuffs = new DebuffCounters();

        debuffs.Record("Breast", "膨乳");
        debuffs.Record("Parasite", "寄生Lv1");

        Assert.Equal("膨乳", debuffs.DisplayNameFor("Breast"));
        Assert.Equal("寄生Lv1", debuffs.DisplayNameFor("Parasite"));
        Assert.Null(debuffs.DisplayNameFor("Lustfull"));
    }

    /// <summary>
    /// A levelled status is named per level, so the latest reading wins — it is the name the player
    /// most recently saw. An application the game could not name leaves the last good one standing
    /// rather than blanking it.
    /// </summary>
    [Fact]
    public void TheLatestNameWinsAndAMissingOneDoesNotErase()
    {
        var debuffs = new DebuffCounters();

        debuffs.Record("LustMarkCurse", "淫紋の呪い");
        debuffs.Record("LustMarkCurse", "淫紋");
        Assert.Equal("淫紋", debuffs.DisplayNameFor("LustMarkCurse"));

        debuffs.Record("LustMarkCurse", null);
        Assert.Equal("淫紋", debuffs.DisplayNameFor("LustMarkCurse"));
        Assert.Equal(3, debuffs.CountFor("LustMarkCurse"));
    }

    /// <summary>Counting without a name still counts. The diary is about the count first.</summary>
    [Fact]
    public void AStatusWithNoNameIsStillCounted()
    {
        var debuffs = new DebuffCounters();
        debuffs.Record("MindControl");

        Assert.Equal(1, debuffs.CountFor("MindControl"));
        Assert.Null(debuffs.DisplayNameFor("MindControl"));
    }

    /// <summary>Most counted first, so the page's tables read as a ranking.</summary>
    [Fact]
    public void OrderingPutsTheMostCountedFirst()
    {
        var debuffs = new DebuffCounters();
        debuffs.Record("Milk");
        debuffs.Record("Breast");
        debuffs.Record("Breast");

        Assert.Equal(new[] { "Breast", "Milk" }, debuffs.Ordered().Select(entry => entry.Key));
    }

    /// <summary>
    /// FR-603: nothing counted can be taken back. The type exposes no way to, which is the point —
    /// the rule is enforced by the shape of the API rather than by every caller remembering it.
    /// </summary>
    [Fact]
    public void RecordingOnlyEverAdds()
    {
        var tally = new LifetimeTally();

        tally.Add("a");
        tally.Add("a");
        tally.Add("");
        tally.Add("   ");

        Assert.Equal(2, tally.CountFor("a"));
        Assert.Equal(2, tally.Total);
        Assert.Equal(1, tally.DistinctKeys);
    }

    /// <summary>A damaged file cannot make a count negative or invent a nameless row.</summary>
    [Fact]
    public void LoadingIgnoresRowsThatCannotHaveBeenCounted()
    {
        var tally = new LifetimeTally();

        tally.LoadFrom(new[]
        {
            new KeyValuePair<string, int>("a", 3),
            new KeyValuePair<string, int>("b", 0),
            new KeyValuePair<string, int>("c", -4),
            new KeyValuePair<string, int>(" ", 9),
        });

        Assert.Equal(3, tally.CountFor("a"));
        Assert.Equal(3, tally.Total);
        Assert.Equal(1, tally.DistinctKeys);
    }

    /// <summary>
    /// A hand-edited file with the same key twice keeps its total rather than losing one of them.
    /// These counts can never be earned back, so the least destructive reading wins.
    /// </summary>
    [Fact]
    public void DuplicateRowsAreSummedRatherThanOverwritten()
    {
        var tally = new LifetimeTally();

        tally.LoadFrom(new[]
        {
            new KeyValuePair<string, int>("a", 3),
            new KeyValuePair<string, int>("a", 4),
        });

        Assert.Equal(7, tally.CountFor("a"));
    }

    /// <summary>
    /// AC-604: a save point clears the climax count and leaves the diary alone.
    ///
    /// The two are separate objects on purpose; this pins that they stay separate, because a future
    /// reset that swept "everything climax-related" would silently erase the record.
    /// </summary>
    [Fact]
    public void ResettingTheClimaxCountLeavesTheDiaryStanding()
    {
        var ledger = new ClimaxLedger();
        var actors = new ActorClimaxLedger();
        var debuffs = new DebuffCounters();

        ledger.Record();
        actors.Record("Worm");
        debuffs.Record("Breast");

        ledger.ResetCount();

        Assert.Equal(0, ledger.Count);
        Assert.Equal(1, actors.CountFor("Worm"));
        Assert.Equal(1, debuffs.CountFor("Breast"));
    }
}

/// <summary>
/// The reading the page is served (SPEC006 4.5).
/// </summary>
public sealed class StatsSnapshotTests
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 10, 12, 34, 56, TimeSpan.Zero);

    private static StatsSnapshot Build(
        ActorClimaxLedger actors,
        DebuffCounters debuffs,
        Func<string, string?>? actorName = null,
        Func<string, string?>? debuffName = null) =>
        StatsSnapshot.Build(42.5f, 100f, 3, 12, actors, debuffs, actorName, debuffName, At);

    /// <summary>AC-607: every figure the page shows is in one reading.</summary>
    [Fact]
    public void TheSnapshotCarriesEverythingThePageShows()
    {
        var actors = new ActorClimaxLedger();
        actors.Record("Worm");
        var debuffs = new DebuffCounters();
        debuffs.Record("Breast");

        StatsSnapshot snapshot = Build(actors, debuffs);

        Assert.Equal(42.5f, snapshot.Corruption.Value);
        Assert.Equal(100f, snapshot.Corruption.Cap);
        Assert.Equal(3, snapshot.Climax.Count);
        Assert.Equal(12, snapshot.Climax.Limit);
        Assert.Equal("Worm", snapshot.TopActor!.ActorId);
        Assert.Single(snapshot.ActorClimaxCounts);
        Assert.Single(snapshot.DebuffCounts);
        Assert.Equal("2026-08-10T12:34:56Z", snapshot.GeneratedAt);
    }

    /// <summary>AC-613: the game's own words are used when it has them.</summary>
    [Fact]
    public void DisplayNamesComeFromTheGame()
    {
        var actors = new ActorClimaxLedger();
        actors.Record("Worm");
        var debuffs = new DebuffCounters();
        debuffs.Record("Breast");

        StatsSnapshot snapshot = Build(
            actors,
            debuffs,
            actorName: id => id == "Worm" ? "大口のワーム" : null,
            debuffName: type => type == "Breast" ? "膨乳" : null);

        Assert.Equal("大口のワーム", snapshot.ActorClimaxCounts[0].DisplayName);
        Assert.Equal("大口のワーム", snapshot.TopActor!.DisplayName);
        Assert.Equal("膨乳", snapshot.DebuffCounts[0].DisplayName);
    }

    /// <summary>
    /// AC-613: a name the game cannot supply is left null, and the page prints the raw identifier.
    /// A blank answer counts as no answer rather than as an empty name.
    /// </summary>
    [Fact]
    public void AMissingNameIsLeftForThePageToStandIn()
    {
        var debuffs = new DebuffCounters();
        debuffs.Record("Breast");

        StatsSnapshot snapshot = Build(
            new ActorClimaxLedger(), debuffs, debuffName: _ => "   ");

        Assert.Null(snapshot.DebuffCounts[0].DisplayName);
        Assert.Equal("Breast", snapshot.DebuffCounts[0].AbnormalType);
    }

    /// <summary>The reserved bucket is never handed to the game: there is no enemy there to name.</summary>
    [Fact]
    public void TheUnknownBucketIsNeverLookedUp()
    {
        var actors = new ActorClimaxLedger();
        actors.Record(null);

        StatsSnapshot snapshot = Build(
            actors,
            new DebuffCounters(),
            actorName: _ => throw new InvalidOperationException("the unknown bucket must not be resolved"));

        Assert.Null(snapshot.TopActor);
        Assert.Equal(ActorClimaxLedger.UnknownActorId, snapshot.ActorClimaxCounts[0].ActorId);
        Assert.Null(snapshot.ActorClimaxCounts[0].DisplayName);
    }

    /// <summary>
    /// A resolver that throws costs a name, never the reading. It reaches into the game across the
    /// interop boundary, and a browser poll must not be able to carry that failure any further.
    /// </summary>
    [Fact]
    public void AThrowingResolverDoesNotTakeTheReadingDown()
    {
        var actors = new ActorClimaxLedger();
        actors.Record("Worm");

        StatsSnapshot snapshot = Build(
            actors, new DebuffCounters(), actorName: _ => throw new InvalidOperationException("boom"));

        Assert.Null(snapshot.TopActor!.DisplayName);
        Assert.Equal("Worm", snapshot.TopActor.ActorId);
    }
}
