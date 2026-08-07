namespace UserFriendlyIds;

/// <summary>
/// Generates prefixed, time-ordered identifiers such as <c>usr_01k2f9v8ta7m3x0qbrn4dzeh5c</c>.
/// </summary>
/// <remarks>
/// <para>
/// Resolve this from the container wherever you create an entity. Implementations are registered as
/// singletons, hold no mutable shared state, and are safe to call concurrently from any number of
/// threads.
/// </para>
/// <para>
/// Every identifier is <c>&lt;prefix&gt;_&lt;value&gt;</c>. The prefix says what the identifier
/// belongs to, so a value in a log line or a support ticket is self-describing. The value is a ULID
/// or a UUIDv7 &#8212; both begin with a millisecond timestamp, so identifiers sort into creation
/// order as plain strings, and rows inserted with them land at the end of an index rather than
/// scattering through it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderService(IUserFriendlyIdGenerator ids)
/// {
///     public Order Create() => new() { Id = ids.Generate&lt;Order&gt;() }; // "ord_01k2f9v8ta7m3x0qbrn4dzeh5c"
/// }
/// </code>
/// </example>
public interface IUserFriendlyIdGenerator
{
    /// <summary>
    /// Generates an identifier for a registered entity type, using the prefix that type was
    /// registered with.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type. It must have been registered with
    /// <see cref="UserFriendlyIdOptions.RegisterEntity{TEntity}(string)"/> during start-up.
    /// </typeparam>
    /// <returns>
    /// A new identifier, for example <c>usr_01k2f9v8ta7m3x0qbrn4dzeh5c</c>. Never
    /// <see langword="null"/> or empty.
    /// </returns>
    /// <exception cref="EntityPrefixNotRegisteredException">
    /// <typeparamref name="TEntity"/> has no registered prefix. Register it during start-up, or use
    /// <see cref="Generate(string)"/> if the prefix is only known at run time.
    /// </exception>
    /// <remarks>
    /// Prefer this overload. The prefix lives in one place &#8212; your start-up configuration
    /// &#8212; instead of being repeated as a string literal at every call site, and a type you
    /// forgot to register fails loudly rather than quietly producing a wrong prefix.
    /// </remarks>
    /// <example>
    /// <code>
    /// string id = generator.Generate&lt;User&gt;();   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
    /// </code>
    /// </example>
    string Generate<TEntity>();

    /// <summary>
    /// Generates an identifier using a prefix supplied at the call site, for entity types that were
    /// not registered.
    /// </summary>
    /// <param name="entityPrefix">
    /// The prefix to use, 1 to 16 ASCII letters or digits, without the trailing underscore.
    /// </param>
    /// <returns>
    /// A new identifier beginning with <paramref name="entityPrefix"/> and an underscore.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityPrefix"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="entityPrefix"/> is empty, longer than 16 characters, or contains anything
    /// other than an ASCII letter or digit.
    /// </exception>
    /// <remarks>
    /// Useful when the entity is not known statically &#8212; a plugin's own types, or a prefix read
    /// from configuration. For types you know at compile time, use
    /// <see cref="Generate{TEntity}"/> instead so the prefix is validated once at start-up rather
    /// than on every call.
    /// </remarks>
    /// <example>
    /// <code>
    /// string id = generator.Generate("tmp");    // "tmp_01k2f9v8ta7m3x0qbrn4dzeh5c"
    /// </code>
    /// </example>
    string Generate(string entityPrefix);
}
