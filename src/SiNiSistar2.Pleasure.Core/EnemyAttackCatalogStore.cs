namespace SiNiSistar2.Pleasure.Core;

/// <summary>What happened when the catalogue was read, so the caller can say why it looks as it does.</summary>
public sealed record EnemyAttackCatalogLoad(EnemyAttackCatalog Catalog, string? Notice, bool Locked);

/// <summary>
/// Reads and writes the enemy classification catalogue (SPEC003 5.3, FR-235〜239).
///
/// Unlike the sidecar this file is not tied to a save slot: the decision "this enemy's attacks are
/// sexual" is a property of the game, not of a playthrough, so one file serves every slot.
/// </summary>
public sealed class EnemyAttackCatalogStore
{
    private readonly string _root;
    private readonly string _path;
    private bool _locked;

    public EnemyAttackCatalogStore(string root, string fileName = "enemy-attacks.json")
    {
        _root = root;
        _path = Path.Combine(root, fileName);
    }

    /// <summary>Where the file lives, so the startup log can point at it.</summary>
    public string FilePath => _path;

    /// <summary>
    /// Reads the catalogue. A missing file is a first run, not an error. A file this version cannot
    /// read locks writing instead of being replaced, so an older MOD cannot destroy a newer one's
    /// decisions; a damaged file is set aside so the evidence survives and play continues.
    /// </summary>
    public EnemyAttackCatalogLoad Load()
    {
        string text;
        try
        {
            if (!File.Exists(_path))
            {
                return new EnemyAttackCatalogLoad(new EnemyAttackCatalog(), null, false);
            }

            text = File.ReadAllText(_path);
        }
        catch (Exception exception) when (JsonFile.IsFileFailure(exception))
        {
            // Readable next time, perhaps; refusing to write avoids replacing a file that may be
            // perfectly good and merely locked by something else right now.
            _locked = true;
            return new EnemyAttackCatalogLoad(
                new EnemyAttackCatalog(),
                $"could not be read ({exception.Message}); no changes will be written",
                true);
        }

        EnemyAttackParse parse = EnemyAttackDocument.Parse(text);
        if (parse.IsLoaded)
        {
            return new EnemyAttackCatalogLoad(new EnemyAttackCatalog(parse.Document!), null, false);
        }

        if (parse.UnsupportedSchema)
        {
            _locked = true;
            return new EnemyAttackCatalogLoad(new EnemyAttackCatalog(), parse.Error, true);
        }

        string? moved = JsonFile.Quarantine(_path);
        return new EnemyAttackCatalogLoad(
            new EnemyAttackCatalog(),
            moved is null
                ? parse.Error
                : $"{parse.Error} It was moved to '{Path.GetFileName(moved)}'.",
            false);
    }

    /// <summary>
    /// Writes the catalogue. Returns null on success or the reason it failed; a failed write is
    /// reported and never interrupts play, on the same footing as the sidecar (FR-239).
    /// </summary>
    public string? Save(EnemyAttackCatalog catalog)
    {
        if (_locked)
        {
            return "the catalogue file could not be read and will not be overwritten";
        }

        string? failure = JsonFile.WriteAtomically(_root, _path, catalog.ToDocument().Serialize());
        if (failure is null)
        {
            catalog.MarkClean();
        }

        return failure;
    }
}
