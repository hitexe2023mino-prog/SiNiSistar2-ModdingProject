namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// The pleasure gauge. Rises only on sexual hits taken while bound, decays only while free, and
/// hands back exactly one climax when it fills (SPEC003 5.2, 5.4).
///
/// The gauge is not persisted. Carrying arousal across a session has no meaning, and saving it
/// would make "load and climax immediately" a reachable state (SPEC003 DEC-207).
/// </summary>
public sealed class PleasureMeter
{
    private readonly float _gainPerHit;
    private readonly float _corruptionScale;
    private readonly float _decayPerSecond;

    public PleasureMeter(float gainPerHit, float corruptionScale, float decayPerSecond)
    {
        _gainPerHit = Math.Max(0f, gainPerHit);
        _corruptionScale = Math.Max(0f, corruptionScale);
        _decayPerSecond = Math.Max(0f, decayPerSecond);
    }

    /// <summary>Current value in 0..1.</summary>
    public float Value { get; private set; }

    /// <summary>
    /// Applies one sexual hit. Returns true exactly once per fill: the caller owns the climax and
    /// must call <see cref="ConsumeClimax"/> to reset the gauge.
    /// </summary>
    public bool AddSexualHit(float corruption)
    {
        if (_gainPerHit <= 0f || Value >= 1f)
        {
            return false;
        }

        float gain = _gainPerHit * (1f + (Math.Max(0f, corruption) * _corruptionScale));
        Value = Math.Min(1f, Value + gain);
        return Value >= 1f;
    }

    /// <summary>
    /// Decays the gauge. Only called while the player is free: decaying inside a hold would let
    /// the player wait out the danger without escaping (SPEC003 5.2).
    /// </summary>
    public void Decay(double deltaSeconds)
    {
        if (_decayPerSecond <= 0f || Value <= 0f || deltaSeconds <= 0d)
        {
            return;
        }

        Value = Math.Max(0f, Value - (float)(_decayPerSecond * deltaSeconds));
    }

    /// <summary>Takes the climax and empties the gauge, so one fill cannot be counted twice.</summary>
    public void ConsumeClimax() => Value = 0f;

    public void Reset() => Value = 0f;
}
