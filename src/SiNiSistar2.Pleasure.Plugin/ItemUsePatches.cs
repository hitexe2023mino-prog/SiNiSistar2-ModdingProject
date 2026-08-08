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
    /// Whether the game considers an item usable right now (SPEC003 付録A A-20).
    ///
    /// <c>Inventory.Item</c> carries an <c>m_ConditionChecker</c>, so usability is gated by an
    /// authored condition rather than by stock alone. If the swelling item is refused while the
    /// player is already swollen, then "use it again while swollen" is not something the game
    /// permits, and no amount of watching the status paths will ever see it — which is the one
    /// explanation left for a use that produces no log at all.
    ///
    /// Recorded once per item and answer, so the menu redrawing every frame does not fill the log.
    /// </summary>
    internal static void IsUsablePostfix(ItemData __instance, ref bool __result)
    {
        try
        {
            if (__instance is null)
            {
                return;
            }

            ItemID id = __instance.ItemID;
            PleasureRuntime.Probe(
                $"usable-{id}-{__result}",
                $"A-20: the game reports {id} as {(__result ? "usable" : "NOT usable")} "
                + $"(count {__instance.Count}).");

            if (__result || !IsForced(id) || __instance.Count <= 0)
            {
                return;
            }

            __result = true;
            PleasureRuntime.Probe(
                $"forced-usable-{id}",
                $"{id} is refused by the game's own condition and is being reported as usable "
                + "because Diagnostics.ForceUsableItems names it.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Item usability could not be observed: {exception.Message}");
        }
    }

    /// <summary><c>ItemData.CanUse</c>, which is what the inventory list asks before greying a row.</summary>
    internal static void CanUsePostfix(ItemData __instance, ref bool __result)
    {
        try
        {
            if (__result || __instance is null || !IsForced(__instance.ItemID) || __instance.Count <= 0)
            {
                return;
            }

            __result = true;
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Item usability could not be overridden: {exception.Message}");
        }
    }

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

    private static bool IsForced(ItemID id)
    {
        foreach (string name in PleasureRuntime.Profile.ForceUsableItems)
        {
            if (string.Equals(name, id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
