using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Manager;
using SiNiSistar2.Manager.Gallery;
using SiNiSistar2.Drama;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Holds the HP0 suppression in place while the player is bound, consumes climaxes, and records the
/// remaining 付録A measurements.
///
/// The suppression is a contribution on the game's own <c>RemainHp1Msv</c>, so the MOD never writes
/// HP and never blocks damage. What it removes is only the moment HP would reach zero
/// (SPEC003 5.1, DEC-201).
/// </summary>
public sealed class PleasureObserver : MonoBehaviour
{
    private bool _wasBound;
    private bool _faultLogged;
    private bool _selfChecked;
    private bool _drawFaultLogged;
    private bool _gameplayActive;
    private bool _wasDead;
    private bool _editing;
    private PleasureOverlayLayout? _layoutBeforeEdit;
    private int _editingElement;
    private Texture2D? _liquid;
    private Texture2D? _cross;
    private float _liquidFill = -1f;
    private float _liquidPhase;
    private double _liquidBuiltAt;
    private int _crossNotches = -1;
    private int _liquidResolution = 96;
    private bool _crossBroken;
    private int _lastSelectId = int.MinValue;
    private string? _lastSaveFile;
    private double _lastFrameTime;
    private float _lastMaxDurability;
    private readonly EnemyCatalogEditor _enemyEditor = new();
    private bool _cureSurfaceReported;
    private bool _breastReported;
    private bool _breastSuperReported;
    private bool _breastSuperRequested;
    private KeyCode _milkingKey = KeyCode.None;
    private static bool _eventWasRunning;
    private double _swellingOverlapSince;
    private double _breastSuperLoadAsked;
    private double _breastSuperWaitLogged;
    private bool _interactionLocked;
    private Texture2D? _milkVessel;
    private float _milkFill = -1f;
    private float _milkPhase;
    private double _milkBuiltAt;


    public PleasureObserver(IntPtr pointer)
        : base(pointer)
    {
    }

    public void Update()
    {
        try
        {
            ProbeGalleryTake();
            Poll();
            _faultLogged = false;
        }
        catch (Exception exception)
        {
            Suspend();
            if (!_faultLogged)
            {
                _faultLogged = true;
                PleasureRuntime.Log?.LogWarning(
                    $"Pleasure observation failed and backed out; it will retry: {exception}");
            }
        }
    }

    /// <summary>
    /// Watches what the player plays while the gallery runs a take (SPEC003 付録A A-27).
    ///
    /// Outside <see cref="Poll"/> because it has to run in the gallery, where the rest of the
    /// observer deliberately does not: the gallery forces statuses onto the player's list, and
    /// counting those once left the escalation applied outside it. Reading an animator does none
    /// of that.
    /// </summary>
    [HideFromIl2Cpp]
    private static void ProbeGalleryTake()
    {
        // The same gate Poll uses. Reading a manager while the game has closed access logs an
        // error of its own, once per frame, and moving this probe outside Poll took its gate off
        // with it: 57 of them arrived during one scene load.
        if (!PleasureRuntime.Profile.ProbeMeasurements
            || ManagerList.IsForbiddenManagerAccessState
            || !ManagerList.HasCompletedFirstInitialize
            || ManagerList.Instance is null
            || !ManagerList.HasDoneSceneSetUp)
        {
            return;
        }

        try
        {
            // The milking scene runs by itself in the field when a swollen player reaches the right
            // place, so that is where its clip can be learned (付録A A-32). One animator, no walk.
            //
            // Widened past IsCinematicEvent. Walking a swollen player around four maps produced
            // only ordinary clips, and the milking scene did not fire — but there is no reading
            // that says it would have been flagged cinematic if it had. Every distinct clip the
            // player plays while the escalated swelling is worn is recorded instead, so the one
            // that matters cannot be missed by having guessed the wrong flag. Still one animator
            // and no walk (DEC-236), and distinct names only, of which a swollen player has few.
            ObjectManager? objects = ManagerList.Object;
            bool swollen = false;
            try
            {
                swollen = PleasureRuntime.PlayerAbnormals?.Has(AbnormalType.BreastSuper) == true;
            }
            catch (Exception)
            {
                swollen = false;
            }

            bool cinematic = objects is not null && objects.IsCinematicEvent;
            if (cinematic && !_eventWasRunning)
            {
                MilkingAnimation.ProbeEventObjects(SceneManager.GetActiveScene().name);
            }

            _eventWasRunning = cinematic;

            if (swollen || cinematic)
            {
                MilkingAnimation.ProbeEvent(SceneManager.GetActiveScene().name, swollen);
            }

            GaTakePlayer? player = ManagerList.Gallery?.CurrentTakePlayer;
            AnimationTakeData? take = player?.PlayingTakeData;
            if (take is null)
            {
                return;
            }

            string name = take.m_TakeName;
            MilkingAnimation.ProbeGallery(take, string.IsNullOrEmpty(name) ? "(unnamed)" : name);
        }
        catch (Exception)
        {
            // The gallery is absent for most of a session, and that is not a fault.
        }
    }

    /// <summary>
    /// Draws the gauge, sensitivity and climax count.
    ///
    /// Immediate-mode GUI rather than a Canvas: it needs no prefab, no game asset and no scene
    /// object, which is what FR-212 asks for, and it cannot disturb the game's own UI hierarchy or
    /// the hold UI that SPEC002 tints. Nothing here touches <c>Time.timeScale</c>.
    /// </summary>
    public void OnGUI()
    {
        // The enemy screen is not part of the overlay and is reachable whether or not the overlay
        // is drawn: it says which enemies raise pleasure at all, which matters even to someone who
        // has turned the gauge off.
        HandleEnemyEditor();

        if (PleasureRuntime.Profile.ShowOverlay && !_enemyEditor.IsOpen)
        {
            HandleLayoutEditor();
        }

        try
        {
            DrawOverlay();
            _enemyEditor.Draw();
            _drawFaultLogged = false;
        }
        catch (Exception exception)
        {
            // Never takes the run down (FR-213), but it is reported. Swallowing this silently is
            // what made "the overlay does not appear" impossible to diagnose: the mechanism was
            // running correctly and only the drawing was failing, with nothing to say so.
            if (!_drawFaultLogged)
            {
                _drawFaultLogged = true;
                PleasureRuntime.Log?.LogError($"The overlay could not be drawn: {exception}");
            }
        }
    }

    /// <summary>
    /// The gauge, the cross and the climax flash.
    ///
    /// Only while gameplay is actually running. The title screen, the loading screens and the menus
    /// have no player to report on, and a gauge floating over them is plainly wrong. While the
    /// layout is being edited it is drawn regardless, so there is something to aim.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawOverlay()
    {
        if (!PleasureRuntime.Profile.ShowOverlay || (!_gameplayActive && !_editing))
        {
            return;
        }

        DrawGauge();
        DrawClimaxFlash();
        DrawMilk();
        DrawTransitionFade();
        DrawEditorChrome();
    }

    public void OnApplicationQuit() => Shutdown();

    [HideFromIl2Cpp]
    public void Shutdown()
    {
        Suspend();
        PleasureRuntime.SaveEnemies("shutdown");
        BreastPatches.Reset();
        PleasureRuntime.Reset();
    }

