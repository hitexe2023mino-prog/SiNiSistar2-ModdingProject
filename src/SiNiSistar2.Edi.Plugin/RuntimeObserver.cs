using System.Collections.Concurrent;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Edi.Core;
using SiNiSistar2.Manager;
using SiNiSistar2.Manager.Gallery;
using SiNiSistar2.Obj;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SiNiSistar2.Edi.Plugin;

public sealed class RuntimeObserver : MonoBehaviour
{
    private readonly List<AnimationFrameSnapshot> _eventFrames = new(4096);
    private readonly ConcurrentBag<Task> _generationTasks = new();
    private readonly Dictionary<string, string> _visibleTexts = new(StringComparer.Ordinal);
    private readonly HashSet<EventKey> _generationStarted = new();
    private PlaybackCoordinator? _coordinator;
    private DiagnosticRecorder? _diagnostics;
    private EventCaptureTracker? _capture;
    private AnimationSessionWriter? _session;
    private Func<EventKey, IReadOnlyList<AnimationFrameSnapshot>, bool, Task<GeneratedAssetResult>>? _generate;
    private ManualLogSource? _log;
    private EventObservation? _currentEvent;
    private Animator? _currentAnimator;
    private Animator[] _eventAnimators = Array.Empty<Animator>();
    private string[] _activeStatuses = Array.Empty<string>();
    private string _eventInstanceId = string.Empty;
    private float _pollInterval;
    private float _nextPollAt;
    private float _nextCoverageAt;
    private float _nextTextAt;
    private bool _wasPaused;
    private bool _faultLogged;
    private bool _eventCaptureComplete = true;

    public RuntimeObserver(IntPtr pointer)
        : base(pointer)
    {
    }

    [HideFromIl2Cpp]
    public void Configure(
        PlaybackCoordinator coordinator,
        DiagnosticRecorder diagnostics,
        AnimationSessionWriter session,
        Func<EventKey, IReadOnlyList<AnimationFrameSnapshot>, bool, Task<GeneratedAssetResult>> generate,
        float pollInterval,
        ManualLogSource log)
    {
        _coordinator = coordinator;
        _diagnostics = diagnostics;
        _capture = new EventCaptureTracker(diagnostics);
        _session = session;
        _generate = generate;
        _pollInterval = pollInterval;
        _log = log;

        foreach (AbnormalType type in Enum.GetValues(typeof(AbnormalType)))
        {
            string id = type.ToString();
            diagnostics.RegisterStatus(id, type == AbnormalType.Breast ? "膨乳" : id);
        }
    }

    public void Update()
    {
        if (_coordinator is null || Time.unscaledTime < _nextPollAt)
        {
            return;
        }

        _nextPollAt = Time.unscaledTime + _pollInterval;
        try
        {
            Poll();
            if (Time.unscaledTime >= _nextTextAt)
            {
                _nextTextAt = Time.unscaledTime + 0.25f;
                CaptureTextChanges();
            }
            _faultLogged = false;
        }
        catch (Exception exception)
        {
            _coordinator.SetInactive();
            if (!_faultLogged)
            {
                _faultLogged = true;
                _log?.LogWarning($"Runtime observation failed closed and will retry: {exception}");
            }
        }

        if (Time.unscaledTime >= _nextCoverageAt)
        {
            _nextCoverageAt = Time.unscaledTime + 2f;
            FlushDiagnostics();
        }
    }

    public void LateUpdate()
    {
        if (_currentEvent is null || _currentAnimator is null || _session is null)
        {
            return;
        }

        try
        {
            AnimationFrameSnapshot frame = CaptureAnimationFrame(
                _currentEvent,
                _currentAnimator,
                _eventAnimators,
                _eventInstanceId,
                _activeStatuses);
            bool saved = _session.Enqueue(
                "animation-sample",
                frame,
                frame.RealtimeMilliseconds,
                frame.FrameCount);
            _eventCaptureComplete &= saved;
            if (saved)
            {
                _eventFrames.Add(frame);
            }

            if (_eventCaptureComplete
                && _currentEvent.IsLooping
                && !_generationStarted.Contains(_currentEvent.Key)
                && MotionScriptGenerator.TryExtractFirstCompleteLoop(_eventFrames, out var loop))
            {
                StartGeneration(_currentEvent.Key, loop, isLoop: true);
            }
        }
        catch (Exception exception)
        {
            _eventCaptureComplete = false;
            _session.Enqueue(
                "capture-warning",
                new { category = "animation-sample", exception = exception.ToString() },
                RealtimeMilliseconds(),
                Time.frameCount);
            if (!_faultLogged)
            {
                _faultLogged = true;
                _log?.LogWarning($"Animation frame capture failed: {exception}");
            }
        }
    }

