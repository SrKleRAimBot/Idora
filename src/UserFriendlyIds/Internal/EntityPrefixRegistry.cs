using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace UserFriendlyIds.Internal;

/// <summary>
/// The frozen entity-type to prefix lookup consulted on every call to
/// <see cref="IUserFriendlyIdGenerator.Generate{TEntity}"/>.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="FrozenDictionary{TKey,TValue}"/> spends extra time at construction choosing a
/// layout that makes subsequent reads as fast as possible. That is exactly the trade this package
/// wants: the registry is built once during start-up and then read for the lifetime of the process.
/// </para>
/// <para>
/// The obvious alternative &#8212; caching the prefix in a static field of a generic type, so the
/// lookup disappears entirely &#8212; was rejected. Such a cache is per-process, not per-container,
/// so a second container configured with different prefixes (or a test suite running cases in
/// parallel) would read another container's registration.
/// </para>
/// <para>
/// Prefixes are stored twice: once as written, for callers reading the registry, and once with the
/// separator already appended, so generating an identifier is a single copy rather than a copy plus
/// a separate character write.
/// </para>
/// </remarks>
internal sealed class EntityPrefixRegistry : IEntityPrefixRegistry
{
    private readonly FrozenDictionary<Type, string> _prefixes;
    private readonly FrozenDictionary<Type, string> _prefixesWithSeparator;

    private EntityPrefixRegistry(
        FrozenDictionary<Type, string> prefixes,
        FrozenDictionary<Type, string> prefixesWithSeparator)
    {
        _prefixes = prefixes;
        _prefixesWithSeparator = prefixesWithSeparator;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Type, string> Registrations => _prefixes;

    /// <summary>Builds a registry from the registrations collected during configuration.</summary>
    /// <param name="registrations">Entity types and the prefix each was given.</param>
    internal static EntityPrefixRegistry Create(IReadOnlyDictionary<Type, string> registrations)
    {
        FrozenDictionary<Type, string> prefixes = registrations.ToFrozenDictionary();

        FrozenDictionary<Type, string> prefixesWithSeparator = registrations.ToFrozenDictionary(
            static registration => registration.Key,
            static registration => registration.Value + EntityPrefixValidator.Separator);

        return new EntityPrefixRegistry(prefixes, prefixesWithSeparator);
    }

    /// <inheritdoc />
    public string GetPrefix<TEntity>() =>
        _prefixes.TryGetValue(typeof(TEntity), out string? prefix)
            ? prefix
            : throw EntityPrefixNotRegisteredException.For(typeof(TEntity));

    /// <inheritdoc />
    public string GetPrefix(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return _prefixes.TryGetValue(entityType, out string? prefix)
            ? prefix
            : throw EntityPrefixNotRegisteredException.For(entityType);
    }

    /// <inheritdoc />
    public bool TryGetPrefix<TEntity>([NotNullWhen(true)] out string? prefix) =>
        _prefixes.TryGetValue(typeof(TEntity), out prefix);

    /// <inheritdoc />
    public bool TryGetPrefix(Type entityType, [NotNullWhen(true)] out string? prefix)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return _prefixes.TryGetValue(entityType, out prefix);
    }

    /// <summary>
    /// Returns the registered prefix with the separator already appended, ready to be copied
    /// straight into a new identifier.
    /// </summary>
    /// <param name="entityType">The entity type to look up.</param>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <paramref name="entityType"/> has no registered prefix.
    /// </exception>
    internal string GetPrefixWithSeparator(Type entityType) =>
        _prefixesWithSeparator.TryGetValue(entityType, out string? prefix)
            ? prefix
            : throw EntityPrefixNotRegisteredException.For(entityType);
}
