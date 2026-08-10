using SiNiSistar2.Enemy;
using SiNiSistar2.Enemy.Character;
using SiNiSistar2.Obj;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 5.2: per-visit multipliers on the scene's spawners. Only the plain fields are written;
/// the `*Hard` fields and their override flags are read to resolve the base value and never
/// written (FR-305, DEC-309). Originals are kept so mid-scene disable can put them back (FR-306).
///
/// Whether a write after scene setup reaches the next spawn cycle is 付録A A-2; if it does not,
/// the write is a safe no-op, which is the agreed degraded direction.
/// </summary>
internal sealed class SpawnerTuningLedger
{
    private sealed record SpawnerEntry(
        EnemySpawner Spawner,
        Vector2Int Count,
        Vector2 Interval,
        Vector2 CoolTime,
        Vector2 CoolTimeFirst);

    private sealed record SimpleAreaEntry(SimpleSpawnArea Area, float Interval);

    private sealed record PoolSettingEntry(EnemyPoolSetting Setting, int MaxSpawn);

    private readonly List<SpawnerEntry> _spawners = new();
    private readonly List<SimpleAreaEntry> _simpleAreas = new();
    private readonly List<PoolSettingEntry> _poolSettings = new();
    private readonly HashSet<IntPtr> _touchedPoolSettings = new();

    private float _countSum;
    private float _intervalSum;
    private float _coolSum;
    private float _maxSpawnSum;
    private int _drawCount;
    private int _maxSpawnDraws;

    public int TunedCount => _spawners.Count + _simpleAreas.Count;

    // Per-spawner independent draws (SPEC004 5.2) have no single representative value, so the
    // HUD is given the mean and labels it as one.
    public float MeanSpawnCountMultiplier => Mean(_countSum, _drawCount);

    public float MeanSpawnIntervalMultiplier => Mean(_intervalSum, _drawCount);

    public float MeanCoolTimeMultiplier => Mean(_coolSum, _drawCount);

    public float MeanMaxSpawnMultiplier => Mean(_maxSpawnSum, _maxSpawnDraws);

    private static float Mean(float sum, int count) => count == 0 ? 1f : sum / count;

    /// <summary>Applies the area's multiplier draws to one EnemySpawner (SPEC004 5.2 の表).</summary>
    public void Tune(EnemySpawner spawner, AreaSettings settings, IRandomSource random, bool isHard)
    {
        float countMult = settings.SpawnCount.Sample(random);
        float intervalMult = settings.SpawnInterval.Sample(random);
        float coolMult = settings.CoolTime.Sample(random);

        _countSum += countMult;
        _intervalSum += intervalMult;
        _coolSum += coolMult;
        _drawCount++;

        Vector2Int baseCount = isHard && spawner.m_SpawnCountHasHardModeOverride
            ? spawner.m_SpawnCountHard
            : spawner.m_SpawnCount;
        Vector2 baseInterval = isHard && spawner.m_SpawnIntervalHasHardModeOverride
            ? spawner.m_SpawnIntervalHard
            : spawner.m_SpawnInterval;
        Vector2 baseCool = isHard && spawner.m_CoolTimeHasHardModeOverride
            ? spawner.m_CoolTimeHard
            : spawner.m_CoolTime;
        Vector2 baseCoolFirst = isHard && spawner.m_CoolTimeFirstHasHardModeOverride
            ? spawner.m_CoolTimeFirstHard
            : spawner.m_CoolTimeFirst;

        _spawners.Add(new SpawnerEntry(
            spawner,
            spawner.m_SpawnCount,
            spawner.m_SpawnInterval,
            spawner.m_CoolTime,
            spawner.m_CoolTimeFirst));

        spawner.m_SpawnCount = new Vector2Int(
            SpawnScaling.ScaleCount(baseCount.x, countMult),
            SpawnScaling.ScaleCount(baseCount.y, countMult));
        spawner.m_SpawnInterval = Scale(baseInterval, intervalMult);
        spawner.m_CoolTime = Scale(baseCool, coolMult);
        spawner.m_CoolTimeFirst = Scale(baseCoolFirst, coolMult);

        TunePoolSettings(spawner, settings, random, isHard, out string pools);

        SpawnRuntime.LogIntervention(
            $"spawner '{spawner.name}' tuned: count {Fmt(baseCount)} x{countMult:0.###} -> "
            + $"{Fmt(spawner.m_SpawnCount)}, interval x{intervalMult:0.###}, cool x{coolMult:0.###}, "
            + $"hardBase={isHard}, pools=[{pools}]");
    }

