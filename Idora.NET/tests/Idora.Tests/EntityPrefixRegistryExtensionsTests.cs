using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers the registry-aware parse and check: identifiers validated against the prefix registered
/// for an entity type, and unregistered types reported as a configuration problem rather than as a
/// bad identifier.
/// </summary>
public sealed class EntityPrefixRegistryExtensionsTests
{
    private const string UserId = "usr_01k226e0g0008j4ct4ank7f24s";
    private const string OrderId = "ord_01k226e0g1zzqdvk5vnacrgxv6";

    [Fact]
    public void IsIdFor_AnIdentifierWithTheRegisteredPrefix_IsTrue()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act & Assert
        registry.IsIdFor<User>(UserId).ShouldBeTrue();
        registry.IsIdFor(typeof(Order), OrderId).ShouldBeTrue();
    }

    [Theory]
    [InlineData(OrderId)]
    [InlineData("USR_01k226e0g0008j4ct4ank7f24s")]
    [InlineData("usr_nope")]
    [InlineData("")]
    [InlineData(null)]
    public void IsIdFor_AnythingElse_IsFalse(string? text)
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act & Assert
        registry.IsIdFor<User>(text).ShouldBeFalse();
    }

    [Fact]
    public void ParseIdFor_AnIdentifierWithTheRegisteredPrefix_ParsesIt()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        IdoraId id = registry.ParseIdFor<User>(UserId);
        IdoraId byType = registry.ParseIdFor(typeof(User), UserId);

        // Assert
        id.ToString().ShouldBe(UserId);
        byType.ShouldBe(id);
    }

    [Fact]
    public void ParseIdFor_AnotherEntitysIdentifier_ThrowsAMismatchNamingTheRegisteredPrefix()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        InvalidIdException exception = Should.Throw<InvalidIdException>(() => registry.ParseIdFor<User>(OrderId));

        // Assert
        exception.Reason.ShouldBe(InvalidIdReason.PrefixMismatch);
        exception.ExpectedPrefix.ShouldBe("usr");
    }

    [Fact]
    public void ParseIdFor_AMalformedIdentifier_ThrowsInvalidId()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act & Assert
        Should.Throw<InvalidIdException>(() => registry.ParseIdFor<User>("nope"))
            .Reason.ShouldBe(InvalidIdReason.NoSeparator);
    }

    [Fact]
    public void ParseIdFor_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => registry.ParseIdFor<User>(null!)).ParamName.ShouldBe("text");
    }

    [Fact]
    public void TryParseIdFor_ChecksThePrefix()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        bool matching = registry.TryParseIdFor<User>(UserId, out IdoraId user);
        bool mismatched = registry.TryParseIdFor<User>(OrderId, out IdoraId notUser);
        bool missing = registry.TryParseIdFor(typeof(User), null, out _);

        // Assert
        matching.ShouldBeTrue();
        user.ToString().ShouldBe(UserId);
        mismatched.ShouldBeFalse();
        notUser.IsEmpty.ShouldBeTrue();
        missing.ShouldBeFalse();
    }

    [Fact]
    public void EveryMethod_ForAnUnregisteredType_ThrowsNotRegisteredRatherThanInvalidId()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act & Assert: even for input that is itself perfectly valid, and from the non-throwing forms.
        Should.Throw<EntityPrefixNotRegisteredException>(() => registry.IsIdFor<UnregisteredEntity>(UserId))
            .EntityType.ShouldBe(typeof(UnregisteredEntity));
        Should.Throw<EntityPrefixNotRegisteredException>(() => registry.TryParseIdFor<UnregisteredEntity>(UserId, out _));
        Should.Throw<EntityPrefixNotRegisteredException>(() => registry.ParseIdFor<UnregisteredEntity>(UserId));
        Should.Throw<EntityPrefixNotRegisteredException>(() => registry.IsIdFor<UnregisteredEntity>(null));
    }

    [Fact]
    public void EveryMethod_WithANullRegistry_ThrowsArgumentNullException()
    {
        // Arrange
        IEntityPrefixRegistry registry = null!;

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => registry.IsIdFor<User>(UserId)).ParamName.ShouldBe("registry");
        Should.Throw<ArgumentNullException>(() => registry.ParseIdFor<User>(UserId)).ParamName.ShouldBe("registry");
        Should.Throw<ArgumentNullException>(() => registry.TryParseIdFor<User>(UserId, out _)).ParamName.ShouldBe("registry");
    }

    [Fact]
    public void EveryMethod_WithANullEntityType_ThrowsArgumentNullException()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => registry.IsIdFor(null!, UserId)).ParamName.ShouldBe("entityType");
        Should.Throw<ArgumentNullException>(() => registry.ParseIdFor(null!, UserId)).ParamName.ShouldBe("entityType");
        Should.Throw<ArgumentNullException>(() => registry.TryParseIdFor(null!, UserId, out _)).ParamName.ShouldBe("entityType");
    }

    [Fact]
    public void GenerateId_ForARegisteredEntity_ParsesWithItsPrefix()
    {
        // Arrange
        IIdoraGenerator generator = new IdoraGenerator(options => options.RegisterEntity<User>("usr"));

        // Act
        IdoraId id = generator.GenerateId<User>();

        // Assert
        id.Prefix.ShouldBe("usr");
        id.Format.ShouldBe(IdGenerationStrategy.Ulid);
    }

    [Fact]
    public void GenerateId_WithAnAdHocPrefix_ParsesWithThatPrefix()
    {
        // Arrange
        IIdoraGenerator generator = new IdoraGenerator(options => options.UseUuidV7());

        // Act
        IdoraId id = generator.GenerateId("tmp");

        // Assert
        id.Prefix.ShouldBe("tmp");
        id.Format.ShouldBe(IdGenerationStrategy.UuidV7);
    }

    [Fact]
    public void GenerateId_PassesTheGeneratorsExceptionsThrough()
    {
        // Arrange
        IIdoraGenerator generator = new IdoraGenerator();

        // Act & Assert
        Should.Throw<EntityPrefixNotRegisteredException>(() => generator.GenerateId<UnregisteredEntity>());
        Should.Throw<InvalidEntityPrefixException>(() => generator.GenerateId("us_r"));
        Should.Throw<ArgumentNullException>(() => ((IIdoraGenerator)null!).GenerateId<User>()).ParamName.ShouldBe("generator");
        Should.Throw<ArgumentNullException>(() => ((IIdoraGenerator)null!).GenerateId("tmp")).ParamName.ShouldBe("generator");
    }

    private static IEntityPrefixRegistry CreateRegistry() =>
        new ServiceCollection()
            .AddIdora(options =>
            {
                options.RegisterEntity<User>("usr");
                options.RegisterEntity<Order>("ord");
            })
            .BuildServiceProvider()
            .GetRequiredService<IEntityPrefixRegistry>();
}
