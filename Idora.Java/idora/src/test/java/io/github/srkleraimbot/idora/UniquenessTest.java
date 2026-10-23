package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.internal.Stripe;
import io.github.srkleraimbot.idora.support.Concurrency;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Pattern;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Uniqueness, including under concurrency. The concurrent cases double as thread-safety tests: a
 * torn read of the monotonic state or a shared buffer would show up as a duplicate or a malformed
 * identifier.
 */
class UniquenessTest {

    private static final Pattern WELL_FORMED_ULID = Pattern.compile("^usr_[0-7][0-9a-hjkmnp-tv-z]{25}$");
    private static final Pattern WELL_FORMED_UUID = Pattern.compile("^usr_[0-9a-f]{12}7[0-9a-f]{3}[89ab][0-9a-f]{15}$");

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_oneMillionTimesInSequence_producesNoDuplicates(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = generator(strategy);
        int count = 1_000_000;
        Set<String> ids = new HashSet<>(count * 2);

        // Act
        for (int i = 0; i < count; i++) {
            ids.add(generator.generate(User.class));
        }

        // Assert
        assertEquals(count, ids.size());
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_fromEveryCoreAtOnce_producesNoDuplicatesAndOnlyWellFormedIdentifiers(IdGenerationStrategy strategy)
            throws Exception {
        // Arrange
        IdoraGenerator generator = generator(strategy);
        int threads = Concurrency.threadCount();
        int perThread = 20_000;

        // Act
        List<String[]> results = Concurrency.onPlatformThreads(threads, threads, task -> generateMany(generator, perThread));

        // Assert: a race on a shared buffer would most likely surface as a corrupted value, so the
        // shape is checked as well as uniqueness.
        assertUniqueAndWellFormed(results, threads * perThread, strategy);
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_fromTenThousandVirtualThreads_producesNoDuplicatesAndOnlyWellFormedIdentifiers(
            IdGenerationStrategy strategy) throws Exception {
        // Arrange
        IdoraGenerator generator = generator(strategy);
        int tasks = 10_000;
        int perTask = 10;

        // Act
        List<String[]> results = Concurrency.onVirtualThreads(tasks, task -> generateMany(generator, perTask));

        // Assert
        assertUniqueAndWellFormed(results, tasks * perTask, strategy);
    }

    @Test
    void generate_fromManyShortLivedVirtualThreads_seedsNoMoreEntropyPoolsThanThereAreStripes() throws Exception {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);

        // Act: with a ThreadLocal, each of these would have allocated and seeded its own 1 KiB
        // buffer and SecureRandom.
        Concurrency.onVirtualThreads(20_000, task -> generator.generate(User.class));

        // Assert
        assertTrue(Stripe.seededCount() <= Stripe.count());
    }

    @Test
    void generate_fromSeveralGeneratorsOnManyThreads_producesNoDuplicates() throws Exception {
        // Arrange: separate generators share the stripes, the arrangement most likely to collide
        // if the state were mishandled.
        IdoraGenerator first = generator(IdGenerationStrategy.ULID);
        IdoraGenerator second = generator(IdGenerationStrategy.ULID);
        int threads = Concurrency.threadCount();

        // Act
        List<String[]> results = Concurrency.onPlatformThreads(threads, threads * 2,
                task -> generateMany(task % 2 == 0 ? first : second, 10_000));

        // Assert
        assertUniqueAndWellFormed(results, threads * 2 * 10_000, IdGenerationStrategy.ULID);
    }

    @Test
    void generate_withAnAdHocPrefixConcurrently_producesNoDuplicates() throws Exception {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);
        int threads = Concurrency.threadCount();

        // Act
        List<String[]> results = Concurrency.onPlatformThreads(threads, threads, task -> {
            String[] ids = new String[10_000];
            for (int i = 0; i < ids.length; i++) {
                ids[i] = generator.generate("usr");
            }
            return ids;
        });

        // Assert
        assertUniqueAndWellFormed(results, threads * 10_000, IdGenerationStrategy.ULID);
    }

    private static void assertUniqueAndWellFormed(List<String[]> results, int expected, IdGenerationStrategy strategy) {
        Pattern shape = strategy == IdGenerationStrategy.ULID ? WELL_FORMED_ULID : WELL_FORMED_UUID;
        Set<String> unique = new HashSet<>(expected * 2);
        for (String[] ids : results) {
            for (String id : ids) {
                if (!shape.matcher(id).matches()) {
                    throw new AssertionError("malformed identifier: " + id);
                }
                unique.add(id);
            }
        }
        assertEquals(expected, unique.size());
    }

    private static String[] generateMany(IdoraGenerator generator, int count) {
        String[] ids = new String[count];
        for (int i = 0; i < count; i++) {
            ids[i] = generator.generate(User.class);
        }
        return ids;
    }

    private static IdoraGenerator generator(IdGenerationStrategy strategy) {
        return IdoraGenerator.builder().strategy(strategy).registerEntity(User.class, "usr").build();
    }
}
