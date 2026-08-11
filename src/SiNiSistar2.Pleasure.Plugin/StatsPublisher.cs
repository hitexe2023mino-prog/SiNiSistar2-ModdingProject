using SiNiSistar2.Lc;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Pleasure.Core;

namespace SiNiSistar2.Pleasure.Plugin;

/// <summary>
/// Builds the reading the statistics page serves (SPEC006 4.5).
///
/// Everything here runs on the game's own thread and leaves behind a finished snapshot. The page is
/// served from a thread pool thread, and a Unity object read from one takes the game down — so the
/// HTTP side is given a value that was already assembled and never a way to ask the game anything
/// (SPEC001 4.3).
///
/// Rebuilt on a timer rather than every frame. The page polls every three seconds, so twice a second
/// is far more than it can show, and it keeps the localisation lookups and the two small lists off
/// the frame budget (SPEC006 8章).
/// </summary>
internal static class StatsPublisher
{
    private const double RebuildIntervalSeconds = 0.5d;

    /// <summary>
    /// What the game calls each status ailment, remembered once.
    ///
    /// A localisation lookup crosses the interop boundary, and the answer cannot change while the
    /// game runs in one language. The same reasoning the captor's name already uses (FR-230).
    /// </summary>
    private static readonly Dictionary<string, string?> DebuffNames = new(StringComparer.Ordinal);

    private static double _nextRebuildAt;

    internal static void Publish(float maxDurability, bool force = false)
    {
        double now = UnityEngine.Time.unscaledTimeAsDouble;
        if (!force && now < _nextRebuildAt)
        {
            return;
        }

        _nextRebuildAt = now + RebuildIntervalSeconds;

        try
        {
            ClimaxTuning climax = PleasureRuntime.Profile.Climax;
            int limit = climax.Enabled && climax.GameOverEnabled
                ? ClimaxLimit.Compute(climax.LimitBase, climax.LimitPerDurability, maxDurability)
                : 0;

            PleasureRuntime.LatestStats = StatsSnapshot.Build(
                PleasureRuntime.Corruption?.Value ?? 0f,
                PleasureRuntime.Corruption?.Cap ?? 0f,
                PleasureRuntime.Climaxes.Count,
                limit,
                PleasureRuntime.ActorClimaxes,
                PleasureRuntime.Debuffs,
                ActorName,
                DebuffName,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            // A reading that could not be built is not worth a frame. The page keeps showing the
            // last one it had, which is what it does for any gap (SPEC006 7章).
            PleasureRuntime.Probe(
                "stats-publish-failed",
                $"The statistics reading could not be built: {exception.Message}");
        }
    }

    /// <summary>
    /// The captor's name as the catalogue recorded it when the hold began (SPEC006 FR-604).
    ///
    /// Read from the catalogue rather than resolved again. The enemy behind an entry may have been
    /// gone for hours, and this MOD must not grow a second way of naming enemies beside the one
    /// SPEC003 5.3.1 already defines.
    /// </summary>
    private static string? ActorName(string actorId) => PleasureRuntime.Enemies.DisplayNameFor(actorId);

    /// <summary>
    /// Reads the name the game has given a status that is attached right now (SPEC006 FR-613).
    ///
    /// <c>AbnormalData.AbnormalNameID</c> is the game's own answer and the only reliable one. The
    /// localisation keys cannot be derived from the type name: <c>Parasite</c> is keyed
    /// <c>ID_Ab_ParasiteLv1</c>, <c>LustMarkCurse</c> is <c>ID_Ab_LustMarkCurse_Lv1</c>,
    /// <c>Spore</c> is <c>ID_Ab_Spore1</c>, and <c>Lustfull</c>, <c>MindControl</c>,
    /// <c>WetNurse</c> and 31 others have no key at all — 34 of the 71 types (付録A A-603). The
    /// spelling also carries the level, which is why the answer belongs to the attached status
    /// rather than to the type.
    ///
    /// Called from the add postfix, which is the moment the data is attached and readable.
    /// </summary>
    internal static string? NameOfAttached(AbnormalList list, AbnormalType type)
    {
        try
        {
            AbnormalData? data = list.GetAbnormalData(type);
            if (data is null)
            {
                return null;
            }

            LocalizeID id = data.AbnormalNameID;
            if (id == LocalizeID.None)
            {
                return null;
            }

            string? text = ManagerList.Localize?.GetLcText(id);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The game's own word for a status ailment, or null to let the enumerator name stand in
    /// (SPEC006 FR-613, A-603).
    ///
    /// What was captured when it was applied comes first, because it is the game's own naming and
    /// it accounts for the level. The exact-key lookup behind it only ever answers for a type whose
    /// name happens to match a key one-for-one, which covers the 37 types that have one; it exists
    /// so a diary restored from a save written before names were stored still reads properly.
    /// </summary>
    private static string? DebuffName(string abnormalType)
    {
        string? captured = PleasureRuntime.Debuffs.DisplayNameFor(abnormalType);
        if (captured is not null)
        {
            return captured;
        }

        if (DebuffNames.TryGetValue(abnormalType, out string? cached))
        {
            return cached;
        }

        string? resolved = ResolveByExactKey(abnormalType);
        DebuffNames[abnormalType] = resolved;
        return resolved;
    }

    /// <summary>
    /// The fallback: a status whose type name is itself a localisation key. Never a guess at a
    /// pattern — either the key exists under exactly this name or there is no answer.
    /// </summary>
    private static string? ResolveByExactKey(string abnormalType)
    {
        try
        {
            if (!Enum.TryParse($"ID_Ab_{abnormalType}", out LocalizeID id) || id == LocalizeID.None)
            {
                return null;
            }

            LocalizeManager? localize = ManagerList.Localize;
            string? text = localize?.GetLcText(id);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Drops the cached names so a language change is picked up on the next reading.</summary>
    internal static void Forget()
    {
        DebuffNames.Clear();
        _nextRebuildAt = 0d;
    }
}
