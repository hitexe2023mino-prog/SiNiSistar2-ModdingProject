using SiNiSistar2.Manager;
using SiNiSistar2.Obj;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Watches the game's own "another one was applied but it is already at the ceiling" hook
/// (SPEC003 5.8, FR-250).
///
/// This is the event the requirement actually describes. Counting <c>AddAbnormal</c> only works
/// while the status is absent: applying <c>Breast</c> to a player who already has it at its maximum
/// level need not reach the add path at all, which is why using the item while already swollen
/// produced nothing. <c>AbnormalData.OnTryAddedOverMax</c> is the game's own name for exactly that
/// case, so it is the honest thing to count.
///
/// Both are watched. Whichever the build actually calls, one application is one count: the frame
/// guard in <see cref="BreastPatches"/> collapses the two.
/// </summary>
internal static class OverMaxPatches
{
    internal static void OnTryAddedOverMaxPostfix(AbnormalData __instance)
    {
        try
        {
            if (__instance is null || __instance.AbnormalType != AbnormalType.Breast)
            {
                return;
            }

            AbnormalList? player = ManagerList.PlayerStatus?.AbnormalList;
            if (player is null || !IsTheSameStatus(player, __instance))
            {
                return;
            }

            PleasureRuntime.Log?.LogInfo(
                "[status] Breast was applied again while already at its maximum level "
                + "(AbnormalData.OnTryAddedOverMax).");

            BreastPatches.Observe(player, AbnormalType.Breast, "OnTryAddedOverMax");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The over-max hook could not be observed: {exception.Message}");
        }
    }

    /// <summary>
    /// Whether this is the player's own <c>Breast</c>.
    ///
    /// Compared through the player's list rather than through <c>AbnormalData.Target</c>: every
    /// reading of <c>Target</c> on this build has come back null, so a rule keyed on it would never
    /// match anything.
    /// </summary>
    private static bool IsTheSameStatus(AbnormalList player, AbnormalData candidate)
    {
        try
        {
            AbnormalData? mine = player.GetAbnormalData(AbnormalType.Breast);
            return mine is not null && mine.Pointer == candidate.Pointer;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
