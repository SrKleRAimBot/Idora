using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers registering entity prefixes and the collision rules that keep a prefix meaningful.
/// </summary>
public sealed class EntityRegistrationTests
{
    [Fact]
    public void RegisterEntity_ForOneType_MakesTheGenericOverloadUseThatPrefix()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("usr"));

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.ShouldStartWith("usr_");
    }

    [Fact]
    public void RegisterEntity_ForSeveralTypes_KeepsEachPrefixWithItsOwnType()
    {
        // Arrange
        IdoraGenerator generator = new(options =>
        {
            options.RegisterEntity<User>("usr");
            options.RegisterEntity<Order>("ord");
            options.RegisterEntity<Invoice>("inv");
            options.RegisterEntity<Account>("acct");
        });

        // Act
        string[] ids =
        [
            generator.Generate<User>(),
            generator.Generate<Order>(),
            generator.Generate<Invoice>(),
            generator.Generate<Account>(),
        ];

        // Assert
        ids[0].ShouldStartWith("usr_");
        ids[1].ShouldStartWith("ord_");
        ids[2].ShouldStartWith("inv_");
        ids[3].ShouldStartWith("acct_");
    }

    [Fact]
    public void Generate_ForAnUnregisteredType_ThrowsAndNamesTheType()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("usr"));

        // Act
        EntityPrefixNotRegisteredException exception =
            Should.Throw<EntityPrefixNotRegisteredException>(() => generator.Generate<UnregisteredEntity>());

        // Assert
        exception.EntityType.ShouldBe(typeof(UnregisteredEntity));
        exception.Message.ShouldContain(nameof(UnregisteredEntity));
    }

    [Fact]
    public void Generate_WhenNothingIsRegisteredAtAll_ThrowsForTheGenericOverload()
    {
        // Arrange
        IdoraGenerator generator = new();

        // Act
        Exception exception = Should.Throw<EntityPrefixNotRegisteredException>(() => generator.Generate<User>());

        // Assert
        exception.ShouldBeAssignableTo<IdoraException>();
    }

    [Fact]
    public void RegisterEntity_WithAPrefixAlreadyGivenToAnotherType_ThrowsAndNamesBothTypes()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>("acc");

        // Act
        DuplicateEntityPrefixException exception =
            Should.Throw<DuplicateEntityPrefixException>(() => options.RegisterEntity<Account>("acc"));

        // Assert
        exception.Prefix.ShouldBe("acc");
        exception.EntityType.ShouldBe(typeof(Account));
        exception.ConflictingEntityType.ShouldBe(typeof(User));
        exception.Message.ShouldContain(nameof(User));
        exception.Message.ShouldContain(nameof(Account));
    }

    [Theory]
    [InlineData("usr", "USR")]
    [InlineData("USR", "usr")]
    [InlineData("Usr", "uSR")]
    public void RegisterEntity_WithAPrefixDifferingOnlyByCase_IsTreatedAsADuplicate(
        string firstPrefix,
        string secondPrefix)
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>(firstPrefix);

        // Act
        DuplicateEntityPrefixException exception =
            Should.Throw<DuplicateEntityPrefixException>(() => options.RegisterEntity<Account>(secondPrefix));

        // Assert
        exception.ConflictingEntityType.ShouldBe(typeof(User));
    }

    [Fact]
    public void RegisterEntity_ForTheSameTypeTwiceWithDifferentPrefixes_Throws()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>("usr");

        // Act
        DuplicateEntityPrefixException exception =
            Should.Throw<DuplicateEntityPrefixException>(() => options.RegisterEntity<User>("user"));

        // Assert
        exception.EntityType.ShouldBe(typeof(User));
        exception.ConflictingEntityType.ShouldBe(typeof(User));
        exception.Message.ShouldContain("usr");
        exception.Message.ShouldContain("user");
    }

    [Fact]
    public void RegisterEntity_ForTheSameTypeTwiceWithTheSamePrefix_Throws()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>("usr");

        // Act
        DuplicateEntityPrefixException exception =
            Should.Throw<DuplicateEntityPrefixException>(() => options.RegisterEntity<User>("usr"));

        // Assert: a repeated registration is a copy-paste slip worth reporting, not a no-op.
        exception.EntityType.ShouldBe(typeof(User));
    }

    [Fact]
    public void RegisterEntity_WithATypeKnownOnlyAtRunTime_BehavesLikeTheGenericOverload()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntity(typeof(Invoice), "inv");

        // Assert
        options.Registrations[typeof(Invoice)].ShouldBe("inv");
    }

    [Fact]
    public void RegisterEntity_WithANullType_ThrowsArgumentNullException()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => options.RegisterEntity(null!, "inv"));

        // Assert
        exception.ParamName.ShouldBe("entityType");
    }

    [Fact]
    public void RegisterEntity_ReturnsTheSameOptionsInstanceSoCallsCanChain()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        IdoraOptions returned = options
            .RegisterEntity<User>("usr")
            .RegisterEntity<Order>("ord")
            .UseUuidV7();

        // Assert
        returned.ShouldBeSameAs(options);
        returned.Registrations.Count.ShouldBe(2);
    }

    [Fact]
    public void RegisterEntity_AfterAFailedRegistration_LeavesTheEarlierRegistrationsIntact()
    {
        // Arrange
        IdoraOptions options = new();
        options.RegisterEntity<User>("usr");

        // Act
        Should.Throw<DuplicateEntityPrefixException>(() => options.RegisterEntity<Account>("usr"));
        options.RegisterEntity<Account>("acc");

        // Assert
        options.Registrations.Count.ShouldBe(2);
        options.Registrations[typeof(User)].ShouldBe("usr");
        options.Registrations[typeof(Account)].ShouldBe("acc");
    }

    [Fact]
    public void RegisterEntity_KeepsThePrefixExactlyAsWritten()
    {
        // Arrange
        IdoraOptions options = new();

        // Act
        options.RegisterEntity<User>("Usr");

        // Assert: collisions ignore case, but the prefix itself is never rewritten.
        options.Registrations[typeof(User)].ShouldBe("Usr");
    }

    [Fact]
    public void Generate_WithAnUppercasePrefix_EmitsItUnchanged()
    {
        // Arrange
        IdoraGenerator generator = new(options => options.RegisterEntity<User>("USR"));

        // Act
        string id = generator.Generate<User>();

        // Assert
        id.ShouldStartWith("USR_");
    }
}
