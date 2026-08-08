using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Watches <c>Breast</c> being applied and escalates it to <c>BreastSuper</c> (SPEC003 5.8).
///
/// The count is of applications that arrive when <c>Breast</c> is already at its maximum level.
/// Below the maximum the game has its own escalation — the level rises — and pre-empting it would
/// make the ceiling arrive while the ordinary progression was still running.
///
/// Nothing is applied from inside this postfix. <c>AddAbnormal</c> is the game's own add path and
/// calling it again from within itself is re-entry into a method that is mid-update; the decision is
/// handed to the observer's frame instead, exactly as the climax is.
/// </summary>
internal static class BreastPatches
{
    internal static void AddAbnormalPostfix(AbnormalList __instance, AbnormalType abnormalType)
    {
        try
        {
            if (abnormalType != AbnormalType.Breast || !PleasureRuntime.Profile.BreastSuper.HasEffect)
            {
                return;
            }

            // Enemies have status lists too. Only the player's escalates.
            if (!ReferenceEquals(__instance, PleasureRuntime.PlayerAbnormals))
            {
                return;
            }

            BreastEscalation? escalation = PleasureRuntime.Breasts;
            if (escalation is null)
            {
                return;
            }

            int level = __instance.GetAbnormalLevel(AbnormalType.Breast);
            int maxLevel = MaxLevel(__instance, AbnormalType.Breast);
            bool atMax = maxLevel > 0 && level >= maxLevel;
            bool alreadySuper = __instance.Has(AbnormalType.BreastSuper);

            PleasureRuntime.Probe(
                "breast-applied",
                $"A-10: Breast applied; level {level} of {(maxLevel > 0 ? maxLevel.ToString() : "unknown")}, "
                + $"BreastSuper already present: {alreadySuper}.");

            BreastOutcome outcome = escalation.Record(
                atMax,
                alreadySuper,
                PleasureRuntime.Sensitivity?.Value ?? 0f);

            switch (outcome)
            {
                case BreastOutcome.Counted:
                    PleasureRuntime.LogTransition(
                        $"Breast is at its maximum and took another application: {escalation.Count} "
                        + $"so far, {escalation.Remaining} more before BreastSuper.");
                    break;

                case BreastOutcome.Escalate:
                    PleasureRuntime.PendingBreastSuper = true;
                    break;
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Breast escalation failed for this application: {exception.Message}");
        }
    }

    /// <summary>
    /// The status's own ceiling. Zero means it could not be read, which switches the escalation off
    /// rather than guessing: counting against an unknown ceiling would fire on the first hit.
    /// </summary>
    internal static int MaxLevel(AbnormalList list, AbnormalType type)
    {
        try
        {
            AbnormalData? data = list.GetAbnormalData(type);
            return data?.MaxLevel ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
