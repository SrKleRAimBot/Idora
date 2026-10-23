package io.github.srkleraimbot.idora;

import io.github.srkleraimbot.idora.internal.PackageScanner;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Generates prefixed, time-ordered identifiers such as {@code usr_01k2f9v8ta7m3x0qbrn4dzeh5c}.
 *
 * <pre>{@code
 * IdoraGenerator ids = IdoraGenerator.builder()
 *     .registerEntity(User.class, "usr")
 *     .registerEntitiesFromPackageOf(Order.class)   // every @EntityPrefix type in that package
 *     .build();
 *
 * ids.generate(User.class);   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
 * ids.generate("tmp");        // "tmp_01k2f9v8tb9p5r2xnc7fkw3m0d"
 * }</pre>
 *
 * <p>Every identifier is {@code <prefix>_<value>}. The prefix says what the identifier belongs to,
 * so a value in a log line or a support ticket is self-describing. The value is a ULID (the
 * default) or a UUIDv7; both begin with a millisecond timestamp, so identifiers sort into creation
 * order as plain strings and rows keyed by them land at the end of an index rather than scattering
 * through it.
 *
 * <p>A generator is immutable and safe to share across any number of platform or virtual threads.
 * Build one at start-up and keep it, typically as a singleton in whatever container you use.
 * Generation takes no process-wide lock; see the README for the exact scope of ULID monotonicity.
 */
public interface IdoraGenerator {

    /**
     * Generates an identifier for a registered entity type, using the prefix it was registered with.
     *
     * <p>Prefer this to {@link #generate(String)}. The prefix lives in one place, your start-up
     * configuration, rather than as a string literal at every call site, and a type you forgot to
     * register fails loudly instead of quietly producing a wrong prefix.
     *
     * <pre>{@code
     * ids.generate(User.class);   // "usr_01k2f9v8ta7m3x0qbrn4dzeh5c"
     * }</pre>
     *
     * @param entityType the entity type; it must have been registered while building the generator
     * @return a new identifier; never {@code null} or empty
     * @throws NullPointerException if {@code entityType} is {@code null}
     * @throws EntityPrefixNotRegisteredException if {@code entityType} has no registered prefix
     */
    String generate(Class<?> entityType);

    /**
     * Generates an identifier with a prefix supplied at the call site, for prefixes known only at
     * run time: a plugin's own types, or a prefix read from configuration.
     *
     * <p>The prefix is validated on every call, which costs a few nanoseconds. For types known at
     * compile time, register them and use {@link #generate(Class)}, which validates once at start-up.
     *
     * <pre>{@code
     * ids.generate("tmp");   // "tmp_01k2f9v8ta7m3x0qbrn4dzeh5c"
     * }</pre>
     *
     * @param entityPrefix 1 to 16 ASCII letters or digits, without the trailing underscore
     * @return a new identifier beginning with {@code entityPrefix} and an underscore
     * @throws NullPointerException if {@code entityPrefix} is {@code null}
     * @throws InvalidEntityPrefixException if {@code entityPrefix} is empty, longer than 16
     *     characters, or contains anything other than an ASCII letter or digit
     */
    String generate(String entityPrefix);

    /**
     * Returns the read-only registry of entity types and prefixes this generator was built with.
     *
     * @return the registry; never {@code null}
     */
    EntityPrefixRegistry registry();

    /**
     * Returns the value format this generator produces.
     *
     * @return the strategy; never {@code null}
     */
    IdGenerationStrategy strategy();

    /**
     * Starts configuring a generator. The defaults are ULID values and no registered entity types.
     *
     * @return a new builder
     */
    static Builder builder() {
        return new Builder();
    }

    /**
     * Creates a generator with the defaults: ULID values and no registered entity types, so only
     * {@link #generate(String)} is usable. Equivalent to {@code builder().build()}.
     *
     * @return a new generator
     */
    static IdoraGenerator create() {
        return builder().build();
    }

    /**
     * Configures an {@link IdoraGenerator}: which value format to use, and which prefix belongs to
     * which entity type.
     *
     * <p>Every registration is validated the moment it is made, so a mistake throws on the line that
     * caused it, at start-up, rather than surfacing later as a puzzling identifier. A failed call
     * leaves the registrations made before it intact.
     *
     * <p>Prefixes can be registered explicitly with {@link #registerEntity(Class, String)}, or
     * declared on the type with {@link EntityPrefix} and collected by {@link #registerEntities} or
     * {@link #registerEntitiesFromPackageOf(Class)}. The styles mix freely, and any prefix or type
     * claimed twice, by whatever combination, fails with a {@link DuplicateEntityPrefixException}.
     *
     * <p>A builder is not thread-safe; configure it on one thread. {@link #build()} takes a snapshot,
     * so the builder may be reused or discarded afterwards without affecting the generator.
     */
    final class Builder {

        private IdGenerationStrategy strategy = IdGenerationStrategy.ULID;

