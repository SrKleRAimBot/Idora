using System.Reflection;
using Shouldly;
using Idora.Internal;
using Idora.Tests.Fixtures;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers registering prefixes with <see cref="EntityPrefixAttribute"/>: what an assembly scan
/// finds and what it leaves alone, that the two registration styles coexist without either being
/// able to claim a prefix twice, and that a failure describes itself the same way every run.
/// </summary>
/// <remarks>
/// Assembly-level behaviour is exercised against <c>Idora.Tests.Fixtures</c>, which holds
/// nothing but well-formed attributed types. Clashes and malformed prefixes go through the scanner's
/// type-list seam with types from <c>Support/AttributedTestEntities.cs</c> instead — putting those in
/// a scanned assembly would break every other test in this file.
/// </remarks>
public sealed class AttributeRegistrationTests
{
    private static readonly Assembly FixtureAssembly = typeof(FixtureUser).Assembly;

    /// <summary>The library itself, as an assembly that contains no attributed types.</summary>
    private static readonly Assembly UnattributedAssembly = typeof(IdoraOptions).Assembly;

    [Fact]
    public void RegisterEntitiesFromAssembly_RegistersEveryAttributedType()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert
        options.Registrations[typeof(FixtureUser)].ShouldBe("fxusr");
        options.Registrations[typeof(FixtureOrder)].ShouldBe("fxord");
        options.Registrations[typeof(FixtureBase)].ShouldBe("fxbase");
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_FindsAttributedStructs()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert
        options.Registrations[typeof(FixturePayment)].ShouldBe("fxpay");
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_FindsNonPublicTypes()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert: an internal entity class is an ordinary thing to want.
        options.Registrations[typeof(FixtureInternalEntity)].ShouldBe("fxint");
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_RegistersNothingElse()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert: the five attributed types and nothing more.
        options.Registrations.Count.ShouldBe(5);
        options.Registrations.ShouldNotContainKey(typeof(FixtureUnattributed));
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_DoesNotGiveADerivedTypeItsBasesPrefix()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert: an inherited attribute would make every subclass collide with its base.
        options.Registrations.ShouldNotContainKey(typeof(FixtureDerived));
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_WithAnAssemblyThatHasNoAttributedTypes_RegistersNothing()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(UnattributedAssembly);

