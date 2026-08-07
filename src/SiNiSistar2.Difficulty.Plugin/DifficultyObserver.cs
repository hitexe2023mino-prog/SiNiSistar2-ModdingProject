using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Difficulty.Core;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using UnityEngine;

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

    public DifficultyObserver(IntPtr pointer)
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

        // Gameplay time, so a paused game does not burn through a nullification window.
        double now = Time.timeAsDouble;
        bool bound = lelia.IsHold;

        UpdateHold(bound, now, abnormals);
        UpdateRecovery(bound, now, status, abnormals);
        _wasBound = bound;
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

            return;
        }

        AbnormalType[] types = DifficultyRuntime.PleasureTypes;

        // Windows exist only while a pleasure status is actually on the player: without one, the
        // hold has to behave exactly as it does in the unmodified game (SPEC002 FR-111).
        if (!DifficultyRuntime.HasAny(abnormals, types))
        {
            scheduler.EndHold();
            return;
        }

        int levelSum = DifficultyRuntime.LevelSum(abnormals, types);
        if (!_wasBound || !scheduler.IsNullifying && scheduler.ChangeAt <= 0d)
        {
            scheduler.BeginHold(now, levelSum);
            return;
        }

        bool before = scheduler.IsNullifying;
        bool after = scheduler.Update(now, levelSum);
        if (before != after)
        {
            DifficultyRuntime.LogIntervention(
                after ? "Nullification window opened." : "Nullification window closed.");
        }
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
