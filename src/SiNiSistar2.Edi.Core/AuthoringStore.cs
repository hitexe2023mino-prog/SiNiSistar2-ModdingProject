using System.Text.Json;

namespace SiNiSistar2.Edi.Core;

public sealed record AuthoringSaveRequest(
    EventKey Key,
    IReadOnlyDictionary<string, FunscriptDocument> Variants,
    bool ApproveLoopMismatch,
    /// <summary>
    /// Whether EDI should replay the gallery until the trigger ends. Defaults to whether the game
    /// clip loops, which is unknown for takes that carry no animator: those report no loop, EDI
    /// plays the script once, and the device then sits still for the rest of the stage. The author
    /// can see the stage repeating on screen, so this lets them say so.
    /// </summary>
    bool? Repeat = null,
    /// <summary>
    /// Outputs this trigger should deliberately silence. Distinct from an output that is simply
    /// absent: absent means "not part of this trigger", silenced means "stop this device"
    /// (SPEC001 6.2, FR-047).
    /// </summary>
    IReadOnlyList<string>? SilentOutputs = null);

/// <summary>
/// A filler gallery referenced by name from the mapping file. Fillers are not triggers, so they
/// never appear in the catalog and would otherwise be unreachable from the GUI.
/// </summary>
public sealed record FillerDescriptor(
    string Gallery,
    IReadOnlyList<string> Outputs,
    string Role,
    string? StatusId,
    string? StatusDisplayName);

public sealed record FillerSaveResult(
    bool Success,
    string Gallery,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> WrittenPaths,
    long DurationMilliseconds,
    bool DefinitionUpdated)
{
    public static FillerSaveResult Rejected(string gallery, params string[] errors) =>
        new(false, gallery, errors, Array.Empty<string>(), 0, false);
}

public sealed record AuthoringSaveResult(
    bool Success,
    string Gallery,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> LoopWarnings,
    IReadOnlyList<string> WrittenPaths,
    string? ManifestPath,
    bool MappingUpdated,
    IReadOnlyList<string>? RemovedPaths = null,
    IReadOnlyList<string>? Outputs = null)
{
    /// <summary>Advisory playback notes; a save succeeds with these present.</summary>
    public IReadOnlyList<string> MotionWarnings { get; init; } = Array.Empty<string>();

    public static AuthoringSaveResult Rejected(
        string gallery,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> loopWarnings) =>
        new(false, gallery, errors, loopWarnings, Array.Empty<string>(), null, false);
}

/// <summary>
/// Persists funscripts authored in the GUI, makes EDI re-read them, and updates the mapping
/// source of truth. The steps are ordered so a failure never leaves a trigger marked
/// <c>mapped</c> without playable assets (SPEC001 6.7, FR-038).
/// </summary>
public sealed class AuthoringStore
{
    private readonly string _galleryRoot;
    private readonly string _manifestRoot;
    private readonly string _mappingPath;
    private readonly string _gameBuildId;
    private readonly MappingRepository _mappings;
    private readonly TriggerCatalog _catalog;
    private readonly Func<CancellationToken, Task> _reloadAsync;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AuthoringStore(
        string galleryRoot,
        string manifestRoot,
        string mappingPath,
        string gameBuildId,
        MappingRepository mappings,
        TriggerCatalog catalog,
        Func<CancellationToken, Task> reloadAsync)
    {
        _galleryRoot = galleryRoot;
        _manifestRoot = manifestRoot;
        _mappingPath = mappingPath;
        _gameBuildId = gameBuildId;
        _mappings = mappings;
        _catalog = catalog;
        _reloadAsync = reloadAsync;
    }

    public string GalleryRoot => _galleryRoot;

    public string DefinitionsPath => Path.Combine(_galleryRoot, "Definitions.csv");

    /// <summary>All variants the roster knows, in roster order.</summary>
    public IReadOnlyList<string> RosterVariants =>
        _mappings.Outputs.Select(x => x.EdiVariant).ToArray();

