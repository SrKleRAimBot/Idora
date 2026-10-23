package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.InvalidEntityPrefixException.Reason;
import io.github.srkleraimbot.idora.internal.CrockfordBase32;
import io.github.srkleraimbot.idora.internal.MonotonicUlidState;
import io.github.srkleraimbot.idora.internal.UuidV7;
import io.github.srkleraimbot.idora.support.Hex;
import io.github.srkleraimbot.idora.support.Json;
import io.github.srkleraimbot.idora.support.TestEntities.Account;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.OptionalInt;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.DynamicTest.dynamicTest;

/**
 * Reproduces every vector in the shared {@code spec/test-vectors.json}, which every Idora package
 * must reproduce exactly. The file's location is passed in by the build as {@code idora.testVectors}.
 */
class ConformanceTest {

    private static Map<String, Object> vectors;

    @BeforeAll
    static void loadVectors() throws IOException {
        // Arrange
        String location = System.getProperty("idora.testVectors", "../../spec/test-vectors.json");
        Path path = Path.of(location);
        assertTrue(Files.isRegularFile(path), "conformance vectors not found at " + path.toAbsolutePath());
        vectors = map(Json.parse(Files.readString(path, StandardCharsets.UTF_8)));
    }

    @Test
    void theVectorFile_containsEverySection() {
        // Assert: an empty section would make the factories below pass vacuously.
        assertFalse(list(vectors.get("ulid")).isEmpty());
        assertFalse(list(vectors.get("uuidv7")).isEmpty());
        assertFalse(list(vectors.get("ulidMonotonic")).isEmpty());
        Map<String, Object> prefixes = map(vectors.get("prefixes"));
        for (String key : List.of("valid", "invalid", "collidingPairs", "distinctPairs")) {
            assertFalse(list(prefixes.get(key)).isEmpty(), key);
        }
    }

    @TestFactory
    Stream<DynamicTest> ulid() {
        return list(vectors.get("ulid")).stream().map(ConformanceTest::map).map(vector -> {
            long timestamp = (Long) vector.get("timestampMs");
            String random = (String) vector.get("randomHex");
            String expected = (String) vector.get("expected");
            return dynamicTest(timestamp + " + " + random, () -> {
                // Act
                String actual = CrockfordBase32.encodeUlid(timestamp, Hex.high16(random), Hex.low64(random));

                // Assert
                assertEquals(expected, actual);
            });
        });
    }

    @TestFactory
    Stream<DynamicTest> uuidv7() {
        return list(vectors.get("uuidv7")).stream().map(ConformanceTest::map).map(vector -> {
            long timestamp = (Long) vector.get("timestampMs");
            byte[] random = Hex.decode((String) vector.get("randomHex"));
            String expected = (String) vector.get("expected");
            return dynamicTest(timestamp + " + " + vector.get("randomHex"), () -> {
                // Act
                byte[] hex = new byte[UuidV7.HEX_LENGTH];
                UuidV7.writeHex(timestamp, random, 0, hex, 0);
                byte[] octets = UuidV7.compose(timestamp, random, 0);

                // Assert: the streaming writer and the octet layout agree with the vector.
                assertEquals(expected, new String(hex, StandardCharsets.US_ASCII));
                assertEquals(expected, toHex(octets));
            });
        });
    }

    @TestFactory
    Stream<DynamicTest> ulidMonotonic() {
        return list(vectors.get("ulidMonotonic")).stream().map(ConformanceTest::map).map(vector -> {
            Map<String, Object> previous = map(vector.get("previous"));
            Map<String, Object> expected = map(vector.get("expected"));
            long now = (Long) vector.get("nowMs");
            return dynamicTest((String) vector.get("description"), () -> {
                // Arrange
                RecordingState state = new RecordingState();
                String previousRandom = (String) previous.get("randomHex");
                state.set((Long) previous.get("timestampMs"), Hex.high16(previousRandom), Hex.low64(previousRandom));

                // Act
                state.next(now);

                // Assert
                assertEquals((long) (Long) expected.get("timestampMs"), state.timestampMs());
                String expectedRandom = (String) expected.get("randomHex");
                if (expectedRandom == null) {
                    // "Freshly drawn, do not compare": but it must actually have been drawn.
                    assertEquals(1, state.draws);
                } else {
                    assertEquals(0, state.draws);
                    assertEquals(expectedRandom, Hex.format80(state.randomHigh(), state.randomLow()));
                    assertEquals(expected.get("encoded"), state.encode());
                }
            });
        });
    }

