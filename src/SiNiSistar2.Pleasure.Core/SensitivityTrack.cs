namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// Sensitivity: rises with climaxes and with sexual hits taken, and never falls (SPEC003 5.7).
///
/// There is deliberately no decrease path. Curing a status, resting, saving, and resetting the
/// climax count all leave it alone. A cure that also lowered sensitivity would make the
/// accumulation meaningless, which is the whole point of the requirement (SPEC003 DEC-208).
/// The cap is a ceiling on growth, not a way down.
/// </summary>
public sealed class SensitivityTrack
{
    private readonly float _cap;

    public SensitivityTrack(float cap) => _cap = Math.Max(0f, cap);

    public float Value { get; private set; }

    public float Cap => _cap;

    public bool IsAtCap => Value >= _cap;

    /// <summary>
    /// Adds to sensitivity. A negative or zero amount is ignored rather than applied: the type is
    /// the single place the one-way rule is enforced, so it does not depend on every caller
    /// remembering it.
    /// </summary>
    public void Add(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        Value = Math.Min(_cap, Value + amount);
    }

    /// <summary>
    /// Sets the value read from a sidecar file. This is not a decrease: loading an earlier save
    /// moves to a different point in the same playthrough, and the one-way rule governs progress
    /// within a timeline rather than which timeline is being played (SPEC003 4.4).
    /// </summary>
    public void LoadFrom(float saved) => Value = Math.Clamp(saved, 0f, _cap);
}
