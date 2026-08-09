namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// Per-visit accounting for everything the MOD adds to an area (SPEC004 FR-308, 5.4-3, 5.5-2).
/// Reset on every area entry. The alive count is reported by the caller each frame because only
/// the plugin can see which spawned enemies still live.
/// </summary>
public sealed class SpawnBudget
{
    private readonly int _spawnCapPerVisit;
    private readonly int _aliveCap;
    private readonly int _gimmickCap;
    private readonly int _mimicCap;

    private int _spawned;
    private int _alive;
    private int _gimmicks;
    private int _mimics;

    public SpawnBudget(int spawnCapPerVisit, int aliveCap, int gimmickCap, int mimicCap)
    {
        _spawnCapPerVisit = spawnCapPerVisit;
        _aliveCap = aliveCap;
        _gimmickCap = gimmickCap;
        _mimicCap = mimicCap;
    }

    public int SpawnedThisVisit => _spawned;

    public int SpawnCapPerVisit => _spawnCapPerVisit;

    public int AliveAdditional => _alive;

    public int AliveCap => _aliveCap;

    public int MimicBoxesPlaced => _mimics;

    public int MimicBoxCap => _mimicCap;

    public int GimmickClones => _gimmicks;

    public void Reset()
    {
        _spawned = 0;
        _alive = 0;
        _gimmicks = 0;
        _mimics = 0;
    }

    public void ReportAlive(int aliveAdditionalEnemies) => _alive = aliveAdditionalEnemies;

    public bool CanSpawnAdditional => _spawned < _spawnCapPerVisit && _alive < _aliveCap;

    /// <summary>Consumes one additional-spawn slot; call only after the spawn actually happened.</summary>
    public void CountAdditionalSpawn()
    {
        _spawned++;
        _alive++;
    }

    public bool CanCloneGimmick => _gimmicks < _gimmickCap;

    public void CountGimmickClone() => _gimmicks++;

    public bool CanPlaceMimicBox => _mimics < _mimicCap;

    public void CountMimicBox() => _mimics++;
}
