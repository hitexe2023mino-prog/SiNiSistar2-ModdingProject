using System.Text.RegularExpressions;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

/// <summary>
/// SPEC004's "must not touch" rules fail a build here rather than a play session, the same way
/// the difficulty MOD guards its own forbidden surface (SPEC004 FR-302, FR-305, FR-315, FR-322,
/// FR-324, 7.1).
/// </summary>
public sealed class ForbiddenSurfaceTests
{
    private static readonly string[] PluginSources = LoadPluginSources();

    /// <summary>
    /// FR-305 / DEC-309: the Hard override fields are the game's own difficulty data. They are
    /// read to resolve the base value and never written, so removing the MOD (or the difficulty
    /// MOD) leaves them exactly as authored. Assignment is the forbidden form; reads appear
    /// throughout the tuning code.
    /// </summary>
    [Theory]
    [InlineData("m_SpawnCountHard")]
    [InlineData("m_SpawnIntervalHard")]
    [InlineData("m_CoolTimeHard")]
    [InlineData("m_CoolTimeFirstHard")]
    [InlineData("m_MaxSpawnHard")]
    [InlineData("m_SpawnCountHasHardModeOverride")]
    [InlineData("m_SpawnIntervalHasHardModeOverride")]
    [InlineData("m_CoolTimeHasHardModeOverride")]
    [InlineData("m_CoolTimeFirstHasHardModeOverride")]
    [InlineData("m_MaxSpawnHasHardModeOverride")]
    public void TheHardOverrideFieldsAreNeverAssigned(string member)
    {
        var assignment = new Regex($@"{Regex.Escape(member)}\s*=(?!=)");
        string[] hits = PluginSources.Where(x => assignment.IsMatch(x)).ToArray();
        Assert.True(
            hits.Length == 0,
            $"The plugin source assigns '{member}', which SPEC004 FR-305 forbids. Found in {hits.Length} file(s).");
    }

    /// <summary>
    /// FR-324: real treasure boxes stay untouched — the pseudo box never goes near the flag, the
    /// contents or the got-item flow. The parameter type may be counted (diagnostics) but its
    /// members must never be named.
    /// </summary>
    [Theory]
    [InlineData("m_TreasureBoxFlag")]
    [InlineData("m_Relics")]
    [InlineData("TreasureGot")]
    [InlineData("TreasureBoxFlag.")]
    public void TheRealTreasureBoxesAreNeverTouched(string member)
    {
        AssertAbsent(member);
    }

    /// <summary>
    /// FR-322 / 付録A A-11: the miss body is removed by deactivation only. A kill would write
    /// defeat records (gallery, achievements, drop flow), which is exactly what the non-death
    /// path exists to avoid.
    /// </summary>
    [Fact]
    public void TheMissBodyIsNeverKilled()
    {
        AssertAbsent("ForceDeadDamage");
        AssertAbsent("AddAbnormal(");
    }

    /// <summary>FR-315: the game's RNG streams are never consumed.</summary>
    [Fact]
    public void TheGameRandomIsNeverConsumed()
    {
        AssertAbsent("UnityEngine.Random");
    }

    /// <summary>FR-302: the saved difficulty and the save files are never written.</summary>
    [Fact]
    public void TheSaveIsNeverWritten()
    {
        AssertAbsent("GameDifficultyRP");
        AssertAbsent("s_GameDifficultyForCheck");
        AssertAbsent("MainSaveData");
    }

    /// <summary>
    /// 7.3: the MOD works alone and never requires (or names) another plugin of this repository.
    /// </summary>
    [Fact]
    public void NoDependencyOnAnotherPluginIsDeclared()
    {
        AssertAbsent("BepInDependency");
        AssertAbsent("community.sinisistar2.edi");
        AssertAbsent("community.sinisistar2.difficulty");
        AssertAbsent("community.sinisistar2.pleasure");
    }

    /// <summary>
    /// FR-328 / DEC-315: the HUD is an IMGUI overlay. Touching the game's Canvas, its UI objects
    /// or the time scale is what would put it in the way of SPEC001's trigger identity and
    /// SPEC003's own overlay.
    /// </summary>
    [Theory]
    [InlineData("timeScale")]
    [InlineData("UIList")]
    [InlineData("Canvas")]
    [InlineData("GameObject.Find")]
    public void TheHudNeverReachesIntoTheGamesUi(string member)
    {
        AssertAbsent(member);
    }

    /// <summary>
    /// FR-327: the HUD reads a snapshot the observer hands it. If it could see the profile or the
    /// managers directly, "read-only" would rest on review rather than on structure.
    /// </summary>
    [Fact]
    public void TheHudDrawsOnlyFromItsSnapshot()
    {
        string[] hud = PluginSources
            .Where(x => x.Contains("class SpawnHud", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(hud);
        Assert.DoesNotContain("ManagerList", hud[0]);
        Assert.DoesNotContain("SpawnRuntime.Profile", hud[0]);
    }

    /// <summary>
    /// UniTask-returning members must not be called. Their exceptions surface asynchronously,
    /// so a try/catch around the call reports success while the game has already faulted.
    ///
    /// This caught a real defect: calling <c>StartTask</c> on a copied enemy registered its state
    /// machine a second time and threw "Key: Ready" inside the async machinery. The copies stopped
    /// walking and the MOD's own log claimed the step had succeeded. SPEC003 DEC-259 records the
    /// same hazard from the patching side.
    /// </summary>
    [Theory]
    [InlineData("StartTask(")]
    [InlineData("TaskSpawn(")]
    [InlineData("SpawnLogic(")]
    [InlineData("PlayItemEvent(")]
    public void UniTaskReturningMembersAreNeverCalled(string member)
    {
        AssertAbsent(member);
    }

    private static void AssertAbsent(string needle)
    {
        string[] hits = PluginSources
            .Where(x => x.Contains(needle, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            hits.Length == 0,
            $"The plugin source names '{needle}', which SPEC004 forbids. Found in {hits.Length} file(s).");
    }

    /// <summary>
    /// Reads the plugin's own source, with comments stripped so that naming a forbidden member in
    /// an explanation of why it is not used does not fail the test.
    /// </summary>
    private static string[] LoadPluginSources()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SiNiSistar2.Edi.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string plugin = Path.Combine(directory!.FullName, "src", "SiNiSistar2.Spawn.Plugin");
        Assert.True(Directory.Exists(plugin), $"Plugin source directory not found at {plugin}.");

        return Directory.GetFiles(plugin, "*.cs", SearchOption.AllDirectories)
            .Select(x => StripComments(File.ReadAllText(x)))
            .ToArray();
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\r\n]*", string.Empty);
    }
}
