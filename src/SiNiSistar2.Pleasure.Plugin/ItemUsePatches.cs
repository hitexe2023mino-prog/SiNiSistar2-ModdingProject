using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Records an item being used (SPEC003 付録A A-18).
///
/// "Using the item logs nothing" has two causes that cannot be told apart from the status side: the
/// item's effect never reaches a status, or the use itself never reaches the MOD. Watching the use
/// separates them. If this line appears and no status line follows, the item's effect is not a
/// status change on this path; if it does not appear, the use is going somewhere else entirely.
/// </summary>
internal static class ItemUsePatches
{
    internal static void PlayItemEventPostfix(ItemID __0)
    {
        try
        {
            // The count and the swelling are printed with it, every time. "The item does nothing"
            // has causes the status lines cannot separate — an empty stack, or a condition inside
            // the item's own event — and a use with no context after it left both open.
            PleasureRuntime.Log?.LogInfo(
                $"[status] InventoryHandler.PlayItemEvent ran for item {__0}; {Describe(__0)}. "
                + "Any status it applies should follow on the next lines.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The item use could not be observed: {exception.Message}");
        }
    }

    /// <summary>
    /// Whether an item is usable is left entirely to the game (SPEC003 付録A A-20, DEC-229).
    ///
    /// An earlier version forced <c>ItemData.IsUsable</c> and <c>ItemData.CanUse</c> to true for
    /// named items, to let the swelling item be used while already swollen. It did not work: the
    /// item's own event carries an <c>m_ConditionChecker</c> that refuses independently, so the use
    /// ran, nothing was applied, and the stock was not even spent. Opening the outer gate leaves the
    /// inner one shut. Those two are also the methods the inventory UI asks about constantly, which
    /// made them a poor thing to be wrong about.
    /// </summary>
    /// <summary>How many are left, and what the player is already wearing.</summary>
    private static string Describe(ItemID id)
    {
        var parts = new List<string>(2);
        try
        {
            InventoryHandler? inventory = ManagerList.PlayerStatus?.m_InventoryHandler;
            parts.Add(inventory is null ? "stock unknown" : $"stock {inventory.GetItemCount(id)}");
        }
        catch (Exception)
        {
            parts.Add("stock unreadable");
        }

        try
        {
            AbnormalList? abnormals = PleasureRuntime.PlayerAbnormals;
            parts.Add(abnormals is null
                ? "swelling unknown"
                : $"Breast={abnormals.Has(AbnormalType.Breast)}, "
                  + $"BreastSuper={abnormals.Has(AbnormalType.BreastSuper)}");
        }
        catch (Exception)
        {
            parts.Add("swelling unreadable");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The item leaving the inventory, watched as well as the event that plays it.
    ///
    /// Two witnesses, because either one alone is ambiguous. <c>PlayItemEvent</c> returns a UniTask,
    /// so a postfix on it fires when the task is created rather than when the effect lands, and an
    /// item that is consumed without playing an event would not appear there at all.
    /// </summary>
    internal static void RemoveItemPostfix(ItemID __0, int __1)
    {
        try
        {
            PleasureRuntime.Log?.LogInfo($"[status] InventoryHandler.RemoveItem: {__1} x {__0}.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The item removal could not be observed: {exception.Message}");
        }
    }
}
