package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.InvalidIdException.Reason;
import io.github.srkleraimbot.idora.support.TestEntities.Order;
import io.github.srkleraimbot.idora.support.TestEntities.UnregisteredEntity;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.time.Instant;
import java.util.Optional;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The generator's registry-aware reading methods: {@code parseId}, {@code isIdFor}, bounds and {@code generateId}. */
class RegistryAwareParsingTest {

    private static final String USER_ID = "usr_01k226e0g0008j4ct4ank7f24s";

    private final IdoraGenerator ids = IdoraGenerator.builder()
            .registerEntity(User.class, "usr")
            .registerEntity(Order.class, "ord")
            .build();

    @Test
    void parseId_anIdentifierForTheType_returnsIt() {
        // Act
        IdoraId id = ids.parseId(User.class, USER_ID);

        // Assert
        assertEquals(USER_ID, id.toString());
    }

    @Test
    void parseId_anIdentifierForAnotherType_isAPrefixMismatchNamingTheRegisteredPrefix() {
        // Act
        InvalidIdException exception = assertThrows(InvalidIdException.class, () -> ids.parseId(Order.class, USER_ID));

        // Assert
        assertEquals(Reason.PREFIX_MISMATCH, exception.getReason());
        assertEquals(Optional.of("ord"), exception.getExpectedPrefix());
    }

    @Test
    void parseId_acceptsEitherFormat_whateverTheGeneratorStrategy() {
        // Act & Assert: identifiers minted before a change of strategy still parse.
        assertEquals(IdGenerationStrategy.UUID_V7,
                ids.parseId(User.class, "usr_0198846702007011a233445566778899").format());
    }

    @Test
    void parseId_forAnUnregisteredType_failsWithEntityPrefixNotRegistered() {
        // Act
        EntityPrefixNotRegisteredException exception = assertThrows(EntityPrefixNotRegisteredException.class,
                () -> ids.parseId(UnregisteredEntity.class, "not even an identifier"));

        // Assert: the identifier is not at fault, so it is not an InvalidIdException.
        assertSame(UnregisteredEntity.class, exception.getEntityType());
        assertThrows(EntityPrefixNotRegisteredException.class, () -> ids.tryParseId(UnregisteredEntity.class, USER_ID));
        assertThrows(EntityPrefixNotRegisteredException.class, () -> ids.isIdFor(UnregisteredEntity.class, USER_ID));
        assertThrows(EntityPrefixNotRegisteredException.class,
                () -> ids.lowerBound(UnregisteredEntity.class, Instant.EPOCH));
    }

    @Test
    void parseId_withNulls_throwsNullPointerExceptionNamingTheParameter() {
        // Act & Assert
        assertEquals("entityType", assertThrows(NullPointerException.class, () -> ids.parseId(null, USER_ID)).getMessage());
        assertEquals("text", assertThrows(NullPointerException.class, () -> ids.parseId(User.class, null)).getMessage());
        assertEquals("text", assertThrows(NullPointerException.class, () -> ids.isIdFor(User.class, null)).getMessage());
    }

    @Test
    void tryParseId_returnsTheIdentifierOrEmpty() {
        // Act & Assert
        assertEquals(Optional.of(IdoraId.parse(USER_ID)), ids.tryParseId(User.class, USER_ID));
        assertEquals(Optional.empty(), ids.tryParseId(Order.class, USER_ID));
        assertEquals(Optional.empty(), ids.tryParseId(User.class, "usr_nope"));
    }

    @Test
    void isIdFor_isTrueOnlyForAWellFormedIdentifierWithTheRegisteredPrefix() {
        // Act & Assert
        assertTrue(ids.isIdFor(User.class, USER_ID));
        assertFalse(ids.isIdFor(Order.class, USER_ID));
        assertFalse(ids.isIdFor(User.class, "USR_01k226e0g0008j4ct4ank7f24s"));
        assertFalse(ids.isIdFor(User.class, "usr_"));
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void bounds_useTheRegisteredPrefixAndTheGeneratorStrategy(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().strategy(strategy).registerEntity(User.class, "usr").build();
        Instant instant = Instant.ofEpochMilli(1754568000000L);

        // Act
        IdoraId lower = generator.lowerBound(User.class, instant);
        IdoraId upper = generator.upperBound(User.class, instant);

        // Assert
        assertEquals(IdoraId.lowerBound("usr", instant, strategy), lower);
        assertEquals(IdoraId.upperBound("usr", instant, strategy), upper);
    }

    @ParameterizedTest
    @EnumSource(IdGenerationStrategy.class)
    void generateId_returnsAParsedIdentifierForTheType(IdGenerationStrategy strategy) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().strategy(strategy).registerEntity(User.class, "usr").build();

        // Act
        IdoraId registered = generator.generateId(User.class);
        IdoraId adHoc = generator.generateId("tmp");

        // Assert
        assertEquals("usr", registered.prefix());
        assertEquals(strategy, registered.format());
        assertEquals("tmp", adHoc.prefix());
        assertTrue(registered.compareTimestamp(adHoc) <= 0);
        assertEquals(registered, IdoraId.parse(registered.toString()));
    }

    @Test
    void generateId_failsAsGenerateDoes() {
        // Act & Assert
        assertThrows(EntityPrefixNotRegisteredException.class, () -> ids.generateId(UnregisteredEntity.class));
        assertThrows(InvalidEntityPrefixException.class, () -> ids.generateId("bad_prefix"));
        assertThrows(NullPointerException.class, () -> ids.generateId((Class<?>) null));
        assertThrows(NullPointerException.class, () -> ids.generateId((String) null));
    }

    @Test
    void validatePrefix_returnsAValidPrefixUnchanged() {
        // Act & Assert
        assertEquals("usr", IdoraGenerator.validatePrefix("usr"));
        assertEquals("A1b2C3d4E5f6G7h8", IdoraGenerator.validatePrefix("A1b2C3d4E5f6G7h8"));
    }

    @Test
    void validatePrefix_rejectsAnInvalidPrefixAsRegistrationWould() {
        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> IdoraGenerator.validatePrefix("us_r"));

        // Assert
        assertEquals(InvalidEntityPrefixException.Reason.SEPARATOR, exception.getReason());
        assertEquals("prefix", assertThrows(NullPointerException.class, () -> IdoraGenerator.validatePrefix(null)).getMessage());
    }
}