    [HideFromIl2Cpp]
    public async Task WaitForGenerationAsync() =>
        await Task.WhenAll(_generationTasks.ToArray()).ConfigureAwait(false);

    public void Shutdown()
    {
        if (_currentEvent is not null && !_currentEvent.IsLooping)
        {
            FinalizeNonLoop(_currentEvent);
        }
        _capture?.Observe(null);
        _currentEvent = null;
        _currentAnimator = null;
        _coordinator?.SetInactive();
        if (_diagnostics is not null)
        {
            FlushDiagnostics();
        }
    }

    public void OnApplicationQuit() => Shutdown();

    private void Poll()
    {
        if (!ManagerList.HasDoneSceneSetUp || ManagerList.Instance is null)
        {
            SetNoEvent();
            return;
        }

        ObjectManager? objects = ManagerList.Object;
        Lelia? lelia = objects?.Lelia;
        PlayerStatusManager? playerStatus = ManagerList.PlayerStatus;
        if (objects is null || lelia is null || playerStatus?.AbnormalList is null)
        {
            SetNoEvent();
            return;
        }

        _activeStatuses = GetActiveStatuses(playerStatus.AbnormalList);
        _coordinator!.SetGameplayActive(_activeStatuses);
        EventObservation? observation = ObserveCurrentEvent(objects, lelia, out Animator? animator);

        EventObservation? previous = _currentEvent;
        bool changed = previous?.Key != observation?.Key;
        if (changed)
        {
            if (previous is not null && !previous.IsLooping)
            {
                FinalizeNonLoop(previous);
            }

            _session!.Enqueue(
                "event-transition",
                new
                {
                    previous = previous?.Key,
                    current = observation?.Key,
                    currentClipLengthSeconds = observation?.ClipLengthSeconds,
                    currentIsLooping = observation?.IsLooping,
                },
                RealtimeMilliseconds(),
                Time.frameCount);
            _eventFrames.Clear();
            _eventCaptureComplete = true;
            _eventInstanceId = observation is null ? string.Empty : Guid.NewGuid().ToString("N");
        }

        _currentEvent = observation;
        _currentAnimator = animator;
        if (changed)
        {
            _eventAnimators = animator is null ? Array.Empty<Animator>() : FindEventAnimators(animator);
        }
        bool capturedNewCandidate = _capture!.Observe(observation);
        if (observation is not null)
        {
            _coordinator.ObserveEvent(observation);
        }

        if (previous is not null && changed)
        {
            _coordinator.EndEvent(previous.Key);
        }

        if (capturedNewCandidate)
        {
            FlushDiagnostics();
        }

        if (observation is null)
        {
            _coordinator.UpdateStatuses(_activeStatuses);
        }

        bool paused = Time.timeScale <= 0.0001f;
        if (paused && !_wasPaused)
        {
            _coordinator.Pause();
        }
        else if (!paused && _wasPaused)
        {
            _coordinator.Resume(_currentEvent);
        }

        _wasPaused = paused;
    }

    private void SetNoEvent()
    {
        if (_currentEvent is not null && !_currentEvent.IsLooping)
        {
            FinalizeNonLoop(_currentEvent);
        }
        if (_capture!.Observe(null))
        {
            FlushDiagnostics();
        }
        _currentEvent = null;
        _currentAnimator = null;
        _eventAnimators = Array.Empty<Animator>();
        _eventFrames.Clear();
        _coordinator!.SetInactive();
    }

    [HideFromIl2Cpp]
    private string[] GetActiveStatuses(AbnormalList abnormalList)
    {
        var active = new List<string>();
        foreach (AbnormalType type in Enum.GetValues(typeof(AbnormalType)))
        {
            if (type != AbnormalType.None && abnormalList.Has(type))
            {
                active.Add(type.ToString());
            }
        }

        return active.ToArray();
    }

