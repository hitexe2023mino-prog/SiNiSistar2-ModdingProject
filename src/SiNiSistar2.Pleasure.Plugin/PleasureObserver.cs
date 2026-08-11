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
/// Consumes climaxes, turns the climax limit into a death, and records the remaining 付録A
/// measurements.
///
/// The HP a sexual hit would have taken is held off around the hit itself, in
/// <see cref="DamageProbePatches"/>, not from here — it is a property of one hit rather than of
/// being held. What this does own is the other half: a climax that reaches the limit takes HP to
/// zero at once, rather than waiting for the enemy to finish the job (SPEC003 5.5, DEC-257).
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
    private bool _crestRequested;
    private bool _lustReported;
    private readonly HashSet<AbnormalType> _lustAsked = new();
    private bool _crestLoadAsked;
    private double _crestDebtChecked;
    private double _crestWaitLogged;
    private double _swellingOverlapSince;
    private double _breastSuperLoadAsked;
    private double _breastSuperWaitLogged;
    private bool _interactionLocked;
    private Texture2D? _milkVessel;
    private float _milkFill = -1f;
    private float _milkPhase;
    private double _milkBuiltAt;
    private bool _milkAnnounced;
    private Texture2D? _crest;
    private int _crestParts = -1;
    private int _crestResolution;
    private bool _stunUnavailableLogged;
    private bool _mpPanel;
    private MpPenaltyState _mpState;
    private readonly HashSet<KeyCode> _eventKeys = new();
    private bool _inputPollAnswered;
    private string? _inputPollBroken;


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
            GaTakePlayer? player = ManagerList.Gallery?.CurrentTakePlayer;
            AnimationTakeData? take = player?.PlayingTakeData;
            if (take is null)
            {
                return;
            }

            _ = take.m_TakeName;
        }
        catch (Exception)
        {
            // The gallery is absent for most of a session, and that is not a fault.
        }
    }

    /// <summary>
    /// Draws the gauge, corruption and climax count.
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
            DrawSpec005Panel();
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
        // The haze is not part of the HUD and does not answer to its switch. ShowOverlay hides the
        // gauges — the things the player consults — whereas this reports that the body just changed,
        // which FR-413 requires on every stock whatever the HUD is set to. Its own CrestFx.Enabled
        // is the control for it.
        if (_gameplayActive || _editing)
        {
            DrawCrestProgressFlash();
        }

        if (!PleasureRuntime.Profile.ShowOverlay || (!_gameplayActive && !_editing))
        {
            return;
        }

        DrawGauge();
        DrawClimaxFlash();
        DrawMilk();
        DrawCrest();
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
        BinderIdentity? binder = ResolveBinder(lelia);
        PleasureRuntime.BinderEnemyId = binder?.Id;
        PleasureRuntime.BinderDisplayName = binder?.DisplayName;
        RecordSighting(binder);

        _gameplayActive = true;
        PleasureRuntime.GameplayStarted = true;
        PleasureRuntime.IsSwollen = IsSwollen(status);
        ReportInteractionLock(status);
        ReportSelfCheck(status);
        ReportLustStatuses();
        ReportBreastCureSurface(status);
        ApplyPendingBreastSuper(status);
        ApplyPendingLustCrest(status);
        TrackCrestSublimation();
        EnforceSingleSwelling(status);
        SweepStaleHpHold();
        ConsumeClimax(lelia, status, dead);
        DecayWhenFree(bound, dead);
        UpdateMpPenalty(bound, dead);
        ProbeSaveSlot();

        // Last, so what the page serves is the state this frame settled on rather than the state it
        // started from (SPEC006 4.5).
        StatsPublisher.Publish(status.m_MaxDurability);

        _wasBound = bound;
    }

    /// <summary>
    /// Closes an HP hold that outlived the hit it was opened for (SPEC003 FR-204).
    ///
    /// The prefix opens it, the postfix closes it, and a finalizer closes it if the game's own
    /// damage code threw. This is the fourth line, and it exists because the failure it guards
    /// against is the worst one the MOD can produce: a <c>DontSub</c> left standing is a player who
    /// cannot be hurt by anything, in or out of a hold, for the rest of the session. A stale hold
    /// therefore cannot survive a single frame, whatever went wrong inside damage resolution.
    /// </summary>
    [HideFromIl2Cpp]
    private void SweepStaleHpHold()
    {
        if (!DamageProbePatches.IsHoldOpen)
        {
            return;
        }

        DamageProbePatches.ReleaseHold();
        PleasureRuntime.Log?.LogWarning(
            "An HP hold outlived the hit it was opened for and was closed by the frame sweep. "
            + "Damage resolution did not return through either the postfix or the finalizer "
            + "(SPEC003 FR-204).");
    }

    /// <summary>
    /// Puts the game's own lust crest on the player once the corruption has earned it
    /// (SPEC003 FR-267).
    ///
    /// The HUD mark is the MOD's picture of the corruption; this is where the picture stops being
    /// one. The status is the game's, applied through the game's own path, and it carries whatever
    /// the game already attaches to it — this adds a status, it does not invent a condition.
    ///
    /// Deferred the same way the escalation is. A status added underneath an open menu goes onto a
    /// list the UI has already drawn and will not redraw.
    /// </summary>
    [HideFromIl2Cpp]
    private void ApplyPendingLustCrest(PlayerStatusManager status)
    {
        // What decides whether the mark belongs on the body is the corruption standing there now,
        // so a cure taken while the corruption still demands a stock is undone at once: lifting a
        // symptom does not remove the cause. A cure taken with no corruption behind it holds,
        // because then nothing demands it — which is the difference between the two cases (FR-274).
        //
        // Not recomputed every frame, though. Reading the level costs an interop call, and the
        // answer can only move when the corruption moves, when the status is added or removed, or
        // when a slot is loaded — all of which this MOD already sees and flags. Behind those sits a
        // half-second sweep, because a path nobody noticed must not be able to strand the mark, and
        // half a second is far below what a cure and its undoing look like (DEC-254).
        double checkedAt = Time.unscaledTimeAsDouble;
        if (PleasureRuntime.CrestDebtDirty || checkedAt - _crestDebtChecked > 0.5d)
        {
            PleasureRuntime.CrestDebtDirty = false;
            _crestDebtChecked = checkedAt;

            int owed = PleasureRuntime.CrestSublimated
                ? PleasureRuntime.CrestMaxLevel
                : PleasureRuntime.EarnedCrestLevel(PleasureRuntime.CrestMaxLevel);
            if (owed > PleasureRuntime.CrestLevel)
            {
                PleasureRuntime.PendingLustCrest = true;
            }
        }

        if (!PleasureRuntime.PendingLustCrest)
        {
            return;
        }

        // Not while the game is holding the screen. A cure runs inside a drama, and re-marking the
        // body in the middle of one would be arguing with the scene as it plays. The debt does not
        // expire — it is settled on the first ordinary frame afterwards.
        if (Time.timeScale <= 0f || PleasureRuntime.IsBound || PleasureRuntime.IsDefeatPerformance
            || ManagerList.Object?.IsCinematicEvent == true)
        {
            return;
        }

        AbnormalList? abnormals = status.AbnormalList;
        if (abnormals is null)
        {
            return;
        }

        // No early-out on "already worn". That check belonged to a crest that was either on or
        // off; the crest has levels, and returning here meant the first level was also the last —
        // which is exactly what was reported (付録A A-46).
        AbnormalManager? manager = ManagerList.Abnormal;
        AbnormalData? data = null;
        if (manager is null || !manager.TryGetData(AbnormalType.LustMarkCurse, out data) || data is null)
        {
            // Left standing: loading is asynchronous, so "not yet" must not become "never". Asked
            // for out loud, though. The first version returned here in silence, and a mark that
            // never arrives with nothing in the log to say why is indistinguishable from one that
            // was never earned — which is exactly how it was reported.
            RequestLustCrestData(manager);
            return;
        }

        // The ceiling comes from the game, once. A number written here would be one that stops
        // being true when the game changes it. Recomputed after reading it, because the first pass
        // through this method is also the first time the real ceiling is known.
        PleasureRuntime.CrestMaxLevel = Math.Max(1, data.MaxLevel);
        PleasureRuntime.CrestMaxLevelKnown = true;

        int target = PleasureRuntime.CrestSublimated
            ? PleasureRuntime.CrestMaxLevel
            : PleasureRuntime.EarnedCrestLevel(PleasureRuntime.CrestMaxLevel);
        int before = PleasureRuntime.CrestLevel;
        if (target <= before)
        {
            PleasureRuntime.PendingLustCrest = false;
            return;
        }

        // Asked for by the level wanted, not by repeating a request for level one. Every repeat
        // reported "added, level=1" and left the status exactly where it was: the argument names
        // the level to hold, it does not add one to it (付録A A-47).
        abnormals.AddAbnormal(data, target, null);

        PleasureRuntime.PendingLustCrest = false;
        int now = PleasureRuntime.CrestLevel;
        if (now <= before)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The lust crest was asked for level {target} and stayed at {now}. The corruption "
                + "keeps accumulating; only the game's own status is not following it.");
            return;
        }

        // The last stock is the sublimation. The game carries three of the curse and turns the
        // fourth into the mark itself, so reaching the ceiling is the moment it stops being a
        // curse (FR-273).
        if (LatchSublimation(now))
        {
            return;
        }

        if (PleasureRuntime.CrestSublimated)
        {
            // The status was put back under a sublimation that has already been announced (a cure
            // was undone). Not a new stage, so no haze and no repeated announcement.
            return;
        }

        RaiseCrestProgressEffect(now, sublimated: false);
        PleasureRuntime.Log?.LogInfo(
            $"The corruption has marked the body: the lust crest holds {now} of "
            + $"{PleasureRuntime.CrestMaxLevel - 1} stocks. It can still be cured at this point; the "
            + $"{PleasureRuntime.CrestMaxLevel}th sublimates it. Corruption is gained "
            + $"{PleasureRuntime.CrestCorruptionScale:0.##}x faster at this stage, and "
            + $"{PleasureRuntime.Profile.Corruption.ScaleFor(0, PleasureRuntime.CrestMaxLevel, true):0.##}x "
            + "once it sublimates (SPEC005 5.5).");
    }

    /// <summary>
    /// Watches for the crest reaching its ceiling by any route (SPEC005 3章 昇華済み).
    ///
    /// The MOD's own apply path already latches what it does itself, but it is not the only way the
    /// status arrives: enemies apply it too, and one that applies it at full level has sublimated
    /// the body just as surely. Both SPEC005 coefficients hang off this flag, so missing that route
    /// leaves the run reading the curse's numbers while wearing the mark.
    /// </summary>
    [HideFromIl2Cpp]
    private void TrackCrestSublimation()
    {
        if (PleasureRuntime.CrestSublimated)
        {
            return;
        }

        // The ceiling is asked for until it is known, then never again. Without it there is no way
        // to tell a first stock from a last one.
        if (!PleasureRuntime.CrestMaxLevelKnown)
        {
            try
            {
                AbnormalManager? manager = ManagerList.Abnormal;
                if (manager is not null
                    && manager.TryGetData(AbnormalType.LustMarkCurse, out AbnormalData? data)
                    && data is not null)
                {
                    PleasureRuntime.CrestMaxLevel = Math.Max(1, data.MaxLevel);
                    PleasureRuntime.CrestMaxLevelKnown = true;
                }
            }
            catch (Exception)
            {
                // Loading is asynchronous; "not yet" is not "never", and it is asked again next frame.
                return;
            }
        }

        LatchSublimation(PleasureRuntime.CrestLevel);
    }

    /// <summary>
    /// Records that the crest has reached its last stock (SPEC003 FR-273, SPEC005 3章).
    ///
    /// Sublimation is defined by the stock the body is carrying, not by who put it there. An enemy
    /// can apply the crest, and applying it at the ceiling is the same irreversible event as
    /// corruption earning its way up to one — the definition says 付与経路を問わず. Before this was
    /// shared, the latch lived only inside the MOD's own apply path, so a crest that arrived at full
    /// level from an enemy left the run permanently on the wrong side of the cliff: corruption kept
    /// the curse's coefficient (FR-419) and pleasure lost the mark's multiplier (FR-408) while the
    /// game showed the mark.
    /// </summary>
    /// <returns>True when this call was the sublimation.</returns>
    [HideFromIl2Cpp]
    private bool LatchSublimation(int level)
    {
        // A ceiling that has not been read from the game yet is 1, and latching on that would call
        // the very first curse stock a sublimation (FR-421).
        if (!PleasureRuntime.CrestMaxLevelKnown
            || PleasureRuntime.CrestSublimated
            || level < PleasureRuntime.CrestMaxLevel)
        {
            return false;
        }

        PleasureRuntime.CrestSublimated = true;
        PleasureRuntime.CrestDebtDirty = true;
        RaiseCrestProgressEffect(level, sublimated: true);
        PleasureRuntime.Log?.LogInfo(
            $"The lust crest reached its final stock ({level} of {PleasureRuntime.CrestMaxLevel}) "
            + "and has sublimated into the mark itself: no cure will take it off for the rest of "
            + "this run. A new game starts a new run and clears it (FR-273). Corruption is now "
            + $"gained {PleasureRuntime.CrestCorruptionScale:0.##}x faster, and pleasure "
            + $"{PleasureRuntime.Profile.Pleasure.CrestScale:0.##}x (SPEC005 5.2, 5.5).");
        return true;
    }

    /// <summary>
    /// Shows the haze that marks the curse advancing (SPEC005 5.4, FR-413, FR-414).
    ///
    /// Raised where the status actually lands rather than where the debt is decided. The observer
    /// defers putting a stock on while an event, a hold, a defeat performance or a pause is running
    /// (SPEC003 FR-274), and the haze belongs to the moment the body changed, not to the moment the
    /// change became due.
    ///
    /// Only ever on the way up, and that is the caller's guarantee rather than a second check here:
    /// it has already established that the level rose. A "highest stage seen" latch of our own
    /// would be one more thing to clear at the start of a run, and forgetting to clear it would
    /// silently swallow the haze for the whole of the next one.
    /// </summary>
    [HideFromIl2Cpp]
    private void RaiseCrestProgressEffect(int stage, bool sublimated)
    {
        CrestFxTuning tuning = PleasureRuntime.Profile.CrestFx;
        if (!tuning.HasEffect)
        {
            return;
        }

        float intensity = CrestProgressEffect.Intensity(
            stage,
            PleasureRuntime.CrestMaxLevel,
            sublimated,
            tuning.IntensityPerStage);
        if (intensity <= 0f)
        {
            return;
        }

        PleasureRuntime.CrestFxIntensity = intensity;
        PleasureRuntime.CrestFxUntil = Time.timeAsDouble + tuning.DurationSeconds;
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

        BeginTransition();

        // The escalated swelling has its own art and its own body flag, but nothing on this path
        // asks for the portrait to be drawn again (付録A A-28, A-30).
        PortraitRefresh.Refresh("escalation to BreastSuper", AbnormalType.BreastSuper);

        PleasureRuntime.Log?.LogInfo(
            $"Breast escalated to BreastSuper (Breast "
            + $"{(PleasureRuntime.Profile.BreastSuper.ReplaceBreast ? "removed" : "kept")}). "
            + $"Corruption {PleasureRuntime.Corruption?.Value ?? 0f:F2}.");
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
                BeginTransition();
                PleasureRuntime.Log?.LogInfo(
                    "BreastSuper was removed along with Breast by the game's own cure.");
            }

            return;
        }

        // Nothing further here. How long the escalation lasts is the milk gauge, worked off in
        // UpdateMilk (FR-264): there is no separate clock, because a clock and a gauge that both
        // ended it would mean the hits that refill the gauge sometimes cost nothing.
        _ = present;
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
        RequestLustCrestLoad(manager);

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
    private void RequestLustCrestData(AbnormalManager? manager)
    {
        double now = Time.unscaledTimeAsDouble;
        bool report = now - _crestWaitLogged > 5d;
        if (report)
        {
            _crestWaitLogged = now;
        }

        if (manager is null)
        {
            return;
        }

        // Asked once, ever. The game's loader adds the result to a dictionary by type, so a second
        // request for a type already in flight throws "An item with the same key has already been
        // added" out of an async continuation, where nothing can catch it. Repeating the request
        // every two seconds turned one wait into a wall of unhandled exceptions (付録A A-46).
        if (!_crestLoadAsked)
        {
            _crestLoadAsked = true;
            try
            {
                manager.LoadAbnormalData(AbnormalType.LustMarkCurse, ManagerList.RootTokenSource.Token);
            }
            catch (Exception exception)
            {
                if (report)
                {
                    PleasureRuntime.Log?.LogWarning(
                        $"The lust crest could not be loaded on request: {exception.Message}");
                }

                return;
            }
        }

        if (report)
        {
            PleasureRuntime.Log?.LogInfo(
                "The lust crest has been earned and is waiting for its data; the game has been "
                + "asked to load it. This repeats until it arrives.");
        }
    }

    [HideFromIl2Cpp]
    private void RequestLustCrestLoad(AbnormalManager manager)
    {
        if (_crestRequested || !PleasureRuntime.Profile.Corruption.MarksTheBody)
        {
            return;
        }

        _crestRequested = true;
        try
        {
            // The same registration BreastSuper needs. The crest is a status the game applies
            // itself, so its data may well already be loaded — but "may well" is a guess, and the
            // failure it guards against is silent: the application returns quietly and the mark
            // never lands (FR-252, A-24).
            manager.PreloadResist(AbnormalType.LustMarkCurse);
            PleasureRuntime.Log?.LogInfo(
                "The lust crest was registered for preloading so it can be applied.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The lust crest could not be registered for preloading: {exception.Message}");
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

    /// <summary>
    /// Completes one climax and, if it was the last the player had, ends the run
    /// (SPEC003 5.4, 5.5).
    ///
    /// The order is the one the spec sets: the gauge is emptied, the count and the corruption move,
    /// the performance starts, and only then is the limit tested. A climax that kills is still a
    /// climax, and the performance not being drawable is never a reason for it not to count
    /// (FR-213).
    /// </summary>
    [HideFromIl2Cpp]
    private void ConsumeClimax(Lelia lelia, PlayerStatusManager status, bool dead)
    {
        if (!PleasureRuntime.PendingClimax)
        {
            return;
        }

        PleasureRuntime.PendingClimax = false;
        PleasureRuntime.Meter?.ConsumeClimax();
        PleasureRuntime.Climaxes.Record();

        // The captor is credited from the identity the hold already resolved, never from a lookup of
        // this MOD's own (SPEC006 FR-604). A climax with nobody named still counts — it goes to the
        // reserved bucket, so the total stays honest even when the enemy behind it cannot be
        // (FR-602).
        PleasureRuntime.ActorClimaxes.Record(PleasureRuntime.BinderEnemyId);

        PleasureRuntime.GainCorruption(PleasureRuntime.Profile.Corruption.PerClimax);

        PleasureRuntime.ClimaxFlashUntil =
            Time.timeAsDouble + PleasureRuntime.Profile.Climax.OverlaySeconds;

        PleasureRuntime.Log?.LogInfo(
            $"Climax {PleasureRuntime.Climaxes.Count}; corruption "
            + $"{PleasureRuntime.Corruption?.Value ?? 0f:F2}.");

        ApplyClimaxLimit(lelia, status, dead);
        GrantRegenBuff();
    }

    /// <summary>
    /// Gives a sublimated body the regeneration it just earned (SPEC005 5.1, FR-402, FR-404).
    ///
    /// After the limit has been applied rather than before it. A climax that ends the run does not
    /// pay out: the buff would start on a corpse, and a restoration racing the death it was caused
    /// by is exactly the kind of thing that leaves HP at 1 and nobody able to say why (DEC-410).
    ///
    /// Sublimation is the condition, not merely wearing the crest. The curse is still something the
    /// player can undo, and paying a reward for a state they can walk back would make walking it
    /// back a mistake (DEC-402).
    /// </summary>
    [HideFromIl2Cpp]
    private static void GrantRegenBuff()
    {
        RegenBuffTrack? regen = PleasureRuntime.Regen;
        if (regen is null
            || !PleasureRuntime.Profile.Regen.HasEffect
            || !PleasureRuntime.CrestSublimated
            || PleasureRuntime.ClimaxDeathFired)
        {
            return;
        }

        double before = regen.Remaining;
        regen.OnQualifyingClimax();
        PleasureRuntime.LogTransition(
            $"The lust mark turned a climax into recovery: the succubus buff runs for "
            + $"{regen.Remaining:F1}s (was {before:F1}s).");
    }

    /// <summary>
    /// Spends the buff and hands back what it restored (SPEC005 5.1 回復, FR-406).
    ///
    /// Suspended rather than spent whenever the game is not actually running. A buff that drained
    /// behind a pause menu, through a defeat performance or over a death would be a buff the player
    /// paid a climax for and never received.
    /// </summary>
    [HideFromIl2Cpp]
    private static void UpdateRegenBuff(double delta, bool dead)
    {
        RegenBuffTrack? regen = PleasureRuntime.Regen;
        if (regen is null || !regen.IsActive)
        {
            return;
        }

        if (dead)
        {
            // The run is over, so the recovery it bought is over with it (FR-404).
            PleasureRuntime.DiscardRegen("the player died");
            return;
        }

        // An event playing is its own condition, not a paused clock: a cutscene runs at ordinary
        // time scale, and spending the buff behind one would burn what a climax was paid for on
        // something the player is only watching (FR-406).
        if (delta <= 0d
            || Time.timeScale <= 0f
            || PleasureRuntime.IsDefeatPerformance
            || IsCinematic())
        {
            return;
        }

        RegenTick tick = regen.Advance(delta);
        if (!tick.IsEmpty)
        {
            PlayerVitals.Restore(tick.Hp, tick.Mp);
        }

        if (!regen.IsActive)
        {
            PleasureRuntime.LogTransition("The succubus regeneration buff ran out.");
        }
    }

    /// <summary>
    /// Turns the climax limit into a death (SPEC003 5.5.2, 5.5.3, FR-215, FR-216).
    ///
    /// Asked here and nowhere else. There is no per-frame test for "is the count at the limit",
    /// because a save stored at or above it would then kill the player the instant it finished
    /// loading. Reaching the limit is an event, and the event is a climax (DEC-258).
    /// </summary>
    [HideFromIl2Cpp]
    private void ApplyClimaxLimit(Lelia lelia, PlayerStatusManager status, bool dead)
    {
        ClimaxTuning tuning = PleasureRuntime.Profile.Climax;
        float durability = _lastMaxDurability;
        try
        {
            durability = status.m_MaxDurability;
        }
        catch (Exception)
        {
            // Falls back to the base limit, which FR-214 requires rather than collapsing to 0.
        }

        int count = PleasureRuntime.Climaxes.Count;
        if (!ClimaxLethality.ShouldBeLethal(
                tuning, count, durability, dead, PleasureRuntime.ClimaxDeathFired))
        {
            int limit = ClimaxLimit.Compute(tuning.LimitBase, tuning.LimitPerDurability, durability);
            if (limit > 0 && count >= limit && !tuning.GameOverEnabled)
            {
                PleasureRuntime.Log?.LogInfo(
                    $"Climax {count} of {limit}: the limit has been reached, but "
                    + "Climax.EnableClimaxGameOver is off, so the run continues (FR-279).");
            }

            return;
        }

        // Latched before the attempt rather than after it. The performance that follows keeps
        // running the observer, and a second pass finding the count still at the limit would ask
        // for the death again.
        PleasureRuntime.ClimaxDeathFired = true;
        PlayerHealth.Kill(lelia, $"climax {count} reached the limit");
    }

    /// <summary>
    /// Rolls for a stagger when the player acts on an empty MP bar (SPEC005 5.3, FR-409, FR-410).
    ///
    /// The conditions are an AND and every one of them is checked here rather than inside the
    /// scheduler, because every one of them is a question only the game can answer. Wearing the
    /// crest is not enough on its own: an enemy can put it on a barely-corrupted player, and
    /// punishing that player for a state they were handed rather than earned is not what this is
    /// for (DEC-405).
    ///
    /// A hold, an event, the gallery, a defeat performance and a paused clock all suppress it
    /// outright. A stagger played over any of those is how a penalty becomes a progression bug
    /// (DEC-404).
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateMpPenalty(bool bound, bool dead)
    {
        MpZeroStunScheduler? scheduler = PleasureRuntime.Stun;
        MpPenaltyTuning tuning = PleasureRuntime.Profile.MpPenalty;
        if (scheduler is null)
        {
            return;
        }

        // Read every frame the debug panel is open, even when the penalty is switched off. What
        // makes "it is not firing" diagnosable is seeing which of the seven conditions is false and
        // whether the keys are being read at all; a mechanism that reports nothing until it is
        // already working is the one that got reported as unverifiable (利用者REVIEW 2026-08-10).
        bool wantDiagnostics = _mpPanel;
        if (!tuning.HasEffect && !wantDiagnostics)
        {
            return;
        }

        MpPenaltyState state = ReadMpPenaltyState(tuning, bound, dead);
        _mpState = state;

        if (!tuning.HasEffect)
        {
            // Diagnostics only. The scheduler is deliberately not advanced: an inert penalty must
            // not accumulate press counts that would make it look as though it had been rolling.
            return;
        }

        // The scheduler is told about the inputs whether or not the conditions hold, so a key that
        // was already down when they became true is not mistaken for a fresh press.
        // The roll is deferred, not drawn here. UnityEngine.Random is the game's own generator, and
        // taking a value from it every frame would reshuffle every roll the game and the other MODs
        // make from it — for a lottery that only runs on a press (§10).
        if (!scheduler.Evaluate(
                state.ConditionsMet,
                state.HeldInputs,
                Time.unscaledTimeAsDouble,
                static () => UnityEngine.Random.value))
        {
            return;
        }

        // A last, redundant guard, separate from the condition set above. While bound, the arrow
        // keys are the resistance input, and playing a stagger over resistance is strictly
        // forbidden (利用者指示 2026-08-10): it would punish the exact input the hold demands. The
        // condition set already excludes a hold; this line is here so no future edit to that set
        // can quietly remove the rule.
        if (bound || PleasureRuntime.IsBound)
        {
            return;
        }

        PlayStagger();
    }

    /// <summary>
    /// Every fact the MP0 penalty turns on, read once a frame (SPEC005 5.3).
    ///
    /// One struct rather than seven booleans computed inline, so the debug panel shows exactly the
    /// values the rule ran on rather than re-reading them a frame later and disagreeing.
    /// </summary>
    private readonly record struct MpPenaltyState(
        bool Corrupted,
        float CorruptionFraction,
        bool CrestWorn,
        bool MpLow,
        float MpFraction,
        int Mp,
        int MpMax,
        bool Bound,
        bool Dead,
        bool Paused,
        bool Cinematic,
        IReadOnlyCollection<string> HeldInputs,
        IReadOnlyCollection<string> AllInputsDown)
    {
        public bool ConditionsMet =>
            Corrupted && CrestWorn && MpLow && !Bound && !Dead && !Paused && !Cinematic;
    }

    [HideFromIl2Cpp]
    private MpPenaltyState ReadMpPenaltyState(MpPenaltyTuning tuning, bool bound, bool dead)
    {
        CorruptionTrack? corruption = PleasureRuntime.Corruption;
        float fraction = corruption is not null && corruption.Cap > 0f
            ? corruption.Value / corruption.Cap
            : 0f;

        var mp = -1;
        var mpMax = -1;
        try
        {
            var bar = PlayerVitals.Mp;
            if (bar is not null)
            {
                mp = bar.Current;
                mpMax = bar.Max;
            }
        }
        catch (Exception)
        {
            // Left at -1, which the panel renders as unreadable rather than as zero.
        }

        // Both the configured set and every known input. The second is what answers "is the key
        // being read at all" when the configured set is the thing under suspicion.
        var held = new List<string>(tuning.TriggerInputs.Count);
        foreach (string input in tuning.TriggerInputs)
        {
            if (IsInputDown(input))
            {
                held.Add(input);
            }
        }

        var down = new List<string>(StunInputs.Known.Count);
        foreach (string input in StunInputs.Known)
        {
            if (IsInputDown(input))
            {
                down.Add(input);
            }
        }

        return new MpPenaltyState(
            corruption is not null && corruption.Cap > 0f && fraction >= tuning.CorruptionFraction,
            fraction,
            PleasureRuntime.IsCrestWorn,
            PlayerVitals.IsMpLow(tuning.MpFraction),
            PlayerVitals.MpFraction,
            mp,
            mpMax,
            bound,
            dead,
            Time.timeScale <= 0f,
            IsCinematic(),
            held,
            down);
    }

    /// <summary>
    /// The keys each action is bound to (利用者確認 2026-08-10).
    ///
    /// X attack, C sword magic, V bow magic, Z jump, ←→ move. Down is crouch and is deliberately
    /// absent: crouching is how you wait, and punishing waiting is not what the penalty is for.
    /// </summary>
    [HideFromIl2Cpp]
    private static string? ActionFor(KeyCode key) => key switch
    {
        KeyCode.X or KeyCode.JoystickButton2 => StunInputs.Attack,
        KeyCode.C or KeyCode.V or KeyCode.JoystickButton3 => StunInputs.Magic,
        KeyCode.Z or KeyCode.JoystickButton0 => StunInputs.Jump,
        KeyCode.LeftArrow or KeyCode.RightArrow => StunInputs.Move,
        _ => null,
    };

    /// <summary>
    /// Records action keys from IMGUI's own key events (付録A A-403).
    ///
    /// The events are observed and never consumed: the game reads its input through its own manager
    /// rather than through IMGUI, but consuming here would still be taking something that was not
    /// ours to take.
    ///
    /// This exists because polling <c>UnityEngine.Input</c> reports nothing on this build — the
    /// panel's key row sat at <c>--</c> through every press (利用者REVIEW 2026-08-10). IMGUI key
    /// events are the reading that demonstrably arrives here, since every debug key in this file
    /// is driven by them. Raw key codes are tracked rather than action names, so releasing one
    /// arrow while the other is held does not read as "movement stopped".
    /// </summary>
    [HideFromIl2Cpp]
    private void ObserveActionKeys(UnityEngine.Event current)
    {
        try
        {
            if (current.type == EventType.KeyDown)
            {
                if (ActionFor(current.keyCode) is not null)
                {
                    _eventKeys.Add(current.keyCode);
                }
            }
            else if (current.type == EventType.KeyUp)
            {
                _eventKeys.Remove(current.keyCode);
            }
        }
        catch (Exception)
        {
            // An unreadable event is not evidence of a press.
        }
    }

    /// <summary>
    /// Whether one action is being asked for right now.
    ///
    /// Two readings, unioned. Polling <c>UnityEngine.Input</c> is tried first and is the better one
    /// when it works — it is frame-accurate and sees the pad — but on this build it answers nothing
    /// at all, so IMGUI's key events carry it. Which of the two is answering is reported on the F4
    /// panel rather than inferred, because "the key is not pressed" and "the API cannot say" are
    /// the two readings that had to be told apart (付録A A-403).
    /// </summary>
    [HideFromIl2Cpp]
    private bool IsInputDown(string input)
    {
        if (PollInputDown(input))
        {
            _inputPollAnswered = true;
            return true;
        }

        foreach (KeyCode key in _eventKeys)
        {
            if (string.Equals(ActionFor(key), input, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The legacy polling reading, with its failure recorded rather than swallowed.
    ///
    /// The first version caught and returned false, which is exactly why the panel could not say
    /// whether the key was up or the API was refusing: both looked like <c>--</c>.
    /// </summary>
    [HideFromIl2Cpp]
    private bool PollInputDown(string input)
    {
        if (_inputPollBroken is not null)
        {
            return false;
        }

        try
        {
            return input switch
            {
                StunInputs.Attack => Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.JoystickButton2),
                StunInputs.Magic => Input.GetKey(KeyCode.C)
                    || Input.GetKey(KeyCode.V)
                    || Input.GetKey(KeyCode.JoystickButton3),
                StunInputs.Jump => Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.JoystickButton0),
                StunInputs.Move => Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow),
                _ => false,
            };
        }
        catch (Exception exception)
        {
            _inputPollBroken = exception.GetType().Name + ": " + exception.Message;
            PleasureRuntime.Log?.LogInfo(
                "A-403: polling UnityEngine.Input is not available on this build "
                + $"({_inputPollBroken}). The MP0 penalty reads the action keys from IMGUI key "
                + "events instead, which is what the debug keys already use.");
            return false;
        }
    }

    /// <summary>
    /// Plays the game's own no-MP stagger (SPEC005 FR-411).
    ///
    /// The playback path has not been settled. The ISIL reading (付録A A-401, 2026-08-10) found
    /// what the vanilla stagger actually is — the magic action running in its empty-shot mode
    /// (<c>MagicArrow.IsEmptyShot</c>: MP is checked in <c>MagicArrow.OnUpdateAction</c> and spent
    /// in <c>_CreateArrow</c> via <c>SubMPForMagic</c>) — but not a way to run that motion from
    /// outside without starting a cast the player never input, which would break the rule that the
    /// take being played must describe what is actually happening (SPEC003 FR-228, SPEC001).
    ///
    /// Until that is solved, a fire is reported rather than shown. Reported every time, not once:
    /// the rule — the AND conditions, the press edges, the cooldown — is the part that can be
    /// verified in game today, and a mechanism that went silent after its first fire was reported
    /// as unverifiable (利用者REVIEW 2026-08-10). The cooldown keeps the log honest about the rate
    /// the penalty would actually fire at.
    /// </summary>
    [HideFromIl2Cpp]
    private void PlayStagger()
    {
        Lelia? lelia = null;
        try
        {
            lelia = ManagerList.Object?.Lelia;
        }
        catch (Exception)
        {
            // Handled below as "no player to stagger".
        }

        AttackActionBase? slot = EquippedMagicSlot(lelia);
        if (slot is null)
        {
            if (!_stunUnavailableLogged)
            {
                _stunUnavailableLogged = true;
                PleasureRuntime.Log?.LogWarning(
                    "The MP0 penalty fired but no magic is equipped in either slot, so there is no "
                    + "cast to fail and nothing was played. The rule is still being applied; only "
                    + "the motion is missing.");
            }

            return;
        }

        try
        {
            // Already mid-cast: leave it alone. Pressing into a running action would either be
            // ignored or start a combo, and neither is a stagger.
            if (slot.IsAction)
            {
                return;
            }

            // The game's own input seam. PressedThisFrame() writes the flag the action actually
            // reads — MagicSword.OnUpdateAction tests it at Call+17 before anything else — and the
            // action takes it from there: it asks PlayerStatusManager.UnUsedMagic, finds no MP
            // (which is condition 3 of this very penalty), and runs the cast in its empty branch.
            // That is the vanilla MP0 stagger, animation and action lock and all (A-401:
            // AnimState.Magic_Sword1_Empty, the take the user identified in the gallery).
            //
            // It has to be the equipped slot, not the MagicSword object. Lelia keeps the three
            // implementations at +480/+488/+496 and the two equipped slots at +568/+584, and
            // Lelia.OnResponseSection calls UpdateAction on the slots (and Melee) and never on the
            // implementations. Pressing MagicSword directly set a flag on an object whose
            // OnUpdateAction is never run, which is why the first version logged a fire every time
            // and played nothing (利用者REVIEW 2026-08-10).
            //
            // Nothing about the motion is chosen here, and no animator state is written. The MOD
            // says "the button went down"; the game decides what that means. It also cannot cast
            // by accident — MP is empty by the time this runs, so the empty branch is the only one
            // reachable. This is what keeps FR-228 honest: the take being played describes what is
            // actually happening, because she really did try.
            slot.PressedThisFrame();
            slot.Pressed();

            PleasureRuntime.Probe(
                "mp0-stagger-played",
                "A-401 answered: the MP0 stagger is played by pressing the equipped magic slot "
                + "(Lelia.Magic01/Magic02, AttackActionBase.PressedThisFrame) while MP is empty. "
                + "The game runs its own empty-cast branch and plays the empty animation itself.");
            PleasureRuntime.Log?.LogInfo(
                "[SPEC005] MP0 penalty fired: the body tried to cast with nothing to cast, and the "
                + "game's own empty-cast stagger is playing.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The MP0 stagger could not be started: {exception.Message}");
        }
    }

    /// <summary>
    /// The equipped magic the stagger is pressed through (A-401).
    ///
    /// Lelia holds the magic implementations (<c>MagicArrow</c>, <c>MagicSword</c>,
    /// <c>MagicOwl</c>) separately from the two slots the player has equipped, and only the slots
    /// — with <c>Melee</c> — are handed to <c>UpdateAction</c> each section. So the slot is the
    /// only object whose press is ever read.
    ///
    /// The sword slot is preferred because its empty cast is the motion the take names
    /// (<c>Magic_Sword1_Empty</c>); any other equipped magic has an empty animation of its own and
    /// is an equally honest failure to cast, so it is taken rather than playing nothing.
    /// </summary>
    [HideFromIl2Cpp]
    private static AttackActionBase? EquippedMagicSlot(Lelia? lelia)
    {
        if (lelia is null)
        {
            return null;
        }

        AttackActionBase? first = null;
        try
        {
            foreach (AttackActionBase? candidate in new[] { lelia.Magic01, lelia.Magic02 })
            {
                if (candidate is null)
                {
                    continue;
                }

                if (candidate.TryCast<MagicSword>() is not null)
                {
                    return candidate;
                }

                first ??= candidate;
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The MP0 stagger could not read the equipped magic slots: {exception.Message}");
        }

        return first;
    }

    /// <summary>
    /// The SPEC005 panel (F4): what each of its four mechanisms is doing right now.
    ///
    /// Written because three of the four ship inert and the fourth has no animation, which between
    /// them make the whole of SPEC005 unobservable from inside the game. Every line here answers a
    /// question that was actually asked: is it switched on, is the state right, are the keys even
    /// being read, and if it is not firing, which gate turned it away.
    ///
    /// Read-only. Nothing on this panel changes a setting; the forcing key is separate and says so.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawSpec005Panel()
    {
        if (!_mpPanel)
        {
            return;
        }

        PleasureProfile profile = PleasureRuntime.Profile;
        MpPenaltyTuning mp = profile.MpPenalty;
        MpPenaltyState state = _mpState;
        RegenBuffTrack? regen = PleasureRuntime.Regen;
        MpZeroStunScheduler? stun = PleasureRuntime.Stun;
        CorruptionTrack? corruption = PleasureRuntime.Corruption;

        var lines = new List<string>(24)
        {
            "=== SPEC005 堕落バフ ===   F4 close   F2 force the MP0 penalty",
            string.Empty,
            "-- crest / corruption --",
            $"corruption {corruption?.Value ?? 0f:F2} / {corruption?.Cap ?? 0f:F2}"
                + $"  ({state.CorruptionFraction:P0})",
            $"crest level {PleasureRuntime.CrestLevel} of {PleasureRuntime.CrestMaxLevel}"
                + $"   sublimated {(PleasureRuntime.CrestSublimated ? "YES" : "no")}",
            $"corruption gain x{PleasureRuntime.CrestCorruptionScale:0.###}"
                + $"   (curse ceiling +{profile.Corruption.CurseGainMax:0.##},"
                + $" sublimated x{profile.Corruption.ScaleFor(0, PleasureRuntime.CrestMaxLevel, true):0.##})",
            $"pleasure gain x{(PleasureRuntime.CrestSublimated ? profile.Pleasure.CrestScale : 1f):0.###}"
                + $"   (crest term {profile.Pleasure.CrestScale:0.##} once sublimated)",
            string.Empty,
            "-- succubus regen buff --",
            profile.Regen.HasEffect
                ? $"{regen?.Remaining ?? 0d:F1}s left"
                    + $"   {profile.Regen.HpPerSecond:0.##} HP/s, {profile.Regen.MpPerSecond:0.##} MP/s"
                    + $"   (+{profile.Regen.DurationPerClimax:0.#}s per climax)"
                : "INERT — set Regen.RegenDurationPerClimax and HpRegenPerSecond/MpRegenPerSecond",
            $"MP recovery path: {PleasureRuntime.MpRecoveryWorks switch
            {
                true => "works",
                false => "does NOT work on this build",
                _ => "not tried yet",
            }}",
            string.Empty,
            "-- MP0 penalty --",
            mp.HasEffect
                ? $"ON   chance {mp.Chance:P0} per press   cooldown {mp.CooldownSeconds:0.#}s"
                    + $"   MP below {mp.MpFraction:P0}   inputs {string.Join("/", mp.TriggerInputs)}"
                : "OFF — set MpPenalty.Enabled=true AND MpPenalty.StunChance above 0",
            $"conditions: {DescribeMpConditions(state, mp)}",
            $"=> {(state.ConditionsMet ? "ALL MET (a press would roll)" : "NOT MET")}",
        };

        // The row that answers "is anything being read at all". Every known input, not just the
        // configured ones, because when the configured set is what is under suspicion, a set that
        // shows nothing is indistinguishable from an input API that returns nothing.
        var keys = new List<string>(StunInputs.Known.Count);
        foreach (string input in StunInputs.Known)
        {
            bool down = state.AllInputsDown.Contains(input);
            bool armed = mp.TriggerInputs.Contains(input);
            keys.Add($"{input}{(armed ? string.Empty : "(off)")}:{(down ? "DOWN" : "--")}");
        }

        lines.Add($"keys now: {string.Join("  ", keys)}");
        lines.Add(
            "key source: "
            + (_inputPollBroken is not null
                ? $"IMGUI events (UnityEngine.Input failed — {_inputPollBroken})"
                : _inputPollAnswered
                    ? "UnityEngine.Input polling + IMGUI events"
                    : "IMGUI events (UnityEngine.Input has never reported a press)"));
        lines.Add(
            stun is null
                ? "scheduler: absent"
                : $"presses {stun.PressCount}   rolls {stun.RollCount}   fires {stun.FireCount}"
                    + $"   cooldown {stun.CooldownRemainingAt(Time.unscaledTimeAsDouble):F1}s");
        lines.Add($"last press: {stun?.LastOutcome ?? "(none seen)"}");
        lines.Add(string.Empty);
        lines.Add(
            "stagger: the game's own empty-cast (AnimState.Magic_Sword1_Empty), started by "
            + "pressing its sword magic action while MP is empty.");

        // Right-hand side. The left is where the game keeps the portrait and its gauges, and the
        // panel sat on top of all of it (利用者REVIEW 2026-08-10). Nothing of the game's own lives
        // in the top right.
        const float width = 720f;
        const float margin = 16f;
        const float lineHeight = 19f;
        float height = (lines.Count * lineHeight) + 20f;
        float left = Math.Max(margin, Screen.width - width - margin);

        // Drawn as a flat fill rather than a GUI.Box: the default skin's box is translucent and
        // the text underneath it was competing with whatever the scene happened to be.
        OverlayPainter.Fill(new Rect(left, margin, width, height), new Color(0.04f, 0.03f, 0.06f, 0.88f));
        OverlayPainter.Fill(new Rect(left, margin, width, 2f), new Color(1f, 0.45f, 0.72f, 0.85f));

        for (var index = 0; index < lines.Count; index++)
        {
            string line = lines[index];

            // Three colours, and each one means something: a heading, a state that stops the
            // mechanism, and everything else. Reading the panel should not require reading it all.
            Color colour = line.StartsWith("===", StringComparison.Ordinal)
                ? new Color(1f, 0.65f, 0.82f, 1f)
                : line.Contains("NO ", StringComparison.Ordinal)
                    || line.Contains("NOT ", StringComparison.Ordinal)
                    || line.Contains("OFF", StringComparison.Ordinal)
                    || line.Contains("INERT", StringComparison.Ordinal)
                    || line.Contains("BOUND", StringComparison.Ordinal)
                    || line.Contains("BELOW", StringComparison.Ordinal)
                        ? new Color(1f, 0.78f, 0.42f, 1f)
                        : line.Contains("ALL MET", StringComparison.Ordinal)
                            || line.Contains("DOWN", StringComparison.Ordinal)
                            ? new Color(0.62f, 1f, 0.68f, 1f)
                            : new Color(0.92f, 0.92f, 0.95f, 1f);

            OverlayPainter.Text(
                new Rect(left + 12f, margin + 8f + (index * lineHeight), width - 24f, lineHeight),
                line,
                colour);
        }
    }

    /// <summary>
    /// Fires one stagger if the state allows it, without waiting on a press, a roll or a cooldown.
    ///
    /// The conditions are still every one of them. What this removes is only the waiting: a
    /// probability the player cannot see and a cooldown they cannot time. If it reports that the
    /// conditions were not met, the panel above says which one, and that is the answer rather than
    /// a failure of the key.
    /// </summary>
    [HideFromIl2Cpp]
    private void ForceMpPenaltyForDebugging()
    {
        MpPenaltyTuning tuning = PleasureRuntime.Profile.MpPenalty;
        MpPenaltyState state = ReadMpPenaltyState(tuning, PleasureRuntime.IsBound, IsPlayerDead());
        _mpState = state;

        if (!state.ConditionsMet)
        {
            PleasureRuntime.Log?.LogInfo(
                "Shift+F4: the MP0 penalty was not fired because the conditions are not met — "
                + $"{DescribeMpConditions(state, tuning)}. The conditions are never bypassed; press "
                + "F4 to watch them.");
            return;
        }

        PleasureRuntime.Log?.LogInfo(
            "Shift+F4: forcing the MP0 penalty. The press edge, the roll and the cooldown are "
            + "short-circuited; the conditions were all met.");
        PlayStagger();
    }

    /// <summary>Why the conditions do or do not hold, as one readable line.</summary>
    [HideFromIl2Cpp]
    private static string DescribeMpConditions(in MpPenaltyState state, MpPenaltyTuning tuning)
    {
        var parts = new List<string>(7);
        parts.Add(state.CrestWorn ? "crest worn" : "NO crest");
        parts.Add(state.Corrupted
            ? $"corruption {state.CorruptionFraction:P0} >= {tuning.CorruptionFraction:P0}"
            : $"corruption {state.CorruptionFraction:P0} BELOW {tuning.CorruptionFraction:P0}");
        parts.Add(state.MpFraction < 0f
            ? "MP UNREADABLE"
            : state.MpLow
                ? $"MP {state.Mp}/{state.MpMax} ({state.MpFraction:P0}) < {tuning.MpFraction:P0}"
                : $"MP {state.Mp}/{state.MpMax} ({state.MpFraction:P0}) NOT below {tuning.MpFraction:P0}");
        if (state.Bound)
        {
            parts.Add("BOUND");
        }

        if (state.Dead)
        {
            parts.Add("DEAD");
        }

        if (state.Paused)
        {
            parts.Add("PAUSED");
        }

        if (state.Cinematic)
        {
            parts.Add("CINEMATIC");
        }

        return string.Join(", ", parts);
    }

    /// <summary>Whether the game currently has the player in a defeat state.</summary>
    [HideFromIl2Cpp]
    private static bool IsPlayerDead()
    {
        try
        {
            return ManagerList.Object?.Lelia?.IsHP0 == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether an event or the gallery is running, which suppresses the penalty.</summary>
    [HideFromIl2Cpp]
    private static bool IsCinematic()
    {
        try
        {
            if (ManagerList.Object?.IsCinematicEvent == true)
            {
                return true;
            }

            return ManagerList.Gallery?.CurrentTakePlayer is not null;
        }
        catch (Exception)
        {
            // Unreadable is not evidence that nothing is playing, and the penalty must not fire on
            // a state nobody confirmed.
            return true;
        }
    }

    [HideFromIl2Cpp]
    private void DecayWhenFree(bool bound, bool dead)
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

        // Being held does not suspend the buff. A hold is where a body that recovers from being
        // used is meant to be worth having (SPEC005 5.1 効果時間).
        UpdateRegenBuff(delta, dead);

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
        else
        {
            PleasureRuntime.EnterNoSlot();
        }
        PleasureRuntime.Log?.LogInfo(
            $"[probe] A-9: save slot is SelectID={selectId}, LoadedFileName='{file ?? "(null)"}', "
            + $"IsAutoSave={main.IsAutoSave}, sidecar key='{SlotKey.Compose(selectId, file) ?? "(none)"}'.");
    }

    /// <summary>
    /// Names the game's own lust statuses, once (SPEC003 付録A A-45, A-46).
    ///
    /// The first version asked the game to load each type on every pass until it arrived, and the
    /// game's loader throws when the same type is requested twice — out of an async continuation,
    /// where nothing can catch it. One request per type, ever.
    ///
    /// The localised name is not read. The template's <c>AbnormalNameID</c> is <c>None</c> until
    /// the status is on someone (付録A A-14), so looking it up returned the localisation table's
    /// own error string, which is worse than saying nothing.
    /// </summary>
    [HideFromIl2Cpp]
    private void ReportLustStatuses()
    {
        if (_lustReported || !PleasureRuntime.Profile.ProbeMeasurements)
        {
            return;
        }

        AbnormalManager? manager = ManagerList.Abnormal;
        if (manager is null)
        {
            return;
        }

        var pending = false;
        foreach (AbnormalType type in new[]
                 {
                     AbnormalType.LustMarkCurse, AbnormalType.Lustfull, AbnormalType.Lustfull_Forever,
                 })
        {
            try
            {
                AbnormalData? data = null;
                if (!manager.TryGetData(type, out data) || data is null)
                {
                    if (_lustAsked.Add(type))
                    {
                        manager.LoadAbnormalData(type, ManagerList.RootTokenSource.Token);
                        PleasureRuntime.Log?.LogInfo(
                            $"A-45: {type} has no data loaded yet; the game has been asked for it, once.");
                    }

                    pending = true;
                    continue;
                }

                PleasureRuntime.Log?.LogInfo(
                    $"A-45: {type} maxLevel={data.MaxLevel}, haanjaCanCure={data.HaanjaCanCure}, "
                    + $"physicalConditionFlag={data.PhysicalConditionFlag}.");
            }
            catch (Exception exception)
            {
                PleasureRuntime.Log?.LogWarning($"A-45: {type} could not be read: {exception.Message}");
            }
        }

        _lustReported = !pending;
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

        // The crest is a wide banner rather than a disc, so its marker has to be too. A square ring
        // around it would say the element is a different size from the one being dragged.
        float halfWidth = _editingElement == 3 ? half * CrestAspect() : half;

        var tint = new Color(1f, 0.85f, 0.35f, 0.85f);
        const float edge = 2f;
        OverlayPainter.Fill(new Rect(x - halfWidth, y - half, halfWidth * 2f, edge), tint);
        OverlayPainter.Fill(new Rect(x - halfWidth, y + half - edge, halfWidth * 2f, edge), tint);
        OverlayPainter.Fill(new Rect(x - halfWidth, y - half, edge, half * 2f), tint);
        OverlayPainter.Fill(new Rect(x + halfWidth - edge, y - half, edge, half * 2f), tint);
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

        // Observed before anything else, and never consumed. This is the second reading of the
        // action keys, and on this build it is the only one that answers: polling UnityEngine.Input
        // returns nothing here (`keys now: --`, 利用者REVIEW 2026-08-10), while IMGUI key events
        // demonstrably arrive — every debug key below is driven by them.
        ObserveActionKeys(current);

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

        // F7 fires a climax and F8 adds one step of corruption. Neither is a shortcut around the
        // mechanism: F7 sets the same pending flag a full gauge sets, and F8 goes through the same
        // gain path a sexual hit does, crest multiplier and all. What they save is the play needed
        // to reach the state, which for corruption is measured in tens of climaxes — long enough
        // that "I cannot tell whether this works" is the likely outcome of asking for it in play.
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F7)
        {
            ClimaxForDebugging();
            current.Use();
            return;
        }

        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F8)
        {
            CorruptForDebugging();
            current.Use();
            return;
        }

        // F4 opens the SPEC005 panel. Every mechanism SPEC005 adds ships inert or invisible — the
        // regen buff is off until it is tuned, the corruption staging only shows up as a rate, and
        // the MP0 penalty has no animation to play — so without somewhere to read their live state
        // the only available report is "I cannot tell whether this works" (利用者REVIEW 2026-08-10).
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F4)
        {
            _mpPanel = !_mpPanel;
            current.Use();
            return;
        }

        // F2 forces one stagger. A key of its own rather than Shift+F4: the modified press was
        // reported as not working, and a plain function key is the mechanism every other debug key
        // here already proves (利用者REVIEW 2026-08-10). Nothing is gained by asking a modifier to
        // be reliable when the thing being debugged is input reliability.
        //
        // The force short-circuits the press edge, the roll and the cooldown and nothing else: the
        // seven conditions are still required, so what it proves is that the effect fires when the
        // state is right, without waiting on a probability. Bypassing the conditions instead would
        // be testing a different mechanism (the line SPEC004 DEC-316 draws for its debug ops).
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.F2)
        {
            ForceMpPenaltyForDebugging();
            current.Use();
            return;
        }

        // Opens the diary in a browser (SPEC006 FR-614). Configurable rather than fixed, because it
        // is the one key here a player is meant to use — the rest are for debugging — and the game
        // shares its function keys with whatever else is installed. None means the key is off, which
        // is also what an unreadable name resolves to.
        if (current.type == EventType.KeyDown
            && PleasureRuntime.StatsPageKey != KeyCode.None
            && current.keyCode == PleasureRuntime.StatsPageKey)
        {
            StatsPageLauncher.Open(
                PleasureRuntime.StatsPageUrl,
                PleasureRuntime.Log,
                $"{PleasureRuntime.StatsPageKey} was pressed");
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
    /// Fires a climax on demand, so the climax performance can be judged without earning one.
    ///
    /// The same pending flag a full gauge sets, so everything downstream runs: the count, the
    /// corruption gain, the haze and the shake. A separate path that only played the effect would
    /// prove the effect and nothing else.
    /// </summary>
    [HideFromIl2Cpp]
    private void ClimaxForDebugging()
    {
        PleasureRuntime.PendingClimax = true;
        PleasureRuntime.Log?.LogInfo("F7: a climax was forced, for checking the performance.");
    }

    /// <summary>
    /// Steps the corruption through the mark, one part per press, and back to nothing after the
    /// last (SPEC003 FR-269).
    ///
    /// The wrap is the whole point. Corruption is one-way by design, and a debug key that is also
    /// one-way can be used exactly once per playthrough — which makes it useless for the thing it
    /// exists for. So the last press clears the track and takes the game's crest back off, leaving
    /// the run where a fresh one starts. Nothing in play reaches this; it is a keypress.
    ///
    /// Every other press goes through the ordinary gain path, so the crest multiplier applies and
    /// the mark lands at its threshold exactly as it would be earned.
    /// </summary>
    [HideFromIl2Cpp]
    private void CorruptForDebugging()
    {
        CorruptionTrack? corruption = PleasureRuntime.Corruption;
        if (corruption is null || corruption.Cap <= 0f)
        {
            PleasureRuntime.Log?.LogInfo("F8: corruption is switched off, so there is nothing to add.");
            return;
        }

        if (corruption.IsAtCap)
        {
            corruption.LoadFrom(0f);
            PleasureRuntime.PendingLustCrest = false;
            PleasureRuntime.CrestSublimated = false;
            RemoveLustCrestForDebugging();
            PleasureRuntime.Log?.LogInfo(
                "F8: corruption was at the cap, so it has been wound back to nothing and the lust "
                + "crest taken off. The next press starts the mark again.");
            return;
        }

        // Divided by the multiplier so one press is one part whether or not the crest is worn.
        // The gain still goes through the real path — the scale is applied there — but a debug key
        // that jumped two parts once the crest was on skipped the very steps it exists to show.
        float scale = PleasureRuntime.CrestCorruptionScale;
        float step = corruption.Cap / LustCrestArt.PartCount / Math.Max(1f, scale);
        PleasureRuntime.GainCorruption(step);

        var parts = (int)Math.Floor((corruption.Value / corruption.Cap) * LustCrestArt.PartCount);
        PleasureRuntime.Log?.LogInfo(
            $"F8: corruption is {corruption.Value:0.##} of {corruption.Cap:0.##}; the crest shows "
            + $"{Math.Clamp(parts, 0, LustCrestArt.PartCount)} of {LustCrestArt.PartCount} parts. "
            + $"The lust crest is {(PleasureRuntime.IsCrestWorn ? "worn" : "not worn")}. "
            + "At the cap, one more press winds it all back.");
    }

    /// <summary>Takes the game's crest off, so the threshold can be crossed again.</summary>
    [HideFromIl2Cpp]
    private static void RemoveLustCrestForDebugging()
    {
        try
        {
            AbnormalList? abnormals = PleasureRuntime.PlayerAbnormals;
            if (abnormals is not null && abnormals.Has(AbnormalType.LustMarkCurse))
            {
                abnormals.RemoveAbnormal(AbnormalType.LustMarkCurse);
            }
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The lust crest could not be taken off: {exception.Message}");
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
    /// Works the milk off while <c>BreastSuper</c> is worn, and takes it back down to <c>Breast</c>
    /// when the gauge empties (SPEC003 FR-259, FR-264, FR-262).
    ///
    /// This is the whole of the escalation's duration. There is no key and no separate clock. The
    /// player endures it, and the same sexual attacks that filled the gauge keep filling it — so
    /// how long it lasts is decided by how the next half minute goes rather than by a number.
    ///
    /// Below the escalation nothing drains: the gauge is a one-way countdown towards it, and a
    /// swelling that leaked back down would make the escalation something you could simply avoid by
    /// waiting.
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateMilk(PlayerStatusManager status, double delta)
    {
        MilkReservoir? milk = PleasureRuntime.Milk;
        AbnormalList? abnormals = status.AbnormalList;
        if (milk is null || abnormals is null)
        {
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

        if (!super)
        {
            _milkAnnounced = false;
            return;
        }

        if (!_milkAnnounced)
        {
            _milkAnnounced = true;
            PleasureRuntime.Log?.LogInfo(
                $"BreastSuper has to be endured until the milk gauge empties. It is at "
                + $"{milk.Fill:P0}, falling {PleasureRuntime.Profile.BreastSuper.MilkDrainPerSecond:P1} "
                + "a second, and sexual attacks put it back up.");
        }

        // Not while held or downed. Recovering out of a hold would make being caught something to
        // be waited out rather than escaped, and the attacks landing there are already filling the
        // gauge from the other side.
        if (PleasureRuntime.IsBound || PleasureRuntime.IsDefeatPerformance)
        {
            return;
        }

        if (milk.Tick(delta) != MilkOutcome.Emptied)
        {
            return;
        }

        // Down to Breast, not to nothing. Working the escalation off still leaves the swelling;
        // otherwise enduring would be better than being cured.
        abnormals.RemoveAbnormal(AbnormalType.BreastSuper);
        PortraitRefresh.Refresh("return to Breast", null);
        AbnormalManager? manager = ManagerList.Abnormal;
        AbnormalData? data = null;
        if (manager is not null && manager.TryGetData(AbnormalType.Breast, out data) && data is not null)
        {
            abnormals.AddAbnormal(data, 1, null);
        }

        PleasureRuntime.Breasts?.Reset();
        _milkAnnounced = false;
        BeginTransition();
        PleasureRuntime.Log?.LogInfo("The milk ran out: BreastSuper subsided to Breast.");
    }

    /// <summary>
    /// Notes that this enemy has been met and what it is called, so the editor can offer the handful
    /// that matter ahead of the two hundred that do not (SPEC003 FR-282). Written through only when
    /// the catalogue learned something: the first sighting, or a display name that has changed.
    /// </summary>
    [HideFromIl2Cpp]
    private static void RecordSighting(BinderIdentity? binder)
    {
        if (binder is null)
        {
            return;
        }

        // A-53: which of 5.3.1's tiers actually answers. If most captors fall through to the object
        // name, the catalogue grows a row per enemy met rather than matching a listed one, and that
        // is worth knowing before trusting the list.
        PleasureRuntime.Probe(
            $"binder-source-{binder.Id}",
            $"A-53: captor '{binder.Id}' was identified by its {binder.Source}"
            + (binder.DisplayName is null
                ? "; no display name was available (A-54)."
                : $"; the game calls it '{binder.DisplayName}' (A-54)."));

        if (!PleasureRuntime.Enemies.MarkSeen(binder.Id, binder.DisplayName))
        {
            return;
        }

        PleasureRuntime.SaveEnemies($"sighting of {binder.DisplayName ?? binder.Id}");
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
                _editingElement = (_editingElement + 1) % 4;
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
            3 => layout with { Crest = change(layout.Crest) },
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
            3 => ("lust crest", layout.Crest),
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
    /// The haze that marks the curse advancing (SPEC005 5.4, FR-413).
    ///
    /// The same wash a climax uses, at a strength set by the stage. Reusing it is the point rather
    /// than an economy: pink over the frame already means "something happened to this body that was
    /// not chosen", and a stock arriving is that same kind of event (DEC-411).
    ///
    /// Nothing here touches <c>Time.timeScale</c> or moves the camera, which is what keeps SPEC001's
    /// pause detection and trigger identification intact (SPEC003 FR-212).
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawCrestProgressFlash()
    {
        double remaining = PleasureRuntime.CrestFxUntil - Time.timeAsDouble;
        if (remaining <= 0d)
        {
            return;
        }

        float duration = Math.Max(0.01f, PleasureRuntime.Profile.CrestFx.DurationSeconds);
        var progress = (float)Math.Clamp(remaining / duration, 0d, 1d);

        // Fades out rather than pulsing. A climax is an event with a peak; this is a state settling
        // onto the body, and it should read as something arriving and staying rather than a flash.
        DrawVignette(Math.Clamp(progress * PleasureRuntime.CrestFxIntensity, 0f, 1f));
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
    /// The milk reservoir, drawn whenever there is any (SPEC003 FR-264).
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
        if (milk is null || (!PleasureRuntime.IsSwollen && milk.Fill <= 0.001f))
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

        // While the escalation is worn the vessel pulses, so the difference between "this is
        // filling towards the escalation" and "this is what has to be worked off" is visible
        // without reading a number.
        if (PleasureRuntime.PlayerAbnormals?.Has(AbnormalType.BreastSuper) == true)
        {
            var pulse = (float)((Math.Sin(Time.unscaledTimeAsDouble * 7d) + 1d) * 0.5d);
            OverlayPainter.Draw(
                new Rect(x - radius, y - radius, diameter, diameter),
                _milkVessel,
                new Color(1f, 1f, 1f, 0.25f + (pulse * 0.35f)));
        }
    }

    /// <summary>
    /// Draws the lust crest, as much of it as the corruption has earned (SPEC003 5.7, FR-266).
    ///
    /// Corruption used to be a number with no face. It is the one axis that never falls, so it is
    /// the one the player most needs to be able to feel, and a figure in the corner is the easiest
    /// thing on a HUD to stop seeing. The mark completing itself says the same thing without a
    /// number: there is less of it left to fill in.
    ///
    /// The pulse is applied here rather than baked into the texture, so every revealed part
    /// breathes together and the whole mark reads as one thing. Slow and shallow on purpose — this
    /// is a state, not an alarm, and something that flashes in the corner of the eye for the whole
    /// run is a thing players turn off.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawCrest()
    {
        CorruptionTrack? corruption = PleasureRuntime.Corruption;
        if (corruption is null || corruption.Cap <= 0f)
        {
            return;
        }

        int parts = LustCrestArt.PartCount;
        var revealed = (int)Math.Floor((corruption.Value / corruption.Cap) * parts);
        revealed = Math.Clamp(revealed, 0, parts);
        if (revealed <= 0)
        {
            // Nothing yet. An empty ring would be a promise the player has not been given a reason
            // to read, and it would claim HUD space for a mechanism that has not started.
            //
            // Except while it is being placed: an element that cannot be seen cannot be dragged,
            // and a fresh save is exactly when someone sets their HUD up.
            if (!_editing || _editingElement != 3)
            {
                return;
            }

            revealed = LustCrestArt.PartCount;
        }

        OverlayPlacement placement = PleasureRuntime.Overlay.Crest;
        float screen = Screen.height;

        // The mark is a wide banner, so its placement size is read as a half-height and the width
        // follows from the shape rather than from the config. A square box would have to be sized
        // for the width and would then leave half of itself empty.
        float half = screen * placement.Size;
        float drawHeight = half * 2f;
        float drawWidth = drawHeight * CrestAspect();
        float x = Screen.width * placement.CentreX;
        float y = screen - (screen * placement.BottomOffset);

        int resolution = Resolution(drawHeight);
        if (_crest is null || _crestParts != revealed || _crestResolution != resolution)
        {
            // Rebuilt only when a part is earned or the window changes size. The build walks every
            // pixel against every stroke, which is far too much to do per frame and costs nothing
            // at the handful of moments it actually happens.
            _crestParts = revealed;
            _crestResolution = resolution;

            // The real image when one has been provided; the drawn approximation otherwise. The
            // requirement is exact overlay with the game's mark, and only projection achieves
            // exactness (FR-270) - but a missing file must cost fidelity, not the mechanism.
            _crest = LustCrestImage.Available
                ? LustCrestImage.Build(resolution, revealed)
                : LustCrestArt.Build(resolution, revealed);
        }

        var pulse = (float)((Math.Sin(Time.unscaledTimeAsDouble * 1.6d) + 1d) * 0.5d);
        float alpha = 0.72f + (pulse * 0.20f);
        OverlayPainter.Draw(
            new Rect(x - (drawWidth * 0.5f), y - half, drawWidth, drawHeight),
            _crest,
            new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>The width-over-height of whichever crest source is in use.</summary>
    [HideFromIl2Cpp]
    private static float CrestAspect() =>
        LustCrestImage.Available ? LustCrestImage.Aspect : LustCrestArt.AspectRatio;

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

    /// <summary>
    /// The pink haze itself.
    ///
    /// Measured against the first version, which was reported as not happening at all. It was
    /// happening: twelve bands over the outer 30% of the frame, the strongest at alpha 0.18, none
    /// of them overlapping. That is a tint the eye discards, and a climax is not a tint — it is the
    /// thing the whole gauge has been counting towards.
    ///
    /// So the bands now overlap deliberately: each is drawn full-width from the edge inwards rather
    /// than as its own slice, so the alpha accumulates towards the edge and falls off smoothly
    /// without IMGUI having a gradient. A wash over the whole frame carries the peak, because a
    /// climax should reach the middle of the screen even though it lives at the edges.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawVignette(float strength)
    {
        if (strength <= 0f)
        {
            return;
        }

        float width = Screen.width;
        float height = Screen.height;

        // The whole frame, faintly. This is what makes the moment arrive rather than creep in from
        // the corners, and it is kept low enough to read the game through.
        OverlayPainter.Fill(
            new Rect(0f, 0f, width, height),
            new Color(1f, 0.45f, 0.72f, strength * 0.16f));

        const int bands = 14;
        float reach = height * 0.42f;
        float step = reach / bands;

        for (var index = 0; index < bands; index++)
        {
            // Drawn from the edge inwards, each band shorter than the last, so they stack. The
            // outermost pixel gets every band's contribution and the innermost gets one.
            float t = 1f - (index / (float)bands);
            float alpha = strength * t * t * 0.085f;
            if (alpha <= 0.002f)
            {
                continue;
            }

            var tint = new Color(1f, 0.38f, 0.68f, alpha);
            float depth = reach - (index * step);
            OverlayPainter.Fill(new Rect(0f, 0f, width, depth), tint);
            OverlayPainter.Fill(new Rect(0f, height - depth, width, depth), tint);
            OverlayPainter.Fill(new Rect(0f, 0f, depth, height), tint);
            OverlayPainter.Fill(new Rect(width - depth, 0f, depth, height), tint);
        }
    }

    [HideFromIl2Cpp]
    private static BinderIdentity? ResolveBinder(Lelia lelia) => BinderIdentityResolver.Resolve(lelia);

    [HideFromIl2Cpp]
    private void Suspend()
    {
        // A hold left open by a scene change would follow the player into the next one (FR-204).
        DamageProbePatches.ReleaseHold();

        _gameplayActive = false;
        PleasureRuntime.IsBound = false;
        PleasureRuntime.BinderEnemyId = null;
        PleasureRuntime.BinderDisplayName = null;
        BinderIdentityResolver.Forget();
        _wasBound = false;

        // Held keys and a running cooldown do not survive the gap. Whatever was down before a
        // scene change was not pressed under the conditions in the next one (SPEC005 FR-416).
        // The observed key set goes too: a key-up delivered while the window had no focus is a
        // key-up nobody saw, and a key left stuck down would stop registering presses entirely.
        _eventKeys.Clear();
        PleasureRuntime.Stun?.Reset();
        PleasureRuntime.CrestFxUntil = 0d;

        // This is the scene-change path — Poll suspends whenever the managers go unready — so it is
        // where the buff has to end (FR-416, AC-414).
        PleasureRuntime.DiscardRegen("play was suspended");

        // Dropped so the first frame back does not charge the buff, the decay and the milk for the
        // whole of the loading screen. Time went on passing while nothing was being advanced, and
        // paying that gap out in one lump is not what any of the three per-second rates mean.
        _lastFrameTime = 0d;
    }
}