        // Assert
        options.Registrations.ShouldBeEmpty();
    }

    [Fact]
    public void RegisterEntitiesFromAssemblyContaining_MatchesRegisterEntitiesFromAssembly()
    {
        // Arrange
        IdoraOptions byMarker = new();
        IdoraOptions byAssembly = new();

        // Act
        byMarker.RegisterEntitiesFromAssemblyContaining<FixtureUser>();
        byAssembly.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert
        byMarker.Registrations.ShouldBe(byAssembly.Registrations, ignoreOrder: true);
    }

    [Fact]
    public void RegisterEntitiesFromAssemblies_ScansEveryAssemblyGiven()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssemblies(UnattributedAssembly, FixtureAssembly);

        // Assert
        options.Registrations.Count.ShouldBe(5);
        options.Registrations[typeof(FixtureUser)].ShouldBe("fxusr");
    }

    [Fact]
    public void RegisterEntitiesFromAssemblies_WithNoAssemblies_RegistersNothing()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssemblies();

        // Assert
        options.Registrations.ShouldBeEmpty();
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_ReturnsTheSameOptionsSoCallsCanChain()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        IdoraOptions returned = options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert
        returned.ShouldBeSameAs(options);
    }

    [Fact]
    public void Generate_ForATypeRegisteredByAttribute_UsesTheDeclaredPrefix()
    {
        // Arrange
        IdoraGenerator generator =
            new(options => options.RegisterEntitiesFromAssemblyContaining<FixtureUser>());

        // Act
        string id = generator.Generate<FixtureUser>();

        // Assert: the whole point — no reflection was needed to get here.
        id.ShouldStartWith("fxusr_");
        id.Length.ShouldBe("fxusr_".Length + 26);
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_ThenRegisterEntity_KeepsBoth()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntitiesFromAssembly(FixtureAssembly);
        options.RegisterEntity<User>("usr");

        // Assert
        options.Registrations[typeof(FixtureUser)].ShouldBe("fxusr");
        options.Registrations[typeof(User)].ShouldBe("usr");
    }

    [Fact]
    public void RegisterEntity_ThenRegisterEntitiesFromAssembly_KeepsBoth()
    {
        // Arrange
        IdoraOptions options = new();

        // Act: the styles mix in either order.
        options.RegisterEntity<User>("usr");
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Assert
        options.Registrations[typeof(User)].ShouldBe("usr");
        options.Registrations[typeof(FixtureUser)].ShouldBe("fxusr");
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_WithAPrefixAlreadyTakenFluently_IsTreatedAsADuplicate()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>("fxusr");

        // Act
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(
            () => options.RegisterEntitiesFromAssembly(FixtureAssembly));

        // Assert
        exception.Prefix.ShouldBe("fxusr");
        exception.ConflictingEntityType.ShouldBe(typeof(User));
    }

    [Fact]
    public void RegisterEntity_WithAPrefixAlreadyTakenByAnAttribute_IsTreatedAsADuplicate()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Act
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(
            () => options.RegisterEntity<User>("fxusr"));

        // Assert
        exception.ConflictingEntityType.ShouldBe(typeof(FixtureUser));
    }

    [Fact]
    public void RegisterEntity_ForATypeAlreadyRegisteredByItsAttribute_ThrowsEvenWithTheSamePrefix()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Act: agreeing on the prefix does not make two sources of truth acceptable.
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(
            () => options.RegisterEntity<FixtureUser>("fxusr"));

        // Assert
        exception.EntityType.ShouldBe(typeof(FixtureUser));
        exception.ConflictingEntityType.ShouldBe(typeof(FixtureUser));
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_ForATypeAlreadyRegisteredFluently_ThrowsEvenWithTheSamePrefix()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<FixtureUser>("fxusr");

        // Act: and the same holds the other way round.
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(
            () => options.RegisterEntitiesFromAssembly(FixtureAssembly));

        // Assert
        exception.EntityType.ShouldBe(typeof(FixtureUser));
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_ScannedTwice_IsTreatedAsADuplicate()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Act
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(
            () => options.RegisterEntitiesFromAssembly(FixtureAssembly));

        // Assert: scanning twice is a repeat like any other, and the advice has to say so — neither
        // "remove the RegisterEntity call" nor "remove the attribute" is the fix here.
        exception.Message.ShouldContain("scanned twice");
        exception.Message.ShouldNotContain("cannot also be registered with");
    }

    [Fact]
    public void RegisterEntity_ForAnAttributedTypeWithADifferentPrefix_SaysWhichPrefixesDisagree()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntitiesFromAssembly(FixtureAssembly);

        // Act
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(
            () => options.RegisterEntity<FixtureUser>("other"));

        // Assert: declaring two different prefixes is a different mistake from declaring one twice.
        exception.Message.ShouldContain("fxusr");
        exception.Message.ShouldContain("other");
        exception.Message.ShouldContain("only one prefix");
    }

    [Fact]
    public void Scan_WithTwoAttributedTypesClaimingOnePrefix_IsTreatedAsADuplicate()
    {
        // Arrange
        IdoraOptions options = new();

        // Act: "acc" and "ACC" differ only by case, which is a collision to a human reader.
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(
                options,
                [typeof(AttributedAccount), typeof(AttributedAccrual)]));

        // Assert
        exception.EntityType.ShouldBe(typeof(AttributedAccrual));
        exception.ConflictingEntityType.ShouldBe(typeof(AttributedAccount));
    }

    [Fact]
    public void Scan_WithAClash_ReportsItTheSameWayWhateverOrderTheTypesArriveIn()
    {
        // Arrange
        IdoraOptions forwards = new();
        IdoraOptions backwards = new();

        // Act: Assembly.GetTypes makes no promise about ordering, so the scanner imposes one.
        DuplicateEntityPrefixException first = Should.Throw<DuplicateEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(
                forwards,
                [typeof(AttributedAccount), typeof(AttributedAccrual)]));

        DuplicateEntityPrefixException second = Should.Throw<DuplicateEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(
                backwards,
                [typeof(AttributedAccrual), typeof(AttributedAccount)]));

        // Assert: a start-up failure that describes itself differently each run is a bad one.
        second.EntityType.ShouldBe(first.EntityType);
        second.ConflictingEntityType.ShouldBe(first.ConflictingEntityType);
        second.Message.ShouldBe(first.Message);
    }

    [Fact]
    public void Scan_SkipsTypesWithoutTheAttribute()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        EntityPrefixAttributeScanner.Register(
            options,
            [typeof(AttributedNothing), typeof(AttributedCustomer)]);

        // Assert
        options.Registrations.Count.ShouldBe(1);
        options.Registrations[typeof(AttributedCustomer)].ShouldBe("cus");
    }

    [Fact]
    public void Scan_WithASeparatorInThePrefix_NamesTheOffendingType()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        InvalidEntityPrefixException exception = Should.Throw<InvalidEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(options, [typeof(AttributedWithSeparator)]));

        // Assert: an attribute leaves no stack trace pointing at the line that declared it.
        exception.Message.ShouldContain(typeof(AttributedWithSeparator).ToString());
        exception.Message.ShouldContain("[EntityPrefix]");
        exception.Prefix.ShouldBe("bad_prefix");
    }

    [Theory]
    [InlineData(typeof(AttributedWithEmptyPrefix))]
    [InlineData(typeof(AttributedWithOverlongPrefix))]
    [InlineData(typeof(AttributedWithNullPrefix))]
    public void Scan_WithAnInvalidPrefix_ThrowsAndNamesTheOffendingType(Type entityType)
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        InvalidEntityPrefixException exception = Should.Throw<InvalidEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(options, [entityType]));

        // Assert
        exception.Message.ShouldContain(entityType.ToString());
    }

    [Fact]
    public void Scan_WithANullPrefix_ReportsItRatherThanThrowingArgumentNullException()
    {
        // Arrange: nothing stops [EntityPrefix(null!)] compiling.
        IdoraOptions options = new();

        // Act
        InvalidEntityPrefixException exception = Should.Throw<InvalidEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(options, [typeof(AttributedWithNullPrefix)]));

        // Assert
        exception.Prefix.ShouldBeNull();
    }

    [Fact]
    public void Scan_WhenOneTypeIsRejected_LeavesEarlierRegistrationsIntact()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        Should.Throw<InvalidEntityPrefixException>(() =>
            EntityPrefixAttributeScanner.Register(
                options,
                [typeof(AttributedCustomer), typeof(AttributedWithSeparator)]));

        // Assert: "AttributedCustomer" sorts first, so it was already in when the scan failed.
        options.Registrations[typeof(AttributedCustomer)].ShouldBe("cus");
    }

    [Fact]
    public void EntityPrefixAttribute_KeepsThePrefixItWasGiven()
    {
        // Arrange
        EntityPrefixAttribute attribute = new("usr");

        // Act
        string prefix = attribute.Prefix;

        // Assert
        prefix.ShouldBe("usr");
    }

    [Fact]
    public void EntityPrefixAttribute_IsNotInheritedAndCannotBeAppliedTwice()
    {
        // Arrange
        AttributeUsageAttribute? usage = typeof(EntityPrefixAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        // Act & Assert
        usage.ShouldNotBeNull();
        usage.Inherited.ShouldBeFalse();
        usage.AllowMultiple.ShouldBeFalse();
    }

    [Fact]
    public void RegisterEntitiesFromAssembly_WithANullAssembly_ThrowsArgumentNullException()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(
            () => options.RegisterEntitiesFromAssembly(null!));

        // Assert
        exception.ParamName.ShouldBe("assembly");
    }

    [Fact]
    public void RegisterEntitiesFromAssemblies_WithANullArray_ThrowsArgumentNullException()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(
            () => options.RegisterEntitiesFromAssemblies(null!));

        // Assert
        exception.ParamName.ShouldBe("assemblies");
    }

    [Fact]
    public void RegisterEntitiesFromAssemblies_WithANullAssemblyInTheArray_ThrowsArgumentNullException()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(
            () => options.RegisterEntitiesFromAssemblies(FixtureAssembly, null!));

        // Assert
        exception.ParamName.ShouldBe("assemblies");
    }
}
