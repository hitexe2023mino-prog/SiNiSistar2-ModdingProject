using SiNiSistar2.Manager;
using SiNiSistar2.Obj;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Every write the MOD makes to the player's HP (SPEC003 5.1.2, 5.5.3, FR-275).
///
/// Two things happen here and nothing else: a sexual hit is stopped from subtracting, and the
/// climax limit is turned into a death. Both go through the game's own <c>BattleMainParameter</c>
/// API rather than through arithmetic of the MOD's own, so the value the game ends up with is one
/// the game computed (10.2).
///
/// The exact call that works cannot be read out of the interop metadata — the boolean arguments on
/// <c>SubAll</c> and <c>SetCurrentValue</c> are unnamed there, and whether <c>DontSub</c> covers
/// damage at all is a question about the native implementation. Rather than guess, each attempt is
/// followed by a look at what actually happened and the next one is tried if it did not
/// (付録A A-50, A-51). The measurement is reported once so the appendix can be settled from a play
/// session rather than from a decompiler.
/// </summary>
internal static class PlayerHealth
{
    /// <summary>The player's HP, or null when there is no running game to ask.</summary>
    internal static HP? Current
    {
        get
        {
            try
            {
                return ManagerList.PlayerStatus?.HP;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A subtraction held off for the duration of one hit.
    ///
    /// Carries what is needed to undo itself rather than relying on state that could have moved,
    /// so a hold opened for one hit is always closed with the values that hit started from.
    /// </summary>
    internal readonly record struct Guard(HP? Health, bool RestoreDontSub, int HpBefore)
    {
        internal bool IsOpen => Health is not null;

        internal static Guard None => default;
    }

    /// <summary>
    /// Stops the hit about to be resolved from taking HP (SPEC003 5.1.2).
    ///
    /// Only the subtraction is touched. The hit itself still lands, still applies its statuses,
    /// still plays its effects — that is the whole point, and it is why invincibility was not used
    /// (DEC-256).
    /// </summary>
    internal static Guard Hold()
    {
        HP? health = Current;
        if (health is null)
        {
            return Guard.None;
        }

        try
        {
            int before = health.Current;
            bool previous = health.DontSub;
            health.DontSub = true;
            return new Guard(health, previous, before);
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The HP of a sexual hit could not be held off: {exception.Message}");
            return Guard.None;
        }
    }

    /// <summary>
    /// Puts the game back exactly as it was.
    ///
    /// Both mechanisms run on every hit rather than one being chosen after a first measurement.
    /// The tempting version — watch the first hit, decide whether <c>DontSub</c> works, then trust
    /// that answer — draws its conclusion from a single hit that may simply have done no damage,
    /// and a wrong "it works" would let every hit after it take HP unopposed. Restoring whenever
    /// HP has fallen costs a comparison and cannot be fooled (FR-275).
    ///
    /// Called from the postfix, from the finalizer, and from the observer's sweep. All three run
    /// for the same hit in the ordinary case, so this has to be safe to call more than once — an
    /// HP that is already where it should be is left alone.
    /// </summary>
    internal static void Release(in Guard guard)
    {
        if (!guard.IsOpen)
        {
            return;
        }

        HP health = guard.Health!;
        try
        {
            health.DontSub = guard.RestoreDontSub;

            int after = health.Current;
            if (after >= guard.HpBefore)
            {
                PleasureRuntime.Probe(
                    "hp-held-off",
                    "A-50: a sexual hit taken while bound left HP untouched. Either "
                    + "BattleMainParameter.DontSub stopped the subtraction or the hit spent no HP; "
                    + "an 'hp-put-back' line later would settle it as the latter.");
                return;
            }

            // The hit went through, so this build does not let DontSub stop damage. The loss is put
            // back at once, which is the fallback 5.1.2 allows and what the observable result has
            // to be either way.
            if (SetTo(health, guard.HpBefore))
            {
                PleasureRuntime.Probe(
                    "hp-put-back",
                    $"A-50 answered: BattleMainParameter.DontSub does NOT stop damage in this build "
                    + $"(HP fell {guard.HpBefore} -> {after}). The loss is written back immediately "
                    + "after each sexual hit instead (SPEC003 5.1.2 手段2).");
                return;
            }

            PleasureRuntime.Probe(
                "hp-cannot-be-held",
                "A-50 caution: neither DontSub nor writing HP back works in this build, so sexual "
                + "hits go on costing HP. Everything else keeps running; the hold is decided by HP "
                + "again, as it was before the MOD (SPEC003 9章).");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"HP could not be put back after a sexual hit: {exception.Message}");
        }
    }

    /// <summary>
    /// Ends the run because the climax limit was reached (SPEC003 5.5.3, FR-215).
    ///
    /// HP is taken to zero and nothing further. The death, the performance, the retry, the penalty
    /// and the save are the game's, and calling them from here would mean owning them (DEC-209,
    /// which v1.1 keeps).
    /// </summary>
    /// <returns>True when the player is at zero HP or has been asked to die.</returns>
    internal static bool Kill(Lelia? lelia, string reason)
    {
        HP? health = Current;
        if (health is null)
        {
            PleasureRuntime.Log?.LogWarning(
                $"The climax limit was reached ({reason}) but the player's HP could not be read, so "
                + "the run was not ended. Nothing else was written.");
            return false;
        }

        try
        {
            if (health.Current <= 0)
            {
                return true;
            }

            // Each attempt is checked rather than trusted. The boolean these take is unnamed in the
            // interop metadata, so both values are worth trying before moving on to a different
            // call altogether (付録A A-51).
            foreach ((string what, Action attempt) in Attempts(health))
            {
                try
                {
                    attempt();
                }
                catch (Exception exception)
                {
                    PleasureRuntime.Log?.LogInfo($"Climax death: {what} threw ({exception.Message}); trying the next.");
                    continue;
                }

                if (health.Current > 0)
                {
                    continue;
                }

                PleasureRuntime.Probe(
                    "climax-death-method",
                    $"A-51 answered: {what} takes HP to 0, so the climax limit ends the run through "
                    + "the game's own death path.");
                PleasureRuntime.Log?.LogInfo(
                    $"Climax limit reached ({reason}); HP taken to 0 by {what}. The game's own "
                    + "defeat path takes it from here.");
                return true;
            }

            return RequestDeath(lelia, reason);
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The run could not be ended at the climax limit: {exception.Message}");
            return false;
        }
    }

    private static IEnumerable<(string What, Action Attempt)> Attempts(HP health)
    {
        yield return ("HP.SubAll(false)", () => health.SubAll(false));
        yield return ("HP.SubAll(true)", () => health.SubAll(true));
        yield return ("HP.SetCurrentValue(0, false)", () => health.SetCurrentValue(0, false));
        yield return ("HP.SetCurrentValue(0, true)", () => health.SetCurrentValue(0, true));
    }

    /// <summary>
    /// The last resort: ask the game to kill the player without having managed to empty the bar
    /// (SPEC003 5.5.3 手段3).
    /// </summary>
    private static bool RequestDeath(Lelia? lelia, string reason)
    {
        try
        {
            if (lelia is null)
            {
                PleasureRuntime.Log?.LogWarning(
                    $"The climax limit was reached ({reason}) but neither HP nor the player object "
                    + "could be reached. The run was not ended.");
                return false;
            }

            lelia.RequestCommonDead = true;
            PleasureRuntime.Probe(
                "climax-death-method",
                "A-51 answered: HP could not be taken to 0, so Lelia.RequestCommonDead was set "
                + "instead. The HP bar stays as it was and the game's death is requested directly.");
            PleasureRuntime.Log?.LogInfo(
                $"Climax limit reached ({reason}); HP would not go to 0, so the game's own death was "
                + "requested instead.");
            return true;
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"The death request failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Writes a value back, trying both meanings of the unnamed boolean.</summary>
    private static bool SetTo(HP health, int value)
    {
        foreach (bool flag in new[] { false, true })
        {
            try
            {
                health.SetCurrentValue(value, flag);
            }
            catch (Exception)
            {
                continue;
            }

            if (health.Current >= value)
            {
                return true;
            }
        }

        try
        {
            health.Recover(value - health.Current, false);
        }
        catch (Exception)
        {
            return false;
        }

        return health.Current >= value;
    }
}
