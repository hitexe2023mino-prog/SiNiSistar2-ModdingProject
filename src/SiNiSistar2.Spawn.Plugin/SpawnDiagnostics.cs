using System.Text.Json;
using Il2CppInterop.Runtime;
using SiNiSistar2.Enemy.Character;
using SiNiSistar2.EventLabel;
using SiNiSistar2.Obj;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 FR-318: the per-area inventory dump the gimmick allowlist and the mimic-area survey
/// (付録A A-12) are built from. Read-only against the game; any failure only loses the dump.
/// </summary>
internal static class SpawnDiagnostics
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal static void DumpArea(
        string sceneName,
        int sceneId,
        IReadOnlyList<EnemySpawner> spawners,
        IReadOnlyList<SimpleSpawnArea> simpleAreas,
        bool excluded)
    {
        try
        {
            var spawnerRows = new List<object>();
            var mimicPools = 0;
            foreach (EnemySpawner spawner in spawners)
            {
                spawnerRows.Add(DescribeSpawner(spawner, ref mimicPools));
            }

            var simpleRows = new List<object>();
            foreach (SimpleSpawnArea area in simpleAreas)
            {
                simpleRows.Add(DescribeSimpleArea(area, ref mimicPools));
            }

            Dictionary<string, int> histogram = GimmickHistogram();

            var document = new
            {
                scene = sceneName,
                sceneId,
                excludedByProfile = excluded,
                mimicPoolCount = mimicPools,
                spawners = spawnerRows,
                simpleSpawnAreas = simpleRows,
                // TreasureBoxParameter is an Il2CppSystem.Object, not a UnityEngine.Object, so it
                // cannot be searched for; its MonoBehaviour is counted through the histogram.
                treasureBoxLabels = histogram.TryGetValue("TreasureBoxLabel", out int boxes) ? boxes : 0,
                sceneEnemies = SceneEnemies(),
                gimmickTypeHistogram = histogram,
            };

            string directory = DiagnosticsDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, $"{Sanitize(sceneName)}.json"),
                JsonSerializer.Serialize(document, Options));
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Area diagnostics dump failed: {exception.Message}");
        }
    }

    private static object DescribeSpawner(EnemySpawner spawner, ref int mimicPools)
    {
        var enemies = new List<string>();
        try
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<EnemySpawner.SpawnSetting>? settings =
                spawner.m_SpawnSettings;
            if (settings is not null)
            {
                foreach (EnemySpawner.SpawnSetting? setting in settings)
                {
                    EnemyID id = setting?.m_EnemyPoolSetting?.m_SpawnEnemy?.m_EnemyID ?? EnemyID.None;
                    enemies.Add(id.ToString());
                    if (id == EnemyID.EnmID_Mimic)
                    {
                        mimicPools++;
                    }
                }
            }

            return new
            {
                name = spawner.name,
                spawnCount = new[] { spawner.m_SpawnCount.x, spawner.m_SpawnCount.y },
                hardCountOverride = spawner.m_SpawnCountHasHardModeOverride,
                spawnPoints = spawner.m_SpawnPoints?.Length ?? 0,
                enemies,
            };
        }
        catch (Exception exception)
        {
            return new { name = "<error>", error = exception.Message, enemies };
        }
    }

    private static object DescribeSimpleArea(SimpleSpawnArea area, ref int mimicPools)
    {
        try
        {
            EnemyID id = area.m_EnemyPoolSetting?.m_SpawnEnemy?.m_EnemyID ?? EnemyID.None;
            if (id == EnemyID.EnmID_Mimic)
            {
                mimicPools++;
            }

            return new { name = area.name, enemy = id.ToString(), interval = area.m_Interval };
        }
        catch (Exception exception)
        {
            return new { name = "<error>", error = exception.Message };
        }
    }

    /// <summary>
    /// The enemies actually present in the scene, by <c>EnemyID</c>.
    ///
    /// This build populates ordinary areas with concrete <c>EnemyObject</c> instances rather than
    /// with <c>EnemySpawner</c>, so this is the inventory that describes what an area really
    /// contains — the spawner list is empty in every area checked so far.
    /// </summary>
    internal static Dictionary<string, int> SceneEnemies()
    {
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(
                Il2CppType.Of<EnemyObject>(), includeInactive: true))
            {
                EnemyObject? enemy = obj.TryCast<EnemyObject>();
                if (enemy is null)
                {
                    continue;
                }

                string id = enemy.m_EnemyID.ToString();
                histogram[id] = histogram.TryGetValue(id, out int count) ? count + 1 : 1;
            }
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Scene enemy scan failed: {exception.Message}");
        }

        return histogram;
    }

    /// <summary>
    /// Histogram of scene MonoBehaviour type names, the raw material for choosing gimmick
    /// allowlist entries (SPEC004 5.4-2). Runs only when diagnostics are on.
    /// </summary>
    private static Dictionary<string, int> GimmickHistogram()
    {
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MonoBehaviour>(), includeInactive: true))
            {
                MonoBehaviour? behaviour = obj.TryCast<MonoBehaviour>();
                if (behaviour is null)
                {
                    continue;
                }

                string name = behaviour.GetIl2CppType().Name;
                histogram[name] = histogram.TryGetValue(name, out int count) ? count + 1 : 1;
            }
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Gimmick histogram failed: {exception.Message}");
        }

        return histogram;
    }

    private static string DiagnosticsDirectory() => Path.Combine(
        BepInEx.Paths.GameRootPath,
        "BepInEx",
        "diagnostics",
        "community.sinisistar2.spawn",
        "inventory");

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }
}
