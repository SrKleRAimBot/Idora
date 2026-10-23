package io.github.srkleraimbot.idora.internal;

import io.github.srkleraimbot.idora.support.Concurrency;
import io.github.srkleraimbot.idora.support.Hex;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.math.BigInteger;
import java.util.Arrays;
import java.util.HashSet;
import java.util.HexFormat;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The striped state: the buffered entropy keeps producing fresh bytes across refills, oversized
 * requests still work, a thread always gets the same stripe, and the pool is sized sensibly.
 */
class StripeTest {

    @Test
    void nextBytes_producesDifferentBytesOnSuccessiveCalls() {
        // Arrange
        Stripe stripe = new Stripe();
        byte[] first = new byte[16];
        byte[] second = new byte[16];

        // Act
        stripe.nextBytes(first);
        stripe.nextBytes(second);

        // Assert
        assertFalse(Arrays.equals(first, second));
    }

    @Test
    void nextBytes_acrossManyBufferRefills_neverRepeatsABlock() {
        // Arrange: 16 bytes at a time for well past the 1 KiB block, so refills happen repeatedly.
        Stripe stripe = new Stripe();
        Set<String> blocks = new HashSet<>();
        byte[] buffer = new byte[16];

        // Act
        for (int i = 0; i < 5_000; i++) {
            stripe.nextBytes(buffer);
            blocks.add(HexFormat.of().formatHex(buffer));
        }

        // Assert
        assertEquals(5_000, blocks.size());
    }

    @Test
    void nextBytes_withARequestLargerThanTheBlock_stillFillsEveryByte() {
        // Arrange
        Stripe stripe = new Stripe();
        byte[] destination = new byte[4096];

        // Act
        stripe.nextBytes(destination);

        // Assert
        Set<Byte> distinct = new HashSet<>();
        for (byte b : destination) {
            distinct.add(b);
        }
        assertTrue(distinct.size() > 200, "only " + distinct.size() + " distinct byte values");
    }

    @Test
    void nextBytes_withAnEmptyArray_doesNothing() {
        // Arrange
        Stripe stripe = new Stripe();

        // Act & Assert
        assertDoesNotThrow(() -> stripe.nextBytes(new byte[0]));
    }

    @Test
    void next_drawsDistinctRandomFieldsWithinEightyBits() {
        // Arrange
        Stripe stripe = new Stripe();
        Set<BigInteger> values = new HashSet<>();

        // Act: a new millisecond each time forces a fresh draw rather than an increment.
        for (int i = 0; i < 20_000; i++) {
            stripe.next(1_000 + i);

            // Assert
            assertTrue(stripe.randomHigh() >= 0 && stripe.randomHigh() <= 0xFFFF);
            values.add(Hex.toBigInteger(stripe.randomHigh(), stripe.randomLow()));
        }
        assertEquals(20_000, values.size());
    }

    @Test
    void ulid_writesThePrefixTheSeparatorAndTheValue() {
        // Arrange
        Stripe stripe = new Stripe();

        // Act
        String fromBytes = stripe.ulid("ab_".getBytes(java.nio.charset.StandardCharsets.US_ASCII));
        String fromString = stripe.ulid("ab");

        // Assert: the stream is shared, so the second sorts after the first.
        assertTrue(fromBytes.matches("ab_[0-9a-hjkmnp-tv-z]{26}"), fromBytes);
        assertTrue(fromString.matches("ab_[0-9a-hjkmnp-tv-z]{26}"), fromString);
        assertTrue(fromString.compareTo(fromBytes) > 0);
    }

    @Test
    void current_returnsTheSameStripeForTheSameThread() {
        // Act
        Stripe first = Stripe.current();
        Stripe second = Stripe.current();

        // Assert: what keeps one thread's ULIDs on one monotonic stream.
        assertSame(first, second);
    }

    @Test
    void current_spreadsConcurrentThreadsAcrossStripes() throws Exception {
        // Arrange
        int threads = Math.min(Stripe.count(), Concurrency.threadCount());

        // Act
        List<Stripe> stripes = Concurrency.onPlatformThreads(threads, threads * 4, task -> Stripe.current());

        // Assert
        assertTrue(new HashSet<>(stripes).size() > 1, "every thread mapped to one stripe");
    }

    @Test
    void count_isAPowerOfTwoAtLeastTwiceTheProcessorsUpToTheCap() {
        // Act
        int count = Stripe.count();

        // Assert
        assertEquals(1, Integer.bitCount(count));
        assertEquals(Stripe.stripeCountFor(Runtime.getRuntime().availableProcessors()), count);
    }

    @ParameterizedTest
    @CsvSource({"0, 2", "1, 2", "2, 4", "3, 8", "4, 8", "14, 32", "16, 32", "100, 256", "128, 256", "1000, 256"})
    void stripeCountFor_roundsTwiceTheProcessorCountUpToAPowerOfTwo(int processors, int expected) {
        // Act & Assert
        assertEquals(expected, Stripe.stripeCountFor(processors));
    }

    @Test
    void seededCount_neverExceedsTheNumberOfStripes() {
        // Act
        Stripe.current().ulid("x");

        // Assert
        assertTrue(Stripe.seededCount() >= 1);
        assertTrue(Stripe.seededCount() <= Stripe.count());
    }
}
