using Enemy.Character.Sewage;
using SiNiSistar2.Enemy;
using SiNiSistar2.Enemy.Character;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Obj.Loot;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 5.5: pseudo treasure boxes whose bodies are real mimic enemies from the scene's own
/// pools (FR-320, DEC-311/312). The lottery runs at the moment the hold would begin, in
/// <see cref="MimicBoxPatches"/> (DEC-313).
/// </summary>
internal static class MimicBoxPlacement
{
    /// <summary>
    /// Finds every pool of the scene that spawns <c>EnmID_Mimic</c> and places up to the budget's
    /// cap of pseudo boxes on their spawners' points. Scenes without a mimic pool place nothing.
    /// </summary>
    internal static void PlaceAll(
        IReadOnlyList<EnemySpawner> spawners,
        IReadOnlyList<SimpleSpawnArea> simpleAreas,
        SpawnBudget budget,
        IRandomSource random)
    {
        List<(EnemyPool Pool, Transform Point)> candidates = Collect(spawners, simpleAreas);
        if (candidates.Count == 0)
        {
            return;
        }

        var placed = 0;
        var attempts = 0;
        while (budget.CanPlaceMimicBox && attempts < candidates.Count * 4)
        {
            attempts++;
            (EnemyPool pool, Transform point) = candidates[random.NextInt(candidates.Count)];
            if (TryPlace(pool, point))
            {
                budget.CountMimicBox();
                placed++;
            }
        }

        if (placed > 0)
        {
            SpawnRuntime.LogIntervention($"placed {placed} mimic pseudo box(es) from {candidates.Count} candidate point(s).");
        }
    }

    /// <summary>
    /// One box on demand for the debug command (SPEC004 5.9). The per-visit cap still applies:
    /// the command short-circuits the placement moment, not the safety limits (FR-332).
    /// </summary>
    internal static string PlaceOne(
        IReadOnlyList<EnemySpawner> spawners,
        IReadOnlyList<SimpleSpawnArea> simpleAreas,
        SpawnBudget budget,
        IRandomSource random)
    {
        if (!budget.CanPlaceMimicBox)
        {
            return $"pseudo box cap reached ({budget.MimicBoxesPlaced}/{budget.MimicBoxCap})";
        }

        List<(EnemyPool Pool, Transform Point)> candidates = Collect(spawners, simpleAreas);
        if (candidates.Count == 0)
        {
            return "this area has no mimic pool";
        }

        (EnemyPool pool, Transform point) = candidates[random.NextInt(candidates.Count)];
        if (!TryPlace(pool, point))
        {
            return "the mimic pool returned nothing";
        }

        budget.CountMimicBox();
        return "pseudo box placed";
    }

    /// <summary>Whether this area can host pseudo boxes at all, for the HUD (SPEC004 5.8-5).</summary>
    internal static bool HasMimicPool(
        IReadOnlyList<EnemySpawner> spawners,
        IReadOnlyList<SimpleSpawnArea> simpleAreas) => Collect(spawners, simpleAreas).Count > 0;

    private static List<(EnemyPool Pool, Transform Point)> Collect(
        IReadOnlyList<EnemySpawner> spawners,
        IReadOnlyList<SimpleSpawnArea> simpleAreas)
    {
        var candidates = new List<(EnemyPool Pool, Transform Point)>();

        foreach (EnemySpawner spawner in spawners)
        {
            try
            {
                CollectFromSpawner(spawner, candidates);
            }
            catch (Exception exception)
            {
                SpawnRuntime.Log?.LogWarning(
                    $"Mimic candidate scan failed on spawner '{SafeName(spawner)}': {exception.Message}");
            }
        }

        foreach (SimpleSpawnArea area in simpleAreas)
        {
            try
            {
                if (IsMimicPool(area.m_EnemyPoolSetting) && area.EnemyPool is not null && area.m_SpawnPoint is not null)
                {
                    candidates.Add((area.EnemyPool, area.m_SpawnPoint));
                }
            }
            catch (Exception exception)
            {
                SpawnRuntime.Log?.LogWarning(
                    $"Mimic candidate scan failed on simple area '{SafeName(area)}': {exception.Message}");
            }
        }

        return candidates;
    }

    private static void CollectFromSpawner(EnemySpawner spawner, List<(EnemyPool, Transform)> candidates)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<EnemySpawner.SpawnSetting>? settings =
            spawner.m_SpawnSettings;
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform>? points = spawner.m_SpawnPoints;
        if (settings is null || points is null || points.Length == 0)
        {
            return;
        }

