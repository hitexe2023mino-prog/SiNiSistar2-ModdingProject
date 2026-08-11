using System.Globalization;

namespace SiNiSistar2.Pleasure.Core;

/// <summary>One enemy's line in the statistics page (SPEC006 4.5).</summary>
public sealed record ActorStat(string ActorId, string? DisplayName, int Count);

/// <summary>One status ailment's line in the statistics page (SPEC006 4.5).</summary>
public sealed record DebuffStat(string AbnormalType, string? DisplayName, int Count);

/// <summary>Corruption as the page shows it: where it stands against where it stops.</summary>
public sealed record CorruptionStat(float Value, float Cap);

/// <summary>
/// The climax count and the limit it is measured against.
///
/// <c>Limit</c> is 0 when the limit is switched off, which the page renders as no limit at all
/// rather than as a limit of zero — a zero ceiling is a configuration error, not a run that ends on
/// the first climax (SPEC003 5.5.1).
/// </summary>
public sealed record ClimaxStat(int Count, int Limit);

/// <summary>
/// Everything the statistics page reads in one poll (SPEC006 4.5, FR-607).
///
/// Built whole rather than assembled field by field over the request: the page is polled while the
/// game runs, and a reply that mixed values read a few frames apart would show a top enemy that did
/// not match the counts underneath it.
///
/// Display names are resolved into the snapshot rather than looked up by the page, because they
/// come from the game's own localisation and only the plugin can reach it (FR-613).
/// </summary>
public sealed record StatsSnapshot(
    CorruptionStat Corruption,
    ClimaxStat Climax,
    ActorStat? TopActor,
    IReadOnlyList<ActorStat> ActorClimaxCounts,
    IReadOnlyList<DebuffStat> DebuffCounts,
    string GeneratedAt)
{
    /// <summary>
    /// Assembles a reading from the live counters.
    /// </summary>
    /// <param name="actorName">
    /// The game's name for an enemy, or null when it has none. Null leaves the raw identifier to
    /// stand in on the page, which is the honest fallback when localisation cannot answer (FR-613).
    /// </param>
    /// <param name="generatedAt">
    /// Passed in rather than read from the clock, so the whole snapshot can be built and asserted
    /// without one (SPEC003 4.1).
    /// </param>
    public static StatsSnapshot Build(
        float corruption,
        float corruptionCap,
        int climaxCount,
        int climaxLimit,
        ActorClimaxLedger actors,
        DebuffCounters debuffs,
        Func<string, string?>? actorName,
        Func<string, string?>? debuffName,
        DateTimeOffset generatedAt)
    {
        var actorLines = new List<ActorStat>();
        foreach (KeyValuePair<string, int> entry in actors.Ordered())
        {
            actorLines.Add(new ActorStat(entry.Key, ResolveActor(entry.Key), entry.Value));
        }

        var debuffLines = new List<DebuffStat>();
        foreach (KeyValuePair<string, int> entry in debuffs.Ordered())
        {
            debuffLines.Add(new DebuffStat(entry.Key, Resolve(debuffName, entry.Key), entry.Value));
        }

        KeyValuePair<string, int>? top = actors.TopActor();
        ActorStat? topLine = top is null
            ? null
            : new ActorStat(top.Value.Key, ResolveActor(top.Value.Key), top.Value.Value);

        return new StatsSnapshot(
            new CorruptionStat(Math.Max(0f, corruption), Math.Max(0f, corruptionCap)),
            new ClimaxStat(Math.Max(0, climaxCount), Math.Max(0, climaxLimit)),
            topLine,
            actorLines,
            debuffLines,
            generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

        // The reserved bucket stands for "nobody could be named", so it is never handed to the
        // game's localisation: there is no enemy there to look up.
        string? ResolveActor(string id) =>
            string.Equals(id, ActorClimaxLedger.UnknownActorId, StringComparison.Ordinal)
                ? null
                : Resolve(actorName, id);
    }

    /// <summary>
    /// Asks the game for a name, and treats a throw as "no name".
    ///
    /// The resolver crosses into the game to read localisation, and a poll from a browser must not
    /// be able to carry an exception out of that and into the request loop.
    /// </summary>
    private static string? Resolve(Func<string, string?>? resolver, string key)
    {
        if (resolver is null)
        {
            return null;
        }

        try
        {
            string? name = resolver(key);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A reading with nothing in it, served before a save slot has been identified.</summary>
    public static StatsSnapshot Empty(DateTimeOffset generatedAt) => new(
        new CorruptionStat(0f, 0f),
        new ClimaxStat(0, 0),
        null,
        Array.Empty<ActorStat>(),
        Array.Empty<DebuffStat>(),
        generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
