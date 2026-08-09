using SiNiSistar2.Spawn.Core;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

public class SpawnPointClassifierTests
{
    [Theory]
    [InlineData(0.5f, 0.5f, 5f, 0.1f, false)] // center of screen
    [InlineData(1.05f, 0.5f, 5f, 0.1f, false)] // inside the margin band still counts visible
    [InlineData(1.2f, 0.5f, 5f, 0.1f, true)] // right of the grown rect
    [InlineData(-0.2f, 0.5f, 5f, 0.1f, true)] // left of the grown rect
    [InlineData(0.5f, 1.2f, 5f, 0.1f, true)] // above
    [InlineData(0.5f, -0.2f, 5f, 0.1f, true)] // below
    [InlineData(0.5f, 0.5f, -1f, 0.1f, true)] // behind the camera
    public void OffscreenJudgement(float x, float y, float z, float margin, bool expected)
    {
        Assert.Equal(expected, SpawnPointClassifier.IsOffscreen(x, y, z, margin));
    }

    [Theory]
    [InlineData(3f, 5f, FacingDir.Right, true)] // facing right, point on the left
    [InlineData(7f, 5f, FacingDir.Right, false)]
    [InlineData(7f, 5f, FacingDir.Left, true)] // facing left, point on the right
    [InlineData(3f, 5f, FacingDir.Left, false)]
    [InlineData(3f, 5f, FacingDir.None, false)] // unknown facing never counts as behind
    public void BehindJudgement(float pointX, float playerX, FacingDir facing, bool expected)
    {
        Assert.Equal(expected, SpawnPointClassifier.IsBehind(pointX, playerX, facing));
    }
}

public class SpawnScalingTests
{
    [Theory]
    [InlineData(2, 1.5f, 3)]
    [InlineData(2, 1.0f, 2)]
    [InlineData(1, 1.2f, 1)] // rounds to 1.2 -> 1, never below original
    [InlineData(0, 3f, 0)] // zero stays zero
    [InlineData(-1, 3f, -1)] // sentinel values pass through untouched
    public void CountsOnlyGoUp(int original, float multiplier, int expected)
    {
        Assert.Equal(expected, SpawnScaling.ScaleCount(original, multiplier));
    }

    [Theory]
    [InlineData(10f, 0.7f, 7f)]
    [InlineData(10f, 1.0f, 10f)]
    [InlineData(0f, 0.5f, 0f)]
    public void DelaysOnlyGoDown(float original, float multiplier, float expected)
    {
        Assert.Equal(expected, SpawnScaling.ScaleDelay(original, multiplier), precision: 3);
    }
}

public class SpawnBudgetTests
{
    [Fact]
    public void AdditionalSpawnsStopAtVisitCap()
    {
        var budget = new SpawnBudget(spawnCapPerVisit: 2, aliveCap: 10, gimmickCap: 0, mimicCap: 0);

        Assert.True(budget.CanSpawnAdditional);
        budget.CountAdditionalSpawn();
        budget.CountAdditionalSpawn();
        Assert.False(budget.CanSpawnAdditional);

        budget.Reset();
        Assert.True(budget.CanSpawnAdditional);
    }

    [Fact]
    public void AliveCapBlocksUntilDeathsAreReported()
    {
        var budget = new SpawnBudget(spawnCapPerVisit: 10, aliveCap: 1, gimmickCap: 0, mimicCap: 0);

        budget.CountAdditionalSpawn();
        Assert.False(budget.CanSpawnAdditional);

        budget.ReportAlive(0);
        Assert.True(budget.CanSpawnAdditional);
    }

    [Fact]
    public void GimmickAndMimicCountersAreIndependent()
    {
        var budget = new SpawnBudget(spawnCapPerVisit: 0, aliveCap: 0, gimmickCap: 1, mimicCap: 1);

        Assert.False(budget.CanSpawnAdditional);
        Assert.True(budget.CanCloneGimmick);
        budget.CountGimmickClone();
        Assert.False(budget.CanCloneGimmick);

        Assert.True(budget.CanPlaceMimicBox);
        budget.CountMimicBox();
        Assert.False(budget.CanPlaceMimicBox);
    }
}

public class RandomSourceTests
{
    [Fact]
    public void SeededVisitStreamsReproduce()
    {
        SeededRandomSource a = SeededRandomSource.ForVisit(123, 45, 2);
        SeededRandomSource b = SeededRandomSource.ForVisit(123, 45, 2);

        for (var i = 0; i < 32; i++)
        {
            Assert.Equal(a.NextFloat(), b.NextFloat());
        }
    }

    [Fact]
    public void DifferentVisitsDiverge()
    {
        SeededRandomSource a = SeededRandomSource.ForVisit(123, 45, 1);
        SeededRandomSource b = SeededRandomSource.ForVisit(123, 45, 2);

        var anyDifferent = false;
        for (var i = 0; i < 8; i++)
        {
            anyDifferent |= a.NextFloat() != b.NextFloat();
        }

        Assert.True(anyDifferent);
    }

    [Fact]
    public void MultiplierRangeSamplesInsideBounds()
    {
        var range = new MultiplierRange(1.0f, 1.5f);
        var random = new SeededRandomSource(7);
        for (var i = 0; i < 100; i++)
        {
            float sample = range.Sample(random);
            Assert.InRange(sample, 1.0f, 1.5f);
        }
    }
}
