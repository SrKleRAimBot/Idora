package io.github.srkleraimbot.idora.support;

import java.math.BigInteger;

/**
 * Decodes identifiers back into their parts.
 *
 * <p>Written independently of the encoder under test, and structured differently from it (a loop
 * over characters into a {@link BigInteger} rather than unrolled shifts), so that a mistake in the
 * encoder cannot be mirrored here and cancel itself out.
 */
public final class UlidDecoder {

    private static final String ALPHABET = "0123456789abcdefghjkmnpqrstvwxyz";
    private static final int TIMESTAMP_LENGTH = 10;

    private UlidDecoder() {
    }

    /** Reads the 48-bit millisecond timestamp from the first 10 characters of a ULID. */
    public static long decodeTimestamp(String ulid) {
        long value = 0;
        for (int i = 0; i < TIMESTAMP_LENGTH; i++) {
            value = (value << 5) | indexOf(ulid.charAt(i));
        }
        return value;
    }

    /** Reads the 80-bit random field from the last 16 characters of a ULID. */
    public static BigInteger decodeRandom(String ulid) {
        BigInteger value = BigInteger.ZERO;
        for (int i = TIMESTAMP_LENGTH; i < ulid.length(); i++) {
            value = value.shiftLeft(5).or(BigInteger.valueOf(indexOf(ulid.charAt(i))));
        }
        return value;
    }

    /** Reads the 48-bit millisecond timestamp from the first 12 hex characters of a UUIDv7. */
    public static long decodeUuidTimestamp(String uuidHex) {
        return Long.parseLong(uuidHex.substring(0, 12), 16);
    }

    /** The part before the first underscore. */
    public static String prefixOf(String id) {
        return id.substring(0, id.indexOf('_'));
    }

    /** The part after the first underscore. */
    public static String valueOf(String id) {
        return id.substring(id.indexOf('_') + 1);
    }

    /** Inserts the hyphens a hyphen-free UUID omits, for {@link java.util.UUID#fromString}. */
    public static String hyphenate(String hex) {
        return hex.substring(0, 8) + '-' + hex.substring(8, 12) + '-' + hex.substring(12, 16) + '-'
                + hex.substring(16, 20) + '-' + hex.substring(20);
    }

    private static int indexOf(char character) {
        int index = ALPHABET.indexOf(character);
        if (index < 0) {
            throw new IllegalArgumentException("'" + character + "' is not a lowercase Crockford Base32 character.");
        }
        return index;
    }
}
