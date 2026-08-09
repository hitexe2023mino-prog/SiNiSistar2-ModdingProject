using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using SiNiSistar2.Enemy.Character;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.Spawn.Core;
using UnityEngine;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// Detects area entry via the game's own setup gates (SPEC004 5.1, the pattern proven by the
/// difficulty MOD), applies the per-visit interventions, and drives stagnation measurement and
/// the pending mimic-miss queue every frame.
/// </summary>
public sealed class SpawnObserver : MonoBehaviour
{
    private readonly SpawnerTuningLedger _ledger = new();
    private readonly AdditionalSpawner _additional = new();
    private readonly GimmickCloner _gimmicks = new();
    private readonly List<EnemySpawner> _spawners = new();
    private readonly List<SimpleSpawnArea> _simpleAreas = new();
    private readonly Dictionary<int, int> _visits = new();

    private readonly SpawnHud _hud = new();

    private StagnationDetector? _stagnation;
    private AreaSettings? _settings;
    private SpawnBudget _budget = new(0, 0, 0, 0);
    private SceneID _scene = SceneID.None;
    private bool _sceneActive;
    private bool _wasEnabled;
    private bool _faultLogged;
    private double _nextAliveCheck;

    // HUD-only state, refreshed on entry and on a re-roll (SPEC004 5.8).
    private int _visitOfCurrentArea;
    private bool _hardBaseUsed;
    private bool _tuningApplied;
    private bool _mimicPoolPresent;
    private string _exclusionSource = "";
    private float _lastCountMultiplier = 1f;
    private float _lastIntervalMultiplier = 1f;
    private float _lastCoolMultiplier = 1f;
    private float _lastMaxSpawnMultiplier = 1f;
    private int _offscreenCandidates;
    private int _behindCandidates;
    private double _nextCandidateScan;
    private StagnationPause _paused;
    private int _sceneEnemyCount;

    public SpawnObserver(IntPtr pointer)
        : base(pointer)
    {
    }

    public void Update()
    {
        try
        {
            Poll();
            _faultLogged = false;
        }
        catch (Exception exception)
        {
            if (!_faultLogged)
            {
                _faultLogged = true;
                SpawnRuntime.Log?.LogWarning($"Spawn observation failed and will retry: {exception}");
            }
        }
    }

    public void OnApplicationQuit() => _sceneActive = false;

    /// <summary>Sets the HUD's starting stage from the config (SPEC004 6章 `HudMode`).</summary>
    [HideFromIl2Cpp]
    internal void InitialiseHud(HudMode mode) => _hud.Mode = mode;

    [HideFromIl2Cpp]
    private void Poll()
    {
        if (!SpawnRuntime.Enabled)
        {
            // FR-316: mid-session disable rolls the current scene back once, then stays quiet.
            if (_wasEnabled)
            {
                Rollback();
                SpawnRuntime.Log?.LogInfo("Spawn MOD disabled; interventions rolled back.");
            }

            _wasEnabled = false;
            return;
        }

        _wasEnabled = true;

        if (ManagerList.IsForbiddenManagerAccessState
            || !ManagerList.HasCompletedFirstInitialize
            || ManagerList.Instance is null
            || !ManagerList.HasDoneSceneSetUp)
        {
            if (_sceneActive)
            {
                // The scene is going away and takes every tuned object with it; forget, don't write.
                ForgetScene();
            }

            return;
        }

        SceneID sceneId = ManagerList.Map?.CurrentSceneID ?? SceneID.None;
        if (sceneId == SceneID.None)
        {
            return;
        }

        if (!_sceneActive || sceneId != _scene)
        {
            EnterArea(sceneId);
        }
        else
        {
            PerFrame();
        }
    }

