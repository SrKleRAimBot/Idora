package io.github.srkleraimbot.idora;

/**
 * Thrown by {@link IdoraGenerator#generate(Class)} and {@link EntityPrefixRegistry#getPrefix(Class)}
 * when the entity type has no registered prefix.
 *
 * <p>Idora deliberately refuses to invent a prefix. Guessing one from the class name would produce
 * identifiers that silently change the day somebody renames the class, and two types in different
 * packages sharing a simple name would quietly collide. Register the type while building the
 * generator, or use {@link IdoraGenerator#generate(String)} if the prefix genuinely is not known
 * until run time.
 */
public final class EntityPrefixNotRegisteredException extends IdoraException {

    private static final long serialVersionUID = 1L;

    /** The type with no registered prefix. */
    private final Class<?> entityType;

    private EntityPrefixNotRegisteredException(String message, Class<?> entityType) {
        super(message);
        this.entityType = entityType;
    }

    /**
     * Returns the entity type that has no registered prefix.
     *
     * @return the entity type; never {@code null}
     */
    public Class<?> getEntityType() {
        return entityType;
    }

    static EntityPrefixNotRegisteredException forType(Class<?> entityType) {
        String simpleName = entityType.getSimpleName().isEmpty() ? entityType.getName() : entityType.getSimpleName();
        return new EntityPrefixNotRegisteredException(
                "No entity prefix is registered for '" + entityType.getName() + "'. Register one when building "
                        + "the generator, for example IdoraGenerator.builder().registerEntity(" + simpleName
                        + ".class, \"abc\"), or annotate the type with @EntityPrefix(\"abc\") and register it with "
                        + "registerEntities or registerEntitiesFromPackageOf. If the prefix is only known at run "
                        + "time, call generate(String) with an explicit prefix instead.",
                entityType);
    }
}
