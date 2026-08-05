using System.Text.Json;
using SiNiSistar2.Edi.Core;

namespace SiNiSistar2.Edi.Core.Tests;

internal static class TestMappings
{
    internal const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public static MappingRepository Create(params EventMapping[] events)
    {
        var document = new MappingDocument
        {
            SchemaVersion = 1,
            MappingVersion = "test",
            TargetGameBuild = new TargetGameBuild
            {
                GameAssemblySha256 = Hash,
                GlobalMetadataSha256 = Hash,
            },
            Events = events.ToList(),
            StatusRules = new List<StatusRule>
            {
                new()
                {
                    StatusId = "Breast",
                    DisplayName = "膨乳",
                    Disposition = "mapped",
                    Channel = EdiChannels.Breast,
                    FillerGallery = "filler-breast-swollen",
                },
            },
            DefaultFillers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EdiChannels.Main] = "filler-main",
                [EdiChannels.Breast] = "filler-breast",
            },
        };

        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        MappingValidationResult result = MappingRepository.Parse(json);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
        }

        return result.Repository!;
    }

    public static EventMapping Event(
        string id,
        string actor,
        string animation,
        string channel = EdiChannels.Main,
        string gallery = "event-gallery") => new()
        {
            Id = id,
            Context = "hold",
            ActorId = actor,
            AnimationId = animation,
            Phase = "loop",
            Disposition = "mapped",
            Gallery = gallery,
            Channels = new List<string> { channel },
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
}
