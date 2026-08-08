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
/// The game has no such classification, so the signals used are what an attack inflicts and who
/// sent it. The sender matters because not every sexual attacker is the captor: the art gallery's
/// picture frame delivers its attack without ever binding the player, so a rule keyed only on
/// <c>BinderEnemy</c> would never see it.
///
/// Anything unrecognised is non-sexual. The two errors are not equally bad: pleasure rising while
/// something eats you is plainly wrong, while pleasure failing to rise on a sexual enemy is a
/// tuning gap that the overrides fix (SPEC003 DEC-204).
/// </summary>
public sealed class SexualAttackClassifier
{
    private readonly HashSet<string> _sexualStatuses;
    private readonly IEnemyAttackOverrides _enemies;
    private readonly string[] _sexualSenders;
    private readonly string[] _nonSexualSenders;

    /// <summary>
    /// The per-enemy decisions are held by reference rather than copied. They live in a file the
    /// player edits from inside the game, and a copy would mean a change only took effect after a
    /// restart — which is the opposite of what an in-game editor is for (FR-236).
    /// </summary>
    public SexualAttackClassifier(
        IReadOnlyCollection<string> sexualStatuses,
        IEnemyAttackOverrides enemies,
        IReadOnlyCollection<string>? sexualSenders = null,
        IReadOnlyCollection<string>? nonSexualSenders = null)
    {
        _sexualStatuses = new HashSet<string>(sexualStatuses, StringComparer.Ordinal);
        _enemies = enemies;
        _sexualSenders = (sexualSenders ?? Array.Empty<string>()).ToArray();
        _nonSexualSenders = (nonSexualSenders ?? Array.Empty<string>()).ToArray();
    }

    /// <summary>A classifier over a fixed pair of lists, for tests and for the inactive profile.</summary>
    public SexualAttackClassifier(
        IReadOnlyCollection<string> sexualStatuses,
        IReadOnlyCollection<string> sexualEnemies,
        IReadOnlyCollection<string> nonSexualEnemies,
        IReadOnlyCollection<string>? sexualSenders = null,
        IReadOnlyCollection<string>? nonSexualSenders = null)
        : this(
            sexualStatuses,
            new FixedEnemyAttackOverrides(sexualEnemies, nonSexualEnemies),
            sexualSenders,
            nonSexualSenders)
    {
    }

    /// <summary>
    /// Applies the rules in order. Every non-sexual rule outranks every sexual one, because
    /// refusing to raise pleasure is the recoverable mistake. Within each side the explicit
    /// identity beats the inferred status test.
    /// </summary>
    public AttackKind Classify(
        string? binderEnemyId,
        string? senderName,
        IReadOnlyCollection<string>? appliedStatuses)
    {
        EnemyAttackSetting declared = string.IsNullOrEmpty(binderEnemyId)
            ? EnemyAttackSetting.Auto
            : _enemies.SettingFor(binderEnemyId!);

        if (declared == EnemyAttackSetting.NonSexual)
        {
            return AttackKind.NonSexual;
        }

        if (MatchesSender(senderName, _nonSexualSenders))
        {
            return AttackKind.NonSexual;
        }

        if (declared == EnemyAttackSetting.Sexual)
        {
            return AttackKind.Sexual;
        }

        if (MatchesSender(senderName, _sexualSenders))
        {
            return AttackKind.Sexual;
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

    /// <summary>
    /// Substring, case-insensitive. Unity object names carry suffixes such as "(Clone)" and scene
    /// decorations, so an exact match would fail on names that are plainly the same enemy.
    /// </summary>
    private static bool MatchesSender(string? senderName, string[] patterns)
    {
        if (string.IsNullOrEmpty(senderName) || patterns.Length == 0)
        {
            return false;
        }

        foreach (string pattern in patterns)
        {
            if (senderName!.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The two comma-separated lists behaving as a set of per-enemy decisions. Kept so a classifier can
/// still be built without a catalogue file — the inactive profile has no file, and a test should not
/// need one to state which enemy it means.
/// </summary>
internal sealed class FixedEnemyAttackOverrides : IEnemyAttackOverrides
{
    private readonly HashSet<string> _sexual;
    private readonly HashSet<string> _nonSexual;

    internal FixedEnemyAttackOverrides(
        IReadOnlyCollection<string> sexual,
        IReadOnlyCollection<string> nonSexual)
    {
        _sexual = new HashSet<string>(sexual, StringComparer.Ordinal);
        _nonSexual = new HashSet<string>(nonSexual, StringComparer.Ordinal);
    }

    public EnemyAttackSetting SettingFor(string enemyId)
    {
        if (_nonSexual.Contains(enemyId))
        {
            return EnemyAttackSetting.NonSexual;
        }

        return _sexual.Contains(enemyId) ? EnemyAttackSetting.Sexual : EnemyAttackSetting.Auto;
    }
}
