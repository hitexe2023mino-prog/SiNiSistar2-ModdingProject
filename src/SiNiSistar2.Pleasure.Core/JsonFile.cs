namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// The file handling shared by the MOD's JSON stores.
///
/// Both of them sit next to things the player would be upset to lose — one tracks a save slot, the
/// other holds hand-made classification decisions — so both want the same discipline: write through
/// a temporary file, set a damaged file aside instead of reading or replacing it, and report a
/// failure rather than throwing it into the frame that called.
/// </summary>
internal static class JsonFile
{
    /// <summary>
    /// Everything a path can fail with. A configured location that is wrong should be reported,
    /// not thrown into the game: an invalid path raises ArgumentException rather than IOException,
    /// which a narrower filter would have let escape.
    /// </summary>
    internal static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    /// <summary>
    /// Writes beside the target and moves it into place, so a crash mid-write leaves the previous
    /// file intact rather than a half-written one. Returns null on success or the reason it failed.
    /// </summary>
    internal static string? WriteAtomically(string root, string path, string text)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            TryDelete(temporary);
            return exception.Message;
        }
    }

    /// <summary>
    /// Moves a file this version cannot read out of the way, returning where it went. The evidence
    /// survives and the caller can start fresh, which is what stops one damaged file bricking a
    /// slot or a whole catalogue.
    /// </summary>
    internal static string? Quarantine(string path)
    {
        try
        {
            string target = path + ".corrupt";
            var suffix = 1;
            while (File.Exists(target))
            {
                target = $"{path}.corrupt{suffix++}";
            }

            File.Move(path, target);
            return target;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return null;
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // Nothing useful to do; the next write will overwrite it.
        }
    }
}
