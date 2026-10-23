using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Idora.Tests.Fixtures;
using Idora.Tests.Support;
using Xunit;

namespace Idora.Tests;

/// <summary>
/// Covers registration with a real container: lifetimes, injection, and the promise that a
/// misconfiguration fails at start-up rather than on the first request.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddIdora_MakesTheGeneratorResolvable()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntity<User>("usr"));

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IIdoraGenerator generator = provider.GetRequiredService<IIdoraGenerator>();

        // Assert
        generator.Generate<User>().ShouldStartWith("usr_");
    }

    [Fact]
    public void AddIdora_RegistersTheGeneratorAsASingleton()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntity<User>("usr"));
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IIdoraGenerator first = provider.GetRequiredService<IIdoraGenerator>();
        IIdoraGenerator second = provider.GetRequiredService<IIdoraGenerator>();

        // Assert
        first.ShouldBeSameAs(second);
    }

    [Fact]
    public void AddIdora_ResolvesTheSameSingletonInsideAScope()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntity<User>("usr"));
        using ServiceProvider provider = services.BuildServiceProvider();
        IIdoraGenerator root = provider.GetRequiredService<IIdoraGenerator>();

        // Act
        using IServiceScope scope = provider.CreateScope();
        IIdoraGenerator scoped = scope.ServiceProvider.GetRequiredService<IIdoraGenerator>();

        // Assert
        scoped.ShouldBeSameAs(root);
    }

    [Fact]
    public void AddIdora_MakesThePrefixRegistryResolvable()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options =>
        {
            options.RegisterEntity<User>("usr");
            options.RegisterEntity<Order>("ord");
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IEntityPrefixRegistry registry = provider.GetRequiredService<IEntityPrefixRegistry>();

        // Assert
        registry.GetPrefix<User>().ShouldBe("usr");
        registry.Registrations.Count.ShouldBe(2);
    }

    [Fact]
    public void AddIdora_InjectsTheGeneratorIntoAConsumer()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntity<Order>("ord"));
        services.AddSingleton<OrderService>();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        OrderService service = provider.GetRequiredService<OrderService>();

        // Assert
        service.CreateOrderId().ShouldStartWith("ord_");
    }

    [Fact]
    public void AddIdora_WithNoConfiguration_DefaultsToUlidValues()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        string id = provider.GetRequiredService<IIdoraGenerator>().Generate("tmp");

        // Assert
        id.Length.ShouldBe("tmp_".Length + 26);
    }

    [Fact]
    public void AddIdora_WithoutChoosingAStrategy_DefaultsToUlidValues()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntity<User>("usr"));
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        string id = provider.GetRequiredService<IIdoraGenerator>().Generate<User>();

        // Assert
        id.Length.ShouldBe("usr_".Length + 26);
    }

    [Fact]
    public void AddIdora_WithUseUlid_ProducesTheSameShapeAsTheDefault()
    {
        // Arrange
        ServiceCollection explicitServices = new();
        explicitServices.AddIdora(options => options.UseUlid().RegisterEntity<User>("usr"));
        ServiceCollection defaultServices = new();
        defaultServices.AddIdora(options => options.RegisterEntity<User>("usr"));

        using ServiceProvider explicitProvider = explicitServices.BuildServiceProvider();
        using ServiceProvider defaultProvider = defaultServices.BuildServiceProvider();

        // Act
        string explicitId = explicitProvider.GetRequiredService<IIdoraGenerator>().Generate<User>();
        string defaultId = defaultProvider.GetRequiredService<IIdoraGenerator>().Generate<User>();

        // Assert
        explicitId.Length.ShouldBe(defaultId.Length);
    }

    [Fact]
    public void AddIdora_WithUseUuidV7_OverridesTheDefault()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.UseUuidV7().RegisterEntity<User>("usr"));
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        string id = provider.GetRequiredService<IIdoraGenerator>().Generate<User>();

        // Assert
        id.Length.ShouldBe("usr_".Length + 32);
    }

    [Fact]
    public void AddIdora_WithADuplicatePrefix_ThrowsWhileRegistering()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(() =>
            services.AddIdora(options =>
            {
                options.RegisterEntity<User>("acc");
                options.RegisterEntity<Account>("acc");
            }));

        // Assert: the failure happens here, at start-up, not on the first identifier generated.
        exception.Prefix.ShouldBe("acc");
        services.ShouldBeEmpty();
    }

    [Fact]
    public void AddIdora_WithAnInvalidPrefix_ThrowsWhileRegistering()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        Action act = () => services.AddIdora(options => options.RegisterEntity<User>("bad prefix"));

        // Assert
        act.ShouldThrow<InvalidEntityPrefixException>();
    }

    [Fact]
    public void AddIdora_WithAnUndefinedStrategy_ThrowsWhileRegistering()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        ArgumentOutOfRangeException exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddIdora(options => options.Strategy = (IdGenerationStrategy)99));

        // Assert
        exception.ParamName.ShouldBe("strategy");
    }

    [Fact]
    public void AddIdora_WithANullServiceCollection_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => services.AddIdora(_ => { }));

        // Assert
        exception.ParamName.ShouldBe("services");
    }

    [Fact]
    public void AddIdora_WithANullConfigureDelegate_ThrowsArgumentNullException()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        ArgumentNullException exception =
            Should.Throw<ArgumentNullException>(() => services.AddIdora(null!));

        // Assert
        exception.ParamName.ShouldBe("configure");
    }

    [Fact]
    public void AddIdora_CalledTwice_LeavesTheFirstRegistrationInPlace()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntity<User>("first"));

        // Act: TryAdd semantics mean a second call does not silently replace the first.
        services.AddIdora(options => options.RegisterEntity<User>("second"));
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IIdoraGenerator>().Generate<User>().ShouldStartWith("first_");
    }

    [Fact]
    public void AddIdora_WithAttributedEntities_MakesThemResolvable()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options => options.RegisterEntitiesFromAssemblyContaining<FixtureUser>());

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IIdoraGenerator generator = provider.GetRequiredService<IIdoraGenerator>();

        // Assert
        generator.Generate<FixtureUser>().ShouldStartWith("fxusr_");
    }

    [Fact]
    public void AddIdora_MixingAttributedAndFluentEntities_RegistersBoth()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIdora(options =>
        {
            options.RegisterEntitiesFromAssemblyContaining<FixtureUser>();
            options.RegisterEntity<User>("usr");
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IIdoraGenerator generator = provider.GetRequiredService<IIdoraGenerator>();

        // Assert
        generator.Generate<FixtureUser>().ShouldStartWith("fxusr_");
        generator.Generate<User>().ShouldStartWith("usr_");
    }

    [Fact]
    public void AddIdora_WhenAnAttributeClashesWithAFluentPrefix_ThrowsWhileRegistering()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        DuplicateEntityPrefixException exception = Should.Throw<DuplicateEntityPrefixException>(() =>
            services.AddIdora(options =>
            {
                options.RegisterEntity<User>("fxusr");
                options.RegisterEntitiesFromAssemblyContaining<FixtureUser>();
            }));

        // Assert: the same start-up guarantee applies whichever style declared the prefix.
        exception.Prefix.ShouldBe("fxusr");
        services.ShouldBeEmpty();
    }

    [Fact]
    public void AddIdora_ReturnsTheSameCollectionSoCallsCanChain()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        IServiceCollection returned = services.AddIdora(options => options.RegisterEntity<User>("usr"));

        // Assert
        returned.ShouldBeSameAs(services);
    }

    /// <summary>A consumer that takes the generator through its constructor, as an application would.</summary>
    private sealed class OrderService(IIdoraGenerator generator)
    {
        internal string CreateOrderId() => generator.Generate<Order>();
    }
}
