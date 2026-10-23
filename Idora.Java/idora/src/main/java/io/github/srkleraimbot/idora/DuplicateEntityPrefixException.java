package io.github.srkleraimbot.idora;

/**
 * Thrown while building a generator when a prefix would no longer identify exactly one entity type.
 *
 * <p>This covers two mistakes. The first is giving the same prefix to two different entity types,
 * which defeats the point of a prefix: you could no longer tell from {@code acc_01k2f9...} whether
 * you were looking at an {@code Account} or an {@code Accrual}. The second is registering one entity
 * type twice, which is almost always a copy-paste slip and would leave it ambiguous which prefix
 * wins. It is reported even when both registrations agree on the prefix.
 *
 * <p>Prefixes are compared ignoring ASCII case, so {@code usr} and {@code USR} collide.
 *
 * <p>It does not matter where the two registrations came from: two
 * {@link IdoraGenerator.Builder#registerEntity(Class, String)} calls, two {@link EntityPrefix}
 * annotations, one of each, or the same package scanned twice all clash the same way, and in
 * either order.
 *
 * <pre>{@code
 * try {
 *     IdoraGenerator.builder()
 *         .registerEntity(Account.class, "acc")
 *         .registerEntity(Accrual.class, "acc");
 * } catch (DuplicateEntityPrefixException e) {
 *     e.getPrefix();                  // "acc"
 *     e.getEntityType();              // Accrual.class
 *     e.getConflictingEntityType();   // Account.class
 * }
 * }</pre>
 */
public final class DuplicateEntityPrefixException extends IdoraException {

    private static final long serialVersionUID = 1L;

    /** The prefix of the refused registration. */
    private final String prefix;
    /** The type being registered. */
    private final Class<?> entityType;
    /** The type already holding the prefix, or the same type when it was registered twice. */
    private final Class<?> conflictingEntityType;

    private DuplicateEntityPrefixException(
            String message, String prefix, Class<?> entityType, Class<?> conflictingEntityType) {
        super(message);
        this.prefix = prefix;
        this.entityType = entityType;
        this.conflictingEntityType = conflictingEntityType;
    }

    /**
     * Returns the prefix of the registration that was refused, exactly as it was supplied.
     *
     * @return the prefix; never {@code null}
     */
    public String getPrefix() {
        return prefix;
    }

    /**
     * Returns the entity type that was being registered when the clash was detected.
     *
     * @return the entity type; never {@code null}
     */
    public Class<?> getEntityType() {
        return entityType;
    }

    /**
     * Returns the entity type that already held the prefix. When the same type was registered twice
     * this is the same as {@link #getEntityType()}.
     *
     * @return the conflicting entity type; never {@code null}
     */
    public Class<?> getConflictingEntityType() {
        return conflictingEntityType;
    }

    /** Two different types competing for one prefix (ignoring case). */
    static DuplicateEntityPrefixException forPrefix(String prefix, Class<?> entityType, Class<?> conflictingEntityType) {
        String type = entityType.getName();
        return new DuplicateEntityPrefixException(
                "The entity prefix '" + prefix + "' is already registered for '" + conflictingEntityType.getName()
                        + "' and cannot also be used for '" + type + "'. Prefixes must identify exactly one "
                        + "entity type, and are compared ignoring case. Choose a different prefix for '" + type + "'.",
                prefix,
                entityType,
                conflictingEntityType);
    }

    /**
     * One type registered more than once. A registration can come from a {@code registerEntity}
     * call, an {@link EntityPrefix} annotation, or a package scan that covers the type, and scanning
     * one package twice is as much a repeat as anything else, so the advice names all of them
     * rather than guessing.
     */
    static DuplicateEntityPrefixException forEntityType(Class<?> entityType, String existingPrefix, String attemptedPrefix) {
        String type = entityType.getName();
        // Both registrations agreeing on the prefix means the mapping was declared twice rather than
        // declared differently: a different mistake with a different fix. Saying "cannot also be
        // registered with 'usr'" when it already is 'usr' would just read as a bug.
        String message = existingPrefix.equals(attemptedPrefix)
                ? "The entity type '" + type + "' is already registered with the prefix '" + existingPrefix
                        + "', and cannot be registered a second time. Each entity type may be registered exactly "
                        + "once. Look for a repeated registerEntity call, a type carrying an @EntityPrefix "
                        + "annotation that is also registered by hand, or the same package being scanned twice."
                : "The entity type '" + type + "' is already registered with the prefix '" + existingPrefix
                        + "' and cannot also be registered with '" + attemptedPrefix + "'. Each entity type may "
                        + "have only one prefix. Remove whichever registration is wrong: a registerEntity call, "
                        + "an @EntityPrefix annotation, or a package scan that covers the type.";
        return new DuplicateEntityPrefixException(message, attemptedPrefix, entityType, entityType);
    }
}
