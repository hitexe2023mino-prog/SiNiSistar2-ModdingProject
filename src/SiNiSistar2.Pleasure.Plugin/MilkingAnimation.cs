using Il2CppInterop.Runtime;
using SiNiSistar2;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using UnityEngine;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Plays the game's own milking animation while the player is milking (SPEC003 FR-258, 付録A A-23,
/// A-25, A-26).
///
/// Three measurements shaped this, in the order they were taken.
///
/// The player's field controller has no state for the milking take (A-23). That was read as "it
/// cannot be played", which was wrong: a state and a clip are not the same thing, and the field
/// controller has states to spare. An override controller puts a chosen clip into a chosen state,
/// so the clip can be brought to the player rather than the player sent to the clip.
///
/// The clip is not in memory during ordinary play (A-25). It belongs to a gallery bundle, so it has
/// to be asked for. The game's own <see cref="AssetBundleLoader"/> does that, and asking it is not
/// the same as reaching around it.
///
/// The load is asynchronous, so milking starts without the animation and picks it up when it
/// arrives. Waiting for the bundle before milking would put a load in front of a keypress.
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
    private static bool _playing;
    private static bool _wanted;
    private static bool _reportedMissing;
    private static bool _reportedPaths;
    private static double _lastRequest;
    private static AnimationClip? _clip;

    /// <summary>Starts the animation, or starts waiting for the clip that plays it.</summary>
    internal static void Start(string clipName, string slotName)
    {
        _previous = null;
        _returnState = 0;
        _playing = false;
        _wanted = clipName.Length > 0;

        if (_wanted)
        {
            TryPlay(clipName, slotName);
        }
    }

    /// <summary>
    /// Called every frame while milking, so the animation can begin the moment its bundle arrives.
    /// </summary>
    internal static void Tick(string clipName, string slotName)
    {
        if (_wanted && !_playing)
        {
            TryPlay(clipName, slotName);
        }
    }

    /// <summary>Puts the controller and the pose back the way they were.</summary>
    internal static void Stop()
    {
        RuntimeAnimatorController? previous = _previous;
        int state = _returnState;
        _previous = null;
        _returnState = 0;
        _playing = false;
        _wanted = false;

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

    private static void TryPlay(string clipName, string slotName)
    {
        try
        {
            Lelia? lelia = ManagerList.Object?.Lelia;
            Animator? animator = lelia?.m_Animator;
            if (lelia is null || animator is null)
            {
                return;
            }

            // A build that names a state after the clip needs none of what follows.
            int direct = Animator.StringToHash(clipName);
            if (animator.HasState(0, direct))
            {
                _returnState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                animator.Play(direct, 0, 0f);
                _playing = true;
                PleasureRuntime.Probe(
                    $"milking-state-direct-{clipName}",
                    $"A-23: milking plays the animator state '{clipName}' directly.");
                return;
            }

            AnimationClip? clip = FindClip(clipName);
            if (clip is null)
            {
                RequestClip(clipName);
                return;
            }

            int slot = Animator.StringToHash(slotName);
            if (!animator.HasState(0, slot))
            {
                _wanted = false;
                PleasureRuntime.Log?.LogWarning(
                    $"A-25: the milking clip '{clipName}' is loaded, but layer 0 has no state "
                    + $"'{slotName}' to play it in. Set BreastSuper.MilkingAnimationSlot to a state "
                    + "name from the A-23 table.");
                return;
            }

            RuntimeAnimatorController? current = animator.runtimeAnimatorController;
            if (current is null)
            {
                return;
            }

            // Wrapping whatever is on the player, rather than a controller of our own, is what
            // keeps the swollen body: the swelling is itself an override, and an override
            // controller layered over it inherits every clip it does not replace.
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
            _playing = true;

            PleasureRuntime.Probe(
                $"milking-override-{clipName}",
                $"A-25: milking plays the clip '{clipName}' through the state '{slotName}' "
                + $"(slot '{original}'), layered over '{current.name}'.");
        }
        catch (Exception exception)
        {
            _previous = null;
            _returnState = 0;
            _wanted = false;
            PleasureRuntime.Log?.LogWarning($"The milking animation could not be started: {exception.Message}");
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
    /// The clip is remembered once found. A bundle the game unloads takes its clips with it, so the
    /// remembered one is checked for having been collected rather than trusted.
    /// </summary>
    private static AnimationClip? FindClip(string clipName)
    {
        if (_clip is not null && !_clip.WasCollected)
        {
            return _clip;
        }

        _clip = null;
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
                    _clip = clip;
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

        // Once per session. This walks every loaded object, and a line per frame would bury the one
        // that matters.
        if (!_reportedMissing)
        {
            _reportedMissing = true;
            PleasureRuntime.Log?.LogInfo(
                $"A-25: '{clipName}' is not among the {count} animation clip(s) loaded during play. "
                + (near.Count == 0
                    ? "No loaded clip has Resume, Breast or Milk in its name either."
                    : $"Clips with a related name: {string.Join(", ", near)}."));
        }

        return null;
    }

    /// <summary>
    /// Asks the game to load the bundle the clip lives in (SPEC003 付録A A-26).
    ///
    /// Through <see cref="AssetBundleLoader"/>, which is how the game loads everything else: the
    /// bundles are hash-named and have dependencies, so opening the files directly would mean
    /// reimplementing the manifest. The virtual paths are searched for the clip's name first and for
    /// the gallery scene that owns the swollen player's takes second, and every candidate is logged,
    /// because a wrong guess at a path is otherwise indistinguishable from a bundle that has no such
    /// clip in it.
    /// </summary>
    private static void RequestClip(string clipName)
    {
        double now = Time.unscaledTimeAsDouble;
        if (now - _lastRequest < 3d)
        {
            return;
        }

        _lastRequest = now;

        try
        {
            AssetBundleLoader loader = AssetBundleLoader.Instance;
            if (loader is null)
            {
                return;
            }

            var candidates = new List<string>();
            var seen = new List<string>();
            foreach (BundleType type in new[]
                     {
                         BundleType.Scene, BundleType.Custom, BundleType.Enemy, BundleType.Location,
                     })
            {
                CollectPaths(loader, type, clipName, candidates, seen);
            }

            if (!_reportedPaths)
            {
                _reportedPaths = true;
                PleasureRuntime.Log?.LogInfo(
                    $"A-26: bundle paths related to milking: {(seen.Count == 0 ? "(none)" : string.Join(", ", seen))}. "
                    + $"Asking for: {(candidates.Count == 0 ? "(nothing)" : string.Join(", ", candidates))}.");
            }

            // Two at most. Each one is a file read and a chunk of memory, and if the clip is in
            // neither then the answer is a wrong path rather than a shortage of attempts.
            for (var index = 0; index < candidates.Count && index < 2; index++)
            {
                loader.LoadSimpleAsyncFromVirtualPath(candidates[index]);
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"A-26: the milking clip's bundle could not be requested: {exception.Message}");
        }
    }

    /// <summary>Virtual paths worth trying, most specific first.</summary>
    private static void CollectPaths(
        AssetBundleLoader loader,
        BundleType type,
        string clipName,
        List<string> candidates,
        List<string> seen)
    {
        var paths = loader.GetAllVirtualPath(type);
        if (paths is null)
        {
            return;
        }

        // Walked through the non-generic enumerator. The generated wrapper for the generic one
        // inherits MoveNext rather than declaring it, so it is not there to call from here.
        var enumerator = paths.GetEnumerator()?.TryCast<Il2CppSystem.Collections.IEnumerator>();
        if (enumerator is null)
        {
            return;
        }

        while (enumerator.MoveNext())
        {
            string? path = enumerator.Current?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            bool named = path!.Contains(clipName, StringComparison.OrdinalIgnoreCase);
            bool swollen = path.Contains("Breast2", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Player_Other", StringComparison.OrdinalIgnoreCase);
            if (!named && !swollen)
            {
                continue;
            }

            if (seen.Count < 40)
            {
                seen.Add($"{type}:{path}");
            }

            if (named)
            {
                candidates.Insert(0, path);
            }
            else if (!candidates.Contains(path))
            {
                candidates.Add(path);
            }
        }
    }
}
