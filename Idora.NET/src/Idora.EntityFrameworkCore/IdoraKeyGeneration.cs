using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Idora.EntityFrameworkCore;

/// <summary>
/// What the value generators share: finding the application's generator and registry from the
/// context, and choosing the prefix for the entity being inserted.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is cached. EF Core caches a value generator per property and shares it between every
/// context instance of the same model, and those contexts can belong to different service
/// providers &#8212; two test hosts, say, configured with different prefixes. Resolving on each call
/// keeps each context on its own configuration, for the cost of two dictionary lookups.
/// </para>
/// </remarks>
internal static class IdoraKeyGeneration
{
    /// <summary>Generates an identifier for the entity <paramref name="entry"/> tracks.</summary>
    /// <param name="entry">The entity being added.</param>
    /// <exception cref="InvalidOperationException">
    /// The context cannot see an <see cref="IIdoraGenerator"/> or <see cref="IEntityPrefixRegistry"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// Neither the entity's type nor any type it derives from in the model has a registered prefix.
    /// </exception>
    internal static string Generate(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        IEntityType entityType = entry.Metadata;
        IIdoraGenerator generator = Resolve<IIdoraGenerator>(entry.Context, entityType);
        IEntityPrefixRegistry registry = Resolve<IEntityPrefixRegistry>(entry.Context, entityType);

        return generator.Generate(FindPrefix(registry, entityType));
    }

    /// <summary>
    /// Returns the prefix registered for the entity's own type or, failing that, the nearest base
    /// type in the model that has one.
    /// </summary>
    /// <remarks>
    /// In an inheritance hierarchy the key is declared once, on the root, and shared by every derived
    /// type, so registering the root is enough; a derived type registered in its own right gets its
    /// own prefix. This walks EF Core's hierarchy, not the CLR's, so it never reaches a base class the
    /// model does not map.
    /// </remarks>
    private static string FindPrefix(IEntityPrefixRegistry registry, IEntityType entityType)
    {
        for (IEntityType? type = entityType; type is not null; type = type.BaseType)
        {
            if (registry.TryGetPrefix(type.ClrType, out string? prefix))
            {
                return prefix;
            }
        }

        // Throws EntityPrefixNotRegisteredException, naming the type that was actually inserted.
        return registry.GetPrefix(entityType.ClrType);
    }

    /// <summary>
    /// Resolves an Idora service from the application's container, which EF Core holds on to when the
    /// context is registered with <c>AddDbContext</c>, and then from EF Core's own internal one.
    /// </summary>
    private static TService Resolve<TService>(DbContext context, IEntityType entityType)
        where TService : class
    {
        IServiceProvider internalServices = ((IInfrastructure<IServiceProvider>)context).Instance;

        IServiceProvider? applicationServices = internalServices.GetService<IDbContextOptions>()
            ?.FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider;

        return applicationServices?.GetService(typeof(TService)) as TService
            ?? internalServices.GetService(typeof(TService)) as TService
            ?? throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The Idora key generator for '{0}' on '{1}' could not find an {2}. Register Idora with "
                    + "services.AddIdora(...) and the context with services.AddDbContext<{1}>(...) in the "
                    + "same container, so that EF Core can reach the application's services. A context "
                    + "created by hand needs optionsBuilder.UseApplicationServiceProvider(serviceProvider), "
                    + "where serviceProvider is one that AddIdora has been called on.",
                    entityType.DisplayName(),
                    context.GetType().Name,
                    typeof(TService).Name));
    }

    private static T? GetService<T>(this IServiceProvider provider)
        where T : class =>
        provider.GetService(typeof(T)) as T;
}