    /// <summary>Applies the interval and pool multipliers to one SimpleSpawnArea (付録A A-7).</summary>
    public void Tune(SimpleSpawnArea area, AreaSettings settings, IRandomSource random, bool isHard)
    {
        float intervalMult = settings.SpawnInterval.Sample(random);
        _simpleAreas.Add(new SimpleAreaEntry(area, area.m_Interval));
        area.m_Interval = SpawnScaling.ScaleDelay(area.m_Interval, intervalMult);

        EnemyPoolSetting? setting = area.m_EnemyPoolSetting;
        if (setting is not null)
        {
            TuneMaxSpawn(setting, settings.MaxSpawn.Sample(random), isHard);
        }

        SpawnRuntime.LogIntervention(
            $"simple spawn area '{area.name}' tuned: interval x{intervalMult:0.###}, hardBase={isHard}");
    }

    private void TunePoolSettings(
        EnemySpawner spawner, AreaSettings settings, IRandomSource random, bool isHard, out string described)
    {
        var names = new List<string>();
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<EnemySpawner.SpawnSetting>? spawnSettings =
            spawner.m_SpawnSettings;
        if (spawnSettings is null)
        {
            described = "";
            return;
        }

        foreach (EnemySpawner.SpawnSetting? spawnSetting in spawnSettings)
        {
            EnemyPoolSetting? poolSetting = spawnSetting?.m_EnemyPoolSetting;
            if (poolSetting is null)
            {
                continue;
            }

            TuneMaxSpawn(poolSetting, settings.MaxSpawn.Sample(random), isHard);
            names.Add(poolSetting.m_SpawnEnemy?.m_EnemyID.ToString() ?? "?");
        }

        described = string.Join(",", names);
    }

    private void TuneMaxSpawn(EnemyPoolSetting setting, float multiplier, bool isHard)
    {
        // A pool setting can be shared by several spawn settings; multiplying it once per visit
        // keeps a shared asset from being raised repeatedly.
        IntPtr pointer = setting.Pointer;
        if (!_touchedPoolSettings.Add(pointer))
        {
            return;
        }

        int baseMax = isHard && setting.m_MaxSpawnHasHardModeOverride
            ? setting.m_MaxSpawnHard
            : setting.m_MaxSpawn;

        _maxSpawnSum += multiplier;
        _maxSpawnDraws++;

        _poolSettings.Add(new PoolSettingEntry(setting, setting.m_MaxSpawn));
        setting.m_MaxSpawn = SpawnScaling.ScaleCount(baseMax, multiplier);
    }

    /// <summary>
    /// Puts every original value back (SPEC004 5.7-1). Entries whose objects died with the scene
    /// are skipped: the scene took the modified values with it.
    /// </summary>
    public void RestoreAll()
    {
        foreach (SpawnerEntry entry in _spawners)
        {
            try
            {
                entry.Spawner.m_SpawnCount = entry.Count;
                entry.Spawner.m_SpawnInterval = entry.Interval;
                entry.Spawner.m_CoolTime = entry.CoolTime;
                entry.Spawner.m_CoolTimeFirst = entry.CoolTimeFirst;
            }
            catch (Exception)
            {
                // The spawner no longer exists; nothing is left to restore.
            }
        }

        foreach (SimpleAreaEntry entry in _simpleAreas)
        {
            try
            {
                entry.Area.m_Interval = entry.Interval;
            }
            catch (Exception)
            {
            }
        }

        foreach (PoolSettingEntry entry in _poolSettings)
        {
            try
            {
                entry.Setting.m_MaxSpawn = entry.MaxSpawn;
            }
            catch (Exception)
            {
            }
        }

        Clear();
    }

    /// <summary>Forgets the ledger without writing; used when the scene it described is gone.</summary>
    public void Clear()
    {
        _spawners.Clear();
        _simpleAreas.Clear();
        _poolSettings.Clear();
        _touchedPoolSettings.Clear();
        _countSum = 0f;
        _intervalSum = 0f;
        _coolSum = 0f;
        _maxSpawnSum = 0f;
        _drawCount = 0;
        _maxSpawnDraws = 0;
    }

    private static Vector2 Scale(Vector2 baseValue, float multiplier) => new(
        SpawnScaling.ScaleDelay(baseValue.x, multiplier),
        SpawnScaling.ScaleDelay(baseValue.y, multiplier));

    private static string Fmt(Vector2Int v) => $"[{v.x},{v.y}]";
}
