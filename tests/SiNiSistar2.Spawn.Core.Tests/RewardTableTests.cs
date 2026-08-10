using SiNiSistar2.Spawn.Core;
using Xunit;

namespace SiNiSistar2.Spawn.Core.Tests;

public class RewardTableTests
{
    [Fact]
    public void ParsesWellFormedEntries()
    {
        RewardTable table = RewardTable.Parse("PortionHP:1:3, PortionMP:2:1", out List<string> errors);

        Assert.Empty(errors);
        Assert.Equal(2, table.Entries.Count);
        Assert.Equal(new RewardEntry("PortionHP", 1, 3), table.Entries[0]);
    }

    [Theory]
    [InlineData("PortionHP")]
    [InlineData("PortionHP:1")]
    [InlineData("PortionHP:x:1")]
    [InlineData("PortionHP:1:x")]
    [InlineData(":1:1")]
    public void MalformedEntryIsReportedAndDropped(string entry)
    {
        RewardTable table = RewardTable.Parse(entry, out List<string> errors);

        Assert.Single(errors);
        Assert.True(table.IsEmpty);
    }

    [Theory]
    [InlineData("PortionHP:0:1")]
    [InlineData("PortionHP:1:0")]
    [InlineData("PortionHP:-1:2")]
    public void NonPositiveCountOrWeightIsRejected(string entry)
    {
        RewardTable.Parse(entry, out List<string> errors);
        Assert.Single(errors);
    }

    [Fact]
    public void DrawIsWeightedAndDeterministicUnderSeed()
    {
        RewardTable table = RewardTable.Parse("A:1:1,B:1:9", out _);
        var random = new SeededRandomSource(42);

        var b = 0;
        for (var i = 0; i < 1000; i++)
        {
            RewardEntry? drawn = table.Draw(random);
            Assert.NotNull(drawn);
            if (drawn!.Value.ItemName == "B")
            {
                b++;
            }
        }

        Assert.InRange(b, 800, 1000);
    }

    [Fact]
    public void DrawFromEmptyTableIsNull()
    {
        Assert.Null(RewardTable.Empty.Draw(new SeededRandomSource(1)));
    }

    [Fact]
    public void WithoutRemovesRejectedNames()
    {
        RewardTable table = RewardTable.Parse("A:1:1,B:1:1", out _).Without(new[] { "a" });

        Assert.Single(table.Entries);
        Assert.Equal("B", table.Entries[0].ItemName);
    }
}
