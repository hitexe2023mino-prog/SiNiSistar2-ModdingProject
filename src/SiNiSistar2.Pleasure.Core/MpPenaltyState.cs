namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// Every fact the MP0 penalty turns on, read once a frame (SPEC005 5.3).
///
/// One struct rather than seven booleans computed inline, so the debug panel shows exactly the
/// values the rule ran on rather than re-reading them a frame later and disagreeing.
///
/// Gathering the values needs the game; deciding what they mean does not. The decision and its
/// explanation live here so both can be tested without launching anything (REFACTOR001 RF-003).
/// That matters more than it looks: RF-005 asks which of these seven is false on a real machine,
/// and the answer arrives as the string <see cref="Describe"/> builds.
/// </summary>
public readonly record struct MpPenaltyState(
    bool Corrupted,
    float CorruptionFraction,
    bool CrestWorn,
    bool MpLow,
    float MpFraction,
    int Mp,
    int MpMax,
    bool Bound,
    bool Dead,
    bool Paused,
    bool Cinematic,
    IReadOnlyCollection<string> HeldInputs,
    IReadOnlyCollection<string> AllInputsDown)
{
    /// <summary>The seven conditions of SPEC005 5.3, all of them at once.</summary>
    public bool ConditionsMet =>
        Corrupted && CrestWorn && MpLow && !Bound && !Dead && !Paused && !Cinematic;

    /// <summary>
    /// Why the conditions do or do not hold, as one readable line.
    ///
    /// The negative cases are shouted in capitals on purpose. This line is read while something
    /// is not happening, and the question it has to answer at a glance is which term failed.
    /// </summary>
    public string Describe(MpPenaltyTuning tuning)
    {
        var parts = new List<string>(7)
        {
            CrestWorn ? "crest worn" : "NO crest",
            Corrupted
                ? $"corruption {CorruptionFraction:P0} >= {tuning.CorruptionFraction:P0}"
                : $"corruption {CorruptionFraction:P0} BELOW {tuning.CorruptionFraction:P0}",

            // Unreadable is its own answer. A bar nobody could read must never be reported as a
            // bar that was low, which is the reading that would send a search off in the wrong
            // direction (SPEC005 5.3 適用条件3).
            MpFraction < 0f
                ? "MP UNREADABLE"
                : MpLow
                    ? $"MP {Mp}/{MpMax} ({MpFraction:P0}) < {tuning.MpFraction:P0}"
                    : $"MP {Mp}/{MpMax} ({MpFraction:P0}) NOT below {tuning.MpFraction:P0}",
        };

        if (Bound)
        {
            parts.Add("BOUND");
        }

        if (Dead)
        {
            parts.Add("DEAD");
        }

        if (Paused)
        {
            parts.Add("PAUSED");
        }

        if (Cinematic)
        {
            parts.Add("CINEMATIC");
        }

        return string.Join(", ", parts);
    }
}
