package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.support.AllocationMeter;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.nio.charset.StandardCharsets;

import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Pins the allocation claim: generating an identifier allocates the returned string (its object
 * and its backing array) and nothing else.
 *
 * <p>Each measurement is compared with allocating one bare Latin-1 string of the same length, the
 * theoretical floor, rather than a hard-coded byte count, so the test stays honest if the JVM's
 * object layout changes.
 */
class AllocationTest {

    private static final int ITERATIONS = 50_000;

    /**
     * Slack per call, in bytes. Covers the amortised cost of refilling the entropy block (DRBG
     * allocates about 200 bytes per 1 KiB refill, which is about 2 bytes per UUIDv7) and rounding.
     * It stays far below the 16 bytes of the smallest possible object, so it cannot hide an extra
     * allocation made on every call.
     */
    private static final double TOLERANCE = 4.0;

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_forARegisteredEntity_allocatesOnlyTheReturnedString(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().strategy(strategy).registerEntity(User.class, "usr").build();
        double floor = floorFor(generator.generate(User.class).length());

        // Act
        double actual = AllocationMeter.bytesPerCall(() -> generator.generate(User.class), ITERATIONS);

        // Assert
        assertOnlyTheString(actual, floor);
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_withAnAdHocPrefix_allocatesOnlyTheReturnedString(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().strategy(strategy).build();
        double floor = floorFor(generator.generate("tmp").length());

        // Act
        double actual = AllocationMeter.bytesPerCall(() -> generator.generate("tmp"), ITERATIONS);

        // Assert
        assertOnlyTheString(actual, floor);
    }

    @Test
    void generate_acrossManyEntropyRefills_stillAllocatesOnlyTheReturnedStrings() {
        // Arrange: 100,000 UUIDs consume about a megabyte of entropy, so the stripe's 1 KiB block is
        // refilled around a thousand times inside the measurement.
        IdoraGenerator generator = IdoraGenerator.builder().useUuidV7().registerEntity(User.class, "usr").build();
        double floor = floorFor(generator.generate(User.class).length());

        // Act
        double actual = AllocationMeter.bytesPerCall(() -> generator.generate(User.class), 100_000);

        // Assert: refilling must not allocate meaningfully, or the cost would scale with volume.
        assertOnlyTheString(actual, floor);
    }

    private static double floorFor(int length) {
        byte[] source = new byte[length];
        return AllocationMeter.bytesPerCall(() -> new String(source, 0, length, StandardCharsets.ISO_8859_1), ITERATIONS);
    }

    private static void assertOnlyTheString(double actual, double floor) {
        assertTrue(Math.abs(actual - floor) <= TOLERANCE,
                "generation should allocate only the returned string (" + floor + " bytes), but allocated " + actual);
    }
}
