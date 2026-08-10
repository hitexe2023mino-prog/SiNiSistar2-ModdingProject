using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SiNiSistar2.Damage;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;
using SiNiSistar2.UI;
using SiNiSistar2.UI.Gallery;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Replaces the HP-based defeat inside a hold with pleasure, climaxes and corruption.
/// Implements SPEC003.
///
/// Version 1.1 completes the separation v1.0 began. A sexual attack no longer touches HP at all,
/// so HP is the instrument for non-sexual threats and nothing else, and reaching the climax limit
/// ends the run then and there rather than leaving it to the enemy's next swing. Corruption still
/// rises one way and is worn as the lust crest, and the escalated swelling is still endured through
/// the milk gauge.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PleasurePlugin : BasePlugin
{
    public const string PluginGuid = "community.sinisistar2.pleasure";
    public const string PluginName = "SiNiSistar2 Pleasure";
    public const string PluginVersion = "1.2.1";

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
            profile.Pleasure.DecayPerSecond,
            profile.Pleasure.CrestScale);
        PleasureRuntime.Corruption = new CorruptionTrack(profile.Corruption.Cap);
        PleasureRuntime.Regen = new RegenBuffTrack(profile.Regen);
        // Built even when the penalty is switched off. It rolls for nothing in that state — the
        // update path returns before advancing it — but the F4 panel needs something to report,
        // and "the scheduler is absent" is not an answer anyone can act on.
        PleasureRuntime.Stun = new MpZeroStunScheduler(profile.MpPenalty);
        PleasureRuntime.Milk = new MilkReservoir(
            profile.BreastSuper.MilkPerSexualHit,
            profile.BreastSuper.MilkDrainPerSecond);
        PleasureRuntime.Breasts = new BreastEscalation(
            profile.BreastSuper.ApplicationsAtMaxLevel,
            profile.BreastSuper.CorruptionThreshold);
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
        // One seam for two jobs: holding the HP subtraction off for a sexual hit (5.1) and reading
        // what the hit applied (5.2). The prefix opens the hold, the postfix closes it and moves
        // the gauge, and the finalizer closes it again if the game's own code threw (FR-204).
        applied += Patch(
            "damage",
            AccessTools.Method(typeof(DamageManager), nameof(DamageManager.OneDamage)),
            typeof(DamageProbePatches),
            postfix: nameof(DamageProbePatches.OneDamagePostfix),
            prefix: nameof(DamageProbePatches.OneDamagePrefix),
            finalizer: nameof(DamageProbePatches.OneDamageFinalizer));
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
        // InventoryHandler.PlayItemEvent is deliberately NOT patched: it returns UniTask, a
        // struct, and detouring a struct-returning IL2CPP method corrupts the returned task and
        // hangs or skips the item's own event. RemoveItem below is the void-returning witness.
        applied += Patch(
            "item-consumed",
            FindMethod(typeof(InventoryHandler), nameof(InventoryHandler.RemoveItem), typeof(ItemID)),
            typeof(ItemUsePatches),
            postfix: nameof(ItemUsePatches.RemoveItemPostfix));
        // SavePointAsyncLabel.ExecutionOneAsync also returns UniTask and must not be detoured —
        // doing so made the save dialog close by itself, taking saving and levelling with it.
        // SetObeliskMode is void, runs as the menu opens, and carries the obelisk flag.
        applied += Patch(
            "save-point",
            AccessTools.Method(typeof(SavePointMenu), nameof(SavePointMenu.SetObeliskMode)),
            typeof(SavePointPatches),
            postfix: nameof(SavePointPatches.SetObeliskModePostfix));

        _observer = AddComponent<PleasureObserver>();

        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded; "
            + $"suppressSexualHpDamage={(profile.BlocksSexualHpDamage ? "on" : "off")}, "
            + $"gauge={(profile.Pleasure.HasEffect ? "on" : "off")}, "
            + $"corruption={(profile.Corruption.HasEffect ? "on" : "off")}, "
            + $"climaxGameOver={profile.Climax.GameOverEnabled}, "
            + $"breastSuper={(profile.BreastSuper.HasEffect ? "on" : "off")}, "
            + $"probe={profile.ProbeMeasurements}, patches={applied}."
            + (profile.BreastSuper.HasEffect
                ? $" BreastSuper after {profile.BreastSuper.ApplicationsAtMaxLevel} further Breast "
                  + "applications."
                : string.Empty));

        LogCorruptionBonus(profile);

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
                + "predator, then read the [probe] lines to settle SPEC003 付録A A-2, A-3, A-6, A-9, "
                + "A-50 (whether HP can be kept off a sexual hit) and A-51 (whether the climax "
                + "limit can take HP to 0).");
        }
    }

    /// <summary>
    /// Reports what SPEC005 adds, and what it is not doing yet and why (FR-418).
    ///
    /// Written out rather than left to be inferred from the config file. Three of these four
    /// mechanisms ship inert, and a mechanism that is silently doing nothing is indistinguishable
    /// from one that is broken — which is the failure this log exists to prevent.
    /// </summary>
    private void LogCorruptionBonus(PleasureProfile profile)
    {
        CorruptionTuning corruption = profile.Corruption;
        int stocks = Math.Max(1, PleasureRuntime.CrestMaxLevel);
        Log.LogInfo(
            "SPEC005 堕落バフ: "
            + $"regen={(profile.Regen.HasEffect ? $"{profile.Regen.DurationPerClimax:0.#}s per climax, "
                + $"{profile.Regen.HpPerSecond:0.##} HP/s and {profile.Regen.MpPerSecond:0.##} MP/s" : "off")}; "
            + $"crest pleasure x{profile.Pleasure.CrestScale:0.##} once sublimated; "
            + $"corruption staging curse +{corruption.CurseGainMax:0.##} at the last curable stock "
            + $"-> x{corruption.ScaleFor(0, stocks, true):0.##} at sublimation; "
            + $"mp0 penalty={(profile.MpPenalty.HasEffect ? $"{profile.MpPenalty.Chance:P0} per press of "
                + $"{string.Join("/", profile.MpPenalty.TriggerInputs)}" : "off")}; "
            + $"crest haze={(profile.CrestFx.HasEffect ? "on" : "off")}.");

        if (!profile.Regen.HasEffect)
        {
            Log.LogInfo(
                "The succubus buff is inert: set Regen.RegenDurationPerClimax and at least one of "
                + "Regen.HpRegenPerSecond / Regen.MpRegenPerSecond. Until 付録A A-405 it ships this "
                + "way deliberately (FR-415).");
        }

        if (corruption.CurseGainMax <= 0f)
        {
            Log.LogInfo(
                "The curse stages do not accelerate corruption yet (Corruption.CorruptionCurseGainMax "
                + "is 0), so only sublimation changes the rate. 付録A A-406 asks how much "
                + "acceleration still leaves a player able to cure the curse and get out.");
        }

        if (!profile.MpPenalty.HasEffect)
        {
            Log.LogInfo(
                "The MP0 penalty is OFF, so it cannot fire and nothing about it can be observed. "
                + "To try it: set MpPenalty.Enabled=true AND MpPenalty.StunChance above 0 (1.0 "
                + "makes every qualifying press fire) in "
                + "BepInEx/config/community.sinisistar2.pleasure.cfg, then restart.");
        }

        Log.LogInfo(
            "Press F4 in game for the SPEC005 panel (top right): it shows the regen buff, the "
            + "crest stage and its coefficients, and — for the MP0 penalty — every one of its "
            + "conditions, which keys are being read right now, and why the last press did or did "
            + "not fire. F2 forces one stagger; it skips the press edge, the roll and the "
            + "cooldown, and never the conditions.");
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
        PleasureRuntime.Profile = PleasureProfile.Inactive;
        return true;
    }

    private PleasureOptions BindOptions()
    {
        ConfigEntry<bool> enabled = Config.Bind("General", "Enabled", true, "");
        ConfigEntry<bool> suppress = Config.Bind(
            "Survival",
            "SuppressSexualHpDamage",
            true,
            "Stop a sexual hit taken while bound from costing HP, so what a sexual hold costs is "
            + "pleasure rather than health. The hit still lands: its damage display, its effects "
            + "and the statuses it applies are untouched, and only the subtraction is held off. "
            + "Non-sexual attacks, slip damage and everything outside a hold are left alone and can "
            + "still reach 0. Does nothing until Pleasure.PleasureGainPerHit is above 0.");
        ReportRetiredSetting("SuppressHp0WhileBound", nameof(PleasureOptions.SuppressSexualHpDamage));

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
            "What one unit of corruption becomes once the lust mark is permanent. The crest's own "
            + "flavour is that the body has been made sensitive, so it is applied to the rate. This "
            + "is also what makes an enemy putting the crest on an uncorrupted player serious: "
            + "nothing has been lost yet, but everything from here costs more. Never below 1, and "
            + "it must stay above 1+CorruptionCurseGainMax so that sublimating costs something "
            + "(SPEC005 5.5).");
        ConfigEntry<float> curseGainMax = Config.Bind(
            "Corruption",
            "CorruptionCurseGainMax",
            0f,
            "What the LAST curable curse stock adds to the rate corruption accumulates at; the "
            + "stocks below it get a proportional share. The curse is the stage that can still be "
            + "lifted and the mark is the one that cannot, so the curse accelerates gently and "
            + "sublimation jumps to CorruptionCrestGainScale outright — the discontinuity is what "
            + "makes the point of no return cost something. 0 leaves the curse stages exactly as "
            + "they were, which is the shipped state until 付録A A-406. Keep it small: more "
            + "corruption means more stocks and more stocks mean more corruption, and this is that "
            + "loop's gain.");
        ConfigEntry<float> crestPleasure = Config.Bind(
            "Crest",
            "CrestPleasureGainScale",
            1.25f,
            "What pleasure gain is multiplied by once the lust mark is permanent. Fixed rather than "
            + "staged: how far the body has gone is already carried by CorruptionGainScale, and a "
            + "second term that also grew with the stage would be the same idea kept in two places. "
            + "The curse stages do not take it. This is the one value here that is not waiting on a "
            + "measurement, so it applies from the moment the MOD is added; set it to 1 to turn it "
            + "off. Never below 1.");

        ConfigEntry<bool> regenEnabled = Config.Bind(
            "Regen",
            "Enabled",
            true,
            "Whether a sublimated body earns recovery by climaxing (SPEC005 5.1).");
        ConfigEntry<float> regenPerClimax = Config.Bind(
            "Regen",
            "RegenDurationPerClimax",
            0f,
            "Seconds of slow HP/MP recovery one climax grants, once the lust mark is permanent. It "
            + "is paid for with a climax, which spends one of the run's remaining ones and moves "
            + "the player towards the limit that ends it. 0 never grants it and is the shipped "
            + "state.");
        ConfigEntry<float> regenCap = Config.Bind(
            "Regen",
            "RegenDurationCap",
            0f,
            "A ceiling on the banked duration, in seconds. 0 means no ceiling: climaxing again adds "
            + "to what is left rather than merely refreshing it. It cannot run away — clearing the "
            + "climax count needs a save point, and a save point also discards this.");
        ConfigEntry<float> hpRegen = Config.Bind(
            "Regen", "HpRegenPerSecond", 0f, "HP restored per second while the buff runs.");
        ConfigEntry<float> mpRegen = Config.Bind(
            "Regen",
            "MpRegenPerSecond",
            0f,
            "MP restored per second while the buff runs. This is the way out of the MP0 penalty, "
            + "and the reason accepting the enemy is worth doing when the bar is empty.");

        ConfigEntry<bool> mpPenaltyEnabled = Config.Bind(
            "MpPenalty",
            "Enabled",
            false,
            "Whether acting on an empty MP bar can stagger a sufficiently corrupted, marked body "
            + "(SPEC005 5.3). The stagger is the game's own empty-cast: the MOD presses the "
            + "game's sword magic action, and with no MP to spend the game plays "
            + "AnimState.Magic_Sword1_Empty and locks the action itself. It never fires while "
            + "bound: the arrow keys are the resistance input there.");
        ConfigEntry<float> mpPenaltyFraction = Config.Bind(
            "MpPenalty",
            "MpPenaltyCorruptionFraction",
            1f,
            "The share of CorruptionCap the corruption must reach before the penalty applies. 1 "
            + "means the whole track: the body has to be as far gone as it can get. Held together "
            + "with wearing the crest by an AND — an enemy can put the crest on a barely corrupted "
            + "player, and punishing them for a state they were handed rather than earned is not "
            + "what this is for.");
        ConfigEntry<float> stunChance = Config.Bind(
            "MpPenalty",
            "StunChance",
            0.2f,
            "Chance, per press of a trigger input, that the press staggers. 0 never staggers. "
            + "Roughly one press in five: acting still works, but acting in front of something is "
            + "a gamble. StunCooldownSeconds bounds it from the other side, so staggers never "
            + "chain whatever this is set to.");
        ConfigEntry<float> stunCooldown = Config.Bind(
            "MpPenalty",
            "StunCooldownSeconds",
            3f,
            "Seconds after a stagger during which no further roll is made, so a run of presses "
            + "cannot chain into a lock the player has no way to act out of.");
        ConfigEntry<string> stunInputs = Config.Bind(
            "MpPenalty",
            "StunTriggerInputs",
            string.Join(",", StunInputs.Defaults),
            "Which inputs roll. Known names: " + string.Join(", ", StunInputs.Known) + ". Magic is "
            + "deliberately absent: the game already staggers every time magic is cast with no MP, "
            + "so rolling for it either changes nothing or adds a second stagger to the game's own.");

        ConfigEntry<bool> crestFxEnabled = Config.Bind(
            "CrestFx",
            "Enabled",
            true,
            "Whether a pink haze marks each curse stock arriving and the sublimation (SPEC005 5.4).");
        ConfigEntry<float> crestFxSeconds = Config.Bind(
            "CrestFx", "CrestFxDurationSeconds", 1.2f, "How long that haze lasts.");
        ConfigEntry<float> crestFxIntensity = Config.Bind(
            "CrestFx",
            "CrestFxIntensityPerStage",
            0.2f,
            "How much strength each stage adds to the haze, so the last warning and the point of no "
            + "return do not read the same as the first.");

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
            SuppressSexualHpDamage = suppress.Value,
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
            CorruptionCurseGainMax = curseGainMax.Value,
            CrestPleasureGainScale = crestPleasure.Value,
            RegenEnabled = regenEnabled.Value,
            RegenDurationPerClimax = regenPerClimax.Value,
            RegenDurationCap = regenCap.Value,
            HpRegenPerSecond = hpRegen.Value,
            MpRegenPerSecond = mpRegen.Value,
            MpPenaltyEnabled = mpPenaltyEnabled.Value,
            MpPenaltyCorruptionFraction = mpPenaltyFraction.Value,
            StunChance = stunChance.Value,
            StunCooldownSeconds = stunCooldown.Value,
            StunTriggerInputs = stunInputs.Value,
            CrestFxEnabled = crestFxEnabled.Value,
            CrestFxDurationSeconds = crestFxSeconds.Value,
            CrestFxIntensityPerStage = crestFxIntensity.Value,
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

    private int Patch(
        string mechanism,
        MethodBase? target,
        Type owner,
        string postfix,
        string? prefix = null,
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
                prefix: Method(owner, prefix),
                postfix: Method(owner, postfix),
                finalizer: Method(owner, finalizer));
            return 1;
        }
        catch (Exception exception)
        {
            Log.LogError($"Mechanism '{mechanism}' is disabled: patching failed: {exception.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Says once that a setting from an earlier version is no longer read (SPEC003 6章, CHG-002).
    ///
    /// An upgraded config file keeps the old key, and a key that is still there but no longer
    /// consulted is worse than one that was deleted: it reads as a switch that has stopped working.
    /// The file on disk is read rather than asking BepInEx, because a key nobody binds is not part
    /// of the in-memory configuration at all — which is the whole reason it needs pointing out.
    /// </summary>
    private void ReportRetiredSetting(string retired, string successor)
    {
        try
        {
            if (!File.Exists(Config.ConfigFilePath)
                || !File.ReadAllLines(Config.ConfigFilePath).Any(line =>
                    line.TrimStart().StartsWith($"{retired} ", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith($"{retired}=", StringComparison.Ordinal)))
            {
                return;
            }

            Log.LogInfo(
                $"The setting '{retired}' is left over from an earlier version and is no longer "
                + $"read. Its successor is '{successor}', which stops a sexual hit costing HP at all "
                + "rather than clamping HP at 1. The old line can be deleted.");
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Could not check for retired settings: {exception.Message}");
        }
    }

    private static HarmonyMethod? Method(Type owner, string? name) =>
        name is null
            ? null
            : new HarmonyMethod(owner.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));

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
        if (catalog.DiscardedUnsetDeclaration is { } discarded)
        {
            Log.LogWarning(
                $"The enemy catalogue had a 'None' row set to {discarded}. 'None' means the game left "
                + "the enemy's id unset, so that row stood for every unidentified captor at once and "
                + "could not be carried anywhere in particular. It has been dropped. Enemies of that "
                + "kind now get a row of their own the first time they take hold; declare them again "
                + "with F10 (SPEC003 5.3.1).");
        }

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
    /// Every enemy identifier the build can be asked for ahead of time (SPEC003 FR-237).
    ///
    /// Both enumerations, because neither covers the whole cast: of the 108 names in each, only 66
    /// suffixes are shared. Listing only the gallery's names left the 42 enemies that have an
    /// <c>EnemyID</c> and no gallery entry unnameable until they had already held the player, which
    /// is the wrong way round for a screen whose purpose is deciding before that happens.
    ///
    /// <c>None</c> is excluded from both: it means "not set" and names no enemy (FR-281). An empty
    /// answer is not fatal — the catalogue then fills in as enemies are met, which is worse but
    /// still works.
    /// </summary>
    private IReadOnlyCollection<string> KnownEnemyIds()
    {
        var ids = new List<string>();
        Collect(typeof(GalleryEnemyID));
        Collect(typeof(EnemyID));
        return ids;

        void Collect(Type enumeration)
        {
            try
            {
                ids.AddRange(Enum.GetNames(enumeration).Where(EnemyIds.IsUsable));
            }
            catch (Exception exception)
            {
                Log.LogWarning(
                    $"Could not enumerate {enumeration.Name}: {exception.Message}. The enemy "
                    + "catalogue will list only enemies that have been met from that set.");
            }
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
