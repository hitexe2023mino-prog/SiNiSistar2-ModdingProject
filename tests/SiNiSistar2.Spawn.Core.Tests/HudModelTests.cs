using SiNiSistar2.Spawn.Core;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

public class HudModelTests
{
    private static HudSnapshot Baseline() => new()
    {
        AreaName = "Road_1_0",
        VisitCount = 2,
        Seeded = true,
        Seed = 12345,
        TuningApplied = true,
        TunedSpawnerCount = 3,
        SpawnerCount = 3,
        HardBase = true,
        SpawnCountMultiplier = 1.4f,
        SpawnIntervalMultiplier = 0.8f,
        CoolTimeMultiplier = 0.9f,
        MaxSpawnMultiplier = 1.2f,
        Dwell = 45,
        StagnationSeconds = 90,
        WindowTravel = 1.5f,
        MoveEpsilon = 3f,
        SecondsUntilNextPenalty = 45,
        Spawned = 1,
        SpawnCap = 5,
        Alive = 1,
        AliveCap = 3,
        OffscreenCandidates = 4,
        BehindCandidates = 2,
    };

    [Fact]
    public void ModeCyclesThroughAllThreeStages()
    {
        Assert.Equal(HudMode.Compact, HudModel.Next(HudMode.Off));
        Assert.Equal(HudMode.Full, HudModel.Next(HudMode.Compact));
        Assert.Equal(HudMode.Off, HudModel.Next(HudMode.Full));
    }

    [Fact]
    public void CompactCarriesAreaBudgetAndDwell()
    {
        string line = HudModel.Compact(Baseline());

        Assert.Contains("Road_1_0 #2", line);
        Assert.Contains("add 1/5", line);
        Assert.Contains("alive 1/3", line);
        Assert.Contains("dwell 45s/90s", line);
    }

    [Fact]
    public void CompactReportsExclusionInsteadOfState()
    {
        string line = HudModel.Compact(Baseline() with { Excluded = true, ExclusionSource = "areas.json" });

        Assert.Contains("excluded (areas.json)", line);
        Assert.DoesNotContain("dwell", line);
    }

    [Fact]
    public void CompactShowsTheStagnationCountdownOnceStagnant()
    {
        string line = HudModel.Compact(Baseline() with { Stagnant = true, SecondsUntilNextPenalty = 12.5 });

        Assert.Contains("stagnant, next 12.5s", line);
    }

    [Fact]
    public void CompactNamesThePauseReason()
    {
        Assert.Contains("paused:hold", HudModel.Compact(Baseline() with { Paused = StagnationPause.Held }));
        Assert.Contains("paused:event", HudModel.Compact(Baseline() with { Paused = StagnationPause.Cinematic }));
    }

    /// <summary>FR-330: the four quantities the in-game acceptance criteria are judged on.</summary>
    [Fact]
    public void FullCoversStagnationBudgetCandidatesAndBoxes()
    {
        IReadOnlyList<string> lines = HudModel.Full(Baseline() with
        {
            MimicEnabled = true,
            MimicPoolPresent = true,
            MimicPlaced = 2,
            MimicCap = 2,
            MimicUnresolved = 1,
            MimicHits = 1,
        });

        string text = string.Join("\n", lines);
        Assert.Contains("dwell 45s/90s", text);
        Assert.Contains("moved 1.5/3", text);
        Assert.Contains("1/5 this visit, alive 1/3", text);
        Assert.Contains("offscreen 4, of which behind 2", text);
        Assert.Contains("placed 2/2", text);
    }

    [Fact]
    public void FullNamesTheHardBaseForTheDifficultyModCheck()
    {
        Assert.Contains(HudModel.Full(Baseline()), x => x.Contains("base Hard"));
        Assert.Contains(HudModel.Full(Baseline() with { HardBase = false }), x => x.Contains("base Normal"));
    }

    [Fact]
    public void FullStopsAtTheExclusionNotice()
    {
        IReadOnlyList<string> lines = HudModel.Full(Baseline() with { Excluded = true, ExclusionSource = "default" });

        Assert.Equal(2, lines.Count);
        Assert.Contains("excluded by default", lines[1]);
    }

    [Fact]
    public void FullExplainsWhyNoSpawnCanHappenWhenNoPositionQualifies()
    {
        IReadOnlyList<string> lines = HudModel.Full(Baseline() with { OffscreenCandidates = 0, BehindCandidates = 0 });

        Assert.Contains(lines, x => x.Contains("no eligible position"));
    }

    [Fact]
    public void FullDistinguishesDisabledBoxesFromAnAreaWithoutMimics()
    {
        Assert.Contains(
            HudModel.Full(Baseline() with { MimicEnabled = false }),
            x => x.Contains("disabled"));
        Assert.Contains(
            HudModel.Full(Baseline() with { MimicEnabled = true, MimicPoolPresent = false }),
            x => x.Contains("no mimic pool"));
    }

    /// <summary>FR-333: a pinned outcome has to be visible, or a forced result reads as a natural draw.</summary>
    [Fact]
    public void FullShowsAPinnedLotteryOutcome()
    {
        IReadOnlyList<string> lines = HudModel.Full(Baseline() with
        {
            MimicEnabled = true,
            MimicPoolPresent = true,
            PinnedOutcome = "REWARD",
        });

        Assert.Contains(lines, x => x.Contains("PINNED->REWARD"));
    }

