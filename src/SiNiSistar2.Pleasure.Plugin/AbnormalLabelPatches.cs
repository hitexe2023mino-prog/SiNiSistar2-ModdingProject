using SiNiSistar2.EventLabel;
using SiNiSistar2.Obj;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Watches the event label that items and authored events use to change statuses (SPEC003 5.8).
///
/// This exists because patching <c>AbnormalList.AddAbnormal</c> is not sufficient on its own. IL2CPP
/// inlines aggressively, and a small method called from one place inside the label can disappear
/// into its caller — SPEC002 already lost <c>GachaGachaSystem.Execution</c> exactly that way, and
/// spent a round trip discovering it. Watching the label as well means the item path is observed
/// whether or not the call inside it survived as a real function.
///
/// Applications seen through both routes collapse to one, on the frame guard in
/// <see cref="BreastPatches"/>.
/// </summary>
internal static class AbnormalLabelPatches
{
    internal static void ExecutionOnePostfix(AbnormalConditionParameter __0)
    {
        try
        {
            if (__0 is null)
            {
                return;
            }

            AbnormalChangeType change = __0.m_ChangeType;
            var types = __0.m_AbnormalTypeArray;
            int count = types?.Length ?? 0;

            var names = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                names.Add(types![index].ToString());
            }

            PleasureRuntime.Probe(
                $"label-{change}-{string.Join("+", names)}",
                $"A-15: AbnormalConditionLabel ran with change={change} over "
                + $"[{string.Join(", ", names)}]; timeScale {Time.timeScale}. This is the path items "
                + "and authored events use.");

            // Only an add can escalate. The label's other change types remove, cap or level down.
            if (change != AbnormalChangeType.Add)
            {
                return;
            }

            AbnormalList? player = PleasureRuntime.PlayerAbnormals;
            if (player is null)
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                if (types![index] == AbnormalType.Breast)
                {
                    BreastPatches.Observe(player, AbnormalType.Breast, "AbnormalConditionLabel");
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The status label could not be observed: {exception.Message}");
        }
    }
}
