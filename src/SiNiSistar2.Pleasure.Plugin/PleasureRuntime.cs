using BepInEx.Logging;
using SiNiSistar2.Damage;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// State the Harmony patches read, and the probe log that answers the SPEC003 付録A measurements.
///
/// The MOD ships changing nothing at all: every tuning value is at a no-change setting and the HP
/// suppression waits for the gauge to be able to rise (SPEC003 FR-233, FR-278). The probe is
/// therefore the most useful thing it can do on a first run.
/// </summary>
internal static class PleasureRuntime
{
    internal const string HaanjaCurableKey = "pleasure-breastsuper-haanja-curable";

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    internal static readonly InterventionLedger Ledger = new();

    internal static PleasureProfile Profile { get; set; } = PleasureProfile.Inactive;

    /// <summary>
    /// Where the gauge sits, live. Separate from the profile because the layout editor changes it
    /// while the game runs and the profile is meant to be the settled configuration.
    /// </summary>
    internal static PleasureOverlayLayout Overlay { get; set; } = PleasureOverlayLayout.Default;

    /// <summary>Writes the layout back to the config file. Supplied by the plugin at load.</summary>
    internal static Action<PleasureOverlayLayout>? SaveOverlay { get; set; }

    /// <summary>Carries corruption and the climax count alongside the game's save slots.</summary>
    internal static SidecarStore? Sidecar { get; set; }

    /// <summary>
    /// Which enemies make sexual attacks, as edited in game. The classifier holds this same object,
    /// so a change applies to the next hit rather than the next launch (FR-236).
    /// </summary>
    internal static EnemyAttackCatalog Enemies { get; set; } = new();

    internal static EnemyAttackCatalogStore? EnemyStore { get; set; }

    /// <summary>Which slot the sidecar is following, or null before one has been identified.</summary>
    internal static string? CurrentSlotKey { get; private set; }

    internal static ManualLogSource? Log { get; set; }

    internal static PleasureMeter? Meter { get; set; }

    internal static CorruptionTrack? Corruption { get; set; }

    /// <summary>Set when the corruption has earned the crest, consumed by the observer.</summary>
    internal static bool PendingLustCrest { get; set; }

    /// <summary>
    /// Whether the crest debt is worth recomputing (SPEC003 FR-274, DEC-254).
    ///
    /// Asking costs an interop call — the level has to be read from the game's own list — and the
    /// answer can only change when the corruption moves, when a status is added or removed, or when
    /// a slot is loaded. All three are events this MOD already sees, so the question is asked on
    /// those rather than on every frame. A slow sweep still runs behind it, because a path nobody
    /// noticed must not be able to strand the mark.
    /// </summary>
    internal static bool CrestDebtDirty { get; set; } = true;

    /// <summary>
    /// The crest's own level ceiling, read from the game once it is known.
    ///
    /// Read rather than assumed: it is three in this build, and a number the MOD wrote down would
    /// be a number that stops being true when the game changes it.
    /// </summary>
    internal static int CrestMaxLevel { get; set; } = 1;

    /// <summary>
    /// Whether the crest has ever been received in this run (SPEC003 FR-272).
    ///
    /// Sublimated means the curse reached its last level, which is where it stops being a curse
    /// that can be lifted and becomes the mark itself (FR-273). Below that it is curable and the
    /// MOD leaves a cure alone; at it, the observer puts the status back whenever it finds it
    /// missing, because the game's cures are written for statuses that were meant to be curable.
    ///
    /// Once true it stays true until the run ends.
    /// </summary>
    internal static bool CrestSublimated { get; set; }

