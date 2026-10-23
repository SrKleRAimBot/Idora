package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.support.TestEntities.Account;
import io.github.srkleraimbot.idora.support.TestEntities.Order;
import io.github.srkleraimbot.idora.support.TestEntities.UnregisteredEntity;
import io.github.srkleraimbot.idora.support.TestEntities.User;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;
import java.util.Optional;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** The read-only registry, for callers that need the prefix itself rather than a whole identifier. */
class EntityPrefixRegistryTest {

    private final EntityPrefixRegistry registry = IdoraGenerator.builder()
            .registerEntity(User.class, "usr")
            .registerEntity(Order.class, "ord")
            .build()
            .registry();

    @Test
    void getPrefix_forARegisteredType_returnsThePrefixWithoutTheSeparator() {
        // Act
        String prefix = registry.getPrefix(User.class);

        // Assert
        assertEquals("usr", prefix);
    }

    @Test
    void getPrefix_forAnUnregisteredType_throws() {
        // Act
        EntityPrefixNotRegisteredException exception =
                assertThrows(EntityPrefixNotRegisteredException.class, () -> registry.getPrefix(UnregisteredEntity.class));

        // Assert
        assertSame(UnregisteredEntity.class, exception.getEntityType());
    }

    @Test
    void getPrefix_withANullType_throwsNullPointerExceptionNamingTheParameter() {
        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> registry.getPrefix(null));

        // Assert
        assertEquals("entityType", exception.getMessage());
    }

    @Test
    void findPrefix_forARegisteredType_returnsThePrefix() {
        // Act
        Optional<String> prefix = registry.findPrefix(Order.class);

        // Assert
        assertEquals(Optional.of("ord"), prefix);
    }

    @Test
    void findPrefix_forAnUnregisteredType_returnsEmpty() {
        // Act
        Optional<String> prefix = registry.findPrefix(UnregisteredEntity.class);

        // Assert
        assertEquals(Optional.empty(), prefix);
    }

    @Test
    void findPrefix_withANullType_throwsNullPointerExceptionNamingTheParameter() {
        // Act
        NullPointerException exception = assertThrows(NullPointerException.class, () -> registry.findPrefix(null));

        // Assert
        assertEquals("entityType", exception.getMessage());
    }

    @Test
    void registrations_exposesEveryRegisteredTypeAndPrefixInRegistrationOrder() {
        // Act
        Map<Class<?>, String> registrations = registry.registrations();

        // Assert
        assertEquals(Map.of(User.class, "usr", Order.class, "ord"), registrations);
        assertEquals(List.of(User.class, Order.class), List.copyOf(registrations.keySet()));
    }

    @Test
    void registrations_cannotBeModified() {
        // Act & Assert
        Map<Class<?>, String> registrations = registry.registrations();
        assertThrows(UnsupportedOperationException.class, () -> registrations.put(Account.class, "acc"));
        assertThrows(UnsupportedOperationException.class, registrations::clear);
    }

    @Test
    void registrations_whenNothingWasRegistered_isEmpty() {
        // Act
        Map<Class<?>, String> registrations = IdoraGenerator.create().registry().registrations();

        // Assert
        assertTrue(registrations.isEmpty());
    }

    @Test
    void registrations_matchTheIdentifiersTheGeneratorActuallyProduces() {
        // Arrange
        IdoraGenerator generator = IdoraGenerator.builder().registerEntity(Order.class, "ord").build();

        // Act
        String id = generator.generate(Order.class);

        // Assert: the round trip a consumer relies on to check an identifier belongs to an entity.
        assertTrue(id.startsWith(generator.registry().getPrefix(Order.class) + "_"), id);
    }
}
