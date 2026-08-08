namespace SiNiSistar2.Pleasure.Core.Tests;

/// <summary>
/// Whether an attack counts as sexual is the requirement that separates this MOD from "pleasure
/// rises whenever you are held". Getting it wrong on a predator is the failure the user named
/// explicitly (SPEC003 5.3).
/// </summary>
public sealed class SexualAttackClassifierTests
{
    private static SexualAttackClassifier Classifier(
        string[]? sexualEnemies = null,
        string[]? nonSexualEnemies = null,
        string[]? sexualSenders = null,
        string[]? nonSexualSenders = null) =>
        new(
            new[] { "Lustfull", "Semen", "Pregnant" },
            sexualEnemies ?? Array.Empty<string>(),
            nonSexualEnemies ?? Array.Empty<string>(),
            sexualSenders,
            nonSexualSenders);

    /// <summary>AC-205: an attack that inflicts a sexual status is a sexual attack.</summary>
    [Fact]
    public void AnAttackApplyingASexualStatusIsSexual()
    {
        Assert.Equal(AttackKind.Sexual, Classifier().Classify("42", null, new[] { "Lustfull" }));
    }

    /// <summary>AC-205: a predator that inflicts nothing sexual does not raise pleasure.</summary>
    [Fact]
    public void AnAttackApplyingNothingSexualIsNotSexual()
    {
        Assert.Equal(AttackKind.NonSexual, Classifier().Classify("42", null, new[] { "Poison", "Blinded" }));
        Assert.Equal(AttackKind.NonSexual, Classifier().Classify("42", null, Array.Empty<string>()));
    }

    /// <summary>AC-206: the non-sexual override beats the status test.</summary>
    [Fact]
    public void TheNonSexualOverrideBeatsTheStatusTest()
    {
        SexualAttackClassifier classifier = Classifier(nonSexualEnemies: new[] { "42" });

        Assert.Equal(AttackKind.NonSexual, classifier.Classify("42", null, new[] { "Lustfull" }));
    }

    /// <summary>The non-sexual override also beats the sexual override; it is the safe answer.</summary>
    [Fact]
    public void TheNonSexualOverrideBeatsTheSexualOverride()
    {
        SexualAttackClassifier classifier = Classifier(
            sexualEnemies: new[] { "42" },
            nonSexualEnemies: new[] { "42" });

        Assert.Equal(AttackKind.NonSexual, classifier.Classify("42", null, null));
    }

    [Fact]
    public void TheSexualOverrideCoversAnEnemyThatInflictsNothing()
    {
        SexualAttackClassifier classifier = Classifier(sexualEnemies: new[] { "7" });

        Assert.Equal(AttackKind.Sexual, classifier.Classify("7", null, Array.Empty<string>()));
    }

    /// <summary>
    /// AC-205 / DEC-204: an unidentified captor skips the overrides and falls through to the
    /// status test, and an unrecognised attack is non-sexual rather than guessed at.
    /// </summary>
    [Fact]
    public void AnUnidentifiedCaptorFallsThroughToTheStatusTest()
    {
        SexualAttackClassifier classifier = Classifier(nonSexualEnemies: new[] { "42" });

        Assert.Equal(AttackKind.Sexual, classifier.Classify(null, null, new[] { "Semen" }));
        Assert.Equal(AttackKind.NonSexual, classifier.Classify(null, null, new[] { "Poison" }));
        Assert.Equal(AttackKind.NonSexual, classifier.Classify(string.Empty, null, null));
    }

    /// <summary>
    /// The art gallery picture frame never binds the player, so no captor-based rule can reach it.
    /// Naming the sender is the only way such an attacker can be classified at all (SPEC003 5.3).
    /// </summary>
    [Fact]
    public void ASenderNameReachesAnAttackerThatNeverBinds()
    {
        SexualAttackClassifier classifier = Classifier(sexualSenders: new[] { "PictureFrame" });

        Assert.Equal(AttackKind.Sexual, classifier.Classify(null, "PictureFrame", Array.Empty<string>()));
    }

