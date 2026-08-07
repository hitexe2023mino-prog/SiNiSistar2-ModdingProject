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

    /// <summary>Carries sensitivity and the climax count alongside the game's save slots.</summary>
    internal static SidecarStore? Sidecar { get; set; }

    /// <summary>Which slot the sidecar is following, or null before one has been identified.</summary>
    internal static string? CurrentSlotKey { get; private set; }

    internal static ManualLogSource? Log { get; set; }

    internal static PleasureMeter? Meter { get; set; }

    internal static SensitivityTrack? Sensitivity { get; set; }

    internal static ClimaxLedger Climaxes { get; } = new();

    /// <summary>The player's own status list, used to tell player-received from enemy-received.</summary>
    internal static AbnormalList? PlayerAbnormals { get; set; }

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
    /// The climax count and sensitivity belong to a save, not to the process. Without this a retry
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
            Sensitivity?.LoadFrom(stored.Sensitivity);
            Climaxes.LoadFrom(stored.ClimaxCount);
        }
        else
        {
            Sensitivity?.LoadFrom(0f);
            Climaxes.ResetCount();
        }

        Meter?.Reset();
        PendingClimax = false;
        ClimaxFlashUntil = 0d;

        string state = stored is not null
            ? $"restored climaxes {Climaxes.Count}, sensitivity {Sensitivity?.Value ?? 0f:F2}"
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

        string? failure = Sidecar.Save(CurrentSlotKey, Sensitivity?.Value ?? 0f, Climaxes.Count);
        if (failure is not null)
        {
            Log?.LogWarning($"Slot '{CurrentSlotKey}' could not be written ({reason}): {failure}");
            return;
        }

        Log?.LogInfo(
            $"Slot '{CurrentSlotKey}' saved ({reason}): climaxes {Climaxes.Count}, "
            + $"sensitivity {Sensitivity?.Value ?? 0f:F2}.");
    }

    internal static void Reset()
    {
        PlayerAbnormals = null;
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
