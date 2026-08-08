using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SiNiSistar2.Damage;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;
using SiNiSistar2.UI.Gallery;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Replaces the HP-based defeat inside a hold with pleasure, climaxes and corruption.
/// Implements SPEC003.
///
/// Version 1.0. The mechanisms are settled: pleasure replaces the HP0 defeat inside a hold,
/// climaxes accumulate towards a limit, corruption rises one way and is worn as the lust crest, and
/// the escalated swelling is endured through the milk gauge. Probe mode stays available, but it is
/// no longer what the build is for.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PleasurePlugin : BasePlugin
{
    public const string PluginGuid = "community.sinisistar2.pleasure";
    public const string PluginName = "SiNiSistar2 Pleasure";
    public const string PluginVersion = "1.0.0";

    private const string ExpectedGameAssemblySha256 =
        "B869493305BBE587598C8709E7FE271F00D79D37803C6A8241946D6A6297499D";

    private const string ExpectedMetadataSha256 =
        "A56278D0162B6C148312B56FBE208B54BA9AF2D3BAD609EBF9349B7AE7DDC84B";

    private ConfigEntry<float>? _gaugeX;
    private ConfigEntry<float>? _gaugeY;
    private ConfigEntry<float>? _gaugeSize;
    private ConfigEntry<float>? _crossX;
    private ConfigEntry<float>? _crossY;
    private ConfigEntry<float>? _crossSize;
    private ConfigEntry<float>? _milkX;
    private ConfigEntry<float>? _milkY;
    private ConfigEntry<float>? _milkSize;
    private ConfigEntry<float>? _crestX;
    private ConfigEntry<float>? _crestY;
    private ConfigEntry<float>? _crestSize;
    private string _gameBuildId = "unknown";
    private Harmony? _harmony;
    private PleasureObserver? _observer;

    public override void Load()
    {
        PleasureRuntime.Log = Log;

        if (StartupGuard.IsAnotherInstanceRunning())
        {
            Log.LogWarning(
                $"{StartupGuard.DuplicateInstanceMessage} This copy is "
                + $"{StartupGuard.DescribeLaunch()}.");
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

        EnemyAttackCatalog enemies = LoadEnemyCatalog(options);

        PleasureValidation validation = PleasureProfileFactory.Create(
            options, KnownAbnormalNames(), enemies);
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
            profile.Pleasure.CorruptionScale,
            profile.Pleasure.DecayPerSecond);
        PleasureRuntime.Corruption = new CorruptionTrack(profile.Corruption.Cap);
        PleasureRuntime.Milk = new MilkReservoir(
            profile.BreastSuper.MilkPerSexualHit,
            profile.BreastSuper.MilkDrainPerSecond);
        PleasureRuntime.Breasts = new BreastEscalation(
            profile.BreastSuper.ApplicationsAtMaxLevel,
            profile.BreastSuper.CorruptionThreshold);
        PleasureRuntime.ContributionKey = new Il2CppSystem.Object();
        PleasureRuntime.Sidecar = new SidecarStore(
            Path.Combine(Paths.BepInExRootPath, "data", PluginGuid),
            _gameBuildId);
        PleasureRuntime.Overlay = profile.Overlay;
        PleasureRuntime.SaveOverlay = layout =>
        {
            _gaugeX!.Value = layout.Gauge.CentreX;
            _gaugeY!.Value = layout.Gauge.BottomOffset;
            _gaugeSize!.Value = layout.Gauge.Size;
            _crossX!.Value = layout.Cross.CentreX;
            _crossY!.Value = layout.Cross.BottomOffset;
            _crossSize!.Value = layout.Cross.Size;
            _milkX!.Value = layout.Milk.CentreX;
            _milkY!.Value = layout.Milk.BottomOffset;
            _milkSize!.Value = layout.Milk.Size;
            _crestX!.Value = layout.Crest.CentreX;
            _crestY!.Value = layout.Crest.BottomOffset;
            _crestSize!.Value = layout.Crest.Size;
            Config.Save();
        };

        _harmony = new Harmony(PluginGuid);
        var applied = 0;
        applied += Patch(
            "damage-probe",
            AccessTools.Method(typeof(DamageManager), nameof(DamageManager.OneDamage)),
            typeof(DamageProbePatches),
            postfix: nameof(DamageProbePatches.OneDamagePostfix));
        // All three add paths. Which one an item, an enemy or an authored event reaches is not
        // visible in the interop metadata, and a status applied by an item must count the same as
        // one applied by a hold (FR-244). Duplicate reports are collapsed per frame.
        applied += Patch(
            "breast-add-by-type",
            AccessTools.Method(
                typeof(AbnormalList),
                nameof(AbnormalList.AddAbnormal),
                new[] { typeof(AbnormalType), typeof(int), typeof(DamageStack) }),
            typeof(BreastPatches),
            postfix: nameof(BreastPatches.AddByTypePostfix));
        applied += Patch(
            "breast-add-by-data",
            AccessTools.Method(
                typeof(AbnormalList),
                nameof(AbnormalList.AddAbnormal),
                new[] { typeof(AbnormalData), typeof(int), typeof(DamageStack) }),
            typeof(BreastPatches),
            postfix: nameof(BreastPatches.AddByDataPostfix));
        applied += Patch(
            "breast-add-or-remove",
            AccessTools.Method(
                typeof(AbnormalList),
                nameof(AbnormalList.AddOrRemoveAbnormal),
                new[] { typeof(AbnormalType), typeof(bool) }),
            typeof(BreastPatches),
            postfix: nameof(BreastPatches.AddOrRemovePostfix));
        applied += Patch(
            "breast-condition-label",
            AccessTools.Method(
                typeof(AbnormalConditionLabel),
                "ExecutionOne",
                new[] { typeof(AbnormalConditionParameter) }),
            typeof(AbnormalLabelPatches),
            postfix: nameof(AbnormalLabelPatches.ExecutionOnePostfix));
        applied += Patch(
            "breast-over-max",
            FindMethod(typeof(AbnormalData), nameof(AbnormalData.OnTryAddedOverMax)),
            typeof(OverMaxPatches),
            postfix: nameof(OverMaxPatches.OnTryAddedOverMaxPostfix));
        applied += Patch(
            "item-use",
            FindMethod(typeof(InventoryHandler), nameof(InventoryHandler.PlayItemEvent), typeof(ItemID)),
            typeof(ItemUsePatches),
            postfix: nameof(ItemUsePatches.PlayItemEventPostfix));
        applied += Patch(
            "item-consumed",
            FindMethod(typeof(InventoryHandler), nameof(InventoryHandler.RemoveItem), typeof(ItemID)),
            typeof(ItemUsePatches),
            postfix: nameof(ItemUsePatches.RemoveItemPostfix));
        applied += Patch(
            "save-point",
            AccessTools.Method(typeof(SavePointAsyncLabel), nameof(SavePointAsyncLabel.ExecutionOneAsync)),
            typeof(SavePointPatches),
            postfix: nameof(SavePointPatches.ExecutionOneAsyncPostfix));

        _observer = AddComponent<PleasureObserver>();

        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; suppressHp0={profile.SuppressHp0WhileBound}, "
            + $"gauge={(profile.Pleasure.HasEffect ? "on" : "off")}, "
            + $"corruption={(profile.Corruption.HasEffect ? "on" : "off")}, "
            + $"climaxGameOver={profile.Climax.GameOverEnabled}, "
            + $"breastSuper={(profile.BreastSuper.HasEffect ? "on" : "off")}, "
            + $"probe={profile.ProbeMeasurements}, patches={applied}."
            + (profile.BreastSuper.HasEffect
                ? $" BreastSuper after {profile.BreastSuper.ApplicationsAtMaxLevel} further Breast "
                  + "applications."
                : string.Empty));

        Log.LogInfo(
            "Press F7 to fire a climax and F8 to add one crest part's worth of corruption, for "
            + "checking the climax performance and the lust crest without playing to them. Both go "
            + "through the real paths, so what they exercise is the mechanism rather than a "
            + "shortcut around it.");
        Log.LogInfo(
            "Press F11 in game to apply Breast to the player, for checking the escalation. "
            + "BreastSuper has no key: it is endured. The milk gauge falls "
            + $"{profile.BreastSuper.MilkDrainPerSecond:P1} a second and BreastSuper subsides to "
            + "Breast when it empties, while sexual attacks put it back up.");

        if (profile.ShowOverlay)
        {
            Log.LogInfo(
                "Press F9 in game to place the overlay with the mouse: Tab cycles the gauge, the "
                + "cross, the milk gauge and the lust crest, drag moves it, the wheel resizes it, "
                + "Enter saves and Escape cancels. The crest is shown whole while it is being "
                + "placed, however little of it corruption has earned.");
        }

        Log.LogInfo(
            "Press F10 in game to say which enemies make sexual attacks while they hold you: the arrow "
            + "keys or the wheel move through the list, Space or a click cycles Auto/Sexual/NonSexual, "
            + "Enter saves and Escape cancels.");

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
        PleasureRuntime.SaveEnemies("unload");

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
            "Seed only. The enemy classification now lives in "
            + "config/community.sinisistar2.pleasure/enemy-attacks.json, which this list fills in "
            + "the first time that file is created. Edit it in game with F10.");
        ConfigEntry<string> nonSexualEnemies = Config.Bind(
            "Pleasure",
            "NonSexualEnemyIds",
            string.Empty,
            "Seed only, like SexualEnemyIds. Once the catalogue file exists it is the authority.");
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

        // Corruption is what used to be called sensitivity. Same axis, same one-way rule, same
        // effect on pleasure gain — a name that says what it is rather than what it does. A sidecar
        // written under the old name is still read (SPEC003 5.7, FR-265).
        ConfigEntry<float> perClimax = Config.Bind(
            "Corruption",
            "CorruptionPerClimax",
            0f,
            "Corruption gained from one climax. It never falls: nothing cures it, and the cap is a "
            + "ceiling on growth rather than a way down.");
        ConfigEntry<float> perHit = Config.Bind(
            "Corruption",
            "CorruptionPerSexualHit",
            0f,
            "Corruption gained from one sexual hit taken.");
        ConfigEntry<float> gainScale = Config.Bind(
            "Corruption", "CorruptionGainScale", 0f, "How much corruption multiplies pleasure gain.");
        ConfigEntry<float> cap = Config.Bind(
            "Corruption",
            "CorruptionCap",
            12f,
            "The most corruption that can be accumulated. Twelve rather than ten so the two things "
            + "it drives land on whole numbers: the drawn mark has six parts and the crest has four "
            + "stocks above the threshold. Raise it for a finer scale — every boundary stays "
            + "proportional.");
        ConfigEntry<float> crestAt = Config.Bind(
            "Corruption",
            "CorruptionCrestAtFraction",
            0.5f,
            "The fraction of the cap at which the game's own lust crest is put on the player. 0 "
            + "never puts it on. The HUD mark is the MOD's picture of the corruption; this is where "
            + "the picture stops being one.");
        ConfigEntry<float> crestScale = Config.Bind(
            "Corruption",
            "CorruptionCrestGainScale",
            2f,
            "What one unit of corruption becomes while the lust crest is worn. The crest's own "
            + "flavour is that the body has been made sensitive, so it is applied to the rate. This "
            + "is also what makes an enemy putting the crest on an uncorrupted player serious: "
            + "nothing has been lost yet, but everything from here costs more. Never below 1.");

        ConfigEntry<int> breastAfter = Config.Bind(
            "BreastSuper",
            "BreastSuperAfterApplications",
            0,
            "How many further Breast applications, arriving while Breast is already at its maximum "
            + "level, escalate to BreastSuper. 0 never escalates and is the shipped state. "
            + "Applications below the maximum are not counted: those raise the level, which is the "
            + "game's own escalation.");
        ConfigEntry<float> breastThreshold = Config.Bind(
            "BreastSuper",
            "BreastSuperCorruptionThreshold",
            0f,
            "Corruption required before the escalation may happen. 0 means no requirement.");
        ConfigEntry<bool> breastReplaces = Config.Bind(
            "BreastSuper",
            "BreastSuperReplacesBreast",
            true,
            "Remove Breast as BreastSuper is applied, so the two do not stack.");
        ConfigEntry<bool> breastCured = Config.Bind(
            "BreastSuper",
            "CuredWithBreast",
            true,
            "Remove BreastSuper when the game's own cure removes Breast. The cure is an authored "
            + "list of statuses and the escalated one is not in it, so without this the MOD would "
            + "have added a status the game cannot take away.");
        ConfigEntry<float> breastFade = Config.Bind(
            "BreastSuper",
            "TransitionFadeSeconds",
            0.8f,
            "Seconds of black over the transition. The body is rebuilt in place, so the player does "
            + "not move and the scene is not reloaded; the black only covers the swap.");
        ConfigEntry<float> milkFill = Config.Bind(
            "BreastSuper",
            "MilkPerSexualHit",
            0.12f,
            "Milk gained from one sexual hit taken while swollen, as a fraction of the gauge. It "
            + "does not fill with time: waiting is not what the escalation is a penalty for. A full "
            + "gauge escalates Breast to BreastSuper.");
        ConfigEntry<float> milkDrain = Config.Bind(
            "BreastSuper",
            "MilkDrainPerSecond",
            0.25f,
            "Milk removed per second while milking. Only BreastSuper can be milked, and it "
            + "subsides to Breast when the gauge empties. 0 switches milking off.");
        ConfigEntry<bool> breastBelowMax = Config.Bind(
            "BreastSuper",
            "CountBelowMaxLevel",
            false,
            "Count every Breast application rather than only those arriving at the maximum level. "
            + "A debugging aid: it makes the escalation reachable by using the item that applies "
            + "swelling a few times, without reaching the ceiling first.");
        ConfigEntry<bool> breastHaanja = Config.Bind(
            "BreastSuper",
            "MakeHaanjaCurable",
            false,
            "Mark BreastSuper as curable by Haanja for the session, so the game's existing cure "
            + "event covers it. Off until the SPEC003 付録A A-14 reading says whether the cure "
            + "actually completes. Undone on unload.");

        ConfigEntry<bool> logTransitions = Config.Bind("Diagnostics", "LogTransitions", false, "");
        ConfigEntry<bool> logStatuses = Config.Bind(
            "Diagnostics",
            "LogAllStatusChanges",
            false,
            "Record every status added to anyone, every time. The probe records each status name "
            + "only once, so a status the save restored at load is never reported again, which "
            + "makes an item that applies it look as though it did nothing. Verbose; for "
            + "diagnosing what an item actually does.");
        ConfigEntry<bool> debugKeys = Config.Bind(
            "Diagnostics",
            "EnableDebugKeys",
            false,
            "Enables F11, which applies Breast to the player through the game's own add path so the "
            + "BreastSuper escalation can be checked without hunting for the item.");
        ConfigEntry<bool> probe = Config.Bind(
            "Diagnostics",
            "ProbeMeasurements",
            true,
            "Record each SPEC003 付録A finding once. Leave on until the measurements are settled.");
        ConfigEntry<bool> showOverlay = Config.Bind(
            "Diagnostics",
            "ShowOverlay",
            true,
            "Draw the pleasure gauge, corruption and climax count on screen.");
        _gaugeX = Config.Bind(
            "Overlay",
            "GaugeCentreX",
            PleasureOverlayLayout.Default.Gauge.CentreX,
            "Gauge centre as a fraction of screen width. Easiest set in game with F9.");
        _gaugeY = Config.Bind(
            "Overlay",
            "GaugeBottomOffset",
            PleasureOverlayLayout.Default.Gauge.BottomOffset,
            "Gauge centre measured up from the bottom edge.");
        _gaugeSize = Config.Bind(
            "Overlay", "GaugeSize", PleasureOverlayLayout.Default.Gauge.Size, "Gauge radius.");
        _crossX = Config.Bind(
            "Overlay", "CrossCentreX", PleasureOverlayLayout.Default.Cross.CentreX, "Cross centre, fraction of width.");
        _crossY = Config.Bind(
            "Overlay", "CrossBottomOffset", PleasureOverlayLayout.Default.Cross.BottomOffset, "Cross centre from the bottom.");
        _crossSize = Config.Bind(
            "Overlay", "CrossSize", PleasureOverlayLayout.Default.Cross.Size, "Cross height.");
        _milkX = Config.Bind(
            "Overlay", "MilkCentreX", PleasureOverlayLayout.Default.Milk.CentreX, "Milk gauge centre, fraction of width.");
        _milkY = Config.Bind(
            "Overlay", "MilkBottomOffset", PleasureOverlayLayout.Default.Milk.BottomOffset, "Milk gauge centre from the bottom.");
        _milkSize = Config.Bind(
            "Overlay", "MilkSize", PleasureOverlayLayout.Default.Milk.Size, "Milk gauge radius.");
        _crestX = Config.Bind(
            "Overlay", "CrestCentreX", PleasureOverlayLayout.Default.Crest.CentreX,
            "Lust crest centre, fraction of width.");
        _crestY = Config.Bind(
            "Overlay", "CrestBottomOffset", PleasureOverlayLayout.Default.Crest.BottomOffset,
            "Lust crest centre from the bottom.");
        _crestSize = Config.Bind(
            "Overlay", "CrestSize", PleasureOverlayLayout.Default.Crest.Size, "Lust crest radius.");
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
            CorruptionPerClimax = perClimax.Value,
            CorruptionPerSexualHit = perHit.Value,
            CorruptionGainScale = gainScale.Value,
            CorruptionCap = cap.Value,
            CorruptionCrestAtFraction = crestAt.Value,
            CorruptionCrestGainScale = crestScale.Value,
            BreastSuperAfterApplications = breastAfter.Value,
            BreastSuperCorruptionThreshold = breastThreshold.Value,
            BreastSuperReplacesBreast = breastReplaces.Value,
            BreastSuperCuredWithBreast = breastCured.Value,
            BreastSuperFadeSeconds = breastFade.Value,
            MilkPerSexualHit = milkFill.Value,
            MilkDrainPerSecond = milkDrain.Value,
            BreastSuperMakeHaanjaCurable = breastHaanja.Value,
            BreastSuperCountBelowMaxLevel = breastBelowMax.Value,
            LogTransitions = logTransitions.Value,
            LogAllStatusChanges = logStatuses.Value,
            EnableDebugKeys = debugKeys.Value,
            ProbeMeasurements = probe.Value,
            ShowOverlay = showOverlay.Value,
            GaugeCentreX = _gaugeX.Value,
            GaugeBottomOffset = _gaugeY.Value,
            GaugeSize = _gaugeSize.Value,
            CrossCentreX = _crossX.Value,
            CrossBottomOffset = _crossY.Value,
            CrossSize = _crossSize.Value,
            MilkCentreX = _milkX.Value,
            MilkBottomOffset = _milkY.Value,
            MilkSize = _milkSize.Value,
            CrestCentreX = _crestX.Value,
            CrestBottomOffset = _crestY.Value,
            CrestSize = _crestSize.Value,
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
                _gameBuildId = $"{gameAssembly[..8].ToLowerInvariant()}-{metadata[..8].ToLowerInvariant()}";
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

    /// <summary>
    /// Finds a method by name and its leading parameter types, ignoring the rest of the signature.
    ///
    /// Matching the full signature is what lost the item probe: the interop assembly declares the
    /// trailing parameter as <c>Il2CppSystem.Threading.CancellationToken</c>, and asking for
    /// <c>System.Threading.CancellationToken</c> found nothing. The leading parameters are the ones
    /// that distinguish the overloads that matter here, and a name that resolves to several
    /// candidates is reported rather than guessed at.
    /// </summary>
    private MethodBase? FindMethod(Type declaringType, string name, params Type[] leading)
    {
        MethodInfo[] candidates = AccessTools.GetDeclaredMethods(declaringType)
            .Where(method => method.Name == name)
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < leading.Length)
                {
                    return false;
                }

                for (var index = 0; index < leading.Length; index++)
                {
                    if (parameters[index].ParameterType != leading[index])
                    {
                        return false;
                    }
                }

                return true;
            })
            .ToArray();

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        string available = string.Join(
            "; ",
            AccessTools.GetDeclaredMethods(declaringType)
                .Where(method => method.Name == name)
                .Select(method => $"({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName))})"));
        Log.LogError(
            $"{declaringType.Name}.{name} matched {candidates.Length} methods for leading parameters "
            + $"({string.Join(", ", leading.Select(t => t.Name))}). Declared overloads: "
            + $"{(available.Length == 0 ? "none" : available)}.");
        return null;
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

    /// <summary>
    /// Reads the enemy classification catalogue, creating it on a first run (SPEC003 FR-235).
    ///
    /// Every id the game defines is written into it, not only the ones met so far. The editor is
    /// only useful if the enemy about to be faced is already listed, and an enemy that has to be
    /// survived once before it can be classified is the wrong way round.
    /// </summary>
    private EnemyAttackCatalog LoadEnemyCatalog(PleasureOptions options)
    {
        var store = new EnemyAttackCatalogStore(
            Path.Combine(Paths.ConfigPath, PluginGuid));
        EnemyAttackCatalogLoad load = store.Load();

        if (load.Notice is not null)
        {
            Log.LogWarning($"The enemy catalogue {load.Notice}");
        }

        EnemyAttackCatalog catalog = load.Catalog;
        bool isNew = catalog.Count == 0 && !load.Locked;
        if (isNew)
        {
            catalog.SeedFrom(SplitList(options.SexualEnemyIds), SplitList(options.NonSexualEnemyIds));
        }

        int added = catalog.AddMissing(KnownEnemyIds());
        PleasureRuntime.EnemyStore = store;
        PleasureRuntime.Enemies = catalog;

        if (!load.Locked)
        {
            PleasureRuntime.SaveEnemies(isNew ? "created" : $"{added} new enemy id(s)");
        }

        Log.LogInfo(
            $"Enemy catalogue: {catalog.Summary()}, at '{store.FilePath}'. Press F10 in game to edit it.");
        return catalog;
    }

    /// <summary>
    /// Every <c>GalleryEnemyID</c> the build defines. An empty answer is not fatal: the catalogue
    /// then fills in as enemies are met, which is worse but still works.
    /// </summary>
    private IReadOnlyCollection<string> KnownEnemyIds()
    {
        try
        {
            return Enum.GetNames(typeof(GalleryEnemyID));
        }
        catch (Exception exception)
        {
            Log.LogWarning(
                $"Could not enumerate GalleryEnemyID: {exception.Message}. The enemy catalogue will "
                + "list only enemies that have been met.");
            return Array.Empty<string>();
        }
    }

    private static string[] SplitList(string? raw) =>
        (raw ?? string.Empty)
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

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
