using System.Text.RegularExpressions;

namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// SPEC002 defines the MOD as much by what it must not touch as by what it does. Those rules are
/// invisible in behaviour until they are already broken: acting on the defilement escape axis or
/// on the hold predicates produces a plausible-looking game that has quietly voided a design
/// decision or the EDI MOD's trigger identity.
///
/// The plugin source is scanned so the rules fail a build rather than a play session
/// (SPEC002 FR-112, FR-113, FR-121, FR-129, FR-130, FR-131).
/// </summary>
public sealed class ForbiddenSurfaceTests
{
    private static readonly string[] PluginSources = LoadPluginSources();

    /// <summary>
    /// AC-112: the defilement-driven escape difficulty is the game's own axis. The MOD reaches its
    /// difficulty increase through a time band instead, so it must not name these members at all
    /// (SPEC002 DEC-102).
    /// </summary>
    [Theory]
    [InlineData("GachaInputRateDefilement")]
    [InlineData("m_DefilementBind")]
    public void TheDefilementEscapeAxisIsNeverReferenced(string member)
    {
        AssertAbsent(member);
    }

    /// <summary>
    /// AC-112: the struggle meter's own numbers stay untouched, so defilement's escalation is not
    /// applied twice to the same quantity (SPEC002 FR-113).
    /// </summary>
    [Theory]
    [InlineData("GachaInputRate")]
    [InlineData("m_SuccessValue")]
    [InlineData("m_DeclineValue")]
    [InlineData("m_SuccessValueHard")]
    [InlineData("m_DeclineValueHard")]
    public void TheStruggleMeterNumbersAreNeverWritten(string member)
    {
        AssertAbsent(member);
    }

    /// <summary>
    /// AC-121: re-capture is made likelier by movement and invincibility only. These predicates sit
    /// upstream of the Lelia.IsHold that the EDI MOD reads to identify its hold trigger, so forcing
    /// them could make a hold look like it began when it did not (SPEC002 FR-121, DEC-105).
    /// </summary>
    [Theory]
    [InlineData("IsHoldable")]
    [InlineData("DisableHoldMsv")]
    [InlineData("Bindable")]
    public void TheHoldPredicatesAreNeverTouched(string member)
    {
        AssertAbsent(member);
    }

    /// <summary>
    /// AC-127: the EDI MOD derives trigger identity from the animator and the gallery identifiers,
    /// and detects pause from timeScale. Writing any of them breaks its trigger matching
    /// (SPEC002 FR-129, 7.1).
    /// </summary>
    [Theory]
    [InlineData("timeScale")]
    [InlineData("GalleryEnemyID")]
    [InlineData("RuntimeAnimatorController")]
    [InlineData("m_TakeName")]
    public void TheSurfacesTheEdiModDependsOnAreNeverTouched(string member)
    {
        AssertAbsent(member);
    }

    /// <summary>
    /// FR-130: the MOD must never make AbnormalList.Has report a status the player does not have,
    /// because the EDI MOD picks its idle filler from exactly that answer (SPEC002 7.1).
    /// </summary>
    [Fact]
    public void StatusesAreNeverAddedOrRemovedByTheMod()
    {
        AssertAbsent("AddAbnormal(");
        AssertAbsent("RemoveAbnormal");
        AssertAbsent("AllClear");
    }

    /// <summary>
    /// AC-128: declaring a dependency on the EDI MOD would stop this MOD loading without it, and
    /// the two are meant to be installable on their own (SPEC002 FR-131, DEC-111).
    /// </summary>
    [Fact]
    public void NoDependencyOnAnotherPluginIsDeclared()
    {
        AssertAbsent("BepInDependency");
        AssertAbsent("community.sinisistar2.edi");
    }

    /// <summary>
    /// FR-104: the saved difficulty is never written. Only the static check-side accessors are
    /// patched (SPEC002 DEC-101).
    /// </summary>
    [Fact]
    public void TheSavedDifficultyIsNeverWritten()
    {
        AssertAbsent("GameDifficultyRP");
        AssertAbsent("set_GameDifficulty");
    }

    private static void AssertAbsent(string needle)
    {
        var hits = PluginSources
            .Where(x => x.Contains(needle, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            hits.Length == 0,
            $"The plugin source names '{needle}', which SPEC002 forbids. Found in {hits.Length} file(s).");
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
        string plugin = Path.Combine(directory!.FullName, "src", "SiNiSistar2.Difficulty.Plugin");
        Assert.True(Directory.Exists(plugin), $"Plugin source directory not found at {plugin}.");

        return Directory.GetFiles(plugin, "*.cs", SearchOption.AllDirectories)
            .Select(x => StripComments(File.ReadAllText(x)))
            .ToArray();
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }
}