    private void Poll()
    {
        if (ManagerList.IsForbiddenManagerAccessState
            || !ManagerList.HasCompletedFirstInitialize
            || ManagerList.Instance is null
            || !ManagerList.HasDoneSceneSetUp)
        {
            Suspend();
            return;
        }

        ObjectManager? objects = ManagerList.Object;
        Lelia? lelia = objects?.Lelia;
        PlayerStatusManager? status = ManagerList.PlayerStatus;
        if (lelia is null || status is null)
        {
            Suspend();
            return;
        }

        PleasureRuntime.PlayerAbnormals = status.AbnormalList;

        bool bound = lelia.IsHold;
        PleasureRuntime.IsBound = bound;

        // Sexual attacks otherwise only happen while bound, but some defeat performances keep
        // delivering them, and the player is in HP0 rather than a hold for those (SPEC003 5.2).
        bool dead = lelia.IsHP0;
        PleasureRuntime.IsDefeatPerformance = dead;

        // Coming back from a defeat is a new run. Patching the game's own load hook looked like the
        // direct way to detect this, but Harmony's wrapper for
        // PlayerStatusManager.OnAfterLoadMainSaveData threw on every call and took the game's load
        // path down with it, so the transition is observed instead of intercepted.
        if (_wasDead && !dead)
        {
            PleasureRuntime.ReloadCurrentSlot("revived after a defeat");
        }

        _wasDead = dead;
        _lastMaxDurability = status.m_MaxDurability;
        PleasureRuntime.BinderEnemyId = ResolveBinderId(lelia);
        RecordSighting(PleasureRuntime.BinderEnemyId);

        _gameplayActive = true;
        PleasureRuntime.GameplayStarted = true;
        PleasureRuntime.IsSwollen = IsSwollen(status);
        ReportInteractionLock(status);
        ReportSelfCheck(status);
        ReportBreastCureSurface(status);
        ApplyPendingBreastSuper(status);
        EnforceSingleSwelling(status);
        UpdateHp0Suppression(lelia, bound);
        ConsumeClimax(status);
        DecayWhenFree(bound);
        ProbeSaveSlot();

        _wasBound = bound;
    }

    /// <summary>
    /// Registers the HP0 suppression while bound and releases it otherwise. Once the climax limit
    /// has been reached the contribution is deliberately withheld, which is how the run ends: HP
    /// falls to zero and the game's own defeat path takes over unchanged (SPEC003 5.5, DEC-209).
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateHp0Suppression(Lelia lelia, bool bound)
    {
        if (!PleasureRuntime.Profile.SuppressHp0WhileBound)
        {
            return;
        }

        bool atLimit = PleasureRuntime.Profile.Climax.GameOverEnabled && IsAtClimaxLimit();
        bool wanted = bound && !atLimit;

        if (wanted == PleasureRuntime.Ledger.IsOpen(PleasureRuntime.RemainHp1Key))
        {
            return;
        }

        if (!wanted)
        {
            string? failure = PleasureRuntime.Ledger.Release(PleasureRuntime.RemainHp1Key);
            if (failure is not null)
            {
                PleasureRuntime.Log?.LogWarning($"Could not release the HP0 suppression: {failure}");
            }
            else if (atLimit)
            {
                PleasureRuntime.Log?.LogInfo(
                    "Climax limit reached; HP0 suppression withheld. The hold is now fatal through "
                    + "the game's own defeat path.");
            }

            return;
        }

        StatusCondition? condition = lelia.StatusCondition;
        MultiSettingValue<bool>? remain = condition?.RemainHp1Msv;
        Il2CppSystem.Object? key = PleasureRuntime.ContributionKey;
        if (condition is null || remain is null || key is null)
        {
            PleasureRuntime.Probe(
                "remain-hp1-unavailable",
                "A-1 caution: RemainHp1Msv could not be resolved, so the HP0 defeat is NOT removed.");
            return;
        }

        remain.ResitValue(key, true);
        PleasureRuntime.Ledger.Register(
            PleasureRuntime.RemainHp1Key,
            () => remain.ReleaseValue(key));
        PleasureRuntime.Probe(
            "remain-hp1-registered",
            $"A-1: HP0 suppression registered while bound; RemainHp1 now reads {condition.RemainHp1}.");
    }

    [HideFromIl2Cpp]
    private static bool IsAtClimaxLimit()
    {
        ClimaxTuning tuning = PleasureRuntime.Profile.Climax;
        float durability = 0f;
        try
        {
            durability = ManagerList.PlayerStatus?.m_MaxDurability ?? 0f;
        }
        catch (Exception)
        {
            // Falls through to the base limit, which FR-214 requires rather than collapsing to 0.
        }

        int limit = ClimaxLimit.Compute(tuning.LimitBase, tuning.LimitPerDurability, durability);
        return PleasureRuntime.Climaxes.IsAtLimit(limit);
    }

    /// <summary>
    /// Applies the <c>BreastSuper</c> escalation decided by the status patch (SPEC003 5.8, FR-221).
    ///
    /// Through <c>AddAbnormal</c>, the game's own path, so the status is a real one:
    /// <c>AbnormalList.Has</c> returns true, it is written into the game's save as an
    /// <c>AbnormalSaveData</c> entry, and SPEC001 sees it like any other. Nothing here fabricates a
    /// state the game would disagree with.
    /// </summary>
    [HideFromIl2Cpp]
    private void ApplyPendingBreastSuper(PlayerStatusManager status)
    {
        if (!PleasureRuntime.PendingBreastSuper)
        {
            return;
        }

        // Held while the game is frozen. Opening the item menu stops the game, and MonoBehaviour
        // Update keeps running at a time scale of zero, so without this the status would be added
        // underneath an open menu — to a UI that has already drawn its list and is not going to
        // redraw it. The flag survives, so it lands as soon as play resumes.
        if (Time.timeScale <= 0f)
        {
            PleasureRuntime.Probe(
                "breastsuper-deferred",
                "The BreastSuper escalation is waiting for the game to resume; the time scale is 0, "
                + "which is what an open menu looks like.");
            return;
        }

        AbnormalList? abnormals = status.AbnormalList;
        if (abnormals is null)
        {
            return;
        }

        if (abnormals.Has(AbnormalType.BreastSuper))
        {
            PleasureRuntime.PendingBreastSuper = false;
            return;
        }

        // Added through the AbnormalData overload, which is the one the game itself uses everywhere.
        // The AbnormalType overload has to resolve the data first, and when BreastSuper had not been
        // loaded it resolved to nothing and returned quietly: the escalation reported success while
        // the status stayed at level 0 and Has kept saying it was absent.
        AbnormalManager? manager = ManagerList.Abnormal;
        AbnormalData? data = null;
        if (manager is null || !manager.TryGetData(AbnormalType.BreastSuper, out data) || data is null)
        {
            // The flag is deliberately left standing. Loading is asynchronous, so "not yet" must not
            // become "never" — the escalation was earned and has to land once the data arrives.
            RequestBreastSuperData(manager);
            return;
        }

        // Applied before the removal. The other order leaves a frame with neither status, and the
        // body and portrait are driven from the status list.
        abnormals.AddAbnormal(data, 1, null);

        bool applied = abnormals.Has(AbnormalType.BreastSuper);
        if (!applied)
        {
            PleasureRuntime.PendingBreastSuper = false;
            PleasureRuntime.Log?.LogError(
                "BreastSuper was applied through AbnormalList.AddAbnormal(AbnormalData) and "
                + "AbnormalList.Has still reports it absent. The escalation cannot take effect on "
                + "this build; Breast is left in place.");
            return;
        }

        PleasureRuntime.PendingBreastSuper = false;

        // Read before Breast is taken off, and again after, so the portrait's inputs are on record
        // both ways round. The unswollen portrait under BreastSuper could be the escalated status
        // carrying no portrait of its own, or it could be the swelling's portrait having been the
        // one doing the work all along (付録A A-28).
        BreastPatches.ReportAttachedData(abnormals, AbnormalType.BreastSuper);

        if (PleasureRuntime.Profile.BreastSuper.ReplaceBreast)
        {
            abnormals.RemoveAbnormal(AbnormalType.Breast);
            PleasureRuntime.Probe(
                "portrait-after-removing-breast",
                "A-28: with Breast removed and BreastSuper worn, the list reports "
                + $"PhysicalConditionFlag={SafeFlag(abnormals)}.");
        }

        // Full from the moment it lands. The escalation is the body having more than it can hold,
        // so a gauge that started empty there would say the opposite of what just happened — and it
        // would make the escalated status the cheapest one to be rid of.
        PleasureRuntime.Milk?.LoadFrom(1f);

        PleasureRuntime.SuperTimer?.Start();
        BeginTransition();

        // The escalated swelling has its own art and its own body flag, but nothing on this path
        // asks for the portrait to be drawn again (付録A A-28, A-30).
        PortraitRefresh.Refresh("escalation to BreastSuper", AbnormalType.BreastSuper);

        PleasureRuntime.Log?.LogInfo(
            $"Breast escalated to BreastSuper (Breast "
            + $"{(PleasureRuntime.Profile.BreastSuper.ReplaceBreast ? "removed" : "kept")}). "
            + $"Sensitivity {PleasureRuntime.Sensitivity?.Value ?? 0f:F2}.");
    }