        foreach (EnemySpawner.SpawnSetting? setting in settings)
        {
            if (setting is null || !IsMimicPool(setting.m_EnemyPoolSetting))
            {
                continue;
            }

            EnemyPool? pool = setting.EnemyPool;
            if (pool is null)
            {
                continue;
            }

            foreach (Transform? point in points)
            {
                if (point is not null)
                {
                    candidates.Add((pool, point));
                }
            }
        }
    }

    private static bool IsMimicPool(EnemyPoolSetting? setting) =>
        setting?.m_SpawnEnemy is EnemyObject prefab && prefab.m_EnemyID == EnemyID.EnmID_Mimic;

    private static bool TryPlace(EnemyPool pool, Transform point)
    {
        try
        {
            EnemyObject? enemy = pool.TryGet((Il2CppSystem.Action<EnemyObject>)((EnemyObject spawned) =>
            {
                // Teleport with the game's own placement call; ground fitting off because the
                // spawner points are already authored positions (DEC-303, 付録A A-1).
                spawned.Teleport(point, isFitGround: false);
            }));

            if (enemy is null)
            {
                return false;
            }

            SpawnRuntime.MimicBoxes[enemy.GetInstanceID()] = new MimicBoxEntry(enemy);
            SpawnRuntime.LogIntervention(
                $"mimic pseudo box registered at ({point.position.x:0.#},{point.position.y:0.#}) id={enemy.GetInstanceID()}");
            return true;
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Mimic pseudo box placement failed and was skipped: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes one miss body on the frame after its hold was suppressed, then grants the reward
    /// (SPEC004 5.5-6, FR-322). The removal is the A-11 candidate non-death path: deactivation,
    /// never <c>ForceDeadDamage</c>, so no defeat record can be written.
    /// </summary>
    internal static void ProcessPendingMisses()
    {
        while (SpawnRuntime.PendingMimicMisses.Count > 0)
        {
            int id = SpawnRuntime.PendingMimicMisses.Dequeue();
            if (!SpawnRuntime.MimicBoxes.TryGetValue(id, out MimicBoxEntry? entry))
            {
                continue;
            }

            Vector3 position = Vector3.zero;
            var removed = false;
            try
            {
                position = entry.Enemy.Position;
                entry.Enemy.gameObject.SetActive(false);
                removed = true;
            }
            catch (Exception exception)
            {
                // FR-322 縮退: the body stays, its holds stay suppressed via ResolvedMiss.
                SpawnRuntime.Log?.LogWarning(
                    $"Pseudo box body could not be removed; it stays inert in place: {exception.Message}");
            }

            GrantReward(position);
            SpawnRuntime.LogIntervention($"pseudo box id={id} resolved as miss; bodyRemoved={removed}.");
        }
    }

    private static void GrantReward(Vector3 position)
    {
        RewardEntry? drawn = SpawnRuntime.Profile.RewardTable.Draw(SpawnRuntime.Random);
        if (drawn is { } reward)
        {
            try
            {
                InventoryHandler? inventory = ManagerList.PlayerStatus?.InventoryHandler;
                if (inventory is not null && Enum.TryParse(reward.ItemName, out ItemID itemId))
                {
                    inventory.AddItem(itemId, reward.Count);
                    SpawnRuntime.LogIntervention($"reward granted: {reward.ItemName} x{reward.Count}.");
                }
            }
            catch (Exception exception)
            {
                // 9章: a failed grant is logged and skipped; suppression and removal still stand.
                SpawnRuntime.Log?.LogWarning($"Reward item could not be granted: {exception.Message}");
            }
        }

        int lootValue = SpawnRuntime.Profile.RewardLootValue;
        if (lootValue <= 0)
        {
            return;
        }

        try
        {
            DropLootPool? loot = ManagerList.Object?.DropLootPool;
            loot?.Play(position, Vector3.up, LootType.MP, lootValue, addGameOver: false);
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Reward loot scatter failed and was skipped: {exception.Message}");
        }
    }

    private static string SafeName(UnityEngine.Object obj)
    {
        try
        {
            return obj.name;
        }
        catch (Exception)
        {
            return "<destroyed>";
        }
    }
}

/// <summary>
/// The lottery interception (SPEC004 5.5-4/5, FR-321/322). `OnlyHoldEnemy.HoldSetup` is the A-9
/// candidate for "the moment the hold begins"; if measurement moves that point, only this class
/// changes. A prefix returning false skips the hold; any error falls back to the vanilla hold,
/// which is the safe direction (9章: 当たり扱い).
/// </summary>
internal static class MimicBoxPatches
{
    internal static bool HoldSetupPrefix(OnlyHoldEnemy __instance)
    {
        try
        {
            if (!SpawnRuntime.Enabled
                || !SpawnRuntime.MimicBoxes.TryGetValue(__instance.GetInstanceID(), out MimicBoxEntry? entry))
            {
                return true;
            }

            if (entry.State == MimicBoxState.ResolvedMiss)
            {
                return false;
            }

            // DEC-313: the lottery runs exactly once, at first contact. A debug pin (FR-333)
            // replaces the roll for one resolution and is consumed here, reported either way.
            bool? pinned = SpawnRuntime.PinnedMimicOutcome;
            SpawnRuntime.PinnedMimicOutcome = null;
            bool isMimic = pinned ?? SpawnRuntime.Random.NextFloat() < SpawnRuntime.Profile.MimicChance;
            string source = pinned is null ? "rolled" : "PINNED";

            if (isMimic)
            {
                // Hit: from here on this is a vanilla mimic and the MOD never touches it again.
                SpawnRuntime.MimicBoxes.Remove(__instance.GetInstanceID());
                SpawnRuntime.MimicHits++;
                SpawnRuntime.LogIntervention(
                    $"pseudo box id={__instance.GetInstanceID()} resolved as mimic ({source}); vanilla hold proceeds.");
                return true;
            }

            entry.State = MimicBoxState.ResolvedMiss;
            SpawnRuntime.MimicMisses++;
            SpawnRuntime.PendingMimicMisses.Enqueue(__instance.GetInstanceID());
            SpawnRuntime.LogIntervention($"pseudo box id={__instance.GetInstanceID()} resolved as reward ({source}); hold suppressed.");
            return false;
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Pseudo box lottery failed; treating as a real mimic: {exception.Message}");
            return true;
        }
    }
}
