using SiNiSistar2.Manager;
using SiNiSistar2.Obj;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// The MP side of the buff, and the reading of it the MP0 penalty depends on
/// (SPEC005 5.1, 5.3, FR-405).
///
/// <c>PlayerStatusManager.MP</c> is a <c>BattleMainParameter</c>, the same base the HP uses, so it
/// carries <c>Current</c>, <c>Max</c> and <c>Recover</c>. That answers 付録A A-402 from the interop
/// metadata rather than from a guess. It is still asked defensively: a member being declared is not
/// the same as it working, and a buff that cannot restore MP has to keep restoring HP rather than
/// falling over (9章).
///
/// Every write goes through the game's own <c>Recover</c>. The MOD never computes a new current
/// value and writes it, which is the same rule the HP obeys (SPEC003 FR-275, 10.2).
/// </summary>
internal static class PlayerVitals
{
    /// <summary>The player's MP, or null when there is no running game to ask.</summary>
    internal static BattleMainParameter? Mp
    {
        get
        {
            try
            {
                return ManagerList.PlayerStatus?.MP;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Whether the MP bar is empty (SPEC005 5.3 適用条件3).
    ///
    /// False when it cannot be read. An unreadable bar is not evidence of an empty one, and the
    /// penalty must never fire on a state nobody confirmed (the same rule SPEC002 FR-123 sets for
    /// an unidentifiable target).
    /// </summary>
    internal static bool IsMpEmpty
    {
        get
        {
            try
            {
                return Mp?.Current <= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Restores HP and MP by whole points (SPEC005 5.1 回復).
    ///
    /// Overflow is the game's problem rather than ours: <c>Recover</c> is what the game's own
    /// potions call, so the ceiling it applies is the ceiling everything else gets.
    /// </summary>
    internal static void Restore(int hp, int mp)
    {
        if (hp > 0)
        {
            try
            {
                PlayerHealth.Current?.Recover(hp, false);
            }
            catch (Exception exception)
            {
                PleasureRuntime.Log?.LogWarning(
                    $"The regeneration buff could not restore HP: {exception.Message}");
            }
        }

        if (mp <= 0)
        {
            return;
        }

        // Asked once and remembered. Reporting a failure on every tick of every buff would bury
        // the log, and the answer cannot change within a session.
        if (PleasureRuntime.MpRecoveryWorks == false)
        {
            return;
        }

        BattleMainParameter? bar = Mp;
        if (bar is null)
        {
            return;
        }

        try
        {
            int before = bar.Current;
            bar.Recover(mp, false);
            bool moved = bar.Current > before || before >= bar.Max;

            if (PleasureRuntime.MpRecoveryWorks is null)
            {
                PleasureRuntime.MpRecoveryWorks = moved;
                PleasureRuntime.Probe(
                    "mp-recovery",
                    moved
                        ? "A-402 answered: PlayerStatusManager.MP is a BattleMainParameter and "
                          + "Recover(int, bool) restores it, so the succubus buff gives MP back "
                          + "through the game's own path."
                        : "A-402 caution: PlayerStatusManager.MP.Recover did not move the bar. The "
                          + "buff goes on restoring HP and stops trying to restore MP; the MP0 "
                          + "penalty therefore has no way out through the buff (SPEC005 9章).");
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.MpRecoveryWorks = false;
            PleasureRuntime.Log?.LogWarning(
                $"The regeneration buff could not restore MP, and will not try again this session: "
                + $"{exception.Message}");
        }
    }
}
