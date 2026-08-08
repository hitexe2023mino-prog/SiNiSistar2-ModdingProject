using Il2CppInterop.Runtime;
using SiNiSistar2.Event.EvSystem;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Obj;
using UnityEngine;
using UnityEngine.Playables;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Watches the game start a scripted performance, and swap the player's animation set
/// (SPEC003 付録A A-38).
///
/// Polling has now failed four times on the same question, each time for a different reason: the
/// wrong gate, the wrong object, the wrong component, the wrong moment. Every one of those was a
/// guess about where to look, and the log's silence could not tell a wrong guess from a real
/// absence. This stops guessing about place and asks the game to say so itself.
///
/// <c>EventPlayerBase.PlayPlayer</c> is the one door every scripted performance goes through —
/// <c>HierarchyEventPlayer</c> and <c>PlayableDirectorPlayer</c> both derive from it — so a postfix
/// there names whatever runs, wherever it lives in the scene, whether or not the MOD thought to look
/// under <c>Root/Event</c>.
///
/// <c>Lelia.ReplaceRuntimeAnimatorController</c> is the game's own way of putting a different
/// animation set on the player, and it is how the swelling itself is worn. If the milking is a
/// state on some other override controller, this names that controller and its whole clip table at
/// the moment it goes on, which is the one reading that would make the animation reproducible.
///
/// Both are read-only postfixes. Nothing here changes what the game does.
/// </summary>
internal static class EventPatches
{
    private static readonly HashSet<string> _played = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _swapped = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _triggered = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _actions = new(StringComparer.Ordinal);

    /// <summary>
    /// Names a scripted performance while it runs (SPEC003 付録A A-38, A-39).
    ///
    /// This hangs off <c>Update</c> rather than <c>PlayPlayer</c>. <c>PlayPlayer</c> returns a
    /// <c>UniTask</c> — a struct returned by value — and a postfix on it left the player unable to
    /// move when the event fired: the performance began and its completion never arrived, so the
    /// game waited for it forever. The game stayed running, which made it worse rather than better;
    /// a probe that quietly makes the game unplayable is not a probe.
    ///
    /// <c>Update</c> returns void, so a postfix on it can carry nothing away, and
    /// <c>IsPlaying</c> is the game's own answer to the same question. It is still the one door
    /// every subclass goes through, which is the whole point.
    /// </summary>
    internal static void UpdatePostfix(EventPlayerBase __instance)
    {
        try
        {
            if (!PleasureRuntime.Profile.ProbeMeasurements || __instance is null
                || !__instance.IsPlaying)
            {
                return;
            }

            GameObject host = __instance.gameObject;
            string path = Describe(host);
            if (_played.Count > 200 || !_played.Add(path))
            {
                return;
            }

            // The concrete type is the answer to "is this a timeline or a hierarchy of actors",
            // which is the question the last three readings were circling.
            string kind = __instance.GetIl2CppType()?.Name ?? "(unknown)";
            var animators = host.GetComponentsInChildren(Il2CppType.Of<Animator>(), true);
            var directors = host.GetComponentsInChildren(Il2CppType.Of<PlayableDirector>(), true);

            PleasureRuntime.Log?.LogInfo(
                $"A-38: a scripted performance is running: '{path}' is a {kind} with "
                + $"{animators.Length} animator(s) and {directors.Length} director(s) beneath it. "
                + $"{DescribeDirectors(directors)} {DescribeOrders(host)}");
        }
        catch (Exception)
        {
            // A probe that can take the game down is worse than one that misses an event.
        }
    }

    /// <summary>Names an animation set as the game puts it on the player (SPEC003 付録A A-38).</summary>
    internal static void ReplaceControllerPostfix(AnimatorOverrideController __0)
    {
        try
        {
            if (!PleasureRuntime.Profile.ProbeMeasurements || __0 is null)
            {
                return;
            }

            string name = __0.name;
            if (_swapped.Count > 40 || !_swapped.Add(name))
            {
                return;
            }

            // The whole table, not the count. A controller is only useful here if the state holding
            // the milking clip can be named, and that name is in this list.
            var clips = __0.animationClips;
            var names = new List<string>(clips?.Length ?? 0);
            for (var index = 0; index < (clips?.Length ?? 0) && index < 120; index++)
            {
                string? clip = clips![index]?.name;
                if (!string.IsNullOrEmpty(clip))
                {
                    names.Add(clip!);
                }
            }

            PleasureRuntime.Log?.LogInfo(
                $"A-38: the game put the animation set '{name}' on the player. Its "
                + $"{names.Count} clip(s): {(names.Count == 0 ? "(none readable)" : string.Join(", ", names))}.");
        }
        catch (Exception)
        {
            // A probe that can take the game down is worse than one that misses a swap.
        }
    }

