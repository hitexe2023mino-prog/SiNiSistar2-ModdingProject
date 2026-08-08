using Il2CppInterop.Runtime;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.UI;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Asks the game to redraw the portrait after the MOD changes the swelling
/// (SPEC003 FR-221, 付録A A-28, A-30).
///
/// The measurement ruled out the two explanations that were easy to believe. Removing
/// <c>Breast</c> does not undress the body: <c>BreastSuper</c> carries
/// <c>physicalConditionFlag=Breast</c> itself, and the list still reports <c>Breast</c> with the
/// ordinary swelling gone. Nor is the art missing: the escalated status carries
/// <c>stand_breast_super</c> in its breast slot, already resolved.
///
/// What is left is that the portrait is drawn from those inputs at particular moments, and a
/// status the game never expected to change here does not produce one of them. So the game's own
/// <see cref="Portrait.UpdatePortrait"/> is called after the change. That decides nothing about
/// what to draw — the inputs above do — it only says now.
/// </summary>
internal static class PortraitRefresh
{
    /// <summary>
    /// Redraws every portrait, and records what changed the first time (SPEC003 付録A A-30).
    ///
    /// The sprite is read before and after. "The refresh did nothing" and "the refresh put the
    /// right art in" look identical from the outside, and the first one means the cause is
    /// somewhere else entirely.
    /// </summary>
    internal static void Refresh(string why, AbnormalType? wearing)
    {
        if (ManagerList.IsForbiddenManagerAccessState)
        {
            return;
        }

        try
        {
            PortraitParameter? parameter = wearing is null ? null : ParameterOf(wearing.Value);
            var portraits = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Portrait>());
            if (portraits.Length == 0)
            {
                PleasureRuntime.Probe(
                    "portrait-absent",
                    $"A-30: no Portrait component is in the scene, so the {why} could not be drawn.");
                return;
            }

            for (var index = 0; index < portraits.Length; index++)
            {
                var portrait = portraits[index]?.TryCast<Portrait>();
                if (portrait is null)
                {
                    continue;
                }

                string before = SpriteName(portrait);

                // Redrawing alone changed nothing — measured: the breast art was 'none' before and
                // after (付録A A-30). UpdatePortrait draws from the parameter already in effect, and
                // nothing had put the escalated status's parameter there. ChangePortrait is the
                // method that does, so the status's own parameter is handed to it. What is drawn is
                // still entirely the game's: this only delivers the parameter the status carries.
                if (parameter is null)
                {
                    portrait.ResetChangePortrait();
                }
                else
                {
                    portrait.ChangePortrait(parameter);
                }

                portrait.UpdatePortrait();
                string after = SpriteName(portrait);

                PleasureRuntime.Probe(
                    $"portrait-refresh-{why}-{after}",
                    $"A-30: the portrait was {(parameter is null ? "reset" : "changed")} for the "
                    + $"{why}. Its breast art was '{before}' and is now '{after}'.");
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The portrait could not be redrawn: {exception.Message}");
        }
    }

    /// <summary>
    /// The portrait parameter a status carries, if any.
    ///
    /// Read from the attached status rather than from the manager's template: the template reports
    /// the values a status has at level 0, before it belongs to anyone (付録A A-14).
    /// </summary>
    private static PortraitParameter? ParameterOf(AbnormalType type)
    {
        try
        {
            AbnormalList? list = PleasureRuntime.PlayerAbnormals;
            AbnormalData? data = list?.GetAbnormalData(type);
            return data?.AbnormalOne?.m_PortraitParameter;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SpriteName(Portrait portrait)
    {
        try
        {
            return portrait.m_Breast?.sprite?.name ?? "(none)";
        }
        catch (Exception)
        {
            return "(unreadable)";
        }
    }
}
