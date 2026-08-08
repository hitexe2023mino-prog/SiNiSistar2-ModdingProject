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
}
