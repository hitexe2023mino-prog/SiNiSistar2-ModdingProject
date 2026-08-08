using System.Text.Json;
using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

internal static class TestMappings
{
    internal const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    internal const string Main = "main";
    internal const string BreastLeft = "breast-left";
    internal const string BreastRight = "breast-right";

    /// <summary>The shipped roster: one output per physical device (SPEC001 4.2).</summary>
    public static List<OutputBinding> Roster() => new()
    {
        new OutputBinding
        {
            Id = Main,
            DisplayName = "A10 piston",
            EdiDeviceName = "Vorze Piston",
            EdiVariant = "a10-main",
        },
        new OutputBinding
        {
            Id = BreastLeft,
            DisplayName = "U.F.O TW left",
            EdiDeviceName = "Vorze UFO TW Rotate: 1",
            EdiVariant = "ufo-left",
        },
        new OutputBinding
        {
            Id = BreastRight,
            DisplayName = "U.F.O TW right",
            EdiDeviceName = "Vorze UFO TW Rotate: 2",
            EdiVariant = "ufo-right",
        },
    };

    public static MappingRepository Create(params EventMapping[] events) =>
        Create(events, null, null);

    public static MappingRepository Create(
        IEnumerable<EventMapping>? events,
        IEnumerable<StatusRule>? statusRules,
        IDictionary<string, string?>? defaultFillers)
    {
        var document = new MappingDocument
        {
            SchemaVersion = MappingRepository.SupportedSchemaVersion,
            MappingVersion = "test",
            TargetGameBuild = new TargetGameBuild
            {
                GameAssemblySha256 = Hash,
                GlobalMetadataSha256 = Hash,
            },
            Outputs = Roster(),
            Events = (events ?? Array.Empty<EventMapping>()).ToList(),
            StatusRules = (statusRules ?? DefaultStatusRules()).ToList(),
            DefaultFillers = new Dictionary<string, string?>(
                defaultFillers ?? new Dictionary<string, string?>
                {
                    [Main] = "filler-main",
                    [BreastLeft] = "filler-breast",
                    [BreastRight] = "filler-breast",
                },
                StringComparer.Ordinal),
        };

        MappingValidationResult result = MappingRepository.Parse(Serialize(document));
        if (!result.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
        }

        return result.Repository!;
    }

    /// <summary>Parses without asserting validity, for tests that assert on the errors.</summary>
    public static MappingValidationResult Parse(MappingDocument document) =>
        MappingRepository.Parse(Serialize(document));

    public static MappingDocument Document(
        IEnumerable<EventMapping>? events = null,
        IEnumerable<StatusRule>? statusRules = null,
        IDictionary<string, string?>? defaultFillers = null,
        IEnumerable<OutputBinding>? roster = null,
        int schemaVersion = MappingRepository.SupportedSchemaVersion) => new()
        {
            SchemaVersion = schemaVersion,
            MappingVersion = "test",
            TargetGameBuild = new TargetGameBuild
            {
                GameAssemblySha256 = Hash,
                GlobalMetadataSha256 = Hash,
            },
            Outputs = (roster ?? Roster()).ToList(),
            Events = (events ?? Array.Empty<EventMapping>()).ToList(),
            StatusRules = (statusRules ?? DefaultStatusRules()).ToList(),
            DefaultFillers = new Dictionary<string, string?>(
                defaultFillers ?? new Dictionary<string, string?>
                {
                    [Main] = "filler-main",
                    [BreastLeft] = "filler-breast",
                    [BreastRight] = "filler-breast",
                },
                StringComparer.Ordinal),
        };

    public static List<StatusRule> DefaultStatusRules() => new()
    {
        new StatusRule
        {
            StatusId = "Breast",
            DisplayName = "膨乳",
            Disposition = "mapped",
            Priority = 10,
            Outputs = new List<OutputAssignment>
            {
                new() { Id = BreastLeft, Gallery = "filler-breast-swollen" },
                new() { Id = BreastRight, Gallery = "filler-breast-swollen" },
            },
        },
    };

    public static StatusRule Status(
        string statusId,
        string gallery,
        int priority = 0,
        params string[] outputs) => new()
        {
            StatusId = statusId,
            DisplayName = statusId,
            Disposition = "mapped",
            Priority = priority,
            Outputs = outputs
                .Select(output => new OutputAssignment { Id = output, Gallery = gallery })
                .ToList(),
        };

    public static EventMapping Event(
        string id,
        string actor,
        string animation,
        string output = Main,
        string gallery = "event-gallery",
        string stageId = EventKey.DefaultStageId) =>
        Event(id, actor, animation, new[] { new OutputAssignment { Id = output, Gallery = gallery } }, stageId);

    public static EventMapping Event(
        string id,
        string actor,
        string animation,
        IReadOnlyList<OutputAssignment> outputs,
        string stageId = EventKey.DefaultStageId) => new()
        {
            Id = id,
            Context = "hold",
            ActorId = actor,
            AnimationId = animation,
            Phase = "loop",
            StageId = stageId,
            Disposition = "mapped",
            Outputs = outputs.ToList(),
            SeekMode = "animation-time",
        };

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SiNiSistar2.Edi.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Serialize(MappingDocument document) =>
        JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
}
