using System.Diagnostics.CodeAnalysis;

namespace UserFriendlyIds;

/// <summary>
/// The immutable set of entity-type to prefix mappings built during start-up.
/// </summary>
/// <remarks>
/// <para>
/// Resolve this from the container when you need the prefix itself rather than a whole identifier
/// &#8212; to validate that an incoming identifier belongs to the entity a route expects, say, or to
/// build a filter over stored identifiers.
/// </para>
/// <para>
/// The registry is fixed once the container is built and is safe to use from any thread.
/// </para>
/// </remarks>
public interface IEntityPrefixRegistry
{
    /// <summary>
    /// Every registered entity type and its prefix, in no particular order.
    /// </summary>
    IReadOnlyDictionary<Type, string> Registrations { get; }

    /// <summary>Returns the prefix registered for <typeparamref name="TEntity"/>.</summary>
    /// <typeparam name="TEntity">The entity type to look up.</typeparam>
    /// <returns>The registered prefix, without the trailing separator.</returns>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix.
    /// </exception>
    string GetPrefix<TEntity>();

    /// <summary>Returns the prefix registered for <paramref name="entityType"/>.</summary>
    /// <param name="entityType">The entity type to look up.</param>
    /// <returns>The registered prefix, without the trailing separator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is <see langword="null"/>.</exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <paramref name="entityType"/> has no registered prefix.
    /// </exception>
    string GetPrefix(Type entityType);

    /// <summary>Looks up the prefix registered for <typeparamref name="TEntity"/>.</summary>
    /// <typeparam name="TEntity">The entity type to look up.</typeparam>
    /// <param name="prefix">
    /// Receives the registered prefix, or <see langword="null"/> if the type is not registered.
    /// </param>
    /// <returns><see langword="true"/> if a prefix was found; otherwise <see langword="false"/>.</returns>
    bool TryGetPrefix<TEntity>([NotNullWhen(true)] out string? prefix);

    /// <summary>Looks up the prefix registered for <paramref name="entityType"/>.</summary>
    /// <param name="entityType">The entity type to look up.</param>
    /// <param name="prefix">
    /// Receives the registered prefix, or <see langword="null"/> if the type is not registered.
    /// </param>
    /// <returns><see langword="true"/> if a prefix was found; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is <see langword="null"/>.</exception>
    bool TryGetPrefix(Type entityType, [NotNullWhen(true)] out string? prefix);
}
