using System.Globalization;

namespace Idora;

/// <summary>
/// Thrown when text that should be an identifier is not one, or is not one for the entity the caller
/// expected.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is strict. It accepts exactly what a conforming generator emits &#8212; a valid prefix, a
/// single underscore, and a lowercase ULID or UUIDv7 &#8212; and nothing else: no trimming, no case
/// folding, no hyphens. <see cref="Reason"/> says which rule failed, and the message says what to do
/// about it.
/// </para>
/// <para>
/// Unlike the other exceptions in this package, this one is usually not a start-up failure. It
/// reports bad input &#8212; a mistyped route, a foreign key from another system, an identifier for
/// the wrong kind of entity &#8212; so it is normally caught and turned into a validation error
/// rather than allowed to fail the request. Where input is expected to be wrong from time to time,
/// <see cref="IdoraId.TryParse(string, out IdoraId)"/> avoids the exception altogether.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     IdoraId id = IdoraId.Parse(input, expectedPrefix: "usr");
/// }
/// catch (InvalidIdException ex) when (ex.Reason == InvalidIdReason.PrefixMismatch)
/// {
///     // A well-formed identifier, but for something other than a user.
/// }
/// </code>
/// </example>
public sealed class InvalidIdException : IdoraException
{
    /// <summary>
    /// The longest stretch of the rejected text quoted in a message. Identifiers are at most 49
    /// characters, so this never truncates anything close to one; it stops a multi-kilobyte value from
    /// an untrusted caller being copied wholesale into a log line.
    /// </summary>
    private const int MaxQuotedLength = 64;

    /// <summary>Initialises a new instance with a default message.</summary>
    public InvalidIdException()
        : base("The text is not a valid identifier.")
    {
    }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the problem.</param>
    public InvalidIdException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with the specified message and underlying cause.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public InvalidIdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance describing a specific rejected identifier.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="text">The text that failed to parse.</param>
    /// <param name="reason">Which rule the text broke.</param>
    /// <param name="expectedPrefix">The prefix the caller asked for, if it asked for one.</param>
    /// <param name="innerException">
    /// The exception that caused this one: for <see cref="InvalidIdReason.Prefix"/>, the
    /// <see cref="InvalidEntityPrefixException"/> describing what is wrong with the prefix.
    /// </param>
    public InvalidIdException(
        string message,
        string? text,
        InvalidIdReason reason,
        string? expectedPrefix,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Text = text;
        Reason = reason;
        ExpectedPrefix = expectedPrefix;
    }

    /// <summary>The text that failed to parse, exactly as it was supplied.</summary>
    public string? Text { get; }

    /// <summary>
    /// Which rule the text broke, or <see langword="null"/> if the exception was created without
    /// one.
    /// </summary>
    public InvalidIdReason? Reason { get; }

    /// <summary>
    /// The prefix the caller asked for, or <see langword="null"/> if any prefix would have done.
    /// </summary>
    /// <remarks>
    /// Set whenever an expected prefix was supplied, not only for
    /// <see cref="InvalidIdReason.PrefixMismatch"/>: an identifier can be malformed and, separately,
    /// have been asked for as a particular entity's.
    /// </remarks>
    public string? ExpectedPrefix { get; }

    /// <summary>Builds the exception for text containing no underscore at all.</summary>
    internal static InvalidIdException ForNoSeparator(string text, string? expectedPrefix) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' is not a valid identifier: it contains no '_' separator. An identifier is a prefix, "
                + "an underscore and a value, for example 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'. If this is a "
                + "bare ULID or UUID, put the entity's prefix and an underscore in front of it.",
                Quote(text)),
            text,
            InvalidIdReason.NoSeparator,
            expectedPrefix);

    /// <summary>Builds the exception for a prefix that breaks the prefix rules.</summary>
    internal static InvalidIdException ForPrefix(
        string text,
        string? expectedPrefix,
        InvalidEntityPrefixException prefixProblem) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' is not a valid identifier: the part before the first '_' is not a valid prefix. {1}",
                Quote(text),
                prefixProblem.Message),
            text,
            InvalidIdReason.Prefix,
            expectedPrefix,
            prefixProblem);

    /// <summary>Builds the exception for a value that is neither a ULID nor a UUIDv7.</summary>
    internal static InvalidIdException ForValue(string text, string? expectedPrefix, string detail) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' is not a valid identifier: the value after the first '_' is neither a 26-character "
                + "lowercase ULID nor a 32-character lowercase UUIDv7. {1}",
                Quote(text),
                detail),
            text,
            InvalidIdReason.Value,
            expectedPrefix);

    /// <summary>Builds the exception for a well-formed identifier with the wrong prefix.</summary>
    internal static InvalidIdException ForPrefixMismatch(string text, string actualPrefix, string expectedPrefix) =>
        new(
            string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' has the prefix '{1}', but an identifier with the prefix '{2}' was expected. It "
                + "identifies a different kind of entity; prefixes are compared exactly, including case.",
                Quote(text),
                actualPrefix,
                expectedPrefix),
            text,
            InvalidIdReason.PrefixMismatch,
            expectedPrefix);

    /// <summary>Shortens over-long text for quoting in a message, leaving the property untouched.</summary>
    private static string Quote(string text) =>
        text.Length <= MaxQuotedLength ? text : string.Concat(text.AsSpan(0, MaxQuotedLength), "…");
}
