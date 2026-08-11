namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// A tally that only ever goes up (SPEC006 4.4, FR-603).
///
/// Shared by the two lifetime counters so the one-way rule lives in one place rather than in every
/// caller, exactly as <see cref="CorruptionTrack"/> does for corruption. There is deliberately no
/// decrement, no removal and no per-key clear: the only way a count moves down is a slot transition
/// replacing the whole tally, which is a different timeline rather than a decrease (SPEC003 4.4).
/// </summary>
public sealed class LifetimeTally
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public int DistinctKeys => _counts.Count;

    public int Total
    {
        get
        {
            var total = 0;
            foreach (int count in _counts.Values)
            {
                total += count;
            }

            return total;
        }
    }

    public int CountFor(string key) => _counts.TryGetValue(key, out int count) ? count : 0;

    /// <summary>Counts one occurrence. A blank key is ignored rather than stored under "".</summary>
    public void Add(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _counts[key] = CountFor(key) + 1;
    }

    /// <summary>
    /// Orders the tally for display and for the file: most counted first, then by key.
    ///
    /// Deterministic on purpose. The top entry of this ordering is the one the page names, and a
    /// tie broken by anything the dictionary happened to do would move the named enemy about
    /// between polls for no reason the player could see (DEC-604).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> Ordered()
    {
        var entries = new List<KeyValuePair<string, int>>(_counts);
        entries.Sort(static (left, right) =>
        {
            int byCount = right.Value.CompareTo(left.Value);
            return byCount != 0 ? byCount : string.CompareOrdinal(left.Key, right.Key);
        });

        return entries;
    }

    /// <summary>
    /// Replaces the tally with what a save holds.
    ///
    /// Duplicate keys are summed rather than overwritten. A file should not have them, but a
    /// hand-edited one might, and these counts can never be earned back — keeping the total is the
    /// least destructive reading of a file nobody can reconstruct.
    /// </summary>
    public void LoadFrom(IEnumerable<KeyValuePair<string, int>>? saved)
    {
        _counts.Clear();
        if (saved is null)
        {
            return;
        }

        foreach (KeyValuePair<string, int> entry in saved)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0)
            {
                continue;
            }

            _counts[entry.Key] = CountFor(entry.Key) + entry.Value;
        }
    }

    /// <summary>Empties the tally for a run that starts from nothing. Not a decrease (SPEC003 4.4).</summary>
    public void Reset() => _counts.Clear();
}

/// <summary>
/// Which enemy has made the player climax, and how often, for the life of a save (SPEC006 FR-602).
///
/// Separate from <see cref="ClimaxLedger"/> and never reset with it. The ledger's count is the one
/// the limit is tested against and a save point clears it; this is the diary, and a diary that
/// forgot every time its owner rested would not be one (DEC-602).
/// </summary>
public sealed class ActorClimaxLedger
{
    /// <summary>
    /// Where a climax goes when the captor could not be named (SPEC006 FR-602).
    ///
    /// Kept rather than dropped: the total has to stay honest even when the enemy behind it does
    /// not. It is excluded from <see cref="TopActor"/> because "unknown" is not an enemy anyone can
    /// be told they lost to most often.
    /// </summary>
    public const string UnknownActorId = "unknown";

    private readonly LifetimeTally _tally = new();

    public int Total => _tally.Total;

    public int CountFor(string actorId) => _tally.CountFor(actorId);

    /// <summary>Counts one climax against its captor, or against "unknown" when there is none.</summary>
    public void Record(string? actorId) =>
        _tally.Add(string.IsNullOrWhiteSpace(actorId) ? UnknownActorId : actorId!);

    public IReadOnlyList<KeyValuePair<string, int>> Ordered() => _tally.Ordered();

    /// <summary>
    /// The enemy with the most climaxes to its name, or null when none has been identified.
    ///
    /// Null rather than "unknown" so the page can say nobody has done this yet instead of naming a
    /// placeholder as the worst offender.
    /// </summary>
    public KeyValuePair<string, int>? TopActor()
    {
        foreach (KeyValuePair<string, int> entry in _tally.Ordered())
        {
            if (!string.Equals(entry.Key, UnknownActorId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    public void LoadFrom(IEnumerable<KeyValuePair<string, int>>? saved) => _tally.LoadFrom(saved);

    public void Reset() => _tally.Reset();
}

/// <summary>
/// How many times each status ailment has actually been applied to the player (SPEC006 FR-601).
///
/// What an attack declared it would apply is not counted, only what landed. The declaration is
/// visible on the damage stack and would have been the easier read, but it says nothing about
/// whether the application succeeded — and SPEC002 moves those odds, so the two numbers come apart
/// exactly when the player is most likely to be looking (DEC-606).
/// </summary>
public sealed class DebuffCounters
{
    private readonly LifetimeTally _tally = new();

    /// <summary>
    /// What the game called each status, captured as it was applied (SPEC006 FR-613).
    ///
    /// Kept beside the count because it cannot be worked out from the type name. The game's
    /// localisation keys for status ailments follow no derivable pattern — <c>ID_Ab_ParasiteLv1</c>,
    /// <c>ID_Ab_LustMarkCurse_Lv1</c> and <c>ID_Ab_Spore1</c> are three different spellings, and 34
    /// of the 71 types have no key of their own at all. The only reliable answer is the one the
    /// game itself uses, <c>AbnormalData.AbnormalNameID</c>, and that can only be read while the
    /// status is attached to somebody (付録A A-603).
    /// </summary>
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public int Total => _tally.Total;

    public int CountFor(string abnormalType) => _tally.CountFor(abnormalType);

    /// <summary>The game's name for this status, or null if it has never said one.</summary>
    public string? DisplayNameFor(string abnormalType) =>
        _names.TryGetValue(abnormalType, out string? name) ? name : null;

    /// <summary>
    /// Counts one application, and remembers the name the game gave it.
    /// </summary>
    /// <param name="displayName">
    /// The game's own name at the moment of application, or null when it could not be read. A
    /// levelled status is named per level, so the latest reading wins: it is the name the player
    /// most recently saw for it.
    /// </param>
    public void Record(string? abnormalType, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(abnormalType))
        {
            return;
        }

        _tally.Add(abnormalType!);
        RememberName(abnormalType!, displayName);
    }

    /// <summary>
    /// Records a name without counting anything. Used when restoring a save, where the names were
    /// stored with the counts and no new application has happened.
    /// </summary>
    public void RememberName(string abnormalType, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(abnormalType) && !string.IsNullOrWhiteSpace(displayName))
        {
            _names[abnormalType!] = displayName!;
        }
    }

    public IReadOnlyList<KeyValuePair<string, int>> Ordered() => _tally.Ordered();

    public void LoadFrom(IEnumerable<KeyValuePair<string, int>>? saved) => _tally.LoadFrom(saved);

    public void Reset()
    {
        _tally.Reset();
        _names.Clear();
    }
}