    /// <summary>
    /// FR-334/FR-337: with commands off, the panel must point at the switch that turns them on.
    /// Pointing at a config file and a restart put the fix further away than the problem.
    /// </summary>
    [Fact]
    public void DebugHeaderPointsAtTheSwitchWhenCommandsAreOff()
    {
        IReadOnlyList<string> lines = HudModel.DebugHeader(Baseline() with { DebugCommandsEnabled = false });

        Assert.Contains("OFF", lines[0]);
        Assert.Contains("switch", string.Join("\n", lines));
    }

    /// <summary>An attempt that does nothing has to say so; silence is what made this look broken.</summary>
    [Fact]
    public void DebugHeaderAcknowledgesAnAttemptMadeWhileDisabled()
    {
        IReadOnlyList<string> quiet = HudModel.DebugHeader(Baseline() with { DebugCommandsEnabled = false });
        IReadOnlyList<string> pressed = HudModel.DebugHeader(
            Baseline() with { DebugCommandsEnabled = false }, pressedWhileDisabled: true);

        Assert.DoesNotContain("DID NOTHING", quiet[0]);
        Assert.Contains("DID NOTHING", pressed[0]);
    }

    [Fact]
    public void DebugHeaderStatesTheCapsStillApplyWhenOn()
    {
        string text = string.Join("\n", HudModel.DebugHeader(Baseline() with { DebugCommandsEnabled = true }));

        Assert.Contains("ON", text);
        Assert.Contains("Caps, area exclusion", text);
    }

    /// <summary>The switch label has to state what a click does, in both states.</summary>
    [Fact]
    public void TheSwitchLabelSaysWhatAClickWillDo()
    {
        Assert.Contains("turn OFF", HudModel.ToggleLabel(enabled: true));
        Assert.Contains("turn ON", HudModel.ToggleLabel(enabled: false));
        Assert.Contains(HudModel.ToggleKey.ToString(), HudModel.ToggleLabel(enabled: false));
    }

    /// <summary>
    /// The enable key must not double as a disable key. A real session turned the tool off five
    /// times by pressing it again when a command legitimately did nothing.
    /// </summary>
    [Fact]
    public void TheEnableKeyIsDistinctFromTheDisableCommandAndFromEveryCommand()
    {
        Assert.NotEqual(HudModel.ToggleKey, HudModel.DisableCommand);
        Assert.DoesNotContain(HudModel.Commands, x => x.Key == HudModel.ToggleKey);
        Assert.DoesNotContain(HudModel.Commands, x => x.Key == HudModel.DisableCommand);
    }

    [Fact]
    public void CommandLabelsCarryTheirKey()
    {
        foreach ((char key, string text) in HudModel.Commands)
        {
            Assert.Contains($"[{key}]", HudModel.CommandLabel(key, text));
            Assert.Contains(text, HudModel.CommandLabel(key, text));
        }
    }

    /// <summary>
    /// An area with no spawner is the one state where every other number on the panel is
    /// meaningless, and it is what a real session hit first. The two counts have to be separable:
    /// this build fills ordinary areas with enemies placed directly, so "no spawner" and "no
    /// enemy" are different findings and the panel must not conflate them.
    /// </summary>
    [Fact]
    public void FullDistinguishesNoSpawnerFromNoEnemy()
    {
        IReadOnlyList<string> populated = HudModel.Full(Baseline() with
        {
            SpawnerCount = 0,
            TunedSpawnerCount = 0,
            SceneEnemyCount = 8,
        });

        Assert.Contains(populated, x => x.Contains("8 enemies are placed directly"));
        Assert.Contains(populated, x => x.Contains("spawners 0"));
        Assert.Contains(populated, x => x.Contains("enemies 8"));

        IReadOnlyList<string> empty = HudModel.Full(Baseline() with
        {
            SpawnerCount = 0,
            TunedSpawnerCount = 0,
            SceneEnemyCount = 0,
        });

        Assert.Contains(empty, x => x.Contains("no EnemySpawner and no enemy"));
    }

    [Fact]
    public void FullDoesNotClaimAnEmptySceneWhenSpawnersExist()
    {
        IReadOnlyList<string> lines = HudModel.Full(Baseline() with { SpawnerCount = 3 });

        Assert.DoesNotContain(lines, x => x.Contains("no EnemySpawner in this scene"));
    }

    /// <summary>Every command must be reachable as its own button, not just as a printed hint.</summary>
    [Fact]
    public void EveryCommandHasALabelledButton()
    {
        Assert.NotEmpty(HudModel.Commands);

        foreach ((char key, string text) in HudModel.Commands)
        {
            string label = HudModel.CommandLabel(key, text);
            Assert.False(string.IsNullOrWhiteSpace(label));
        }
    }

    [Fact]
    public void CommandKeysAreUnique()
    {
        Assert.Equal(
            HudModel.Commands.Count,
            HudModel.Commands.Select(x => x.Key).Distinct().Count());
    }
}
