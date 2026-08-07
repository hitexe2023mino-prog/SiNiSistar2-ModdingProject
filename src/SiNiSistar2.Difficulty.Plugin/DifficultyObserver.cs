using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Difficulty.Core;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using UnityEngine;
using UnityEngine.UI;

namespace SiNiSistar2.Difficulty.Plugin;

/// <summary>
/// Drives the two time-based mechanisms and keeps the resolved player references fresh.
///
/// Work is confined to the states that need it: the nullification schedule only advances during a
/// hold, and the recovery window only while it is open. Nothing here scans every object or every
/// status list per frame (SPEC002 10.1).
/// </summary>
public sealed class DifficultyObserver : MonoBehaviour
{
    private bool _wasBound;
    private bool _faultLogged;
    private bool _selfChecked;

    public DifficultyObserver(IntPtr pointer)
        : base(pointer)
    {
    }

    public void Update()
    {
        try
        {
            Poll();
            ClampGauge();
            _faultLogged = false;
        }
        catch (Exception exception)
        {
            // Fail back to the unmodified game rather than leaving a contribution registered.
            Suspend();
            if (!_faultLogged)
            {
                _faultLogged = true;
                DifficultyRuntime.Log?.LogWarning(
                    $"Difficulty observation failed and backed out; it will retry: {exception}");
            }
        }
    }

    /// <summary>
    /// Second chance at the gauge, after every component's <c>Update</c> has run. Unity gives no
    /// ordering guarantee between components, so the clamp has to happen on both sides of the
    /// frame to be sure a raise is undone before it is drawn or acted on.
    /// </summary>
    public void LateUpdate() => ClampGauge();

    public void OnApplicationQuit() => Shutdown();

    [HideFromIl2Cpp]
    public void Shutdown()
    {
        Suspend();
        DifficultyRuntime.Reset();
    }

    private void Poll()
    {
        // The game logs a manager-access violation if any manager is read during scene teardown or
        // setup, so its own gate has to come first (the same order RuntimeObserver uses).
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
        AbnormalList? abnormals = status?.AbnormalList;
        if (lelia is null || status is null || abnormals is null)
        {
            Suspend();
            return;
        }

        DifficultyRuntime.PlayerAbnormals = abnormals;
        DifficultyRuntime.PlayerGacha = lelia.GachaBind?.GachaSystem;

        // The self-check reads before the override on its one frame, so it has to run first.
        ReportSelfCheck(status);
        EnsureHardReported();

        // Gameplay time, so a paused game does not burn through a nullification window.
        double now = Time.timeAsDouble;
        bool bound = lelia.IsHold;

        UpdateHold(bound, now, abnormals);
        UpdateRecovery(bound, now, status, abnormals);
        _wasBound = bound;
    }

    /// <summary>
    /// Holds the check-side difficulty mirror at <c>Hard</c>. It is a static field, so loading a
    /// save puts the saved difficulty back into it and it has to be re-asserted rather than set
    /// once. The value seen the first time is what <see cref="DifficultyPlugin.Unload"/> restores,
    /// so removing the MOD leaves the mirror exactly as the game had it (SPEC002 4.4, FR-124).
    /// </summary>
    [HideFromIl2Cpp]
    private static void EnsureHardReported()
    {
        if (!DifficultyRuntime.OverrideCheckValue)
        {
            return;
        }

        GameDifficulty current = PlayerStatusManager.s_GameDifficultyForCheck;
        if (current == GameDifficulty.Hard)
        {
            return;
        }

        if (!DifficultyRuntime.Ledger.IsOpen(DifficultyRuntime.HardCheckKey))
        {
            GameDifficulty original = current;
            DifficultyRuntime.Ledger.Register(
                DifficultyRuntime.HardCheckKey,
                () => PlayerStatusManager.s_GameDifficultyForCheck = original);
        }

        PlayerStatusManager.s_GameDifficultyForCheck = GameDifficulty.Hard;
    }

