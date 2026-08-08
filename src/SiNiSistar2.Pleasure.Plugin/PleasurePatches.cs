using SiNiSistar2.Damage;
using SiNiSistar2.Obj;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Wraps the resolution of one hit.
///
/// Two things happen around it. A sexual hit taken while bound has its HP subtraction held off, so
/// the cost of being held by that kind of enemy is paid in pleasure rather than in HP
/// (SPEC003 5.1). And every hit that lands on the player is read for what it applies, which is what
/// moves the gauge and the corruption.
///
/// The hold is opened in the prefix and closed in the postfix, with a finalizer behind that for
/// the case where the game's own code throws, the observer's frame sweep behind that, and
/// <c>Suspend</c> behind that again for a scene change. Four closes for one open is deliberate: a
/// <c>DontSub</c> left standing is a player nothing can hurt, anywhere, for the rest of the
/// session, and that is the worst failure this MOD can produce (FR-204).
/// </summary>
internal static class DamageProbePatches
{
    /// <summary>
    /// The hold opened for the hit being resolved right now.
    ///
    /// A single field rather than a stack. The game walks its damage stacks one at a time from
    /// <c>UpdateDamage</c> on the main thread, so a hit is never resolved inside another hit. If
    /// that ever stopped being true, the inner prefix would see the hold already open and not take
    /// a second one — the failure would be an inner hit that costs HP, which is the safe direction.
    /// </summary>
    private static PlayerHealth.Guard _guard;

    /// <summary>Whether a hold is still open. Read by the observer's sweep (FR-204).</summary>
    internal static bool IsHoldOpen => _guard.IsOpen;

    /// <summary>
    /// Decides, before the hit resolves, whether it is allowed to take HP (SPEC003 5.1.1).
    ///
    /// The classification is the same one the gauge uses, evaluated per hit, so an enemy declared
    /// <c>Sexual</c> costs no HP for anything it does, an enemy declared <c>NonSexual</c> costs HP
    /// for everything, and an <c>Auto</c> enemy is judged attack by attack from what that attack
    /// inflicts (5.3). Nothing outside a hold is touched, and nothing that is not an attack — slip
    /// damage, the environment, a fall — passes through here at all (5.1.3).
    /// </summary>
    internal static void OneDamagePrefix(DamageStack stack)
    {
        try
        {
            if (_guard.IsOpen || !PleasureRuntime.Profile.BlocksSexualHpDamage)
            {
                return;
            }

            // Only a hold. A defeat performance can go on delivering sexual attacks, but the player
            // is already at zero there and holding the subtraction off would change nothing except
            // to leave the guard open across a scene the observer is about to suspend on.
            if (!PleasureRuntime.IsBound || !PleasureRuntime.IsPlayerReceiving(stack))
            {
                return;
            }

            AttackKind kind = Classify(stack);
            if (kind != AttackKind.Sexual)
            {
                return;
            }

            _guard = PlayerHealth.Hold();
        }
        catch (Exception exception)
        {
            _guard = PlayerHealth.Guard.None;
            PleasureRuntime.Log?.LogWarning(
                $"The HP of a sexual hit could not be held off: {exception.Message}");
        }
    }

    /// <summary>
    /// Closes the hold whatever happened inside, including an exception from the game's own damage
    /// code. Harmony runs a finalizer after the postfixes, so the ordinary path has already closed
    /// it and this finds nothing to do (FR-204).
    /// </summary>
    internal static void OneDamageFinalizer() => ReleaseHold();

    /// <summary>
    /// Closes the hold. Safe to call when there is none, which is what lets the postfix, the
    /// finalizer and the observer all call it for the same hit.
    /// </summary>
    internal static void ReleaseHold()
    {
        if (!_guard.IsOpen)
        {
            return;
        }

        PlayerHealth.Guard guard = _guard;
        _guard = PlayerHealth.Guard.None;
        PlayerHealth.Release(guard);
    }

