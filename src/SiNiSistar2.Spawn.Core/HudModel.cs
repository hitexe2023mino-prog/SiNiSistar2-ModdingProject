using System.Globalization;

namespace SiNiSistar2.Spawn.Core;

/// <summary>HUD display stage (SPEC004 5.8 表示段階). Cycles Off → Compact → Full → Off.</summary>
public enum HudMode
{
    Off,
    Compact,
    Full,
}

/// <summary>Why stagnation measurement is not advancing, so the HUD can say so rather than look stuck.</summary>
public enum StagnationPause
{
    None,
    Held,
    Cinematic,
}

/// <summary>
/// Everything the HUD shows, captured once per frame by the plugin. Keeping it a plain value type
/// is what lets the whole display be unit-tested without the game (SPEC004 4.1, 12.1).
/// </summary>
public sealed record HudSnapshot
{
    public string AreaName { get; init; } = "-";

    public int VisitCount { get; init; }

    public bool Excluded { get; init; }

    /// <summary>"default" or "areas.json"; only meaningful when <see cref="Excluded"/> is set.</summary>
    public string ExclusionSource { get; init; } = "";

    public bool Seeded { get; init; }

    public int Seed { get; init; }

    // 出現調整 (5.2)
    public bool TuningApplied { get; init; }

    public int TunedSpawnerCount { get; init; }

    public bool HardBase { get; init; }

    public float SpawnCountMultiplier { get; init; } = 1f;

    public float SpawnIntervalMultiplier { get; init; } = 1f;

    public float CoolTimeMultiplier { get; init; } = 1f;

    public float MaxSpawnMultiplier { get; init; } = 1f;

    // 停滞 (5.3)
    public double Dwell { get; init; }

    public double StagnationSeconds { get; init; }

    public float WindowTravel { get; init; }

    public float MoveEpsilon { get; init; }

    public bool Stagnant { get; init; }

    public double? SecondsUntilNextPenalty { get; init; }

    public StagnationPause Paused { get; init; }

    // 追加出現 (5.3)
    public int Spawned { get; init; }

    public int SpawnCap { get; init; }

    public int Alive { get; init; }

    public int AliveCap { get; init; }

    public string LastSpawnNote { get; init; } = "";

    public int OffscreenCandidates { get; init; }

    public int BehindCandidates { get; init; }

    // 疑似宝箱 (5.5)
    public bool MimicEnabled { get; init; }

    public bool MimicPoolPresent { get; init; }

    public int MimicPlaced { get; init; }

    public int MimicCap { get; init; }

    public int MimicUnresolved { get; init; }

    public int MimicHits { get; init; }

    public int MimicMisses { get; init; }

    /// <summary>Pinned next lottery outcome, if a debug command set one (SPEC004 5.9 抽選固定).</summary>
    public string? PinnedOutcome { get; init; }

    public bool DebugCommandsEnabled { get; init; }
}

