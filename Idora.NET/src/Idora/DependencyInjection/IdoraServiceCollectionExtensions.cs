using Microsoft.Extensions.DependencyInjection.Extensions;
using Idora;
using Idora.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers identifier generation with a dependency injection container.
/// </summary>
/// <remarks>
/// This lives in the <c>Microsoft.Extensions.DependencyInjection</c> namespace, the convention for
/// registration extensions, so it is available in <c>Program.cs</c> without an extra
/// <c>using</c> directive.
/// </remarks>
public static class IdoraServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IIdoraGenerator"/> with the default configuration: ULID values
    /// and no registered entity types.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// With no entity types registered, only <see cref="IIdoraGenerator.Generate(string)"/>
    /// is usable; <see cref="IIdoraGenerator.Generate{TEntity}"/> will throw. Use the
    /// overload that takes a configuration delegate to register prefixes.
    /// </remarks>
    public static IServiceCollection AddIdora(this IServiceCollection services) =>
        services.AddIdora(static _ => { });

    /// <summary>
    /// Registers <see cref="IIdoraGenerator"/> and the entity prefixes it should use.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">
    /// Configures the value format and registers entity prefixes. Runs immediately, not lazily.
    /// </param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">A registered prefix is not valid.</exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// A prefix was registered for two different entity types, or an entity type was registered
    /// twice.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="IdoraOptions.Strategy"/> was set to a value that is not defined.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <paramref name="configure"/> is invoked here rather than deferred, and the resulting
    /// registrations are frozen into an immutable lookup straight away. A duplicate or malformed
    /// prefix therefore fails the process at start-up, where it is obvious, instead of surfacing on
    /// the first request that happens to create that kind of entity.
    /// </para>
    /// <para>
    /// Both <see cref="IIdoraGenerator"/> and <see cref="IEntityPrefixRegistry"/> are
    /// registered as singletons, using <c>TryAdd</c> so that an earlier registration of your own
    /// wins.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddIdora(options =>
    /// {
    ///     options.RegisterEntity&lt;User&gt;("usr");
    ///     options.RegisterEntity&lt;Order&gt;("ord");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddIdora(
        this IServiceCollection services,
        Action<IdoraOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        IdoraOptions options = new();
        configure(options);

        EntityPrefixRegistry registry = EntityPrefixRegistry.Create(options.Registrations);
        IIdValueWriter writer = IdValueWriterFactory.Create(options.Strategy);

        services.TryAddSingleton<IEntityPrefixRegistry>(registry);
        services.TryAddSingleton<IIdoraGenerator>(new IdoraGenerator(registry, writer));

        return services;
    }
}
