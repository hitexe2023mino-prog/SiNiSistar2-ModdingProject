using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SiNiSistar2.Damage;
using SiNiSistar2.Difficulty.Core;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;

namespace SiNiSistar2.Difficulty.Plugin;

/// <summary>
/// Adds a difficulty step above the game's own <c>Hard</c> by making status ailments land harder
/// and holds harder to get out of and recover from. Implements SPEC002.
///
/// Nothing here writes the game's files or its saved difficulty. Every intervention is a runtime
/// patch, a contribution on a multi-source value, or a transient override that is always put back,
/// which is what lets the MOD be removed with no trace and lets the EDI MOD's build fingerprint
/// keep matching (SPEC002 FR-102, FR-104, DEC-110).
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class DifficultyPlugin : BasePlugin
{
    public const string PluginGuid = "community.sinisistar2.difficulty";
    public const string PluginName = "SiNiSistar2 Difficulty";
    public const string PluginVersion = "0.1.0";

    private const string ExpectedGameAssemblySha256 =
        "B869493305BBE587598C8709E7FE271F00D79D37803C6A8241946D6A6297499D";

    private const string ExpectedMetadataSha256 =
        "A56278D0162B6C148312B56FBE208B54BA9AF2D3BAD609EBF9349B7AE7DDC84B";

    private Harmony? _harmony;
    private DifficultyObserver? _observer;

    public override void Load()
    {
        DifficultyRuntime.Log = Log;
        DifficultyOptions options = BindOptions();

        if (options.Tier == DifficultyTier.Off)
        {
            Log.LogInfo($"{PluginName} {PluginVersion}: Tier=Off, no patches applied.");
            return;
        }

        if (!VerifyGameBuild())
        {
            return;
        }

        ProfileValidation validation = DifficultyProfileFactory.Create(options, KnownAbnormalNames());
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

        DifficultyProfile profile = validation.Profile;
        DifficultyRuntime.Profile = profile;
        ReportInactiveMechanisms(profile);

        if (!profile.AnyMechanismActive)
        {
            Log.LogInfo(
                $"{PluginName} {PluginVersion}: no mechanism would change anything, so no patches "
                + "were applied. Set the tuning values once SPEC002 付録A has been measured.");
            return;
        }

        DifficultyRuntime.PleasureTypes = ResolveTypes(profile.Pleasure.Types);
        DifficultyRuntime.BurdenTypes = ResolveTypes(profile.Burden.Types);
        DifficultyRuntime.Nullification = new NullificationScheduler(
            profile.Pleasure,
            new SystemRandomSource());
        DifficultyRuntime.Recovery = new RecoveryPenaltyScheduler(profile.Burden);
        DifficultyRuntime.ContributionKey = new Il2CppSystem.Object();

        if (profile.Burden.HasEffect && profile.Burden.InvincibleScale < 1f)
        {
            Log.LogWarning(
                "Burden.RecoveryInvincibleScale is set below 1.0 but is not applied yet: where the "
                + "post-escape invincibility is held has not been measured (SPEC002 付録A A-14). "
                + "The recovery window still applies its movement penalty.");
        }

        _harmony = new Harmony(PluginGuid);
        var applied = 0;
        applied += ApplyHardModePatches(profile);
        applied += ApplyAbnormalPatches(profile);
        applied += ApplyNullificationPatch(profile);

        if (applied == 0)
        {
            Log.LogError(
                "No patch target could be resolved, so the MOD is inert for this game build.");
            return;
        }

        _observer = AddComponent<DifficultyObserver>();

        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; tier={profile.Tier}, "
            + $"reportHard={DifficultyRuntime.ReportHard}, "
            + $"abnormal={Describe(profile.Abnormal)}, pleasure={Describe(profile.Pleasure)}, "
            + $"burden={Describe(profile.Burden)}, patches={applied}.");
    }

    public override bool Unload()
    {
        _observer?.Shutdown();

        // Cleared before the ledger runs, so nothing re-asserts the value being restored.
        DifficultyRuntime.ReportHard = false;
        DifficultyRuntime.OverrideCheckValue = false;

        // The ledger is emptied before the patches come off: a release that needs a patched
        // accessor has to still have one (SPEC002 FR-124).
        foreach (InterventionFailure failure in DifficultyRuntime.Ledger.ReleaseAll())
        {
            Log.LogError($"Intervention '{failure.Key}' could not be undone: {failure.Reason}");
        }

        try
        {
            _harmony?.UnpatchSelf();
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Harmony patches were not fully removed: {exception.Message}");
        }

        DifficultyRuntime.Reset();
        DifficultyRuntime.Nullification = null;
        DifficultyRuntime.Recovery = null;
        DifficultyRuntime.ContributionKey = null;
        DifficultyRuntime.Profile = DifficultyProfile.Inactive;
        return true;
    }

    private DifficultyOptions BindOptions()
    {
        ConfigEntry<DifficultyTier> tier = Config.Bind(
            "General",
            "Tier",
            DifficultyTier.Nightmare,
            "MOD difficulty step. Off applies no patches at all.");
        ConfigEntry<bool> forceHard = Config.Bind(
            "General",
            "ForceHardData",
            true,
            "Report Hard to the game's difficulty checks so its Hard-only data and enemy placement "
            + "become active. The difficulty stored in the save is never written.");

        ConfigEntry<bool> abnormalEnabled = Config.Bind("Abnormal", "Enabled", true, "");
        ConfigEntry<float> rateMultiplier = Config.Bind(
            "Abnormal",
            "AbnormalRateMultiplier",
            1f,
            "Multiplier on the status-ailment application rate for damage the player receives. "
            + "1.0 changes nothing. Stays at 1.0 until SPEC002 付録A A-4 has been measured.");
        ConfigEntry<int> levelBonus = Config.Bind(
            "Abnormal",
            "LevelBonus",
            0,
            "Extra levels advanced right after a status lands on the player. 0 changes nothing.");

        ConfigEntry<bool> pleasureEnabled = Config.Bind("Pleasure", "Enabled", true, "");
        ConfigEntry<string> pleasureTypes = Config.Bind(
            "Pleasure",
            "PleasureAbnormalTypes",
            string.Join(",", AbnormalTypeDefaults.Pleasure),
            "Statuses that make resistance falter, comma separated. Defilement is refused here: "
            + "escalating escape difficulty with defilement is the game's own axis.");
        ConfigEntry<float> interval = Config.Bind(
            "Pleasure",
            "NullificationIntervalSeconds",
            0f,
            "Gap between nullification windows. 0 with a 0 duration changes nothing.");
        ConfigEntry<float> intervalJitter = Config.Bind("Pleasure", "NullificationIntervalJitter", 0.5f, "");
        ConfigEntry<float> duration = Config.Bind(
            "Pleasure",
            "NullificationDurationSeconds",
            0f,
            "How long resistance input is ignored. 0 disables the window entirely.");
        ConfigEntry<float> durationJitter = Config.Bind("Pleasure", "NullificationDurationJitter", 0.3f, "");
        ConfigEntry<float> pleasureScaling = Config.Bind(
            "Pleasure",
            "PleasureLevelScaling",
            0f,
            "Shortens the gap and lengthens the window as the summed pleasure level rises.");
        ConfigEntry<float> dutyWarn = Config.Bind(
            "Pleasure",
            "NullificationDutyWarnThreshold",
            0.6f,
            "Warn at startup when windows would be open more than this fraction of the time.");
        ConfigEntry<float> resistPenalty = Config.Bind(
            "Pleasure",
            "NullificationResistPenalty",
            1f,
            "How much of an attempted gauge rise is turned into a fall inside the window. 1.0 "
            + "loses exactly what the input would have gained; 0 only stops the rise.");
        ConfigEntry<bool> highlightGauge = Config.Bind(
            "Pleasure",
            "HighlightGauge",
            true,
            "Tint the struggle gauge during a window. Ignored while NullificationResistPenalty is "
            + "above 0: once resisting costs progress, the colour is the only cue to stop.");
        ConfigEntry<string> gaugeColor = Config.Bind(
            "Pleasure",
            "NullificationGaugeColor",
            HexColor.DefaultNullificationHex,
            "Gauge tint as RRGGBB or RRGGBBAA.");

        ConfigEntry<bool> burdenEnabled = Config.Bind("Burden", "Enabled", true, "");
        ConfigEntry<string> burdenTypes = Config.Bind(
            "Burden",
            "BurdenAbnormalTypes",
            string.Join(",", AbnormalTypeDefaults.Burden),
            "Statuses that make the body heavy, comma separated.");
        ConfigEntry<float> penalty = Config.Bind(
            "Burden",
            "RecoveryPenaltySeconds",
            0f,
            "How long the player stays slowed after escaping a hold. 0 disables it.");
        ConfigEntry<float> moveSlow = Config.Bind(
            "Burden",
            "RecoveryMoveSlowRate",
            0f,
            "Movement slow contributed during the recovery window.");
        ConfigEntry<float> invincibleScale = Config.Bind(
            "Burden",
            "RecoveryInvincibleScale",
            1f,
            "Reserved. Not applied until SPEC002 付録A A-14 has been measured.");
        ConfigEntry<float> burdenScaling = Config.Bind(
            "Burden",
            "BurdenLevelScaling",
            0f,
            "Lengthens the recovery window as the summed burden level rises.");

        ConfigEntry<bool> logInterventions = Config.Bind("Diagnostics", "LogInterventions", false, "");

        return new DifficultyOptions
        {
            Tier = tier.Value,
            ForceHardData = forceHard.Value,
            AbnormalEnabled = abnormalEnabled.Value,
            AbnormalRateMultiplier = rateMultiplier.Value,
            LevelBonus = levelBonus.Value,
            PleasureEnabled = pleasureEnabled.Value,
            PleasureAbnormalTypes = pleasureTypes.Value,
            NullificationIntervalSeconds = interval.Value,
            NullificationIntervalJitter = intervalJitter.Value,
            NullificationDurationSeconds = duration.Value,
            NullificationDurationJitter = durationJitter.Value,
            PleasureLevelScaling = pleasureScaling.Value,
            NullificationDutyWarnThreshold = dutyWarn.Value,
            NullificationResistPenalty = resistPenalty.Value,
            HighlightGauge = highlightGauge.Value,
            NullificationGaugeColor = gaugeColor.Value,
            BurdenEnabled = burdenEnabled.Value,
            BurdenAbnormalTypes = burdenTypes.Value,
            RecoveryPenaltySeconds = penalty.Value,
            RecoveryMoveSlowRate = moveSlow.Value,
            RecoveryInvincibleScale = invincibleScale.Value,
            BurdenLevelScaling = burdenScaling.Value,
            LogInterventions = logInterventions.Value,
        };
    }

    /// <summary>
    /// The MOD's patch targets are named for one build. Running them against a different one is
    /// how a patch quietly lands on the wrong method (SPEC002 9章).
    /// </summary>
    private bool VerifyGameBuild()
    {
        try
        {
            string gameAssembly = Sha256(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            string metadata = Sha256(Path.Combine(
                Paths.GameRootPath,
                "SiNiSistar2_Data",
                "il2cpp_data",
                "Metadata",
                "global-metadata.dat"));

            if (string.Equals(gameAssembly, ExpectedGameAssemblySha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(metadata, ExpectedMetadataSha256, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Log.LogError(
                "Disabled: this game build is not the one SPEC002 targets. "
                + $"GameAssembly.dll expected {ExpectedGameAssemblySha256} but found {gameAssembly}; "
                + $"global-metadata.dat expected {ExpectedMetadataSha256} but found {metadata}.");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError($"Disabled: could not fingerprint the game build: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Says which mechanisms are doing nothing and which key turns each one on.
    ///
    /// The shipped tuning values are all no-change (FR-128), so a fresh install reports
    /// "pleasure=off" and is easy to read as the MOD being broken rather than as the MOD waiting
    /// to be configured. Naming the key removes that ambiguity.
    /// </summary>
    private void ReportInactiveMechanisms(DifficultyProfile profile)
    {
        if (!profile.Abnormal.HasEffect)
        {
            Log.LogWarning(
                "Status ailments are unchanged: set Abnormal.AbnormalRateMultiplier above 1.0 "
                + "and/or Abnormal.LevelBonus above 0.");
        }

        if (!profile.Pleasure.HasEffect)
        {
            Log.LogWarning(
                "Resistance nullification is off, so no window opens and the gauge is never "
                + "tinted: set Pleasure.NullificationDurationSeconds above 0 (and normally "
                + "Pleasure.NullificationIntervalSeconds too).");
        }

        if (!profile.Burden.HasEffect)
        {
            Log.LogWarning(
                "Post-escape recovery is off: set Burden.RecoveryPenaltySeconds above 0 and "
                + "Burden.RecoveryMoveSlowRate to the slow you want.");
        }
    }

    private int ApplyHardModePatches(DifficultyProfile profile)
    {
        if (!profile.ForceHardData)
        {
            return 0;
        }

        int applied = Patch(
            "hard-report",
            AccessTools.PropertyGetter(typeof(PlayerStatusManager), nameof(PlayerStatusManager.IsHardMode)),
            postfix: nameof(HardModeReportPatches.IsHardModePostfix),
            owner: typeof(HardModeReportPatches));

        // s_GameDifficultyForCheck is a plain static field on this build, so its getter cannot be
        // patched: Il2CppInterop reports "is a field accessor, it can't be patched" and the patch
        // is accepted but inert. It is overridden as a value by DifficultyObserver instead, which
        // is the transient-override form SPEC002 4.4 already allows. It is the check-side mirror,
        // not the save-backed GameDifficultyRP, so FR-104 still holds.
        DifficultyRuntime.ReportHard = true;
        DifficultyRuntime.OverrideCheckValue = true;

        if (applied == 0)
        {
            Log.LogWarning(
                "IsHardMode could not be patched, so Hard reporting rests entirely on the "
                + "s_GameDifficultyForCheck override. Check the self-check line below to see what "
                + "the game actually reports.");
        }

        return applied;
    }

    private int ApplyAbnormalPatches(DifficultyProfile profile)
    {
        if (!profile.Abnormal.HasEffect)
        {
            return 0;
        }

        var applied = 0;

        if (Math.Abs(profile.Abnormal.RateMultiplier - 1f) > 1e-6f)
        {
            applied += Patch(
                "abnormal-rate",
                AccessTools.Method(typeof(DamageManager), nameof(DamageManager.OneDamage)),
                prefix: nameof(AbnormalRatePatches.OneDamagePrefix),
                finalizer: nameof(AbnormalRatePatches.OneDamageFinalizer),
                owner: typeof(AbnormalRatePatches));
        }

        if (profile.Abnormal.LevelBonus > 0)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(AbnormalList),
                nameof(AbnormalList.AddAbnormal),
                new[] { typeof(AbnormalType), typeof(int), typeof(DamageStack) });
            applied += Patch(
                "abnormal-level",
                target,
                postfix: nameof(AbnormalLevelPatches.AddAbnormalPostfix),
                owner: typeof(AbnormalLevelPatches));
        }

        return applied;
    }

    private int ApplyNullificationPatch(DifficultyProfile profile) =>
        !profile.Pleasure.HasEffect
            ? 0
            : Patch(
                "nullification",
                AccessTools.Method(typeof(GachaGachaSystem), nameof(GachaGachaSystem.Execution)),
                prefix: nameof(NullificationPatches.ExecutionPrefix),
                owner: typeof(NullificationPatches));

    /// <summary>
    /// Applies one patch. A target that cannot be found, or a patch that will not apply, disables
    /// only its own mechanism and says which signature was missing (SPEC002 9章).
    /// </summary>
    private int Patch(
        string mechanism,
        MethodBase? target,
        Type owner,
        string? prefix = null,
        string? postfix = null,
        string? finalizer = null)
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
                prefix is null ? null : new HarmonyMethod(owner.GetMethod(prefix, PatchBinding)),
                postfix is null ? null : new HarmonyMethod(owner.GetMethod(postfix, PatchBinding)),
                finalizer: finalizer is null
                    ? null
                    : new HarmonyMethod(owner.GetMethod(finalizer, PatchBinding)));
            return 1;
        }
        catch (Exception exception)
        {
            Log.LogError(
                $"Mechanism '{mechanism}' is disabled: patching {target.DeclaringType?.Name}."
                + $"{target.Name} failed: {exception.Message}");
            return 0;
        }
    }

    private static BindingFlags PatchBinding =>
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// The game's own status names, used to tell a typo from a status the MOD does not know about.
    /// An empty result makes the validator accept configured names as written (SPEC002 FR-126).
    /// </summary>
    private IReadOnlyCollection<string> KnownAbnormalNames()
    {
        try
        {
            return Enum.GetNames(typeof(AbnormalType));
        }
        catch (Exception exception)
        {
            Log.LogWarning(
                $"Could not enumerate AbnormalType, so configured status names are taken as "
                + $"written: {exception.Message}");
            return Array.Empty<string>();
        }
    }

    private AbnormalType[] ResolveTypes(AbnormalTypeSet set)
    {
        var resolved = new List<AbnormalType>(set.Count);
        foreach (string name in set.Names)
        {
            if (Enum.TryParse(name, out AbnormalType value) && value != AbnormalType.None)
            {
                resolved.Add(value);
            }
            else
            {
                Log.LogWarning($"Status '{name}' has no AbnormalType in this build and is ignored.");
            }
        }

        return resolved.ToArray();
    }

    private static string Describe(AbnormalTuning tuning) =>
        !tuning.HasEffect ? "off" : $"rate x{tuning.RateMultiplier}, +{tuning.LevelBonus}lv";

    private static string Describe(PleasureTuning tuning) =>
        !tuning.HasEffect
            ? "off"
            : $"{tuning.DurationSeconds}s every {tuning.IntervalSeconds}s over {tuning.Types.Count} statuses "
              + $"(duty {tuning.ExpectedDutyCycle:P0})";

    private static string Describe(BurdenTuning tuning) =>
        !tuning.HasEffect
            ? "off"
            : $"{tuning.PenaltySeconds}s slow {tuning.MoveSlowRate} over {tuning.Types.Count} statuses";

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream));
    }
}
