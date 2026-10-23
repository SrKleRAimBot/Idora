package io.github.srkleraimbot.idora;

import java.util.Map;
import java.util.Optional;

/**
 * The immutable set of entity-type-to-prefix mappings a generator was built with.
 *
 * <p>Obtain it from {@link IdoraGenerator#registry()} when you need the prefix itself rather than
 * a whole identifier: to check that an incoming identifier belongs to the entity a route expects,
 * say, or to build a filter over stored identifiers.
 *
 * <pre>{@code
 * boolean isOrderId = id.startsWith(ids.registry().getPrefix(Order.class) + "_");
 * }</pre>
 *
 * <p>The registry never changes once built and is safe to use from any thread. Prefixes are
 * returned exactly as registered, without the trailing separator.
 */
public interface EntityPrefixRegistry {

    /**
     * Returns the prefix registered for {@code entityType}.
     *
     * @param entityType the entity type to look up
     * @return the registered prefix, without the trailing separator
     * @throws NullPointerException if {@code entityType} is {@code null}
     * @throws EntityPrefixNotRegisteredException if {@code entityType} has no registered prefix
     */
    String getPrefix(Class<?> entityType);

    /**
     * Looks up the prefix registered for {@code entityType}, without failing if there is none.
     *
     * @param entityType the entity type to look up
     * @return the registered prefix, or empty if {@code entityType} is not registered
     * @throws NullPointerException if {@code entityType} is {@code null}
     */
    Optional<String> findPrefix(Class<?> entityType);

    /**
     * Returns every registered entity type and its prefix, in the order they were registered.
     *
     * @return an unmodifiable map; empty if nothing was registered
     */
    Map<Class<?>, String> registrations();
}
