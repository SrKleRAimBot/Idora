package io.github.srkleraimbot.idora.internal;

import io.github.srkleraimbot.idora.support.Hex;
import io.github.srkleraimbot.idora.support.UlidDecoder;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Random;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The Crockford Base32 encoder, against hand-checkable extremes and an independently written decoder. */
class CrockfordBase32Test {

    private static final long MAX_TIMESTAMP = (1L << 48) - 1;

    @Test
    void encodeUlid_withEverythingZero_producesTwentySixZeroCharacters() {
        // Act
        String encoded = CrockfordBase32.encodeUlid(0, 0, 0);

        // Assert
        assertEquals("0".repeat(26), encoded);
    }

    @Test
    void encodeUlid_withEveryBitSet_producesTheLargestRepresentableValue() {
        // Act
        String encoded = CrockfordBase32.encodeUlid(MAX_TIMESTAMP, 0xFFFF, -1L);

        // Assert: the leading character holds only three bits, so it tops out at 7.
        assertEquals("7" + "z".repeat(25), encoded);
    }

    @ParameterizedTest
    @CsvSource({
        "1, 00000000010000000000000000",
        "31, 000000000z0000000000000000",
        "32, 00000000100000000000000000",
        "33, 00000000110000000000000000"})
    void encodeUlid_withASmallTimestamp_placesItInTheFirstTenCharacters(long timestamp, String expected) {
        // Act & Assert
        assertEquals(expected, CrockfordBase32.encodeUlid(timestamp, 0, 0));
    }

    @ParameterizedTest
    @CsvSource({
        "1, 00000000000000000000000001",
        "31, 0000000000000000000000000z",
        "32, 00000000000000000000000010"})
    void encodeUlid_withASmallRandomValue_placesItAtTheEnd(long randomLow, String expected) {
        // Act & Assert
        assertEquals(expected, CrockfordBase32.encodeUlid(0, 0, randomLow));
    }

    @Test
    void encodeUlid_placesTheHighRandomWordImmediatelyAfterTheTimestamp() {
        // Act: the top bit of the 80-bit field is bit 15 of the high word.
        String encoded = CrockfordBase32.encodeUlid(0, 0x8000, 0);

        // Assert
        assertEquals("0000000000g000000000000000", encoded);
    }

    @Test
    void encodeUlid_treatsTheLowWordAsUnsigned() {
        // Act: Java longs are signed; bit 63 must land in character 13, not sign-extend.
        String encoded = CrockfordBase32.encodeUlid(0, 0, Long.MIN_VALUE);

        // Assert
        assertEquals("0000000000008000000000000", encoded.substring(1));
        assertEquals(Hex.toBigInteger(0, Long.MIN_VALUE), UlidDecoder.decodeRandom(encoded));
    }

    @Test
    void encodeUlid_ignoresBitsAboveTheFieldWidths() {
        // Act
        String masked = CrockfordBase32.encodeUlid(MAX_TIMESTAMP, 0xFFFF, -1L);
        String unmasked = CrockfordBase32.encodeUlid(-1L, -1L, -1L);

        // Assert
        assertEquals(masked, unmasked);
    }

    @Test
    void encodeUlid_roundTripsThroughAnIndependentDecoder() {
        // Arrange
        Random random = new Random(20260807);

        for (int i = 0; i < 10_000; i++) {
            long timestamp = random.nextLong() & MAX_TIMESTAMP;
            long high = random.nextInt(0x10000);
            long low = random.nextLong();

            // Act
            String encoded = CrockfordBase32.encodeUlid(timestamp, high, low);

            // Assert
            assertEquals(timestamp, UlidDecoder.decodeTimestamp(encoded));
            assertEquals(Hex.toBigInteger(high, low), UlidDecoder.decodeRandom(encoded));
        }
    }

    @Test
    void encodeUlid_neverEmitsTheLettersCrockfordExcludes() {
        // Arrange
        Random random = new Random(20260807);
        Set<Character> produced = new HashSet<>();

        // Act
        for (int i = 0; i < 10_000; i++) {
            for (char c : CrockfordBase32.encodeUlid(random.nextLong(), random.nextLong(), random.nextLong()).toCharArray()) {
                produced.add(c);
            }
        }

        // Assert
        for (char excluded : "ilou".toCharArray()) {
            assertFalse(produced.contains(excluded), "emitted '" + excluded + "'");
        }
        produced.forEach(c -> assertTrue(CrockfordBase32.ALPHABET.indexOf(c) >= 0, "emitted '" + c + "'"));
        assertEquals(32, produced.size());
    }

    @Test
    void encodeUlid_writesOnlyTheTwentySixBytesItPromisesAtTheGivenOffset() {
        // Arrange: a longer buffer, pre-filled, so an overrun on either side would be visible.
        byte[] destination = new byte[32];
        Arrays.fill(destination, (byte) '#');

        // Act
        CrockfordBase32.encodeUlid(MAX_TIMESTAMP, 0xFFFF, -1L, destination, 3);

        // Assert
        assertEquals("###" + "7" + "z".repeat(25) + "###", new String(destination, StandardCharsets.US_ASCII));
    }

    @Test
    void encodeUlid_withTooSmallADestination_throwsBeforeWritingAnything() {
        // Arrange
        byte[] destination = new byte[30];

        // Act & Assert
        assertThrows(ArrayIndexOutOfBoundsException.class, () -> CrockfordBase32.encodeUlid(0, 0, 0, destination, 5));
        assertThrows(ArrayIndexOutOfBoundsException.class, () -> CrockfordBase32.encodeUlid(0, 0, 0, destination, -1));
        assertEquals(0, destination[4]);
    }
}
