namespace UserFriendlyIds;

/// <summary>
/// Thrown when an entity prefix does not meet the rules described in
/// <see cref="UserFriendlyIdOptions.RegisterEntity{TEntity}(string)"/>.
/// </summary>
/// <remarks>
/// A prefix must be 1 to 16 characters long and contain only ASCII letters and digits. The
/// underscore is excluded because it separates the prefix from the value; if a prefix could contain
/// one, an identifier could no longer be split back into its two parts unambiguously.
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
}
