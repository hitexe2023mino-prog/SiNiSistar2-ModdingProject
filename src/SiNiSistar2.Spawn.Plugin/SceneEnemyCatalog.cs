using Il2CppInterop.Runtime;
using SiNiSistar2.Manager;
using Lightbug.CharacterControllerPro.Core;
using SiNiSistar2.Obj;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// The enemies an area actually contains, and the means to copy one (SPEC004 5.3 出現源, DEC-302).
///
/// This build populates ordinary areas by placing <c>EnemyObject</c> instances in the scene, not
/// by running <c>EnemySpawner</c>; a real area was measured with zero spawners and eight enemies
/// (付録A A-15). Copying one of those is what keeps an added enemy "the kind that belongs here"
/// without loading anything from outside the scene.
/// </summary>
internal static class SceneEnemyCatalog
{
    /// <summary>Every EnemyObject in the loaded scenes, including the inactive ones.</summary>
    internal static List<EnemyObject> Collect()
    {
        var enemies = new List<EnemyObject>();
        try
        {
            foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(
                Il2CppType.Of<EnemyObject>(), includeInactive: true))
            {
                EnemyObject? enemy = obj.TryCast<EnemyObject>();
                if (enemy is not null)
                {
                    enemies.Add(enemy);
                }
            }
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Scene enemy scan failed: {exception.Message}");
        }

