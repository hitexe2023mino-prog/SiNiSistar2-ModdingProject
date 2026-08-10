using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Enemy.Character.Sewage;
using HarmonyLib;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// Randomized enemy presence per area: spawner tuning, off-screen/behind/stagnation extra spawns,
/// gimmick clones and mimic pseudo treasure boxes. Implements SPEC004 v1.1.
///
/// Every intervention is a runtime write that is restored, an object the scene destroys, or a
/// pooled enemy the game itself retires; nothing touches the game's files or its save
/// (SPEC004 FR-302, 4.4).
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class SpawnPlugin : BasePlugin
{
    public const string PluginGuid = "community.sinisistar2.spawn";
    public const string PluginName = "SiNiSistar2 Spawn Randomizer";
    public const string PluginVersion = "0.1.0";

    private Harmony? _harmony;
    private SpawnObserver? _observer;
    private ConfigEntry<bool>? _enabledEntry;

    public override void Load()
    {
        SpawnRuntime.Log = Log;

        SpawnOptions options = BindOptions();
        string areasJson = ReadAreasJson();

        ProfileValidation validation = SpawnProfileFactory.Create(options, areasJson, KnownSceneNames());
        foreach (string error in validation.Errors)
        {
            Log.LogError(error);
        }

        foreach (string warning in validation.Warnings)
        {
            Log.LogWarning(warning);
        }

        SpawnProfile profile = validation.Profile;
        profile = profile with { RewardTable = ValidateRewardItems(profile.RewardTable) };

        if (!profile.Enabled)
        {
            Log.LogInfo($"{PluginName} {PluginVersion}: Enabled=false, no intervention.");
            return;
        }

        VerifyGameBuild();

        SpawnRuntime.Profile = profile;
        SpawnRuntime.Enabled = true;

        if (profile.MimicBoxEnabled)
        {
            ApplyMimicPatch();
        }

        _observer = AddComponent<SpawnObserver>();
        _observer.InitialiseHud(profile.HudMode);

        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; count={profile.SpawnCount}, "
            + $"interval={profile.SpawnInterval}, cool={profile.CoolTime}, maxSpawn={profile.MaxSpawn}, "
            + $"stagnation={profile.StagnationSeconds}s/{profile.StagnationPenaltyInterval}s, "
            + $"ambush={profile.AmbushChance:P0}, mimicBox={(profile.MimicBoxEnabled ? $"on ({profile.MimicChance:P0})" : "off")}, "
            + $"gimmicks={(profile.GimmickCloningEnabled ? string.Join("/", profile.AllowedGimmickTypes) : "off")}, "
            + $"areasOverridden={profile.AreaOverrides.Count}, seed={profile.Seed}.");

        Log.LogInfo(
            $"HUD: press {SpawnRuntime.HudHotkey} to cycle it (starts {profile.HudMode}); "
            + $"{SpawnRuntime.DebugPanelHotkey} opens the debug panel "
            + $"({(profile.DebugCommandsEnabled ? "commands active" : "read-only; set [Debug] DebugCommandsEnabled=true")}).");
    }

    public override bool Unload()
    {
        SpawnRuntime.Enabled = false;
        _observer?.Rollback();

        try
        {
            _harmony?.UnpatchSelf();
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Harmony patches were not fully removed: {exception.Message}");
        }

        SpawnRuntime.Profile = new SpawnProfile { Enabled = false };
        return true;
    }

    private SpawnOptions BindOptions()
    {
        var defaults = new SpawnOptions();

        _enabledEntry = Config.Bind("General", "Enabled", defaults.Enabled, "Master switch for every mechanism.");
        ConfigEntry<int> seed = Config.Bind(
            "General",
            "Seed",
            defaults.Seed,
            "Random seed. 0 draws differently every visit; any other value reproduces the same "
            + "draws for the same area and visit number (SPEC004 5.6).");
        ConfigEntry<bool> diagnostics = Config.Bind(
            "General",
            "DiagnosticsEnabled",
            defaults.DiagnosticsEnabled,
            "Dump a per-area inventory JSON (spawners, pools, gimmick type histogram, treasure "
            + "boxes) under BepInEx/diagnostics/community.sinisistar2.spawn/.");

        ConfigEntry<float> countMin = Config.Bind("SpawnerTuning", "SpawnCountMultiplierMin", defaults.SpawnCountMultiplierMin, "Lower bound of the per-visit spawn count multiplier (>= 1).");
        ConfigEntry<float> countMax = Config.Bind("SpawnerTuning", "SpawnCountMultiplierMax", defaults.SpawnCountMultiplierMax, "Upper bound of the per-visit spawn count multiplier.");
        ConfigEntry<float> intervalMin = Config.Bind("SpawnerTuning", "SpawnIntervalMultiplierMin", defaults.SpawnIntervalMultiplierMin, "Lower bound of the spawn interval multiplier (<= 1 shortens).");
        ConfigEntry<float> intervalMax = Config.Bind("SpawnerTuning", "SpawnIntervalMultiplierMax", defaults.SpawnIntervalMultiplierMax, "Upper bound of the spawn interval multiplier.");
        ConfigEntry<float> coolMin = Config.Bind("SpawnerTuning", "CoolTimeMultiplierMin", defaults.CoolTimeMultiplierMin, "Lower bound of the spawner cool time multiplier (<= 1 shortens).");
        ConfigEntry<float> coolMax = Config.Bind("SpawnerTuning", "CoolTimeMultiplierMax", defaults.CoolTimeMultiplierMax, "Upper bound of the spawner cool time multiplier.");
        ConfigEntry<float> maxMin = Config.Bind("SpawnerTuning", "MaxSpawnMultiplierMin", defaults.MaxSpawnMultiplierMin, "Lower bound of the pool size multiplier (>= 1).");
        ConfigEntry<float> maxMax = Config.Bind("SpawnerTuning", "MaxSpawnMultiplierMax", defaults.MaxSpawnMultiplierMax, "Upper bound of the pool size multiplier.");

        ConfigEntry<int> spawnCap = Config.Bind("AdditionalSpawn", "AdditionalSpawnCapPerVisit", defaults.AdditionalSpawnCapPerVisit, "Total extra spawns per area visit (SPEC004 FR-308).");
        ConfigEntry<int> aliveCap = Config.Bind("AdditionalSpawn", "AdditionalAliveCap", defaults.AdditionalAliveCap, "Simultaneously alive extra spawns.");
        ConfigEntry<float> margin = Config.Bind("AdditionalSpawn", "OffscreenMargin", defaults.OffscreenMargin, "Extra viewport margin a point must clear to count as off-screen.");
        ConfigEntry<float> ambush = Config.Bind("AdditionalSpawn", "AmbushChance", defaults.AmbushChance, "Chance a penalty spawn additionally requires a behind-the-player position (FR-310).");
        ConfigEntry<float> stagnation = Config.Bind("AdditionalSpawn", "StagnationSeconds", defaults.StagnationSeconds, "Dwell time in an area before stagnation can begin.");
        ConfigEntry<float> window = Config.Bind("AdditionalSpawn", "StagnationWindowSeconds", defaults.StagnationWindowSeconds, "Window over which movement is measured.");
        ConfigEntry<float> epsilon = Config.Bind("AdditionalSpawn", "StagnationMoveEpsilon", defaults.StagnationMoveEpsilon, "Movement (world units) within the window that counts as not stagnant.");
        ConfigEntry<float> interval = Config.Bind("AdditionalSpawn", "StagnationPenaltyInterval", defaults.StagnationPenaltyInterval, "Seconds between penalty spawns while stagnant.");

        ConfigEntry<string> gimmickTypes = Config.Bind(
            "Gimmick",
            "AllowedGimmickTypes",
            defaults.AllowedGimmickTypes,
            "Comma-separated MonoBehaviour type names that may be cloned. Empty disables cloning "
            + "entirely; only register a type after verifying it in-game (SPEC004 5.4).");
        ConfigEntry<int> gimmickCap = Config.Bind("Gimmick", "GimmickClonesPerVisit", defaults.GimmickClonesPerVisit, "Clones per area visit.");
        ConfigEntry<float> gimmickOffset = Config.Bind("Gimmick", "GimmickCloneOffsetRange", defaults.GimmickCloneOffsetRange, "Random X offset applied to a clone's position.");

        ConfigEntry<bool> mimicEnabled = Config.Bind(
            "MimicBox",
            "MimicBoxEnabled",
            defaults.MimicBoxEnabled,
            "Pseudo treasure boxes backed by real mimics from the scene's own pools. Off by "
            + "default until SPEC004 付録A A-9/A-11 have been measured on this build.");
        ConfigEntry<int> mimicCap = Config.Bind("MimicBox", "MimicBoxesPerVisit", defaults.MimicBoxesPerVisit, "Pseudo boxes per area visit.");
        ConfigEntry<float> mimicChance = Config.Bind("MimicBox", "MimicChance", defaults.MimicChance, "Chance a touched pseudo box resolves as a real mimic (vanilla hold).");
        ConfigEntry<string> rewards = Config.Bind(
            "MimicBox",
            "RewardTable",
            defaults.RewardTable,
            "Miss rewards as ItemID:count:weight, comma separated. Wand items are refused.");
        ConfigEntry<int> lootValue = Config.Bind("MimicBox", "RewardLootValue", defaults.RewardLootValue, "MP orbs scattered on a miss; 0 scatters none.");

        ConfigEntry<bool> logInterventions = Config.Bind("Diagnostics", "LogInterventions", defaults.LogInterventions, "Log each intervention (AC-301..AC-315 rely on this).");

        // The game binds no function keys at all (it is entirely on the new input system), so the
        // only contention is between MODs: F6 opens the funscript authoring GUI and F7..F11 are
        // the pleasure MOD's screens. F12 is Steam's screenshot key. That leaves F1..F5, and F4
        // is avoided because Alt+F4 sits next to a key meant to be pressed repeatedly.
        ConfigEntry<KeyCode> hudHotkey = Config.Bind(
            "Debug",
            "HudHotkey",
            KeyCode.F5,
            "Cycles the HUD: Off -> Compact -> Full. Taken by other MODs in this game folder: F6 "
            + "(funscript authoring GUI), F7..F11 (pleasure MOD screens). F12 is Steam's "
            + "screenshot key.");
        ConfigEntry<KeyCode> panelHotkey = Config.Bind(
            "Debug",
            "DebugPanelHotkey",
            KeyCode.F3,
            "Opens and closes the debug command panel. See HudHotkey for the keys already taken.");
        ConfigEntry<HudMode> hudMode = Config.Bind(
            "Debug",
            "HudMode",
            defaults.HudMode,
            "HUD stage at startup. Off draws nothing during normal play.");
        ConfigEntry<bool> debugCommands = Config.Bind(
            "Debug",
            "DebugCommandsEnabled",
            defaults.DebugCommandsEnabled,
            "Lets the debug panel's number keys act. Off by default so a stray keypress cannot "
            + "spawn anything; the panel still shows the current state. Commands shorten waiting "
            + "and pin lotteries, but never bypass the caps or the excluded areas.");

        SpawnRuntime.HudHotkey = hudHotkey.Value;
        SpawnRuntime.DebugPanelHotkey = panelHotkey.Value;

        // Two features on one key means the second one silently never runs.
        if (hudHotkey.Value != KeyCode.None && hudHotkey.Value == panelHotkey.Value)
        {
            Log.LogWarning(
                $"HudHotkey and DebugPanelHotkey are both {hudHotkey.Value}; only the HUD will "
                + "respond. Give the debug panel a different key.");
        }

        _enabledEntry.SettingChanged += (_, _) =>
        {
            SpawnRuntime.Enabled = _enabledEntry.Value;
        };

        // Turning the commands on is the one setting whose whole audience is mid-session: someone
        // who has just opened the panel and found the keys inert. Requiring a restart there is the
        // difference between a usable tool and a dead end.
        debugCommands.SettingChanged += (_, _) =>
        {
            SpawnRuntime.Profile = SpawnRuntime.Profile with { DebugCommandsEnabled = debugCommands.Value };
            Log.LogInfo(
                $"[debug] commands are now {(debugCommands.Value ? "ACTIVE" : "off")}; "
                + $"open the panel with {SpawnRuntime.DebugPanelHotkey}.");
        };

        // BepInEx never re-reads the file on its own, so opening the panel triggers it here. Only
        // the two switches meant to be live are applied; the tuning values are still read once at
        // startup, which is what the config comments say.
        // Writing the entry persists it (BepInEx saves on set) and fires SettingChanged above,
        // which is what pushes the new value into the active profile.
        SpawnRuntime.SetDebugCommands = value => debugCommands.Value = value;

        SpawnRuntime.ReloadConfig = () =>
        {
            try
            {
                Config.Reload();
                SpawnRuntime.Profile = SpawnRuntime.Profile with { DebugCommandsEnabled = debugCommands.Value };
            }
            catch (Exception exception)
            {
                Log.LogWarning($"The config file could not be re-read: {exception.Message}");
            }
        };

        return new SpawnOptions
        {
            Enabled = _enabledEntry.Value,
            Seed = seed.Value,
            DiagnosticsEnabled = diagnostics.Value,
            SpawnCountMultiplierMin = countMin.Value,
            SpawnCountMultiplierMax = countMax.Value,
            SpawnIntervalMultiplierMin = intervalMin.Value,
            SpawnIntervalMultiplierMax = intervalMax.Value,
            CoolTimeMultiplierMin = coolMin.Value,
            CoolTimeMultiplierMax = coolMax.Value,
            MaxSpawnMultiplierMin = maxMin.Value,
            MaxSpawnMultiplierMax = maxMax.Value,
            AdditionalSpawnCapPerVisit = spawnCap.Value,
            AdditionalAliveCap = aliveCap.Value,
            OffscreenMargin = margin.Value,
            AmbushChance = ambush.Value,
            StagnationSeconds = stagnation.Value,
            StagnationWindowSeconds = window.Value,
            StagnationMoveEpsilon = epsilon.Value,
            StagnationPenaltyInterval = interval.Value,
            AllowedGimmickTypes = gimmickTypes.Value,
            GimmickClonesPerVisit = gimmickCap.Value,
            GimmickCloneOffsetRange = gimmickOffset.Value,
            MimicBoxEnabled = mimicEnabled.Value,
            MimicBoxesPerVisit = mimicCap.Value,
            MimicChance = mimicChance.Value,
            RewardTable = rewards.Value,
            RewardLootValue = lootValue.Value,
            LogInterventions = logInterventions.Value,
            HudMode = hudMode.Value,
            DebugCommandsEnabled = debugCommands.Value,
        };
    }

    private string ReadAreasJson()
    {
        try
        {
            string path = Path.Combine(Paths.ConfigPath, PluginGuid, "areas.json");
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception exception)
        {
            Log.LogWarning($"areas.json could not be read and is ignored: {exception.Message}");
            return string.Empty;
        }
    }

    private IReadOnlyCollection<string> KnownSceneNames()
    {
        try
        {
            return Enum.GetNames(typeof(SceneID));
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Could not enumerate SceneID; areas.json keys are taken as written: {exception.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// FR-323 / 6章: only names that are real ItemIDs may be granted, and wand (equipment) items
    /// are refused. Relics are not an ItemID, so the enum check already excludes them.
    /// </summary>
    private RewardTable ValidateRewardItems(RewardTable table)
    {
        var rejected = new List<string>();
        foreach (RewardEntry entry in table.Entries)
        {
            if (!Enum.TryParse(entry.ItemName, out ItemID id) || id == ItemID.None)
            {
                Log.LogError($"RewardTable item '{entry.ItemName}' is not an ItemID of this build and is ignored.");
                rejected.Add(entry.ItemName);
            }
            else if (entry.ItemName.Contains("Wand", StringComparison.OrdinalIgnoreCase))
            {
                Log.LogError($"RewardTable item '{entry.ItemName}' is equipment and is ignored (SPEC004 6章).");
                rejected.Add(entry.ItemName);
            }
        }

        return rejected.Count == 0 ? table : table.Without(rejected);
    }

    /// <summary>
    /// SPEC004 10.3: a build mismatch warns and keeps going; each mechanism already degrades on
    /// its own when a symbol no longer resolves (9章).
    /// </summary>
    private void VerifyGameBuild()
    {
        try
        {
            string gameAssembly = BuildFingerprint.Sha256(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            string metadata = BuildFingerprint.Sha256(Path.Combine(
                Paths.GameRootPath, "SiNiSistar2_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));

            if (!string.Equals(gameAssembly, BuildFingerprint.ExpectedGameAssemblySha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(metadata, BuildFingerprint.ExpectedMetadataSha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.LogWarning(
                    "This game build is not the one SPEC004 was measured against; field semantics "
                    + "may differ. Mechanisms whose symbols no longer resolve will disable themselves.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogWarning($"Could not fingerprint the game build: {exception.Message}");
        }
    }

    /// <summary>
    /// The mimic lottery interception point (SPEC004 5.5-4, 付録A A-9). Failure to patch only
    /// disables the pseudo boxes: no box is ever registered, so no behaviour changes.
    /// </summary>
    private void ApplyMimicPatch()
    {
        MethodInfo? target = AccessTools.Method(typeof(OnlyHoldEnemy), nameof(OnlyHoldEnemy.HoldSetup));
        if (target is null)
        {
            Log.LogError("OnlyHoldEnemy.HoldSetup was not found; mimic pseudo boxes are disabled for this build.");
            SpawnRuntime.Profile = SpawnRuntime.Profile with { MimicBoxEnabled = false };
            return;
        }

        try
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(MimicBoxPatches).GetMethod(
                    nameof(MimicBoxPatches.HoldSetupPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)));
        }
        catch (Exception exception)
        {
            Log.LogError($"Mimic hold interception could not be patched; pseudo boxes are disabled: {exception.Message}");
            SpawnRuntime.Profile = SpawnRuntime.Profile with { MimicBoxEnabled = false };
        }
    }
}
