package io.github.srkleraimbot.idora.internal;

import java.nio.charset.StandardCharsets;

/**
 * Lays out RFC 9562 version 7 UUIDs (specification section 3.2): a 48-bit big-endian millisecond
 * timestamp in octets 0 to 5, then octets 6 to 15 from random bytes with the version nibble
 * ({@code 7}) and the variant bits ({@code 10}) written over the top. 74 random bits remain.
 *
 * <p>{@link java.util.UUID#randomUUID()} is not used even as a source of randomness: it is version
 * 4, and it calls into a shared {@link java.security.SecureRandom} for every value. The random
 * bytes here come from the same kind of generator, drawn a block at a time by {@link Stripe}.
 */
public final class UuidV7 {

    /** Number of characters in a hyphen-free UUID. */
    public static final int HEX_LENGTH = 32;

    /** Number of random bytes consumed: octets 6 to 15. */
    public static final int RANDOM_BYTES = 10;

    private static final byte[] HEX = "0123456789abcdef".getBytes(StandardCharsets.US_ASCII);

    private UuidV7() {
    }

    /**
     * Returns the 16 octets of a version 7 UUID.
     *
     * @param timestampMs milliseconds since the Unix epoch; only the low 48 bits are used
     * @param random the array holding the ten bytes destined for octets 6 to 15
     * @param randomOffset where those ten bytes start
     * @return the UUID, big-endian
     */
    public static byte[] compose(long timestampMs, byte[] random, int randomOffset) {
        byte[] octets = new byte[16];
        for (int i = 0; i < 6; i++) {
            octets[i] = (byte) (timestampMs >>> (40 - 8 * i));
        }
        System.arraycopy(random, randomOffset, octets, 6, RANDOM_BYTES);
        octets[6] = (byte) (0x70 | (octets[6] & 0x0F));
        octets[8] = (byte) (0x80 | (octets[8] & 0x3F));
        return octets;
    }

    /**
     * Writes a version 7 UUID as 32 lowercase hexadecimal ASCII bytes, with no hyphens. Produces
     * exactly the hexadecimal form of {@link #compose(long, byte[], int)}, without the intermediate
     * array.
     *
     * @param timestampMs milliseconds since the Unix epoch; only the low 48 bits are used
     * @param random the array holding the ten bytes destined for octets 6 to 15
     * @param randomOffset where those ten bytes start
     * @param destination the array to write into
     * @param offset where the first character goes; exactly 32 bytes are written from here
     */
    public static void writeHex(long timestampMs, byte[] random, int randomOffset, byte[] destination, int offset) {
        if (offset < 0 || offset > destination.length - HEX_LENGTH) {
            throw new ArrayIndexOutOfBoundsException(offset);
        }
        if (randomOffset < 0 || randomOffset > random.length - RANDOM_BYTES) {
            throw new ArrayIndexOutOfBoundsException(randomOffset);
        }
        byte[] hex = HEX;

        // unix_ts_ms: 48 bits, 12 nibbles, most significant first so that text order is time order.
        for (int i = 0; i < 12; i++) {
            destination[offset + i] = hex[(int) ((timestampMs >>> (44 - 4 * i)) & 0xF)];
        }

        // Octet 6: version 7 in the high nibble, the low nibble stays random.
        destination[offset + 12] = '7';
        destination[offset + 13] = hex[random[randomOffset] & 0x0F];

        // Octet 7: random.
        writeOctet(random[randomOffset + 1], hex, destination, offset + 14);

        // Octet 8: variant 10xx in the top two bits, the rest stays random.
        writeOctet((byte) (0x80 | (random[randomOffset + 2] & 0x3F)), hex, destination, offset + 16);

        // Octets 9 to 15: random.
        for (int i = 3; i < RANDOM_BYTES; i++) {
            writeOctet(random[randomOffset + i], hex, destination, offset + 12 + 2 * i);
        }
    }

    private static void writeOctet(byte octet, byte[] hex, byte[] destination, int offset) {
        destination[offset] = hex[(octet >>> 4) & 0x0F];
        destination[offset + 1] = hex[octet & 0x0F];
    }
}
