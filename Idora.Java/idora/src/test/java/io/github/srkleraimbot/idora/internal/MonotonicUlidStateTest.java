package io.github.srkleraimbot.idora.internal;

import io.github.srkleraimbot.idora.support.Hex;
import io.github.srkleraimbot.idora.support.UlidDecoder;
import org.junit.jupiter.api.Test;

import java.math.BigInteger;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The monotonic ULID step, with the clock supplied as a parameter so that the awkward cases are
 * deterministic instead of dependent on timing luck.
 */
class MonotonicUlidStateTest {

    private static final long ARBITRARY_TIMESTAMP = 1_700_000_000_000L;

    @Test
    void next_onFirstUse_stampsTheSuppliedTimestampAndDrawsRandomness() {
        // Arrange
        CountingState state = new CountingState();

        // Act
        state.next(ARBITRARY_TIMESTAMP);

        // Assert
        assertEquals(ARBITRARY_TIMESTAMP, state.timestampMs());
        assertEquals(ARBITRARY_TIMESTAMP, UlidDecoder.decodeTimestamp(state.encode()));
        assertEquals(1, state.draws);
    }

    @Test
    void next_onFirstUse_drawsEvenForTheEpoch() {
        // Arrange
        CountingState state = new CountingState();

        // Act
        state.next(0);

        // Assert: a fresh state must not mistake time zero for "same millisecond as last time".
        assertEquals(1, state.draws);
    }

    @Test
    void next_withinTheSameMillisecond_incrementsTheRandomFieldByExactlyOne() {
        // Arrange
        CountingState state = new CountingState();
        state.next(ARBITRARY_TIMESTAMP);
        BigInteger before = random(state);

        // Act
        state.next(ARBITRARY_TIMESTAMP);

        // Assert
        assertEquals(before.add(BigInteger.ONE), random(state));
        assertEquals(1, state.draws);
    }

    @Test
    void next_forANewMillisecond_drawsFreshRandomnessRatherThanCounting() {
        // Arrange
        CountingState state = new CountingState();
        state.next(ARBITRARY_TIMESTAMP);
        BigInteger before = random(state);

        // Act
        state.next(ARBITRARY_TIMESTAMP + 1);

        // Assert
        assertEquals(2, state.draws);
        assertNotEquals(before.add(BigInteger.ONE), random(state));
        assertEquals(ARBITRARY_TIMESTAMP + 1, state.timestampMs());
    }

    @Test
    void next_whenTheClockStepsBackwards_keepsTheLastTimestampAndStillIncreases() {
        // Arrange
        CountingState state = new CountingState();
        state.next(ARBITRARY_TIMESTAMP);
        String first = state.encode();

        // Act: an NTP correction or a resumed snapshot moves the clock back a full second.
        state.next(ARBITRARY_TIMESTAMP - 1_000);

        // Assert
        assertEquals(ARBITRARY_TIMESTAMP, state.timestampMs());
        assertTrue(state.encode().compareTo(first) > 0);
    }

    @Test
    void next_whenTheLowWordWraps_carriesIntoTheHighWord() {
        // Arrange
        CountingState state = new CountingState();
        state.set(ARBITRARY_TIMESTAMP, 0, -1L);

        // Act
        state.next(ARBITRARY_TIMESTAMP);

        // Assert
        assertEquals(1, state.randomHigh());
        assertEquals(0, state.randomLow());
        assertEquals(BigInteger.ONE.shiftLeft(64), UlidDecoder.decodeRandom(state.encode()));
    }

    @Test
    void next_whenTheRandomFieldIsExhausted_borrowsTheNextMillisecondAndRedraws() {
        // Arrange: one step from wrapping, which takes 2^80 values in one millisecond in reality.
        CountingState state = new CountingState();
        state.set(ARBITRARY_TIMESTAMP, 0xFFFF, -1L);

        // Act
        state.next(ARBITRARY_TIMESTAMP);

        // Assert
        assertEquals(ARBITRARY_TIMESTAMP + 1, state.timestampMs());
        assertEquals(ARBITRARY_TIMESTAMP + 1, UlidDecoder.decodeTimestamp(state.encode()));
        assertEquals(1, state.draws);
    }

    @Test
    void next_calledRepeatedlyWithAFrozenClock_producesStrictlyIncreasingDistinctValues() {
        // Arrange
        CountingState state = new CountingState();
        List<String> values = new ArrayList<>();

        // Act: the clock never moves, so every value after the first comes from the counter.
        for (int i = 0; i < 1_000; i++) {
            state.next(ARBITRARY_TIMESTAMP);
            values.add(state.encode());
        }

        // Assert
        List<String> sorted = new ArrayList<>(values);
        sorted.sort(null);
        assertEquals(sorted, values);
        assertEquals(values.size(), new HashSet<>(values).size());
    }

    @Test
    void set_keepsOnlySixteenBitsOfTheHighWord() {
        // Arrange
        CountingState state = new CountingState();

        // Act
        state.set(ARBITRARY_TIMESTAMP, 0x1_2345L, 7);

        // Assert
        assertEquals(0x2345L, state.randomHigh());
        assertEquals("2345" + "0000000000000007", Hex.format80(state.randomHigh(), state.randomLow()));
    }

    private static BigInteger random(MonotonicUlidState state) {
        return Hex.toBigInteger(state.randomHigh(), state.randomLow());
    }

    /** Draws from a simple counter so each fresh field is distinct and recognisable. */
    private static final class CountingState extends MonotonicUlidState {
        int draws;

        @Override
        protected void drawRandom() {
            draws++;
            setRandom(draws * 0x1111L, draws * 0x0101_0101_0101_0101L);
        }
    }
}
