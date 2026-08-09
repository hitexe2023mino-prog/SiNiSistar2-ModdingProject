using SiNiSistar2.Enemy;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 5.3: the MOD-driven extra spawns. Every spawn draws its enemy from a weighted pick of
/// the scene's own spawner pools (FR-304) and its position from the spawners' authored spawn
/// points, filtered off-screen — plus behind-the-player when the ambush roll says so (FR-309/310).
/// </summary>
internal sealed class AdditionalSpawner
{
    private readonly List<EnemyObject> _spawned = new();
    private readonly List<GameObject> _clones = new();
    private int _consecutiveFailures;
    private bool _suspendedForVisit;

    private const int FailureLimit = 5;

    /// <summary>Last outcome, shown on the HUD so a skip is distinguishable from an idle frame.</summary>
    public string LastNote { get; private set; } = "";

    public void ResetForVisit()
    {
        _spawned.Clear();
        // Copies die with the scene they were made in; the references are dropped, not destroyed.
        _clones.Clear();
        _consecutiveFailures = 0;
        _suspendedForVisit = false;
        LastNote = "";
    }

    /// <summary>
    /// Counts the spawn points that currently qualify, for the HUD's POINTS line (SPEC004 5.8-6).
    /// Read-only: it runs the same filters as a spawn attempt without spawning anything.
    /// </summary>
    public (int Offscreen, int Behind) CountCandidates(IReadOnlyList<EnemySpawner> spawners)
    {
        Camera? camera = ManagerList.Camera?.MainCamera;
        Lelia? lelia = ManagerList.Object?.Lelia;
        if (camera is null || lelia is null)
        {
            return (0, 0);
        }

        float playerX = lelia.Position.x;
        FacingDir facing = Facing(lelia);

        var offscreen = new List<(EnemySpawner, Transform)>();
        var behind = new List<(EnemySpawner, Transform)>();
        foreach (EnemySpawner spawner in spawners)
        {
            try
            {
                CollectCandidatePoints(spawner, camera, playerX, facing, requireBehind: false, offscreen);
                CollectCandidatePoints(spawner, camera, playerX, facing, requireBehind: true, behind);
            }
            catch (Exception)
            {
                // A destroyed spawner contributes nothing; the HUD does not need to report it.
            }
        }

        return (offscreen.Count, behind.Count);
    }

    /// <summary>Counts survivors among the enemies this MOD spawned and reports them to the budget.</summary>
    public void ReportAlive(SpawnBudget budget)
    {
        var alive = 0;
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            try
            {
                // Presence in the scene, not IsUsed: that flag belongs to the pool bookkeeping and
                // reads false on a copy, which made the budget believe nothing was alive while
                // five copies stood in the area.
                EnemyObject enemy = _spawned[i];
                if (enemy.gameObject is GameObject body && body.activeInHierarchy && enemy.IsLiving)
                {
                    alive++;
                }
                else
                {
                    _spawned.RemoveAt(i);
                }
            }
            catch (Exception)
            {
                _spawned.RemoveAt(i);
            }
        }

