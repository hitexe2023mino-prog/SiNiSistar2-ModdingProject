using SiNiSistar2.Spawn.Core;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

public class SpawnProfileFactoryTests
{
    private static readonly string[] KnownScenes = { "Road_1_0", "Dungeon_Cave1" };

    [Fact]
    public void DefaultsValidateCleanly()
    {
        ProfileValidation validation = SpawnProfileFactory.Create(new SpawnOptions(), "", KnownScenes);

        Assert.Empty(validation.Errors);
        Assert.True(validation.Profile.Enabled);
        Assert.False(validation.Profile.MimicBoxEnabled);
        Assert.False(validation.Profile.GimmickCloningEnabled);
        Assert.Equal(2, validation.Profile.RewardTable.Entries.Count);
    }

    [Fact]
    public void CountRangeBelowOneFallsBackToDefault()
    {
        var options = new SpawnOptions { SpawnCountMultiplierMin = 0.5f, SpawnCountMultiplierMax = 2f };
        ProfileValidation validation = SpawnProfileFactory.Create(options, "", KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("SpawnCountMultiplier"));
        Assert.Equal(new MultiplierRange(1.0f, 1.5f), validation.Profile.SpawnCount);
    }

    [Fact]
    public void IntervalRangeAboveOneFallsBackToDefault()
    {
        var options = new SpawnOptions { SpawnIntervalMultiplierMin = 0.9f, SpawnIntervalMultiplierMax = 1.4f };
        ProfileValidation validation = SpawnProfileFactory.Create(options, "", KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("SpawnIntervalMultiplier"));
        Assert.Equal(new MultiplierRange(0.7f, 1.0f), validation.Profile.SpawnInterval);
    }

    [Fact]
    public void MinAboveMaxIsRejected()
    {
        var options = new SpawnOptions { CoolTimeMultiplierMin = 0.9f, CoolTimeMultiplierMax = 0.5f };
        ProfileValidation validation = SpawnProfileFactory.Create(options, "", KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("CoolTimeMultiplier"));
    }

    [Fact]
    public void ProbabilityOutOfRangeFallsBack()
    {
        var options = new SpawnOptions { MimicChance = 1.5f, AmbushChance = -0.1f };
        ProfileValidation validation = SpawnProfileFactory.Create(options, "", KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("MimicChance"));
        Assert.Contains(validation.Errors, e => e.Contains("AmbushChance"));
        Assert.Equal(0.5f, validation.Profile.MimicChance);
        Assert.Equal(0.25f, validation.Profile.AmbushChance);
    }

    [Fact]
    public void UnknownSceneOverrideIsDroppedWithError()
    {
        const string json = """{"NoSuchScene": {"ambushChance": 1.0}, "Road_1_0": {"ambushChance": 1.0}}""";
        ProfileValidation validation = SpawnProfileFactory.Create(new SpawnOptions(), json, KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("NoSuchScene"));
        Assert.True(validation.Profile.AreaOverrides.ContainsKey("Road_1_0"));
        Assert.False(validation.Profile.AreaOverrides.ContainsKey("NoSuchScene"));
    }

    [Fact]
    public void MalformedAreasJsonIsIgnoredWithError()
    {
        ProfileValidation validation = SpawnProfileFactory.Create(new SpawnOptions(), "{not json", KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("areas.json"));
        Assert.Empty(validation.Profile.AreaOverrides);
    }

    [Fact]
    public void AreaOverrideResolvesOnTopOfGlobals()
    {
        const string json = """{"Road_1_0": {"spawnCountMultiplierMin": 2.0, "spawnCountMultiplierMax": 3.0, "excluded": false}}""";
        ProfileValidation validation = SpawnProfileFactory.Create(new SpawnOptions(), json, KnownScenes);

        AreaSettings road = validation.Profile.Resolve("Road_1_0", excludedByDefault: true);
        Assert.False(road.Excluded);
        Assert.Equal(new MultiplierRange(2f, 3f), road.SpawnCount);
        Assert.Equal(validation.Profile.SpawnInterval, road.SpawnInterval);

        AreaSettings other = validation.Profile.Resolve("Dungeon_Cave1", excludedByDefault: false);
        Assert.False(other.Excluded);
        Assert.Equal(validation.Profile.SpawnCount, other.SpawnCount);
    }

    [Fact]
    public void DefaultExclusionHoldsUnlessExplicitlyLifted()
    {
        ProfileValidation validation = SpawnProfileFactory.Create(new SpawnOptions(), "", KnownScenes);
        Assert.True(validation.Profile.Resolve("Dungeon_Cave1", excludedByDefault: true).Excluded);
    }

    [Fact]
    public void MimicEnabledWithEmptyRewardsWarns()
    {
        var options = new SpawnOptions { MimicBoxEnabled = true, RewardTable = "bogus" };
        ProfileValidation validation = SpawnProfileFactory.Create(options, "", KnownScenes);

        Assert.Contains(validation.Errors, e => e.Contains("bogus"));
        Assert.Contains(validation.Warnings, w => w.Contains("RewardTable"));
    }
}
