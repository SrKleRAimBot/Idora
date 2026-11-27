namespace Idora;

/// <summary>
/// Generates identifiers as <see cref="IdoraId"/> values rather than strings.
/// </summary>
/// <remarks>
/// For code that holds identifiers as <see cref="IdoraId"/> throughout &#8212; an entity whose key
/// property is an <see cref="IdoraId"/>, say. The identifier is generated exactly as
/// <see cref="IIdoraGenerator.Generate{TEntity}"/> would generate it and then parsed, which costs a
/// few tens of nanoseconds and allocates nothing beyond the string itself. Parsing rather than
/// trusting the generator is deliberate: <see cref="IIdoraGenerator"/> is an interface, and an
/// implementation that produced something malformed should fail here, not wherever the value is next
/// read.
/// </remarks>
/// <example>
/// <code>
/// IdoraId id = ids.GenerateId&lt;User&gt;();
/// </code>
/// </example>
public static class IdoraGeneratorExtensions
{
    /// <summary>
    /// Generates an identifier for a registered entity type, as an <see cref="IdoraId"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type, registered during start-up.</typeparam>
    /// <param name="generator">The generator to use.</param>
    /// <returns>A new identifier carrying the prefix registered for <typeparamref name="TEntity"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> is <see langword="null"/>.</exception>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix.
    /// </exception>
    public static IdoraId GenerateId<TEntity>(this IIdoraGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        return IdoraId.Parse(generator.Generate<TEntity>());
    }

    /// <summary>
    /// Generates an identifier with a prefix supplied at the call site, as an <see cref="IdoraId"/>.
    /// </summary>
    /// <param name="generator">The generator to use.</param>
    /// <param name="entityPrefix">The prefix, 1 to 16 ASCII letters or digits.</param>
    /// <returns>A new identifier beginning with <paramref name="entityPrefix"/> and an underscore.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="generator"/> or <paramref name="entityPrefix"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="entityPrefix"/> is not a valid prefix.
    /// </exception>
    public static IdoraId GenerateId(this IIdoraGenerator generator, string entityPrefix)
    {
        ArgumentNullException.ThrowIfNull(generator);

        return IdoraId.Parse(generator.Generate(entityPrefix));
    }
}
