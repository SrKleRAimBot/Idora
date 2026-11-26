using System.Globalization;

namespace Idora.Internal;

/// <summary>
/// Checks the value half of an identifier &#8212; everything after the first underscore &#8212; and
/// reads the millisecond timestamp it begins with.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the generators' output, read backwards, and nothing more lenient: a ULID is exactly
/// 26 lowercase Crockford Base32 characters whose first is no greater than <c>7</c>; a UUIDv7 is
/// exactly 32 lowercase hexadecimal characters carrying the version <c>7</c> at index 12 and an RFC
/// variant (<c>8</c>, <c>9</c>, <c>a</c> or <c>b</c>) at index 16. The length alone decides which
/// rule applies, so no value can satisfy both.
/// </para>
/// <para>
/// Like the prefix check, this is split in two. <see cref="TryRead"/> is the fast path every parse
/// takes: one pass, no allocation, a yes or no. <see cref="Describe"/> works out what exactly is
/// wrong, and runs only once the answer is known to be no.
/// </para>
/// </remarks>
internal static class IdValueReader
{
    /// <summary>Number of characters in a hyphen-free UUIDv7 value.</summary>
    internal const int UuidV7Length = UuidV7ValueWriter.UuidLength;

    /// <summary>Number of leading hexadecimal characters that encode a UUIDv7's timestamp.</summary>
    internal const int UuidV7TimestampLength = 12;

    /// <summary>Index of the character that carries a UUIDv7's version.</summary>
    private const int UuidV7VersionIndex = 12;

    /// <summary>Index of the character whose top two bits carry a UUIDv7's variant.</summary>
    private const int UuidV7VariantIndex = 16;

    /// <summary>Marks a character that is not in the lowercase Crockford alphabet.</summary>
    private const byte NotCrockford = 0xFF;

    /// <summary>
    /// The value of each ASCII character in the lowercase Crockford Base32 alphabet, or
    /// <see cref="NotCrockford"/>. Uppercase letters are deliberately absent: generators emit
    /// lowercase, and parsing is strict.
    /// </summary>
    /// <remarks>
    /// Compiled into the assembly's data section, so the lookup costs neither an allocation nor a
    /// static constructor.
    /// </remarks>
    private static ReadOnlySpan<byte> CrockfordValues =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0xFF, 0x12, 0x13, 0xFF, 0x14, 0x15, 0xFF,
        0x16, 0x17, 0x18, 0x19, 0x1A, 0xFF, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    /// <summary>
    /// Checks <paramref name="value"/> and, if it is a valid ULID or UUIDv7, reads its timestamp.
    /// </summary>
    /// <param name="value">The text after the first underscore.</param>
    /// <param name="timestampMs">
    /// Receives the 48-bit millisecond timestamp the value begins with, or zero if it is not valid.
    /// </param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid value.</returns>
    internal static bool TryRead(ReadOnlySpan<char> value, out long timestampMs) => value.Length switch
    {
        CrockfordBase32.UlidLength => TryReadUlid(value, out timestampMs),
        UuidV7Length => TryReadUuidV7(value, out timestampMs),
        _ => Fail(out timestampMs),
    };

    /// <summary>
    /// Explains why <paramref name="value"/> is not a valid ULID or UUIDv7, as a sentence that can
    /// follow on from "the value is not valid".
    /// </summary>
    /// <param name="value">A value that <see cref="TryRead"/> has already rejected.</param>
    internal static string Describe(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return "There is nothing after the separator.";
        }

        if (value.Length == CrockfordBase32.UlidLength)
        {
            return DescribeUlid(value);
        }