    internal static void OneDamagePostfix(DamageStack stack)
    {
        // Before anything else, and outside the try below: the HP has to go back even if reading
        // the hit for the gauge throws.
        ReleaseHold();

        try
        {
            if (!PleasureRuntime.IsPlayerReceiving(stack))
            {
                return;
            }

            bool bound = PleasureRuntime.CanAccumulate;
            string? sender = SenderName(stack);
            string[] statuses = PleasureRuntime.AppliedStatuses(stack);
            string? enemyId = PleasureRuntime.BinderEnemyId;

            PleasureRuntime.Probe(
                bound ? "hit-while-bound" : "hit-while-free",
                bound
                    ? "A-2 answered: a hit taken while bound is visible to the MOD as a damage stack."
                    : "A damage stack reached the player outside a hold.");

            if (!bound)
            {
                return;
            }

            PleasureRuntime.Probe(
                statuses.Length > 0 ? "bound-hit-with-statuses" : "bound-hit-without-statuses",
                statuses.Length > 0
                    ? $"A-3 answered: a hit taken while bound carries m_AbnormalTypes [{string.Join(", ", statuses)}]."
                    : "A-3 caution: a hit taken while bound carried no m_AbnormalTypes, so the "
                      + "classifier cannot judge it from statuses alone. Such hits need an entry in "
                      + "Pleasure.SexualEnemyIds.");

            AttackKind kind = PleasureRuntime.Profile.Classifier.Classify(enemyId, sender, statuses);
            PleasureRuntime.Probe(
                $"classified-{enemyId ?? "unknown"}-{sender ?? "?"}-{kind}",
                $"Captor '{enemyId ?? "(unidentified)"}' sender '{sender ?? "(unknown)"}' classified "
                + $"as {kind} from [{string.Join(", ", statuses)}].");

            // Everything above is measurement. The gauge only moves once it has been tuned.
            PleasureTuning tuning = PleasureRuntime.Profile.Pleasure;
            if (!tuning.HasEffect || kind != AttackKind.Sexual)
            {
                return;
            }

            CorruptionTrack? corruption = PleasureRuntime.Corruption;
            PleasureMeter? meter = PleasureRuntime.Meter;
            if (meter is null)
            {
                return;
            }

            float before = meter.Value;
            PleasureRuntime.GainCorruption(PleasureRuntime.Profile.Corruption.PerSexualHit);
            if (meter.AddSexualHit(corruption?.Value ?? 0f))
            {
                PleasureRuntime.PendingClimax = true;
            }

            // Without this there is no way to tell "the gauge never rises" from "the gauge rose
            // and decayed again before it was looked at".
            if (meter.Value > before)
            {
                PleasureRuntime.Probe(
                    "pleasure-rose",
                    $"The pleasure gauge rose for the first time: {before:F2} -> {meter.Value:F2} "
                    + $"on a hit from '{sender ?? "(unknown)"}'.");
            }

            RecordMilkFromHit();

            PleasureRuntime.LogTransition(
                $"Pleasure {meter.Value:F2} (corruption {corruption?.Value ?? 0f:F2}).");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Damage observation failed for this hit: {exception.Message}");
        }
    }

