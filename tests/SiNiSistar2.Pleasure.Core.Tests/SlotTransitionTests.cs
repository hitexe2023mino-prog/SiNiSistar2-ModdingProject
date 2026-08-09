namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// What happens to the accumulated values when the slot changes (SPEC003 FR-284).
///
/// Every one of these cases has been shipped wrong at least once, in both directions, which is why
/// the rule was pulled out of the plugin and given a home that can be tested at all.
/// </summary>
public sealed class SlotTransitionTests
{
    /// <summary>The sidecar is the truth whenever there is one; nothing else is consulted.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ASidecarAlwaysWins(bool authoritative, bool justSaved)
    {
        Assert.Equal(
            SlotAction.Restore,
            SlotTransition.Decide(hasSidecar: true, authoritative, justSaved));
    }

    /// <summary>
    /// 付録A A-55: a defeat sends the player back to the last save. Carrying the run in hand across
    /// it let corruption survive a retry, which is the opposite of what a defeat means.
    /// </summary>
    [Fact]
    public void ADefeatWithNothingRecordedStartsFromZero()
    {
        Assert.Equal(
            SlotAction.Reset,
            SlotTransition.Decide(hasSidecar: false, authoritative: true, justSaved: false));
    }

    /// <summary>
    /// Even a save written moments before does not let an authoritative change carry. The save is
    /// speaking; if it recorded nothing, nothing was accumulated.
    /// </summary>
    [Fact]
    public void AuthorityOutranksARecentSave()
    {
        Assert.Equal(
            SlotAction.Reset,
            SlotTransition.Decide(hasSidecar: false, authoritative: true, justSaved: true));
    }

    /// <summary>
    /// 付録A A-55: loading a save this MOD has never written must not inherit the run in progress.
    /// This is the case reported as "corruption crosses save data".
    /// </summary>
    [Fact]
    public void LoadingAnUnknownSaveStartsFromZero()
    {
        Assert.Equal(
            SlotAction.Reset,
            SlotTransition.Decide(hasSidecar: false, authoritative: false, justSaved: false));
    }

    /// <summary>
    /// 付録A A-44, the opposite failure: saving a fresh run into a new file changes the key to one
    /// with no sidecar. Clearing there threw the run away at the moment it was first saved.
    /// </summary>
    [Fact]
    public void SavingIntoANewFileCarriesTheRun()
    {
        Assert.Equal(
            SlotAction.Carry,
            SlotTransition.Decide(hasSidecar: false, authoritative: false, justSaved: true));
    }
}