    /// <summary>The list's condition flag, or why it could not be read.</summary>
    [HideFromIl2Cpp]
    private static string SafeFlag(AbnormalList list)
    {
        try
        {
            return list.PhysicalConditionFlag.ToString();
        }
        catch (Exception exception)
        {
            return $"(unreadable: {exception.Message})";
        }
    }

    /// <summary>
    /// Covers the moment the status changes with black.
    ///
    /// It does not rebuild anything. An earlier version called
    /// <c>AbnormalList.AllObjectSetUp</c> here, on the reasoning that the game's own "set everything
    /// up from the current status list" would refresh the body without a scene reload. It froze the
    /// game on the next conversation: the cure runs inside a drama, so the rebuild was being driven
    /// synchronously while an event held the screen. Adding and removing a status is something the
    /// game does constantly and refreshes on its own; forcing the rebuild was never established as
    /// necessary and is now known to be harmful (DEC-221).
    /// </summary>
    [HideFromIl2Cpp]
    private void BeginTransition()
    {
        float seconds = PleasureRuntime.Profile.BreastSuper.FadeSeconds;
        if (seconds > 0f)
        {
            PleasureRuntime.TransitionFadeUntil = Time.timeAsDouble + seconds;
        }
    }

    /// <summary>
    /// Reports the game's own "interaction is disabled" answer as it changes (SPEC003 付録A A-24).
    ///
    /// <c>AbnormalManager.IsDisableInteract</c> is the game's rule, not the MOD's: some statuses
    /// stop the player using things. If a save point stops responding while swollen, this says
    /// whether the game decided that or the MOD broke it — a distinction no amount of reading the
    /// MOD's own code can settle.
    /// </summary>
    [HideFromIl2Cpp]
    private void ReportInteractionLock(PlayerStatusManager status)
    {
        bool locked;
        try
        {
            locked = AbnormalManager.IsDisableInteract();
        }
        catch (Exception)
        {
            return;
        }

        if (locked == _interactionLocked)
        {
            return;
        }

        _interactionLocked = locked;
        var names = new List<string>();
        try
        {
            AbnormalList? abnormals = status.AbnormalList;
            if (abnormals is not null)
            {
                foreach (AbnormalType type in new[]
                         {
                             AbnormalType.Breast, AbnormalType.BreastSuper, AbnormalType.Milk,
                             AbnormalType.Parasite, AbnormalType.Blessing_Lost,
                         })
                {
                    if (abnormals.Has(type))
                    {
                        names.Add(type.ToString());
                    }
                }
            }
        }
        catch (Exception)
        {
            names.Add("(statuses unreadable)");
        }

        PleasureRuntime.Log?.LogInfo(
            $"[probe] A-24: AbnormalManager.IsDisableInteract is now {locked}. Statuses: "
            + $"{(names.Count == 0 ? "none of the watched ones" : string.Join(", ", names))}. "
            + "This is the game's own rule about whether things can be used.");
    }