        budget.ReportAlive(alive);
    }

    /// <summary>
    /// One stagnation-penalty spawn attempt (FR-309). Returns without effect when no candidate
    /// position or pool qualifies — never inventing a position (DEC-303) and never forcing a pool.
    /// </summary>
    public void TrySpawnPenalty(
        IReadOnlyList<EnemySpawner> spawners,
        AreaSettings settings,
        SpawnBudget budget,
        IRandomSource random,
        bool? forceAmbush = null)
    {
        // FR-332: a debug command reaches this method the normal way and is still subject to
        // every cap. Only the stagnation timing and the ambush roll can be short-circuited.
        if (_suspendedForVisit)
        {
            LastNote = "suspended for this visit after repeated failures";
            return;
        }

        if (!budget.CanSpawnAdditional)
        {
            LastNote = $"cap reached ({budget.SpawnedThisVisit}/{budget.SpawnCapPerVisit}, "
                + $"alive {budget.AliveAdditional}/{budget.AliveCap})";
            return;
        }

        Camera? camera = ManagerList.Camera?.MainCamera;
        Lelia? lelia = ManagerList.Object?.Lelia;
        if (camera is null || lelia is null)
        {
            LastNote = "player or camera unavailable";
            return;
        }

        bool ambush = forceAmbush ?? random.NextFloat() < settings.AmbushChance;
        float playerX = lelia.Position.x;
        FacingDir facing = Facing(lelia);

        if (ambush && facing == FacingDir.None)
        {
            // FR-310: an unknown facing is never grounds for an ambush; fall back to plain off-screen.
            ambush = false;
        }

        var candidates = new List<(EnemySpawner Spawner, Transform Point)>();
        foreach (EnemySpawner spawner in spawners)
        {
            try
            {
                CollectCandidatePoints(spawner, camera, playerX, facing, ambush, candidates);
            }
            catch (Exception exception)
            {
                SpawnRuntime.Log?.LogWarning(
                    $"Spawn point scan failed on a spawner and it was skipped: {exception.Message}");
            }
        }

        string condition = ambush ? "offscreen+behind" : "offscreen";

        if (candidates.Count == 0)
        {
            // No spawner, or none of its points qualify: fall back to copying an enemy that the
            // area already contains, placed where the area already puts one (SPEC004 5.3 出現源).
            TryCloneSceneEnemy(camera, playerX, facing, ambush, condition, budget, random);
            return;
        }

        (EnemySpawner chosen, Transform point) = candidates[random.NextInt(candidates.Count)];
        if (TrySpawnFrom(chosen, point, random, out string enemyName))
        {
            budget.CountAdditionalSpawn();
            _consecutiveFailures = 0;
            LastNote = $"{enemyName} ({condition})";
            SpawnRuntime.LogIntervention(
                $"penalty spawn: {enemyName} from '{chosen.name}' at "
                + $"({point.position.x:0.#},{point.position.y:0.#}) condition={condition} "
                + $"spawnedThisVisit={budget.SpawnedThisVisit}");
        }
        else
        {
            LastNote = "the enemy pool returned nothing";
            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureLimit)
            {
                // SPEC004 9章: repeated pool failures stop additional spawns for this visit.
                _suspendedForVisit = true;
                SpawnRuntime.Log?.LogWarning(
                    $"Additional spawns are suspended for this visit after {FailureLimit} consecutive failures.");
            }
        }
    }

    /// <summary>
    /// The spawner-less path (SPEC004 5.3): copy an enemy the scene already has, onto the
    /// position of another enemy the scene already has. Both halves stay inside what the author
    /// placed in this area, which is what DEC-302 and DEC-303 are protecting.
    /// </summary>
    private void TryCloneSceneEnemy(
        Camera camera,
        float playerX,
        FacingDir facing,
        bool ambush,
        string condition,
        SpawnBudget budget,
        IRandomSource random)
    {
        List<EnemyObject> enemies = SceneEnemyCatalog.Collect();
        var liveSources = new List<EnemyObject>();
        var dormantSources = new List<EnemyObject>();
        var positions = new List<Vector3>();
        float margin = SpawnRuntime.Profile.OffscreenMargin;

        foreach (EnemyObject enemy in enemies)
        {
            try
            {
                if (SceneEnemyCatalog.IsCopyable(enemy))
                {
                    // A live enemy is one the game has built completely; a dormant one has no
                    // movement yet, and copying it copies that absence (see IsLive).
                    (SceneEnemyCatalog.IsLive(enemy) ? liveSources : dormantSources).Add(enemy);
                }

                Vector3 world = enemy.transform.position;
                Vector3 viewport = camera.WorldToViewportPoint(world);
                if (!SpawnPointClassifier.IsOffscreen(viewport.x, viewport.y, viewport.z, margin))
                {
                    continue;
                }

                if (ambush && !SpawnPointClassifier.IsBehind(world.x, playerX, facing))
                {
                    continue;
                }

                positions.Add(world);
            }
            catch (Exception)
            {
                // A destroyed enemy contributes neither a source nor a position.
            }
        }

        List<EnemyObject> sources = liveSources.Count > 0 ? liveSources : dormantSources;
        if (sources.Count == 0)
        {
            LastNote = enemies.Count == 0
                ? "this area has no enemy to copy"
                : "this area's enemies cannot be copied";
            SpawnRuntime.LogIntervention($"penalty spawn skipped: {LastNote}.");
            return;
        }

        if (liveSources.Count == 0)
        {
            SpawnRuntime.LogIntervention(
                $"no live enemy to copy among {enemies.Count}; copying a dormant one, which may "
                + "not have its movement built yet.");
        }

        if (positions.Count == 0)
        {
            LastNote = $"no {condition} position available";
            SpawnRuntime.LogIntervention(
                $"penalty spawn skipped: no {condition} enemy position among {enemies.Count}.");
            return;
        }

        EnemyObject source = sources[random.NextInt(sources.Count)];
        Vector3 position = positions[random.NextInt(positions.Count)];
        string enemyName = SafeEnemyName(source);

        EnemyObject? copy = SceneEnemyCatalog.Clone(source, position, out GameObject? cloneRoot);
        if (copy is null)
        {
            LastNote = "the enemy copy failed";
            if (cloneRoot is not null)
            {
                UnityEngine.Object.Destroy(cloneRoot);
            }

            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureLimit)
            {
                _suspendedForVisit = true;
                SpawnRuntime.Log?.LogWarning(
                    $"Additional spawns are suspended for this visit after {FailureLimit} failures.");
            }

            return;
        }

        _spawned.Add(copy);
        _clones.Add(cloneRoot!);
        budget.CountAdditionalSpawn();
        _consecutiveFailures = 0;
        LastNote = $"{enemyName} copy ({condition})";
        SpawnRuntime.LogIntervention(
            $"penalty spawn: copied {enemyName} ({(liveSources.Count > 0 ? "live" : "dormant")} source) "
            + $"to ({position.x:0.#},{position.y:0.#}) "
            + $"condition={condition} spawnedThisVisit={budget.SpawnedThisVisit}");
    }

    /// <summary>Destroys the copies this MOD made that are still alive (SPEC004 5.7).</summary>
    public void DestroyClones()
    {
        foreach (GameObject clone in _clones)
        {
            try
            {
                if (clone is not null)
                {
                    UnityEngine.Object.Destroy(clone);
                }
            }
            catch (Exception)
            {
                // Already gone with its scene.
            }
        }

        _clones.Clear();
    }

    private static string SafeEnemyName(EnemyObject enemy)
    {
        try
        {
            return enemy.m_EnemyID.ToString();
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static FacingDir Facing(Lelia lelia) => lelia.Dir switch
    {
        Dir.Left => FacingDir.Left,
        Dir.Right => FacingDir.Right,
        _ => FacingDir.None,
    };

    private static void CollectCandidatePoints(
        EnemySpawner spawner,
        Camera camera,
        float playerX,
        FacingDir facing,
        bool requireBehind,
        List<(EnemySpawner, Transform)> candidates)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform>? points = spawner.m_SpawnPoints;
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<EnemySpawner.SpawnSetting>? settings =
            spawner.m_SpawnSettings;
        if (points is null || points.Length == 0 || settings is null || settings.Length == 0)
        {
            return;
        }

        float margin = SpawnRuntime.Profile.OffscreenMargin;
        foreach (Transform? point in points)
        {
            if (point is null)
            {
                continue;
            }

            Vector3 world = point.position;
            Vector3 viewport = camera.WorldToViewportPoint(world);
            if (!SpawnPointClassifier.IsOffscreen(viewport.x, viewport.y, viewport.z, margin))
            {
                continue;
            }

            if (requireBehind && !SpawnPointClassifier.IsBehind(world.x, playerX, facing))
            {
                continue;
            }

            candidates.Add((spawner, point));
        }
    }

    private bool TrySpawnFrom(EnemySpawner spawner, Transform point, IRandomSource random, out string enemyName)
    {
        enemyName = "?";
        try
        {
            EnemyPool? pool = PickWeightedPool(spawner, random, out enemyName);
            if (pool is null)
            {
                return false;
            }

            EnemyObject? enemy = pool.TryGet((Il2CppSystem.Action<EnemyObject>)((EnemyObject spawned) =>
            {
                spawned.Teleport(point, isFitGround: false);
            }));

            if (enemy is null)
            {
                // Pool exhausted; a silent skip is the specified behaviour (FR-308).
                return false;
            }

            _spawned.Add(enemy);
            return true;
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Additional spawn failed and was skipped: {exception.Message}");
            return false;
        }
    }

    /// <summary>The same weighted pick the spawner itself makes over its SpawnSettings (FR-304).</summary>
    private static EnemyPool? PickWeightedPool(EnemySpawner spawner, IRandomSource random, out string enemyName)
    {
        enemyName = "?";
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<EnemySpawner.SpawnSetting>? settings =
            spawner.m_SpawnSettings;
        if (settings is null || settings.Length == 0)
        {
            return null;
        }

        var totalWeight = 0;
        foreach (EnemySpawner.SpawnSetting? setting in settings)
        {
            totalWeight += Math.Max(0, setting?.m_Weight ?? 0);
        }

        if (totalWeight <= 0)
        {
            return null;
        }

        int roll = random.NextInt(totalWeight);
        foreach (EnemySpawner.SpawnSetting? setting in settings)
        {
            if (setting is null)
            {
                continue;
            }

            roll -= Math.Max(0, setting.m_Weight);
            if (roll < 0)
            {
                enemyName = setting.m_EnemyPoolSetting?.m_SpawnEnemy?.m_EnemyID.ToString() ?? "?";
                return setting.EnemyPool;
            }
        }

        return null;
    }
}
