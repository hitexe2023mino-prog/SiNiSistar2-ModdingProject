using System.Globalization;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Edi.Core;
using SiNiSistar2.Manager;
using SiNiSistar2.Manager.Gallery;
using SiNiSistar2.Obj;
using SiNiSistar2.UI.Gallery;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SiNiSistar2.Edi.Plugin;

/// <summary>
/// Detects trigger transitions and feeds the catalog. Per-frame Transform and Animator capture was
/// withdrawn: the target build has no Humanoid avatar and no bones, so there is nothing to measure
/// (SPEC001 6.4, 付録C).
/// </summary>
public sealed class RuntimeObserver : MonoBehaviour
{
    private readonly Dictionary<string, string> _visibleTexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unreadableTakes = new(StringComparer.Ordinal);
    private PlaybackCoordinator? _coordinator;
    private DiagnosticRecorder? _diagnostics;
    private EventCaptureTracker? _capture;
    private AnimationSessionWriter? _session;
    private TriggerCatalog? _catalog;
    private LiveTriggerState? _live;
    private ManualLogSource? _log;
    private EventObservation? _currentEvent;
    private string[] _activeStatuses = Array.Empty<string>();
    private string _eventInstanceId = string.Empty;
    private string? _galleryActorDisplayName;
    private int _enumeratedTakePlayerId;
    private int _enumeratedCategoryId;
    private bool _actorFallbackLogged;
    private float _pollInterval;
    private float _nextPollAt;
    private float _nextCoverageAt;
    private float _nextTextAt;
    private bool _wasPaused;
    private bool _faultLogged;
    private bool _catalogDirty;

    public RuntimeObserver(IntPtr pointer)
        : base(pointer)
    {
    }

