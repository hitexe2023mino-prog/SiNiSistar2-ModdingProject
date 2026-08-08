using SiNiSistar2.Manager;
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
    private static bool _identityReported;

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

    internal static void Observe(AbnormalList? list, AbnormalType type, string entryPoint)
    {
        try
        {
            if (list is null)
            {
                return;
            }

            bool isPlayer = IsPlayer(list);

            if (type == AbnormalType.Breast)
            {
                // Every Breast application, at Info, unconditionally.
                //
                // The one-shot probe hid the failure that led here: it keyed on the outcome of the
                // attribution, so once "Breast, not the player" had been recorded, every later
                // application that was also mis-attributed produced no line at all. The log then
                // read the same whether the item did nothing or the MOD threw the evidence away.
                // Breast is rare enough that recording each one costs nothing.
                PleasureRuntime.Log?.LogInfo(
                    $"Breast applied via {entryPoint}: owner={Describe(list)}, "
                    + $"isPlayer={isPlayer}, level={SafeLevel(list, type)}, timeScale={Time.timeScale}, "
                    + $"gameplayStarted={PleasureRuntime.GameplayStarted}.");
                ReportIdentities(list);
            }
            else
            {
                PleasureRuntime.Probe(
                    $"status-{type}",
                    $"A-15: {type} was added through {entryPoint} to {Describe(list)}; level "
                    + $"{SafeLevel(list, type)}, timeScale {Time.timeScale}.");
            }

            if (!isPlayer || type != AbnormalType.Breast)
            {
                return;
            }

            // Statuses are re-added as a save is restored. Counting those would advance the
            // escalation just for loading a game that already had swelling.
            if (!PleasureRuntime.GameplayStarted)
            {
                PleasureRuntime.Log?.LogInfo(
                    "Breast was applied before gameplay started, which is the save being restored; "
                    + "it is not counted towards BreastSuper.");
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

            PleasureRuntime.Log?.LogInfo(
                $"Breast escalation: outcome={outcome}, counted={escalation.Count}, "
                + $"remaining={escalation.Remaining}, atMax={atMax} (level {level}/{maxLevel}), "
                + $"alreadySuper={alreadySuper}.");

            if (outcome == BreastOutcome.Escalate)
            {
                PleasureRuntime.PendingBreastSuper = true;
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Breast escalation failed for this application: {exception}");
        }
    }

    /// <summary>
    /// Whether this is the player's status list.
    ///
    /// Compared by native pointer, never by reference: Il2CppInterop hands a Harmony postfix its own
    /// managed wrapper, so two wrappers around one native object are not the same object to
    /// <c>ReferenceEquals</c>.
    ///
    /// Resolved from the managers on every call rather than from state the observer caches. The
    /// cached value is null until the observer's first successful frame, and the game restores a
    /// save's statuses before that — so relying on the cache attributed the player's own swelling to
    /// nobody. Three identities are accepted because it is not established which of them the game
    /// hands to the add path, and being wrong about that silently disables the whole mechanism.
    /// </summary>
    private static bool IsPlayer(AbnormalList list)
    {
        IntPtr subject;
        try
        {
            subject = list.Pointer;
        }
        catch (Exception)
        {
            return false;
        }

        return Matches(subject, () => PleasureRuntime.PlayerAbnormals)
            || Matches(subject, () => ManagerList.PlayerStatus?.AbnormalList)
            || Matches(subject, () => ManagerList.Object?.Lelia?.AbnormalList)
            || TargetIsLelia(list);
    }

    private static bool Matches(IntPtr subject, Func<AbnormalList?> candidate)
    {
        try
        {
            AbnormalList? other = candidate();
            return other is not null && other.Pointer == subject;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The list's own answer about whose it is. Independent of which manager holds a reference to
    /// it, so it still works if the managers expose different instances.
    /// </summary>
    private static bool TargetIsLelia(AbnormalList list)
    {
        try
        {
            SiNiObject? target = list.Target;
            Lelia? lelia = ManagerList.Object?.Lelia;
            return target is not null && lelia is not null && target.Pointer == lelia.Pointer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Describe(AbnormalList list)
    {
        try
        {
            SiNiObject? target = list.Target;
            return target is null ? "a list with no target" : target.name;
        }
        catch (Exception)
        {
            return "a list whose target could not be read";
        }
    }

    /// <summary>
    /// Prints every candidate identity once, so which one the add path uses is a matter of record
    /// rather than of assumption.
    /// </summary>
    private static void ReportIdentities(AbnormalList list)
    {
        if (_identityReported)
        {
            return;
        }

        _identityReported = true;
        PleasureRuntime.Log?.LogInfo(
            $"[probe] A-17: the list receiving Breast is {Hex(() => list.Pointer)}. "
            + $"PleasureRuntime.PlayerAbnormals={Hex(() => PleasureRuntime.PlayerAbnormals?.Pointer)}, "
            + $"PlayerStatus.AbnormalList={Hex(() => ManagerList.PlayerStatus?.AbnormalList?.Pointer)}, "
            + $"Lelia.AbnormalList={Hex(() => ManagerList.Object?.Lelia?.AbnormalList?.Pointer)}, "
            + $"list.Target={Hex(() => list.Target?.Pointer)}, "
            + $"Lelia={Hex(() => ManagerList.Object?.Lelia?.Pointer)}.");
    }

    private static string Hex(Func<IntPtr?> get)
    {
        try
        {
            IntPtr? value = get();
            return value is null ? "(null)" : "0x" + value.Value.ToString("X");
        }
        catch (Exception exception)
        {
            return $"(unavailable: {exception.GetType().Name})";
        }
    }

    /// <summary>
    /// Counts one application per frame per status list.
    ///
    /// The overloads call one another — an add by type resolves the data and adds by data — so all
    /// the postfixes can fire for a single application. De-duplicating on the frame rather than with
    /// a re-entry counter means an exception thrown inside the game's add path cannot leave the
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
