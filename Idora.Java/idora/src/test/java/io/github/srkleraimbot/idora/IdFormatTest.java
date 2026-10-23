package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.support.TestEntities.Order;
import io.github.srkleraimbot.idora.support.TestEntities.Payment;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import io.github.srkleraimbot.idora.support.UlidDecoder;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.math.BigInteger;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.regex.Pattern;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The shape of a generated identifier: prefix, separator, and the alphabet, case and length of its value. */
class IdFormatTest {

    /** Prefix, one underscore, then 26 lowercase Crockford Base32 characters. */
    private static final Pattern ULID_ID = Pattern.compile("^usr_[0-9a-hjkmnp-tv-z]{26}$");

    /** Prefix, one underscore, then 32 lowercase hexadecimal characters. */
    private static final Pattern UUID_ID = Pattern.compile("^usr_[0-9a-f]{32}$");

    @Test
    void generate_withUlid_producesPrefixUnderscoreAndTwentySixBase32Characters() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);

        // Act
        String id = generator.generate(User.class);

        // Assert
        assertTrue(ULID_ID.matcher(id).matches(), id);
        assertEquals("usr_".length() + 26, id.length());
    }

    @Test
    void generate_withUuidV7_producesPrefixUnderscoreAndThirtyTwoHexCharacters() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.UUID_V7);

        // Act
        String id = generator.generate(User.class);

        // Assert
        assertTrue(UUID_ID.matcher(id).matches(), id);
        assertEquals("usr_".length() + 32, id.length());
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_forEitherStrategy_producesLowercaseAsciiOnly(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = generator(strategy);

        // Act
        String id = generator.generate(User.class);

        // Assert
        assertEquals(id.toLowerCase(Locale.ROOT), id);
        assertTrue(id.chars().allMatch(c -> c < 0x80), id);
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_forEitherStrategy_containsExactlyOneSeparator(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = generator(strategy);

        // Act
        String id = generator.generate(User.class);

        // Assert
        assertEquals(1, id.chars().filter(c -> c == '_').count());
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generate_forDifferentEntityTypes_usesEachRegisteredPrefix(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = generator(strategy);

        // Act
        String userId = generator.generate(User.class);
        String orderId = generator.generate(Order.class);

        // Assert
        assertTrue(userId.startsWith("usr_"), userId);
        assertTrue(orderId.startsWith("ord_"), orderId);
    }

    @Test
    void generate_withUlid_encodesTheCurrentTimeInTheTimestampCharacters() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);
        long before = System.currentTimeMillis();

        // Act
        String id = generator.generate(User.class);

        // Assert
        long after = System.currentTimeMillis();
        long encoded = UlidDecoder.decodeTimestamp(UlidDecoder.valueOf(id));
        assertTrue(encoded >= before && encoded <= after, encoded + " not in [" + before + ", " + after + "]");
    }

    @Test
    void generate_withUuidV7_producesAValueThatParsesAsAVersionSevenUuid() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.UUID_V7);

        // Act
        String id = generator.generate(User.class);

        // Assert
        UUID parsed = UUID.fromString(UlidDecoder.hyphenate(UlidDecoder.valueOf(id)));
        assertEquals(7, parsed.version());
        assertEquals(2, parsed.variant(), "RFC 9562 variant");
        assertEquals(UlidDecoder.valueOf(id), parsed.toString().replace("-", ""));
    }

    @Test
    void generate_withUuidV7_setsTheVersionAndVariantCharacters() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.UUID_V7);

        // Act
        String value = UlidDecoder.valueOf(generator.generate(User.class));

        // Assert: character 12 is the version nibble; character 16 carries the variant bits 10xx.
        assertEquals('7', value.charAt(12));
        assertTrue("89ab".indexOf(value.charAt(16)) >= 0, value);
    }

    @Test
    void generate_withUlid_variesTheRandomFieldWithinASingleMillisecond() {
        // Arrange
        IdoraGenerator generator = generator(IdGenerationStrategy.ULID);

        // Act: a hundred identifiers take microseconds, so most adjacent pairs share a millisecond.
        List<String> values = new ArrayList<>();
        for (int i = 0; i < 100; i++) {
            values.add(UlidDecoder.valueOf(generator.generate(User.class)));
        }

        // Assert
        int sameMillisecondPairs = 0;
        for (int i = 1; i < values.size(); i++) {
            String first = values.get(i - 1);
            String second = values.get(i);
            if (UlidDecoder.decodeTimestamp(first) == UlidDecoder.decodeTimestamp(second)) {
                sameMillisecondPairs++;
                BigInteger before = UlidDecoder.decodeRandom(first);
                BigInteger after = UlidDecoder.decodeRandom(second);
                assertNotEquals(before, after);
            }
        }
        assertTrue(sameMillisecondPairs > 0);
    }

    @Test
    void generate_withoutChoosingAStrategy_defaultsToUlid() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, "usr").build();

        // Act
        String id = generator.generate(User.class);

        // Assert
        assertTrue(ULID_ID.matcher(id).matches(), id);
        assertEquals(IdGenerationStrategy.ULID, generator.strategy());
    }

    @Test
    void generate_forARecordEntityType_usesItsRegisteredPrefix() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(Payment.class, "pay").build();

        // Act
        String id = generator.generate(Payment.class);

        // Assert
        assertTrue(id.startsWith("pay_"), id);
    }

    @Test
    void generate_withTheLongestPrefix_producesTheLongestIdentifiers() {
        // Arrange
        IdoraGenerator ulid = IdoraGenerator.create();
        IdoraGenerator uuid = IdoraGenerator.builder().useUuidV7().build();
        String prefix = "abcdefghijklmnop";

        // Act
        String ulidId = ulid.generate(prefix);
        String uuidId = uuid.generate(prefix);

        // Assert: 16 + 1 + 26 and 16 + 1 + 32, the column sizes the README promises.
        assertEquals(43, ulidId.length());
        assertEquals(49, uuidId.length());
        assertFalse(ulidId.substring(17).contains("_"));
    }

    private static IdoraGenerator generator(IdGenerationStrategy strategy) {
        return IdoraGenerator.builder()
                .strategy(strategy)
                .registerEntity(User.class, "usr")
                .registerEntity(Order.class, "ord")
                .build();
    }
}
