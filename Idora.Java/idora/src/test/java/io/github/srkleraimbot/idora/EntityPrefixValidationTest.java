package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.InvalidEntityPrefixException.Reason;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

import java.util.Optional;
import java.util.OptionalInt;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The rules a prefix must satisfy, applied identically at registration and on every ad-hoc generate. */
class EntityPrefixValidationTest {

    static Stream<String> invalidPrefixes() {
        return Stream.of(
                "",                                     // empty
                " ",                                    // whitespace is not a letter or digit
                "   ",
                "us r",                                 // embedded space
                "us_r",                                 // contains the separator
                "_usr",                                 // leading separator
                "usr_",                                 // trailing separator
                "usr-1",                                // hyphen
                "usr.1",                                // full stop
                "user!",                                // punctuation
                "usér",                            // non-ASCII letter
                "ユーザ",                   // non-Latin script
                "١٢",                         // non-ASCII digits
                "😀",                         // emoji, a surrogate pair
                "usr\n",                                // control character
                "İnv",                             // capital I with dot above
                "12345678901234567",                    // 17 characters, one over the limit
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }

    static Stream<String> validPrefixes() {
        return Stream.of(
                "a",                  // shortest possible
                "1",                  // a digit alone is fine
                "usr",                // the common case
                "usr1",               // letters and digits mixed
                "1usr",               // may start with a digit
                "USR",                // uppercase is allowed, though lowercase reads better
                "UsR",
                "1234567890123456");  // exactly 16 characters, the limit
    }

    @ParameterizedTest
    @MethodSource("invalidPrefixes")
    void registerEntity_withAnInvalidPrefix_throwsAndReportsThePrefix(String prefix) {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, prefix));

        // Assert
        assertEquals(prefix, exception.getPrefix());
        assertInstanceOf(IdoraException.class, exception);
        assertEquals(Optional.empty(), exception.getEntityType());
    }

    @ParameterizedTest
    @MethodSource("validPrefixes")
    void registerEntity_withAValidPrefix_succeeds(String prefix) {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        IdoraGenerator generator = builder.registerEntity(User.class, prefix).build();

        // Assert
        assertEquals(prefix, generator.registry().getPrefix(User.class));
    }

    @ParameterizedTest
    @MethodSource("invalidPrefixes")
    void generate_withAnInvalidAdHocPrefix_throws(String prefix) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.create();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> generator.generate(prefix));

        // Assert
        assertEquals(prefix, exception.getPrefix());
    }

    @ParameterizedTest
    @MethodSource("validPrefixes")
    void generate_withAValidAdHocPrefix_producesAnIdentifierWithThatPrefix(String prefix) {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.create();

        // Act
        String id = generator.generate(prefix);

        // Assert
        assertTrue(id.startsWith(prefix + "_"), id);
        assertEquals(prefix.length() + 1 + 26, id.length());
    }

    @Test
    void registerEntity_withANullPrefix_throwsNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        NullPointerException exception =
                assertThrows(NullPointerException.class, () -> builder.registerEntity(User.class, null));

        // Assert
        assertEquals("prefix", exception.getMessage());
    }

    @Test
    void generate_withANullAdHocPrefix_throwsNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.create();

        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> generator.generate((String) null));

        // Assert
        assertEquals("entityPrefix", exception.getMessage());
    }

    @Test
    void validate_reportsTheRulesInSpecificationOrder() {
        // Arrange: over-long and full of bad characters, so only the order decides what is reported.
        IdoraGenerator generator = IdoraGenerator.create();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> generator.generate("!!!!!!!!!!!!!!!!!!!!"));

        // Assert
        assertEquals(Reason.TOO_LONG, exception.getReason());
        assertEquals(OptionalInt.empty(), exception.getIndex());
    }

    @Test
    void registerEntity_withAnEmptyPrefix_saysWhatAValidOneLooksLike() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, ""));

        // Assert
        assertEquals(Reason.EMPTY, exception.getReason());
        assertTrue(exception.getMessage().contains("cannot be empty"), exception.getMessage());
        assertTrue(exception.getMessage().contains("\"usr\""), exception.getMessage());
    }

    @Test
    void registerEntity_withAPrefixContainingTheSeparator_explainsWhyItIsRejected() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, "user_account"));

        // Assert
        assertEquals(Reason.SEPARATOR, exception.getReason());
        assertEquals(OptionalInt.of(4), exception.getIndex());
        assertTrue(exception.getMessage().contains("separator"), exception.getMessage());
        assertTrue(exception.getMessage().contains("index 4"), exception.getMessage());
    }

    @Test
    void registerEntity_withABadCharacter_reportsTheCharacterAndItsIndex() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, "usr!"));

        // Assert
        assertEquals(Reason.CHARACTER, exception.getReason());
        assertEquals(OptionalInt.of(3), exception.getIndex());
        assertTrue(exception.getMessage().contains("'!'"), exception.getMessage());
        assertTrue(exception.getMessage().contains("index 3"), exception.getMessage());
        assertTrue(exception.getMessage().contains("only ASCII letters and digits"), exception.getMessage());
    }

    @Test
    void registerEntity_withAControlCharacter_escapesItInTheMessage() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, "usr\n"));

        // Assert: the message names the character without breaking the log line it lands in.
        assertTrue(exception.getMessage().contains("U+000A"), exception.getMessage());
        assertTrue(exception.getMessage().contains("'\\u000a'"), exception.getMessage());
    }

    @Test
    void registerEntity_withAnEmoji_reportsTheWholeCodePointAtItsFirstIndex() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, "a😀"));

        // Assert
        assertEquals(OptionalInt.of(1), exception.getIndex());
        assertTrue(exception.getMessage().contains("U+1F600"), exception.getMessage());
    }

    @Test
    void registerEntity_withAnOverlongPrefix_reportsBothLengths() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();
        String prefix = "a".repeat(17);

        // Act
        InvalidEntityPrefixException exception =
                assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(User.class, prefix));

        // Assert
        assertEquals(Reason.TOO_LONG, exception.getReason());
        assertTrue(exception.getMessage().contains("17"), exception.getMessage());
        assertTrue(exception.getMessage().contains("16"), exception.getMessage());
    }
}
