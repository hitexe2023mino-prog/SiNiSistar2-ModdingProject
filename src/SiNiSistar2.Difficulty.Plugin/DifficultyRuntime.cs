using BepInEx.Logging;
using SiNiSistar2.Damage;
using SiNiSistar2.Difficulty.Core;
using SiNiSistar2.Obj;

namespace SiNiSistar2.Difficulty.Plugin;

/// <summary>
/// State the Harmony patches read. Harmony entry points have to be static, so the resolved game
/// objects and the validated profile live here rather than being threaded through them.
///
/// Everything an Il2Cpp reference is stored in is refreshed by <see cref="DifficultyObserver"/>
/// and cleared when the managers are unavailable, because a reference held across a scene teardown
/// throws on every member access.
/// </summary>
internal static class DifficultyRuntime
{
    internal static readonly InterventionLedger Ledger = new();

    /// <summary>Guards the transient rate override against re-entrant damage resolution (FR-108).</summary>
    internal static readonly ReentrantScope RateScope = new();

    /// <summary>Guards the level bonus against being re-triggered by its own effect (FR-110).</summary>
    internal static readonly ReentrantScope LevelScope = new();

    internal const string MoveSlowKey = "burden-recovery-move-slow";

    internal static DifficultyProfile Profile { get; set; } = DifficultyProfile.Inactive;

    internal static ManualLogSource? Log { get; set; }

    internal static NullificationScheduler? Nullification { get; set; }

    internal static RecoveryPenaltyScheduler? Recovery { get; set; }

    internal static AbnormalType[] PleasureTypes { get; set; } = Array.Empty<AbnormalType>();

    internal static AbnormalType[] BurdenTypes { get; set; } = Array.Empty<AbnormalType>();

    /// <summary>
    /// True while the difficulty checks should answer <c>Hard</c>. Only the check-side accessors
    /// are patched; the saved value is never written (SPEC002 FR-104, DEC-101).
    /// </summary>
    internal static bool ReportHard { get; set; }

    /// <summary>The player's own status list. Used to tell player-received from enemy-received.</summary>
    internal static AbnormalList? PlayerAbnormals { get; set; }

    /// <summary>
    /// The struggle meter belonging to the player. The nullification prefix only suppresses input
    /// for this instance; when it cannot be resolved nothing is suppressed (SPEC002 FR-123).
    /// </summary>
    internal static GachaGachaSystem? PlayerGacha { get; set; }

    /// <summary>
    /// The MOD's identity in the game's multi-source values. <c>ResitValue</c> and
    /// <c>ReleaseValue</c> key on an object reference, so this instance has to outlive every
    /// contribution the MOD registers (SPEC002 FR-119).
    /// </summary>
    internal static Il2CppSystem.Object? ContributionKey { get; set; }

    internal static void LogIntervention(string message)
    {
        if (Profile.LogInterventions)
        {
            Log?.LogInfo(message);
        }
    }

    /// <summary>
    /// Whether resistance input from this struggle meter must be dropped right now. Anything other
    /// than the resolved player meter is left alone.
    /// </summary>
    internal static bool ShouldNullify(GachaGachaSystem? system)
    {
        if (system is null || PlayerGacha is null || Nullification is null)
        {
            return false;
        }

        return system.Pointer == PlayerGacha.Pointer && Nullification.IsNullifying;
    }

    /// <summary>
    /// Whether a status landing on this list is landing on the player. The damage stack is the
    /// game's own answer and is preferred; the cached player list covers applications that carry
    /// no stack. An unresolved owner means no intervention (SPEC002 FR-123).
    /// </summary>
    internal static bool IsPlayerReceiving(AbnormalList? list, DamageStack? stack)
    {
        if (stack is not null)
        {
            try
            {
                return stack.IsReceiverLelia;
            }
            catch (Exception)
            {
                // A stack recycled out from under us is not evidence that the player was hit.
                return false;
            }
        }

        return list is not null && PlayerAbnormals is not null && list.Pointer == PlayerAbnormals.Pointer;
    }

    /// <summary>Summed level of the configured statuses currently on the player.</summary>
    internal static int LevelSum(AbnormalList list, AbnormalType[] types)
    {
        var total = 0;
        foreach (AbnormalType type in types)
        {
            total += list.GetAbnormalLevel(type);
        }

        return total;
    }

    /// <summary>True when at least one of the configured statuses is active on the player.</summary>
    internal static bool HasAny(AbnormalList list, AbnormalType[] types)
    {
        foreach (AbnormalType type in types)
        {
            if (list.Has(type))
            {
                return true;
            }
        }

        return false;
    }

    internal static void Reset()
    {
        PlayerAbnormals = null;
        PlayerGacha = null;
        RateScope.Reset();
        LevelScope.Reset();
    }
}
