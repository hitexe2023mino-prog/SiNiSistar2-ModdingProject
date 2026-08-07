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

    internal static ManualLogSource? Log { get; set; }

    internal static PleasureMeter? Meter { get; set; }

    internal static SensitivityTrack? Sensitivity { get; set; }

    internal static ClimaxLedger Climaxes { get; } = new();

    /// <summary>The player's own status list, used to tell player-received from enemy-received.</summary>
    internal static AbnormalList? PlayerAbnormals { get; set; }

    /// <summary>True while the player is held. Set by the observer each frame.</summary>
    internal static bool IsBound { get; set; }

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

    internal static void Reset()
    {
        PlayerAbnormals = null;
        IsBound = false;
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
