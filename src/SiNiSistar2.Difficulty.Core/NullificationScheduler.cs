namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// Decides when resistance input is ignored during a hold (SPEC002 5.3).
///
/// The window is a time band, not a change to the struggle meter's own numbers. Raising the
/// success threshold or the decay rate would act on the very quantity that defilement already
/// escalates, and the player's reason to manage defilement would quietly disappear; a band leaves
/// those numbers untouched and is observable as the gauge failing to rise (SPEC002 DEC-102).
/// </summary>
public sealed class NullificationScheduler
{
    private readonly PleasureTuning _tuning;
    private readonly IRandomSource _random;
    private bool _holding;
    private bool _open;
    private double _changeAt;

    public NullificationScheduler(PleasureTuning tuning, IRandomSource random)
    {
        _tuning = tuning;
        _random = random;
    }

    /// <summary>True while a window is open and input must not reach the struggle meter.</summary>
    public bool IsNullifying => _holding && _open;

    /// <summary>When the current band ends. Exposed for diagnostics and tests.</summary>
    public double ChangeAt => _changeAt;

    /// <summary>
    /// Starts a hold. The first band is always a gap, so a hold never begins with input already
    /// being ignored: the player has to see the gauge respond before it stops responding.
    /// </summary>
    public void BeginHold(double now, int pleasureLevelSum)
    {
        _holding = true;
        _open = false;
        _changeAt = now + NextGap(pleasureLevelSum);
    }

    /// <summary>
    /// Ends the hold and discards the schedule. The next hold builds a fresh one rather than
    /// resuming mid-window (SPEC002 5.3).
    /// </summary>
    public void EndHold()
    {
        _holding = false;
        _open = false;
        _changeAt = 0d;
    }

    /// <summary>
    /// Advances the schedule to <paramref name="now"/> and reports whether input is currently
    /// ignored. Safe to call many times per frame.
    /// </summary>
    public bool Update(double now, int pleasureLevelSum)
    {
        if (!_holding || !_tuning.HasEffect)
        {
            return false;
        }

        // A loop, not a single branch: a frame that lands well past the boundary (a stall, a load)
        // must not leave the schedule one band behind for the rest of the hold.
        var guard = 0;
        while (now >= _changeAt && guard++ < 64)
        {
            _open = !_open;
            _changeAt += _open ? NextDuration(pleasureLevelSum) : NextGap(pleasureLevelSum);
        }

        if (guard >= 64)
        {
            // Both bands would have to be near zero to get here. Re-anchor instead of spinning.
            _changeAt = now + NextGap(pleasureLevelSum);
            _open = false;
        }

        return _open;
    }

    private double NextGap(int levelSum) => Math.Max(
        0.0001d,
        WindowMath.Jitter(
            WindowMath.Shorten(_tuning.IntervalSeconds, _tuning.LevelScaling, levelSum),
            _tuning.IntervalJitter,
            _random));

    private double NextDuration(int levelSum) => Math.Max(
        0.0001d,
        WindowMath.Jitter(
            WindowMath.Lengthen(_tuning.DurationSeconds, _tuning.LevelScaling, levelSum),
            _tuning.DurationJitter,
            _random));
}
