namespace SiNiSistar2.Pleasure.Core;

public enum AttackKind
{
    /// <summary>Does not raise pleasure. Predation, violence, traps, and anything unrecognised.</summary>
    NonSexual,

    /// <summary>Raises pleasure.</summary>
    Sexual,
}

/// <summary>
/// Decides whether an attack raises pleasure (SPEC003 5.3).
///
/// The game has no such classification, so the signal used is what an attack inflicts: an attack
/// that applies a sexual status is a sexual attack. That answers for enemies nobody has catalogued
/// yet, which listing enemies by hand cannot (SPEC003 DEC-203).
///
/// Anything unrecognised is non-sexual. The two errors are not equally bad: pleasure rising while
/// something eats you is plainly wrong, while pleasure failing to rise on a sexual enemy is a
/// tuning gap that the enemy overrides fix (SPEC003 DEC-204).
/// </summary>
public sealed class SexualAttackClassifier
{
    private readonly HashSet<string> _sexualStatuses;
    private readonly HashSet<string> _sexualEnemies;
    private readonly HashSet<string> _nonSexualEnemies;

    public SexualAttackClassifier(
        IReadOnlyCollection<string> sexualStatuses,
        IReadOnlyCollection<string> sexualEnemies,
        IReadOnlyCollection<string> nonSexualEnemies)
    {
        _sexualStatuses = new HashSet<string>(sexualStatuses, StringComparer.Ordinal);
        _sexualEnemies = new HashSet<string>(sexualEnemies, StringComparer.Ordinal);
        _nonSexualEnemies = new HashSet<string>(nonSexualEnemies, StringComparer.Ordinal);
    }

    /// <summary>
    /// Applies the rules in order: the non-sexual override wins over the sexual override, both win
    /// over the status test, and no match means non-sexual. Pass a null or empty
    /// <paramref name="binderEnemyId"/> when the captor cannot be identified; the overrides are
    /// then skipped rather than guessed at.
    /// </summary>
    public AttackKind Classify(string? binderEnemyId, IReadOnlyCollection<string>? appliedStatuses)
    {
        if (!string.IsNullOrEmpty(binderEnemyId))
        {
            if (_nonSexualEnemies.Contains(binderEnemyId!))
            {
                return AttackKind.NonSexual;
            }

            if (_sexualEnemies.Contains(binderEnemyId!))
            {
                return AttackKind.Sexual;
            }
        }

        if (appliedStatuses is not null)
        {
            foreach (string status in appliedStatuses)
            {
                if (_sexualStatuses.Contains(status))
                {
                    return AttackKind.Sexual;
                }
            }
        }

        return AttackKind.NonSexual;
    }
}
