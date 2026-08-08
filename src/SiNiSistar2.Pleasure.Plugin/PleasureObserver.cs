using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;
using UnityEngine;

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
    private bool _editingCross;
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


    public PleasureObserver(IntPtr pointer)
        : base(pointer)
    {
    }

    public void Update()
    {
        try
        {
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
        ReportSelfCheck(status);
        ReportBreastCureSurface(status);
        ApplyPendingBreastSuper(status);
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

        PleasureRuntime.PendingBreastSuper = false;

        AbnormalList? abnormals = status.AbnormalList;
        if (abnormals is null)
        {
            return;
        }

        if (abnormals.Has(AbnormalType.BreastSuper))
        {
            return;
        }

        // Applied before the removal. The other order leaves a frame with neither status, and the
        // body and portrait are driven from the status list.
        abnormals.AddAbnormal(AbnormalType.BreastSuper, 1, null);

        bool applied = abnormals.Has(AbnormalType.BreastSuper);
        if (applied && PleasureRuntime.Profile.BreastSuper.ReplaceBreast)
        {
            abnormals.RemoveAbnormal(AbnormalType.Breast);
        }

        if (!applied)
        {
            PleasureRuntime.Log?.LogWarning(
                "BreastSuper was requested but AbnormalList.Has still reports it absent. The "
                + "escalation is not taking effect; leaving Breast in place.");
            return;
        }

        PleasureRuntime.Log?.LogInfo(
            $"Breast escalated to BreastSuper (Breast "
            + $"{(PleasureRuntime.Profile.BreastSuper.ReplaceBreast ? "removed" : "kept")}). "
            + $"Sensitivity {PleasureRuntime.Sensitivity?.Value ?? 0f:F2}.");
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

        float x;
        float y;
        float half;
        if (_editingCross)
        {
            x = Screen.width * layout.Cross.CentreX;
            y = height - (height * layout.Cross.BottomOffset);
            half = height * layout.Cross.Size * 0.5f;
        }
        else
        {
            x = gaugeX;
            y = gaugeY;
            half = radius;
        }

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
        if (current.type == EventType.KeyDown
            && current.keyCode == KeyCode.F11
            && PleasureRuntime.Profile.EnableDebugKeys)
        {
            ApplyBreastForDebugging();
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
            int before = PleasureRuntime.Breasts?.Count ?? 0;
            abnormals.AddAbnormal(AbnormalType.Breast, 1, null);
            PleasureRuntime.Log?.LogInfo(
                $"F11: Breast applied for debugging. Count was {before}, is now "
                + $"{PleasureRuntime.Breasts?.Count ?? 0}; "
                + $"{PleasureRuntime.Breasts?.Remaining ?? 0} more before BreastSuper.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"F11: Breast could not be applied: {exception.Message}");
        }
    }

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
                _editingCross = !_editingCross;
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
        PleasureRuntime.Overlay = _editingCross
            ? layout with { Cross = change(layout.Cross) }
            : layout with { Gauge = change(layout.Gauge) };
    }

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
            "Layout editing started. Tab picks the gauge or the cross, drag moves it, the wheel "
            + "resizes it, Enter saves and Escape cancels.");
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
            + $"CrossBottomOffset={layout.Cross.BottomOffset:F3}, CrossSize={layout.Cross.Size:F3}.");
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
        OverlayPlacement selected = _editingCross ? layout.Cross : layout.Gauge;
        string name = _editingCross ? "cross" : "gauge";

        GUI.Box(new Rect(12f, 12f, 520f, 96f), GUIContent.none);
        GUI.Label(new Rect(24f, 20f, 500f, 22f), $"Editing the {name}    Tab: switch element");
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