        if (value.Length == UuidV7Length)
        {
            return DescribeUuidV7(value);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "It is {0} characters long, but a ULID is {1} characters and a UUIDv7 {2}.",
            value.Length,
            CrockfordBase32.UlidLength,
            UuidV7Length);
    }

    private static bool TryReadUlid(ReadOnlySpan<char> value, out long timestampMs)
    {
        // 10 characters hold 50 bits; a 48-bit timestamp leaves the top two clear, so the first
        // character can be at most 7. Anything above would describe a time no ULID can carry.
        if (value[0] > '7')
        {
            return Fail(out timestampMs);
        }

        ReadOnlySpan<byte> lookup = CrockfordValues;
        long timestamp = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character >= lookup.Length || lookup[character] == NotCrockford)
            {
                return Fail(out timestampMs);
            }

            if (i < CrockfordBase32.TimestampLength)
            {
                timestamp = (timestamp << 5) | lookup[character];
            }
        }

        timestampMs = timestamp;
        return true;
    }

    private static bool TryReadUuidV7(ReadOnlySpan<char> value, out long timestampMs)
    {
        if (value[UuidV7VersionIndex] != '7' || !IsVariantCharacter(value[UuidV7VariantIndex]))
        {
            return Fail(out timestampMs);
        }

        long timestamp = 0;
        for (int i = 0; i < value.Length; i++)
        {
            int nibble = LowercaseHexValue(value[i]);
            if (nibble < 0)
            {
                return Fail(out timestampMs);
            }

            if (i < UuidV7TimestampLength)
            {
                timestamp = (timestamp << 4) | (uint)nibble;
            }
        }

        timestampMs = timestamp;
        return true;
    }

    private static string DescribeUlid(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<byte> lookup = CrockfordValues;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character < lookup.Length && lookup[character] != NotCrockford)
            {
                continue;
            }

            char lowered = char.ToLowerInvariant(character);
            return lowered != character && lowered < lookup.Length && lookup[lowered] != NotCrockford
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "It has the uppercase character '{0}' at index {1}. Identifier values are always "
                    + "lowercase, and are compared exactly.",
                    character,
                    i)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "It is 26 characters long, so it should be a ULID, but it has the character '{0}' at "
                    + "index {1}, which is not in the lowercase Crockford Base32 alphabet (0-9 and a-z "
                    + "without i, l, o and u).",
                    character,
                    i);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "It is 26 characters long, so it should be a ULID, but it begins with '{0}'. A ULID's first "
            + "character is never greater than '7'.",
            value[0]);
    }

    private static string DescribeUuidV7(ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (LowercaseHexValue(character) >= 0)
            {
                continue;
            }

            return LowercaseHexValue(char.ToLowerInvariant(character)) >= 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "It has the uppercase character '{0}' at index {1}. Identifier values are always "
                    + "lowercase, and are compared exactly.",
                    character,
                    i)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "It is 32 characters long, so it should be a UUIDv7, but it has the character '{0}' at "
                    + "index {1}, which is not a lowercase hexadecimal digit. UUIDv7 values are written "
                    + "without hyphens.",
                    character,
                    i);
        }

        return value[UuidV7VersionIndex] != '7'
            ? string.Format(
                CultureInfo.InvariantCulture,
                "It is 32 characters long, so it should be a UUIDv7, but it has '{0}' at index {1}, where "
                + "a UUIDv7 carries its version, 7.",
                value[UuidV7VersionIndex],
                UuidV7VersionIndex)
            : string.Format(
                CultureInfo.InvariantCulture,
                "It is 32 characters long, so it should be a UUIDv7, but it has '{0}' at index {1}, where "
                + "a UUIDv7 carries its variant, one of 8, 9, a or b.",
                value[UuidV7VariantIndex],
                UuidV7VariantIndex);
    }

    /// <summary>The value of a lowercase hexadecimal digit, or -1 for anything else.</summary>
    private static int LowercaseHexValue(char character)
    {
        uint digit = (uint)(character - '0');
        if (digit <= 9)
        {
            return (int)digit;
        }

        uint letter = (uint)(character - 'a');
        return letter <= 5 ? (int)letter + 10 : -1;
    }

    /// <summary>Whether a character carries the RFC 9562 variant, binary <c>10xx</c>.</summary>
    private static bool IsVariantCharacter(char character) => character is '8' or '9' or 'a' or 'b';

    private static bool Fail(out long timestampMs)
    {
        timestampMs = 0;
        return false;
    }
}