    /// <summary>What level of the crest the player is carrying, or 0 for none.</summary>
    internal static int CrestLevel
    {
        get
        {
            try
            {
                return PlayerAbnormals?.GetAbnormalLevel(AbnormalType.LustMarkCurse) ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// The level the corruption has earned, from the point the mark first appears to the cap.
    ///
    /// The crest is a levelled status — three of them — and the corruption is a continuum, so the
    /// span above the threshold is divided into as many steps as the status has. The last step
    /// lands exactly at the cap, which is also where the drawn mark completes: the picture finishes
    /// and the thing it pictures finishes with it (FR-273).
    /// </summary>
    internal static int EarnedCrestLevel(int maxLevel)
    {
        CorruptionTrack? track = Corruption;
        CorruptionTuning tuning = Profile.Corruption;
        if (track is null || track.Cap <= 0f || maxLevel <= 0 || !tuning.MarksTheBody)
        {
            return 0;
        }

        float fraction = track.Value / track.Cap;
        if (fraction < tuning.CrestAtFraction)
        {
            return 0;
        }

        // Divided into the steps the status has, so the last one lands exactly at the cap: the
        // drawn mark completes and the thing it pictures sublimates at the same instant. Dividing
        // by the count rather than by the gaps between them put the last step short of the cap,
        // which left the mark finished and the curse still curable.
        float span = Math.Max(1e-4f, 1f - tuning.CrestAtFraction);
        float steps = Math.Max(1, maxLevel - 1);
        var level = 1 + (int)Math.Floor(((fraction - tuning.CrestAtFraction) / span * steps) + 1e-4f);
        return Math.Clamp(level, 1, maxLevel);
    }

    /// <summary>
    /// Whether the game's lust crest is on the player right now (SPEC003 FR-267).
    ///
    /// Asked wherever corruption is gained, so it has to be cheap and it has to be safe during a
    /// status callback. Reading the list is both; the answer is cached for the frame by the caller
    /// rather than here, because a status can be added and removed within one.
    /// </summary>
    internal static bool IsCrestWorn
    {
        get
        {
            try
            {
                return PlayerAbnormals?.Has(AbnormalType.LustMarkCurse) == true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// One gain of corruption, scaled by whether the crest is worn.
    ///
    /// Every gain goes through here rather than each caller multiplying for itself. The crest's
    /// effect is a property of the body, not of the particular thing that happened to it, and a
    /// caller that forgot would make the crest silently harmless on that path.
    /// </summary>
    internal static void GainCorruption(float amount)
    {
        CorruptionTrack? track = Corruption;
        if (track is null || amount <= 0f)
        {
            return;
        }

        track.Add(amount * Profile.Corruption.ScaleFor(IsCrestWorn));
        CrestDebtDirty = true;

        // Nothing about the crest is decided here any more. Whether the body owes a stock is a
        // question about the corruption standing now, not about the moment it last rose, and the
        // observer asks it every pass (FR-274). Deciding it in both places meant two answers that
        // could disagree, and the one here was the one that could not see a cure.
    }

    internal static ClimaxLedger Climaxes { get; } = new();

    /// <summary>Counts the <c>Breast</c> applications that lead to <c>BreastSuper</c> (SPEC003 5.8).</summary>
    internal static BreastEscalation? Breasts { get; set; }

    /// <summary>
    /// Set by the status patch when the escalation is due, consumed by the observer. Applying a
    /// status from inside the game's own add path would re-enter it; the main-thread update is the
    /// safe place, the same arrangement the climax uses.
    /// </summary>
    internal static bool PendingBreastSuper { get; set; }

    /// <summary>Set when the game's own cure removes <c>Breast</c>, consumed by the observer.</summary>
    internal static bool PendingBreastSuperCure { get; set; }

    /// <summary>Game time at which the transition's black stops being drawn.</summary>
    internal static double TransitionFadeUntil { get; set; }

    /// <summary>The milk the swelling makes and the body works off (SPEC003 FR-259, FR-264).</summary>
    internal static MilkReservoir? Milk { get; set; }

    /// <summary>The player's own status list, used to tell player-received from enemy-received.</summary>
    internal static AbnormalList? PlayerAbnormals { get; set; }

    /// <summary>
    /// True once the observer has seen a running game with a player in it. Statuses are re-added as
    /// a save is restored, which happens before this; counting those would advance the escalation
    /// just for loading a game that already had swelling.
    /// </summary>
    internal static bool GameplayStarted { get; set; }

    /// <summary>Whether <c>Breast</c> or <c>BreastSuper</c> is worn. Set by the observer each frame.</summary>
    internal static bool IsSwollen { get; set; }

    /// <summary>True while the player is held. Set by the observer each frame.</summary>
    internal static bool IsBound { get; set; }

    /// <summary>True while the player is in a defeat performance (HP0, not yet a game over).</summary>
    internal static bool IsDefeatPerformance { get; set; }

    /// <summary>
    /// Whether pleasure may rise right now. Sexual attacks only happen while bound, except for the
    /// defeat performances that go on delivering them (SPEC003 5.2).
    /// </summary>
    internal static bool CanAccumulate =>
        IsBound || (IsDefeatPerformance && Profile.RaiseDuringDefeatPerformance);

    /// <summary>Game time at which the climax flash stops being drawn.</summary>
    internal static double ClimaxFlashUntil { get; set; }

    /// <summary>The captor's enemy identifier (SPEC003 5.3.1), or null when it cannot be resolved.</summary>
    internal static string? BinderEnemyId { get; set; }

    /// <summary>What the game calls the captor, or null when it has no name to give.</summary>
    internal static string? BinderDisplayName { get; set; }

    /// <summary>
    /// Set by the damage patch when the gauge fills, consumed by the observer. The climax writes to
    /// the UI and, at the limit, to the player's HP; both belong on the main-thread update rather
    /// than inside damage resolution.
    /// </summary>
    internal static bool PendingClimax { get; set; }

    /// <summary>
    /// Whether this run has already been ended by the climax limit (SPEC003 FR-216).
    ///
    /// The defeat performance keeps the observer running, so without a latch a second pass would
    /// find the count still at the limit and ask for the death again. Cleared when a slot is loaded
    /// or a run begins, which is where the count itself goes back.
    /// </summary>
    internal static bool ClimaxDeathFired { get; set; }

    /// <summary>
    /// Records a probe finding once. Every measurement in 付録A is about whether something is
    /// observable at all, so the first sighting answers it and the rest is noise.
    /// </summary>
    internal static void Probe(string key, string message)
    {
        if (!Profile.ProbeMeasurements)
        {
            return;
        }

        lock (Reported)
        {
            if (!Reported.Add(key))
            {
                return;
            }
        }

        Log?.LogInfo($"[probe] {message}");
    }

    internal static void LogTransition(string message)
    {
        if (Profile.LogTransitions)
        {
            Log?.LogInfo(message);
        }
    }

    /// <summary>
    /// Whether this damage landed on the player. <see cref="DamageStack.IsReceiverLelia"/> is the
    /// game's own answer; an unusable stack is not evidence that the player was hit.
    /// </summary>
    internal static bool IsPlayerReceiving(DamageStack? stack)
    {
        if (stack is null)
        {
            return false;
        }

        try
        {
            return stack.IsReceiverLelia;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The statuses an attack would apply, used to classify it (SPEC003 5.3).</summary>
    internal static string[] AppliedStatuses(DamageStack stack)
    {
        try
        {
            DamageParameter? parameter = stack.m_DamageParameter;
            var types = parameter?.m_AbnormalTypes;
            if (types is null || types.Length == 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>(types.Length);
            for (var index = 0; index < types.Length; index++)
            {
                AbnormalType type = types[index];
                if (type != AbnormalType.None)
                {
                    names.Add(type.ToString());
                }
            }

            return names.ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Points the run at a save slot and restores whatever that slot holds.
    ///
    /// The climax count and corruption belong to a save, not to the process. Without this a retry
    /// after a game over came back still at the limit and the next hold killed the player at once.
    /// Restoring rather than clearing is what makes loading an earlier save mean what it says: the
    /// values go back to where that save left them (SPEC003 4.4, FR-222).
    /// </summary>
    /// <summary>
    /// Called when the game reports no loaded save at all, which is where a new game begins.
    ///
    /// This is the boundary between playthroughs, and the only place the accumulated values are
    /// cleared without a sidecar saying so. It has to be here rather than on an unknown key: an
    /// unknown key is a save that has just been created, and clearing there throws the run away
    /// (付録A A-44).
    /// </summary>
    internal static void EnterNoSlot()
    {
        if (CurrentSlotKey is null)
        {
            return;
        }

        CurrentSlotKey = null;
        Corruption?.LoadFrom(0f);
        Climaxes.ResetCount();
        Breasts?.Reset();
        Milk?.Reset();
        Meter?.Reset();
        PendingClimax = false;
        ClimaxDeathFired = false;
        PendingBreastSuper = false;
        PendingLustCrest = false;
        CrestSublimated = false;
        CrestDebtDirty = true;
        ClimaxFlashUntil = 0d;
        Log?.LogInfo(
            "No save is loaded, so this is a new run: corruption, climaxes, swelling, milk and the "
            + "lust crest all start from zero. A new game plus is a new run by this reading, which "
            + "is why the crest does not follow it across (FR-272). They attach to a slot when the "
            + "game is first saved.");
    }

    /// <summary>
    /// Points the run at a slot (SPEC003 FR-284).
    ///
    /// What happens to the accumulated values is decided by <see cref="SlotTransition"/> rather
    /// than here, so the rule has one home and a test. The three facts it needs are gathered here
    /// and nowhere else.
    /// </summary>
    /// <param name="authoritative">
    /// True when the slot is speaking rather than being written to — a load, or a return from a
    /// defeat. Such a change never carries the run in hand: a defeat sends the player back to the
    /// last save, and a save that recorded nothing means nothing was accumulated.
    /// </param>
    internal static void LoadSlot(string slotKey, string reason, bool authoritative = false)
    {
        CurrentSlotKey = slotKey;

        SidecarLoad load = Sidecar?.Load(slotKey) ?? new SidecarLoad(null, null, false);
        SidecarDocument? stored = load.Document;

        // A save written moments ago is the only innocent way to reach a key with no sidecar: the
        // player put the run in progress into a new file. Anything else that lands on an unknown
        // key is a load, and a load must not inherit (付録A A-55).
        bool justSaved = LastSaveAt > 0d
            && UnityEngine.Time.unscaledTimeAsDouble - LastSaveAt < 10d;
        SlotAction action = SlotTransition.Decide(stored is not null, authoritative, justSaved);

        switch (action)
        {
            case SlotAction.Restore when stored is not null:
                Corruption?.LoadFrom(stored.Corruption);
                Climaxes.LoadFrom(stored.ClimaxCount);
                Breasts?.LoadFrom(stored.BreastAtMaxCount);
                Milk?.LoadFrom(stored.Milk);
                CrestSublimated = stored.LustCrest;
                break;

            case SlotAction.Reset:
                Corruption?.LoadFrom(0f);
                Climaxes.ResetCount();
                Breasts?.Reset();
                Milk?.Reset();
                CrestSublimated = false;
                break;

            case SlotAction.Carry:
                break;
        }

        CrestDebtDirty = true;
        Meter?.Reset();
        PendingClimax = false;

        // The count that made the run lethal has just been replaced by whatever the slot holds, so
        // the latch that recorded it has to go with it. Leaving it set would make a reloaded save
        // unkillable by the limit for the rest of the session (FR-216).
        ClimaxDeathFired = false;
        PendingBreastSuper = false;
        PendingLustCrest = false;
        ClimaxFlashUntil = 0d;

        string state = action switch
        {
            SlotAction.Restore => "restored from its sidecar",
            SlotAction.Carry => "has no sidecar and was just saved into, so the run in progress carries in",
            _ => "has no sidecar, so everything starts from zero",
        };
        string notice = load.Notice is null ? string.Empty : $" The stored file {load.Notice}.";
        Log?.LogInfo(
            $"Slot '{slotKey}' ({reason}): {state} — climaxes {Climaxes.Count}, corruption "
            + $"{Corruption?.Value ?? 0f:F2}, breast applications {Breasts?.Count ?? 0}, milk "
            + $"{Milk?.Fill ?? 0f:P0}, crest {(CrestSublimated ? "sublimated" : "not sublimated")}."
            + notice);

        // Written straight back when it was carried, so the same key can never be ambiguous twice.
        // Without this, dying before the next save point would find no sidecar and start from zero.
        if (action == SlotAction.Carry)
        {
            SaveSlot("the run was carried into a new slot");
        }
    }

    /// <summary>
    /// Reloads the current slot. Used when the player comes back from a defeat, which returns them
    /// to the last save and should return these values with them.
    ///
    /// Authoritative: a defeat is the save speaking. Carrying the run in hand across a death is
    /// what let corruption survive a retry (付録A A-55).
    /// </summary>
    internal static void ReloadCurrentSlot(string reason)
    {
        if (CurrentSlotKey is null)
        {
            // No slot means nothing was ever saved, and a defeat still ends the attempt. Zeroing
            // is the honest reading: there is no record to go back to.
            Corruption?.LoadFrom(0f);
            Climaxes.ResetCount();
            Breasts?.Reset();
            Milk?.Reset();
            CrestSublimated = false;
            CrestDebtDirty = true;
            Meter?.Reset();
            ClimaxDeathFired = false;
            Log?.LogInfo(
                $"No slot is attached ({reason}), so there is nothing to reload; the run starts "
                + "from zero.");
            return;
        }

        LoadSlot(CurrentSlotKey, reason, authoritative: true);
    }

    /// <summary>
    /// Writes the run to the sidecar. A failure is reported and nothing else happens: losing a
    /// write must never interrupt play (SPEC003 FR-226).
    /// </summary>
    /// <summary>Unscaled time of the last successful write, used to recognise a fresh save.</summary>
    internal static double LastSaveAt { get; private set; }

    internal static void SaveSlot(string reason)
    {
        if (CurrentSlotKey is null || Sidecar is null)
        {
            return;
        }

        string? failure = Sidecar.Save(
            CurrentSlotKey,
            Corruption?.Value ?? 0f,
            Climaxes.Count,
            Breasts?.Count ?? 0,
            Milk?.Fill ?? 0f,
            CrestSublimated);
        if (failure is not null)
        {
            Log?.LogWarning($"Slot '{CurrentSlotKey}' could not be written ({reason}): {failure}");
            return;
        }

        LastSaveAt = UnityEngine.Time.unscaledTimeAsDouble;
        Log?.LogInfo(
            $"Slot '{CurrentSlotKey}' saved ({reason}): climaxes {Climaxes.Count}, "
            + $"corruption {Corruption?.Value ?? 0f:F2}.");
    }

    /// <summary>
    /// Writes the enemy catalogue if anything changed. A failure is reported and nothing else
    /// happens: losing a classification edit must never interrupt play (FR-239).
    /// </summary>
    internal static void SaveEnemies(string reason)
    {
        if (EnemyStore is null || !Enemies.IsDirty)
        {
            return;
        }

        string? failure = EnemyStore.Save(Enemies);
        if (failure is not null)
        {
            Log?.LogWarning($"The enemy catalogue could not be written ({reason}): {failure}");
            return;
        }

        Log?.LogInfo($"Enemy catalogue saved ({reason}): {Enemies.Summary()}.");
    }

    internal static void Reset()
    {
        PlayerAbnormals = null;
        GameplayStarted = false;
        IsSwollen = false;
        IsBound = false;
        IsDefeatPerformance = false;
        BinderEnemyId = null;
        BinderDisplayName = null;
        Meter?.Reset();
    }
}

/// <summary>
/// Tracks contributions the MOD has registered so all of them can be undone on unload, on a scene
/// change, or after an exception (SPEC003 5.10).
/// </summary>
internal sealed class InterventionLedger
{
    private readonly Dictionary<string, Action> _open = new(StringComparer.Ordinal);

    internal bool IsOpen(string key) => _open.ContainsKey(key);

    internal void Register(string key, Action release)
    {
        Release(key);
        _open[key] = release;
    }

    internal string? Release(string key)
    {
        if (!_open.Remove(key, out Action? release))
        {
            return null;
        }

        try
        {
            release();
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    internal IReadOnlyList<string> ReleaseAll()
    {
        string[] keys = _open.Keys.ToArray();
        var failures = new List<string>();
        foreach (string key in keys)
        {
            string? failure = Release(key);
            if (failure is not null)
            {
                failures.Add($"{key}: {failure}");
            }
        }

        return failures;
    }
}