    [HideFromIl2Cpp]
    private void EnterArea(SceneID sceneId)
    {
        _scene = sceneId;
        _sceneActive = true;
        SpawnRuntime.ResetVisitState();
        _additional.ResetForVisit();
        SceneEnemyCatalog.ResetReporting();
        _ledger.Clear();
        _gimmicks.ForgetSceneObjects();

        SpawnProfile profile = SpawnRuntime.Profile;
        int visit = _visits.TryGetValue((int)sceneId, out int count) ? count + 1 : 1;
        _visits[(int)sceneId] = visit;

        SpawnRuntime.Random = profile.Seed != 0
            ? SeededRandomSource.ForVisit(profile.Seed, (int)sceneId, visit)
            : new SystemRandomSource();

        string sceneName = sceneId.ToString();
        bool excludedByDefault = DefaultExclusions.IsExcluded(sceneName);
        _settings = profile.Resolve(sceneName, excludedByDefault);
        _visitOfCurrentArea = visit;
        _exclusionSource = !_settings.Excluded
            ? ""
            : excludedByDefault && !profile.AreaOverrides.ContainsKey(sceneName) ? "default" : "areas.json";
        _budget = new SpawnBudget(
            _settings.AdditionalSpawnCapPerVisit,
            _settings.AdditionalAliveCap,
            profile.GimmickClonesPerVisit,
            profile.MimicBoxesPerVisit);
        _stagnation = new StagnationDetector(
            _settings.StagnationSeconds,
            profile.StagnationWindowSeconds,
            profile.StagnationMoveEpsilon,
            _settings.StagnationPenaltyInterval);
        _nextAliveCheck = 0;

        CollectSceneSpawners();

        if (profile.DiagnosticsEnabled)
        {
            SpawnDiagnostics.DumpArea(sceneName, (int)sceneId, _spawners, _simpleAreas, _settings.Excluded);
        }

        if (_settings.Excluded)
        {
            SpawnRuntime.LogIntervention($"area '{sceneName}' visit {visit}: excluded, no intervention.");
            return;
        }

        ApplyVisitInterventions(sceneName, visit, profile);
    }

    /// <summary>
    /// The per-visit work, kept separate so the debug re-roll command (SPEC004 5.9) runs exactly
    /// the same path rather than an imitation of it (FR-332).
    /// </summary>
    [HideFromIl2Cpp]
    private void ApplyVisitInterventions(string sceneName, int visit, SpawnProfile profile)
    {
        bool isHard = SafeIsHardMode();
        _hardBaseUsed = isHard;
        _tuningApplied = _settings!.TuningHasEffect;

        if (_tuningApplied)
        {
            foreach (EnemySpawner spawner in _spawners)
            {
                try
                {
                    _ledger.Tune(spawner, _settings, SpawnRuntime.Random, isHard);
                }
                catch (Exception exception)
                {
                    SpawnRuntime.Log?.LogWarning(
                        $"Spawner tuning failed and was skipped for one spawner: {exception.Message}");
                }
            }

            foreach (SimpleSpawnArea area in _simpleAreas)
            {
                try
                {
                    _ledger.Tune(area, _settings, SpawnRuntime.Random, isHard);
                }
                catch (Exception exception)
                {
                    SpawnRuntime.Log?.LogWarning(
                        $"Simple spawn area tuning failed and was skipped: {exception.Message}");
                }
            }
        }

        _mimicPoolPresent = MimicBoxPlacement.HasMimicPool(_spawners, _simpleAreas);
        if (profile.MimicBoxEnabled)
        {
            MimicBoxPlacement.PlaceAll(_spawners, _simpleAreas, _budget, SpawnRuntime.Random);
        }

        _gimmicks.CloneForVisit(profile, _budget, SpawnRuntime.Random);

        _lastCountMultiplier = _ledger.MeanSpawnCountMultiplier;
        _lastIntervalMultiplier = _ledger.MeanSpawnIntervalMultiplier;
        _lastCoolMultiplier = _ledger.MeanCoolTimeMultiplier;
        _lastMaxSpawnMultiplier = _ledger.MeanMaxSpawnMultiplier;

        SpawnRuntime.LogIntervention(
            $"area '{sceneName}' visit {visit}: spawners={_spawners.Count}, "
            + $"simpleAreas={_simpleAreas.Count}, sceneEnemies={_sceneEnemyCount}, "
            + $"tuned={_ledger.TunedCount}, hardBase={isHard}, "
            + $"seedMode={(profile.Seed != 0 ? "seeded" : "system")}.");
    }