/// <summary>
/// Builds the HUD's text. Pure string assembly: the plugin only draws what this returns, so the
/// display can be verified without launching the game (SPEC004 FR-330).
/// </summary>
public static class HudModel
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static HudMode Next(HudMode mode) => mode switch
    {
        HudMode.Off => HudMode.Compact,
        HudMode.Compact => HudMode.Full,
        _ => HudMode.Off,
    };

    /// <summary>One line: area, budget, stagnation countdown, pseudo boxes left (SPEC004 5.8 Compact).</summary>
    public static string Compact(HudSnapshot s)
    {
        if (s.Excluded)
        {
            return $"SPAWN | {s.AreaName} | excluded ({s.ExclusionSource})";
        }

        string stagnation = s.Paused != StagnationPause.None
            ? $"paused:{Describe(s.Paused)}"
            : s.Stagnant
                ? $"stagnant, next {Seconds(s.SecondsUntilNextPenalty)}"
                : $"dwell {Seconds(s.Dwell)}/{Seconds(s.StagnationSeconds)}";

        string mimic = s.MimicEnabled
            ? s.MimicPoolPresent ? $" | box {s.MimicPlaced}/{s.MimicCap}" : " | box n/a"
            : "";

        return $"SPAWN | {s.AreaName} #{s.VisitCount} | add {s.Spawned}/{s.SpawnCap} "
            + $"(alive {s.Alive}/{s.AliveCap}) | {stagnation}{mimic}";
    }

    /// <summary>The full panel, one string per line (SPEC004 5.8 Full の6区画).</summary>
    public static IReadOnlyList<string> Full(HudSnapshot s)
    {
        var lines = new List<string>
        {
            $"AREA   {s.AreaName}  visit #{s.VisitCount}  rng {(s.Seeded ? $"seed {s.Seed}" : "system")}",
        };

        if (s.Excluded)
        {
            lines.Add($"       excluded by {s.ExclusionSource}: no intervention in this area.");
            return lines;
        }

        // Each spawner draws its own multipliers (5.2), so a single number cannot describe the
        // area; the mean is shown and labelled as such rather than picking one spawner's draw.
        lines.Add(s.TuningApplied
            ? $"TUNING {s.TunedSpawnerCount} spawner(s), base {(s.HardBase ? "Hard" : "Normal")}, mean  "
              + $"count x{Num(s.SpawnCountMultiplier)}  interval x{Num(s.SpawnIntervalMultiplier)}  "
              + $"cool x{Num(s.CoolTimeMultiplier)}  pool x{Num(s.MaxSpawnMultiplier)}"
            : "TUNING all multipliers are 1.0; spawners left untouched.");

        lines.Add(s.Paused != StagnationPause.None
            ? $"STAY   measurement paused ({Describe(s.Paused)})  dwell {Seconds(s.Dwell)}"
            : $"STAY   dwell {Seconds(s.Dwell)}/{Seconds(s.StagnationSeconds)}  "
              + $"moved {Num(s.WindowTravel)}/{Num(s.MoveEpsilon)}  "
              + $"{(s.Stagnant ? "STAGNANT" : "moving")}  next {Seconds(s.SecondsUntilNextPenalty)}");

        lines.Add($"ADD    {s.Spawned}/{s.SpawnCap} this visit, alive {s.Alive}/{s.AliveCap}"
            + (s.LastSpawnNote.Length > 0 ? $"  last: {s.LastSpawnNote}" : ""));

        lines.Add($"POINTS offscreen {s.OffscreenCandidates}, of which behind {s.BehindCandidates}"
            + (s.OffscreenCandidates == 0 ? "  (no eligible position: spawns will be skipped)" : ""));

        if (s.MimicEnabled)
        {
            lines.Add(s.MimicPoolPresent
                ? $"BOX    placed {s.MimicPlaced}/{s.MimicCap}  waiting {s.MimicUnresolved}  "
                  + $"mimic {s.MimicHits}  reward {s.MimicMisses}"
                  + (s.PinnedOutcome is null ? "" : $"  PINNED->{s.PinnedOutcome}")
                : "BOX    this area has no mimic pool; pseudo boxes cannot be placed here.");
        }
        else
        {
            lines.Add("BOX    disabled (MimicBoxEnabled=false)");
        }

        return lines;
    }

    /// <summary>The debug panel's command list, with the disabled notice when commands are off.</summary>
    public static IReadOnlyList<string> DebugPanel(HudSnapshot s)
    {
        var lines = new List<string> { "DEBUG COMMANDS" };

        if (!s.DebugCommandsEnabled)
        {
            lines.Add("  disabled. Set [Debug] DebugCommandsEnabled = true to use these.");
        }

        foreach ((char key, string label) in Commands)
        {
            lines.Add($"  [{key}] {label}");
        }

        lines.Add(s.DebugCommandsEnabled
            ? "  Caps, area exclusion and the hold/cinematic block still apply."
            : "  (state above is shown regardless)");

        return lines;
    }

    /// <summary>
    /// Digit key to command. Held in Core so the panel text and the dispatch table cannot drift
    /// apart: the plugin switches on the same characters this list documents.
    /// </summary>
    public static IReadOnlyList<(char Key, string Label)> Commands { get; } = new[]
    {
        ('1', "force an additional spawn now (off-screen)"),
        ('2', "force an ambush spawn now (off-screen + behind)"),
        ('3', "place one mimic pseudo box"),
        ('4', "pin the next box lottery to MIMIC"),
        ('5', "pin the next box lottery to REWARD"),
        ('6', "re-roll this area (tuning + boxes)"),
        ('7', "write the area diagnostics JSON now"),
        ('8', "advance stagnation to just before it fires"),
    };

    private static string Describe(StagnationPause pause) => pause switch
    {
        StagnationPause.Held => "hold",
        StagnationPause.Cinematic => "event",
        _ => "-",
    };

    private static string Seconds(double? value) =>
        value is null ? "-" : value.Value.ToString("0.#", Culture) + "s";

    private static string Num(double value) => value.ToString("0.##", Culture);
}