        /** Registration order is kept so that {@link EntityPrefixRegistry#registrations()} reports it. */
        private final Map<Class<?>, String> prefixesByEntityType = new LinkedHashMap<>();

        /** The reverse lookup used to detect collisions, keyed by the ASCII-case-folded prefix. */
        private final Map<String, Class<?>> entityTypesByFoldedPrefix = new HashMap<>();

        private Builder() {
        }

        /**
         * Sets the value format. The default is {@link IdGenerationStrategy#ULID}.
         *
         * @param strategy the value format
         * @return this builder
         * @throws NullPointerException if {@code strategy} is {@code null}
         */
        public Builder strategy(IdGenerationStrategy strategy) {
            this.strategy = Objects.requireNonNull(strategy, "strategy");
            return this;
        }

        /**
         * Uses a 26-character lowercase ULID as the value. This is already the default; call it when
         * you would rather the choice were written down than implied.
         *
         * @return this builder
         */
        public Builder useUlid() {
            return strategy(IdGenerationStrategy.ULID);
        }

        /**
         * Uses a 32-character lowercase, hyphen-free UUIDv7 as the value instead of a ULID. Worth it
         * when a column, schema or downstream service insists on a real UUID; otherwise the ULID
         * default is six characters shorter for the same 128 bits.
         *
         * @return this builder
         */
        public Builder useUuidV7() {
            return strategy(IdGenerationStrategy.UUID_V7);
        }

        /**
         * Associates an entity type with the prefix its identifiers carry.
         *
         * <pre>{@code
         * builder.registerEntity(User.class, "usr");   // generate(User.class) -> "usr_01k2f9v8ta..."
         * }</pre>
         *
         * <p>Nothing is required of the type; it is used purely as a key, so it can be an entity
         * class, a record, a DTO or a marker interface. This is the registration style to use under
         * GraalVM native-image, because it involves no reflection.
         *
         * @param entityType the entity type
         * @param prefix 1 to 16 ASCII letters or digits, stored exactly as written; lowercase reads
         *     best
         * @return this builder
         * @throws NullPointerException if {@code entityType} or {@code prefix} is {@code null}
         * @throws InvalidEntityPrefixException if {@code prefix} breaks the rules
         * @throws DuplicateEntityPrefixException if {@code entityType} is already registered, or
         *     {@code prefix} is already registered (ignoring ASCII case) for a different type
         */
        public Builder registerEntity(Class<?> entityType, String prefix) {
            Objects.requireNonNull(entityType, "entityType");
            EntityPrefixValidator.validate(prefix, "prefix");
            register(entityType, prefix);
            return this;
        }

        /**
         * Registers each listed type with the prefix its {@link EntityPrefix} annotation declares.
         *
         * <pre>{@code
         * builder.registerEntities(User.class, Order.class, Invoice.class);
         * }</pre>
         *
         * <p>The types are registered in ordinal order of their fully qualified (binary) name, not
         * the order given, so a clash between two of them is reported the same way round every time.
         *
         * @param entityTypes the annotated types
         * @return this builder
         * @throws NullPointerException if {@code entityTypes} or any element is {@code null}
         * @throws IllegalArgumentException if a type does not carry {@link EntityPrefix} itself (an
         *     annotation on a supertype does not count)
         * @throws InvalidEntityPrefixException if an annotation declares an invalid prefix; the
         *     message names the type
         * @throws DuplicateEntityPrefixException if a type or prefix is already registered
         */
        public Builder registerEntities(Class<?>... entityTypes) {
            Objects.requireNonNull(entityTypes, "entityTypes");
            return registerEntities(Arrays.asList(entityTypes));
        }

        /**
         * Registers each type in the collection with the prefix its {@link EntityPrefix} annotation
         * declares. Behaves exactly like {@link #registerEntities(Class[])}.
         *
         * @param entityTypes the annotated types
         * @return this builder
         * @throws NullPointerException if {@code entityTypes} or any element is {@code null}
         * @throws IllegalArgumentException if a type does not carry {@link EntityPrefix} itself
         * @throws InvalidEntityPrefixException if an annotation declares an invalid prefix; the
         *     message names the type
         * @throws DuplicateEntityPrefixException if a type or prefix is already registered
         */
        public Builder registerEntities(Collection<? extends Class<?>> entityTypes) {
            Objects.requireNonNull(entityTypes, "entityTypes");
            List<Class<?>> ordered = new ArrayList<>(entityTypes.size());
            for (Class<?> entityType : entityTypes) {
                ordered.add(Objects.requireNonNull(entityType, "entityTypes"));
            }
            ordered.sort(Comparator.comparing(Class::getName));

            for (Class<?> entityType : ordered) {
                EntityPrefix annotation = entityType.getDeclaredAnnotation(EntityPrefix.class);
                if (annotation == null) {
                    throw new IllegalArgumentException(
                            "'" + entityType.getName() + "' does not carry an @EntityPrefix annotation, so it has "
                                    + "no prefix to register. Annotate it, for example @EntityPrefix(\"abc\"), or "
                                    + "register it explicitly with registerEntity(" + entityType.getSimpleName()
                                    + ".class, \"abc\"). The annotation is not inherited from supertypes.");
                }
                registerAnnotated(entityType, annotation.value());
            }
            return this;
        }

