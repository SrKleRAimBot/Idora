package io.github.srkleraimbot.idora;

import org.junit.jupiter.api.Test;

import java.util.Arrays;
import java.util.Set;
import java.util.stream.Collectors;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Pins the exception hierarchy that callers compile against. {@link IdoraException} is sealed and
 * 1.0 documented a {@code switch} over its three subclasses as exhaustive, so its permitted set is
 * part of the API: a new subclass would break that code in a minor release.
 */
class ExceptionHierarchyTest {

    @Test
    void idoraException_permitsExactlyTheThreeSubclassesOf1_0() {
        // Act
        Set<Class<?>> permitted = Arrays.stream(IdoraException.class.getPermittedSubclasses()).collect(Collectors.toSet());

        // Assert
        assertTrue(IdoraException.class.isSealed());
        assertEquals(
                Set.of(InvalidEntityPrefixException.class, DuplicateEntityPrefixException.class, EntityPrefixNotRegisteredException.class),
                permitted);
    }

    @Test
    void invalidIdException_isAnIllegalArgumentException_outsideTheSealedHierarchy() {
        // Act
        InvalidIdException exception = assertThrows(InvalidIdException.class, () -> IdoraId.parse("not an identifier"));

        // Assert
        assertInstanceOf(IllegalArgumentException.class, exception);
        assertFalse(IdoraException.class.isAssignableFrom(InvalidIdException.class));
    }
}
