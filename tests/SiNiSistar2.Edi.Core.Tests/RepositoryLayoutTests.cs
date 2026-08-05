using System.Text.Json;

namespace SiNiSistar2.Edi.Core.Tests;

public sealed class RepositoryLayoutTests
{
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
            Path.Combine(gallery, "EdiConfig.json"),
            Path.Combine(gallery, "a10-main", "filler-main.funscript"),
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
            File.ReadAllText(Path.Combine(gallery, "EdiConfig.json")));
        JsonElement edi = config.RootElement.GetProperty("Edi");
        Assert.True(edi.GetProperty("UseChannels").GetBoolean());
        Assert.Equal(new[] { "main", "breast" }, edi.GetProperty("Channels")
            .EnumerateArray()
            .Select(x => x.GetString()));

        JsonElement devices = config.RootElement.GetProperty("Devices").GetProperty("Devices");
        Assert.Equal("main", devices.GetProperty("Vorze Piston").GetProperty("Channel").GetString());
        Assert.Equal("ufo-left", devices.GetProperty("Vorze UFO TW Rotate: 1").GetProperty("Variant").GetString());
        Assert.Equal("ufo-right", devices.GetProperty("Vorze UFO TW Rotate: 2").GetProperty("Variant").GetString());
        Assert.Equal(
            "breast",
            devices.GetProperty("Vorze UFO TW Rotate: 1").GetProperty("Channel").GetString());
    }

    [Fact]
    public void SwollenFillerIsMechanicallyStrongerThanNormalFiller()
    {
        string gallery = Path.Combine(TestMappings.FindRepositoryRoot(), "Edi", "Gallery");
        using JsonDocument normal = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(gallery, "ufo-left", "filler-breast.funscript")));
        using JsonDocument swollen = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(gallery, "ufo-left", "filler-breast-swollen.funscript")));

        Assert.True(
            swollen.RootElement.GetProperty("range").GetInt32()
                > normal.RootElement.GetProperty("range").GetInt32());
        Assert.True(
            swollen.RootElement.GetProperty("actions").GetArrayLength()
                > normal.RootElement.GetProperty("actions").GetArrayLength());
    }
}
