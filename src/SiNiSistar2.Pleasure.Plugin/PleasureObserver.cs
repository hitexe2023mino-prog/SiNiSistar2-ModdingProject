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
    private int _lastSelectId = int.MinValue;
    private string? _lastSaveFile;
    private double _lastFrameTime;
    private float _lastMaxDurability;
    private Texture2D? _track;
    private Texture2D? _active;
    private Texture2D? _idle;
    private Texture2D? _sensitivity;
    private Texture2D? _flash;

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
        if (!PleasureRuntime.Profile.ShowOverlay)
        {
            return;
        }

        try
        {
            DrawGauge();
            DrawClimaxFlash();
        }
        catch (Exception)
        {
            // A drawing failure must never take the run down; the mechanism keeps working
            // without its display (SPEC003 FR-213).
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
    /// HUD — same centre, same arc idiom — instead of parking a floating box in a corner, which is
    /// what made the first attempt read as debug output rather than part of the game.
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
        float centreY = height * layout.CentreY;
        float radius = height * layout.Radius;
        float thickness = Math.Max(2f, height * layout.Thickness);

        bool live = PleasureRuntime.CanAccumulate;

        // The unfilled track is drawn first and dimly, echoing the dotted ring the dial already has.
        DrawArc(centreX, centreY, radius, thickness, 1f, TrackTexture());
        DrawArc(centreX, centreY, radius, thickness, meter.Value, live ? ActiveTexture() : IdleTexture());

        DrawSensitivityRing(centreX, centreY, radius + (thickness * 1.6f), thickness * 0.45f);
        DrawClimaxPips(centreX, centreY, radius + (thickness * 3.2f));
    }

    /// <summary>
    /// Sensitivity as a thinner outer ring. It only ever grows, so it reads as a rim that fills in
    /// permanently while the pleasure ring inside it rises and falls.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawSensitivityRing(float centreX, float centreY, float radius, float thickness)
    {
        SensitivityTrack? track = PleasureRuntime.Sensitivity;
        if (track is null || track.Cap <= 0f)
        {
            return;
        }

        DrawArc(centreX, centreY, radius, Math.Max(1f, thickness), track.Value / track.Cap, SensitivityTexture());
    }

    /// <summary>
    /// The climax count as pips around the top of the dial. A count is discrete, so dots say it
    /// better than a bar, and the remaining pips show how much headroom the limit still allows.
    /// </summary>
    [HideFromIl2Cpp]
    private void DrawClimaxPips(float centreX, float centreY, float radius)
    {
        ClimaxTuning tuning = PleasureRuntime.Profile.Climax;
        int limit = ClimaxLimit.Compute(tuning.LimitBase, tuning.LimitPerDurability, _lastMaxDurability);
        int count = PleasureRuntime.Climaxes.Count;
        if (limit <= 0 || limit > 24)
        {
            return;
        }

        float size = Math.Max(3f, Screen.height * 0.0045f);
        const float spreadDegrees = 90f;
        float step = limit == 1 ? 0f : spreadDegrees / (limit - 1);
        float first = -spreadDegrees / 2f;

        for (var index = 0; index < limit; index++)
        {
            double radians = (first + (step * index)) * Math.PI / 180d;
            float x = centreX + (float)(radius * Math.Sin(radians));
            float y = centreY - (float)(radius * Math.Cos(radians));
            GUI.DrawTexture(
                new Rect(x - (size / 2f), y - (size / 2f), size, size),
                index < count ? ActiveTexture() : TrackTexture());
        }
    }

    /// <summary>
    /// Draws a ring by stepping small quads along it. IMGUI has no arc primitive, and a texture
    /// cannot be clipped to an angle, so the arc is walked instead. The quads overlap slightly so
    /// the ring reads as continuous rather than as beads.
    /// </summary>
    [HideFromIl2Cpp]
    private static void DrawArc(
        float centreX,
        float centreY,
        float radius,
        float thickness,
        float fill,
        Texture2D texture)
    {
        float clamped = Math.Clamp(fill, 0f, 1f);
        if (clamped <= 0f || radius <= 0f)
        {
            return;
        }

        var segments = (int)Math.Ceiling(clamped * 180f);
        float sweep = clamped * 360f;
        float size = thickness * 1.5f;

        for (var index = 0; index <= segments; index++)
        {
            double degrees = sweep * index / Math.Max(1, segments);
            double radians = degrees * Math.PI / 180d;

            // Starts at twelve o'clock and fills clockwise, the direction the dial's own arcs run.
            float x = centreX + (float)(radius * Math.Sin(radians));
            float y = centreY - (float)(radius * Math.Cos(radians));
            GUI.DrawTexture(new Rect(x - (size / 2f), y - (size / 2f), size, size), texture);
        }
    }

    [HideFromIl2Cpp]
    private void DrawClimaxFlash()
    {
        double remaining = PleasureRuntime.ClimaxFlashUntil - Time.timeAsDouble;
        if (remaining <= 0d)
        {
            return;
        }

        // A ring that blooms outward from the dial, so the moment is announced where the gauge
        // lives rather than by text across the middle of the screen.
        PleasureOverlayLayout layout = PleasureRuntime.Profile.Overlay;
        float height = Screen.height;
        float progress = 1f - (float)Math.Clamp(remaining / Math.Max(0.01f, layout.FlashSeconds), 0d, 1d);
        float radius = height * layout.Radius * (1f + (progress * 0.6f));
        float thickness = Math.Max(2f, height * layout.Thickness * (1f - progress));

        DrawArc(
            Screen.width * layout.CentreX,
            height * layout.CentreY,
            radius,
            thickness,
            1f,
            FlashTexture());
    }

    [HideFromIl2Cpp]
    private Texture2D TrackTexture() => _track ??= SolidTexture(new Color(0.10f, 0.08f, 0.10f, 0.55f));

    [HideFromIl2Cpp]
    private Texture2D ActiveTexture() => _active ??= SolidTexture(new Color(0.93f, 0.20f, 0.55f, 0.92f));

    [HideFromIl2Cpp]
    private Texture2D IdleTexture() => _idle ??= SolidTexture(new Color(0.52f, 0.24f, 0.38f, 0.72f));

    [HideFromIl2Cpp]
    private Texture2D SensitivityTexture() => _sensitivity ??= SolidTexture(new Color(0.72f, 0.62f, 0.78f, 0.60f));

    [HideFromIl2Cpp]
    private Texture2D FlashTexture() => _flash ??= SolidTexture(new Color(1f, 0.62f, 0.82f, 0.55f));

    [HideFromIl2Cpp]
    private static Texture2D SolidTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();

        // Unity destroys textures on scene unload unless they are marked, and a destroyed texture
        // would throw on every frame the overlay draws.
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
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

        PleasureRuntime.IsBound = false;
        PleasureRuntime.BinderEnemyId = null;
        _wasBound = false;
    }
}
