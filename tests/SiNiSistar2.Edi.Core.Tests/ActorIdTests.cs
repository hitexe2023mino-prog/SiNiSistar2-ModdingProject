namespace SiNiSistar2.Edi.Core.Tests;

/// <summary>
/// How a hold names the enemy that is holding the player (SPEC001 6.2.1, FR-058, FR-059).
///
/// The rules exist because the trigger key is what a funscript is filed under. An actor id that
/// stands for more than one enemy plays the wrong waveform; an actor id that never gets written
/// plays nothing and leaves no record that anything happened.
/// </summary>
public sealed class ActorIdTests
{
    /// <summary>
    /// AC-056: <c>None</c> is the game's word for "gallery entry not set". Six unrelated holds in a
    /// real trigger catalogue had collapsed onto it.
    /// </summary>
    [Theory]
    [InlineData("None", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("GaID_Mimic", true)]
    [InlineData("EnmID_MeatWorm1", true)]
    public void UnsetNamesNoActor(string? id, bool usable) =>
        Assert.Equal(usable, ActorIds.IsUsable(id));

    [Theory]
    [InlineData("ParasiteTentacle", "obj:ParasiteTentacle")]
    [InlineData("EnemyMeatWorm1 (2)", "obj:EnemyMeatWorm1")]
    [InlineData("EnemyMeatWombBaby(Clone)(Clone)", "obj:EnemyMeatWombBaby")]
    [InlineData("StoneEye (3) (Clone)", "obj:StoneEye")]
    public void UnitySuffixesAreNotPartOfTheActor(string objectName, string expected) =>
        Assert.Equal(expected, ActorIds.FromObjectName(objectName));

    /// <summary>
    /// A structural name is as bad as `None`: hold colliders in this build hang off objects called
    /// "Root" under every character, so an actor built from one would merge unrelated binders.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(Clone)")]
    [InlineData("Root")]
    [InlineData("Root (2)")]
    [InlineData("Base")]
    public void ANameThatSaysNothingYieldsNoActor(string? objectName) =>
        Assert.Null(ActorIds.FromObjectName(objectName));

    /// <summary>The binder's class is the fallback identity when its object name is structural.</summary>
    [Fact]
    public void TheComponentTypeNamesABinderWhoseObjectDoesNot()
    {
        Assert.Null(ActorIds.FromObjectName("Root"));
        Assert.Equal("obj:ParasiteTentacle", ActorIds.FromTypeName("ParasiteTentacle"));
        Assert.Null(ActorIds.FromTypeName("  "));
    }

    /// <summary>
    /// FR-059: an unnameable binder still produces a trigger. The value has to be distinct from both
    /// "not set" and a real identifier, or the distinction it exists to make is lost.
    /// </summary>
    [Fact]
    public void TheUnidentifiedBinderIsItsOwnActor()
    {
        Assert.NotEqual(ActorIds.Unset, ActorIds.UnidentifiedBinder);
        Assert.True(ActorIds.IsUsable(ActorIds.UnidentifiedBinder));
        Assert.DoesNotContain(ActorIds.ObjectPrefix, ActorIds.UnidentifiedBinder, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-060: sharing a key means sharing a waveform. The receptacle for unnameable binders is
    /// therefore recorded but never authorable, unlike every other actor id.
    /// </summary>
    [Fact]
    public void AnUnidentifiedActorCanNeverCarryAScript()
    {
        var unidentified = new EventKey("hold", ActorIds.UnidentifiedBinder, "Idle_Broken", "loop", "Idle_Broken");
        var named = new EventKey("hold", "EnmID_MeatTentacleCeiling", "Idle_Broken", "loop", "Idle_Broken");

        Assert.True(unidentified.IsUnidentifiedActor);
        Assert.False(unidentified.IsAuthorable);

        Assert.False(named.IsUnidentifiedActor);
        Assert.True(named.IsAuthorable);
    }

    /// <summary>
    /// FR-060: the authoring side refuses to write such an entry, so one can only reach the file by
    /// hand. Resolution refuses it there too, because the harm is in the playback, not the file.
    /// </summary>
    [Fact]
    public void AHandWrittenMappingForAnUnidentifiedActorIsNotHonoured()
    {
        MappingRepository mappings = TestMappings.Create(
            Mapped("hand-written", ActorIds.UnidentifiedBinder),
            Mapped("named", "EnmID_MeatTentacleCeiling"));

        Assert.False(mappings.TryResolve(Key(ActorIds.UnidentifiedBinder), out _));

        // The identical entry under a real actor resolves, so the refusal above is the guard doing
        // its work rather than the harness failing to load either entry.
        Assert.True(mappings.TryResolve(Key("EnmID_MeatTentacleCeiling"), out _));

        static EventKey Key(string actorId) => new("hold", actorId, "Idle_Broken", "loop", "Idle_Broken");

        static EventMapping Mapped(string id, string actorId) => new()
        {
            Id = id,
            Context = "hold",
            ActorId = actorId,
            AnimationId = "Idle_Broken",
            Phase = "loop",
            StageId = "Idle_Broken",
            Disposition = "mapped",
            Outputs = new List<OutputAssignment>
            {
                new() { Id = TestMappings.Main, Gallery = "some-gallery" },
            },
            SeekMode = "zero",
        };
    }

    /// <summary>Two binders named differently must not share a trigger key.</summary>
    [Fact]
    public void DifferentBindersGetDifferentKeys()
    {
        var worm = new EventKey("hold", "EnmID_MeatWorm1", "Idle_Broken", "loop", "Idle_Broken");
        var tentacle = new EventKey(
            "hold",
            ActorIds.FromObjectName("ParasiteTentacle")!,
            "Idle_Broken",
            "loop",
            "Idle_Broken");

        Assert.NotEqual(worm, tentacle);
        Assert.Equal("hold/EnmID_MeatWorm1/Idle_Broken/loop/Idle_Broken", worm.ToString());
        Assert.Equal("hold/obj:ParasiteTentacle/Idle_Broken/loop/Idle_Broken", tentacle.ToString());
    }
}
