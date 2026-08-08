namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// Keeps the struggle gauge from rising while a nullification window is open, without caring which
/// code path raised it (SPEC002 5.3, FR-111).
///
/// Suppressing the input method directly turned out not to work on this build: the patch applies
/// and the window opens, but resistance still registers, so either <c>Execution</c> is not the
/// path input takes or IL2CPP inlined it away (付録A A-7). Holding the value instead is
/// independent of that question.
///
/// The ceiling only ever ratchets down. Decay is the game's own and must keep working inside the
/// window — DEC-103 relies on the gauge visibly falling while the player mashes — so a fall is
/// adopted as the new ceiling rather than being undone.
/// </summary>
public sealed class GaugeHold
{
    private float _ceiling;
    private float _penalty;

    public bool IsHolding { get; private set; }

    /// <summary>The value the gauge is currently not allowed to exceed.</summary>
    public float Ceiling => _ceiling;

    /// <summary>
    /// Starts holding from wherever the gauge is now. <paramref name="penalty"/> is how much of an
    /// attempted rise is turned into a fall: 1.0 loses exactly what the input would have gained,
    /// and 0 only stops the rise (SPEC002 FR-136).
    /// </summary>
    public void Begin(float current, float penalty)
    {
        IsHolding = true;
        _ceiling = current;
        _penalty = Math.Max(0f, penalty);
    }

    public void End()
    {
        IsHolding = false;
        _ceiling = 0f;
    }

    /// <summary>
    /// Given the gauge's present value, reports the value it should be written back as. Returns
    /// false when no write is needed, so an idle window costs nothing and the game keeps ownership
    /// of the value whenever it is not rising.
    /// </summary>
    public bool TryHold(float current, out float held)
    {
        held = current;
        if (!IsHolding)
        {
            return false;
        }

        if (current > _ceiling)
        {
            // The rise is the only evidence the MOD has that the player resisted, because the path
            // input takes could not be identified (SPEC002 DEC-114). Turning that evidence into a
            // fall is what makes resisting inside the window cost ground instead of merely
            // achieving nothing.
            float rise = current - _ceiling;
            held = Math.Max(0f, _ceiling - (rise * _penalty));
            _ceiling = held;
            return true;
        }

        // Fell on its own: that is the decay doing its job, and it becomes the new ceiling so the
        // player cannot win back the ground they lost.
        _ceiling = current;
        return false;
    }
}