    private static EventObservation? ObserveCurrentEvent(
        ObjectManager objects,
        Lelia lelia,
        out Animator? selectedAnimator)
    {
        selectedAnimator = null;
        GaTakePlayer? takePlayer = ManagerList.Gallery?.CurrentTakePlayer;
        AnimationTakeData? take = takePlayer?.PlayingTakeData;
        if (take is not null && TryReadAnimator(take.m_Animator, out AnimatorSample gallerySample))
        {
            selectedAnimator = take.m_Animator;
            return CreateObservation(
                "gallery",
                NonEmpty(take.m_TakeName, "gallery"),
                gallerySample,
                take.m_IsAnimatorLoop ? "loop" : "reaction");
        }

        if (lelia.IsHold && lelia.Bind?.BinderEnemy is { } binder
            && TryReadAnimator(lelia.m_Animator, out AnimatorSample holdSample))
        {
            selectedAnimator = lelia.m_Animator;
            return CreateObservation("hold", binder.GalleryEnemyID.ToString(), holdSample, "loop");
        }

        if (lelia.IsHP0 && TryReadAnimator(lelia.m_Animator, out AnimatorSample gameOverSample))
        {
            selectedAnimator = lelia.m_Animator;
            string actor = lelia.Bind?.BinderEnemy?.GalleryEnemyID.ToString() ?? "lelia";
            return CreateObservation("game-over", actor, gameOverSample, "reaction");
        }

        if (objects.IsCinematicEvent && TryReadAnimator(lelia.m_Animator, out AnimatorSample eventSample))
        {
            selectedAnimator = lelia.m_Animator;
            return CreateObservation("scripted-event", SceneManager.GetActiveScene().name, eventSample, "reaction");
        }

        return null;
    }

    private static EventObservation CreateObservation(
        string context,
        string actor,
        AnimatorSample sample,
        string phase) =>
        new(
            new EventKey(context, actor, sample.AnimationId, phase),
            sample.NormalizedTime,
            sample.ClipLengthSeconds,
            sample.IsLooping,
            SceneManager.GetActiveScene().name,
            DateTimeOffset.UtcNow);