    /// <summary>
    /// Reads the orders an event object carries, rather than intercepting them (SPEC003 付録A A-41).
    ///
    /// The postfix on <c>AnimatorTriggerLabel.ExecutionOne</c> never fired, though the step called
    /// <c>Play Animation</c> plainly ran. The async sibling,
    /// <c>AnimatorTriggerAsyncLabel.ExecutionOneAsync</c>, returns a <c>UniTask</c>, and DEC-239
    /// forbids patching that — the last time a postfix went on a struct-returning method the player
    /// could not move.
    ///
    /// So this does not intercept the call at all. The orders are fields on components sitting on
    /// the event object, and they are there to be read whether or not anyone is executing them.
    /// Reading them needs no patch, cannot corrupt a return value, and gives the same answer:
    /// <c>m_PlayName</c> is the name of the thing to play.
    /// </summary>
    private static string DescribeOrders(GameObject host)
    {
        try
        {
            var rows = new List<string>(8);
            var components = host.GetComponentsInChildren(Il2CppType.Of<Component>(), true);
            var kinds = new List<string>(components.Length);
            for (var index = 0; index < components.Length && index < 60; index++)
            {
                var component = components[index];
                if (component is null)
                {
                    continue;
                }

                string kind = component.GetIl2CppType()?.Name ?? "";
                if (kind.EndsWith("Label", StringComparison.Ordinal))
                {
                    kinds.Add(kind);
                }

                ReadTrigger(component.TryCast<AnimatorTriggerLabel>()?.m_Parameters, rows);
                ReadTrigger(component.TryCast<AnimatorTriggerAsyncLabel>()?.m_Parameters, rows);
            }

            string labels = kinds.Count == 0 ? "no labels" : string.Join(", ", kinds);
            return rows.Count == 0
                ? $"Its orders ({labels}) name no animation."
                : $"Its orders ({labels}) name: {string.Join(" | ", rows)}.";
        }
        catch (Exception exception)
        {
            return $"Its orders could not be read ({exception.Message}).";
        }
    }

    /// <summary>One label's parameters, each of which names an animation.</summary>
    private static void ReadTrigger<T>(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T>? parameters, List<string> rows)
        where T : AnimatorTriggerParameter
    {
        if (parameters is null)
        {
            return;
        }

        for (var index = 0; index < parameters.Length && index < 8; index++)
        {
            AnimatorTriggerParameter? parameter = parameters[index];
            if (parameter is null)
            {
                continue;
            }

            Animator? animator = parameter.m_Animator;
            rows.Add(
                $"play '{parameter.m_PlayName ?? "(no name)"}' on layer {parameter.m_LayerIndex} "
                + $"at t={parameter.m_PlayTime:0.00}, found by {parameter.m_SiNiSearchType}, target "
                + $"{(animator is null ? "resolved at run time"
                    : $"'{Describe(animator.gameObject)}' on '{animator.runtimeAnimatorController?.name ?? "(none)"}'")}");
        }
    }

    /// <summary>
    /// Names the animation an event asks for (SPEC003 付録A A-40).
    ///
    /// <c>RemoveBreastCollision</c> turned out to hold a step called <c>Play Animation</c>, and it
    /// is a <c>HierarchyEventPlayer</c> with no animator and no timeline beneath it — so it does not
    /// perform anything itself, it asks. <c>AnimatorTriggerParameter</c> is the shape of that
    /// asking: a target animator, a layer, a time, and <c>m_PlayName</c> — the name of the thing to
    /// play. That name is what four rounds of polling were looking for.
    ///
    /// <c>ExecutionOne</c> returns void, so a postfix on it can carry nothing away (DEC-239).
    /// </summary>
    internal static void AnimatorTriggerPostfix(AnimatorTriggerParameter __0)
    {
        try
        {
            if (!PleasureRuntime.Profile.ProbeMeasurements || __0 is null)
            {
                return;
            }

            string play = __0.m_PlayName ?? "(no name)";
            if (_triggered.Count > 120 || !_triggered.Add(play))
            {
                return;
            }

            Animator? animator = __0.m_Animator;
            PleasureRuntime.Log?.LogInfo(
                $"A-40: an event asked for the animation '{play}' on layer {__0.m_LayerIndex} at "
                + $"t={__0.m_PlayTime:0.00}, searched as {__0.m_SiNiSearchType}. The target animator "
                + $"is {(animator is null ? "not set on the parameter (the event finds it at run time)"
                    : $"'{Describe(animator.gameObject)}' on controller "
                      + $"'{animator.runtimeAnimatorController?.name ?? "(none)"}'")}.");
        }
        catch (Exception)
        {
            // A probe that can take the game down is worse than one that misses a name.
        }
    }

    /// <summary>Names an action an event asks the player to take (SPEC003 付録A A-40).</summary>
    internal static void PlayerActionPostfix(PlayerActionParameter __0)
    {
        try
        {
            if (!PleasureRuntime.Profile.ProbeMeasurements || __0 is null)
            {
                return;
            }

            string action = __0.m_ActionType.ToString();
            if (_actions.Count > 60 || !_actions.Add(action))
            {
                return;
            }

            PleasureRuntime.Log?.LogInfo(
                $"A-40: an event asked the player for the action '{action}' "
                + $"(bool={__0.m_VariousBool}, float={__0.m_VariousFloat:0.00}).");
        }
        catch (Exception)
        {
            // A probe that can take the game down is worse than one that misses an action.
        }
    }

    private static string DescribeDirectors(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<UnityEngine.Component> directors)
    {
        if (directors.Length == 0)
        {
            return "It plays no timeline.";
        }

        var rows = new List<string>(directors.Length);
        for (var index = 0; index < directors.Length && index < 8; index++)
        {
            var director = directors[index]?.TryCast<PlayableDirector>();
            if (director is null)
            {
                continue;
            }

            rows.Add(
                $"'{director.playableAsset?.name ?? "(no asset)"}' ({director.duration:0.00}s, "
                + $"state={director.state})");
        }

        return $"Its timeline(s): {string.Join(", ", rows)}.";
    }

    private static string Describe(GameObject gameObject)
    {
        var parts = new List<string>(6);
        Transform? transform = gameObject.transform;
        while (transform is not null && parts.Count < 6)
        {
            parts.Insert(0, transform.name);
            transform = transform.parent;
        }

        return string.Join("/", parts);
    }
}
