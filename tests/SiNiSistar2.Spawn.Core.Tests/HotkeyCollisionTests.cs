using System.Text.RegularExpressions;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

/// <summary>
/// The MODs in this game folder share one keyboard and no arbiter: two plugins on one key both
/// act, and the second feature looks broken rather than double-bound.
///
/// This caught a real defect. The debug panel shipped on F6, which is the funscript authoring
/// GUI's key — invisible to a search for <c>KeyCode.F6</c> because that plugin stores its key as a
/// configured *string*. Both spellings are scanned here so the next default cannot repeat it.
/// </summary>
public sealed class HotkeyCollisionTests
{
    private static readonly Regex KeyCodeLiteral = new(@"KeyCode\.(F\d{1,2})\b");
    private static readonly Regex QuotedKeyName = new("\"(F\\d{1,2})\"");

    [Fact]
    public void TheSpawnModsDefaultsAreNotTakenByAnotherMod()
    {
        string root = RepositoryRoot();
        string ownPlugin = Path.Combine(root, "src", "SiNiSistar2.Spawn.Plugin");

        var taken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string directory in Directory.GetDirectories(Path.Combine(root, "src")))
        {
            if (string.Equals(directory, ownPlugin, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                string source = StripComments(File.ReadAllText(file));
                foreach (Match match in KeyCodeLiteral.Matches(source).Concat(QuotedKeyName.Matches(source)))
                {
                    taken[match.Groups[1].Value] = Path.GetFileName(directory);
                }
            }
        }

        foreach ((string setting, string key) in OwnDefaults(ownPlugin))
        {
            Assert.False(
                taken.ContainsKey(key),
                $"[Debug] {setting} defaults to {key}, which {taken.GetValueOrDefault(key)} already uses.");
        }
    }

    [Fact]
    public void TheSpawnModsTwoDefaultsDifferFromEachOther()
    {
        Dictionary<string, string> defaults = OwnDefaults(
            Path.Combine(RepositoryRoot(), "src", "SiNiSistar2.Spawn.Plugin"));

        Assert.Equal(2, defaults.Count);
        Assert.NotEqual(defaults["HudHotkey"], defaults["DebugPanelHotkey"]);
    }

    /// <summary>F12 is Steam's screenshot key, and this game ships with Steam integration.</summary>
    [Fact]
    public void TheSpawnModAvoidsTheSteamScreenshotKey()
    {
        foreach ((string setting, string key) in OwnDefaults(
            Path.Combine(RepositoryRoot(), "src", "SiNiSistar2.Spawn.Plugin")))
        {
            Assert.True(key != "F12", $"[Debug] {setting} defaults to F12, Steam's screenshot key.");
        }
    }

    private static Dictionary<string, string> OwnDefaults(string pluginDirectory)
    {
        string source = File.ReadAllText(Path.Combine(pluginDirectory, "SpawnPlugin.cs"));
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string setting in new[] { "HudHotkey", "DebugPanelHotkey" })
        {
            Match match = Regex.Match(source, $@"""{setting}"",\s*KeyCode\.(\w+)", RegexOptions.Singleline);
            Assert.True(match.Success, $"Could not find the bound default for {setting}.");
            defaults[setting] = match.Groups[1].Value;
        }

        return defaults;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SiNiSistar2.Edi.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\r\n]*", string.Empty);
    }
}