        return enemies;
    }

    /// <summary>
    /// Whether this enemy may be copied. Bosses and anything named as a boss are refused: a second
    /// boss is a different kind of change from a second wandering enemy, and the game's event and
    /// arena logic assumes one (SPEC004 2.2, DEC-310).
    /// </summary>
    internal static bool IsCopyable(EnemyObject enemy)
    {
        try
        {
            if (!enemy.m_IsEnemyIDOwner)
            {
                // Not the owner of its identity: a part of a larger creature rather than a unit.
                return false;
            }

            string id = enemy.m_EnemyID.ToString();
            return !id.Contains("Boss", StringComparison.OrdinalIgnoreCase)
                && !id.Contains("Mother", StringComparison.OrdinalIgnoreCase)
                && enemy.m_EnemyID != EnemyID.None;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether this enemy is one the game has actually brought to life.
    ///
    /// The scan deliberately includes inactive objects, and most of an area's enemies are
    /// dormant until the player nears them: measured, a source picked without this check read
    /// <c>walk=null, active=False, setupEnd=False</c>. Copying a dormant template means copying
    /// something whose movement was never built, and no amount of setting up the copy afterwards
    /// puts back what the original never had.
    /// </summary>
    internal static bool IsLive(EnemyObject enemy)
    {
        try
        {
            return enemy.gameObject.activeInHierarchy && enemy.IsSetupEnd;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The outermost ancestor that still contains only this enemy.
    ///
    /// The component sits deep inside its own hierarchy (<c>EnemyZombie (6)/CCP_Enemy/Zombie</c>),
    /// and copying only the component's own object would leave the colliders, effects and AI
    /// children behind. Climbing stops as soon as a parent holds a second enemy, which is the
    /// scene's container.
    /// </summary>
    internal static Transform RootOf(EnemyObject enemy)
    {
        Transform root = enemy.transform;

        while (root.parent is Transform parent && parent.parent is not null)
        {
            if (CountEnemiesUnder(parent) > 1)
            {
                break;
            }

            root = parent;
        }

        return root;
    }

    private static int CountEnemiesUnder(Transform transform)
    {
        try
        {
            return transform.GetComponentsInChildren(Il2CppType.Of<EnemyObject>(), true).Length;
        }
        catch (Exception)
        {
            // Unknown means "stop climbing"; a smaller copy is safer than one that swallows the
            // rest of the scene.
            return int.MaxValue;
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="position"/> and returns the copy's
    /// <c>EnemyObject</c>, or null when the copy could not be made or wired up.
    ///
    /// The copy is renamed so its hierarchy path differs from the original's. The game keys saved
    /// enemy state by path (<c>TemporaryEnemySaver.m_Path</c>), and two objects sharing a path is
    /// the way a copy could corrupt the original's saved state (付録A A-16).
    /// </summary>
    internal static EnemyObject? Clone(EnemyObject source, Vector3 position, out GameObject? clone)
    {
        clone = null;
        try
        {
            Transform root = RootOf(source);

            // Copied where the original stands, not straight to the target. The root and the
            // EnemyObject's own transform sit at different depths of the hierarchy, so dropping
            // the root at a position measured from an EnemyObject offsets the copy by whatever
            // separates them — into the ground or above it. The move is done afterwards through
            // the game's own Teleport, which is what knows how to place a character.
            UnityEngine.Object? spawned = UnityEngine.Object.Instantiate(
                root.gameObject, root.position, root.rotation);

            clone = spawned?.TryCast<GameObject>();
            if (clone is null)
            {
                return null;
            }

            clone.name = $"{root.gameObject.name}-spawnmod-{clone.GetInstanceID()}";
            clone.transform.SetParent(root.parent, worldPositionStays: true);
            clone.SetActive(true);

            Component? component = clone.GetComponentInChildren(Il2CppType.Of<EnemyObject>(), true);
            EnemyObject? copy = component?.TryCast<EnemyObject>();
            if (copy is null)
            {
                return null;
            }

            Vector3 beforeMove = copy.transform.position;
            Move(copy, position);
            Vector3 afterMove = copy.transform.position;
            Initialise(copy);
            ReportMovementDifference(source, copy);

            if (SpawnRuntime.PendingCopyCheck is null && !_settledReported)
            {
                SpawnRuntime.PendingCopyCheck =
                    (copy, Time.timeAsDouble + 2.0, copy.transform.position);
            }

            if (!_placementReported)
            {
                _placementReported = true;
                SpawnRuntime.Log?.LogInfo(
                    $"[copy] placement: source=({beforeMove.x:0.##},{beforeMove.y:0.##}) "
                    + $"target=({position.x:0.##},{position.y:0.##}) "
                    + $"afterTeleport=({afterMove.x:0.##},{afterMove.y:0.##}) "
                    + $"afterInit=({copy.transform.position.x:0.##},{copy.transform.position.y:0.##}).");
            }

            return copy;
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"Enemy copy failed and was skipped: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Brings a copy into play through the game's own entry points.
    ///
    /// A plain <c>Instantiate</c> produces an object the game has never been told about: it is not
    /// in <c>ObjectManager</c>'s list, its HP and state machine were never set up, and its spawn
    /// process never ran. The first build shipped without this and the copies stood inert and
    /// could not be damaged — <c>alive 0/3</c> in the log while five had been placed.
    ///
    /// Each step is attempted separately and the outcome is logged once per session, because
    /// which of them the game actually requires can only be settled by running it (付録A A-17).
    /// </summary>
    /// <summary>
    /// Moves the copy with the game's own relocation, which fits it to the ground and updates
    /// whatever internal state a character move requires. A raw transform assignment leaves a
    /// character standing on nothing, which is the shape of "it spawned but cannot move".
    /// </summary>
    private static void Move(EnemyObject copy, Vector3 position)
    {
        try
        {
            copy.Teleport(position, useZ: false, isFitGround: true);
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning(
                $"[copy] Teleport failed; the copy stays where the original stands: {exception.Message}");
        }
    }

    /// <summary>
    /// Rebuilds whichever link of the movement chain the copy is missing.
    ///
    /// Read from the game's own <c>SiNiObject.FixedUpdate</c>, movement runs as
    /// <c>Walk.VelocityUpdate</c> → <c>Gravity.FixedUpdate</c> → <c>ActorHandler.FixedUpdate</c>,
    /// and every one of those is null-checked and skipped in silence. A copy missing any of them
    /// plays its walk animation and stays where it stands, with nothing logged anywhere. The
    /// handler in turn does nothing at all when its <c>CharacterActor</c> is null, so that is
    /// checked first.
    ///
    /// Only missing links are replaced: a chain the copy inherited intact is left alone.
    /// </summary>
    private static void RepairMovementChain(EnemyObject copy, List<string> steps)
    {
        try
        {
            CharacterActor? actor = copy.m_CharacterActor;
            if (actor is null)
            {
                // Nothing downstream can work, and nothing here can invent an actor.
                steps.Add("actor=NULL (movement cannot be repaired)");
                return;
            }

            ActorHandler? handler = copy.ActorHandler;
            if (handler is null)
            {
                handler = new ActorHandler(actor);
                copy._ActorHandler_k__BackingField = handler;
                steps.Add("rebuilt ActorHandler");
            }

            if (copy.Walk is null)
            {
                copy._Walk_k__BackingField = new Walk(copy, handler);
                steps.Add("rebuilt Walk");
            }

            if (copy.Gravity is null)
            {
                steps.Add("gravity=NULL (needs GravityParam; left alone)");
            }
        }
        catch (Exception exception)
        {
            steps.Add($"movement repair failed ({exception.Message})");
        }
    }

    /// <summary>
    /// Nulls the copy's inherited state dictionary so the behaviour can be started fresh.
    ///
    /// The field is declared on the concrete enemy type (<c>Zombie</c> holds a
    /// <c>SequenceDict&lt;Zombie.State&gt;</c>), so it is reached through the object's own IL2CPP
    /// class rather than a typed property. Returns false when the type has no such field, which is
    /// the case for enemies whose behaviour is not built that way.
    /// </summary>
    private static bool ClearSequenceDict(EnemyObject copy)
    {
        try
        {
            IntPtr klass = IL2CPP.il2cpp_object_get_class(copy.Pointer);
            IntPtr field = IL2CPP.GetIl2CppField(klass, "<SequenceDict>k__BackingField");
            if (field == IntPtr.Zero)
            {
                return false;
            }

            var offset = (int)IL2CPP.il2cpp_field_get_offset(field);
            IL2CPP.il2cpp_gc_wbarrier_set_field(
                copy.Pointer, IntPtr.Add(copy.Pointer, offset), IntPtr.Zero);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Initialise(EnemyObject copy)
    {
        var steps = new List<string>();

        try
        {
            ManagerList.Object?.SetUpSiNiObject(copy);
            steps.Add("SetUpSiNiObject");
        }
        catch (Exception exception)
        {
            steps.Add($"SetUpSiNiObject failed ({exception.Message})");
        }

        // Without this the copy reported IsLiving=False: registered and set up, but with no HP it
        // is not a living enemy, so it cannot be damaged and its death state never resolves.
        try
        {
            copy.SetUpHP();
            steps.Add("SetUpHP");
        }
        catch (Exception exception)
        {
            steps.Add($"SetUpHP failed ({exception.Message})");
        }

        try
        {
            copy.StartRunning();
            steps.Add("StartRunning");
        }
        catch (Exception exception)
        {
            steps.Add($"StartRunning failed ({exception.Message})");
        }

        try
        {
            copy.StartSpawnProcess();
            steps.Add("StartSpawnProcess");
        }
        catch (Exception exception)
        {
            steps.Add($"StartSpawnProcess failed ({exception.Message})");
        }

        // Rebuild any missing link of the movement chain before touching the actor.
        RepairMovementChain(copy, steps);

        // The actor is deliberately left alone. Start() and SetKinematic(false) were added here on
        // a guess and both were wrong:
        //
        //  - a copy taken from a live source already has a started handler, so Start() only
        //    subscribes its collider callback a second time;
        //  - these characters are driven by the character controller while kinematic. Handing the
        //    body to Unity's physics instead is what left the copy ungrounded, and an ungrounded
        //    character does not walk. The measurement was unambiguous: every link of the chain
        //    read ok on both, and the only difference between the original and the copy was
        //    grounded=True against grounded=False.
        //
        // SetTimeSetting is likewise unnecessary: ActorHandler.FixedUpdate falls back to
        // Time.deltaTime when it has none.

        // The behaviour state machine.
        //
        // A copy inherits its source's <SequenceDict> — the states are serialised with the object
        // — but not the task that walks it, because a running UniTask is not part of what
        // Instantiate copies. That combination is the whole of "it attacks but never moves": the
        // collider-driven reactions (hold, grab) still fire, while everything the state machine
        // drives, walking included, never runs. It is also why calling StartTask on its own threw
        // "An item with the same key has already been added. Key: Ready": the states were already
        // there.
        //
        // Emptying the dictionary first lets StartTask rebuild it and start the task cleanly.
        try
        {
            if (ClearSequenceDict(copy))
            {
                steps.Add("cleared SequenceDict");
                ObjectManager? objects = ManagerList.Object;
                if (objects is null)
                {
                    steps.Add("StartTask skipped (no ObjectManager)");
                }
                else
                {
                    // Returns a UniTask: a failure inside it surfaces asynchronously and will
                    // appear in the log as an unhandled exception rather than here.
                    copy.StartTask(objects.Token);
                    steps.Add("StartTask requested");
                }
            }
            else
            {
                steps.Add("SequenceDict not found; behaviour left as inherited");
            }
        }
        catch (Exception exception)
        {
            steps.Add($"behaviour start failed ({exception.Message})");
        }

        if (!_initialisationReported)
        {
            _initialisationReported = true;
            SpawnRuntime.Log?.LogInfo($"[copy] enemy initialisation: {string.Join(", ", steps)}.");

            try
            {
                SpawnRuntime.Log?.LogInfo(
                    $"[copy] first copy reports IsUsed={copy.IsUsed}, IsLiving={copy.IsLiving}, "
                    + $"IsSetupEnd={copy.IsSetupEnd}, deadState={copy.DeadState}, "
                    + $"enemyId={copy.m_EnemyID}. IsLiving must be True for it to take damage.");
            }
            catch (Exception exception)
            {
                SpawnRuntime.Log?.LogInfo($"[copy] state could not be read: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Reports the movement machinery of a working original beside the copy that will not move.
    ///
    /// Movement is built in code rather than serialised (<c>Walk</c> is constructed with the
    /// object and its actor handler), so whether a copy can walk depends on parts that a
    /// screenshot cannot show. Comparing the two is what turns "it does not move" into a named
    /// difference; guessing at it one property per session is not a method.
    /// </summary>
    private static void ReportMovementDifference(EnemyObject source, EnemyObject copy)
    {
        if (_movementReported)
        {
            return;
        }

        _movementReported = true;
        SpawnRuntime.Log?.LogInfo($"[copy] movement, original: {DescribeMovement(source)}");
        SpawnRuntime.Log?.LogInfo($"[copy] movement, copy:     {DescribeMovement(copy)}");
    }

    private static string DescribeMovement(EnemyObject enemy)
    {
        var parts = new List<string>();

        // The chain SiNiObject.FixedUpdate actually walks, read from the game's own code:
        //   [this+192] Walk.VelocityUpdate → [this+168] ActorHandler.FixedUpdate
        // and ActorHandler.FixedUpdate returns immediately when its CharacterActor is null or
        // destroyed. Every link is null-checked and skipped silently, so the broken one has to be
        // named rather than inferred.
        Add(parts, "walk", () => enemy.Walk is null ? "NULL" : "ok");
        Add(parts, "gravity", () => enemy.Gravity is null ? "NULL" : "ok");
        Add(parts, "handler", () => enemy.ActorHandler is null ? "NULL" : "ok");
        Add(parts, "actor", () => enemy.m_CharacterActor is null ? "NULL" : "ok");
        Add(parts, "handlerEnabled", () => enemy.ActorHandler.IsEnabled.ToString());
        Add(parts, "grounded", () => enemy.ActorHandler.IsGrounded.ToString());
        Add(parts, "walkSpeed", () => enemy.GetWalkSpeed().ToString("0.##"));
        Add(parts, "behaviourEnabled", () => enemy.enabled.ToString());
        Add(parts, "active", () => enemy.gameObject.activeInHierarchy.ToString());
        Add(parts, "setupEnd", () => enemy.IsSetupEnd.ToString());

        return string.Join(", ", parts);
    }

    private static void Add(List<string> parts, string name, Func<string> read)
    {
        try
        {
            parts.Add($"{name}={read()}");
        }
        catch (Exception exception)
        {
            parts.Add($"{name}=<{exception.GetType().Name}>");
        }
    }

    /// <summary>
    /// Re-reads the copy a couple of seconds on, and says whether it settled and whether it has
    /// moved at all since. Grounding on the spawn frame proves nothing either way.
    /// </summary>
    internal static void ReportSettled(EnemyObject copy, Vector3 spawnedAt)
    {
        if (_settledReported)
        {
            return;
        }

        _settledReported = true;
        try
        {
            Vector3 now = copy.transform.position;
            float moved = (now - spawnedAt).magnitude;
            SpawnRuntime.Log?.LogInfo(
                $"[copy] 2s later: grounded={copy.ActorHandler.IsGrounded}, "
                + $"moved={moved:0.###} from ({spawnedAt.x:0.##},{spawnedAt.y:0.##}) "
                + $"to ({now.x:0.##},{now.y:0.##}).");
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogInfo($"[copy] 2s later: could not read ({exception.Message}).");
        }
    }

    private static bool _initialisationReported;
    private static bool _placementReported;
    private static bool _movementReported;
    private static bool _settledReported;

    /// <summary>Lets the next area report initialisation again, for diagnosis across areas.</summary>
    internal static void ResetReporting()
    {
        _initialisationReported = false;
        _placementReported = false;
        _movementReported = false;
        _settledReported = false;
    }
}
