using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Idora.Internal;

/// <summary>
/// Finds types carrying <see cref="EntityPrefixAttribute"/> and registers them.
/// </summary>
/// <remarks>
/// <para>
/// This is the only reflection in the package, and it runs once, during configuration. Everything it
/// finds goes through <see cref="IdoraOptions.RegisterEntity(Type, string)"/> and ends up
/// in the same frozen lookup as a fluent registration, so nothing here is on the path of a call to
/// <see cref="IIdoraGenerator.Generate{TEntity}"/>.
/// </para>
/// <para>
/// Routing through <c>RegisterEntity</c> rather than writing to the dictionaries directly is what
/// makes the two registration styles coexist. That method is already the single place where prefixes
/// are validated and where both kinds of collision &#8212; one prefix on two types, one type
/// registered twice &#8212; are detected, so an attribute and a fluent call police each other with
/// no extra logic and in either order.
/// </para>
/// </remarks>
internal static class EntityPrefixAttributeScanner
{
    /// <summary>
    /// Registers every type in <paramref name="assembly"/> that carries
    /// <see cref="EntityPrefixAttribute"/>.
    /// </summary>
    /// <param name="options">The options to register into.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <remarks>
    /// A <see cref="ReflectionTypeLoadException"/> from <see cref="Assembly.GetTypes"/> is allowed to
    /// propagate. An assembly that cannot be fully loaded is a broken deployment, and catching it
    /// here would mean quietly registering some of your entities and not others &#8212; the failure
    /// mode this design exists to avoid.
    /// </remarks>
    [RequiresUnreferencedCode(
        "Entity types discovered by scanning an assembly cannot be seen by the trimmer and may be "
        + "removed. Register entities with RegisterEntity instead when publishing trimmed or "
        + "ahead-of-time compiled.")]
    internal static void Register(IdoraOptions options, Assembly assembly) =>
        Register(options, assembly.GetTypes());

    /// <summary>
    /// Registers every type in <paramref name="types"/> that carries
    /// <see cref="EntityPrefixAttribute"/>, ignoring the rest.
    /// </summary>
    /// <param name="options">The options to register into.</param>
    /// <param name="types">The candidate types.</param>
    /// <remarks>
    /// <para>
    /// Candidates are ordered by full name before being registered. <see cref="Assembly.GetTypes"/>
    /// makes no promise about the order it returns types in, and without a stable order two
    /// attributed types competing for one prefix would report the clash the other way round from one
    /// run to the next &#8212; the message, and the <c>EntityType</c> and <c>ConflictingEntityType</c>
    /// properties, would swap. A start-up failure that describes itself differently each time is a
    /// bad start-up failure.
    /// </para>
    /// <para>
    /// The attribute is read with <c>inherit: false</c>. It is declared
    /// <see cref="AttributeUsageAttribute.Inherited"/> <see langword="false"/> as well, because a
    /// derived type inheriting its base's prefix would collide with that base the moment it was
    /// scanned.
    /// </para>
    /// </remarks>
    internal static void Register(IdoraOptions options, IEnumerable<Type> types)
    {
        IOrderedEnumerable<Type> ordered = types.OrderBy(
            static type => type.FullName ?? type.Name,
            StringComparer.Ordinal);

        foreach (Type type in ordered)
        {
            EntityPrefixAttribute? attribute = type.GetCustomAttribute<EntityPrefixAttribute>(inherit: false);
            if (attribute is null)
            {
                continue;
            }

            Register(options, type, attribute.Prefix);
        }
    }

    /// <summary>
    /// Registers one attributed type, restating any prefix complaint in terms of the attribute.
    /// </summary>
    /// <remarks>
    /// A fluent registration that names a bad prefix leaves a stack trace pointing at the line you
    /// wrote. An attribute leaves nothing, so "the prefix contains '!' at index 3" would send you
    /// hunting through every entity in the assembly. The message is rewritten to name the type.
    /// </remarks>
    private static void Register(IdoraOptions options, Type entityType, string prefix)
    {
        // Reachable in practice: `[EntityPrefix(null!)]` compiles.
        if (prefix is null)
        {
            throw InvalidEntityPrefixException.ForAttribute(
                entityType,
                prefix: null,
                detail: "The prefix is null. Supply 1 to 16 ASCII letters or digits, for example \"usr\".");
        }

        try
        {
            options.RegisterEntity(entityType, prefix);
        }
        catch (InvalidEntityPrefixException exception)
        {
            throw InvalidEntityPrefixException.ForAttribute(entityType, prefix, exception.Message);
        }
    }
}
