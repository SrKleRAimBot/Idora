package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.support.TestEntities.Account;
import io.github.srkleraimbot.idora.support.TestEntities.Auditable;
import io.github.srkleraimbot.idora.support.TestEntities.Invoice;
import io.github.srkleraimbot.idora.support.TestEntities.Order;
import io.github.srkleraimbot.idora.support.TestEntities.UnregisteredEntity;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.util.List;
import java.util.Locale;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotSame;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Registering prefixes explicitly, configuring the builder, and the collision rules that keep a prefix meaningful. */
class EntityRegistrationTest {

    @Test
    void registerEntity_forOneType_makesGenerateUseThatPrefix() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, "usr").build();

        // Act
        String id = generator.generate(User.class);

        // Assert
        assertTrue(id.startsWith("usr_"), id);
    }

    @Test
    void registerEntity_forSeveralTypes_keepsEachPrefixWithItsOwnType() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder()
                .registerEntity(User.class, "usr")
                .registerEntity(Order.class, "ord")
                .registerEntity(Invoice.class, "inv")
                .registerEntity(Account.class, "acct")
                .registerEntity(Auditable.class, "aud")
                .build();

        // Act
        List<String> ids = List.of(
                generator.generate(User.class),
                generator.generate(Order.class),
                generator.generate(Invoice.class),
                generator.generate(Account.class),
                generator.generate(Auditable.class));

        // Assert
        assertTrue(ids.get(0).startsWith("usr_"));
        assertTrue(ids.get(1).startsWith("ord_"));
        assertTrue(ids.get(2).startsWith("inv_"));
        assertTrue(ids.get(3).startsWith("acct_"));
        assertTrue(ids.get(4).startsWith("aud_"));
    }

    @Test
    void generate_forAnUnregisteredType_throwsAndNamesTheType() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, "usr").build();

        // Act
        EntityPrefixNotRegisteredException exception =
                assertThrows(EntityPrefixNotRegisteredException.class, () -> generator.generate(UnregisteredEntity.class));

        // Assert: and says how to fix it.
        assertSame(UnregisteredEntity.class, exception.getEntityType());
        assertTrue(exception.getMessage().contains(UnregisteredEntity.class.getName()), exception.getMessage());
        assertTrue(exception.getMessage().contains("registerEntity(UnregisteredEntity.class"), exception.getMessage());
        assertTrue(exception.getMessage().contains("generate(String)"), exception.getMessage());
    }

    @Test
    void generate_forASubclassOfARegisteredType_doesNotBorrowItsPrefix() {
        // Arrange: a registration is for exactly one type.
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(Object.class, "obj").build();

        // Act & Assert
        assertThrows(EntityPrefixNotRegisteredException.class, () -> generator.generate(User.class));
    }

    @Test
    void generate_whenNothingIsRegistered_throwsForATypeButAcceptsAnAdHocPrefix() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.create();

        // Act
        IdoraException exception = assertThrows(EntityPrefixNotRegisteredException.class, () -> generator.generate(User.class));
        String adHoc = generator.generate("tmp");

        // Assert
        assertInstanceOf(IdoraException.class, exception);
        assertEquals("tmp_".length() + 26, adHoc.length());
    }

    @Test
    void generate_withANullType_throwsNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, "usr").build();

        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> generator.generate((Class<?>) null));

        // Assert
        assertEquals("entityType", exception.getMessage());
    }

    @Test
    void registerEntity_withAPrefixAlreadyGivenToAnotherType_throwsAndNamesBothTypes() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "acc");

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(Account.class, "acc"));

        // Assert
        assertEquals("acc", exception.getPrefix());
        assertSame(Account.class, exception.getEntityType());
        assertSame(User.class, exception.getConflictingEntityType());
        assertTrue(exception.getMessage().contains(User.class.getName()), exception.getMessage());
        assertTrue(exception.getMessage().contains(Account.class.getName()), exception.getMessage());
        assertTrue(exception.getMessage().contains("ignoring case"), exception.getMessage());
    }

    @ParameterizedTest
    @CsvSource({"usr, USR", "USR, usr", "Usr, uSR"})
    void registerEntity_withAPrefixDifferingOnlyByCase_isADuplicate(String firstPrefix, String secondPrefix) {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, firstPrefix);

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(Account.class, secondPrefix));

        // Assert
        assertSame(User.class, exception.getConflictingEntityType());
    }

    @Test
    void registerEntity_underATurkishDefaultLocale_stillFoldsCaseByAsciiRules() {
        // Arrange: in Turkish, "I".toLowerCase() is a dotless "ı", so a locale-sensitive fold would
        // let "INV" and "inv" coexist on Turkish machines only.
        Locale original = Locale.getDefault();
        Locale.setDefault(Locale.forLanguageTag("tr-TR"));
        try {
            IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "inv");

            // Act
            DuplicateEntityPrefixException exception =
                    assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(Invoice.class, "INV"));

            // Assert
            assertSame(User.class, exception.getConflictingEntityType());
        } finally {
            Locale.setDefault(original);
        }
    }

    @Test
    void registerEntity_forTheSameTypeTwiceWithDifferentPrefixes_throwsSayingWhichPrefixesDisagree() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "usr");

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(User.class, "user"));

        // Assert
        assertSame(User.class, exception.getEntityType());
        assertSame(User.class, exception.getConflictingEntityType());
        assertEquals("user", exception.getPrefix());
        assertTrue(exception.getMessage().contains("'usr'"), exception.getMessage());
        assertTrue(exception.getMessage().contains("'user'"), exception.getMessage());
        assertTrue(exception.getMessage().contains("only one prefix"), exception.getMessage());
    }

    @Test
    void registerEntity_forTheSameTypeTwiceWithTheSamePrefix_throwsSayingItWasRegisteredTwice() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "usr");

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(User.class, "usr"));

        // Assert: a repeated registration is a copy-paste slip worth reporting, not a no-op.
        assertSame(User.class, exception.getEntityType());
        assertTrue(exception.getMessage().contains("registered a second time"), exception.getMessage());
        assertFalse(exception.getMessage().contains("cannot also be registered with"), exception.getMessage());
    }

    @Test
    void registerEntity_whenTheTypeAndThePrefixAreBothTaken_reportsTheTypeFirst() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder()
                .registerEntity(User.class, "usr")
                .registerEntity(Account.class, "acc");

        // Act
        DuplicateEntityPrefixException exception =
                assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(User.class, "acc"));

        // Assert
        assertSame(User.class, exception.getConflictingEntityType());
        assertTrue(exception.getMessage().contains("only one prefix"), exception.getMessage());
    }

    @Test
    void registerEntity_withANullType_throwsNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> builder.registerEntity(null, "inv"));

        // Assert
        assertEquals("entityType", exception.getMessage());
    }

    @Test
    void builder_returnsItselfFromEveryConfigurationMethodSoCallsChain() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act & Assert
        assertSame(builder, builder.registerEntity(User.class, "usr"));
        assertSame(builder, builder.useUuidV7());
        assertSame(builder, builder.useUlid());
        assertSame(builder, builder.strategy(IdGenerationStrategy.ULID));
        assertSame(builder, builder.registerEntities());
    }

    @Test
    void builder_withANullStrategy_throwsNullPointerExceptionNamingTheParameter() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder();

        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> builder.strategy(null));

        // Assert
        assertEquals("strategy", exception.getMessage());
    }

    @Test
    void builder_lastStrategyChosenWins() {
        // Arrange & Act
        IdoraGenerator uuid = IdoraGenerator.builder().useUlid().useUuidV7().build();
        IdoraGenerator ulid = IdoraGenerator.builder().useUuidV7().useUlid().build();

        // Assert
        assertEquals(IdGenerationStrategy.UUID_V7, uuid.strategy());
        assertEquals("tmp_".length() + 32, uuid.generate("tmp").length());
        assertEquals(IdGenerationStrategy.ULID, ulid.strategy());
        assertEquals("tmp_".length() + 26, ulid.generate("tmp").length());
    }

    @Test
    void registerEntity_afterAFailedRegistration_leavesTheEarlierRegistrationsIntact() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "usr");

        // Act
        assertThrows(DuplicateEntityPrefixException.class, () -> builder.registerEntity(Account.class, "usr"));
        assertThrows(InvalidEntityPrefixException.class, () -> builder.registerEntity(Account.class, "a_b"));
        builder.registerEntity(Account.class, "acc");

        // Assert
        assertEquals(Map.of(User.class, "usr", Account.class, "acc"), builder.build().registry().registrations());
    }

    @Test
    void registerEntity_keepsThePrefixExactlyAsWritten() {
        // Arrange & Act
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(User.class, "USR").build();

        // Assert: collisions ignore case, but the prefix itself is never rewritten.
        assertEquals("USR", generator.registry().getPrefix(User.class));
        assertTrue(generator.generate(User.class).startsWith("USR_"));
    }

    @Test
    void build_takesASnapshotThatLaterRegistrationsDoNotChange() {
        // Arrange
        IdoraGenerator.Builder builder = IdoraGenerator.builder().registerEntity(User.class, "usr");
        IdoraGenerator first = builder.build();

        // Act
        builder.registerEntity(Order.class, "ord").useUuidV7();
        IdoraGenerator second = builder.build();

        // Assert
        assertNotSame(first, second);
        assertEquals(Map.of(User.class, "usr"), first.registry().registrations());
        assertEquals(IdGenerationStrategy.ULID, first.strategy());
        assertThrows(EntityPrefixNotRegisteredException.class, () -> first.generate(Order.class));
        assertTrue(second.generate(Order.class).startsWith("ord_"));
    }

    @Test
    void toString_describesTheConfigurationForDiagnostics() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().useUuidV7().registerEntity(User.class, "usr").build();

        // Act
        String description = generator.toString();

        // Assert
        assertTrue(description.contains("UUID_V7"), description);
        assertTrue(description.contains("usr"), description);
    }
}