    /// <summary>
    /// Adds to the milk reservoir when a sexual hit lands on a swollen body (SPEC003 FR-259).
    ///
    /// The gauge fills from what is done to the player, never from time. "Sexual hit while swollen"
    /// is as close as this build allows to "an attack on the breasts": the game does not label an
    /// attack by where it lands, and inventing that label would be a guess dressed as a measurement.
    /// A full gauge escalates the swelling.
    /// </summary>
    private static void RecordMilkFromHit()
    {
        MilkReservoir? milk = PleasureRuntime.Milk;
        AbnormalList? abnormals = PleasureRuntime.PlayerAbnormals;
        if (milk is null || abnormals is null || BreastPatches.IsGalleryActive())
        {
            return;
        }

        bool super;
        try
        {
            super = abnormals.Has(AbnormalType.BreastSuper);
            if (!super && !abnormals.Has(AbnormalType.Breast))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        // It fills under the escalation as well, which it did not used to. The escalation's only
        // way out is the gauge running down (FR-264), so a sexual hit taken while escalated has to
        // put the way out further away — otherwise the escalation would be a fixed wait that
        // enemies could not make worse, and being caught while swollen would cost nothing.
        float before = milk.Fill;
        bool filled = milk.AddFromHit();
        if (super)
        {
            if (milk.Fill > before)
            {
                PleasureRuntime.LogTransition(
                    $"Milk {before:P0} -> {milk.Fill:P0} while BreastSuper is worn; the escalation "
                    + "lasts longer for it.");
            }

            return;
        }

        if (!filled)
        {
            if (milk.Fill > before)
            {
                PleasureRuntime.LogTransition($"Milk {before:P0} -> {milk.Fill:P0}.");
            }

            return;
        }

        PleasureRuntime.PendingBreastSuper = true;
        PleasureRuntime.Log?.LogInfo("The milk gauge filled; Breast escalates to BreastSuper.");
    }

    /// <summary>
    /// Sexual or not, for one hit (SPEC003 5.3).
    ///
    /// The prefix and the postfix ask the same question about the same hit and must not be able to
    /// disagree: one decides whether HP is taken, the other whether pleasure is given, and a hit
    /// that cost HP but also raised the gauge would be charged twice.
    /// </summary>
    private static AttackKind Classify(DamageStack stack) =>
        PleasureRuntime.Profile.Classifier.Classify(
            PleasureRuntime.BinderEnemyId,
            SenderName(stack),
            PleasureRuntime.AppliedStatuses(stack));

    /// <summary>
    /// The attacker's own name. Needed because an attack that carries no statuses cannot be judged
    /// any other way when the captor is not in the override list.
    /// </summary>
    private static string? SenderName(DamageStack stack)
    {
        try
        {
            return stack.SenderName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Detects a save point or obelisk being used, which is what clears the climax count
/// (SPEC003 5.6, FR-217).
///
/// <c>IsObeliskLabel</c> tells the two apart. Restricting the reset to obelisks is offered but not
/// the default: <c>SavePointSelector.m_ChangeObeliskInHardMode</c> swaps save points for obelisks
/// depending on difficulty, so an obelisk-only rule can leave a run with no reset point at all.
/// </summary>
internal static class SavePointPatches
{
    internal static void ExecutionOneAsyncPostfix(SavePointAsyncLabel __instance)
    {
        try
        {
            bool isObelisk = __instance.IsObeliskLabel;
            PleasureRuntime.Probe(
                $"savepoint-activated-{isObelisk}",
                $"A-7 answered: SavePointAsyncLabel.ExecutionOneAsync ran with IsObeliskLabel={isObelisk}.");

            ClimaxTuning climax = PleasureRuntime.Profile.Climax;
            if (!climax.Enabled || (climax.ResetAtObeliskOnly && !isObelisk))
            {
                return;
            }

            int before = PleasureRuntime.Climaxes.Count;
            PleasureRuntime.Climaxes.ResetCount();

            // Using a save point is when the game writes its save, so it is when the sidecar has to
            // match. Writing on every change instead would leave the file ahead of the save and
            // make loading an earlier one restore the wrong numbers.
            PleasureRuntime.SaveSlot(isObelisk ? "obelisk" : "save point");

            if (before == 0)
            {
                return;
            }

            // Corruption is deliberately untouched: it is the one-way track, and the reset point
            // is not a way down (SPEC003 FR-219).
            PleasureRuntime.Log?.LogInfo(
                $"Climax count reset from {before} to 0 at a "
                + $"{(isObelisk ? "obelisk" : "save point")}. Corruption is unchanged at "
                + $"{PleasureRuntime.Corruption?.Value ?? 0f:F2}.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Save point observation failed: {exception.Message}");
        }
    }
}
