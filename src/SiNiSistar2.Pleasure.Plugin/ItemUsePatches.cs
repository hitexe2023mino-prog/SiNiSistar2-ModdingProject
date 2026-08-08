using SiNiSistar2.Obj;

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
            PleasureRuntime.Log?.LogInfo(
                $"[status] InventoryHandler.PlayItemEvent ran for item {__0}. Any status it applies "
                + "should follow on the next lines.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The item use could not be observed: {exception.Message}");
        }
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