        /**
         * Registers every type carrying {@link EntityPrefix} in the package of {@code marker} and its
         * sub-packages, within the directory, jar or named module that {@code marker} was loaded from.
         *
         * <pre>{@code
         * builder.registerEntitiesFromPackageOf(User.class);   // scans com.example.domain.**
         * }</pre>
         *
         * <p>The scan happens here, once; what it finds goes into the same immutable lookup as an
         * explicit registration, so {@link IdoraGenerator#generate(Class)} costs exactly the same
         * afterwards. Types are registered in ordinal order of their fully qualified name.
         * Non-public, nested and local types, records, interfaces and enums are all included; a
         * subclass of an annotated type is not, because the annotation is not inherited.
         *
         * <p>You name the package rather than the library discovering it. Walking the whole class
         * path would cost time on every start-up, would see only what happened to be visible, and
         * would let unrelated libraries register prefixes into your application. Point this at one
         * of your entities: it survives a package rename, and fails to compile rather than fails at
         * start-up if the type moves.
         *
         * <p>Only class files that mention {@code @EntityPrefix} are loaded, and none is initialised
         * (no static initialisers run). Exploded directories, jar files and named JPMS modules are
         * supported. Scanning relies on reflection and on reading class files at run time, so under
         * GraalVM native-image, or any packaging that does not keep class files as resources, use
         * {@link #registerEntity(Class, String)} or {@link #registerEntities(Class[])} instead.
         *
         * @param marker any type in the package to scan; one of your entities reads best
         * @return this builder
         * @throws NullPointerException if {@code marker} is {@code null}
         * @throws IllegalArgumentException if {@code marker} is a primitive or array type, lives in
         *     the unnamed package, or was loaded from a location that cannot be listed
         * @throws java.io.UncheckedIOException if reading the directory, jar or module fails
         * @throws InvalidEntityPrefixException if an annotation declares an invalid prefix; the
         *     message names the type
         * @throws DuplicateEntityPrefixException if two annotated types claim one prefix (ignoring
         *     ASCII case), or a type found is already registered, including by scanning the same
         *     package twice
         */
        public Builder registerEntitiesFromPackageOf(Class<?> marker) {
            Objects.requireNonNull(marker, "marker");
            ClassLoader loader = marker.getClassLoader();
            String descriptor = 'L' + EntityPrefix.class.getName().replace('.', '/') + ';';

            for (String className : PackageScanner.classesMentioning(marker, descriptor)) {
                Class<?> candidate = PackageScanner.load(className, loader);
                // The class file mentioning the descriptor is only a cheap pre-filter; this is the
                // authoritative check, and it ignores annotations on supertypes.
                EntityPrefix annotation = candidate.getDeclaredAnnotation(EntityPrefix.class);
                if (annotation != null) {
                    registerAnnotated(candidate, annotation.value());
                }
            }
            return this;
        }

        /**
         * Builds an immutable, thread-safe generator from the configuration so far.
         *
         * @return a new generator
         */
        public IdoraGenerator build() {
            return new DefaultIdoraGenerator(strategy, new DefaultEntityPrefixRegistry(prefixesByEntityType));
        }

        /** Validates and registers one annotated type, restating a bad prefix in terms of the type. */
        private void registerAnnotated(Class<?> entityType, String prefix) {
            try {
                EntityPrefixValidator.validate(prefix, "prefix");
            } catch (InvalidEntityPrefixException e) {
                throw InvalidEntityPrefixException.forAnnotation(entityType, e);
            }
            register(entityType, prefix);
        }

        /**
         * The single place both registration styles go through, which is what lets them police each
         * other with no extra logic and in either order. The prefix is already validated.
         */
        private void register(Class<?> entityType, String prefix) {
            // Checked first because "you already registered this type" is the more useful message
            // when both problems are true.
            String existingPrefix = prefixesByEntityType.get(entityType);
            if (existingPrefix != null) {
                throw DuplicateEntityPrefixException.forEntityType(entityType, existingPrefix, prefix);
            }

            String folded = EntityPrefixValidator.foldCase(prefix);
            Class<?> conflictingEntityType = entityTypesByFoldedPrefix.get(folded);
            if (conflictingEntityType != null) {
                throw DuplicateEntityPrefixException.forPrefix(prefix, entityType, conflictingEntityType);
            }

            prefixesByEntityType.put(entityType, prefix);
            entityTypesByFoldedPrefix.put(folded, entityType);
        }
    }
}