    [HideFromIl2Cpp]
    private void PerFrame()
    {
        MimicBoxPlacement.ProcessPendingMisses();

        if (SpawnRuntime.PendingCopyCheck is { } check && Time.timeAsDouble >= check.Due)
        {
            SpawnRuntime.PendingCopyCheck = null;
            SceneEnemyCatalog.ReportSettled(check.Enemy, check.SpawnedAt);
        }

        if (_settings is null || _settings.Excluded || _stagnation is null)
        {
            return;
        }

        Lelia? lelia = ManagerList.Object?.Lelia;
        if (lelia is null)
        {
            return;
        }

        // FR-311: holds, cinematics and game-over teardown pause measurement and block spawns.
        _paused = lelia.IsHold
            ? StagnationPause.Held
            : ManagerList.Object?.IsCinematicEvent == true ? StagnationPause.Cinematic : StagnationPause.None;
        bool paused = _paused != StagnationPause.None;

        double now = Time.timeAsDouble;
        if (now >= _nextAliveCheck)
        {
            _nextAliveCheck = now + 1.0;
            _additional.ReportAlive(_budget);
        }

        // The HUD's candidate counts run the full point filter, so they are refreshed on their own
        // slower cadence rather than every frame (SPEC004 10.1).
        if (_hud.Mode == HudMode.Full && now >= _nextCandidateScan)
        {
            _nextCandidateScan = now + 0.5;
            (_offscreenCandidates, _behindCandidates) = _additional.CountCandidates(_spawners);
        }

        Vector3 position = lelia.Position;
        bool penaltyDue = _stagnation.Sample(now, position.x, position.y, paused);
        if (penaltyDue && !paused)
        {
            _additional.ReportAlive(_budget);
            _additional.TrySpawnPenalty(_spawners, _settings, _budget, SpawnRuntime.Random);
        }
    }

    /// <summary>
    /// The HUD and the debug panel (SPEC004 5.8, 5.9). Unity calls this several times per frame
    /// with different event types, which is exactly what IMGUI key handling needs.
    /// </summary>
    public void OnGUI()
    {
        if (!SpawnRuntime.Enabled)
        {
            return;
        }

        _hud.Snapshot = BuildSnapshot();

        bool panelWasOpen = _hud.DebugPanelOpen;
        _hud.OnGUI(SpawnRuntime.HudHotkey, SpawnRuntime.DebugPanelHotkey, SpawnRuntime.Profile.DebugCommandsEnabled);

        if (!panelWasOpen && _hud.DebugPanelOpen)
        {
            // Opening the panel is the moment someone has just edited the file, so this is where
            // the re-read belongs (SpawnRuntime.ReloadConfig).
            SpawnRuntime.ReloadConfig?.Invoke();
        }

        char command = _hud.TakePendingCommand();
        if (command != '\0')
        {
            RunDebugCommand(command);
        }
    }