    @TestFactory
    Stream<DynamicTest> prefixesValid() {
        return list(map(vectors.get("prefixes")).get("valid")).stream().map(String.class::cast).map(prefix ->
                dynamicTest("valid '" + prefix + "'", () -> {
                    // Act
                    IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, prefix).build();

                    // Assert
                    assertTrue(generator.generate(User.class).startsWith(prefix + "_"));
                    assertTrue(generator.generate(prefix).startsWith(prefix + "_"));
                }));
    }

    @TestFactory
    Stream<DynamicTest> prefixesInvalid() {
        return list(map(vectors.get("prefixes")).get("invalid")).stream().map(ConformanceTest::map).map(vector -> {
            String prefix = (String) vector.get("prefix");
            Reason reason = reasonFor((String) vector.get("reason"));
            OptionalInt index = vector.containsKey("index")
                    ? OptionalInt.of(((Long) vector.get("index")).intValue())
                    : OptionalInt.empty();
            return dynamicTest("invalid '" + prefix + "' (" + vector.get("reason") + ")", () -> {
                // Act
                InvalidEntityPrefixException adHoc =
                        assertThrows(InvalidEntityPrefixException.class, () -> IdoraGenerator.create().generate(prefix));
                InvalidEntityPrefixException registered = assertThrows(InvalidEntityPrefixException.class,
                        () -> IdoraGenerator.builder().registerEntity(User.class, prefix));

                // Assert
                for (InvalidEntityPrefixException exception : List.of(adHoc, registered)) {
                    assertEquals(reason, exception.getReason());
                    assertEquals(index, exception.getIndex());
                    assertEquals(prefix, exception.getPrefix());
                    index.ifPresent(i -> assertTrue(exception.getMessage().contains("index " + i), exception.getMessage()));
                }
            });
        });
    }

    @TestFactory
    Stream<DynamicTest> prefixesCollidingPairs() {
        return list(map(vectors.get("prefixes")).get("collidingPairs")).stream().map(ConformanceTest::list).map(pair -> {
            String first = (String) pair.get(0);
            String second = (String) pair.get(1);
            return dynamicTest(first + " collides with " + second, () -> {
                // Arrange
                IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, first);

                // Act
                DuplicateEntityPrefixException exception =
                        assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(Account.class, second));

                // Assert
                assertSame(Account.class, exception.getEntityType());
                assertSame(User.class, exception.getConflictingEntityType());
            });
        });
    }

    @TestFactory
    Stream<DynamicTest> prefixesDistinctPairs() {
        return list(map(vectors.get("prefixes")).get("distinctPairs")).stream().map(ConformanceTest::list).map(pair -> {
            String first = (String) pair.get(0);
            String second = (String) pair.get(1);
            return dynamicTest(first + " does not collide with " + second, () -> assertDoesNotThrow(() ->
                    IdoraGenerator.builder().registerEntity(User.class, first).registerEntity(Account.class, second)));
        });
    }

    private static Reason reasonFor(String reason) {
        switch (reason) {
            case "empty":
                return Reason.EMPTY;
            case "tooLong":
                return Reason.TOO_LONG;
            case "separator":
                return Reason.SEPARATOR;
            case "character":
                return Reason.CHARACTER;
            default:
                throw new IllegalArgumentException("unknown reason in test vectors: " + reason);
        }
    }

    private static String toHex(byte[] bytes) {
        StringBuilder hex = new StringBuilder();
        for (byte b : bytes) {
            hex.append(Character.forDigit((b >>> 4) & 0xF, 16)).append(Character.forDigit(b & 0xF, 16));
        }
        return hex.toString();
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> map(Object value) {
        return (Map<String, Object>) value;
    }

    @SuppressWarnings("unchecked")
    private static List<Object> list(Object value) {
        return (List<Object>) value;
    }

    /** A state whose fresh draws are recognisable and counted. */
    private static final class RecordingState extends MonotonicUlidState {
        int draws;

        @Override
        protected void drawRandom() {
            draws++;
            setRandom(0xABCD, 0x1234_5678_9ABC_DEF0L);
        }
    }
}