    private static bool TryReadAnimator(Animator? animator, out AnimatorSample sample)
    {
        sample = default;
        if (animator is null || !animator.isActiveAndEnabled || animator.layerCount == 0)
        {
            return false;
        }

        var clips = animator.GetCurrentAnimatorClipInfo(0);
        if (clips.Length == 0 || clips[0].clip is null)
        {
            return false;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        AnimationClip clip = clips[0].clip;
        sample = new AnimatorSample(clip.name, state.normalizedTime, clip.length, clip.isLooping);
        return !string.IsNullOrWhiteSpace(sample.AnimationId);
    }

    private static AnimationFrameSnapshot CaptureAnimationFrame(
        EventObservation observation,
        Animator animator,
        IReadOnlyList<Animator> eventAnimators,
        string eventInstanceId,
        IReadOnlyList<string> activeStatuses)
    {
        AnimatorRuntimeSnapshot primaryAnimator = CaptureAnimator(animator);
        AnimatorRuntimeSnapshot[] relatedAnimators = eventAnimators
            .Where(candidate => candidate.GetInstanceID() != animator.GetInstanceID())
            .Select(CaptureAnimator)
            .ToArray();
        string animatorPath = primaryAnimator.Path;

        return new AnimationFrameSnapshot(
            eventInstanceId,
            observation.Key,
            observation.SceneName,
            RealtimeMilliseconds(),
            Time.frameCount,
            Time.timeAsDouble,
            Time.unscaledTimeAsDouble,
            Time.deltaTime,
            Time.unscaledDeltaTime,
            Time.timeScale,
            primaryAnimator.Layers.Count == 0
                ? observation.NormalizedTime
                : primaryAnimator.Layers[0].CurrentState.NormalizedTime,
            activeStatuses.ToArray(),
            primaryAnimator,
            relatedAnimators);
    }

    private static AnimatorRuntimeSnapshot CaptureAnimator(Animator animator)
    {
        string animatorPath = BuildAbsolutePath(animator.transform);
        IReadOnlyList<TransformSnapshot> transforms = CaptureTransforms(animator, animatorPath);
        var layers = new List<AnimatorLayerSnapshot>(animator.layerCount);
        for (var index = 0; index < animator.layerCount; index++)
        {
            bool inTransition = animator.IsInTransition(index);
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(index);
            AnimatorStateSnapshot? next = inTransition ? State(animator.GetNextAnimatorStateInfo(index)) : null;
            AnimatorTransitionSnapshot? transition = null;
            if (inTransition)
            {
                AnimatorTransitionInfo info = animator.GetAnimatorTransitionInfo(index);
                transition = new AnimatorTransitionSnapshot(
                    info.fullPathHash,
                    info.nameHash,
                    info.userNameHash,
                    info.duration,
                    info.normalizedTime,
                    info.durationUnit == DurationUnit.Fixed,
                    info.anyState,
                    info.entry,
                    info.exit);
            }

            layers.Add(new AnimatorLayerSnapshot(
                index,
                animator.GetLayerName(index),
                animator.GetLayerWeight(index),
                inTransition,
                State(current),
                next,
                transition,
                Clips(animator.GetCurrentAnimatorClipInfo(index)),
                inTransition ? Clips(animator.GetNextAnimatorClipInfo(index)) : Array.Empty<AnimationClipSnapshot>()));
        }

        var parameters = new[]
        {
            new AnimatorParameterSnapshot(
                null,
                null,
                null,
                null,
                false,
                "UnityEngine.AnimatorControllerParameter getters are unstripped and throw NotSupportedException in this build's generated interop."),
        };
        return new AnimatorRuntimeSnapshot(
            animator.name,
            animatorPath,
            animator.isActiveAndEnabled,
            animator.speed,
            animator.updateMode.ToString(),
            animator.applyRootMotion,
            animator.hasRootMotion,
            animator.isHuman,
            Vector(animator.deltaPosition),
            Quaternion(animator.deltaRotation),
            Vector(animator.rootPosition),
            Quaternion(animator.rootRotation),
            animator.isHuman ? Vector(animator.bodyPosition) : default,
            animator.isHuman ? Quaternion(animator.bodyRotation) : new QuaternionSnapshot(0, 0, 0, 1),
            Vector(animator.velocity),
            Vector(animator.angularVelocity),
            Vector(animator.pivotPosition),
            layers,
            parameters,
            transforms);
    }

    private static Animator[] FindEventAnimators(Animator primary)
    {
        var animators = new Dictionary<int, Animator>
        {
            [primary.GetInstanceID()] = primary,
        };
        foreach (Animator animator in Resources.FindObjectsOfTypeAll<Animator>())
        {
            if (animator is not null && animator.isActiveAndEnabled)
            {
                animators[animator.GetInstanceID()] = animator;
            }
        }

        return animators.Values.ToArray();
    }

    private static IReadOnlyList<TransformSnapshot> CaptureTransforms(Animator animator, string rootPath)
    {
        var boneNames = new Dictionary<int, string>();
        if (animator.isHuman)
        {
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                Transform? transform = animator.GetBoneTransform(bone);
                if (transform is not null)
                {
                    boneNames[transform.GetInstanceID()] = bone.ToString();
                }
            }
        }

        var result = new List<TransformSnapshot>();
        var stack = new Stack<(Transform Transform, string Path)>();
        stack.Push((animator.transform, rootPath));
        while (stack.Count > 0)
        {
            (Transform transform, string path) = stack.Pop();
            boneNames.TryGetValue(transform.GetInstanceID(), out string? boneName);
            result.Add(new TransformSnapshot(
                path,
                transform.name,
                transform.gameObject.activeInHierarchy,
                Vector(transform.localPosition),
                Quaternion(transform.localRotation),
                Vector(transform.localScale),
                Vector(transform.position),
                Quaternion(transform.rotation),
                Vector(transform.lossyScale),
                boneName));
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                stack.Push((child, $"{path}/{Segment(child)}"));
            }
        }