    /// <summary>
    /// The fillers the mapping file refers to: the defaults plus one per mapped status rule,
    /// each with the outputs that select it.
    /// </summary>
    public IReadOnlyList<FillerDescriptor> ListFillers()
    {
        var fillers = new List<FillerDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (StatusRule rule in _mappings.Document.StatusRules)
        {
            if (rule.Disposition != "mapped")
            {
                continue;
            }

            foreach (OutputAssignment assignment in rule.Outputs)
            {
                if (assignment.Gallery is not { Length: > 0 } gallery || !seen.Add(gallery))
                {
                    continue;
                }

                fillers.Add(new FillerDescriptor(
                    gallery,
                    GalleryRegistration.OutputsForFiller(_mappings, gallery),
                    "status",
                    rule.StatusId,
                    rule.DisplayName));
            }
        }

        foreach ((string output, string? gallery) in _mappings.Document.DefaultFillers)
        {
            if (gallery is { Length: > 0 } && seen.Add(gallery))
            {
                fillers.Add(new FillerDescriptor(
                    gallery,
                    GalleryRegistration.OutputsForFiller(_mappings, gallery),
                    "default",
                    null,
                    null));
            }
        }

        return fillers;
    }

    /// <summary>Variants required by a gallery, derived from the outputs it is selected for.</summary>
    public IReadOnlyList<string> VariantsForOutputs(IEnumerable<string> outputs) =>
        outputs
            .Select(_mappings.VariantFor)
            .Where(variant => variant is not null)
            .Select(variant => variant!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyDictionary<string, FunscriptDocument> LoadFiller(string gallery)
    {
        FillerDescriptor? descriptor = ListFillers()
            .FirstOrDefault(filler => string.Equals(filler.Gallery, gallery, StringComparison.Ordinal));
        IEnumerable<string> variants = descriptor is null
            ? RosterVariants
            : VariantsForOutputs(descriptor.Outputs);

        var result = new Dictionary<string, FunscriptDocument>(StringComparer.Ordinal);
        foreach (string variant in variants)
        {
            string path = Path.Combine(_galleryRoot, variant, $"{gallery}.funscript");
            if (File.Exists(path) && Funscript.TryRead(path) is { } document)
            {
                result[variant] = document;
            }
        }

        return result;
    }

    /// <summary>
    /// Saves a filler's variants and keeps <c>Definitions.csv</c> in step. EDI takes the playback
    /// length from that table, so a waveform whose length changed would otherwise be cut short or
    /// padded by the stale end time.
    /// </summary>
    public async Task<FillerSaveResult> SaveFillerAsync(
        string gallery,
        IReadOnlyDictionary<string, FunscriptDocument> variants,
        CancellationToken cancellationToken = default)
    {
        FillerDescriptor? descriptor = ListFillers()
            .FirstOrDefault(filler => string.Equals(filler.Gallery, gallery, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return FillerSaveResult.Rejected(gallery, $"'{gallery}' is not a filler in the mapping file.");
        }

        IReadOnlyList<string> required = VariantsForOutputs(descriptor.Outputs);
        if (required.Count == 0)
        {
            return FillerSaveResult.Rejected(gallery, $"No output selects the filler '{gallery}'.");
        }

        var errors = new List<string>();
        foreach (string variant in required)
        {
            if (!variants.ContainsKey(variant))
            {
                errors.Add($"The filler '{gallery}' requires '{variant}'.");
            }
        }

        // A gallery that carries a variant no output plays makes its own target set ambiguous
        // (FR-057, DEC-026).
        foreach (string variant in variants.Keys.Where(variant => !required.Contains(variant)))
        {
            errors.Add($"'{variant}' is not played by any output that selects '{gallery}'.");
        }

        foreach ((string variant, FunscriptDocument script) in variants)
        {
            FunscriptValidation validation = Funscript.Validate(script, 0, false);
            errors.AddRange(validation.Errors.Select(error => $"{variant}: {error}"));
        }

        // A filler loops on one EDI gallery, so every side must end together or they drift apart.
        long[] durations = variants.Values.Select(script => script.DurationMilliseconds).Distinct().ToArray();
        if (durations.Length > 1)
        {
            errors.Add(
                "Every variant of a filler must be the same length; got "
                + string.Join(", ", durations.Select(value => $"{value}ms")) + ".");
        }

        if (errors.Count > 0)
        {
            return FillerSaveResult.Rejected(gallery, errors.ToArray());
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var written = new List<string>(variants.Count);
            foreach ((string variant, FunscriptDocument script) in variants)
            {
                string path = Path.Combine(_galleryRoot, variant, $"{gallery}.funscript");
                await Funscript.WriteAtomicAsync(path, script, cancellationToken).ConfigureAwait(false);
                written.Add(path);
            }

            long duration = durations[0];

            // Every gallery is a `gallery` row: the MOD owns filler selection, so EDI must not
            // retain one of its own and replay it (DEC-023).
            bool definitionUpdated = await EdiGalleryDefinitions
                .UpsertAsync(DefinitionsPath, gallery, duration, "gallery", true, string.Empty, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await _reloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
            {
                return new FillerSaveResult(
                    false,
                    gallery,
                    new[] { $"The filler was saved but EDI did not re-read the gallery: {exception.Message}" },
                    written,
                    duration,
                    definitionUpdated);
            }

            return new FillerSaveResult(true, gallery, Array.Empty<string>(), written, duration, definitionUpdated);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Returns the variants already present on disk for a trigger.</summary>
    public IReadOnlyDictionary<string, FunscriptDocument> LoadExisting(EventKey key)
    {
        string gallery = Funscript.CreateGalleryName(key);
        var result = new Dictionary<string, FunscriptDocument>(StringComparer.Ordinal);
        foreach (string variant in RosterVariants)
        {
            string path = Path.Combine(_galleryRoot, variant, $"{gallery}.funscript");
            if (!File.Exists(path))
            {
                continue;
            }

            FunscriptDocument? document = Funscript.TryRead(path);
            if (document is not null)
            {
                result[variant] = document;
            }
        }

        return result;
    }

    public async Task<AuthoringSaveResult> SaveAsync(
        AuthoringSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        string gallery = Funscript.CreateGalleryName(request.Key);
        double clipLength = 0;
        bool isLoop = false;
        if (_catalog.TryGet(request.Key, out TriggerCatalogEntry entry))
        {
            clipLength = entry.ClipLengthSeconds ?? 0;
            isLoop = entry.IsLooping ?? false;
        }

        // EDI plays a definition row once unless it is marked as looping, so this is what decides
        // whether the device keeps moving for the whole stage or stops after one pass.
        bool repeat = request.Repeat ?? isLoop;

        var errors = new List<string>();
        var loopWarnings = new List<string>();
        var motionWarnings = new List<string>();
        IReadOnlyList<string> silentOutputs = request.SilentOutputs ?? Array.Empty<string>();

        if (request.Variants.Count == 0 && silentOutputs.Count == 0)
        {
            errors.Add("At least one variant must be authored before saving.");
        }

        // A placeholder names a stage whose clips have never been observed, so there is nothing to
        // synchronise a script against and it must never become a mapping.
        if (request.Key.IsUnobservedPlaceholder)
        {
            errors.Add(
                "This stage has not been played yet, so its animation and length are unknown. "
                + "Play it once in the gallery, then author it.");
        }

        var assignments = new List<OutputAssignment>();
        foreach (string variant in request.Variants.Keys)
        {
            string? output = _mappings.OutputForVariant(variant);
            if (output is null)
            {
                errors.Add($"No output in the roster plays the variant '{variant}'.");
                continue;
            }

            assignments.Add(new OutputAssignment { Id = output, Gallery = gallery });
        }

        foreach (string output in silentOutputs)
        {
            if (!_mappings.TryGetOutput(output, out _))
            {
                errors.Add($"Unknown output '{output}'.");
                continue;
            }

            if (assignments.Any(x => string.Equals(x.Id, output, StringComparison.Ordinal)))
            {
                errors.Add($"Output '{output}' cannot both play a variant and be silenced.");
                continue;
            }

            assignments.Add(new OutputAssignment { Id = output, Gallery = null });
        }

        foreach ((string variant, FunscriptDocument script) in request.Variants)
        {
            FunscriptValidation validation = Funscript.Validate(script, clipLength, isLoop, variant, repeat);
            errors.AddRange(validation.Errors.Select(error => $"{variant}: {error}"));
            loopWarnings.AddRange(validation.LoopWarnings.Select(warning => $"{variant}: {warning}"));
            motionWarnings.AddRange(validation.MotionWarnings.Select(warning => $"{variant}: {warning}"));
        }

        if (loopWarnings.Count > 0 && !request.ApproveLoopMismatch)
        {
            return AuthoringSaveResult.Rejected(gallery, errors, loopWarnings);
        }

        if (errors.Count > 0)
        {
            return AuthoringSaveResult.Rejected(gallery, errors, loopWarnings);
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var written = new List<string>(request.Variants.Count);
            var assetPaths = new List<string>(request.Variants.Count);
            foreach ((string variant, FunscriptDocument script) in request.Variants)
            {
                string path = Path.Combine(_galleryRoot, variant, $"{gallery}.funscript");
                await Funscript.WriteAtomicAsync(path, script, cancellationToken).ConfigureAwait(false);
                written.Add(path);
                assetPaths.Add(path);
            }

            // A variant the user cleared has to leave the disk too. A gallery's target outputs are
            // derived from the variants it carries, so a stale file would re-add an output on the
            // next edit (DEC-026).
            var removed = new List<string>();
            foreach (string variant in RosterVariants)
            {
                if (request.Variants.ContainsKey(variant))
                {
                    continue;
                }

                string path = Path.Combine(_galleryRoot, variant, $"{gallery}.funscript");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                    removed.Add(path);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"Could not remove the unused variant '{variant}': {exception.Message}");
                }
            }

            if (errors.Count > 0)
            {
                return AuthoringSaveResult.Rejected(gallery, errors, loopWarnings);
            }

            // Without a definition row EDI does not know the gallery exists, so Play would never
            // resolve it. The row has to be written before EDI is asked to re-read.
            long duration = request.Variants.Count == 0
                ? 0
                : request.Variants.Values.Max(script => script.DurationMilliseconds);
            if (request.Variants.Count > 0)
            {
                await EdiGalleryDefinitions.UpsertAsync(
                    DefinitionsPath,
                    gallery,
                    duration,
                    "gallery",
                    repeat,
                    $"{request.Key.Context}/{request.Key.ActorId}/{request.Key.StageId}",
                    cancellationToken).ConfigureAwait(false);
            }

            string manifestPath = Path.Combine(_manifestRoot, _gameBuildId, $"{gallery}.manifest.json");
            await WriteManifestAsync(
                manifestPath,
                request.Key,
                gallery,
                clipLength,
                isLoop,
                repeat,
                request.ApproveLoopMismatch && loopWarnings.Count > 0,
                loopWarnings,
                motionWarnings,
                assetPaths,
                cancellationToken).ConfigureAwait(false);

            // EDI must know the gallery before the trigger becomes mapped, otherwise it would be
            // asked to play a name it cannot resolve.
            try
            {
                await _reloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
            {
                return new AuthoringSaveResult(
                    false,
                    gallery,
                    new[] { $"EDI did not re-read the gallery; the trigger was not mapped: {exception.Message}" },
                    loopWarnings,
                    written,
                    manifestPath,
                    false);
            }

            // Saving one side must not drop the side saved a moment ago (SPEC001 6.7-8).
            List<OutputAssignment> mergedOutputs = _mappings.MergeOutputs(request.Key, assignments);
            var mapping = new EventMapping
            {
                Id = $"authored-{gallery}",
                Context = request.Key.Context,
                ActorId = request.Key.ActorId,
                AnimationId = request.Key.AnimationId,
                Phase = request.Key.Phase,
                StageId = request.Key.StageId,
                Disposition = "mapped",
                Outputs = mergedOutputs,
                SeekMode = "animation-time",
            };

            try
            {
                await _mappings.UpsertAsync(mapping, _mappingPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return new AuthoringSaveResult(
                    false,
                    gallery,
                    new[] { $"The mapping file could not be updated: {exception.Message}" },
                    loopWarnings,
                    written,
                    manifestPath,
                    false);
            }

            return new AuthoringSaveResult(
                true,
                gallery,
                Array.Empty<string>(),
                loopWarnings,
                written,
                manifestPath,
                true,
                removed,
                mergedOutputs.Select(x => x.Id).ToArray())
            {
                MotionWarnings = motionWarnings,
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task WriteManifestAsync(
        string path,
        EventKey key,
        string gallery,
        double clipLengthSeconds,
        bool isLoop,
        bool repeat,
        bool loopMismatchApproved,
        IReadOnlyList<string> loopWarnings,
        IReadOnlyList<string> motionWarnings,
        IReadOnlyList<string> assetPaths,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string assetPath in assetPaths)
        {
            string variant = Path.GetFileName(Path.GetDirectoryName(assetPath)) ?? string.Empty;
            hashes[$"{variant}/{Path.GetFileName(assetPath)}"] = Funscript.ComputeSha256(assetPath);
        }

        var manifest = new
        {
            schemaVersion = 2,
            authoringVersion = 2,
            eventKey = key,
            gallery,
            clipLengthSeconds,
            isLoop,
            repeat,
            loopMismatchApproved,
            loopWarnings,
            motionWarnings,
            fileSha256 = hashes,
            savedAt = DateTimeOffset.UtcNow,
        };

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
