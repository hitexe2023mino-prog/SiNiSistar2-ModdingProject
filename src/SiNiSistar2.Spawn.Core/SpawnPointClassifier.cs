namespace SiNiSistar2.Spawn.Core;

/// <summary>Player facing as the classifier needs it; mirrors the game's Dir enum values.</summary>
public enum FacingDir
{
    None,
    Left,
    Right,
}

/// <summary>
/// Pure position classification for SPEC004 5.3: off-screen (viewport-space, with margin) and
/// behind-the-player (opposite the facing direction). The plugin does the world-to-viewport
/// projection; this class only judges the numbers so the judgement is unit-testable.
/// </summary>
public static class SpawnPointClassifier
{
    /// <summary>
    /// A point is off-screen when it lies outside the viewport rect grown by <paramref name="margin"/>
    /// on every side, or behind the camera plane (z &lt; 0). Growing the rect is the conservative
    /// direction: a point just past the edge still counts as visible (SPEC004 6章 OffscreenMargin).
    /// </summary>
    public static bool IsOffscreen(float viewportX, float viewportY, float viewportZ, float margin)
    {
        if (viewportZ < 0f)
        {
            return true;
        }

        return viewportX < -margin
            || viewportX > 1f + margin
            || viewportY < -margin
            || viewportY > 1f + margin;
    }

    /// <summary>
    /// A point is behind the player when it lies on the opposite side of the facing direction.
    /// An unknown facing never classifies as behind, so no ambush spawn happens on it (FR-310:
    /// 判別できない状態を奇襲の根拠にしない).
    /// </summary>
    public static bool IsBehind(float pointX, float playerX, FacingDir facing) => facing switch
    {
        FacingDir.Right => pointX < playerX,
        FacingDir.Left => pointX > playerX,
        _ => false,
    };
}
