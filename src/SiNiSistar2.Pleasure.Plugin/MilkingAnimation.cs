using Il2CppInterop.Runtime;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Plays the game's own milking animation while the player is milking (SPEC003 FR-258, 付録A A-23,
/// A-25).
///
/// The first attempt asked the player's field controller for the milking state and was told there
/// is none, and the reading was right: <c>Breast2_AnimatorOverride</c> maps 82 clips and the
/// milking take is not among them. The conclusion drawn from it was wrong. A state and a clip are
/// not the same thing. The field controller has no shortage of states; what it lacks is that clip
/// in any of them. An override controller exists precisely to put a chosen clip into a chosen
/// state, so the clip can be brought to the player rather than the player sent to the clip.
///
/// This is not the borrowing DEC-224 forbids. What is borrowed is a state name; what plays is the
/// real milking clip. SPEC001 samples <c>GetCurrentAnimatorClipInfo</c> and reports the clip's
/// name, so an observer watching this reads <c>ResumeBreast</c> — which is what is happening. A
/// substitute take would have made that reading false; this makes it true.
/// </summary>
internal static class MilkingAnimation
{
    private static RuntimeAnimatorController? _previous;
    private static int _returnState;
    private static bool _reportedSearch;

    /// <summary>
    /// Starts the milking animation, and says why in the log when it cannot.
    ///
    /// Every exit reports. A silent failure here reads to the player as "the animation is broken",
    /// which is the one thing the log has to be able to tell apart from "this build calls it
    /// something else".
    /// </summary>
    internal static void Start(string clipName, string slotName)
    {
        _previous = null;
        _returnState = 0;

        if (clipName.Length == 0)
        {
            return;
        }

        try
        {
            Lelia? lelia = ManagerList.Object?.Lelia;
            Animator? animator = lelia?.m_Animator;
            if (lelia is null || animator is null)
            {
                PleasureRuntime.Log?.LogWarning("Milking has no animator to play on.");
                return;
            }

            // A build that names a state after the clip needs none of what follows.
            int direct = Animator.StringToHash(clipName);
            if (animator.HasState(0, direct))
            {
                _returnState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                animator.Play(direct, 0, 0f);
                PleasureRuntime.Probe(
                    $"milking-state-direct-{clipName}",
                    $"A-23: milking plays the animator state '{clipName}' directly.");
                return;
            }

            AnimationClip? clip = FindClip(clipName);
            if (clip is null)
            {
                return;
            }

            int slot = Animator.StringToHash(slotName);
            if (!animator.HasState(0, slot))
            {
                PleasureRuntime.Log?.LogWarning(
                    $"A-25: the milking clip '{clipName}' was found, but layer 0 has no state "
                    + $"'{slotName}' to play it in. Set BreastSuper.MilkingAnimationSlot to a state "
                    + "name from the A-23 table.");
                return;
            }

            RuntimeAnimatorController? current = animator.runtimeAnimatorController;
            if (current is null)
            {
                PleasureRuntime.Log?.LogWarning("The player has no animator controller to override.");
                return;
            }

            // Wrapping whatever is on the player, rather than a controller of our own, is what
            // keeps the swollen body: the swelling is itself an override, and an override
            // controller layered over it inherits every clip it does not replace.
            // Built empty and pointed at the current controller afterwards. The interop assembly
            // exposes only the parameterless constructor plus the pointer one Il2CppInterop adds,
            // so the Unity constructor that takes a controller is not reachable from here.
            var over = new AnimatorOverrideController
            {
                runtimeAnimatorController = current,
                name = $"{current.name}+Milking",
            };
            string original = OriginalClipName(current, slotName);
            over[original] = clip;

            _previous = current;
            _returnState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;

            // Assigning the controller directly, and putting the old one back ourselves, rather
            // than through Lelia.ReplaceRuntimeAnimatorController / ResumeRuntimeAnimatorController.
            // Those restore the controller the game remembers, which is the unswollen one; leaving
            // milking would then undress the swelling as a side effect.
            animator.runtimeAnimatorController = over;
            animator.Play(slot, 0, 0f);

            PleasureRuntime.Probe(
                $"milking-override-{clipName}",
                $"A-25: milking plays the clip '{clipName}' through the state '{slotName}' "
                + $"(slot '{original}'), layered over '{current.name}'.");
        }
        catch (Exception exception)
        {
            _previous = null;
            _returnState = 0;
            PleasureRuntime.Log?.LogWarning($"The milking animation could not be started: {exception.Message}");
        }
    }