    /// <summary>
    /// Logs once, on the first frame the managers are up, what the game actually reports against
    /// what the save actually holds.
    ///
    /// Without it there is no way to tell a working MOD from an inert one: a patch that Harmony
    /// accepted but Il2CppInterop could not apply looks identical at startup to one that took.
    /// The saved value is read through the unpatched instance property, so the same line is also
    /// the evidence that FR-104 holds (SPEC002 10.4, 付録A A-1/A-2).
    /// </summary>
    [HideFromIl2Cpp]
    private void ReportSelfCheck(PlayerStatusManager status)
    {
        if (_selfChecked)
        {
            return;
        }

        _selfChecked = true;

        GameDifficulty checkBefore = PlayerStatusManager.s_GameDifficultyForCheck;
        GameDifficulty savedBefore = status.GameDifficulty;

        EnsureHardReported();

        GameDifficulty checkAfter = PlayerStatusManager.s_GameDifficultyForCheck;
        GameDifficulty savedAfter = status.GameDifficulty;

        DifficultyRuntime.Log?.LogInfo(
            $"Self-check: before the override, checkValue={checkBefore}, saved={savedBefore}. "
            + $"After it, checkValue={checkAfter}, saved={savedAfter}, "
            + $"IsHardMode={PlayerStatusManager.IsHardMode}.");

        if (savedBefore != savedAfter)
        {
            // The saved difficulty is meant to be a different member from the check mirror. If
            // overriding one moves the other, the MOD is on the save path and FR-104 does not
            // hold, which is the one failure that outlives the MOD being uninstalled.
            DifficultyRuntime.Log?.LogError(
                $"The saved difficulty moved from {savedBefore} to {savedAfter} when the check "
                + "value was overridden, so it is not independent of it on this build. Set "
                + "ForceHardData=false and restore the save from a backup before playing further "
                + "(SPEC002 FR-104, FR-105, 付録A A-1).");
        }
    }

    /// <summary>
    /// Opens and closes the nullification schedule with the hold. The schedule is discarded when
    /// the hold ends rather than resumed, so a new hold always starts responsive (SPEC002 5.3).
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateHold(bool bound, double now, AbnormalList abnormals)
    {
        NullificationScheduler? scheduler = DifficultyRuntime.Nullification;
        if (scheduler is null)
        {
            return;
        }

        if (!bound)
        {
            if (_wasBound)
            {
                scheduler.EndHold();
            }

            SetNullified(false);
            return;
        }

        AbnormalType[] types = DifficultyRuntime.PleasureTypes;

        // Windows exist only while a pleasure status is actually on the player: without one, the
        // hold has to behave exactly as it does in the unmodified game (SPEC002 FR-111).
        if (!DifficultyRuntime.HasAny(abnormals, types))
        {
            scheduler.EndHold();
            SetNullified(false);
            return;
        }

        int levelSum = DifficultyRuntime.LevelSum(abnormals, types);
        if (!_wasBound || !scheduler.IsNullifying && scheduler.ChangeAt <= 0d)
        {
            scheduler.BeginHold(now, levelSum);
            SetNullified(false);
            return;
        }

        bool before = scheduler.IsNullifying;
        bool after = scheduler.Update(now, levelSum);
        if (before != after)
        {
            DifficultyRuntime.LogIntervention(
                after
                    ? "Nullification window opened."
                    : "Nullification window closed; the Execution prefix dropped "
                      + $"{DifficultyRuntime.SuppressedExecutions} call(s) during it. Zero means "
                      + "resistance input does not go through that method on this build, and the "
                      + "gauge hold is what is doing the work (付録A A-7).");
        }

        SetNullified(after);
    }

