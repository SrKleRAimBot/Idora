package io.github.srkleraimbot.idora;

import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/**
 * The registry built once by {@link IdoraGenerator.Builder#build()} and read for the lifetime of
 * the generator.
 *
 * <p>Prefixes are held twice: as written, for callers reading the registry, and as the ASCII bytes
 * of the prefix with the separator already appended, so that generating an identifier for a
 * registered type is one map lookup and one array copy.
 */
final class DefaultEntityPrefixRegistry implements EntityPrefixRegistry {

    private final Map<Class<?>, String> registrations;

    /**
     * Private and never mutated after construction, so a plain {@link HashMap} is safe to share
     * (it is published through a final field) and skips the indirection of an unmodifiable wrapper
     * on the hot path. {@link Class#hashCode()} is the identity hash, so lookups are cheap.
     */
    private final HashMap<Class<?>, byte[]> prefixBytesWithSeparator;

    DefaultEntityPrefixRegistry(Map<Class<?>, String> source) {
        this.registrations = Collections.unmodifiableMap(new LinkedHashMap<>(source));
        this.prefixBytesWithSeparator = new HashMap<>(Math.max(16, source.size() * 2));
        source.forEach((type, prefix) -> prefixBytesWithSeparator.put(
                type, (prefix + EntityPrefixValidator.SEPARATOR).getBytes(StandardCharsets.US_ASCII)));
    }

    @Override
    public String getPrefix(Class<?> entityType) {
        String prefix = registrations.get(Objects.requireNonNull(entityType, "entityType"));
        if (prefix == null) {
            throw EntityPrefixNotRegisteredException.forType(entityType);
        }
        return prefix;
    }

    @Override
    public Optional<String> findPrefix(Class<?> entityType) {
        return Optional.ofNullable(registrations.get(Objects.requireNonNull(entityType, "entityType")));
    }

    @Override
    public Map<Class<?>, String> registrations() {
        return registrations;
    }

    /**
     * Returns the registered prefix plus separator as ASCII bytes, ready to copy into an identifier.
     * The caller must not modify the array.
     *
     * @throws NullPointerException if {@code entityType} is {@code null}
     * @throws EntityPrefixNotRegisteredException if {@code entityType} is not registered
     */
    byte[] prefixBytesWithSeparator(Class<?> entityType) {
        byte[] prefix = prefixBytesWithSeparator.get(entityType);
        if (prefix == null) {
            // Only now pay for the null check: a null key simply misses the map above.
            throw EntityPrefixNotRegisteredException.forType(Objects.requireNonNull(entityType, "entityType"));
        }
        return prefix;
    }

    @Override
    public String toString() {
        return "EntityPrefixRegistry" + registrations;
    }
}