        return result;
    }

    private void CaptureTextChanges()
    {
        foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (text is null)
            {
                continue;
            }

            string path = BuildAbsolutePath(text.transform);
            string value = text.text ?? string.Empty;
            bool active = text.gameObject.activeInHierarchy;
            string state = $"{active}\n{value}";
            if (_visibleTexts.TryGetValue(path, out string? previous) && previous == state)
            {
                continue;
            }

            _visibleTexts[path] = state;
            _session!.Enqueue(
                "text-change",
                new TextChangeSnapshot("UnityEngine.UI.Text", path, value, active),
                RealtimeMilliseconds(),
                Time.frameCount);
        }
    }

    [HideFromIl2Cpp]
    private void FinalizeNonLoop(EventObservation observation)
    {
        if (_eventCaptureComplete
            && _eventFrames.Count >= 8
            && !_generationStarted.Contains(observation.Key))
        {
            StartGeneration(observation.Key, _eventFrames.ToArray(), isLoop: false);
        }
    }

    [HideFromIl2Cpp]
    private void StartGeneration(
        EventKey key,
        IReadOnlyList<AnimationFrameSnapshot> frames,
        bool isLoop)
    {
        _generationStarted.Add(key);
        Task task = GenerateAndRecordAsync(key, frames, isLoop);
        _generationTasks.Add(task);
    }

    [HideFromIl2Cpp]
    private async Task GenerateAndRecordAsync(
        EventKey key,
        IReadOnlyList<AnimationFrameSnapshot> frames,
        bool isLoop)
    {
        try
        {
            GeneratedAssetResult result = await _generate!(key, frames, isLoop).ConfigureAwait(false);
            _session!.Enqueue(
                "generation-result",
                result,
                frames[^1].RealtimeMilliseconds,
                frames[^1].FrameCount);
            if (result.Success)
            {
                _log?.LogInfo($"Generated measured-motion gallery '{result.Gallery}'; manifest={result.ManifestPath}");
            }
            else
            {
                _log?.LogWarning($"Measured-motion generation rejected for {key}: {result.UnavailableReason}");
            }
        }
        catch (Exception exception)
        {
            _session!.Enqueue(
                "generation-result",
                new { success = false, key, exception = exception.ToString() },
                frames[^1].RealtimeMilliseconds,
                frames[^1].FrameCount);
            _log?.LogWarning($"Measured-motion generation failed for {key}: {exception}");
        }
    }

    private static AnimatorStateSnapshot State(AnimatorStateInfo state) => new(
        state.fullPathHash,
        state.shortNameHash,
        state.tagHash,
        state.normalizedTime,
        state.length,
        state.speed,
        state.speedMultiplier,
        state.loop);

    private static IReadOnlyList<AnimationClipSnapshot> Clips(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<AnimatorClipInfo> clips)
    {
        var result = new List<AnimationClipSnapshot>(clips.Length);
        foreach (AnimatorClipInfo info in clips)
        {
            AnimationClip? clip = info.clip;
            if (clip is null)
            {
                continue;
            }

            result.Add(new AnimationClipSnapshot(
                clip.name,
                clip.length,
                clip.frameRate,
                clip.isLooping,
                info.weight,
                clip.hasMotionCurves,
                clip.hasRootCurves,
                clip.hasRootMotion,
                clip.humanMotion));
        }

        return result;
    }

    private static string BuildAbsolutePath(Transform transform)
    {
        var names = new Stack<string>();
        Transform? current = transform;
        while (current is not null)
        {
            names.Push(Segment(current));
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static string Segment(Transform transform) => $"{transform.name}[{transform.GetSiblingIndex()}]";

    private static Vector3Snapshot Vector(Vector3 value) => new(value.x, value.y, value.z);
    private static QuaternionSnapshot Quaternion(Quaternion value) => new(value.x, value.y, value.z, value.w);
    private static long RealtimeMilliseconds() => (long)Math.Round(Time.realtimeSinceStartupAsDouble * 1000);
    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    [HideFromIl2Cpp]
    private async void FlushDiagnostics()
    {
        try
        {
            await _diagnostics!.WriteCoverageAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log?.LogWarning($"Could not write captured event results: {exception.Message}");
        }
    }

    private readonly record struct AnimatorSample(
        string AnimationId,
        double NormalizedTime,
        double ClipLengthSeconds,
        bool IsLooping);
}
