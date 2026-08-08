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
/// This is thrown during configuration at application start-up, not at the point an identifier is
/// generated, so a misconfiguration fails the process immediately rather than in production traffic.
/// It does not matter where the two registrations came from: two
/// <see cref="UserFriendlyIdOptions.RegisterEntity{TEntity}(string)"/> calls, two
/// <see cref="EntityPrefixAttribute"/> declarations, or one of each all clash the same way.
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
    /// <remarks>
    /// A registration can arrive from a <c>RegisterEntity</c> call, an
    /// <see cref="EntityPrefixAttribute"/>, or an assembly scan that covers the type &#8212; and
    /// scanning one assembly twice is as much a repeat as any other. The advice below therefore names
    /// all three rather than guessing which one the caller used.
    /// </remarks>
    internal static DuplicateEntityPrefixException ForEntityType(
        Type entityType,
        string existingPrefix,
        string attemptedPrefix) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                // Both registrations agreeing on the prefix says the mapping was declared twice rather
                // than declared differently, which is a different mistake and a different fix. Saying
                // "cannot also be registered with 'usr'" when it is already 'usr' just reads as a bug.
                string.Equals(existingPrefix, attemptedPrefix, StringComparison.Ordinal)
                    ? "The entity type '{0}' is already registered with the prefix '{1}', and cannot be "
                      + "registered a second time. Each entity type may be registered exactly once. Look "
                      + "for a repeated RegisterEntity call, a type carrying an [EntityPrefix] attribute "
                      + "that is also registered by hand, or the same assembly being scanned twice."
                    : "The entity type '{0}' is already registered with the prefix '{1}' and cannot also "
                      + "be registered with '{2}'. Each entity type may have only one prefix. Remove "
                      + "whichever registration is wrong: a RegisterEntity call, an [EntityPrefix] "
                      + "attribute, or an assembly scan that covers the type.",
                entityType,
                existingPrefix,
                attemptedPrefix),
            attemptedPrefix,
            entityType,
            entityType);
}
