namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// <c>BreastSuper</c> is authored for an event and this is what opens it to ordinary play. Getting
/// the gate wrong makes a heavy penalty arrive either at random or never (SPEC003 5.8, FR-221).
/// </summary>
public sealed class BreastEscalationTests
{
    private static BreastEscalation Escalation(int after = 3, float threshold = 0f) => new(after, threshold);

    /// <summary>FR-233: the shipped count of 0 can never escalate, whatever else happens.</summary>
    [Fact]
    public void ACountOfZeroNeverEscalates()
    {
        BreastEscalation escalation = Escalation(after: 0);

        for (var index = 0; index < 20; index++)
        {
            Assert.Equal(BreastOutcome.None, escalation.Record(true, false, 99f));
        }

        Assert.Equal(0, escalation.Count);
    }

    /// <summary>
    /// Applications below the maximum level are not counted. Those raise the level, which is the
    /// game's own escalation; counting them would make the ceiling arrive while the ordinary
    /// progression was still running.
    /// </summary>
    [Fact]
    public void ApplicationsBelowTheMaximumAreNotCounted()
    {
        BreastEscalation escalation = Escalation(after: 2);

        Assert.Equal(BreastOutcome.None, escalation.Record(false, false, 0f));
        Assert.Equal(BreastOutcome.None, escalation.Record(false, false, 0f));
        Assert.Equal(0, escalation.Count);

        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 0f));
        Assert.Equal(BreastOutcome.Escalate, escalation.Record(true, false, 0f));
    }

    [Fact]
    public void TheEscalationLandsOnTheConfiguredApplication()
    {
        BreastEscalation escalation = Escalation(after: 3);

        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 0f));
        Assert.Equal(2, escalation.Remaining);
        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 0f));
        Assert.Equal(BreastOutcome.Escalate, escalation.Record(true, false, 0f));

        // The count clears, so a cure followed by swelling again climbs from the start.
        Assert.Equal(0, escalation.Count);
    }

    /// <summary>Nothing sits above BreastSuper, so further applications must not keep counting.</summary>
    [Fact]
    public void NothingEscalatesOnceBreastSuperIsPresent()
    {
        BreastEscalation escalation = Escalation(after: 2);
        escalation.Record(true, false, 0f);

        Assert.Equal(BreastOutcome.None, escalation.Record(true, alreadySuper: true, 0f));
        Assert.Equal(0, escalation.Count);
    }

    /// <summary>
    /// The sensitivity gate holds the escalation back without discarding the count, so it lands on
    /// the next application once the threshold is met rather than restarting the climb.
    /// </summary>
    [Fact]
    public void TheSensitivityGateHoldsTheEscalationRatherThanResettingIt()
    {
        BreastEscalation escalation = Escalation(after: 2, threshold: 5f);

        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 1f));
        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 1f));
        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 4.9f));

        Assert.Equal(BreastOutcome.Escalate, escalation.Record(true, false, 5f));
    }

    /// <summary>
    /// FR-244: an application from an item counts like any other. The escalation is told only
    /// whether the ceiling was reached, never where the application came from, which is what lets
    /// the swelling item drive it for debugging.
    /// </summary>
    [Fact]
    public void TheSourceOfTheApplicationDoesNotMatter()
    {
        BreastEscalation escalation = Escalation(after: 3);

        // Measured on the target build: Breast has a maximum level of 1, so every application after
        // the status exists arrives at the ceiling, whatever applied it.
        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 0f));
        Assert.Equal(BreastOutcome.Counted, escalation.Record(true, false, 0f));
        Assert.Equal(BreastOutcome.Escalate, escalation.Record(true, false, 0f));
    }

    /// <summary>FR-222: the count belongs to the run, so a reload must not hand back a clean slate.</summary>
    [Fact]
    public void TheCountIsRestoredFromASave()
    {
        BreastEscalation escalation = Escalation(after: 3);
        escalation.LoadFrom(2);

        Assert.Equal(1, escalation.Remaining);
        Assert.Equal(BreastOutcome.Escalate, escalation.Record(true, false, 0f));
    }

    [Fact]
    public void ANegativeStoredCountIsClampedRatherThanTrusted()
    {
        BreastEscalation escalation = Escalation(after: 2);
        escalation.LoadFrom(-5);

        Assert.Equal(0, escalation.Count);
    }
}
