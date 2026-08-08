namespace SiNiSistar2.Difficulty.Core;

/// <summary>An intervention that could not be undone, and why.</summary>
public sealed record InterventionFailure(string Key, string Reason);

/// <summary>
/// Tracks every intervention that is still in force so that all of them can be undone on unload,
/// on a scene change, or after an exception (SPEC002 FR-124, DEC-110).
///
/// A runtime patch that leaves something registered produces behaviour with no visible cause in
/// the next scene or the next launch. Keeping the undo action next to the registration makes
/// "the ledger is empty" the definition of having fully backed out.
/// </summary>
public sealed class InterventionLedger
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Action> _open = new(StringComparer.Ordinal);

    public bool IsEmpty
    {
        get
        {
            lock (_sync)
            {
                return _open.Count == 0;
            }
        }
    }

    public IReadOnlyList<string> OpenKeys
    {
        get
        {
            lock (_sync)
            {
                return _open.Keys.ToArray();
            }
        }
    }

    /// <summary>
    /// Records an intervention. Registering a key that is already open releases the previous one
    /// first, so a caller cannot accumulate two undo actions for the same thing (SPEC002 FR-120).
    /// Returns any failure from releasing the superseded entry.
    /// </summary>
    public InterventionFailure? Register(string key, Action release)
    {
        InterventionFailure? superseded = Release(key);
        lock (_sync)
        {
            _open[key] = release;
        }

        return superseded;
    }

    public bool IsOpen(string key)
    {
        lock (_sync)
        {
            return _open.ContainsKey(key);
        }
    }

    /// <summary>
    /// Undoes one intervention. Returns null on success or when nothing was open under that key.
    /// Never throws: a failed release must not take down the frame that is trying to clean up.
    /// </summary>
    public InterventionFailure? Release(string key)
    {
        Action? release;
        lock (_sync)
        {
            if (!_open.Remove(key, out release))
            {
                return null;
            }
        }

        try
        {
            release();
            return null;
        }
        catch (Exception exception)
        {
            return new InterventionFailure(key, exception.Message);
        }
    }

    /// <summary>
    /// Undoes everything still open and empties the ledger. Each entry is attempted even if an
    /// earlier one failed, so one stuck intervention cannot strand the rest.
    /// </summary>
    public IReadOnlyList<InterventionFailure> ReleaseAll()
    {
        KeyValuePair<string, Action>[] pending;
        lock (_sync)
        {
            pending = _open.ToArray();
            _open.Clear();
        }

        var failures = new List<InterventionFailure>();
        foreach ((string key, Action release) in pending)
        {
            try
            {
                release();
            }
            catch (Exception exception)
            {
                failures.Add(new InterventionFailure(key, exception.Message));
            }
        }

        return failures;
    }
}