    [HideFromIl2Cpp]
    private static bool IsSwollen(PlayerStatusManager status)
    {
        try
        {
            AbnormalList? abnormals = status.AbnormalList;
            return abnormals is not null
                && (abnormals.Has(AbnormalType.Breast) || abnormals.Has(AbnormalType.BreastSuper));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps <c>Breast</c> and <c>BreastSuper</c> from being worn at once (SPEC003 FR-263).
    ///
    /// The MOD removes <c>Breast</c> as it escalates, but nothing stops the game putting it back:
    /// the item and the events that apply swelling do not know the escalated status exists, so they
    /// add the ordinary one on top of it. That leaves two body overrides describing the same body,
    /// a state nothing in the game was authored for.
    ///
    /// Enforced as a standing invariant rather than at each place that could break it, because the
    /// places that can break it are the game's, not the MOD's, and there is no list of them.
    /// </summary>
    [HideFromIl2Cpp]
    private void EnforceSingleSwelling(PlayerStatusManager status)
    {
        if (!PleasureRuntime.Profile.BreastSuper.ReplaceBreast)
        {
            return;
        }

        // Never inside an event or a hold. Taking a status away while something else holds the
        // screen is what froze the game once already.
        if (Time.timeScale <= 0f || PleasureRuntime.IsBound || BreastPatches.IsGalleryActive())
        {
            return;
        }

        AbnormalList? abnormals = status.AbnormalList;
        if (abnormals is null)
        {
            return;
        }

        try
        {
            if (!abnormals.Has(AbnormalType.BreastSuper) || !abnormals.Has(AbnormalType.Breast))
            {
                _swellingOverlapSince = 0d;
                return;
            }

            // Given a moment before acting, and never more than once a second.
            //
            // Removing a status is not free: the game rebuilds what depends on it, and doing that
            // repeatedly under an interaction is the shape of failure this MOD has already caused
            // twice. An overlap that lasts a frame or two is something the game is in the middle of
            // arranging, not something to fight.
            double now = Time.unscaledTimeAsDouble;
            if (_swellingOverlapSince <= 0d)
            {
                _swellingOverlapSince = now;
                return;
            }

            if (now - _swellingOverlapSince < 1d)
            {
                return;
            }

            _swellingOverlapSince = now;

            abnormals.RemoveAbnormal(AbnormalType.Breast);

            // Absorbed rather than discarded. Deleting it outright made the item that applies
            // swelling look broken while the escalated status was worn: it ran, and nothing
            // happened. Turning the application into milk keeps it meaningful — the swelling was
            // reinforced, so there is more to get through before it can be milked away.
            MilkReservoir? milk = PleasureRuntime.Milk;
            bool absorbed = milk is not null && milk.Fill < 1f;
            milk?.AddFromHit();

            PleasureRuntime.Log?.LogInfo(
                "Breast was re-applied while BreastSuper is worn. The ordinary swelling is removed "
                + $"so the two do not overlap{(absorbed ? $", and the application went into the milk gauge ({milk!.Fill:P0})" : string.Empty)}.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The swellings could not be separated: {exception.Message}");
        }
    }

    /// <summary>
    /// Takes <c>BreastSuper</c> back down to <c>Breast</c>: on the game's own cure, or when the
    /// duration runs out (SPEC003 5.8, FR-253, FR-254).
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateBreastSuperLife(PlayerStatusManager status, double delta)
    {
        AbnormalList? abnormals = status.AbnormalList;
        if (abnormals is null)
        {
            return;
        }

        bool present;
        try
        {
            present = abnormals.Has(AbnormalType.BreastSuper);
        }
        catch (Exception)
        {
            return;
        }

        if (PleasureRuntime.PendingBreastSuperCure)
        {
            // Held until the game is running normally again. The cure happens inside a drama, and
            // taking a status away while an event holds the screen is what took the game down.
            if (Time.timeScale <= 0f || PleasureRuntime.IsBound)
            {
                return;
            }

            PleasureRuntime.PendingBreastSuperCure = false;
            if (present)
            {
                abnormals.RemoveAbnormal(AbnormalType.BreastSuper);
                PortraitRefresh.Refresh("return to Breast", null);
                PleasureRuntime.SuperTimer?.Stop();
                BeginTransition();
                PleasureRuntime.Log?.LogInfo(
                    "BreastSuper was removed along with Breast by the game's own cure.");
            }

            return;
        }

        if (!present)
        {
            PleasureRuntime.SuperTimer?.Stop();
            return;
        }

        BreastSuperTimer? timer = PleasureRuntime.SuperTimer;
        if (timer is null || !timer.HasDuration)
        {
            return;
        }

        timer.Start();
        if (!timer.Tick(delta))
        {
            return;
        }

        // Back to Breast rather than to nothing. Enduring the escalation costs the ordinary
        // swelling, it does not cure it; otherwise waiting would be better than being cured.
        abnormals.RemoveAbnormal(AbnormalType.BreastSuper);
        PortraitRefresh.Refresh("return to Breast", null);
        AbnormalManager? manager = ManagerList.Abnormal;
        AbnormalData? data = null;
        if (manager is not null && manager.TryGetData(AbnormalType.Breast, out data) && data is not null)
        {
            abnormals.AddAbnormal(data, 1, null);
        }

        BeginTransition();
        PleasureRuntime.Log?.LogInfo("BreastSuper subsided back to Breast after its duration.");
    }

    /// <summary>
    /// Reports what the game itself says about curing <c>Breast</c> and <c>BreastSuper</c>
    /// (SPEC003 付録A A-14).
    ///
    /// This is the measurement that decides whether the existing self-milking cure can reach the
    /// escalated status at all. Two values settle it: whether <c>BreastSuper</c> carries the same
    /// <c>PhysicalConditionFlag</c> as <c>Breast</c> — the interaction that offers the cure is
    /// conditioned on something, and a shared flag means it already sees it — and whether the game
    /// marks it curable by Haanja.
    /// </summary>
    [HideFromIl2Cpp]
    private void ReportBreastCureSurface(PlayerStatusManager status)
    {
        if (_cureSurfaceReported)
        {
            return;
        }

        AbnormalManager? manager = ManagerList.Abnormal;
        if (manager is null)
        {
            return;
        }

        RequestBreastSuperLoad(manager);

        // Retried rather than latched on the first attempt. Statuses are loaded on demand, and the
        // first attempt found BreastSuper absent and then never looked again — which read as "it
        // cannot be measured" when it only meant "not yet".
        bool breast = DescribeAbnormal(manager, AbnormalType.Breast, ref _breastReported);
        bool super = DescribeAbnormal(manager, AbnormalType.BreastSuper, ref _breastSuperReported);
        if (breast && super)
        {
            _cureSurfaceReported = true;
        }

        ApplyHaanjaCurableOverride(manager);
    }

    /// <summary>
    /// Makes the game load <c>BreastSuper</c>, and says so while it has not (SPEC003 FR-252).
    ///
    /// <c>PreloadResist</c> only registers a wish: it puts the status on a list the game consults at
    /// its own loading points. Measured on this build, a run whose save never carried the status
    /// never loaded it, and the escalation sat pending for ever while the log said "waiting" exactly
    /// once. Earlier runs looked fine only because their saves already had it.
    ///
    /// <c>LoadAbnormalData</c> is the game's own loader. It is started and not awaited; the retry
    /// that brought us here is the wait.
    /// </summary>
    [HideFromIl2Cpp]
    private void RequestBreastSuperData(AbnormalManager? manager)
    {
        double now = Time.unscaledTimeAsDouble;
        bool report = now - _breastSuperWaitLogged > 5d;
        if (report)
        {
            _breastSuperWaitLogged = now;
        }

        if (manager is null)
        {
            return;
        }

        if (now - _breastSuperLoadAsked > 2d)
        {
            _breastSuperLoadAsked = now;
            try
            {
                manager.LoadAbnormalData(AbnormalType.BreastSuper, ManagerList.RootTokenSource.Token);
            }
            catch (Exception exception)
            {
                if (report)
                {
                    PleasureRuntime.Log?.LogWarning(
                        $"BreastSuper could not be loaded on request: {exception.Message}");
                }

                return;
            }
        }

        if (report)
        {
            PleasureRuntime.Log?.LogInfo(
                "The BreastSuper escalation is waiting for its data; the game has been asked to "
                + "load it. This repeats until it arrives.");
        }
    }

    /// <summary>
    /// Asks the game to load <c>BreastSuper</c>.
    ///
    /// Not only so it can be measured: a status the game has not loaded cannot be applied either, so
    /// without this the escalation would have nothing to escalate to. <c>PreloadResist</c> is the
    /// game's own registration call — the same "resist" spelling as
    /// <c>MultiSettingValue.ResitValue</c> — and it only adds to the preload list.
    /// </summary>
    [HideFromIl2Cpp]
    private void RequestBreastSuperLoad(AbnormalManager manager)
    {
        if (_breastSuperRequested || !PleasureRuntime.Profile.BreastSuper.HasEffect)
        {
            return;
        }

        _breastSuperRequested = true;
        try
        {
            manager.PreloadResist(AbnormalType.BreastSuper);
            PleasureRuntime.Log?.LogInfo("BreastSuper was registered for preloading so it can be applied.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"BreastSuper could not be registered for preloading: {exception.Message}. The "
                + "escalation may fail to apply it.");
        }
    }

    /// <summary>Returns true once the reading has been taken, so the retry can stop.</summary>
    [HideFromIl2Cpp]
    private static bool DescribeAbnormal(AbnormalManager manager, AbnormalType type, ref bool reported)
    {
        if (reported)
        {
            return true;
        }

        try
        {
            AbnormalData? data = null;
            if (!manager.TryGetData(type, out data) || data is null)
            {
                return false;
            }

            reported = true;
            PleasureRuntime.Log?.LogInfo(
                $"[probe] A-14: {type} maxLevel={data.MaxLevel}, haanjaCanCure={data.HaanjaCanCure}, "
                + $"physicalConditionFlag={data.PhysicalConditionFlag}, "
                + $"removeWhenChangeScene={data.m_RemoveWhenChangeScene}, deleteTime={data.m_DeleteTime}, "
                + $"nameID={data.AbnormalNameID}. Note that this is the unattached template: the "
                + "flag and the name are read again once it is actually on the player.");
            return true;
        }
        catch (Exception exception)
        {
            reported = true;
            PleasureRuntime.Log?.LogInfo($"[probe] A-14: {type} could not be read: {exception.Message}");
            return true;
        }
    }

    /// <summary>
    /// Marks <c>BreastSuper</c> curable by Haanja, if asked.
    ///
    /// This is a value on the game's own loaded <c>AbnormalData</c>, so it is recorded in the ledger
    /// and put back on unload. It is off by default: it changes what an existing cure event will do,
    /// and that has to be seen working in game before it can be the shipped behaviour.
    /// </summary>
    [HideFromIl2Cpp]
    private static void ApplyHaanjaCurableOverride(AbnormalManager manager)
    {
        if (!PleasureRuntime.Profile.BreastSuper.MakeHaanjaCurable
            || PleasureRuntime.Ledger.IsOpen(PleasureRuntime.HaanjaCurableKey))
        {
            return;
        }

        try
        {
            AbnormalData? data = null;
            if (!manager.TryGetData(AbnormalType.BreastSuper, out data) || data is null)
            {
                return;
            }

            bool previous = data.m_HaanjaCanCure;
            if (previous)
            {
                PleasureRuntime.Log?.LogInfo(
                    "BreastSuper is already curable by Haanja; no override was needed.");
                return;
            }

            data.m_HaanjaCanCure = true;
            PleasureRuntime.Ledger.Register(
                PleasureRuntime.HaanjaCurableKey,
                () => data.m_HaanjaCanCure = previous);
            PleasureRuntime.Log?.LogInfo(
                "BreastSuper is now marked curable by Haanja. Confirm in game that the cure "
                + "actually completes; this only makes the game's own cure consider it.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"BreastSuper could not be marked curable by Haanja: {exception.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void ConsumeClimax(PlayerStatusManager status)
    {
        if (!PleasureRuntime.PendingClimax)
        {
            return;
        }

        PleasureRuntime.PendingClimax = false;
        PleasureRuntime.Meter?.ConsumeClimax();
        PleasureRuntime.Climaxes.Record();
        PleasureRuntime.Sensitivity?.Add(PleasureRuntime.Profile.Sensitivity.PerClimax);

        PleasureRuntime.ClimaxFlashUntil =
            Time.timeAsDouble + PleasureRuntime.Profile.Climax.OverlaySeconds;

        PleasureRuntime.Log?.LogInfo(
            $"Climax {PleasureRuntime.Climaxes.Count}; sensitivity "
            + $"{PleasureRuntime.Sensitivity?.Value ?? 0f:F2}.");
    }

    [HideFromIl2Cpp]
    private void DecayWhenFree(bool bound)
    {
        double now = Time.timeAsDouble;
        double delta = _lastFrameTime <= 0d ? 0d : now - _lastFrameTime;
        _lastFrameTime = now;

        PlayerStatusManager? status = ManagerList.PlayerStatus;
        if (status is not null)
        {
            UpdateBreastSuperLife(status, delta);
            UpdateMilk(status, delta);
        }

        // Decaying inside a hold would let the player wait out the danger (SPEC003 5.2).
        if (!bound && delta > 0d)
        {
            PleasureRuntime.Meter?.Decay(delta);
        }
    }

    /// <summary>
    /// Reports what the save slot looks like, which is what the sidecar file has to key on
    /// (付録A A-9).
    /// </summary>
    [HideFromIl2Cpp]
    private void ProbeSaveSlot()
    {
        MainSaveData? main = ManagerList.SaveData?.Main;
        if (main is null)
        {
            return;
        }

        int selectId = main.SelectID;
        string? file = main.LoadedFileName;
        if (selectId == _lastSelectId && string.Equals(file, _lastSaveFile, StringComparison.Ordinal))
        {
            return;
        }

        _lastSelectId = selectId;
        _lastSaveFile = file;

        string? key = SlotKey.Compose(selectId, file);
        if (key is not null)
        {
            PleasureRuntime.LoadSlot(key, "the save slot became known");
        }
        PleasureRuntime.Log?.LogInfo(
            $"[probe] A-9: save slot is SelectID={selectId}, LoadedFileName='{file ?? "(null)"}', "
            + $"IsAutoSave={main.IsAutoSave}, sidecar key='{SlotKey.Compose(selectId, file) ?? "(none)"}'.");
    }

    [HideFromIl2Cpp]
    private void ReportSelfCheck(PlayerStatusManager status)
    {
        if (_selfChecked)
        {
            return;
        }

        _selfChecked = true;

        // Durability and HP are BattleMainParameter objects. Printing them directly gave
        // "durability is SiNiSistar2.Obj.Durability of 100", which answers nothing.
        PleasureRuntime.Log?.LogInfo(
            $"[probe] A-6: durability {status.Durability?.Current} of {status.Durability?.Max} "
            + $"(m_MaxDurability {status.m_MaxDurability}); HP {status.HP?.Current} of "
            + $"{status.HP?.Max}. Climax limit would be "
            + $"{ClimaxLimit.Compute(PleasureRuntime.Profile.Climax.LimitBase, PleasureRuntime.Profile.Climax.LimitPerDurability, status.m_MaxDurability)}.");
    }

    /// <summary>
    /// Draws pleasure as a ring concentric with the game's own HP/MP dial.
    ///
    /// The dial already spends its left half on HP and its right half on MP, so a fourth reading
    /// cannot go inside it. Sitting just outside as a full ring keeps the shape language of the
    /// <summary>
    /// Draws the gauge and the cross.
    ///
    /// Everything is a generated texture drawn through <c>GUI.Label(Rect, Texture)</c>.
    /// <c>GUI.DrawTexture</c> is stripped from this build, and flat rectangles through
    /// <c>GUI.Box</c> picked up the default skin's rounded border, which is what made the earlier
    /// attempt look like debug output rather than part of the game.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawGauge()
    {
        PleasureMeter? meter = PleasureRuntime.Meter;
        if (meter is null)
        {
            return;
        }

        PleasureOverlayLayout layout = PleasureRuntime.Overlay;
        float height = Screen.height;

        OverlayPlacement gauge = layout.Gauge;
        float gaugeX = Screen.width * gauge.CentreX;
        float gaugeY = height - (height * gauge.BottomOffset);
        float radius = height * gauge.Size;

        // Generated to match the size it is drawn at, in coarse steps so a nudge of the wheel does
        // not rebuild the texture on every frame. A fixed 96 px looked soft once the gauge was
        // enlarged.
        _liquidResolution = Resolution(radius * 2f);
        RefreshLiquid(meter.Value);
        if (_liquid is not null)
        {
            float diameter = radius * 2f;
            OverlayPainter.Draw(new Rect(gaugeX - radius, gaugeY - radius, diameter, diameter), _liquid, Color.white);
        }

        if (layout.ShowCross)
        {
            RefreshCross();
            if (_cross is not null)
            {
                OverlayPlacement cross = layout.Cross;
                float crossHeight = height * cross.Size;
                float crossWidth = crossHeight * 0.66f;
                float crossX = Screen.width * cross.CentreX;
                float crossY = height - (height * cross.BottomOffset);
                OverlayPainter.Draw(
                    new Rect(crossX - (crossWidth / 2f), crossY - (crossHeight / 2f), crossWidth, crossHeight),
                    _cross,
                    Color.white);
            }
        }

        if (_editing)
        {
            DrawSelectionMarker(radius, gaugeX, gaugeY, height, layout);
        }
    }

    /// <summary>Texture size for a given on-screen size, rounded so it changes rarely.</summary>
    [HideFromIl2Cpp]
    private static int Resolution(float drawnPixels)
    {
        var stepped = (int)(Math.Ceiling(drawnPixels / 64f) * 64f);
        return Math.Clamp(stepped, 64, 512);
    }

    /// <summary>Rings whichever element the editor is currently moving, so the target is never in doubt.</summary>
    [HideFromIl2Cpp]
    private void DrawSelectionMarker(float radius, float gaugeX, float gaugeY, float height, PleasureOverlayLayout layout)
    {

        (_, OverlayPlacement selected) = Selected(layout);
        float x = _editingElement == 0 ? gaugeX : Screen.width * selected.CentreX;
        float y = _editingElement == 0 ? gaugeY : height - (height * selected.BottomOffset);
        float half = _editingElement switch
        {
            0 => radius,
            1 => height * selected.Size * 0.5f,
            _ => height * selected.Size,
        };

        var tint = new Color(1f, 0.85f, 0.35f, 0.85f);
        const float edge = 2f;
        OverlayPainter.Draw(new Rect(x - half, y - half, half * 2f, edge), OverlayPainter.Solid, tint);
        OverlayPainter.Draw(new Rect(x - half, y + half - edge, half * 2f, edge), OverlayPainter.Solid, tint);
        OverlayPainter.Draw(new Rect(x - half, y - half, edge, half * 2f), OverlayPainter.Solid, tint);
        OverlayPainter.Draw(new Rect(x + half - edge, y - half, edge, half * 2f), OverlayPainter.Solid, tint);
    }

    /// <summary>
    /// Opens and drives the enemy classification screen.
    ///
    /// It is deliberately reachable while a hold is in progress: the judgement being made is about
    /// what is happening on screen right now, and it is worth nothing if it can only be made from
    /// memory afterwards.
    /// </summary>
    [HideFromIl2Cpp]
    private void HandleEnemyEditor()
    {
        UnityEngine.Event current = UnityEngine.Event.current;
        if (current is null)
        {
            return;
        }

        // F11 applies Breast through the game's own add path, so the escalation can be exercised
        // without hunting for the item. Counting only advances once per frame per list, so one
        // press is one application, exactly as a use of the item is.
        //
        // Not behind a config switch. The switch was reset to its default by the game writing the
        // config back, and a debug key that silently does nothing is worse than no debug key: it
        // reads as "the mechanism is broken" when nothing was ever asked of it.
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F11)
        {
            ApplyBreastForDebugging();
            current.Use();
            return;
        }

        // One sweep per press, never on its own (付録A A-27).
        if (current.type == EventType.KeyDown && current.keyCode == MilkingAnimation.SweepKey)
        {
            MilkingAnimation.RequestSweep();
        }

        if (current.type == EventType.KeyDown
            && current.keyCode == MilkingKey()
            && !_enemyEditor.IsOpen
            && !_editing)
        {
            TryStartMilking();
            current.Use();
            return;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F10)
        {
            // Closing the layout editor first, so the two are never taking the same keys.
            if (_editing)
            {
                CommitEditing();
            }

            _enemyEditor.Toggle();
            current.Use();
            return;
        }

        if (_enemyEditor.HandleEvent(current))
        {
            current.Use();
        }
    }

    /// <summary>
    /// Applies <c>Breast</c> to the player on demand, for checking the escalation.
    ///
    /// Through <c>AbnormalList.AddAbnormal</c>, the same call an item or an enemy makes, so what is
    /// exercised is the real path rather than a shortcut that would prove nothing.
    /// </summary>
    [HideFromIl2Cpp]
    private void ApplyBreastForDebugging()
    {
        AbnormalList? abnormals = PleasureRuntime.PlayerAbnormals;
        if (abnormals is null)
        {
            PleasureRuntime.Log?.LogWarning(
                "F11: the player's status list is not available yet; load a save first.");
            return;
        }

        try
        {
            // Through the AbnormalData overload, like everything else that adds a status here. The
            // one that takes a type has to resolve the data itself and returns quietly when it
            // cannot, which is the failure that already cost a round trip on BreastSuper. Using it
            // here meant F11 could add nothing, silently, and the count never moved.
            AbnormalManager? manager = ManagerList.Abnormal;
            AbnormalData? data = null;
            if (manager is null || !manager.TryGetData(AbnormalType.Breast, out data) || data is null)
            {
                PleasureRuntime.Log?.LogWarning(
                    "F11: the Breast status data is not loaded, so nothing can be applied yet.");
                return;
            }

            int before = PleasureRuntime.Breasts?.Count ?? 0;
            abnormals.AddAbnormal(data, 1, null);

            // Reported as fact rather than as intent. "Applied" without saying whether it took is
            // how the escalation looked healthy for three rounds while nothing was happening.
            bool present = abnormals.Has(AbnormalType.Breast);
            bool super = abnormals.Has(AbnormalType.BreastSuper);
            PleasureRuntime.Log?.LogInfo(
                $"F11: Breast={present}, BreastSuper={super}, level="
                + $"{abnormals.GetAbnormalLevel(AbnormalType.Breast)}. Count was {before}, is now "
                + $"{PleasureRuntime.Breasts?.Count ?? 0}; "
                + $"{PleasureRuntime.Breasts?.Remaining ?? 0} more before BreastSuper. "
                + $"Milk {PleasureRuntime.Milk?.Fill ?? 0f:P0}.");

            if (!present && !super)
            {
                PleasureRuntime.Log?.LogWarning(
                    "F11: the status did not attach. AbnormalList.Has reports it absent right after "
                    + "the add, so the escalation has nothing to count.");
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"F11: Breast could not be applied: {exception.Message}");
        }
    }

    /// <summary>
    /// The key that milks (SPEC003 FR-260).
    ///
    /// Configurable, and not C. The game casts with C, and immediate-mode GUI cannot stop the game
    /// reading the keyboard for itself — consuming an event only ends its travel within IMGUI — so a
    /// shared key would milk and cast at once. "Only while swollen" cannot be arranged from here for
    /// the same reason: the game's read does not know about the condition.
    /// </summary>
    [HideFromIl2Cpp]
    private KeyCode MilkingKey()
    {
        if (_milkingKey != KeyCode.None)
        {
            return _milkingKey;
        }

        string name = PleasureRuntime.Profile.BreastSuper.MilkingKey;
        if (!Enum.TryParse(name, ignoreCase: true, out KeyCode parsed) || parsed == KeyCode.None)
        {
            parsed = KeyCode.F8;
            PleasureRuntime.Log?.LogWarning(
                $"BreastSuper.MilkingKey '{name}' is not a KeyCode name; F8 is used instead.");
        }

        _milkingKey = parsed;
        return parsed;
    }

    /// <summary>
    /// Starts self-milking (SPEC003 5.8, FR-257).
    ///
    /// It works wherever the player is standing. A safe place is not an area the game marks as safe;
    /// it is any moment nothing is attacking, which is the player's judgement to make and to get
    /// wrong. What it costs is the milk gauge: milking empties it, and the swelling steps down when
    /// it reaches nothing.
    /// </summary>
    [HideFromIl2Cpp]
    private void TryStartMilking()
    {
        AbnormalList? abnormals = PleasureRuntime.PlayerAbnormals;
        MilkReservoir? milk = PleasureRuntime.Milk;
        if (abnormals is null || milk is null)
        {
            return;
        }

        if (milk.IsMilking)
        {
            milk.StopMilking();
            StopMilkingAnimation();
            PleasureRuntime.Log?.LogInfo("Milking stopped.");
            return;
        }

        // Only the escalated swelling can be milked. Ordinary swelling has no way out through
        // milking: its gauge fills one way towards the escalation, and its cure is the game's own
        // (FR-262).
        if (!abnormals.Has(AbnormalType.BreastSuper))
        {
            if (abnormals.Has(AbnormalType.Breast))
            {
                PleasureRuntime.Log?.LogInfo(
                    "Ordinary swelling cannot be milked away; only BreastSuper can.");
            }

            return;
        }

        // Not while held or downed: the hands are not free, and letting it run there would make a
        // hold something to be waited out rather than escaped.
        if (PleasureRuntime.IsBound || PleasureRuntime.IsDefeatPerformance)
        {
            PleasureRuntime.Log?.LogInfo("Milking cannot be started while held.");
            return;
        }

        if (!milk.CanMilk)
        {
            PleasureRuntime.Log?.LogInfo(
                $"There is nothing to milk: the gauge is at {milk.Fill:P0}.");
            return;
        }

        if (milk.TryStartMilking())
        {
            PleasureRuntime.MilkingWasHit = false;
            StartMilkingAnimation();
            PleasureRuntime.Log?.LogInfo(
                $"Milking started at {milk.Fill:P0}; being hit will waste it and the gauge will "
                + "start filling again. BreastSuper will subside to Breast.");
        }
    }

    /// <summary>
    /// Drains the reservoir while milking, and takes <c>BreastSuper</c> back down to <c>Breast</c>
    /// when it empties (SPEC003 FR-259, FR-262).
    ///
    /// Nothing fills it here. Milk comes from sexual hits taken while swollen, recorded where those
    /// hits are already being watched.
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateMilk(PlayerStatusManager status, double delta)
    {
        MilkReservoir? milk = PleasureRuntime.Milk;
        AbnormalList? abnormals = status.AbnormalList;
        if (milk is null || abnormals is null || !milk.IsMilking)
        {
            PleasureRuntime.MilkingWasHit = false;
            return;
        }

        bool super;
        try
        {
            super = abnormals.Has(AbnormalType.BreastSuper);
        }
        catch (Exception)
        {
            return;
        }

        if (PleasureRuntime.MilkingWasHit || PleasureRuntime.IsBound
            || PleasureRuntime.IsDefeatPerformance || !super)
        {
            PleasureRuntime.MilkingWasHit = false;
            if (milk.StopMilking())
            {
                StopMilkingAnimation();
                PleasureRuntime.Log?.LogInfo(
                    $"Milking was interrupted at {milk.Fill:P0}; the gauge fills again from here.");
            }

            return;
        }

        // The clip lives in a bundle that is not loaded during ordinary play, so the animation may
        // arrive a moment after the keypress rather than with it (付録A A-25, A-26).
        MilkingAnimation.Tick(
            PleasureRuntime.Profile.BreastSuper.MilkingAnimationState,
            PleasureRuntime.Profile.BreastSuper.MilkingAnimationSlot);

        if (milk.Tick(delta) != MilkOutcome.Emptied)
        {
            return;
        }

        StopMilkingAnimation();

        // Down to Breast, not to nothing. Milking out of the escalation still leaves the swelling,
        // which is the same ladder the duration walks back down.
        abnormals.RemoveAbnormal(AbnormalType.BreastSuper);
        PortraitRefresh.Refresh("return to Breast", null);
        PleasureRuntime.SuperTimer?.Stop();
        AbnormalManager? manager = ManagerList.Abnormal;
        AbnormalData? data = null;
        if (manager is not null && manager.TryGetData(AbnormalType.Breast, out data) && data is not null)
        {
            abnormals.AddAbnormal(data, 1, null);
        }

        PleasureRuntime.Breasts?.Reset();
        BeginTransition();
        PleasureRuntime.Log?.LogInfo("Milked dry: BreastSuper subsided to Breast.");
    }

    /// <summary>
    /// Starts and stops the milking animation. The work is in <see cref="MilkingAnimation"/>.
    /// </summary>
    [HideFromIl2Cpp]
    private void StartMilkingAnimation() => MilkingAnimation.Start(
        PleasureRuntime.Profile.BreastSuper.MilkingAnimationState,
        PleasureRuntime.Profile.BreastSuper.MilkingAnimationSlot);

    /// <summary>Puts the player back into whatever they were doing before milking.</summary>
    [HideFromIl2Cpp]
    private static void StopMilkingAnimation() => MilkingAnimation.Stop();

    /// <summary>
    /// Notes that this enemy has been met, so the editor can offer the handful that matter ahead of
    /// the hundred that do not. Written through on the first sighting only.
    /// </summary>
    [HideFromIl2Cpp]
    private static void RecordSighting(string? enemyId)
    {
        if (enemyId is null || !PleasureRuntime.Enemies.MarkSeen(enemyId))
        {
            return;
        }

        PleasureRuntime.SaveEnemies($"first sighting of {enemyId}");
    }

    /// <summary>
    /// Lets the gauge be placed with the mouse.
    ///
    /// Reading the dial's position off a screenshot kept being wrong — window borders, taskbars and
    /// aspect ratios all move it — and every miss cost a round trip. Dragging it into place takes
    /// the guesswork out entirely, and what the player sets is what gets written to the config.
    /// </summary>
    [HideFromIl2Cpp]
    private void HandleLayoutEditor()
    {
        UnityEngine.Event current = UnityEngine.Event.current;
        if (current is null)
        {
            return;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F9)
        {
            ToggleEditing();
            current.Use();
            return;
        }

        if (!_editing)
        {
            return;
        }

        switch (current.type)
        {
            case EventType.KeyDown when current.keyCode is KeyCode.Return or KeyCode.KeypadEnter:
                CommitEditing();
                current.Use();
                break;

            case EventType.KeyDown when current.keyCode == KeyCode.Escape:
                CancelEditing();
                current.Use();
                break;

            case EventType.KeyDown when current.keyCode == KeyCode.Tab:
                _editingElement = (_editingElement + 1) % 3;
                current.Use();
                break;

            case EventType.MouseDrag:
                Adjust(placement => placement with
                {
                    CentreX = Math.Clamp(placement.CentreX + (current.delta.x / Screen.width), 0f, 1f),

                    // Measured up from the bottom, so dragging downward reduces it.
                    BottomOffset = Math.Clamp(placement.BottomOffset - (current.delta.y / Screen.height), 0f, 1f),
                });
                current.Use();
                break;

            case EventType.ScrollWheel:
                Adjust(placement => placement with
                {
                    // Proportional rather than a fixed step: one notch is the same visual change
                    // whatever the current size, so growing stays responsive instead of crawling
                    // once the element is already large. The ceiling is generous because a gauge
                    // wider than the screen is a legitimate thing to want on a large display.
                    Size = Math.Clamp(
                        placement.Size * (float)Math.Pow(1.06d, -current.delta.y),
                        0.01f,
                        1.5f),
                });
                current.Use();
                break;
        }
    }

    /// <summary>Applies a change to whichever element is selected, leaving the other alone.</summary>
    [HideFromIl2Cpp]
    private void Adjust(Func<OverlayPlacement, OverlayPlacement> change)
    {
        PleasureOverlayLayout layout = PleasureRuntime.Overlay;
        PleasureRuntime.Overlay = _editingElement switch
        {
            1 => layout with { Cross = change(layout.Cross) },
            2 => layout with { Milk = change(layout.Milk) },
            _ => layout with { Gauge = change(layout.Gauge) },
        };
    }

    /// <summary>Which element the editor is on, and where it sits.</summary>
    [HideFromIl2Cpp]
    private (string Name, OverlayPlacement Placement) Selected(PleasureOverlayLayout layout) =>
        _editingElement switch
        {
            1 => ("cross", layout.Cross),
            2 => ("milk gauge", layout.Milk),
            _ => ("gauge", layout.Gauge),
        };

    [HideFromIl2Cpp]
    private void ToggleEditing()
    {
        if (_editing)
        {
            CommitEditing();
            return;
        }

        _editing = true;
        _layoutBeforeEdit = PleasureRuntime.Overlay;
        PleasureRuntime.Log?.LogInfo(
            "Layout editing started. Tab cycles the gauge, the cross and the milk gauge, drag "
            + "moves it, the wheel resizes it, Enter saves and Escape cancels.");
    }

    [HideFromIl2Cpp]
    private void CommitEditing()
    {
        _editing = false;
        _layoutBeforeEdit = null;
        PleasureOverlayLayout layout = PleasureRuntime.Overlay;
        PleasureRuntime.SaveOverlay?.Invoke(layout);
        PleasureRuntime.Log?.LogInfo(
            $"Layout saved: GaugeCentreX={layout.Gauge.CentreX:F3}, GaugeBottomOffset={layout.Gauge.BottomOffset:F3}, "
            + $"GaugeSize={layout.Gauge.Size:F3}, CrossCentreX={layout.Cross.CentreX:F3}, "
            + $"CrossBottomOffset={layout.Cross.BottomOffset:F3}, CrossSize={layout.Cross.Size:F3}, "
            + $"MilkCentreX={layout.Milk.CentreX:F3}, MilkBottomOffset={layout.Milk.BottomOffset:F3}, "
            + $"MilkSize={layout.Milk.Size:F3}.");
    }

    [HideFromIl2Cpp]
    private void CancelEditing()
    {
        if (_layoutBeforeEdit is not null)
        {
            PleasureRuntime.Overlay = _layoutBeforeEdit;
        }

        _editing = false;
        _layoutBeforeEdit = null;
        PleasureRuntime.Log?.LogInfo("Layout editing cancelled; the previous position is restored.");
    }

    /// <summary>Shows the numbers being edited, so the result can also be typed into the config by hand.</summary>
    [HideFromIl2Cpp]
    private void DrawEditorChrome()
    {
        if (!_editing)
        {
            return;
        }

        PleasureOverlayLayout layout = PleasureRuntime.Overlay;
        (string name, OverlayPlacement selected) = Selected(layout);

        GUI.Box(new Rect(12f, 12f, 520f, 96f), GUIContent.none);
        GUI.Label(new Rect(24f, 20f, 500f, 22f), $"Editing the {name}    Tab: gauge / cross / milk gauge");
        GUI.Label(new Rect(24f, 44f, 500f, 22f), "Drag: move    Wheel: resize    Enter: save    Escape: cancel");
        GUI.Label(
            new Rect(24f, 68f, 500f, 22f),
            $"CentreX={selected.CentreX:F3}  BottomOffset={selected.BottomOffset:F3}  Size={selected.Size:F3}");
    }

    /// <summary>
    /// Rebuilds the liquid only when it would look different. Regenerating a disc every frame is
    /// pure waste, and a step of one percent with a slow wave is already past what the eye picks up.
    /// </summary>
    [HideFromIl2Cpp]
    private void RefreshLiquid(float fill)
    {
        double now = Time.unscaledTimeAsDouble;
        bool moved = Math.Abs(fill - _liquidFill) > 0.01f;
        bool resized = _liquid is not null && _liquid.width != _liquidResolution;
        if (_liquid is not null && !moved && !resized && now - _liquidBuiltAt < 0.08d)
        {
            return;
        }

        _liquidFill = fill;
        _liquidBuiltAt = now;
        _liquidPhase += 0.35f;
        _liquid = PleasureArt.LiquidDisc(_liquidResolution, fill, _liquidPhase);
    }

    /// <summary>Rebuilt only when the damage changes, which is once per climax.</summary>
    [HideFromIl2Cpp]
    private void RefreshCross()
    {
        ClimaxTuning tuning = PleasureRuntime.Profile.Climax;
        int limit = ClimaxLimit.Compute(tuning.LimitBase, tuning.LimitPerDurability, _lastMaxDurability);
        int count = PleasureRuntime.Climaxes.Count;

        // Only severed when the break would actually end the run. A cross that snaps while the game
        // over is switched off would be telling the player something untrue.
        bool broken = tuning.GameOverEnabled && limit > 0 && count >= limit;

        if (_cross is not null && count == _crossNotches && broken == _crossBroken)
        {
            return;
        }

        _crossNotches = count;
        _crossBroken = broken;

        // Erosion runs a step behind the count so the cross is never whole once a climax has
        // landed, and never fully gone until the break.
        float progress = limit > 0 ? Math.Clamp(count / (float)limit, 0f, 1f) * 0.85f : 0f;
        _cross = PleasureArt.Cross(96, 144, progress, broken);
    }

    /// <summary>
    /// A pink haze closing in from the edges when a climax lands.
    ///
    /// IMGUI has no gradient, so the falloff is nested bands whose alpha drops toward the centre.
    /// It covers the whole frame, which is the point: the moment should take the screen rather than
    /// annotate a corner of it. Nothing here touches <c>Time.timeScale</c> (FR-212).
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawClimaxFlash()
    {
        double remaining = PleasureRuntime.ClimaxFlashUntil - Time.timeAsDouble;
        if (remaining <= 0d)
        {
            return;
        }

        PleasureOverlayLayout layout = PleasureRuntime.Overlay;
        var progress = (float)Math.Clamp(remaining / Math.Max(0.01f, layout.FlashSeconds), 0d, 1d);

        // Blooms quickly and fades slowly, which reads as a pulse instead of a light switching on.
        float strength = progress > 0.75f ? (1f - progress) / 0.25f : progress / 0.75f;
        DrawVignette(Math.Clamp(strength, 0f, 1f));
    }

    /// <summary>
    /// The milk reservoir, drawn whenever there is any (SPEC003 FR-261).
    ///
    /// Always on while it holds something, not only while milking: what it reports is how long the
    /// next milking will take, and that is worth knowing before deciding whether now is the moment.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawMilk()
    {
        MilkReservoir? milk = PleasureRuntime.Milk;

        // Drawn whenever the player is swollen, empty or not. Hiding an empty gauge is the mistake
        // the pleasure gauge already taught: a gauge that vanishes when it reads zero cannot be told
        // apart from one that is broken, and this one reads zero exactly when the player has just
        // been cured and wants to see that it worked.
        if (milk is null || (!PleasureRuntime.IsSwollen && milk.Fill <= 0.001f && !milk.IsMilking))
        {
            return;
        }

        OverlayPlacement placement = PleasureRuntime.Overlay.Milk;
        float height = Screen.height;
        float radius = height * placement.Size;
        float x = Screen.width * placement.CentreX;
        float y = height - (height * placement.BottomOffset);

        RefreshMilk(milk.Fill, Resolution(radius * 2f));
        if (_milkVessel is null)
        {
            return;
        }

        float diameter = radius * 2f;
        OverlayPainter.Draw(new Rect(x - radius, y - radius, diameter, diameter), _milkVessel, Color.white);

        // While milking the vessel pulses, so the difference between "there is milk" and "it is
        // being taken out" is visible without reading a number.
        if (milk.IsMilking)
        {
            var pulse = (float)((Math.Sin(Time.unscaledTimeAsDouble * 7d) + 1d) * 0.5d);
            OverlayPainter.Draw(
                new Rect(x - radius, y - radius, diameter, diameter),
                _milkVessel,
                new Color(1f, 1f, 1f, 0.25f + (pulse * 0.35f)));
        }
    }

    [HideFromIl2Cpp]
    private void RefreshMilk(float fill, int resolution)
    {
        double now = Time.unscaledTimeAsDouble;
        bool moved = Math.Abs(fill - _milkFill) > 0.005f;
        bool resized = _milkVessel is not null && _milkVessel.width != resolution;
        if (_milkVessel is not null && !moved && !resized && now - _milkBuiltAt < 0.08d)
        {
            return;
        }

        _milkFill = fill;
        _milkBuiltAt = now;
        _milkPhase += 0.3f;
        _milkVessel = PleasureArt.MilkVessel(resolution, fill, _milkPhase);
    }

    /// <summary>
    /// Black over the moment the status changes, fading back in.
    ///
    /// Drawn by the MOD rather than through the game's fader: the fader belongs to the event system,
    /// and driving it from outside an event would leave it holding state nobody clears. A rectangle
    /// costs nothing and cannot strand the screen dark.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawTransitionFade()
    {
        double remaining = PleasureRuntime.TransitionFadeUntil - Time.timeAsDouble;
        if (remaining <= 0d)
        {
            return;
        }

        float seconds = Math.Max(0.01f, PleasureRuntime.Profile.BreastSuper.FadeSeconds);
        var progress = (float)Math.Clamp(remaining / seconds, 0d, 1d);

        // Full black for the first third, then back to clear. The body is rebuilt on the frame the
        // fade starts, so what the black hides is the swap itself.
        float alpha = progress > 0.66f ? 1f : progress / 0.66f;
        OverlayPainter.Fill(
            new Rect(0f, 0f, Screen.width, Screen.height),
            new Color(0f, 0f, 0f, Math.Clamp(alpha, 0f, 1f)));
    }

    [HideFromIl2Cpp]
    private void DrawVignette(float strength)
    {
        if (strength <= 0f)
        {
            return;
        }

        float width = Screen.width;
        float height = Screen.height;
        const int bands = 12;
        float step = height * 0.30f / bands;

        for (var index = 0; index < bands; index++)
        {
            float t = 1f - (index / (float)bands);
            float alpha = strength * t * t * 0.18f;
            if (alpha <= 0.002f)
            {
                continue;
            }

            var tint = new Color(1f, 0.42f, 0.70f, alpha);
            float offset = index * step;
            OverlayPainter.Draw(new Rect(0f, offset, width, step + 1f), OverlayPainter.Solid, tint);
            OverlayPainter.Draw(new Rect(0f, height - offset - step - 1f, width, step + 1f), OverlayPainter.Solid, tint);
            OverlayPainter.Draw(new Rect(offset, 0f, step + 1f, height), OverlayPainter.Solid, tint);
            OverlayPainter.Draw(new Rect(width - offset - step - 1f, 0f, step + 1f, height), OverlayPainter.Solid, tint);
        }
    }


    [HideFromIl2Cpp]
    private static string? ResolveBinderId(Lelia lelia)
    {
        try
        {
            return lelia.Bind?.BinderEnemy?.GalleryEnemyID.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [HideFromIl2Cpp]
    private void Suspend()
    {
        string? failure = PleasureRuntime.Ledger.Release(PleasureRuntime.RemainHp1Key);
        if (failure is not null)
        {
            PleasureRuntime.Log?.LogWarning($"Could not release the HP0 suppression: {failure}");
        }

        _gameplayActive = false;
        PleasureRuntime.IsBound = false;
        PleasureRuntime.BinderEnemyId = null;
        _wasBound = false;
    }
}
