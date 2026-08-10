namespace SiNiSistar2.Spawn.Core;

/// <summary>One reward candidate: an item name (validated against ItemID by the plugin), a count and a weight.</summary>
public readonly record struct RewardEntry(string ItemName, int Count, int Weight);

/// <summary>
/// The miss-outcome reward table (SPEC004 5.5-6, 用語「報酬テーブル」). Parsing and drawing live
/// here; whether a name is a real, permitted <c>ItemID</c> is the plugin's check because the enum
/// only exists in the interop assembly.
/// </summary>
public sealed class RewardTable
{
    private readonly RewardEntry[] _entries;
    private readonly int _totalWeight;

    private RewardTable(RewardEntry[] entries)
    {
        _entries = entries;
        foreach (RewardEntry entry in entries)
        {
            _totalWeight += entry.Weight;
        }
    }

    public IReadOnlyList<RewardEntry> Entries => _entries;

    public bool IsEmpty => _entries.Length == 0;

    /// <summary>
    /// Parses `Item:count:weight` comma-separated entries. Malformed entries become errors and are
    /// dropped rather than failing the whole table, matching the config policy of "present the
    /// error, fall back, keep running" (SPEC004 FR-317).
    /// </summary>
    public static RewardTable Parse(string text, out List<string> errors)
    {
        errors = new List<string>();
        var entries = new List<RewardEntry>();

        foreach (string rawEntry in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = rawEntry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || parts[0].Length == 0
                || !int.TryParse(parts[1], out int count)
                || !int.TryParse(parts[2], out int weight))
            {
                errors.Add($"RewardTable entry '{rawEntry}' is not in the form Item:count:weight and is ignored.");
                continue;
            }

            if (count <= 0 || weight <= 0)
            {
                errors.Add($"RewardTable entry '{rawEntry}' needs a positive count and weight and is ignored.");
                continue;
            }

            entries.Add(new RewardEntry(parts[0], count, weight));
        }

        return new RewardTable(entries.ToArray());
    }

    public static RewardTable Empty => new(Array.Empty<RewardEntry>());

    /// <summary>Weighted draw. Returns null when the table is empty.</summary>
    public RewardEntry? Draw(IRandomSource random)
    {
        if (_entries.Length == 0)
        {
            return null;
        }

        int roll = random.NextInt(_totalWeight);
        foreach (RewardEntry entry in _entries)
        {
            roll -= entry.Weight;
            if (roll < 0)
            {
                return entry;
            }
        }

        return _entries[^1];
    }

    /// <summary>Removes entries rejected by the plugin's ItemID validation, reporting each.</summary>
    public RewardTable Without(IEnumerable<string> rejectedNames)
    {
        var rejected = new HashSet<string>(rejectedNames, StringComparer.OrdinalIgnoreCase);
        return new RewardTable(Array.FindAll(_entries, e => !rejected.Contains(e.ItemName)));
    }
}
