namespace SiNiSistar2.Edi.Core;

/// <summary>What a startup gallery check found, so the caller can log one line about it.</summary>
public sealed record GalleryRegistrationResult(
    IReadOnlyList<string> Fillers,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Stray,
    string? Failure)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Makes EDI re-read the repository's gallery root at startup, and reports the asset problems
/// that would otherwise surface as a device doing nothing.
///
/// No files are transferred. The MOD is already the writer of <c>Edi/Gallery/**</c> and
/// <c>Definitions.csv</c> (SPEC001 6.6, 12.1), and EDI's asset upload endpoint moves the gallery
/// root to its own upload folder, which loses the definition table and resets the channels
/// (7.4 E3, DEC-029). A re-scan is the whole of what EDI needs.
/// </summary>
public static class GalleryRegistration
{
    /// <summary>
    /// Asks EDI to re-scan, and checks the fillers named by the mapping against the roster.
    /// Failure is reported rather than thrown: the game must keep running when EDI is
    /// unavailable (FR-015).
    /// </summary>
    public static async Task<GalleryRegistrationResult> ReloadAsync(
        string galleryRoot,
        MappingRepository mappings,
        Func<CancellationToken, Task> reloadAsync,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> fillers = FillerGalleries(mappings);
        var missing = new List<string>();
        foreach (string gallery in fillers)
        {
            foreach (string output in OutputsForFiller(mappings, gallery))
            {
                string? variant = mappings.VariantFor(output);
                if (variant is null)
                {
                    continue;
                }

                string path = Path.Combine(galleryRoot, variant, $"{gallery}.funscript");
                if (!File.Exists(path))
                {
                    missing.Add($"{variant}/{gallery}.funscript");
                }
            }
        }

        IReadOnlyList<string> stray = FindStrayVariants(galleryRoot, mappings);

        try
        {
            await reloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException
                or UnauthorizedAccessException)
        {
            return new GalleryRegistrationResult(fillers, missing, stray, exception.Message);
        }

        return new GalleryRegistrationResult(fillers, missing, stray, null);
    }

    /// <summary>Every filler the mapping can select, defaults included, in a stable order.</summary>
    public static IReadOnlyList<string> FillerGalleries(MappingRepository mappings)
    {
        var galleries = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string? gallery in mappings.Document.DefaultFillers.Values)
        {
            if (gallery is { Length: > 0 })
            {
                galleries.Add(gallery);
            }
        }

        foreach (StatusRule rule in mappings.Document.StatusRules)
        {
            if (rule.Disposition != "mapped")
            {
                continue;
            }

            foreach (OutputAssignment assignment in rule.Outputs)
            {
                if (assignment.Gallery is { Length: > 0 } filler)
                {
                    galleries.Add(filler);
                }
            }
        }

        return galleries.ToArray();
    }

    /// <summary>The outputs a filler gallery is selected for, from defaults and status rules.</summary>
    public static IReadOnlyList<string> OutputsForFiller(MappingRepository mappings, string gallery)
    {
        var outputs = new List<string>();
        foreach ((string output, string? filler) in mappings.Document.DefaultFillers)
        {
            if (string.Equals(filler, gallery, StringComparison.Ordinal))
            {
                outputs.Add(output);
            }
        }

        foreach (StatusRule rule in mappings.Document.StatusRules.Where(x => x.Disposition == "mapped"))
        {
            foreach (OutputAssignment assignment in rule.Outputs)
            {
                if (string.Equals(assignment.Gallery, gallery, StringComparison.Ordinal)
                    && !outputs.Contains(assignment.Id, StringComparer.Ordinal))
                {
                    outputs.Add(assignment.Id);
                }
            }
        }

        return outputs
            .OrderBy(x => mappings.OutputIds.ToList().IndexOf(x))
            .ToArray();
    }

    /// <summary>
    /// Variant folders on disk that no output claims. A gallery's target outputs are derived from
    /// the variants it carries, so a variant nothing owns makes that derivation meaningless
    /// (FR-057, DEC-026).
    /// </summary>
    public static IReadOnlyList<string> FindStrayVariants(string galleryRoot, MappingRepository mappings)
    {
        if (!Directory.Exists(galleryRoot))
        {
            return Array.Empty<string>();
        }

        var known = mappings.Outputs.Select(x => x.EdiVariant).ToHashSet(StringComparer.Ordinal);
        return Directory
            .EnumerateDirectories(galleryRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) && !known.Contains(name!))
            .Where(name => Directory.EnumerateFiles(
                Path.Combine(galleryRoot, name!), "*.funscript").Any())
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
