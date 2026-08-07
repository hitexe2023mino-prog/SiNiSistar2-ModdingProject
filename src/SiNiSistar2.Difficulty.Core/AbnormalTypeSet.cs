namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// A configured group of status-ailment names. Membership is tested by name so that this layer
/// stays free of the game's IL2CPP enum and can be unit tested without launching the game
/// (SPEC002 FR-134, DEC-112).
/// </summary>
public sealed class AbnormalTypeSet
{
    private readonly HashSet<string> _names;

    private AbnormalTypeSet(HashSet<string> names) => _names = names;

    public static AbnormalTypeSet Empty { get; } = new(new HashSet<string>(StringComparer.Ordinal));

    public IReadOnlyCollection<string> Names => _names;

    public int Count => _names.Count;

    public bool Contains(string name) => _names.Contains(name);

    /// <summary>
    /// Parses a comma-separated list. A name the game does not define is dropped and reported
    /// rather than invalidating the whole group: a single typo must not silently switch a
    /// mechanism off (SPEC002 FR-126). Names in <paramref name="forbidden"/> are dropped the same
    /// way, which is how <c>Defilement</c> is kept out of the pleasure group through the config
    /// path as well as the code path (SPEC002 FR-114).
    /// </summary>
    public static AbnormalTypeSetParse Parse(
        string? raw,
        IReadOnlyCollection<string> known,
        IReadOnlyCollection<string>? forbidden = null)
    {
        var knownSet = new HashSet<string>(known, StringComparer.Ordinal);
        var forbiddenSet = new HashSet<string>(forbidden ?? Array.Empty<string>(), StringComparer.Ordinal);
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        var rejected = new List<string>();

        foreach (string token in (raw ?? string.Empty).Split(','))
        {
            string name = token.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (forbiddenSet.Contains(name))
            {
                rejected.Add(name);
                continue;
            }

            // An empty known set means the caller could not enumerate the game's names, so the
            // configured names are taken at face value rather than all being reported as unknown.
            if (knownSet.Count > 0 && !knownSet.Contains(name))
            {
                unknown.Add(name);
                continue;
            }

            accepted.Add(name);
        }

        return new AbnormalTypeSetParse(new AbnormalTypeSet(accepted), unknown, rejected);
    }
}

/// <summary>Outcome of parsing one group: what was kept, and what was dropped and why.</summary>
public sealed record AbnormalTypeSetParse(
    AbnormalTypeSet Set,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> Rejected);
