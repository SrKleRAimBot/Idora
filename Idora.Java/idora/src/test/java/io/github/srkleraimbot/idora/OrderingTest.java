package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.support.Concurrency;
import io.github.srkleraimbot.idora.support.TestEntities.Order;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import io.github.srkleraimbot.idora.support.UlidDecoder;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.util.ArrayList;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Sorting identifiers as plain strings puts them in the order they were created. */
class OrderingTest {

    @Test
    void generate_withUlid_producesStrictlyIncreasingIdentifiersOnOneThread() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);
        String[] ids = new String[100_000];

        // Act
        for (int i = 0; i < ids.length; i++) {
            ids[i] = generator.generate(User.class);
        }

        // Assert: the monotonic counter guarantees this even within a millisecond; no tolerance.
        assertStrictlyIncreasing(ids);
    }

    @Test
    void generate_withUlidFromTwoGeneratorsOnOneThread_isStillOneIncreasingSequence() {
        // Arrange: the monotonic stream belongs to the thread's stripe, not to a generator.
        IdoraGenerator first = generator(IdGenerationStrategy.ULID);
        IdoraGenerator second = generator(IdGenerationStrategy.ULID);
        String[] ids = new String[10_000];

        // Act
        for (int i = 0; i < ids.length; i++) {
            ids[i] = (i % 2 == 0 ? first : second).generate(User.class);
        }

        // Assert
        assertStrictlyIncreasing(ids);
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_forEitherStrategy_producesIncreasingIdentifiersAcrossMillisecondBoundaries(
            IdGenerationStrategy strategy) throws InterruptedException {
        // Arrange
        IdoraGenerator generator = generator(strategy);
        List<String> ids = new ArrayList<>();

        // Act: a pause guarantees a new millisecond, the granularity both strategies order at.
        for (int i = 0; i < 20; i++) {
            ids.add(generator.generate(User.class));
            Thread.sleep(2);
        }

        // Assert
        assertStrictlyIncreasing(ids.toArray(new String[0]));
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_forEitherStrategy_sortsByPrefixThenTime(IdGenerationStrategy strategy) throws InterruptedException {
        // Arrange
        IdoraGenerator generator = generator(strategy);

        // Act
        String firstOrder = generator.generate(Order.class);
        Thread.sleep(2);
        String secondOrder = generator.generate(Order.class);
        String user = generator.generate(User.class);

        // Assert: identifiers group by prefix, and within a prefix they are in time order.
        assertTrue(secondOrder.compareTo(firstOrder) > 0);
        assertTrue(user.compareTo(firstOrder) > 0, "'usr' sorts after 'ord'");
    }

    @Test
    void generate_withUlid_keepsEachPlatformThreadsSequenceIncreasingUnderConcurrentUse() throws Exception {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);
        int threads = Concurrency.threadCount();

        // Act: monotonicity is per thread; across threads the guarantee is the millisecond.
        List<String[]> perThread = Concurrency.onPlatformThreads(threads, threads, task -> generateMany(generator, 5_000));

        // Assert
        perThread.forEach(OrderingTest::assertStrictlyIncreasing);
    }

    @Test
    void generate_withUlid_keepsEachVirtualThreadsSequenceIncreasing() throws Exception {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);

        // Act
        List<String[]> perThread = Concurrency.onVirtualThreads(1_000, task -> generateMany(generator, 200));

        // Assert
        perThread.forEach(OrderingTest::assertStrictlyIncreasing);
    }

    @Test
    void generate_withUlid_encodesANonDecreasingTimestamp() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);
        long previous = 0;

        for (int i = 0; i < 10_000; i++) {
            // Act
            long timestamp = UlidDecoder.decodeTimestamp(UlidDecoder.valueOf(generator.generate(User.class)));

            // Assert
            assertTrue(timestamp >= previous);
            previous = timestamp;
        }
    }

    @Test
    void generate_withUlidAndAnAdHocPrefix_sharesTheSameIncreasingSequence() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);

        // Act
        String registered = UlidDecoder.valueOf(generator.generate(User.class));
        String adHoc = UlidDecoder.valueOf(generator.generate("usr"));

        // Assert
        assertTrue(adHoc.compareTo(registered) > 0);
        assertEquals(26, adHoc.length());
    }

    private static String[] generateMany(IdoraGenerator generator, int count) {
        String[] ids = new String[count];
        for (int i = 0; i < count; i++) {
            ids[i] = generator.generate(User.class);
        }
        return ids;
    }

    private static void assertStrictlyIncreasing(String[] ids) {
        for (int i = 1; i < ids.length; i++) {
            if (ids[i].compareTo(ids[i - 1]) <= 0) {
                throw new AssertionError("identifier " + i + " ('" + ids[i] + "') should sort after identifier "
                        + (i - 1) + " ('" + ids[i - 1] + "')");
            }
        }
    }

    private static IdoraGenerator generator(IdGenerationStrategy strategy) {
        return IdoraGenerator.builder()
                .strategy(strategy)
                .registerEntity(User.class, "usr")
                .registerEntity(Order.class, "ord")
                .build();
    }
}
