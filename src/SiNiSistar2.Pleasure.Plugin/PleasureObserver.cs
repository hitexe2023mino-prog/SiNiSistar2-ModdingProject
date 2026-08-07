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
    private bool _labelUnavailable;
    private bool _gameplayActive;
    private Texture2D? _liquid;
    private Texture2D? _cross;
    private Texture2D? _haze;
    private float _liquidFill = -1f;
    private float _liquidPhase;
    private double _liquidBuiltAt;
    private int _crossNotches = -1;
    private bool _crossBroken;
    private int _lastSelectId = int.MinValue;
    private string? _lastSaveFile;
    private double _lastFrameTime;
    private float _lastMaxDurability;


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
        // Only while gameplay is actually running. The title screen, the loading screens and the
        // menus have no player to report on, and a gauge floating over them is plainly wrong.
        if (!PleasureRuntime.Profile.ShowOverlay || !_gameplayActive)
        {
            return;
        }

        try
        {
            DrawGauge();
            DrawClimaxFlash();
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

    public void OnApplicationQuit() => Shutdown();

    [HideFromIl2Cpp]
    public void Shutdown()
    {
        Suspend();
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
        PleasureRuntime.IsDefeatPerformance = lelia.IsHP0;
        _lastMaxDurability = status.m_MaxDurability;
        PleasureRuntime.BinderEnemyId = ResolveBinderId(lelia);

        _gameplayActive = true;
        ReportSelfCheck(status);
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
        PleasureRuntime.Log?.LogInfo(
            $"[probe] A-6: durability is {status.Durability} of {status.m_MaxDurability}; "
            + $"HP {status.HP} of {status.m_MaxHP}. Climax limit would be "
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

        PleasureOverlayLayout layout = PleasureRuntime.Profile.Overlay;
        float height = Screen.height;
        float centreX = Screen.width * layout.CentreX;

        // Anchored to the bottom, because the game's HUD is. Measuring down from the top let the
        // gauge drift off the dial as soon as the window was not the height it was measured on.
        float centreY = height - (height * layout.BottomOffset);
        float radius = height * layout.Radius;

        RefreshLiquid(meter.Value);
        if (_liquid is not null)
        {
            float diameter = radius * 2f;
            Draw(new Rect(centreX - radius, centreY - radius, diameter, diameter), _liquid, Color.white);
        }

        if (layout.ShowCross)
        {
            RefreshCross();
            if (_cross is not null)
            {
                float crossHeight = radius * 1.5f;
                Draw(
                    new Rect(
                        centreX - (crossHeight * 0.33f),
                        centreY - radius - crossHeight - (height * 0.01f),
                        crossHeight * 0.66f,
                        crossHeight),
                    _cross,
                    Color.white);
            }
        }
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
        if (_liquid is not null && !moved && now - _liquidBuiltAt < 0.08d)
        {
            return;
        }

        _liquidFill = fill;
        _liquidBuiltAt = now;
        _liquidPhase += 0.35f;
        _liquid = PleasureArt.LiquidDisc(96, fill, _liquidPhase);
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
    /// Puts a texture on screen, tinted. Falls back to a tinted box if this build cannot draw a
    /// texture through a label either, so the gauge degrades instead of disappearing.
    /// </summary>
    [HideFromIl2Cpp]
    private void Draw(Rect area, Texture2D texture, Color tint)
    {
        Color previous = GUI.color;
        GUI.color = tint;

        if (!_labelUnavailable)
        {
            try
            {
                GUI.Label(area, texture);
                GUI.color = previous;
                return;
            }
            catch (Exception exception)
            {
                _labelUnavailable = true;
                PleasureRuntime.Log?.LogWarning(
                    "Textures cannot be drawn on this build; the gauge falls back to plain blocks "
                    + $"({exception.Message}).");
            }
        }

        GUI.Box(area, GUIContent.none);
        GUI.color = previous;
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

        PleasureOverlayLayout layout = PleasureRuntime.Profile.Overlay;
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

        _haze ??= PleasureArt.Solid(Color.white);
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
            Draw(new Rect(0f, offset, width, step + 1f), _haze, tint);
            Draw(new Rect(0f, height - offset - step - 1f, width, step + 1f), _haze, tint);
            Draw(new Rect(offset, 0f, step + 1f, height), _haze, tint);
            Draw(new Rect(width - offset - step - 1f, 0f, step + 1f, height), _haze, tint);
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
