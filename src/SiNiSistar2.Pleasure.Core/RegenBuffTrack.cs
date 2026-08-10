namespace SiNiSistar2.Pleasure.Core;

/// <summary>Tuning for the succubus regeneration buff (SPEC005 5.1).</summary>
public sealed record RegenTuning(
    bool Enabled,
    float DurationPerClimax,
    float DurationCap,
    float HpPerSecond,
    float MpPerSecond)
{
    public static RegenTuning Disabled { get; } = new(false, 0f, 0f, 0f, 0f);

    /// <summary>
    /// Whether the buff can ever be felt. A duration with nothing to restore, or restoration with
    /// no duration, is inert — which is the shipped state until the 付録A measurements land
    /// (FR-415).
    /// </summary>
    public bool HasEffect =>
        Enabled && DurationPerClimax > 0f && (HpPerSecond > 0f || MpPerSecond > 0f);
}

/// <summary>What one advance of the buff hands the game, in whole points.</summary>
public readonly record struct RegenTick(int Hp, int Mp)
{
    public bool IsEmpty => Hp <= 0 && Mp <= 0;
}

/// <summary>
/// The regeneration a sublimated body earns by climaxing (SPEC005 5.1, FR-402, FR-403).
///
/// The buff is deliberately not free and deliberately not passive. It is paid for with a climax,
/// which spends one of the run's remaining climaxes and moves the player closer to the limit that
/// ends it — so accepting the enemy's attacks is a trade rather than a discovery (DEC-402).
///
/// Time accumulates rather than resetting. A player who climaxes repeatedly in quick succession
/// banks the duration instead of merely refreshing it, which is what "連続して絶頂した場合は効果
/// 時間を延長する" asks for (DEC-403). It cannot run away: the only way to clear the climax count
/// is a save point, and a save point also discards this (FR-407, DEC-412).
///
/// Nothing here is persisted. Carrying a half-spent buff across a load would let the player store
/// recovery in a save file, and the gauge it belongs to is not stored either (DEC-409).
/// </summary>
public sealed class RegenBuffTrack
{
    private readonly RegenTuning _tuning;

    // Fractional remainders. A rate below one point per second would otherwise floor to zero on
    // every tick and restore nothing at all, which is the quiet way a regen mechanic does nothing.
    private double _hpCarry;
    private double _mpCarry;

    public RegenBuffTrack(RegenTuning tuning) => _tuning = tuning;

    /// <summary>Seconds the buff still has to run.</summary>
    public double Remaining { get; private set; }

    public bool IsActive => Remaining > 0d;

    /// <summary>
    /// Records a climax that qualifies. The caller decides whether it qualifies — this type cannot
    /// see the crest or whether the climax was fatal, and guessing either would put the rule in two
    /// places.
    /// </summary>
    public void OnQualifyingClimax()
    {
        if (!_tuning.HasEffect)
        {
            return;
        }

        Remaining += _tuning.DurationPerClimax;

        // A cap of zero is not a cap of nothing: it means no ceiling was asked for (DEC-403).
        if (_tuning.DurationCap > 0f && Remaining > _tuning.DurationCap)
        {
            Remaining = _tuning.DurationCap;
        }
    }

    /// <summary>
    /// Advances the buff and reports what to restore.
    ///
    /// Only called while the game is actually running. Time paused, an event playing, a defeat
    /// performance and a dead player all suspend it rather than spend it, so a buff cannot drain
    /// away behind a menu (FR-406). Being held does not suspend it: that is where it is meant to
    /// be useful.
    /// </summary>
    public RegenTick Advance(double deltaSeconds)
    {
        if (!IsActive || deltaSeconds <= 0d)
        {
            return default;
        }

        double spent = Math.Min(Remaining, deltaSeconds);
        Remaining -= spent;
        if (Remaining < 1e-4d)
        {
            Remaining = 0d;
        }

        _hpCarry += _tuning.HpPerSecond * spent;
        _mpCarry += _tuning.MpPerSecond * spent;

        var hp = (int)Math.Floor(_hpCarry);
        var mp = (int)Math.Floor(_mpCarry);
        _hpCarry -= hp;
        _mpCarry -= mp;

        return new RegenTick(Math.Max(0, hp), Math.Max(0, mp));
    }

    /// <summary>
    /// Ends the buff and drops the fractions with it.
    ///
    /// Used by a save point, a slot load, the start of a run, a game over and the teardown
    /// (FR-407, FR-404, FR-416). Keeping the carried fractions would leak a point of recovery into
    /// whatever came next, which is small but is exactly the kind of thing that makes a mechanism
    /// impossible to reason about.
    /// </summary>
    public void Discard()
    {
        Remaining = 0d;
        _hpCarry = 0d;
        _mpCarry = 0d;
    }
}
