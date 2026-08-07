using UserFriendlyIds.Internal;

namespace UserFriendlyIds;

/// <summary>
/// Configures identifier generation: which value format to use, and which prefix belongs to which
/// entity type.
/// </summary>
/// <remarks>
/// <para>
/// You do not construct this yourself. An instance is handed to the delegate you pass to
/// <c>AddUserFriendlyIds</c>, and is read once during start-up to build an immutable lookup. Nothing
/// here is consulted again afterwards, so there is no per-call configuration cost.
/// </para>
/// <para>
/// Registrations are validated as they are made, which means a mistake surfaces on the line that
/// caused it, at application start-up, rather than as a puzzling identifier in production.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddUserFriendlyIds(options =>
/// {
///     options.RegisterEntity&lt;User&gt;("usr");
///     options.RegisterEntity&lt;Order&gt;("ord");
///     options.RegisterEntity&lt;Invoice&gt;("inv");
/// });
/// </code>
/// </example>
public sealed class UserFriendlyIdOptions
{
    private readonly Dictionary<Type, string> _prefixesByEntityType = [];

    /// <summary>
    /// The reverse lookup used to detect collisions. Case-insensitive, because two prefixes that
    /// differ only in capitalisation are a collision as far as a human reader is concerned.
    /// </summary>
    private readonly Dictionary<string, Type> _entityTypesByPrefix = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The value format that follows the prefix. Defaults to
    /// <see cref="IdGenerationStrategy.Ulid"/>.
    /// </summary>
    /// <remarks>
    /// ULID is the default because it carries the same 128 bits in 26 characters rather than 32,
    /// which is cheaper to store and to index. Set this to
    /// <see cref="IdGenerationStrategy.UuidV7"/>, or call <see cref="UseUuidV7"/>, if you need a
    /// value that parses as a real UUID.
    /// </remarks>
    public IdGenerationStrategy Strategy { get; set; } = IdGenerationStrategy.Ulid;

    /// <summary>
    /// The entity types registered so far, and the prefix each was given.
    /// </summary>
    internal IReadOnlyDictionary<Type, string> Registrations => _prefixesByEntityType;

    /// <summary>
    /// Uses a 26-character lowercase ULID as the value. This is already the default; call it when
    /// you would rather the choice were written down than implied.
    /// </summary>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public UserFriendlyIdOptions UseUlid()
    {
        Strategy = IdGenerationStrategy.Ulid;
        return this;
    }

    /// <summary>
    /// Uses a 32-character lowercase UUIDv7 as the value instead of the default ULID.
    /// </summary>
    /// <returns>The same options instance, so calls can be chained.</returns>
    /// <remarks>
    /// Worth doing when a database column, schema or downstream service insists on a real UUID.
    /// Otherwise the ULID default is six characters shorter for the same 128 bits.
    /// </remarks>
    public UserFriendlyIdOptions UseUuidV7()
    {
        Strategy = IdGenerationStrategy.UuidV7;
        return this;
    }

    /// <summary>
    /// Associates an entity type with the prefix its identifiers should carry.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type. Nothing is required of it &#8212; it is used purely as a key, so it can be
    /// an entity class, a DTO, or a marker type.
    /// </typeparam>
    /// <param name="prefix">
    /// The prefix, 1 to 16 ASCII letters or digits. Lowercase reads best, but the prefix is stored
    /// exactly as you write it.
    /// </param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="prefix"/> is empty, too long, or contains something other than an ASCII
    /// letter or digit.
    /// </exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// <paramref name="prefix"/> is already registered for a different entity type (ignoring case),
    /// or <typeparamref name="TEntity"/> has already been registered.
    /// </exception>
    /// <example>
    /// <code>
    /// options.RegisterEntity&lt;User&gt;("usr");   // Generate&lt;User&gt;() -> "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
    /// </code>
    /// </example>
    public UserFriendlyIdOptions RegisterEntity<TEntity>(string prefix) =>
        RegisterEntity(typeof(TEntity), prefix);

    /// <summary>
    /// Associates an entity type with the prefix its identifiers should carry, where the type is
    /// only known at run time.
    /// </summary>
    /// <param name="entityType">The entity type to register.</param>
    /// <param name="prefix">
    /// The prefix, 1 to 16 ASCII letters or digits. Lowercase reads best, but the prefix is stored
    /// exactly as you write it.
    /// </param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityType"/> or <paramref name="prefix"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="prefix"/> is empty, too long, or contains something other than an ASCII
    /// letter or digit.
    /// </exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// <paramref name="prefix"/> is already registered for a different entity type (ignoring case),
    /// or <paramref name="entityType"/> has already been registered.
    /// </exception>
    /// <remarks>
    /// Prefer the generic <see cref="RegisterEntity{TEntity}(string)"/> where the type is known at
    /// compile time; this overload exists for configuration driven by reflection.
    /// </remarks>
    public UserFriendlyIdOptions RegisterEntity(Type entityType, string prefix)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        EntityPrefixValidator.Validate(prefix, nameof(prefix));

        // Checked first because "you already registered this type" is the more useful message when
        // both problems are true.
        if (_prefixesByEntityType.TryGetValue(entityType, out string? existingPrefix))
        {
            throw DuplicateEntityPrefixException.ForEntityType(entityType, existingPrefix, prefix);
        }

        if (_entityTypesByPrefix.TryGetValue(prefix, out Type? conflictingEntityType))
        {
            throw DuplicateEntityPrefixException.ForPrefix(prefix, entityType, conflictingEntityType);
        }

        _prefixesByEntityType.Add(entityType, prefix);
        _entityTypesByPrefix.Add(prefix, entityType);
        return this;
    }
}
