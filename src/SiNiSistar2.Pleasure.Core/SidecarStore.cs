namespace SiNiSistar2.Pleasure.Core;

/// <summary>What happened when a slot was read, so the caller can say why it started from nothing.</summary>
public sealed record SidecarLoad(SidecarDocument? Document, string? Notice, bool Locked)
{
    public bool IsLoaded => Document is not null;
}

/// <summary>
/// Reads and writes the file that carries sensitivity and the climax count alongside one of the
/// game's save slots (SPEC003 5.9).
///
/// It is a file of the MOD's own rather than anything inside the game's save. The game's
/// flag/variable system has fixed categories and fixed-length arrays with no way to add a name, so
/// using it would mean squatting on an index nobody can prove is free — and a game update that
/// began using that index would quietly corrupt quest or boss progress (SPEC003 DEC-206).
/// </summary>
public sealed class SidecarStore
{
    private readonly string _root;
    private readonly string _gameBuildId;
    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);

    public SidecarStore(string root, string gameBuildId)
    {
        _root = root;
        _gameBuildId = gameBuildId;
    }

    public string PathFor(string slotKey) => Path.Combine(_root, $"{slotKey}.json");

    /// <summary>
    /// Reads a slot. A missing file is simply a slot that has never been played with the MOD, so it
    /// starts from defaults. A file this version cannot read locks the slot instead: refusing to
    /// write is what stops an older MOD destroying a newer one's save (SPEC003 FR-225).
    /// </summary>
    public SidecarLoad Load(string slotKey)
    {
        string path = PathFor(slotKey);
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return new SidecarLoad(null, null, false);
            }

            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (JsonFile.IsFileFailure(exception))
        {
            return new SidecarLoad(null, $"could not be read ({exception.Message})", false);
        }

        SidecarParse parse = SidecarDocument.Parse(text);
        if (parse.IsLoaded)
        {
            string? notice = string.Equals(parse.Document!.GameBuildId, _gameBuildId, StringComparison.Ordinal)
                ? null
                : $"was written by game build '{parse.Document.GameBuildId}', not '{_gameBuildId}'";
            return new SidecarLoad(parse.Document, notice, false);
        }

        if (parse.UnsupportedSchema)
        {
            _locked.Add(slotKey);
            return new SidecarLoad(null, parse.Error, true);
        }

        // Damaged rather than newer: the file is set aside so the evidence survives, and the slot
        // starts fresh instead of refusing to work.
        string? moved = JsonFile.Quarantine(path);
        return new SidecarLoad(
            null,
            moved is null ? parse.Error : $"{parse.Error} It was moved to '{Path.GetFileName(moved)}'.",
            false);
    }

    /// <summary>
    /// Writes a slot atomically. Returns null on success or the reason it failed; a failure never
    /// stops the game (SPEC003 FR-223, FR-226).
    /// </summary>
    public string? Save(
        string slotKey,
        float sensitivity,
        int climaxCount,
        int breastAtMaxCount = 0,
        float milk = 0f)
    {
        if (_locked.Contains(slotKey))
        {
            return "the slot holds a file from a newer version of this MOD and will not be overwritten";
        }

        var document = new SidecarDocument
        {
            GameBuildId = _gameBuildId,
            Sensitivity = Math.Max(0f, sensitivity),
            ClimaxCount = Math.Max(0, climaxCount),
            BreastAtMaxCount = Math.Max(0, breastAtMaxCount),
            Milk = Math.Clamp(milk, 0f, 1f),
        };

        return JsonFile.WriteAtomically(_root, PathFor(slotKey), document.Serialize());
    }
}
