using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Watches <c>Breast</c> being applied and escalates it to <c>BreastSuper</c> (SPEC003 5.8).
///
/// Every way the game has of adding a status is watched, not just the one an enemy attack uses. A
/// status arrives from an enemy, from an item, and from an authored event
/// (<c>AbnormalConditionLabel</c>), and which overload each of those reaches is not visible in the
/// interop metadata — the bodies are native. Watching all of them and de-duplicating is the only way
/// to be sure the item that applies swelling counts the same as a hold (FR-244).
///
/// Nothing is applied from inside these postfixes. <c>AddAbnormal</c> is the game's own add path and
/// calling it again from within itself is re-entry into a method that is mid-update; the decision is
/// handed to the observer's frame instead, exactly as the climax is.
/// </summary>
internal static class BreastPatches
{
    private static int _lastCountedFrame = int.MinValue;
    private static IntPtr _lastCountedList = IntPtr.Zero;

    /// <summary><c>AbnormalList.AddAbnormal(AbnormalType, int, DamageStack)</c>.</summary>
    internal static void AddByTypePostfix(AbnormalList __instance, AbnormalType __0) =>
        Observe(__instance, __0, "AddAbnormal(AbnormalType)");

    /// <summary><c>AbnormalList.AddAbnormal(AbnormalData, int, DamageStack)</c>.</summary>
    internal static void AddByDataPostfix(AbnormalList __instance, AbnormalData __0)
    {
        AbnormalType type;
        try
        {
            type = __0?.AbnormalType ?? AbnormalType.None;
        }
        catch (Exception)
        {
            return;
        }

        Observe(__instance, type, "AddAbnormal(AbnormalData)");
    }

    /// <summary><c>AbnormalList.AddOrRemoveAbnormal(AbnormalType, bool)</c>, the event-label path.</summary>
    internal static void AddOrRemovePostfix(AbnormalList __instance, AbnormalType __0, bool __1)
    {
        if (__1)
        {
            Observe(__instance, __0, "AddOrRemoveAbnormal");
        }
    }

    private static void Observe(AbnormalList? list, AbnormalType type, string entryPoint)
    {
        try
        {
            // Enemies have status lists too. Only the player's is of any interest here.
            if (list is null || !ReferenceEquals(list, PleasureRuntime.PlayerAbnormals))
            {
                return;
            }

            // Every status, not only Breast. "The item does nothing" has two very different causes
            // — the MOD never saw the application, or the item applies something other than Breast
            // — and reporting only Breast cannot tell them apart. Once per status type, so a hold
            // that reapplies a status every frame does not fill the log (A-15).
            PleasureRuntime.Probe(
                $"status-{type}",
                $"A-15: {type} was added to the player through {entryPoint}; it is now at level "
                + $"{SafeLevel(list, type)}.");

            if (type != AbnormalType.Breast)
            {
                return;
            }

            if (!PleasureRuntime.Profile.BreastSuper.HasEffect)
            {
                PleasureRuntime.Probe(
                    "breast-seen-while-off",
                    "Breast was applied but the escalation is off. Set "
                    + "BreastSuper.BreastSuperAfterApplications above 0 in "
                    + "community.sinisistar2.pleasure.cfg to enable it.");
                return;
            }

            if (!ClaimThisFrame(list))
            {
                return;
            }

            BreastEscalation? escalation = PleasureRuntime.Breasts;
            if (escalation is null)
            {
                return;
            }

            int level = list.GetAbnormalLevel(AbnormalType.Breast);
            int maxLevel = MaxLevel(list, AbnormalType.Breast);
            bool atMax = maxLevel > 0 && level >= maxLevel;
            bool alreadySuper = list.Has(AbnormalType.BreastSuper);

            ReportAttachedData(list);

            BreastOutcome outcome = escalation.Record(
                atMax || PleasureRuntime.Profile.BreastSuper.CountBelowMaxLevel,
                alreadySuper,
                PleasureRuntime.Sensitivity?.Value ?? 0f);

            switch (outcome)
            {
                case BreastOutcome.Counted:
                    // At Info rather than behind LogTransitions: this is what the escalation is
                    // counted by, and there is no way to tell "not counting" from "counting slowly"
                    // without seeing it. It only advances at the maximum level, so it is not chatty.
                    PleasureRuntime.Log?.LogInfo(
                        $"Breast applied at level {level}/{maxLevel} via {entryPoint}: "
                        + $"{escalation.Count} counted, {escalation.Remaining} more before BreastSuper.");
                    break;

                case BreastOutcome.Escalate:
                    PleasureRuntime.PendingBreastSuper = true;
                    break;

                case BreastOutcome.None when !atMax:
                    PleasureRuntime.LogTransition(
                        $"Breast applied at level {level}/{maxLevel} via {entryPoint}; below the "
                        + "maximum, so it raises the level rather than counting towards BreastSuper.");
                    break;
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Breast escalation failed for this application: {exception.Message}");
        }
    }

    /// <summary>
    /// Counts one application per frame per status list.
    ///
    /// The overloads call one another — an add by type resolves the data and adds by data — so all
    /// three postfixes can fire for a single application. De-duplicating on the frame rather than
    /// with a re-entry counter means an exception thrown inside the game's add path cannot leave the
    /// guard stuck closed.
    /// </summary>
    private static bool ClaimThisFrame(AbnormalList list)
    {
        int frame = Time.frameCount;
        IntPtr handle = list.Pointer;
        if (frame == _lastCountedFrame && handle == _lastCountedList)
        {
            return false;
        }

        _lastCountedFrame = frame;
        _lastCountedList = handle;
        return true;
    }

    /// <summary>
    /// Reports the attached <c>Breast</c>, which is the only reading that means anything.
    ///
    /// The first attempt read the manager's template instead and reported
    /// <c>physicalConditionFlag=Base</c> and <c>nameID=None</c> — the values a status has at level 0,
    /// before it is attached to anyone. What decides whether the existing cure can see
    /// <c>BreastSuper</c> is what the status carries once it is actually on the player.
    /// </summary>
    private static void ReportAttachedData(AbnormalList list)
    {
        try
        {
            AbnormalData? data = list.GetAbnormalData(AbnormalType.Breast);
            if (data is null)
            {
                return;
            }

            PleasureRuntime.Probe(
                "breast-attached",
                $"A-14: Breast while attached at level {data.Level}: "
                + $"physicalConditionFlag={data.PhysicalConditionFlag}, nameID={data.AbnormalNameID}, "
                + $"haanjaCanCure={data.HaanjaCanCure}. The list now reports "
                + $"PhysicalConditionFlag={list.PhysicalConditionFlag}.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Probe("breast-attached", $"A-14: the attached Breast could not be read: {exception.Message}");
        }
    }

    private static int SafeLevel(AbnormalList list, AbnormalType type)
    {
        try
        {
            return list.GetAbnormalLevel(type);
        }
        catch (Exception)
        {
            return -1;
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

    internal static void Reset()
    {
        _lastCountedFrame = int.MinValue;
        _lastCountedList = IntPtr.Zero;
    }
}
