namespace UserFriendlyIds.Tests.Support;

/// <summary>
/// Decodes a ULID back into its parts.
/// </summary>
/// <remarks>
/// Written independently of the encoder under test, and structured differently from it (a loop over
/// characters rather than unrolled shifts), so that a mistake in the encoder cannot be mirrored here
/// and cancel itself out.
/// </remarks>
internal static class UlidDecoder
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>Characters of a ULID that hold the timestamp.</summary>
    private const int TimestampLength = 10;

    /// <summary>Reads the 48-bit millisecond timestamp from the first 10 characters.</summary>
    internal static long DecodeTimestamp(string ulid)
    {
        long value = 0;
        for (int i = 0; i < TimestampLength; i++)
        {
            value = (value << 5) | (uint)IndexOf(ulid[i]);
        }

        return value;
    }

    /// <summary>Reads the 80-bit random field from the last 16 characters.</summary>
    internal static UInt128 DecodeRandomness(string ulid)
    {
        UInt128 value = UInt128.Zero;
        for (int i = TimestampLength; i < ulid.Length; i++)
        {
            value = (value << 5) | (UInt128)(ulong)IndexOf(ulid[i]);
        }

        return value;
    }

    /// <summary>Returns the value of a Crockford Base32 character.</summary>
    private static int IndexOf(char character)
    {
        int index = Alphabet.IndexOf(character, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new FormatException($"'{character}' is not a lowercase Crockford Base32 character.");
        }

        return index;
    }

    /// <summary>Splits an identifier into its prefix and its value.</summary>
    internal static (string Prefix, string Value) Split(string id)
    {
        int separator = id.IndexOf('_', StringComparison.Ordinal);
        return (id[..separator], id[(separator + 1)..]);
    }
}