    [HideFromIl2Cpp]
    public void Configure(
        PlaybackCoordinator coordinator,
        DiagnosticRecorder diagnostics,
        AnimationSessionWriter session,
        TriggerCatalog catalog,
        LiveTriggerState live,
        float pollInterval,
        ManualLogSource log)
    {
        _coordinator = coordinator;
        _diagnostics = diagnostics;
        _capture = new EventCaptureTracker(diagnostics);
        _session = session;
        _catalog = catalog;
        _live = live;
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

    public void Shutdown()
    {
        _capture?.Observe(null);
        _currentEvent = null;
        PublishLive(null);
        _coordinator?.SetInactive();
        if (_diagnostics is not null)
        {
            FlushDiagnostics();
        }
    }

    public void OnApplicationQuit() => Shutdown();

    private void Poll()
    {
        // The game logs "マネージャーアクセス禁止タイミング" if any manager is read during scene
        // teardown or setup, so its own gate must precede every ManagerList access — including the
        // gallery one below.
        if (ManagerList.IsForbiddenManagerAccessState
            || !ManagerList.HasCompletedFirstInitialize
            || ManagerList.Instance is null)
        {
            SetNoEvent("manager-access-forbidden");
            return;
        }

        // The gallery runs from its own UI root and does not require the gameplay player object,
        // so it is evaluated before the gameplay guards. Testing it afterwards made the gallery
        // branch unreachable and produced sessions with no gallery samples at all. HasDoneSceneSetUp
        // stays a gameplay-only precondition so the gallery is not gated on it again.
        EventObservation? observation = ObserveGallery();
        if (observation is null)
        {
            if (!ManagerList.HasDoneSceneSetUp)
            {
                SetNoEvent("scene-setup-incomplete");
                return;
            }

            ObjectManager? objects = ManagerList.Object;
            Lelia? lelia = objects?.Lelia;
            PlayerStatusManager? playerStatus = ManagerList.PlayerStatus;
            if (objects is null || lelia is null || playerStatus?.AbnormalList is null)
            {
                SetNoEvent("gameplay-objects-unavailable");
                return;
            }

            _activeStatuses = GetActiveStatuses(playerStatus.AbnormalList);
            _coordinator!.SetGameplayActive(_activeStatuses);
            observation = ObserveGameplay(objects, lelia);
        }
        else
        {
            _coordinator!.SetGameplayActive(_activeStatuses);
        }

        EventObservation? previous = _currentEvent;
        bool changed = previous?.Key != observation?.Key;
        if (changed)
        {
            _eventInstanceId = observation is null ? string.Empty : Guid.NewGuid().ToString("N");
            RecordTransition(previous, observation);
        }

        _currentEvent = observation;
        PublishLive(observation);
        if (observation is not null)
        {
            RegisterObserved(observation);
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

    /// <summary>
    /// Reads the gallery take player. The take array is the game's own stage list, so every stage
    /// is catalogued on arrival even before the user plays it (FR-032).
    /// </summary>
    private EventObservation? ObserveGallery()
    {
        GalleryManager? gallery = ManagerList.Gallery;
        if (gallery is null)
        {
            return null;
        }

        // The gallery's own category data names every enemy and take, so the whole catalog can be
        // built from it without playing anything (FR-032).
        CategoryData? category = gallery.GalleryUI?.CharacterSelectUI?.CategoryData;
        EnumerateCategory(category);

        GaTakePlayer? takePlayer = gallery.CurrentTakePlayer;
        if (takePlayer is null)
        {
            _enumeratedTakePlayerId = 0;
            return null;
        }

        (string actorId, string? actorDisplayName) = ResolveGalleryActor(gallery, takePlayer);
        _galleryActorDisplayName = actorDisplayName;
        EnumerateTakes(takePlayer, actorId, actorDisplayName);

        AnimationTakeData? take = takePlayer.PlayingTakeData;
        if (take is null)
        {
            return null;
        }

        string stageId = ResolveStageId(takePlayer, take);

        // An EventPlayer take is a scripted performance, not an animator state: it legitimately has
        // no animator and no clip. Boss and defeat performances use this, which is why they were
        // reported as "animator-null" and never captured. Identify them by the take itself.
        if (take.m_PlayType == PlayType.EventPlayer)
        {
            return CreateObservation(
                "gallery",
                actorId,
                new AnimatorSample(NonEmpty(take.m_TakeName, stageId), 0, 0, take.m_IsAnimatorLoop),
                take.m_IsAnimatorLoop ? "loop" : "reaction",
                stageId);
        }

        if (!TryReadAnimator(take.m_Animator, out AnimatorSample sample, out string failure))
        {
            // An Animator take that yields nothing is a real gap, so it stays diagnosable.
            ReportUnreadableTake(NonEmpty(take.m_TakeName, "(unnamed)"), actorId, failure);
            return null;
        }

        return CreateObservation(
            "gallery",
            actorId,
            sample,
            take.m_IsAnimatorLoop ? "loop" : "reaction",
            stageId);
    }

    /// <summary>
    /// Resolves the enemy the gallery is showing. <see cref="EnemyData.GalleryEnemyID"/> is the
    /// same stable identifier the `hold` context already uses, so an enemy has one actor id in
    /// both contexts. Falls back to the shared take-name prefix when the UI chain is unavailable.
    /// </summary>
    [HideFromIl2Cpp]
    private (string ActorId, string? DisplayName) ResolveGalleryActor(
        GalleryManager gallery,
        GaTakePlayer takePlayer)
    {
        EnemyData? viewed = ViewedEnemy(gallery);
        if (viewed is not null)
        {
            return (viewed.GalleryEnemyID.ToString(), NullIfBlank(viewed.SelectText));
        }

        // Guessing from the loaded take names produced confidently wrong answers (外なる者の呪い
        // was reported as 巨大豚) because a take player covers a whole background scene, which
        // several enemies share. An unidentified actor is now labelled as such instead: a visibly
        // suspect row beats a plausible but wrong attribution, and the prefix keeps distinct
        // enemies from collapsing into one row.
        string fallback = $"unidentified:{ResolveGalleryActorIdFromTakes(takePlayer)}";
        if (!_actorFallbackLogged)
        {
            _actorFallbackLogged = true;
            _session?.Enqueue(
                "capture-warning",
                new
                {
                    category = "gallery-actor-identity",
                    reason = "GalleryUI.ButtonGuideUI.EnemyData was unavailable, so the viewed enemy "
                        + "could not be identified and the actor id is marked unidentified.",
                    fallback,
                },
                RealtimeMilliseconds(),
                Time.frameCount);
            _log?.LogWarning(
                $"The gallery enemy could not be identified; entries are recorded as '{fallback}'.");
        }

        return (fallback, null);
    }

    /// <summary>
    /// The enemy the gallery viewer is showing, taken from the UI that draws its tab bar. This is
    /// the game's own answer, so it does not depend on any index or name-matching assumption.
    /// </summary>
    [HideFromIl2Cpp]
    private static EnemyData? ViewedEnemy(GalleryManager gallery)
    {
        try
        {
            EnemyData? enemy = gallery.GalleryUI?.ButtonGuideUI?.EnemyData;
            return enemy is null || enemy.WasCollected ? null : enemy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Registers every enemy and take the gallery knows about, so unreached stages appear with
    /// their real names instead of only after being played.
    /// </summary>
    [HideFromIl2Cpp]
    private void EnumerateCategory(CategoryData? category)
    {
        var enemies = category?.m_EnemyDataArray;
        if (enemies is null)
        {
            return;
        }

        int categoryId = category!.GetInstanceID();
        if (categoryId == _enumeratedCategoryId)
        {
            return;
        }

        _enumeratedCategoryId = categoryId;
        var added = new List<object>();
        for (var enemyIndex = 0; enemyIndex < enemies.Length; enemyIndex++)
        {
            EnemyData? enemy = enemies[enemyIndex];
            var takes = enemy?.m_AnimationTakeArray;
            if (enemy is null || takes is null)
            {
                continue;
            }

            string actorId = enemy.GalleryEnemyID.ToString();
            string? actorDisplayName = NullIfBlank(enemy.SelectText);
            for (var takeIndex = 0; takeIndex < takes.Length; takeIndex++)
            {
                AnimationTake? take = takes[takeIndex];
                if (take is null)
                {
                    continue;
                }

                string stageId = NonEmpty(
                    take.m_TakeName,
                    takeIndex.ToString(CultureInfo.InvariantCulture));

                // Neither the clip nor whether it loops is known until the take plays, so the
                // phase is left at the non-looping default and corrected on first observation.
                var key = new EventKey("gallery", actorId, EventKey.UnobservedAnimationId, "reaction", stageId);
                // The in-game viewer selects stages by number, so the position in the take array
                // and the game's own SelectID are what let a catalog row be matched on screen.
                if (_catalog!.Register(TriggerCatalogEntry.Create(
                        key,
                        null,
                        null,
                        TriggerSources.StaticEnumeration,
                        NullIfBlank(take.SelectText) ?? stageId,
                        null,
                        DateTimeOffset.UtcNow,
                        actorDisplayName,
                        take.SelectID,
                        takeIndex)))
                {
                    added.Add(new { actorId, actorDisplayName, stageId, number = take.SelectID, index = takeIndex });
                }
            }
        }

        if (added.Count > 0)
        {
            _catalogDirty = true;
            _session?.Enqueue(
                "catalog-update",
                new { source = TriggerSources.StaticEnumeration, scope = "gallery-category", stages = added },
                RealtimeMilliseconds(),
                Time.frameCount);
        }
    }

    private EventObservation? ObserveGameplay(ObjectManager objects, Lelia lelia)
    {
        if (lelia.IsHold && lelia.Bind?.BinderEnemy is { } binder
            && TryReadAnimator(lelia.m_Animator, out AnimatorSample holdSample))
        {
            // No general hold state machine is exposed by this build, so the animator state name
            // is the stage name (SPEC001 3章 stageId, FR-033 degradation).
            return CreateObservation(
                "hold",
                binder.GalleryEnemyID.ToString(),
                holdSample,
                "loop",
                holdSample.AnimationId);
        }

        // Game-over and cinematic reactions play a single animation, so they are single-stage
        // triggers and keep the default stage id (SPEC001 3章).
        if (lelia.IsHP0 && TryReadAnimator(lelia.m_Animator, out AnimatorSample gameOverSample))
        {
            string actor = lelia.Bind?.BinderEnemy?.GalleryEnemyID.ToString() ?? "lelia";
            return CreateObservation("game-over", actor, gameOverSample, "reaction", EventKey.DefaultStageId);
        }

        if (objects.IsCinematicEvent && TryReadAnimator(lelia.m_Animator, out AnimatorSample eventSample))
        {
            return CreateObservation(
                "scripted-event",
                SceneManager.GetActiveScene().name,
                eventSample,
                "reaction",
                EventKey.DefaultStageId);
        }

        return null;
    }

    /// <summary>
    /// Fallback identity. Every take player component is named "Root", so its own name cannot
    /// identify the enemy. Take names are prefixed with the enemy (for example
    /// "VillagerRegion_HoldDown"), so the shared prefix is used instead.
    /// </summary>
    [HideFromIl2Cpp]
    private static string ResolveGalleryActorIdFromTakes(GaTakePlayer takePlayer)
    {
        var takes = takePlayer.m_TakeDataArray;
        string? shared = null;
        if (takes is not null)
        {
            for (var index = 0; index < takes.Length; index++)
            {
                string? name = takes[index]?.m_TakeName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                int separator = name.IndexOf('_');
                string prefix = separator > 0 ? name[..separator] : name;
                if (shared is null)
                {
                    shared = prefix;
                }
                else if (!string.Equals(shared, prefix, StringComparison.Ordinal))
                {
                    shared = null;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(shared))
        {
            return shared;
        }

        string path = BuildAbsolutePath(takePlayer.transform);
        return NonEmpty(path, SceneManager.GetActiveScene().name);
    }

    [HideFromIl2Cpp]
    private void EnumerateTakes(GaTakePlayer takePlayer, string actorId, string? actorDisplayName)
    {
        int instanceId = takePlayer.GetInstanceID();
        if (instanceId == _enumeratedTakePlayerId)
        {
            return;
        }

        _enumeratedTakePlayerId = instanceId;
        var takes = takePlayer.m_TakeDataArray;
        if (takes is null)
        {
            _session?.Enqueue(
                "capture-warning",
                new
                {
                    category = "stage-enumeration",
                    actorId,
                    reason = "The gallery take player exposed no take array for this build.",
                },
                RealtimeMilliseconds(),
                Time.frameCount);
            return;
        }

        var added = new List<object>();
        for (var index = 0; index < takes.Length; index++)
        {
            AnimationTakeData? take = takes[index];
            if (take is null)
            {
                continue;
            }

            string stageId = NonEmpty(take.m_TakeName, index.ToString(CultureInfo.InvariantCulture));

            // The stage array names the stage but not the clips it will queue, so this is a
            // placeholder. TriggerCatalog retires it when the stage is first observed.
            var key = new EventKey(
                "gallery",
                actorId,
                EventKey.UnobservedAnimationId,
                take.m_IsAnimatorLoop ? "loop" : "reaction",
                stageId);

            // The clip length is only readable once the animator plays, so it stays absent here.
            if (_catalog!.Register(TriggerCatalogEntry.Create(
                    key,
                    null,
                    take.m_IsAnimatorLoop,
                    TriggerSources.StaticEnumeration,
                    NullIfBlank(take.m_TakeName) ?? stageId,
                    SceneManager.GetActiveScene().name,
                    DateTimeOffset.UtcNow,
                    actorDisplayName,
                    stageIndex: index)))
            {
                added.Add(new { stageId, index, isLooping = take.m_IsAnimatorLoop });
            }
        }

        if (added.Count > 0)
        {
            _catalogDirty = true;
            _session?.Enqueue(
                "catalog-update",
                new { source = TriggerSources.StaticEnumeration, actorId, stages = added },
                RealtimeMilliseconds(),
                Time.frameCount);
        }
    }

    [HideFromIl2Cpp]
    private static string ResolveStageId(GaTakePlayer takePlayer, AnimationTakeData take)
    {
        if (!string.IsNullOrWhiteSpace(take.m_TakeName))
        {
            return take.m_TakeName;
        }

        var takes = takePlayer.m_TakeDataArray;
        if (takes is not null)
        {
            for (var index = 0; index < takes.Length; index++)
            {
                if (takes[index] is { } candidate && candidate.Pointer == take.Pointer)
                {
                    return index.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return EventKey.DefaultStageId;
    }

    [HideFromIl2Cpp]
    private void RegisterObserved(EventObservation observation)
    {
        if (_catalog!.Register(TriggerCatalogEntry.Create(
                observation.Key,
                // An EventPlayer take has no animator, so its length is reported as zero. Storing
                // that as a measured value made it permanent (MergeWith only fills in nulls) and
                // silently disabled the loop-length check for the stage, so unknown stays null.
                observation.ClipLengthSeconds > 0 ? observation.ClipLengthSeconds : null,
                observation.IsLooping,
                TriggerSources.Observed,
                // An observation cannot know the game's own stage label; it is inherited from the
                // enumerated placeholder, and the GUI falls back to the stage id if there is none.
                null,
                observation.SceneName,
                DateTimeOffset.UtcNow,
                observation.Key.Context == "gallery" ? _galleryActorDisplayName : null)))
        {
            _catalogDirty = true;
            _session?.Enqueue(
                "catalog-update",
                new { source = TriggerSources.Observed, key = observation.Key },
                RealtimeMilliseconds(),
                Time.frameCount);
        }
    }

    /// <summary>
    /// Records a take that is playing but yields no readable animator, once per take, so an event
    /// that never reaches the catalog leaves evidence instead of disappearing silently.
    /// </summary>
    [HideFromIl2Cpp]
    private void ReportUnreadableTake(string takeName, string actorId, string failure)
    {
        if (!_unreadableTakes.Add($"{actorId}\n{takeName}\n{failure}"))
        {
            return;
        }

        _session?.Enqueue(
            "capture-warning",
            new
            {
                category = "take-animator-unreadable",
                actorId,
                takeName,
                reason = failure,
            },
            RealtimeMilliseconds(),
            Time.frameCount);
        _log?.LogWarning(
            $"Gallery take '{takeName}' ({actorId}) is playing but no clip could be read ({failure}); "
            + "this stage will not be catalogued.");
    }

    /// <summary>Publishes what the game is playing so the GUI can highlight the matching row.</summary>
    [HideFromIl2Cpp]
    private void PublishLive(EventObservation? observation) =>
        _live?.Set(observation is null
            ? null
            : new LiveTrigger(
                observation.Key,
                observation.SceneName,
                observation.NormalizedTime,
                observation.ClipLengthSeconds,
                observation.IsLooping,
                DateTimeOffset.UtcNow));

    [HideFromIl2Cpp]
    private void RecordTransition(EventObservation? previous, EventObservation? current) =>
        _session?.Enqueue(
            "event-transition",
            new TriggerTransitionSnapshot(
                _eventInstanceId,
                previous?.Key,
                current?.Key,
                SceneManager.GetActiveScene().name,
                current?.ClipLengthSeconds,
                current?.IsLooping,
                current?.NormalizedTime,
                Time.timeAsDouble,
                Time.unscaledTimeAsDouble,
                Time.timeScale,
                _activeStatuses),
            RealtimeMilliseconds(),
            Time.frameCount);

    [HideFromIl2Cpp]
    private void SetNoEvent(string reason)
    {
        if (_currentEvent is not null)
        {
            // SetNoEvent previously returned without a record, which made it impossible to tell an
            // ended trigger from an observation the guards never reached.
            RecordTransition(_currentEvent, null);
            _session?.Enqueue(
                "capture-warning",
                new { category = "observation-suspended", reason },
                RealtimeMilliseconds(),
                Time.frameCount);
            _coordinator!.EndEvent(_currentEvent.Key);
        }

        if (_capture!.Observe(null))
        {
            FlushDiagnostics();
        }

        _currentEvent = null;
        PublishLive(null);
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

    private static EventObservation CreateObservation(
        string context,
        string actor,
        AnimatorSample sample,
        string phase,
        string stageId) =>
        new(
            new EventKey(context, actor, sample.AnimationId, phase, stageId),
            sample.NormalizedTime,
            sample.ClipLengthSeconds,
            sample.IsLooping,
            SceneManager.GetActiveScene().name,
            DateTimeOffset.UtcNow);

    /// <summary>
    /// Reads the clip an animator is playing. Every layer is scanned, not just layer 0: defeat and
    /// boss performances are driven from a higher layer, so a layer-0-only read reported "nothing
    /// playing" and those events were never captured. The heaviest-weighted layer wins.
    /// </summary>
    private static bool TryReadAnimator(Animator? animator, out AnimatorSample sample) =>
        TryReadAnimator(animator, out sample, out _);

    private static bool TryReadAnimator(Animator? animator, out AnimatorSample sample, out string failure)
    {
        sample = default;
        if (animator is null || animator.WasCollected)
        {
            failure = "animator-null";
            return false;
        }

        // A destroyed Unity object is not a C# null but throws on every member access, which took
        // the whole observer down through the fail-closed path. Treat it as simply not readable.
        try
        {
            return TryReadLiveAnimator(animator, out sample, out failure);
        }
        catch (Exception)
        {
            failure = "animator-destroyed";
            return false;
        }
    }

    private static bool TryReadLiveAnimator(Animator animator, out AnimatorSample sample, out string failure)
    {
        sample = default;
        if (!animator.isActiveAndEnabled)
        {
            failure = "animator-inactive";
            return false;
        }

        if (animator.layerCount == 0)
        {
            failure = "no-layers";
            return false;
        }

        // Layer 0 keeps priority so every case that already worked is unchanged; the scan below
        // only rescues animators that previously reported nothing at all.
        if (TryReadLayer(animator, 0, out sample))
        {
            failure = string.Empty;
            return true;
        }

        var found = false;
        float bestWeight = 0f;
        for (var layer = 1; layer < animator.layerCount; layer++)
        {
            float weight = animator.GetLayerWeight(layer);
            if (weight <= bestWeight || !TryReadLayer(animator, layer, out AnimatorSample candidate))
            {
                continue;
            }

            sample = candidate;
            bestWeight = weight;
            found = true;
        }

        failure = found ? string.Empty : "no-clip-on-any-layer";
        return found;
    }

    private static bool TryReadLayer(Animator animator, int layer, out AnimatorSample sample)
    {
        sample = default;
        var clips = animator.GetCurrentAnimatorClipInfo(layer);
        if (clips.Length == 0 || clips[0].clip is null)
        {
            return false;
        }

        AnimationClip clip = clips[0].clip;
        if (string.IsNullOrWhiteSpace(clip.name))
        {
            return false;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
        sample = new AnimatorSample(clip.name, state.normalizedTime, clip.length, clip.isLooping);
        return true;
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

    private static string BuildAbsolutePath(Transform transform)
    {
        var names = new Stack<string>();
        Transform? current = transform;
        while (current is not null)
        {
            names.Push($"{current.name}[{current.GetSiblingIndex()}]");
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static long RealtimeMilliseconds() => (long)Math.Round(Time.realtimeSinceStartupAsDouble * 1000);

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    [HideFromIl2Cpp]
    private async void FlushDiagnostics()
    {
        try
        {
            await _diagnostics!.WriteCoverageAsync().ConfigureAwait(false);
            if (_catalogDirty)
            {
                _catalogDirty = false;
                await _catalog!.SaveAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _log?.LogWarning($"Could not write captured trigger results: {exception.Message}");
        }
    }

    private readonly record struct AnimatorSample(
        string AnimationId,
        double NormalizedTime,
        double ClipLengthSeconds,
        bool IsLooping);
}
