using System.Text.Json;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class RepositoryLayoutTests
{
    /// <summary>
    /// AC-039: startup asks EDI to re-read the gallery root it already has. No files are
    /// transferred: uploading them would move EDI's gallery root to its upload folder and lose the
    /// definition table, which is what left `filler-main` and `filler-breast` unplayable
    /// (CHG-033, DEC-029).
    /// </summary>
    [Fact]
    public async Task StartupAsksEdiToRereadTheGalleryRoot()
    {
        string gallery = Path.Combine(TestMappings.FindRepositoryRoot(), "Edi", "Gallery");
        MappingRepository mappings = LoadShippedMapping();
        var reloads = 0;

        GalleryRegistrationResult result = await GalleryRegistration.ReloadAsync(
            gallery,
            mappings,
            _ =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal(1, reloads);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Stray);

        // The defaults are the ones that were missing in the field, so they are named explicitly.
        Assert.Contains("filler-main", result.Fillers);
        Assert.Contains("filler-breast", result.Fillers);
    }

    /// <summary>
    /// AC-045: no asset may express "do not move" as a waveform. A still script makes "idle" and
    /// "receiving another output's gallery" the same observation, which is exactly how a piston
    /// fed the breast filler went unnoticed (FR-047, DEC-024).
    /// </summary>
    [Fact]
    public void NoAssetExpressesStillnessAsAWaveform()
    {
        string gallery = Path.Combine(TestMappings.FindRepositoryRoot(), "Edi", "Gallery");

        string[] still = Directory
            .EnumerateFiles(gallery, "*.funscript", SearchOption.AllDirectories)
            .Where(path => Funscript.TryRead(path) is { } script
                           && script.Actions.Count > 0
                           && script.Actions.Select(action => action.Pos).Distinct().Count() <= 1)
            .ToArray();

        Assert.Empty(still);
    }

    /// <summary>
    /// A gallery must carry exactly the variants of the outputs it is meant for: the target set is
    /// derived from the variants, so a stray one makes the derivation meaningless (FR-049, FR-057).
    /// </summary>
    [Fact]
    public void EveryGalleryVariantBelongsToAnOutputInTheRoster()
    {
        string gallery = Path.Combine(TestMappings.FindRepositoryRoot(), "Edi", "Gallery");
        MappingRepository mappings = LoadShippedMapping();

        Assert.Empty(GalleryRegistration.FindStrayVariants(gallery, mappings));

        foreach (string filler in GalleryRegistration.FillerGalleries(mappings))
        {
            IReadOnlyList<string> outputs = GalleryRegistration.OutputsForFiller(mappings, filler);
            Assert.NotEmpty(outputs);

            foreach (OutputBinding output in mappings.Outputs)
            {
                string path = Path.Combine(gallery, output.EdiVariant, $"{filler}.funscript");
                bool shouldExist = outputs.Contains(output.Id, StringComparer.Ordinal);
                Assert.True(
                    File.Exists(path) == shouldExist,
                    shouldExist
                        ? $"{filler} is selected for {output.Id} but {path} is missing"
                        : $"{filler} is not for {output.Id}, so {path} must not exist");
            }
        }
    }

    private static MappingRepository LoadShippedMapping() =>
        MappingRepository.Load(Path.Combine(
            TestMappings.FindRepositoryRoot(),
            "BepInEx", "config", "community.sinisistar2.edi", "mappings.json")).Repository!;

    [Fact]
    public void RuntimeFilesAndRequiredFillerVariantsExistInRepositoryLayout()
    {
        string root = TestMappings.FindRepositoryRoot();
        string gallery = Path.Combine(root, "Edi", "Gallery");
        string[] required =
        {
            Path.Combine(root, "winhttp.dll"),
            Path.Combine(root, "dotnet", "coreclr.dll"),
            Path.Combine(root, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"),
            Path.Combine(root, "BepInEx", "interop", "SiNiSistar2.dll"),
            Path.Combine(root, "BepInEx", "config", "community.sinisistar2.edi", "mappings.json"),
            Path.Combine(root, "BepInEx", "plugins", "community.sinisistar2.edi", "SiNiSistar2.Edi.Core.dll"),
            Path.Combine(root, "BepInEx", "plugins", "community.sinisistar2.edi", "SiNiSistar2.Edi.Plugin.dll"),
            Path.Combine(gallery, "Definitions.csv"),
            Path.Combine(root, "Edi", "EdiConfig.json"),
            Path.Combine(gallery, "a10-main", "filler-main.funscript"),
            Path.Combine(gallery, "a10-main", "filler-main-parasite.funscript"),
            Path.Combine(gallery, "ufo-left", "filler-breast.funscript"),
            Path.Combine(gallery, "ufo-right", "filler-breast.funscript"),
            Path.Combine(gallery, "ufo-left", "filler-breast-swollen.funscript"),
            Path.Combine(gallery, "ufo-right", "filler-breast-swollen.funscript"),
        };
        Assert.All(required, path => Assert.True(File.Exists(path), path));
        Assert.Contains(
            @"target_assembly = BepInEx\core\BepInEx.Unity.IL2CPP.dll",
            File.ReadAllText(Path.Combine(root, "doorstop_config.ini")),
            StringComparison.Ordinal);

        using JsonDocument config = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "Edi", "EdiConfig.json")));
        JsonElement edi = config.RootElement.GetProperty("Edi");
        Assert.True(edi.GetProperty("UseChannels").GetBoolean());

        // One channel per device, plus the holding channel unrecognised devices are parked on so
        // they cannot land on an output the MOD drives (SPEC001 7.4 E4).
        string?[] channels = edi.GetProperty("Channels").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(new[] { "main", "breast-left", "breast-right", "unassigned" }, channels);
        Assert.Equal("unassigned", edi.GetProperty("UnassignedDeviceChannel").GetString());
        Assert.Contains(edi.GetProperty("UnassignedDeviceChannel").GetString(), channels);

        // The MOD owns filler selection and stopping, so EDI must not retain a filler of its own.
        Assert.False(edi.GetProperty("Filler").GetBoolean());
        Assert.True(edi.GetProperty("StopClearsFiller").GetBoolean());
        Assert.True(config.RootElement.GetProperty("Gallery")
            .GetProperty("StrictVariantResolution").GetBoolean());

        // Edi resolves its config from the folder the user selected, which for this repository is
        // the gallery folder itself. Both copies therefore have to carry the same settings, and
        // each has to point at the gallery root from where it sits.
        using JsonDocument galleryConfig = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(gallery, "EdiConfig.json")));
        Assert.Equal(
            "./Gallery",
            config.RootElement.GetProperty("Gallery").GetProperty("GalleryPath").GetString());
        Assert.Equal(
            "./",
            galleryConfig.RootElement.GetProperty("Gallery").GetProperty("GalleryPath").GetString());
        Assert.True(galleryConfig.RootElement.GetProperty("Gallery")
            .GetProperty("StrictVariantResolution").GetBoolean());
        Assert.True(galleryConfig.RootElement.GetProperty("Edi")
            .GetProperty("StopClearsFiller").GetBoolean());
        Assert.Equal(
            "unassigned",
            galleryConfig.RootElement.GetProperty("Edi")
                .GetProperty("UnassignedDeviceChannel").GetString());

        JsonElement devices = config.RootElement.GetProperty("Devices").GetProperty("Devices");
        MappingRepository mappings = LoadShippedMapping();
        foreach (OutputBinding output in mappings.Outputs)
        {
            JsonElement device = devices.GetProperty(output.EdiDeviceName);
            Assert.Equal(output.Id, device.GetProperty("Channel").GetString());
            Assert.Equal(output.EdiVariant, device.GetProperty("Variant").GetString());
        }
    }

    /// <summary>
    /// Every filler a status rule can select must exist as an asset and have a definition row,
    /// otherwise the mapping resolves to a gallery EDI cannot play.
    /// </summary>
    [Fact]
    public void EveryMappedStatusFillerHasAnAssetAndADefinitionRow()
    {
        string root = TestMappings.FindRepositoryRoot();
        string gallery = Path.Combine(root, "Edi", "Gallery");
        MappingRepository mappings = MappingRepository
            .Load(Path.Combine(root, "BepInEx", "config", "community.sinisistar2.edi", "mappings.json"))
            .Repository!;
        IReadOnlyList<EdiGalleryDefinition> definitions =
            EdiGalleryDefinitions.Read(Path.Combine(gallery, "Definitions.csv"));

        var required = mappings.Document.StatusRules
            .Where(rule => rule.Disposition == "mapped")
            .SelectMany(rule => rule.Outputs.Select(
                o => (StatusId: rule.StatusId, Output: o.Id, Gallery: o.Gallery)))
            .Concat(mappings.Document.DefaultFillers.Select(
                x => (StatusId: "default", Output: x.Key, Gallery: x.Value)))
            .Where(x => x.Gallery is not null)
            .ToArray();

        Assert.NotEmpty(required);
        foreach ((string statusId, string output, string? filler) in required)
        {
            string variant = mappings.VariantFor(output)!;
            string path = Path.Combine(gallery, variant, $"{filler}.funscript");
            Assert.True(File.Exists(path), $"{statusId} -> {filler}: missing {path}");

            Assert.True(
                definitions.Any(row => row.FileName == filler),
                $"{statusId} -> {filler}: no row in Definitions.csv");
        }

        // Every row is a plain gallery: EDI must not keep a filler of its own (DEC-023, CHG-027).
        Assert.All(definitions, row => Assert.Equal("gallery", row.Type));
    }

    /// <summary>The parasite filler exists to be gentler than the default piston filler.</summary>
    [Fact]
    public void ParasiteFillerIsWeakerAndSlowerThanTheDefaultPistonFiller()
    {
        string gallery = Path.Combine(TestMappings.FindRepositoryRoot(), "Edi", "Gallery", "a10-main");
        FunscriptDocument normal = Funscript.TryRead(Path.Combine(gallery, "filler-main.funscript"))!;
        FunscriptDocument parasite = Funscript.TryRead(Path.Combine(gallery, "filler-main-parasite.funscript"))!;

        static int Amplitude(FunscriptDocument script) =>
            script.Actions.Max(a => a.Pos) - script.Actions.Min(a => a.Pos);

        // Strokes per second: fewer means a slower, less insistent pattern.
        static double Rate(FunscriptDocument script) =>
            script.Actions.Count / (script.DurationMilliseconds / 1000d);

        Assert.True(
            Amplitude(parasite) < Amplitude(normal),
            $"amplitude {Amplitude(parasite)} should be below {Amplitude(normal)}");
        Assert.True(
            Rate(parasite) < Rate(normal),
            $"rate {Rate(parasite):F2}/s should be below {Rate(normal):F2}/s");
        Assert.Equal(parasite.Actions[0].Pos, parasite.Actions[^1].Pos);
    }

    /// <summary>
    /// AC-016: the swollen filler has to be a stronger stimulus than the normal one, on both
    /// sides. Strength is measured as travel and how often the device is told to move.
    ///
    /// The funscript `range` field is deliberately not used: the authoring GUI writes 100 for
    /// every script it saves, so the moment a filler is edited through the GUI the two files
    /// agree on `range` no matter what was drawn. Asserting on it made this test pass only until
    /// someone used the editor.
    /// </summary>
    [Theory]
    [InlineData("ufo-left")]
    [InlineData("ufo-right")]
    public void SwollenFillerIsMechanicallyStrongerThanNormalFiller(string variant)
    {
        string gallery = Path.Combine(TestMappings.FindRepositoryRoot(), "Edi", "Gallery", variant);
        FunscriptDocument normal = Funscript.TryRead(Path.Combine(gallery, "filler-breast.funscript"))!;
        FunscriptDocument swollen = Funscript.TryRead(Path.Combine(gallery, "filler-breast-swollen.funscript"))!;

        Assert.NotNull(normal);
        Assert.NotNull(swollen);

        static int Amplitude(FunscriptDocument script) =>
            script.Actions.Max(action => action.Pos) - script.Actions.Min(action => action.Pos);

        Assert.True(
            Amplitude(swollen) > Amplitude(normal),
            $"{variant}: swollen amplitude {Amplitude(swollen)} should exceed {Amplitude(normal)}");
        Assert.True(
            swollen.Actions.Count > normal.Actions.Count,
            $"{variant}: swollen has {swollen.Actions.Count} actions, normal has {normal.Actions.Count}");
    }
}
