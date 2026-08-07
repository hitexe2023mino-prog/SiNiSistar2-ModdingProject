using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SiNiSistar2.Damage;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Replaces the HP-based defeat inside a hold with pleasure, climaxes and sensitivity.
/// Implements SPEC003.
///
/// This build ships in its measuring state: the HP0 defeat is removed, and everything else records
/// what SPEC003 付録A still needs before its tuning values can be chosen (FR-233).
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PleasurePlugin : BasePlugin
{
    public const string PluginGuid = "community.sinisistar2.pleasure";
    public const string PluginName = "SiNiSistar2 Pleasure";
    public const string PluginVersion = "0.1.0";

    private const string ExpectedGameAssemblySha256 =
        "B869493305BBE587598C8709E7FE271F00D79D37803C6A8241946D6A6297499D";

    private const string ExpectedMetadataSha256 =
        "A56278D0162B6C148312B56FBE208B54BA9AF2D3BAD609EBF9349B7AE7DDC84B";

    private Harmony? _harmony;
    private PleasureObserver? _observer;

    public override void Load()
    {
        PleasureRuntime.Log = Log;

        if (StartupGuard.IsAnotherInstanceRunning())
        {
            Log.LogWarning(StartupGuard.DuplicateInstanceMessage);
        }

        PleasureOptions options = BindOptions();

        if (!options.Enabled)
        {
            Log.LogInfo($"{PluginName} {PluginVersion}: Enabled=false, no patches applied.");
            return;
        }

        if (!VerifyGameBuild())
        {
            return;
        }

        PleasureValidation validation = PleasureProfileFactory.Create(options, KnownAbnormalNames());
        foreach (string error in validation.Errors)
        {
            Log.LogError(error);
        }

        foreach (string warning in validation.Warnings)
        {
            Log.LogWarning(warning);
        }

        foreach (string notice in validation.Notices)
        {
            Log.LogInfo(notice);
        }

        PleasureProfile profile = validation.Profile;
        PleasureRuntime.Profile = profile;

        if (!profile.AnyMechanismActive)
        {
            Log.LogInfo($"{PluginName} {PluginVersion}: nothing would change; no patches applied.");
            return;
        }

        PleasureRuntime.Meter = new PleasureMeter(
            profile.Pleasure.GainPerHit,
            profile.Pleasure.SensitivityScale,
            profile.Pleasure.DecayPerSecond);
        PleasureRuntime.Sensitivity = new SensitivityTrack(profile.Sensitivity.Cap);
        PleasureRuntime.ContributionKey = new Il2CppSystem.Object();

        _harmony = new Harmony(PluginGuid);
        var applied = 0;
        applied += Patch(
            "damage-probe",
            AccessTools.Method(typeof(DamageManager), nameof(DamageManager.OneDamage)),
            typeof(DamageProbePatches),
            postfix: nameof(DamageProbePatches.OneDamagePostfix));
        applied += Patch(
            "save-point",
            AccessTools.Method(typeof(SavePointAsyncLabel), nameof(SavePointAsyncLabel.ExecutionOneAsync)),
            typeof(SavePointPatches),
            postfix: nameof(SavePointPatches.ExecutionOneAsyncPostfix));

        _observer = AddComponent<PleasureObserver>();

        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; suppressHp0={profile.SuppressHp0WhileBound}, "
            + $"gauge={(profile.Pleasure.HasEffect ? "on" : "off")}, "
            + $"sensitivity={(profile.Sensitivity.HasEffect ? "on" : "off")}, "
            + $"climaxGameOver={profile.Climax.GameOverEnabled}, "
            + $"breastSuper={(profile.BreastSuper.HasEffect ? "on" : "off")}, "
            + $"probe={profile.ProbeMeasurements}, patches={applied}.");

        if (profile.ProbeMeasurements)
        {
            Log.LogInfo(
                "Probe mode is on. Play through a hold with a sexual enemy and a hold with a "
                + "predator, then read the [probe] lines to settle SPEC003 付録A A-1, A-2, A-3, "
                + "A-6, A-7 and A-9.");
        }
    }

    public override bool Unload()
    {
        _observer?.Shutdown();

        foreach (string failure in PleasureRuntime.Ledger.ReleaseAll())
        {
            Log.LogError($"Contribution could not be undone: {failure}");
        }

        try
        {
            _harmony?.UnpatchSelf();
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Harmony patches were not fully removed: {exception.Message}");
        }

        PleasureRuntime.Reset();
        PleasureRuntime.ContributionKey = null;
        PleasureRuntime.Profile = PleasureProfile.Inactive;
        return true;
    }

    private PleasureOptions BindOptions()
    {
        ConfigEntry<bool> enabled = Config.Bind("General", "Enabled", true, "");
        ConfigEntry<bool> suppress = Config.Bind(
            "Survival",
            "SuppressHp0WhileBound",
            true,
            "Stop HP reaching 0 while bound, so a hold is no longer decided by HP. Damage, its "
            + "display and its effects are untouched; only the moment HP would hit 0 is removed.");

        ConfigEntry<float> gain = Config.Bind(
            "Pleasure",
            "PleasureGainPerHit",
            0f,
            "Pleasure added by one sexual hit. 0 changes nothing and is the shipped state until "
            + "SPEC003 付録A A-2 and A-3 have been measured.");
        ConfigEntry<float> decay = Config.Bind("Pleasure", "PleasureDecayPerSecond", 0f, "Decay while free.");
        ConfigEntry<string> sexualTypes = Config.Bind(
            "Pleasure",
            "SexualAbnormalTypes",
            string.Join(",", SexualAbnormalDefaults.Types),
            "Statuses that mark an attack as sexual, comma separated.");
        ConfigEntry<string> sexualEnemies = Config.Bind(
            "Pleasure",
            "SexualEnemyIds",
            string.Join(",", SexualAbnormalDefaults.SexualEnemyIds),
            "GalleryEnemyIDs whose every attack is sexual, whatever it inflicts.");
        ConfigEntry<string> nonSexualEnemies = Config.Bind(
            "Pleasure",
            "NonSexualEnemyIds",
            string.Empty,
            "GalleryEnemyIDs never treated as sexual. Takes priority over everything else.");
        ConfigEntry<string> sexualSenders = Config.Bind(
            "Pleasure",
            "SexualSenderNames",
            string.Join(",", SexualAbnormalDefaults.SenderNames),
            "Attacker names always treated as sexual, matched as case-insensitive substrings.");
        ConfigEntry<string> nonSexualSenders = Config.Bind(
            "Pleasure", "NonSexualSenderNames", string.Empty, "Attacker names never treated as sexual.");
        ConfigEntry<bool> duringDefeat = Config.Bind(
            "Pleasure",
            "RaiseDuringDefeatPerformance",
            true,
            "Keep raising pleasure during a defeat performance. Sexual attacks otherwise only "
            + "happen while bound, but some defeat performances go on delivering them.");

        ConfigEntry<float> overlay = Config.Bind("Climax", "ClimaxOverlaySeconds", 1.5f, "");
        ConfigEntry<int> limitBase = Config.Bind("Climax", "ClimaxLimitBase", 0, "Base climax limit.");
        ConfigEntry<float> limitPerDurability = Config.Bind(
            "Climax", "ClimaxLimitPerDurability", 0f, "Climax limit added per point of maximum durability.");
        ConfigEntry<bool> gameOver = Config.Bind(
            "Climax",
            "EnableClimaxGameOver",
            false,
            "When the climax limit is reached, stop suppressing HP0 so the hold becomes fatal "
            + "through the game's own defeat path.");
        ConfigEntry<bool> obeliskOnly = Config.Bind(
            "Climax",
            "ResetAtObeliskOnly",
            false,
            "Reset the climax count only at obelisks rather than at any save point.");

        ConfigEntry<float> perClimax = Config.Bind("Sensitivity", "SensitivityPerClimax", 0f, "");
        ConfigEntry<float> perHit = Config.Bind("Sensitivity", "SensitivityPerSexualHit", 0f, "");
        ConfigEntry<float> gainScale = Config.Bind(
            "Sensitivity", "SensitivityGainScale", 0f, "How much sensitivity multiplies pleasure gain.");
        ConfigEntry<float> cap = Config.Bind("Sensitivity", "SensitivityCap", 10f, "");

        ConfigEntry<float> breastChance = Config.Bind(
            "BreastSuper",
            "BreastSuperChance",
            0f,
            "Chance of applying BreastSuper when the conditions hold. It is authored for an event, "
            + "so confirm its in-game behaviour before raising this.");
        ConfigEntry<float> breastThreshold = Config.Bind(
            "BreastSuper", "BreastSuperSensitivityThreshold", 0f, "");

        ConfigEntry<bool> logTransitions = Config.Bind("Diagnostics", "LogTransitions", false, "");
        ConfigEntry<bool> probe = Config.Bind(
            "Diagnostics",
            "ProbeMeasurements",
            true,
            "Record each SPEC003 付録A finding once. Leave on until the measurements are settled.");
        ConfigEntry<bool> showOverlay = Config.Bind(
            "Diagnostics",
            "ShowOverlay",
            true,
            "Draw the pleasure gauge, sensitivity and climax count on screen.");
        ConfigEntry<float> centreX = Config.Bind(
            "Overlay",
            "OverlayCentreX",
            PleasureOverlayLayout.Default.CentreX,
            "Ring centre as a fraction of screen width. The default sits on the game's HP/MP dial.");
        ConfigEntry<float> centreY = Config.Bind(
            "Overlay", "OverlayCentreY", PleasureOverlayLayout.Default.CentreY, "Ring centre, fraction of screen height.");
        ConfigEntry<float> ringRadius = Config.Bind(
            "Overlay",
            "OverlayRadius",
            PleasureOverlayLayout.Default.Radius,
            "Ring radius as a fraction of screen height. Raise it to sit further outside the dial.");
        ConfigEntry<float> ringThickness = Config.Bind(
            "Overlay", "OverlayThickness", PleasureOverlayLayout.Default.Thickness, "Ring thickness.");
        ConfigEntry<bool> showCross = Config.Bind(
            "Overlay",
            "ShowCross",
            true,
            "Show the cross above the dial. It chips with every climax and snaps at the limit, "
            + "which is the game over.");

        return new PleasureOptions
        {
            Enabled = enabled.Value,
            SuppressHp0WhileBound = suppress.Value,
            PleasureGainPerHit = gain.Value,
            PleasureDecayPerSecond = decay.Value,
            SexualAbnormalTypes = sexualTypes.Value,
            SexualEnemyIds = sexualEnemies.Value,
            NonSexualEnemyIds = nonSexualEnemies.Value,
            SexualSenderNames = sexualSenders.Value,
            NonSexualSenderNames = nonSexualSenders.Value,
            RaiseDuringDefeatPerformance = duringDefeat.Value,
            ClimaxOverlaySeconds = overlay.Value,
            ClimaxLimitBase = limitBase.Value,
            ClimaxLimitPerDurability = limitPerDurability.Value,
            EnableClimaxGameOver = gameOver.Value,
            ResetAtObeliskOnly = obeliskOnly.Value,
            SensitivityPerClimax = perClimax.Value,
            SensitivityPerSexualHit = perHit.Value,
            SensitivityGainScale = gainScale.Value,
            SensitivityCap = cap.Value,
            BreastSuperChance = breastChance.Value,
            BreastSuperSensitivityThreshold = breastThreshold.Value,
            LogTransitions = logTransitions.Value,
            ProbeMeasurements = probe.Value,
            ShowOverlay = showOverlay.Value,
            OverlayCentreX = centreX.Value,
            OverlayCentreY = centreY.Value,
            OverlayRadius = ringRadius.Value,
            OverlayThickness = ringThickness.Value,
            ShowCross = showCross.Value,
        };
    }

    private bool VerifyGameBuild()
    {
        try
        {
            string gameAssembly = Sha256(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            string metadata = Sha256(Path.Combine(
                Paths.GameRootPath, "SiNiSistar2_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));

            if (string.Equals(gameAssembly, ExpectedGameAssemblySha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(metadata, ExpectedMetadataSha256, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Log.LogError(
                "Disabled: this game build is not the one SPEC003 targets. "
                + $"GameAssembly.dll found {gameAssembly}; global-metadata.dat found {metadata}.");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError($"Disabled: could not fingerprint the game build: {exception.Message}");
            return false;
        }
    }

    private int Patch(string mechanism, MethodBase? target, Type owner, string postfix)
    {
        if (target is null)
        {
            Log.LogError(
                $"Mechanism '{mechanism}' is disabled: its patch target was not found in this game "
                + "build. Every other mechanism keeps working.");
            return 0;
        }

        try
        {
            _harmony!.Patch(
                target,
                postfix: new HarmonyMethod(owner.GetMethod(
                    postfix,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)));
            return 1;
        }
        catch (Exception exception)
        {
            Log.LogError($"Mechanism '{mechanism}' is disabled: patching failed: {exception.Message}");
            return 0;
        }
    }

    private IReadOnlyCollection<string> KnownAbnormalNames()
    {
        try
        {
            return Enum.GetNames(typeof(AbnormalType));
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Could not enumerate AbnormalType: {exception.Message}");
            return Array.Empty<string>();
        }
    }

    private static string Sha256(string path) => StartupGuard.Sha256(path);
}
