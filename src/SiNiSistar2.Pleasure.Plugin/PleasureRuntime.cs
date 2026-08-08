using BepInEx.Logging;
using SiNiSistar2.Damage;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// State the Harmony patches read, and the probe log that answers the SPEC003 付録A measurements.
///
/// The MOD ships in a state where the only behaviour change is removing the HP0 defeat; everything
/// else is off until the measurements have been taken (SPEC003 FR-233). The probe is therefore the
/// most useful thing it can do on a first run.
/// </summary>
internal static class PleasureRuntime
{
    internal const string RemainHp1Key = "pleasure-remain-hp1-while-bound";

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

    /// <summary>Unscaled time at which the climax shake stops (SPEC003 FR-268).</summary>
    internal static double ClimaxShakeUntil { get; set; }

    /// <summary>Set when the corruption has earned the crest, consumed by the observer.</summary>
    internal static bool PendingLustCrest { get; set; }

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

        CorruptionTuning tuning = Profile.Corruption;
        if (!tuning.MarksTheBody || track.Cap <= 0f || PendingLustCrest)
        {
            return;
        }

        if (track.Value / track.Cap >= tuning.CrestAtFraction && !IsCrestWorn)
        {
            PendingLustCrest = true;
        }
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

    /// <summary>The captor's gallery id, or null when it cannot be resolved.</summary>
    internal static string? BinderEnemyId { get; set; }

    /// <summary>
    /// Set by the damage patch when the gauge fills, consumed by the observer. The climax has side
    /// effects on the UI and on the HP0 contribution, which belong on the main-thread update rather
    /// than inside damage resolution.
    /// </summary>
    internal static bool PendingClimax { get; set; }

    /// <summary>
    /// The MOD's identity in the game's multi-source values. <c>ResitValue</c> and
    /// <c>ReleaseValue</c> key on an object reference, so this must outlive every contribution.
    /// </summary>
    internal static Il2CppSystem.Object? ContributionKey { get; set; }

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
    internal static void LoadSlot(string slotKey, string reason)
    {
        CurrentSlotKey = slotKey;

        SidecarLoad load = Sidecar?.Load(slotKey) ?? new SidecarLoad(null, null, false);
        SidecarDocument? stored = load.Document;
        if (stored is not null)
        {
            Corruption?.LoadFrom(stored.Corruption);
            Climaxes.LoadFrom(stored.ClimaxCount);
            Breasts?.LoadFrom(stored.BreastAtMaxCount);
            Milk?.LoadFrom(stored.Milk);
        }
        else
        {
            Corruption?.LoadFrom(0f);
            Climaxes.ResetCount();
            Breasts?.Reset();
            Milk?.Reset();
        }

        Meter?.Reset();
        PendingClimax = false;
        PendingBreastSuper = false;
        ClimaxFlashUntil = 0d;

        string state = stored is not null
            ? $"restored climaxes {Climaxes.Count}, corruption {Corruption?.Value ?? 0f:F2}, "
              + $"breast applications {Breasts?.Count ?? 0} "
              + $"({Breasts?.Remaining ?? 0} more before BreastSuper), milk {Milk?.Fill ?? 0f:P0}"
            : "no sidecar yet, starting from zero";
        string notice = load.Notice is null ? string.Empty : $" The stored file {load.Notice}.";
        Log?.LogInfo($"Slot '{slotKey}' ({reason}): {state}.{notice}");
    }

    /// <summary>
    /// Reloads the current slot. Used when the player comes back from a defeat, which returns them
    /// to the last save and should return these values with them.
    /// </summary>
    internal static void ReloadCurrentSlot(string reason)
    {
        if (CurrentSlotKey is null)
        {
            return;
        }

        LoadSlot(CurrentSlotKey, reason);
    }

    /// <summary>
    /// Writes the run to the sidecar. A failure is reported and nothing else happens: losing a
    /// write must never interrupt play (SPEC003 FR-226).
    /// </summary>
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
            Milk?.Fill ?? 0f);
        if (failure is not null)
        {
            Log?.LogWarning($"Slot '{CurrentSlotKey}' could not be written ({reason}): {failure}");
            return;
        }

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