    [HideFromIl2Cpp]
    private HudSnapshot BuildSnapshot()
    {
        SpawnProfile profile = SpawnRuntime.Profile;

        if (!_sceneActive || _settings is null)
        {
            return new HudSnapshot
            {
                AreaName = _sceneActive ? _scene.ToString() : "loading",
                DebugCommandsEnabled = profile.DebugCommandsEnabled,
                MimicEnabled = profile.MimicBoxEnabled,
            };
        }

        var unresolved = 0;
        foreach (MimicBoxEntry entry in SpawnRuntime.MimicBoxes.Values)
        {
            if (entry.State == MimicBoxState.Unresolved)
            {
                unresolved++;
            }
        }

        return new HudSnapshot
        {
            AreaName = _scene.ToString(),
            VisitCount = _visitOfCurrentArea,
            Excluded = _settings.Excluded,
            ExclusionSource = _exclusionSource,
            Seeded = profile.Seed != 0,
            Seed = profile.Seed,
            TuningApplied = _tuningApplied,
            TunedSpawnerCount = _ledger.TunedCount,
            SpawnerCount = _spawners.Count + _simpleAreas.Count,
            SceneEnemyCount = _sceneEnemyCount,
            HardBase = _hardBaseUsed,
            SpawnCountMultiplier = _lastCountMultiplier,
            SpawnIntervalMultiplier = _lastIntervalMultiplier,
            CoolTimeMultiplier = _lastCoolMultiplier,
            MaxSpawnMultiplier = _lastMaxSpawnMultiplier,
            Dwell = _stagnation?.Dwell ?? 0,
            StagnationSeconds = _settings.StagnationSeconds,
            WindowTravel = _stagnation?.WindowTravel ?? 0f,
            MoveEpsilon = profile.StagnationMoveEpsilon,
            Stagnant = _stagnation?.IsStagnant ?? false,
            SecondsUntilNextPenalty = _stagnation?.SecondsUntilNextPenalty,
            Paused = _paused,
            Spawned = _budget.SpawnedThisVisit,
            SpawnCap = _budget.SpawnCapPerVisit,
            Alive = _budget.AliveAdditional,
            AliveCap = _budget.AliveCap,
            LastSpawnNote = _additional.LastNote,
            OffscreenCandidates = _offscreenCandidates,
            BehindCandidates = _behindCandidates,
            MimicEnabled = profile.MimicBoxEnabled,
            MimicPoolPresent = _mimicPoolPresent,
            MimicPlaced = _budget.MimicBoxesPlaced,
            MimicCap = _budget.MimicBoxCap,
            MimicUnresolved = unresolved,
            MimicHits = SpawnRuntime.MimicHits,
            MimicMisses = SpawnRuntime.MimicMisses,
            PinnedOutcome = SpawnRuntime.PinnedMimicOutcome switch
            {
                true => "MIMIC",
                false => "REWARD",
                null => null,
            },
            DebugCommandsEnabled = profile.DebugCommandsEnabled,
        };
    }

    /// <summary>
    /// Runs one debug command (SPEC004 5.9). Every command goes through the ordinary code path;
    /// caps, exclusions and the hold/cinematic block are enforced there, not bypassed here
    /// (FR-332). The outcome is always reported, including refusals.
    /// </summary>
    [HideFromIl2Cpp]
    private void RunDebugCommand(char command)
    {
        try
        {
            string outcome = Dispatch(command);
            SpawnRuntime.Log?.LogInfo($"[debug] '{command}': {outcome}");
        }
        catch (Exception exception)
        {
            SpawnRuntime.Log?.LogWarning($"[debug] '{command}' failed: {exception.Message}");
        }
    }

    [HideFromIl2Cpp]
    private string Dispatch(char command)
    {
        if (command == HudModel.ToggleKey)
        {
            if (SpawnRuntime.Profile.DebugCommandsEnabled)
            {
                return "debug commands are already ON";
            }

            SpawnRuntime.SetDebugCommands?.Invoke(true);
            return "debug commands are now ON";
        }

        if (command == HudModel.DisableCommand)
        {
            SpawnRuntime.SetDebugCommands?.Invoke(false);
            return "debug commands are now off";
        }

        if (!_sceneActive || _settings is null)
        {
            return "no active area";
        }

        if (_settings.Excluded && command is '1' or '2' or '3' or '6' or '8')
        {
            return $"this area is excluded ({_exclusionSource}); the command is refused";
        }

        if (_paused != StagnationPause.None && command is '1' or '2')
        {
            return "spawning is blocked while held or during an event";
        }

        switch (command)
        {
            case '1':
                _additional.ReportAlive(_budget);
                _additional.TrySpawnPenalty(_spawners, _settings, _budget, SpawnRuntime.Random, forceAmbush: false);
                return _additional.LastNote;

            case '2':
                _additional.ReportAlive(_budget);
                _additional.TrySpawnPenalty(_spawners, _settings, _budget, SpawnRuntime.Random, forceAmbush: true);
                return _additional.LastNote;

            case '3':
                return MimicBoxPlacement.PlaceOne(_spawners, _simpleAreas, _budget, SpawnRuntime.Random);

            case '4':
                SpawnRuntime.PinnedMimicOutcome = true;
                return "the next box lottery is pinned to MIMIC";

            case '5':
                SpawnRuntime.PinnedMimicOutcome = false;
                return "the next box lottery is pinned to REWARD";

            case '6':
                _ledger.RestoreAll();
                ApplyVisitInterventions(_scene.ToString(), _visitOfCurrentArea, SpawnRuntime.Profile);
                return "the area was re-rolled";

            case '7':
                SpawnDiagnostics.DumpArea(
                    _scene.ToString(), (int)_scene, _spawners, _simpleAreas, _settings.Excluded);
                return "the diagnostics JSON was written";

            case '8':
                _stagnation?.FastForwardToStagnation();
                return "stagnation was advanced to just before it fires";

            default:
                return "unknown command";
        }
    }