    /// <summary>
    /// Turns the two window effects on and off together: the gauge is held from rising, and it is
    /// tinted so the stall reads as deliberate.
    /// </summary>
    [HideFromIl2Cpp]
    private static void SetNullified(bool nullifying)
    {
        GaugeHold hold = DifficultyRuntime.Gauge;
        if (nullifying && !hold.IsHolding)
        {
            GachaGachaSystem? gacha = DifficultyRuntime.PlayerGacha;
            if (gacha is not null && !gacha.WasCollected)
            {
                DifficultyRuntime.SuppressedExecutions = 0;
                hold.Begin(gacha.CurrentValue, DifficultyRuntime.Profile.Pleasure.ResistPenalty);
            }
        }
        else if (!nullifying && hold.IsHolding)
        {
            hold.End();
        }

        ApplyGaugeTint(nullifying);
    }

    /// <summary>
    /// Writes the gauge back down if something raised it during a window.
    ///
    /// Run from both <c>Update</c> and <c>LateUpdate</c> because Unity does not order components
    /// among themselves: whichever side moves first, the value is corrected before the frame is
    /// drawn, and the escape check has the smallest possible chance of seeing a raised value.
    /// </summary>
    [HideFromIl2Cpp]
    private static void ClampGauge()
    {
        GaugeHold hold = DifficultyRuntime.Gauge;
        if (!hold.IsHolding)
        {
            return;
        }

        try
        {
            GachaGachaSystem? gacha = DifficultyRuntime.PlayerGacha;
            if (gacha is null || gacha.WasCollected)
            {
                return;
            }

            if (hold.TryHold(gacha.CurrentValue, out float held))
            {
                gacha.CurrentValue = held;
            }
        }
        catch (Exception)
        {
            // The hold UI can be torn down between frames; letting the player struggle is the
            // safe failure here.
            hold.End();
        }
    }

    /// <summary>
    /// Tints the struggle gauge's fill while input is being ignored (SPEC002 DEC-103).
    ///
    /// DEC-103 requires the gauge to stay on screen so the player can see that input is not
    /// registering, but a gauge that simply stops moving reads as the game having locked up. The
    /// tint says the stall is deliberate. Only the fill colour changes; the gauge, its value and
    /// its decay are untouched, so nothing about the escape maths moves (FR-113, FR-115).
    /// </summary>
    [HideFromIl2Cpp]
    private static void ApplyGaugeTint(bool nullifying)
    {
        Rgba? highlight = DifficultyRuntime.Profile.Pleasure.GaugeHighlight;
        if (highlight is null)
        {
            return;
        }

        if (!nullifying)
        {
            if (DifficultyRuntime.Ledger.IsOpen(DifficultyRuntime.GaugeTintKey))
            {
                DifficultyRuntime.Ledger.Release(DifficultyRuntime.GaugeTintKey);
            }

            return;
        }

        Image? fill = ResolveGaugeFill();
        if (fill is null)
        {
            return;
        }

        if (!DifficultyRuntime.Ledger.IsOpen(DifficultyRuntime.GaugeTintKey))
        {
            Color original = fill.color;
            Image captured = fill;
            DifficultyRuntime.Ledger.Register(
                DifficultyRuntime.GaugeTintKey,
                () =>
                {
                    // The hold UI is torn down with the scene, so the restore has to tolerate the
                    // image already being gone rather than reporting a failure every scene change.
                    if (!captured.WasCollected)
                    {
                        captured.color = original;
                    }
                });
        }

        // Re-asserted every frame: the game drives this UI itself and may repaint the fill.
        Rgba tint = highlight.Value;
        fill.color = new Color(tint.R, tint.G, tint.B, tint.A);
    }

    /// <summary>
    /// The image the struggle gauge fills. Unity puts it on the slider's fill rect, but a skinned
    /// slider can carry it on a child, so both are tried before giving up.
    /// </summary>
    [HideFromIl2Cpp]
    private static Image? ResolveGaugeFill()
    {
        GachaGachaSystem? gacha = DifficultyRuntime.PlayerGacha;
        Slider? slider = gacha?.m_Slider;
        RectTransform? fill = slider?.fillRect;
        if (fill is null || fill.WasCollected)
        {
            return null;
        }

        return fill.GetComponent<Image>() ?? fill.GetComponentInChildren<Image>(true);
    }

