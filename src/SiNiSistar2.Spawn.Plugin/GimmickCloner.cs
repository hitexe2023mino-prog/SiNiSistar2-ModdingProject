using Il2CppInterop.Runtime;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 5.4: clones of gimmick objects that already exist in the current scene, gated by the
/// user-maintained allowlist (FR-312). The default allowlist is empty, so this whole class is
/// inert until a type has been verified in-game and registered.
/// </summary>
internal sealed class GimmickCloner
{
    private readonly List<GameObject> _clones = new();
    private readonly HashSet<string> _sessionDenied = new(StringComparer.Ordinal);

    /// <summary>Clones allowed gimmicks for this visit, up to the budget (5.4-3).</summary>
    public void CloneForVisit(SpawnProfile profile, SpawnBudget budget, IRandomSource random)
    {
        if (!profile.GimmickCloningEnabled)
        {
            return;
        }

        foreach (string typeName in profile.AllowedGimmickTypes)
        {
            if (!budget.CanCloneGimmick)
            {
                return;
            }

            if (_sessionDenied.Contains(typeName))
            {
                continue;
            }

            try
            {
                CloneOne(typeName, profile, budget, random);
            }
            catch (Exception exception)
            {
                // FR-313: one failure denies the type for the whole session.
                _sessionDenied.Add(typeName);
                SpawnRuntime.Log?.LogWarning(
                    $"Gimmick type '{typeName}' failed to clone and is denied for this session: {exception.Message}");
            }
        }
    }

    private void CloneOne(string typeName, SpawnProfile profile, SpawnBudget budget, IRandomSource random)
    {
        var sources = new List<MonoBehaviour>();
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MonoBehaviour>(), includeInactive: true))
        {
            MonoBehaviour? behaviour = obj.TryCast<MonoBehaviour>();
            if (behaviour is not null && behaviour.GetIl2CppType().Name == typeName)
            {
                sources.Add(behaviour);
            }
        }

        if (sources.Count == 0)
        {
            return;
        }

        MonoBehaviour source = sources[random.NextInt(sources.Count)];
        float range = profile.GimmickCloneOffsetRange;
        var offset = new Vector3(((random.NextFloat() * 2f) - 1f) * range, 0f, 0f);

        GameObject clone = UnityEngine.Object.Instantiate(
            source.gameObject,
            source.transform.position + offset,
            source.transform.rotation);
        clone.name = $"{source.gameObject.name} (spawnmod clone)";

        _clones.Add(clone);
        budget.CountGimmickClone();
        SpawnRuntime.LogIntervention(
            $"gimmick '{typeName}' cloned at ({clone.transform.position.x:0.#},{clone.transform.position.y:0.#}).");
    }

    /// <summary>Destroys every clone this MOD made (SPEC004 5.7-3).</summary>
    public void DestroyAll()
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

    /// <summary>Forgets clone references that died with their scene.</summary>
    public void ForgetSceneObjects() => _clones.Clear();
}
