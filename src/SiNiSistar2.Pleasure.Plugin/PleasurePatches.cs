using SiNiSistar2.Damage;
using SiNiSistar2.Obj;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Observes damage as it resolves. In the shipped configuration this only measures: it answers
/// 付録A A-2 (can a hit taken while bound be seen at all) and A-3 (does that hit carry the statuses
/// the classifier needs). The gauge itself stays at zero gain until those are confirmed.
/// </summary>
internal static class DamageProbePatches
{
    internal static void OneDamagePostfix(DamageStack stack)
    {
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
            corruption?.Add(PleasureRuntime.Profile.Corruption.PerSexualHit);
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
