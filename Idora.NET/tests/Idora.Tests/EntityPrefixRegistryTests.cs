using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers the prefix registry that consumers resolve when they need the prefix itself rather than a
/// whole identifier.
/// </summary>
public sealed class EntityPrefixRegistryTests
{
    [Fact]
    public void GetPrefix_ForARegisteredType_ReturnsThePrefixWithoutTheSeparator()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        string prefix = registry.GetPrefix<User>();

        // Assert
        prefix.ShouldBe("usr");
    }

    [Fact]
    public void GetPrefix_ByRuntimeType_ReturnsTheSameValueAsTheGenericOverload()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        string generic = registry.GetPrefix<Order>();
        string byType = registry.GetPrefix(typeof(Order));

        // Assert
        byType.ShouldBe(generic);
    }

    [Fact]
    public void GetPrefix_ForAnUnregisteredType_Throws()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        EntityPrefixNotRegisteredException exception =
            Should.Throw<EntityPrefixNotRegisteredException>(() => registry.GetPrefix<UnregisteredEntity>());

        // Assert
        exception.EntityType.ShouldBe(typeof(UnregisteredEntity));
    }

    [Fact]
    public void GetPrefix_WithANullType_ThrowsArgumentNullException()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => registry.GetPrefix(null!));

        // Assert
        exception.ParamName.ShouldBe("entityType");
    }

    [Fact]
    public void TryGetPrefix_ForARegisteredType_ReturnsTrueAndThePrefix()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        bool found = registry.TryGetPrefix<User>(out string? prefix);

        // Assert
        found.ShouldBeTrue();
        prefix.ShouldBe("usr");
    }

    [Fact]
    public void TryGetPrefix_ForAnUnregisteredType_ReturnsFalseAndNull()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        bool found = registry.TryGetPrefix<UnregisteredEntity>(out string? prefix);

        // Assert
        found.ShouldBeFalse();
        prefix.ShouldBeNull();
    }

    [Fact]
    public void TryGetPrefix_ByRuntimeType_BehavesLikeTheGenericOverload()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        bool found = registry.TryGetPrefix(typeof(Order), out string? prefix);
        bool missing = registry.TryGetPrefix(typeof(UnregisteredEntity), out string? missingPrefix);

        // Assert
        found.ShouldBeTrue();
        prefix.ShouldBe("ord");
        missing.ShouldBeFalse();
        missingPrefix.ShouldBeNull();
    }

    [Fact]
    public void Registrations_ExposesEveryRegisteredTypeAndPrefix()
    {
        // Arrange
        IEntityPrefixRegistry registry = CreateRegistry();

        // Act
        IReadOnlyDictionary<Type, string> registrations = registry.Registrations;

        // Assert
        registrations.Count.ShouldBe(2);
        registrations[typeof(User)].ShouldBe("usr");
        registrations[typeof(Order)].ShouldBe("ord");
    }

    [Fact]
    public void Registrations_WhenNothingWasRegistered_IsEmpty()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IEntityPrefixRegistry registry = provider.GetRequiredService<IEntityPrefixRegistry>();

        // Assert
        registry.Registrations.ShouldBeEmpty();
    }

    [Fact]
    public void Registrations_MatchTheIdentifiersTheGeneratorActuallyProduces()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options =>
        {
            options.RegisterEntity<User>("usr");
            options.RegisterEntity<Order>("ord");
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        IEntityPrefixRegistry registry = provider.GetRequiredService<IEntityPrefixRegistry>();
        IIdoraGenerator generator = provider.GetRequiredService<IIdoraGenerator>();

        // Act
        string id = generator.Generate<Order>();

        // Assert: this is the round trip a consumer relies on when checking that an identifier
        // belongs to the entity a route expects.
        id.ShouldStartWith(registry.GetPrefix<Order>() + "_");
    }

    private static IEntityPrefixRegistry CreateRegistry()
    {
        ServiceCollection services = new();
        services.AddIdora(options =>
        {
            options.RegisterEntity<User>("usr");
            options.RegisterEntity<Order>("ord");
        });

        return services.BuildServiceProvider().GetRequiredService<IEntityPrefixRegistry>();
    }
}
