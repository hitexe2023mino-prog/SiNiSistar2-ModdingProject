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

    /// <summary>FR-331: the panel is readable when commands are off, and says why they do nothing.</summary>
    [Fact]
    public void DebugPanelStatesWhenCommandsAreDisabled()
    {
        IReadOnlyList<string> lines = HudModel.DebugPanel(Baseline() with { DebugCommandsEnabled = false });

        Assert.Contains(lines, x => x.Contains("DebugCommandsEnabled = true"));
    }

    [Fact]
    public void DebugPanelListsEveryCommandKey()
    {
        IReadOnlyList<string> lines = HudModel.DebugPanel(Baseline() with { DebugCommandsEnabled = true });

        foreach ((char key, _) in HudModel.Commands)
        {
            Assert.Contains(lines, x => x.Contains($"[{key}]"));
        }

        Assert.Contains(lines, x => x.Contains("Caps, area exclusion"));
    }

    [Fact]
    public void CommandKeysAreUnique()
    {
        Assert.Equal(
            HudModel.Commands.Count,
            HudModel.Commands.Select(x => x.Key).Distinct().Count());
    }
}
