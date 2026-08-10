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

    /// <summary>Spawners found in the scene, tuned or not — 0 means no mechanism can act.</summary>
    public int SpawnerCount { get; init; }

    /// <summary>
    /// EnemyObject instances present in the scene. This build populates ordinary areas with these
    /// directly rather than through spawners, so the two counts together say whether an area is
    /// empty or merely spawner-less.
    /// </summary>
    public int SceneEnemyCount { get; init; }

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
            $"AREA   {s.AreaName}  visit #{s.VisitCount}  spawners {s.SpawnerCount}  "
            + $"enemies {s.SceneEnemyCount}  rng {(s.Seeded ? $"seed {s.Seed}" : "system")}",
        };

        if (s.Excluded)
        {
            lines.Add($"       excluded by {s.ExclusionSource}: no intervention in this area.");
            return lines;
        }

        // Each spawner draws its own multipliers (5.2), so a single number cannot describe the
        // area; the mean is shown and labelled as such rather than picking one spawner's draw.
        // A scene with no spawner at all cannot do anything, and saying so beats every other line
        // on this panel when it happens.
        if (s.TunedSpawnerCount == 0 && s.SpawnerCount == 0)
        {
            lines.Add(s.SceneEnemyCount > 0
                ? $"TUNING no EnemySpawner here, but {s.SceneEnemyCount} enemies are placed directly: "
                  + "nothing for the spawner mechanisms to act on."
                : "TUNING no EnemySpawner and no enemy in this scene.");
        }

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

    /// <summary>The config file the enable switch lives in, named so the notice is actionable.</summary>
    public const string ConfigFileName = "BepInEx/config/community.sinisistar2.spawn.cfg";

    /// <summary>
    /// Turns the commands ON from inside the panel and persists the choice. Enabling only, never
    /// disabling: when a command legitimately does nothing (no eligible position, cap reached),
    /// pressing the enable key again is the natural next move, and a toggle turns the tool off at
    /// exactly the moment its user is trying to make it work. A real session lost five rounds to
    /// that. Disabling is available on the panel's switch button (<see cref="DisableCommand"/>).
    /// </summary>
    public const char ToggleKey = '0';

    /// <summary>Turns the commands off. Reachable by clicking the switch, not by a digit.</summary>
    public const char DisableCommand = '-';

    /// <summary>
    /// The debug panel's command list.
    ///
    /// When commands are off the notice has to be impossible to miss: a key that produces no
    /// effect and no message reads as a broken feature rather than a disabled one, which is
    /// exactly how this was first reported.
    /// </summary>
    /// <summary>The panel's status lines, drawn above the buttons.</summary>
    public static IReadOnlyList<string> DebugHeader(HudSnapshot s, bool pressedWhileDisabled = false)
    {
        var lines = new List<string>();

        if (s.DebugCommandsEnabled)
        {
            lines.Add("DEBUG COMMANDS -- ON.  Click a button, or press its number.");
            lines.Add("Caps, area exclusion and the hold/cinematic block still apply.");
        }
        else
        {
            lines.Add(pressedWhileDisabled
                ? "DEBUG COMMANDS -- OFF. THAT DID NOTHING."
                : "DEBUG COMMANDS -- OFF.");
            lines.Add($"Click the switch below, or press [{ToggleKey}], to turn them on.");
        }

        return lines;
    }

    /// <summary>Label of the on/off switch button, which states what a click will do.</summary>
    public static string ToggleLabel(bool enabled) => enabled
        ? "COMMANDS: ON      -- click to turn OFF"
        : $"COMMANDS: OFF     -- click to turn ON   (or press {ToggleKey})";

    /// <summary>Label of one command button.</summary>
    public static string CommandLabel(char key, string text) => $"[{key}]  {text}";

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
