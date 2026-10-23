package io.github.srkleraimbot.idora.internal;

import io.github.srkleraimbot.idora.support.UlidDecoder;
import org.junit.jupiter.api.Test;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

/** The UUIDv7 layout, checked against java.util.UUID's own reading of the version and variant. */
class UuidV7Test {

    private static final long ARBITRARY_TIMESTAMP = 1_700_000_000_000L;

    @Test
    void writeHex_producesAVersionSevenRfcVariantUuid() {
        // Arrange
        Random random = new Random(7);

        for (int i = 0; i < 1_000; i++) {
            byte[] bytes = new byte[10];
            random.nextBytes(bytes);

            // Act
            UUID uuid = UUID.fromString(UlidDecoder.hyphenate(hex(ARBITRARY_TIMESTAMP, bytes)));

            // Assert
            assertEquals(7, uuid.version());
            assertEquals(2, uuid.variant());
        }
    }

    @Test
    void writeHex_keepsSeventyFourRandomBitsAndOverwritesOnlyVersionAndVariant() {
        // Arrange: every bit set, so any random bit that survives reads as 1.
        byte[] ones = new byte[10];
        Arrays.fill(ones, (byte) 0xFF);
        byte[] zeros = new byte[10];

        // Act
        UUID allOnes = UUID.fromString(UlidDecoder.hyphenate(hex(0, ones)));
        UUID allZeros = UUID.fromString(UlidDecoder.hyphenate(hex(0, zeros)));

        // Assert: 128 - 48 timestamp - 4 version - 2 variant = 74 bits differ between the two.
        int differing = Long.bitCount(allOnes.getMostSignificantBits() ^ allZeros.getMostSignificantBits())
                + Long.bitCount(allOnes.getLeastSignificantBits() ^ allZeros.getLeastSignificantBits());
        assertEquals(74, differing);
    }

    @Test
    void writeHex_encodesTheTimestampInTheLeadingTwelveCharacters() {
        // Act
        String hex = hex(ARBITRARY_TIMESTAMP, new byte[10]);

        // Assert
        assertEquals(ARBITRARY_TIMESTAMP, UlidDecoder.decodeUuidTimestamp(hex));
    }

    @Test
    void writeHex_withTheEpochAndTheLargestTimestamp_keepsTheLow48Bits() {
        // Act & Assert
        assertEquals(0, UlidDecoder.decodeUuidTimestamp(hex(0, new byte[10])));
        assertEquals((1L << 48) - 1, UlidDecoder.decodeUuidTimestamp(hex((1L << 48) - 1, new byte[10])));
        assertEquals((1L << 48) - 1, UlidDecoder.decodeUuidTimestamp(hex(-1L, new byte[10])));
    }

    @Test
    void writeHex_matchesComposeByteForByte() {
        // Arrange
        Random random = new Random(9562);

        for (int i = 0; i < 1_000; i++) {
            byte[] bytes = new byte[14];
            random.nextBytes(bytes);
            long timestamp = random.nextLong() & ((1L << 48) - 1);

            // Act: read from a non-zero offset, as the generator does.
            byte[] octets = UuidV7.compose(timestamp, bytes, 4);
            byte[] hex = new byte[UuidV7.HEX_LENGTH];
            UuidV7.writeHex(timestamp, bytes, 4, hex, 0);

            // Assert
            ByteBuffer buffer = ByteBuffer.wrap(octets);
            UUID fromOctets = new UUID(buffer.getLong(), buffer.getLong());
            assertEquals(fromOctets.toString().replace("-", ""), new String(hex, StandardCharsets.US_ASCII));
        }
    }

    @Test
    void writeHex_forIncreasingTimestamps_producesTextThatSortsInTimeOrder() {
        // Arrange
        Random random = new Random(1);
        List<String> generated = new ArrayList<>();

        // Act
        for (int offset = 0; offset < 100; offset++) {
            byte[] bytes = new byte[10];
            random.nextBytes(bytes);
            generated.add(hex(ARBITRARY_TIMESTAMP + offset, bytes));
        }

        // Assert
        List<String> sorted = new ArrayList<>(generated);
        sorted.sort(null);
        assertEquals(generated, sorted);
    }

    @Test
    void stripe_withTheSameMillisecond_stillProducesDistinctValues() {
        // Arrange
        Stripe stripe = new Stripe();
        Set<String> values = new HashSet<>();

        // Act: the generator stamps the real clock, so most of these share a millisecond.
        for (int i = 0; i < 10_000; i++) {
            values.add(stripe.uuidV7("t"));
        }

        // Assert: uniqueness within a millisecond comes entirely from the random field.
        assertEquals(10_000, values.size());
    }

    @Test
    void writeHex_withTooSmallABuffer_throws() {
        // Act & Assert
        assertThrows(ArrayIndexOutOfBoundsException.class, () -> UuidV7.writeHex(0, new byte[10], 0, new byte[31], 0));
        assertThrows(ArrayIndexOutOfBoundsException.class, () -> UuidV7.writeHex(0, new byte[9], 0, new byte[32], 0));
    }

    private static String hex(long timestamp, byte[] random) {
        byte[] destination = new byte[UuidV7.HEX_LENGTH];
        UuidV7.writeHex(timestamp, random, 0, destination, 0);
        return new String(destination, StandardCharsets.US_ASCII);
    }
}
