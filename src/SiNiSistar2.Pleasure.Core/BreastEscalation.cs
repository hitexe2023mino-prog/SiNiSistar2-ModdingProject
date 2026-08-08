namespace SiNiSistar2.Pleasure.Core;

/// <summary>What should happen after one more <c>Breast</c> application (SPEC003 5.8).</summary>
public enum BreastOutcome
{
    /// <summary>Nothing; the application was ordinary.</summary>
    None,

    /// <summary>The application counted towards the escalation but has not reached it.</summary>
    Counted,

    /// <summary>The escalation is due: <c>BreastSuper</c> should replace <c>Breast</c>.</summary>
    Escalate,
}

/// <summary>
/// Turns repeated <c>Breast</c> applications into the <c>BreastSuper</c> escalation (SPEC003 5.8).
///
/// The count is of applications that arrive when <c>Breast</c> is already at its maximum level.
/// Below the maximum an application still has somewhere to go — it raises the level, which is the
/// game's own escalation — so counting those would make the ceiling arrive while the ordinary
/// progression was still running. Only a hit that the existing status can no longer absorb is
/// evidence that the player is past what <c>Breast</c> expresses.
///
/// The count survives the session because it belongs to the run, not to the process: a player who
/// reloads must not find the ceiling reset (SPEC003 FR-222).
/// </summary>
public sealed class BreastEscalation
{
    private readonly int _applications;
    private readonly float _sensitivityThreshold;

    public BreastEscalation(int applicationsAtMaxLevel, float sensitivityThreshold)
    {
        _applications = Math.Max(0, applicationsAtMaxLevel);
        _sensitivityThreshold = Math.Max(0f, sensitivityThreshold);
    }

    /// <summary>How many qualifying applications have been seen since the last escalation.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Records one <c>Breast</c> application and says what should follow.
    /// </summary>
    /// <param name="atMaxLevel">Whether <c>Breast</c> was already at its maximum level.</param>
    /// <param name="alreadySuper">Whether <c>BreastSuper</c> is already present.</param>
    /// <param name="sensitivity">The player's current sensitivity.</param>
    public BreastOutcome Record(bool atMaxLevel, bool alreadySuper, float sensitivity)
    {
        if (_applications <= 0)
        {
            return BreastOutcome.None;
        }

        // Nothing above BreastSuper to escalate to. The count is cleared so that curing it and
        // swelling again starts the climb over rather than escalating on the next hit.
        if (alreadySuper)
        {
            Count = 0;
            return BreastOutcome.None;
        }

        if (!atMaxLevel)
        {
            return BreastOutcome.None;
        }

        Count++;
        if (Count < _applications || sensitivity < _sensitivityThreshold)
        {
            return BreastOutcome.Counted;
        }

        Count = 0;
        return BreastOutcome.Escalate;
    }

    /// <summary>How many more qualifying applications are needed, for the log and the overlay.</summary>
    public int Remaining => _applications <= 0 ? 0 : Math.Max(0, _applications - Count);

    public void LoadFrom(int count) => Count = Math.Max(0, count);

    public void Reset() => Count = 0;
}
