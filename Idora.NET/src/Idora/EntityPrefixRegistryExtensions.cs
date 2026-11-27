using System.Diagnostics.CodeAnalysis;

namespace Idora;

/// <summary>
/// Parses and checks identifiers against the prefix registered for an entity type.
/// </summary>
/// <remarks>
/// <para>
/// These are the registry-aware forms of <see cref="IdoraId.Parse(string, string)"/>,
/// <see cref="IdoraId.TryParse(string, string, out IdoraId)"/> and
/// <see cref="IdoraId.IsValid(string, string)"/>. They look the prefix up instead of taking it as a
/// string literal, so the prefix still lives in exactly one place &#8212; your start-up
/// configuration &#8212; and checking that a route's identifier really is a user's reads as
/// <c>prefixes.IsIdFor&lt;User&gt;(id)</c>.
/// </para>
/// <para>
/// An entity type with no registered prefix throws <see cref="EntityPrefixNotRegisteredException"/>
/// from every method here, including <c>TryParseIdFor</c> and <c>IsIdFor</c>. The identifier is not
/// at fault in that case &#8212; the configuration is &#8212; so it is not reported as an invalid
/// identifier, or quietly as <see langword="false"/>.
/// </para>
/// <para>
/// They are extension methods rather than members of <see cref="IEntityPrefixRegistry"/> so that
/// adding them breaks no existing implementation of the interface; they work with any of them.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// app.MapGet("/users/{id}", (string id, IEntityPrefixRegistry prefixes) =>
///     prefixes.TryParseIdFor&lt;User&gt;(id, out IdoraId userId)
///         ? Results.Ok(userId)
///         : Results.NotFound());
/// </code>
/// </example>
public static class EntityPrefixRegistryExtensions
{
    /// <summary>
    /// Reports whether <paramref name="text"/> is a valid identifier for
    /// <typeparamref name="TEntity"/>, carrying exactly its registered prefix.
    /// </summary>
    /// <typeparam name="TEntity">The entity type the identifier should belong to.</typeparam>
    /// <param name="registry">The registry to look the prefix up in.</param>
    /// <param name="text">The text to check. <see langword="null"/> is simply not valid.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> parses and has the prefix registered for
    /// <typeparamref name="TEntity"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix.
    /// </exception>
    public static bool IsIdFor<TEntity>(this IEntityPrefixRegistry registry, [NotNullWhen(true)] string? text) =>
        IsIdFor(registry, typeof(TEntity), text);

    /// <summary>
    /// Reports whether <paramref name="text"/> is a valid identifier for
    /// <paramref name="entityType"/>, carrying exactly its registered prefix.
    /// </summary>
    /// <param name="registry">The registry to look the prefix up in.</param>
    /// <param name="entityType">The entity type the identifier should belong to.</param>
    /// <param name="text">The text to check. <see langword="null"/> is simply not valid.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> parses and has the prefix registered for
    /// <paramref name="entityType"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registry"/> or <paramref name="entityType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <paramref name="entityType"/> has no registered prefix.
    /// </exception>
    public static bool IsIdFor(
        this IEntityPrefixRegistry registry,
        Type entityType,
        [NotNullWhen(true)] string? text) =>
        IdoraId.IsValid(text, GetPrefix(registry, entityType));

    /// <summary>
    /// Parses an identifier that must belong to <typeparamref name="TEntity"/>, throwing if it is
    /// not valid or carries another prefix.
    /// </summary>
    /// <typeparam name="TEntity">The entity type the identifier should belong to.</typeparam>
    /// <param name="registry">The registry to look the prefix up in.</param>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registry"/> or <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix.
    /// </exception>
    /// <exception cref="InvalidIdException">
    /// <paramref name="text"/> is not a valid identifier, or its prefix is not the one registered
    /// for <typeparamref name="TEntity"/> (<see cref="InvalidIdReason.PrefixMismatch"/>, with
    /// <see cref="InvalidIdException.ExpectedPrefix"/> set to the registered prefix).
    /// </exception>
    public static IdoraId ParseIdFor<TEntity>(this IEntityPrefixRegistry registry, string text) =>
        ParseIdFor(registry, typeof(TEntity), text);

    /// <summary>
    /// Parses an identifier that must belong to <paramref name="entityType"/>, throwing if it is
    /// not valid or carries another prefix.
    /// </summary>
    /// <param name="registry">The registry to look the prefix up in.</param>
    /// <param name="entityType">The entity type the identifier should belong to.</param>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registry"/>, <paramref name="entityType"/> or <paramref name="text"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <paramref name="entityType"/> has no registered prefix.
    /// </exception>
    /// <exception cref="InvalidIdException">
    /// <paramref name="text"/> is not a valid identifier, or its prefix is not the one registered
    /// for <paramref name="entityType"/>.
    /// </exception>
    public static IdoraId ParseIdFor(this IEntityPrefixRegistry registry, Type entityType, string text)
    {
        string prefix = GetPrefix(registry, entityType);
        ArgumentNullException.ThrowIfNull(text);

        return IdoraId.Parse(text, prefix);
    }

    /// <summary>
    /// Parses an identifier that must belong to <typeparamref name="TEntity"/>, reporting failure
    /// rather than throwing.
    /// </summary>
    /// <typeparam name="TEntity">The entity type the identifier should belong to.</typeparam>
    /// <param name="registry">The registry to look the prefix up in.</param>
    /// <param name="text">The text to parse. <see langword="null"/> is simply not valid.</param>
    /// <param name="id">Receives the parsed identifier, or <c>default</c> if parsing failed.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> parses and has the prefix registered for
    /// <typeparamref name="TEntity"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix. This is thrown rather than reported
    /// as <see langword="false"/>, because it is a configuration mistake, not bad input.
    /// </exception>
    public static bool TryParseIdFor<TEntity>(
        this IEntityPrefixRegistry registry,
        [NotNullWhen(true)] string? text,
        out IdoraId id) =>
        TryParseIdFor(registry, typeof(TEntity), text, out id);

    /// <summary>
    /// Parses an identifier that must belong to <paramref name="entityType"/>, reporting failure
    /// rather than throwing.
    /// </summary>
    /// <param name="registry">The registry to look the prefix up in.</param>
    /// <param name="entityType">The entity type the identifier should belong to.</param>
    /// <param name="text">The text to parse. <see langword="null"/> is simply not valid.</param>
    /// <param name="id">Receives the parsed identifier, or <c>default</c> if parsing failed.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="text"/> parses and has the prefix registered for
    /// <paramref name="entityType"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registry"/> or <paramref name="entityType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <paramref name="entityType"/> has no registered prefix.
    /// </exception>
    public static bool TryParseIdFor(
        this IEntityPrefixRegistry registry,
        Type entityType,
        [NotNullWhen(true)] string? text,
        out IdoraId id) =>
        IdoraId.TryParse(text, GetPrefix(registry, entityType), out id);

    private static string GetPrefix(IEntityPrefixRegistry registry, Type entityType)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry.GetPrefix(entityType);
    }
}
