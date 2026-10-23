using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Idora.Internal;

namespace Idora;

/// <summary>
/// Configures identifier generation: which value format to use, and which prefix belongs to which
/// entity type.
/// </summary>
/// <remarks>
/// <para>
/// You do not construct this yourself. An instance is handed to the delegate you pass to
/// <c>AddIdora</c>, and is read once during start-up to build an immutable lookup. Nothing
/// here is consulted again afterwards, so there is no per-call configuration cost.
/// </para>
/// <para>
/// Registrations are validated as they are made, which means a mistake surfaces on the line that
/// caused it, at application start-up, rather than as a puzzling identifier in production.
/// </para>
/// <para>
/// Prefixes can be declared two ways: fluently with <see cref="RegisterEntity{TEntity}(string)"/>,
/// or on the type itself with <see cref="EntityPrefixAttribute"/> and collected by
/// <see cref="RegisterEntitiesFromAssemblyContaining{TMarker}"/>. The two mix freely in one
/// configuration delegate, and a prefix claimed twice by either route is still a start-up failure.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddIdora(options =>
/// {
///     options.RegisterEntity&lt;User&gt;("usr");
///     options.RegisterEntity&lt;Order&gt;("ord");
///     options.RegisterEntity&lt;Invoice&gt;("inv");
/// });
/// </code>
/// </example>
public sealed class IdoraOptions
{
    /// <summary>
    /// Why the assembly-scanning methods are not trim-safe. Shared so all three say the same thing.
    /// </summary>
    private const string ScanningRequiresUnreferencedCode =
        "Entity types discovered by scanning an assembly cannot be seen by the trimmer and may be "
        + "removed. Register entities with RegisterEntity instead when publishing trimmed or "
        + "ahead-of-time compiled.";

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
    public IdoraOptions UseUlid()
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
    public IdoraOptions UseUuidV7()
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
    public IdoraOptions RegisterEntity<TEntity>(string prefix) =>
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
    public IdoraOptions RegisterEntity(Type entityType, string prefix)
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

    /// <summary>
    /// Registers every type in <paramref name="assembly"/> that carries
    /// <see cref="EntityPrefixAttribute"/>, using the prefix each attribute declares.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// An attribute declares a prefix that is empty, too long, or contains something other than an
    /// ASCII letter or digit. The message names the type carrying it.
    /// </exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// Two attributed types declare the same prefix (ignoring case), or an attributed type has also
    /// been registered with <see cref="RegisterEntity(Type, string)"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Scanning happens here, once, during start-up; the types it finds go into the same immutable
    /// lookup as a fluent registration. Nothing is reflected over afterwards, so
    /// <see cref="IIdoraGenerator.Generate{TEntity}"/> costs exactly what it did before.
    /// </para>
    /// <para>
    /// You name the assemblies rather than the package discovering them. Walking every loaded
    /// assembly would find only those the runtime happened to have loaded already &#8212; assemblies
    /// load lazily, so an entity assembly untouched at start-up would be skipped in silence and
    /// surface later as an <see cref="EntityPrefixNotRegisteredException"/> on whichever request
    /// first created that kind of entity. Naming the assemblies turns that into a deterministic
    /// start-up failure, and stops an unrelated package from registering prefixes into your
    /// application.
    /// </para>
    /// <para>
    /// Non-public and nested types are included; an <c>internal</c> entity is a perfectly ordinary
    /// thing to want. The attribute is not inherited, so a derived type does not pick up its base's
    /// prefix.
    /// </para>
    /// <para>
    /// The types this finds are invisible to the trimmer, hence the
    /// <see cref="RequiresUnreferencedCodeAttribute"/>. Register entities with
    /// <see cref="RegisterEntity{TEntity}(string)"/> if you publish trimmed or ahead-of-time
    /// compiled; that path stays fully trim- and AOT-safe.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddIdora(options =>
    ///     options.RegisterEntitiesFromAssembly(typeof(User).Assembly));
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(ScanningRequiresUnreferencedCode)]
    public IdoraOptions RegisterEntitiesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        EntityPrefixAttributeScanner.Register(this, assembly);
        return this;
    }

    /// <summary>
    /// Registers every type carrying <see cref="EntityPrefixAttribute"/> in each of
    /// <paramref name="assemblies"/>.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan, in the order they should be scanned.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="assemblies"/>, or any assembly in it, is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// An attribute declares a prefix that is not valid. The message names the type carrying it.
    /// </exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// Two attributed types declare the same prefix (ignoring case), including across two
    /// assemblies, or an attributed type has also been registered with
    /// <see cref="RegisterEntity(Type, string)"/>.
    /// </exception>
    /// <remarks>
    /// Collisions are detected across the whole set, not within each assembly, so two modules that
    /// each claim <c>usr</c> fail the process rather than one quietly winning. See
    /// <see cref="RegisterEntitiesFromAssembly"/> for the rest of the behaviour.
    /// </remarks>
    /// <example>
    /// <code>
    /// options.RegisterEntitiesFromAssemblies(typeof(User).Assembly, typeof(Invoice).Assembly);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(ScanningRequiresUnreferencedCode)]
    public IdoraOptions RegisterEntitiesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (Assembly assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));
            EntityPrefixAttributeScanner.Register(this, assembly);
        }

        return this;
    }

    /// <summary>
    /// Registers every type carrying <see cref="EntityPrefixAttribute"/> in the assembly that
    /// declares <typeparamref name="TMarker"/>.
    /// </summary>
    /// <typeparam name="TMarker">
    /// Any type from the assembly to scan. One of your entities reads best &#8212; it says where the
    /// entities live without needing a marker interface.
    /// </typeparam>
    /// <returns>The same options instance, so calls can be chained.</returns>
    /// <exception cref="InvalidEntityPrefixException">
    /// An attribute declares a prefix that is not valid. The message names the type carrying it.
    /// </exception>
    /// <exception cref="DuplicateEntityPrefixException">
    /// Two attributed types declare the same prefix (ignoring case), or an attributed type has also
    /// been registered with <see cref="RegisterEntity(Type, string)"/>.
    /// </exception>
    /// <remarks>
    /// Equivalent to <c>RegisterEntitiesFromAssembly(typeof(TMarker).Assembly)</c>, and the form
    /// worth reaching for first: it survives the assembly being renamed, and it fails to compile
    /// rather than fails at start-up if the type moves. See
    /// <see cref="RegisterEntitiesFromAssembly"/> for the rest of the behaviour.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddIdora(options => options.RegisterEntitiesFromAssemblyContaining&lt;User&gt;());
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(ScanningRequiresUnreferencedCode)]
    public IdoraOptions RegisterEntitiesFromAssemblyContaining<TMarker>()
    {
        EntityPrefixAttributeScanner.Register(this, typeof(TMarker).Assembly);
        return this;
    }
}
