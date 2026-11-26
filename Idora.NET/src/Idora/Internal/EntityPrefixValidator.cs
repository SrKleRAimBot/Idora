using System.Globalization;

namespace Idora.Internal;

/// <summary>
/// Enforces the rules an entity prefix must satisfy.
/// </summary>
/// <remarks>
/// Validation is a single pass over the characters with no regular expression, no LINQ and no
/// allocation on the success path, because it runs on every call to
/// <see cref="IIdoraGenerator.Generate(string)"/> as well as at configuration time.
/// </remarks>
internal static class EntityPrefixValidator
{
    /// <summary>The character that separates the prefix from the value.</summary>
    internal const char Separator = '_';

    /// <summary>
    /// The longest prefix allowed. Long enough for a readable abbreviation, short enough that the
    /// prefix never dominates the identifier.
    /// </summary>
    internal const int MaxLength = 16;

    /// <summary>
    /// Throws if <paramref name="prefix"/> is not a valid entity prefix.
    /// </summary>
    /// <param name="prefix">The prefix to check.</param>
    /// <param name="parameterName">
    /// The name of the caller's parameter, used for the <see cref="ArgumentNullException"/> that a
    /// <see langword="null"/> prefix produces.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidEntityPrefixException">
    /// <paramref name="prefix"/> is empty, longer than <see cref="MaxLength"/>, or contains a
    /// character other than an ASCII letter or digit.
    /// </exception>
    internal static void Validate(string prefix, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(prefix, parameterName);

        if (!IsValid(prefix))
        {
            throw Describe(prefix);
        }
    }

    /// <summary>
    /// Reports whether <paramref name="prefix"/> is a valid entity prefix, without throwing and
    /// without allocating.
    /// </summary>
    /// <param name="prefix">The prefix to check.</param>
    /// <returns><see langword="true"/> if every rule is satisfied; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Parsing an identifier checks its prefix on every call, including from
    /// <see cref="IdoraId.TryParse(string, out IdoraId)"/>, where an exception would be the wrong
    /// way to say no. The full explanation is built by <see cref="Describe"/>, and only once the
    /// answer is known to be no.
    /// </remarks>
    internal static bool IsValid(ReadOnlySpan<char> prefix)
    {
        if (prefix.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (char character in prefix)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the exception explaining which rule <paramref name="prefix"/> breaks.
    /// </summary>
    /// <param name="prefix">A prefix that <see cref="IsValid"/> has already rejected.</param>
    /// <returns>An exception reporting the first rule that fails, in the order the rules are listed.</returns>
    internal static InvalidEntityPrefixException Describe(string prefix)
    {
        if (prefix.Length == 0)
        {
            return new InvalidEntityPrefixException(
                "An entity prefix cannot be empty. Supply 1 to " + MaxLength
                + " ASCII letters or digits, for example \"usr\".",
                prefix);
        }

        if (prefix.Length > MaxLength)
        {
            return new InvalidEntityPrefixException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The entity prefix '{0}' is {1} characters long; the maximum is {2}.",
                    prefix,
                    prefix.Length,
                    MaxLength),
                prefix);
        }

        ReadOnlySpan<char> characters = prefix.AsSpan();
        for (int i = 0; i < characters.Length; i++)
        {
            char character = characters[i];
            if (char.IsAsciiLetterOrDigit(character))
            {
                continue;
            }

            return new InvalidEntityPrefixException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    character == Separator
                        ? "The entity prefix '{0}' contains the separator '{1}' at index {2}. The separator "
                          + "divides the prefix from the value, so a prefix cannot contain one."
                        : "The entity prefix '{0}' contains the character '{1}' at index {2}. Prefixes may "
                          + "contain only ASCII letters and digits.",
                    prefix,
                    character,
                    i),
                prefix);
        }

        throw new ArgumentException("The prefix is valid, so there is nothing to describe.", nameof(prefix));
    }
}
