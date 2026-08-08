namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// How long <c>BreastSuper</c> has to be survived before it subsides back to <c>Breast</c>
/// (SPEC003 5.8, FR-259).
///
/// The escalation is a penalty, not a permanent state. Without a way back, a player who reaches it
/// in a fight carries it until they find the cure, which turns one bad moment into a detour.
///
/// The game's own way back is the self-milking scene, and that one cannot be borrowed: it fades the
/// screen, cuts to a virtual camera and switches on a set of actors that exist only on the map it
/// was authored for (付録A A-42). So what is left is to survive it. The span is what makes that a
/// situation rather than a status — enemies keep coming, and being caught while swollen is worse
/// than being caught otherwise.
///
/// The span is drawn afresh each time rather than fixed. A player who knows the escalation lasts
/// exactly forty seconds counts to forty; one who knows it lasts somewhere between thirty and sixty
/// has to keep reading the fight instead of the clock.
///
/// A minimum of zero means it never subsides, which is what a build that wants the old behaviour
/// sets.
/// </summary>
public sealed class BreastSuperTimer
{
    private readonly double _minimum;
    private readonly double _maximum;
    private readonly Func<double> _sample;
    private double _target;

    /// <summary>
    /// A timer over a span, with the draw injected.
    ///
    /// The sample is a function returning 0..1 rather than a <c>Random</c> so a test can say which
    /// span it wants without reaching for a seed. It defaults to a shared random, which is what
    /// play uses.
    /// </summary>
    public BreastSuperTimer(double minimumSeconds, double maximumSeconds, Func<double>? sample = null)
    {
        _minimum = Math.Max(0d, minimumSeconds);
        _maximum = Math.Max(_minimum, maximumSeconds);
        _sample = sample ?? Random.Shared.NextDouble;
        _target = _minimum;
    }

    public bool IsRunning { get; private set; }

    public double Elapsed { get; private set; }

    /// <summary>The span drawn for the run in progress, or the minimum before one has started.</summary>
    public double Target => _target;

    public double Remaining => _target <= 0d ? 0d : Math.Max(0d, _target - Elapsed);

    /// <summary>Whether a duration was configured at all.</summary>
    public bool HasDuration => _maximum > 0d;

    /// <summary>
    /// Starts the countdown, or leaves it alone if it is already running. Restarting on every frame
    /// the status is present would mean it never expired.
    ///
    /// The span is drawn here, not in the constructor: each escalation gets its own, and one drawn
    /// once at load would be a fixed number wearing a range's clothes.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Elapsed = 0d;
        _target = Draw();
    }

    public void Stop()
    {
        IsRunning = false;
        Elapsed = 0d;
        _target = _minimum;
    }

    /// <summary>
    /// Advances the countdown. Returns true exactly once, on the tick that reaches the span, so the
    /// caller can act on it without needing to remember that it already has.
    /// </summary>
    public bool Tick(double delta)
    {
        if (!IsRunning || !HasDuration || delta <= 0d)
        {
            return false;
        }

        Elapsed += delta;
        if (Elapsed < _target)
        {
            return false;
        }

        IsRunning = false;
        Elapsed = 0d;
        _target = _minimum;
        return true;
    }

    /// <summary>
    /// One span. The sample is clamped rather than trusted: a supplied function is outside this
    /// class's control, and a value of 2 would silently double the longest wait the config allows.
    /// </summary>
    private double Draw()
    {
        if (_maximum <= _minimum)
        {
            return _minimum;
        }

        double sample = Math.Clamp(_sample(), 0d, 1d);
        return _minimum + ((_maximum - _minimum) * sample);
    }
}
