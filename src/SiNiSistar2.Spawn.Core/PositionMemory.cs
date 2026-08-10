namespace SiNiSistar2.Spawn.Core;

/// <summary>
/// The places an area has had an enemy standing, remembered for the length of a visit
/// (SPEC004 5.3 出現位置, DEC-303).
///
/// Without this the only positions a spawn could use were the ones enemies occupied at that very
/// instant, so an area whose enemies were all defeated in view of the player offered nowhere to
/// put anything — the spawn was refused even though the area plainly supports enemies at those
/// spots. Remembering them keeps every position one the game itself put an enemy on, which is what
/// DEC-303 asks for, while removing the requirement that the enemy still be there.
/// </summary>
public sealed class PositionMemory
{
    private readonly List<(float X, float Y, float Z)> _positions = new();
    private readonly float _grid;
    private readonly int _limit;

    /// <param name="grid">
    /// Positions closer than this on both axes count as the same place. Enemies wander, so an
    /// unfiltered memory would fill with a hundred points along one patrol route.
    /// </param>
    /// <param name="limit">
    /// Upper bound on remembered places. Reaching it stops new entries rather than evicting old
    /// ones: the first entries are the area's authored positions, recorded on entry before
    /// anything has moved, and those are the ones worth keeping.
    /// </param>
    public PositionMemory(float grid = 1.5f, int limit = 64)
    {
        _grid = grid <= 0f ? 0.01f : grid;
        _limit = limit < 1 ? 1 : limit;
    }

    public IReadOnlyList<(float X, float Y, float Z)> Positions => _positions;

    public int Count => _positions.Count;

    public bool IsFull => _positions.Count >= _limit;

    /// <summary>Records a place, and says whether it was new.</summary>
    public bool Remember(float x, float y, float z)
    {
        foreach ((float rememberedX, float rememberedY, _) in _positions)
        {
            // Depth is not compared: this is a side-on game and z separates rendering layers
            // rather than standing room.
            if (Math.Abs(rememberedX - x) < _grid && Math.Abs(rememberedY - y) < _grid)
            {
                return false;
            }
        }

        if (IsFull)
        {
            return false;
        }

        _positions.Add((x, y, z));
        return true;
    }

    public void Clear() => _positions.Clear();
}
