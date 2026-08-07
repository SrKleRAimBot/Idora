namespace UserFriendlyIds.Internal;

/// <summary>
/// Encodes the 128 bits of a ULID as 26 lowercase Crockford Base32 characters.
/// </summary>
/// <remarks>
/// <para>
/// Crockford Base32 omits <c>i</c>, <c>l</c>, <c>o</c> and <c>u</c> from its alphabet: the first
/// three are easily confused with <c>1</c> and <c>0</c> when read aloud or copied by hand, and the
/// last is excluded so that the encoding cannot accidentally spell obscenities. That is what makes
/// a ULID pleasant to quote over the phone or read off a screenshot.
/// </para>
/// <para>
/// The canonical alphabet is uppercase; this package emits the lowercase form throughout because
/// lowercase identifiers read better in URLs and log lines. Because the alphabet contains no two
/// characters that differ only by case, lowercasing is loss-free and fully reversible.
/// </para>
/// <para>
/// 26 characters carry 130 bits, two more than a ULID needs, so the encoding is right-aligned and
/// the leading character never exceeds <c>7</c>. The timestamp occupies the first 10 characters and
/// the randomness the remaining 16, which is exactly equivalent to encoding all 128 bits as one
/// stream because both fields land on character boundaries.
/// </para>
/// </remarks>
internal static class CrockfordBase32
{
    /// <summary>The lowercase Crockford Base32 alphabet: digits and letters, less <c>i l o u</c>.</summary>
    internal const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>Number of characters a ULID occupies when encoded.</summary>
    internal const int UlidLength = 26;

    /// <summary>Number of leading characters that encode the 48-bit timestamp.</summary>
    internal const int TimestampLength = 10;

    /// <summary>
    /// Writes the 26-character encoding of a ULID into <paramref name="destination"/>.
    /// </summary>
    /// <param name="timestampMs">
    /// Milliseconds since the Unix epoch. Only the low 48 bits are encoded, which is the full range
    /// a ULID can represent (it runs out in the year 10889).
    /// </param>
    /// <param name="randomHigh">Random bits 79..64, held in the low 16 bits of the value.</param>
    /// <param name="randomLow">Random bits 63..0.</param>
    /// <param name="destination">
    /// A span of at least <see cref="UlidLength"/> characters. Exactly 26 characters are written.
    /// </param>
    internal static void EncodeUlid(long timestampMs, ulong randomHigh, ulong randomLow, Span<char> destination)
    {
        // A single bounds check up front lets the JIT drop the per-character checks below.
        destination = destination[..UlidLength];

        ReadOnlySpan<char> alphabet = Alphabet;
        ulong timestamp = (ulong)timestampMs;

        // Timestamp: 48 bits over 10 characters. The first character carries only the top 3 bits,
        // because 10 characters hold 50 bits and the top 2 are always zero.
        destination[0] = alphabet[(int)((timestamp >> 45) & 0x1F)];
        destination[1] = alphabet[(int)((timestamp >> 40) & 0x1F)];
        destination[2] = alphabet[(int)((timestamp >> 35) & 0x1F)];
        destination[3] = alphabet[(int)((timestamp >> 30) & 0x1F)];
        destination[4] = alphabet[(int)((timestamp >> 25) & 0x1F)];
        destination[5] = alphabet[(int)((timestamp >> 20) & 0x1F)];
        destination[6] = alphabet[(int)((timestamp >> 15) & 0x1F)];
        destination[7] = alphabet[(int)((timestamp >> 10) & 0x1F)];
        destination[8] = alphabet[(int)((timestamp >> 5) & 0x1F)];
        destination[9] = alphabet[(int)(timestamp & 0x1F)];

        // Randomness: 80 bits over 16 characters, split across the two words. Character 13 straddles
        // the boundary, taking its top bit from randomHigh and its bottom four from randomLow.
        destination[10] = alphabet[(int)((randomHigh >> 11) & 0x1F)];
        destination[11] = alphabet[(int)((randomHigh >> 6) & 0x1F)];
        destination[12] = alphabet[(int)((randomHigh >> 1) & 0x1F)];
        destination[13] = alphabet[(int)(((randomHigh & 0x1) << 4) | (randomLow >> 60))];
        destination[14] = alphabet[(int)((randomLow >> 55) & 0x1F)];
        destination[15] = alphabet[(int)((randomLow >> 50) & 0x1F)];
        destination[16] = alphabet[(int)((randomLow >> 45) & 0x1F)];
        destination[17] = alphabet[(int)((randomLow >> 40) & 0x1F)];
        destination[18] = alphabet[(int)((randomLow >> 35) & 0x1F)];
        destination[19] = alphabet[(int)((randomLow >> 30) & 0x1F)];
        destination[20] = alphabet[(int)((randomLow >> 25) & 0x1F)];
        destination[21] = alphabet[(int)((randomLow >> 20) & 0x1F)];
        destination[22] = alphabet[(int)((randomLow >> 15) & 0x1F)];
        destination[23] = alphabet[(int)((randomLow >> 10) & 0x1F)];
        destination[24] = alphabet[(int)((randomLow >> 5) & 0x1F)];
        destination[25] = alphabet[(int)(randomLow & 0x1F)];
    }
}
