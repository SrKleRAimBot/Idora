using System.Globalization;

namespace UserFriendlyIds;

/// <summary>
/// Thrown when an entity prefix does not meet the rules described in
/// <see cref="UserFriendlyIdOptions.RegisterEntity{TEntity}(string)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A prefix must be 1 to 16 characters long and contain only ASCII letters and digits. The
/// underscore is excluded because it separates the prefix from the value; if a prefix could contain
/// one, an identifier could no longer be split back into its two parts unambiguously.
/// </para>
/// <para>
/// The same rules apply to a prefix declared with <see cref="EntityPrefixAttribute"/>. Because an
/// attribute leaves no stack trace pointing at the line that declared it, the message in that case
/// names the type carrying the attribute.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// options.RegisterEntity&lt;User&gt;("user_account"); // throws: contains the separator
/// options.RegisterEntity&lt;User&gt;("");            // throws: empty
/// options.RegisterEntity&lt;User&gt;("usr");          // fine
/// </code>
/// </example>
public sealed class InvalidEntityPrefixException : UserFriendlyIdsException
{
    /// <summary>Initialises a new instance with a default message.</summary>
    public InvalidEntityPrefixException()
        : base("The entity prefix is not valid.")
    {
    }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the problem.</param>
    public InvalidEntityPrefixException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with the specified message and underlying cause.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public InvalidEntityPrefixException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance describing a specific offending prefix.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="prefix">The prefix that was rejected.</param>
    public InvalidEntityPrefixException(string message, string? prefix)
        : base(message) => Prefix = prefix;

    /// <summary>The prefix that was rejected, if one was supplied.</summary>
    public string? Prefix { get; }

    /// <summary>
    /// Builds the exception for a prefix declared with <see cref="EntityPrefixAttribute"/>, naming
    /// the type that carries it.
    /// </summary>
    /// <param name="entityType">The type the attribute is applied to.</param>
    /// <param name="prefix">The prefix the attribute declared.</param>
    /// <param name="detail">The underlying complaint about the prefix.</param>
    internal static InvalidEntityPrefixException ForAttribute(
        Type entityType,
        string? prefix,
        string detail) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "The [EntityPrefix] attribute on '{0}' does not declare a valid prefix. {1}",
                entityType,
                detail),
            prefix);
}
