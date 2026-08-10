namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// The built-in exclusion verdict for a scene (SPEC004 DEC-310). Name-based rules over the
/// SceneID identifier: boss arenas, tutorials, gallery replays, endings, cinematic "go" run
/// scenes and the character setup scene. areas.json `excluded: false` is the only override
/// (SPEC004 6章); the rules themselves are an implementation assumption recorded in the
/// traceability document.
/// </summary>
internal static class DefaultExclusions
{
    internal static bool IsExcluded(string sceneName)
    {
        if (sceneName.StartsWith("Ga_", StringComparison.Ordinal))
        {
            return true;
        }

        if (sceneName == "Character_Setting" || sceneName == "None")
        {
            return true;
        }

        if (sceneName.Contains("Boss", StringComparison.Ordinal)
            || sceneName.Contains("Tutorial", StringComparison.Ordinal)
            || sceneName.Contains("Ending", StringComparison.Ordinal)
            || sceneName.Contains("Title", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // "_GO"-style suffixes mark one-way cinematic run scenes (e.g. Road_2_1_GO, MonasteryGo_*).
        return sceneName.EndsWith("_GO", StringComparison.OrdinalIgnoreCase)
            || sceneName.Contains("Go_", StringComparison.Ordinal);
    }
}
