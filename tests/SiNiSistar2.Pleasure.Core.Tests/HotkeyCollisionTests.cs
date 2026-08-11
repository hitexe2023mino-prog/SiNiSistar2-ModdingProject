using System.Text.RegularExpressions;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The key that opens the statistics page has to be one nobody else in this game folder has taken
/// (SPEC006 FR-614).
///
/// The MODs share one keyboard and no arbiter: two plugins bound to one key both act, and the
/// second feature reads as broken rather than double-bound. The spawn MOD already guards its own
/// two defaults this way, and this is the same guard pointed the other direction — its first run
/// caught F3, which the spawn MOD's debug panel owns.
///
/// Both spellings are scanned, because this MOD stores its key as a configured *string* while the
/// others use <c>KeyCode</c> literals, and a search for one spelling misses the other.
/// </summary>
public sealed class HotkeyCollisionTests
{
    private static readonly Regex KeyCodeLiteral = new(@"KeyCode\.(F\d{1,2})\b");
    private static readonly Regex QuotedKeyName = new("\"(F\\d{1,2})\"");

    /// <summary>The bound default for the statistics key, read from the source that binds it.</summary>
    private static string OwnDefault()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "SiNiSistar2.Pleasure.Plugin", "PleasurePlugin.cs"));

        Match match = Regex.Match(source, @"""OpenPageKey"",\s*""(\w+)""", RegexOptions.Singleline);
        Assert.True(match.Success, "Could not find the bound default for OpenPageKey.");
        return match.Groups[1].Value;
    }

    [Fact]
    public void TheStatisticsKeyIsNotTakenByAnotherMod()
    {
        string root = RepositoryRoot();
        string ownPlugin = Path.Combine(root, "src", "SiNiSistar2.Pleasure.Plugin");

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

        string key = OwnDefault();
        Assert.False(
            taken.ContainsKey(key),
            $"Statistics.OpenPageKey defaults to {key}, which {taken.GetValueOrDefault(key)} already uses.");
    }

    /// <summary>
    /// This MOD's own debug keys are bound as literals in the observer, and the statistics key is a
    /// configured string, so nothing but a test relates the two.
    /// </summary>
    [Fact]
    public void TheStatisticsKeyIsNotOneOfThisModsOwnScreens()
    {
        string observer = StripComments(File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "SiNiSistar2.Pleasure.Plugin", "PleasureObserver.cs")));

        var taken = KeyCodeLiteral.Matches(observer)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        string key = OwnDefault();
        Assert.False(
            taken.Contains(key),
            $"Statistics.OpenPageKey defaults to {key}, which this MOD's own screens already use.");
    }

    /// <summary>F12 is Steam's screenshot key, and this game ships with Steam integration.</summary>
    [Fact]
    public void TheStatisticsKeyAvoidsTheSteamScreenshotKey() =>
        Assert.NotEqual("F12", OwnDefault());

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
