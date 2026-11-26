package io.github.srkleraimbot.idora;

/**
 * The base class of every exception Idora throws for misuse.
 *
 * <p>Catch this type to handle any Idora failure uniformly, or one of its four subclasses if you
 * care which problem it was:
 *
 * <ul>
 *   <li>{@link InvalidEntityPrefixException}: a prefix breaks the rules;
 *   <li>{@link DuplicateEntityPrefixException}: a prefix or an entity type was registered twice;
 *   <li>{@link EntityPrefixNotRegisteredException}: an entity type has no registered prefix;
 *   <li>{@link InvalidIdException}: a text is not an identifier, or not one with the expected
 *       prefix.
 * </ul>
 *
 * <p>The hierarchy is sealed, so a {@code switch} over these four is exhaustive. A {@code null}
 * argument is reported separately, as a {@link NullPointerException} naming the parameter.
 */
public abstract sealed class IdoraException extends RuntimeException
        permits InvalidEntityPrefixException, DuplicateEntityPrefixException, EntityPrefixNotRegisteredException,
                InvalidIdException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates an exception with a message describing the problem and how to fix it.
     *
     * @param message the detail message
     */
    IdoraException(String message) {
        super(message);
    }

    /**
     * Creates an exception with a message describing the problem and how to fix it, and the failure
     * that caused it.
     *
     * @param message the detail message
     * @param cause the underlying failure, or {@code null} if there is none
     */
    IdoraException(String message, Throwable cause) {
        super(message, cause);
    }
}
