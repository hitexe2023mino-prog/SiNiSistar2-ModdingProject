namespace SiNiSistar2.Edi.Core.Tests;

/// <summary>
/// The roster is the only definition of which outputs exist, so its own consistency and the
/// references into it are what the whole design rests on (SPEC001 6.1.1, FR-048).
/// </summary>
public sealed class MappingSchemaTests
{
    /// <summary>AC-046: a version 1 file is not read, and the message says how to move it forward.</summary>
    [Fact]
    public void SchemaVersionOneIsRefusedWithAPointerToTheMigration()
    {
        MappingValidationResult result = TestMappings.Parse(TestMappings.Document(schemaVersion: 1));

        Assert.False(result.IsValid);
        string error = Assert.Single(result.Errors);
        Assert.Contains("schemaVersion 1", error, StringComparison.Ordinal);
        Assert.Contains("12.4", error, StringComparison.Ordinal);
    }

    /// <summary>AC-054: an output with no default entry has no defined idle state.</summary>
    [Fact]
    public void EveryOutputNeedsADefaultFillerKeyEvenIfItIsNull()
    {
        MappingValidationResult result = TestMappings.Parse(TestMappings.Document(
            defaultFillers: new Dictionary<string, string?>
            {
                [TestMappings.Main] = "filler-main",
                [TestMappings.BreastLeft] = "filler-breast",
            }));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            x => x.Contains("breast-right", StringComparison.Ordinal)
                 && x.Contains("defaultFillers", StringComparison.Ordinal));

        // An explicit null is a legitimate answer: that output idles silent.
        Assert.True(TestMappings.Parse(TestMappings.Document(
            defaultFillers: new Dictionary<string, string?>
            {
                [TestMappings.Main] = "filler-main",
                [TestMappings.BreastLeft] = null,
                [TestMappings.BreastRight] = null,
            })).IsValid);
    }

    /// <summary>
    /// A gallery's target outputs are derived from the variants it carries, so two outputs may
    /// never claim one variant or one device (DEC-002, DEC-026).
    /// </summary>
    [Theory]
    [InlineData("ediVariant")]
    [InlineData("ediDeviceName")]
    [InlineData("id")]
    public void TheRosterRejectsTwoOutputsClaimingTheSameThing(string field)
    {
        List<OutputBinding> roster = TestMappings.Roster();
        roster[1] = new OutputBinding
        {
            Id = field == "id" ? roster[0].Id : roster[1].Id,
            DisplayName = roster[1].DisplayName,
            EdiDeviceName = field == "ediDeviceName" ? roster[0].EdiDeviceName : roster[1].EdiDeviceName,
            EdiVariant = field == "ediVariant" ? roster[0].EdiVariant : roster[1].EdiVariant,
        };

        MappingValidationResult result = TestMappings.Parse(TestMappings.Document(roster: roster));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AnEmptyRosterIsAConfigurationError()
    {
        MappingValidationResult result = TestMappings.Parse(
            TestMappings.Document(roster: Array.Empty<OutputBinding>()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("at least one device", StringComparison.Ordinal));
    }

    /// <summary>
    /// "Silence this output" is a value like any other: at equal priority it disagrees with a
    /// gallery name just as much as two names disagree with each other (FR-043).
    /// </summary>
    [Fact]
    public void SilencingAndPlayingAtTheSamePriorityIsAmbiguous()
    {
        var rules = TestMappings.DefaultStatusRules();
        rules.Add(new StatusRule
        {
            StatusId = "Milk",
            DisplayName = "Milk",
            Disposition = "mapped",
            Priority = 10,
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.BreastLeft, Gallery = null },
            },
        });

        MappingValidationResult result = TestMappings.Parse(TestMappings.Document(statusRules: rules));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("higher priority", StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-055: saving one side must not drop the side saved a moment ago. The merge is what makes
    /// authoring left and right separately safe (SPEC001 6.7-8).
    /// </summary>
    [Fact]
    public void MergingOutputsKeepsTheAssignmentsSavedEarlier()
    {
        EventMapping existing = TestMappings.Event(
            "one",
            "enemy",
            "clip",
            new[] { new OutputAssignment { Id = TestMappings.BreastLeft, Gallery = "g" } });
        MappingRepository mappings = TestMappings.Create(existing);

        List<OutputAssignment> merged = mappings.MergeOutputs(
            existing.Key,
            new[] { new OutputAssignment { Id = TestMappings.BreastRight, Gallery = "g" } });

        Assert.Equal(
            new[] { TestMappings.BreastLeft, TestMappings.BreastRight },
            merged.Select(x => x.Id));

        // Re-saving one output replaces just that assignment.
        List<OutputAssignment> replaced = mappings.MergeOutputs(
            existing.Key,
            new[] { new OutputAssignment { Id = TestMappings.BreastLeft, Gallery = null } });
        Assert.Null(replaced.Single(x => x.Id == TestMappings.BreastLeft).Gallery);
    }

    [Fact]
    public void VariantsAndOutputsResolveBothWays()
    {
        MappingRepository mappings = TestMappings.Create();

        Assert.Equal("ufo-right", mappings.VariantFor(TestMappings.BreastRight));
        Assert.Equal(TestMappings.BreastRight, mappings.OutputForVariant("ufo-right"));
        Assert.Null(mappings.VariantFor("nope"));
        Assert.Null(mappings.OutputForVariant("nope"));
    }
}
