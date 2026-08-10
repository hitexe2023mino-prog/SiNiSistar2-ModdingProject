using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>The MP0 penalty's rule (SPEC005 5.3, AC-408, AC-409, AC-410).</summary>
public sealed class MpZeroStunSchedulerTests
{
    private static readonly string[] Attack = { StunInputs.Attack };
    private static readonly string[] Nothing = Array.Empty<string>();

    private static MpZeroStunScheduler Scheduler(float chance = 1f, float cooldown = 3f) =>
        new(new MpPenaltyTuning(true, 0.5f, chance, cooldown, StunInputs.Defaults));

    private static Func<float> Roll(float value) => () => value;

    /// <summary>AC-408: a press under the conditions can stagger.</summary>
    [Fact]
    public void PressUnderTheConditionsStaggers()
    {
        MpZeroStunScheduler scheduler = Scheduler();

        Assert.True(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));
    }

    /// <summary>
    /// AC-408: the conditions are an AND. The caller collapses them into one boolean, so the rule
    /// under test here is that a false one is never overridden by anything else.
    /// </summary>
    [Fact]
    public void PressWithoutTheConditionsDoesNothing()
    {
        MpZeroStunScheduler scheduler = Scheduler();

        Assert.False(scheduler.Evaluate(false, Attack, 0d, Roll(0f)));
    }

    /// <summary>AC-410: held down is one press, not one per frame (FR-410).</summary>
    [Fact]
    public void HoldingAnInputRollsOnlyOnce()
    {
        MpZeroStunScheduler scheduler = Scheduler(chance: 1f, cooldown: 0f);

        Assert.True(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));
        Assert.False(scheduler.Evaluate(true, Attack, 1d, Roll(0f)));
        Assert.False(scheduler.Evaluate(true, Attack, 2d, Roll(0f)));

        scheduler.Evaluate(true, Nothing, 3d, Roll(0f));

        Assert.True(scheduler.Evaluate(true, Attack, 4d, Roll(0f)));
    }

    /// <summary>
    /// A key already down when the conditions become true was not pressed under them. Without this
    /// a player who was simply walking takes a stagger for a decision they never made.
    /// </summary>
    [Fact]
    public void InputHeldFromBeforeTheConditionsIsNotAFreshPress()
    {
        MpZeroStunScheduler scheduler = Scheduler(cooldown: 0f);

        Assert.False(scheduler.Evaluate(false, Attack, 0d, Roll(0f)));
        Assert.False(scheduler.Evaluate(true, Attack, 1d, Roll(0f)));
    }

    /// <summary>The cooldown stops a run of presses chaining into a lock (FR-410).</summary>
    [Fact]
    public void CooldownSuppressesTheNextPresses()
    {
        MpZeroStunScheduler scheduler = Scheduler(cooldown: 3f);

        Assert.True(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));

        scheduler.Evaluate(true, Nothing, 0.5d, Roll(0f));
        Assert.False(scheduler.Evaluate(true, Attack, 1d, Roll(0f)));

        scheduler.Evaluate(true, Nothing, 2.9d, Roll(0f));
        Assert.False(scheduler.Evaluate(true, Attack, 2.95d, Roll(0f)));

        scheduler.Evaluate(true, Nothing, 3.5d, Roll(0f));
        Assert.True(scheduler.Evaluate(true, Attack, 4d, Roll(0f)));
    }

    /// <summary>The roll decides, and a chance of 0 never fires.</summary>
    [Theory]
    [InlineData(0.25f, 0.10f, true)]
    [InlineData(0.25f, 0.30f, false)]
    [InlineData(0f, 0f, false)]
    public void TheRollDecides(float chance, float roll, bool expected)
    {
        MpZeroStunScheduler scheduler = Scheduler(chance, cooldown: 0f);

        Assert.Equal(expected, scheduler.Evaluate(true, Attack, 0d, Roll(roll)));
    }

    /// <summary>
    /// The roll is only drawn when a press has actually got through every gate.
    ///
    /// It comes from the game's own global generator, so taking a value on every frame would
    /// reshuffle every roll the game and the other MODs make from it — for a lottery §10 says runs
    /// on the edge and nowhere else.
    /// </summary>
    [Fact]
    public void TheRollIsNotDrawnUnlessAPressGetsThrough()
    {
        MpZeroStunScheduler scheduler = Scheduler(chance: 1f, cooldown: 3f);
        var draws = 0;
        Func<float> counted = () => { draws++; return 0f; };

        // No press.
        scheduler.Evaluate(true, Nothing, 0d, counted);
        Assert.Equal(0, draws);

        // Pressed, but the conditions do not hold.
        scheduler.Evaluate(false, Attack, 1d, counted);
        Assert.Equal(0, draws);
        scheduler.Evaluate(false, Nothing, 2d, counted);

        // Pressed under the conditions: drawn exactly once.
        Assert.True(scheduler.Evaluate(true, Attack, 3d, counted));
        Assert.Equal(1, draws);

        // Held: no further draw.
        scheduler.Evaluate(true, Attack, 4d, counted);
        Assert.Equal(1, draws);

        // Pressed again, but inside the cooldown: no draw.
        scheduler.Evaluate(true, Nothing, 5d, counted);
        scheduler.Evaluate(true, Attack, 5.5d, counted);
        Assert.Equal(1, draws);
    }

    /// <summary>
    /// An input outside the configured set is not a trigger. Magic is the one that matters: the
    /// game already staggers for it, so the default set leaves it out (DEC-406, FR-412).
    /// </summary>
    [Fact]
    public void InputsOutsideTheConfiguredSetNeverTrigger()
    {
        MpZeroStunScheduler scheduler = Scheduler();

        Assert.False(scheduler.Evaluate(true, new[] { StunInputs.Magic }, 0d, Roll(0f)));
    }

    /// <summary>A disabled penalty never fires, which is the shipped state until A-401 (FR-411).</summary>
    [Fact]
    public void DisabledTuningNeverFires()
    {
        var scheduler = new MpZeroStunScheduler(MpPenaltyTuning.Disabled);

        Assert.False(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));
    }

    /// <summary>Teardown forgets the cooldown (FR-416).</summary>
    [Fact]
    public void ResetClearsTheCooldown()
    {
        MpZeroStunScheduler scheduler = Scheduler(cooldown: 100f);
        Assert.True(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));

        scheduler.Reset();

        // The key is released and pressed again, so this is a genuine new press rather than the
        // one the reset primed against.
        scheduler.Evaluate(true, Nothing, 1d, Roll(0f));
        Assert.True(scheduler.Evaluate(true, Attack, 2d, Roll(0f)));
    }

    /// <summary>
    /// Every press records which gate turned it away.
    ///
    /// This is what makes "it is not firing" diagnosable in game rather than a guess: the counters
    /// separate "no press was seen at all" (the keys are not being read) from "pressed but the
    /// conditions were wrong" from "rolled and lost" (利用者REVIEW 2026-08-10).
    /// </summary>
    [Fact]
    public void EveryPressIsCountedAndExplained()
    {
        MpZeroStunScheduler scheduler = Scheduler(chance: 0.5f, cooldown: 3f);

        Assert.Equal(0, scheduler.PressCount);
        Assert.Null(scheduler.LastOutcome);

        // Pressed, conditions wrong.
        scheduler.Evaluate(false, Attack, 0d, Roll(0f));
        Assert.Equal(1, scheduler.PressCount);
        Assert.Equal(0, scheduler.RollCount);
        Assert.Contains("conditions", scheduler.LastOutcome!, StringComparison.Ordinal);

        // Pressed under the conditions but the roll loses.
        scheduler.Evaluate(false, Nothing, 1d, Roll(0f));
        scheduler.Evaluate(true, Attack, 2d, Roll(0.9f));
        Assert.Equal(2, scheduler.PressCount);
        Assert.Equal(1, scheduler.RollCount);
        Assert.Equal(0, scheduler.FireCount);
        Assert.Contains("roll was", scheduler.LastOutcome!, StringComparison.Ordinal);

        // Pressed under the conditions and the roll wins.
        scheduler.Evaluate(true, Nothing, 3d, Roll(0f));
        Assert.True(scheduler.Evaluate(true, Attack, 4d, Roll(0.1f)));
        Assert.Equal(1, scheduler.FireCount);
        Assert.Contains("fired", scheduler.LastOutcome!, StringComparison.Ordinal);

        // And the cooldown is reported rather than being silent.
        scheduler.Evaluate(true, Nothing, 4.5d, Roll(0f));
        scheduler.Evaluate(true, Attack, 5d, Roll(0f));
        Assert.Contains("cooldown", scheduler.LastOutcome!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped-but-untuned case: armed inputs, chance still 0.
    ///
    /// This is what the config actually holds when someone turns Enabled on and forgets
    /// StunChance, and it is the case the F4 panel has to explain rather than sit silent through.
    /// </summary>
    [Fact]
    public void AZeroChancePenaltyCountsThePressAndSaysItIsOff()
    {
        var scheduler = new MpZeroStunScheduler(
            new MpPenaltyTuning(true, 0.5f, 0f, 3f, StunInputs.Defaults));

        Assert.False(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));
        Assert.Equal(1, scheduler.PressCount);
        Assert.Equal(0, scheduler.RollCount);
        Assert.Contains("switched off", scheduler.LastOutcome!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fully disabled tuning arms no inputs at all, so there is nothing to count.
    ///
    /// The panel does not lean on the scheduler for this case: it reads the keys itself and marks
    /// every one "(off)", which is why an empty press count here is a report rather than a hole.
    /// </summary>
    [Fact]
    public void ADisabledPenaltyArmsNothing()
    {
        var scheduler = new MpZeroStunScheduler(MpPenaltyTuning.Disabled);

        Assert.False(scheduler.Evaluate(true, Attack, 0d, Roll(0f)));
        Assert.Equal(0, scheduler.RollCount);
        Assert.Equal(0, scheduler.FireCount);
        Assert.Empty(MpPenaltyTuning.Disabled.TriggerInputs);
    }

    /// <summary>The cooldown is readable while it runs, so the panel can count it down.</summary>
    [Fact]
    public void CooldownRemainingIsReadable()
    {
        MpZeroStunScheduler scheduler = Scheduler(chance: 1f, cooldown: 3f);

        Assert.Equal(0d, scheduler.CooldownRemainingAt(0d), 3);

        Assert.True(scheduler.Evaluate(true, Attack, 10d, Roll(0f)));

        Assert.Equal(3d, scheduler.CooldownRemainingAt(10d), 3);
        Assert.Equal(0.5d, scheduler.CooldownRemainingAt(12.5d), 3);
        Assert.Equal(0d, scheduler.CooldownRemainingAt(13d), 3);
        Assert.Equal(0d, scheduler.CooldownRemainingAt(99d), 3);
    }

    /// <summary>
    /// A key held across a scene change is not a press in the scene it arrives in.
    ///
    /// The reset clears the held set, so without the priming frame the first ready frame after a
    /// load would see the key as newly down: running through a door holding right would roll for a
    /// press the player never made (FR-410).
    /// </summary>
    [Fact]
    public void KeyHeldAcrossAResetIsNotAPhantomPress()
    {
        MpZeroStunScheduler scheduler = Scheduler(cooldown: 0f);

        scheduler.Reset();

        Assert.False(scheduler.Evaluate(true, Attack, 1d, Roll(0f)));

        // Released and pressed again under the conditions: that one counts.
        scheduler.Evaluate(true, Nothing, 2d, Roll(0f));
        Assert.True(scheduler.Evaluate(true, Attack, 3d, Roll(0f)));
    }
}
