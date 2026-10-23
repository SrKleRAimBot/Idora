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

        if (prefix.Length == 0)
        {
            throw new InvalidEntityPrefixException(
                "An entity prefix cannot be empty. Supply 1 to " + MaxLength
                + " ASCII letters or digits, for example \"usr\".",
                prefix);
        }

        if (prefix.Length > MaxLength)
        {
            throw new InvalidEntityPrefixException(
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

            throw new InvalidEntityPrefixException(
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
    }
}
