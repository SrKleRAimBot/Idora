package io.github.srkleraimbot.idora.support;

import java.math.BigInteger;

/** Hexadecimal helpers for the conformance vectors, which write random fields as big-endian hex. */
public final class Hex {

    private Hex() {
    }

    /** Parses big-endian hex into bytes. */
    public static byte[] decode(String hex) {
        byte[] bytes = new byte[hex.length() / 2];
        for (int i = 0; i < bytes.length; i++) {
            bytes[i] = (byte) Integer.parseInt(hex.substring(2 * i, 2 * i + 2), 16);
        }
        return bytes;
    }

    /** Bits 79..64 of an 80-bit field written as 20 hex characters. */
    public static long high16(String hex20) {
        return Long.parseLong(hex20.substring(0, 4), 16);
    }

    /** Bits 63..0 of an 80-bit field written as 20 hex characters. */
    public static long low64(String hex20) {
        return Long.parseUnsignedLong(hex20.substring(4), 16);
    }

    /** Formats an 80-bit field as 20 lowercase hex characters. */
    public static String format80(long high, long low) {
        return String.format("%04x%016x", high, low);
    }

    /** The 80-bit field as one number. */
    public static BigInteger toBigInteger(long high, long low) {
        return BigInteger.valueOf(high).shiftLeft(64).or(new BigInteger(Long.toUnsignedString(low)));
    }
}
