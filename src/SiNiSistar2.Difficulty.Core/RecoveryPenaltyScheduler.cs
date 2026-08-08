namespace SiNiSistar2.Difficulty.Core;

/// <summary>Why a recovery window stopped, so the caller can log the reason it released.</summary>
public enum RecoveryClose
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>The window ran to its end.</summary>
    Elapsed,

    /// <summary>The player was bound again, or the scene went away.</summary>
    Interrupted,
}

/// <summary>
/// Decides how long the player stays slowed after escaping a hold (SPEC002 5.4).
///
/// The window only makes re-capture more likely; it never touches whether a hold may start.
/// <c>IsHoldable</c> and <c>DisableHoldMsv</c> sit upstream of the <c>Lelia.IsHold</c> that the EDI
/// MOD reads to identify its <c>hold</c> trigger, so forcing them could make a hold look like it
/// began when it did not (SPEC002 DEC-105, FR-121).
/// </summary>
public sealed class RecoveryPenaltyScheduler
{
    private readonly BurdenTuning _tuning;
    private bool _active;
    private double _endsAt;

    public RecoveryPenaltyScheduler(BurdenTuning tuning) => _tuning = tuning;

    /// <summary>True while the movement penalty should be registered.</summary>
    public bool IsActive => _active;

    public double EndsAt => _endsAt;

    /// <summary>
    /// Opens a window, or replaces the one already running. Replacing rather than extending is
    /// what keeps exactly one contribution registered at a time (SPEC002 FR-120).
    /// Returns true when the caller now needs a contribution registered.
    /// </summary>
    public bool Begin(double now, int burdenLevelSum)
    {
        if (!_tuning.HasEffect)
        {
            return false;
        }

        bool wasActive = _active;
        _active = true;
        _endsAt = now + Math.Max(
            0d,
            WindowMath.Lengthen(_tuning.PenaltySeconds, _tuning.LevelScaling, burdenLevelSum));

        // Already registered, so the caller must not register a second contribution.
        return !wasActive;
    }

    /// <summary>
    /// Advances to <paramref name="now"/>. Returns <see cref="RecoveryClose.Elapsed"/> exactly
    /// once, on the poll that carries the window past its end, so the caller releases its
    /// contribution one time.
    /// </summary>
    public RecoveryClose Poll(double now)
    {
        if (!_active || now < _endsAt)
        {
            return RecoveryClose.None;
        }

        _active = false;
        _endsAt = 0d;
        return RecoveryClose.Elapsed;
    }

    /// <summary>
    /// Closes the window early. Returns <see cref="RecoveryClose.Interrupted"/> only if it was
    /// open, so the caller does not try to release a contribution it never registered.
    /// </summary>
    public RecoveryClose Cancel()
    {
        if (!_active)
        {
            return RecoveryClose.None;
        }

        _active = false;
        _endsAt = 0d;
        return RecoveryClose.Interrupted;
    }
}
