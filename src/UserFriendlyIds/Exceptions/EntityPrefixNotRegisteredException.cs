using System.Globalization;

namespace UserFriendlyIds;

/// <summary>
/// Thrown by <see cref="IUserFriendlyIdGenerator.Generate{TEntity}"/> when the entity type has no
/// registered prefix.
/// </summary>
/// <remarks>
/// The generic overload deliberately refuses to invent a prefix. Guessing one from the type name
/// would produce identifiers that silently change the day somebody renames the class, and two types
/// in different namespaces sharing a short name would quietly collide. Register the type instead, or
/// use <see cref="IUserFriendlyIdGenerator.Generate(string)"/> if the prefix genuinely is not known
/// until run time.
/// </remarks>
/// <example>
/// <code>
/// services.AddUserFriendlyIds(options => options.RegisterEntity&lt;Invoice&gt;("inv"));
/// </code>
/// </example>
public sealed class EntityPrefixNotRegisteredException : UserFriendlyIdsException
{
    /// <summary>Initialises a new instance with a default message.</summary>
    public EntityPrefixNotRegisteredException()
        : base("No entity prefix has been registered for the requested entity type.")
    {
    }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the problem.</param>
    public EntityPrefixNotRegisteredException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with the specified message and underlying cause.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public EntityPrefixNotRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance describing a specific unregistered type.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="entityType">The entity type that has no registered prefix.</param>
    public EntityPrefixNotRegisteredException(string message, Type? entityType)
        : base(message) => EntityType = entityType;

    /// <summary>The entity type that has no registered prefix.</summary>
    public Type? EntityType { get; }

    /// <summary>Builds the exception for a specific unregistered entity type.</summary>
    internal static EntityPrefixNotRegisteredException For(Type entityType) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "No entity prefix is registered for '{0}'. Register one during start-up, for example "
                + "services.AddUserFriendlyIds(options => options.RegisterEntity<{1}>(\"abc\")), or call "
                + "Generate(string) with an explicit prefix.",
                entityType,
                entityType.Name),
            entityType);
}
