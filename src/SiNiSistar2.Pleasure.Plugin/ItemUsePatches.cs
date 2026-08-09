using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Records an item being consumed (SPEC003 付録A A-18).
///
/// An earlier version also patched <c>InventoryHandler.PlayItemEvent</c> to watch the use itself.
/// That method returns <c>UniTask</c>, a struct, and detouring a struct-returning IL2CPP method
/// corrupts the returned task — the item's own event then hung or was skipped at random. Only
/// void-returning methods are safe to patch on this runtime, so the consumption below is the one
/// witness kept.
///
/// Whether an item is usable is left entirely to the game (SPEC003 付録A A-20, DEC-229).
/// An earlier version forced <c>ItemData.IsUsable</c> and <c>ItemData.CanUse</c> to true for
/// named items, to let the swelling item be used while already swollen. It did not work: the
/// item's own event carries an <c>m_ConditionChecker</c> that refuses independently, so the use
/// ran, nothing was applied, and the stock was not even spent.
/// </summary>
internal static class ItemUsePatches
{
    /// <summary>The item leaving the inventory.</summary>
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
