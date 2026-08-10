using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiNiSistar2.Edi.Core;

public sealed class MappingRepository
{
    /// <summary>
    /// Version 2 moved control from two fixed channels to a roster of per-device outputs. A
    /// version 1 file is not read; SPEC001 12.4 describes the manual migration (FR-016, CHG-028).
    /// </summary>
    public const int SupportedSchemaVersion = 2;

    /// <summary>Display name the shipped mapping must keep wired to the swollen breast filler.</summary>
    public const string SwollenBreastStatusDisplayName = "膨乳";

    private static readonly Regex Sha256Pattern = new(
        "^[A-Fa-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> Contexts = new(StringComparer.Ordinal)
    {
        "hold", "gallery", "game-over", "scripted-event",
    };

    private static readonly HashSet<string> Phases = new(StringComparer.Ordinal)
    {
        "start", "loop", "reaction", "end",
    };

    private static readonly HashSet<string> SeekModes = new(StringComparer.Ordinal)
    {
        "animation-time", "zero",
    };

    private readonly Dictionary<EventKey, EventMapping> _events;
    private readonly object _eventLock = new();

    private MappingRepository(MappingDocument document)
    {
        Document = document;
        _events = document.Events.ToDictionary(x => x.Key);
        Outputs = document.Outputs.ToArray();
        OutputIds = Outputs.Select(x => x.Id).ToArray();
    }

    public MappingDocument Document { get; }

    /// <summary>The device roster, in file order. The only source of the output set (FR-048).</summary>
    public IReadOnlyList<OutputBinding> Outputs { get; }

    /// <summary>Output identifiers in roster order. Each doubles as an EDI channel name.</summary>
    public IReadOnlyList<string> OutputIds { get; }

    public bool TryGetOutput(string outputId, out OutputBinding output)
    {
        output = Outputs.FirstOrDefault(x => string.Equals(x.Id, outputId, StringComparison.Ordinal))!;
        return output is not null;
    }

    /// <summary>The EDI variant an output plays, or null when the roster does not name it.</summary>
    public string? VariantFor(string outputId) =>
        Outputs.FirstOrDefault(x => string.Equals(x.Id, outputId, StringComparison.Ordinal))?.EdiVariant;

    /// <summary>
    /// The output that owns a variant. The roster keeps variants unique, so a gallery's target
    /// outputs can be derived from the variants it carries (DEC-026).
    /// </summary>
    public string? OutputForVariant(string variant) =>
        Outputs.FirstOrDefault(x => string.Equals(x.EdiVariant, variant, StringComparison.Ordinal))?.Id;

    public static MappingValidationResult Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, new[] { $"Could not read mapping file '{path}': {exception.Message}" });
        }
    }

    public static MappingValidationResult Parse(string json)
    {
        MappingDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MappingDocument>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            return new(null, new[] { $"Invalid mapping JSON: {exception.Message}" });
        }

        if (document is null)
        {
            return new(null, new[] { "The mapping document is empty." });
        }

        var errors = Validate(document);
        return errors.Count == 0
            ? new(new MappingRepository(document), errors)
            : new(null, errors);
    }

    public bool TryResolve(EventKey key, out EventMapping mapping)
    {
        // An unidentified actor stands for more than one binder, so a mapping filed under it would
        // give them all the same waveform (SPEC001 FR-060). The authoring side refuses to write such
        // an entry; this refuses to honour one that reached the file by hand.
        if (key.IsUnidentifiedActor)
        {
            mapping = null!;
            return false;
        }

        lock (_eventLock)
        {
            if (_events.TryGetValue(key, out var candidate) && candidate.Disposition == "mapped")
            {
                mapping = candidate;
                return true;
            }
        }

        mapping = null!;
        return false;
    }

    public bool TryGet(EventKey key, out EventMapping mapping)
    {
        lock (_eventLock)
        {
            return _events.TryGetValue(key, out mapping!);
        }
    }

    public MappingDisposition Classify(EventKey key)
    {
        EventMapping? mapping;
        lock (_eventLock)
        {
            _events.TryGetValue(key, out mapping);
        }

        if (mapping is null)
        {
            return MappingDisposition.Unclassified;
        }

        return mapping.Disposition == "mapped"
            ? MappingDisposition.Mapped
            : MappingDisposition.Ignored;
    }

    public MappingDisposition ClassifyStatus(string statusId)
    {
        var rule = Document.StatusRules.FirstOrDefault(x => x.StatusId == statusId);
        if (rule is null)
        {
            return MappingDisposition.Unclassified;
        }

        return rule.Disposition == "mapped"
            ? MappingDisposition.Mapped
            : MappingDisposition.Ignored;
    }

    /// <summary>
    /// The filler one output should idle on, or <see langword="null"/> when it should stay silent.
    /// Evaluated per output, so a rule that targets one device cannot change what another device
    /// is playing (SPEC001 5.4, FR-006, FR-043).
    /// </summary>
    public string? SelectFiller(string outputId, IReadOnlySet<string> activeStatuses)
    {
        // Debuffs stack, so more than one rule can match. The highest priority wins; load-time
        // validation has already proven that equal priorities cannot disagree (FR-043).
        StatusRule? winner = null;
        OutputAssignment? winningAssignment = null;
        foreach (StatusRule rule in Document.StatusRules)
        {
            if (rule.Disposition != "mapped" || !activeStatuses.Contains(rule.StatusId))
            {
                continue;
            }

            OutputAssignment? assignment = rule.Outputs
                .FirstOrDefault(x => string.Equals(x.Id, outputId, StringComparison.Ordinal));
            if (assignment is null || (winner is not null && rule.Priority <= winner.Priority))
            {
                continue;
            }

            winner = rule;
            winningAssignment = assignment;
        }

        if (winner is not null)
        {
            return winningAssignment!.Gallery;
        }

        return Document.DefaultFillers.TryGetValue(outputId, out string? filler) ? filler : null;
    }

    /// <summary>
    /// Validates <paramref name="mapping"/>, applies it in memory, and rewrites the whole mapping
    /// file atomically. The authoring GUI is the only writer of the mapping source of truth
    /// (SPEC001 6.1, FR-038).
    /// </summary>
    public async Task UpsertAsync(
        EventMapping mapping,
        string path,
        CancellationToken cancellationToken = default)
    {
        List<string> errors = ValidateSingleEvent(mapping);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        MappingDocument snapshot;
        lock (_eventLock)
        {
            _events[mapping.Key] = mapping;
            Document.Events.RemoveAll(existing => existing.Key == mapping.Key);
            Document.Events.Add(mapping);
            Document.Events.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            snapshot = new MappingDocument
            {
                SchemaVersion = Document.SchemaVersion,
                MappingVersion = Document.MappingVersion,
                TargetGameBuild = Document.TargetGameBuild,
                Outputs = Document.Outputs.ToList(),
                Events = Document.Events.ToList(),
                StatusRules = Document.StatusRules.ToList(),
                DefaultFillers = new Dictionary<string, string?>(Document.DefaultFillers, StringComparer.Ordinal),
            };
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    /// <summary>
    /// Merges <paramref name="assignments"/> into the trigger's existing output list rather than
    /// replacing it. Saving the right side of a pair must not drop the left side that was saved a
    /// moment earlier (SPEC001 6.7-8).
    /// </summary>
    public List<OutputAssignment> MergeOutputs(EventKey key, IReadOnlyList<OutputAssignment> assignments)
    {
        var merged = new List<OutputAssignment>();
        if (TryGet(key, out EventMapping existing))
        {
            merged.AddRange(existing.Outputs);
        }

        foreach (OutputAssignment assignment in assignments)
        {
            merged.RemoveAll(x => string.Equals(x.Id, assignment.Id, StringComparison.Ordinal));
            merged.Add(assignment);
        }

        // Roster order keeps the file readable and the diff stable across saves.
        return merged
            .OrderBy(x => OutputIds.ToList().IndexOf(x.Id))
            .ToList();
    }

    /// <summary>Validates one entry without re-running document-level requirements.</summary>
    public List<string> ValidateSingleEvent(EventMapping mapping)
    {
        var probe = new MappingDocument
        {
            SchemaVersion = Document.SchemaVersion,
            MappingVersion = Document.MappingVersion,
            TargetGameBuild = Document.TargetGameBuild,
            Outputs = Document.Outputs,
            Events = new List<EventMapping> { mapping },
            StatusRules = Document.StatusRules,
            DefaultFillers = Document.DefaultFillers,
        };
        return Validate(probe)
            .Where(error => !error.StartsWith("statusRules must map", StringComparison.Ordinal))
            .ToList();
    }

    private static List<string> Validate(MappingDocument document)
    {
        var errors = new List<string>();

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(
                $"Unsupported schemaVersion {document.SchemaVersion}; expected {SupportedSchemaVersion}. "
                + "Version 1 files are not read; migrate them by hand (SPEC001 12.4).");
            return errors;
        }

        Require(document.MappingVersion, "mappingVersion", errors);
        ValidateHash(document.TargetGameBuild.GameAssemblySha256, "targetGameBuild.gameAssemblySha256", errors);
        ValidateHash(document.TargetGameBuild.GlobalMetadataSha256, "targetGameBuild.globalMetadataSha256", errors);

        var outputIds = ValidateRoster(document, errors);
        if (outputIds.Count == 0)
        {
            // Nothing downstream can be judged without a roster; reporting each reference as
            // unknown would bury the one error that matters.
            return errors;
        }

        foreach (string outputId in outputIds)
        {
            if (!document.DefaultFillers.ContainsKey(outputId))
            {
                errors.Add(
                    $"defaultFillers must contain a key for output '{outputId}'. Use null to leave "
                    + "the output silent by default (FR-056).");
            }
        }

        foreach (string unknown in document.DefaultFillers.Keys.Where(x => !outputIds.Contains(x)))
        {
            errors.Add($"defaultFillers contains unknown output '{unknown}'.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<EventKey>();
        foreach (var mapping in document.Events)
        {
            Require(mapping.Id, "events[].id", errors);
            if (!ids.Add(mapping.Id))
            {
                errors.Add($"Duplicate event id '{mapping.Id}'.");
            }

            Require(mapping.ActorId, $"event '{mapping.Id}' actorId", errors);
            Require(mapping.AnimationId, $"event '{mapping.Id}' animationId", errors);
            Require(mapping.StageId, $"event '{mapping.Id}' stageId", errors);
            ValidateChoice(mapping.Context, Contexts, $"event '{mapping.Id}' context", errors);
            ValidateChoice(mapping.Phase, Phases, $"event '{mapping.Id}' phase", errors);
            ValidateDisposition(mapping.Disposition, $"event '{mapping.Id}'", errors);

            if (!keys.Add(mapping.Key))
            {
                errors.Add($"Duplicate event key '{mapping.Key}'.");
            }

            if (mapping.Disposition == "mapped")
            {
                ValidateAssignments(
                    mapping.Outputs,
                    outputIds,
                    $"Mapped event '{mapping.Id}'",
                    requireGallery: false,
                    errors);
                ValidateChoice(mapping.SeekMode, SeekModes, $"mapped event '{mapping.Id}' seekMode", errors);
            }
            else if (mapping.Disposition == "ignored")
            {
                Require(mapping.IgnoreReason, $"ignored event '{mapping.Id}' ignoreReason", errors);
            }
        }

        var statusIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in document.StatusRules)
        {
            Require(rule.StatusId, "statusRules[].statusId", errors);
            Require(rule.DisplayName, $"status '{rule.StatusId}' displayName", errors);
            ValidateDisposition(rule.Disposition, $"status '{rule.StatusId}'", errors);

            if (!statusIds.Add(rule.StatusId))
            {
                errors.Add($"Duplicate statusId '{rule.StatusId}'.");
            }

            if (rule.Disposition == "mapped")
            {
                ValidateAssignments(
                    rule.Outputs,
                    outputIds,
                    $"Mapped status '{rule.StatusId}'",
                    requireGallery: false,
                    errors);
            }
            else if (rule.Disposition == "ignored")
            {
                Require(rule.IgnoreReason, $"ignored status '{rule.StatusId}' ignoreReason", errors);
            }
        }

        errors.AddRange(FindAmbiguousStatusRules(document));

        StatusRule? breastRule = document.StatusRules
            .FirstOrDefault(x => x.DisplayName == SwollenBreastStatusDisplayName);
        string[] expectedBreastOutputs = { "breast-left", "breast-right" };
        bool breastRuleIsWired = breastRule?.Disposition == "mapped"
            && expectedBreastOutputs.All(id => breastRule.Outputs.Any(
                assignment => assignment.Id == id && assignment.Gallery == "filler-breast-swollen"));
        if (!breastRuleIsWired && expectedBreastOutputs.All(outputIds.Contains))
        {
            errors.Add(
                $"statusRules must map {SwollenBreastStatusDisplayName} to filler-breast-swollen on "
                + $"{string.Join(" and ", expectedBreastOutputs)} (FR-011).");
        }

        return errors;
    }

    /// <summary>
    /// Statuses stack, so two rules on one output can match at the same time. If they share a
    /// priority but name different galleries, nothing in the file says which one should win, and
    /// the answer would silently be "whichever was typed first" (FR-043, DEC-019).
    /// </summary>
    private static IEnumerable<string> FindAmbiguousStatusRules(MappingDocument document)
    {
        var byOutputAndPriority = new Dictionary<(string Output, int Priority), List<(string Status, string? Gallery)>>();
        foreach (StatusRule rule in document.StatusRules.Where(x => x.Disposition == "mapped"))
        {
            foreach (OutputAssignment assignment in rule.Outputs)
            {
                var key = (assignment.Id, rule.Priority);
                if (!byOutputAndPriority.TryGetValue(key, out var bucket))
                {
                    bucket = new List<(string, string?)>();
                    byOutputAndPriority[key] = bucket;
                }

                bucket.Add((rule.StatusId, assignment.Gallery));
            }
        }

        foreach (var ((output, priority), bucket) in byOutputAndPriority)
        {
            // null is one of the values: "silence this output" disagrees with "play X" just as
            // much as two different gallery names do.
            if (bucket.Select(x => x.Gallery ?? " silent").Distinct(StringComparer.Ordinal).Count() <= 1)
            {
                continue;
            }

            yield return
                $"Statuses {string.Join(", ", bucket.Select(x => $"'{x.Status}'"))} all target output "
                + $"'{output}' at priority {priority} but select different galleries "
                + $"({string.Join(", ", bucket.Select(x => x.Gallery ?? "silent").Distinct(StringComparer.Ordinal))}). "
                + "Statuses can be active at once, so give the one that should win a higher priority.";
        }
    }

    private static HashSet<string> ValidateRoster(MappingDocument document, ICollection<string> errors)
    {
        var outputIds = new HashSet<string>(StringComparer.Ordinal);
        if (document.Outputs.Count == 0)
        {
            errors.Add("outputs must declare at least one device (SPEC001 6.1.1).");
            return outputIds;
        }

        var deviceNames = new HashSet<string>(StringComparer.Ordinal);
        var variants = new HashSet<string>(StringComparer.Ordinal);
        foreach (OutputBinding output in document.Outputs)
        {
            Require(output.Id, "outputs[].id", errors);
            Require(output.DisplayName, $"output '{output.Id}' displayName", errors);
            Require(output.EdiDeviceName, $"output '{output.Id}' ediDeviceName", errors);
            Require(output.EdiVariant, $"output '{output.Id}' ediVariant", errors);

            if (!string.IsNullOrWhiteSpace(output.Id) && !outputIds.Add(output.Id))
            {
                errors.Add($"Duplicate output id '{output.Id}'.");
            }

            if (!string.IsNullOrWhiteSpace(output.EdiDeviceName) && !deviceNames.Add(output.EdiDeviceName))
            {
                errors.Add(
                    $"Two outputs claim the EDI device '{output.EdiDeviceName}'. One device drives "
                    + "exactly one output (DEC-002).");
            }

            if (!string.IsNullOrWhiteSpace(output.EdiVariant) && !variants.Add(output.EdiVariant))
            {
                errors.Add(
                    $"Two outputs claim the EDI variant '{output.EdiVariant}'. A gallery's target "
                    + "outputs are derived from its variants, so they must stay unique (DEC-026).");
            }
        }

        return outputIds;
    }

    private static void ValidateAssignments(
        IReadOnlyList<OutputAssignment> assignments,
        IReadOnlySet<string> outputIds,
        string subject,
        bool requireGallery,
        ICollection<string> errors)
    {
        if (assignments.Count == 0)
        {
            errors.Add($"{subject} must contain at least one output assignment.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (OutputAssignment assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.Id))
            {
                errors.Add($"{subject} contains an output assignment without an id.");
                continue;
            }

            if (!outputIds.Contains(assignment.Id))
            {
                errors.Add($"{subject} references unknown output '{assignment.Id}'.");
            }

            if (!seen.Add(assignment.Id))
            {
                errors.Add($"{subject} assigns output '{assignment.Id}' more than once.");
            }

            if (requireGallery && string.IsNullOrWhiteSpace(assignment.Gallery))
            {
                errors.Add($"{subject} must name a gallery for output '{assignment.Id}'.");
            }
        }
    }

    private static void ValidateHash(string value, string field, ICollection<string> errors)
    {
        if (!Sha256Pattern.IsMatch(value))
        {
            errors.Add($"{field} must be a 64-character SHA-256 value.");
        }
    }

    private static void ValidateDisposition(string value, string field, ICollection<string> errors) =>
        ValidateChoice(value, new HashSet<string>(StringComparer.Ordinal) { "mapped", "ignored" }, $"{field} disposition", errors);

    private static void ValidateChoice(
        string? value,
        IReadOnlySet<string> allowed,
        string field,
        ICollection<string> errors)
    {
        if (value is null || !allowed.Contains(value))
        {
            errors.Add($"{field} must be one of: {string.Join(", ", allowed)}.");
        }
    }

    private static void Require(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };
}
