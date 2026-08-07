using System.Globalization;

namespace UserFriendlyIds;

/// <summary>
/// Thrown during configuration when a prefix would no longer identify exactly one entity type.
/// </summary>
/// <remarks>
/// <para>
/// This covers two mistakes. The first is giving the same prefix to two different entity types,
/// which would defeat the whole point of a prefix &#8212; you could no longer tell from
/// <c>acc_01k2f9…</c> whether you were looking at an <c>Account</c> or an <c>Accrual</c>. The second
/// is registering the same entity type twice, which is almost always a copy-paste slip and would
/// leave it ambiguous which prefix wins.
/// </para>
/// <para>
/// Prefixes are compared case-insensitively, so <c>usr</c> and <c>USR</c> collide. Two identifiers
/// that differ only in the capitalisation of their prefix would be a support nightmare, and many
/// stores compare strings case-insensitively anyway.
/// </para>
/// <para>
/// This is thrown from <see cref="UserFriendlyIdOptions.RegisterEntity{TEntity}(string)"/> at
/// application start-up, not at the point an identifier is generated, so a misconfiguration fails
/// the process immediately rather than in production traffic.
/// </para>
/// </remarks>
public sealed class DuplicateEntityPrefixException : UserFriendlyIdsException
{
    /// <summary>Initialises a new instance with a default message.</summary>
    public DuplicateEntityPrefixException()
        : base("The entity prefix is already registered.")
    {
    }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the problem.</param>
    public DuplicateEntityPrefixException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with the specified message and underlying cause.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public DuplicateEntityPrefixException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance describing a specific clash.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="prefix">The prefix involved in the clash.</param>
    /// <param name="entityType">The entity type being registered when the clash was detected.</param>
    /// <param name="conflictingEntityType">The entity type that already held the prefix.</param>
    public DuplicateEntityPrefixException(
        string message,
        string? prefix,
        Type? entityType,
        Type? conflictingEntityType)
        : base(message)
    {
        Prefix = prefix;
        EntityType = entityType;
        ConflictingEntityType = conflictingEntityType;
    }

    /// <summary>The prefix involved in the clash.</summary>
    public string? Prefix { get; }

    /// <summary>The entity type that was being registered when the clash was detected.</summary>
    public Type? EntityType { get; }

    /// <summary>
    /// The entity type that already held the prefix. When the same type is registered twice this is
    /// the same value as <see cref="EntityType"/>.
    /// </summary>
    public Type? ConflictingEntityType { get; }

    /// <summary>Builds the exception for two different types competing for one prefix.</summary>
    internal static DuplicateEntityPrefixException ForPrefix(
        string prefix,
        Type entityType,
        Type conflictingEntityType) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "The entity prefix '{0}' is already registered for '{1}' and cannot also be used for '{2}'. "
                + "Prefixes must identify exactly one entity type, and are compared ignoring case. "
                + "Choose a different prefix for '{2}'.",
                prefix,
                conflictingEntityType,
                entityType),
            prefix,
            entityType,
            conflictingEntityType);

    /// <summary>Builds the exception for one type being registered more than once.</summary>
    internal static DuplicateEntityPrefixException ForEntityType(
        Type entityType,
        string existingPrefix,
        string attemptedPrefix) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "The entity type '{0}' is already registered with the prefix '{1}' and cannot also be "
                + "registered with '{2}'. Each entity type may have only one prefix; remove the duplicate "
                + "RegisterEntity call.",
                entityType,
                existingPrefix,
                attemptedPrefix),
            attemptedPrefix,
            entityType,
            entityType);
}
