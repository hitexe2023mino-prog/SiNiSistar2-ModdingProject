using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiNiSistar2.Edi.Core;

public sealed class MappingRepository
{
    private const int SupportedSchemaVersion = 1;
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
    private readonly HashSet<EventKey> _generatedKeys = new();
    private readonly object _eventLock = new();

    private MappingRepository(MappingDocument document)
    {
        Document = document;
        _events = document.Events.ToDictionary(x => x.Key);
    }

    public MappingDocument Document { get; }

    public static MappingValidationResult Load(string path, string? generatedPath = null)
    {
        try
        {
            var json = File.ReadAllText(path);
            MappingValidationResult result = Parse(json);
            if (result.IsValid && !string.IsNullOrEmpty(generatedPath))
            {
                IReadOnlyList<string> generatedErrors = result.Repository!.LoadGenerated(generatedPath);
                if (generatedErrors.Count > 0)
                {
                    return new(null, generatedErrors);
                }
            }

            return result;
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

    public string SelectFiller(string channel, IReadOnlySet<string> activeStatuses)
    {
        foreach (var rule in Document.StatusRules)
        {
            if (rule.Disposition == "mapped"
                && rule.Channel == channel
                && activeStatuses.Contains(rule.StatusId))
            {
                return rule.FillerGallery!;
            }
        }

        return Document.DefaultFillers[channel];
    }

    public void RegisterGenerated(EventMapping mapping)
    {
        var validationDocument = new MappingDocument
        {
            SchemaVersion = Document.SchemaVersion,
            MappingVersion = Document.MappingVersion,
            TargetGameBuild = Document.TargetGameBuild,
            Events = new List<EventMapping> { mapping },
            StatusRules = Document.StatusRules,
            DefaultFillers = Document.DefaultFillers,
        };
        List<string> errors = Validate(validationDocument)
            .Where(error => !error.StartsWith("statusRules must map", StringComparison.Ordinal))
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        lock (_eventLock)
        {
            if (_events.ContainsKey(mapping.Key) && !_generatedKeys.Contains(mapping.Key))
            {
                throw new InvalidDataException("A generated mapping cannot replace an explicit mapping.");
            }

            _events[mapping.Key] = mapping;
            _generatedKeys.Add(mapping.Key);
        }
    }

    public async Task SaveGeneratedAsync(string path, CancellationToken cancellationToken = default)
    {
        EventMapping[] generated;
        lock (_eventLock)
        {
            generated = _generatedKeys.Select(key => _events[key])
                .OrderBy(mapping => mapping.Id, StringComparer.Ordinal)
                .ToArray();
        }

        var document = new GeneratedMappingDocument(
            1,
            Document.TargetGameBuild,
            DateTimeOffset.UtcNow,
            generated);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    private IReadOnlyList<string> LoadGenerated(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        try
        {
            GeneratedMappingDocument? generated = JsonSerializer.Deserialize<GeneratedMappingDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (generated is null || generated.SchemaVersion != 1)
            {
                return new[] { $"Unsupported or empty generated mapping file '{path}'." };
            }

            if (!string.Equals(
                    generated.TargetGameBuild.GameAssemblySha256,
                    Document.TargetGameBuild.GameAssemblySha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    generated.TargetGameBuild.GlobalMetadataSha256,
                    Document.TargetGameBuild.GlobalMetadataSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new[] { $"Generated mappings in '{path}' target a different game build." };
            }

            foreach (EventMapping mapping in generated.Events)
            {
                RegisterGenerated(mapping);
            }

            return Array.Empty<string>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new[] { $"Could not load generated mappings '{path}': {exception.Message}" };
        }
    }

    private static List<string> Validate(MappingDocument document)
    {
        var errors = new List<string>();

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"Unsupported schemaVersion {document.SchemaVersion}; expected {SupportedSchemaVersion}.");
        }

        Require(document.MappingVersion, "mappingVersion", errors);
        ValidateHash(document.TargetGameBuild.GameAssemblySha256, "targetGameBuild.gameAssemblySha256", errors);
        ValidateHash(document.TargetGameBuild.GlobalMetadataSha256, "targetGameBuild.globalMetadataSha256", errors);

        foreach (var channel in EdiChannels.All)
        {
            if (!document.DefaultFillers.TryGetValue(channel, out var filler) || string.IsNullOrWhiteSpace(filler))
            {
                errors.Add($"defaultFillers must define a non-empty '{channel}' gallery.");
            }
        }

        foreach (var unknown in document.DefaultFillers.Keys.Where(x => !EdiChannels.All.Contains(x)))
        {
            errors.Add($"defaultFillers contains unknown channel '{unknown}'.");
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
            ValidateChoice(mapping.Context, Contexts, $"event '{mapping.Id}' context", errors);
            ValidateChoice(mapping.Phase, Phases, $"event '{mapping.Id}' phase", errors);
            ValidateDisposition(mapping.Disposition, $"event '{mapping.Id}'", errors);

            if (!keys.Add(mapping.Key))
            {
                errors.Add($"Duplicate event key '{FormatKey(mapping.Key)}'.");
            }

            if (mapping.Disposition == "mapped")
            {
                Require(mapping.Gallery, $"mapped event '{mapping.Id}' gallery", errors);
                if (mapping.Channels.Count == 0)
                {
                    errors.Add($"Mapped event '{mapping.Id}' must contain at least one channel.");
                }

                foreach (var channel in mapping.Channels)
                {
                    if (!EdiChannels.All.Contains(channel))
                    {
                        errors.Add($"Mapped event '{mapping.Id}' contains unknown channel '{channel}'.");
                    }
                }

                if (mapping.Channels.Count != mapping.Channels.Distinct(StringComparer.Ordinal).Count())
                {
                    errors.Add($"Mapped event '{mapping.Id}' contains duplicate channels.");
                }

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
                if (rule.Channel is null || !EdiChannels.All.Contains(rule.Channel))
                {
                    errors.Add($"Mapped status '{rule.StatusId}' must use channel 'main' or 'breast'.");
                }

                Require(rule.FillerGallery, $"mapped status '{rule.StatusId}' fillerGallery", errors);
            }
            else if (rule.Disposition == "ignored")
            {
                Require(rule.IgnoreReason, $"ignored status '{rule.StatusId}' ignoreReason", errors);
            }
        }

        var breastRule = document.StatusRules.FirstOrDefault(x => x.DisplayName == "膨乳");
        if (breastRule is null
            || breastRule.Disposition != "mapped"
            || breastRule.Channel != EdiChannels.Breast
            || breastRule.FillerGallery != "filler-breast-swollen")
        {
            errors.Add("statusRules must map 膨乳 to breast/filler-breast-swollen.");
        }

        return errors;
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

    private static string FormatKey(EventKey key) =>
        $"{key.Context}/{key.ActorId}/{key.AnimationId}/{key.Phase}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record GeneratedMappingDocument(
        int SchemaVersion,
        TargetGameBuild TargetGameBuild,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<EventMapping> Events);
}