    /// <summary>Unity names carry suffixes, so an exact match would miss the same enemy.</summary>
    [Fact]
    public void SenderMatchingIsSubstringAndCaseInsensitive()
    {
        SexualAttackClassifier classifier = Classifier(sexualSenders: new[] { "pictureframe" });

        Assert.Equal(AttackKind.Sexual, classifier.Classify(null, "ArtGallery_PictureFrame(Clone)", null));
        Assert.Equal(AttackKind.NonSexual, classifier.Classify(null, "StoneEye", null));
    }

    /// <summary>Every non-sexual rule outranks every sexual one; refusing is the safe mistake.</summary>
    [Fact]
    public void ANonSexualSenderBeatsASexualCaptor()
    {
        SexualAttackClassifier classifier = Classifier(
            sexualEnemies: new[] { "42" },
            nonSexualSenders: new[] { "Teeth" });

        Assert.Equal(AttackKind.NonSexual, classifier.Classify("42", "GiantTeeth", new[] { "Lustfull" }));
    }

    /// <summary>The picture frame ships in the defaults, because it is the case that motivated this.</summary>
    [Fact]
    public void ThePictureFrameIsNamedInTheShippedDefaults()
    {
        Assert.Contains("PictureFrame", SexualAbnormalDefaults.SenderNames);
    }
}

/// <summary>
/// The sidecar carries the only state that survives a session. A version mismatch that overwrote
/// the file would destroy a save written by a newer MOD (SPEC003 5.9, FR-225).
/// </summary>
public sealed class SidecarDocumentTests
{
    [Fact]
    public void ADocumentSurvivesARoundTrip()
    {
        var original = new SidecarDocument
        {
            GameBuildId = "b869-a562",
            Corruption = 4.25f,
            ClimaxCount = 7,
        };

        SidecarParse parsed = SidecarDocument.Parse(original.Serialize());

        Assert.True(parsed.IsLoaded);
        Assert.Equal(4.25f, parsed.Document!.Corruption, 5);
        Assert.Equal(7, parsed.Document.ClimaxCount);
        Assert.Equal("b869-a562", parsed.Document.GameBuildId);
    }

    /// <summary>AC-220: a newer schema is neither read nor overwritten.</summary>
    [Fact]
    public void AnUnsupportedSchemaIsRefusedAndFlagged()
    {
        string json = new SidecarDocument { SchemaVersion = 99, Corruption = 3f }.Serialize();

        SidecarParse parsed = SidecarDocument.Parse(json);

        Assert.False(parsed.IsLoaded);
        Assert.True(parsed.UnsupportedSchema);
        Assert.Contains("99", parsed.Error!, StringComparison.Ordinal);
    }

    /// <summary>AC-219: a truncated or corrupt file yields defaults, not an exception.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"schemaVersion\":1,\"sensi")]
    [InlineData("not json at all")]
    public void ADamagedFileFailsWithoutThrowing(string json)
    {
        SidecarParse parsed = SidecarDocument.Parse(json);

        Assert.False(parsed.IsLoaded);
        Assert.False(parsed.UnsupportedSchema);
        Assert.NotNull(parsed.Error);
    }

    /// <summary>Hand-edited negatives cannot produce a limit that can never be reached.</summary>
    [Fact]
    public void NegativeValuesAreClampedOnRead()
    {
        string json = new SidecarDocument { Corruption = -3f, ClimaxCount = -9 }.Serialize();

        SidecarParse parsed = SidecarDocument.Parse(json);

        Assert.True(parsed.IsLoaded);
        Assert.Equal(0f, parsed.Document!.Corruption, 5);
        Assert.Equal(0, parsed.Document.ClimaxCount);
    }

    [Fact]
    public void TheSlotKeyCombinesBothIdentifiers()
    {
        Assert.Equal("slot2-Save02", SlotKey.Compose(2, "Save02.json"));
        Assert.Equal("slot0", SlotKey.Compose(0, null));
        Assert.Null(SlotKey.Compose(-1, null));
    }

    [Fact]
    public void TheSlotKeyStripsCharactersAFileNameCannotHold()
    {
        string? key = SlotKey.Compose(1, "bad:name*here.json");

        Assert.NotNull(key);
        Assert.DoesNotContain(':', key!);
        Assert.DoesNotContain('*', key!);
    }
}