    /// <summary>
    /// Opens the recovery window on escape and takes it down again on expiry or re-capture. The
    /// movement penalty is a contribution on the game's own multi-source value, so releasing the
    /// MOD's key leaves every other contributor untouched (SPEC002 5.4, FR-119, DEC-104).
    /// </summary>
    [HideFromIl2Cpp]
    private void UpdateRecovery(
        bool bound,
        double now,
        PlayerStatusManager status,
        AbnormalList abnormals)
    {
        RecoveryPenaltyScheduler? recovery = DifficultyRuntime.Recovery;
        if (recovery is null)
        {
            return;
        }

        if (bound)
        {
            // Being caught again ends the window; the slow must not survive into the next hold.
            if (recovery.Cancel() != RecoveryClose.None)
            {
                ReleaseMoveSlow("re-bound");
            }

            return;
        }

        if (_wasBound && DifficultyRuntime.HasAny(abnormals, DifficultyRuntime.BurdenTypes))
        {
            int levelSum = DifficultyRuntime.LevelSum(abnormals, DifficultyRuntime.BurdenTypes);

            // Begin replaces an existing window and reports false when a contribution is already
            // registered, which is what keeps exactly one of them open (SPEC002 FR-120).
            if (recovery.Begin(now, levelSum))
            {
                RegisterMoveSlow(status);
            }

            return;
        }

        if (recovery.Poll(now) != RecoveryClose.None)
        {
            ReleaseMoveSlow("elapsed");
        }
    }

    [HideFromIl2Cpp]
    private static void RegisterMoveSlow(PlayerStatusManager status)
    {
        Il2CppSystem.Object? key = DifficultyRuntime.ContributionKey;
        MultiSettingValue<float>? slow = status.MoveSlowRateMsv;
        if (key is null || slow is null)
        {
            return;
        }

        float rate = DifficultyRuntime.Profile.Burden.MoveSlowRate;
        slow.ResitValue(key, rate);
        DifficultyRuntime.Ledger.Register(
            DifficultyRuntime.MoveSlowKey,
            () => slow.ReleaseValue(key));
        DifficultyRuntime.LogIntervention($"Recovery slow registered at {rate}.");
    }

    [HideFromIl2Cpp]
    private static void ReleaseMoveSlow(string reason)
    {
        InterventionFailure? failure = DifficultyRuntime.Ledger.Release(DifficultyRuntime.MoveSlowKey);
        if (failure is not null)
        {
            DifficultyRuntime.Log?.LogWarning(
                $"Could not release the recovery slow ({failure.Key}): {failure.Reason}");
            return;
        }

        DifficultyRuntime.LogIntervention($"Recovery slow released ({reason}).");
    }

    /// <summary>
    /// Backs every time-based intervention out. Called whenever the managers are unavailable, so a
    /// scene change cannot leave the player slowed or a schedule half-open (SPEC002 5.6).
    /// </summary>
    [HideFromIl2Cpp]
    private void Suspend()
    {
        DifficultyRuntime.Nullification?.EndHold();
        DifficultyRuntime.Recovery?.Cancel();
        DifficultyRuntime.Gauge.End();
        DifficultyRuntime.Ledger.Release(DifficultyRuntime.GaugeTintKey);

        // Driven off the ledger rather than off Cancel's answer: this runs every frame the managers
        // are unavailable, and a release that had nothing to release would log on all of them.
        if (DifficultyRuntime.Ledger.IsOpen(DifficultyRuntime.MoveSlowKey))
        {
            ReleaseMoveSlow("suspended");
        }

        DifficultyRuntime.PlayerAbnormals = null;
        DifficultyRuntime.PlayerGacha = null;
        _wasBound = false;
    }
}
