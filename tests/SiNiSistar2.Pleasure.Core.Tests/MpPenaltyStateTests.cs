using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// The MP0 penalty's condition set and the line that explains it (SPEC005 5.3).
///
/// This logic used to sit inside <c>PleasureObserver</c>, where nothing could reach it without
/// the game running (REFACTOR001 RF-003). RF-005 asks which of the seven conditions is false on
/// a real machine, and the answer arrives as the string these tests pin down — so the string
/// being wrong would send that investigation somewhere it should not go.
/// </summary>
public sealed class MpPenaltyStateTests
{
    private static readonly string[] NoInputs = Array.Empty<string>();

    private static readonly MpPenaltyTuning Tuning =
        new(true, 1f, 0.2f, 3f, StunInputs.Defaults, 0.2f);

    /// <summary>Every condition satisfied: crest worn, corruption at the cap, MP under a fifth.</summary>
    private static MpPenaltyState Met() => new(
        Corrupted: true,
        CorruptionFraction: 1f,
        CrestWorn: true,
        MpLow: true,
        MpFraction: 0.1f,
        Mp: 10,
        MpMax: 100,
        Bound: false,
        Dead: false,
        Paused: false,
        Cinematic: false,
        HeldInputs: NoInputs,
        AllInputsDown: NoInputs);

    [Fact]
    public void EveryConditionTogetherAllowsTheStagger()
    {
        Assert.True(Met().ConditionsMet);
    }

    /// <summary>
    /// Each term is load bearing. A penalty that fired with one of them false would be firing on
    /// a state SPEC005 5.3 does not describe.
    /// </summary>
    [Theory]
    [InlineData("Corrupted")]
    [InlineData("CrestWorn")]
    [InlineData("MpLow")]
    [InlineData("Bound")]
    [InlineData("Dead")]
    [InlineData("Paused")]
    [InlineData("Cinematic")]
    public void AnySingleConditionBlocksTheStagger(string condition)
    {
        MpPenaltyState state = condition switch
        {
            "Corrupted" => Met() with { Corrupted = false },
            "CrestWorn" => Met() with { CrestWorn = false },
            "MpLow" => Met() with { MpLow = false },
            "Bound" => Met() with { Bound = true },
            "Dead" => Met() with { Dead = true },
            "Paused" => Met() with { Paused = true },
            "Cinematic" => Met() with { Cinematic = true },
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };

        Assert.False(state.ConditionsMet);
    }

    /// <summary>
    /// An unreadable bar is not a low one. The MOD refuses to fire on a state nobody confirmed,
    /// and the explanation has to say so rather than print a fraction it did not have.
    /// </summary>
    [Fact]
    public void AnUnreadableBarIsReportedAsUnreadableRatherThanLow()
    {
        MpPenaltyState state = Met() with { MpFraction = -1f, MpLow = false, Mp = -1, MpMax = -1 };

        string described = state.Describe(Tuning);

        Assert.Contains("MP UNREADABLE", described);
        Assert.DoesNotContain("NOT below", described);
        Assert.False(state.ConditionsMet);
    }

    [Fact]
    public void TheFailingTermIsNamedInCapitals()
    {
        Assert.Contains("NO crest", (Met() with { CrestWorn = false }).Describe(Tuning));
        Assert.Contains("BELOW", (Met() with { Corrupted = false }).Describe(Tuning));
        Assert.Contains("NOT below", (Met() with { MpLow = false, MpFraction = 0.8f }).Describe(Tuning));
    }

    /// <summary>
    /// The four suppressors are listed only while they apply. A line that always named them
    /// would make an ordinary frame look like a suppressed one.
    /// </summary>
    [Fact]
    public void SuppressorsAppearOnlyWhileTheyApply()
    {
        string quiet = Met().Describe(Tuning);
        Assert.DoesNotContain("BOUND", quiet);
        Assert.DoesNotContain("DEAD", quiet);
        Assert.DoesNotContain("PAUSED", quiet);
        Assert.DoesNotContain("CINEMATIC", quiet);

        string suppressed = (Met() with { Bound = true, Dead = true, Paused = true, Cinematic = true })
            .Describe(Tuning);
        Assert.Contains("BOUND", suppressed);
        Assert.Contains("DEAD", suppressed);
        Assert.Contains("PAUSED", suppressed);
        Assert.Contains("CINEMATIC", suppressed);
    }

    /// <summary>The satisfied case still states the readings, so a working penalty is legible too.</summary>
    [Fact]
    public void TheSatisfiedCaseStillCarriesTheReadings()
    {
        string described = Met().Describe(Tuning);

        Assert.Contains("crest worn", described);
        Assert.Contains("10/100", described);
    }
}