    [HideFromIl2Cpp]
    private void CollectSceneSpawners()
    {
        _spawners.Clear();
        _simpleAreas.Clear();

        // includeInactive is required, not optional: this game leaves its spawners disabled until
        // the player reaches them, so the default overload reported zero spawners in every real
        // area and every mechanism silently had nothing to work with.
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(
            Il2CppType.Of<EnemySpawner>(), includeInactive: true))
        {
            EnemySpawner? spawner = obj.TryCast<EnemySpawner>();
            if (spawner is not null)
            {
                _spawners.Add(spawner);
            }
        }

        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(
            Il2CppType.Of<SimpleSpawnArea>(), includeInactive: true))
        {
            SimpleSpawnArea? area = obj.TryCast<SimpleSpawnArea>();
            if (area is not null)
            {
                _simpleAreas.Add(area);
            }
        }

        // Counted separately: this build places ordinary enemies directly in the scene rather
        // than through spawners, so "spawners 0" alone does not say whether an area is empty.
        _sceneEnemyCount = 0;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(
            Il2CppType.Of<EnemyObject>(), includeInactive: true))
        {
            if (obj.TryCast<EnemyObject>() is not null)
            {
                _sceneEnemyCount++;
            }
        }
    }

    /// <summary>
    /// The game's own difficulty answer, which the difficulty MOD may already be patching to Hard;
    /// reading it (rather than the save) is what makes the multipliers stack on the resolved base
    /// (SPEC004 DEC-309, AC-309).
    /// </summary>
    [HideFromIl2Cpp]
    private static bool SafeIsHardMode()
    {
        try
        {
            return PlayerStatusManager.IsHardMode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Scene teardown: objects die with the scene, so records are dropped without writes.</summary>
    [HideFromIl2Cpp]
    private void ForgetScene()
    {
        _sceneActive = false;
        _settings = null;
        _ledger.Clear();
        _gimmicks.ForgetSceneObjects();
        _spawners.Clear();
        _simpleAreas.Clear();
        SpawnRuntime.ResetVisitState();
    }

    /// <summary>SPEC004 5.7: undo everything that can be undone while the scene still lives.</summary>
    [HideFromIl2Cpp]
    internal void Rollback()
    {
        MimicBoxPlacement.ProcessPendingMisses();

        foreach (MimicBoxEntry entry in SpawnRuntime.MimicBoxes.Values)
        {
            try
            {
                if (entry.State == MimicBoxState.Unresolved)
                {
                    entry.Enemy.gameObject.SetActive(false);
                }
            }
            catch (Exception)
            {
                // The body is already gone.
            }
        }

        _ledger.RestoreAll();
        _gimmicks.DestroyAll();
        _additional.DestroyClones();
        SpawnRuntime.ResetVisitState();
        _settings = null;
        _sceneActive = false;
    }
}
