namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// The action inputs the MP0 penalty can fire on (SPEC005 5.3).
///
/// <c>Magic</c> exists but is not in the shipped set. The game already staggers the player every
/// time magic is attempted with no MP, so rolling for it would either change nothing or produce a
/// second stagger on top of the game's own — and the vanilla behaviour is explicitly not ours to
/// alter (DEC-406, FR-412).
/// </summary>
public static class StunInputs
{
    public const string Attack = "Attack";
    public const string Jump = "Jump";
    public const string Move = "Move";
    public const string Magic = "Magic";

    public static readonly IReadOnlyList<string> Known = new[] { Attack, Jump, Move, Magic };

    public static readonly IReadOnlyList<string> Defaults = new[] { Attack, Jump, Move };
}

/// <summary>Tuning for the MP0 penalty (SPEC005 5.3).</summary>
public sealed record MpPenaltyTuning(
    bool Enabled,
    float CorruptionFraction,
    float Chance,
    float CooldownSeconds,
    IReadOnlyList<string> TriggerInputs)
{
    public static MpPenaltyTuning Disabled { get; } =
        new(false, 0f, 0f, 0f, Array.Empty<string>());

    public bool HasEffect => Enabled && Chance > 0f && TriggerInputs.Count > 0;
}

/// <summary>
/// Decides when acting on an empty MP bar costs the player a stagger (SPEC005 5.3, FR-409, FR-410).
///
/// The penalty is the other half of the buff. A sufficiently corrupted body recovers MP by
/// climaxing, and an empty bar makes ordinary movement unreliable, so the shortest way out of the
/// unreliability is to accept what the enemy is doing. That is the loop the MOD exists to build
/// (SPEC005 4.2).
///
/// Rolls happen on the press, never on the frame. An unprompted stagger on a timer would fire
/// during an event or a cutscene and break progression outright, and tying it to the moment the
/// player asked for something also makes it legible: the input was refused, rather than the game
/// having seized (DEC-404).
///
/// Nothing about playing the stagger lives here. This type only ever answers "now", so the rule can
/// be tested without a game (FR-232 in spirit).
/// </summary>
public sealed class MpZeroStunScheduler
{
    private readonly MpPenaltyTuning _tuning;
    private readonly HashSet<string> _triggers;
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    private double _cooldownUntil;

    // Set by a reset, not at construction. The first frame back records what is down without
    // calling any of it a press: a key held through a scene load was not pressed in the scene it
    // arrives in. At construction there is nothing to prime against — the set is empty and the
    // plugin has been watching since the game started.
    private bool _priming;

    /// <summary>Trigger presses seen, whatever came of them.</summary>
    public int PressCount { get; private set; }

    /// <summary>Presses that got as far as drawing a roll.</summary>
    public int RollCount { get; private set; }

    /// <summary>Rolls that came up a stagger.</summary>
    public int FireCount { get; private set; }

    /// <summary>Why the last press did not fire, or null if none has been seen yet.</summary>
    public string? LastOutcome { get; private set; }

    /// <summary>Seconds of cooldown left. Zero when a press would be rolled for right now.</summary>
    public double CooldownRemainingAt(double now) => Math.Max(0d, _cooldownUntil - now);

    public MpZeroStunScheduler(MpPenaltyTuning tuning)
    {
        _tuning = tuning;
        _triggers = new HashSet<string>(tuning.TriggerInputs, StringComparer.Ordinal);
    }

    /// <summary>
    /// Advances one frame and reports whether a stagger is owed.
    /// </summary>
    /// <param name="conditionsMet">
    /// Everything 5.3 requires at once: the crest is worn, corruption is at or above the threshold,
    /// MP is empty, play is ordinary, and the player is alive. Gathered by the caller because every
    /// one of those is a question only the game can answer.
    /// </param>
    /// <param name="heldInputs">Trigger inputs held down this frame.</param>
    /// <param name="now">Unscaled seconds, used for the cooldown.</param>
    /// <param name="roll">
    /// Draws a value in 0..1. Deferred rather than passed by value because the generator behind it
    /// is the game's own global one: drawing every frame regardless of whether anything was pressed
    /// would quietly reshuffle every roll the game and the other MODs make from it, and §10 asks
    /// for the lottery to run on the edge and nowhere else.
    /// </param>
    public bool Evaluate(
        bool conditionsMet,
        IReadOnlyCollection<string> heldInputs,
        double now,
        Func<float> roll)
    {
        // Edges are tracked whatever the conditions are. A key already down when the conditions
        // became true has not been pressed under them, and treating it as a press would let a
        // player who was simply walking take a stagger for a decision they never made.
        bool pressed = false;
        foreach (string input in heldInputs)
        {
            if (_held.Add(input) && _triggers.Contains(input) && !_priming)
            {
                pressed = true;
            }
        }

        _held.RemoveWhere(x => !heldInputs.Contains(x));
        _priming = false;

        if (!pressed)
        {
            return false;
        }

        // Counted and explained from here down. "It did nothing" and "it did something you could
        // not see" are the two readings a silent mechanism allows, and only one of them is a bug —
        // so every press records which of the gates turned it away (利用者REVIEW 2026-08-10).
        PressCount++;

        if (!_tuning.HasEffect)
        {
            LastOutcome = "the penalty is switched off (MpPenalty.Enabled or StunChance is 0)";
            return false;
        }

        if (!conditionsMet)
        {
            LastOutcome = "the conditions were not all met";
            return false;
        }

        if (now < _cooldownUntil)
        {
            LastOutcome = $"the cooldown had {_cooldownUntil - now:F1}s left";
            return false;
        }

        RollCount++;
        float drawn = roll();
        if (drawn >= _tuning.Chance)
        {
            LastOutcome = $"the roll was {drawn:F3}, which is not under StunChance {_tuning.Chance:F3}";
            return false;
        }

        FireCount++;
        LastOutcome = $"it fired on a roll of {drawn:F3}";

        // Started before the stagger is played rather than after it ends. The cooldown is there to
        // stop a held-down run of presses chaining staggers into a lock the player cannot act out
        // of, and that has to hold even if the playback itself fails.
        _cooldownUntil = now + Math.Max(0f, _tuning.CooldownSeconds);
        return true;
    }

    /// <summary>
    /// Forgets the frame's input state and the cooldown (FR-416).
    ///
    /// The next frame is a priming one. Clearing the held set alone would make every key that
    /// happened to be down look newly pressed the moment play resumed — running through a door
    /// holding right would roll for a press the player never made.
    /// </summary>
    public void Reset()
    {
        _held.Clear();
        _cooldownUntil = 0d;
        _priming = true;
    }
}
