package io.github.srkleraimbot.idora.internal;

import java.nio.charset.StandardCharsets;

/**
 * Encodes the 128 bits of a ULID as 26 lowercase Crockford Base32 characters (specification
 * section 3.1).
 *
 * <p>Crockford Base32 omits {@code i}, {@code l}, {@code o} and {@code u}: the first three are
 * easily confused with {@code 1} and {@code 0}, and the last keeps the encoding from spelling
 * obscenities. The canonical alphabet is uppercase; Idora emits lowercase throughout, which is
 * loss-free because no two characters of the alphabet differ only by case.
 *
 * <p>26 characters carry 130 bits, two more than a ULID needs, so the value is right-aligned and
 * the first character never exceeds {@code 7}. The timestamp fills the first 10 characters and the
 * random field the remaining 16, both landing on character boundaries, which is why encoding the
 * two fields separately equals encoding all 128 bits as one number.
 */
public final class CrockfordBase32 {

    /** The lowercase Crockford Base32 alphabet. */
    public static final String ALPHABET = "0123456789abcdefghjkmnpqrstvwxyz";

    /** Number of characters in an encoded ULID. */
    public static final int ULID_LENGTH = 26;

    private static final byte[] ALPHABET_BYTES = ALPHABET.getBytes(StandardCharsets.US_ASCII);

    private CrockfordBase32() {
    }

    /**
     * Writes the 26-character encoding of a ULID as ASCII bytes.
     *
     * @param timestampMs milliseconds since the Unix epoch; only the low 48 bits are encoded
     * @param randomHigh random bits 79..64, in the low 16 bits; higher bits are ignored
     * @param randomLow random bits 63..0
     * @param destination the array to write into
     * @param offset where the first character goes; exactly 26 bytes are written from here
     */
    public static void encodeUlid(long timestampMs, long randomHigh, long randomLow, byte[] destination, int offset) {
        // One bounds check up front; the JIT can then drop the per-element checks below.
        if (offset < 0 || offset > destination.length - ULID_LENGTH) {
            throw new ArrayIndexOutOfBoundsException(offset);
        }
        byte[] a = ALPHABET_BYTES;
        long t = timestampMs;

        // Timestamp: 48 bits over 10 characters. The first carries only the top 3 bits, which also
        // discards anything above bit 47.
        destination[offset] = a[(int) ((t >>> 45) & 0x07)];
        destination[offset + 1] = a[(int) ((t >>> 40) & 0x1F)];
        destination[offset + 2] = a[(int) ((t >>> 35) & 0x1F)];
        destination[offset + 3] = a[(int) ((t >>> 30) & 0x1F)];
        destination[offset + 4] = a[(int) ((t >>> 25) & 0x1F)];
        destination[offset + 5] = a[(int) ((t >>> 20) & 0x1F)];
        destination[offset + 6] = a[(int) ((t >>> 15) & 0x1F)];
        destination[offset + 7] = a[(int) ((t >>> 10) & 0x1F)];
        destination[offset + 8] = a[(int) ((t >>> 5) & 0x1F)];
        destination[offset + 9] = a[(int) (t & 0x1F)];

        // Random field: 80 bits over 16 characters. Character 13 straddles the two words, taking
        // its top bit from randomHigh and its bottom four from randomLow.
        long h = randomHigh;
        long l = randomLow;
        destination[offset + 10] = a[(int) ((h >>> 11) & 0x1F)];
        destination[offset + 11] = a[(int) ((h >>> 6) & 0x1F)];
        destination[offset + 12] = a[(int) ((h >>> 1) & 0x1F)];
        destination[offset + 13] = a[(int) (((h & 0x1) << 4) | (l >>> 60))];
        destination[offset + 14] = a[(int) ((l >>> 55) & 0x1F)];
        destination[offset + 15] = a[(int) ((l >>> 50) & 0x1F)];
        destination[offset + 16] = a[(int) ((l >>> 45) & 0x1F)];
        destination[offset + 17] = a[(int) ((l >>> 40) & 0x1F)];
        destination[offset + 18] = a[(int) ((l >>> 35) & 0x1F)];
        destination[offset + 19] = a[(int) ((l >>> 30) & 0x1F)];
        destination[offset + 20] = a[(int) ((l >>> 25) & 0x1F)];
        destination[offset + 21] = a[(int) ((l >>> 20) & 0x1F)];
        destination[offset + 22] = a[(int) ((l >>> 15) & 0x1F)];
        destination[offset + 23] = a[(int) ((l >>> 10) & 0x1F)];
        destination[offset + 24] = a[(int) ((l >>> 5) & 0x1F)];
        destination[offset + 25] = a[(int) (l & 0x1F)];
    }

    /**
     * Returns the 26-character encoding of a ULID as a string. A convenience for tests and
     * diagnostics; the generator writes straight into its own buffer instead.
     *
     * @param timestampMs milliseconds since the Unix epoch; only the low 48 bits are encoded
     * @param randomHigh random bits 79..64, in the low 16 bits
     * @param randomLow random bits 63..0
     * @return the encoded ULID
     */
    public static String encodeUlid(long timestampMs, long randomHigh, long randomLow) {
        byte[] buffer = new byte[ULID_LENGTH];
        encodeUlid(timestampMs, randomHigh, randomLow, buffer, 0);
        return new String(buffer, StandardCharsets.ISO_8859_1);
    }
}
