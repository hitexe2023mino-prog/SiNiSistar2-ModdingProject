namespace SiNiSistar2.Difficulty.Core.Tests;

/// <summary>
/// The status-ailment rate is raised on shared damage data and put back immediately. If damage
/// resolution re-enters, only the outermost scope may write, or the multiplier compounds and the
/// restore order decides the final value (SPEC002 FR-108).
/// </summary>
public sealed class ReentrantScopeTests
{
    /// <summary>AC-108: exactly one level of nesting is allowed to act.</summary>
    [Fact]
    public void OnlyTheOutermostEntryOwnsTheWrite()
    {
        var scope = new ReentrantScope();

        Assert.True(scope.TryEnter());
        Assert.False(scope.TryEnter());
        Assert.False(scope.TryEnter());

        scope.Exit();
        scope.Exit();
        scope.Exit();

        Assert.False(scope.IsHeld);

        // Fully unwound, so the next outermost call owns the write again.
        Assert.True(scope.TryEnter());
        scope.Exit();
    }

    /// <summary>
    /// AC-107: an abandoned frame must not wedge the scope open, or the multiplier would never be
    /// applied again for the rest of the session.
    /// </summary>
    [Fact]
    public void AnAbandonedScopeCanBeForcedClosed()
    {
        var scope = new ReentrantScope();
        scope.TryEnter();
        scope.TryEnter();

        scope.Reset();

        Assert.False(scope.IsHeld);
        Assert.Equal(0, scope.Depth);
        Assert.True(scope.TryEnter());
    }

    [Fact]
    public void UnbalancedExitsCannotDriveTheDepthNegative()
    {
        var scope = new ReentrantScope();

        scope.Exit();
        scope.Exit();

        Assert.Equal(0, scope.Depth);
        Assert.True(scope.TryEnter());
    }
}
