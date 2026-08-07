using SiNiSistar2.Damage;
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

            bool bound = PleasureRuntime.IsBound;
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

            AttackKind kind = PleasureRuntime.Profile.Classifier.Classify(enemyId, statuses);
            PleasureRuntime.Probe(
                $"classified-{enemyId ?? "unknown"}-{kind}",
                $"Captor '{enemyId ?? "(unidentified)"}' classified as {kind} "
                + $"from [{string.Join(", ", statuses)}].");

            // Everything above is measurement. The gauge only moves once it has been tuned.
            PleasureTuning tuning = PleasureRuntime.Profile.Pleasure;
            if (!tuning.HasEffect || kind != AttackKind.Sexual)
            {
                return;
            }

            SensitivityTrack? sensitivity = PleasureRuntime.Sensitivity;
            PleasureMeter? meter = PleasureRuntime.Meter;
            if (meter is null)
            {
                return;
            }

            sensitivity?.Add(PleasureRuntime.Profile.Sensitivity.PerSexualHit);
            if (meter.AddSexualHit(sensitivity?.Value ?? 0f))
            {
                PleasureRuntime.PendingClimax = true;
            }

            PleasureRuntime.LogTransition(
                $"Pleasure {meter.Value:F2} (sensitivity {sensitivity?.Value ?? 0f:F2}).");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Damage observation failed for this hit: {exception.Message}");
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

            if (PleasureRuntime.Climaxes.Count == 0)
            {
                return;
            }

            int before = PleasureRuntime.Climaxes.Count;
            PleasureRuntime.Climaxes.ResetCount();

            // Sensitivity is deliberately untouched: it is the one-way track, and the reset point
            // is not a way down (SPEC003 FR-219).
            PleasureRuntime.Log?.LogInfo(
                $"Climax count reset from {before} to 0 at a "
                + $"{(isObelisk ? "obelisk" : "save point")}. Sensitivity is unchanged at "
                + $"{PleasureRuntime.Sensitivity?.Value ?? 0f:F2}.");
        }
        catch (Exception exception)
        {
            PleasureRuntime.Log?.LogWarning($"Save point observation failed: {exception.Message}");
        }
    }
}