    /// <summary>Puts the controller and the pose back the way they were.</summary>
    internal static void Stop()
    {
        RuntimeAnimatorController? previous = _previous;
        int state = _returnState;
        _previous = null;
        _returnState = 0;

        try
        {
            Animator? animator = ManagerList.Object?.Lelia?.m_Animator;
            if (animator is null)
            {
                return;
            }

            if (previous is not null)
            {
                animator.runtimeAnimatorController = previous;
            }

            if (state != 0)
            {
                animator.Play(state, 0, 0f);
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The player could not be returned to their previous animation: {exception.Message}");
        }
    }

    /// <summary>
    /// The name the override table knows a slot by.
    ///
    /// An override controller is keyed by the clip names of the controller it wraps, not by state
    /// names. Those coincide for the base controller — its clips are named after their states — but
    /// not once a swelling override has already renamed them, where the state <c>Idle</c> holds a
    /// clip called <c>Breast2_Idle</c>. Asking the wrapped controller which clip is in the slot is
    /// what makes this work on the swollen player as well as the plain one.
    /// </summary>
    private static string OriginalClipName(RuntimeAnimatorController current, string slotName)
    {
        try
        {
            var over = current.TryCast<AnimatorOverrideController>();
            if (over is null)
            {
                return slotName;
            }

            RuntimeAnimatorController? inner = over.runtimeAnimatorController;
            var effective = over.animationClips;
            var originals = inner?.animationClips;
            int count = Math.Min(effective?.Length ?? 0, originals?.Length ?? 0);
            for (var index = 0; index < count; index++)
            {
                if (string.Equals(originals![index]?.name, slotName, StringComparison.OrdinalIgnoreCase))
                {
                    return effective![index]?.name ?? slotName;
                }
            }
        }
        catch (Exception)
        {
            // The slot name itself is the right fallback: on a controller whose table cannot be
            // read, it is also the only name available.
        }

        return slotName;
    }

    /// <summary>
    /// Finds the milking clip among everything Unity has loaded (SPEC003 付録A A-25).
    ///
    /// The clip belongs to a gallery scene, so whether it is in memory during ordinary play is a
    /// measurement and not a thing to reason about. When it is missing the neighbours are listed:
    /// "no clip called ResumeBreast" and "no clip with Breast in its name at all" are different
    /// findings, and only the first one is worth changing a config value over.
    /// </summary>
    private static AnimationClip? FindClip(string clipName)
    {
        var near = new List<string>();
        var count = 0;

        try
        {
            var loaded = Resources.FindObjectsOfTypeAll(Il2CppType.Of<AnimationClip>());
            for (var index = 0; index < loaded.Length; index++)
            {
                var clip = loaded[index]?.TryCast<AnimationClip>();
                string? name = clip?.name;
                if (clip is null || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                count++;
                if (string.Equals(name, clipName, StringComparison.Ordinal))
                {
                    return clip;
                }

                if (near.Count < 40
                    && (name!.Contains("Resume", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Breast", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Milk", StringComparison.OrdinalIgnoreCase)))
                {
                    near.Add(name);
                }
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"A-25: the loaded clips could not be searched: {exception.Message}");
            return null;
        }

        // Once per session. This walks every loaded object, and a line per keypress would bury the
        // one that matters.
        if (!_reportedSearch)
        {
            _reportedSearch = true;
            PleasureRuntime.Log?.LogInfo(
                $"A-25: '{clipName}' is not among the {count} animation clip(s) loaded during play. "
                + (near.Count == 0
                    ? "No loaded clip has Resume, Breast or Milk in its name either, so the gallery "
                      + "scene that owns it is not in memory."
                    : $"Clips with a related name: {string.Join(", ", near)}."));
        }

        return null;
    }
}
