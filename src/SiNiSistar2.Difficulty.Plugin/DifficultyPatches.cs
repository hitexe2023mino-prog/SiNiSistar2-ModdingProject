using SiNiSistar2.Damage;
using SiNiSistar2.Difficulty.Core;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;

namespace SiNiSistar2.Difficulty.Plugin;

/// <summary>
/// Reports <c>Hard</c> to the checks that branch on difficulty, without writing the value the save
/// carries. <c>IsHardMode</c> is a static accessor beside the save-backed difficulty rather than on
/// top of it, which is what lets the MOD be removed and leave the player on the difficulty they
/// chose (SPEC002 DEC-101, FR-104).
///
/// The other check-side reader, <c>s_GameDifficultyForCheck</c>, is a plain static field on this
/// build; Harmony accepts a patch on its getter but Il2CppInterop cannot apply one. It is written
/// as a value by <see cref="DifficultyObserver"/> instead.
/// </summary>
internal static class HardModeReportPatches
{
    internal static void IsHardModePostfix(ref bool __result)
    {
        if (DifficultyRuntime.ReportHard)
        {
            __result = true;
        }
    }
}

/// <summary>What the rate override changed, so the finalizer can put it back (SPEC002 FR-107).</summary>
internal sealed class RateOverrideState
{
    internal RateOverrideState(DamageParameter parameter, int original)
    {
        Parameter = parameter;
        Original = original;
    }

    internal DamageParameter Parameter { get; }

    internal int Original { get; }
}

/// <summary>
/// Raises the status-ailment rate for the duration of one player-received damage resolution.
///
/// The rate lives on shared damage data, so it is written just before the roll and put back
/// immediately. A finalizer rather than a postfix does the restore: an exception thrown inside
/// damage resolution would otherwise leave the raised value on an asset that also serves the
/// statuses the player inflicts on enemies (SPEC002 FR-107, 残存リスク).
/// </summary>
internal static class AbnormalRatePatches
{
    internal static void OneDamagePrefix(DamageStack stack, out RateOverrideState? __state)
    {
        __state = null;
        AbnormalTuning tuning = DifficultyRuntime.Profile.Abnormal;
        if (!tuning.HasEffect)
        {
            return;
        }

        // Every entry has to be balanced by Exit in the finalizer, including the ones that lose.
        if (!DifficultyRuntime.RateScope.TryEnter())
        {
            return;
        }

        try
        {
            if (stack is null || !DifficultyRuntime.IsPlayerReceiving(null, stack))
            {
                return;
            }

            DamageParameter? parameter = stack.m_DamageParameter;
            if (parameter is null)
            {
                return;
            }

            int original = parameter.m_AbnormalRate;
            int scaled = AbnormalRateScaling.Apply(original, tuning.RateMultiplier);
            if (scaled == original)
            {
                return;
            }

            parameter.m_AbnormalRate = scaled;
            __state = new RateOverrideState(parameter, original);
        }
        catch (Exception exception)
        {
            DifficultyRuntime.Log?.LogWarning(
                $"Status rate override skipped for this hit: {exception.Message}");
        }
    }

    internal static void OneDamageFinalizer(RateOverrideState? __state)
    {
        try
        {
            if (__state is not null)
            {
                __state.Parameter.m_AbnormalRate = __state.Original;
            }
        }
        catch (Exception exception)
        {
            // A rate left raised would strengthen what the player inflicts on enemies too, which
            // is the opposite of the intent, so this is reported rather than swallowed.
            DifficultyRuntime.Log?.LogError(
                $"Could not restore the status rate to {__state?.Original}: {exception.Message}");
        }
        finally
        {
            DifficultyRuntime.RateScope.Exit();
        }
    }
}

/// <summary>
/// Advances a status a further step the moment it lands on the player (SPEC002 5.2).
/// <c>_IncrementLevel</c> is used rather than assigning <c>Level</c> so the game's own level-change
/// notification runs and the portrait, UI and derived effects are not skipped (SPEC002 FR-109).
/// </summary>
internal static class AbnormalLevelPatches
{
    internal static void AddAbnormalPostfix(
        AbnormalList __instance,
        AbnormalType abnormalType,
        DamageStack damageStack)
    {
        AbnormalTuning tuning = DifficultyRuntime.Profile.Abnormal;
        if (!tuning.Enabled || tuning.LevelBonus <= 0)
        {
            return;
        }

        // The bonus itself can drive another application; one bonus per application (FR-110).
        if (!DifficultyRuntime.LevelScope.TryEnter())
        {
            DifficultyRuntime.LevelScope.Exit();
            return;
        }

        try
        {
            if (!DifficultyRuntime.IsPlayerReceiving(__instance, damageStack))
            {
                return;
            }

            AbnormalData? data = __instance.GetAbnormalData(abnormalType);
            if (data is null)
            {
                return;
            }

            for (var step = 0; step < tuning.LevelBonus; step++)
            {
                if (data.Level >= data.MaxLevel)
                {
                    break;
                }

                data._IncrementLevel();
            }

            DifficultyRuntime.LogIntervention(
                $"Level bonus applied to {abnormalType}: now {data.Level}/{data.MaxLevel}.");
        }
        catch (Exception exception)
        {
            DifficultyRuntime.Log?.LogWarning(
                $"Level bonus skipped for {abnormalType}: {exception.Message}");
        }
        finally
        {
            DifficultyRuntime.LevelScope.Exit();
        }
    }
}

/// <summary>
/// Drops resistance input while a nullification window is open (SPEC002 5.3, FR-111).
///
/// Only the application of input is skipped. The meter's decay keeps running from
/// <c>Update</c>, the hold UI stays on screen, and none of the struggle numbers are written, so
/// the escalation defilement already drives is left entirely alone (SPEC002 FR-112, DEC-102/103).
/// </summary>
internal static class NullificationPatches
{
    internal static bool ExecutionPrefix(GachaGachaSystem __instance)
    {
        try
        {
            return !DifficultyRuntime.ShouldNullify(__instance);
        }
        catch (Exception)
        {
            // Failing open keeps the player able to struggle; failing closed could strand them.
            return true;
        }
    }
}
