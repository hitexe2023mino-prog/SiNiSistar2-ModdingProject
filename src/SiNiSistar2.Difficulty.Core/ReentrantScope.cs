namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// Lets only the outermost of a set of nested calls act (SPEC002 FR-108).
///
/// The status-ailment rate lives on shared damage data, so the MOD raises it just before the roll
/// and puts it back straight after. If damage resolution re-enters, a nested scope would multiply
/// the rate a second time and the restore order would decide what the value ends up as. Only the
/// outermost scope writes, and it is the one that restores.
/// </summary>
public sealed class ReentrantScope
{
    private int _depth;

    /// <summary>True while any scope is held, nested or not.</summary>
    public bool IsHeld => _depth > 0;

    /// <summary>Nesting depth. Exposed so a leak shows up in tests rather than in a play session.</summary>
    public int Depth => _depth;

    /// <summary>
    /// Enters the scope. Returns true only for the outermost entry, which is the caller that owns
    /// the write and the restore. Every successful or unsuccessful entry must be matched by
    /// <see cref="Exit"/>.
    /// </summary>
    public bool TryEnter()
    {
        _depth++;
        return _depth == 1;
    }

    /// <summary>Leaves the scope. Never goes below zero, so an unbalanced exit cannot wedge it open.</summary>
    public void Exit()
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }

    /// <summary>Forces the scope closed after an abandoned frame.</summary>
    public void Reset() => _depth = 0;
}
