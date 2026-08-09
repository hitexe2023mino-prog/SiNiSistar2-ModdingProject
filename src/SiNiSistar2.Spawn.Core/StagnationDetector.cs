namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// Detects the SPEC004 3章 definition of stagnation — at least <c>StagnationSeconds</c> in the
/// area with less than <c>StagnationMoveEpsilon</c> of movement over the last
/// <c>StagnationWindowSeconds</c> — and paces the penalty spawns it authorizes
/// (<c>StagnationPenaltyInterval</c>, FR-309).
///
/// Pure logic: the caller feeds it time and position samples; it never reads the game. Paused
/// samples (hold, cinematic, game over) freeze the internal clock rather than resetting it
/// (SPEC004 5.3 停滞ペナルティ).
/// </summary>
public sealed class StagnationDetector
{
    private readonly float _stagnationSeconds;
    private readonly float _windowSeconds;
    private readonly float _moveEpsilon;
    private readonly float _penaltyInterval;

    private readonly Queue<(double Clock, float X, float Y)> _samples = new();
    private double _clock;
    private double? _lastSampleTime;
    private double _dwellStart;
    private double? _lastPenaltyClock;
    private bool _stagnant;
    private bool _windowForced;

    public StagnationDetector(
        float stagnationSeconds,
        float windowSeconds,
        float moveEpsilon,
        float penaltyInterval)
    {
        _stagnationSeconds = stagnationSeconds;
        _windowSeconds = windowSeconds;
        _moveEpsilon = moveEpsilon;
        _penaltyInterval = penaltyInterval;
    }

    public bool IsStagnant => _stagnant;

    /// <summary>Seconds of un-paused presence in the area, for the HUD (SPEC004 5.8-3).</summary>
    public double Dwell => _clock - _dwellStart;

    /// <summary>Distance travelled inside the current window, for the HUD.</summary>
    public float WindowTravel => PathLength();

    /// <summary>
    /// Seconds until the next penalty spawn is due, or until stagnation itself begins when it has
    /// not started yet. Null when the detector has no pending countdown to report.
    /// </summary>
    public double? SecondsUntilNextPenalty
    {
        get
        {
            if (!_stagnant)
            {
                double remaining = _stagnationSeconds - Dwell;
                return remaining > 0 ? remaining : 0;
            }

            if (_lastPenaltyClock is not double last)
            {
                return 0;
            }

            double until = _penaltyInterval - (_clock - last);
            return until > 0 ? until : 0;
        }
    }

    /// <summary>
    /// Debug affordance (SPEC004 5.9): moves the dwell clock and the sample window to the point
    /// they would reach after standing still for the full threshold, so the natural firing path
    /// can be observed in seconds rather than minutes.
    ///
    /// Only elapsed time is fabricated. The decision itself is untouched — <see cref="Sample"/>
    /// still has to see an un-paused frame with no movement, so what fires is the real mechanism
    /// (FR-332).
    /// </summary>
    public void FastForwardToStagnation()
    {
        _dwellStart = _clock - _stagnationSeconds;
        _windowForced = true;
    }

    /// <summary>Called on area entry and whenever measurement must restart (SPEC004 9章).</summary>
    public void Reset()
    {
        _samples.Clear();
        _clock = 0;
        _lastSampleTime = null;
        _dwellStart = 0;
        _lastPenaltyClock = null;
        _stagnant = false;
        _windowForced = false;
    }

    /// <summary>
    /// Feeds one frame. Returns true when a penalty spawn is due this frame; the caller decides
    /// whether one can actually happen (budget, positions, exclusions).
    /// </summary>
    public bool Sample(double now, float x, float y, bool paused)
    {
        if (paused)
        {
            // The wall clock keeps running but the stagnation clock does not; skipping the delta
            // on the next unpaused frame is what "pauses" measurement without resetting it.
            _lastSampleTime = null;
            return false;
        }

        if (_lastSampleTime is double last && now > last)
        {
            _clock += now - last;
        }

        _lastSampleTime = now;
        _samples.Enqueue((_clock, x, y));
        while (_samples.Count > 1 && _clock - _samples.Peek().Clock > _windowSeconds)
        {
            _samples.Dequeue();
        }

        float traveled = PathLength();
        bool windowFilled = _clock - _samples.Peek().Clock >= _windowSeconds * 0.999;

        // A real window supersedes a forced one; until then the debug fast-forward stands in for
        // the samples that have not been collected yet (SPEC004 5.9).
        if (windowFilled)
        {
            _windowForced = false;
        }
        else if (_windowForced)
        {
            windowFilled = true;
        }

        if (traveled >= _moveEpsilon)
        {
            // Movement resumed: stagnation ends and the dwell requirement starts over
            // (SPEC004 5.3 「計測を初期化する」). The fast-forward is spent along with it.
            _stagnant = false;
            _dwellStart = _clock;
            _lastPenaltyClock = null;
            _windowForced = false;
            return false;
        }

        if (!_stagnant)
        {
            if (_clock - _dwellStart < _stagnationSeconds || !windowFilled)
            {
                return false;
            }

            _stagnant = true;
            _lastPenaltyClock = null;
        }

        if (_lastPenaltyClock is null || _clock - _lastPenaltyClock >= _penaltyInterval)
        {
            _lastPenaltyClock = _clock;
            return true;
        }

        return false;
    }

    private float PathLength()
    {
        var total = 0f;
        (double Clock, float X, float Y)? previous = null;
        foreach ((double Clock, float X, float Y) sample in _samples)
        {
            if (previous is { } p)
            {
                float dx = sample.X - p.X;
                float dy = sample.Y - p.Y;
                total += MathF.Sqrt((dx * dx) + (dy * dy));
            }

            previous = sample;
        }

        return total;
    }
}
