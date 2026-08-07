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
    private Texture2D? _backdrop;
    private Texture2D? _active;
    private Texture2D? _idle;

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

    [HideFromIl2Cpp]
    private void DrawGauge()
    {
        PleasureMeter? meter = PleasureRuntime.Meter;
        if (meter is null)
        {
            return;
        }

        float sensitivity = PleasureRuntime.Sensitivity?.Value ?? 0f;
        int count = PleasureRuntime.Climaxes.Count;
        ClimaxTuning climax = PleasureRuntime.Profile.Climax;
        int limit = ClimaxLimit.Compute(climax.LimitBase, climax.LimitPerDurability, _lastMaxDurability);

        const float x = 12f;
        const float y = 12f;
        const float width = 232f;

        GUI.Box(new Rect(x, y, width, 76f), GUIContent.none);

        // The gauge is drawn even at zero gain: seeing it sit still is how the player can tell the
        // MOD is loaded but untuned, rather than loaded and broken.
        GUI.Label(new Rect(x + 8f, y + 4f, width - 16f, 20f), $"快楽  {meter.Value * 100f:F0}%");
        DrawBar(new Rect(x + 8f, y + 24f, width - 16f, 10f), meter.Value, PleasureRuntime.CanAccumulate);

        GUI.Label(
            new Rect(x + 8f, y + 36f, width - 16f, 20f),
            $"感度  {sensitivity:F2} / {PleasureRuntime.Sensitivity?.Cap ?? 0f:F0}");
        GUI.Label(
            new Rect(x + 8f, y + 54f, width - 16f, 20f),
            limit > 0 ? $"絶頂  {count} / {limit}" : $"絶頂  {count}");
    }

    [HideFromIl2Cpp]
    private void DrawBar(Rect area, float fill, bool active)
    {
        GUI.DrawTexture(area, BackdropTexture());
        if (fill <= 0f)
        {
            return;
        }

        var filled = new Rect(area.x, area.y, area.width * Math.Clamp(fill, 0f, 1f), area.height);

        // Dimmed while pleasure cannot rise, so the gauge also says whether the situation is one
        // where it could move at all.
        GUI.DrawTexture(filled, active ? ActiveTexture() : IdleTexture());
    }

    [HideFromIl2Cpp]
    private void DrawClimaxFlash()
    {
        if (Time.timeAsDouble >= PleasureRuntime.ClimaxFlashUntil)
        {
            return;
        }

        var area = new Rect(0f, Screen.height * 0.38f, Screen.width, 48f);
        GUI.Label(area, $"絶頂  {PleasureRuntime.Climaxes.Count}");
    }

    [HideFromIl2Cpp]
    private Texture2D BackdropTexture() => _backdrop ??= SolidTexture(new Color(0f, 0f, 0f, 0.65f));

    [HideFromIl2Cpp]
    private Texture2D ActiveTexture() => _active ??= SolidTexture(new Color(1f, 0.24f, 0.62f, 0.95f));

    [HideFromIl2Cpp]
    private Texture2D IdleTexture() => _idle ??= SolidTexture(new Color(0.55f, 0.35f, 0.45f, 0.75f));

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
