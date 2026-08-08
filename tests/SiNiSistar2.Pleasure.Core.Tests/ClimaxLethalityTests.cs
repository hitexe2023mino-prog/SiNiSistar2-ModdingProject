namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// Reaching the climax limit is what ends the run in v1.1, and it ends it there and then rather
/// than leaving the enemy's next swing to finish the job (SPEC003 5.5, FR-215, FR-216, FR-279).
/// </summary>
public sealed class ClimaxLethalityTests
{
    private static ClimaxTuning Tuning(
        int limitBase = 3,
        float perDurability = 0f,
        bool gameOver = true) =>
        new(true, 1.5f, limitBase, perDurability, gameOver, false);

    /// <summary>AC-213: the climax that reaches the limit is the one that kills.</summary>
    [Fact]
    public void TheClimaxThatReachesTheLimitIsLethal()
    {
        Assert.False(ClimaxLethality.ShouldBeLethal(Tuning(), 2, 0f, false, false));
        Assert.True(ClimaxLethality.ShouldBeLethal(Tuning(), 3, 0f, false, false));
    }

    /// <summary>
    /// AC-213: HP is not part of the question. The signature has no room for it, which is the
    /// point — v1.0 left the moment of defeat to whatever HP was left (DEC-257).
    /// </summary>
    [Fact]
    public void AClimaxPastTheLimitIsStillLethal()
    {
        Assert.True(ClimaxLethality.ShouldBeLethal(Tuning(), 9, 0f, false, false));
    }

    /// <summary>AC-244 / FR-279: with the game over switched off, the limit is only a number.</summary>
    [Fact]
    public void TheLimitDoesNothingWhenTheGameOverIsOff()
    {
        Assert.False(ClimaxLethality.ShouldBeLethal(Tuning(gameOver: false), 5, 0f, false, false));
    }

    /// <summary>AC-243 / FR-216: the latch stops the defeat performance asking for a second death.</summary>
    [Fact]
    public void ARunIsOnlyMadeLethalOnce()
    {
        Assert.False(ClimaxLethality.ShouldBeLethal(Tuning(), 3, 0f, false, alreadyFired: true));
    }

    /// <summary>FR-216: a player the game already has in a defeat state is not killed again.</summary>
    [Fact]
    public void AlreadyDeadIsNotKilledAgain()
    {
        Assert.False(ClimaxLethality.ShouldBeLethal(Tuning(), 3, 0f, alreadyDead: true, false));
    }

    /// <summary>
    /// AC-212 / FR-214: durability raises the ceiling, so the same count is fatal for one player
    /// and survivable for another.
    /// </summary>
    [Fact]
    public void DurabilityRaisesTheCeiling()
    {
        ClimaxTuning tuning = Tuning(limitBase: 2, perDurability: 0.1f);

        Assert.True(ClimaxLethality.ShouldBeLethal(tuning, 2, 0f, false, false));
        Assert.False(ClimaxLethality.ShouldBeLethal(tuning, 2, 50f, false, false));
        Assert.True(ClimaxLethality.ShouldBeLethal(tuning, 7, 50f, false, false));
    }

    /// <summary>
    /// 5.5.1: a limit of zero is a misconfiguration, not "fatal at once". Treating it as a ceiling
    /// would make the first climax of a fresh install end the run.
    /// </summary>
    [Fact]
    public void AZeroLimitIsNeverLethal()
    {
        Assert.False(ClimaxLethality.ShouldBeLethal(Tuning(limitBase: 0), 1, 0f, false, false));
        Assert.False(ClimaxLethality.ShouldBeLethal(Tuning(limitBase: 0), 99, 0f, false, false));
    }

    /// <summary>A disabled climax mechanism cannot end the run through the back door.</summary>
    [Fact]
    public void ADisabledMechanismIsNeverLethal()
    {
        Assert.False(ClimaxLethality.ShouldBeLethal(ClimaxTuning.Disabled, 99, 0f, false, false));
    }
}
