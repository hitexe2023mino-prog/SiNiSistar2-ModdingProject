using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

/// <summary>
/// SPEC004 5.3 出現位置: the places an area has held an enemy outlive the enemy that stood there,
/// so clearing an area does not leave the MOD with nowhere to spawn.
/// </summary>
public sealed class PositionMemoryTests
{
    [Fact]
    public void APlaceIsRememberedOnce()
    {
        var memory = new PositionMemory();

        Assert.True(memory.Remember(10f, 2f, 0f));
        Assert.False(memory.Remember(10f, 2f, 0f));
        Assert.Equal(1, memory.Count);
    }

    /// <summary>
    /// Enemies wander. Without the grid, one patrolling enemy sampled once a second would fill the
    /// memory with its own route and crowd out every other place in the area.
    /// </summary>
    [Fact]
    public void NearbyPlacesCollapseIntoOne()
    {
        var memory = new PositionMemory(grid: 1.5f);

        memory.Remember(10f, 2f, 0f);
        memory.Remember(10.9f, 2.4f, 0f);
        memory.Remember(11.6f, 2f, 0f);

        Assert.Equal(2, memory.Count);
    }

    /// <summary>Depth separates rendering layers, not standing room, so it is not compared.</summary>
    [Fact]
    public void DepthDoesNotMakeAPlaceNew()
    {
        var memory = new PositionMemory();

        memory.Remember(10f, 2f, 0f);

        Assert.False(memory.Remember(10f, 2f, 40f));
    }

    /// <summary>
    /// The first entries are the authored positions, recorded on entry before anything has moved.
    /// A full memory therefore refuses new places rather than evicting those.
    /// </summary>
    [Fact]
    public void TheAuthoredPlacesSurviveAFullMemory()
    {
        var memory = new PositionMemory(grid: 1f, limit: 3);

        memory.Remember(0f, 0f, 0f);
        memory.Remember(10f, 0f, 0f);
        memory.Remember(20f, 0f, 0f);

        Assert.True(memory.IsFull);
        Assert.False(memory.Remember(30f, 0f, 0f));
        Assert.Equal(3, memory.Count);
        Assert.Equal(0f, memory.Positions[0].X);
    }

    [Fact]
    public void AVisitStartsWithNothingRemembered()
    {
        var memory = new PositionMemory();
        memory.Remember(10f, 2f, 0f);

        memory.Clear();

        Assert.Equal(0, memory.Count);
    }
}
